#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""联网检索 + LLM 链路 + 追问切分。"""
import json
import logging
import re
import time
from datetime import datetime

import httpx
import jieba

from ..config import CFG, STATE

logger = logging.getLogger("kb.generation.llm")

CACHE_TTL = 300          # 同 query 5 分钟内复用
QUOTA_PER_DAY = 300      # 每用户每日联网检索上限
# 引擎质量先验（实测：bing/360 相关性优于 sogou 聚合页）
ENGINE_PRIOR = {"bing": 1.0, "360search": 0.95, "sogou": 0.70, "baidu": 0.85}
JUNK = ("抖音", "快手", "好看视频", "西瓜视频", "备胎说车", "万商云集", "漫画", "本子")
# 技术内容高发站：命中加分
GOOD_HOSTS = (
    "bilibili.com", "zhihu.com", "zhuanlan.zhihu.com", "tieba.baidu.com",
    "github.com", "gitee.com", "docs.unity3d.com", "discussions.unity.com",
    "csdn.net", "juejin.cn", "cnblogs.com", "segmentfault.com",
    "iwara.tv", "booth.pm", "kknow.cc", "zodgame.xyz", "south-plus.net",
    "v2ex.com", "linux.do", "reddit.com",
)


def quota_check(email):
    """返回 (是否允许, 今日已用)"""
    day = datetime.now().strftime("%Y-%m-%d")
    row = STATE.fetchone("SELECT n FROM quota WHERE email=? AND day=?", (email, day))
    used = row[0] if row else 0
    return used < QUOTA_PER_DAY, used


def quota_inc(email):
    day = datetime.now().strftime("%Y-%m-%d")
    STATE.execute(
        "INSERT INTO quota(email,day,n) VALUES(?,?,1) "
        "ON CONFLICT(email,day) DO UPDATE SET n=n+1", (email, day))
    STATE.commit()


def _rank_zh(items, q):
    """RRF 融合 + 相关性闸门。
    1) 先按"是否含特征词"筛掉噪音（bing 对中文短查询常返回不相关结果）
    2) 再用 RRF 融合，尊重各引擎自己的排名
    3) 特征词不足 3 条命中时逐级放宽，避免把结果筛空
    """
    from collections import defaultdict
    terms = [t for t in jieba.cut_for_search(q) if len(t) >= 2]
    # 特征词：拉丁词（KKUTS/TessBias）或 3 字以上中文
    strong = [t for t in terms if all(ord(c) < 128 for c in t) or len(t) >= 3]
    weak = terms

    def hit(it, pool):
        hay = (it.get("title", "") + " " + it.get("snippet", "") + " " + it.get("url", "")).lower()
        return sum(1 for t in pool if t.lower() in hay)

    pool = strong if strong else weak
    keep = [it for it in items if hit(it, pool) > 0]
    if len(keep) < 3:
        keep = [it for it in items if hit(it, weak) > 0]
    if len(keep) < 3:
        keep = items

    buckets = defaultdict(list)
    for it in keep:
        buckets[it.get("engine", "?")].append(it)

    score, best = {}, {}
    for eng, lst in buckets.items():
        prior = ENGINE_PRIOR.get(eng, 0.6)
        for rank, it in enumerate(lst):
            url = it.get("url", "")
            if not url:
                continue
            # 命中数也参与打分（同分时更相关的靠前）
            bonus = 1.0 + 0.25 * min(hit(it, pool), 3)
            host = it.get("url", "").lower()
            if any(h in host for h in GOOD_HOSTS):
                bonus += 0.5
            score[url] = score.get(url, 0) + prior * bonus / (60 + rank + 1)
            if url not in best:
                best[url] = it

    for url, it in best.items():
        if any(b in it.get("title", "") for b in JUNK):
            score[url] *= 0.25

    return [best[u] for u, _ in sorted(score.items(), key=lambda x: -x[1])]


def websearch(q, limit=6, email="", use_cache=True):
    """带缓存 + 配额 + 中文加权的联网检索"""
    qk = q.strip().lower()
    now = int(time.time())

    if use_cache:
        row = STATE.fetchone("SELECT payload,ts FROM seo_cache WHERE qkey=?", (qk,))
        if row and now - row[1] < CACHE_TTL:
            try:
                d = json.loads(row[0])
                return d["items"], {"ms": 0, "fail": d.get("fail", []),
                                    "cached": True, "age": now - row[1]}
            except Exception:
                pass

    if email:
        ok, used = quota_check(email)
        if not ok:
            return [], {"ms": 0, "fail": [], "quota": f"今日联网额度已用完（{used}/{QUOTA_PER_DAY}）"}

    t0 = time.time()
    try:
        r = httpx.get(CFG["searxng"], params={"q": q, "format": "json", "language": "zh-CN"}, timeout=20)
        d = r.json()
        raw = [{"title": i.get("title", ""), "url": i.get("url", ""),
                "snippet": (i.get("content") or "")[:280], "engine": i.get("engine", "")}
               for i in d.get("results", [])]
        raw = _rank_zh(raw, q)
        items = raw[:limit]
        fail = [e[0] for e in (d.get("unresponsive_engines") or [])]
        if email:
            quota_inc(email)
        try:
            STATE.execute(
                "INSERT INTO seo_cache(qkey,payload,ts) VALUES(?,?,?) "
                "ON CONFLICT(qkey) DO UPDATE SET payload=?, ts=?",
                (qk, json.dumps({"items": items, "fail": fail}, ensure_ascii=False), now,
                 json.dumps({"items": items, "fail": fail}, ensure_ascii=False), now))
            STATE.commit()
        except Exception:
            pass
        return items, {"ms": int((time.time() - t0) * 1000), "fail": fail}
    except Exception as e:
        return [], {"ms": 0, "fail": [type(e).__name__]}


FOLLOW_SEP = "%%%"


def split_followups(text):
    if FOLLOW_SEP not in text:
        return text.strip(), []
    ans, tail = text.split(FOLLOW_SEP, 1)
    qs = [re.sub(r"^[\-\*\d\.\、\)\s]+", "", ln).strip() for ln in tail.split("\n") if ln.strip()]
    return ans.strip(), [q for q in qs if 4 <= len(q) <= 60][:3]


def llm_chain():
    prim = {"url": CFG["llm_url"], "model": CFG["llm_model"], "key": CFG["llm_key"]}
    return [prim] + CFG.get("llm_fallbacks", [])
