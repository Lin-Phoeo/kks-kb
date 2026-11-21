"""Run: py -m unittest discover -s tests -p test_image_recovery_v2.py -v"""
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
import zipfile

from PIL import Image
import pymupdf

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('recovery', ROOT / 'ops/tools/recover_images_v2.py')
recovery = importlib.util.module_from_spec(spec)
spec.loader.exec_module(recovery)


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.out = self.root / 'out'
        self.out.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def image(self, name, color='red', size=(160, 80)):
        p = self.root / name
        Image.new('RGB', size, color).save(p)
        return p

    def test_html_lazy_small_screenshots_are_not_dropped(self):
        self.image('small.png')
        p = self.root / 'a.html'
        p.write_text('<p>step</p><img src="missing.gif" data-src="small.png" alt="setting">', encoding='utf-8')
        result = recovery.extract_document(p, self.out)
        self.assertEqual(len(result['images']), 1)
        self.assertEqual(result['images'][0]['w'], 160)
        self.assertEqual(result['unresolved'], [])

    def test_markdown_extracts_more_than_twelve_images_in_order(self):
        for i in range(16):
            self.image(f'img{i}.png', (i * 10, 20, 30))
        p = self.root / 'a.md'
        p.write_text('\n'.join(f'![setting{i}](img{i}.png)' for i in range(16)), encoding='utf-8')
        result = recovery.extract_document(p, self.out)
        self.assertEqual(len(result['images']), 16)
        self.assertEqual(result['images'][-1]['reference'], 'img15.png')

    def test_gif_animation_is_preserved_not_flattened_to_jpeg(self):
        p = self.root / 'anim.gif'
        Image.new('RGB', (90, 60), 'red').save(p, save_all=True, append_images=[Image.new('RGB', (90, 60), 'blue')], duration=150, loop=0)
        source = self.root / 'a.md'
        source.write_text('![animation](anim.gif)', encoding='utf-8')
        result = recovery.extract_document(source, self.out)
        self.assertEqual(len(result['images']), 1)
        orig = result['images'][0]['orig'].split('/', 1)[1]
        with Image.open(self.out / 'orig' / orig) as im:
            self.assertEqual(im.n_frames, 2)

    def test_docx_media_uses_document_relationship_order(self):
        b = io.BytesIO(); Image.new('RGB', (120, 100), 'blue').save(b, format='PNG')
        r = io.BytesIO(); Image.new('RGB', (100, 90), 'red').save(r, format='PNG')
        p = self.root / 'a.docx'
        with zipfile.ZipFile(p, 'w') as z:
            z.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><a:blip r:embed="r2"/><a:blip r:embed="r1"/></w:document>')
            z.writestr('word/_rels/document.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Target="media/red.png"/><Relationship Id="r2" Target="media/blue.png"/></Relationships>')
            z.writestr('word/media/red.png', r.getvalue()); z.writestr('word/media/blue.png', b.getvalue())
        result = recovery.extract_document(p, self.out)
        self.assertEqual([im['reference'] for im in result['images']], ['word/media/blue.png', 'word/media/red.png'])

    def test_pdf_vector_diagram_page_is_rendered(self):
        p = self.root / 'vector.pdf'
        doc = pymupdf.open(); page = doc.new_page(); page.draw_rect(pymupdf.Rect(50, 50, 300, 300), color=(1, 0, 0)); doc.save(p); doc.close()
        result = recovery.extract_document(p, self.out)
        self.assertEqual(len(result['images']), 1)
        self.assertEqual(result['images'][0]['role'], 'pdf_page')
        self.assertEqual(result['images'][0]['page'], 1)

    def test_forum_avatars_outside_post_are_not_tutorial_images(self):
        self.image('avatar.png')
        self.image('setting.png', 'blue')
        p = self.root / 'post.html'
        p.write_text('<img src="avatar.png"><div class="pcb"><div class="t_f"><img data-src="setting.png"></div></div>', encoding='utf-8')
        result = recovery.extract_document(p, self.out)
        self.assertEqual([im['reference'] for im in result['images']], ['setting.png'])

    def test_remote_reference_remains_pending_not_classified_no_images(self):
        p = self.root / 'a.md'; p.write_text('![unfetched](https://example.com/tutorial.png)', encoding='utf-8')
        result = recovery.extract_document(p, self.out)
        self.assertEqual(result['visual_count'], 1)
        self.assertEqual(result['unresolved'][0]['reason'], 'remote_not_fetched')


if __name__ == '__main__':
    unittest.main()
