"""Append compatibility and atomicity against real temporary SQLite databases."""
import os
import sqlite3
import sys
import tempfile
import types
import unittest
from contextlib import closing
from pathlib import Path
from unittest.mock import patch

_stub = types.ModuleType("jieba")
_stub.setLogLevel = lambda *_: None
_stub.cut_for_search = lambda s: s.split()
sys.modules.setdefault("jieba", _stub)
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "app"))
import indexer


class TrackedConnection(sqlite3.Connection):
    closed = False

    def close(self):
        self.closed = True
        super().close()


class AppendTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.db = str(Path(self.tmp.name) / "kb.db")
        self.docs = Path(self.tmp.name) / "docs"
        self.docs.mkdir()
        for name in ("a", "b"):
            (self.docs / f"{name}.md").write_text(
                f"# Newheading\n\nnewbody {name}\n", encoding="utf-8")
        self.connections = []
        self.sql = []
        self.connect = sqlite3.connect

    def seed(self, columns):
        with closing(self.connect(self.db)) as con:
            con.execute(f"CREATE VIRTUAL TABLE fts USING fts5({columns})")
            con.execute("CREATE TABLE meta(id INTEGER PRIMARY KEY, col TEXT, path TEXT, "
                        "heading TEXT, section TEXT, body TEXT, hash TEXT, tbl_rows INTEGER, ord INTEGER)")
            con.execute("CREATE TABLE doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT)")
            con.execute("CREATE TABLE users(id INTEGER PRIMARY KEY, email TEXT)")
            con.execute("INSERT INTO users VALUES(1, 'preserved@example.test')")
            for cid, col in ((1, "target"), (2, "other")):
                path = f"{col}.md"
                con.execute("INSERT INTO meta VALUES(?,?,?,?,?,?,?,?,?)",
                            (cid, col, path, "Oldheading", "Oldsection", "oldbody", "hash", 0, 0))
                con.execute("INSERT INTO doc VALUES(?,?,?,?)", (path, "Oldheading", 7, col))
                names = [s.strip() for s in columns.split(",")]
                values = {"path": path, "heading": "oldheading", "section": "oldsection", "body": "oldbody"}
                con.execute(f"INSERT INTO fts(rowid,{columns}) VALUES({','.join('?' for _ in range(len(names)+1))})",
                            (cid, *(values[n] for n in names)))
            con.commit()

    def snapshot(self):
        with closing(self.connect(self.db)) as con:
            return {table: con.execute(f"SELECT rowid,* FROM {table} ORDER BY rowid").fetchall()
                    for table in ("meta", "doc", "fts", "users")}

    def tracked_connect(self, *args, **kwargs):
        con = self.connect(*args, **kwargs, factory=TrackedConnection)
        con.set_trace_callback(self.sql.append)
        self.connections.append(con)
        self.addCleanup(con.close)
        return con

    def build(self):
        with patch.object(indexer.sqlite3, "connect", self.tracked_connect):
            indexer.build(self.db, str(self.docs), do_embed=False, append=True, collection="target")

    def assert_closed(self):
        self.assertEqual(len(self.connections), 1)
        self.assertTrue(self.connections[0].closed, "build must explicitly close its connection")
        with self.assertRaises(sqlite3.ProgrammingError):
            self.connections[0].execute("SELECT 1")

    def check_success(self, columns):
        self.seed(columns)
        before = self.snapshot()
        self.build()
        self.assert_closed()
        after = self.snapshot()
        for table in ("meta", "doc", "fts"):
            self.assertIn(before[table][1], after[table], f"other collection changed: {table}")
        self.assertEqual(before["users"], after["users"])
        with closing(self.connect(self.db)) as con:
            self.assertEqual(con.execute("SELECT path FROM meta WHERE col='target' ORDER BY path").fetchall(),
                             [("a.md",), ("b.md",)])
            self.assertEqual(con.execute("SELECT path FROM fts WHERE fts MATCH 'body:newbody' ORDER BY path").fetchall(),
                             [("a.md",), ("b.md",)])
            self.assertEqual(con.execute("SELECT path FROM fts WHERE fts MATCH 'body:oldbody'").fetchall(), [("other.md",)])
            actual_columns = [r[1] for r in con.execute("PRAGMA table_info(fts)")]
            self.assertEqual(actual_columns, [s.strip() for s in columns.split(",")])
            if "heading" in actual_columns:
                self.assertEqual(con.execute("SELECT count(*) FROM fts WHERE fts MATCH 'heading:Newheading'").fetchone()[0], 2)
            else:
                self.assertEqual(con.execute("SELECT path FROM fts WHERE fts MATCH 'path:a'").fetchall(), [("a.md",)])

    def test_v1_append_preserves_path_body_query_semantics(self):
        self.check_success("path, body")

    def test_v2_append_indexes_heading_and_preserves_other_collection(self):
        self.check_success("path, heading, section, body")

    def test_partial_insert_failure_restores_all_original_rows(self):
        for columns in ("path, body", "path, heading, section, body"):
            with self.subTest(columns=columns):
                # Each schema gets its own database; no production files are touched.
                self.db = str(Path(self.tmp.name) / f"failure-{len(columns)}.db")
                self.seed(columns)
                before = self.snapshot()
                self.connections.clear()
                self.sql.clear()
                original_tok = indexer.tok
                def fail_second_body(text):
                    if "newbody b" in text:
                        raise RuntimeError("injected after first complete chunk")
                    return original_tok(text)
                with patch.object(indexer, "tok", side_effect=fail_second_body):
                    with self.assertRaisesRegex(RuntimeError, "injected"):
                        self.build()
                self.assertTrue(any(s.startswith("INSERT INTO fts") for s in self.sql))
                self.assertEqual(self.snapshot(), before)
                self.assert_closed()

    def test_incompatible_schema_fails_before_any_delete(self):
        self.seed("path, heading, body")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "fts"):
            self.build()
        self.assertFalse(any(s.lstrip().upper().startswith("DELETE") for s in self.sql))
        self.assertEqual(self.snapshot(), before)
        self.assert_closed()

    def test_initialization_failure_closes_connection(self):
        self.seed("path, body")
        with closing(self.connect(self.db)) as con:
            con.execute("DROP TABLE doc")
            con.execute("CREATE VIEW doc AS SELECT path FROM meta")
            con.commit()
        with self.assertRaises(sqlite3.OperationalError):
            self.build()
        self.assert_closed()


if __name__ == "__main__":
    unittest.main()
