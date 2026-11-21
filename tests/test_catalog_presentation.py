"""Server-rendered discovery contracts and highlight safety, synthetic data only."""
from bs4 import BeautifulSoup
import unittest
from unittest.mock import patch
from urllib.parse import parse_qs, urlsplit

from catalog_fixture import CatalogFixture


class CatalogPresentationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.fixture = CatalogFixture()
        cls.addClassCleanup(cls.fixture.close)

    def test_collection_selection_survives_export_browse_and_example_search(self):
        browse = BeautifulSoup(self.fixture.client.get('/browse?col=demo').text, 'html.parser')
        self.assertEqual(browse.select_one('#catalog-count').get_text(), '显示 3 / 3 篇')
        self.assertEqual(browse.select_one('.discovery-secondary')['href'], '/export/all?col=demo')
        self.assertEqual(len(browse.select('[data-catalog-item]')), 3)
        search = BeautifulSoup(self.fixture.client.get('/search?col=demo').text, 'html.parser')
        self.assertEqual(search.select_one('input[name=col]')['value'], 'demo')
        for example in search.select('.catalog-suggestions a'):
            self.assertEqual(parse_qs(urlsplit(example['href']).query)['col'], ['demo'])
        self.assertEqual(search.select_one('.discovery-secondary')['href'], '/browse?col=demo')

    def test_empty_collection_has_honest_zero_state(self):
        page = BeautifulSoup(self.fixture.client.get('/browse?col=unknown').text, 'html.parser')
        self.assertEqual(page.select_one('#catalog-count').get_text(), '显示 0 / 0 篇')
        self.assertNotIn('hidden', page.select_one('#catalog-empty').attrs)
        self.assertEqual(page.select_one('#catalog-empty h2').get_text(), '这个分区还没有文档')

    def test_internal_round_marker_is_only_removed_from_the_display_title(self):
        from urllib.parse import unquote
        browse = BeautifulSoup(self.fixture.client.get('/browse?col=demo').text, 'html.parser')
        item = next(item for item in browse.select('[data-catalog-item]') if 'normal-map.md' in item['data-k'])
        self.assertEqual(item.select_one('.catalog-title').get_text(), '法线贴图核对')
        self.assertIn('（新目标第15轮）', item['data-k'])
        self.assertEqual(unquote(item.a['href']), '/doc?path=材质与贴图/normal-map.md')
        search = BeautifulSoup(self.fixture.client.get('/search?q=法线&col=demo').text, 'html.parser')
        self.assertEqual(search.select_one('.catalog-hit h3').get_text(), '法线贴图核对')
        self.assertEqual(unquote(search.select_one('.catalog-hit')['href']), '/doc?path=材质与贴图/normal-map.md')

    def test_search_keeps_only_sanitized_mark_tags_and_escapes_heading(self):
        hit = {'path': 'unsafe-name.md', 'heading': '<img src=x onerror=alert(1)>',
               'snippet': '<script>alert(1)</script><mark>RimLight</mark><img src=x onerror=alert(1)>', 'col': 'demo'}
        with patch.object(self.fixture.app.KBX, 'search', return_value=[hit]):
            response = self.fixture.client.get('/search?q=RimLight&col=demo')
        self.assertEqual(response.status_code, 200)
        page = BeautifulSoup(response.text, 'html.parser')
        result = page.select_one('.catalog-hit')
        self.assertEqual(result.select_one('h3').get_text(), '<img src=x onerror=alert(1)>')
        self.assertEqual([node.name for node in result.select_one('.catalog-excerpt').find_all()], ['mark'])
        self.assertEqual(result.select_one('mark').get_text(), 'RimLight')
        self.assertEqual(result['href'], '/doc?path=unsafe-name.md')
