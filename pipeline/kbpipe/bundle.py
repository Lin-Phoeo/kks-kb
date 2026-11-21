#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""bundle：把 kb.db + 向量 + manifest 组装进 releases/<id>/。

确定性以 content_fingerprint 为准（SQLite 文件本身不保证逐字节相同）。
"""
import hashlib
import json
import shutil
import sqlite3
from pathlib import Path

import numpy as np


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def content_fingerprint(db_path):
    """meta 里 (col, path, body_hash) 按 id 序的稳定指纹。"""
    con = sqlite3.connect(f"file:{Path(db_path).as_posix()}?mode=ro", uri=True)
    try:
        rows = con.execute("SELECT col, path, hash FROM meta ORDER BY id").fetchall()
    finally:
        con.close()
    h = hashlib.sha256()
    for col, path, body_hash in rows:
        h.update((col + "\0" + path + "\0" + body_hash + "\n").encode("utf-8"))
    return h.hexdigest()


def collection_counts(db_path, collections):
    con = sqlite3.connect(f"file:{Path(db_path).as_posix()}?mode=ro", uri=True)
    try:
        docs = dict(con.execute("SELECT col, COUNT(*) FROM doc GROUP BY col").fetchall())
        chunks = dict(con.execute("SELECT col, COUNT(*) FROM meta GROUP BY col").fetchall())
    finally:
        con.close()
    return {col: {"docs": int(docs.get(col, 0)), "chunks": int(chunks.get(col, 0))}
            for col in collections}


def vector_fingerprint(mat):
    return hashlib.sha256(
        np.ascontiguousarray(mat, dtype=np.float32).tobytes()).hexdigest()


def run_bundle(paths, cfg, release_id, no_embed=False):
    rel = paths.release_dir(release_id)
    rel.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(paths.work_db, rel / "kb.db")

    files = {"kb.db": sha256_file(rel / "kb.db")}
    dim = None
    vfingerprint = None
    has_vectors = (not no_embed) and paths.vec_npy.exists() and paths.vec_ids.exists()
    if has_vectors:
        shutil.copyfile(paths.vec_npy, rel / "kb.db.vec.npy")
        shutil.copyfile(paths.vec_ids, rel / "kb.db.vec.ids.json")
        files["kb.db.vec.npy"] = sha256_file(rel / "kb.db.vec.npy")
        files["kb.db.vec.ids.json"] = sha256_file(rel / "kb.db.vec.ids.json")
        mat = np.load(rel / "kb.db.vec.npy", mmap_mode="r")
        dim = int(mat.shape[1]) if mat.ndim == 2 else 0
        vfingerprint = vector_fingerprint(mat)

    manifest = {
        "files": files,
        "collections": collection_counts(str(rel / "kb.db"), cfg["collections"]),
        "embed_model": cfg["embed_model"],
        "dim": dim,
        "instruction_version": cfg.get("instruction", ""),
        "content_fingerprint": content_fingerprint(str(rel / "kb.db")),
        "vector_fingerprint": vfingerprint,
        "vectors": has_vectors,
    }
    with open(rel / "manifest.json", "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    return manifest
