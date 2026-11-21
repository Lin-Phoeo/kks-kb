import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('doc_images', ROOT / 'app/kb/content/doc_images.py')
renderer = importlib.util.module_from_spec(spec); spec.loader.exec_module(renderer)


class DocImageTests(unittest.TestCase):
    def test_preserves_image_at_its_original_position(self):
        html = '<h2>first</h2><p>before</p><img src="../imgs/diagram.png" alt="diagram"><p>after</p>'
        mapping = [{'reference': '../imgs/diagram.png', 'orig': 'recover/a.png', 'thumb': 'recover/a.jpg'}]
        result, used = renderer.rewrite_images(html, mapping)
        self.assertIn('src="/thumb/recover/a.jpg"', result)
        self.assertIn('data-original="/orig/recover/a.png"', result)
        self.assertIn('loading="lazy"', result)
        self.assertLess(result.index('before'), result.index('/orig/'))
        self.assertLess(result.index('/orig/'), result.index('after'))
        self.assertEqual(used, {'recover/a.jpg'})

    def test_lazy_source_is_used_without_executable_attributes(self):
        html = '<img src="placeholder.gif" data-src="figure.gif" onerror="evil()">'
        result, used = renderer.rewrite_images(html, [{'reference': 'figure.gif', 'thumb': 'recover/b.jpg', 'orig': 'recover/b.gif'}])
        self.assertIn('/orig/recover/b.gif', result)
        self.assertNotIn('onerror', result)
        self.assertNotIn('data-src', result)
        self.assertEqual(len(used), 1)

    def test_unrelated_reference_is_not_guessed(self):
        result, used = renderer.rewrite_images('<img src="other.png">', [{'reference': 'figure.png', 'thumb': 'recover/b.jpg'}])
        self.assertEqual(used, set())
        self.assertIn('other.png', result)

    def test_untrusted_manifest_path_is_not_injected(self):
        result, used = renderer.rewrite_images('<img src="figure.png">', [{'reference': 'figure.png', 'thumb': '../config.json'}])
        self.assertEqual(used, set())
        self.assertNotIn('/orig/../', result)


if __name__ == '__main__': unittest.main()
