"""Materialize reviewed optional translations from user-owned or public sources."""
from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import urllib.request
import urllib.parse

from studio_zh import PACKAGE, contained, digest, read_json, write_json


def ensure_codec(game: Path, cache: Path) -> Path:
    tool = PACKAGE / 'tools/runtime/StudioLocalization.LocalTools.dll'
    if not tool.is_file():
        raise ValueError('请使用带本地工具的正式 ZIP，或按 BUILDING.md 构建 LocalTools。')
    return tool


def download_fixed(spec, output: Path):
    parsed = urllib.parse.urlsplit(spec['url'])
    if parsed.scheme != 'https' or parsed.hostname != 'codeload.github.com' or parsed.username or parsed.password:
        raise ValueError('固定来源必须使用清单中的 GitHub HTTPS 归档。')
    if output.exists():
        if digest(output) != spec['sha256']:
            raise ValueError('已有下载文件哈希不匹配。')
        return
    output.parent.mkdir(parents=True, exist_ok=True)
    request = urllib.request.Request(spec['url'], headers={'User-Agent': 'StudioLocalization/0.3'})
    part = output.with_suffix(output.suffix + '.part')
    if part.exists():
        archived = part.with_name(part.name + '.failed-' + str(time.time_ns()))
        part.replace(archived)
    with urllib.request.urlopen(request, timeout=60) as response, part.open('xb') as stream:
        total = 0
        while chunk := response.read(1024 * 1024):
            total += len(chunk)
            if total > spec['maximum_bytes']:
                raise ValueError('下载文件超过预期大小。')
            stream.write(chunk)
    if digest(part) != spec['sha256']:
        raise ValueError('下载文件身份不匹配，未导入。')
    part.replace(output)


def run_codec(codec: Path, mode: str, game: Path, input_file: Path, output_file: Path, sources=()):
    command = ['dotnet', str(codec), mode, str(game), str(input_file), str(output_file), *(str(path) for path in sources)]
    subprocess.run(command, check=True)


def materialize(game: Path, sources: list[Path], output: Path, download: bool, catalogs=()):
    output = output.resolve()
    if output.is_relative_to(PACKAGE.resolve()):
        raise ValueError('本地导入输出必须放在解压包目录之外，避免递归复制自身。')
    if output.exists():
        raise ValueError('输出目录已存在，请指定新的本地输出目录。')
    manifest = read_json(PACKAGE / 'manifest.json')
    requests = read_json(PACKAGE / 'optional-import.json')
    cache = output.parent / '.studio-zh-downloads'
    inputs = [path.resolve() for path in sources]
    for path in inputs:
        if path.is_dir() and output.is_relative_to(path):
            raise ValueError('输出目录必须放在输入词表目录之外，避免下一次扫描混入导出包。')
    if download:
        spec = read_json(PACKAGE / 'sources/optional-community.json')
        archive = cache / 'community-fixed.zip'
        download_fixed(spec, archive)
        inputs.append(archive)
    if not inputs and not catalogs:
        raise ValueError('指定 --translations、--catalog 或 --download-community。')
    codec = ensure_codec(game, cache)
    with tempfile.TemporaryDirectory(prefix='studio-zh-import-') as directory:
        temp = Path(directory)
        found_file = temp / 'found.json'
        if inputs:
            run_codec(codec, 'import', game, PACKAGE / 'optional-import.json', found_file, inputs)
            found = read_json(found_file)
        else:
            found = {}
        request_map = defaultdict(list)
        for row in requests:
            request_map[row['key'], row['value_sha256']].append(row['id'])
        for path in catalogs:
            for row in read_json(path):
                if not isinstance(row, dict) or not isinstance(row.get('source'), str) or not isinstance(row.get('value'), str):
                    raise ValueError('显示目录格式必须包含 source/value。')
                identity = row['source'], hashlib.sha256(row['value'].encode('utf-8')).hexdigest()
                for request_id in request_map.get(identity, []):
                    found[request_id] = row['value']
        by_destination = defaultdict(list)
        config = read_json(PACKAGE / 'translations/configuration-original.json')
        imported_ids = []
        for row in requests:
            if row['id'] not in found:
                continue
            value = found[row['id']]
            if hashlib.sha256(value.encode('utf-8')).hexdigest() != row['value_sha256']:
                raise ValueError('导入值身份不同。')
            imported_ids.append(row['id'])
            for target in row['targets']:
                if target['kind'] == 'configuration':
                    config.append(dict(target['identity'], value=value))
                else:
                    by_destination[target['destination']].append({'key': target['key'], 'value': value})
        import shutil
        shutil.copytree(PACKAGE, output, ignore=shutil.ignore_patterns('__pycache__', '.git', 'tests'))
        files = {row['destination']: row for row in manifest['files']}
        for destination, rows in by_destination.items():
            original = files.get(destination)
            if original:
                target = contained(output, original['source'])
                existing = target.read_text('utf-8')
            else:
                rel = 'translations/imported/' + str(len(files)) + '.txt'
                target = contained(output, rel)
                existing = '#set exe CharaStudio\n' + ('#enable fallback\n' if '/Plugins/' in destination else '')
                original = {'source': rel, 'destination': destination, 'kind': 'translation', 'requires': []}
            request_file = temp / 'encode.json'
            write_json(request_file, rows)
            encoded = temp / 'encoded.txt'
            run_codec(codec, 'encode', game, request_file, encoded)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(existing.rstrip('\r\n') + '\n' + encoded.read_text('utf-8'), 'utf-8')
            original['sha256'] = digest(target)
            files[destination] = original
        config_path = output / 'translations/configuration-original.json'
        write_json(config_path, config)
        for item in files.values():
            if item['source'] == 'translations/configuration-original.json':
                item['sha256'] = digest(config_path)
        rule_input = temp / 'rule-files.json'
        write_json(rule_input, [{'destination': item['destination'], 'path': str(contained(output, item['source']))}
                                for item in files.values() if item.get('kind') == 'translation'])
        rule_output = output / 'translations/dynamic-original.json'
        run_codec(codec, 'rules', game, rule_input, rule_output)
        for item in files.values():
            if item['source'] == 'translations/dynamic-original.json':
                item['sha256'] = digest(rule_output)
        manifest['files'] = list(files.values())
        missing = [row['id'] for row in requests if row['id'] not in found]
        manifest['local_reuse_status'] = {'imported_requests': len(imported_ids), 'missing_requests': len(missing),
                                         'redistribution': 'Contains locally imported values. Check source rights before sharing.'}
        write_json(output / 'manifest.json', manifest)
        write_json(output / 'local-import-report.json', {'requested': len(requests), 'imported': len(imported_ids),
                                                       'missing_ids': missing, 'source_content_uploaded': False})
    return manifest['local_reuse_status']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, required=True)
    parser.add_argument('--translations', type=Path, action='append', default=[])
    parser.add_argument('--catalog', type=Path, action='append', default=[], help='显式提供现有的已审核显示目录 JSON')
    parser.add_argument('--download-community', action='store_true')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(materialize(args.game.resolve(), args.translations, args.output.resolve(), args.download_community, args.catalog), ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
