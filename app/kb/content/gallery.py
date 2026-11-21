#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""教程配图：OCR 映射 + 配图 manifest + 关联图片。"""
import json
import os

from ..config import DATA

_IMG_MANIFEST = {"data": [], "mtime": 0}


_OCR_MAP = {"data": None, "mtime": 0}


def _load_ocr_map():
    """图片 ↔ OCR 文档 双向映射，文件变了自动重读"""
    p = os.path.join(DATA, "img", "ocr_map.json")
    if not os.path.isfile(p):
        return {"by_doc": {}, "by_img": {}, "by_slug": {}, "by_cat": {}}
    m = os.path.getmtime(p)
    if _OCR_MAP["mtime"] != m or _OCR_MAP["data"] is None:
        with open(p, encoding="utf-8") as f:
            _OCR_MAP["data"] = json.load(f)
        _OCR_MAP["mtime"] = m
    return _OCR_MAP["data"]


def doc_key(path):
    """把任意 `.../img-ocr/<name>.md` 规整成 ocr_map 的 key（`<name>.md`）

    文档实际路径带库前缀（如 `web/img-ocr/x.md`），旧实现只认 `img-ocr/` 开头，
    导致所有 OCR 教程页拿不到配图。
    """
    p = (path or "").replace("\\", "/")
    if "img-ocr/" not in p:
        return ""
    return p.split("img-ocr/", 1)[1]


def album_of(entry):
    """图片所属教程目录名（manifest.orig 的上一级目录）"""
    parts = (entry.get("orig") or "").replace("\\", "/").split("/")
    return parts[-2] if len(parts) >= 2 else ""


def imgs_for_doc(path, limit=8, section=""):
    """文档 -> 关联图片。

    by_doc 的 key 统一为完整 doc path（带 .md）；只做精确匹配，不做 basename
    兜底（同名文档会串图）。section 命中时那张排第一并标 hit。
    """
    p = (path or "").replace("\\", "/")
    bd = _load_ocr_map()["by_doc"]
    lst = list(bd.get(p) or [])
    name = (section or "").split("（", 1)[0].strip()
    if not name:
        return lst[:limit]
    i = next((k for k, x in enumerate(lst) if x["name"] == name), -1)
    if i < 0:
        return lst[:limit]
    # 命中图 + 前后相邻页，教程截图通常连续
    lo = max(0, i - (limit // 2 - 1))
    out = [dict(lst[i], hit=True)] + [x for x in lst[lo:lo + limit] if x["name"] != name]
    return out[:limit]


def _load_img_manifest():
    """懒加载教程配图 manifest，文件变了自动重读"""
    p = os.path.join(DATA, "img", "manifest.json")
    try:
        m = os.path.getmtime(p)
    except OSError:
        return []
    if _IMG_MANIFEST["mtime"] != m:
        try:
            with open(p, encoding="utf-8") as f:
                _IMG_MANIFEST["data"] = json.load(f)
            _IMG_MANIFEST["mtime"] = m
        except Exception:
            return []
    return _IMG_MANIFEST["data"]
