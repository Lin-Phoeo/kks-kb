#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 来源权威度：按 path 第一段 / col 打分。"""


def _authority(self, c):
    """权威度：先看 path 第一段（web 库下的 video-subs / uts-docs 等），
    再看 col（kks、shader、src 这类库本身就是来源）"""
    p = (c.get("path") or "").replace("\\", "/")
    if "/" in p:
        head = p.split("/", 1)[0]
        if head in self.AUTHORITY:
            return self.AUTHORITY[head]
    col = c.get("col") or ""
    if col in self.AUTHORITY:
        return self.AUTHORITY[col]
    return 0.75
