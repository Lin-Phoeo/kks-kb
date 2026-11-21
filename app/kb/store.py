#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""用户态数据的独立 SQLite 存储（state.db）。

拆库后语料连接对 kb.db 只读，users/codes/conv/msg/feedback/quota/seo_cache
七张表全部落到 state.db；doc_votes 为反馈学习信号聚合表。一个进程一个
StateStore：单连接 + threading.RLock，所有读写都走锁；调用方不再碰语料连接。
"""
import os
import sqlite3
import threading

# 与 app.py 原先 _init_conv 里逐字一致，不新增、不删改任何列。
STATE_SCHEMA = """
CREATE TABLE IF NOT EXISTS conv(
  id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT, created TEXT, updated TEXT);
CREATE TABLE IF NOT EXISTS msg(
  id INTEGER PRIMARY KEY AUTOINCREMENT, conv_id INTEGER, role TEXT,
  content TEXT, meta TEXT, created TEXT);
CREATE INDEX IF NOT EXISTS idx_msg_conv ON msg(conv_id);
CREATE TABLE IF NOT EXISTS users(
  id INTEGER PRIMARY KEY AUTOINCREMENT, email TEXT UNIQUE, pass TEXT,
  created TEXT, last_login TEXT, verified INTEGER DEFAULT 1);
CREATE TABLE IF NOT EXISTS codes(
  email TEXT PRIMARY KEY, code TEXT, exp INTEGER, sent INTEGER, tries INTEGER DEFAULT 0);
CREATE TABLE IF NOT EXISTS seo_cache(
  qkey TEXT PRIMARY KEY, payload TEXT, ts INTEGER);
CREATE TABLE IF NOT EXISTS quota(
  email TEXT, day TEXT, n INTEGER, PRIMARY KEY(email, day));
CREATE TABLE IF NOT EXISTS feedback(
  id INTEGER PRIMARY KEY AUTOINCREMENT, msg_id INTEGER, email TEXT,
  vote INTEGER, note TEXT, created TEXT);
CREATE TABLE IF NOT EXISTS doc_votes(
  path TEXT PRIMARY KEY, up INTEGER, down INTEGER, updated TEXT);
CREATE TABLE IF NOT EXISTS embed_cache(
  key TEXT PRIMARY KEY, vec BLOB, dim INTEGER, created TEXT);
"""

STATE_TABLES = ("users", "codes", "conv", "msg", "feedback", "quota", "seo_cache")


def _ensure_feedback_path(con):
    """feedback 表补 path 列（迁移安全：只增不改，旧行不丢）。"""
    cols = {r[1] for r in con.execute("PRAGMA table_info(feedback)")}
    if "path" not in cols:
        con.execute("ALTER TABLE feedback ADD COLUMN path TEXT")


def ro_connect(path):
    """语料库（kb.db）只读连接。

    权衡：每线程一个只读连接最理想；为不改动现有检索方法的同步调用方式，
    这里保持 check_same_thread=False——SQLite 自身会把并发读串行化，
    写操作由 mode=ro 从文件系统层面挡住（任何 INSERT/UPDATE 都会抛错）。
    """
    return sqlite3.connect(
        f"file:{os.path.abspath(path).replace(os.sep, '/')}?mode=ro",
        uri=True, check_same_thread=False)


class StateStore:
    def __init__(self, path):
        self.path = os.path.abspath(path)
        parent = os.path.dirname(self.path)
        if parent:
            os.makedirs(parent, exist_ok=True)
        self._lock = threading.RLock()
        self.con = sqlite3.connect(self.path, check_same_thread=False)
        with self._lock:
            # busy_timeout：并发写时等待 5s 再抛 SQLITE_BUSY，避免互相踩死锁。
            self.con.execute("PRAGMA busy_timeout=5000;")
            self.con.execute("PRAGMA foreign_keys=ON;")
            try:
                with self.con:
                    self.con.executescript("BEGIN IMMEDIATE;\n" + STATE_SCHEMA)
                    _ensure_feedback_path(self.con)
                    for table, column in (("conv", "owner_user_id"), ("feedback", "actor_user_id")):
                        cols = {r[1] for r in self.con.execute(f"PRAGMA table_info({table})")}
                        if column not in cols:
                            self.con.execute(f"ALTER TABLE {table} ADD COLUMN {column} INTEGER REFERENCES users(id)")
                    self.con.execute("CREATE INDEX IF NOT EXISTS idx_conv_owner ON conv(owner_user_id)")
                    self.con.execute("CREATE UNIQUE INDEX IF NOT EXISTS idx_feedback_actor ON feedback(actor_user_id,msg_id,path) WHERE actor_user_id IS NOT NULL")
                    self.con.execute("CREATE TABLE IF NOT EXISTS oauth_identities(provider TEXT NOT NULL, sub TEXT NOT NULL, user_id INTEGER NOT NULL REFERENCES users(id), PRIMARY KEY(provider,sub))")
            except Exception:
                self.con.close()
                raise

    # ---- 锁内原语：对 state 表的读写只准走这里 ----
    def execute(self, sql, params=()):
        with self._lock:
            return self.con.execute(sql, params)

    def commit(self):
        with self._lock:
            self.con.commit()

    def fetchone(self, sql, params=()):
        return self.execute(sql, params).fetchone()

    def fetchall(self, sql, params=()):
        return self.execute(sql, params).fetchall()

    def close(self):
        with self._lock:
            self.con.close()
