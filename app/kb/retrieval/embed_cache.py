#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""查询向量缓存：进程内 LRU + state.db 持久表。

key = sha256(model + "\\0" + instruction_version + "\\0" + text).hexdigest()
向量按 float32 字节存 BLOB；命中即返回归一化后的向量，不再调用嵌入 API。

本模块不 import ..config：持久层只认一个具备 execute/fetchone/commit
的 store 对象（即 StateStore），便于在无 config.json 的环境里单测。
"""
import hashlib
import threading
from collections import OrderedDict

import numpy as np


def cache_key(model, instruction_version, text):
    raw = (model or "") + "\0" + (instruction_version or "") + "\0" + (text or "")
    return hashlib.sha256(raw.encode("utf-8")).hexdigest()


class LRU:
    """最简单正确的线程安全 LRU：OrderedDict，最近使用放末尾。"""

    def __init__(self, maxsize=512):
        self.maxsize = max(1, int(maxsize))
        self._d = OrderedDict()
        self._lock = threading.Lock()

    def get(self, key):
        with self._lock:
            if key not in self._d:
                return None
            self._d.move_to_end(key)
            return self._d[key]

    def put(self, key, value):
        with self._lock:
            self._d[key] = value
            self._d.move_to_end(key)
            if len(self._d) > self.maxsize:
                self._d.popitem(last=False)

    def clear(self):
        with self._lock:
            self._d.clear()

    def __len__(self):
        with self._lock:
            return len(self._d)


def vec_to_blob(vec):
    a = np.asarray(vec, dtype=np.float32).reshape(-1)
    return int(a.shape[0]), a.tobytes()


def blob_to_vec(dim, blob):
    return np.frombuffer(blob, dtype=np.float32).reshape(int(dim)).copy()


class QueryEmbedCache:
    """LRU 优先，未命中再查持久表并回填 LRU。store 为 None 时仅用 LRU。"""

    def __init__(self, maxsize=512, store=None):
        self.lru = LRU(maxsize)
        self.store = store

    def get(self, key):
        v = self.lru.get(key)
        if v is not None:
            return v
        if self.store is None:
            return None
        row = self.store.fetchone(
            "SELECT vec, dim FROM embed_cache WHERE key=?", (key,))
        if row is None:
            return None
        try:
            v = blob_to_vec(row[1], row[0])
        except Exception:
            return None
        self.lru.put(key, v)
        return v

    def put(self, key, vec, created=""):
        self.lru.put(key, np.asarray(vec, dtype=np.float32).reshape(-1))
        if self.store is None:
            return
        dim, blob = vec_to_blob(vec)
        self.store.execute(
            "INSERT OR REPLACE INTO embed_cache(key,vec,dim,created) VALUES(?,?,?,?)",
            (key, blob, dim, created))
        self.store.commit()
