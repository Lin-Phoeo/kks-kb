"""Only temporary synthetic installations; no real game files or processes."""
import argparse
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import reviewed_zh as delta

PACKAGE = None


class ReviewedDeltaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='reviewed-zh-test-')
        self.addCleanup(self.temp.cleanup)
        self.game = Path(self.temp.name) / 'synthetic-game'
        self.game.mkdir()
        self.write('CharaStudio.exe', b'synthetic marker')
        self.shared = delta.load_shared()
        self.api, original = self.shared.load_installer(PACKAGE)
        self.api.game_stopped = lambda: None
        self.base = {'requirements': {key: dict(original['requirements'][key]) for key in ('configuration_manager', 'patched_core')}}
        for key, requirement in self.base['requirements'].items():
            data = ('synthetic-' + key).encode()
            self.write(requirement['paths'][0], data)
            requirement['sha256'] = [hashlib.sha256(data).hexdigest()]
        self.shared.HELPERS = {}
        for key in ('Codex.StudioConfigLocalization', 'Codex.StudioImGuiDynamicBridge'):
            data = ('synthetic-' + key).encode()
            self.write('BepInEx/plugins/' + key + '.dll', data)
            self.shared.HELPERS[key] = [hashlib.sha256(data).hexdigest()]
        plugins = {}
        for key in delta.PLUGINS:
            data = ('synthetic reviewed ' + key).encode()
            self.write('BepInEx/plugins/' + key + '.dll', data)
            plugins[key] = [hashlib.sha256(data).hexdigest()]
        self.write('BepInEx/config/com.shallty.blendshapecreator.cfg', b'[MAIN]\nKeyboard shortcut=N + RightAlt\n')
        self.write('BepInEx/config/local.kkworkshop.0a52b20541f5.gin-rope-this.cfg', b'[Interface]\nToggle panel=F8\n')
        self.write('mods/synthetic-unrelated.zipmod', b'keep this resource')
        self.write(self.api.CONFIG, b'[General]\nLanguage=zh-CN\n[Files]\nDirectory=Translation/{Lang}/Text\n[Texture]\nTextureDirectory=Translation/{Lang}/Texture\n[ResourceRedirector]\nPreferredStoragePath=Translation/{Lang}/RedirectedResources\n[Behaviour]\nEnableTranslationScoping=True\n[Service]\nEndpoint=unchanged\nFallbackEndpoint=unchanged\n')
        self.catalog, self.dynamic = delta.FILES[0][1], delta.FILES[1][1]
        self.custom = {'pluginGuid': 'synthetic.other', 'kind': 'setting', 'section': 'Settings', 'settingKey': 'Keep', 'source': 'Keep', 'value': '保留'}
        self.json(self.catalog, [self.custom])
        self.json(self.dynamic, [{'id': 'other-owner', 'owner': 'Other', 'source': 'Search', 'pattern': None, 'replacement': '另一个搜索'}])
        self.addCleanup(patch.stopall)
        patch.object(self.shared, 'load_installer', return_value=(self.api, self.base)).start()
        patch.object(delta, 'load_shared', return_value=self.shared).start()
        patch.object(delta, 'PLUGINS', plugins).start()

    def write(self, relative, data):
        path = self.game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)

    def json(self, relative, data):
        self.write(relative, json.dumps(data, ensure_ascii=False).encode())

    def snapshot(self):
        return {p.relative_to(self.game).as_posix(): p.read_bytes() for p in self.game.rglob('*') if p.is_file()}

    def test_doctor_read_only_plans_four_files(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE)
        self.assertEqual(result['planned_files'], 4)
        self.assertEqual(set(result['destinations']), {row[1] for row in delta.FILES})
        self.assertEqual(before, self.snapshot())

    def test_apply_exact_four_files_and_distinct_scope_bytes_then_rollback(self):
        before = self.snapshot()
        result = delta.run(self.game, PACKAGE, True)
        after = self.snapshot()
        changed = {name for name in before.keys() | after.keys() if before.get(name) != after.get(name) and not name.startswith('.studio-localization/')}
        self.assertEqual(changed, {row[1] for row in delta.FILES})
        self.assertEqual(self.api.read_json(self.game / self.catalog)[0], self.custom)
        self.assertEqual(len(self.api.read_json(self.game / self.catalog)), 23)
        self.assertEqual(len(self.api.read_json(self.game / self.dynamic)), 75)
        scope_bytes = []
        for source, destination, _ in delta.FILES[2:]:
            expected = (delta.SOURCE / source).read_bytes()
            self.assertEqual((self.game / destination).read_bytes(), expected)
            scope_bytes.append(expected)
        self.assertNotEqual(scope_bytes[0], scope_bytes[1])
        receipt = Path(result['receipt'])
        self.assertEqual(len(self.api.read_json(receipt)['files']), 4)
        self.api.rollback(receipt, True)
        self.assertEqual({k: v for k, v in self.snapshot().items() if not k.startswith('.studio-localization/')}, before)

    def test_repeated_apply_noop(self):
        delta.run(self.game, PACKAGE, True)
        before = self.snapshot()
        self.assertEqual(delta.run(self.game, PACKAGE, True)['status'], 'already_current')
        self.assertEqual(before, self.snapshot())

    def test_each_plugin_exact_bytes_required(self):
        for name in delta.PLUGINS:
            with self.subTest(plugin=name):
                path = self.game / ('BepInEx/plugins/' + name + '.dll')
                old = path.read_bytes()
                path.write_bytes(b'wrong game build or version')
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, '依赖不匹配'):
                    delta.run(self.game, PACKAGE, True)
                self.assertEqual(before, self.snapshot())
                path.write_bytes(old)

    def test_conflicting_f1_or_dynamic_translation_rejected(self):
        for file, target, value_key in [('catalog-additions.json', self.catalog, 'value'), ('imgui-additions.json', self.dynamic, 'replacement')]:
            with self.subTest(file=file):
                old = (self.game / target).read_bytes()
                row = self.api.read_json(delta.SOURCE / file)[0]
                self.json(target, [dict(row, **{value_key: '冲突'})])
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, '冲突'):
                    delta.run(self.game, PACKAGE, True)
                self.assertEqual(before, self.snapshot())
                self.write(target, old)

    def test_each_conflicting_scope_preserved(self):
        for _, destination, _ in delta.FILES[2:]:
            with self.subTest(destination=destination):
                self.write(destination, b'prior custom translation')
                before = self.snapshot()
                with self.assertRaisesRegex(ValueError, 'TXT 内容冲突'):
                    delta.run(self.game, PACKAGE, True)
                self.assertEqual(before, self.snapshot())
                (self.game / destination).unlink()

    def test_competing_owner_scope_rejected(self):
        target = Path(delta.FILES[2][1]).parent / '000.txt'
        self.write(str(target), b'Search=other')
        with self.assertRaisesRegex(ValueError, '优先级'):
            delta.run(self.game, PACKAGE)

    def test_concurrent_catalog_edit_is_not_overwritten(self):
        original = self.api.install
        def concurrent(*args):
            self.write(self.catalog, b'concurrent edit')
            return original(*args)
        self.api.install = concurrent
        with self.assertRaisesRegex(ValueError, '预检后文件发生变化'):
            delta.run(self.game, PACKAGE, True)
        self.assertEqual((self.game / self.catalog).read_bytes(), b'concurrent edit')

    def test_source_and_target_escape_rejected(self):
        for row in [('../outside.json', delta.FILES[0][1], 'configuration_catalog'), ('catalog-additions.json', '../outside.json', 'configuration_catalog')]:
            with self.subTest(row=row), patch.object(delta, 'FILES', [row]):
                with self.assertRaisesRegex(ValueError, 'Unsafe'):
                    delta.run(self.game, PACKAGE)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--localization-package', type=Path, required=True)
    args, remaining = parser.parse_known_args()
    PACKAGE = args.localization_package.resolve()
    unittest.main(argv=['test_reviewed_zh.py'] + remaining)
