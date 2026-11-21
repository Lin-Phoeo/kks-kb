#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""检索质量评估器
   指标：hit@1 / hit@3 / MRR / nDCG@10
   用法：
     python3 eval_retrieval.py                       # 现行人读报告（全部题目）
     python3 eval_retrieval.py --split holdout       # 只报告留出集
     python3 eval_retrieval.py --json base.json      # 写机器可读运行 + 置信区间摘要
"""
import argparse
import datetime
import io
import json
import math
import os
import sys
import time

_EVAL_DIR = os.path.dirname(os.path.abspath(__file__))
if _EVAL_DIR not in sys.path:
    sys.path.insert(0, _EVAL_DIR)
import stats  # noqa: E402  同目录的统计工具（wilson_ci / split_of / compare）

_SPLITS = ("all", "train", "holdout")


def build_parser():
    ap = argparse.ArgumentParser(description="检索质量评估器")
    ap.add_argument("--k", type=int, default=10, help="检索返回条数")
    ap.add_argument("--json", default="", metavar="OUT",
                    help="写机器可读运行文件（stats.compare 直接可用）")
    ap.add_argument("--split", choices=_SPLITS, default="all",
                    help="只报告 all / train / holdout 之一")
    ap.add_argument("--golden", default=os.path.join(_EVAL_DIR, "golden.json"),
                    help="黄金集 json 路径")
    return ap


def load_app():
    """延迟导入当前检出的 app；KB_APP_DIR 可显式指定另一个应用目录。"""
    p = os.environ.get("KB_APP_DIR", os.path.join(_EVAL_DIR, "..", "app"))
    if p not in sys.path:
        sys.path.insert(0, p)
    import app as A  # noqa: E402,F401
    return A


def hit_rank(chunks, expect):
    """返回第一个命中的名次（1-based），没命中返回 None"""
    for i, c in enumerate(chunks, 1):
        p = (c.get("path") or "").replace("\\", "/")
        for e in expect:
            if e and e in p:
                return i
    return None


def _record(it, rank):
    """把一条题目的结果转成 stats.compare 要的 item。"""
    return {
        "id": it["id"],
        "hit1": rank == 1,
        "hit3": rank is not None and rank <= 3,
        "hit10": rank is not None and rank <= 10,
        "rr": (1.0 / rank) if rank else 0.0,
        "col": it.get("col") or "",
    }


def evaluate(A, items, k):
    """逐条检索。返回 (records, 耗时秒, 调试行)。"""
    records = []
    debug_rows = []
    t0 = time.time()
    for it in items:
        q, col, expect = it["q"], it.get("col") or "", it["expect"]
        chunks, _hybrid_stats = A.KBX.hybrid(q, k=k, col=col)
        rank = hit_rank(chunks, expect)
        records.append(_record(it, rank))
        debug_rows.append({"id": it["id"], "q": q, "col": col, "rank": rank,
                           "top": [(c.get("path") or "")[:60] for c in chunks[:3]]})
    return records, time.time() - t0, debug_rows


def _table_metrics(rows, k):
    n = len(rows)
    h1 = h3 = hitk = 0
    rr_sum = ndcg_sum = 0.0
    for r in rows:
        rank = r["rank"]
        if rank is None:
            continue
        if rank == 1:
            h1 += 1
        if rank <= 3:
            h3 += 1
        hitk += 1
        rr_sum += 1.0 / rank
        ndcg_sum += 1.0 if rank == 1 else 1.0 / math.log2(rank + 1)
    return n, h1, h3, hitk, rr_sum, ndcg_sum


def _print_table(rows, k, elapsed, split):
    n, h1, h3, hitk, rr_sum, ndcg_sum = _table_metrics(rows, k)
    if n == 0:
        print(f"评估集 0 条｜耗时 {elapsed:.1f}s")
        return
    label = "" if split == "all" else f"（{split}）"
    print(f"评估集 {n} 条{label}｜耗时 {elapsed:.1f}s（{elapsed/n*1000:.0f}ms/条）\n")
    print(f"  hit@1   {h1:3d}/{n}  {h1/n*100:5.1f}%")
    print(f"  hit@3   {h3:3d}/{n}  {h3/n*100:5.1f}%")
    print(f"  hit@{k}  {hitk:3d}/{n}  {hitk/n*100:5.1f}%")
    print(f"  MRR          {rr_sum/n:.4f}")
    print(f"  nDCG@{k}     {ndcg_sum/n:.4f}")

    miss = [r for r in rows if r["rank"] is None]
    if miss:
        print(f"\n=== 完全没命中 {len(miss)} 条 ===")
        for r in miss:
            print(f"  [{r['id']}] {r['q'][:44]}")
            print(f"        实际 top3: {r['top']}")

    low = [r for r in rows if r["rank"] and r["rank"] > 3]
    if low:
        print(f"\n=== 命中但排在 3 名后 {len(low)} 条 ===")
        for r in low:
            print(f"  [{r['id']}] 第{r['rank']}名  {r['q'][:40]}")


def _print_summary(records):
    """--json 时的紧凑摘要：all/train/holdout 各报 hit@3 与 MRR。"""
    buckets = {s: [] for s in _SPLITS}
    for r in records:
        buckets["all"].append(r)
        buckets[stats.split_of(r["id"])].append(r)
    for name in _SPLITS:
        sub = buckets[name]
        n = len(sub)
        k = sum(1 for r in sub if r["hit3"])
        lo, hi = stats.wilson_ci(k, n)
        pct = (k / n * 100) if n else 0.0
        mrr = (sum(float(r["rr"]) for r in sub) / n) if n else 0.0
        print(f"── {name} ── n={n}")
        print(f"  hit@3 {k}/{n} {pct:.1f}% [{lo*100:.1f},{hi*100:.1f}]")
        print(f"  MRR   {mrr:.4f}")


def main(argv=None):
    args = build_parser().parse_args(argv)

    golden = json.load(io.open(args.golden, encoding="utf-8"))
    items = [x for x in golden["items"] if x.get("expect")]

    A = load_app()
    records, elapsed, debug_rows = evaluate(A, items, args.k)

    if args.split == "all":
        reported = records
        rows = debug_rows
    else:
        reported = [r for r in records if stats.split_of(r["id"]) == args.split]
        rows = [d for d in debug_rows if stats.split_of(d["id"]) == args.split]

    if args.json:
        out = {
            "items": reported,
            "meta": {
                "n_total": len(items),
                "n_reported": len(reported),
                "generated": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
            },
        }
        with io.open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, ensure_ascii=False, indent=1)
        _print_summary(records)
        print(f"\n结果已存 {args.json}")
    else:
        _print_table(rows, args.k, elapsed, args.split)
    return 0


if __name__ == "__main__":
    sys.exit(main())
