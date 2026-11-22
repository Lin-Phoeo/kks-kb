#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Web 路由层：FastAPI 应用、模板、鉴权装配与全部路由。"""
# 鉴权由中间件统一处理，routes 内不再重复检查
import json
import hashlib
import logging
import mimetypes
import os
import re
import secrets
import asyncio
import time
from urllib.parse import quote
from datetime import datetime

import httpx
import markdown

logger = logging.getLogger("kb.web.routes")
from fastapi import FastAPI, Form, HTTPException, Request, Response
from fastapi.responses import (HTMLResponse, JSONResponse, RedirectResponse,
                               StreamingResponse, FileResponse)
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates

from .. import sanitize
from .. import security as sec
from ..security_turnstile import verify_turnstile
from .. import oauth_linuxdo
from ..config import BASE, CFG, COLLECTIONS, DATA, GAME_ROOT, STATE
from ..content.gallery import _load_img_manifest, _load_ocr_map, album_of, imgs_for_doc
from ..content.doc_images import rewrite_images
from ..content.manual import _load_manual, manual_nav, manual_ref, manual_tree
from ..content.learning import featured_guides, reader_title
from ..content.doc_metadata import load_doc_metadata, load_doc_view
from ..content.reading_view import prepare_reading_html
from ..content.sources import source_for_document, source_content_disposition
from ..content.params import PARAM_IDX, PARAM_IDX_AT, lookup_param
from ..generation.llm import QUOTA_PER_DAY, llm_chain, split_followups, websearch
from ..generation.prompt import SYS, SYS_RECIPE, build_prompt, build_prompt_recipe
from ..generation.validate import validate_params
from ..retrieval import KBX

app = FastAPI(title="KKS 知识库", docs_url=None, redoc_url=None, openapi_url=None)

# Windows file associations can otherwise label ES modules text/plain.
mimetypes.init()
mimetypes.add_type("text/javascript", ".js")
_STATIC_DIR = os.path.join(BASE, "static")
if os.path.isdir(_STATIC_DIR):
    app.mount("/static", StaticFiles(directory=_STATIC_DIR), name="static")

templates = Jinja2Templates(directory=os.path.join(BASE, "templates"))
templates.env.filters["reader_title"] = reader_title
templates.env.globals["auth_on"] = bool(CFG.get("auth", {}).get("pass_sha256"))
templates.env.globals["foot_build"] = datetime.now().strftime("%Y-%m-%d")
templates.env.globals["COLLECTIONS"] = COLLECTIONS
templates.env.globals["manual_tree"] = manual_tree
templates.env.globals["manual_ref"] = manual_ref
# Turnstile sitekey 以 Jinja 全局形式暴露给所有模板（login/register/forgot）；
# 空字串时模板里的 {% if cf_turnstile_sitekey %} 整段 widget 不渲染，功能自然禁用。
templates.env.globals["cf_turnstile_sitekey"] = \
    CFG.get("cloudflare", {}).get("turnstile_sitekey", "") or ""
# Linux.do OAuth 开关：空凭据 → False，模板的「使用 Linux.do 登录」按钮不渲染。
LINUXDO_CFG = (CFG.get("oauth", {}) or {}).get("linuxdo", {}) or {}
templates.env.globals["linuxdo_enabled"] = oauth_linuxdo.is_enabled(LINUXDO_CFG)

MD = markdown.Markdown(extensions=["tables", "fenced_code", "sane_lists", "nl2br"])
CITE_L = re.compile(r"\[L(\d+)\]")
CITE_W = re.compile(r"\[W(\d+)\]")


def extract_toc(md_text):
    """从 markdown 抽取 ## / ### 生成锚点目录"""
    import hashlib as _h
    toc = []
    for line in (md_text or "").split("\n"):
        m = re.match(r"^(#{2,3})\s+(.+?)\s*$", line)
        if not m:
            continue
        level = len(m.group(1))
        title = re.sub(r"[*`_]", "", m.group(2)).strip()
        anchor = "h-" + _h.md5(title.encode("utf-8")).hexdigest()[:8]
        toc.append({"level": level, "title": title, "anchor": anchor})
    return toc


def add_anchors(html, md_text):
    """给渲染后的 h2/h3 加 id，配合同样的锚点算法"""
    import hashlib as _h
    titles = [re.sub(r"[*`_]", "", m.group(2)).strip()
              for m in re.finditer(r"^(#{2,3})\s+(.+?)\s*$", md_text or "", re.M)]

    def repl(m):
        nonlocal titles
        tag = m.group(1)
        inner = m.group(2)
        plain = re.sub(r"<[^>]+>", "", inner).strip()
        if plain in titles:
            anchor = "h-" + _h.md5(plain.encode("utf-8")).hexdigest()[:8]
            return f'<{tag} id="{anchor}">{inner}</{tag}>'
        return m.group(0)

    return re.sub(r"<(h[23])>(.*?)</\1>", repl, html)


def md2html(t):
    MD.reset()
    # 管线顺序：markdown → 净化 → linkify → add_anchors（doc/问答页共用同一入口）
    return sanitize.clean_html(MD.convert(t or ""))


def linkify(html):
    html = CITE_L.sub(lambda m: f'<span class="cite" data-n="{m.group(1)}" data-t="l">L{m.group(1)}</span>', html)
    html = CITE_W.sub(lambda m: f'<span class="cite w" data-n="{m.group(1)}" data-t="w">W{m.group(1)}</span>', html)
    return html


# ---------------- 认证 ----------------
AUTH = CFG.get("auth", {})
SECRET, _secret_warn = sec.resolve_secret(AUTH, os.path.join(DATA, ".session_secret"))
if _secret_warn:
    logger.warning("[auth] %s", _secret_warn)
ADMIN_EMAIL = AUTH.get("admin_email", "")
COOKIE = "kb_session"
SMTP = CFG.get("smtp", {})
OPEN_REGISTRATION = bool(AUTH.get("open_registration", False))
SIGNER = sec.TokenSigner(SECRET)
# Cloudflare & 跨源配置
CF_CFG = CFG.get("cloudflare", {}) or {}
CF_TURNSTILE_SECRET = CF_CFG.get("turnstile_secret", "") or ""
CF_TRUSTED_PROXIES = CF_CFG.get("trusted_proxies", []) or []
ALLOWED_ORIGINS = (CFG.get("security", {}) or {}).get("allowed_origins", []) or []
# 登录失败按 IP 与按账号各计一次：前者防单点爆破，后者防分布式撞同一个账号
LOGIN_FAILS_IP = sec.RateLimiter(int(AUTH.get("rate_limit_fails", 8)),
                                 int(AUTH.get("rate_limit_window_min", 15)) * 60)
LOGIN_FAILS_ACCT = sec.RateLimiter(int(AUTH.get("rate_limit_fails", 8)) * 2,
                                   int(AUTH.get("rate_limit_window_min", 15)) * 60)
# 发验证码：每 IP 每小时 10 次，防止被拿来刷 SMTP
CODE_SENDS_IP = sec.RateLimiter(10, 3600)


def make_token(email, days=30):
    return SIGNER.make(email, days)


def check_token(tok):
    """校验通过返回 email，否则 None"""
    return SIGNER.check(tok)


def ip_of(request: Request):
    """客户端真实 IP：
    - 若直连方在 CF 可信代理段 → 取 CF-Connecting-IP 或 XFF 第一跳；
    - 否则沿用 client_ip（本机反代走 XFF 最右一跳的旧逻辑）。
    两条路径互补：Caddy 本机反代 → client_ip；CF 公网反代 → real_client_ip。
    """
    peer = request.client.host if request.client else ""
    xff = request.headers.get("x-forwarded-for", "")
    if CF_TRUSTED_PROXIES:
        real = sec.real_client_ip(request, CF_TRUSTED_PROXIES)
        if real:
            return real
    return sec.client_ip(peer, xff)


def current_user(request: Request):
    tok = request.cookies.get(COOKIE)
    return check_token(tok) if tok else None


def actor_id(request: Request, required=False):
    email = current_user(request)
    row = STATE.fetchone("SELECT id FROM users WHERE email=?", (email,)) if email else None
    if required and not row:
        raise HTTPException(401)
    return row[0] if row else None


def owned_conversation(request, cid):
    actor = actor_id(request, required=True)
    try:
        KBX.require_owner(cid, actor=actor)
    except PermissionError:
        raise HTTPException(404)
    return actor


@app.exception_handler(PermissionError)
async def permission_error(request, exc):
    return JSONResponse({"detail": "Not found"}, status_code=404)


def authed(request: Request):
    if not CFG.get("auth", {}).get("required", True):
        return True
    return current_user(request) is not None


def send_mail(to_addr, subject, body):
    """QQ SMTP（465 SSL）"""
    import smtplib, ssl
    from email.mime.text import MIMEText
    from email.header import Header
    if not SMTP.get("enabled") or not SMTP.get("user"):
        return False, "SMTP 未配置"
    try:
        msg = MIMEText(body, "plain", "utf-8")
        msg["Subject"] = Header(subject, "utf-8")
        msg["From"] = SMTP.get("from") or SMTP["user"]
        msg["To"] = to_addr
        ctx = ssl.create_default_context()
        with smtplib.SMTP_SSL(SMTP["host"], int(SMTP.get("port", 465)), context=ctx, timeout=20) as s:
            s.login(SMTP["user"], SMTP["pass"])
            s.sendmail(msg["From"], [to_addr], msg.as_string())
        return True, ""
    except Exception as e:
        return False, f"{type(e).__name__}: {e}"


def _seed_admin():
    if not ADMIN_EMAIL:
        return
    if STATE.fetchone("SELECT 1 FROM users WHERE email=?", (ADMIN_EMAIL,)):
        return
    pw = CFG.get("auth", {}).get("admin_password", "")
    if pw:
        STATE.execute("INSERT INTO users(email,pass,created,verified) VALUES(?,?,?,1)",
                      (ADMIN_EMAIL, sec.hash_password(pw), datetime.now().isoformat(timespec="seconds")))
        STATE.commit()
        logger.info("[seed] 管理员账号已创建: %s", ADMIN_EMAIL)


_seed_admin()
_n0, _d0 = KBX.counts()
templates.env.globals["foot_docs"] = _d0
templates.env.globals["foot_chunks"] = _n0
templates.env.globals["foot_dim"] = 0 if KBX.mat is None else KBX.mat.shape[1]


RECIPE_COLS = ("shader", "src", "mods", "pidx")


def hybrid_recipe(q, k=6):
    """改法模式检索：全库打底 + 代码/参数三库加强，去重合并"""
    out, seen = [], set()

    def _grab(kw, cap):
        try:
            res = KBX.hybrid(q, **kw)
        except Exception as e:
            logger.warning("[recipe] hybrid 失败 %s: %s: %s", kw, type(e).__name__, e)
            return
        # KBX.hybrid 返回 (chunks, stats)，也可能只返回 chunks
        chunks = res[0] if isinstance(res, tuple) else res
        if not chunks:
            return
        n = 0
        for c in chunks:
            key = (c.get("path"), c.get("heading"))
            if key in seen:
                continue
            seen.add(key)
            out.append(c)
            n += 1
            if n >= cap:
                break

    _grab({"k": 4}, 4)
    for col, cap in zip(RECIPE_COLS, (5, 3, 3, 2)):
        _grab({"k": cap + 2, "col": col}, cap)

    logger.info("[recipe] 检索到 %d 段，来自 %s",
                len(out), sorted(set(c.get('col', '?') for c in out)))
    return out[:k + 6]


# ---------------- 路由 ----------------
@app.get("/healthz")
def healthz(request: Request):
    n, d = KBX.counts()
    vec = None if KBX.mat is None else list(KBX.mat.shape)
    vec_ok = vec is not None and vec[0] == n
    if request.headers.get("x-forwarded-for"):
        # 经反代来的（即公网请求）只给结论，不曝光规模数字
        return {"ok": bool(vec_ok)}
    return {"ok": True, "chunks": n, "docs": d, "vec": vec, "vec_ok": vec_ok}


def _rate_ok(ip, email=""):
    return LOGIN_FAILS_IP.ok(ip) and (not email or LOGIN_FAILS_ACCT.ok(email))


def _add_fail(ip, email=""):
    LOGIN_FAILS_IP.fail(ip)
    if email:
        LOGIN_FAILS_ACCT.fail(email)


def _user_exists(email):
    return STATE.fetchone("SELECT 1 FROM users WHERE email=?", (email,)) is not None


@app.get("/login", response_class=HTMLResponse)
def login_page(request: Request, err: str = ""):
    n, d = KBX.counts()
    return templates.TemplateResponse(request=request, name="login.html", context={
        "err": err, "n_chunks": n, "n_docs": d, "email": "",
        "open_registration": OPEN_REGISTRATION})


@app.post("/send_code")
def send_code(request: Request, email: str = Form(...)):
    email = email.strip().lower()
    if not re.match(r"^[^@\s]+@[^@\s]+\.[^@\s]+$", email):
        return JSONResponse({"ok": False, "msg": "邮箱格式不对"})
    ip = ip_of(request)
    if not CODE_SENDS_IP.ok(ip):
        return JSONResponse({"ok": False, "msg": "请求过于频繁，请稍后再试"})
    CODE_SENDS_IP.fail(ip)   # 这里的“失败”指“发送次数”，窗口内计数即限流
    if not OPEN_REGISTRATION and not _user_exists(email):
        # 关闭注册时对陌生邮箱不发信，也不透露该邮箱是否已注册
        return JSONResponse({"ok": True})
    now = int(time.time())
    row = STATE.fetchone("SELECT sent FROM codes WHERE email=?", (email,))
    if row and now - (row[0] or 0) < 60:
        left = 60 - (now - row[0])
        return JSONResponse({"ok": False, "msg": f"请 {left} 秒后再试"})
    code = f"{secrets.randbelow(900000) + 100000}"
    STATE.execute(
        "INSERT INTO codes(email,code,exp,sent,tries) VALUES(?,?,?,?,0) "
        "ON CONFLICT(email) DO UPDATE SET code=?, exp=?, sent=?, tries=0",
        (email, code, now + 600, now, code, now + 600, now))
    STATE.commit()
    body = (f"KKS 知识库\n\n你的验证码是：{code}\n\n10 分钟内有效，只能使用一次。\n"
            f"如果不是你本人操作，请忽略这封邮件。\n")
    ok, err = send_mail(email, "KKS 知识库 · 登录验证码", body)
    if not ok:
        logger.error("[smtp] %s", err)
        return JSONResponse({"ok": False, "msg": "邮件发送失败，请稍后再试"})
    logger.info("[code] 已发送验证码 -> %s", email)   # 不要把验证码本身写进日志
    return JSONResponse({"ok": True})


def _verify_code(email, code):
    row = STATE.fetchone("SELECT code,exp,tries FROM codes WHERE email=?", (email,))
    if not row:
        return False, "请先获取验证码"
    c, exp, tries = row
    if int(exp) < time.time():
        return False, "验证码已过期，请重新获取"
    if tries >= 5:
        return False, "尝试次数过多，请重新获取"
    if c != code.strip():
        STATE.execute("UPDATE codes SET tries=tries+1 WHERE email=?", (email,))
        STATE.commit()
        return False, "验证码不正确"
    STATE.execute("DELETE FROM codes WHERE email=?", (email,))
    STATE.commit()
    return True, ""


def _issue_session(email, request: Request):
    resp = RedirectResponse("/", status_code=303)
    secure = sec.cookie_secure(request.headers.get("x-forwarded-proto", ""), request.url.scheme)
    resp.set_cookie(COOKIE, make_token(email), max_age=30 * 86400,
                    httponly=True, samesite="lax", secure=secure)
    return resp


@app.post("/login")
async def login(request: Request, email: str = Form(...), password: str = Form(...),
                cf_turnstile_response: str = Form("", alias="cf-turnstile-response")):
    ip = ip_of(request)
    email = email.strip().lower()
    # Turnstile 先过：失败只提示重试，不计入密码失败锁，账号不会被锁。
    ts_ok, ts_err = await verify_turnstile(cf_turnstile_response, ip, CF_TURNSTILE_SECRET)
    if not ts_ok:
        return JSONResponse({"ok": False, "msg": "人机校验未通过，请重试。", "turnstile": ts_err})
    if not _rate_ok(ip, email):
        return JSONResponse({"ok": False, "msg": "尝试过多，请 15 分钟后再试"})
    row = STATE.fetchone("SELECT pass FROM users WHERE email=?", (email,))
    ok, rehash = sec.verify_password(password, row[0]) if row else (False, False)
    if not ok:
        _add_fail(ip, email)
        return JSONResponse({"ok": False, "msg": "邮箱或密码不正确"})
    LOGIN_FAILS_ACCT.reset(email)
    if rehash:   # 旧哈希迭代次数不足，登录成功时静默升级
        STATE.execute("UPDATE users SET pass=? WHERE email=?", (sec.hash_password(password), email))
    STATE.execute("UPDATE users SET last_login=? WHERE email=?",
                  (datetime.now().isoformat(timespec="seconds"), email))
    STATE.commit()
    return _issue_session(email, request)


@app.post("/login_code")
async def login_code(request: Request, email: str = Form(...), code: str = Form(...),
                     cf_turnstile_response: str = Form("", alias="cf-turnstile-response")):
    ip = ip_of(request)
    email = email.strip().lower()
    ts_ok, ts_err = await verify_turnstile(cf_turnstile_response, ip, CF_TURNSTILE_SECRET)
    if not ts_ok:
        return JSONResponse({"ok": False, "msg": "人机校验未通过，请重试。", "turnstile": ts_err})
    if not _rate_ok(ip, email):
        return JSONResponse({"ok": False, "msg": "尝试过多，请 15 分钟后再试"})
    ok, msg = _verify_code(email, code)
    if not ok:
        _add_fail(ip, email)
        return JSONResponse({"ok": False, "msg": msg})
    if not _user_exists(email):
        if not OPEN_REGISTRATION:   # 验证码登录原先会自动建号，等于另一条开放注册路径
            return JSONResponse({"ok": False, "msg": "该邮箱未开通，请联系管理员"})
        STATE.execute("INSERT INTO users(email,pass,created,verified) VALUES(?,?,?,1)",
                      (email, "", datetime.now().isoformat(timespec="seconds")))
        STATE.commit()
    return _issue_session(email, request)


@app.post("/register")
async def register(request: Request, email: str = Form(...), code: str = Form(...),
                   password: str = Form(...),
                   cf_turnstile_response: str = Form("", alias="cf-turnstile-response")):
    if not OPEN_REGISTRATION:
        return JSONResponse({"ok": False, "msg": "注册已关闭，请联系管理员"}, status_code=403)
    ip = ip_of(request)
    ts_ok, ts_err = await verify_turnstile(cf_turnstile_response, ip, CF_TURNSTILE_SECRET)
    if not ts_ok:
        return JSONResponse({"ok": False, "msg": "人机校验未通过，请重试。", "turnstile": ts_err})
    if not _rate_ok(ip):
        return JSONResponse({"ok": False, "msg": "尝试过多，请稍后再试"})
    email = email.strip().lower()
    if not re.match(r"^[^@\s]+@[^@\s]+\.[^@\s]+$", email):
        return JSONResponse({"ok": False, "msg": "邮箱格式不对"})
    if len(password) < sec.MIN_PASSWORD_LEN:
        return JSONResponse({"ok": False, "msg": f"密码至少 {sec.MIN_PASSWORD_LEN} 位"})
    if _user_exists(email):
        return JSONResponse({"ok": False, "msg": "这个邮箱已注册，直接登录即可"})
    ok, msg = _verify_code(email, code)
    if not ok:
        _add_fail(ip)
        return JSONResponse({"ok": False, "msg": msg})
    STATE.execute("INSERT INTO users(email,pass,created,verified) VALUES(?,?,?,1)",
                  (email, sec.hash_password(password), datetime.now().isoformat(timespec="seconds")))
    STATE.commit()
    logger.info("[register] %s", email)
    return _issue_session(email, request)


@app.get("/logout")
def logout():
    resp = RedirectResponse("/login", status_code=303)
    resp.delete_cookie(COOKIE)
    return resp


# ---------------- Linux.do OAuth2 ----------------
OAUTH_LINUXDO_COOKIE = "kb_oauth_state"
OAUTH_LINUXDO_STATE_TTL = 600  # 10 分钟


def _oauth_linuxdo_redirect_uri(request: Request):
    """固定路径 /oauth/linuxdo/callback；scheme 跟随 X-Forwarded-Proto（CF 后面是 https）。"""
    proto = (request.headers.get("x-forwarded-proto", "")
             .split(",")[0].strip().lower() or request.url.scheme)
    host = request.headers.get("x-forwarded-host", "") or request.headers.get("host", "")
    return f"{proto}://{host}/oauth/linuxdo/callback"


@app.get("/oauth/linuxdo/start")
def oauth_linuxdo_start(request: Request):
    if not oauth_linuxdo.is_enabled(LINUXDO_CFG):
        raise HTTPException(404)
    state = secrets.token_urlsafe(16)
    redirect_uri = _oauth_linuxdo_redirect_uri(request)
    url = oauth_linuxdo.build_authorize_url(LINUXDO_CFG, state, redirect_uri)
    resp = RedirectResponse(url, status_code=302)
    secure = sec.cookie_secure(request.headers.get("x-forwarded-proto", ""),
                               request.url.scheme)
    resp.set_cookie(OAUTH_LINUXDO_COOKIE, state, max_age=OAUTH_LINUXDO_STATE_TTL,
                    httponly=True, samesite="lax", secure=secure, path="/oauth/linuxdo")
    return resp


@app.get("/oauth/linuxdo/callback")
def oauth_linuxdo_callback(request: Request, code: str = "", state: str = ""):
    if not oauth_linuxdo.is_enabled(LINUXDO_CFG):
        raise HTTPException(404)
    cookie_state = request.cookies.get(OAUTH_LINUXDO_COOKIE, "")
    # 常量时间比较 + 任一为空直接拒。state 不匹配 → CSRF，400 结束。
    if not cookie_state or not state or not secrets.compare_digest(cookie_state, state):
        raise HTTPException(400, "state 不匹配")
    if not code:
        return RedirectResponse("/login?err=linuxdo_token", status_code=303)

    redirect_uri = _oauth_linuxdo_redirect_uri(request)
    try:
        tok = oauth_linuxdo.exchange_code(LINUXDO_CFG, code, redirect_uri)
        access_token = (tok or {}).get("access_token") or ""
        if not access_token:
            raise RuntimeError("token response 无 access_token")
    except Exception as e:
        logger.warning("[oauth] linux.do token 失败: %s: %s", type(e).__name__, e)
        return RedirectResponse("/login?err=linuxdo_token", status_code=303)

    try:
        info = oauth_linuxdo.fetch_userinfo(LINUXDO_CFG, access_token)
    except Exception as e:
        logger.warning("[oauth] linux.do userinfo 失败: %s: %s", type(e).__name__, e)
        return RedirectResponse("/login?err=linuxdo_userinfo", status_code=303)

    try:
        email = oauth_linuxdo.resolve_account(STATE, info)
    except (ValueError, TypeError, AttributeError):
        return RedirectResponse("/login?err=linuxdo_userinfo", status_code=303)
    resp = _issue_session(email, request)
    # 清掉一次性 state cookie
    resp.delete_cookie(OAUTH_LINUXDO_COOKIE, path="/oauth/linuxdo")
    return resp


@app.get("/", response_class=HTMLResponse)
def home(request: Request):
    col = request.query_params.get("col", "")
    n, d = KBX.counts(col)
    # 首登判定：created 距今 <10 分钟视为新用户；读不到或解析失败也当新用户，让引导兜底
    show_onboarding = True
    who = current_user(request)
    if who:
        row = STATE.fetchone("SELECT created FROM users WHERE email=?", (who,))
        if row and row[0]:
            try:
                show_onboarding = (datetime.now() - datetime.fromisoformat(row[0])).total_seconds() < 600
            except Exception:
                show_onboarding = True
    return templates.TemplateResponse(request=request, name="home.html", context={
        "n_chunks": n, "n_docs": d, "convs": KBX.convs(actor=actor_id(request)), "cid": None, "msgs": [],
        "col": col, "col_stats": KBX.col_stats(), "show_onboarding": show_onboarding,
        "learning_guides": featured_guides(KBX.doc_list('kks')),
        "reader_scope": hashlib.sha256(str(actor_id(request) or 'local').encode()).hexdigest()[:16]})


@app.get("/shader-lab", response_class=HTMLResponse)
def shader_lab(request: Request):
    n, d = KBX.counts()
    return templates.TemplateResponse(request=request, name="shader_lab.html", context={
        "n_chunks": n, "n_docs": d, "convs": KBX.convs(actor=actor_id(request)),
        "cid": None, "col": "", "col_stats": KBX.col_stats()})


@app.get("/c/{cid}", response_class=HTMLResponse)
def conv_view(request: Request, cid: int):
    title = KBX.conv_title(cid, actor=owned_conversation(request, cid))
    if title is None:
        raise HTTPException(404)
    n, _ = KBX.counts()
    msgs = KBX.msgs(cid, actor=actor_id(request, required=True))
    for m in msgs:
        if m["role"] == "assistant":
            ans, fol = split_followups(m["content"])
            m["answer_html"] = linkify(md2html(ans))
            m["followups"] = fol
    return templates.TemplateResponse(request=request, name="home.html", context={
        "n_chunks": n, "n_docs": None, "convs": KBX.convs(actor=actor_id(request)), "cid": cid,
        "msgs": msgs, "title": title, "col": "", "col_stats": KBX.col_stats()})


@app.post("/api/ask")
def api_ask(request: Request):
    """非流式（兼容）"""
    raise HTTPException(400, "use /ask_stream")


@app.post("/ask_stream")
async def ask_stream(request: Request, q: str = Form(...), useweb: str = Form("off"),
               cid: str = Form(""), col: str = Form(""), mode: str = Form("")):

    actor = actor_id(request, required=True)
    if cid.strip():
        if not cid.strip().isdigit():
            raise HTTPException(400, "Invalid conversation")
        owned_conversation(request, int(cid))
    t0 = time.time()
    who = current_user(request) or ""
    recipe = (mode == "recipe")

    # 检索走 hybrid_async：异步包装内部仍是 asyncio.to_thread，排序逐字节不变，
    # 但事件循环交回去给其它请求用，而不是陪着整段 prep 阻塞。
    # recipe 分支多路聚合，直接丢线程池即可。
    if recipe:
        chunks, stats = await asyncio.to_thread(
            lambda: (hybrid_recipe(q, k=6), {"mode": "recipe", "n": 0}))
        stats["n"] = len(chunks)
    else:
        chunks, stats = await KBX.hybrid_async(q, k=6, col=col)

    # 联网检索（可选）与 SQLite 写入保留各自的 to_thread：职责清晰，一处失败不拖累另一处。
    web, winfo = ([], {"ms": 0, "fail": []})
    if useweb == "on":
        web, winfo = await asyncio.to_thread(websearch, q, email=who)

    def _state_writes():
        conv_id_local = int(cid) if cid.strip().isdigit() else KBX.new_conv(q, actor=actor)
        history_local = [{"role": m["role"], "content": m["content"]}
                         for m in KBX.msgs(conv_id_local, actor=actor)][-4:]
        KBX.add_msg(conv_id_local, "user", q, {"useweb": useweb}, actor=actor)
        return conv_id_local, history_local

    conv_id, history = await asyncio.to_thread(_state_writes)
    payload = {
        "conv_id": conv_id,
        "stats": stats,
        "winfo": winfo,
        "web": web,
        "chunks": [{"path": c["path"], "heading": c["heading"], "section": c.get("section", ""),
                    "anchor": c.get("anchor", ""), "rrf": c["rrf"],
                    "preview": c["preview"],
                    "imgs": imgs_for_doc(c["path"], 6, c.get("section", ""))} for c in chunks],
    }

    def sse(event, data):
        return f"event: {event}\ndata: {json.dumps(data, ensure_ascii=False)}\n\n"

    async def gen():
        yield sse("meta", payload)
        yield sse("stage", {"text": "正在连接模型…"})
        prompt = (build_prompt_recipe(q, chunks, web, history) if recipe
              else build_prompt(q, chunks, web, history))
        buf, used, thinking, last_stage = [], "", False, 0.0
        for c in llm_chain():
            try:
                async with httpx.AsyncClient(timeout=180) as cli:
                    async with cli.stream("POST", c["url"],
                                          headers={"Authorization": f"Bearer {c['key']}"},
                                          json={"model": c["model"],
                                                "messages": [{"role": "system",
                              "content": SYS_RECIPE if recipe else SYS},
                                                             {"role": "user", "content": prompt}],
                                                "temperature": 0.2, "max_tokens": 3200,
                                                "stream": True}) as r:
                        r.raise_for_status()
                        used = c["model"]
                        async for line in r.aiter_lines():
                            if not line or not line.startswith("data: "):
                                continue
                            d = line[6:]
                            if d == "[DONE]":
                                break
                            try:
                                j = json.loads(d)
                            except Exception:
                                continue
                            delta = (j.get("choices") or [{}])[0].get("delta") or {}
                            rc = delta.get("reasoning_content")
                            if rc:
                                if not thinking:
                                    thinking = True
                                    yield sse("stage", {"text": "正在思考…"})
                                    last_stage = time.time()
                                elif time.time() - last_stage > 2.5:
                                    last_stage = time.time()
                                    yield sse("stage", {"text": "正在思考…（%ds）" % int(time.time() - t0)})
                            piece = delta.get("content") or ""
                            if piece:
                                buf.append(piece)
                                yield sse("delta", {"text": piece})
                        break
                # 推理模型可能把 max_tokens 全用在 reasoning 上，content 为空。
                # 空响应要当作失败降级，否则用户拿到一片空白。
                if not buf:
                    raise RuntimeError("模型返回空内容（推理额度可能耗尽）")
            except Exception as e:
                logger.warning(f"[llm] {c['model']} 失败: {type(e).__name__}: {e}")
                winfo["fail"].append({"model": c["model"],
                                      "error_type": type(e).__name__,
                                      "error_msg": str(e)[:200]})
                buf.clear()
                thinking = False
                yield sse("stage", {"text": f"{c['model']} 失败，切换下一个…"})
                continue
        text = "".join(buf).strip()
        answer, followups = split_followups(text)
        # 参数校验：改法模式给的参数名必须在参数索引里真实存在
        p_ok, p_near, p_bad = ([], [], [])
        note = ""
        if mode == "recipe":
            p_ok, p_near, p_bad = validate_params(answer)
            bits = []
            if p_bad:
                items = "、".join(f"`{x}`" for x in p_bad[:8])
                bits.append(f'<b>未在 Properties 表声明</b>（{len(p_bad)}）：{items}'
                            f'　<span style="opacity:.75">（多为代码内变量，材质面板里没有，改需动源码）</span>')
            if p_near:
                items = "、".join(f"`{a}`→`{b}`" for a, b in p_near[:5])
                bits.append(f'<b>近似</b>（{len(p_near)}）：{items}')
            if bits:
                note = (f'<div style="margin-top:16px;padding:11px 14px;border-left:3px solid #d97706;'
                        f'background:rgba(217,119,6,.08);border-radius:6px;font-size:13px;line-height:1.7">'
                        f'<b>参数校验</b>：已确认 {len(p_ok)} 个参数存在；'
                        + '；'.join(bits)
                        + '。抄改前请核对目标 shader 的实际面板。</div>')
            elif p_ok:
                note = (f'<div style="margin-top:16px;padding:9px 14px;border-left:3px solid #16a34a;'
                        f'background:rgba(22,163,74,.07);border-radius:6px;font-size:13px">'
                        f'<b>参数校验</b>：引用的 {len(p_ok)} 个参数名全部在库中确认存在</div>')
        mid = await asyncio.to_thread(KBX.add_msg, conv_id, "assistant", text,
                          {"model": used, "ms": int((time.time() - t0) * 1000),
                           "stats": stats, "web": web, "chunks": payload["chunks"],
                           "pcheck": {"ok": len(p_ok), "near": p_near, "bad": p_bad}}, actor=actor)
        yield sse("done", {"conv_id": conv_id, "msg_id": mid, "followups": followups,
                           "model": used, "ms": int((time.time() - t0) * 1000),
                           "cached": bool(winfo.get("cached")),
                           "quota": winfo.get("quota", ""),
                           "pcheck": {"ok": len(p_ok), "near": p_near, "bad": p_bad},
                           "html": linkify(md2html(answer)) + note})

    return StreamingResponse(gen(), media_type="text/event-stream",
                             headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"})


def _mem_mb():
    try:
        with open("/proc/self/status") as f:
            for line in f:
                if line.startswith("VmRSS:"):
                    return round(int(line.split()[1]) / 1024)
    except Exception:
        pass
    return 0


@app.get("/status", response_class=HTMLResponse)
def status_page(request: Request):
    st = KBX.stats()
    st["rss_mb"] = _mem_mb()
    st["build"] = templates.env.globals.get("foot_build", "-")
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="status.html",
                                      context={"st": st, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request))})


@app.get("/about", response_class=HTMLResponse)
def about_page(request: Request):
    st = KBX.stats()
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="about.html",
                                      context={"st": st, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request))})



@app.post("/c/{cid}/rename")
def conv_rename(request: Request, cid: int, title: str = Form(...)):
    KBX.rename_conv(cid, title, actor=owned_conversation(request, cid))
    return RedirectResponse(f"/c/{cid}", status_code=303)


@app.get("/c/{cid}/export")
def conv_export(request: Request, cid: int):
    title = KBX.conv_title(cid, actor=owned_conversation(request, cid))
    if title is None:
        raise HTTPException(404)
    lines = [f"# {title}", ""]
    for m in KBX.msgs(cid, actor=actor_id(request, required=True)):
        who = "用户" if m["role"] == "user" else "助手"
        lines.append(f"## {who}")
        lines.append("")
        lines.append(m["content"])
        lines.append("")
    body = "\n".join(lines)
    fn = re.sub(r"[^\w\-]+", "_", title)[:40] or f"conv{cid}"
    disp = "attachment; filename=\"conv%d.md\"; filename*=UTF-8''%s" % (cid, quote(fn + ".md"))
    return Response(body, media_type="text/markdown; charset=utf-8",
                    headers={"Content-Disposition": disp})


@app.post("/c/{cid}/delete")
def conv_delete(request: Request, cid: int):
    KBX.del_conv(cid, actor=owned_conversation(request, cid))
    return RedirectResponse("/", status_code=303)



@app.get("/search", response_class=HTMLResponse)
def search_page(request: Request, q: str = ""):
    col = request.query_params.get("col", "")
    n, _ = KBX.counts(col)
    hits = KBX.search(q, 40, col) if q.strip() else []
    for h in hits:
        # 先整段转义、只还原 <mark>，用户/LLM 文本无法注入标签
        h["snippet"] = sanitize.highlight_snippet(h["snippet"])
    groups = {}
    for h in hits:
        top = h["path"].split("/")[0] if "/" in h["path"] else "（根）"
        groups.setdefault(top, []).append(h)
    return templates.TemplateResponse(request=request, name="search.html", context={
        "q": q, "hits": hits, "groups": sorted(groups.items()), "n_chunks": n,
        "convs": KBX.convs(actor=actor_id(request)), "col": col, "col_stats": KBX.col_stats()})


@app.get("/manual", response_class=HTMLResponse)
def manual_home(request: Request):
    """手册封面：章节 + 参考层"""
    d = _load_manual()
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="manual.html", context={
        "chapters": d.get("chapters") or [], "reference": d.get("reference") or [],
        "total": d.get("total", 0), "manual_total": d.get("manual_total", 0),
        "ref_total": d.get("ref_total", 0), "dropped": d.get("dropped", 0),
        "built": d.get("built", ""), "n_chunks": n, "convs": KBX.convs(actor=actor_id(request))})


@app.get("/manual/{cid}", response_class=HTMLResponse)
def manual_chapter(request: Request, cid: str, loose: int = 0):
    """章节目录页：有序文章列表"""
    d = _load_manual()
    grp = None
    for g in (d.get("chapters") or []) + (d.get("reference") or []):
        if g["id"] == cid:
            grp = g
            break
    if not grp:
        raise HTTPException(404)
    docs = grp["docs"]
    tight = [x for x in docs if not x.get("loose")]
    loose_docs = [x for x in docs if x.get("loose")]
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="chapter.html", context={
        "g": grp, "docs": docs, "tight": tight, "loose_docs": loose_docs,
        "show_loose": bool(loose), "n_chunks": n, "convs": KBX.convs(actor=actor_id(request)),
        "is_ref": cid in [r["id"] for r in (d.get("reference") or [])]})


@app.get("/browse", response_class=HTMLResponse)
def browse(request: Request):
    col = request.query_params.get("col", "")
    n, _ = KBX.counts(col)
    return templates.TemplateResponse(request=request, name="browse.html", context={
        "tree": KBX.tree(col), "docs": KBX.doc_list(col), "n_chunks": n,
        "convs": KBX.convs(actor=actor_id(request)), "col": col, "col_stats": KBX.col_stats()})


@app.get("/export/all")
def export_all(request: Request):
    """整库导出：所有 markdown 打包成 tar.gz"""
    import tarfile, io as _io
    col = request.query_params.get("col", "")
    base = os.path.join(DATA, "col", col) if col else os.path.join(DATA, "docs")
    buf = _io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w:gz") as tar:
        for row in KBX.doc_list(col):
            p = os.path.join(base, row[0])
            if os.path.exists(p):
                tar.add(p, arcname=row[0])
    buf.seek(0)
    disp = "attachment; filename=kks-kb-docs.tar.gz"
    return Response(buf.read(), media_type="application/gzip",
                    headers={"Content-Disposition": disp})




@app.post("/api/feedback")
async def api_feedback(request: Request):
    who = current_user(request) or ""
    if not who:
        return JSONResponse({"ok": False, "msg": "未登录"})
    ct = (request.headers.get("content-type") or "").lower()
    if "application/json" in ct:
        try:
            body = await request.json()
        except Exception:
            return JSONResponse({"ok": False, "msg": "JSON 解析失败"})
        msg_id, vote = body.get("msg_id"), body.get("vote")
        note = str(body.get("note") or "")[:200]
        path = str(body.get("path") or "").strip()[:500]
    else:   # 兼容旧的表单提交（msg_id/vote/note）
        form = await request.form()
        msg_id, vote = form.get("msg_id"), form.get("vote")
        note = str(form.get("note") or "")[:200]
        path = str(form.get("path") or "").strip()[:500]
    try:
        msg_id = int(msg_id)
        v = int(vote)
        if v not in (-1, 0, 1):
            raise ValueError("Invalid vote")
    except (TypeError, ValueError):
        return JSONResponse({"ok": False, "msg": "参数错误"})
    actor = actor_id(request, required=True)
    now = datetime.now().isoformat(timespec="seconds")
    with STATE._lock, STATE.con:
        STATE.execute("BEGIN IMMEDIATE")
        message = STATE.fetchone("SELECT m.role,m.meta FROM msg m JOIN conv c ON c.id=m.conv_id WHERE m.id=? AND c.owner_user_id=?", (msg_id, actor))
        if not message:
            raise HTTPException(404)
        if message[0] != "assistant":
            raise HTTPException(400, "Feedback requires an assistant message")
        if path:
            try:
                meta = json.loads(message[1] or "{}")
                chunks = meta.get("chunks", []) if isinstance(meta, dict) else []
                cited = {c.get("path") for c in chunks if isinstance(c, dict) and isinstance(c.get("path"), str)} if isinstance(chunks, list) else set()
            except (ValueError, TypeError):
                cited = set()
            if path not in cited:
                raise HTTPException(400, "Path is not cited by this message")
        previous = STATE.fetchone("SELECT id,vote FROM feedback WHERE actor_user_id=? AND msg_id=? AND path=?", (actor, msg_id, path))
        old = previous[1] if previous else 0
        if previous:
            STATE.execute("UPDATE feedback SET vote=?,note=?,created=? WHERE id=?", (v, note, now, previous[0]))
        elif v:
            STATE.execute("INSERT INTO feedback(msg_id,email,vote,note,path,created,actor_user_id) VALUES(?,?,?,?,?,?,?)", (msg_id, who, v, note, path, now, actor))
        if path:
            STATE.execute(
                "INSERT INTO doc_votes(path,up,down,updated) VALUES(?,?,?,?) "
                "ON CONFLICT(path) DO UPDATE SET up=doc_votes.up+excluded.up, down=doc_votes.down+excluded.down, updated=excluded.updated",
                (path, int(v == 1) - int(old == 1), int(v == -1) - int(old == -1), now))
    return JSONResponse({"ok": True})


@app.post("/api/feedback/reset")
def api_feedback_reset(request: Request):
    who = current_user(request) or ""
    if not who:
        return JSONResponse({"ok": False, "msg": "未登录"})
    if not _is_admin(who):
        raise HTTPException(403)
    with STATE._lock, STATE.con:
        STATE.execute("UPDATE feedback SET vote=0")
        STATE.execute("DELETE FROM doc_votes")
    return JSONResponse({"ok": True})


@app.get("/feedback", response_class=HTMLResponse)
def feedback_page(request: Request):
    actor = actor_id(request, required=True)
    agg_rows = STATE.fetchall(
        "SELECT path,SUM(vote=1),SUM(vote=-1) FROM feedback WHERE actor_user_id=? GROUP BY path LIMIT 200", (actor,))
    aggs = [{"path": r[0], "up": r[1], "down": r[2], "net": r[1] - r[2]} for r in agg_rows]
    recent_rows = STATE.fetchall(
        "SELECT id, msg_id, email, vote, COALESCE(path,''), note, created "
        "FROM feedback WHERE actor_user_id=? ORDER BY id DESC LIMIT 50", (actor,))
    recent = [{"id": r[0], "msg_id": r[1], "email": r[2], "vote": r[3],
               "path": r[4], "note": r[5], "created": r[6]} for r in recent_rows]
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="feedback.html", context={
        "aggs": aggs, "recent": recent, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request)),
        "col": "", "col_stats": KBX.col_stats(), "cid": None})


@app.get("/forgot", response_class=HTMLResponse)
def forgot_page(request: Request):
    n, d = KBX.counts()
    return templates.TemplateResponse(request=request, name="forgot.html", context={
        "n_chunks": n, "n_docs": d})


@app.post("/reset_password")
async def reset_password(request: Request, email: str = Form(...), code: str = Form(...),
                         password: str = Form(...),
                         cf_turnstile_response: str = Form("", alias="cf-turnstile-response")):
    ip = ip_of(request)
    email = email.strip().lower()
    ts_ok, ts_err = await verify_turnstile(cf_turnstile_response, ip, CF_TURNSTILE_SECRET)
    if not ts_ok:
        return JSONResponse({"ok": False, "msg": "人机校验未通过，请重试。", "turnstile": ts_err})
    if not _rate_ok(ip, email):
        return JSONResponse({"ok": False, "msg": "尝试过多，请稍后再试"})
    if len(password) < sec.MIN_PASSWORD_LEN:
        return JSONResponse({"ok": False, "msg": f"密码至少 {sec.MIN_PASSWORD_LEN} 位"})
    if not _user_exists(email):
        return JSONResponse({"ok": False, "msg": "这个邮箱没有注册过"})
    ok, msg = _verify_code(email, code)
    if not ok:
        _add_fail(ip, email)
        return JSONResponse({"ok": False, "msg": msg})
    STATE.execute("UPDATE users SET pass=? WHERE email=?", (sec.hash_password(password), email))
    # 旧会话全部失效：换 secret 后缀即可，这里用 last_login 时间参与签名不划算，
    # 直接给该用户打一个失效时间戳
    STATE.execute("UPDATE users SET last_login=? WHERE email=?",
                  (datetime.now().isoformat(timespec="seconds"), email))
    STATE.commit()
    logger.info("[reset] %s", email)
    return _issue_session(email, request)


@app.get("/api/quota")
def api_quota(request: Request):
    who = current_user(request) or ""
    if not who:
        return JSONResponse({"ok": False})
    day = datetime.now().strftime("%Y-%m-%d")
    row = STATE.fetchone("SELECT n FROM quota WHERE email=? AND day=?", (who, day))
    used = row[0] if row else 0
    return JSONResponse({"ok": True, "used": used, "limit": QUOTA_PER_DAY,
                         "left": max(0, QUOTA_PER_DAY - used)})


@app.get("/api/docs")
def api_docs(q: str = "", limit: int = 8):
    rows = KBX.doc_list()
    ql = q.strip().lower()
    out = []
    for path, title, chars in rows:
        hay = (title + " " + path).lower()
        if ql and ql not in hay:
            continue
        out.append({"title": title, "path": path, "chars": chars})
        if len(out) >= limit:
            break
    return JSONResponse({"items": out, "total": len(rows)})



@app.get("/download/manual")
def download_manual(request: Request):
    """离线版手册（双击 index.html 即可用，含本地搜索）"""
    p = os.path.join(DATA, "download", "kks-manual-offline.zip")
    if not os.path.exists(p):
        raise HTTPException(404)
    with open(p, "rb") as f:
        data = f.read()
    disp = "attachment; filename=\"kks-manual-offline.zip\"; filename*=UTF-8''%s" % quote("KKS手册-离线版.zip")
    return Response(data, media_type="application/zip",
                    headers={"Content-Disposition": disp})


def _load_source_index():
    """源文件索引（懒加载）"""
    p = os.path.join(DATA, "source-files", "index.json")
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return []


@app.get("/api/params")
def api_params(q: str = "", limit: int = 20):
    """参数查询：q 为空时返回统计"""
    if not PARAM_IDX:
        return JSONResponse({"ok": False, "msg": "参数索引未载入", "items": []})
    if not q.strip():
        top = sorted(PARAM_IDX.items(), key=lambda kv: -kv[1]["c"])[:limit]
        return JSONResponse({"ok": True, "total": len(PARAM_IDX), "loaded_at": PARAM_IDX_AT[0],
                             "items": [{"name": k, **v} for k, v in top]})
    kw = q.strip().lstrip("_").lower()
    hits = []
    for k, v in PARAM_IDX.items():
        kl = k.lstrip("_").lower()
        if kw == kl:
            hits.insert(0, (k, v))
        elif kw in kl:
            hits.append((k, v))
    hits = hits[:limit]
    return JSONResponse({"ok": True, "query": q, "count": len(hits),
                         "items": [{"name": k, **v} for k, v in hits]})


def _param_hits(kw):
    """参数名检索：精确命中排最前，其余子串命中随后。
    口径与 /api/params 一致（去前导下划线 + 小写后比较）。"""
    hits = []
    for k, v in PARAM_IDX.items():
        kl = k.lstrip("_").lower()
        if kw == kl:
            hits.insert(0, (k, v))
        elif kw in kl:
            hits.append((k, v))
    return hits


def _find_param(name):
    """参数详情：先精确键名，再走 lookup_param 的大小写/下划线近似。"""
    if not name:
        return None, None
    if name in PARAM_IDX:
        return name, PARAM_IDX[name]
    v = lookup_param(name)
    if v is None:
        return None, None
    for k, val in PARAM_IDX.items():
        if val is v:
            return k, v
    return None, None


@app.get("/params", response_class=HTMLResponse)
def params_page(request: Request, q: str = ""):
    """G1 参数总索引：搜索 + 全表，默认值分歧打标。"""
    n, _ = KBX.counts()
    if q and q.strip():
        items = [{"name": k, "v": v} for k, v in _param_hits(q.strip().lstrip("_").lower())]
    else:
        items = [{"name": k, "v": v} for k, v in
                 sorted(PARAM_IDX.items(), key=lambda kv: (kv[0].lstrip("_").lower(), kv[0]))]
    return templates.TemplateResponse(request=request, name="params.html", context={
        "q": q, "items": items, "total": len(PARAM_IDX),
        "loaded_at": PARAM_IDX_AT[0], "n_chunks": n, "convs": KBX.convs(actor=actor_id(request))})


@app.get("/param/{name}", response_class=HTMLResponse)
def param_page(request: Request, name: str):
    """G1 参数详情：各家族默认值分布、分歧提示、来源链接。"""
    key, v = _find_param(name)
    if v is None:
        raise HTTPException(404)
    n, _ = KBX.counts()
    dv = v.get("dv") or {}
    return templates.TemplateResponse(request=request, name="param_detail.html", context={
        "name": key, "v": v, "dv": sorted(dv.items(), key=lambda kv: -kv[1]),
        "divergent": len(dv) > 1, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request))})


@app.get("/source", response_class=HTMLResponse)
def source_list(request: Request):
    """源文件归档列表"""
    items = _load_source_index()
    groups = {}
    for it in items:
        g = (it.get("path") or "").split("/")[0] or "其他"
        groups.setdefault(g, []).append(it)
    return templates.TemplateResponse(request=request, name="source.html", context={
        "items": items, "groups": sorted(groups.items()),
        "total": len(items),
        "mb": round(sum(i.get("size_bytes", 0) for i in items) / 1024 / 1024, 1),
        "user": current_user(request),
    })


@app.get("/source/file")
def source_file(path: str, request: Request, download: bool = False):
    """下载/预览源文件"""
    if ".." in path or path.startswith("/"):
        raise HTTPException(400)
    p = os.path.join(DATA, "source-files", path)
    if not os.path.isfile(p):
        raise HTTPException(404)
    ext = os.path.splitext(p)[1].lower()
    mt = {".pdf": "application/pdf", ".png": "image/png",
          ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
          ".html": "text/html; charset=utf-8"}.get(ext, "application/octet-stream")
    disp = "inline" if not download and mt.startswith(("image/", "application/pdf", "text/html")) else "attachment"
    filename = os.path.basename(p)
    if path.startswith("originals/"):
        filename = next((r['original_filename'] for r in _load_source_index()
                         if isinstance(r, dict) and r.get('path') == path
                         and isinstance(r.get('original_filename'), str) and r['original_filename']), filename)
    hdrs = {"Content-Disposition": source_content_disposition(filename, disp == "inline"),
            "Cache-Control": "private, max-age=86400"}
    if mt.startswith("text/html"):
        # 归档的第三方网页不可信：sandbox 会把它当作独立源，脚本拿不到本站会话
        hdrs["Content-Security-Policy"] = "sandbox"
    return FileResponse(p, media_type=mt, headers=hdrs)


@app.get("/gallery", response_class=HTMLResponse)
def gallery(request: Request, cat: str = "", album: str = "", page: int = 1):
    """教程配图画廊（分页，每页 240 张）；album 按教程分册过滤"""
    mf = _load_img_manifest()
    items = [x for x in mf
             if (not cat or x["cat"] == cat) and (not album or album_of(x) == album)]

    counts = {}
    for x in mf:
        counts[x["cat"]] = counts.get(x["cat"], 0) + 1
    cats = sorted(counts.items(), key=lambda kv: -kv[1])

    albums = {}
    for x in mf:
        a = album_of(x)
        if not a:
            continue
        d = albums.setdefault(a, {"name": a, "cat": x["cat"], "n": 0, "thumb": x["thumb"]})
        d["n"] += 1
    album_list = sorted(albums.values(), key=lambda d: (-d["n"], d["name"]))

    per = 240
    total = len(items)
    pages = max(1, (total + per - 1) // per)
    page = min(max(1, page), pages)
    view = items[(page - 1) * per: page * per]

    om = _load_ocr_map()
    # 由 basename 反查真实文档路径（真实路径带库前缀，如 web/img-ocr/x.md）
    real = {}
    known = set()
    for p, _t, _c in KBX.doc_list():
        known.add(p)
        if "img-ocr/" in p:
            real[os.path.basename(p)] = p
    ocr_of = {}
    for doc, lst in (om.get("by_doc") or {}).items():
        full = doc if doc in known else real.get(doc)
        if not full:
            continue
        for it in lst:
            ocr_of[it["thumb"]] = full
    return templates.TemplateResponse(request=request, name="gallery.html", context={
        "items": view, "cats": cats, "cat": cat, "page": page,
        "pages": pages, "shown": total, "total": len(mf),
        "albums": album_list, "album": album,
        "ocr_of": ocr_of, "ocr_docs": len(om.get("by_doc") or {}),
        "user": current_user(request),
    })


@app.get("/thumb/{cat}/{fn}")
def thumb(cat: str, fn: str, request: Request):
    """缩略图文件"""
    if "/" in cat or "\\" in cat or "/" in fn or "\\" in fn or ".." in cat or ".." in fn:
        raise HTTPException(400)
    p = os.path.join(DATA, "img", "thumbs", cat, fn)
    if not os.path.isfile(p):
        raise HTTPException(404)
    return FileResponse(p, media_type="image/jpeg",
                        headers={"Cache-Control": "public, max-age=604800"})


@app.get("/orig/{cat}/{fn}")
def orig(cat: str, fn: str, request: Request):
    """原图（全尺寸）。只允许 manifest 里登记过的组合，杜绝路径穿越"""
    if "/" in cat or "\\" in cat or "/" in fn or "\\" in fn or ".." in cat or ".." in fn:
        raise HTTPException(400)
    key = cat + "/" + fn
    hit = None
    for x in _load_img_manifest():
        if x.get("thumb") == key or x.get("orig") == key:
            hit = x
            break
    if not hit:
        raise HTTPException(404)
    p = os.path.join(DATA, "img", "orig", cat, fn)
    if not os.path.isfile(p):
        # 退回本地原始路径（开发机直跑时可用）
        p = os.path.join(GAME_ROOT, hit["orig"].replace("/", os.sep))
        if not os.path.isfile(p):
            raise HTTPException(404)
    ext = os.path.splitext(p)[1].lower()
    mt = {".png": "image/png", ".jpg": "image/jpeg",
          ".jpeg": "image/jpeg", ".webp": "image/webp", ".gif": "image/gif"}.get(ext, "image/jpeg")
    return FileResponse(p, media_type=mt,
                        headers={"Cache-Control": "public, max-age=604800"})


@app.get("/api/search")
def api_search(q: str, k: int = 8, col: str = ""):
    chunks, stats = KBX.hybrid(q, k=k, col=col)
    return JSONResponse({"query": q, "col": col, "stats": stats,
                         "items": [{"path": c["path"], "col": c.get("col", ""),
                                    "heading": c["heading"], "rrf": c["rrf"]}
                                   for c in chunks]})


# ---------------- 个人中心 ----------------
@app.get("/me", response_class=HTMLResponse)
def me_page(request: Request):
    """个人中心：身份、登录方式、近 7 天活跃统计。"""
    email = current_user(request) or ""
    row = STATE.fetchone(
        "SELECT pass, created, last_login FROM users WHERE email=?", (email,))
    if not row:
        raise HTTPException(404)
    pw, created, last_login = row
    is_oauth = (pw or "") == "oauth:linuxdo"
    login_method = "Linux.do" if is_oauth else "密码"
    recent_qa = STATE.fetchone(
        "SELECT COUNT(*) FROM msg m JOIN conv c ON c.id=m.conv_id WHERE m.role='user' "
        "AND m.created >= datetime('now','-7 days') AND c.owner_user_id=?", (actor_id(request, required=True),))[0]
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="me.html", context={
        "email": email, "created": created or "-", "last_login": last_login or "-",
        "is_oauth": is_oauth, "login_method": login_method,
        "recent_qa": recent_qa, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request)),
        "col": "", "col_stats": KBX.col_stats(), "cid": None})


# ---------------- 管理后台 ----------------
def _is_admin(email):
    return bool(email) and bool(ADMIN_EMAIL) and email == ADMIN_EMAIL


@app.get("/admin")
def admin_page(request: Request):
    """管理员仪表盘：用户 / 问答 / 反馈 / 0-召回 / 文档投票榜。"""
    who = current_user(request) or ""
    if not _is_admin(who):
        return JSONResponse({"ok": False, "msg": "仅限管理员"}, status_code=403)
    t0 = time.time()
    # 用户
    u_total = STATE.fetchone("SELECT COUNT(*) FROM users")[0]
    u_active7 = STATE.fetchone(
        "SELECT COUNT(*) FROM users WHERE last_login >= datetime('now','-7 days')")[0]
    u_new24 = STATE.fetchone(
        "SELECT COUNT(*) FROM users WHERE created >= datetime('now','-1 days')")[0]
    # 问答
    q_7d = STATE.fetchone(
        "SELECT COUNT(*) FROM msg WHERE role='user' "
        "AND created >= datetime('now','-7 days')")[0]
    q_24h = STATE.fetchone(
        "SELECT COUNT(*) FROM msg WHERE role='user' "
        "AND created >= datetime('now','-1 days')")[0]
    # 反馈
    f_docs = STATE.fetchone("SELECT COUNT(*) FROM doc_votes")[0]
    f_up7 = STATE.fetchone(
        "SELECT COUNT(*) FROM feedback WHERE vote>0 "
        "AND created >= datetime('now','-7 days')")[0]
    f_down7 = STATE.fetchone(
        "SELECT COUNT(*) FROM feedback WHERE vote<0 "
        "AND created >= datetime('now','-7 days')")[0]
    # 0-召回：近 7 天 assistant meta.chunks 为空的条数
    zero_recall = 0
    for (meta_text,) in STATE.fetchall(
            "SELECT meta FROM msg WHERE role='assistant' "
            "AND created >= datetime('now','-7 days')"):
        try:
            md = json.loads(meta_text or "{}")
            if not (md.get("chunks") or []):
                zero_recall += 1
        except Exception:
            pass
    # doc_votes 榜：净值前 5 + 倒数 5
    top_rows = STATE.fetchall(
        "SELECT path,up,down FROM doc_votes ORDER BY (up-down) DESC, up DESC LIMIT 5")
    bot_rows = STATE.fetchall(
        "SELECT path,up,down FROM doc_votes ORDER BY (up-down) ASC, down DESC LIMIT 5")
    top = [{"path": r[0], "up": r[1], "down": r[2], "net": r[1] - r[2]} for r in top_rows]
    bot = [{"path": r[0], "up": r[1], "down": r[2], "net": r[1] - r[2]} for r in bot_rows]
    query_ms = int((time.time() - t0) * 1000)
    n, _ = KBX.counts()
    return templates.TemplateResponse(request=request, name="admin.html", context={
        "u_total": u_total, "u_active7": u_active7, "u_new24": u_new24,
        "q_7d": q_7d, "q_24h": q_24h,
        "f_docs": f_docs, "f_up7": f_up7, "f_down7": f_down7,
        "zero_recall": zero_recall, "top_docs": top, "bot_docs": bot,
        "query_ms": query_ms, "n_chunks": n, "convs": KBX.convs(actor=actor_id(request)),
        "col": "", "col_stats": KBX.col_stats(), "cid": None})


# ---------------- sitemap / robots ----------------
_SITEMAP_CACHE = {"xml": "", "ts": 0.0}
_SITEMAP_TTL = 3600
_SITE_ROOT = str(CFG.get("site_url") or "http://127.0.0.1:8000").rstrip("/")


def _doc_lastmod(path):
    """文档物理路径的 mtime。多个候选目录找头一个能读到的；失败回退常量。"""
    for base in (os.path.join(DATA, "col", "kks"),
                 os.path.join(DATA, "docs"),
                 os.path.join(DATA, "col", "ai"),
                 os.path.join(DATA, "col", "ref")):
        p = os.path.join(base, path)
        if os.path.isfile(p):
            try:
                return datetime.fromtimestamp(os.path.getmtime(p)).strftime("%Y-%m-%d")
            except OSError:
                continue
    return "2026-11-01"


def _build_sitemap():
    parts = ['<?xml version="1.0" encoding="UTF-8"?>',
             '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">']
    fixed = [("/", "1.0"), ("/manual", "0.9"), ("/browse", "0.8"),
             ("/gallery", "0.8"), ("/params", "0.8")]
    today = datetime.now().strftime("%Y-%m-%d")
    for p, pri in fixed:
        parts.append(f"  <url><loc>{_SITE_ROOT}{p}</loc>"
                     f"<lastmod>{today}</lastmod>"
                     f"<changefreq>weekly</changefreq>"
                     f"<priority>{pri}</priority></url>")
    for row in KBX.doc_list():
        path = row[0]
        lm = _doc_lastmod(path)
        # path 里可能有 & / 空格，quote 一下作 query 值
        loc = f"{_SITE_ROOT}/doc?path={quote(path, safe='/')}"
        parts.append(f"  <url><loc>{loc}</loc>"
                     f"<lastmod>{lm}</lastmod>"
                     f"<changefreq>monthly</changefreq>"
                     f"<priority>0.5</priority></url>")
    parts.append("</urlset>")
    return "\n".join(parts)


@app.get("/sitemap.xml")
def sitemap_xml():
    now = time.time()
    xml = _SITEMAP_CACHE["xml"]
    if not xml or (now - _SITEMAP_CACHE["ts"]) >= _SITEMAP_TTL:
        xml = _build_sitemap()
        _SITEMAP_CACHE["xml"] = xml
        _SITEMAP_CACHE["ts"] = now
    return Response(xml, media_type="application/xml")


_ROBOTS_TXT = f"""User-agent: *
Allow: /
Disallow: /login
Disallow: /register
Disallow: /forgot
Disallow: /reset_password
Disallow: /me
Disallow: /admin
Disallow: /feedback
Disallow: /api/
Disallow: /source/
Disallow: /orig/
Disallow: /oauth/

Sitemap: {_SITE_ROOT}/sitemap.xml
"""


@app.get("/robots.txt")
def robots_txt():
    return Response(_ROBOTS_TXT, media_type="text/plain")


@app.get("/favicon.ico")
def favicon_ico():
    """浏览器自动请求 /favicon.ico，重定向到我们的 SVG。"""
    return RedirectResponse("/static/favicon.svg", status_code=301)


@app.get("/privacy", response_class=HTMLResponse)
def privacy_page(request: Request):
    n, d = KBX.counts()
    return templates.TemplateResponse(request=request, name="privacy.html",
                                      context={"n_chunks": n, "n_docs": d,
                                               "convs": KBX.convs(actor=actor_id(request)), "col": "",
                                               "col_stats": KBX.col_stats(), "cid": None})


@app.get("/tos", response_class=HTMLResponse)
def tos_page(request: Request):
    n, d = KBX.counts()
    return templates.TemplateResponse(request=request, name="tos.html",
                                      context={"n_chunks": n, "n_docs": d,
                                               "convs": KBX.convs(actor=actor_id(request)), "col": "",
                                               "col_stats": KBX.col_stats(), "cid": None})


@app.get("/doc", response_class=HTMLResponse)
def doc(request: Request, path: str, section: str = ""):
    # 参数原名 sec，与模块级 `from .. import security as sec` 同名；
    # 改成 section 以免遮蔽；模板侧继续读 "sec" 这个上下文键。
    body = KBX.doc_body(path)
    if body is None:
        raise HTTPException(404)
    n, _ = KBX.counts()
    imgs = imgs_for_doc(path, None)
    MD.reset()
    doc_view = load_doc_view(DATA, KBX.doc_col(path), path)
    if doc_view.get('kind') in ('word_semantic_flow', 'discuz_post_flow') and doc_view.get('indexed_body_sha256') != hashlib.sha256(body.encode()).hexdigest():
        doc_view = {}  # A changed indexed source must not receive a stale reading projection.
    semantic = doc_view.get('kind') == 'word_semantic_flow'
    reader_source = source_for_document(DATA, KBX.doc_col(path), path)
    if doc_view.get('kind') == 'discuz_post_flow' and (not reader_source or reader_source.get('sha256') != doc_view.get('source_sha256')):
        doc_view = {}
    if semantic and (not reader_source or reader_source.get('sha256') != doc_view.get('source_sha256')):
        doc_view = {}
        semantic = False
        reader_source = None
    if semantic:
        projection = prepare_reading_html(doc_view['html'], KBX.doc_col(path), path)
        html, inline = projection['html'], set()
        toc = projection['toc']
        reader_anchor_map = projection['anchor_map']
        # The stored HTML is already namespaced; keep its Word bookmarks only if the target survived sanitization.
        saved_aliases = doc_view.get('anchor_map')
        if isinstance(saved_aliases, dict):
            surviving_ids = set(reader_anchor_map.values())
            for alias, target in saved_aliases.items():
                if isinstance(target, str) and target in surviving_ids:
                    reader_anchor_map.setdefault(alias, target)
    elif doc_view.get('kind') == 'discuz_post_flow':
        projection = prepare_reading_html(doc_view['html'], KBX.doc_col(path), path)
        html, toc = projection['html'], projection['toc']
        inline = {i['thumb'] for i in imgs}
        reader_anchor_map = {}
    else:
        html, inline = rewrite_images(doc_view['html'] if doc_view else MD.convert(body), imgs)
        html = linkify(sanitize.clean_html(html))
        html = add_anchors(html, body)
        toc = [] if doc_view else extract_toc(body)
        reader_anchor_map = {}
    doc_meta = load_doc_metadata(DATA, KBX.doc_col(path), path)
    display_title = doc_meta['title'] or next((row[1] for row in KBX.doc_list(KBX.doc_col(path)) if row[0] == path), path.split('/')[-1])
    return templates.TemplateResponse(request=request, name="doc.html", context={
        "path": path, "title": reader_title(display_title),
        "body_html": html, "toc": toc,
        "doc_view_note": doc_view.get('note', ''),
        "reader_revision": doc_view.get('revision', '')[:16] if semantic else hashlib.sha256(body.encode()).hexdigest()[:16],
        "semantic_reader": semantic, "reader_anchor_map": reader_anchor_map,
        "reader_source": reader_source, "forum_reader": doc_view.get('kind') == 'discuz_post_flow',
        "reader_scope": hashlib.sha256(str(actor_id(request) or 'local').encode()).hexdigest()[:16],
        "chars": len(body), "sec": section or request.query_params.get('sec', ''), "n_chunks": n, "convs": KBX.convs(actor=actor_id(request)),
        "col": KBX.doc_col(path), "col_stats": KBX.col_stats(),
        "imgs": imgs, "reading_imgs": [] if semantic else [i for i in imgs if i['thumb'] not in inline],
        "album": album_of(imgs[0]) if imgs else "",
        "is_album": KBX.doc_col(path) == "album", "nav": manual_nav(path),
        "doc_meta": doc_meta})
