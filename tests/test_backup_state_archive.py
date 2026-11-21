"""Local-only archive regression: python3 tests/test_backup_state_archive.py --bash /usr/bin/bash."""
import argparse
from contextlib import closing
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import tarfile
import tempfile
import unittest

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
TABLES = ("users", "codes", "conv", "msg", "feedback", "quota", "seo_cache",
          "doc_votes", "embed_cache", "oauth_identities")
# Windows PATH may resolve WSL bash, which cannot read Git-Bash /c paths.
_git_bash = Path(os.environ.get("ProgramFiles", "C:/Program Files")) / "Git/bin/bash.exe"
BASH = os.environ.get("KKS_TEST_BASH") or (
    str(_git_bash) if os.name == "nt" and _git_bash.is_file() else "bash")


def shell_path(path):
    value = str(path).replace("\\", "/")
    if os.name == "nt" and len(value) > 1 and value[1] == ":":
        return "/" + value[0].lower() + value[2:]
    return value


class BackupArchiveTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="kb-backup-test-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.kb = self.root / "kb"
        data = self.kb / "data"
        (data / "col").mkdir(parents=True)
        (data / "source-files").mkdir()
        for i in range(12):
            (data / "col" / f"{i}.txt").write_text("fixture", encoding="utf-8")
        (self.kb / "config.json").write_text("{}", encoding="utf-8")
        np.save(data / "kb.db.vec.npy", np.zeros((1, 2)))
        (data / "kb.db.vec.ids.json").write_text("[1]", encoding="utf-8")
        with closing(sqlite3.connect(data / "kb.db")) as con:
            con.executescript("CREATE TABLE meta(id INTEGER); INSERT INTO meta VALUES(1);"
                              "CREATE TABLE doc(id INTEGER); INSERT INTO doc VALUES(1);"
                              "CREATE TABLE users(id INTEGER); INSERT INTO users VALUES(99);")
        self.state = sqlite3.connect(data / "state.db")
        self.addCleanup(self.state.close)
        self.state.execute("PRAGMA journal_mode=WAL")
        for table in TABLES:
            self.state.execute(f'CREATE TABLE "{table}"(id INTEGER, marker TEXT)')
            self.state.execute(f'INSERT INTO "{table}" VALUES(1, ?)', ("state-only-record",))
        self.state.commit()  # Keep connection open: committed records remain in WAL.

    def run_script(self, name, *args, success=True):
        env = dict(os.environ, KB=shell_path(self.kb), DEST=shell_path(self.root / "backups"),
                   MIN_FREE_MB="0", TMPDIR=shell_path(self.root))
        result = subprocess.run([BASH, (ROOT / "ops/deploy" / name).as_posix(), *map(shell_path, args)],
                                env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
        # Logs contain only fixture counts/paths, never real data.
        if success:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout + result.stderr

    def backup(self):
        self.run_script("backup.sh")
        return next((self.root / "backups/daily").glob("*.tar.gz"))

    def unpack(self, archive):
        target = self.root / "unpacked"
        target.mkdir()
        with tarfile.open(archive) as tar:
            tar.extractall(target, filter="data")
        return target

    def repack(self, target):
        archive = self.root / "changed.tar.gz"
        with tarfile.open(archive, "w:gz") as tar:
            for path in target.iterdir():
                tar.add(path, arcname=path.name)
        return archive

    def test_split_wal_state_roundtrip(self):
        archive = self.backup()
        target = self.unpack(archive)
        self.assertTrue((target / "state.db").is_file())
        with closing(sqlite3.connect(target / "state.db")) as con:
            for table in TABLES:
                self.assertEqual(con.execute(f'SELECT marker FROM "{table}"').fetchone()[0],
                                 "state-only-record")
        counts = json.loads((target / "state.db.counts.json").read_text())
        self.assertEqual(counts, dict.fromkeys(TABLES, 1))
        self.assertIn("state.db", self.run_script("restore_check.sh", archive.as_posix()))

    def test_missing_state_fails(self):
        target = self.unpack(self.backup())
        (target / "state.db").unlink(missing_ok=True)
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_corrupt_state_fails(self):
        target = self.unpack(self.backup())
        (target / "state.db").write_bytes(b"corrupt fixture")
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_state_counts_mismatch_fails(self):
        target = self.unpack(self.backup())
        with closing(sqlite3.connect(target / "state.db")) as con:
            con.execute("DELETE FROM oauth_identities")
            con.commit()
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_missing_state_counts_fails(self):
        target = self.unpack(self.backup())
        (target / "state.db.counts.json").unlink(missing_ok=True)
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_legacy_and_old_archive(self):
        self.state.close()
        (self.kb / "data/state.db").unlink()
        archive = self.backup()
        self.assertIn("legacy", self.run_script("restore_check.sh", archive.as_posix()))
        target = self.unpack(archive)
        (target / "backup.manifest.json").unlink(missing_ok=True)
        (target / "kb.db.counts.json").write_text(json.dumps(
            {"users": 1, "conv": None, "msg": None, "meta": 1, "doc": 1}))
        self.assertIn("legacy", self.run_script("restore_check.sh", self.repack(target).as_posix()))

    def test_missing_state_and_counts_fails(self):
        target = self.unpack(self.backup())
        (target / "state.db").unlink()
        (target / "state.db.counts.json").unlink()
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_state_without_manifest_fails(self):
        target = self.unpack(self.backup())
        (target / "backup.manifest.json").unlink()
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_kb_counts_mismatch_fails(self):
        target = self.unpack(self.backup())
        with closing(sqlite3.connect(target / "kb.db")) as con:
            con.execute("DELETE FROM doc")
            con.commit()
        self.run_script("restore_check.sh", self.repack(target).as_posix(), success=False)

    def test_corrupt_source_state_fails_backup(self):
        self.state.close()
        (self.kb / "data/state.db").write_bytes(b"corrupt source fixture")
        self.run_script("backup.sh", success=False)
        self.assertFalse(list((self.root / "backups/daily").glob("*.tar.gz")))

    def test_shell_lf_and_syntax(self):
        for name in ("backup.sh", "restore_check.sh"):
            path = ROOT / "ops/deploy" / name
            self.assertNotIn(b"\r", path.read_bytes())
            subprocess.run([BASH, "-n", path.as_posix()], check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--bash", default="bash")
    args, remaining = parser.parse_known_args()
    BASH = args.bash
    unittest.main(argv=[__file__, *remaining])
