#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""认证与请求安全的纯逻辑（只依赖标准库，可脱离 FastAPI 单测）。

app.py 负责把这里的函数接到路由/中间件上；这里不碰 HTTP 框架、不碰数据库。
"""
import base64
import hashlib
import hmac
import ipaddress
import os
import secrets
import threading
import time
from urllib.parse import urlsplit

# ---------------- 口令哈希 ----------------
# OWASP 2023 对 PBKDF2-HMAC-SHA256 的建议值。旧哈希（120k）在登录成功时自动升级。
PBKDF2_ITERS = 600_000
MIN_PASSWORD_LEN = 10


def hash_password(password, salt=None, iters=PBKDF2_ITERS):
    salt = salt or secrets.token_hex(16)
    dk = hashlib.pbkdf2_hmac("sha256", password.encode(), salt.encode(), iters)
    return f"pbkdf2${iters}${salt}${dk.hex()}"


def verify_password(password, stored):
    """返回 (是否通过, 是否需要用新参数重新哈希)"""
    try:
        _, iters, salt, hexh = stored.split("$")
        iters = int(iters)
        dk = hashlib.pbkdf2_hmac("sha256", password.encode(), salt.encode(), iters)
        ok = hmac.compare_digest(dk.hex(), hexh)
        return ok, ok and iters < PBKDF2_ITERS
    except Exception:
        return False, False


# ---------------- 会话 token ----------------
# 格式与旧实现完全一致（base64(email:exp:sig32)），已登录用户的 cookie 升级后继续有效。
class TokenSigner:
    def __init__(self, secret):
        self._key = secret.encode()

    def _sig(self, email, exp):
        return hmac.new(self._key, f"{email}:{exp}".encode(), hashlib.sha256).hexdigest()[:32]

    def make(self, email, days=30, now=None):
        exp = int(now if now is not None else time.time()) + days * 86400
        raw = f"{email}:{exp}:{self._sig(email, exp)}"
        return base64.urlsafe_b64encode(raw.encode()).decode()

    def check(self, tok, now=None):
        """通过返回 email，否则 None"""
        try:
            raw = base64.urlsafe_b64decode(tok.encode()).decode()
            email, exp, sig = raw.rsplit(":", 2)
            if int(exp) < (now if now is not None else time.time()):
                return None
            return email if hmac.compare_digest(sig, self._sig(email, exp)) else None
        except Exception:
            return None


_WEAK_SECRETS = {"", "change-me", "changeme", "secret", "<32+ 位随机串，openssl rand -hex 32>"}


def resolve_secret(auth_cfg, persist_path=None):
    """取会话签名密钥。返回 (secret, 警告或 None)。

    - 兼容两个键名：config.example.json 写的是 session_secret，旧代码读的是 secret。
    - 缺失或弱值时绝不回落到固定字符串（那等于任何人都能伪造 cookie），
      而是生成随机密钥；给了 persist_path 就落盘，避免每次重启都把所有人登出。
    """
    s = auth_cfg.get("secret") or auth_cfg.get("session_secret") or ""
    if s not in _WEAK_SECRETS and len(s) >= 16:
        return s, None
    warn = "auth.secret 未配置或过弱，已改用随机密钥；请在 config.json 里设置 32+ 位随机串"
    if persist_path:
        try:
            with open(persist_path, encoding="utf-8") as f:
                old = f.read().strip()
            if len(old) >= 32:
                return old, warn
        except OSError:
            pass
    new = secrets.token_hex(32)
    if persist_path:
        try:
            fd = os.open(persist_path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
            with os.fdopen(fd, "w", encoding="utf-8") as f:
                f.write(new)
        except OSError:
            warn += "（密钥无法落盘，重启后所有会话失效）"
    return new, warn


# ---------------- 客户端 IP ----------------
_TRUSTED_PROXIES = {"127.0.0.1", "::1"}


def client_ip(peer, xff):
    """只有直连方是本机反代（Caddy）时才信 X-Forwarded-For，并取最右一项
    （最右是反代自己看到的对端；左边的可以被客户端随意伪造）。"""
    if peer in _TRUSTED_PROXIES and xff:
        last = xff.split(",")[-1].strip()
        if last:
            return last
    return peer or "?"


def _parse_cidrs(cidrs):
    """把字串 CIDR 列表解析成 ip_network 列表，坏值直接忽略不抛。"""
    nets = []
    for c in cidrs or ():
        try:
            nets.append(ipaddress.ip_network(c, strict=False))
        except (ValueError, TypeError):
            continue
    return nets


def _ip_in_any(ip_str, nets):
    try:
        ip = ipaddress.ip_address(ip_str)
    except (ValueError, TypeError):
        return False
    return any(ip in n for n in nets)


def real_client_ip(request, trusted_proxies=()):
    """如果请求来自可信代理（trusted_proxies CIDR 列表），取 CF-Connecting-IP 或 XFF
    第一跳；否则 request.client.host。无法确定时返回 "".

    - 可信判定基于 TCP 对端（request.client.host），不是任何头部，否则可伪造。
    - XFF 优先取最左（最接近客户端的那一跳），CF-Connecting-IP 更可靠时优先它。
    """
    peer = ""
    if getattr(request, "client", None):
        peer = request.client.host or ""
    nets = _parse_cidrs(trusted_proxies)
    headers = getattr(request, "headers", {}) or {}
    cf = (headers.get("cf-connecting-ip") or "").strip()
    xff = (headers.get("x-forwarded-for") or "").strip()
    if peer and _ip_in_any(peer, nets):
        if cf:
            return cf
        if xff:
            return xff.split(",")[0].strip()
    return peer or ""


# ---------------- 限流 ----------------
class RateLimiter:
    """滑动窗口失败计数（内存；重启清零）。max_keys 防止被刷爆内存。"""

    def __init__(self, max_fails=8, window_s=900, max_keys=20000):
        self.max_fails, self.window_s, self.max_keys = max_fails, window_s, max_keys
        self._d = {}
        self._lock = threading.Lock()

    def _fresh(self, key, now):
        arr = [t for t in self._d.get(key, ()) if now - t < self.window_s]
        if arr:
            self._d[key] = arr
        else:
            self._d.pop(key, None)
        return arr

    def ok(self, key, now=None):
        now = now if now is not None else time.time()
        with self._lock:
            return len(self._fresh(key, now)) < self.max_fails

    def fail(self, key, now=None):
        now = now if now is not None else time.time()
        with self._lock:
            if len(self._d) >= self.max_keys and key not in self._d:
                self._d.pop(next(iter(self._d)))
            self._d.setdefault(key, []).append(now)

    def reset(self, key):
        with self._lock:
            self._d.pop(key, None)


# ---------------- CSRF：Origin / Referer 同源校验 ----------------
_SAFE_METHODS = {"GET", "HEAD", "OPTIONS"}


def origin_ok(method, origin, referer, host, forwarded_host="", allowed_origins=()):
    """写请求必须同源。浏览器的跨站表单/fetch 一定带 Origin（或至少 Referer）。

    allowed_origins 为显式白名单（如 CF 公网域名）。为空时仅 same-host 通过，
    不会放任何跨源 —— 和原来的行为一致。白名单比对 scheme://netloc，
    大小写不敏感。
    """
    if method.upper() in _SAFE_METHODS:
        return True
    want = (forwarded_host.split(",")[0].strip() or host or "").lower()
    allow = set()
    for a in allowed_origins or ():
        if not a:
            continue
        u = urlsplit(a)
        if u.scheme and u.netloc:
            allow.add(f"{u.scheme.lower()}://{u.netloc.lower()}")
    if origin:
        if origin == "null":
            return False
        ou = urlsplit(origin)
        if ou.netloc.lower() == want:
            return True
        return f"{ou.scheme.lower()}://{ou.netloc.lower()}" in allow
    if referer:
        ru = urlsplit(referer)
        if ru.netloc.lower() == want:
            return True
        return f"{ru.scheme.lower()}://{ru.netloc.lower()}" in allow
    # 无 Origin 无 Referer 的写请求 = 可疑脚本
    return False


# ---------------- 路由放行表（默认拒绝） ----------------
# 不在表里的路径，未登录一律拒绝；新增路由默认受保护，不会再出现"忘了加鉴权"。
PUBLIC_PATHS = frozenset({
    "/login", "/send_code", "/login_code", "/register",
    "/forgot", "/reset_password", "/logout", "/healthz",
    "/oauth/linuxdo/start", "/oauth/linuxdo/callback",
    "/sitemap.xml", "/robots.txt",
    "/privacy", "/tos", "/favicon.ico",
})


def is_public_path(path):
    return path in PUBLIC_PATHS


def cookie_secure(forwarded_proto, scheme):
    """经 HTTPS 访问（Caddy 会带 X-Forwarded-Proto）时才加 Secure，
    这样同一份代码在隧道（http://127.0.0.1）和 HTTPS 下都能登录。"""
    return (forwarded_proto or scheme or "").split(",")[0].strip().lower() == "https"


# ---------------- 安全响应头 ----------------
# TODO: script-src/style-src 暂带 'unsafe-inline' —— 现有模板有内联脚本/样式与 onclick，
#       改成 nonce 并把内联脚本抽到静态文件后才能收紧。
CSP = ("default-src 'self'; img-src 'self' data: https:; connect-src 'self'; "
       "object-src 'none'; base-uri 'none'; frame-ancestors 'self'; "
       "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com; "
       "style-src 'self' 'unsafe-inline'; "
       "frame-src https://challenges.cloudflare.com")


def apply_security_headers(headers):
    """补齐安全响应头。全部 setdefault：不覆盖框架/路由已设的同名头
    （如 /source/file 给第三方归档 HTML 设的 Content-Security-Policy: sandbox）。"""
    headers.setdefault("X-Content-Type-Options", "nosniff")
    headers.setdefault("Referrer-Policy", "same-origin")
    headers.setdefault("X-Frame-Options", "SAMEORIGIN")
    headers.setdefault("Content-Security-Policy", CSP)
