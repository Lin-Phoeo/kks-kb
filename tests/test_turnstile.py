#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Cloudflare Turnstile 后端校验 + CF 真实 IP 识别。

- verify_turnstile 的四条分支（disabled / success / failure / network-error）
- real_client_ip 对不可信/可信 peer 的处理
- login POST 的 Turnstile 门：缺 token → 失败消息，成功时让密码错误原样抛出

端到端分支复用 test_app_smoke 的 tempdir 搭档，但独立开自己的 config（启用 Turnstile）。
缺依赖整体跳过（和 test_app_smoke 同一套 HAVE_DEPS 门）。
"""
import asyncio
import json
import os
import shutil
import sys
import tempfile
import unittest
from unittest.mock import AsyncMock, MagicMock, patch

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

sys.path.insert(0, SRC_APP)
from kb.security_turnstile import verify_turnstile  # noqa: E402
from kb import security as sec  # noqa: E402


def run_async(coro):
    return asyncio.new_event_loop().run_until_complete(coro)


def _mock_async_client(response_json=None, raise_exc=None):
    """构造一个假的 httpx.AsyncClient：post 返回带 json() 的 response，或抛 raise_exc。"""
    mock_cm = MagicMock()
    mock_cli = MagicMock()
    if raise_exc is not None:
        mock_cli.post = AsyncMock(side_effect=raise_exc)
    else:
        resp = MagicMock()
        resp.raise_for_status.return_value = None
        resp.json.return_value = response_json or {}
        mock_cli.post = AsyncMock(return_value=resp)
    mock_cm.__aenter__ = AsyncMock(return_value=mock_cli)
    mock_cm.__aexit__ = AsyncMock(return_value=False)
    return MagicMock(return_value=mock_cm)


class VerifyTurnstileTests(unittest.TestCase):
    def test_empty_secret_disables_feature(self):
        # 显式空 secret
        self.assertEqual(run_async(verify_turnstile("", "", secret="")),
                         (True, "disabled"))
        # 空白 secret 同样当作未配置
        self.assertEqual(run_async(verify_turnstile("", "", secret="   ")),
                         (True, "disabled"))

    def test_valid_token_success(self):
        with patch("kb.security_turnstile.httpx.AsyncClient",
                   _mock_async_client({"success": True})):
            self.assertEqual(run_async(verify_turnstile("x", secret="s")), (True, ""))

    def test_invalid_token_returns_error_code(self):
        with patch("kb.security_turnstile.httpx.AsyncClient",
                   _mock_async_client({"success": False,
                                       "error-codes": ["invalid-input-response"]})):
            self.assertEqual(run_async(verify_turnstile("x", secret="s")),
                             (False, "invalid-input-response"))

    def test_timeout_returns_network_error(self):
        import httpx as _httpx
        with patch("kb.security_turnstile.httpx.AsyncClient",
                   _mock_async_client(raise_exc=_httpx.TimeoutException("boom"))):
            self.assertEqual(run_async(verify_turnstile("x", secret="s")),
                             (False, "network-error"))

    def test_generic_exception_also_network_error(self):
        # 任意异常都归类为 network-error，上层只需关心 ok=False
        with patch("kb.security_turnstile.httpx.AsyncClient",
                   _mock_async_client(raise_exc=RuntimeError("boom"))):
            self.assertEqual(run_async(verify_turnstile("x", secret="s")),
                             (False, "network-error"))

    def test_missing_error_codes_falls_back_to_unknown(self):
        with patch("kb.security_turnstile.httpx.AsyncClient",
                   _mock_async_client({"success": False})):
            self.assertEqual(run_async(verify_turnstile("x", secret="s")),
                             (False, "unknown"))


class RealClientIpTests(unittest.TestCase):
    CF = ["162.158.0.0/15", "104.16.0.0/13"]

    def _req(self, peer, headers=None):
        r = MagicMock()
        r.client = MagicMock()
        r.client.host = peer
        r.headers = headers or {}
        return r

    def test_untrusted_peer_ignores_cf_header(self):
        """socket peer 不在 CF 段 → 不信 CF-Connecting-IP"""
        req = self._req("1.2.3.4", {"cf-connecting-ip": "5.6.7.8"})
        self.assertEqual(sec.real_client_ip(req, self.CF), "1.2.3.4")

    def test_cf_peer_uses_cf_connecting_ip(self):
        """socket peer 在 CF 段 → 用 CF-Connecting-IP"""
        req = self._req("162.158.1.1", {"cf-connecting-ip": "5.6.7.8"})
        self.assertEqual(sec.real_client_ip(req, self.CF), "5.6.7.8")

    def test_cf_peer_falls_back_to_xff_first_hop(self):
        """没有 CF 头时取 XFF 第一跳（客户端那一端）"""
        req = self._req("104.17.0.1", {"x-forwarded-for": "7.7.7.7, 8.8.8.8"})
        self.assertEqual(sec.real_client_ip(req, self.CF), "7.7.7.7")

    def test_no_trusted_proxies_returns_peer(self):
        req = self._req("162.158.1.1", {"cf-connecting-ip": "5.6.7.8"})
        self.assertEqual(sec.real_client_ip(req, []), "162.158.1.1")

    def test_no_client_returns_empty(self):
        r = MagicMock()
        r.client = None
        r.headers = {}
        self.assertEqual(sec.real_client_ip(r, self.CF), "")


class OriginAllowListTests(unittest.TestCase):
    H = "127.0.0.1:3000"

    def test_allowed_origin_passes_cross_host(self):
        self.assertTrue(sec.origin_ok("POST", "https://kb.example.test", "", self.H,
                                      allowed_origins=["https://kb.example.test"]))

    def test_disallowed_origin_still_blocked(self):
        self.assertFalse(sec.origin_ok("POST", "https://evil.test", "", self.H,
                                       allowed_origins=["https://kb.example.test"]))

    def test_empty_allow_list_preserves_same_host_only(self):
        self.assertTrue(sec.origin_ok("POST", "http://127.0.0.1:3000", "", self.H,
                                      allowed_origins=[]))
        self.assertFalse(sec.origin_ok("POST", "http://evil.test", "", self.H,
                                       allowed_origins=[]))

    def test_allow_list_matches_referer_too(self):
        self.assertTrue(sec.origin_ok("POST", "", "https://kb.example.test/login",
                                      self.H, allowed_origins=["https://kb.example.test"]))


# ---------------- 端到端：/login + Turnstile ----------------
try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"
TURN_SECRET = "test-turnstile-secret"

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
                     "admin_password": ADMIN_PW, "open_registration": True},
            "cloudflare": {"turnstile_secret": TURN_SECRET, "turnstile_sitekey": "sk",
                           "trusted_proxies": ["162.158.0.0/15"]},
            "security": {"allowed_origins": ["https://kb.example.test"]},
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
class LoginTurnstileGateTests(unittest.TestCase):
    def setUp(self):
        from fastapi.testclient import TestClient
        self.c = TestClient(_mod.app, follow_redirects=False)
        self.c.headers["Origin"] = "http://testserver"
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()

    def test_missing_token_rejected_with_flash_message(self):
        """开启 Turnstile 后，缺 cf-turnstile-response → 失败且提示 '人机校验未通过'。
        仍然是 200+JSON（契合现有 fetch 表单），不是真跳转。"""
        with patch("kb.web.routes.verify_turnstile",
                   AsyncMock(return_value=(False, "missing-input-response"))):
            r = self.c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})
        self.assertEqual(r.status_code, 200)
        body = r.json()
        self.assertFalse(body["ok"])
        self.assertIn("人机校验未通过", body["msg"])

    def test_turnstile_pass_then_wrong_password_shows_password_error(self):
        """Turnstile 先过关 → 后续走密码校验：错密码应报密码错，而不是 Turnstile 错。"""
        with patch("kb.web.routes.verify_turnstile",
                   AsyncMock(return_value=(True, ""))):
            r = self.c.post("/login", data={"email": ADMIN, "password": 'incorrect-' + 'fixture'})
        self.assertEqual(r.status_code, 200)
        body = r.json()
        self.assertFalse(body["ok"])
        self.assertIn("邮箱或密码", body["msg"])
        self.assertNotIn("人机校验", body["msg"])

    def test_turnstile_failure_does_not_count_toward_lockout(self):
        """Turnstile 失败不写入 LOGIN_FAILS_ACCT / IP：否则一个爬虫就能把人锁住。"""
        with patch("kb.web.routes.verify_turnstile",
                   AsyncMock(return_value=(False, "invalid-input-response"))):
            for _ in range(20):
                self.c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})
        # 现在用正确密码 + 过关的 Turnstile，必须能登录（没被锁）
        with patch("kb.web.routes.verify_turnstile",
                   AsyncMock(return_value=(True, ""))):
            r = self.c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})
        self.assertEqual(r.status_code, 303, r.text)

    def test_turnstile_form_field_name_cf_turnstile_response(self):
        """表单字段名必须是 Cloudflare 规定的 cf-turnstile-response（带连字符）。"""
        captured = {}

        async def spy(token, ip, secret):
            captured["token"] = token
            return True, ""

        with patch("kb.web.routes.verify_turnstile", side_effect=spy):
            self.c.post("/login", data={"email": ADMIN, "password": ADMIN_PW,
                                        "cf-turnstile-response": "tok-abc"})
        self.assertEqual(captured.get("token"), "tok-abc")


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过端到端")
class JinjaContextTests(unittest.TestCase):
    def test_sitekey_rendered_into_login(self):
        from fastapi.testclient import TestClient
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        html = c.get("/login").text
        # Jinja 全局 cf_turnstile_sitekey 已经注入；配置里值为 "sk"
        self.assertIn('data-sitekey="sk"', html)
        self.assertIn("challenges.cloudflare.com/turnstile/v0/api.js", html)

    def test_sitekey_rendered_into_forgot(self):
        from fastapi.testclient import TestClient
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        html = c.get("/forgot").text
        self.assertIn('data-sitekey="sk"', html)


if __name__ == "__main__":
    unittest.main()
