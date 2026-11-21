#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""导入方向测试：kb.retrieval 不得导入 kb.web 或 kb.generation。

用临时环境复制 app/，建最小语料库，让 kb.retrieval 的 KBX 能只读打开 kb.db，
然后真实导入 kb.retrieval，断言 web / generation 从未被拉进 sys.modules。
"""
import os
import shutil
import sqlite3
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

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
        f.write('{"auth": {"secret": "' + "s" * 40 + '"}}\n')
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
        for _k in list(sys.modules):
            if _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        _tmp.cleanup()


class ImportDirectionTests(unittest.TestCase):
    def test_retrieval_does_not_import_web_or_generation(self):
        for _k in list(sys.modules):
            if _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        sys.path.insert(0, os.path.join(_root, "app"))
        import kb.retrieval  # noqa: F401
        for mod in ("kb.web", "kb.generation"):
            self.assertNotIn(mod, sys.modules, f"{mod} 被 kb.retrieval 间接导入")


if __name__ == "__main__":
    unittest.main()
