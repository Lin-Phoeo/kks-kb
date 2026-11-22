import importlib.util
import io
from pathlib import Path
import sys
import tempfile
import unittest
import hashlib
from docx import Document
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
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

    def make_table(self, document):
        table = document.add_table(rows=2, cols=3)
        for row, labels in zip(table.rows, (('A', 'B', 'C'), ('D', '', 'F'))):
            for cell, label in zip(row.cells, labels):
                cell.text = label
        table.cell(0, 1).merge(table.cell(1, 1))
        return table

    def convert_table(self, document):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'table.docx'
            document.save(source)
            before = hashlib.sha256(source.read_bytes()).hexdigest()
            try:
                view = m.build_word_view(source, [], 'doc2', 'table.md')
                self.assertEqual(view['source_sha256'], before)
                return BeautifulSoup(view['html'], 'html.parser')
            finally:
                self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), before)

    def test_vertical_merge_preserves_rendered_coordinates(self):
        document = Document()
        self.make_table(document)
        table = self.convert_table(document).table
        top, bottom = table.find_all('tr')
        self.assertEqual([(td.text, td.get('rowspan')) for td in top.find_all('td')],
                         [('A', None), ('B', '2'), ('C', None)])
        self.assertEqual([td.text for td in bottom.find_all('td')], ['D', 'F'])
        self.assertEqual(table.get_text(strip=True), 'ABCDF')

    def test_omitted_leading_cell_rejected_even_when_all_text_survives(self):
        document = Document()
        table = self.make_table(document)
        row = table.rows[1]._tr
        row.remove(row.tc_lst[0])
        offset = OxmlElement('w:gridBefore')
        offset.set(qn('w:val'), '1')
        row.get_or_add_trPr().append(offset)
        with self.assertRaisesRegex(ValueError, r'table 1, row 2, column 1: unsupported gridBefore'):
            self.convert_table(document)

    def test_continuation_cannot_change_width(self):
        document = Document()
        table = document.add_table(rows=2, cols=2)
        table.cell(0, 0).merge(table.cell(1, 1)).text = 'Wide'
        merged = self.convert_table(document).select_one('td')
        self.assertEqual((merged.text, merged['colspan'], merged['rowspan']), ('Wide', '2', '2'))
        continuation = table.rows[1]._tr.tc_lst[0]
        continuation.tcPr.gridSpan.val = 1
        with self.assertRaisesRegex(ValueError, r'table 1, row 2, column 1: vMerge'):
            self.convert_table(document)

    def test_orphaned_continuation_in_wrapped_nested_row(self):
        document = Document()
        outer = document.add_table(rows=1, cols=1)
        nested = outer.cell(0, 0).add_table(rows=1, cols=1)
        row = nested.rows[0]._tr
        row.tc_lst[0].get_or_add_tcPr().append(OxmlElement('w:vMerge'))
        control = OxmlElement('w:sdt')
        content = OxmlElement('w:sdtContent')
        nested._tbl.append(control)
        control.append(content)
        content.append(row)
        with self.assertRaisesRegex(ValueError, r'table 2, row 1, column 1: vMerge'):
            self.convert_table(document)

if __name__=='__main__':unittest.main()
