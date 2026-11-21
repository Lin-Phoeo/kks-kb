#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""参数索引：加载 params.json 并做精确/近似查找。"""
import json
import logging
import os
import re
import time

from ..config import DATA

logger = logging.getLogger("kb.content.params")

PARAM_IDX = {}
PARAM_IDX_AT = [""]


def _load_param_index():
    """加载参数总索引（params.json）"""
    global PARAM_IDX
    p = os.path.join(DATA, "col", "paramidx", "params.json")
    try:
        with open(p, encoding="utf-8") as f:
            PARAM_IDX = json.load(f)
        PARAM_IDX_AT[0] = time.strftime("%Y-%m-%d %H:%M")
        logger.info("[param] 载入 %d 个参数", len(PARAM_IDX))
    except Exception as e:
        PARAM_IDX = {}
        logger.warning("[param] 索引未载入: %s", e)


_load_param_index()


def lookup_param(name):
    """查参数：精确名 → 去下划线 → 大小写不敏感"""
    if not PARAM_IDX or not name:
        return None
    if name in PARAM_IDX:
        return PARAM_IDX[name]
    n = name.lstrip("_").lower()
    for k, v in PARAM_IDX.items():
        if k.lstrip("_").lower() == n:
            return v
    return None


# 只认反引号包起来的（提示词要求参数名用反引号，代码块里的标识符不带）
PARAM_TOKEN = re.compile(r"`(_[A-Za-z][A-Za-z0-9_]{1,40})`")
# HLSL 语义/宏常见形如 _POS、_WS、_MAIN_LIGHT 之类全大写
ALLCAP = re.compile(r"^_[A-Z0-9_]+$")


def _param_kind(name):
    """返回 exact / near / none"""
    if lookup_param(name):
        return "exact"
    # 近似：去掉尾部 _xxx 再试（如 _ShadingGradeMap_Mobile → _ShadingGradeMap）
    parts = name.split("_")
    for cut in range(len(parts) - 1, 1, -1):
        base = "_".join(parts[:cut])
        if lookup_param(base):
            return "near:" + base
    return "none"
