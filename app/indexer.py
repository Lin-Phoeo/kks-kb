#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""知识库索引器：markdown → 表格感知分块 → SQLite FTS5(jieba) + 向量

用法:
  python3 app/indexer.py --docs data/docs --db data/kb.db [--no-embed]
"""
import argparse, hashlib, json, os, re, sqlite3, sys, time
import glob

import jieba
jieba.setLogLevel(60)

EXCLUDE_NAMES = {"全书标题索引.md", "00-手册总目录.md", "全库shader参数注册表.md"}
MAX_CHUNK = 3500      # 单 chunk 字符上限
TABLE_WIN = 30        # 表格行窗口
HEAD_CTX = 6          # 表格窗口保留的上下文行数


# ---------------- 分块 ----------------
def split_by_heading(txt):
    """按 markdown 标题切，绝不切断表格块；返回 [(小节标题, 正文)]"""
    lines = txt.split("\n")
    out, buf, in_tbl = [], [], False
    cur = ""                     # 当前所在的小节标题
    for ln in lines:
        is_tbl = ln.strip().startswith("|")
        m = re.match(r"^(#{1,6})\s+(.+?)\s*$", ln)
        if m and not in_tbl and buf:
            out.append((cur, "\n".join(buf)))
            buf = []
        if m:
            cur = re.sub(r"[*`_]", "", m.group(2)).strip()
        buf.append(ln)
        in_tbl = is_tbl
    if buf:
        out.append((cur, "\n".join(buf)))
    return [(s, c) for s, c in out if c.strip()]


def split_big_table(chunk):
    """超长表格按行窗口切开，每段重复表头 + 所属标题上下文"""
    lines = chunk.split("\n")
    res, tbl, head_ctx = [], [], []

    def flush():
        nonlocal tbl
        if tbl:
            res.append("\n".join(tbl))
            tbl = []

    i = 0
    while i < len(lines):
        ln = lines[i]
        if ln.strip().startswith("|"):
            block = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                block.append(lines[i])
                i += 1
            if len(block) <= TABLE_WIN + 2:
                tbl.extend(block)
            else:
                hdr, body = block[:2], block[2:]
                for s in range(0, len(body), TABLE_WIN):
                    part = ["（续表）"] + hdr + body[s:s + TABLE_WIN]
                    sep = "\n".join(head_ctx[-HEAD_CTX:]) if head_ctx else ""
                    res.append((sep + "\n" if sep else "") + "\n".join(part))
            continue
        if re.match(r"^#{1,6}\s", ln):
            flush()
            head_ctx = [ln]
        tbl.append(ln)
        i += 1
    flush()
    return [c for c in res if c.strip()]


def hard_split(chunk, maxc=MAX_CHUNK):
    if len(chunk) <= maxc:
        return [chunk]
    parts, cur = [], ""
    for para in chunk.split("\n\n"):
        if len(cur) + len(para) > maxc and cur:
            parts.append(cur)
            cur = para
        else:
            cur = (cur + "\n\n" + para) if cur else para
    if cur:
        parts.append(cur)
    return parts


def merge_small(chunks, min_len=420):
    """把过短的块（标题 67 字符这种）向后合并。
    否则检索时「标题块」会以极高相似度占满 top-k，正文一条都进不来。"""
    out, buf = [], None
    for sec, text in chunks:
        if buf is None:
            buf = [sec, text]
        else:
            buf[1] = buf[1] + "\n\n" + text
        if len(buf[1]) >= min_len:
            out.append((buf[0], buf[1]))
            buf = None
    if buf:
        out.append((buf[0], buf[1]))
    return out


def chunk_doc(txt):
    """返回 [(section, text)]"""
    out = []
    for sec, c in split_by_heading(txt):
        for c2 in split_big_table(c):
            for c3 in hard_split(c2):
                out.append((sec, c3))
    return merge_small(out)


def tok(s):
    return " ".join(w for w in jieba.cut_for_search(s) if w.strip())


# ---------------- 建库 ----------------
STATE_TABLES = ("users", "conv", "msg", "feedback", "quota")


def state_rows(db_path):
    """kb.db 里用户态数据（账号/会话/反馈）的行数；这些数据重建不回来。"""
    if not os.path.exists(db_path):
        return {}
    con = sqlite3.connect(f"file:{db_path}?mode=ro", uri=True)
    try:
        have = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        out = {}
        for t in STATE_TABLES:
            if t in have:
                n = con.execute(f"SELECT COUNT(*) FROM {t}").fetchone()[0]
                if n:
                    out[t] = n
        return out
    finally:
        con.close()


def build(db_path, docs_dir, do_embed=True, api_key=None, embed_model="Qwen3-Embedding-4B",
          embed_url="https://ai.gitee.com/v1/embeddings", batch=16,
          collection="kks", append=False, allow_destroy_state=False):
    if not append and os.path.exists(db_path):
        st = state_rows(db_path)
        if st and not allow_destroy_state:
            raise SystemExit(
                f"拒绝删库：{db_path} 里有用户态数据 {st}，全量重建会永久丢掉它们。\n"
                f"  加库请用 --append；确实要清空请先备份，再加 --allow-destroy-state。")
        os.remove(db_path)
    con = sqlite3.connect(db_path)
    try:
        cur = con.cursor()
        cur.execute("PRAGMA journal_mode=WAL")
        # FTS v2：path + heading + section + body 四列全部进索引。
        # 旧库（只有 path+body 两列）由 lexical 层自动走旧查询，不强制重建。
        cur.execute("CREATE VIRTUAL TABLE IF NOT EXISTS fts USING "
                    "fts5(path, heading, section, body, tokenize='unicode61')")
        cur.execute("""CREATE TABLE IF NOT EXISTS meta(
            id INTEGER PRIMARY KEY, col TEXT, path TEXT, heading TEXT, section TEXT, body TEXT,
            hash TEXT, tbl_rows INTEGER DEFAULT 0, ord INTEGER)""")
        cur.execute("CREATE TABLE IF NOT EXISTS doc(path TEXT PRIMARY KEY, title TEXT, chars INTEGER, col TEXT)")

        fts_columns = {r[1] for r in cur.execute("PRAGMA table_info(fts)")}
        if fts_columns not in ({"path", "body"}, {"path", "heading", "section", "body"}):
            raise ValueError(f"Unsupported fts schema: {sorted(fts_columns)}")
        fts_v2 = "heading" in fts_columns

        cur.execute("BEGIN")
        if append:
            # 幂等：清理与写入必须在同一事务中完成。
            old = [r[0] for r in cur.execute("SELECT id FROM meta WHERE col=?", (collection,))]
            for cid in old:
                cur.execute("DELETE FROM fts WHERE rowid=?", (cid,))
            cur.execute("DELETE FROM meta WHERE col=?", (collection,))
            cur.execute("DELETE FROM doc WHERE col=?", (collection,))
            print(f"[{collection}] 清理旧数据 {len(old)} 条")

        row0 = cur.execute("SELECT MAX(id) FROM meta").fetchone()[0]
        cid = (row0 or 0) + 1

        files = []
        for p in glob.glob(os.path.join(docs_dir, "**", "*.md"), recursive=True):
            n = os.path.basename(p)
            if n in EXCLUDE_NAMES or n.startswith("_append_"):
                continue
            files.append(p)
        files.sort()

        for p in files:
            rel = os.path.relpath(p, docs_dir).replace("\\", "/")
            txt = open(p, encoding="utf-8", errors="ignore").read()
            title = rel
            m = re.search(r"^#\s+(.+)$", txt, re.M)
            if m:
                title = m.group(1).strip()
            cur.execute("INSERT OR REPLACE INTO doc(path,title,chars,col) VALUES(?,?,?,?)",
                        (rel, title, len(txt), collection))
            for k, (sec, body) in enumerate(chunk_doc(txt)):
                h = hashlib.sha256(body.encode("utf-8")).hexdigest()[:16]
                rows = body.count("\n|")
                cur.execute("INSERT INTO meta(id,col,path,heading,section,body,hash,tbl_rows,ord) "
                            "VALUES(?,?,?,?,?,?,?,?,?)",
                            (cid, collection, rel, title, sec, body, h, rows, k))
                # FTS v2：heading / section 原文也进索引，便于标题查询直命中（见 c-142）。
                if fts_v2:
                    cur.execute("INSERT INTO fts(rowid,path,heading,section,body) VALUES(?,?,?,?,?)",
                                (cid, rel, tok(title or ""), tok(sec or ""), tok(body)))
                else:
                    cur.execute("INSERT INTO fts(rowid,path,body) VALUES(?,?,?)",
                                (cid, rel, tok(body)))
                cid += 1
        con.commit()
        n_new = cid - ((row0 or 0) + 1)
        print(f"[{collection}] {len(files)} 篇 → {n_new} chunk，库 {os.path.getsize(db_path)/1024/1024:.2f} MB")

        # ---------- 向量（增量：只嵌缺失的，旧向量按 id 复用）----------
        if do_embed and api_key:
            try:
                import numpy as np, httpx

                # 全库 id 顺序（向量矩阵必须与 meta 对齐）
                all_ids = [r[0] for r in cur.execute("SELECT id FROM meta ORDER BY id")]
                body_of = dict(cur.execute("SELECT id, body FROM meta").fetchall())

                # 复用已有向量
                have, old_ids = {}, []
                if os.path.exists(db_path + ".vec.npy") and os.path.exists(db_path + ".vec.ids.json"):
                    try:
                        old_mat = np.load(db_path + ".vec.npy")
                        with open(db_path + ".vec.ids.json", encoding="utf-8") as _f:
                            old_ids = json.load(_f)
                        index_of = {iid: k for k, iid in enumerate(old_ids)}
                        for iid in all_ids:
                            k = index_of.get(iid)
                            if k is not None and k < len(old_mat):
                                have[iid] = old_mat[k]
                    except Exception as e:
                        print(f"  旧向量读取失败，全量重建: {type(e).__name__}")

                need = [iid for iid in all_ids if iid not in have]
                print(f"[向量] 全库 {len(all_ids)} 条，已有 {len(have)}，待嵌 {len(need)}")

                t0 = time.time()
                headers = {"Authorization": f"Bearer {api_key}", "Content-Type": "application/json"}
                fresh = {}
                if need:
                    with httpx.Client(timeout=120) as cli:
                        for i in range(0, len(need), batch):
                            grp = need[i:i + batch]
                            payload = {"model": embed_model,
                                       "input": [body_of[x][:8000] for x in grp],
                                       "encoding_format": "float"}
                            r = cli.post(embed_url, headers=headers, json=payload)
                            r.raise_for_status()
                            for iid, it in zip(grp, r.json()["data"]):
                                fresh[iid] = it["embedding"]
                            if (i // batch) % 10 == 0:
                                print(f"  嵌入 {i+len(grp)}/{len(need)}")

                if not fresh and not have:
                    raise RuntimeError("没有可用的向量")

                dim = len(next(iter(fresh.values()))) if fresh else have[all_ids[0]].shape[0]
                mat = np.zeros((len(all_ids), dim), dtype=np.float32)
                for k, iid in enumerate(all_ids):
                    v = fresh.get(iid)
                    mat[k] = np.asarray(v, dtype=np.float32) if v is not None else have[iid]
                mat /= (np.linalg.norm(mat, axis=1, keepdims=True) + 1e-9)

                np.save(db_path + ".vec.npy", mat)
                json.dump(all_ids, open(db_path + ".vec.ids.json", "w"))
                print(f"[向量] {mat.shape} 耗时 {time.time()-t0:.1f}s，"
                      f"新嵌 {len(fresh)}，复用 {len(have)}，文件 {mat.nbytes/1024/1024:.1f} MB")
            except Exception as e:
                print(f"[向量] 失败（可无向量运行）: {type(e).__name__} {e}")
    except BaseException:
        con.rollback()
        raise
    finally:
        con.close()


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    data_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "data")
    ap.add_argument("--docs", default=os.path.join(data_dir, "docs"))
    ap.add_argument("--db", default=os.path.join(data_dir, "kb.db"))
    ap.add_argument("--no-embed", action="store_true")
    ap.add_argument("--collection", default="kks")
    ap.add_argument("--append", action="store_true")
    ap.add_argument("--allow-destroy-state", action="store_true",
                    help="全量重建时允许删掉已有的用户/会话数据（先备份）")
    ap.add_argument("--api-key", default=os.environ.get("GITEE_AI_KEY", ""))
    a = ap.parse_args()
    build(a.db, a.docs, do_embed=not a.no_embed, api_key=a.api_key,
          collection=a.collection, append=a.append,
          allow_destroy_state=a.allow_destroy_state)
