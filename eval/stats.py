#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""检索评估的统计工具：置信区间 / 显著性检验 / 稳定切分 / 两次运行对比。

为什么需要它：n=45 时 hit@3 97.8% 的 95% Wilson 区间约 [88.4%, 99.6%]，
93.3% 是 [82.1%, 97.7%] —— 两者大幅重叠。只报点估计会把噪声当成提升，
所以这里所有结论都带区间，并且明确规定什么规模才允许说「提升」。

用法：
  py -3.14 eval/stats.py compare base.json new.json [--json]
  py -3.14 eval/stats.py ci 44 45 [--json]

依赖：标准库 + numpy（无 scipy）；语法兼容 Python 3.10。
"""
import argparse
import hashlib
import io
import json
import math
import sys

import numpy as np

_HITS = ("hit@1", "hit@3", "hit@10")
_SPLITS = ("all", "train", "holdout")
_CLAIM_MIN_HOLDOUT = 30  # 留出集题数下限：低于此只允许「无退步」
_CLAIM_ALPHA = 0.05      # 配对检验显著性水平


def wilson_ci(k, n, z=1.96):
    """二项比例的 Wilson 得分区间（95% 用 z=1.96）。

    比 Wald 区间好在 p 接近 0/1 或 n 很小时不会越界、不会退化成零宽度。
    n=0 时没有任何信息，返回 (0.0, 1.0)。
    """
    if n == 0:
        return (0.0, 1.0)
    p = k / n
    denom = 1.0 + z * z / n
    center = (p + z * z / (2.0 * n)) / denom
    half = z * math.sqrt(p * (1.0 - p) / n + z * z / (4.0 * n * n)) / denom
    return (max(0.0, center - half), min(1.0, center + half))


def mcnemar_exact(b, c):
    """配对二分类的 McNemar 精确检验（双侧），返回 p 值。

    b = base 对而 new 错的题数，c = 反过来。双侧精确二项检验 p=0.5，
    用 math.comb 精确求和而非卡方近似：本题集 n 太小，近似不可靠。
    """
    n = b + c
    if n == 0:
        return 1.0
    k = min(b, c)
    # 先算单尾（用 min 保证对称），int/int 真除在 CPython 里对大整数也是正确舍入的
    tail = sum(math.comb(n, i) for i in range(k + 1)) / 2 ** n
    return min(1.0, 2.0 * tail)


def paired_bootstrap(a, b, n_boot=10000, seed=0):
    """配对 bootstrap：mean(b)-mean(a) 的 95% 百分位区间。

    a、b 是按同一顺序配对的每题得分。有放回重采样题目下标，
    保留「同一题两次运行」的配对结构（题目难度带来的方差因此被消掉）。
    同一个 seed 结果完全可复现。
    """
    av = np.asarray(a, dtype=float)
    bv = np.asarray(b, dtype=float)
    if av.shape != bv.shape:
        raise ValueError("paired_bootstrap: a 与 b 长度必须相同")
    n = av.size
    if n == 0:
        return (0.0, 0.0)
    rng = np.random.default_rng(seed)
    idx = rng.integers(0, n, size=(n_boot, n))
    diffs = bv[idx].mean(axis=1) - av[idx].mean(axis=1)
    lo, hi = np.percentile(diffs, [2.5, 97.5])
    return (float(lo), float(hi))


def split_of(item_id, n_folds=10, holdout_folds=(0, 1, 2)):
    """按 id 把题目分到 "train" / "holdout"，跨进程、跨 Python 版本稳定。

    这里刻意不用内置 hash()：CPython 对 str 的 hash 每个进程都加盐
    （PYTHONHASHSEED），换台机器/重启一次切分就变了，留出集也就失效了。
    改用 sha1 前 8 个十六进制字符取模，纯函数、可复现。
    """
    digest = hashlib.sha1(item_id.encode("utf-8")).hexdigest()
    bucket = int(digest[:8], 16) % n_folds
    return "holdout" if bucket in holdout_folds else "train"


def _rate_block(items):
    n = len(items)
    counts = {
        "hit@1": sum(1 for it in items if it["hit1"]),
        "hit@3": sum(1 for it in items if it["hit3"]),
        "hit@10": sum(1 for it in items if it["hit10"]),
    }
    block = {"n": n, "counts": counts, "mrr": (sum(float(it["rr"]) for it in items) / n) if n else 0.0}
    for key in _HITS:
        block[key] = counts[key] / n if n else 0.0
    return block


def _split_block(pairs):
    """pairs: [(base_item, new_item), ...]，同一批题目在两次运行里的配对。"""
    base_block = _rate_block([p[0] for p in pairs])
    new_block = _rate_block([p[1] for p in pairs])
    b = sum(1 for bi, ni in pairs if bi["hit3"] and not ni["hit3"])
    c = sum(1 for bi, ni in pairs if ni["hit3"] and not bi["hit3"])
    boot = paired_bootstrap(
        [1.0 if bi["hit3"] else 0.0 for bi, _ in pairs],
        [1.0 if ni["hit3"] else 0.0 for _, ni in pairs],
    )
    return {
        "n": base_block["n"],
        "base": base_block,
        "new": new_block,
        "ci": {
            "base": {key: wilson_ci(base_block["counts"][key], base_block["n"]) for key in _HITS},
            "new": {key: wilson_ci(new_block["counts"][key], new_block["n"]) for key in _HITS},
        },
        "delta": {key: new_block[key] - base_block[key] for key in ("hit@1", "hit@3", "hit@10", "mrr")},
        "discordant": {"b": b, "c": c},
        "mcnemar_p": mcnemar_exact(b, c),
        "boot_ci": list(boot),
    }


def compare(base, new):
    """对比两次评估运行，按 id 配对。

    base / new 形如 {"items": [{"id", "hit1", "hit3", "hit10", "rr", "col"?}, ...]}。
    返回 all / train / holdout 三块的指标、区间与检验，逐题变化，分库退步，
    以及有没有资格声明「提升」。
    """
    base_by_id = {it["id"]: it for it in base["items"]}
    new_by_id = {it["id"]: it for it in new["items"]}
    pairs = [(base_by_id[i], new_by_id[i]) for i in base_by_id if i in new_by_id]

    splits = {}
    for name in _SPLITS:
        sub = pairs if name == "all" else [p for p in pairs if split_of(p[0]["id"]) == name]
        splits[name] = _split_block(sub)

    changes = []
    for bi, ni in pairs:
        if bool(bi["hit3"]) != bool(ni["hit3"]):
            changes.append({
                "id": bi["id"],
                "base_hit3": bool(bi["hit3"]),
                "new_hit3": bool(ni["hit3"]),
                "split": split_of(bi["id"]),
                "col": bi.get("col", "") or "",
            })

    by_col = {}
    for bi, ni in pairs:
        col = bi.get("col", "") or ""
        if col:
            by_col.setdefault(col, []).append((bi, ni))
    collections = []
    for col, group in by_col.items():
        old = sum(1 for bi, _ in group if bi["hit3"]) / len(group)
        cur = sum(1 for _, ni in group if ni["hit3"]) / len(group)
        collections.append({
            "col": col,
            "n": len(group),
            "base_hit3": old,
            "new_hit3": cur,
            "delta": cur - old,
            "regressed": cur < old,
        })
    collections.sort(key=lambda r: (r["delta"], r["col"]))

    holdout_n = splits["holdout"]["n"]
    ho = splits["holdout"]
    ho_p = ho["mcnemar_p"]
    lo, hi = ho["boot_ci"]
    # 边际 Wilson 区间在 n<100 时宽到无法分辨 5pp，所以不用它做门槛；
    # 改用配对检验（同一批题，只比较结果不一致的对），它对小样本敏感得多。
    significant = (ho_p < _CLAIM_ALPHA) or (lo > 0 or hi < 0)
    no_drop = ho["delta"]["hit@3"] >= 0
    bad_cols = [r["col"] for r in collections
                if r["regressed"] and (r["base_hit3"] - r["new_hit3"]) * r["n"] > 1]
    allowed = (holdout_n >= _CLAIM_MIN_HOLDOUT) and significant and no_drop and not bad_cols

    if holdout_n < _CLAIM_MIN_HOLDOUT:
        reason = (f"holdout n={holdout_n} < {_CLAIM_MIN_HOLDOUT}：样本太小，只能声明「无退步」，"
                  f"不能声明「提升」")
    elif not no_drop:
        reason = (f"holdout n={holdout_n} 达标，但留出集 hit@3 下降 "
                  f"{abs(ho['delta']['hit@3']) * 100:.1f}pp，不能声明提升")
    elif not significant:
        reason = (f"holdout n={holdout_n} 达标，但配对检验不显著"
                  f"（McNemar p={ho_p:.4f}，bootstrap 95% CI "
                  f"[{lo * 100:+.1f}pp, {hi * 100:+.1f}pp] 跨 0）：差异无法与噪声区分")
    elif bad_cols:
        reason = (f"holdout n={holdout_n} 且配对检验显著，但以下库退步超过 1 题："
                  f"{', '.join(bad_cols)}")
    else:
        reason = (f"holdout n={holdout_n} ≥ {_CLAIM_MIN_HOLDOUT}，留出集 hit@3 未下降，"
                  f"配对检验显著（McNemar p={ho_p:.4f}，bootstrap 95% CI "
                  f"[{lo * 100:+.1f}pp, {hi * 100:+.1f}pp]）：可声明提升")

    return {
        "claim_allowed": allowed,
        "claim_reason": reason,
        "holdout_n": holdout_n,
        "matched": len(pairs),
        "unmatched_base": [i for i in base_by_id if i not in new_by_id],
        "unmatched_new": [i for i in new_by_id if i not in base_by_id],
        "splits": splits,
        "changes": changes,
        "collections": collections,
    }


# ---------------------------------------------------------------- CLI

def _pct(x):
    return f"{x * 100:.1f}%"


def _pp(x):
    return f"{x * 100:+.1f}pp"


def _fmt_ci(ci):
    return f"[{ci[0] * 100:.1f},{ci[1] * 100:.1f}]"


def _cell(block, key, ci):
    """一格里放「命中数/总数 比例 [区间]」，定宽以便两列对齐。"""
    return f"{block['counts'][key]:>3}/{block['n']:<3}{_pct(block[key]):>7} {_fmt_ci(ci)}"


def _print_compare(rep):
    print(f"配对题目 {rep['matched']} 条"
          f"（base 独有 {len(rep['unmatched_base'])}，new 独有 {len(rep['unmatched_new'])}）")
    if rep["unmatched_base"]:
        print(f"  base 独有 id: {', '.join(rep['unmatched_base'])}")
    if rep["unmatched_new"]:
        print(f"  new 独有 id: {', '.join(rep['unmatched_new'])}")

    for name in _SPLITS:
        blk = rep["splits"][name]
        print(f"\n── {name} ── n={blk['n']} ──")
        for key in _HITS:
            left = _cell(blk["base"], key, blk["ci"]["base"][key])
            right = _cell(blk["new"], key, blk["ci"]["new"][key])
            print(f"  {key:<7} {left:<28} →  {right:<28} {_pp(blk['delta'][key])}")
        mrr_l = f"{blk['base']['mrr']:.4f}".rjust(20)
        mrr_r = f"{blk['new']['mrr']:.4f}".rjust(20)
        print(f"  {'MRR':<7} {mrr_l:<28} →  {mrr_r:<28} {blk['delta']['mrr']:+.4f}")
        print(f"  McNemar(hit@3) b={blk['discordant']['b']} c={blk['discordant']['c']} "
              f"p={blk['mcnemar_p']:.4f}   bootstrap 95% CI Δhit@3 "
              f"[{_pp(blk['boot_ci'][0])}, {_pp(blk['boot_ci'][1])}]")

    if rep["changes"]:
        print(f"\n── hit@3 有变化的题（{len(rep['changes'])}）──")
        for ch in rep["changes"]:
            mark = "+" if ch["new_hit3"] else "-"
            verdict = "提升" if ch["new_hit3"] else "退步"
            col = f" col={ch['col']}" if ch["col"] else ""
            print(f"  {mark} {ch['id']:<14} [{ch['split']}{col}]  "
                  f"{int(ch['base_hit3'])} → {int(ch['new_hit3'])}  {verdict}")

    if rep["collections"]:
        print("\n── 分库 hit@3 ——")
        for row in rep["collections"]:
            flag = " [退步]" if row["regressed"] else ""
            print(f"  col={row['col']:<6} n={row['n']:<3} {_pct(row['base_hit3'])} → "
                  f"{_pct(row['new_hit3'])} {_pp(row['delta'])}{flag}")

    print(f"\n结论：{'允许' if rep['claim_allowed'] else '不允许'}声明提升")
    print(f"  {rep['claim_reason']}")


def _print_ci(k, n):
    lo, hi = wilson_ci(k, n)
    rate = k / n if n else 0.0
    print(f"Wilson 95% CI：{k}/{n} = {_pct(rate)}")
    print(f"  [{lo:.4f}, {hi:.4f}]  ({_pct(lo)} – {_pct(hi)})")


def main(argv=None):
    ap = argparse.ArgumentParser(description="检索评估统计工具")
    sub = ap.add_subparsers(dest="cmd", required=True)

    cp = sub.add_parser("compare", help="对比两次评估运行")
    cp.add_argument("base", help="基线运行 json（含 items）")
    cp.add_argument("new", help="新运行 json（含 items）")
    cp.add_argument("--json", action="store_true", help="输出机器可读 JSON")

    cip = sub.add_parser("ci", help="单个比例的 Wilson 区间")
    cip.add_argument("k", type=int)
    cip.add_argument("n", type=int)
    cip.add_argument("--json", action="store_true", help="输出机器可读 JSON")

    args = ap.parse_args(argv)

    if args.cmd == "ci":
        if args.json:
            lo, hi = wilson_ci(args.k, args.n)
            print(json.dumps({"k": args.k, "n": args.n, "ci": [lo, hi]}, ensure_ascii=False))
        else:
            _print_ci(args.k, args.n)
        return 0

    with io.open(args.base, encoding="utf-8") as f:
        base = json.load(f)
    with io.open(args.new, encoding="utf-8") as f:
        new = json.load(f)
    rep = compare(base, new)
    if args.json:
        print(json.dumps(rep, ensure_ascii=False, indent=1))
    else:
        _print_compare(rep)
    return 0


if __name__ == "__main__":
    sys.exit(main())
