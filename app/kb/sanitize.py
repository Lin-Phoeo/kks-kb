#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""渲染后 HTML 净化 + 搜索片段转义。

白名单依据 docs/security/2026-11-01-语料可执行HTML扫描.md §6：
放行 Python-Markdown 自身产出（tables / fenced_code / nl2br）与语料里的良性标记，
拒绝 script / style / iframe / object / embed / form / input / button / link / meta / base、
所有 on* 属性、style 属性，以及 javascript: / data:text/html / vbscript: 等协议。
"""
import html as _html

import nh3

TAGS = {
    "p", "h1", "h2", "h3", "h4", "h5", "h6", "br", "hr",
    "blockquote", "ul", "ol", "li", "pre", "code", "em", "strong",
    "del", "a", "img", "table", "thead", "tbody", "tr", "th", "td",
    "div", "details", "summary", "video", "sub", "sup", "b", "i",
}

# 任务书 §C2 的扁平属性白名单，按 nh3 的要求拆到各标签上。
_ATTRS = {
    "a": {"href", "title", "id", "class"},
    "img": {"src", "alt", "title", "width", "height", "align", "id", "class", "data-original", "loading", "decoding"},
    "p": {"align", "id", "class"},
    "div": {"align", "id", "class"},
    "table": {"align", "width", "id", "class"},
    "th": {"colspan", "rowspan", "align", "width", "height", "id", "class"},
    "td": {"colspan", "rowspan", "align", "width", "height", "id", "class"},
    "h1": {"id", "class"}, "h2": {"id", "class"}, "h3": {"id", "class"},
    "h4": {"id", "class"}, "h5": {"id", "class"}, "h6": {"id", "class"},
    "pre": {"id", "class"},
    "code": {"id", "class"},
    "video": {"src", "width", "height", "title", "id", "class"},
}

# href/src 的协议白名单；data: 由 _safe_url 再细分（只放行 data:image/*）。
_URL_SCHEMES = {"http", "https", "mailto", "data"}


def _safe_url(value, allow_data):
    """href/src 的协议白名单。无 scheme（相对路径/锚点）放行，其余按协议判断。"""
    v = (value or "").strip()
    if not v:
        return None
    if ":" not in v:
        return v
    scheme = v.split(":", 1)[0].lower()
    if scheme in ("http", "https", "mailto"):
        return v
    if allow_data and scheme == "data":
        # 只放行 data:image/*（语料里的内联图）；data:text/html 等一律拒绝
        if v.lower().startswith("data:image/"):
            return v
        return None
    return None


def _attr_filter(tag, attr, value):
    """nh3 的 attribute_filter：只在它已经按 _ATTRS 放行的属性上再收紧 URL 协议。"""
    if attr == "href":
        return _safe_url(value, allow_data=False)
    if attr == "src":
        return _safe_url(value, allow_data=True)
    if attr == "data-original":
        return _safe_url(value, allow_data=False)
    if attr == "loading":
        return value if value in ("lazy", "eager") else None
    if attr == "decoding":
        return value if value in ("async", "sync", "auto") else None
    return value


def clean_html(html, *, reader=False):
    """Only the semantic reader opts into figure captions and list numbering."""
    tags = TAGS
    attrs = _ATTRS
    if reader:
        tags = TAGS | {'figure', 'figcaption', 'span'}
        attrs = {**_ATTRS, 'figure': {'id', 'class'}, 'figcaption': {'id', 'class'},
                 'span': {'id'}, 'ol': {'id', 'start'}, 'ul': {'id'}, 'li': {'id', 'value'}}
    return nh3.clean(
        html,
        tags=tags,
        attributes=attrs,
        attribute_filter=_attr_filter,
        url_schemes=_URL_SCHEMES,
        link_rel="noopener noreferrer",
    )


def highlight_snippet(snippet):
    """搜索片段：先整段 HTML 转义，再把 SQLite snippet() 生成的 <mark> 还原。
    这样用户/LLM 文本里的 < > 永远不可能成为标签。"""
    escaped = _html.escape(snippet or "")
    escaped = escaped.replace("&lt;/mark&gt;", "</mark>")
    escaped = escaped.replace("&lt;mark&gt;", "<mark>")
    return escaped
