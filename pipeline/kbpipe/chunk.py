#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""chunk：复用 indexer.chunk_doc，产出与建库时完全一致的块序列。"""
import hashlib
import json
import re

from ._compat import chunk_doc


def doc_title(rel, txt):
    """与 indexer 相同：取文档第一个 # 标题，否则退回相对路径。"""
    m = re.search(r"^#\s+(.+)$", txt, re.M)
    return m.group(1).strip() if m else rel


def chunk_record(collection, rel, txt):
    """单文档 → 块记录列表。hash 与 indexer 的 meta.hash 口径一致（sha256(body)[:16]）。"""
    title = doc_title(rel, txt)
    recs = []
    for k, (sec, body) in enumerate(chunk_doc(txt)):
        recs.append({
            "collection": collection,
            "path": rel,
            "title": title,
            "section": sec,
            "body": body,
            "ord": k,
            "hash": hashlib.sha256(body.encode("utf-8")).hexdigest()[:16],
        })
    return recs


def run_chunk(paths, files):
    """scan 结果 → chunks.jsonl（顺序与 indexer 建库逐位一致）。"""
    paths.work.mkdir(parents=True, exist_ok=True)
    n = 0
    with open(paths.chunks_file, "w", encoding="utf-8", newline="\n") as f:
        for fe in files:
            with open(fe["abs"], encoding="utf-8", errors="ignore") as fh:
                txt = fh.read()
            for rec in chunk_record(fe["collection"], fe["path"], txt):
                f.write(json.dumps(rec, ensure_ascii=False) + "\n")
                n += 1
    return n
