"""Clo-specific boundaries in temporary synthetic installations only."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import cloxray_zh as delta

PACKAGE = None


class CloDeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='cloxray-zh-test-')
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name) / 'synthetic-game'
        self.game.mkdir()
        self.write('CharaStudio.exe', b'synthetic marker')
        self.shared = delta.load_shared()
        self.api, original = self.shared.load_installer(PACKAGE)
        self.api.game_stopped = lambda: None
        self.base = {'requirements': {key: dict(original['requirements'][key]) for key in ('configuration_manager', 'patched_core')}}
        for key, requirement in self.base['requirements'].items():
            raw = ('synthetic-' + key).encode()
            self.write(requirement['paths'][0], raw)
            requirement['sha256'] = [hashlib.sha256(raw).hexdigest()]
        self.shared.HELPERS = {}
        for key in ('Codex.StudioConfigLocalization', 'Codex.StudioImGuiDynamicBridge'):
            raw = ('synthetic-' + key).encode()
            self.write('BepInEx/plugins/' + key + '.dll', raw)
            self.shared.HELPERS[key] = [hashlib.sha256(raw).hexdigest()]
        self.plugin = 'BepInEx/plugins/Clo/LiquidWobbleMPB.dll'
        plugin_bytes = b'synthetic reviewed CloXray marker'
        self.write(self.plugin, plugin_bytes)
        self.write('BepInEx/config/Clo.LiquidWobbleMPB.cfg', b'[AutoBodyReveal]\nApply Now Hotkey=L + LeftShift\n')
        self.write('BepInEx/plugins/Codex.StudioImGuiDynamicBridge.json', b'[{"unrelated":"keep"}]')
        self.write('mods/synthetic-unrelated.zipmod', b'unchanged resource')
        self.write(self.api.CONFIG, b'[General]\nLanguage=zh-CN\n[Files]\nDirectory=Translation/{Lang}/Text\n[Texture]\nTextureDirectory=Translation/{Lang}/Texture\n[ResourceRedirector]\nPreferredStoragePath=Translation/{Lang}/RedirectedResources\n[Behaviour]\nEnableTranslationScoping=True\n[Service]\nEndpoint=unchanged\nFallbackEndpoint=unchanged\n')
        self.catalog = delta.FILES[0][1]
        self.required = self.api.read_json(delta.SOURCE / 'required-base-identities.json')
        self.unrelated = {'pluginGuid': 'synthetic.other', 'kind': 'setting', 'section': 'Settings', 'settingKey': 'Keep', 'source': 'Keep', 'value': '保留'}
        self.json(self.catalog, [self.unrelated] + self.required)
        self.addCleanup(patch.stopall)
        patch.object(self.shared, 'load_installer', return_value=(self.api, self.base)).start()
        patch.object(delta, 'load_shared', return_value=self.shared).start()
        patch.object(delta, 'PLUGIN_SHA', hashlib.sha256(plugin_bytes).hexdigest()).start()

    def write(self, relative, raw):
        path = self.game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)

    def json(self, relative, value):
        self.write(relative, json.dumps(value, ensure_ascii=False).encode('utf-8'))

    def snapshot(self):
        return {p.relative_to(self.game).as_posix(): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}

    def test_doctor_writes_nothing(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE)
        self.assertEqual(result['planned_files'], 1)
        self.assertEqual(result['reviewed_display_identities'], 39)
        self.assertEqual(before, self.snapshot())

    def test_one_catalog_only_preserves_values_and_rolls_back(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE, True)
        after = self.snapshot()
        changes = [name for name, raw in before.items() if after.get(name) != raw]
        self.assertEqual(changes, [self.catalog])
        rows = self.api.read_json(self.game / self.catalog)
        self.assertEqual(len(rows), 40)
        self.assertEqual(rows[:15], [self.unrelated] + self.required)
        receipt = Path(result['receipt'])
        record = self.api.read_json(receipt)
        self.assertEqual(len(record['files']), 1)
        self.assertIsNone(record['config'])
        self.api.rollback(receipt, True)
        self.assertEqual({k: v for k, v in self.snapshot().items() if not k.startswith('.studio-localization/')}, before)

    def test_unknown_clo_binary_rejected_without_writes(self):
        self.write(self.plugin, b'synthetic unsupported or old Clo DLL')
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '依赖不匹配 Clo/LiquidWobbleMPB'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual(before, self.snapshot())

    def test_incomplete_or_changed_base_never_claims_full_coverage(self):
        variants = [self.required[1:], [dict(self.required[0], value='未审译文')] + self.required[1:]]
        for rows in variants:
            with self.subTest(rows=len(rows)):
                self.json(self.catalog, [self.unrelated] + rows)
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, '14 条复用基础译文'):
                    delta.run(self.game, PACKAGE, True)
                self.assertEqual(before, self.snapshot())

    def test_added_identity_conflict_rejected(self):
        first = self.api.read_json(delta.SOURCE / 'catalog-additions.json')[0]
        self.json(self.catalog, [self.unrelated] + self.required + [dict(first, value='冲突')])
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, '冲突'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual(before, self.snapshot())

    def test_repeated_apply_is_noop(self):
        delta.run(self.game, PACKAGE, True)
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE, True)['status'], 'already_current')
        self.assertEqual(before, self.snapshot())

    def test_data_is_scoped_and_no_value_mappings(self):
        added = self.api.read_json(delta.SOURCE / 'catalog-additions.json')
        self.assertEqual(len(added), 25)
        self.assertEqual(len(self.required), 14)
        self.assertTrue(all(row['pluginGuid'] == 'Clo.LiquidWobbleMPB' and row['kind'] != 'enum' for row in added + self.required))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--localization-package', type=Path, required=True)
    args, remaining = parser.parse_known_args()
    PACKAGE = args.localization_package.resolve()
    unittest.main(argv=['test_cloxray_zh.py'] + remaining)
