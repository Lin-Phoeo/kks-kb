import hashlib
import sys
import unittest
from pathlib import Path
from bs4 import BeautifulSoup

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from kb.content.reading_view import prepare_reading_html


class ReadingStructureTests(unittest.TestCase):
    def prepare(self, text):
        return prepare_reading_html(text, 'doc2', 'Word文档/example.md')

    def test_empty_heading_bookmark_is_kept_but_not_in_outline(self):
        view = self.prepare('<h1><a id="blank"></a></h1><h1>第一章</h1><p>正文</p><h2>参数</h2><a href="#blank">原目录</a>')
        self.assertEqual([t['title'] for t in view['toc']], ['第一章', '参数'])
        self.assertEqual([t['level'] for t in view['toc']], [2, 3])
        soup = BeautifulSoup(view['html'], 'html.parser')
        self.assertFalse(any(not h.get_text(strip=True) for h in soup.select('h1,h2,h3')))
        self.assertIn(soup.select_one('a[href]')['href'][1:], [n['id'] for n in soup.select('[id]')])

    def test_fragment_links_namespaced_duplicates_unique_and_reserved_ids_not_exposed(self):
        view = self.prepare('<h1 id="reader-size">章</h1><h1 id="reader-size">章</h1><a name="evil" href="#reader-size">跳转</a><script>bad()</script>')
        soup = BeautifulSoup(view['html'], 'html.parser')
        ids = [n['id'] for n in soup.select('[id]')]
        self.assertEqual(len(ids), len(set(ids)))
        self.assertNotIn('reader-size', ids)
        self.assertTrue(all(i.startswith('rd-') for i in ids))
        self.assertFalse(soup.select('script,[name]'))
        self.assertEqual(soup.select_one('a[href]')['href'], '#' + ids[0])
        self.assertEqual(len(view['toc']), 2)

    def test_image_occurrences_and_order_stay_in_place_without_fake_captions(self):
        text = '<p>先调参数</p><p><img src="/thumb/a.jpg" data-original="/orig/a.png"></p><p>再检查</p><p><img src="/thumb/a.jpg" data-original="/orig/a.png"></p>'
        view = self.prepare(text)
        soup = BeautifulSoup(view['html'], 'html.parser')
        self.assertEqual([i['data-original'] for i in soup.select('img')], ['/orig/a.png', '/orig/a.png'])
        self.assertEqual(len(soup.select('figure')), 2)
        self.assertEqual([f.get_text(strip=True) for f in soup.select('figcaption')], ['原文插图 · 1', '原文插图 · 2'])
        self.assertLess(view['html'].index('先调参数'), view['html'].index('/orig/a.png'))
        self.assertLess(view['html'].index('/orig/a.png'), view['html'].index('再检查'))

    def test_no_heading_never_creates_a_chapter_and_numbering_tables_survive(self):
        view = self.prepare('<p>仅文字</p><ol start="4"><li value="6">步骤</li></ol><table><tr><td rowspan="2" colspan="3">数值</td></tr></table><pre><code>  a\n    b</code></pre>')
        soup = BeautifulSoup(view['html'], 'html.parser')
        self.assertEqual(view['toc'], [])
        self.assertEqual(soup.ol['start'], '4')
        self.assertEqual(soup.li['value'], '6')
        self.assertEqual(soup.td['rowspan'], '2')
        self.assertEqual(soup.code.get_text(), '  a\n    b')

    def test_existing_heading_anchor_can_be_resolved_and_preparation_is_idempotent(self):
        title = '参数调整'
        view = self.prepare('<h1>' + title + '</h1><p>内容</p>')
        old = 'h-' + hashlib.md5(title.encode()).hexdigest()[:8]
        self.assertEqual(view['anchor_map'][old], view['toc'][0]['anchor'])
        second = self.prepare(view['html'])
        self.assertEqual(second['html'], view['html'])
        self.assertEqual(second['toc'], view['toc'])


if __name__ == '__main__':
    unittest.main()
