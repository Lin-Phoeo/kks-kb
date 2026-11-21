#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 融合检索：RRF 融合 + 路径检索 + 去重。"""
import os
import re

import jieba

from ..config import DB


def sec_anchor(title):
    """小节标题 → 锚点 id（与 extract_toc 一致）"""
    import hashlib as _h
    t = re.sub(r"[*`_]", "", title or "").strip()
    return "h-" + _h.md5(t.encode("utf-8")).hexdigest()[:8] if t else ""


def _dedupe_bodies(self, cands, k):
    """同一段正文只留一条；同一文件最多占 2 个槽位"""
    seen, per_path, out = {}, {}, []
    for c in cands:
        b = (c.get("body") or "")
        key = b[:400] + str(len(b))
        if key in seen:
            # 记到第一条上，提示里能说明"另有 N 个变体共用"
            seen[key]["siblings"] = seen[key].get("siblings", 0) + 1
            continue
        p = c.get("path") or ""
        if per_path.get(p, 0) >= 2:
            continue
        seen[key] = c
        per_path[p] = per_path.get(p, 0) + 1
        out.append(c)
        if len(out) >= k:
            break
    return out


def _path_index_sig(self):
    try:
        st = os.stat(DB)
        return (st.st_mtime_ns, st.st_size)
    except OSError:
        return None


def _build_path_index(self):
    """路径 → 首个命中片段：只扫一次 meta，保留扫描序里的第一条（同当前 first-wins）。"""
    rows = self.con.execute(
        "SELECT id,col,path,heading,section,body FROM meta").fetchall()
    index = {}
    for r in rows:
        index.setdefault(r[2], r)
    self._path_index = index
    self._path_index_sig = _path_index_sig(self)


def path_search(self, q, k=30, col=""):
    """按文件路径命中检索：结构性词（注册表/速查表/总览）只在路径里出现时的补救"""
    terms = [t.lower() for t in jieba.cut_for_search(q)
             if len(t.strip()) > 1]
    terms += [w.lower() for w in re.findall(r"[A-Za-z_][A-Za-z0-9_]{2,}", q)]
    terms = [t for t in dict.fromkeys(terms)
             if t not in ("哪些", "什么", "怎么", "如何", "多少", "有哪")]
    if not terms:
        return []
    if (getattr(self, "_path_index", None) is None
            or self._path_index_sig != _path_index_sig(self)):
        _build_path_index(self)
    scored = {}
    for key, r in self._path_index.items():
        if col and r[1] != col:
            continue
        p = (r[2] or "").lower()
        hit = sum(1 for t in terms if t in p)
        if hit < 2:                     # 至少命中 2 个词才算路径相关
            continue
        if key not in scored or hit > scored[key][0]:
            scored[key] = (hit, r)
    best = sorted(scored.values(), key=lambda x: -x[0])[:k]
    return [{"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
             "section": r[4], "body": r[5]} for _, r in best]


def hybrid(self, q, k=6, col=""):
    qx = self.expand_query(q)
    a, b = self.fts(qx, 24, col), self.vector(q, 24, col)
    c_path = self.path_search(qx, 20, col)
    rrf, pool = {}, {}
    for lst, w in ((a, 1.0), (b, 0.8), (c_path, 0.9)):
        for rank, it in enumerate(lst):
            rrf[it["id"]] = rrf.get(it["id"], 0) + w / (60 + rank + 1)
            pool.setdefault(it["id"], it)
    order = sorted(rrf.items(), key=lambda x: -x[1])
    # 跨库检索：单库最多占一半，避免 4000+ 片段的库压死小库
    if not col and len({pool[c].get("col", "") for c, _ in order}) > 1:
        cap = max(2, (k * 3) // 2)
        picked, budget, rest = [], {}, []
        for cid, sc in order:
            cc = pool[cid].get("col", "")
            if budget.get(cc, 0) < cap:
                picked.append((cid, sc))
                budget[cc] = budget.get(cc, 0) + 1
            else:
                rest.append((cid, sc))
        for item in rest:
            if len(picked) >= k:
                break
            picked.append(item)
        order = picked[:max(k * 3, 12)]
    else:
        order = order[:max(k * 3, 12)]

    res = []
    for cid, sc in order:
        it = dict(pool[cid])
        it["rrf"] = round(sc, 4)
        it["anchor"] = sec_anchor(it.get("section") or "")
        it["preview"] = it["body"][:900]
        res.append(it)
    # 候选池留三倍 → 精排 → 去重 → 截断
    res = self.rerank(q, res[:max(k * 3, 12)], max(k * 4, 16))
    res = self._dedupe_bodies(res, k)
    return res, {"fts": len(a), "vec": len(b), "path": len(c_path)}
