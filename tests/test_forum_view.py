import hashlib
from pathlib import Path
import sys
import tempfile
import unittest

from bs4 import BeautifulSoup

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'ops/tools'))
from build_forum_view import build_forum_view


class ForumViewTests(unittest.TestCase):
    def test_post_text_replies_and_attachments_keep_their_order(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'thread.html'
            path.write_text('''<nav>站点导航</nav><div id="post_1">
              <div class="pls"><div class="authi"><a>作者</a></div></div>
              <div class="pcb"><div class="t_f">第一步<img src="a.png"><br>第二步</div>
              <div class="ratl"><img src="avatar.png"></div></div></div>
              <div class="pcb"><div class="t_f"><b>补充</b><img src="missing.png" alt="示意图"></div>
              <div class="pattl"><p class="savephotop"><img src="a.png"></p></div></div>''', encoding='utf8')
            before = hashlib.sha256(path.read_bytes()).hexdigest()
            view = build_forum_view(path, [{'reference': 'a.png', 'thumb': 'recover/a.jpg',
                                             'orig': 'recover/a.png'}], 'indexed')
            self.assertEqual((view['source_sha256'], hashlib.sha256(path.read_bytes()).hexdigest()), (before, before))
            rendered = BeautifulSoup(view['html'], 'html.parser')
            self.assertEqual([n.get_text() for n in rendered.select('h2')], ['帖子 1 · 作者', '帖子 2'])
            self.assertEqual([i['data-original'] for i in rendered.select('img')], ['/orig/recover/a.png'] * 2)
            self.assertEqual(rendered.b.text, '补充')
            self.assertIn('配图缺失：示意图', rendered.get_text())
            self.assertNotIn('站点导航', rendered.get_text())
            self.assertNotIn('avatar.png', view['html'])
            self.assertLess(view['html'].index('/thumb/'), view['html'].index('第二步'))

    def test_unrecognized_layout_cannot_be_promoted(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'page.html'
            source.write_text('<div class="pcb">incomplete</div>')
            with self.assertRaises(ValueError):
                build_forum_view(source, [], 'indexed')

    def test_excess_breaks_collapse_only_in_prose(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'thread.html'
            breaks = '<br> \n' * 9
            source.write_text(f'<div class="pcb"><div class="t_f"><p>A{breaks}B</p><pre>X{breaks}Y</pre></div></div>')
            result = BeautifulSoup(build_forum_view(source, [], 'body')['html'], 'html.parser')
            self.assertEqual(len(result.p.select('br')), 2)
            self.assertEqual(len(result.pre.select('br')), 9)

    def test_editor_spacers_inside_formatting_and_at_block_edges(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'thread.html'
            source.write_text('''<div class="pcb"><div class="t_f"><br><p>first<br>
              <b><font>&nbsp;<br></font></b><span>&#8203;</span><br><i><br></i>second
              <br></p><div><p>&nbsp;<br></p></div><ul><li><br>third<br></li></ul>
              <p><img src="missing.png" alt="diagram"></p><p>two<b>&nbsp;</b>words</p>
              <code>A<br><br><br>B</code><br></div></div>''')
            result = BeautifulSoup(build_forum_view(source, [], 'body')['html'], 'html.parser')
            self.assertEqual(len(result.p.select('br')), 2)
            self.assertIsNone(result.li.br)
            self.assertEqual(len(result.select('br')), 5)
            self.assertIn('two\u00a0words', result.get_text())
            self.assertIn('配图缺失：diagram', result.get_text())
            self.assertFalse(any(not p.get_text(strip=True) for p in result.select('p')))

    def test_spoiler_title_body_and_real_table_survive_without_archived_controls(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'thread.html'
            source.write_text('''<div class="pcb"><div class="t_f">before
              <div class="spoiler"><div class="spoilerheader">
                <input class="spoilerbutton" type="button" value="Section A"> （点击展开 / 收起）
              </div><div class="spoilerbody" style="display:none"><table><tbody><tr><td>
                <br>entry<table><tr><th>key</th><td>value</td></tr></table>
              </td></tr></tbody></table></div></div>
              <div class="spoiler"><div class="spoilerheader"><input type="button"
                class="spoilerbutton" value="Section B">（点击展开 / 收起）</div>
                <div class="spoilerbody"><table><caption>Units</caption><tr><td>m</td></tr></table></div></div>
              after</div></div>''', encoding='utf-8')
            result = BeautifulSoup(build_forum_view(source, [], 'body')['html'], 'html.parser')
            self.assertEqual([h.get_text(strip=True) for h in result.select('h3')], ['Section A', 'Section B'])
            self.assertEqual(len(result.select('table')), 2)
            visible = result.get_text(' ', strip=True)
            self.assertNotIn('点击展开', visible)
            parts = ['before', 'Section A', 'entry', 'key', 'value', 'Section B', 'Units', 'after']
            self.assertEqual(sorted(parts, key=visible.index), parts)

    def test_signed_attachment_and_malformed_links_become_text(self):
        with tempfile.TemporaryDirectory() as tmp:
            source = Path(tmp) / 'thread.html'
            source.write_text('''<div class="pcb"><div class="t_f">
              <a href="https://example.org/guide">guide</a>
              <a href="https://[broken">broken</a></div><div class="pattl">
              <a href="https://example.org/forum.php?mod=attachment&amp;aid=stale">diagram.png</a>
              <img src="missing.png" alt="diagram.png"></div></div>''')
            result = BeautifulSoup(build_forum_view(source, [], 'body')['html'], 'html.parser')
            self.assertEqual([a['href'] for a in result.select('a[href]')], ['https://example.org/guide'])
            self.assertIn('broken', result.get_text())
            self.assertIn('diagram.png', result.get_text())
            self.assertNotIn('aid=stale', str(result))


if __name__ == '__main__':
    unittest.main()
