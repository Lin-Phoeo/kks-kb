#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""端到端冒烟：临时目录里搭一个迷你知识库，用 TestClient 走真实路由 + 中间件。
不碰线上数据；缺依赖（fastapi/httpx/jieba/numpy/markdown/jinja2/python-multipart）时整体跳过。
运行：python -m unittest discover -s tests -v
"""
import hashlib
import html
import json
import os
import re
import secrets
import shutil
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:   # pragma: no cover
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"
EVIL = {"Origin": "http://evil.example"}

_tmp = None
_mod = None
client = None


def setUpModule():
    global _tmp, _mod, client
    if not HAVE_DEPS:
        return
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    os.makedirs(os.path.join(root, "data"))
    docs = os.path.join(root, "docs")
    os.makedirs(docs)
    with open(os.path.join(docs, "a.md"), "w", encoding="utf-8") as f:
        f.write("# 测试文档\n\n## 边缘光\n\nRimLight 参数 `_RimColor` 控制边缘光颜色。\n")
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
                   "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                            "admin_password": ADMIN_PW}}, f)
    sys.path.insert(0, os.path.join(root, "app"))
    # kb 是进程内单例：其它测试模块已缓存仓库版 kb，这里必须清掉，
    # 让临时副本里的 app/kb 真正被加载（否则 kb.config 会读到仓库缺省的 config.json）。
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    import indexer
    indexer.build(os.path.join(root, "data", "kb.db"), docs, do_embed=False, collection="kks")
    import app as mod
    _mod = mod
    client = TestClient(mod.app, follow_redirects=False)
    client.headers["Origin"] = "http://testserver"


def tearDownModule():
    if _tmp:
        try:
            _mod.KBX.con.close()
            _mod.STATE.close()
        except Exception:
            pass
        for _k in list(sys.modules):
            if _k == "app" or _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        _tmp.cleanup()


def _login(c, email=ADMIN, pw=ADMIN_PW, headers=None):
    return c.post("/login", data={"email": email, "password": pw}, headers=headers or {})


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过端到端冒烟")
class SmokeTests(unittest.TestCase):
    def setUp(self):
        global client   # 每个用例一个全新的匿名客户端，避免 cookie 串用例
        client = TestClient(_mod.app, follow_redirects=False)
        client.headers["Origin"] = "http://testserver"
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()
        _mod.CODE_SENDS_IP._d.clear()

    def authed_client(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        r = _login(c)
        self.assertEqual(r.status_code, 303, r.text)
        return c

    # ---- 默认拒绝 ----
    def test_anonymous_html_redirects_to_login(self):
        for p in ("/", "/browse", "/doc?path=a.md", "/gallery", "/manual", "/status"):
            r = client.get(p)
            self.assertEqual((r.status_code, r.headers.get("location")), (303, "/login"), p)

    def test_anonymous_api_is_401_not_data(self):
        """审计 S3：这几个接口原先完全无鉴权，其中 /api/search 每次都烧 Embedding 额度"""
        for p in ("/api/search?q=rim", "/api/docs", "/api/params", "/api/quota"):
            r = client.get(p)
            self.assertEqual(r.status_code, 401, p)
            self.assertEqual(r.json(), {"error": "unauthorized"})

    def test_anonymous_cannot_post_anything_private(self):
        for p in ("/ask_stream", "/api/feedback", "/c/1/delete", "/c/1/rename"):
            self.assertEqual(client.post(p, data={"q": "x"}).status_code, 401, p)

    def test_login_page_and_healthz_stay_public(self):
        self.assertEqual(client.get("/login").status_code, 200)
        self.assertEqual(client.get("/healthz").status_code, 200)

    def test_healthz_hides_sizes_from_proxied_requests(self):
        direct = client.get("/healthz").json()
        self.assertIn("chunks", direct)
        proxied = client.get("/healthz", headers={"X-Forwarded-For": "203.0.113.5"}).json()
        self.assertEqual(set(proxied), {"ok"})

    # ---- 登录 ----
    def test_login_success_sets_hardened_cookie(self):
        r = _login(client)
        self.assertEqual(r.status_code, 303)
        sc = r.headers["set-cookie"].lower()
        self.assertIn("httponly", sc)
        self.assertIn("samesite=lax", sc)
        self.assertNotIn("secure", sc)    # 隧道走 http，不能加 Secure 否则登不上

    def test_cookie_is_secure_behind_https_proxy(self):
        tc = TestClient(_mod.app, follow_redirects=False)
        tc.headers["Origin"] = "http://testserver"
        r = _login(tc, headers={"X-Forwarded-Proto": "https"})
        self.assertIn("secure", r.headers["set-cookie"].lower())

    def test_wrong_password_rejected(self):
        r = _login(client, pw="nope-nope-nope")
        self.assertEqual(r.status_code, 200)
        self.assertFalse(r.json()["ok"])

    def test_authed_user_gets_data(self):
        c = self.authed_client()
        self.assertEqual(c.get("/").status_code, 200)
        self.assertEqual(c.get("/browse").status_code, 200)
        r = c.get("/api/docs?q=%E6%B5%8B%E8%AF%95")
        self.assertEqual(r.status_code, 200)
        self.assertGreaterEqual(r.json()["total"], 1)
        self.assertEqual(c.get("/api/search?q=RimLight").status_code, 200)

    def test_forged_cookie_rejected(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.cookies.set("kb_session", "YWRtaW5AdGVzdC5sb2NhbDo5OTk5OTk5OTk5OmRlYWRiZWVm")
        self.assertEqual(c.get("/api/docs").status_code, 401)

    def test_old_pbkdf2_hash_is_upgraded_on_login(self):
        from kb.security import hash_password
        from kb.security import PBKDF2_ITERS
        legacy_password = secrets.token_urlsafe(18)
        old = hash_password(legacy_password, iters=120000)
        _mod.STATE.execute("INSERT OR REPLACE INTO users(email,pass,created,verified) "
                           "VALUES('legacy@test.local',?, 'x', 1)", (old,))
        _mod.STATE.commit()
        tc = TestClient(_mod.app, follow_redirects=False)
        tc.headers["Origin"] = "http://testserver"
        r = _login(tc, "legacy@test.local", legacy_password)
        self.assertEqual(r.status_code, 303)
        stored = _mod.STATE.fetchone(
            "SELECT pass FROM users WHERE email='legacy@test.local'")[0]
        self.assertEqual(int(stored.split("$")[1]), PBKDF2_ITERS)

    def test_bruteforce_is_throttled(self):
        for _ in range(8):
            _login(client, pw="bad-bad-bad-1")
        r = _login(client, pw=ADMIN_PW)   # 即使这次密码对，也被锁
        self.assertIn("尝试过多", r.json()["msg"])

    # ---- 注册关闭 ----
    def test_registration_closed(self):
        r = client.post("/register", data={"email": "new@test.local", "code": "123456",
                                           "password": "a-long-password-1"})
        self.assertEqual(r.status_code, 403)

    def test_code_flow_does_not_mail_or_create_strangers(self):
        r = client.post("/send_code", data={"email": "stranger@test.local"})
        self.assertEqual(r.json(), {"ok": True})   # 不泄露该邮箱是否存在
        n = _mod.STATE.fetchone(
            "SELECT COUNT(*) FROM codes WHERE email='stranger@test.local'")[0]
        self.assertEqual(n, 0)
        r = client.post("/login_code", data={"email": "stranger@test.local", "code": "000000"})
        self.assertFalse(r.json()["ok"])
        self.assertFalse(_mod._user_exists("stranger@test.local"))

    def test_send_code_is_rate_limited(self):
        last = None
        for i in range(12):
            last = client.post("/send_code", data={"email": f"x{i}@test.local"}).json()
        self.assertFalse(last["ok"])

    # ---- CSRF ----
    def test_cross_origin_posts_blocked(self):
        c = self.authed_client()
        self.assertEqual(c.post("/c/1/delete", headers=EVIL).status_code, 403)
        self.assertEqual(c.post("/api/feedback", data={"msg_id": 1, "vote": 1},
                                headers=EVIL).status_code, 403)
        self.assertEqual(_login(client, headers=EVIL).status_code, 403)

    def test_same_origin_post_allowed(self):
        c = self.authed_client()
        actor = _mod.STATE.fetchone('SELECT id FROM users WHERE email=?', (ADMIN,))[0]
        cid = _mod.KBX.new_conv('fixture', actor=actor)
        mid = _mod.KBX.add_msg(cid, 'assistant', 'answer', actor=actor)
        r = c.post("/api/feedback", data={"msg_id": mid, "vote": 1},
                   headers={"Origin": "http://testserver"})
        self.assertEqual(r.status_code, 200)

    # ---- 响应头 ----
    def test_security_headers_present(self):
        r = client.get("/login")
        self.assertEqual(r.headers["x-content-type-options"], "nosniff")
        self.assertEqual(r.headers["referrer-policy"], "same-origin")
        self.assertEqual(r.headers["x-frame-options"], "SAMEORIGIN")

    def test_archived_html_is_sandboxed(self):
        root = os.path.dirname(os.path.dirname(_mod.__file__))
        d = os.path.join(root, "data", "source-files")
        os.makedirs(d, exist_ok=True)
        with open(os.path.join(d, "page.html"), "w", encoding="utf-8") as f:
            f.write("<script>alert(1)</script>")
        r = self.authed_client().get("/source/file?path=page.html")
        self.assertEqual(r.status_code, 200)
        self.assertEqual(r.headers["content-security-policy"], "sandbox")

    def test_pdf_can_be_viewed_or_downloaded_from_reader(self):
        import pymupdf
        from bs4 import BeautifulSoup
        from kb.web import routes

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'source-files'
            root.mkdir()
            source = root / 'lesson.pdf'
            with pymupdf.open() as doc:
                for text in ('First page', 'Second page'):
                    doc.new_page().insert_text((30, 40), text)
                doc.save(source)
            raw = source.read_bytes()
            item = {'doc_key': _mod.KBX.doc_col('a.md') + ':a.md', 'path': source.name,
                    'role': 'original_pdf', 'sha256': hashlib.sha256(raw).hexdigest(),
                    'size_bytes': len(raw), 'original_filename': '教程.pdf'}
            (root / 'index.json').write_text(json.dumps([item]), encoding='utf8')
            with patch.object(routes, 'DATA', temp):
                c = self.authed_client()
                page = BeautifulSoup(c.get('/doc', params={'path': 'a.md'}).text, 'html.parser')
                for link, mode in (('查看原 PDF', 'inline'), ('下载原 PDF', 'attachment')):
                    href = next(a['href'] for a in page.select('.reader-original-entry a') if link in a.get_text())
                    response = c.get(href)
                    self.assertEqual(response.content, raw)
                    self.assertTrue(response.headers['Content-Disposition'].startswith(mode + ';'))
                    self.assertEqual(response.headers['content-type'], 'application/pdf')

    def test_forum_reader_falls_back_when_either_source_changes(self):
        from kb.web import routes
        from kb.content import doc_metadata

        with tempfile.TemporaryDirectory() as temp, patch.object(routes, 'DATA', temp), \
                patch.object(doc_metadata, '_VIEW_CACHE', {'mtime': None, 'data': {}}):
            root = Path(temp) / 'source-files'
            root.mkdir()
            source = root / 'thread.html'
            source.write_bytes(b'<article>archive</article>')
            key = _mod.KBX.doc_col('a.md') + ':a.md'
            original = hashlib.sha256(source.read_bytes()).hexdigest()
            record = {'doc_key': key, 'path': source.name, 'role': 'original_file',
                      'size_bytes': source.stat().st_size, 'sha256': original}
            (root / 'index.json').write_text(json.dumps([record]), encoding='utf8')
            content = '<h2>论坛样板正文</h2>'
            view = {'kind': 'discuz_post_flow', 'enabled': True, 'html': content,
                    'revision': hashlib.sha256(content.encode()).hexdigest(), 'source_sha256': original,
                    'indexed_body_sha256': hashlib.sha256(_mod.KBX.doc_body('a.md').encode()).hexdigest()}
            views_file = Path(temp) / 'doc_views.json'
            views_file.write_text(json.dumps({'documents': {key: view}}), encoding='utf8')
            c = self.authed_client()
            def render():
                return c.get('/doc', params={'path': 'a.md'}).text
            self.assertIn('论坛样板正文', render())
            with patch.object(_mod.KBX, 'doc_body', return_value='changed index'):
                self.assertNotIn('论坛样板正文', render())
            source.write_bytes(b'<article>changed</article>')
            self.assertNotIn('论坛样板正文', render())

    def test_doc_semantic_view_requires_matching_original_and_exposes_anchor_map(self):
        from kb.content.reading_view import prepare_reading_html

        client = self.authed_client()
        path = "a.md"
        body = _mod.KBX.doc_body(path)
        app_root = os.path.dirname(os.path.dirname(_mod.__file__))
        data_dir = os.path.join(app_root, "data")
        source_root = os.path.join(data_dir, "source-files")
        original_rel = "originals/tutorial.docx"
        original_path = os.path.join(source_root, original_rel)
        os.makedirs(os.path.dirname(original_path), exist_ok=True)
        original = b"isolated source bytes"
        with open(original_path, "wb") as f:
            f.write(original)

        raw_html = '<h2 id="_Toc123">Semantic section</h2><p id="note">A note</p>'
        projection = prepare_reading_html(raw_html, _mod.KBX.doc_col(path), path)
        reading_html = projection['html']  # The offline builder stores already-namespaced HTML.
        heading_id = projection['toc'][0]['anchor']
        archived_map = {**projection['anchor_map'], 'stale': 'not-in-this-body',
                        heading_id: projection['anchor_map']['note']}
        doc_key = f"{_mod.KBX.doc_col(path)}:{path}"
        with open(os.path.join(data_dir, "doc_views.json"), "w", encoding="utf-8") as f:
            json.dump({"documents": {doc_key: {
                "html": reading_html, "anchor_map": archived_map,
                "kind": "word_semantic_flow",
                "enabled": True,
                "revision": hashlib.sha256(reading_html.encode()).hexdigest(),
                "indexed_body_sha256": hashlib.sha256(body.encode()).hexdigest(),
                "source_sha256": hashlib.sha256(original).hexdigest(),
                "checks": {"full_text_order_equal": True, "images_in_source_order": True},
            }}}, f)
        with open(os.path.join(source_root, "index.json"), "w", encoding="utf-8") as f:
            json.dump([{
                "doc_key": doc_key, "role": "original_docx", "path": original_rel,
                "size_bytes": len(original), "sha256": hashlib.sha256(original).hexdigest(),
                "original_filename": "教程.docx",
            }], f)

        response = client.get("/doc", params={"path": path, "sec": "_Toc123"})
        self.assertEqual(response.status_code, 200)
        self.assertIn('data-semantic-reader="true"', response.text)
        self.assertIn("下载原 Word", response.text)
        anchor_attr = re.search(r'data-anchor-map="([^"]*)"', response.text)
        self.assertIsNotNone(anchor_attr)
        actual_map = json.loads(html.unescape(anchor_attr.group(1)))
        self.assertEqual(actual_map.get('_Toc123'), heading_id)
        self.assertIn(f'<h2 id="{heading_id}">Semantic section</h2>', response.text)
        self.assertEqual(actual_map.get(heading_id, heading_id), heading_id)
        self.assertNotIn('stale', actual_map)
        source_link = re.search(r'<a href="([^"]+)" class="reader-tool">下载原 Word', response.text)
        self.assertIsNotNone(source_link)
        download = client.get(source_link.group(1))
        self.assertEqual(download.status_code, 200)
        self.assertEqual(download.content, original)
        from urllib.parse import unquote
        self.assertIn("filename*=UTF-8''教程.docx", unquote(download.headers['content-disposition']))
        self.assertTrue(download.headers['content-disposition'].startswith('attachment;'))

        def assert_legacy():
            result = client.get('/doc', params={'path': path})
            self.assertEqual(result.status_code, 200)
            self.assertIn('data-semantic-reader="false"', result.text)
            self.assertIn('RimLight', result.context['body_html'])
            self.assertNotIn('Semantic section', result.context['body_html'])

        view_file = os.path.join(data_dir, 'doc_views.json')
        index_file = os.path.join(source_root, 'index.json')
        for sidecar in (view_file, index_file):
            os.rename(sidecar, sidecar + '.held')
            try:
                assert_legacy()
            finally:
                os.rename(sidecar + '.held', sidecar)

        for rel, field in (('doc_views.json', 'source_sha256'), ('source-files/index.json', 'sha256')):
            filename = os.path.join(data_dir, rel)
            with open(filename, encoding='utf-8') as f:
                records = json.load(f)
            (records[0] if isinstance(records, list) else records['documents'][doc_key]).pop(field)
            with open(filename, 'w', encoding='utf-8') as f:
                json.dump(records, f)
        assert_legacy()

        os.unlink(index_file)
        os.unlink(view_file)

    def test_path_traversal_still_blocked(self):
        c = self.authed_client()
        self.assertIn(c.get("/source/file?path=../config.json").status_code, (400, 404))

    # ---- 路由表 ----
    def test_no_duplicate_routes_and_no_unprotected_new_routes(self):
        from kb.security import PUBLIC_PATHS
        seen = {}
        for r in _mod.app.routes:
            for m in getattr(r, "methods", None) or ():
                key = (m, r.path)
                self.assertNotIn(key, seen, f"重复路由 {key}")
                seen[key] = True
        for (_, path) in seen:
            if path in PUBLIC_PATHS or path == "/openapi.json":
                continue
            # 凡不在放行表里的路径，匿名请求都必须被挡
            probe = path.replace("{cid}", "1").replace("{cat}", "c01").replace("{fn}", "x.jpg")
            r = client.get(probe)
            self.assertIn(r.status_code, (303, 401, 405), f"{path} 匿名可达: {r.status_code}")


if __name__ == "__main__":
    unittest.main()
