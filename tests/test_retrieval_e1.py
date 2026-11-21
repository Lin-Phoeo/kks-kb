#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""E0/E1 检索改动测试（隔离环境，不碰线上 kb.db）。

E0：path_search 预计算与旧实现逐行一致。
E1：exact_topk 精确降序、col 打分前掩码、单条 IN 查询、RERANK_W/RRF_BASE 可配。

用临时 app 副本 + 迷你 kb.db / 向量矩阵，与 test_import_direction 同款隔离。
"""
import importlib
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

_tmp = None
_root = None
kbx = None
cfg = None
vector = None
_orig_con = None

DIM = 4
QV = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)

# (id, col, path, heading, section, body, sim)。sim 是第一分量，查询向量 [1,0,0,0]。
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

# path_search 等价性专用：多 chunk 同路径 + 跨库 + 并列命中
EXTRA = [
    (11, "kks", "alpha/beta.md", "Y1", "S", "b11"),
    (12, "kks", "alpha/beta.md", "Y2", "S", "b12"),
    (13, "kks", "beta/alpha.md", "X1", "S", "b13"),
    (14, "kks", "beta/alpha.md", "X2", "S", "b14"),
    (15, "src", "alpha/beta/src.md", "SRC", "S", "b15"),
    (16, "kks", "alpha/beta/gamma.md", "EX", "S", "b16"),
]

# 记录基线：exact_topk 关闭时 hybrid top-5 的 id（迷你语料，逐次确定性）。
HYBRID_BASELINE = {
    "rim light": [9, 1, 10, 2, 4],
    "texture map": [7, 2, 10, 4, 9],
    "shader color": [6, 5, 3, 10, 2],
}


def _tok(s):
    import jieba
    return " ".join(w for w in jieba.cut_for_search(s) if w.strip())


def setUpModule():
    global _tmp, _root, kbx, cfg, vector, _orig_con
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    _root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(_root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    data = os.path.join(_root, "data")
    os.makedirs(data)
    with open(os.path.join(_root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"gitee_key": "test-key",
                   "gitee_embed_url": "http://127.0.0.1:1/v1/embeddings",
                   "embed_model": "test-model", "instruction": "v1"}, f)

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
        mat[i] = [sim, 0.0, 0.0, 0.0]
        ids.append(mid)
    for (mid, col, path, heading, section, body) in EXTRA:
        con.execute("INSERT INTO meta(id,col,path,heading,section,body,hash,tbl_rows,ord)"
                    " VALUES(?,?,?,?,?,?,?,0,0)",
                    (mid, col, path, heading, section, body, "h%d" % mid))
    con.commit()
    con.close()
    np.save(db + ".vec.npy", mat)
    json.dump(ids, open(db + ".vec.ids.json", "w"))

    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    sys.path.insert(0, os.path.join(_root, "app"))

    from kb.retrieval import KBX  # noqa: E402
    kbx = KBX
    cfg = importlib.import_module("kb.config").CFG
    vector = importlib.import_module("kb.retrieval.vector")
    _orig_con = kbx.con


def tearDownModule():
    global _tmp
    try:
        kbx.con.close()
    except Exception:
        pass
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    if _tmp:
        _tmp.cleanup()


def _reset_cfg_and_caches():
    cfg.pop("retrieval", None)
    kbx._col_rows_cache = None
    kbx._path_index = None
    kbx._path_index_sig = None


def _old_vector(self, q, k=20, col=""):
    """改动前的 vector：argpartition 取窗口 + 逐条 SELECT + 后置 col 过滤。"""
    if self.mat is None:
        return []
    qv = self.embed(q)
    if qv is None:
        return []
    sims = self.mat @ qv
    out = []
    idx = np.argpartition(-sims, min(max(k * 4, k), len(sims) - 1))[:max(k * 4, k)]
    for i in idx:
        r = self.con.execute(
            "SELECT id,col,path,heading,section,body FROM meta WHERE id=?",
            (int(self.ids_arr[i]),)).fetchone()
        if not r:
            continue
        if col and r[1] != col:
            continue
        out.append({"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
                    "section": r[4], "body": r[5]})
        if len(out) >= k:
            break
    return out


def _old_path_search(self, q, k=30, col=""):
    import re as _re
    import jieba as _j
    terms = [t.lower() for t in _j.cut_for_search(q)
             if len(t.strip()) > 1]
    terms += [w.lower() for w in _re.findall(r"[A-Za-z_][A-Za-z0-9_]{2,}", q)]
    terms = [t for t in dict.fromkeys(terms)
             if t not in ("哪些", "什么", "怎么", "如何", "多少", "有哪")]
    if not terms:
        return []
    rows = self.con.execute(
        "SELECT id,col,path,heading,section,body FROM meta"
        + (" WHERE col=?" if col else ""), ((col,) if col else ())).fetchall()
    scored = {}
    for r in rows:
        p = (r[2] or "").lower()
        hit = sum(1 for t in terms if t in p)
        if hit < 2:
            continue
        key = r[2]
        if key not in scored or hit > scored[key][0]:
            scored[key] = (hit, r)
    best = sorted(scored.values(), key=lambda x: -x[0])[:k]
    return [{"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
             "section": r[4], "body": r[5]} for _, r in best]


class CountingConn:
    def __init__(self, con):
        self._con = con
        self.selects = 0

    def execute(self, sql, params=()):
        if sql.strip().upper().startswith("SELECT"):
            self.selects += 1
        return self._con.execute(sql, params)


class VectorSwitchOffTests(unittest.TestCase):
    def setUp(self):
        _reset_cfg_and_caches()

    def test_off_path_matches_old_reference(self):
        with mock.patch.object(kbx, "embed", return_value=QV):
            for q in ("rim light", "texture map", "shader color"):
                for k in (1, 4, 7, 20):
                    for col in ("", "kks", "src", "nope"):
                        self.assertEqual(
                            kbx.vector(q, k, col), _old_vector(kbx, q, k, col),
                            "q=%r k=%r col=%r" % (q, k, col))

    def test_n_plus_1_to_in_query_preserves_order(self):
        """专项固定 10 条查询：N+1 改 IN 后，返回 chunk id 顺序必须逐字节一致。"""
        cases = [
            ("rim light", 5, ""),
            ("rim light", 5, "kks"),
            ("rim light", 5, "src"),
            ("texture map", 5, ""),
            ("texture map", 3, "kks"),
            ("shader color", 5, ""),
            ("shader color", 6, "src"),
            ("outline", 4, ""),
            ("map lookup", 2, "src"),
            ("ramp", 20, ""),
        ]
        with mock.patch.object(kbx, "embed", return_value=QV):
            for q, k, col in cases:
                new_ids = [c["id"] for c in kbx.vector(q, k, col)]
                old_ids = [c["id"] for c in _old_vector(kbx, q, k, col)]
                self.assertEqual(new_ids, old_ids,
                                 "q=%r k=%d col=%r：IN 查询必须保留窗口顺序" % (q, k, col))

    def test_off_path_single_in_query_not_n_plus_one(self):
        """OFF 路径（exact_topk 关）每次调用应只跑一条 SELECT，不再是 96 条。"""
        cc = CountingConn(_orig_con)
        kbx.con = cc
        try:
            with mock.patch.object(kbx, "embed", return_value=QV):
                kbx.vector("rim light", k=5, col="")
            self.assertEqual(cc.selects, 1,
                             "OFF 路径不得逐候选 SELECT，必须单条 IN 完成")
            cc.selects = 0
            with mock.patch.object(kbx, "embed", return_value=QV):
                kbx.vector("rim light", k=5, col="kks")
            self.assertEqual(cc.selects, 1, "带 col 过滤也只允许一条 IN")
        finally:
            kbx.con = _orig_con

    def test_off_hybrid_matches_recorded_baseline(self):
        with mock.patch.object(kbx, "embed", return_value=QV):
            for q, expect in HYBRID_BASELINE.items():
                res, _meta = kbx.hybrid(q, 5, "")
                self.assertEqual([c["id"] for c in res], expect, q)


class VectorSwitchOnTests(unittest.TestCase):
    def setUp(self):
        _reset_cfg_and_caches()
        cfg["retrieval"] = {"exact_topk": True}

    def _sims(self):
        return kbx.mat @ QV

    def _pos(self):
        return {int(kbx.ids_arr[i]): i for i in range(len(kbx.ids_arr))}

    def test_exact_topk_descending_similarity(self):
        sims = self._sims()
        pos = self._pos()
        with mock.patch.object(kbx, "embed", return_value=QV):
            res = kbx.vector("q", k=4, col="")
        expect = [int(kbx.ids_arr[i]) for i in np.argsort(-sims, kind="stable")[:4]]
        self.assertEqual([c["id"] for c in res], expect)
        got_sims = [float(sims[pos[c["id"]]]) for c in res]
        self.assertTrue(all(a >= b for a, b in zip(got_sims, got_sims[1:])),
                        "返回顺序必须按相似度非增")

    def test_col_mask_ranks_within_collection(self):
        with mock.patch.object(kbx, "embed", return_value=QV):
            res = kbx.vector("q", k=4, col="src")
        self.assertEqual([c["id"] for c in res], [6, 8, 7, 5])
        self.assertTrue(all(c["col"] == "src" for c in res))
        self.assertEqual(res[0]["id"], 6, "top-1 应是该库内相似度最高者")

    def test_on_issues_single_in_query_not_n_plus_one(self):
        cc = CountingConn(_orig_con)
        kbx.con = cc
        try:
            with mock.patch.object(kbx, "embed", return_value=QV):
                kbx.vector("q", k=4, col="src")   # 冷：建 col 掩码 + 一条 IN
            cold = cc.selects
            cc.selects = 0
            with mock.patch.object(kbx, "embed", return_value=QV):
                kbx.vector("q", k=4, col="src")   # 热：只一条 IN
            warm = cc.selects
            cc.selects = 0
            with mock.patch.object(kbx, "embed", return_value=QV):
                kbx.vector("q", k=4, col="")      # 无 col：只一条 IN
            no_col = cc.selects
        finally:
            kbx.con = _orig_con
        self.assertLessEqual(cold, 2)
        self.assertEqual(warm, 1)
        self.assertEqual(no_col, 1)
        self.assertLess(cold, 4, "不得逐候选一条 SELECT")


class ArgpartitionDemonstrationTests(unittest.TestCase):
    def test_argpartition_prefix_is_not_exact_topk(self):
        """论证为什么要换成 argsort：argpartition 窗口无序，首元素可能不是 argmax。"""
        n, k = 1000, 24
        sims = np.random.default_rng(1).random(n).astype(np.float32)
        kth = min(max(k * 4, k), n - 1)
        buggy = np.argpartition(-sims, kth)[:max(k * 4, k)]
        exact = np.argsort(-sims, kind="stable")[:k]
        self.assertNotEqual(buggy[:k].tolist(), exact.tolist())
        self.assertNotEqual(int(np.argmax(sims)), int(buggy[0]),
                            "argpartition 窗口首元素不等于全局 argmax")


class PathIndexEquivalenceTests(unittest.TestCase):
    def setUp(self):
        _reset_cfg_and_caches()

    def test_path_search_identical_to_old(self):
        cases = [
            ("alpha beta", 30, ""),
            ("alpha beta gamma", 30, ""),
            ("alpha beta", 30, "kks"),
            ("alpha beta", 30, "src"),
            ("alpha beta", 2, ""),
            ("gamma", 30, ""),
            ("zzz qqq", 30, ""),
            ("rim light", 20, ""),
            ("shader color", 20, "src"),
        ]
        for q, k, col in cases:
            new = kbx.path_search(q, k, col)
            old = _old_path_search(kbx, q, k, col)
            self.assertEqual(
                [(c["id"], c["path"], c["section"]) for c in new],
                [(c["id"], c["path"], c["section"]) for c in old],
                "q=%r k=%r col=%r" % (q, k, col))

    def test_path_index_first_chunk_wins(self):
        res = kbx.path_search("alpha beta", 30, "")
        by_path = {c["path"]: c["id"] for c in res}
        self.assertEqual(by_path["alpha/beta.md"], 11)
        self.assertEqual(by_path["beta/alpha.md"], 13)


class RerankConfigTests(unittest.TestCase):
    CAND = {"id": 1, "col": "kks", "path": "docs/alpha.md", "heading": "Alpha",
            "section": "Intro", "body": "rim light", "rrf": 0.30}

    def setUp(self):
        _reset_cfg_and_caches()

    def test_defaults_unchanged(self):
        self.assertEqual(kbx.RERANK_W, (0.30, 0.3224, 0.1488, 0.1488, 0.08, True))
        self.assertEqual(kbx.RRF_BASE, 0.033)
        res = kbx.rerank("rim light", [dict(self.CAND)], 5)
        self.assertAlmostEqual(res[0]["_score"], 0.4668, places=4)

    def test_override_via_fake_cfg(self):
        cfg["retrieval"] = {"rerank_w": [0.0, 1.0, 0.0, 0.0, 0.0, True],
                            "rrf_base": 0.5}
        res = kbx.rerank("rim light", [dict(self.CAND)], 5)
        self.assertAlmostEqual(res[0]["_score"], 0.75, places=4)

    def test_override_rrf_base_used_only_when_fixed(self):
        cfg["retrieval"] = {"rerank_w": [1.0, 0.0, 0.0, 0.0, 0.0, False],
                            "rrf_base": 0.5}
        c1 = dict(self.CAND)
        c2 = dict(self.CAND)
        c2["id"] = 2
        c2["rrf"] = 0.50
        res = kbx.rerank("rim light", [c1, c2], 5)
        # use_fixed=False → base = max_rrf = 0.50，rrf_base 不生效
        self.assertEqual(res[0]["id"], 2)


class WuwaDampTests(unittest.TestCase):
    """鸣潮素材对 KKS 问题是噪音：查询不涉及鸣潮时，带 wuwa 的路径降权。

    测试策略：固定同一候选，比较 wuwa_damp=1.0 与 wuwa_damp=0.5 的分数，
    其他分量（权威度、head、path 命中）在两次调用间保持恒定，因此比值 = damp。
    """
    WUWA_CAND = {"id": 1, "col": "kks", "path": "docs/wuwa/x.md", "heading": "Alpha",
                 "section": "Intro", "body": "rim light rim light rim light",
                 "rrf": 0.30}
    SAFE_CAND = {"id": 2, "col": "kks", "path": "docs/alpha/x.md", "heading": "Alpha",
                 "section": "Intro", "body": "rim light rim light rim light",
                 "rrf": 0.30}

    def setUp(self):
        _reset_cfg_and_caches()

    def _score(self, q, cand, **retrieval):
        cfg["retrieval"] = dict(retrieval)
        res = kbx.rerank(q, [dict(cand)], 5)
        return res[0]["_score"]

    def test_damp_halves_wuwa_path_when_query_unrelated(self):
        # BEFORE（wuwa_damp=1.0 关闭）→ AFTER（wuwa_damp=0.5 启用）：
        # 查询不含鸣潮词、路径含 wuwa 时，分数应被压到一半。
        baseline = self._score("rim light", self.WUWA_CAND, wuwa_damp=1.0)
        damped = self._score("rim light", self.WUWA_CAND, wuwa_damp=0.5)
        self.assertAlmostEqual(damped, baseline * 0.5, places=4,
                               msg=f"BEFORE={baseline} AFTER={damped}（应 ×0.5）")

    def test_no_damp_when_query_mentions_wuwa(self):
        # 查询出现 wuwa 任意 token：即使 wuwa_damp=0.5 也不应触发。
        baseline = self._score("wuwa shader", self.WUWA_CAND, wuwa_damp=1.0)
        active = self._score("wuwa shader", self.WUWA_CAND, wuwa_damp=0.5)
        self.assertAlmostEqual(active, baseline, places=4,
                               msg="查询含 wuwa 关键词时不应 damp")

    def test_non_wuwa_path_untouched(self):
        # 路径不含 wuwa：wuwa_damp 对它无影响。
        baseline = self._score("rim light", self.SAFE_CAND, wuwa_damp=1.0)
        active = self._score("rim light", self.SAFE_CAND, wuwa_damp=0.5)
        self.assertAlmostEqual(active, baseline, places=4,
                               msg="非 wuwa 路径不受 damp 影响")

    def test_damp_configurable_to_quarter(self):
        baseline = self._score("rim light", self.WUWA_CAND, wuwa_damp=1.0)
        damped = self._score("rim light", self.WUWA_CAND, wuwa_damp=0.25)
        self.assertAlmostEqual(damped, baseline * 0.25, places=4)

    def test_damp_matches_all_three_tokens(self):
        for p in ("docs/wuwa/x.md", "docs/wuthering/x.md", "docs/鸣潮/x.md"):
            cand = dict(self.WUWA_CAND)
            cand["path"] = p
            baseline = self._score("rim light", cand, wuwa_damp=1.0)
            damped = self._score("rim light", cand, wuwa_damp=0.5)
            self.assertAlmostEqual(damped, baseline * 0.5, places=4,
                                   msg=f"path={p}: BEFORE={baseline} AFTER={damped}")


if __name__ == "__main__":
    unittest.main()
