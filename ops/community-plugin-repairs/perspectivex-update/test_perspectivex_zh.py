"""Temporary synthetic installations; never inspect or modify a real game."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import perspectivex_zh as delta

PACKAGE = None


class PerspectiveDeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='perspectivex-delta-test-')
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name) / 'synthetic-game'
        self.game.mkdir()
        self.write('CharaStudio.exe', b'synthetic marker')
        self.shared = delta.load_shared()
        self.api, original = self.shared.load_installer(PACKAGE)
        self.api.game_stopped = lambda: None
        self.scope = next(row['destination'] for row in original['files'] if '/Text/Plugins/KK_PerspectiveX/' in row['destination'])
        scope_bytes = b'PerspectiveX=synthetic scope'
        self.base = {'requirements': {key: dict(original['requirements'][key]) for key in ('configuration_manager', 'patched_core')},
                     'files': [{'destination': self.scope, 'sha256': hashlib.sha256(scope_bytes).hexdigest()}]}
        self.write(self.scope, scope_bytes)
        for key, requirement in self.base['requirements'].items():
            data = ('synthetic-' + key).encode()
            self.write(requirement['paths'][0], data)
            requirement['sha256'] = [hashlib.sha256(data).hexdigest()]
        self.shared.HELPERS = {}
        for key in ('Codex.StudioConfigLocalization', 'Codex.StudioImGuiDynamicBridge'):
            data = ('synthetic-' + key).encode()
            self.write('BepInEx/plugins/' + key + '.dll', data)
            self.shared.HELPERS[key] = [hashlib.sha256(data).hexdigest()]
        self.plugin = 'BepInEx/plugins/KKS_PerspectiveX.dll'
        plugin_bytes = b'synthetic reviewed 1.4.1 marker'
        self.write(self.plugin, plugin_bytes)
        self.plugin_config = 'BepInEx/config/bucky.kk.perspectivex.cfg'
        self.write(self.plugin_config, b'[Saved views]\nSlot 1=synthetic-preserved-view\n[Comfort]\nRight-drag moves the camera=true\n')
        self.write(self.api.CONFIG, b'[General]\nLanguage=zh-CN\n[Files]\nDirectory=Translation/{Lang}/Text\n[Texture]\nTextureDirectory=Translation/{Lang}/Texture\n[ResourceRedirector]\nPreferredStoragePath=Translation/{Lang}/RedirectedResources\n[Behaviour]\nEnableTranslationScoping=True\n[Service]\nEndpoint=unchanged\nFallbackEndpoint=unchanged\n')
        self.catalog, self.dynamic = [row[1] for row in delta.FILES]
        self.original_row = {'pluginGuid': 'unrelated.synthetic', 'kind': 'setting', 'section': 'Settings', 'settingKey': 'Keep', 'source': 'Keep', 'value': '保留'}
        self.json(self.catalog, [self.original_row])
        self.json(self.dynamic, [{'id': 'synthetic-elsewhere', 'owner': 'OtherOwner', 'source': 'Enabled', 'pattern': None, 'replacement': '另一个窗口'}])
        self.addCleanup(patch.stopall)
        patch.object(self.shared, 'load_installer', return_value=(self.api, self.base)).start()
        patch.object(delta, 'load_shared', return_value=self.shared).start()
        patch.object(delta, 'PLUGIN_SHA', hashlib.sha256(plugin_bytes).hexdigest()).start()

    def write(self, relative, data):
        path = self.game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def json(self, relative, data):
        self.write(relative, json.dumps(data, ensure_ascii=False).encode())

    def snapshot(self):
        return {p.relative_to(self.game).as_posix(): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}

    def test_doctor_has_no_game_writes(self):
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE)['planned_files'], 2)
        self.assertEqual(before, self.snapshot())

    def test_apply_preserves_unrelated_binary_config_and_rolls_back(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE, True)
        self.assertEqual(self.api.read_json(self.game / self.catalog)[0], self.original_row)
        self.assertEqual(len(self.api.read_json(self.game / self.catalog)), 47)
        self.assertEqual(len(self.api.read_json(self.game / self.dynamic)), 5)
        self.assertEqual((self.game / self.plugin).read_bytes(), before[self.plugin])
        self.assertEqual((self.game / self.plugin_config).read_bytes(), before[self.plugin_config])
        self.assertFalse(result['runtime_verified'])
        self.api.rollback(Path(result['receipt']), True)
        self.assertEqual({k: v for k, v in self.snapshot().items() if not k.startswith('.studio-localization/')}, before)

    def test_second_apply_is_noop(self):
        delta.run(self.game, PACKAGE, True)
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE, True)['status'], 'already_current')
        self.assertEqual(before, self.snapshot())

    def test_wrong_plugin_version_or_bytes_rejected(self):
        self.write(self.plugin, b'synthetic old version or modified candidate')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '依赖不匹配 KKS_PerspectiveX'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual(before, self.snapshot())

    def test_missing_base_scope_rejected(self):
        (self.game / self.scope).unlink()
        with self.assertRaisesRegex(ValueError, '作用域词表'):
            delta.run(self.game, PACKAGE)

    def test_configuration_conflict_rejected(self):
        first = self.api.read_json(delta.SOURCE / 'catalog-additions.json')[0]
        self.json(self.catalog, [dict(first, value='冲突')])
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '冲突'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual(before, self.snapshot())

    def test_reviewed_earlier_scope_accepted_without_replacing_it(self):
        (self.game / self.scope).unlink()
        earlier = 'BepInEx/Translation/zh-CN/Text/Plugins/KK_PerspectiveX/earlier-reviewed.txt'
        content = b'PerspectiveX=synthetic reviewed earlier text'
        self.write(earlier, content)
        with patch.object(delta, 'REVIEWED_EARLIER_SCOPES', [{'destination': earlier, 'sha256': hashlib.sha256(content).hexdigest()}]):
            before = self.snapshot()
            result = delta.run(self.game, PACKAGE)
            self.assertEqual(result['scope_identity']['destination'], earlier)
            self.assertEqual(before, self.snapshot())
            self.write(earlier, b'changed')
            with self.assertRaisesRegex(ValueError, '作用域词表'):
                delta.run(self.game, PACKAGE)

    def test_empty_comment_or_modified_scope_rejected(self):
        for content in (b'', b'// comments only', b'PerspectiveX=custom value'):
            with self.subTest(content=content):
                self.write(self.scope, content)
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, '作用域词表'):
                    delta.run(self.game, PACKAGE, True)
                self.assertEqual(before, self.snapshot())

    def test_dynamic_owner_conflict_rejected(self):
        first = self.api.read_json(delta.SOURCE / 'imgui-additions.json')[0]
        self.json(self.dynamic, [dict(first, replacement='冲突')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            delta.run(self.game, PACKAGE)

    def test_concurrent_catalog_edit_preserved(self):
        original = self.api.install
        def changed(*args):
            self.write(self.catalog, b'concurrent-edit')
            return original(*args)
        self.api.install = changed
        with self.assertRaisesRegex(ValueError, '预检后文件发生变化'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual((self.game / self.catalog).read_bytes(), b'concurrent-edit')

    def test_escape_rejected(self):
        with patch.object(delta, 'FILES', [('catalog-additions.json', '../outside.json', 'configuration_catalog')]):
            with self.assertRaisesRegex(ValueError, 'Unsafe'):
                delta.run(self.game, PACKAGE)


class DataTests(unittest.TestCase):
    def test_reviewed_counts_and_scopes(self):
        config = json.loads((delta.SOURCE / 'catalog-additions.json').read_text('utf-8'))
        dynamic = json.loads((delta.SOURCE / 'imgui-additions.json').read_text('utf-8'))
        self.assertEqual(len(config), 46)
        self.assertEqual(len(dynamic), 4)
        self.assertTrue(all(row['pluginGuid'] == 'bucky.kk.perspectivex' and row['kind'] != 'enum' for row in config))
        self.assertTrue(all(row['owner'] == 'KK_PerspectiveX' and row['pattern'] is None for row in dynamic))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--localization-package', type=Path, required=True)
    args, remaining = parser.parse_known_args()
    PACKAGE = args.localization_package.resolve()
    unittest.main(argv=['test_perspectivex_zh.py'] + remaining)
