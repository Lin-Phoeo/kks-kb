#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""模型输出参数校验：引用参数名必须在参数索引里真实存在。"""
from ..content.params import (PARAM_IDX, PARAM_TOKEN, ALLCAP,
                              lookup_param, _param_kind)


def validate_params(text):
    """校验模型引用的参数名。返回 (确认存在, 近似, 查不到)"""
    if not PARAM_IDX:
        return [], [], []
    seen, ok, near, bad = set(), [], [], []
    for m in PARAM_TOKEN.finditer(text or ""):
        n = m.group(1)
        if n in seen or ALLCAP.match(n):
            continue
        seen.add(n)
        k = _param_kind(n)
        if k == "exact":
            ok.append(n)
        elif k.startswith("near:"):
            near.append((n, k[5:]))
        else:
            bad.append(n)
    return ok, near, bad
