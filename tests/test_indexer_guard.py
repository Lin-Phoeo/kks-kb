#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""indexer 的"不静默删用户数据"保护。jieba 用桩替代，测试不需要装依赖。"""
import os
import sqlite3
import sys
import tempfile
import types
import unittest

_stub = types.ModuleType("jieba")
_stub.setLogLevel = lambda *_: None
_stub.cut_for_search = lambda s: s.split()
sys.modules.setdefault("jieba", _stub)
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "app"))
import indexer  # noqa: E402


def _make_db(path, users=0, convs=0):
    con = sqlite3.connect(path)
    con.execute("CREATE TABLE users(id INTEGER PRIMARY KEY, email TEXT)")
    con.execute("CREATE TABLE conv(id INTEGER PRIMARY KEY, title TEXT)")

    for i in range(users):
        con.execute("INSERT INTO users(email) VALUES(?)", (f"u{i}@x.y",))
    for i in range(convs):
        con.execute("INSERT INTO conv(title) VALUES(?)", (f"c{i}",))
    con.commit()
    con.close()


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory()
        self.db = os.path.join(self.d.name, "kb.db")
        self.docs = os.path.join(self.d.name, "docs")
        os.makedirs(self.docs)
        with open(os.path.join(self.docs, "a.md"), "w", encoding="utf-8") as f:
            f.write("# T\n\nhello world\n")

    def tearDown(self):
        self.d.cleanup()

    def test_state_rows_counts_only_nonempty_tables(self):
        _make_db(self.db, users=2, convs=0)
        self.assertEqual(indexer.state_rows(self.db), {"users": 2})

    def test_state_rows_missing_db(self):
        self.assertEqual(indexer.state_rows(self.db), {})

    def test_full_rebuild_refuses_when_state_exists(self):
        _make_db(self.db, users=1)
        with self.assertRaises(SystemExit) as cm:
            indexer.build(self.db, self.docs, do_embed=False)
        self.assertIn("拒绝删库", str(cm.exception))
        self.assertTrue(os.path.exists(self.db))
        con = sqlite3.connect(self.db)
        self.assertEqual(con.execute("SELECT COUNT(*) FROM users").fetchone()[0], 1)
        con.close()

    def test_full_rebuild_allowed_on_corpus_only_db(self):
        _make_db(self.db)   # 没有用户态数据
        indexer.build(self.db, self.docs, do_embed=False)
        con = sqlite3.connect(self.db)
        self.assertEqual(con.execute("SELECT COUNT(*) FROM meta").fetchone()[0], 1)
        con.close()

    def test_override_flag(self):
        _make_db(self.db, users=1)
        indexer.build(self.db, self.docs, do_embed=False, allow_destroy_state=True)
        con = sqlite3.connect(self.db)
        has_users = con.execute(
            "SELECT 1 FROM sqlite_master WHERE name='users'").fetchone()
        con.close()
        self.assertIsNone(has_users)

    def test_append_keeps_state(self):
        _make_db(self.db, users=3)
        indexer.build(self.db, self.docs, do_embed=False, append=True, collection="x")
        con = sqlite3.connect(self.db)
        self.assertEqual(con.execute("SELECT COUNT(*) FROM users").fetchone()[0], 3)
        con.close()


if __name__ == "__main__":
    unittest.main()
