#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""检索 top-K 快照：重构/改参前后的"逐位一致"回归护栏。

小样本 hit@3 容易漏掉"没掉分但排序悄悄变了"的改动。
这个工具对黄金集（以及可选的额外查询）记录完整的 top-K chunk id 序列，
重构后重跑比对：纯搬移型改动必须与快照逐位一致（id 序列相同，rrf 分数差 < 1e-6）。

用法（在仓库根目录；语义检索需要已配置的 Embedding 服务）：
  python3 eval/snapshot_topk.py record  --out eval/snap-base.json
  python3 eval/snapshot_topk.py compare --base eval/snap-base.json
可选: --extra queries.txt  每行一个查询，用来补黄金集覆盖不到的真实问题
退出码: 0 一致, 1 有差异
"""
import argparse
import io
import json
import os
import sys

TOL = 1e-6
_EVAL_DIR = os.path.dirname(os.path.abspath(__file__))


def load_queries(golden_path, extra_path):
    qs = []
    if os.path.exists(golden_path):
        g = json.load(io.open(golden_path, encoding="utf-8"))
        for it in g["items"]:
            qs.append({"id": it["id"], "q": it["q"], "col": it.get("col") or ""})
    if extra_path:
        for i, line in enumerate(io.open(extra_path, encoding="utf-8")):
            line = line.strip()
            if line and not line.startswith("#"):
                qs.append({"id": f"extra-{i + 1:03d}", "q": line, "col": ""})
    return qs


def run(app_mod, queries, k):
    out = {}
    for it in queries:
        chunks, _ = app_mod.KBX.hybrid(it["q"], k=k, col=it["col"])
        out[it["id"]] = {
            "q": it["q"], "col": it["col"],
            "ids": [c["id"] for c in chunks],
            "paths": [c["path"] for c in chunks],
            "rrf": [c["rrf"] for c in chunks],
        }
    return out


def diff(base, cur):
    """返回差异列表；空列表 = 逐位一致。纯函数，便于单测。"""
    problems = []
    for qid, b in base.items():
        c = cur.get(qid)
        if c is None:
            problems.append((qid, "当前缺少这条查询"))
            continue
        if b["ids"] != c["ids"]:
            moved = [i for i, (x, y) in enumerate(zip(b["ids"], c["ids"])) if x != y]
            first = moved[0] + 1 if moved else min(len(b["ids"]), len(c["ids"])) + 1
            problems.append((qid, f"id 序列变化，首个差异在第 {first} 位；"
                                  f"基线 top3={b['paths'][:3]} 现在 top3={c['paths'][:3]}"))
        elif any(abs(x - y) > TOL for x, y in zip(b["rrf"], c["rrf"])):
            problems.append((qid, "id 序列相同但分数漂移超过 1e-6"))
    for qid in cur:
        if qid not in base:
            problems.append((qid, "基线里没有这条查询（用 record 重新生成基线）"))
    return problems


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("mode", choices=["record", "compare"])
    ap.add_argument("--out", default="")
    ap.add_argument("--base", default="")
    ap.add_argument("--k", type=int, default=10)
    ap.add_argument("--golden", default=os.path.join(_EVAL_DIR, "golden.json"))
    ap.add_argument("--extra", default="")
    ap.add_argument("--app-dir", default=os.environ.get("KB_APP_DIR", os.path.join(_EVAL_DIR, "..", "app")))
    a = ap.parse_args()

    sys.path.insert(0, a.app_dir)
    import app as A   # 与 eval_retrieval.py 一样，直接加载真实应用

    qs = load_queries(a.golden, a.extra)
    cur = run(A, qs, a.k)

    if a.mode == "record":
        if not a.out:
            ap.error("record 需要 --out")
        json.dump({"k": a.k, "n": len(cur), "items": cur},
                  io.open(a.out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"已记录 {len(cur)} 条查询的 top-{a.k} -> {a.out}")
        return 0

    if not a.base:
        ap.error("compare 需要 --base")
    base = json.load(io.open(a.base, encoding="utf-8"))
    probs = diff(base["items"], cur)
    if not probs:
        print(f"一致：{len(cur)} 条查询的 top-{a.k} 逐位相同")
        return 0
    print(f"有差异：{len(probs)}/{len(cur)} 条")
    for qid, msg in probs[:30]:
        print(f"  [{qid}] {msg}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
