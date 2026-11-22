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

    def test_mapping_without_reference_does_not_replace_unrelated_image(self):
        html = '<p>x</p><img src="unrelated.png" alt="u">'
        mapping = [{'orig': 'recover/a.png', 'thumb': 'recover/a.jpg'}]
        result, used = renderer.rewrite_images(html, mapping)
        self.assertEqual(result, html)
        self.assertEqual(used, set())
        self.assertNotIn('/thumb/', result)
        self.assertNotIn('/orig/', result)

    def test_traversal_thumb_with_valid_orig_is_never_written_to_src(self):
        html = '<img src="figure.png">'
        mapping = [{'reference': 'figure.png', 'catdir': 'recover', 'orig': 'recover/a.png', 'thumb': '../secret.jpg'}]
        result, used = renderer.rewrite_images(html, mapping)
        self.assertEqual(result, html)
        self.assertEqual(used, set())
        self.assertNotIn('../secret.jpg', result)
        self.assertNotIn('/thumb/', result)

if __name__ == '__main__': unittest.main()
