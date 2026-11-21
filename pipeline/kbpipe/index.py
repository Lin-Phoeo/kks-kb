#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""index：全新空库，按 sources.json 顺序逐库 indexer.build(..., do_embed=False)。"""
import os

from ._compat import indexer_build


def run_index(paths, collections):
    """collections 顺序决定 id 分配，保证确定性。"""
    paths.work.mkdir(parents=True, exist_ok=True)
    db = str(paths.work_db)
    for suffix in ("", "-wal", "-shm"):
        p = db + suffix
        if os.path.exists(p):
            os.remove(p)
    first = True
    for col in collections:
        docs_dir = str(paths.col_dir(col))
        indexer_build(db, docs_dir, collection=col, append=not first)
        first = False
    return db
