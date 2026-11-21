#!/bin/bash
# 只解到临时目录，核对哈希、SQLite 完整性及快照 counts；不修改在线数据。
# 用法: restore_check.sh <归档.tar.gz>
set -euo pipefail

ar=${1:?用法: restore_check.sh <归档.tar.gz>}
[ -f "$ar" ] || { echo "找不到 $ar" >&2; exit 2; }
if [ -f "$ar.sha256" ]; then
  ( cd "$(dirname "$ar")" && sha256sum -c "$(basename "$ar").sha256" )
else
  echo "警告: 没有 .sha256 文件，跳过哈希校验"
fi
work=$(mktemp -d "${TMPDIR:-/tmp}/kb-restore.XXXXXX")
trap 'rm -rf "$work"' EXIT
tar -xzf "$ar" -C "$work"

python3 - "$work" <<'PY'
import json, sqlite3, sys
from pathlib import Path
from contextlib import closing
w = Path(sys.argv[1])
manifest_path = w / "backup.manifest.json"
modern = manifest_path.exists()
if modern:
    manifest = json.loads(manifest_path.read_text())
    mode = manifest.get("mode")
    if manifest.get("version") != 2 or mode not in ("split", "legacy"):
        sys.exit("Invalid backup manifest")
    databases = ["kb.db", "state.db"] if mode == "split" else ["kb.db"]
    if manifest.get("databases") != databases:
        sys.exit("Invalid database inventory")
else:
    if (w / "state.db").exists() or (w / "state.db.counts.json").exists():
        sys.exit("state.db archive missing manifest")
    mode, databases = "legacy", ["kb.db"]
if mode == "legacy":
    if (w / "state.db").exists() or (w / "state.db.counts.json").exists():
        sys.exit("Unexpected state.db in legacy archive")
    print("legacy: kb.db-only compatibility; split user data coverage is unknown.")
else:
    print("split: user/session/identity/feedback data comes from state.db, not old kb.db tables.")

counts_by_db = {}
for name in databases:
    path = w / name
    if not path.is_file():
        sys.exit("Missing " + name)
    try:
        with closing(sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True)) as con:
            if con.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
                sys.exit(name + " integrity_check failed")
            tables = [r[0] for r in con.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
            actual = {t: con.execute('SELECT COUNT(*) FROM "' + t.replace('"', '""') + '"').fetchone()[0]
                      for t in tables}
    except sqlite3.DatabaseError:
        sys.exit(name + " SQLite validation failed")
    counts_path = w / (name + ".counts.json")
    if not counts_path.is_file():
        sys.exit("Missing " + name + ".counts.json")
    expected = json.loads(counts_path.read_text())
    if not isinstance(expected, dict):
        sys.exit("Invalid counts for " + name)
    # Old archives recorded only five tables, with null for absent tables.
    matches = actual == expected if modern else all(actual.get(t) == n for t, n in expected.items())
    if not matches:
        sys.exit(name + " counts mismatch")
    counts_by_db[name] = actual
    print(name + ": integrity_check ok; counts match (" + str(len(actual)) + " tables)")

n = counts_by_db["kb.db"]
if not n.get("meta"):
    sys.exit("meta is empty")
# Validate vectors without printing any user records or configuration content.
try:
    import numpy as np
    mat = np.load(w / "data/kb.db.vec.npy", mmap_mode="r")
    ids = json.loads((w / "data/kb.db.vec.ids.json").read_text())
    if mat.shape[0] != n["meta"] or len(ids) != n["meta"]:
        sys.exit("Vector/meta counts mismatch")
except ImportError:
    print("numpy unavailable; vector validation skipped")
except (OSError, ValueError):
    sys.exit("Vector files validation failed")
if not (w / "config.json").is_file():
    sys.exit("Missing config.json")
PY
echo "恢复演练通过: $ar"
