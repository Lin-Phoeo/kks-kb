#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""state.db 拆库：StateStore / migrate_state / 语料只读连接。只依赖标准库 + 临时文件。"""
import os
import sqlite3
import sys
import tempfile
import threading
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
APP = os.path.join(HERE, "..", "app")
sys.path.insert(0, os.path.abspath(APP))

from kb import store    # noqa: E402
import migrate_state    # noqa: E402

# 与 app.py 旧 _init_conv 一致的表结构（列名 + 顺序），用于校验“今天的 schema”。
EXPECTED_COLS = {
    "conv": ["id", "title", "created", "updated", "owner_user_id"],
    "msg": ["id", "conv_id", "role", "content", "meta", "created"],
    "users": ["id", "email", "pass", "created", "last_login", "verified"],
    "codes": ["email", "code", "exp", "sent", "tries"],
    "seo_cache": ["qkey", "payload", "ts"],
    "quota": ["email", "day", "n"],
    "feedback": ["id", "msg_id", "email", "vote", "note", "created", "path", "actor_user_id"],
}


def _cols(con, table):
    rows = con.execute(f"PRAGMA table_info({table})").fetchall()
    return [r[1] for r in rows]


def _make_source(db_path):
    """模拟拆库前的 kb.db：含七张用户态表 + 若干行数据。"""
    con = sqlite3.connect(db_path)
    con.executescript(store.STATE_SCHEMA)
    con.execute("INSERT INTO users(email,pass,created,verified) VALUES(?,?,?,1)",
                ("a@x.y", "p1", "2026-01-01"))
    con.execute("INSERT INTO users(email,pass,created,verified) VALUES(?,?,?,1)",
                ("b@x.y", "p2", "2026-01-02"))
    con.execute("INSERT INTO codes(email,code,exp,sent,tries) VALUES(?,?,?,?,0)",
                ("a@x.y", "123456", 9999999999, 1700000000))
    con.execute("INSERT INTO conv(title,created,updated) VALUES(?,?,?)",
                ("会话一", "2026-01-01", "2026-01-02"))
    con.execute("INSERT INTO conv(title,created,updated) VALUES(?,?,?)",
                ("会话二", "2026-01-03", "2026-01-04"))
    con.execute("INSERT INTO msg(conv_id,role,content,meta,created) VALUES(?,?,?,?,?)",
                (1, "user", "你好", '{"useweb":"off"}', "2026-01-02"))
    con.execute("INSERT INTO msg(conv_id,role,content,meta,created) VALUES(?,?,?,?,?)",
                (1, "assistant", "答复", '{"model":"x","chunks":[]}', "2026-01-02"))
    con.execute("INSERT INTO feedback(msg_id,email,vote,note,created) VALUES(?,?,?,?,?)",
                (1, "a@x.y", 1, "好", "2026-01-02"))
    con.execute("INSERT INTO quota(email,day,n) VALUES(?,?,?)", ("a@x.y", "2026-01-02", 3))
    con.execute("INSERT INTO quota(email,day,n) VALUES(?,?,?)", ("b@x.y", "2026-01-02", 5))
    con.execute("INSERT INTO seo_cache(qkey,payload,ts) VALUES(?,?,?)",
                ("rim", '{"items":[]}', 1700000000))
    con.commit()
    con.close()


EXPECTED = {"users": 2, "codes": 1, "conv": 2, "msg": 2,
            "feedback": 1, "quota": 2, "seo_cache": 1}


class StateStoreTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.state = os.path.join(self.d.name, "state.db")
        self.kb = os.path.join(self.d.name, "kb.db")

    def tearDown(self):
        self.d.cleanup()

    def test_creates_all_seven_tables_with_today_schema(self):
        store.StateStore(self.state)
        con = sqlite3.connect(self.state)
        have = {r[0] for r in con.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")}
        for t in EXPECTED_COLS:
            self.assertIn(t, have, f"缺表 {t}")
            self.assertEqual(_cols(con, t), EXPECTED_COLS[t], f"{t} 列与旧 schema 不一致")
        con.close()

    def test_owner_migration_preserves_legacy_rows_and_is_idempotent(self):
        _make_source(self.state)
        with sqlite3.connect(self.state) as con:
            before = con.execute('SELECT * FROM conv').fetchall()
            messages = con.execute('SELECT * FROM msg').fetchall()
        for _ in range(2):
            s = store.StateStore(self.state)
            self.assertEqual(s.fetchall('SELECT id,title,created,updated FROM conv'), before)
            self.assertEqual(s.fetchall('SELECT * FROM msg'), messages)
            self.assertEqual(s.fetchall('SELECT owner_user_id FROM conv'), [(None,), (None,)])
            self.assertEqual(s.fetchone('PRAGMA foreign_keys'), (1,))
            s.close()

    def test_failed_migration_rolls_back_all_schema_and_rows(self):
        from unittest.mock import patch
        _make_source(self.state)
        with sqlite3.connect(self.state) as con:
            before = list(con.iterdump())
        original = store._ensure_feedback_path
        def broken(con):
            original(con)
            con.execute('UPDATE conv SET title=\'broken\'')
            raise RuntimeError('migration fault')
        with patch.object(store, '_ensure_feedback_path', side_effect=broken):
            with self.assertRaises(RuntimeError):
                store.StateStore(self.state)
        with sqlite3.connect(self.state) as con:
            self.assertEqual(list(con.iterdump()), before)

    def test_late_migration_failure_rolls_back_owner_column(self):
        _make_source(self.state)
        with sqlite3.connect(self.state) as con:
            con.execute('ALTER TABLE feedback ADD COLUMN actor_user_id INTEGER')
            con.execute('ALTER TABLE feedback ADD COLUMN path TEXT')
            for _ in range(2):
                con.execute("INSERT INTO feedback(msg_id,actor_user_id,path,vote) VALUES(1,1,'duplicate',1)")
        with sqlite3.connect(self.state) as con:
            before = list(con.iterdump())
        with self.assertRaises(sqlite3.IntegrityError):
            store.StateStore(self.state)
        with sqlite3.connect(self.state) as con:
            self.assertEqual(list(con.iterdump()), before)
            self.assertNotIn('owner_user_id', _cols(con, 'conv'))

    def test_second_instance_shares_lock_without_deadlock(self):
        s1 = store.StateStore(self.state)
        s2 = store.StateStore(self.state)
        # RLock 可重入：同一线程重复获取不会死锁
        with s1._lock:
            with s1._lock:
                pass
        # 两个实例并发写同一文件，不互相死锁
        def work(s, tag):
            for j in range(25):
                s.execute("INSERT INTO quota(email,day,n) VALUES(?,?,1) "
                          "ON CONFLICT(email,day) DO UPDATE SET n=n+1", (f"{tag}{j}@x", "d"))
            s.commit()

        t1 = threading.Thread(target=work, args=(s1, "a"))
        t2 = threading.Thread(target=work, args=(s2, "b"))
        t1.start(); t2.start()
        t1.join(timeout=15); t2.join(timeout=15)
        self.assertFalse(t1.is_alive(), "线程 1 疑似死锁")
        self.assertFalse(t2.is_alive(), "线程 2 疑似死锁")
        n = s1.fetchone("SELECT COUNT(*) FROM quota")[0]
        self.assertEqual(n, 50)

    def test_readonly_corpus_connection_rejects_write(self):
        con = sqlite3.connect(self.kb)
        con.execute("CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT)")
        con.execute("INSERT INTO doc(path,title) VALUES('a.md','A')")
        con.commit()
        con.close()

        ro = store.ro_connect(self.kb)
        try:
            self.assertEqual(ro.execute("SELECT title FROM doc WHERE path='a.md'")
                             .fetchone()[0], "A")
            with self.assertRaises(sqlite3.DatabaseError):
                ro.execute("INSERT INTO doc(path,title) VALUES('b.md','B')")
        finally:
            ro.close()


class MigrateTests(unittest.TestCase):
    def setUp(self):
        self.d = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.kb = os.path.join(self.d.name, "kb.db")
        self.state = os.path.join(self.d.name, "state.db")
        _make_source(self.kb)

    def tearDown(self):
        self.d.cleanup()

    def _counts(self, path, tables=store.STATE_TABLES):
        con = sqlite3.connect(path)
        try:
            return migrate_state._counts(con)
        finally:
            con.close()

    def test_migrate_copies_counts_exactly(self):
        r = migrate_state.migrate(self.kb, self.state)
        self.assertTrue(r["marker"])
        self.assertEqual(self._counts(self.state), EXPECTED)

    def test_migrate_never_deletes_source_tables(self):
        migrate_state.migrate(self.kb, self.state)
        src = self._counts(self.kb)
        self.assertEqual(src, EXPECTED)
        # 源表仍在且数据原封不动
        con = sqlite3.connect(self.kb)
        names = {r[0] for r in con.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")}
        con.close()
        self.assertTrue(set(store.STATE_TABLES).issubset(names))

    def test_migrate_is_idempotent_via_marker(self):
        migrate_state.migrate(self.kb, self.state)
        # 迁移后又往源库加一行：第二次运行若真去复制，会把新行带过去
        con = sqlite3.connect(self.kb)
        con.execute("INSERT INTO conv(title,created,updated) VALUES('后加的','x','x')")
        con.commit()
        con.close()
        r = migrate_state.migrate(self.kb, self.state)
        self.assertTrue(r["marker"])
        self.assertEqual(r["copied"], {})
        self.assertEqual(self._counts(self.state)["conv"], EXPECTED["conv"])

    def test_dry_run_makes_no_changes(self):
        before = self._counts(self.kb)
        r = migrate_state.migrate(self.kb, self.state, dry_run=True)
        self.assertTrue(r["dry_run"])
        self.assertEqual(r["source"], EXPECTED)
        self.assertFalse(os.path.exists(self.state), "dry-run 不应创建 state.db")
        self.assertEqual(self._counts(self.kb), before)

    def test_dry_run_cli_creates_nothing(self):
        migrate_state.main(["--db", self.kb, "--state", self.state, "--dry-run"])
        self.assertFalse(os.path.exists(self.state))


class BusyTimeoutTests(unittest.TestCase):
    """PRAGMA busy_timeout=5000：两个线程同时写不应该再抛 SQLITE_BUSY。"""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        self.path = os.path.join(self._tmp.name, "state.db")

    def tearDown(self):
        self._tmp.cleanup()

    def test_busy_timeout_pragma_set(self):
        s = store.StateStore(self.path)
        try:
            got = s.fetchone("PRAGMA busy_timeout")[0]
        finally:
            s.close()
        # SQLite 把毫秒原值回放回来
        self.assertEqual(got, 5000)

    def test_concurrent_writes_do_not_raise_busy(self):
        s = store.StateStore(self.path)
        errors = []

        def worker(prefix):
            try:
                for i in range(50):
                    s.execute(
                        "INSERT INTO conv(title,created,updated) VALUES(?,?,?)",
                        (f"{prefix}-{i}", "t", "t"))
                    s.commit()
            except sqlite3.OperationalError as e:
                errors.append(f"{prefix}: {e}")

        t1 = threading.Thread(target=worker, args=("A",))
        t2 = threading.Thread(target=worker, args=("B",))
        try:
            t1.start()
            t2.start()
            t1.join(timeout=30)
            t2.join(timeout=30)
        finally:
            s.close()
        self.assertEqual(errors, [], "并发写不应触发 SQLITE_BUSY")
        # 验证两线程的数据都落库了
        con = sqlite3.connect(self.path)
        try:
            n = con.execute("SELECT COUNT(*) FROM conv").fetchone()[0]
        finally:
            con.close()
        self.assertEqual(n, 100)


if __name__ == "__main__":
    unittest.main()
