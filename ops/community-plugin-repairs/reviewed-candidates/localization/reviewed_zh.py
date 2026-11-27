"""Preflight or apply reviewed Rope This and KKS BlendshapeCreator translations."""
import argparse
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile

SOURCE = Path(__file__).resolve().parent
PLUGINS = {
    'GIN Rope This': ['6554f04badf59acf97441f3070b6a119c6f31b0eb3e984e5baa39b87a7f94a17'],
    'KKS_BlendshapeCreator': ['c05bffae6400a730d1ae3a1d7a2476ce78bf254afa6cec4f92714c1be0711fb9'],
}
FILES = [
    ('catalog-additions.json', 'BepInEx/plugins/Codex.StudioConfigLocalization.json', 'configuration_catalog'),
    ('imgui-additions.json', 'BepInEx/plugins/Codex.StudioImGuiDynamicBridge.json', 'dynamic_catalog'),
    ('Text/Plugins/GIN Rope This/0000_ReviewedCandidates.zh-CN.txt',
     'BepInEx/Translation/zh-CN/Text/Plugins/GIN Rope This/0000_ReviewedCandidates.zh-CN.txt', 'translation'),
    ('Text/Plugins/KKS_BlendshapeCreator/0000_ReviewedCandidates.zh-CN.txt',
     'BepInEx/Translation/zh-CN/Text/Plugins/KKS_BlendshapeCreator/0000_ReviewedCandidates.zh-CN.txt', 'translation'),
]


def load_shared():
    path = SOURCE.parent.parent / 'localization' / 'community_zh.py'
    spec = importlib.util.spec_from_file_location('reviewed_candidates_transaction', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(game, package, apply=False):
    shared = load_shared()
    shared.HELPERS = dict(shared.HELPERS, **PLUGINS)
    api, _ = shared.load_installer(package)
    with tempfile.TemporaryDirectory(prefix='reviewed-zh-source-') as directory:
        staged = Path(directory)
        shared.FILES = []
        for index, (relative, destination, kind) in enumerate(FILES):
            incoming = api.contained(SOURCE, relative)
            # The two owner scopes share a filename; staging must keep their bytes separate.
            name = f'{index:02d}-{incoming.name}'
            shutil.copyfile(incoming, staged / name)
            shared.FILES.append((name, destination, kind))
        result = shared.run(game, package, apply, source=staged)
    result.update(plugin_binaries_changed=False, plugin_configurations_changed=False,
                  configuration_entries=22, dynamic_entries=74, scope_files=2)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--localization-package', type=Path, required=True)
    parser.add_argument('--apply', action='store_true', help='备份后合并四个译文文件；不安装 DLL 或修改插件配置')
    args = parser.parse_args()
    try:
        print(json.dumps(run(args.game, args.localization_package, args.apply), ensure_ascii=False, indent=2))
    except (OSError, ValueError, RuntimeError, TypeError) as error:
        print('Stopped: ' + str(error).replace('同名 Grey TXT', '同名插件作用域 TXT'), file=sys.stderr)
        raise SystemExit(1)
