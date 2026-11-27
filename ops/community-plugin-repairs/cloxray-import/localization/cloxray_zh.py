"""Preflight or merge the reviewed CloXray 1.1.6 F1 translation delta."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

SOURCE = Path(__file__).resolve().parent
PLUGIN_SHA = '74612e01c4d2278aca4b4d993dc4d944c223496afca63b87f6a23c1c1f68014d'
FILES = [('catalog-additions.json', 'BepInEx/plugins/Codex.StudioConfigLocalization.json', 'configuration_catalog')]


def load_shared():
    path = SOURCE.parent.parent / 'localization' / 'community_zh.py'
    spec = importlib.util.spec_from_file_location('cloxray_community_transaction', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def identity(row):
    return (row['pluginGuid'], row['kind'], row.get('section') or '', row.get('settingKey') or '',
            row['source'].replace('\r\n', '\n').replace('\r', '\n'))


def require_base(api, existing):
    if not existing.is_file():
        raise ValueError('缺少 CloXray 基础译文目录；请先安装已审 rc.2 基础包。')
    rows = {identity(row): row['value'] for row in api.read_json(existing)}
    for expected in api.read_json(SOURCE / 'required-base-identities.json'):
        if rows.get(identity(expected)) != expected['value']:
            raise ValueError('CloXray 14 条复用基础译文不完整或已改变；请先核对 rc.2 基础包，不能据增量声明全部覆盖。')


def run(game, package, apply=False):
    shared = load_shared()
    shared.FILES = list(FILES)
    shared.HELPERS = dict(shared.HELPERS, **{'Clo/LiquidWobbleMPB': [PLUGIN_SHA]})
    check_conflicts = shared.check_conflicts

    def checked_base(api, kind, existing, incoming):
        require_base(api, existing)
        return check_conflicts(api, kind, existing, incoming)

    shared.check_conflicts = checked_base
    result = shared.run(game, package, apply, source=SOURCE)
    result.update(plugin_version='1.1.6', plugin_sha256=PLUGIN_SHA,
                  plugin_binary_changed=False, plugin_configuration_changed=False,
                  reviewed_display_identities=39, reused_base_identities=14, new_identities=25)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--localization-package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='备份后合并这份 F1 增量；不改 DLL、插件设置或快捷键')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.game, args.localization_package, args.apply), ensure_ascii=False, indent=2))
    except (OSError, ValueError, RuntimeError, TypeError, KeyError) as error:
        print('Stopped: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
