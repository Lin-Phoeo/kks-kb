#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""复用 app/indexer：直接按目录导入 indexer，避免触碰 app/app.py。

app/ 没有 __init__.py，而 app/app.py 又会在导入时读 config.json。
这里把 app/ 目录放进 sys.path 后 import indexer（与 tests/test_indexer_guard.py 同法），
只加载 indexer 模块本身，不执行 app.py。

indexer 会往 stdout 打印进度，流水线自己汇报，这里统一静默。
"""
import contextlib
import io
import os
import sys

_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
_APP_DIR = os.path.join(_ROOT, "app")
if _APP_DIR not in sys.path:
    sys.path.insert(0, _APP_DIR)

import indexer as _indexer_mod  # noqa: E402

EXCLUDE_NAMES = _indexer_mod.EXCLUDE_NAMES
chunk_doc = _indexer_mod.chunk_doc


def indexer_build(db_path, docs_dir, collection, append=False):
    """index 阶段唯一入口：只建 FTS/meta/doc，绝不嵌向量。"""
    with contextlib.redirect_stdout(io.StringIO()):
        _indexer_mod.build(db_path, docs_dir, do_embed=False,
                           collection=collection, append=append)
