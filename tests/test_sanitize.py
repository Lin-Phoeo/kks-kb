#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""app/sanitize.py 单测：nh3 净化、搜索片段转义、CSP 响应头。
运行：py -3.14 -m unittest tests.test_sanitize -v
"""
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "app"))

try:
    from kb import sanitize  # noqa: E402
    from kb import security as sec  # noqa: E402
    HAVE_NH3 = True
except Exception:   # pragma: no cover
    HAVE_NH3 = False

try:
    from fastapi import FastAPI  # noqa: E402
    from fastapi.responses import Response  # noqa: E402
    from fastapi.testclient import TestClient  # noqa: E402
    HAVE_FASTAPI = True
except Exception:
    HAVE_FASTAPI = False

CSP_EXPECTED = ("default-src 'self'; img-src 'self' data: https:; connect-src 'self'; "
                "object-src 'none'; base-uri 'none'; frame-ancestors 'self'; "
                "script-src 'self' 'unsafe-inline' https://challenges.cloudflare.com; "
                "style-src 'self' 'unsafe-inline'; "
                "frame-src https://challenges.cloudflare.com")


@unittest.skipUnless(HAVE_NH3, "缺 nh3")
class CleanHtmlTests(unittest.TestCase):
    def test_script_removed_with_content(self):
        out = sanitize.clean_html("<script>alert(1)</script><p>ok</p>")
        self.assertNotIn("<script", out)
        self.assertNotIn("alert(1)", out)
        self.assertIn("<p>ok</p>", out)

    def test_style_removed_with_content(self):
        out = sanitize.clean_html("<style>p{color:red}</style><p>ok</p>")
        self.assertNotIn("<style", out)
        self.assertNotIn("color:red", out)
        self.assertIn("<p>ok</p>", out)

    def test_iframe_object_embed_removed(self):
        out = sanitize.clean_html(
            '<iframe src="https://evil"></iframe><object data="x"></object>'
            '<embed src="x"><p>ok</p>')
        self.assertNotIn("<iframe", out)
        self.assertNotIn("<object", out)
        self.assertNotIn("<embed", out)
        self.assertIn("<p>ok</p>", out)

    def test_event_handlers_stripped(self):
        out = sanitize.clean_html('<img src="x.png" onerror="alert(1)" onclick="go()">')
        self.assertNotIn("onerror", out)
        self.assertNotIn("onclick", out)
        self.assertIn("<img", out)
        self.assertIn('src="x.png"', out)

    def test_javascript_href_rejected(self):
        out = sanitize.clean_html('<a href="javascript:alert(1)">x</a>')
        self.assertNotIn("javascript:", out)
        self.assertNotIn("href", out)

    def test_data_text_html_href_rejected(self):
        out = sanitize.clean_html('<a href="data:text/html,<b>x</b>">x</a>')
        self.assertNotIn("data:", out)
        self.assertNotIn("href", out)

    def test_javascript_img_src_rejected(self):
        out = sanitize.clean_html('<img src="javascript:alert(1)">')
        self.assertNotIn("javascript:", out)
        self.assertNotIn("src", out)
        self.assertIn("<img", out)

    def test_data_image_src_allowed(self):
        out = sanitize.clean_html('<img src="data:image/png;base64,AAAA">')
        self.assertIn("data:image/png", out)

    def test_tables_survive(self):
        out = sanitize.clean_html(
            '<table><thead><tr><th>a</th></tr></thead>'
            '<tbody><tr><td colspan="2">b</td></tr></tbody></table>')
        for token in ("<table", "<thead", "<tbody", "<tr", "<th", "<td", 'colspan="2"'):
            self.assertIn(token, out)

    def test_fenced_code_block_survives(self):
        out = sanitize.clean_html('<pre><code class="language-python">print(1)</code></pre>')
        self.assertIn("<pre>", out)
        self.assertIn("<code", out)
        self.assertIn('class="language-python"', out)

    def test_heading_ids_survive(self):
        out = sanitize.clean_html('<h2 id="h-abc">T</h2><h3 id="h-def">U</h3>')
        self.assertIn('id="h-abc"', out)
        self.assertIn('id="h-def"', out)

    def test_rel_noopener_added_to_http_links(self):
        out = sanitize.clean_html('<a href="https://example.com/x">y</a>')
        self.assertIn('rel="noopener noreferrer"', out)

    def test_citation_markers_survive(self):
        # linkify（在净化之后运行）把这些 [L1]/[W2] 转成 <span class="cite">，
        # 净化这一步不能把它们吃掉。
        out = sanitize.clean_html("<p>见 [L1] 与 [W2]。</p>")
        self.assertIn("[L1]", out)
        self.assertIn("[W2]", out)


@unittest.skipUnless(HAVE_NH3, "缺 nh3")
class SnippetTests(unittest.TestCase):
    def test_highlight_escapes_text_and_keeps_marks(self):
        out = sanitize.highlight_snippet("xx<mark>a<b<script></mark>yy")
        self.assertEqual(out, "xx<mark>a&lt;b&lt;script&gt;</mark>yy")

    def test_highlight_plain_text_fully_escaped(self):
        out = sanitize.highlight_snippet("a<b<script>")
        self.assertEqual(out, "a&lt;b&lt;script&gt;")
        self.assertNotIn("<script", out)
        self.assertNotIn("<b", out)


@unittest.skipUnless(HAVE_NH3 and HAVE_FASTAPI, "缺 nh3 或 fastapi")
class CspTests(unittest.TestCase):
    def _app_with_middleware(self):
        """最小 FastAPI 应用，复用 app.py 中间件里调用的同一个 header 函数。"""
        app = FastAPI()

        @app.middleware("http")
        async def security_gate(request, call_next):
            resp = await call_next(request)
            sec.apply_security_headers(resp.headers)
            return resp

        @app.get("/plain")
        def plain():
            return Response("ok")

        @app.get("/custom")
        def custom():
            return Response("ok", headers={"Content-Security-Policy": "sandbox"})

        return TestClient(app, follow_redirects=False)

    def test_csp_header_present(self):
        r = self._app_with_middleware().get("/plain")
        self.assertEqual(r.headers["content-security-policy"], CSP_EXPECTED)

    def test_csp_does_not_override_existing_header(self):
        r = self._app_with_middleware().get("/custom")
        self.assertEqual(r.headers["content-security-policy"], "sandbox")

    def test_other_security_headers_still_present(self):
        r = self._app_with_middleware().get("/plain")
        self.assertEqual(r.headers["x-content-type-options"], "nosniff")
        self.assertEqual(r.headers["referrer-policy"], "same-origin")
        self.assertEqual(r.headers["x-frame-options"], "SAMEORIGIN")


if __name__ == "__main__":
    unittest.main()
