#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 轻量重排：RRF + 词覆盖率 + 标题/路径命中 + 长度健全性。"""
import re

import jieba

from ..config import CFG, STATE

# 鸣潮素材路径里出现这些 token 时视为鸣潮文档；查询不提到任何一个即视为无关查询。
_WUWA_TOKENS = ("wuwa", "wuthering", "鸣潮")

# 学习信号缓存：签名（行数、总票数、最近更新时间）变化时重载。
_DV = {"sig": None, "data": None}


def _doc_votes():
    sig = STATE.fetchone(
        "SELECT COUNT(*), COALESCE(SUM(up)+SUM(down), 0), "
        "COALESCE(MAX(updated), '') FROM doc_votes")
    if _DV["data"] is not None and _DV["sig"] == sig:
        return _DV["data"]
    rows = STATE.fetchall("SELECT path, up, down FROM doc_votes")
    _DV["data"] = {p: (u, d) for p, u, d in rows}
    _DV["sig"] = sig
    return _DV["data"]


def rerank(self, q, cands, k):
    """轻量重排：RRF + 词覆盖率 + 标题/路径命中 + 长度健全性"""
    terms = [t.lower() for t in jieba.cut_for_search(q) if len(t.strip()) > 1]
    terms += [w.lower() for w in re.findall(r"[A-Za-z_][A-Za-z0-9_]{2,}", q)]
    terms = list(dict.fromkeys(terms))[:14]
    if not terms or not cands:
        return cands[:k]

    max_rrf = max((c.get("rrf") or 0) for c in cands) or 1.0
    ret = CFG.get("retrieval", {}) or {}
    w_rrf, w_cov, w_head, w_path, w_stem, use_fixed = ret.get("rerank_w", self.RERANK_W)
    base = ret.get("rrf_base", self.RRF_BASE) if use_fixed else max_rrf
    lw = float(ret.get("learned_weight", 0.0) or 0.0)
    learned = _doc_votes() if lw > 0 else None
    # 鸣潮素材对 KKS/Shader 问题是噪音：路径带 wuwa 而查询不涉及时降权，1.0 关闭。
    wuwa_damp = float(ret.get("wuwa_damp", 0.5))
    ql = q.lower()
    query_is_wuwa = any(tok in ql for tok in _WUWA_TOKENS)
    # title_exact：查询完全包含文档 heading 时额外加分，正交信号；默认 0.0 关闭 → 快照逐位一致。
    # c-142「《KKLT 版本历史》」与 c-063「_NormalMapScale 的默认值是多少」的查询都完全含目标 heading，
    # 这个信号可以把它们从 top3 外顶上来。详见 EXPERIMENTS.md「E3 Fix 2」。
    title_exact_w = float(ret.get("title_exact_weight", 0.0) or 0.0)
    scored = []
    for c in cands:
        body = (c.get("body") or "")[:3000].lower()
        head = ((c.get("heading") or "") + " " + (c.get("section") or "")).lower()
        path = (c.get("path") or "").lower()

        hit_body = sum(1 for t in terms if t in body)
        hit_head = sum(1 for t in terms if t in head)
        hit_path = sum(1 for t in terms if t in path)
        cov = hit_body / len(terms)

        head_s = min(1.0, hit_head / max(1, len(terms) * 0.4))
        path_s = min(1.0, hit_path / max(1, len(terms) * 0.4))
        # 文件名整体命中（本库文件名信息量大：07-Poiyomi-Main、卷九补充2-BlendType）
        stem = (c.get("path") or "").rsplit("/", 1)[-1].lower()
        stem_hits = sum(1 for t in terms if t in stem)
        stem_s = min(1.0, stem_hits / max(1, len(terms) * 0.5))
        # title_exact：查询完全包含文档 heading（归一化为 lower + 两端 strip）时置 1，否则 0。
        # 这是正交信号——与 head_s（词命中率）不同，这里要求查询包含整段 heading。
        doc_head = (c.get("heading") or "").strip().lower()
        title_exact = 1.0 if (doc_head and doc_head in ql) else 0.0
        s = (w_rrf * min(1.0, (c.get("rrf") or 0) / base)
             + w_cov * cov
             + w_head * head_s
             + w_path * path_s
             + w_stem * stem_s
             + title_exact_w * title_exact)
        s *= (0.5 + 0.5 * self._authority(c))
        # 太短的碎片降权
        blen = len(c.get("body") or "")
        if blen < 120:
            s *= 0.75
        # 鸣潮路径但查询无关 → 乘 wuwa_damp（默认 0.5；1.0 关闭）
        if wuwa_damp != 1.0 and not query_is_wuwa:
            if any(tok in path for tok in _WUWA_TOKENS):
                s *= wuwa_damp
        # 学习信号：按文档路径对最终分数做小倍率调整（默认关闭，逐字节不改变旧排序）
        if learned:
            uv = learned.get(c.get("path"))
            if uv:
                net = max(-20, min(20, uv[0] - uv[1]))
                if net:
                    s *= (1.0 + lw * net / 20.0)
        c2 = dict(c)
        c2["_score"] = round(s, 4)
        scored.append(c2)

    scored.sort(key=lambda x: -x["_score"])
    return scored[:k]
