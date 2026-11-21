#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Linux.do (Discourse Connect) OAuth2 后端：纯逻辑，不依赖 FastAPI。

空 client_id 或 client_secret ⇒ is_enabled → False；路由层据此直接 404、模板不渲染按钮。
"""
import logging
import urllib.parse

import httpx

logger = logging.getLogger("kb.oauth.linuxdo")


def is_enabled(cfg):
    """两把钥匙都齐才算接入。任一为空（或全空白）就当作未配置。"""
    cfg = cfg or {}
    cid = (cfg.get("client_id") or "").strip()
    sec = (cfg.get("client_secret") or "").strip()
    return bool(cid and sec)


def build_authorize_url(cfg, state, redirect_uri):
    """拼 OAuth2 Authorize URL。state 由调用方生成并落 cookie，这里不管存储。"""
    qs = urllib.parse.urlencode({
        "response_type": "code",
        "client_id": cfg["client_id"],
        "redirect_uri": redirect_uri,
        "scope": cfg.get("scope", "user"),
        "state": state,
    })
    return f"{cfg['authorize_url']}?{qs}"


def exchange_code(cfg, code, redirect_uri, timeout=10.0):
    """code → {"access_token": ...}。网络/HTTP 错直接抛，由上层转 302+err。"""
    data = {
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": redirect_uri,
        "client_id": cfg["client_id"],
        "client_secret": cfg["client_secret"],
    }
    with httpx.Client(timeout=timeout) as cli:
        r = cli.post(cfg["token_url"], data=data,
                     headers={"Accept": "application/json"})
        r.raise_for_status()
        return r.json()


def fetch_userinfo(cfg, access_token, timeout=10.0):
    """GET /api/user，Bearer 鉴权。返回 Discourse 原生用户 JSON。"""
    with httpx.Client(timeout=timeout) as cli:
        r = cli.get(cfg["userinfo_url"],
                    headers={"Authorization": f"Bearer {access_token}",
                             "Accept": "application/json"})
        r.raise_for_status()
        return r.json()


def resolve_subject(info):
    value = info.get("sub") if info.get("sub") is not None else info.get("id")
    if isinstance(value, bool) or not isinstance(value, (str, int)) or not str(value).strip():
        raise ValueError("Stable OAuth subject required")
    return str(value).strip()


def resolve_account(state, info):
    """Bind only the stable provider identity; remote email is never a login key."""
    import secrets
    from datetime import datetime
    sub = resolve_subject(info)
    now = datetime.now().isoformat(timespec="seconds")
    with state._lock, state.con:
        state.execute("BEGIN IMMEDIATE")
        row = state.fetchone("SELECT u.email FROM oauth_identities o JOIN users u ON u.id=o.user_id WHERE o.provider=? AND o.sub=?", ("linuxdo", sub))
        if row:
            state.execute("UPDATE users SET last_login=? WHERE email=?", (now, row[0]))
            return row[0]
        # Random internal address also avoids pre-registration of a predictable subject address.
        email = "linuxdo-" + secrets.token_hex(24) + "@users.linux.do"
        cur = state.execute("INSERT INTO users(email,pass,created,last_login,verified) VALUES(?,?,?,?,1)", (email, "oauth:linuxdo", now, now))
        state.execute("INSERT INTO oauth_identities(provider,sub,user_id) VALUES(?,?,?)", ("linuxdo", sub, cur.lastrowid))
        return email


def resolve_email(info):
    """真实邮箱优先；用户在 linux.do 隐藏了邮箱时退化成合成占位邮箱。
    合成格式：linuxdo-{id|sub}@users.linux.do，和 users 表 UNIQUE(email) 不会撞车。"""
    email = (info.get("email") or "").strip().lower()
    if email:
        return email
    sub = info.get("sub") or info.get("id") or ""
    return f"linuxdo-{sub}@users.linux.do"
