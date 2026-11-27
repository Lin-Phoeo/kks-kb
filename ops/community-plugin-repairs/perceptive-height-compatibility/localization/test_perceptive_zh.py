"""Synthetic install checks; requires the existing rc.2 package, not a game installation."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import perceptive_zh as delta

PACKAGE = None


class DeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='perceptive-zh-test-')
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name)
        self.shared = delta.load_shared()
        self.api, manifest = self.shared.load_installer(PACKAGE)
        self.api.game_stopped = lambda: None
        self.base = {'requirements': {key: dict(manifest['requirements'][key]) for key in ('configuration_manager', 'patched_core')}}
        self.write('CharaStudio.exe', b'synthetic marker')
        for key, row in self.base['requirements'].items():
            data = key.encode()
            self.write(row['paths'][0], data)
            row['sha256'] = [hashlib.sha256(data).hexdigest()]
        self.shared.HELPERS = {}
        for key in ('Codex.StudioConfigLocalization', 'Codex.StudioImGuiDynamicBridge'):
            self.write('BepInEx/plugins/' + key + '.dll', key.encode())
            self.shared.HELPERS[key] = [hashlib.sha256(key.encode()).hexdigest()]
        self.plugin = 'BepInEx/plugins/PerceptiveHeightX.KKS.dll'
        self.write(self.plugin, b'synthetic reviewed plugin')
        self.cfg = 'BepInEx/config/bucky.kk.perceptiveheightx.cfg'
        self.write(self.cfg, b'[General]\nRelock reference height = F7 + RightControl + RightShift\nUnits=FeetInches\n')
        self.write(self.api.CONFIG, b'[General]\nLanguage=zh-CN\n[Files]\nDirectory=Translation/{Lang}/Text\n[Texture]\nTextureDirectory=Translation/{Lang}/Texture\n[ResourceRedirector]\nPreferredStoragePath=Translation/{Lang}/RedirectedResources\n[Behaviour]\nEnableTranslationScoping=True\n[Service]\nEndpoint=preserved\nFallbackEndpoint=preserved\n')
        self.catalog, self.dynamic, self.scope = [row[1] for row in delta.FILES]
        self.custom = dict(pluginGuid='synthetic', kind='setting', section='General', settingKey='Units', source='Units', value='other units')
        self.json(self.catalog, [self.custom])
        self.json(self.dynamic, [dict(id='other', owner='Other', source='PerceptiveHeightX', pattern=None, replacement='other')])
        self.addCleanup(patch.stopall)
        patch.object(delta, 'load_shared', return_value=self.shared).start()
        patch.object(self.shared, 'load_installer', return_value=(self.api, self.base)).start()
        patch.object(delta, 'PLUGIN_SHA', hashlib.sha256(b'synthetic reviewed plugin').hexdigest()).start()

    def write(self, rel, data):
        p = self.game / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)

    def json(self, rel, value):
        self.write(rel, json.dumps(value, ensure_ascii=False).encode())

    def snapshot(self):
        return {p.relative_to(self.game).as_posix(): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}

    def test_doctor_read_only(self):
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE)['planned_files'], 3)
        self.assertEqual(before, self.snapshot())

    def test_apply_preserves_scopes_binary_cfg_and_rollback(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE, True)
        self.assertIn(self.custom, self.api.read_json(self.game / self.catalog))
        self.assertEqual(len(self.api.read_json(self.game / self.catalog)), 20)
        self.assertEqual(len(self.api.read_json(self.game / self.dynamic)), 4)
        self.assertEqual((self.game / self.plugin).read_bytes(), before[self.plugin])
        self.assertEqual((self.game / self.cfg).read_bytes(), before[self.cfg])
        self.assertEqual((self.game / self.scope).read_bytes(), (delta.SOURCE / delta.FILES[2][0]).read_bytes())
        self.api.rollback(Path(result['receipt']), True)
        self.assertEqual(before, {k: v for k, v in self.snapshot().items() if not k.startswith('.studio-localization/')})

    def test_repeat_is_noop(self):
        delta.run(self.game, PACKAGE, True)
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE, True)['status'], 'already_current')
        self.assertEqual(before, self.snapshot())

    def test_wrong_plugin_refused(self):
        self.write(self.plugin, b'unsupported original')
        with self.assertRaisesRegex(ValueError, '依赖不匹配'):
            delta.run(self.game, PACKAGE, True)

    def test_catalog_conflict_refused(self):
        row = self.api.read_json(delta.SOURCE / 'catalog-additions.json')[0]
        self.json(self.catalog, [dict(row, value='different')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            delta.run(self.game, PACKAGE, True)

    def test_dynamic_conflict_refused(self):
        row = self.api.read_json(delta.SOURCE / 'imgui-additions.json')[1]
        self.json(self.dynamic, [dict(row, replacement='different')])
        with self.assertRaisesRegex(ValueError, '冲突'):
            delta.run(self.game, PACKAGE)

    def test_scope_conflict_refused(self):
        self.write(self.scope, b'other text')
        with self.assertRaisesRegex(ValueError, 'TXT 内容冲突'):
            delta.run(self.game, PACKAGE)

    def test_escape_refused(self):
        with patch.object(delta, 'FILES', [(delta.FILES[0][0], '../outside.json', 'configuration_catalog')]):
            with self.assertRaisesRegex(ValueError, 'Unsafe'):
                delta.run(self.game, PACKAGE)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--localization-package', type=Path, required=True)
    args, remaining = parser.parse_known_args()
    PACKAGE = args.localization_package
    unittest.main(argv=['test_perceptive_zh.py'] + remaining)
