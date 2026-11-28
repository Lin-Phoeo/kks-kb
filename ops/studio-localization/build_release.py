"""Assemble a portable release ZIP from public sources and reviewed build outputs."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import shutil
import re
import zipfile

from studio_zh import PACKAGE, contained, digest, read_json, write_json


def build(artifacts: Path, output: Path, version: str):
    if not re.fullmatch(r'[0-9A-Za-z][0-9A-Za-z._-]{0,63}', version):
        raise ValueError('Invalid release version')
    if output.resolve().is_relative_to(PACKAGE.resolve()):
        raise ValueError('Release output must be outside the source package')
    if output.exists():
        raise ValueError('Release output already exists')
    for path in PACKAGE.rglob('*'):
        relative = path.relative_to(PACKAGE)
        if any(part in {'__pycache__', 'bin', 'obj', '.git'} for part in relative.parts):
            continue
        if path.is_symlink():
            raise ValueError('Source symlink requires explicit review: ' + relative.as_posix())
        if path.is_file() and path.suffix.lower() in {'.dll', '.exe', '.log', '.dmp', '.db', '.ttf', '.otf', '.ttc'}:
            raise ValueError('Runtime/private file in source tree: ' + relative.as_posix())
    spec = read_json(PACKAGE / 'release-components.json')
    output.mkdir(parents=True)
    folder = output / ('KKS-Studio-zh-CN-' + version)
    shutil.copytree(PACKAGE, folder, ignore=shutil.ignore_patterns('__pycache__', '.git', '*.pyc', 'bin', 'obj'))
    files = read_json(PACKAGE / 'payload-index.json')
    components = []
    for item in spec['components']:
        source = contained(artifacts, item['artifact'])
        if not source.is_file() or digest(source) != item['sha256']:
            raise ValueError('Reviewed build output differs: ' + item['artifact'])
        target = contained(folder, item['source'])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        files.append({key: value for key, value in item.items() if key != 'artifact'})
        components.append({'file': item['source'], 'sha256': item['sha256']})
    tool_files = ['StudioLocalization.LocalTools.dll', 'StudioLocalization.LocalTools.deps.json', 'StudioLocalization.LocalTools.runtimeconfig.json']
    for name in tool_files:
        source = artifacts / 'tools' / name
        if not source.is_file():
            raise ValueError('Missing built local tool ' + name)
        (folder / 'tools/runtime').mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, folder / 'tools/runtime' / name)
    manifest = {'schema_version': 1, 'version': version, 'target': 'Koikatsu Sunshine CharaStudio',
                'requirements': spec['requirements'], 'files': files,
                'local_reuse_status': 'not_imported', 'runtime_validation': 'See VALIDATION.md'}
    write_json(folder / 'manifest.json', manifest)
    write_json(folder / 'build-manifest.json', {'version': version, 'files': len(files), 'components': components,
                                             'local_tool_sha256': digest(folder / 'tools/runtime/StudioLocalization.LocalTools.dll')})
    archive = output / (folder.name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        for path in sorted(folder.rglob('*')):
            if path.is_file():
                bundle.write(path, path.relative_to(folder).as_posix())
    archive.with_suffix('.zip.sha256.txt').write_text(digest(archive) + '  ' + archive.name + '\n', 'ascii')
    return {'directory': str(folder), 'zip': str(archive), 'sha256': digest(archive), 'files': len(files)}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--artifacts', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--version', default='0.3.0-rc.3-cm19.1')
    args = parser.parse_args()
    print(json.dumps(build(args.artifacts.resolve(), args.output.resolve(), args.version), ensure_ascii=False, indent=2))
