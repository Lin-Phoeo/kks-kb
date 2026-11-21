import importlib.util
from pathlib import Path
import tempfile,unittest,zipfile
p=Path(__file__).resolve().parents[1]/'ops/tools/word_reading_view.py'
s=importlib.util.spec_from_file_location('word_view',p);m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
class WordViewTests(unittest.TestCase):
    def test_original_text_image_and_table_order_is_retained(self):
        with tempfile.TemporaryDirectory() as t:
            f=Path(t)/'a.docx'
            with zipfile.ZipFile(f,'w') as z:
                z.writestr('word/_rels/document.xml.rels','<Relationships><Relationship Id="r1" Target="media/setting.png"/></Relationships>')
                z.writestr('word/document.xml','<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body><w:p><w:r><w:t>打开参数面板</w:t></w:r></w:p><w:p><w:r><a:blip r:embed="r1"/></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>参数A</w:t></w:r></w:p></w:tc></w:tr></w:tbl><w:p><w:r><w:t>完成检查</w:t></w:r></w:p></w:body></w:document>')
            text=m.word_html(f)
            self.assertLess(text.index('打开参数面板'),text.index('word/media/setting.png'))
            self.assertLess(text.index('word/media/setting.png'),text.index('参数A'))
            self.assertLess(text.index('参数A'),text.index('完成检查'))
            self.assertIn('<table>',text)
if __name__=='__main__':unittest.main()
