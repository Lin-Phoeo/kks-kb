import importlib.util
from pathlib import Path
import struct
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location('debug_paths_under_test', Path(__file__).resolve().parents[1] / 'tools/sanitize_debug_paths.py')
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)
NAME = 'Codex.StudioFontController.dll'


def fixture(path=r'X:\synthetic-build\Codex.StudioFontController.pdb', *, pe64=False):
    data = bytearray(2048)
    data[:2] = b'MZ'; struct.pack_into('<I', data, 0x3c, 0x80)
    data[0x80:0x84] = b'PE\0\0'
    size = 240 if pe64 else 224
    struct.pack_into('<HHIIIHH', data, 0x84, 0x8664 if pe64 else 0x14c, 1, 0, 0, 0, size, 0x2002)
    optional = 0x98; struct.pack_into('<H', data, optional, 0x20b if pe64 else 0x10b)
    directory = optional + (112 if pe64 else 96)
    struct.pack_into('<I', data, directory - 4, 16)
    section = optional + size; data[section:section + 8] = b'.text\0\0\0'
    struct.pack_into('<IIII', data, section + 8, 1536, 0x2000, 1536, 512)
    struct.pack_into('<II', data, directory + 14 * 8, 0x2000, 72)
    struct.pack_into('<IHHIIII', data, 512, 72, 2, 5, 0x2080, 32, 1, 0)
    data[640:644] = b'BSJB'
    struct.pack_into('<II', data, directory + 6 * 8, 0x2100, 28)
    payload = b'RSDS' + bytes(range(16)) + struct.pack('<I', 1) + path.encode() + b'\0'
    struct.pack_into('<IIHHIIII', data, 768, 0, 0, 0, 0, 2, len(payload), 0x2140, 832)
    data[832:832 + len(payload)] = payload
    data[1100:1120] = b'synthetic-IL-preserve'
    return data, directory


class DebugPathTests(unittest.TestCase):
    def test_only_absolute_path_bytes_change(self):
        for pe64 in (False, True):
            original, _ = fixture(pe64=pe64)
            patched, report = module.sanitize_debug_paths(bytes(original), NAME)
            self.assertEqual(len(original), len(patched))
            self.assertTrue(report['all_other_bytes_identical'])
            for index, (left, right) in enumerate(zip(original, patched)):
                if left != right:
                    self.assertTrue(any(row['start'] <= index < row['end'] for row in report['path_regions']))
            self.assertEqual(patched[832:856], original[832:856])
            self.assertEqual(patched[1100:1120], original[1100:1120])
            self.assertNotIn(b'synthetic-build', patched)
            self.assertEqual(module.sanitize_debug_paths(patched, NAME)[0], patched)

    def test_unc_path(self):
        data, _ = fixture(r'\\synthetic-host\build\Codex.StudioFontController.pdb')
        patched, report = module.sanitize_debug_paths(bytes(data), NAME)
        self.assertEqual(len(report['path_regions']), 1)
        self.assertNotIn(b'synthetic-host', patched)

    def test_only_known_managed_tool_executable_allowed(self):
        data, _ = fixture(r'X:\build\StudioLocalization.LocalTools.pdb')
        struct.pack_into('<H', data, 0x80 + 22, 0x22)
        struct.pack_into('<I', data, 532, 0x06000001)
        patched, report = module.sanitize_debug_paths(bytes(data), 'StudioLocalization.LocalTools.dll')
        self.assertTrue(report['all_other_bytes_identical'])
        self.assertEqual(len(patched), len(data))
        with self.assertRaises(ValueError): module.sanitize_debug_paths(bytes(data), NAME)
        struct.pack_into('<I', data, 532, 0x2000)
        with self.assertRaises(ValueError): module.sanitize_debug_paths(bytes(data), 'StudioLocalization.LocalTools.dll')

    def test_signed_native_and_unknown_debug_refused(self):
        for category in ('authenticode', 'strong-flag', 'strong-area', 'native', 'non-il', 'embedded', 'unknown'):
            data, directory = fixture()
            if category == 'authenticode': struct.pack_into('<II', data, directory + 4 * 8, 1500, 32)
            elif category == 'strong-flag': struct.pack_into('<I', data, 528, 9)
            elif category == 'strong-area': struct.pack_into('<II', data, 544, 0x2380, 32)
            elif category == 'native': struct.pack_into('<II', data, directory + 14 * 8, 0, 0)
            elif category == 'non-il': struct.pack_into('<I', data, 528, 0)
            else: struct.pack_into('<I', data, 780, 17 if category == 'embedded' else 99)
            with self.subTest(category=category), self.assertRaises(ValueError): module.sanitize_debug_paths(bytes(data), NAME)

    def test_malformed_and_nonowned_refused(self):
        for category in ('truncated', 'signature', 'pointer', 'padding', 'relative', 'identity', 'overlap'):
            data, directory = fixture()
            if category == 'truncated': data = data[:700]
            elif category == 'signature': data[832:836] = b'NB10'
            elif category == 'pointer': struct.pack_into('<I', data, 792, 833)
            elif category == 'padding': data[832 + struct.unpack_from('<I', data, 784)[0] - 1] = 1
            elif category == 'relative': data, _ = fixture(r'relative\Codex.StudioFontController.pdb')
            elif category == 'identity': data, _ = fixture(r'X:\build\other.pdb')
            else: struct.pack_into('<II', data, 536, 0x2140, 90)
            with self.subTest(category=category), self.assertRaises(ValueError): module.sanitize_debug_paths(bytes(data), NAME)
        with self.assertRaises(ValueError): module.sanitize_debug_paths(bytes(fixture()[0]), 'XUnity.AutoTranslator.Plugin.Core.dll')

    def test_source_preserved_and_no_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / NAME; target = Path(folder) / 'output' / NAME
            original = bytes(fixture()[0]); source.write_bytes(original)
            report = module.sanitize_file(source, target)
            self.assertEqual(source.read_bytes(), original)
            self.assertEqual(module.digest(target.read_bytes()), report['after_sha256'])
            with self.assertRaises(ValueError): module.sanitize_file(source, source)
            with self.assertRaises(ValueError): module.sanitize_file(source, target)


if __name__ == '__main__': unittest.main()
