#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""scan：按 indexer.build 完全一致的过滤规则枚举每个 collection 的 md 文件。"""
import glob
import json
import os

from ._compat import EXCLUDE_NAMES


def scan_files(paths, collections):
    """返回文件清单，顺序 = collections 顺序 → 库内文件名排序（与 indexer 一致）。

    过滤规则与 indexer.build 完全相同：跳过 EXCLUDE_NAMES 与 `_append_` 前缀。
    路径统一为 `/`。
    """
    out = []
    for col in collections:
        docs_dir = str(paths.col_dir(col))
        if not os.path.isdir(docs_dir):
            continue
        files = []
        for p in glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True):
            n = os.path.basename(p)
            if n in EXCLUDE_NAMES or n.startswith("_append_"):
                continue
            files.append(p)
        files.sort()
        for p in files:
            rel = os.path.relpath(p, docs_dir).replace("\\", "/")
            out.append({"collection": col, "path": rel, "abs": p})
    return out


def run_scan(paths, collections):
    paths.work.mkdir(parents=True, exist_ok=True)
    files = scan_files(paths, collections)
    with open(paths.scan_file, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"collections": collections, "files": files}, f,
                  ensure_ascii=False, indent=2)
    return files
