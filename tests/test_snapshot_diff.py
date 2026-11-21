#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""eval/snapshot_topk.py 的 diff 逻辑（纯函数）。"""
import importlib.util
import os
import unittest

_p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "eval", "snapshot_topk.py")
_spec = importlib.util.spec_from_file_location("snapshot_topk", _p)
st = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(st)


def item(ids, rrf=None, paths=None):
    return {"q": "q", "col": "", "ids": ids, "paths": paths or [f"p{i}" for i in ids],
            "rrf": rrf or [0.03 - 0.001 * k for k in range(len(ids))]}


class DiffTests(unittest.TestCase):
    def test_identical(self):
        b = {"a": item([1, 2, 3])}
        self.assertEqual(st.diff(b, {"a": item([1, 2, 3])}), [])

    def test_order_swap_detected_with_position(self):
        probs = st.diff({"a": item([1, 2, 3])}, {"a": item([1, 3, 2])})
        self.assertEqual(len(probs), 1)
        self.assertIn("第 2 位", probs[0][1])

    def test_score_drift_detected_even_when_order_same(self):
        b = {"a": item([1, 2], rrf=[0.03, 0.02])}
        c = {"a": item([1, 2], rrf=[0.03, 0.0200011])}
        self.assertIn("分数漂移", st.diff(b, c)[0][1])

    def test_tiny_float_noise_tolerated(self):
        b = {"a": item([1, 2], rrf=[0.03, 0.02])}
        c = {"a": item([1, 2], rrf=[0.03 + 1e-9, 0.02])}
        self.assertEqual(st.diff(b, c), [])

    def test_missing_and_extra_queries(self):
        probs = st.diff({"a": item([1])}, {"b": item([1])})
        self.assertEqual({p[0] for p in probs}, {"a", "b"})

    def test_shorter_result_list_is_a_diff(self):
        self.assertEqual(len(st.diff({"a": item([1, 2, 3])}, {"a": item([1, 2])})), 1)


if __name__ == "__main__":
    unittest.main()
