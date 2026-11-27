"""Synthetic installation only; pass the existing rc.2 package with --localization-package."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import community_zh as delta

PACKAGE = None


class CommunityDeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='community-delta-test-')
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name) / 'synthetic-game'
        self.game.mkdir()
        (self.game / 'CharaStudio.exe').write_bytes(b'synthetic marker only')
        self.api, base = delta.load_installer(PACKAGE)
        self.api.game_stopped = lambda: None
        self.base = {'requirements': {key: dict(base['requirements'][key]) for key in ('configuration_manager', 'patched_core')}}
        helpers = {}
        for key, requirement in self.base['requirements'].items():
            data = ('synthetic-' + key).encode()
            self.write(requirement['paths'][0], data)
            requirement['sha256'] = [hashlib.sha256(data).hexdigest()]
        for key in delta.HELPERS:
            data = ('synthetic-' + key).encode()
            self.write('BepInEx/plugins/' + key + '.dll', data)
            helpers[key] = [hashlib.sha256(data).hexdigest()]
        self.addCleanup(patch.stopall)
        patch.object(delta, 'HELPERS', helpers).start()
        patch.object(delta, 'load_installer', return_value=(self.api, self.base)).start()
        self.write(self.api.CONFIG, b'[General]\nLanguage=zh-CN\n[Files]\nDirectory=Translation/{Lang}/Text\n[Texture]\nTextureDirectory=Translation/{Lang}/Texture\n[ResourceRedirector]\nPreferredStoragePath=Translation/{Lang}/RedirectedResources\n[Behaviour]\nEnableTranslationScoping=True\n[Service]\nEndpoint=unchanged\nFallbackEndpoint=unchanged\n')
        self.catalog = delta.FILES[0][1]
        self.dynamic = delta.FILES[1][1]
        self.scope = delta.FILES[2][1]
        self.custom = {'pluginGuid': 'synthetic.unrelated', 'kind': 'setting', 'section': 'Other', 'settingKey': 'Keep', 'source': 'Keep', 'value': '保留'}
        self.json(self.catalog, [self.custom])
        self.json(self.dynamic, [{'id': 'synthetic-other', 'owner': 'AnotherOwner', 'source': 'Close this window', 'pattern': None, 'replacement': '其他窗口'}])

    def write(self, relative, data):
        path = self.game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def json(self, relative, data):
        self.write(relative, json.dumps(data, ensure_ascii=False).encode())

    def snapshot(self):
        return {p.relative_to(self.game).as_posix(): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}

    def run_delta(self, apply=False):
        return delta.run(self.game, PACKAGE, apply)

    def test_doctor_is_read_only(self):
        before = self.snapshot()
        self.assertEqual(self.run_delta()['planned_files'], 3)
        self.assertEqual(self.snapshot(), before)

    def test_apply_preserves_existing_and_rolls_back(self):
        before = self.snapshot()
        result = self.run_delta(True)
        rows = self.api.read_json(self.game / self.catalog)
        self.assertEqual(rows[0], self.custom)
        self.assertEqual(len(rows), 49)
        self.assertEqual(len(self.api.read_json(self.game / self.dynamic)), 19)
        receipt = Path(result['receipt'])
        record = self.api.read_json(receipt)
        self.assertEqual(len(record['files']), 3)
        self.assertIsNone(record['config'])
        self.assertEqual((receipt.parent / 'originals' / self.catalog).read_bytes(), before[self.catalog])
        self.api.rollback(receipt, True)
        self.assertEqual({k: v for k, v in self.snapshot().items() if not k.startswith('.studio-localization/')}, before)

    def test_repeated_apply_is_noop(self):
        self.run_delta(True)
        before = self.snapshot()
        self.assertEqual(self.run_delta(True)['status'], 'already_current')
        self.assertEqual(self.snapshot(), before)

    def test_conflicting_translation_rejected(self):
        row = dict(self.api.read_json(delta.SOURCE / 'catalog-additions.json')[0], value='Conflicting value')
        self.json(self.catalog, [self.custom, row])
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '冲突'):
            self.run_delta(True)
        self.assertEqual(before, self.snapshot())

    def test_existing_duplicate_conflict_rejected(self):
        self.json(self.catalog, [self.custom, dict(self.custom, value='Changed')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            self.run_delta()

    def test_normalized_catalog_identity_conflict_rejected(self):
        row = dict(self.custom, source='Synthetic\nMultiline')
        for newline in ('\r\n', '\r'):
            self.json(self.catalog, [row, dict(row, source=row['source'].replace('\n', newline), value='Conflicting value')])
            with self.assertRaisesRegex(ValueError, '冲突'):
                self.run_delta()

    def test_null_and_empty_header_identity_conflict_rejected(self):
        row = self.api.read_json(delta.SOURCE / 'catalog-additions.json')[0]
        self.json(self.catalog, [dict(row, section='', settingKey='', value='Conflicting value')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            self.run_delta()

    def test_equivalent_catalog_identity_preserves_source(self):
        row = dict(self.custom, source='Synthetic\nMultiline')
        old = dict(row, source=row['source'].replace('\n', '\r\n'))
        self.json(self.catalog, [old, row])
        self.run_delta(True)
        self.assertIn(old, self.api.read_json(self.game / self.catalog))

    def test_dynamic_exact_ignores_pattern_for_conflicts(self):
        row = self.api.read_json(delta.SOURCE / 'grey-imgui-additions.json')[0]
        self.json(self.dynamic, [dict(row, pattern='unused for exact text', replacement='Conflicting value')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            self.run_delta()

    def test_dynamic_conflict_rejected(self):
        row = dict(self.api.read_json(delta.SOURCE / 'grey-imgui-additions.json')[0], replacement='Conflicting value')
        self.json(self.dynamic, [row])
        with self.assertRaisesRegex(ValueError, '冲突'):
            self.run_delta()

    def test_wrong_helper_hash_rejected(self):
        self.write('BepInEx/plugins/Codex.StudioConfigLocalization.dll', b'unsupported')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '依赖不匹配'):
            self.run_delta(True)
        self.assertEqual(before, self.snapshot())

    def test_unknown_scope_file_rejected(self):
        self.write(self.scope, b'custom text')
        with self.assertRaisesRegex(ValueError, 'TXT 内容冲突'):
            self.run_delta()

    def test_owned_conflicting_scope_file_rejected(self):
        self.write(self.scope, b'previously owned custom text')
        self.api.owned_current_hash = lambda *args: True
        with self.assertRaisesRegex(ValueError, 'TXT 内容冲突'):
            self.run_delta()

    def test_wrong_core_hash_rejected(self):
        self.write(self.base['requirements']['patched_core']['paths'][0], b'unsupported core')
        with self.assertRaisesRegex(ValueError, '依赖不匹配'):
            self.run_delta()

    def test_competing_scope_rejected(self):
        self.write(str(Path(self.scope).parent / '000.txt'), b'Close this window=custom')
        with self.assertRaisesRegex(ValueError, '优先级'):
            self.run_delta()

    def test_path_escape_rejected(self):
        with patch.object(delta, 'FILES', [(delta.FILES[0][0], '../outside.json', 'configuration_catalog')]):
            with self.assertRaisesRegex(ValueError, 'Unsafe'):
                self.run_delta()

    def test_concurrent_catalog_change_rejected(self):
        original_install = self.api.install
        changed = b'concurrent user edit'
        def concurrent(*args):
            self.write(self.catalog, changed)
            return original_install(*args)
        self.api.install = concurrent
        with self.assertRaisesRegex(ValueError, '预检后文件发生变化'):
            self.run_delta(True)
        self.assertEqual((self.game / self.catalog).read_bytes(), changed)


class PackageIdentityTests(unittest.TestCase):
    def test_unknown_installer_never_executed(self):
        with tempfile.TemporaryDirectory(prefix='community-package-test-') as directory:
            package = Path(directory)
            (package / 'studio_zh.py').write_text('raise AssertionError("must not execute")', encoding='utf-8')
            (package / 'manifest.json').write_text('{}', encoding='utf-8')
            with self.assertRaisesRegex(ValueError, '未经修改'):
                delta.load_installer(package)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--localization-package', required=True, type=Path)
    args, remaining = parser.parse_known_args()
    PACKAGE = args.localization_package.resolve()
    unittest.main(argv=['test_community_zh.py'] + remaining)
