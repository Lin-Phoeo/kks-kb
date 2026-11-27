"""Preflight or apply the reviewed PerceptiveHeightX display additions."""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

SOURCE = Path(__file__).resolve().parent
PLUGIN_SHA = 'd2016fa189a6ec3108282a47ca184ca4bcef4f6992345298b310d91237b16931'
FILES = [
    ('catalog-additions.json', 'BepInEx/plugins/Codex.StudioConfigLocalization.json', 'configuration_catalog'),
    ('imgui-additions.json', 'BepInEx/plugins/Codex.StudioImGuiDynamicBridge.json', 'dynamic_catalog'),
    ('Text/Plugins/PerceptiveHeightX.KKS/0000_PerceptiveHeight.zh-CN.txt', 'BepInEx/Translation/zh-CN/Text/Plugins/PerceptiveHeightX.KKS/0000_PerceptiveHeight.zh-CN.txt', 'translation'),
]


def load_shared():
    path = SOURCE.parent.parent / 'localization' / 'community_zh.py'
    spec = importlib.util.spec_from_file_location('perceptive_community_transaction', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(game, package, apply=False):
    shared = load_shared()
    shared.FILES = list(FILES)
    shared.HELPERS = dict(shared.HELPERS, **{'PerceptiveHeightX.KKS': [PLUGIN_SHA]})
    result = shared.run(game, package, apply, source=SOURCE)
    result.update(plugin_sha256=PLUGIN_SHA, plugin_binary_changed=False, plugin_configuration_changed=False)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--localization-package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='先备份再合并三份译文；不修改插件 DLL 或设置值')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.game, args.localization_package, args.apply), ensure_ascii=False, indent=2))
    except (OSError, ValueError, RuntimeError, TypeError) as error:
        print('Stopped: ' + str(error).replace('同名 Grey TXT', '同名 PerceptiveHeightX TXT'), file=sys.stderr)
        raise SystemExit(1)
