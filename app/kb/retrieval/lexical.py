#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KB 词法/全文检索与文档清单方法。"""
import sqlite3

import jieba

from ..config import col_label


def _fts_v2(self):
    """FTS 列数检测：v2（含 heading+section）→ 启用标题/章节直命中。

    SELECT sql FROM sqlite_master WHERE name='fts' 一次读到建表 DDL；
    结果按 KB 实例缓存，不改 schema、不动旧库数据。旧 2 列库返回 False，
    走旧逻辑（body OR path 匹配），行为逐字节不变。
    """
    if getattr(self, "_fts_v2_cached", None) is not None:
        return self._fts_v2_cached
    try:
        row = self.con.execute(
            "SELECT sql FROM sqlite_master WHERE name='fts'").fetchone()
        sql = ((row[0] if row else "") or "").lower()
        # 粗判：v2 的建表 DDL 里一定同时出现 heading 和 section 两个列名。
        self._fts_v2_cached = ("heading" in sql and "section" in sql)
    except Exception:
        self._fts_v2_cached = False
    return self._fts_v2_cached


def _fts_match_expr(self, terms):
    """按 schema 版本构造 MATCH 表达式。

    v2：{path heading section body} : (\"t1\" OR \"t2\" ...) — 四列显式 OR 匹配，
    让 heading / section 命中直接贡献到 bm25。
    v1：\"t1\" OR \"t2\" — 旧 2 列（path + body）兜底，与改动前完全一致。
    """
    inner = " OR ".join(f'"{t}"' for t in terms)
    if _fts_v2(self):
        return "{path heading section body} : (" + inner + ")"
    return inner


def fts(self, q, k=20, col=""):
    terms = [t for t in jieba.cut_for_search(q) if t.strip()]
    if not terms:
        return []
    match = _fts_match_expr(self, terms)
    try:
        rows = self.con.execute(
            "SELECT m.id,m.col,m.path,m.heading,m.section,m.body,bm25(fts) AS s FROM fts "
            "JOIN meta m ON m.id=fts.rowid WHERE fts MATCH ? "
            + ("AND m.col=? " if col else "") + "ORDER BY s LIMIT ?",
            (match, col, k) if col else (match, k)).fetchall()
    except sqlite3.OperationalError:
        return []
    return [{"id": r[0], "col": r[1], "path": r[2], "heading": r[3],
             "section": r[4], "body": r[5]} for r in rows]


def search(self, q, limit=40, col=""):
    """FTS 搜索，返回带 <mark> 高亮的片段"""
    terms = [t for t in jieba.cut_for_search(q) if t.strip()]
    if not terms:
        return []
    match = _fts_match_expr(self, terms)
    # v2 schema 下 body 是第 4 列（索引 3），v1 schema 下 body 是第 2 列（索引 1）
    body_col = 3 if _fts_v2(self) else 1
    try:
        rows = self.con.execute(
            "SELECT m.path, m.heading, m.col, snippet(fts, %d, '<mark>', '</mark>', '…', 24) AS sn, "
            "bm25(fts) AS sc FROM fts JOIN meta m ON m.id=fts.rowid "
            "WHERE fts MATCH ? " % body_col
            + ("AND m.col=? " if col else "") + "ORDER BY sc LIMIT ?",
            (match, col, limit) if col else (match, limit)).fetchall()
    except sqlite3.OperationalError:
        return []
    return [{"path": r[0], "heading": r[1], "col": r[2], "snippet": r[3],
             "score": -r[4]} for r in rows]


def tree(self, col=""):
    """按一级目录分组"""
    rows = self.doc_list(col)
    groups = {}
    for path, title, chars in rows:
        top = path.split("/")[0] if "/" in path else "（根）"
        groups.setdefault(top, []).append({"path": path, "title": title, "chars": chars})
    return sorted(groups.items(), key=lambda x: x[0])


def doc_list(self, col=""):
    if col:
        return self.con.execute(
            "SELECT path,title,chars FROM doc WHERE col=? ORDER BY path", (col,)).fetchall()
    return self.con.execute("SELECT path,title,chars FROM doc ORDER BY path").fetchall()


def doc_body(self, path):
    r = self.con.execute("SELECT body FROM meta WHERE path=? ORDER BY ord", (path,)).fetchall()
    return "\n".join(x[0] for x in r) if r else None


def doc_col(self, path):
    """文档所属库 id（模板按库切换版式用）"""
    r = self.con.execute("SELECT col FROM doc WHERE path=?", (path,)).fetchone()
    return r[0] if r else ""


def counts(self, col=""):
    if col:
        return (self.con.execute("SELECT COUNT(*) FROM meta WHERE col=?", (col,)).fetchone()[0],
                self.con.execute("SELECT COUNT(*) FROM doc WHERE col=?", (col,)).fetchone()[0])
    return (self.con.execute("SELECT COUNT(*) FROM meta").fetchone()[0],
            self.con.execute("SELECT COUNT(*) FROM doc").fetchone()[0])


def col_stats(self):
    out = []
    for r in self.con.execute(
            "SELECT col, COUNT(DISTINCT path), COUNT(*) FROM meta GROUP BY col ORDER BY col"):
        out.append({"id": r[0], "docs": r[1], "chunks": r[2], "name": col_label(r[0])})
    return out


def expand_query(self, q):
    """把中文需求词扩成英文术语，拼进检索式（只加不替换）"""
    extra = []
    for zh, ens in self.SYNONYMS.items():
        if zh in q:
            extra.extend(ens)
    if not extra:
        return q
    # 去重保序
    seen, out = set(), []
    for t in extra:
        if t.lower() not in seen:
            seen.add(t.lower())
            out.append(t)
    return q + " " + " ".join(out)
