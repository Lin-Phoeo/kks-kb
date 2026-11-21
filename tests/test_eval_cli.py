#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""eval/eval_retrieval.py 的 CLI：--json 运行文件与 --split。"""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.dirname(_HERE)
_ER_P = os.path.join(_REPO, "eval", "eval_retrieval.py")
_ST_P = os.path.join(_REPO, "eval", "stats.py")
_GOLDEN = None  # Created from original synthetic text in the test fixture.

_spec = importlib.util.spec_from_file_location("eval_retrieval", _ER_P)
er = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(er)

_spec_st = importlib.util.spec_from_file_location("stats_for_eval_cli", _ST_P)
st = importlib.util.module_from_spec(_spec_st)
_spec_st.loader.exec_module(st)


def _golden_expect_ids():
    with io.open(_GOLDEN, encoding="utf-8") as f:
        golden = json.load(f)
    return [x["id"] for x in golden["items"] if x.get("expect")]


class ArgparseTests(unittest.TestCase):
    """参数解析不碰 app / 数据库，任何环境都必须跑。"""

    def test_defaults(self):
        args = er.build_parser().parse_args([])
        self.assertEqual(args.k, 10)
        self.assertEqual(args.json, "")
        self.assertEqual(args.split, "all")
        self.assertTrue(args.golden.endswith("golden.json"))

    def test_split_accepts_three_values(self):
        for s in ("all", "train", "holdout"):
            args = er.build_parser().parse_args(["--split", s])
            self.assertEqual(args.split, s)

    def test_split_rejects_unknown(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            er.build_parser().parse_args(["--split", "nope"])

    def test_json_arg(self):
        args = er.build_parser().parse_args(["--json", "out.json"])
        self.assertEqual(args.json, "out.json")

    def test_k_is_int(self):
        args = er.build_parser().parse_args(["--k", "7"])
        self.assertEqual(args.k, 7)
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            er.build_parser().parse_args(["--k", "x"])

    def test_golden_arg(self):
        args = er.build_parser().parse_args(["--golden", "g.json"])
        self.assertEqual(args.golden, "g.json")


class RunMetricsTests(unittest.TestCase):
    """Real FTS retrieval and CLI metrics over a temporary, synthetic corpus."""

    @classmethod
    def setUpClass(cls):
        global _GOLDEN
        cls._tmp = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls._tmp.cleanup)
        d = cls._tmp.name
        root = Path(d)
        shutil.copytree(Path(_REPO) / 'app', root / 'app', ignore=shutil.ignore_patterns('__pycache__'))
        (root / 'data').mkdir()
        (root / 'docs').mkdir()
        (root / 'config.json').write_text(json.dumps({'auth': {'secret': 's' * 40},
              'llm_url': 'http://127.0.0.1:1/v1', 'llm_key': '', 'gitee_key': ''}), encoding='utf-8')
        items = []
        for i in range(45):
            term = f'syntheticconcept{i:03d}'
            path = f'guide-{i:03d}.md'
            (root / 'docs' / path).write_text(f'# Demo {i}\n\n{term} explains an original sample concept.\n', encoding='utf-8')
            items.append({'id': f'demo-{i:03d}', 'q': term, 'col': 'demo', 'expect': [path]})
        _GOLDEN = str(root / 'golden.json')
        Path(_GOLDEN).write_text(json.dumps({'items': items}), encoding='utf-8')
        cls._old_path = sys.path[:]
        cls.addClassCleanup(lambda: sys.path.__setitem__(slice(None), cls._old_path))
        for name in list(sys.modules):
            if name in {'app', 'kb', 'indexer'} or name.startswith('kb.'):
                del sys.modules[name]
        sys.path.insert(0, str(root / 'app'))
        import indexer
        indexer.build(str(root / 'data/kb.db'), str(root / 'docs'), do_embed=False, collection='demo')
        import app
        cls.mod = app
        cls.addClassCleanup(app.KBX.con.close)
        cls.addClassCleanup(app.STATE.close)
        cls.loader = patch.object(er, 'load_app', return_value=app)
        cls.loader.start()
        cls.addClassCleanup(cls.loader.stop)
        cls.runs = {}
        for name, extra in (("all", []), ("train", ["--split", "train"]),
                            ("holdout", ["--split", "holdout"])):
            p = os.path.join(d, f"{name}.json")
            argv = ["--json", p] + extra + ["--golden", _GOLDEN]
            with contextlib.redirect_stdout(io.StringIO()):
                rc = er.main(argv)
            if rc != 0:
                raise RuntimeError(f"eval_retrieval --json {name} 退出码 {rc}")
            with io.open(p, encoding="utf-8") as f:
                cls.runs[name] = json.load(f)

    def test_json_has_45_items_with_six_keys(self):
        data = self.runs["all"]
        items = data["items"]
        self.assertEqual(len(items), 45)
        self.assertEqual(sorted(i["id"] for i in items), sorted(_golden_expect_ids()))
        for it in items:
            self.assertEqual(set(it), {"id", "hit1", "hit3", "hit10", "rr", "col"})
            self.assertIsInstance(it["id"], str)
            self.assertIsInstance(it["hit1"], bool)
            self.assertIsInstance(it["hit3"], bool)
            self.assertIsInstance(it["hit10"], bool)
            self.assertIsInstance(it["rr"], float)
            self.assertIsInstance(it["col"], str)
        self.assertEqual(data["meta"]["n_total"], 45)
        self.assertEqual(data["meta"]["n_reported"], 45)
        self.assertRegex(data["meta"]["generated"], r"^\d{4}-\d{2}-\d{2}T")

    def test_json_run_accepted_by_stats_compare(self):
        rep = st.compare(self.runs["all"], self.runs["all"])
        self.assertEqual(rep["matched"], 45)
        self.assertEqual(rep["unmatched_base"], [])
        self.assertEqual(rep["unmatched_new"], [])

    def test_split_holdout_fewer_than_all_and_all_holdout(self):
        all_items = self.runs["all"]["items"]
        ho_items = self.runs["holdout"]["items"]
        self.assertLess(len(ho_items), len(all_items))
        self.assertGreater(len(ho_items), 0)
        for it in ho_items:
            self.assertEqual(st.split_of(it["id"]), "holdout")

    def test_train_and_holdout_cover_all_items(self):
        tr_ids = {i["id"] for i in self.runs["train"]["items"]}
        ho_ids = {i["id"] for i in self.runs["holdout"]["items"]}
        all_ids = {i["id"] for i in self.runs["all"]["items"]}
        self.assertEqual(tr_ids | ho_ids, all_ids)
        self.assertEqual(tr_ids & ho_ids, set())
        self.assertEqual(len(all_ids), 45)

    def test_split_and_json_respect_split_in_items(self):
        for it in self.runs["train"]["items"]:
            self.assertEqual(st.split_of(it["id"]), "train")
        for it in self.runs["holdout"]["items"]:
            self.assertEqual(st.split_of(it["id"]), "holdout")

    def test_no_flag_prints_table(self):
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            rc = er.main(["--golden", _GOLDEN])
        self.assertEqual(rc, 0)
        out = buf.getvalue()
        self.assertIn("hit@3", out)
        self.assertIn("MRR", out)


if __name__ == "__main__":
    unittest.main()
