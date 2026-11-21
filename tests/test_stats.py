#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""eval/stats.py 的统计函数与 CLI。"""
import contextlib
import importlib.util
import io
import json
import os
import tempfile
import unittest

_p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "eval", "stats.py")
_spec = importlib.util.spec_from_file_location("stats", _p)
st = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(st)


def it(item_id, hit3, col="", rr=None):
    """造一条评估结果。hit1/hit10 与 hit3 对齐（真实的 hit1 ⊆ hit3 ⊆ hit10）。"""
    return {"id": item_id, "hit1": hit3, "hit3": hit3, "hit10": hit3,
            "rr": (1.0 if hit3 else 0.0) if rr is None else rr, "col": col}


def run(items):
    return {"items": items}


class WilsonTests(unittest.TestCase):
    def test_known_value_44_of_45(self):
        lo, hi = st.wilson_ci(44, 45)
        self.assertAlmostEqual(lo, 0.884, delta=0.002)
        self.assertAlmostEqual(hi, 0.996, delta=0.002)

    def test_known_value_42_of_45(self):
        # 93.3% 的区间必须落在 [82.1%, 97.7%] 附近，正是「两次结果大幅重叠」的证据
        lo, hi = st.wilson_ci(42, 45)
        self.assertAlmostEqual(lo, 0.821, delta=0.002)
        self.assertAlmostEqual(hi, 0.977, delta=0.002)

    def test_zero_n(self):
        self.assertEqual(st.wilson_ci(0, 0), (0.0, 1.0))

    def test_bounds_never_exceeded(self):
        for k, n in [(0, 45), (45, 45), (1, 3), (0, 1), (7, 7)]:
            lo, hi = st.wilson_ci(k, n)
            self.assertGreaterEqual(lo, 0.0)
            self.assertLessEqual(hi, 1.0)
            self.assertLessEqual(lo, k / n)
            self.assertGreaterEqual(hi, k / n)

    def test_interval_narrows_with_n(self):
        lo_small, hi_small = st.wilson_ci(9, 10)
        lo_big, hi_big = st.wilson_ci(90, 100)
        self.assertGreater(hi_small - lo_small, hi_big - lo_big)


class McNemarTests(unittest.TestCase):
    def test_no_discordant_pairs(self):
        self.assertEqual(st.mcnemar_exact(0, 0), 1.0)

    def test_all_discordant_one_way(self):
        self.assertAlmostEqual(st.mcnemar_exact(10, 0), 2 * 0.5 ** 10, places=6)

    def test_symmetric_no_evidence_is_one(self):
        self.assertEqual(st.mcnemar_exact(10, 10), 1.0)

    def test_symmetry(self):
        for b, c in [(10, 0), (3, 7), (1, 3), (12, 4), (0, 5)]:
            self.assertEqual(st.mcnemar_exact(b, c), st.mcnemar_exact(c, b))

    def test_known_small_case(self):
        # n=10, k=3：(1+10+45+120)/1024 * 2 = 0.34375
        self.assertAlmostEqual(st.mcnemar_exact(3, 7), 0.34375, places=9)

    def test_p_value_in_range(self):
        self.assertTrue(0.0 <= st.mcnemar_exact(5, 2) <= 1.0)


class BootstrapTests(unittest.TestCase):
    def test_identical_lists_ci_contains_zero(self):
        a = [1.0, 0.0, 1.0, 1.0, 0.0, 1.0, 1.0]
        lo, hi = st.paired_bootstrap(a, list(a))
        self.assertLessEqual(lo, 0.0)
        self.assertGreaterEqual(hi, 0.0)

    def test_deterministic_for_same_seed(self):
        a = [0.0, 0.0, 1.0, 0.0, 1.0, 1.0]
        b = [1.0, 0.0, 1.0, 1.0, 1.0, 1.0]
        self.assertEqual(st.paired_bootstrap(a, b), st.paired_bootstrap(a, b))

    def test_interval_brackets_point_estimate(self):
        a = [0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 1.0, 0.0]
        b = [1.0, 1.0, 1.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0, 1.0]
        point = sum(b) / len(b) - sum(a) / len(a)
        lo, hi = st.paired_bootstrap(a, b)
        self.assertLessEqual(lo, point)
        self.assertGreaterEqual(hi, point)

    def test_uniform_improvement_shifts_interval_up(self):
        lo, hi = st.paired_bootstrap([0.0] * 8, [1.0] * 8)
        self.assertAlmostEqual(lo, 1.0)
        self.assertAlmostEqual(hi, 1.0)

    def test_mismatched_length_raises(self):
        with self.assertRaises(ValueError):
            st.paired_bootstrap([1.0, 0.0], [1.0])


class SplitTests(unittest.TestCase):
    def test_stable_per_id(self):
        for _ in range(5):
            self.assertEqual(st.split_of("kkuts-01"), "holdout")
            self.assertEqual(st.split_of("az-01"), "holdout")
        self.assertIn(st.split_of("whatever-id"), ("train", "holdout"))

    def test_pinned_values_survive_python_version(self):
        # sha1 取模是刻意选择的：内置 hash() 会因 PYTHONHASHSEED 每进程不同。
        # 这几个值钉死，换 Python 版本/换机器都不许变。
        self.assertEqual(st.split_of("kkuts-01"), "holdout")          # bucket 0
        self.assertEqual(st.split_of("q000"), "train")                # bucket 6
        self.assertEqual(st.split_of("q003"), "train")                # bucket 3，不在默认留出折
        self.assertEqual(st.split_of("q003", holdout_folds=(0, 1, 2, 3)), "holdout")

    def test_holdout_ratio_is_about_30_percent(self):
        ids = [f"q{i:03d}" for i in range(200)]
        holdout = sum(1 for i in ids if st.split_of(i) == "holdout")
        self.assertTrue(0.20 <= holdout / len(ids) <= 0.40, f"holdout 比例 {holdout}/200")

    def test_custom_folds(self):
        self.assertIn(st.split_of("x", n_folds=2, holdout_folds=(0,)), ("train", "holdout"))


class CompareTests(unittest.TestCase):
    def test_strictly_better_raises_hit3_but_cannot_claim(self):
        ids = [f"q{i:03d}" for i in range(45)]
        base = run([it(i, False) for i in ids])
        new = run([it(i, True) for i in ids])
        rep = st.compare(base, new)
        self.assertGreater(rep["splits"]["all"]["new"]["hit@3"], rep["splits"]["all"]["base"]["hit@3"])
        self.assertEqual(rep["splits"]["all"]["delta"]["hit@3"], 1.0)
        self.assertFalse(rep["claim_allowed"])
        self.assertIn("n=", rep["claim_reason"])
        self.assertIn("30", rep["claim_reason"])
        self.assertLess(rep["holdout_n"], 30)

    def test_claim_allowed_with_significant_improvement(self):
        # 留出集 >=30 且配对检验显著、留出集未下降、无库退步 -> 可声明提升
        ids = [f"q{i:03d}" for i in range(150)]
        hold = [i for i in ids if st.split_of(i) == "holdout"]
        self.assertGreaterEqual(len(hold), 30)
        base = run([it(i, False) for i in ids])
        new = run([it(i, i in hold) for i in ids])
        rep = st.compare(base, new)
        self.assertTrue(rep["claim_allowed"])

    def test_claim_blocked_when_not_significant(self):
        # 留出集 >=30 但只有 1 对不一致 -> McNemar p=1.0，bootstrap 跨 0
        ids = [f"q{i:03d}" for i in range(150)]
        hold = [i for i in ids if st.split_of(i) == "holdout"]
        self.assertGreaterEqual(len(hold), 30)
        base = run([it(i, i != hold[0]) for i in ids])
        new = run([it(i, True) for i in ids])
        rep = st.compare(base, new)
        self.assertFalse(rep["claim_allowed"])
        self.assertIn("配对检验不显著", rep["claim_reason"])

    def test_claim_blocked_by_small_n_even_if_significant(self):
        # 全部改善但留出集 <30 -> 仍然只允许「无退步」
        ids = [f"q{i:03d}" for i in range(20)]
        base = run([it(i, False) for i in ids])
        new = run([it(i, True) for i in ids])
        rep = st.compare(base, new)
        self.assertFalse(rep["claim_allowed"])
        self.assertIn("样本太小", rep["claim_reason"])
        self.assertIn("30", rep["claim_reason"])

    def test_claim_blocked_by_collection_regression(self):
        # 留出集显著改善，但某库（仅 train 上）退步超过 1 题 -> 仍不允提升
        ids = [f"q{i:03d}" for i in range(150)]
        train = [i for i in ids if st.split_of(i) == "train"]
        hold = [i for i in ids if st.split_of(i) == "holdout"]
        self.assertGreaterEqual(len(hold), 30)
        bad = set(train[:5])
        base_items, new_items = [], []
        for i in ids:
            if i in bad:
                base_items.append(it(i, True, "badlib"))
                new_items.append(it(i, False, "badlib"))
            elif i in hold:
                base_items.append(it(i, False))
                new_items.append(it(i, True))
            else:
                base_items.append(it(i, True))
                new_items.append(it(i, True))
        rep = st.compare(run(base_items), run(new_items))
        self.assertFalse(rep["claim_allowed"])
        self.assertIn("badlib", rep["claim_reason"])

    def test_regressed_item_is_listed(self):
        ids = [f"q{i:03d}" for i in range(20)]
        base = run([it(i, True) for i in ids])
        new = run([it(i, i != "q007") for i in ids])
        rep = st.compare(base, new)
        self.assertEqual(len(rep["changes"]), 1)
        change = rep["changes"][0]
        self.assertEqual(change["id"], "q007")
        self.assertTrue(change["base_hit3"])
        self.assertFalse(change["new_hit3"])

    def test_split_sizes_add_up_and_metrics_bounded(self):
        ids = [f"q{i:03d}" for i in range(45)]
        rep = st.compare(run([it(i, i.endswith("5")) for i in ids]),
                         run([it(i, not i.endswith("5")) for i in ids]))
        splits = rep["splits"]
        self.assertEqual(splits["train"]["n"] + splits["holdout"]["n"], splits["all"]["n"])
        self.assertEqual(splits["all"]["n"], 45)
        for name in ("all", "train", "holdout"):
            for side in ("base", "new"):
                for key in ("hit@1", "hit@3", "hit@10"):
                    lo, hi = splits[name]["ci"][side][key]
                    self.assertTrue(0.0 <= lo <= splits[name][side][key] <= hi <= 1.0)

    def test_discordant_pair_counts_and_p_value(self):
        # c=3 题只有 new 命中，b=1 题只有 base 命中 → McNemar p = 0.625
        base, new = [], []
        for i in range(10):
            hit_b = i == 0
            hit_n = i in (1, 2, 3)
            base.append(it(f"q{i:03d}", hit_b))
            new.append(it(f"q{i:03d}", hit_n))
        blk = st.compare(run(base), run(new))["splits"]["all"]
        self.assertEqual(blk["discordant"], {"b": 1, "c": 3})
        self.assertAlmostEqual(blk["mcnemar_p"], 0.625, places=9)

    def test_bootstrap_interval_reported(self):
        ids = [f"q{i:03d}" for i in range(45)]
        rep = st.compare(run([it(i, False) for i in ids]), run([it(i, True) for i in ids]))
        lo, hi = rep["splits"]["all"]["boot_ci"]
        self.assertLessEqual(lo, hi)
        self.assertGreater(lo, 0.0)

    def test_collection_regression_detected(self):
        web = [f"w{i:02d}" for i in range(5)]
        other = [f"o{i:02d}" for i in range(5)]
        base = run([it(i, True, col="web") for i in web] + [it(i, True, col="") for i in other])
        new = run([it(i, i != "w02", col="web") for i in web] +
                  [it(i, False, col="") for i in other])
        cols = {c["col"]: c for c in st.compare(base, new)["collections"]}
        self.assertEqual(set(cols), {"web"})
        self.assertTrue(cols["web"]["regressed"])
        self.assertEqual(cols["web"]["n"], 5)
        self.assertAlmostEqual(cols["web"]["base_hit3"], 1.0)
        self.assertAlmostEqual(cols["web"]["new_hit3"], 0.8)

    def test_unmatched_ids_reported_not_fatal(self):
        rep = st.compare(run([it("a", True), it("b", True)]), run([it("a", True), it("c", True)]))
        self.assertEqual(rep["matched"], 1)
        self.assertEqual(rep["unmatched_base"], ["b"])
        self.assertEqual(rep["unmatched_new"], ["c"])

    def test_tiny_run_with_empty_split_does_not_crash(self):
        rep = st.compare(run([it("a", True), it("b", False)]), run([it("a", True), it("b", True)]))
        self.assertEqual(rep["splits"]["all"]["n"], 2)
        self.assertEqual(rep["splits"]["all"]["delta"]["hit@3"], 0.5)
        # 题量小时整折可能落空：指标退化为 0.0、区间退化为 (0,1)，而不是 ZeroDivisionError
        self.assertEqual(rep["splits"]["holdout"]["n"], 0)
        self.assertEqual(rep["splits"]["holdout"]["base"]["hit@3"], 0.0)
        self.assertEqual(rep["splits"]["holdout"]["ci"]["base"]["hit@3"], (0.0, 1.0))
        self.assertEqual(rep["splits"]["holdout"]["mcnemar_p"], 1.0)

    def test_large_enough_holdout_allows_claim(self):
        ids = [f"q{i:03d}" for i in range(400)]
        holdout_n = sum(1 for i in ids if st.split_of(i) == "holdout")
        self.assertGreaterEqual(holdout_n, 100)
        rep = st.compare(run([it(i, False) for i in ids]), run([it(i, True) for i in ids]))
        self.assertEqual(rep["holdout_n"], holdout_n)
        self.assertTrue(rep["claim_allowed"])


class CliTests(unittest.TestCase):
    def test_ci_json(self):
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = st.main(["ci", "44", "45", "--json"])
        self.assertEqual(rc, 0)
        data = json.loads(buf.getvalue())
        self.assertEqual(data["k"], 44)
        self.assertAlmostEqual(data["ci"][0], 0.884, delta=0.002)

    def test_ci_human_readable(self):
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            st.main(["ci", "44", "45"])
        self.assertIn("Wilson", buf.getvalue())

    def test_compare_files_json(self):
        ids = [f"q{i:03d}" for i in range(10)]
        with tempfile.TemporaryDirectory() as d:
            p_base = os.path.join(d, "base.json")
            p_new = os.path.join(d, "new.json")
            for path, items in ((p_base, [it(i, False) for i in ids]),
                                (p_new, [it(i, True) for i in ids])):
                with io.open(path, "w", encoding="utf-8") as f:
                    json.dump(run(items), f)
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                rc = st.main(["compare", p_base, p_new, "--json"])
        self.assertEqual(rc, 0)
        data = json.loads(buf.getvalue())
        self.assertFalse(data["claim_allowed"])
        self.assertEqual(data["splits"]["all"]["n"], 10)

    def test_compare_human_output_mentions_claim(self):
        ids = [f"q{i:03d}" for i in range(10)]
        with tempfile.TemporaryDirectory() as d:
            p_base = os.path.join(d, "base.json")
            p_new = os.path.join(d, "new.json")
            for path, items in ((p_base, [it(i, True) for i in ids]),
                                (p_new, [it(i, i != "q004") for i in ids])):
                with io.open(path, "w", encoding="utf-8") as f:
                    json.dump(run(items), f)
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                st.main(["compare", p_base, p_new])
        out = buf.getvalue()
        self.assertIn("q004", out)
        self.assertIn("结论", out)


if __name__ == "__main__":
    unittest.main()
