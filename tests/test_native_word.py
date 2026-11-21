import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest
import hashlib
from docx import Document
from bs4 import BeautifulSoup

p=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(p/'app'))
s=importlib.util.spec_from_file_location('native_word',p/'ops/tools/build_native_word.py')
m=importlib.util.module_from_spec(s);s.loader.exec_module(m)

@unittest.skipUnless(importlib.util.find_spec('mammoth'), 'optional mammoth conversion dependency is not installed')
class NativeWordTests(unittest.TestCase):
    def test_source_headings_links_and_full_text_are_restored_without_altering_source(self):
        with tempfile.TemporaryDirectory() as folder:
            f=Path(folder)/'a.docx';d=Document();d.add_heading('章节甲',1);d.add_paragraph('操作说明');d.add_heading('参数乙',2);d.save(f)
            before=f.read_bytes()
            result=m.build_word_view(f,[], 'doc2','a.md')
            soup=BeautifulSoup(result['html'],'html.parser')
            self.assertEqual([h.get_text() for h in soup.select('h2,h3')],['章节甲','参数乙'])
            self.assertEqual([t['title'] for t in result['toc']],['章节甲','参数乙'])
            self.assertTrue(result['checks']['full_text_order_equal'])
            self.assertEqual(result['source_sha256'],hashlib.sha256(before).hexdigest())
            self.assertEqual(before,f.read_bytes())

    def test_zero_heading_source_stays_zero_heading(self):
        with tempfile.TemporaryDirectory() as folder:
            f=Path(folder)/'a.docx';d=Document();d.add_paragraph('只加粗而非原文标题').runs[0].bold=True;d.save(f)
            v=m.build_word_view(f,[],'doc2','a.md')
            self.assertEqual(v['toc'],[])

if __name__=='__main__':unittest.main()
