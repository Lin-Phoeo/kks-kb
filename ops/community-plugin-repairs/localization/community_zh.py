"""Preflight or apply three community translation additions through the rc.2 installer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import tempfile
import types

SOURCE = Path(__file__).resolve().parent
INSTALLER_SHA = 'fee73b87e9e6d5c6040db2342bb0c7d702cb03b37cfa270fde942d95013fe344'
MANIFEST_SHA = '1e7e146a4696d2c37780ad131b61d77ece9db0ff816e6b3aab78a37bbb6b6372'
HELPERS = {
    'Codex.StudioConfigLocalization': ['9e936202e6671f85572e4f9667fa21de2ab2831a1d285efa5066b2db3284acb6', '268e235b245506b011a0532ea8addc04a26550978daa09a4cfc2d4c7b1c02ca1'],
    'Codex.StudioImGuiDynamicBridge': ['3f258c6329f5ab33ece17f8e072baa60cf06b780b7e720e48be2b9a545b533ad', '896efe60c0095c4592d219f1d7bd64041c8f9c469bb4c5a5b4ef7e4851a671a2'],
}
FILES = [
    ('catalog-additions.json', 'BepInEx/plugins/Codex.StudioConfigLocalization.json', 'configuration_catalog'),
    ('grey-imgui-additions.json', 'BepInEx/plugins/Codex.StudioImGuiDynamicBridge.json', 'dynamic_catalog'),
    ('Text/Plugins/KKSMeshExporter/0000_CommunityGrey.zh-CN.txt', 'BepInEx/Translation/zh-CN/Text/Plugins/KKSMeshExporter/0000_CommunityGrey.zh-CN.txt', 'translation'),
]


def load_installer(package):
    package = package.resolve()
    raw = (package / 'studio_zh.py').read_bytes()
    if hashlib.sha256(raw).hexdigest() != INSTALLER_SHA or hashlib.sha256((package / 'manifest.json').read_bytes()).hexdigest() != MANIFEST_SHA:
        raise ValueError('需要未经修改的 0.3.0-rc.2 解压包。')
    module = types.ModuleType('community_rc2_installer')
    module.__file__ = str(package / 'studio_zh.py')
    exec(compile(raw, module.__file__, 'exec'), module.__dict__)
    return module, module.read_json(package / 'manifest.json')


def check_conflicts(api, kind, existing, incoming):
    fields = ('pluginGuid', 'kind', 'section', 'settingKey', 'source') if kind == 'configuration_catalog' else ('owner', 'source', 'pattern')
    seen = {}
    for path in ([existing] if existing.is_file() else []) + [incoming]:
        rows = api.read_json(path)
        if not isinstance(rows, list):
            raise ValueError('显示目录必须是数组。')
        for row in rows:
            if not isinstance(row, dict) or any(key not in row for key in fields):
                raise ValueError('显示目录身份字段缺失。')
            key = tuple(row[field] for field in fields)
            if kind == 'configuration_catalog':
                key = key[:2] + (key[2] or '', key[3] or '', key[4].replace('\r\n', '\n').replace('\r', '\n'))
            elif row['source'] is not None:
                key = (row['owner'], row['source'], None)
            value = row.get('value') if kind == 'configuration_catalog' else row.get('replacement')
            if key in seen and value != seen[key]:
                raise ValueError('同身份词条内容冲突；请校对后再安装：' + str(key))
            seen[key] = value


def run(game, package, apply=False, source=SOURCE):
    api, base = load_installer(package)
    game = game.resolve()
    requirements = {key: dict(base['requirements'][key], required=True) for key in ('configuration_manager', 'patched_core')}
    requirements.update({key: {'required': True, 'paths': ['BepInEx/plugins/' + key + '.dll'], 'sha256': hashes} for key, hashes in HELPERS.items()})
    with tempfile.TemporaryDirectory(prefix='community-zh-') as directory:
        delta = Path(directory)
        observed = {}
        manifest = {'schema_version': 1, 'version': 'community-localization-20261128', 'requirements': requirements, 'files': []}
        for relative, destination, kind in FILES:
            incoming, target = api.contained(source, relative), api.contained(game, destination)
            copied = delta / incoming.name
            shutil.copyfile(incoming, copied)
            observed[destination] = api.digest(target) if target.is_file() else None
            allowed = []
            if kind != 'translation':
                before = api.digest(target) if target.is_file() else None
                check_conflicts(api, kind, target, copied)
                if before is not None and api.digest(target) != before:
                    raise ValueError('冲突检查时目标发生变化。')
                allowed = [before] if before else []
            elif target.is_file() and api.digest(target) != api.digest(copied):
                raise ValueError('同名 Grey TXT 内容冲突；请校对后再安装。')
            manifest['files'].append({'source': copied.name, 'destination': destination, 'kind': kind, 'sha256': api.digest(copied), 'replace_sha256': allowed, 'requires': list(requirements)})
        api.write_json(delta / 'manifest.json', manifest)
        plan, skipped, old, new, keys = api.build_plan(game, manifest, delta, offline=False, strict=True)
        if any((api.digest(api.contained(game, dest)) if api.contained(game, dest).is_file() else None) != before for dest, before in observed.items()):
            raise ValueError('预检期间目标发生变化。')
        result = {'status': 'doctor_passed', 'planned_files': len(plan), 'destinations': [item['destination'] for item in plan], 'configuration_changes': keys if old != new else [], 'runtime_verified': False}
        if apply and (plan or old != new):
            result['receipt'] = str(api.install(game, plan, old, new, keys, manifest, delta))
            result['status'] = 'installed'
        elif apply:
            result['status'] = 'already_current'
        return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--localization-package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='授权合并这三个文件；原安装器先备份并生成可回滚回执')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.game, args.localization_package, args.apply), ensure_ascii=False, indent=2))
    except (OSError, ValueError, RuntimeError, TypeError) as error:
        print('Stopped: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
