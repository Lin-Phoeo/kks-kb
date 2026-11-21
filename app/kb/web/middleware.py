#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Web 中间件：同源校验 → 默认拒绝鉴权 → 安全响应头 + 404 页。"""
import logging

from fastapi import Request
from fastapi.responses import JSONResponse, RedirectResponse

from .. import security as sec
from .routes import ALLOWED_ORIGINS, app, authed, templates

logger = logging.getLogger("kb.web.middleware")


@app.middleware("http")
async def security_gate(request: Request, call_next):
    """统一入口：同源校验（CSRF）→ 默认拒绝鉴权 → 安全响应头。
    鉴权在这里一次完成，routes 内不再重复 authed() 检查。"""
    h = request.headers
    if not sec.origin_ok(request.method, h.get("origin", ""), h.get("referer", ""),
                         h.get("host", ""), h.get("x-forwarded-host", ""),
                         allowed_origins=ALLOWED_ORIGINS):
        return JSONResponse({"error": "cross-origin request blocked"}, status_code=403)
    path = request.url.path
    # /static/* 公开（CSS/JS 没必要登录才能加载）
    is_public = sec.is_public_path(path) or path.startswith("/static/")
    if not is_public and not authed(request):
        if request.method == "GET" and not path.startswith("/api/"):
            return RedirectResponse("/login", status_code=303)
        return JSONResponse({"error": "unauthorized"}, status_code=401)
    resp = await call_next(request)
    sec.apply_security_headers(resp.headers)
    return resp


def _error_page(request, status):
    # Error rendering must not depend on the database that may have failed.
    response = templates.TemplateResponse(request=request, name=f"{status}.html",
                                          context={}, status_code=status)
    response.headers["Cache-Control"] = "no-store"
    sec.apply_security_headers(response.headers)
    return response


@app.exception_handler(404)
async def not_found(request: Request, exc):
    return _error_page(request, 404)


@app.exception_handler(Exception)
async def internal_error(request: Request, exc):
    # Avoid logging query strings, credentials, or remote response bodies.
    logger.error("Unhandled request exception type=%s", type(exc).__name__)
    return _error_page(request, 500)
