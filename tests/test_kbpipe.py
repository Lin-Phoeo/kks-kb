#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""kbpipe 数据流水线的测试。只用假嵌入器（确定性向量），不碰网络。

覆盖：切块与 indexer.chunk_doc 逐位一致、幂等（第二次零调用）、断点续跑、
去重、向量行序对齐、verify 抓篡改、publish/rollback 原子性、
Windows 空格 + 非 ASCII 路径。
"""
import hashlib
import json
import os
import sys
import tempfile
import unittest

import numpy as np

_HERE = os.path.dirname(os.path.abspath(__file__))
_ROOT = os.path.abspath(os.path.join(_HERE, ".."))
for p in (_ROOT, os.path.join(_ROOT, "app")):
    if p not in sys.path:
        sys.path.insert(0, p)

import indexer  # noqa: E402  直接调 chunk_doc 做对比（同 test_indexer_guard 的做法）

from pipeline.kbpipe.config import Paths, load_config  # noqa: E402
from pipeline.kbpipe import scan as kbscan  # noqa: E402
from pipeline.kbpipe import chunk as kbchunk  # noqa: E402
from pipeline.kbpipe import embed as kbembed  # noqa: E402
from pipeline.kbpipe import index as kbindex  # noqa: E402
from pipeline.kbpipe import vectors as kbvectors  # noqa: E402
from pipeline.kbpipe import bundle as kbbundle  # noqa: E402
from pipeline.kbpipe import verify as kbverify  # noqa: E402
from pipeline.kbpipe import publish as kbpublish  # noqa: E402

DIM = 8


def fake_vectors(bodies, dim=DIM):
    """确定性向量：由 body 长度派生，非零，未经归一化。"""
    out = []
    for b in bodies:
        v = np.zeros(dim, dtype=np.float64)
        v[0] = float(len(b) % 7) + 1.0
        v[1] = 1.0
        out.append(v)
    return out


def normalise(v):
    a = np.asarray(v, dtype=np.float32).reshape(1, -1)
    a /= (np.linalg.norm(a, axis=1, keepdims=True) + 1e-9)
    return a[0]


class CountingEmbedder:
    def __init__(self, dim=DIM):
        self.calls = []          # 每次调用的 body 列表
        self.bodies_called = 0
        self.dim = dim

    def __call__(self, bodies):
        self.calls.append(list(bodies))
        self.bodies_called += len(bodies)
        return fake_vectors(bodies, self.dim)


class FailingEmbedder:
    """第 fail_on_call 次调用时抛异常，模拟中断。"""

    def __init__(self, fail_on_call, dim=DIM):
        self.fail_on_call = fail_on_call
        self.calls = []
        self.dim = dim

    def __call__(self, bodies):
        self.calls.append(list(bodies))
        if len(self.calls) == self.fail_on_call:
            raise RuntimeError("simulated crash")
        return fake_vectors(bodies, self.dim)


def make_corpus(paths, spec):
    """spec: list of (collection, relpath, text)。"""
    for col, rel, text in spec:
        p = paths.col_dir(col) / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text, encoding="utf-8")


def run_pipeline(paths, cfg, embedder, release="r1", no_embed=False, run_index=True):
    files = kbscan.run_scan(paths, cfg["collections"])
    kbchunk.run_chunk(paths, files)
    if not no_embed:
        kbembed.run_embed(paths, embedder, cfg["embed_model"], cfg["batch"])
    if run_index:
        kbindex.run_index(paths, cfg["collections"])
        if not no_embed:
            kbvectors.run_vectors(paths, cfg["embed_model"])
    return kbbundle.run_bundle(paths, cfg, release, no_embed=no_embed)


class ChunkParityTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.paths = Paths(self.d.name)

    def tearDown(self):
        self.d.cleanup()

    def test_chunk_record_fields_match_indexer(self):
        txt = ("# 主标题\n\n## 小节 A\n\n正文段落。\n\n"
               "| a | b |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n")
        recs = kbchunk.chunk_record("kks", "sub/x.md", txt)
        direct = indexer.chunk_doc(txt)
        self.assertEqual([(r["section"], r["body"]) for r in recs], direct)
        self.assertEqual(recs[0]["title"], "主标题")
        self.assertEqual(recs[0]["path"], "sub/x.md")
        self.assertEqual(recs[0]["ord"], 0)
        for r in recs:
            self.assertEqual(r["hash"],
                             hashlib.sha256(r["body"].encode("utf-8")).hexdigest()[:16])

    def test_scan_chunk_parity_over_corpus(self):
        make_corpus(self.paths, [
            ("kks", "a.md", "# A\n\nbody A\n"),
            ("kks", "b.md", "# B\n\n## s\n\nbody B\n\n| x |\n|---|\n| 1 |\n"),
        ])
        files = kbscan.run_scan(self.paths, ["kks"])
        kbchunk.run_chunk(self.paths, files)
        records = list(kbembed.iter_chunks(self.paths.chunks_file))
        for fe in files:
            with open(fe["abs"], encoding="utf-8", errors="ignore") as f:
                txt = f.read()
            got = [(r["section"], r["body"]) for r in records
                   if r["path"] == fe["path"]]
            self.assertEqual(got, indexer.chunk_doc(txt))


class ScanFilterTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.paths = Paths(self.d.name)

    def tearDown(self):
        self.d.cleanup()

    def test_excludes_and_append_prefix(self):
        make_corpus(self.paths, [
            ("kks", "keep.md", "# K\n"),
            ("kks", "全书标题索引.md", "# excluded\n"),
            ("kks", "00-手册总目录.md", "# excluded\n"),
            ("kks", "全库shader参数注册表.md", "# excluded\n"),
            ("kks", "_append_extra.md", "# excluded\n"),
            ("kks", "sub/keep2.md", "# K2\n"),
        ])
        files = kbscan.run_scan(self.paths, ["kks"])
        rels = [f["path"] for f in files]
        self.assertEqual(rels, ["keep.md", "sub/keep2.md"])


class EmbedTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.paths = Paths(self.d.name)
        self.cfg = load_config(None)
        self.cfg["batch"] = 16
        self.cfg["collections"] = ["kks"]

    def tearDown(self):
        self.d.cleanup()

    def _chunked(self, spec):
        make_corpus(self.paths, spec)
        files = kbscan.run_scan(self.paths, self.cfg["collections"])
        kbchunk.run_chunk(self.paths, files)
        return files

    def test_idempotency_second_run_zero_calls(self):
        self._chunked([("kks", "a.md", "# A\n\nhello world\n"),
                       ("kks", "b.md", "# B\n\nanother body\n")])
        emb1 = CountingEmbedder()
        kbembed.run_embed(self.paths, emb1, self.cfg["embed_model"], self.cfg["batch"])
        self.assertGreater(emb1.bodies_called, 0)

        emb2 = CountingEmbedder()
        kbembed.run_embed(self.paths, emb2, self.cfg["embed_model"], self.cfg["batch"])
        self.assertEqual(emb2.bodies_called, 0)

    def test_resume_only_missing_batches(self):
        self.cfg["batch"] = 1
        self._chunked([("kks", f"d{i}.md", f"# D{i}\n\nbody {i}\n") for i in range(3)])

        failing = FailingEmbedder(fail_on_call=2)
        with self.assertRaises(RuntimeError):
            kbembed.run_embed(self.paths, failing, self.cfg["embed_model"], 1)

        restarted = CountingEmbedder()
        res = kbembed.run_embed(self.paths, restarted, self.cfg["embed_model"], 1)
        self.assertEqual(res["embedded"], 2)          # 只剩 2 个缺失 key
        self.assertEqual(restarted.bodies_called, 2)

        cache = kbembed.EmbedCache(self.paths.embed_cache_db)
        try:
            order, _ = kbembed.distinct_keys(self.paths.chunks_file, self.cfg["embed_model"])
            for k in order:
                self.assertIsNotNone(cache.get(k))
        finally:
            cache.close()

    def test_dedupe_same_body_embeds_once(self):
        body = "identical body text across two docs"
        # 两篇文档内容完全相同 → chunk body 相同 → 缓存 key 相同，只嵌一次
        self._chunked([("kks", "x.md", f"# 同\n\n{body}\n"),
                       ("kks", "y.md", f"# 同\n\n{body}\n"),
                       ("kks", "z.md", "# Z\n\nunique z body\n")])
        emb = CountingEmbedder()
        kbembed.run_embed(self.paths, emb, self.cfg["embed_model"], self.cfg["batch"])
        hits = sum(1 for c in emb.calls for b in c if body in b)
        self.assertEqual(hits, 1)

    def test_dry_run_counts_missing(self):
        self._chunked([("kks", "a.md", "# A\n\nbody A\n"),
                       ("kks", "b.md", "# B\n\nbody B\n")])
        self.assertEqual(kbembed.count_missing(self.paths, self.cfg["embed_model"]), 2)
        kbembed.run_embed(self.paths, CountingEmbedder(),
                          self.cfg["embed_model"], self.cfg["batch"])
        self.assertEqual(kbembed.count_missing(self.paths, self.cfg["embed_model"]), 0)


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.paths = Paths(self.d.name)
        self.cfg = load_config(None)
        self.cfg["collections"] = ["kks", "ai"]

    def tearDown(self):
        self.d.cleanup()

    def _full(self, embedder, release="r1", no_embed=False):
        make_corpus(self.paths, [
            ("kks", "a.md", "# A\n\nkks body one\n"),
            ("kks", "b.md", "# B\n\nkks body two\n"),
            ("ai", "c.md", "# C\n\nai body three\n"),
        ])
        return run_pipeline(self.paths, self.cfg, embedder, release=release,
                            no_embed=no_embed)

    def test_vector_row_order_matches_meta_id(self):
        emb = CountingEmbedder()
        self._full(emb, "r1")

        con = __import__("sqlite3").connect(str(self.paths.release_dir("r1") / "kb.db"))
        try:
            ids = [r[0] for r in con.execute("SELECT id FROM meta ORDER BY id")]
            bodies = [r[0] for r in con.execute("SELECT body FROM meta ORDER BY id")]
        finally:
            con.close()

        ids_json = json.loads((self.paths.release_dir("r1") / "kb.db.vec.ids.json")
                              .read_text(encoding="utf-8"))
        self.assertEqual(ids_json, ids)

        mat = np.load(self.paths.release_dir("r1") / "kb.db.vec.npy")
        self.assertEqual(mat.shape[0], len(ids))
        for i, body in enumerate(bodies):
            expect = normalise(fake_vectors([body])[0])
            self.assertTrue(np.allclose(mat[i], expect, atol=1e-6))

    def test_verify_catches_tampered_db_and_vec(self):
        emb = CountingEmbedder()
        self._full(emb, "r1")
        kbverify.run_verify(self.paths, "r1")   # 干净包应通过

        rel = self.paths.release_dir("r1")
        # 篡改 kb.db：追加垃圾字节 → 文件哈希不符
        with open(rel / "kb.db", "ab") as f:
            f.write(b"\x00garbage")
        with self.assertRaises(kbverify.VerifyError):
            kbverify.run_verify(self.paths, "r1")

        # 恢复 db，再篡改向量文件 → 哈希 + 指纹不符
        kbbundle.run_bundle(self.paths, self.cfg, "r1")
        kbverify.run_verify(self.paths, "r1")
        mat = np.load(rel / "kb.db.vec.npy")
        mat[0, 0] += 100.0
        np.save(rel / "kb.db.vec.npy", mat)
        with self.assertRaises(kbverify.VerifyError):
            kbverify.run_verify(self.paths, "r1")

    def test_content_fingerprint_deterministic_across_roots(self):
        emb1 = CountingEmbedder()
        self._full(emb1, "r1")
        fp1 = json.loads((self.paths.release_dir("r1") / "manifest.json")
                         .read_text(encoding="utf-8"))

        d2 = tempfile.TemporaryDirectory()
        try:
            paths2 = Paths(d2.name)
            make_corpus(paths2, [
                ("kks", "a.md", "# A\n\nkks body one\n"),
                ("kks", "b.md", "# B\n\nkks body two\n"),
                ("ai", "c.md", "# C\n\nai body three\n"),
            ])
            run_pipeline(paths2, self.cfg, CountingEmbedder(), release="r1")
            fp2 = json.loads((paths2.release_dir("r1") / "manifest.json")
                             .read_text(encoding="utf-8"))
        finally:
            d2.cleanup()

        self.assertEqual(fp1["content_fingerprint"], fp2["content_fingerprint"])
        self.assertEqual(fp1["vector_fingerprint"], fp2["vector_fingerprint"])

    def test_no_embed_fts_only_bundle(self):
        self._full(None, "r1", no_embed=True)
        rel = self.paths.release_dir("r1")
        manifest = json.loads((rel / "manifest.json").read_text(encoding="utf-8"))
        self.assertFalse(manifest["vectors"])
        self.assertIsNone(manifest["dim"])
        self.assertFalse((rel / "kb.db.vec.npy").exists())
        kbverify.run_verify(self.paths, "r1")   # FTS-only 也能 verify


class PublishRollbackTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.paths = Paths(self.d.name)
        self.cfg = load_config(None)
        self.cfg["collections"] = ["kks"]
        make_corpus(self.paths, [("kks", "a.md", "# A\n\nbody\n")])
        run_pipeline(self.paths, self.cfg, CountingEmbedder(), release="r1")
        run_pipeline(self.paths, self.cfg, CountingEmbedder(), release="r2")

    def tearDown(self):
        self.d.cleanup()

    def test_publish_rollback_and_resolve_current(self):
        self.assertIsNone(kbpublish.resolve_current(self.paths.root))
        self.assertIsNone(kbpublish.resolve_previous(self.paths.root))

        kbpublish.publish(self.paths, "r1")
        self.assertEqual(kbpublish.resolve_current(self.paths.root),
                         self.paths.release_dir("r1"))
        self.assertIsNone(kbpublish.resolve_previous(self.paths.root))

        kbpublish.publish(self.paths, "r2")
        self.assertEqual(kbpublish.resolve_current(self.paths.root),
                         self.paths.release_dir("r2"))
        self.assertEqual(kbpublish.resolve_previous(self.paths.root),
                         self.paths.release_dir("r1"))

        kbpublish.rollback(self.paths)
        self.assertEqual(kbpublish.resolve_current(self.paths.root),
                         self.paths.release_dir("r1"))
        self.assertEqual(kbpublish.resolve_previous(self.paths.root),
                         self.paths.release_dir("r2"))

        # 发布/回滚绝不删除旧发布目录
        self.assertTrue(self.paths.release_dir("r1").is_dir())
        self.assertTrue(self.paths.release_dir("r2").is_dir())

    def test_text_pointer_resolution_forced(self):
        # 强制走文本指针路径（Windows 回退形态），不依赖平台
        kbpublish._atomic_write_pointer(self.paths.root / "current", "r1")
        self.assertEqual(kbpublish.resolve_current(self.paths.root),
                         self.paths.release_dir("r1"))
        self.assertEqual((self.paths.root / "current").read_text(encoding="utf-8").strip(),
                         "r1")

    def test_prune_keeps_referenced_releases(self):
        kbpublish.publish(self.paths, "r1")           # current=r1
        run_pipeline(self.paths, self.cfg, CountingEmbedder(), release="r3")
        kbpublish.publish(self.paths, "r2")           # current=r2, previous=r1
        removed = kbpublish.prune(self.paths)
        self.assertIn("r3", removed)
        self.assertTrue(self.paths.release_dir("r1").is_dir())   # previous
        self.assertTrue(self.paths.release_dir("r2").is_dir())   # current
        self.assertFalse(self.paths.release_dir("r3").exists())


class WindowsPathTests(unittest.TestCase):
    def test_spaces_and_nonascii_paths(self):
        d = tempfile.TemporaryDirectory(prefix="kbpipe 测试 space ")
        try:
            paths = Paths(d.name)
            cfg = load_config(None)
            cfg["collections"] = ["kks"]
            make_corpus(paths, [("kks", "章节 一/测试 文档.md",
                                 "# 标题\n\n中文正文 body。\n")])
            files = kbscan.run_scan(paths, cfg["collections"])
            self.assertEqual(files[0]["path"], "章节 一/测试 文档.md")
            run_pipeline(paths, cfg, CountingEmbedder(), release="r1")
            kbverify.run_verify(paths, "r1")
            rel = paths.release_dir("r1")
            self.assertTrue((rel / "kb.db").exists())
            self.assertTrue((rel / "kb.db.vec.npy").exists())
        finally:
            d.cleanup()


if __name__ == "__main__":
    unittest.main()
