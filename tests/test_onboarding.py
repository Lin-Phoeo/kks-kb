#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""首页与历史对话的手动帮助。设置 KKS_TEST_BROWSER_CHANNEL（如 msedge）可启用独立无头浏览器。"""
import json
import os
import shutil
import sys
import tempfile
import unittest
from datetime import datetime, timedelta

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:  # pragma: no cover
    HAVE_DEPS = False

ADMIN = "ob@test.local"
ADMIN_PW = "ob-password-123"

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
        f.write("# 测试文档\n\n## 边缘光\n\n_RimColor 控制边缘光。\n")
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
                   "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                            "admin_password": ADMIN_PW}}, f)
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


def _login(c):
    return c.post("/login", data={"email": ADMIN, "password": ADMIN_PW})


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过")
class OnboardingTests(unittest.TestCase):
    def setUp(self):
        self.c = TestClient(_mod.app, follow_redirects=False)
        self.c.headers["Origin"] = "http://testserver"
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()
        # 每个用例把管理员的 created 刷到当下，默认视为新用户
        now = datetime.now().isoformat(timespec="seconds")
        _mod.STATE.execute("UPDATE users SET created=? WHERE email=?", (now, ADMIN))
        _mod.STATE.commit()
        r = _login(self.c)
        self.assertEqual(r.status_code, 303)

    def saved_conversation(self):
        actor = _mod.STATE.fetchone('SELECT id FROM users WHERE email=?', (ADMIN,))[0]
        cid = _mod.KBX.new_conv('历史测试', actor=actor)
        _mod.KBX.add_msg(cid, 'user', '上一次的问题', actor=actor)
        return cid

    @unittest.skipUnless(os.environ.get('KKS_TEST_BROWSER_CHANNEL'), '未启用独立无头浏览器测试')
    def test_browser_help_highlights_visible_page_controls(self):
        from itertools import product
        from urllib.parse import urlsplit
        from playwright.sync_api import sync_playwright, expect

        cid = self.saved_conversation()

        # 浏览器只回放隔离应用的 GET 响应，不连接站点或推理服务。
        def serve(route):
            url = urlsplit(route.request.url)
            if url.netloc != 'testserver' or route.request.method != 'GET':
                return route.abort()
            response = self.c.get(url.path + ('?' + url.query if url.query else ''))
            route.fulfill(status=response.status_code, body=response.content, headers=dict(response.headers))

        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, channel=os.environ['KKS_TEST_BROWSER_CHANNEL'])
            ctx = browser.new_context(viewport={'width': 1200, 'height': 850}, reduced_motion='reduce')
            try:
                ctx.route('**/*', serve)
                page = ctx.new_page()
                errors = []
                page.on('pageerror', lambda error: errors.append(str(error)))
                scenarios = [('/', '#learn-query'), (f'/c/{cid}', '#q')]
                for width, (path, target) in product((1200, 375), scenarios):
                    with self.subTest(path=path, width=width):
                        page.set_viewport_size({'width': width, 'height': 850})
                        page.goto('http://testserver' + path, wait_until='networkidle')
                        dialog = page.get_by_role('dialog')
                        expect(dialog).to_have_count(0, timeout=1000)
                        if path == '/':
                            expect(page.locator('#q')).to_be_hidden()
                        else:
                            expect(page.get_by_text('上一次的问题', exact=True)).to_be_visible()
                        help_button = page.get_by_role('button', name='引导', exact=False)
                        help_button.click(timeout=1500)
                        expect(dialog).to_be_visible()
                        expect(page.locator(target)).to_be_in_viewport()
                        box = page.locator(target).bounding_box()
                        halo = page.locator('.ob-halo').bounding_box()
                        self.assertIsNotNone(halo)
                        for axis, size in [('x', 'width'), ('y', 'height')]:
                            self.assertAlmostEqual(halo[axis] + halo[size] / 2,
                                                   box[axis] + box[size] / 2, delta=2)
                        page.keyboard.press('Escape')
                        expect(dialog).to_have_count(0)
                        expect(help_button).to_be_focused()
                        if path == '/':
                            page.get_by_role('link', name='问 AI 助教').click()
                        page.locator('#q').fill('保留的提问草稿')
                        expect(page.locator('#q')).to_have_value('保留的提问草稿')
                self.assertEqual(errors, [])
            finally:
                browser.close()

    def test_home_help_is_manual_for_new_and_returning_users(self):
        for age_minutes in (0, 15):
            with self.subTest(age_minutes=age_minutes):
                created = (datetime.now() - timedelta(minutes=age_minutes)).isoformat(timespec='seconds')
                _mod.STATE.execute('UPDATE users SET created=? WHERE email=?', (created, ADMIN))
                _mod.STATE.commit()
                response = self.c.get('/')
                self.assertEqual(response.status_code, 200)
                self.assertEqual(response.text.count('id="ob-help"'), 1)
                self.assertNotIn('window.KB_SHOW_ONBOARDING=true', response.text)
                self.assertIn('/static/onboard.js', response.text)
                self.assertIn('/static/onboard.css', response.text)

    def test_saved_conversation_keeps_help_and_followup(self):
        cid = self.saved_conversation()
        response = self.c.get(f'/c/{cid}')
        self.assertEqual(response.status_code, 200)
        self.assertIn('上一次的问题', response.text)
        self.assertEqual(response.text.count('id="ob-help"'), 1)
        self.assertIn('id="q"', response.text)
        self.assertIn(f'href="/c/{cid}/export"', response.text)


if __name__ == "__main__":
    unittest.main()
