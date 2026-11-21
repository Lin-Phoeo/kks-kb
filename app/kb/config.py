#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""配置加载：路径、CFG、state.db 句柄、资料库清单。"""
import json
import os

from . import store

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(BASE, "..", "data")
GAME_ROOT = os.environ.get("KB_GAME_ROOT", os.path.join(BASE, "..", "game"))
DB = os.path.join(DATA, "kb.db")
VEC = DB + ".vec.npy"
IDS = DB + ".vec.ids.json"
with open(os.path.join(BASE, "..", "config.json"), encoding="utf-8") as _f:
    CFG = json.load(_f)
STATE = store.StateStore(os.path.join(DATA, "state.db"))

COLLECTIONS = [
    {"id": "all", "name": "全部", "desc": "跨库检索"},
    {"id": "kks", "name": "恋活技术", "desc": "shader / 材质 / 管线"},
    {"id": "ai", "name": "AI 沉淀", "desc": "决定 / 教训 / 步骤"},
    {"id": "ref", "name": "官方文档", "desc": "参考文档 / 教程 / 工程"},
    {"id": "mods", "name": "Mod 与参数", "desc": "Mod 资料 / shader 参数表"},
    {"id": "doc2", "name": "教程与文档", "desc": "PDF / Word / PPT / 网页 / 笔记"},
    {"id": "album", "name": "教程图集", "desc": "教程图片，按资料顺序浏览"},
    {"id": "shader", "name": "Shader 源码", "desc": "shader 实现 / 参数对照"},
    {"id": "src", "name": "C# 源码", "desc": "Mod 工具与脚本"},
    {"id": "web", "name": "网络文档", "desc": "Poiyomi / lilToon / GitHub 官方文档"},
    {"id": "pidx", "name": "参数索引", "desc": "shader 参数的默认值与家族"},
    {"id": "dc", "name": "Discord 存档", "desc": "由部署者管理的社区讨论资料"},
]


def col_label(cid):
    for c in COLLECTIONS:
        if c["id"] == cid:
            return c["name"]
    return cid
