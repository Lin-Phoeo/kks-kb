#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 kb.db 里的用户态表迁到 state.db（离线、幂等、绝不删源表）。

用法：
    py -3.14 -m app.migrate_state --db <path-to-kb.db> --state <path-to-state.db> [--dry-run]

- 先打开旧 kb.db（只读）核对七张表的行数；
- 建 state.db、逐表复制（只 INSERT，绝不 DELETE/DROP 源表）；
- 再核对一次行数，全部一致才算成功；
- 成功后写标记表 _state_migrated；标记存在时重复运行是 no-op。
"""
import argparse
import os
import sqlite3
from datetime import datetime

try:
    from .kb import store   # py -m app.migrate_state（包上下文）
except ImportError:
    from kb import store    # 测试里把 app/ 直接放进 sys.path

STATE_TABLES = store.STATE_TABLES
MARKER = "_state_migrated"


def _ro(path):
    return sqlite3.connect(f"file:{os.path.abspath(path).replace(os.sep, '/')}?mode=ro",
                           uri=True)


def _counts(con):
    have = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    return {t: (con.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0] if t in have else 0)
            for t in STATE_TABLES}


def _marker_present(con):
    try:
        return con.execute(f"SELECT 1 FROM {MARKER} LIMIT 1").fetchone() is not None
    except sqlite3.OperationalError:
        return False


def migrate(db_path, state_path, dry_run=False):
    if not os.path.exists(db_path):
        raise SystemExit(f"源库不存在：{db_path}")

    src = _ro(db_path)
    try:
        src_counts = _counts(src)
    finally:
        src.close()

    if dry_run:
        print(f"[dry-run] 将从 {db_path} 复制（源表不动）：")
        for t in STATE_TABLES:
            print(f"  {t}: {src_counts[t]} 行")
        print(f"[dry-run] 目标 {state_path} 不会被创建或修改")
        return {"dry_run": True, "source": src_counts, "copied": {}, "marker": False}

    if os.path.exists(state_path):
        chk = _ro(state_path)
        done = _marker_present(chk)
        chk.close()
        if done:
            print(f"[migrate] {state_path} 已有迁移标记，跳过（no-op）")
            return {"dry_run": False, "source": src_counts, "copied": {},
                    "marker": True, "state": src_counts}

    st = sqlite3.connect(state_path)
    src = _ro(db_path)
    try:
        # 防污染：state.db 无标记但已有状态表数据时，拒绝继续。
        # 见过一次真实事故：staging 导入 app 触发 _seed_admin() 播种了管理员，
        # 迁移再 INSERT 源用户就撞 UNIQUE users.id。源库才是权威，宁可拒绝也不静默覆盖。
        polluted = []
        have_tbls = {r[0] for r in st.execute(
            "SELECT name FROM sqlite_master WHERE type=\"table\"")}
        for _t in STATE_TABLES:
            if _t in have_tbls:
                _n = st.execute(f"SELECT COUNT(*) FROM {_t}").fetchone()[0]
                if _n:
                    polluted.append(f"{_t}={_n}")
        if polluted:
            raise SystemExit(
                f"拒绝迁移：{state_path} 无迁移标记但已有数据（{chr(39).join([])}{', '.join(polluted)}）。"
                f"可能被 app 启动播种过。请停止应用后删除该文件，再重跑迁移。")
        st.executescript(store.STATE_SCHEMA)
        st.commit()
        have = {r[0] for r in src.execute(
            "SELECT name FROM sqlite_master WHERE type='table'")}
        for t in STATE_TABLES:
            if t not in have:
                continue
            cols = [r[1] for r in src.execute(f"PRAGMA table_info({t})")]
            rows = src.execute(f"SELECT * FROM {t}").fetchall()
            ph = ",".join("?" * len(cols))
            st.executemany(f"INSERT INTO {t} VALUES ({ph})", rows)
        st.commit()

        state_counts = _counts(st)
        for t in STATE_TABLES:
            if state_counts[t] != src_counts[t]:
                raise SystemExit(
                    f"迁移后行数不一致：{t} 源={src_counts[t]} 目标={state_counts[t]}")
        st.execute(f"CREATE TABLE IF NOT EXISTS {MARKER}(k TEXT PRIMARY KEY, at TEXT)")
        st.execute(f"INSERT OR IGNORE INTO {MARKER}(k,at) VALUES('done',?)",
                   (datetime.now().isoformat(timespec="seconds"),))
        st.commit()
    finally:
        src.close()
        st.close()

    print(f"[migrate] 完成：{state_path}")
    for t in STATE_TABLES:
        print(f"  {t}: {src_counts[t]} 行（源表未动）")
    return {"dry_run": False, "source": src_counts, "copied": src_counts,
            "marker": True, "state": src_counts}


def main(argv=None):
    ap = argparse.ArgumentParser(description="kb.db 用户态表 → state.db（幂等，不删源表）")
    ap.add_argument("--db", required=True, help="源 kb.db 路径")
    ap.add_argument("--state", required=True, help="目标 state.db 路径")
    ap.add_argument("--dry-run", action="store_true", help="只打印将复制的行数，不写任何文件")
    args = ap.parse_args(argv)
    migrate(args.db, args.state, dry_run=args.dry_run)


if __name__ == "__main__":
    main()
