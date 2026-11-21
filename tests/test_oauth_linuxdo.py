#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Linux.do OAuth2 后端 + 路由集成测试。

- 纯逻辑：is_enabled / build_authorize_url / exchange_code / fetch_userinfo / resolve_email
- 端到端：/oauth/linuxdo/start / /oauth/linuxdo/callback 两条路由

端到端分支复用 test_turnstile 的 tempdir 模式：独开一份启用了 linuxdo 的 config。
缺依赖整体跳过（与 test_turnstile 同一套 HAVE_DEPS 门）。
"""
import json
import os
import shutil
import sys
import tempfile
import unittest
from unittest.mock import MagicMock, patch
from urllib.parse import parse_qs, urlparse

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

sys.path.insert(0, SRC_APP)
from kb import oauth_linuxdo  # noqa: E402


CFG_FULL = {
    "client_id": "cid-xyz",
    "client_secret": "secret-xyz",
    "authorize_url": "https://connect.linux.do/oauth2/authorize",
    "token_url": "https://connect.linux.do/oauth2/token",
    "userinfo_url": "https://connect.linux.do/api/user",
    "scope": "user",
}


# ---------------- 纯逻辑 ----------------

class IsEnabledTests(unittest.TestCase):
    def test_empty_dict_is_disabled(self):
        self.assertFalse(oauth_linuxdo.is_enabled({}))

    def test_none_is_disabled(self):
        self.assertFalse(oauth_linuxdo.is_enabled(None))

    def test_only_client_id_is_disabled(self):
        self.assertFalse(oauth_linuxdo.is_enabled({"client_id": "x", "client_secret": ""}))

    def test_only_client_secret_is_disabled(self):
        self.assertFalse(oauth_linuxdo.is_enabled({"client_id": "", "client_secret": "x"}))

    def test_whitespace_only_credentials_are_disabled(self):
        self.assertFalse(oauth_linuxdo.is_enabled({"client_id": "   ", "client_secret": "   "}))

    def test_both_present_is_enabled(self):
        self.assertTrue(oauth_linuxdo.is_enabled(CFG_FULL))


class BuildAuthorizeUrlTests(unittest.TestCase):
    def test_contains_all_required_params(self):
        url = oauth_linuxdo.build_authorize_url(
            CFG_FULL, "state-abc", "https://kb.example.test/oauth/linuxdo/callback")
        u = urlparse(url)
        self.assertEqual(f"{u.scheme}://{u.netloc}{u.path}", CFG_FULL["authorize_url"])
        q = parse_qs(u.query)
        self.assertEqual(q["response_type"], ["code"])
        self.assertEqual(q["client_id"], ["cid-xyz"])
        self.assertEqual(q["state"], ["state-abc"])
        self.assertEqual(q["redirect_uri"],
                         ["https://kb.example.test/oauth/linuxdo/callback"])
        self.assertEqual(q["scope"], ["user"])

    def test_scope_defaults_when_cfg_missing_scope(self):
        cfg = dict(CFG_FULL)
        cfg.pop("scope")
        url = oauth_linuxdo.build_authorize_url(cfg, "s", "https://x/cb")
        self.assertEqual(parse_qs(urlparse(url).query)["scope"], ["user"])


def _mock_sync_client(method_attr, response_json=None, raise_exc=None, capture=None):
    """造一个 httpx.Client 的替身：post/get 返回假 response，或抛异常。capture 收 kwargs。"""
    mock_cm = MagicMock()
    mock_cli = MagicMock()

    def _call(*args, **kwargs):
        if capture is not None:
            capture["args"] = args
            capture["kwargs"] = kwargs
        if raise_exc is not None:
            raise raise_exc
        resp = MagicMock()
        resp.raise_for_status.return_value = None
        resp.json.return_value = response_json or {}
        return resp

    setattr(mock_cli, method_attr, MagicMock(side_effect=_call))
    mock_cm.__enter__ = MagicMock(return_value=mock_cli)
    mock_cm.__exit__ = MagicMock(return_value=False)
    return MagicMock(return_value=mock_cm)


class ExchangeCodeTests(unittest.TestCase):
    def test_posts_and_returns_json(self):
        captured = {}
        fake = _mock_sync_client("post",
                                 response_json={"access_token": "tok-123",
                                                "token_type": "Bearer"},
                                 capture=captured)
        with patch("kb.oauth_linuxdo.httpx.Client", fake):
            out = oauth_linuxdo.exchange_code(
                CFG_FULL, "code-abc", "https://kb.example.test/oauth/linuxdo/callback")
        self.assertEqual(out["access_token"], "tok-123")
        # POST 发往 token_url
        self.assertEqual(captured["args"][0], CFG_FULL["token_url"])
        data = captured["kwargs"]["data"]
        self.assertEqual(data["grant_type"], "authorization_code")
        self.assertEqual(data["code"], "code-abc")
        self.assertEqual(data["client_id"], "cid-xyz")
        self.assertEqual(data["client_secret"], "secret-xyz")
        self.assertEqual(data["redirect_uri"],
                         "https://kb.example.test/oauth/linuxdo/callback")


class FetchUserinfoTests(unittest.TestCase):
    def test_bearer_auth_and_returns_dict(self):
        captured = {}
        info = {"id": 42, "sub": "42", "username": "foo",
                "email": "foo@example.com", "trust_level": 2}
        fake = _mock_sync_client("get", response_json=info, capture=captured)
        with patch("kb.oauth_linuxdo.httpx.Client", fake):
            out = oauth_linuxdo.fetch_userinfo(CFG_FULL, "tok-123")
        self.assertEqual(out, info)
        self.assertEqual(captured["args"][0], CFG_FULL["userinfo_url"])
        self.assertEqual(captured["kwargs"]["headers"]["Authorization"], "Bearer tok-123")


class ResolveEmailTests(unittest.TestCase):
    def test_prefers_real_email(self):
        self.assertEqual(
            oauth_linuxdo.resolve_email({"id": 1, "sub": "1", "email": "Foo@Example.com"}),
            "foo@example.com")

    def test_falls_back_to_synthetic_email_on_empty(self):
        self.assertEqual(
            oauth_linuxdo.resolve_email({"id": 42, "sub": "42", "email": ""}),
            "linuxdo-42@users.linux.do")

    def test_falls_back_when_email_missing(self):
        self.assertEqual(oauth_linuxdo.resolve_email({"sub": "99"}),
                         "linuxdo-99@users.linux.do")


# ---------------- 路由集成 ----------------
try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"

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
    with open(os.path.join(docs, "a.md"), "w", encoding="utf-8") as f:
        f.write("# T\n\n## S\n\nhello.\n")
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({
            "llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
            "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                     "admin_password": ADMIN_PW, "open_registration": False},
            "oauth": {"linuxdo": CFG_FULL},
            "security": {"allowed_origins": []},
        }, f)
    sys.path.insert(0, os.path.join(root, "app"))
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    import indexer
    indexer.build(os.path.join(root, "data", "kb.db"), docs, do_embed=False, collection="kks")
    import app as mod
    _mod = mod


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


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过端到端")
class OAuthLinuxDoStartTests(unittest.TestCase):
    def setUp(self):
        self.c = TestClient(_mod.app, follow_redirects=False)
        self.c.headers["Origin"] = "http://testserver"

    def test_start_redirects_to_connect_linux_do_and_sets_state_cookie(self):
        r = self.c.get("/oauth/linuxdo/start")
        self.assertEqual(r.status_code, 302, r.text)
        loc = r.headers["location"]
        self.assertTrue(loc.startswith("https://connect.linux.do/oauth2/authorize"), loc)
        q = parse_qs(urlparse(loc).query)
        self.assertEqual(q["client_id"], ["cid-xyz"])
        self.assertEqual(q["response_type"], ["code"])
        self.assertIn("state", q)
        self.assertEqual(q["redirect_uri"],
                         ["http://testserver/oauth/linuxdo/callback"])
        state_from_url = q["state"][0]
        # kb_oauth_state cookie 必须跟 URL 里的 state 一致
        self.assertEqual(r.cookies.get("kb_oauth_state"), state_from_url)

    def test_start_404_when_credentials_empty(self):
        with patch("kb.web.routes.LINUXDO_CFG", {}):
            r = self.c.get("/oauth/linuxdo/start")
        self.assertEqual(r.status_code, 404)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过端到端")
class OAuthLinuxDoCallbackTests(unittest.TestCase):
    def setUp(self):
        self.c = TestClient(_mod.app, follow_redirects=False)
        self.c.headers["Origin"] = "http://testserver"
        # 清掉可能遗留的 OAuth 新用户，让 happy path 走 INSERT 分支
        try:
            _mod.STATE.execute("DELETE FROM oauth_identities")
            _mod.STATE.execute("DELETE FROM users WHERE email LIKE 'linuxdo-%@users.linux.do'")
            _mod.STATE.execute("DELETE FROM users WHERE email='foo@example.com'")
            _mod.STATE.commit()
        except Exception:
            pass

    def test_state_mismatch_returns_400(self):
        self.c.cookies.set("kb_oauth_state", "A" * 22)
        r = self.c.get("/oauth/linuxdo/callback?code=x&state=" + "B" * 22)
        self.assertEqual(r.status_code, 400)
        self.assertIn("state", r.text)

    def test_missing_cookie_returns_400(self):
        r = self.c.get("/oauth/linuxdo/callback?code=x&state=anything")
        self.assertEqual(r.status_code, 400)

    def test_404_when_feature_disabled(self):
        with patch("kb.web.routes.LINUXDO_CFG", {}):
            r = self.c.get("/oauth/linuxdo/callback?code=x&state=y")
        self.assertEqual(r.status_code, 404)

    def test_token_failure_redirects_to_login_with_err(self):
        self.c.cookies.set("kb_oauth_state", "state-xyz")
        with patch("kb.web.routes.oauth_linuxdo.exchange_code",
                   side_effect=RuntimeError("boom")):
            r = self.c.get("/oauth/linuxdo/callback?code=c&state=state-xyz")
        self.assertEqual(r.status_code, 303)
        self.assertIn("err=linuxdo_token", r.headers["location"])

    def test_happy_path_creates_user_and_issues_session(self):
        self.c.cookies.set("kb_oauth_state", "state-xyz")
        info = {"id": 42, "sub": "42", "username": "foo",
                "email": "foo@example.com", "trust_level": 2, "active": True}
        with patch("kb.web.routes.oauth_linuxdo.exchange_code",
                   return_value={"access_token": "tok-123"}), \
             patch("kb.web.routes.oauth_linuxdo.fetch_userinfo",
                   return_value=info):
            r = self.c.get("/oauth/linuxdo/callback?code=c&state=state-xyz")
        self.assertEqual(r.status_code, 303, r.text)
        self.assertEqual(r.headers["location"], "/")
        # session cookie 已下发
        self.assertIn("kb_session", r.cookies)
        # DB 里应该已经创建了 oauth 用户
        row = _mod.STATE.fetchone(
            "SELECT u.pass FROM users u JOIN oauth_identities o ON o.user_id=u.id WHERE o.provider=? AND o.sub=?", ("linuxdo", "42"))
        self.assertIsNotNone(row)
        self.assertEqual(row[0], "oauth:linuxdo")

    def test_happy_path_without_email_uses_synthetic_address(self):
        self.c.cookies.set("kb_oauth_state", "state-xyz")
        info = {"id": 777, "sub": "777", "username": "noemail", "email": ""}
        with patch("kb.web.routes.oauth_linuxdo.exchange_code",
                   return_value={"access_token": "tok-xyz"}), \
             patch("kb.web.routes.oauth_linuxdo.fetch_userinfo",
                   return_value=info):
            r = self.c.get("/oauth/linuxdo/callback?code=c&state=state-xyz")
        self.assertEqual(r.status_code, 303, r.text)
        row = _mod.STATE.fetchone(
            "SELECT u.pass FROM users u JOIN oauth_identities o ON o.user_id=u.id WHERE o.provider=? AND o.sub=?",
            ("linuxdo", "777"))
        self.assertIsNotNone(row)


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过模板")
class LoginTemplateButtonTests(unittest.TestCase):
    def test_login_page_renders_linuxdo_button_when_enabled(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        html = c.get("/login").text
        self.assertIn("/oauth/linuxdo/start", html)
        self.assertIn("使用 Linux.do 登录", html)
        self.assertNotIn("第三方登录 <span", html)


if __name__ == "__main__":
    unittest.main()
