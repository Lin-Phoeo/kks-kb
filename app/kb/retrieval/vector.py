#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 向量通道：嵌入与向量相似检索。

同步 embed() 保留给索引器/离线使用；异步 embed_async() 走 httpx.AsyncClient，
429/5xx 退避重试 2 次，其余 4xx 立即失败。两条路径共用查询向量缓存
（进程内 LRU + state.db 持久表），命中不调嵌入 API，返回的向量逐字节一致。
"""
import asyncio
from datetime import datetime

import httpx
import numpy as np

from ..config import CFG, STATE
from .embed_cache import QueryEmbedCache, cache_key

# 查询向量缓存：LRU 上限 512，持久层写 state.db（StateStore 自带锁）。
CACHE = QueryEmbedCache(maxsize=512, store=STATE)


def _embed_key(text):
    return cache_key(CFG.get("embed_model", ""), CFG.get("instruction", ""), text)


def _embed_via_http(text):
    """同步嵌入（离线/索引器路径），失败返回 None。"""
    try:
        r = httpx.post(CFG["gitee_embed_url"],
                       headers={"Authorization": f"Bearer {CFG['gitee_key']}"},
                       json={"model": CFG["embed_model"], "input": [text[:8000]],
                             "encoding_format": "float"}, timeout=30)
        v = np.array(r.json()["data"][0]["embedding"], dtype=np.float32)
        return v / (np.linalg.norm(v) + 1e-9)
    except Exception:
        return None


def embed(self, text):
    if self.mat is None or not CFG.get("gitee_key"):
        return None
    key = _embed_key(text)
    hit = CACHE.get(key)
    if hit is not None:
        return hit
    v = _embed_via_http(text)
    if v is None:
        return None
    CACHE.put(key, v, datetime.now().isoformat(timespec="seconds"))
    return v


async def _embed_via_http_async(text):
    """异步嵌入：超时 30s；429/5xx 退避重试 2 次；其余 4xx 立即失败。"""
    headers = {"Authorization": f"Bearer {CFG['gitee_key']}"}
    payload = {"model": CFG["embed_model"], "input": [text[:8000]],
               "encoding_format": "float"}
    async with httpx.AsyncClient(timeout=30) as cli:
        for attempt in range(3):
            try:
                r = await cli.post(CFG["gitee_embed_url"], headers=headers, json=payload)
            except httpx.HTTPError:
                if attempt == 2:
                    raise
                await asyncio.sleep(2 ** attempt)
                continue
            if r.status_code == 429 or r.status_code >= 500:
                if attempt == 2:
                    r.raise_for_status()
                await asyncio.sleep(2 ** attempt)
                continue
            r.raise_for_status()   # 其余 4xx 立即失败
            v = np.array(r.json()["data"][0]["embedding"], dtype=np.float32)
            return v / (np.linalg.norm(v) + 1e-9)
    raise RuntimeError("嵌入请求重试耗尽")   # pragma: no cover


async def embed_async(self, text):
    if self.mat is None or not CFG.get("gitee_key"):
        return None
    key = _embed_key(text)
    hit = CACHE.get(key)
    if hit is not None:
        return hit
    try:
        v = await _embed_via_http_async(text)
    except Exception:
        return None
    CACHE.put(key, v, datetime.now().isoformat(timespec="seconds"))
    return v


_EMPTY_ROWS = np.empty(0, dtype=np.intp)


def vector(self, q, k=20, col=""):
    if self.mat is None:
        return []
    qv = self.embed(q)
    if qv is None:
        return []
    sims = self.mat @ qv
    if CFG.get("retrieval", {}).get("exact_topk", False):
        return _vector_exact(self, sims, k, col)
    # 旧实现：候选 96 条，逐条 SELECT id=? → 一个查询 96 次 round-trip。
    # 换成单条 WHERE id IN (…)，再按 argpartition 的候选顺序回填，
    # 保留原语义：无 col 时按窗口原序取前 k 条，有 col 时按窗口原序过滤后取前 k 条。
    idx = np.argpartition(-sims, min(max(k * 4, k), len(sims) - 1))[:max(k * 4, k)]
    cand_ids = [int(self.ids_arr[i]) for i in idx]
    if not cand_ids:
        return []
    marks = ",".join("?" for _ in cand_ids)
    fetched = self.con.execute(
        "SELECT id,col,path,heading,section,body FROM meta WHERE id IN (%s)" % marks,
        cand_ids).fetchall()
    by_id = {r[0]: r for r in fetched}
    out = []
    for cid in cand_ids:
        r = by_id.get(cid)
        if not r:
            continue
        if col and r[1] != col:
            continue
        out.append({"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
                    "section": r[4], "body": r[5]})
        if len(out) >= k:
            break
    return out


def _vector_exact(self, sims, k, col):
    """精确 top-k：按相似度严格降序；col 过滤在打分前做（掩码）。"""
    if len(sims) == 0:
        return []
    if col:
        rows = _col_rows(self, col)
        if len(rows) == 0:
            return []
        idx = rows[np.argsort(-sims[rows], kind="stable")[:k]]
    else:
        idx = np.argsort(-sims, kind="stable")[:k]
    ids = [int(self.ids_arr[i]) for i in idx]
    if not ids:
        return []
    marks = ",".join("?" for _ in ids)
    fetched = self.con.execute(
        "SELECT id,col,path,heading,section,body FROM meta WHERE id IN (%s)" % marks,
        ids).fetchall()
    by_id = {r[0]: r for r in fetched}
    out = []
    for cid in ids:
        r = by_id.get(cid)
        if not r:
            continue
        out.append({"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
                    "section": r[4], "body": r[5]})
    return out


def _col_rows(self, col):
    if getattr(self, "_col_rows_cache", None) is None:
        self._col_rows_cache = _build_col_rows(self)
    return self._col_rows_cache.get(col, _EMPTY_ROWS)


def _build_col_rows(self):
    """预建 col → 行索引掩码：一次 SELECT 取全库 id/col，之后打分前 O(1) 取掩码。"""
    id_to_col = dict(self.con.execute("SELECT id,col FROM meta").fetchall())
    groups = {}
    for i, mid in enumerate(self.ids_arr):
        groups.setdefault(id_to_col.get(int(mid)), []).append(i)
    return {c: np.array(v, dtype=np.intp) for c, v in groups.items()}
