"""The public demo must be useful offline and must never overwrite user data."""

import hashlib
from contextlib import closing
import importlib.util
import json
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts" / "create_demo.py"
SPEC = importlib.util.spec_from_file_location("create_demo", SCRIPT)
DEMO = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DEMO)


def snapshot(root):
    return {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest()
            for path in root.rglob("*") if path.is_file()}


class DemoTests(unittest.TestCase):
    def test_generates_self_contained_keyword_index_without_network(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch("socket.socket.connect", side_effect=AssertionError("Network is forbidden")) as network:
                self.assertEqual(DEMO.create_demo(root), 3)
                network.assert_not_called()
            config = json.loads((root / "config.json").read_text(encoding="utf-8"))
            self.assertFalse(config["auth"]["required"])
            self.assertGreaterEqual(len(config["auth"]["secret"]), 32)
            self.assertFalse(config["gitee_key"])
            self.assertFalse(config["llm_key"])
            self.assertFalse(config["llm_fallbacks"])
            self.assertTrue((root / "data" / "source-files").is_dir())
            self.assertEqual(len(list((root / "data" / "docs").glob("*.md"))), 3)
            self.assertFalse((root / "data" / "kb.db.vec.npy").exists())
            with closing(sqlite3.connect(root / "data" / "kb.db")) as connection:
                self.assertEqual(connection.execute("SELECT count(*) FROM doc").fetchone()[0], 3)
                self.assertGreater(connection.execute(
                    "SELECT count(*) FROM fts WHERE fts MATCH 'RimLight'").fetchone()[0], 0)
            manual = json.loads((root / "data" / "manual.json").read_text(encoding="utf-8"))
            self.assertEqual(manual["manual_total"], 3)
            self.assertFalse(list(root.glob(".demo-build-*")))

    def test_existing_data_or_config_is_refused_without_changes(self):
        for existing in ("data", "config.json"):
            with self.subTest(existing=existing), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                if existing == "data":
                    (root / "data").mkdir()
                    (root / "data" / "keep.txt").write_text("existing personal data", encoding="utf-8")
                else:
                    (root / "config.json").write_text('{"keep": true}', encoding="utf-8")
                before = snapshot(root)
                result = subprocess.run([sys.executable, str(SCRIPT), "--root", str(root)],
                                        capture_output=True, text=True, timeout=30)
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("Refusing to overwrite", result.stderr)
                self.assertEqual(snapshot(root), before)
                self.assertEqual({path.name for path in root.iterdir()}, {existing})

    def test_empty_existing_data_directory_is_also_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "data").mkdir()
            with self.assertRaises(FileExistsError):
                DEMO.create_demo(root)
            self.assertEqual(list((root / "data").iterdir()), [])
            self.assertFalse((root / "config.json").exists())

    def test_generated_demo_serves_real_pages_without_network(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            result = subprocess.run([sys.executable, str(SCRIPT), "--root", str(root)],
                                    capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn("AI Q&A is not configured", result.stdout)
            shutil.copytree(ROOT / "app", root / "app", ignore=shutil.ignore_patterns("__pycache__"))
            probe = textwrap.dedent("""
                import sys
                from unittest.mock import patch
                sys.path.insert(0, 'app')
                from fastapi.testclient import TestClient
                with patch('httpx.HTTPTransport.handle_request', side_effect=AssertionError('External HTTP is forbidden')) as sync_http, \
                     patch('httpx.AsyncHTTPTransport.handle_async_request', side_effect=AssertionError('External HTTP is forbidden')) as async_http:
                    import app
                    try:
                        with TestClient(app.app, follow_redirects=False) as client:
                            for url in ('/', '/browse', '/manual', '/manual/demo',
                                        '/search?q=RimLight', '/doc?path=02-lighting-notes.md'):
                                response = client.get(url)
                                assert response.status_code == 200, (url, response.status_code, response.text[:500])
                            assert 'RimLight' in client.get('/doc?path=02-lighting-notes.md').text
                            assert '02-lighting-notes.md' in client.get('/search?q=RimLight').text
                            assert client.get('/api/search?q=RimLight').status_code == 200
                            assert client.get('/search?q=demo_no_such_term_938').status_code == 200
                            assert client.get('/doc?path=does-not-exist.md').status_code == 404
                            assert app.KBX.mat is None
                            sync_http.assert_not_called()
                            async_http.assert_not_called()
                    finally:
                        app.KBX.con.close()
                        app.STATE.close()
            """)
            result = subprocess.run([sys.executable, "-c", probe], cwd=root,
                                    capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
