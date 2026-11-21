#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""verify：文件哈希、integrity_check、三处行数相等、ids 对齐、向量有限且近似单位范数。"""
import hashlib
import json
import sqlite3
from pathlib import Path

import numpy as np

from .bundle import content_fingerprint, sha256_file, vector_fingerprint

UNIT_NORM_TOL = 1e-3


class VerifyError(RuntimeError):
    pass


def run_verify(paths, release_id):
    rel = paths.release_dir(release_id)
    if not rel.is_dir():
        raise VerifyError(f"发布目录不存在：{rel}")
    man_path = rel / "manifest.json"
    if not man_path.exists():
        raise VerifyError("缺少 manifest.json")
    manifest = json.loads(man_path.read_text(encoding="utf-8"))
    errors = []

    for name, want in manifest.get("files", {}).items():
        fp = rel / name
        if not fp.exists():
            errors.append(f"文件缺失：{name}")
            continue
        if sha256_file(fp) != want:
            errors.append(f"哈希不符：{name}")

    db = rel / "kb.db"
    if db.exists():
        con = sqlite3.connect(str(db))
        try:
            ok = con.execute("PRAGMA integrity_check").fetchone()[0]
            if ok != "ok":
                errors.append(f"integrity_check: {ok}")
            meta_count = con.execute("SELECT COUNT(*) FROM meta").fetchone()[0]
            ids_in_meta = [r[0] for r in con.execute("SELECT id FROM meta ORDER BY id")]
        finally:
            con.close()
    else:
        errors.append("文件缺失：kb.db")
        meta_count, ids_in_meta = 0, []

    if manifest.get("vectors"):
        vec_path = rel / "kb.db.vec.npy"
        ids_path = rel / "kb.db.vec.ids.json"
        if not vec_path.exists() or not ids_path.exists():
            errors.append("向量文件缺失")
        else:
            vec = np.load(vec_path, mmap_mode="r")
            ids_json = json.loads(ids_path.read_text(encoding="utf-8"))
            if vec.shape[0] != meta_count or len(ids_json) != meta_count:
                errors.append("meta/向量/ids 行数不一致")
            if ids_json != ids_in_meta:
                errors.append("ids 与 meta 不对齐")
            if not np.isfinite(vec).all():
                errors.append("向量含非有限值")
            norms = np.linalg.norm(vec.astype(np.float32), axis=1)
            if len(norms) and (np.abs(norms - 1.0) > UNIT_NORM_TOL).any():
                errors.append("向量范数偏离 1")
            if manifest.get("vector_fingerprint") is not None:
                if vector_fingerprint(vec) != manifest["vector_fingerprint"]:
                    errors.append("向量指纹不符")

    if manifest.get("content_fingerprint") is not None and db.exists():
        if content_fingerprint(str(db)) != manifest["content_fingerprint"]:
            errors.append("内容指纹不符")

    if errors:
        raise VerifyError("verify 失败：\n" + "\n".join("  - " + e for e in errors))
    return manifest
