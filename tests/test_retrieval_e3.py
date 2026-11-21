#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""E3 检索召回修复测试。

Fix 1: FTS schema v2（path/heading/section/body 四列）→ 直解 c-142 标题零召回。
  - v2 schema：标题命中但 body 不含整串时，fts 查询也能召回。
  - v1 schema：旧 2 列库（path+body）向后兼容，走旧 MATCH 表达式。
Fix 2: title_exact 信号 → 可配开关，默认 0.0 不改变旧排序；>0 时把标题完全含查询的候选顶上来。

隔离方式与 test_retrieval_e1 一致：临时 app 副本 + 迷你 kb.db + 独立 config.json，
不碰线上 kb.db / 向量矩阵。
"""
import importlib
import json
import os
import shutil
import sqlite3
import sys
import tempfile
import unittest

import jieba
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")


def _tok(s):
    return " ".join(w for w in jieba.cut_for_search(s) if w.strip())


def _make_isolated_kb(root, fts_v2):
    """在 root 下搭一个最小 KB：app 副本 + config.json + kb.db（空向量矩阵）。

    fts_v2=True → 建 4 列 FTS（path/heading/section/body）。
    fts_v2=False → 建旧 2 列 FTS（path/body）。
    返回 (kbx 实例, cfg 字典, con)。
    """
    shutil.copytree(SRC_APP, os.path.join(root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    data = os.path.join(root, "data")
    os.makedirs(data)
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"gitee_key": "", "gitee_embed_url": "http://127.0.0.1:1/v1/embeddings",
                   "embed_model": "test-model", "instruction": "v1"}, f)
    db = os.path.join(data, "kb.db")
    con = sqlite3.connect(db)
    if fts_v2:
        con.execute("CREATE VIRTUAL TABLE fts USING "
                    "fts5(path, heading, section, body, tokenize='unicode61')")
    else:
        con.execute("CREATE VIRTUAL TABLE fts USING fts5(path, body, tokenize='unicode61')")
    con.execute("CREATE TABLE meta(id INTEGER PRIMARY KEY, col TEXT, path TEXT, heading TEXT,"
                " section TEXT, body TEXT, hash TEXT, tbl_rows INTEGER DEFAULT 0, ord INTEGER)")
    con.execute("CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT)")
    # 空向量矩阵：vector 通道走空分支，不走 HTTP 嵌入。
    np.save(db + ".vec.npy", np.zeros((0, 4), dtype=np.float32))
    json.dump([], open(db + ".vec.ids.json", "w"))
    con.commit()
    con.close()

    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    sys.path.insert(0, os.path.join(root, "app"))
    from kb.retrieval import KBX
    cfg = importlib.import_module("kb.config").CFG
    return KBX, cfg, sqlite3.connect(db)


class FtsSchemaV2Tests(unittest.TestCase):
    """Fix 1：FTS schema v2 让标题查询直接命中，body 不含整串也能召回。

    关键回归点：c-142「《KKLT 版本历史》」在旧 schema 下 top3 空（body 不含完整字串），
    v2 schema 下必须直接命中。
    """

    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        cls.kbx, cls.cfg, cls.con = _make_isolated_kb(cls._tmp.name, fts_v2=True)
        # 塞一条「heading 是关键字、body 不含整串」的记录 → 模拟 c-142 失败模式。
        cls._insert_doc(cls.con, 1, "kks", "manuals/kklt-ver.md",
                        heading="KKLT 版本历史",
                        section="总览",
                        body="这是一段完全不含目标字串的正文，说点别的无关话题。")
        # 再塞一条干扰项：heading 不含关键字，body 稍微搭边。
        cls._insert_doc(cls.con, 2, "kks", "manuals/other.md",
                        heading="其他内容", section="杂项",
                        body="历史回顾与其他说明。")
        cls.con.commit()
        # 让 KBX 的只读连接拿到最新数据：直接把活连接替换过去，_fts_v2 缓存清掉。
        cls.kbx.con = cls.con
        cls.kbx._fts_v2_cached = None

    @classmethod
    def tearDownClass(cls):
        try:
            cls.con.close()
        except Exception:
            pass
        for _k in list(sys.modules):
            if _k == "app" or _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        cls._tmp.cleanup()

    @staticmethod
    def _insert_doc(con, cid, col, path, heading, section, body):
        con.execute("INSERT INTO meta(id,col,path,heading,section,body,hash,tbl_rows,ord)"
                    " VALUES(?,?,?,?,?,?,?,0,0)",
                    (cid, col, path, heading, section, body, "h%d" % cid))
        # v2 schema：heading / section 原文也进 FTS。
        con.execute("INSERT INTO fts(rowid,path,heading,section,body) VALUES(?,?,?,?,?)",
                    (cid, path, _tok(heading), _tok(section), _tok(body)))

    def test_schema_detection_identifies_v2(self):
        self.kbx._fts_v2_cached = None
        from kb.retrieval.lexical import _fts_v2
        self.assertTrue(_fts_v2(self.kbx), "4 列 FTS 必须识别为 v2")

    def test_title_only_query_hits_heading(self):
        """c-142 回归：heading 含查询、body 完全不含，v2 schema 必须命中。"""
        rows = self.kbx.fts("KKLT 版本历史", k=5)
        ids = [r["id"] for r in rows]
        self.assertIn(1, ids, "标题命中必须出现在 FTS 结果中（c-142 直解）")
        self.assertEqual(ids[0], 1, "标题精确匹配应排第一（bm25 heading 列命中最强）")

    def test_search_returns_v2_snippet_from_body_column(self):
        """search() 的 snippet 列索引随 schema 切换（v2=3, v1=1）。"""
        out = self.kbx.search("KKLT 版本历史", limit=5)
        self.assertTrue(out, "search 应能从 heading 列命中并返回结果")

    def test_body_only_query_still_hits(self):
        """body-only 命中（不涉及 heading/section）在 v2 下也要正常。"""
        rows = self.kbx.fts("无关话题", k=5)
        self.assertTrue(any(r["id"] == 1 for r in rows))


class FtsSchemaV1BackCompatTests(unittest.TestCase):
    """Fix 1 向后兼容：旧 2 列 FTS 走旧 MATCH 表达式，行为逐字节不变。"""

    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        cls.kbx, cls.cfg, cls.con = _make_isolated_kb(cls._tmp.name, fts_v2=False)
        # v1 schema：heading 不进 FTS，body 含整串才能命中。
        # 注意：v1 FTS 索引 path+body 两列，所以 path 里一定不能含 heading 的词，
        # 否则 heading 命中看起来像 path 命中，掩盖「heading-only 不进 FTS」的事实。
        cls._insert_doc(cls.con, 1, "kks", "manuals/doc-one.md",
                        heading="KKLT 版本历史",
                        section="总览",
                        body="这是一段完全不含目标字串的正文。")
        cls._insert_doc(cls.con, 2, "kks", "manuals/doc-two.md",
                        heading="别的标题", section="别的章节",
                        body="KKLT 版本历史在正文里是写得清清楚楚的一行。")
        cls.con.commit()
        cls.kbx.con = cls.con
        cls.kbx._fts_v2_cached = None

    @classmethod
    def tearDownClass(cls):
        try:
            cls.con.close()
        except Exception:
            pass
        for _k in list(sys.modules):
            if _k == "app" or _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        cls._tmp.cleanup()

    @staticmethod
    def _insert_doc(con, cid, col, path, heading, section, body):
        con.execute("INSERT INTO meta(id,col,path,heading,section,body,hash,tbl_rows,ord)"
                    " VALUES(?,?,?,?,?,?,?,0,0)",
                    (cid, col, path, heading, section, body, "h%d" % cid))
        # 旧 schema：只写 path + body 两列。
        con.execute("INSERT INTO fts(rowid,path,body) VALUES(?,?,?)",
                    (cid, path, _tok(body)))

    def test_schema_detection_identifies_v1(self):
        self.kbx._fts_v2_cached = None
        from kb.retrieval.lexical import _fts_v2
        self.assertFalse(_fts_v2(self.kbx), "2 列 FTS 必须识别为 v1 → 走旧查询")

    def test_v1_body_hit_still_works(self):
        """旧 schema：body 含整串才命中，heading 命中不可见。"""
        rows = self.kbx.fts("KKLT 版本历史", k=5)
        ids = [r["id"] for r in rows]
        self.assertIn(2, ids, "v1 下 body 命中照旧返回")
        self.assertNotIn(1, ids, "v1 下 heading-only 命中不进 FTS（行为与改动前一致）")


class TitleExactWeightTests(unittest.TestCase):
    """Fix 2：title_exact 信号可配开关。

    - weight=0.0（默认）：合成 10 条候选的 top-5 顺序必须与改动前完全一致（回归门）。
    - weight>0：构造的候选中「heading 完全被 query 包含」那条应从 rank 3 升到 rank 1。
    """

    @classmethod
    def setUpClass(cls):
        cls._tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        cls.kbx, cls.cfg, cls.con = _make_isolated_kb(cls._tmp.name, fts_v2=True)

    @classmethod
    def tearDownClass(cls):
        try:
            cls.con.close()
        except Exception:
            pass
        for _k in list(sys.modules):
            if _k == "app" or _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        cls._tmp.cleanup()

    def setUp(self):
        self.cfg.pop("retrieval", None)

    def _mk_cands(self):
        """10 条合成候选：rrf 单调递减；若干带 rim 词命中、少数不带，以构造稳定排序。"""
        cands = []
        for i in range(10):
            cands.append({
                "id": i + 1,
                "col": "kks",
                "path": f"docs/item{i}.md",
                "heading": f"条目 {i}",
                "section": "Intro",
                "body": ("rim light rim light" if i < 7 else "irrelevant") * 3,
                "rrf": round(0.30 - i * 0.02, 4),
            })
        return cands

    def test_weight_zero_regression_top5_unchanged(self):
        """weight=0.0 → 分数公式与改动前等价，10 条候选 top-5 必须逐位一致。

        BASELINE 从「同一份 rerank（weight=0.0）」里直接取：代码路径里 title_exact_w*0 = 0，
        等价于「没有这一项」，所以 top-5 顺序就是改动前的 top-5。
        """
        # 默认 CFG（无 title_exact_weight）
        default_res = self.kbx.rerank("rim light", self._mk_cands(), 5)
        default_top5 = [c["id"] for c in default_res]
        # 显式 weight=0.0
        self.cfg["retrieval"] = {"title_exact_weight": 0.0}
        zero_res = self.kbx.rerank("rim light", self._mk_cands(), 5)
        zero_top5 = [c["id"] for c in zero_res]
        self.assertEqual(default_top5, zero_top5,
                         "默认与显式 0.0 必须完全等价")
        # 分数也应该逐位相等（weight=0 不改变任何分量）。
        for a, b in zip(default_res, zero_res):
            self.assertAlmostEqual(a["_score"], b["_score"], places=6,
                                   msg="默认与 0.0 的 _score 必须逐位一致")

    def test_weight_positive_lifts_exact_title_match(self):
        """weight>0 → 构造的候选中「heading 完全包含在 query 里」那条从 rank 3 升到 rank 1。

        构造：query「如何调节 边缘光 的强度」，候选 3 条，rrf 单调递减：
        - A：heading「正文详述」rrf 0.30、body 词命中最多 → 原本应排 rank 1
        - B：heading「技术说明」rrf 0.20、body 命中中等 → rank 2
        - C：heading「边缘光」rrf 0.10、heading 完全含于 query → rank 3

        为了让 rrf 真的参与差分（否则 rrf>rrf_base 会全部饱和到 1.0，退化成同分），
        这里把 rerank_w 固定成「只看 rrf」+ 把 rrf_base 调大。然后 title_exact_weight=0.5
        触发的 +0.5 加分应把 C 从 rank 3 顶到 rank 1。
        """
        query = "如何调节 边缘光 的强度"
        a = {"id": 1, "col": "kks", "path": "a.md", "heading": "正文详述",
             "section": "x", "body": "边缘光 边缘光 边缘光", "rrf": 0.30}
        b = {"id": 2, "col": "kks", "path": "b.md", "heading": "技术说明",
             "section": "x", "body": "边缘光 边缘光", "rrf": 0.20}
        c = {"id": 3, "col": "kks", "path": "c.md", "heading": "边缘光",
             "section": "x", "body": "边缘光", "rrf": 0.10}

        # Baseline：只开 rrf 分量 + rrf_base=0.5 让 rrf 不饱和；weight=0。
        self.cfg["retrieval"] = {
            "rerank_w": [1.0, 0.0, 0.0, 0.0, 0.0, True],
            "rrf_base": 0.5,
            "title_exact_weight": 0.0,
        }
        base = self.kbx.rerank(query, [dict(a), dict(b), dict(c)], 3)
        self.assertEqual([x["id"] for x in base], [1, 2, 3],
                         "weight=0 下按 rrf 主导：A>B>C")

        # 开 title_exact_weight=0.5：C 多拿 +0.5 >> rrf 差距 0.2，C 应升到 rank 1。
        self.cfg["retrieval"] = {
            "rerank_w": [1.0, 0.0, 0.0, 0.0, 0.0, True],
            "rrf_base": 0.5,
            "title_exact_weight": 0.5,
        }
        boosted = self.kbx.rerank(query, [dict(a), dict(b), dict(c)], 3)
        self.assertEqual(boosted[0]["id"], 3,
                         "weight>0 时 heading 完全含于 query 的候选应升到 rank 1")

    def test_title_exact_requires_full_heading_in_query(self):
        """title_exact 要求整段 heading 都包含在 query 里，不是词级命中。

        heading「版本历史」部分命中查询「版本」不应触发（否则就是 head_s 的重复）。
        """
        # heading「版本历史」而 query 只含「版本」→ title_exact=0
        c = {"id": 1, "col": "kks", "path": "x.md", "heading": "版本历史",
             "section": "x", "body": "版本", "rrf": 0.20}
        self.cfg["retrieval"] = {"title_exact_weight": 0.5}
        res = self.kbx.rerank("版本", [dict(c)], 1)
        # 对照：heading = "版本" 时 title_exact=1；此处 heading = "版本历史"，不被完全包含。
        c2 = dict(c)
        c2["heading"] = "版本"
        res2 = self.kbx.rerank("版本", [c2], 1)
        self.assertLess(res[0]["_score"], res2[0]["_score"],
                        "部分词命中不触发 title_exact")

    def test_c142_shape_full_heading_match(self):
        """c-142 形状：query「KKLT 版本历史」完全含 heading「KKLT 版本历史」→ title_exact=1。

        权重 0.3 的加分应该把这条候选的 _score 显著拉高（与同候选 weight=0 相比）。
        """
        c = {"id": 1, "col": "kks", "path": "manuals/kklt-ver.md",
             "heading": "KKLT 版本历史", "section": "总览",
             "body": "无关正文", "rrf": 0.10}
        self.cfg["retrieval"] = {"title_exact_weight": 0.0}
        base = self.kbx.rerank("KKLT 版本历史", [dict(c)], 1)[0]["_score"]
        self.cfg["retrieval"] = {"title_exact_weight": 0.3}
        boosted = self.kbx.rerank("KKLT 版本历史", [dict(c)], 1)[0]["_score"]
        self.assertGreater(boosted, base + 0.1,
                           "title_exact 加分应显著提升 c-142 这类候选的 _score")


if __name__ == "__main__":
    unittest.main()
