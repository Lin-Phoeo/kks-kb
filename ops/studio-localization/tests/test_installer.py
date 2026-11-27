import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('installer_under_test', Path(__file__).resolve().parents[1] / 'studio_zh.py')
installer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(installer)

CONFIG = '''[General]
Language=zh-CN
[Files]
Directory=Translation/{Lang}/Text
[Texture]
TextureDirectory=Translation/{Lang}/Texture
[ResourceRedirector]
PreferredStoragePath=Translation/{Lang}/RedirectedResources
[Behaviour]
EnableTranslationScoping=False
[Service]
Endpoint=FixtureProvider
FallbackEndpoint=FixtureFallback
'''


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.package = root / 'package 路径'
        self.game = root / 'different drive fixture' / 'Studio with spaces'
        self.package.mkdir()
        self.game.mkdir(parents=True)
        (self.game / 'CharaStudio.exe').write_bytes(b'synthetic game identity')
        config = self.game / installer.CONFIG
        config.parent.mkdir(parents=True)
        config.write_text(CONFIG, 'utf-8')
        source = self.package / 'translation.txt'
        source.write_text('#set exe CharaStudio\nTest=测试\n', 'utf-8')
        self.destination = 'BepInEx/Translation/zh-CN/Text/Plugins/Fixture/0000_Test.txt'
        self.manifest = {'version': 'test', 'requirements': {}, 'files': [{
            'source': 'translation.txt', 'destination': self.destination, 'sha256': installer.digest(source),
            'kind': 'translation', 'requires': [], 'replace_sha256': [],
        }]}
        self.stopped = patch.object(installer, 'game_stopped')
        self.stopped.start()
        self.addCleanup(self.stopped.stop)

    def plan(self):
        return installer.build_plan(self.game, self.manifest, self.package, True, False)

    def apply(self):
        plan, skipped, old, new, keys = self.plan()
        return installer.install(self.game, plan, old, new, keys, self.manifest, self.package)

    def test_dry_run_and_full_rollback_preserve_original(self):
        plan, skipped, old, new, keys = self.plan()
        self.assertEqual(len(plan), 1)
        self.assertFalse((self.game / self.destination).exists())
        receipt = self.apply()
        self.assertIn('Endpoint=\n', (self.game / installer.CONFIG).read_text('utf-8'))
        self.assertEqual(installer.rollback(receipt, False)['files'], 1)
        installer.rollback(receipt, True)
        self.assertEqual((self.game / installer.CONFIG).read_text('utf-8'), CONFIG)
        self.assertFalse((self.game / self.destination).exists())
        self.assertFalse((self.game / self.destination).parent.exists())
        self.assertTrue((receipt.parent / 'removed' / self.destination).is_file())

    def test_changed_file_rejects_rollback(self):
        receipt = self.apply()
        (self.game / self.destination).write_text('user edit', 'utf-8')
        with self.assertRaises(ValueError):
            installer.rollback(receipt, True)
        self.assertEqual((self.game / self.destination).read_text('utf-8'), 'user edit')

    def test_unknown_existing_target_is_preserved(self):
        target = self.game / self.destination
        target.parent.mkdir(parents=True)
        target.write_text('unrelated existing translation', 'utf-8')
        with self.assertRaises(ValueError):
            self.plan()
        self.assertEqual(target.read_text('utf-8'), 'unrelated existing translation')

    def test_interrupted_second_copy_recovers_first(self):
        self.manifest['files'].append(dict(self.manifest['files'][0], destination='BepInEx/plugins/Fixture/helper.txt'))
        real_copy = installer.atomic_copy
        calls = []
        def failing_copy(source, target, run_id):
            calls.append(target)
            if len(calls) == 2:
                raise OSError('synthetic interrupted second copy')
            return real_copy(source, target, run_id)
        with patch.object(installer, 'atomic_copy', failing_copy):
            with self.assertRaises(OSError):
                self.apply()
        self.assertFalse((self.game / self.destination).exists())
        self.assertEqual((self.game / installer.CONFIG).read_text('utf-8'), CONFIG)
        receipts = list((self.game / installer.BACKUPS).glob('*/receipt.json'))
        self.assertEqual(installer.read_json(receipts[0])['status'], 'rolled_back')

    def test_known_replacement_restores_bytes(self):
        target = self.game / self.destination
        target.parent.mkdir(parents=True)
        target.write_bytes(b'old reviewed bytes')
        self.manifest['files'][0]['replace_sha256'] = [installer.digest(target)]
        receipt = self.apply()
        installer.rollback(receipt, True)
        self.assertEqual(target.read_bytes(), b'old reviewed bytes')

    def test_rollback_resumes_after_quarantine_before_restore(self):
        target = self.game / self.destination
        target.parent.mkdir(parents=True)
        target.write_bytes(b'old reviewed bytes')
        self.manifest['files'][0]['replace_sha256'] = [installer.digest(target)]
        receipt = self.apply()
        with patch.object(installer, 'atomic_copy', side_effect=OSError('restore interrupted')):
            with self.assertRaises(OSError):
                installer.rollback(receipt, True)
        self.assertFalse(target.exists())
        self.assertEqual(installer.read_json(receipt)['files'][0]['state'], 'rollback_prepared')
        installer.rollback(receipt, True)
        self.assertEqual(target.read_bytes(), b'old reviewed bytes')
        self.assertEqual((self.game / installer.CONFIG).read_text('utf-8'), CONFIG)

    def test_dependency_unknown_skips_optional(self):
        self.manifest['requirements'] = {'optional': {'paths': ['plugins/missing.dll'], 'sha256': ['a'*64]}}
        self.manifest['files'][0]['requires'] = ['optional']
        plan, skipped, *_ = self.plan()
        self.assertFalse(plan)
        self.assertEqual(len(skipped), 1)

    def test_path_escape_is_rejected(self):
        for value in ('../outside.txt', 'C:/outside', '/outside', 'folder/../../outside'):
            with self.assertRaises(ValueError):
                installer.contained(self.game, value)

    def test_duplicate_target_rejected(self):
        self.manifest['files'].append(self.manifest['files'][0].copy())
        with self.assertRaises(ValueError):
            self.plan()

    def test_manifest_cannot_replace_game_executable(self):
        self.manifest['files'][0]['destination'] = 'CharaStudio.exe'
        with self.assertRaises(ValueError):
            self.plan()

    def test_installer_lock_rejects_concurrent_run(self):
        lock = self.game / '.studio-localization/install.lock'
        lock.parent.mkdir()
        lock.write_text('synthetic concurrent installation', 'utf-8')
        with self.assertRaises(RuntimeError):
            self.apply()
        self.assertFalse((self.game / self.destination).exists())
        self.assertTrue(lock.exists())

    def test_planned_core_upgrade_satisfies_adapter_requirement(self):
        core = self.game / 'BepInEx/plugins/XUnity/Core.dll'
        core.parent.mkdir(parents=True)
        core.write_bytes(b'old supported core')
        source = self.package / 'core.new'
        source.write_bytes(b'new verified core')
        new_hash = installer.digest(source)
        self.manifest['requirements'] = {'patched_core': {
            'paths': ['BepInEx/plugins/XUnity/Core.dll'], 'sha256': [new_hash],
            'allow_planned_replacement': True,
        }}
        self.manifest['files'].append({'source': 'core.new', 'destination': 'BepInEx/plugins/XUnity/Core.dll',
                                       'sha256': new_hash, 'kind': 'core', 'replace_sha256': [installer.digest(core)]})
        self.manifest['files'][0]['requires'] = ['patched_core']
        plan, skipped, *_ = self.plan()
        self.assertEqual(len(plan), 2)
        self.assertFalse(skipped)

    def test_nested_global_priority_is_detected(self):
        self.manifest['files'][0]['destination'] = 'BepInEx/Translation/zh-CN/Text/0000_Test.txt'
        competitor = self.game / 'BepInEx/Translation/zh-CN/Text/0000_Earlier/override.txt'
        competitor.parent.mkdir(parents=True)
        competitor.write_text('Test=另一个值', 'utf-8')
        with self.assertRaises(ValueError):
            self.plan()

    def test_partial_catalog_preserves_existing_reviewed_entries(self):
        destination = 'BepInEx/plugins/Codex.StudioConfigLocalization.json'
        target = self.game / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        old = [{'pluginGuid': 'fixture', 'kind': 'setting', 'section': 'Main', 'settingKey': 'Old', 'source': 'Old', 'value': '已有'},
               {'pluginGuid': 'fixture', 'kind': 'setting', 'section': 'Main', 'settingKey': 'New', 'source': 'New', 'value': '旧译'}]
        new = [dict(old[1], value='修订')]
        target.write_text(json.dumps(old), 'utf-8')
        original = target.read_bytes()
        source = self.package / 'catalog.json'
        source.write_text(json.dumps(new), 'utf-8')
        self.manifest['files'] = [{'source': 'catalog.json', 'destination': destination,
                                  'sha256': installer.digest(source), 'kind': 'configuration_catalog',
                                  'replace_sha256': [installer.digest(target)]}]
        receipt = self.apply()
        values = {row['settingKey']: row['value'] for row in installer.read_json(target)}
        self.assertEqual(values, {'Old': '已有', 'New': '修订'})
        self.assertFalse(self.plan()[0])
        installer.rollback(receipt, True)
        self.assertEqual(target.read_bytes(), original)

    def test_same_catalog_bytes_remain_noop_without_reserialization(self):
        source = self.package / 'catalog.json'
        source.write_text('[{"id":"x","owner":"Fixture","source":"A","pattern":null,"replacement":"甲"}]', 'utf-8')
        destination = 'BepInEx/plugins/FixtureCatalog.json'
        target = self.game / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(source.read_bytes())
        self.manifest['files'] = [{'source': 'catalog.json', 'destination': destination,
                                  'sha256': installer.digest(source), 'kind': 'dynamic_catalog'}]
        self.assertFalse(self.plan()[0])


if __name__ == '__main__':
    unittest.main()
