#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 类：语料只读连接 + 会话。检索方法按职责拆到同包子模块。"""
import asyncio
import json
import os
from datetime import datetime

import numpy as np

from .. import store
from ..config import DB, VEC, IDS, CFG, STATE
from .authority import _authority
from .fuse import _dedupe_bodies, hybrid, path_search
from .lexical import col_stats, counts, doc_body, doc_col, doc_list, expand_query, fts, search, tree
from .rerank import rerank
from .vector import embed, embed_async, vector


class KB:
    def __init__(self):
        # 语料只读：拆库后 kb.db 不再持有用户态表，也不允许写入。
        self.con = store.ro_connect(DB)
        self.mat = None
        self.ids_arr = None
        if os.path.exists(VEC):
            self.mat = np.load(VEC)
            with open(IDS, encoding="utf-8") as f:
                self.ids_arr = np.array(json.load(f))

    # ---- 领域词典：中文需求 → 英文/日文术语（提升召回）----
    SYNONYMS = {
        "边缘光": ["RimLight", "Rim", "rim"],
        "轮廓光": ["RimLight", "Rim"],
        "描边": ["Outline", "outline"],
        "轮廓线": ["Outline"],
        "透明": ["Cutout", "Alpha", "Transparent"],
        "半透明": ["Transparent", "Alpha", "Blend"],
        "高光": ["Specular", "Highlight", "HighColor"],
        "反光": ["Specular", "Reflection"],
        "发光": ["Emission", "Emissive"],
        "自发光": ["Emission", "Emissive"],
        "阴影": ["Shadow", "Shade"],
        "影子": ["Shadow"],
        "法线": ["Normal", "NormalMap"],
        "贴图": ["Texture", "Map"],
        "材质": ["Material"],
        "卡通": ["Toon"],
        "渐变": ["Ramp", "Gradient"],
        "后处理": ["PostProcess", "Post"],
        "泛光": ["Bloom"],
        "环境光": ["Ambient", "IBL"],
        "皮肤": ["Skin"],
        "头发": ["Hair", "Aniso"],
        "各向异性": ["Anisotropic", "KajiyaKay"],
        "眼睛": ["Eye", "Iris"],
        "眉毛": ["Eyebrow", "Brow"],
        "衣服": ["Cloth", "Clothing"],
        "闪避": ["Dither"],
        "抖动": ["Dither"],
    }

    # 重排权重（运行时可变，便于扫参）
    # (rrf, cov, head, path, stem, 是否用固定RRF基准)
    # 扫参结论（2026-11-01，45 条评估集）：rrf 0.30 + cov 0.32 + 固定基准 最优
    RERANK_W = (0.30, 0.3224, 0.1488, 0.1488, 0.08, True)
    RRF_BASE = 0.033

    # 来源权威度：原文/官方文档 > 源码 > 整理 > ASR/OCR/论坛
    AUTHORITY = {
        "kks": 1.00,        # 自建手册（人工整理，最高）
        "uts-docs": 1.00,   # Unity Toon Shader 官方手册
        "poiyomi-docs": 1.00,
        "liltoon-docs": 1.00,
        "pdf-docs": 1.00,   # 作者原始指南
        "pidx": 0.95,       # 参数索引（从源码抽取，可信）
        "github-docs": 0.90,
        "shader": 0.88,     # 源码本身
        "src": 0.88,
        "ref": 0.80,
        "doc2": 0.78,
        "mods": 0.75,
        "ai": 0.72,
        "bilibili": 0.62,   # 只有目录
        "zodgame": 0.60,    # 论坛，质量参差
        "video-subs": 0.55, # ASR 转录，噪声大
        "pixiv-docs": 0.62, # 含 OCR
        "discord-qa": 0.58,  # Discord 社区讨论，非正式且多为 2018 时代
        "discord-docs": 0.50,  # Discord 里挖到的官方 wiki 教程
        "tomshaders": 1.00,   # TomShaders X 官方手册（作者原文）
        "img-ocr": 0.52,      # 教程配图 OCR（绘画技法，非 shader）
    }

    # --- 会话 ---
    def require_owner(self, cid, *, actor):
        if actor is None or not STATE.fetchone(
                "SELECT 1 FROM conv WHERE id=? AND owner_user_id=?", (cid, actor)):
            raise PermissionError("Conversation not found")

    def new_conv(self, title, *, actor):
        now = datetime.now().isoformat(timespec="seconds")
        with STATE._lock, STATE.con:
            if actor is None or not STATE.fetchone("SELECT 1 FROM users WHERE id=?", (actor,)):
                raise PermissionError("Actor required")
            cur = STATE.execute("INSERT INTO conv(title,created,updated,owner_user_id) VALUES(?,?,?,?)",
                                (title[:60], now, now, actor))
            return cur.lastrowid

    def add_msg(self, cid, role, content, meta=None, *, actor):
        now = datetime.now().isoformat(timespec="seconds")
        with STATE._lock, STATE.con:
            STATE.execute("BEGIN IMMEDIATE")
            self.require_owner(cid, actor=actor)
            cur = STATE.execute(
                "INSERT INTO msg(conv_id,role,content,meta,created) VALUES(?,?,?,?,?)",
                (cid, role, content, json.dumps(meta or {}, ensure_ascii=False), now))
            STATE.execute("UPDATE conv SET updated=? WHERE id=? AND owner_user_id=?", (now, cid, actor))
            return cur.lastrowid

    def convs(self, limit=40, *, actor=None):
        return STATE.fetchall(
            "SELECT id,title,updated FROM conv WHERE owner_user_id=? ORDER BY updated DESC LIMIT ?", (actor, limit))

    def msgs(self, cid, *, actor):
        with STATE._lock:
            self.require_owner(cid, actor=actor)
            rows = STATE.fetchall(
                "SELECT m.id,m.role,m.content,m.meta,m.created FROM msg m JOIN conv c ON c.id=m.conv_id "
                "WHERE c.id=? AND c.owner_user_id=? ORDER BY m.id", (cid, actor))
        return [{"id": r[0], "role": r[1], "content": r[2],
                 "meta": json.loads(r[3] or "{}"), "created": r[4]} for r in rows]

    def rename_conv(self, cid, title, *, actor):
        with STATE._lock, STATE.con:
            STATE.execute("BEGIN IMMEDIATE")
            self.require_owner(cid, actor=actor)
            if title.strip():
                STATE.execute("UPDATE conv SET title=? WHERE id=? AND owner_user_id=?", (title.strip()[:80], cid, actor))

    def del_conv(self, cid, *, actor):
        with STATE._lock, STATE.con:
            STATE.execute("BEGIN IMMEDIATE")
            self.require_owner(cid, actor=actor)
            votes = STATE.fetchall(
                "SELECT f.path,SUM(f.vote>0),SUM(f.vote<0) FROM feedback f "
                "JOIN msg m ON m.id=f.msg_id WHERE m.conv_id=? AND COALESCE(f.path,'')<>'' GROUP BY f.path", (cid,))
            now = datetime.now().isoformat(timespec="seconds")
            for path, up, down in votes:
                # Legacy aggregates may have been reset without clearing the old vote rows.
                STATE.execute("UPDATE doc_votes SET up=MAX(0,up-?),down=MAX(0,down-?),updated=? WHERE path=?", (up, down, now, path))
            STATE.execute("DELETE FROM feedback WHERE msg_id IN (SELECT id FROM msg WHERE conv_id=?)", (cid,))
            STATE.execute("DELETE FROM msg WHERE conv_id=?", (cid,))
            STATE.execute("DELETE FROM conv WHERE id=? AND owner_user_id=?", (cid, actor))

    def stats(self):
        docs = self.con.execute("SELECT COUNT(*) FROM doc").fetchone()[0]
        chunks = self.con.execute("SELECT COUNT(*) FROM meta").fetchone()[0]
        convs = STATE.fetchone("SELECT COUNT(*) FROM conv")[0]
        msgs = STATE.fetchone("SELECT COUNT(*) FROM msg")[0]
        size = os.path.getsize(DB) / 1024 / 1024 if os.path.exists(DB) else 0
        vsz = os.path.getsize(VEC) / 1024 / 1024 if os.path.exists(VEC) else 0
        return {"docs": docs, "chunks": chunks, "convs": convs, "msgs": msgs,
                "db_mb": round(size, 2), "vec_mb": round(vsz, 1),
                "dim": 0 if self.mat is None else self.mat.shape[1]}

    def conv_title(self, cid, *, actor):
        r = STATE.fetchone("SELECT title FROM conv WHERE id=? AND owner_user_id=?", (cid, actor))
        return r[0] if r else None

    async def hybrid_async(self, q, k=6, col=""):
        """异步包装：把同步检索（SQLite/numpy/jieba）整体丢进线程池。

        排序逻辑仍是 self.hybrid，包装层不碰结果，保证逐字节一致。
        """
        return await asyncio.to_thread(self.hybrid, q, k, col)


KB.fts = fts
KB.search = search
KB.tree = tree
KB.doc_list = doc_list
KB.doc_body = doc_body
KB.doc_col = doc_col
KB.counts = counts
KB.col_stats = col_stats
KB.expand_query = expand_query
KB.embed = embed
KB.embed_async = embed_async
KB.vector = vector
KB.hybrid = hybrid
KB.path_search = path_search
KB._dedupe_bodies = _dedupe_bodies
KB.rerank = rerank
KB._authority = _authority


KBX = KB()
