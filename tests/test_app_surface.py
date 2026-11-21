#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""`import app as A` 表面测试：eval/snapshot_topk.py 与 eval/eval_retrieval.py
里 `A.` 用到的名字必须存在；服务器（app 目录入 path）与本地包（仓库根入 path）
两种入口风格分别用子进程验证，互不污染。
"""
import json
import os
import shutil
import subprocess
import sqlite3
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

# 来自 eval/snapshot_topk.py / eval/eval_retrieval.py / eval/eval_answer.py 的 A. 用法
REQUIRED = ["KBX", "llm_chain", "hybrid_recipe", "build_prompt_recipe",
            "build_prompt", "SYS_RECIPE", "validate_params"]

_CODE = ("import sys; sys.path.insert(0, r'{p}'); import app as A; "
         "missing = [n for n in {req!r} if not hasattr(A, n)]; "
         "print('OK' if not missing else 'MISSING:' + ','.join(missing))")

_tmp = None
_root = None


def setUpModule():
    global _tmp, _root
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    _root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(_root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    os.makedirs(os.path.join(_root, "data"))
    with open(os.path.join(_root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"auth": {"secret": "s" * 40}}, f)
    con = sqlite3.connect(os.path.join(_root, "data", "kb.db"))
    con.executescript(
        "CREATE VIRTUAL TABLE fts USING fts5(path, body, tokenize='unicode61');\n"
        "CREATE TABLE meta(id INTEGER PRIMARY KEY, col TEXT, path TEXT, heading TEXT,\n"
        "  section TEXT, body TEXT, hash TEXT, tbl_rows INTEGER DEFAULT 0, ord INTEGER);\n"
        "CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT);")
    con.commit()
    con.close()


def tearDownModule():
    if _tmp:
        _tmp.cleanup()


class AppSurfaceTests(unittest.TestCase):
    def _run(self, path):
        r = subprocess.run([sys.executable, "-c", _CODE.format(p=path, req=REQUIRED)],
                           capture_output=True, text=True, timeout=60)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertTrue(r.stdout.strip().endswith("OK"), r.stdout)

    def test_server_style_app_dir_on_path(self):
        self._run(os.path.join(_root, "app"))

    def test_package_style_repo_root_on_path(self):
        self._run(_root)


if __name__ == "__main__":
    unittest.main()
