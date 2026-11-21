"""Catalogue filtering and search presentation, using no real corpus or network."""
import json
import os
from pathlib import Path
import unittest
from urllib.parse import parse_qs, urlsplit

from catalog_fixture import CatalogFixture


class CatalogBrowserTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = CatalogFixture()
        cls.addClassCleanup(cls.fixture.close)
        cls.evidence = Path(os.environ['KKS_CATALOG_EVIDENCE_DIR']) if os.environ.get('KKS_CATALOG_EVIDENCE_DIR') else None
        cls.measurements = []
        if cls.evidence:
            cls.evidence.mkdir(parents=True, exist_ok=True)

    @classmethod
    def tearDownClass(cls):
        if cls.evidence:
            (cls.evidence / 'measurements.json').write_text(json.dumps(cls.measurements, ensure_ascii=False, indent=2), encoding='utf-8')

    def capture(self, page, name, width, theme):
        row = page.evaluate('''() => ({width:innerWidth,scrollWidth:document.documentElement.scrollWidth,
          theme:document.documentElement.dataset.theme,heading:document.querySelector('main h1')?.textContent,
          bodyBackground:getComputedStyle(document.body).backgroundColor})''')
        row.update(page=name, theme=theme)
        self.measurements.append(row)
        self.assertLessEqual(row['scrollWidth'], width)
        if self.evidence:
            page.screenshot(path=str(self.evidence / f'{name}-{width}-{theme}.png'))

    def test_browser_catalog_filter_clear_count_and_empty_state(self):
        from playwright.sync_api import expect
        for width, height in ((1440, 900), (375, 812), (320, 720)):
            for theme in ('light', 'dark'):
                with self.subTest(width=width, theme=theme), self.fixture.browser(width, height, theme) as (page, errors):
                    page.goto('http://testserver/browse?col=demo', wait_until='networkidle')
                    field = page.get_by_role('textbox', name='筛选文档')
                    expect(page.get_by_role('status')).to_have_text('显示 3 / 3 篇')
                    expect(page.locator('[data-catalog-item]:visible')).to_have_count(3)
                    self.assertEqual(page.get_by_role('link', name='导出本库').get_attribute('href'), '/export/all?col=demo')
                    self.capture(page, 'browse', width, theme)
                    field.focus()
                    self.assertNotEqual(field.evaluate('el=>getComputedStyle(el).outlineStyle'), 'none')
                    field.fill('法线')
                    expect(page.get_by_role('status')).to_have_text('显示 1 / 3 篇')
                    expect(page.locator('[data-catalog-item]:visible')).to_have_count(1)
                    expect(page.locator('[data-catalog-group-count]')).to_have_text('1')
                    field.fill('没有这个合成标题')
                    expect(page.get_by_role('status')).to_have_text('显示 0 / 3 篇')
                    expect(page.locator('[data-catalog-group-count]')).to_have_text('0')
                    expect(page.get_by_text('没有匹配的文档', exact=True)).to_be_visible()
                    self.capture(page, 'browse-empty', width, theme)
                    page.get_by_role('button', name='清空筛选', exact=True).click()
                    expect(field).to_have_value('')
                    expect(field).to_be_focused()
                    expect(page.get_by_role('status')).to_have_text('显示 3 / 3 篇')
                    expect(page.get_by_text('没有匹配的文档', exact=True)).to_be_hidden()
                    self.assertEqual(errors, [])

    def test_browser_search_submit_readable_results_and_safe_highlights(self):
        from playwright.sync_api import expect
        for width, height in ((1440, 900), (375, 812), (320, 720)):
            for theme in ('light', 'dark'):
                with self.subTest(width=width, theme=theme), self.fixture.browser(width, height, theme) as (page, errors):
                    page.goto('http://testserver/search?col=demo', wait_until='networkidle')
                    field = page.get_by_role('searchbox', name='搜索资料')
                    field.fill('RimLight')
                    field.press('Enter')
                    page.wait_for_url('**/search?*q=RimLight*')
                    params = parse_qs(urlsplit(page.url).query)
                    self.assertEqual(params['col'], ['demo'])
                    expect(page.locator('.catalog-hit')).to_have_count(2)
                    expect(page.get_by_role('heading', name='边缘光与材质参数', exact=True)).to_be_visible()
                    self.assertGreater(page.locator('.catalog-excerpt mark').count(), 0)
                    self.assertEqual(page.locator('.catalog-excerpt script,.catalog-excerpt img').count(), 0)
                    self.assertIsNone(page.evaluate('window.catalogInjected'))
                    self.assertNotIn('jieba', page.locator('main').inner_text())
                    self.capture(page, 'search', width, theme)
                    field.fill('zxqvnomatchfixture')
                    page.get_by_role('button', name='搜索', exact=True).click()
                    expect(page.get_by_text('没有找到相关内容', exact=True)).to_be_visible()
                    self.capture(page, 'search-empty', width, theme)
                    self.assertEqual(errors, [])
