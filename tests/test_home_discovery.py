"""Home discovery against isolated FastAPI data and synthetic learning material."""
from contextlib import contextmanager
import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

from bs4 import BeautifulSoup
from fastapi.testclient import TestClient


ROOT = Path(__file__).resolve().parents[1]
FEATURED_PATH = '06-新目标研究/62-synthetic-material.md'
WIDTHS = ((1440, 900), (375, 812), (320, 720))


def application_module(name):
    return name in {'app', 'kb', 'indexer'} or name.startswith(('app.', 'kb.'))


@contextmanager
def home_fixture(mode):
    """Use a real index and manual; never import a configured installation."""
    old_path = sys.path[:]
    saved_modules = {name: value for name, value in sys.modules.items() if application_module(name)}
    with tempfile.TemporaryDirectory(prefix='home-discovery-') as directory:
        root = Path(directory)
        shutil.copytree(ROOT / 'app', root / 'app', ignore=shutil.ignore_patterns('__pycache__'))
        data = root / 'data'
        docs = data / 'docs'
        docs.mkdir(parents=True)
        records = []
        if mode != 'empty':
            records.append(('synthetic-start.md', '自造阅读起点', 'SyntheticNeedle 用于检查首页搜索和阅读导航。'))
        if mode == 'curated':
            records += [(FEATURED_PATH, '自造材质观察', '此编号仅用于专题匹配测试，没有复制真实教程。'),
                        ('synthetic-light.md', '自造灯光观察', '仅演示章节入口，不包含实测结论。')]
        for path, title, body in records:
            target = docs / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(f'# {title}\n\n{body}\n', encoding='utf-8')
        entries = [{'path': path, 'title': title, 'chars': len(body), 'col': 'kks', 'loose': False}
                   for path, title, body in records]
        def chapter(identifier, title, selected):
            return {'id': identifier, 'title': title, 'intro': '原创测试章节。',
                    'docs': selected, 'count': len(selected), 'chars': sum(item['chars'] for item in selected)}
        chapters = [] if mode == 'empty' else [chapter('demo', '自造演示章节', entries)]
        if mode == 'curated':
            chapters = [chapter('03', '自造材质章节', entries[:2]), chapter('04', '自造灯光章节', entries[2:])]
        (data / 'manual.json').write_text(json.dumps({'chapters': chapters, 'reference': [],
                'total': len(entries), 'manual_total': len(entries), 'ref_total': 0, 'dropped': 0}), encoding='utf-8')
        (root / 'config.json').write_text(json.dumps({'auth': {'required': False, 'secret': secrets.token_hex(32)},
                'llm_url': '', 'llm_key': '', 'llm_fallbacks': [], 'gitee_key': '',
                'oauth': {'linuxdo': {'client_id': '', 'client_secret': ''}}}), encoding='utf-8')
        spec = importlib.util.spec_from_file_location('home_indexer', root / 'app/indexer.py')
        indexer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(indexer)
        indexer.build(str(data / 'kb.db'), str(docs), do_embed=False, collection='kks')
        app = None
        try:
            for name in list(sys.modules):
                if application_module(name):
                    del sys.modules[name]
            sys.path.insert(0, str(root / 'app'))
            with patch.dict(os.environ, {'KB_GAME_ROOT': str(root / 'game')}), \
                    patch('httpx.HTTPTransport.handle_request', side_effect=AssertionError('External HTTP forbidden')), \
                    patch('httpx.AsyncHTTPTransport.handle_async_request', side_effect=AssertionError('External HTTP forbidden')):
                import app
                with TestClient(app.app, follow_redirects=False) as client:
                    yield client, entries, chapters
        finally:
            if app:
                app.KBX.con.close()
                app.STATE.close()
            sys.path[:] = old_path
            for name in list(sys.modules):
                if application_module(name):
                    del sys.modules[name]
            sys.modules.update(saved_modules)


class HomeDiscoveryTests(unittest.TestCase):
    def home(self, client):
        response = client.get('/')
        self.assertEqual(response.status_code, 200)
        return BeautifulSoup(response.text, 'html.parser')

    def assert_existing_entries(self, client, soup, chapter_ids, paths):
        tracks = soup.select('.learn-track[href]')
        self.assertEqual({urlsplit(link['href']).path for link in tracks},
                         {'/manual/' + identifier for identifier in chapter_ids})
        for link in tracks + soup.select('.learn-guide[href]'):
            self.assertEqual(client.get(link['href']).status_code, 200, link['href'])
        self.assertEqual({parse_qs(urlsplit(link['href']).query)['path'][0]
                          for link in soup.select('.learn-guide[href]')}, set(paths))

    def test_demo_chapter_is_discoverable_without_fabricated_featured_guides(self):
        with home_fixture('demo') as (client, _, chapters):
            soup = self.home(client)
            self.assert_existing_entries(client, soup, [chapter['id'] for chapter in chapters], [])
            self.assertIsNone(soup.select_one('#learn-guides-title'))
            self.assertNotIn('open', soup.select_one('#ask-entry').attrs)
            form = soup.select_one('form[role="search"]')
            self.assertEqual(form.get('method', 'get').lower(), 'get')
            self.assertEqual(form['action'], '/search')
            response = client.get(form['action'], params={'q': 'SyntheticNeedle'})
            self.assertEqual(response.status_code, 200)
            self.assertIn('synthetic-start.md', response.text)

    def test_curated_chapters_and_featured_guide_only_link_existing_content(self):
        with home_fixture('curated') as (client, _, chapters):
            soup = self.home(client)
            self.assert_existing_entries(client, soup, [chapter['id'] for chapter in chapters], [FEATURED_PATH])
            self.assertIsNotNone(soup.select_one('#learn-guides-title'))
            self.assertEqual(len(soup.select('.learn-guide')), 1)

    def test_empty_library_has_an_honest_empty_state_without_fake_topics(self):
        with home_fixture('empty') as (client, _, _):
            soup = self.home(client)
            self.assert_existing_entries(client, soup, [], [])
            self.assertIsNone(soup.select_one('#learn-guides-title'))
            empty = soup.select_one('.learn-empty')
            self.assertIsNotNone(empty)
            self.assertIn('尚未整理', empty.get_text())
            self.assertEqual(client.get(empty.a['href']).status_code, 200)

    def test_browser_home_discovery_search_assistant_resume_and_layout(self):
        from playwright.sync_api import expect, sync_playwright
        evidence = Path(os.environ['KKS_HOME_EVIDENCE_DIR']) if os.environ.get('KKS_HOME_EVIDENCE_DIR') else None
        measurements = []
        if evidence:
            evidence.mkdir(parents=True, exist_ok=True)
        with home_fixture('curated') as (client, _, _), sync_playwright() as playwright:
            scope = self.home(client).body['data-reader-scope']
            browser = playwright.chromium.launch(headless=True, channel=os.environ.get('KKS_TEST_BROWSER_CHANNEL', 'chromium'))
            try:
                for width, height in WIDTHS:
                    for theme in ('light', 'dark'):
                        with self.subTest(width=width, theme=theme):
                            context = browser.new_context(viewport={'width': width, 'height': height}, reduced_motion='reduce')
                            requests, errors = [], []
                            def serve(route):
                                request = route.request
                                url = urlsplit(request.url)
                                requests.append({'method': request.method, 'path': url.path})
                                if url.netloc != 'testserver' or request.method != 'GET':
                                    return route.abort()
                                response = client.get(url.path + ('?' + url.query if url.query else ''))
                                route.fulfill(status=response.status_code, body=response.content, headers=dict(response.headers))
                            context.route('**/*', serve)
                            storage = {'theme': theme, 'kks:reading:v1:' + scope: json.dumps({
                                'path': 'synthetic-start.md', 'title': '自造阅读起点', 'anchor': 'saved-section'})}
                            context.add_init_script('for (const [key,value] of Object.entries(' + json.dumps(storage) + ')) localStorage.setItem(key,value);')
                            page = context.new_page()
                            page.on('pageerror', lambda error: errors.append(str(error)))
                            try:
                                page.goto('http://testserver/', wait_until='networkidle')
                                expect(page.locator('html')).to_have_attribute('data-theme', theme)
                                expect(page.locator('#learn-resume')).to_be_visible()
                                resume = page.locator('#learn-resume-link')
                                expect(resume).to_have_text('自造阅读起点')
                                self.assertEqual(parse_qs(urlsplit(resume.get_attribute('href')).query)['path'], ['synthetic-start.md'])
                                self.assertEqual(urlsplit(resume.get_attribute('href')).fragment, 'saved-section')
                                assistant = page.locator('#ask-entry')
                                self.assertFalse(assistant.evaluate('node=>node.open'))
                                row = page.evaluate('''() => ({width:innerWidth, scroll:document.documentElement.scrollWidth,
                                    firstTrack:document.querySelector('.learn-track').getBoundingClientRect().top,
                                    topics:document.querySelectorAll('.learn-guide').length})''')
                                row.update(theme=theme)
                                self.assertLessEqual(row['scroll'], width)
                                if evidence:
                                    page.screenshot(path=str(evidence / f'home-{width}-{theme}.png'), full_page=True)
                                # Tab from the search input to its submit button: keyboard focus must be visible.
                                page.locator('#learn-query').focus()
                                page.keyboard.press('Tab')
                                focus = page.evaluate('''() => {const n=document.activeElement,c=getComputedStyle(n);return {
                                    submit:n.matches('.learn-search button'),visible:n.matches(':focus-visible'),
                                    outline:c.outlineStyle,width:parseFloat(c.outlineWidth),shadow:c.boxShadow};}''')
                                self.assertTrue(focus['submit'] and focus['visible'])
                                self.assertTrue((focus['outline'] != 'none' and focus['width'] > 0) or focus['shadow'] != 'none')
                                page.locator('.learn-shortcuts [data-open-assistant]').click()
                                expect(assistant).to_have_attribute('open', '')
                                expect(page.locator('#q')).to_be_visible()
                                self.assertFalse(any(request['method'] == 'POST' for request in requests))
                                self.assertLessEqual(page.evaluate('document.documentElement.scrollWidth'), width)
                                page.locator('#learn-query').fill('SyntheticNeedle')
                                page.locator('.learn-search button').click()
                                page.wait_for_url(re.compile(r'/search\?q=SyntheticNeedle$'))
                                page.wait_for_load_state('networkidle')
                                expect(page.get_by_role('link', name='自造阅读起点', exact=False).first).to_be_visible()
                                self.assertFalse(any(request['method'] == 'POST' for request in requests))
                                self.assertEqual(errors, [])
                                row['focus_visible'] = True
                                row['search_navigation'] = True
                                row['assistant_post_count'] = sum(request['method'] == 'POST' for request in requests)
                                measurements.append(row)
                            finally:
                                context.close()
            finally:
                browser.close()
                if evidence:
                    (evidence / 'measurements.json').write_text(json.dumps(measurements, ensure_ascii=False, indent=2), encoding='utf-8')


if __name__ == '__main__':
    unittest.main()
