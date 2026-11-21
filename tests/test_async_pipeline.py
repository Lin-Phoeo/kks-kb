#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""D3 异步化 + 查询向量缓存测试。

不碰真实嵌入 API：`_embed_via_http` 用 mock 替换。KBX/embed_cache 在临时
app 副本里加载，与 test_import_direction 同款隔离，不依赖线上 kb.db。
"""
import asyncio
import json
import os
import pickle
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
vector = None
embed_cache = None
_orig_mat = None


def setUpModule():
    global _tmp, _root, kbx, vector, embed_cache, _orig_mat
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    _root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(_root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    os.makedirs(os.path.join(_root, "data"))
    with open(os.path.join(_root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"gitee_key": "test-key",
                   "gitee_embed_url": "http://127.0.0.1:1/v1/embeddings",
                   "embed_model": "test-model", "instruction": "v1"}, f)
    con = sqlite3.connect(os.path.join(_root, "data", "kb.db"))
    con.executescript(
        "CREATE VIRTUAL TABLE fts USING fts5(path, body, tokenize='unicode61');\n"
        "CREATE TABLE meta(id INTEGER PRIMARY KEY, col TEXT, path TEXT, heading TEXT,\n"
        "  section TEXT, body TEXT, hash TEXT, tbl_rows INTEGER DEFAULT 0, ord INTEGER);\n"
        "CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT);")
    con.commit()
    con.close()

    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    sys.path.insert(0, os.path.join(_root, "app"))

    from kb.retrieval import KBX  # noqa: E402
    import importlib
    from kb.retrieval import embed_cache as _ec  # noqa: E402
    vector = importlib.import_module("kb.retrieval.vector")
    kbx, embed_cache = KBX, _ec
    _orig_mat = kbx.mat


def tearDownModule():
    global _tmp
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    if _tmp:
        _tmp.cleanup()


def _clear_cache():
    vector.CACHE.lru.clear()
    vector.CACHE.store.execute("DELETE FROM embed_cache")
    vector.CACHE.store.commit()


class CacheIsolation(unittest.TestCase):
    def setUp(self):
        _clear_cache()

    def tearDown(self):
        _clear_cache()
        kbx.mat = _orig_mat


class LruTests(unittest.TestCase):
    def test_inserting_513_evicts_oldest(self):
        lru = embed_cache.LRU(512)
        for i in range(513):
            lru.put(f"k{i}", i)
        self.assertEqual(len(lru), 512)
        self.assertIsNone(lru.get("k0"), "最早插入的应被淘汰")
        self.assertEqual(lru.get("k512"), 512)
        # 访问过 k1 后它变最新，再塞满一轮也不该最先走
        self.assertEqual(lru.get("k1"), 1)
        for i in range(513, 1024):
            lru.put(f"k{i}", i)
        self.assertIsNotNone(lru.get("k1"))

    def test_hit_returns_cached_vector(self):
        c = embed_cache.QueryEmbedCache(maxsize=512, store=None)
        v = np.array([0.6, 0.8], dtype=np.float32)
        c.put("k", v)
        hit = c.get("k")
        np.testing.assert_array_equal(hit, v)
        # 未命中的 key 返回 None，不抛异常
        self.assertIsNone(c.get("missing"))


class CacheKeyTests(unittest.TestCase):
    def test_key_differs_when_instruction_or_text_differs(self):
        k = embed_cache.cache_key
        base = k("m", "v1", "文本")
        self.assertEqual(base, k("m", "v1", "文本"))
        self.assertNotEqual(base, k("m", "v2", "文本"))
        self.assertNotEqual(base, k("m", "v1", "另一段文本"))
        self.assertNotEqual(base, k("m2", "v1", "文本"))


class EmbedCacheHitTests(CacheIsolation):
    def test_lru_hit_skips_embed_api(self):
        fake = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)
        with mock.patch.object(vector, "_embed_via_http", return_value=fake) as m:
            kbx.mat = np.zeros((2, 4), dtype=np.float32)
            v1 = kbx.embed("同一个问题")
            self.assertEqual(m.call_count, 1)
            v2 = kbx.embed("同一个问题")   # LRU 命中，不再调 HTTP
            self.assertEqual(m.call_count, 1)
        np.testing.assert_array_equal(v1, v2)


class PersistentCacheTests(CacheIsolation):
    def test_second_query_hits_state_db_without_embed(self):
        fake = np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)
        with mock.patch.object(vector, "_embed_via_http", return_value=fake) as m:
            kbx.mat = np.zeros((2, 4), dtype=np.float32)
            v1 = kbx.embed("持久化问题")
            self.assertEqual(m.call_count, 1)
            vector.CACHE.lru.clear()        # 只剩 state.db 持久层
            v2 = kbx.embed("持久化问题")
            self.assertEqual(m.call_count, 1, "第二次应命中 state.db，不再调嵌入")
        np.testing.assert_array_equal(v1, v2)
        key = vector._embed_key("持久化问题")
        row = vector.CACHE.store.fetchone(
            "SELECT dim, vec FROM embed_cache WHERE key=?", (key,))
        self.assertIsNotNone(row, "持久表应有该条缓存")
        self.assertEqual(row[0], 4)


class ThreadWrapperTests(unittest.TestCase):
    def test_to_thread_propagates_exception(self):
        def boom(q, k=6, col=""):
            raise ValueError("boom")

        with mock.patch.object(kbx, "hybrid", side_effect=boom):
            with self.assertRaises(ValueError):
                asyncio.run(kbx.hybrid_async("q"))

    def test_to_thread_returns_result(self):
        def ok(q, k=6, col=""):
            return "done"

        with mock.patch.object(kbx, "hybrid", side_effect=ok):
            self.assertEqual(asyncio.run(kbx.hybrid_async("q")), "done")


class EmbedAsyncTests(CacheIsolation):
    def test_embed_async_uses_async_http_once_then_hits_lru(self):
        calls = []

        async def fake_http(text):
            calls.append(text)
            return np.array([1.0, 0.0, 0.0, 0.0], dtype=np.float32)

        with mock.patch.object(vector, "_embed_via_http_async", side_effect=fake_http):
            kbx.mat = np.zeros((2, 4), dtype=np.float32)
            v1 = asyncio.run(kbx.embed_async("异步问题"))
            v2 = asyncio.run(kbx.embed_async("异步问题"))
        self.assertEqual(len(calls), 1, "第二次应命中 LRU，不再走异步 HTTP")
        np.testing.assert_array_equal(v1, v2)


class RankingUnchangedTests(unittest.TestCase):
    def test_sync_and_async_wrapper_byte_identical(self):
        canned = ([{"id": 1, "col": "kks", "path": "a/b.md", "heading": "标题",
                    "section": "小节", "anchor": "", "rrf": 0.1234,
                    "preview": "预览正文", "body": "完整正文"},
                   {"id": 2, "col": "kks", "path": "c/d.md", "heading": "标题二",
                    "section": "", "anchor": "", "rrf": 0.12,
                    "preview": "预览二", "body": "正文二"}],
                  {"fts": 2, "vec": 0, "path": 1})
        with mock.patch.object(kbx, "hybrid", return_value=canned):
            sync_res = kbx.hybrid("rim", 6, "")
            async_res = asyncio.run(kbx.hybrid_async("rim", 6, ""))
        self.assertEqual(pickle.dumps(sync_res), pickle.dumps(async_res))


if __name__ == "__main__":
    unittest.main()
