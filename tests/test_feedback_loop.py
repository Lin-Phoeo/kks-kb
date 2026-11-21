#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""反馈闭环：POST /api/feedback 落库 + doc_votes 聚合 + 迁移幂等 +
学习信号回归闸门 + /feedback 页面鉴权。全部在临时目录里跑，不碰线上数据。"""
import json
import os
import shutil
import sqlite3
import sys
import tempfile
import unittest
from unittest import mock

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

try:
    import fastapi, httpx, jieba, markdown, jinja2, multipart  # noqa: F401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:   # pragma: no cover
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"

DIM = 4
QV = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)

# (id, col, path, heading, section, body, sim)。查询向量 [1,0,0,0]，sim 即第一分量。
ROWS = [
    (1, "kks", "kks/docs/alpha.md", "Alpha", "Intro", "rim light rim light rim light", 0.10),
    (2, "kks", "kks/docs/beta.md", "Beta", "Intro", "texture map texture map texture map", 0.90),
    (3, "kks", "kks/docs/gamma.md", "Gamma", "Intro", "shader color shader color shader color", 0.20),
    (4, "kks", "kks/docs/delta.md", "Delta", "Intro", "outline edge outline edge outline edge", 0.80),
    (5, "src", "src/core/shader.cg", "Shader", "Core", "core shader core shader core shader", 0.30),
    (6, "src", "src/core/color.cg", "Color", "Core", "color ramp color ramp color ramp", 0.70),
    (7, "src", "src/util/map.cg", "Map", "Util", "map lookup map lookup map lookup", 0.40),
    (8, "src", "src/util/list.cg", "List", "Util", "list data list data list data", 0.60),
    (9, "kks", "kks/ref/rimlight.md", "RimLight", "Ref", "rim rim rim rim rim rim", 0.50),
    (10, "kks", "kks/ref/outline.md", "Outline", "Ref", "outline outline outline outline outline", 0.95),
]

# 与 test_retrieval_e1 相同的迷你语料基线（learned_weight=0 时的 top-5 id）。
HYBRID_BASELINE = {
    "rim light": [9, 1, 10, 2, 4],
    "texture map": [7, 2, 10, 4, 9],
    "shader color": [6, 5, 3, 10, 2],
}


def _tok(s):
    return " ".join(w for w in jieba.cut_for_search(s) if w.strip())


_tmp = None
_root = None
_mod = None
cfg = None
kbx = None
client = None


def setUpModule():
    global _tmp, _root, _mod, cfg, kbx, client
    if not HAVE_DEPS:
        return
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    _root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(_root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    data = os.path.join(_root, "data")
    os.makedirs(data)
    with open(os.path.join(_root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
                   "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                            "admin_password": ADMIN_PW}}, f)

    db = os.path.join(data, "kb.db")
    con = sqlite3.connect(db)
    con.execute("CREATE VIRTUAL TABLE fts USING fts5(path, body, tokenize='unicode61')")
    con.execute("CREATE TABLE meta(id INTEGER PRIMARY KEY, col TEXT, path TEXT, heading TEXT,"
                " section TEXT, body TEXT, hash TEXT, tbl_rows INTEGER DEFAULT 0, ord INTEGER)")
    con.execute("CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT)")
    mat = np.zeros((len(ROWS), DIM), dtype=np.float32)
    ids = []
    for i, (mid, col, path, heading, section, body, sim) in enumerate(ROWS):
        con.execute("INSERT INTO meta(id,col,path,heading,section,body,hash,tbl_rows,ord)"
                    " VALUES(?,?,?,?,?,?,?,0,0)",
                    (mid, col, path, heading, section, body, "h%d" % mid))
        con.execute("INSERT INTO fts(rowid,path,body) VALUES(?,?,?)",
                    (mid, path, _tok(body)))
        con.execute("INSERT INTO doc(path,title,chars,col) VALUES(?,?,?,?)",
                    (path, heading, len(body), col))
        mat[i] = [sim, 0.0, 0.0, 0.0]
        ids.append(mid)
    con.commit()
    con.close()
    np.save(db + ".vec.npy", mat)
    json.dump(ids, open(db + ".vec.ids.json", "w"))

    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    sys.path.insert(0, os.path.join(_root, "app"))
    import app as mod
    _mod = mod
    from kb.config import CFG as _cfg
    from kb.retrieval import KBX as _kbx
    cfg = _cfg
    kbx = _kbx
    client = TestClient(mod.app, follow_redirects=False)
    client.headers["Origin"] = "http://testserver"


def tearDownModule():
    global _tmp
    if not HAVE_DEPS:
        return
    try:
        _mod.KBX.con.close()
        _mod.STATE.close()
    except Exception:
        pass
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    if _tmp:
        _tmp.cleanup()


def _clear_feedback():
    _mod.STATE.execute("DELETE FROM feedback")
    _mod.STATE.execute("DELETE FROM doc_votes")
    _mod.STATE.commit()


def _clear_rates():
    _mod.LOGIN_FAILS_IP._d.clear()
    _mod.LOGIN_FAILS_ACCT._d.clear()
    _mod.CODE_SENDS_IP._d.clear()


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过反馈闭环测试")
class FeedbackApiTests(unittest.TestCase):
    def setUp(self):
        _clear_rates()
        _clear_feedback()
        actor = _mod.STATE.fetchone('SELECT id FROM users WHERE email=?', (ADMIN,))[0]
        _mod.STATE.execute('DELETE FROM msg')
        _mod.STATE.execute('DELETE FROM conv')
        cid = kbx.new_conv('fixture', actor=actor)
        for mid in (1, 2, 3):
            _mod.STATE.execute("INSERT INTO msg(id,conv_id,role,content,meta) VALUES(?,?,'assistant','answer',?)", (mid, cid, json.dumps({'chunks': [{'path': 'kks/docs/alpha.md'}]})))
        _mod.STATE.commit()

    def authed(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        r = c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})
        self.assertEqual(r.status_code, 303, r.text)
        return c

    def test_json_feedback_stores_row_and_aggregate(self):
        c = self.authed()
        r = c.post("/api/feedback",
                   json={"msg_id": 1, "vote": 1, "path": "kks/docs/alpha.md", "note": "好"})
        self.assertEqual(r.status_code, 200, r.text)
        self.assertTrue(r.json()["ok"])
        row = _mod.STATE.fetchone(
            "SELECT msg_id,email,vote,note,path FROM feedback ORDER BY id DESC LIMIT 1")
        self.assertEqual(row, (1, ADMIN, 1, "好", "kks/docs/alpha.md"))
        dv = _mod.STATE.fetchone(
            "SELECT up,down FROM doc_votes WHERE path='kks/docs/alpha.md'")
        self.assertEqual(dv, (1, 0))

    def test_form_feedback_still_works(self):
        c = self.authed()
        r = c.post("/api/feedback", data={"msg_id": 2, "vote": -1, "note": "bad"})
        self.assertTrue(r.json()["ok"])
        row = _mod.STATE.fetchone(
            "SELECT vote,note,path FROM feedback ORDER BY id DESC LIMIT 1")
        self.assertEqual(row, (-1, "bad", ""))
        # 整条答案反馈（path 为空）不应污染 doc_votes
        self.assertEqual(_mod.STATE.fetchone("SELECT COUNT(*) FROM doc_votes")[0], 0)

    def test_repeat_votes_accumulate(self):
        c = self.authed()
        for mid, v in ((1, 1), (2, 1), (3, -1)):
            r = c.post("/api/feedback",
                       json={"msg_id": mid, "vote": v, "path": "kks/docs/alpha.md"})
            self.assertTrue(r.json()["ok"])
        dv = _mod.STATE.fetchone(
            "SELECT up,down FROM doc_votes WHERE path='kks/docs/alpha.md'")
        self.assertEqual(dv, (2, 1))

    def test_reset_truncates_signal_keeps_raw_rows(self):
        c = self.authed()
        c.post("/api/feedback", json={"msg_id": 1, "vote": 1, "path": "kks/docs/alpha.md"})
        self.assertEqual(_mod.STATE.fetchone("SELECT COUNT(*) FROM doc_votes")[0], 1)
        r = c.post("/api/feedback/reset")
        self.assertTrue(r.json()["ok"])
        self.assertEqual(_mod.STATE.fetchone("SELECT COUNT(*) FROM doc_votes")[0], 0)
        self.assertEqual(_mod.STATE.fetchone("SELECT COUNT(*) FROM feedback")[0], 1)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过反馈闭环测试")
class MigrationTests(unittest.TestCase):
    def test_feedback_path_migration_is_idempotent(self):
        from kb import store
        d = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        p = os.path.join(d.name, "state.db")
        try:
            # 模拟线上旧库：feedback 无 path 列且已有行
            con = sqlite3.connect(p)
            con.execute("CREATE TABLE feedback(id INTEGER PRIMARY KEY AUTOINCREMENT,"
                        " msg_id INTEGER, email TEXT, vote INTEGER, note TEXT, created TEXT)")
            con.execute("INSERT INTO feedback(msg_id,email,vote,note,created)"
                        " VALUES(?,?,?,?,?)", (1, "a@x.y", 1, "旧", "2026-01-01"))
            con.commit()
            con.close()
            store.StateStore(p)
            store.StateStore(p)   # 再次初始化必须无异常且不重复加列
            con = sqlite3.connect(p)
            cols = [r[1] for r in con.execute("PRAGMA table_info(feedback)").fetchall()]
            rows = con.execute("SELECT msg_id,vote,note FROM feedback").fetchall()
            con.close()
            self.assertEqual(cols.count("path"), 1)
            self.assertEqual(rows, [(1, 1, "旧")])
        finally:
            d.cleanup()


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过反馈闭环测试")
class ReviewPageTests(unittest.TestCase):
    def setUp(self):
        _clear_rates()
        _clear_feedback()

    def test_anonymous_redirects_to_login(self):
        r = client.get("/feedback")
        self.assertEqual((r.status_code, r.headers.get("location")), (303, "/login"))

    def test_authed_200(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        r = c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})
        self.assertEqual(r.status_code, 303)
        self.assertEqual(c.get("/feedback").status_code, 200)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过反馈闭环测试")
class RegressionGateTests(unittest.TestCase):
    def setUp(self):
        rr = sys.modules["kb.retrieval.rerank"]
        cfg.pop("retrieval", None)
        kbx._col_rows_cache = None
        kbx._path_index = None
        kbx._path_index_sig = None
        rr._DV["sig"] = None
        rr._DV["data"] = None
        _clear_feedback()

    def _top5(self, q):
        with mock.patch.object(kbx, "embed", return_value=QV):
            res, _ = kbx.hybrid(q, 5, "")
        return [c["id"] for c in res]

    def _seed_votes(self):
        now = "2026-01-02T00:00:00"
        _mod.STATE.execute(
            "INSERT INTO doc_votes(path,up,down,updated) VALUES(?,?,?,?)",
            ("kks/docs/beta.md", 20, 0, now))
        _mod.STATE.execute(
            "INSERT INTO doc_votes(path,up,down,updated) VALUES(?,?,?,?)",
            ("kks/ref/rimlight.md", 0, 20, now))
        _mod.STATE.commit()

    def test_weight_zero_bit_identical_with_and_without_votes(self):
        baseline = {q: self._top5(q) for q in HYBRID_BASELINE}
        self._seed_votes()   # doc_votes 非空，但 learned_weight 仍为 0
        for q, expect in baseline.items():
            self.assertEqual(self._top5(q), expect, q)

    def test_high_weight_moves_upvoted_up_and_downvoted_down(self):
        baseline = {q: self._top5(q) for q in HYBRID_BASELINE}
        self._seed_votes()
        cfg["retrieval"] = {"learned_weight": 10.0}
        q = "rim light"
        got = self._top5(q)
        before = HYBRID_BASELINE[q]   # [9, 1, 10, 2, 4]
        self.assertIn(2, got)
        self.assertLess(got.index(2), before.index(2), "被重仓点赞的 doc 应前进")
        if 9 in got:
            self.assertGreater(got.index(9), before.index(9), "被重仓点踩的 doc 应后退")
        # 若 9 掉出 top-5 也是「排名下降」，不额外断言


if __name__ == "__main__":
    unittest.main()
