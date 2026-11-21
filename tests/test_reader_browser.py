"""Reading controls against a real temporary FastAPI app and original synthetic text."""
from contextlib import contextmanager
import hashlib
import json
import os
import secrets
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
from urllib.parse import urlencode, urlsplit

from fastapi.testclient import TestClient

ROOT = Path(__file__).resolve().parents[1]
WIDTHS = ((1440, 900), (375, 812), (320, 720))
SCOPE = '这份合成资料仅用于阅读器测试。适用游戏、插件及 Shader 版本尚未逐项核定，操作前请对照原文说明。'
FLOW_NOTE = '由所存 Word 原件生成阅读版；保留原文图序，未知版本与未实测状态不变。'


class ReaderBrowserTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        cls.addClassCleanup(cls.tmp.cleanup)
        root = Path(cls.tmp.name)
        shutil.copytree(ROOT / 'app', root / 'app', ignore=shutil.ignore_patterns('__pycache__'))
        data = root / 'data'
        data.mkdir()
        docs = root / 'docs'
        docs.mkdir()
        cls.path = 'synthetic-reader.md'
        (docs / cls.path).write_text('# 阅读步骤与参数核对\n\n仅用于合成阅读器测试。\n', encoding='utf-8')
        password = secrets.token_urlsafe(24)
        (root / 'config.json').write_text(json.dumps({'auth': {'secret': 's' * 40,
                'admin_email': 'reader@example.invalid', 'admin_password': password},
                'llm_url': 'http://127.0.0.1:1/v1', 'llm_key': '', 'gitee_key': ''}), encoding='utf-8')
        cls.old_path = sys.path[:]
        cls.addClassCleanup(lambda: sys.path.__setitem__(slice(None), cls.old_path))
        for name in list(sys.modules):
            if name in {'app', 'kb', 'indexer'} or name.startswith('kb.'):
                del sys.modules[name]
        sys.path.insert(0, str(root / 'app'))
        import indexer
        indexer.build(str(data / 'kb.db'), str(docs), do_embed=False, collection='demo')
        import app
        cls.mod = app
        cls.addClassCleanup(app.KBX.con.close)
        cls.addClassCleanup(app.STATE.close)
        from kb.content.reading_view import prepare_reading_html
        parts = ['<h1 id="intro">开始阅读</h1><p id="opening">正文从这里开始：先查看参数，再对照当前环境。这是原创合成示例。</p>',
                 '<p><a href="#word-note">跳到原文书签</a> · <a href="#folded-note">跳到折叠书签</a> · <a href="#%E0%A4%A">格式异常的书签</a></p>']
        for number in range(1, 15):
            parts += [f'<h2 id="section-{number}">第 {number} 节 · 参数核对</h2>',
                      '<p>' + ('先保留原始设置，再逐项核对示例中的参数与说明。' * 7) + '</p>']
            if number == 7:
                parts += ['<p><a id="word-note"></a>原文内联书签：此行应完整显示在工具栏下方。</p>']
            if number == 8:
                parts += ['<details id="folded-outer"><summary>折叠补充</summary><details id="folded-inner"><summary>进一步说明</summary><p id="folded-note">折叠书签目标。</p></details></details>',
                          '<details id="unrelated-fold"><summary>无关内容</summary><p>不应自动展开。</p></details>']
        view = prepare_reading_html(''.join(parts), 'demo', cls.path)
        cls.anchors = view['anchor_map']
        original = b'Original synthetic archive fixture. No private document content.'
        source = data / 'source-files/originals/demo.docx'
        source.parent.mkdir(parents=True)
        source.write_bytes(original)
        cls.source_sha = hashlib.sha256(original).hexdigest()
        body = app.KBX.doc_body(cls.path)
        view.update(kind='word_semantic_flow', enabled=True,
                    revision=hashlib.sha256(view['html'].encode()).hexdigest(),
                    indexed_body_sha256=hashlib.sha256(body.encode()).hexdigest(),
                    source_sha256=cls.source_sha, note=FLOW_NOTE,
                    checks={'full_text_order_equal': True, 'images_in_source_order': True})
        (data / 'doc_views.json').write_text(json.dumps({'documents': {'demo:' + cls.path: view}}), encoding='utf-8')
        (data / 'source-files/index.json').write_text(json.dumps([{
            'doc_key': 'demo:' + cls.path, 'role': 'original_docx', 'path': 'originals/demo.docx',
            'original_filename': '阅读步骤与参数核对.docx', 'size_bytes': len(original),
            'sha256': cls.source_sha, 'author': '示例作者'}]), encoding='utf-8')
        (data / 'doc_metadata.json').write_text(json.dumps({'documents': {'demo:' + cls.path: {
            'title': '阅读步骤与参数核对', 'content_type': 'tutorial', 'source_type': 'author_experience',
            'scope_note': SCOPE, 'sources': [{'url': 'https://example.com/guide', 'label': '作者原文'}],
            'reviewed_at': '2026-01-01', 'image_status': 'text_only'}}}), encoding='utf-8')
        cls.client = TestClient(app.app, follow_redirects=False)
        cls.client.headers['Origin'] = 'http://testserver'
        login = cls.client.post('/login', data={'email': 'reader@example.invalid', 'password': password})
        if login.status_code != 303 or not cls.client.cookies.get('kb_session'):
            raise RuntimeError('Synthetic reader fixture login failed')
        cls.evidence = Path(os.environ['KKS_READER_EVIDENCE_DIR']) if os.environ.get('KKS_READER_EVIDENCE_DIR') else None
        cls.records = []
        if cls.evidence:
            cls.evidence.mkdir(parents=True, exist_ok=True)

    @classmethod
    def tearDownClass(cls):
        if cls.evidence:
            (cls.evidence / 'reader-measurements.json').write_text(json.dumps(cls.records, ensure_ascii=False, indent=2), encoding='utf-8')

    @contextmanager
    def browser(self, width, height):
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, channel=os.environ.get('KKS_TEST_BROWSER_CHANNEL', 'chromium'))
            context = browser.new_context(viewport={'width': width, 'height': height}, reduced_motion='reduce')
            def serve(route):
                url = urlsplit(route.request.url)
                if url.netloc != 'testserver' or route.request.method != 'GET':
                    return route.abort()
                response = self.client.get(url.path + ('?' + url.query if url.query else ''))
                route.fulfill(status=response.status_code, body=response.content, headers=dict(response.headers))
            context.route('**/*', serve)
            page = context.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            try:
                yield page, errors
            finally:
                context.close()
                browser.close()

    def open(self, page, *, fragment='', sec=''):
        params = {'path': self.path}
        if sec:
            params['sec'] = sec
        page.goto('http://testserver/doc?' + urlencode(params) + fragment, wait_until='networkidle')

    def record(self, page, name, width, target=None):
        page.evaluate('''() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))''')
        row = page.evaluate('''id => {
          const rect = selector => {const el=document.querySelector(selector); return el ? el.getBoundingClientRect().toJSON() : null;};
          const target=id ? document.getElementById(id) : null;
          return {height:innerHeight,width:innerWidth,scrollY,overflow:document.documentElement.scrollWidth>innerWidth,
            topbar:rect('.topbar'),toolbar:rect('.reader-toolbar'),header:rect('.reader-header'),
            outlineOpen:document.querySelector('.reader-outline')?.open,
            target:target?.getBoundingClientRect().toJSON()};
        }''', target)
        row.update(name=name, width=width)
        self.records.append(row)
        if self.evidence:
            page.screenshot(path=str(self.evidence / f'{name}-{width}.png'))
        return row

    def assert_visible_landing(self, page, anchor):
        page.wait_for_function('''id => {
          const r=document.getElementById(id)?.getBoundingClientRect();
          const bottom=Math.max(document.querySelector('.topbar').getBoundingClientRect().bottom,
                                document.querySelector('.reader-toolbar').getBoundingClientRect().bottom);
          return r && r.top >= bottom+5 && r.top < innerHeight-30;
        }''', arg=anchor, timeout=2000)
        page.evaluate('''() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))''')
        box = page.locator('#' + anchor).bounding_box()
        bottom = page.evaluate("Math.max(document.querySelector('.topbar').getBoundingClientRect().bottom,document.querySelector('.reader-toolbar').getBoundingClientRect().bottom)")
        self.assertGreaterEqual(box['y'], bottom + 5)
        self.assertLess(box['y'], page.viewport_size['height'] - 30)

    def assert_stable_landing(self, page, anchor):
        # Wait for layout to settle, then require twelve consecutive rendered frames.
        page.evaluate('window.__landingFrames=0')
        page.wait_for_function('''id => {
          const r=document.getElementById(id)?.getBoundingClientRect();
          const bottom=Math.max(document.querySelector('.topbar').getBoundingClientRect().bottom,
                                document.querySelector('.reader-toolbar').getBoundingClientRect().bottom);
          const visible=r && r.top>=bottom+5 && r.top<innerHeight-30;
          window.__landingFrames=visible ? (window.__landingFrames||0)+1 : 0;
          return window.__landingFrames>=12;
        }''', arg=anchor, timeout=3000)
        self.assert_visible_landing(page, anchor)

    def test_browser_folded_anchor_opens_only_ancestors_and_survives_history(self):
        from playwright.sync_api import expect
        for width, height in WIDTHS:
            with self.subTest(width=width), self.browser(width, height) as (page, errors):
                self.open(page)
                page.get_by_role('link', name='跳到原文书签', exact=True).click()
                self.assert_visible_landing(page, self.anchors['word-note'])
                # Return to the link by native scrolling; choosing the link pushes a new entry.
                page.get_by_role('link', name='跳到折叠书签', exact=True).click()
                self.assert_stable_landing(page, self.anchors['folded-note'])
                outer=page.locator('#doc details').filter(has=page.get_by_text('折叠补充', exact=True))
                inner=page.locator('#doc details').filter(has=page.get_by_text('进一步说明', exact=True)).last
                unrelated=page.locator('#doc details').filter(has=page.get_by_text('无关内容', exact=True))
                self.assertTrue(outer.evaluate('(el)=>el.open'))
                self.assertTrue(inner.evaluate('(el)=>el.open'))
                self.assertFalse(unrelated.evaluate('(el)=>el.open'))
                expect(page.get_by_text('折叠书签目标。', exact=True)).to_be_visible()
                page.go_back()
                self.assert_stable_landing(page, self.anchors['word-note'])
                outer.evaluate('(el)=>el.open=false')
                page.go_forward()
                self.assert_stable_landing(page, self.anchors['folded-note'])
                self.assertTrue(outer.evaluate('(el)=>el.open'))
                self.record(page, 'folded-forward', width, self.anchors['folded-note'])
                self.assertEqual(errors, [])

    def test_browser_reader_header_keeps_evidence_without_hiding_the_first_paragraph(self):
        from playwright.sync_api import expect
        for width, height in WIDTHS:
            with self.subTest(width=width), self.browser(width, height) as (page, errors):
                self.open(page)
                row = self.record(page, 'header', width, self.anchors['opening'])
                expect(page.get_by_text('适用游戏未核定', exact=True)).to_be_visible()
                expect(page.get_by_text('本站未做游戏实测', exact=True)).to_be_visible()
                expect(page.get_by_text('示例作者', exact=False)).to_be_visible()
                expect(page.get_by_role('link', name='作者原文').first).to_be_visible()
                expect(page.locator('.reader-original-entry a')).to_be_visible()
                details = page.locator('.reader-header details').filter(has=page.get_by_text(SCOPE, exact=True))
                self.assertEqual(details.count(), 1)
                self.assertFalse(details.evaluate('(el)=>el.open'))
                self.assertLess(row['target']['top'], height - 40)
                self.assertFalse(row['overflow'])
                details.locator('summary').click()
                expect(page.get_by_text(SCOPE, exact=True)).to_be_visible()
                expect(page.get_by_text(FLOW_NOTE, exact=True)).to_be_visible()
                expect(page.get_by_text('SHA-256 ' + self.source_sha, exact=True)).to_be_visible()
                self.assertEqual(errors, [])

    def test_browser_all_anchor_entry_points_clear_both_sticky_bars(self):
        for width, height in WIDTHS:
            for action in ('initial-old', 'initial-current', 'sec', 'click', 'hashchange', 'invalid'):
                with self.subTest(width=width, action=action), self.browser(width, height) as (page, errors):
                    target = self.anchors['word-note']
                    fragment = '#word-note' if action == 'initial-old' else ('#' + target if action == 'initial-current' else ('#%E0%A4%A' if action == 'invalid' else ''))
                    self.open(page, fragment=fragment,
                              sec='word-note' if action == 'sec' else '')
                    if action == 'click':
                        page.get_by_role('link', name='跳到原文书签', exact=True).click()
                    elif action == 'hashchange':
                        page.evaluate("location.hash='word-note'")
                    elif action == 'invalid':
                        page.locator('#reader-size').click()
                        self.assertEqual(page.locator('#reader-size').inner_text(), '字号 18')
                        page.evaluate("location.hash='%E0%A4%A%'")
                        page.locator('#reader-size').click()
                        self.assertEqual(page.locator('#reader-size').inner_text(), '字号 20')
                    self.record(page, action, width, target)
                    if action != 'invalid':
                        self.assert_visible_landing(page, target)
                    self.assertEqual(errors, [])

    def test_browser_mobile_outline_collapses_after_selection_and_respects_large_text(self):
        for width, height in WIDTHS[1:]:
            with self.subTest(width=width), self.browser(width, height) as (page, errors):
                self.open(page)
                outline = page.locator('.reader-outline')
                self.assertFalse(outline.evaluate('(el)=>el.open'))
                outline.locator('summary').click()
                outline.get_by_role('link', name='第 7 节 · 参数核对', exact=True).click()
                self.assertFalse(outline.evaluate('(el)=>el.open'))
                self.assert_visible_landing(page, self.anchors['section-7'])
                self.record(page, 'outline-selected', width, self.anchors['section-7'])
                page.locator('[data-reader]').evaluate("el=>el.style.setProperty('--reader-size','32px')")
                page.evaluate("location.hash='word-note'")
                self.assert_stable_landing(page, self.anchors['word-note'])
                self.record(page, 'large-text', width, self.anchors['word-note'])
                # Explicit reader scrolling must release automatic landing alignment.
                page.mouse.wheel(0, 360)
                page.wait_for_function('''id=>document.getElementById(id).getBoundingClientRect().top<0''', arg=self.anchors['word-note'])
                page.locator('[data-reader]').evaluate("el=>el.style.setProperty('--reader-size','30px')")
                page.evaluate('''() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))''')
                self.assertLess(page.locator('#' + self.anchors['word-note']).bounding_box()['y'], 0)
                self.assertEqual(errors, [])
