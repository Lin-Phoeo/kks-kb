"""Preflight or merge the reviewed PerspectiveX 1.4.1 translation additions."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

SOURCE = Path(__file__).resolve().parent
PLUGIN_SHA = 'bfca7b2371b598a6908b3ded8d6cb8c02fc8b6e65b68465b40239798657f9e7c'
REVIEWED_EARLIER_SCOPES = [
    {'destination': 'BepInEx/Translation/zh-CN/Text/Plugins/KK_PerspectiveX/0000_Studio000Perspective-20261127.zh-CN.txt',
     'sha256': '43eb5aa24e06c42167f3da38827695a31656353da4bbbcf9ac822a4764c41e36'},
]
FILES = [
    ('catalog-additions.json', 'BepInEx/plugins/Codex.StudioConfigLocalization.json', 'configuration_catalog'),
    ('imgui-additions.json', 'BepInEx/plugins/Codex.StudioImGuiDynamicBridge.json', 'dynamic_catalog'),
]


def load_shared():
    path = SOURCE.parent / 'localization' / 'community_zh.py'
    spec = importlib.util.spec_from_file_location('perspectivex_community_transaction', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(game, package, apply=False):
    shared = load_shared()
    shared.FILES = list(FILES)
    shared.HELPERS = dict(shared.HELPERS, KKS_PerspectiveX=[PLUGIN_SHA])
    api, manifest = shared.load_installer(package)
    scopes = [row for row in manifest['files'] if '/Text/Plugins/KK_PerspectiveX/' in row['destination']] + REVIEWED_EARLIER_SCOPES
    matched = next((row for row in scopes if api.contained(game, row['destination']).is_file() and
                    api.digest(api.contained(game, row['destination'])) == row['sha256']), None)
    if matched is None:
        raise ValueError('缺少已审的 PerspectiveX 插件作用域词表，或其内容已改变；请先核对基础包。')
    result = shared.run(game, package, apply, source=SOURCE)
    result['plugin_version'] = '1.4.1'
    result['plugin_sha256'] = PLUGIN_SHA
    result['plugin_binary_changed'] = False
    result['plugin_configuration_changed'] = False
    result['scope_identity'] = {key: matched[key] for key in ('destination', 'sha256')}
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--localization-package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='备份后合并两份译文；不安装 DLL 或修改 PerspectiveX 设置')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.game, args.localization_package, args.apply), ensure_ascii=False, indent=2))
    except (OSError, ValueError, RuntimeError, TypeError) as error:
        print('Stopped: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
