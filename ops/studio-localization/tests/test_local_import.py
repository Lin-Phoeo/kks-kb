import importlib.util
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import local_import
import build_release


class ImportPathTests(unittest.TestCase):
    def test_nested_package_output_is_rejected_before_io(self):
        with self.assertRaisesRegex(ValueError, '递归复制'):
            local_import.materialize(Path('unused game'), [], ROOT / 'local', False)

    def test_nested_release_output_is_rejected_before_io(self):
        with self.assertRaisesRegex(ValueError, 'outside'):
            build_release.build(Path('unused artifacts'), ROOT / 'release', 'fixture')

    def test_release_version_cannot_escape_output(self):
        with self.assertRaisesRegex(ValueError, 'version'):
            build_release.build(Path('unused artifacts'), Path('unused output'), '../outside')


if __name__ == '__main__':
    unittest.main()
