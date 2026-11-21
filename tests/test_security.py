#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""app/security.py 单测。只依赖标准库：python -m unittest discover -s tests -v"""
import base64
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "app"))
from kb import security as sec  # noqa: E402


class PasswordTests(unittest.TestCase):
    def test_roundtrip(self):
        h = sec.hash_password("correct horse battery")
        self.assertEqual(sec.verify_password("correct horse battery", h), (True, False))
        self.assertEqual(sec.verify_password("wrong", h), (False, False))

    def test_old_hash_still_verifies_and_asks_for_rehash(self):
        old = sec.hash_password("hunter2hunter2", iters=120000)   # 线上现存的旧格式
        ok, rehash = sec.verify_password("hunter2hunter2", old)
        self.assertTrue(ok)
        self.assertTrue(rehash)

    def test_wrong_password_never_requests_rehash(self):
        old = sec.hash_password("hunter2hunter2", iters=120000)
        self.assertEqual(sec.verify_password("nope", old), (False, False))

    def test_garbage_stored_value(self):
        for bad in ("", "x", "pbkdf2$a$b", "pbkdf2$abc$salt$hex"):
            self.assertEqual(sec.verify_password("x", bad), (False, False))

    def test_empty_stored_for_code_only_accounts(self):
        # login_code 建的账号 pass 为空串，密码登录必须失败而不是抛异常
        self.assertEqual(sec.verify_password("", ""), (False, False))


class TokenTests(unittest.TestCase):
    def setUp(self):
        self.s = sec.TokenSigner("k" * 32)

    def test_roundtrip(self):
        self.assertEqual(self.s.check(self.s.make("a@b.c")), "a@b.c")

    def test_expired(self):
        tok = self.s.make("a@b.c", days=1, now=1000)
        self.assertIsNone(self.s.check(tok, now=1000 + 86400 + 1))
        self.assertEqual(self.s.check(tok, now=1000 + 86399), "a@b.c")

    def test_tamper_and_wrong_key(self):
        tok = self.s.make("a@b.c")
        raw = base64.urlsafe_b64decode(tok).decode().replace("a@b.c", "admin@x.y", 1)
        forged = base64.urlsafe_b64encode(raw.encode()).decode()
        self.assertIsNone(self.s.check(forged))
        self.assertIsNone(sec.TokenSigner("z" * 32).check(tok))

    def test_garbage(self):
        for bad in ("", "!!!", "YQ=="):
            self.assertIsNone(self.s.check(bad))

    def test_format_compatible_with_legacy_implementation(self):
        """旧实现：base64(email:exp:hmac_sha256(secret, 'email:exp')[:32])。
        升级后线上已登录的 cookie 必须继续有效。"""
        import hashlib
        import hmac
        secret, email, exp = "k" * 32, "u@x.y", 4102444800
        sig = hmac.new(secret.encode(), f"{email}:{exp}".encode(), hashlib.sha256).hexdigest()[:32]
        legacy = base64.urlsafe_b64encode(f"{email}:{exp}:{sig}".encode()).decode()
        self.assertEqual(sec.TokenSigner(secret).check(legacy, now=1), email)


class SecretTests(unittest.TestCase):
    def test_accepts_both_key_names(self):
        self.assertEqual(sec.resolve_secret({"secret": "a" * 32}), ("a" * 32, None))
        self.assertEqual(sec.resolve_secret({"session_secret": "b" * 32}), ("b" * 32, None))

    def test_never_falls_back_to_a_fixed_string(self):
        for cfg in ({}, {"secret": "change-me"}, {"secret": "short"}):
            s, warn = sec.resolve_secret(cfg)
            self.assertNotIn(s, sec._WEAK_SECRETS)
            self.assertGreaterEqual(len(s), 32)
            self.assertTrue(warn)

    def test_random_secret_differs_between_calls_without_persist(self):
        self.assertNotEqual(sec.resolve_secret({})[0], sec.resolve_secret({})[0])

    def test_persisted_secret_is_stable_across_restarts(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, ".session_secret")
            a, _ = sec.resolve_secret({}, p)
            b, _ = sec.resolve_secret({}, p)
            self.assertEqual(a, b)
            if os.name == "posix":
                self.assertEqual(os.stat(p).st_mode & 0o777, 0o600)


class ClientIpTests(unittest.TestCase):
    def test_direct_peer_ignores_spoofed_header(self):
        self.assertEqual(sec.client_ip("8.8.8.8", "1.2.3.4"), "8.8.8.8")

    def test_trusted_proxy_uses_rightmost(self):
        self.assertEqual(sec.client_ip("127.0.0.1", "6.6.6.6, 203.0.113.9"), "203.0.113.9")
        self.assertEqual(sec.client_ip("::1", "203.0.113.9"), "203.0.113.9")

    def test_trusted_proxy_without_header(self):
        self.assertEqual(sec.client_ip("127.0.0.1", ""), "127.0.0.1")

    def test_no_peer(self):
        self.assertEqual(sec.client_ip("", ""), "?")


class RateLimiterTests(unittest.TestCase):
    def test_blocks_after_max_and_recovers_after_window(self):
        r = sec.RateLimiter(max_fails=3, window_s=100)
        for i in range(3):
            self.assertTrue(r.ok("ip", now=i))
            r.fail("ip", now=i)
        self.assertFalse(r.ok("ip", now=10))
        self.assertTrue(r.ok("ip", now=103))   # 最早一次已滑出窗口

    def test_keys_are_independent(self):
        r = sec.RateLimiter(max_fails=1, window_s=100)
        r.fail("a", now=0)
        self.assertFalse(r.ok("a", now=1))
        self.assertTrue(r.ok("b", now=1))

    def test_reset(self):
        r = sec.RateLimiter(max_fails=1, window_s=100)
        r.fail("a", now=0)
        r.reset("a")
        self.assertTrue(r.ok("a", now=1))

    def test_memory_is_bounded(self):
        r = sec.RateLimiter(max_fails=1, window_s=1000, max_keys=50)
        for i in range(500):
            r.fail(f"k{i}", now=i)
        self.assertLessEqual(len(r._d), 50)


class OriginTests(unittest.TestCase):
    H = "127.0.0.1:3000"

    def test_safe_methods_always_pass(self):
        self.assertTrue(sec.origin_ok("GET", "http://evil.test", "", self.H))

    def test_same_origin_post(self):
        self.assertTrue(sec.origin_ok("POST", "http://127.0.0.1:3000", "", self.H))

    def test_cross_origin_post_blocked(self):
        self.assertFalse(sec.origin_ok("POST", "http://evil.test", "", self.H))
        self.assertFalse(sec.origin_ok("POST", "null", "", self.H))

    def test_port_mismatch_is_cross_origin(self):
        self.assertFalse(sec.origin_ok("POST", "http://127.0.0.1:9999", "", self.H))

    def test_referer_fallback(self):
        self.assertTrue(sec.origin_ok("POST", "", "http://127.0.0.1:3000/login", self.H))
        self.assertFalse(sec.origin_ok("POST", "", "http://evil.test/x", self.H))

    def test_write_without_any_origin_or_referer_is_blocked(self):
        # 无 Origin 无 Referer 的写请求 = 可疑脚本，默认拒绝
        self.assertFalse(sec.origin_ok("POST", "", "", self.H))
        self.assertFalse(sec.origin_ok("DELETE", "", "", self.H))
        # 安全方法不受影响
        self.assertTrue(sec.origin_ok("GET", "", "", self.H))
        self.assertTrue(sec.origin_ok("HEAD", "", "", self.H))

    def test_forwarded_host_wins_and_case_insensitive(self):
        self.assertTrue(sec.origin_ok("POST", "https://KB.example", "", "127.0.0.1:8000", "kb.example"))
        self.assertFalse(sec.origin_ok("POST", "https://kb.example", "", "127.0.0.1:8000", "other.example"))

    def test_delete_and_put_are_also_checked(self):
        self.assertFalse(sec.origin_ok("DELETE", "http://evil.test", "", self.H))
        self.assertFalse(sec.origin_ok("PUT", "http://evil.test", "", self.H))


class RoutingTests(unittest.TestCase):
    def test_default_deny(self):
        for p in ("/api/search", "/api/docs", "/api/params", "/doc", "/gallery", "/", "/new-route"):
            self.assertFalse(sec.is_public_path(p), p)

    def test_public_login_flow(self):
        for p in ("/login", "/send_code", "/login_code", "/forgot", "/reset_password", "/healthz"):
            self.assertTrue(sec.is_public_path(p), p)

    def test_path_must_match_exactly(self):
        self.assertFalse(sec.is_public_path("/login/../api/search"))
        self.assertFalse(sec.is_public_path("/healthz/x"))


class CookieTests(unittest.TestCase):
    def test_secure_only_over_https(self):
        self.assertTrue(sec.cookie_secure("https", "http"))
        self.assertTrue(sec.cookie_secure("https, http", "http"))
        self.assertFalse(sec.cookie_secure("", "http"))
        self.assertFalse(sec.cookie_secure("http", "http"))
        self.assertTrue(sec.cookie_secure("", "https"))


if __name__ == "__main__":
    unittest.main()
