#!/bin/bash
# 每日备份（服务器上以 root 跑，由 kb-backup.timer 触发）。
#
# 备什么：不可重建的 + 重建很贵的
#   kb.db/state.db  分别用 SQLite 在线备份 API 取一致快照；用户数据以 state.db 为准
#   无 state.db 时标记 legacy，仅覆盖 kb.db（不代表拆库后的用户数据已备份）
#   vec.npy/ids  重建可能需要调用 Embedding 服务
#   col/ source-files/ manual.json img 映射 config.json Caddyfile kb.service
# 不备什么：data/img/thumbs 与 orig；无法重建的原图需另行制定备份策略
#
# 为什么不直接 cp kb.db：WAL 模式下直接拷文件会得到损坏的副本。
set -euo pipefail

SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
KB=${KB:-$(cd -- "$SCRIPT_DIR/../.." && pwd)}
DEST=${DEST:-/var/backups/kb}
KEEP_DAILY=${KEEP_DAILY:-7}
KEEP_WEEKLY=${KEEP_WEEKLY:-4}
MIN_FREE_MB=${MIN_FREE_MB:-3000}

umask 077
mkdir -p "$DEST/daily" "$DEST/weekly"
chmod 700 "$DEST"

free_mb=$(df -Pm "$DEST" | awk 'NR==2{print $4}')
if [ "$free_mb" -lt "$MIN_FREE_MB" ]; then
  echo "磁盘剩余 ${free_mb}MB < ${MIN_FREE_MB}MB，放弃备份" >&2
  exit 3
fi

stamp=$(date +%Y%m%d-%H%M%S)
tmp=$(mktemp -d "$DEST/.tmp.XXXXXX")
trap 'rm -rf "$tmp"' EXIT

echo "[1/4] SQLite 一致快照"
python3 - "$KB/data" "$tmp" <<'PY'
import sqlite3, sys, json
from pathlib import Path
from contextlib import closing
source, target = map(Path, sys.argv[1:])
state = (source / "state.db").exists()
manifest = {"version": 2, "mode": "split" if state else "legacy",
            "databases": ["kb.db", "state.db"] if state else ["kb.db"]}
for name in manifest["databases"]:
    with closing(sqlite3.connect((source / name).resolve().as_uri() + "?mode=ro", uri=True)) as src:
        with closing(sqlite3.connect(target / name)) as dst:
            src.backup(dst)
            if dst.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
                sys.exit(name + " integrity_check failed")
            tables = [r[0] for r in dst.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")]
            counts = {t: dst.execute('SELECT COUNT(*) FROM "' + t.replace('"', '""') + '"').fetchone()[0]
                      for t in tables}
    with (target / (name + ".counts.json")).open("w") as f:
        json.dump(counts, f)
    print(name + ": integrity_check ok; table counts saved")
with (target / "backup.manifest.json").open("w") as f:
    json.dump(manifest, f)
if not state:
    print("legacy backup: state.db absent; kb.db only. Split user data is not covered.")
PY
snapshot_items=(kb.db kb.db.counts.json backup.manifest.json)
[ ! -f "$tmp/state.db" ] || snapshot_items+=(state.db state.db.counts.json)

echo "[2/4] 收集文件清单"
cd "$KB"
items=()
for p in data/col data/source-files data/manual.json data/img/manifest.json \
         data/img/ocr_map.json data/kb.db.vec.npy data/kb.db.vec.ids.json \
         data/.session_secret config.json; do
  [ -e "$p" ] && items+=("$p") || echo "  跳过(不存在): $p"
done

echo "[3/4] 打包"
out="$DEST/daily/kb-$stamp.tar.gz"
etc_items=()
for p in etc/caddy/Caddyfile etc/systemd/system/kb.service; do
  [ -e "/$p" ] && etc_items+=("$p")
done
tar_args=(-C "$tmp" "${snapshot_items[@]}" -C "$KB" "${items[@]}")
[ "${#etc_items[@]}" -gt 0 ] && tar_args+=(-C / "${etc_items[@]}")
tar -czf "$out.part" "${tar_args[@]}"

echo "[4/4] 校验（tar 曾静默截断过，必须核对项数）"
gzip -t "$out.part"
# 只解一次目录列表并落成文件再 grep。
# 不能写 tar -tzf x | grep -q：grep -q 命中即退出会关掉管道，tar 收到 SIGPIPE
# 退出 141，set -o pipefail 把整条管道判为失败，导致归档完好却被误判"没有 kb.db"。
tar -tzf "$out.part" > "$tmp/list.txt"
n_items=$(wc -l < "$tmp/list.txt")
for p in "${snapshot_items[@]}"; do
  if ! grep -Fxq "$p" "$tmp/list.txt"; then
    echo "归档缺失 $p，作废" >&2; rm -f "$out.part"; exit 4
  fi
done
if [ "$n_items" -lt 10 ]; then
  echo "归档只有 $n_items 项，疑似截断，作废" >&2; rm -f "$out.part"; exit 4
fi
mv "$out.part" "$out"
( cd "$DEST/daily" && sha256sum "$(basename "$out")" > "$(basename "$out").sha256" )
cp "$tmp/kb.db.counts.json" "$DEST/latest.counts.json"

# 周日额外留一份周备份
if [ "$(date +%u)" = "7" ]; then
  cp "$out" "$out.sha256" "$DEST/weekly/"
fi

# 保留策略
prune() { # dir keep
  local files=("$1"/kb-*.tar.gz)
  [ -e "${files[0]}" ] || return 0
  ls -1t "${files[@]}" | tail -n +"$(( $2 + 1 ))" | while read -r f; do
    rm -f "$f" "$f.sha256"
  done
}
prune "$DEST/daily" "$KEEP_DAILY"
prune "$DEST/weekly" "$KEEP_WEEKLY"

touch "$DEST/last_ok"
echo "完成: $out ($(du -h "$out" | cut -f1), $n_items 项)"
