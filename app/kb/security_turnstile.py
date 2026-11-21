#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Cloudflare Turnstile 后端校验：siteverify。

secret 空字串 → 功能关闭（返回 (True, "disabled")），模板侧 cf_turnstile_sitekey
为空时也不会渲染 widget，整个链路 trivially 禁用。
"""
import logging

import httpx

logger = logging.getLogger("kb.security.turnstile")

SITEVERIFY_URL = "https://challenges.cloudflare.com/turnstile/v0/siteverify"


async def verify_turnstile(token, remote_ip=None, secret=None, timeout=10.0):
    """校验 Turnstile token。

    返回 (ok, error_code)：
    - secret 为空 → (True, "disabled")，功能关闭
    - 通过 → (True, "")
    - 失败 → (False, error_code)：业务错取 data["error-codes"][0]，网络错为 "network-error"

    secret 走参数传入（而不是在模块里 import CFG），让单测可以不建全局 CFG。
    """
    s = (secret or "").strip()
    if not s:
        logger.info("[turnstile] 未配置 secret，跳过校验")
        return True, "disabled"
    data = {"secret": s, "response": token or ""}
    if remote_ip:
        data["remoteip"] = remote_ip
    try:
        async with httpx.AsyncClient(timeout=timeout) as cli:
            r = await cli.post(SITEVERIFY_URL, data=data)
            r.raise_for_status()
            j = r.json()
    except Exception as e:
        logger.warning("[turnstile] siteverify 网络错误: %s: %s", type(e).__name__, e)
        return False, "network-error"
    if j.get("success"):
        return True, ""
    codes = j.get("error-codes") or ["unknown"]
    code = codes[0] if codes else "unknown"
    logger.warning("[turnstile] 校验未通过: %s", code)
    return False, code
