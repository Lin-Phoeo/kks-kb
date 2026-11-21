#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""手册层：章节 + 参考层 + 面包屑导航。"""
import json
import os

from ..config import DATA

_MANUAL = {"data": None, "mtime": 0}


def manual_tree():
    return _load_manual().get("chapters") or []


def manual_ref():
    return _load_manual().get("reference") or []


def _load_manual():
    """手册层（章节 + 参考层），文件变了自动重读"""
    p = os.path.join(DATA, "manual.json")
    if not os.path.isfile(p):
        return {"chapters": [], "reference": []}
    m = os.path.getmtime(p)
    if _MANUAL["mtime"] != m or _MANUAL["data"] is None:
        with open(p, encoding="utf-8") as f:
            d = json.load(f)
        # path -> (kind, id, title, index, total)
        idx = {}
        for c in d.get("chapters") or []:
            for i, x in enumerate(c["docs"]):
                idx[x["path"]] = ("chapter", c["id"], c["title"], i, len(c["docs"]))
        for r in d.get("reference") or []:
            for i, x in enumerate(r["docs"]):
                idx.setdefault(x["path"], ("ref", r["id"], r["title"], i, len(r["docs"])))
        d["_idx"] = idx
        _MANUAL["data"] = d
        _MANUAL["mtime"] = m
    return _MANUAL["data"]


def manual_nav(path):
    """某篇文档的手册位置：面包屑 + 上下篇"""
    d = _load_manual()
    hit = (d.get("_idx") or {}).get(path)
    if not hit:
        return None
    kind, cid, title, i, total = hit
    lst = None
    for g in (d.get("chapters") or []) + (d.get("reference") or []):
        if g["id"] == cid:
            lst = g["docs"]
            break
    if lst is None:
        return None
    base = "/manual/" + cid
    return {
        "kind": kind, "id": cid, "title": title, "pos": i + 1, "total": total,
        "href": base,
        "prev": lst[i - 1] if i > 0 else None,
        "next": lst[i + 1] if i + 1 < len(lst) else None,
    }
