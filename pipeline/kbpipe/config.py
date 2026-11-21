#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""sources.json 加载与目录约定。

sources.json（JSON，非 YAML）字段：
  embed_model / embed_url / batch / instruction / collections
collections 顺序决定 chunk id 的分配顺序。
"""
import json
import os
from pathlib import Path

DEFAULT_EMBED_MODEL = "Qwen3-Embedding-4B"
DEFAULT_EMBED_URL = "https://ai.gitee.com/v1/embeddings"
DEFAULT_BATCH = 16
DEFAULT_COLLECTIONS = ["kks", "ai", "ref", "mods", "doc2", "shader", "src", "web", "pidx", "dc"]

# collection 名 → col/ 下的目录名。只有 pidx 例外：参数索引放在 col/paramidx。
_COL_DIR_OVERRIDES = {"pidx": "paramidx"}


def default_config():
    return {
        "embed_model": DEFAULT_EMBED_MODEL,
        "embed_url": DEFAULT_EMBED_URL,
        "batch": DEFAULT_BATCH,
        "instruction": "",
        "collections": list(DEFAULT_COLLECTIONS),
    }


def load_config(path=None):
    """读 sources.json；缺字段用默认值。path 不存在时退回默认配置。"""
    cfg = default_config()
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            raw = json.load(f)
        for key in cfg:
            if key in raw:
                cfg[key] = raw[key]
    collections = cfg["collections"]
    if not isinstance(collections, list) or not collections:
        raise ValueError("sources.json: collections 必须是非空数组")
    cfg["batch"] = int(cfg["batch"])
    return cfg


class Paths:
    """仓库内所有路径的单一来源。"""

    def __init__(self, root):
        self.root = Path(root)
        self.col = self.root / "col"
        self.work = self.root / "build" / "work"
        self.cache = self.root / "build" / "cache"
        self.embed_cache_db = self.cache / "embed.sqlite"
        self.releases = self.root / "releases"
        self.scan_file = self.work / "scan.json"
        self.chunks_file = self.work / "chunks.jsonl"
        self.work_db = self.work / "kb.db"
        self.vec_npy = self.work / "kb.db.vec.npy"
        self.vec_ids = self.work / "kb.db.vec.ids.json"

    def col_dir(self, collection):
        return self.col / _COL_DIR_OVERRIDES.get(collection, collection)

    def release_dir(self, release_id):
        return self.releases / release_id
