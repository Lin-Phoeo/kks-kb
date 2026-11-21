#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""embed：sha256(model+"\\0"+body) 去重缓存；每批成功立即提交；崩溃可续跑。

缓存落在 build/cache/embed.sqlite。真实嵌入器里才延迟导入 httpx。
"""
import hashlib
import json
import sqlite3
import time

import numpy as np


def cache_key(model, body):
    return hashlib.sha256((model + "\0" + body).encode("utf-8")).hexdigest()


class EmbedCache:
    def __init__(self, db_path):
        db_path.parent.mkdir(parents=True, exist_ok=True)
        self.con = sqlite3.connect(str(db_path))
        self.con.execute("CREATE TABLE IF NOT EXISTS vecs("
                         "key TEXT PRIMARY KEY, model TEXT, dim INTEGER, vec BLOB)")
        self.con.commit()

    def get(self, key):
        row = self.con.execute("SELECT dim, vec FROM vecs WHERE key=?", (key,)).fetchone()
        if row is None:
            return None
        dim, blob = row
        return np.frombuffer(blob, dtype=np.float32).reshape(dim).copy()

    def put(self, key, model, vec):
        a = np.asarray(vec, dtype=np.float32).reshape(-1)
        self.con.execute("INSERT OR REPLACE INTO vecs(key,model,dim,vec) VALUES(?,?,?,?)",
                         (key, model, int(a.shape[0]), a.tobytes()))

    def commit(self):
        self.con.commit()

    def close(self):
        self.con.close()


def iter_chunks(chunks_file):
    with open(chunks_file, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                yield json.loads(line)


def distinct_keys(chunks_file, model):
    """chunk 顺序里第一次出现为序的去重 (key, body)。"""
    order, seen = [], {}
    for rec in iter_chunks(chunks_file):
        k = cache_key(model, rec["body"])
        if k not in seen:
            seen[k] = rec["body"]
            order.append(k)
    return order, seen


def run_embed(paths, embedder, model, batch):
    """只嵌缓存里缺失的 key；每批成功即 commit；异常只丢当前批。"""
    order, key_body = distinct_keys(paths.chunks_file, model)
    cache = EmbedCache(paths.embed_cache_db)
    try:
        missing = [k for k in order if cache.get(k) is None]
        embedded = 0
        for i in range(0, len(missing), batch):
            grp = missing[i:i + batch]
            vecs = embedder([key_body[k] for k in grp])
            if len(vecs) != len(grp):
                raise RuntimeError(f"嵌入器返回 {len(vecs)} 个向量，期望 {len(grp)}")
            for k, v in zip(grp, vecs):
                cache.put(k, model, v)
            cache.commit()  # 每批成功立即提交：崩溃只丢当前批
            embedded += len(grp)
        return {"keys": len(order), "missing": len(missing), "embedded": embedded}
    finally:
        cache.close()


def count_missing(paths, model):
    """--dry-run 用：缓存命中后还缺多少 key（= 需要多少次嵌入调用？见 run_embed）。"""
    order, _ = distinct_keys(paths.chunks_file, model)
    cache = EmbedCache(paths.embed_cache_db)
    try:
        return sum(1 for k in order if cache.get(k) is None)
    finally:
        cache.close()


def make_http_embedder(embed_url, model, api_key, max_retries=3):
    """真实嵌入器：httpx 延迟导入；429/5xx 指数退避重试，其余 4xx 立即失败。"""
    def embed(bodies):
        import httpx  # 延迟导入：包核心不依赖 httpx

        headers = {"Authorization": "Bearer " + api_key, "Content-Type": "application/json"}
        payload = {"model": model, "input": [b[:8000] for b in bodies],
                   "encoding_format": "float"}
        for attempt in range(max_retries + 1):
            try:
                r = httpx.post(embed_url, headers=headers, json=payload, timeout=120)
            except httpx.HTTPError:
                if attempt == max_retries:
                    raise
                time.sleep(2 ** attempt)
                continue
            if r.status_code == 429 or r.status_code >= 500:
                if attempt == max_retries:
                    r.raise_for_status()
                time.sleep(2 ** attempt)
                continue
            r.raise_for_status()  # 其余 4xx 立即失败
            return [item["embedding"] for item in r.json()["data"]]
        raise RuntimeError("嵌入请求重试耗尽")  # pragma: no cover

    return embed
