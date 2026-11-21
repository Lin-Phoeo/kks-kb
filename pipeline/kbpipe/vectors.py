#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""vectors：按 meta.id 顺序从缓存取向量，缓存必须全命中；L2 归一化与 indexer 一致。"""
import json
import sqlite3
from pathlib import Path

import numpy as np

from .embed import EmbedCache, cache_key


def run_vectors(paths, model):
    db = Path(paths.work_db).as_posix()
    con = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
    try:
        rows = con.execute("SELECT id, body FROM meta ORDER BY id").fetchall()
    finally:
        con.close()
    ids = [r[0] for r in rows]

    cache = EmbedCache(paths.embed_cache_db)
    try:
        mat = None
        for i, body in enumerate(r[1] for r in rows):
            v = cache.get(cache_key(model, body))
            if v is None:
                raise RuntimeError(f"缓存未命中：id={ids[i]}（body 不在 embed.sqlite）")
            if mat is None:
                mat = np.zeros((len(ids), int(v.shape[0])), dtype=np.float32)
            mat[i] = v
        if mat is None:
            mat = np.zeros((0, 0), dtype=np.float32)
    finally:
        cache.close()

    mat /= (np.linalg.norm(mat, axis=1, keepdims=True) + 1e-9)
    np.save(paths.vec_npy, mat)
    with open(paths.vec_ids, "w", encoding="utf-8", newline="\n") as f:
        json.dump(ids, f)
    dim = int(mat.shape[1]) if mat.ndim == 2 else 0
    return {"ids": len(ids), "dim": dim}
