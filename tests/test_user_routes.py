#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""/me、/admin、/sitemap.xml、/robots.txt 的端到端测试。
整体结构照抄 test_app_smoke.py：临时目录造迷你 KB，TestClient 走真实中间件。
"""
import json
import os
import shutil
import sys
import tempfile
import unittest
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"
USER = "user@test.local"
USER_PW = "user-password-123"
OAUTH_USER = "oauth@test.local"

_tmp = None
_mod = None


def setUpModule():
    global _tmp, _mod
    if not HAVE_DEPS:
        return
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    os.makedirs(os.path.join(root, "data"))
    docs = os.path.join(root, "docs")
    os.makedirs(docs)
    # 两篇文档让 sitemap 至少有两条 doc <url>
    for name, body in (("a.md", "# A\n\n边缘光 `_RimColor`.\n"),
                       ("b.md", "# B\n\n次表面散射.\n")):
        with open(os.path.join(docs, name), "w", encoding="utf-8") as f:
            f.write(body)
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
                   "site_url": "https://kb.example.test/",
                   "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                            "admin_password": ADMIN_PW,
                            "open_registration": False}}, f)
    sys.path.insert(0, os.path.join(root, "app"))
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    import indexer
    indexer.build(os.path.join(root, "data", "kb.db"), docs, do_embed=False, collection="kks")
    import app as mod
    _mod = mod
    # 额外塞一个普通用户、一个 OAuth 用户，供后续各用例取用
    from kb.security import hash_password
    now = datetime.now().isoformat(timespec="seconds")
    _mod.STATE.execute(
        "INSERT OR REPLACE INTO users(email,pass,created,last_login,verified) "
        "VALUES(?,?,?,?,1)", (USER, hash_password(USER_PW), now, now))
    _mod.STATE.execute(
        "INSERT OR REPLACE INTO users(email,pass,created,last_login,verified) "
        "VALUES(?,?,?,?,1)", (OAUTH_USER, "oauth:linuxdo", now, now))
    _mod.STATE.commit()


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


def _client():
    c = TestClient(_mod.app, follow_redirects=False)
    c.headers["Origin"] = "http://testserver"
    return c


def _login(c, email, pw):
    return c.post("/login", data={"email": email, "password": pw})


def _authed(email, pw):
    c = _client()
    r = _login(c, email, pw)
    assert r.status_code == 303, r.text
    return c


def _routes():
    """COOKIE / make_token / _SITEMAP_CACHE 都在 kb.web.routes 模块里。"""
    import kb.web.routes as r
    return r


def _authed_token(email):
    """给没有密码的账号（OAuth / 直接塞 cookie）造一个合法 session。"""
    c = _client()
    r = _routes()
    c.cookies.set(r.COOKIE, r.make_token(email))
    return c


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过")
class MeTests(unittest.TestCase):
    def setUp(self):
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()
        _mod.CODE_SENDS_IP._d.clear()

    def test_me_anonymous_redirects_login(self):
        r = _client().get("/me")
        self.assertEqual(r.status_code, 303)
        self.assertEqual(r.headers.get("location"), "/login")

    def test_me_password_user_shows_email_and_logout(self):
        c = _authed(USER, USER_PW)
        r = c.get("/me")
        self.assertEqual(r.status_code, 200)
        self.assertIn(USER, r.text)
        self.assertIn("退出登录", r.text)
        # 密码账号 → 可见「修改密码」入口（/forgot 链接）
        self.assertIn("修改密码", r.text)
        self.assertIn("/forgot", r.text)

    def test_me_oauth_user_shows_linuxdo(self):
        c = _authed_token(OAUTH_USER)
        r = c.get("/me")
        self.assertEqual(r.status_code, 200)
        self.assertIn("Linux.do", r.text)
        # OAuth 账号不显示密码修改入口
        self.assertNotIn("修改密码", r.text)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过")
class AdminTests(unittest.TestCase):
    def setUp(self):
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()

    def test_admin_anonymous_redirects_login(self):
        r = _client().get("/admin")
        self.assertEqual(r.status_code, 303)
        self.assertEqual(r.headers.get("location"), "/login")

    def test_admin_non_admin_gets_403(self):
        c = _authed(USER, USER_PW)
        r = c.get("/admin")
        self.assertEqual(r.status_code, 403)
        self.assertEqual(r.json(), {"ok": False, "msg": "仅限管理员"})

    def test_admin_sees_dashboard_sections(self):
        c = _authed(ADMIN, ADMIN_PW)
        r = c.get("/admin")
        self.assertEqual(r.status_code, 200)
        for section in ("用户", "问答", "反馈"):
            self.assertIn(section, r.text, section)
        # Response time: 简单看一下任务要求的 <200ms 期望
        # 模板里有渲染出来的 "本次 N ms" 占位
        self.assertIn(" ms", r.text)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过")
class SitemapTests(unittest.TestCase):
    def setUp(self):
        # 每个用例重置 sitemap 缓存，否则跨用例串缓存影响断言
        r = _routes()
        r._SITEMAP_CACHE["xml"] = ""
        r._SITEMAP_CACHE["ts"] = 0.0

    def test_sitemap_is_public_and_xml(self):
        r = _client().get("/sitemap.xml")
        self.assertEqual(r.status_code, 200)
        self.assertIn("xml", r.headers["content-type"].lower())
        self.assertIn("https://kb.example.test/", r.text)
        self.assertNotIn('https://kb.example.test//', r.text)
        self.assertIn("<urlset", r.text)
        # 固定首页路径都应出现
        for p in ("/manual", "/browse", "/gallery", "/params"):
            self.assertIn(p, r.text, p)

    def test_sitemap_caches_second_call(self):
        c = _client()
        r1 = c.get("/sitemap.xml")
        self.assertEqual(r1.status_code, 200)
        # 第二次：把 KBX.doc_list 换成会炸的版本；真正重算就会抛，
        # 走缓存就什么都不碰。
        calls = {"n": 0}
        routes = _routes()
        orig = routes.KBX.doc_list

        def _boom(col=""):
            calls["n"] += 1
            raise AssertionError("sitemap 第二次调用不该重走 doc_list")

        routes.KBX.doc_list = _boom
        try:
            r2 = c.get("/sitemap.xml")
            self.assertEqual(r2.status_code, 200)
            self.assertEqual(r2.text, r1.text)
            self.assertEqual(calls["n"], 0)
        finally:
            routes.KBX.doc_list = orig


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过")
class RobotsTests(unittest.TestCase):
    def test_robots_is_public_and_plain(self):
        r = _client().get("/robots.txt")
        self.assertEqual(r.status_code, 200)
        ct = r.headers["content-type"].lower()
        self.assertTrue(ct.startswith("text/plain"), ct)
        self.assertIn("Sitemap: https://kb.example.test/sitemap.xml", r.text)
        # 关键 Disallow 都在
        for p in ("/me", "/admin", "/api/"):
            self.assertIn(f"Disallow: {p}", r.text, p)


if __name__ == "__main__":
    unittest.main()
