"""Install the reviewed KKS Studio Chinese package with reversible receipts."""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tempfile
import uuid


PACKAGE = Path(__file__).resolve().parent
CONFIG = 'BepInEx/config/AutoTranslatorConfig.ini'
BACKUPS = '.studio-localization/backups'


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path):
    return json.loads(path.read_text('utf-8-sig'))


def write_json(path: Path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', 'utf-8')
    os.replace(temporary, path)


def contained(root: Path, relative: str) -> Path:
    normalized = relative.replace('\\', '/')
    rel = PurePosixPath(normalized)
    if rel.is_absolute() or '..' in rel.parts or ':' in normalized:
        raise ValueError('Unsafe relative path: ' + relative)
    target = (root / normalized).resolve()
    if not target.is_relative_to(root.resolve()):
        raise ValueError('Path escapes its root: ' + relative)
    return target


def game_stopped():
    if os.name != 'nt':
        return
    result = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], capture_output=True, text=True, check=True)
    names = {row[0].lower() for row in csv.reader(io.StringIO(result.stdout)) if row}
    if names & {'charastudio.exe', 'koikatsusunshine.exe', 'koikatsusunshine_vr.exe'}:
        raise RuntimeError('请先保存并正常退出 Studio 和主游戏。')


def font_preflight(game: Path):
    fonts = Path(os.environ.get('WINDIR', 'C:/Windows')) / 'Fonts'
    yahei = any((fonts / name).is_file() for name in ('msyh.ttc', 'msyh.ttf', 'msyhbd.ttc'))
    translator_fonts = game / 'BepInEx/Translation/fonts'
    sdf_files = [path.name for path in translator_fonts.glob('*arialuni*')] if translator_fonts.is_dir() else []
    return {'system_yahei_file_present': yahei, 'existing_cjk_sdf_files': sdf_files,
            'runtime_face_and_glyph_validation': 'required_in_Studio',
            'guidance': '默认保留原中文字体，统一字体为可选功能，已有配置保留。可选择等线、楷体等本机字体；恢复默认中文会关闭统一覆盖。'}


def config_update(path: Path, offline: bool):
    raw = path.read_bytes()
    encoding = 'utf-8-sig' if raw.startswith(b'\xef\xbb\xbf') else 'utf-8'
    text = raw.decode(encoding)
    section, values = '', {}
    for line in text.splitlines():
        clean = line.strip()
        if clean.startswith('[') and clean.endswith(']'):
            section = clean[1:-1]
        elif '=' in clean and not clean.startswith(('#', ';')):
            key, value = clean.split('=', 1)
            identity = (section, key.strip())
            if identity in values:
                raise ValueError('配置键重复，请先检查 ' + '.'.join(identity))
            values[identity] = value.strip()
    expected = {
        ('General', 'Language'): 'zh-CN',
        ('Files', 'Directory'): 'Translation/{Lang}/Text',
        ('Texture', 'TextureDirectory'): 'Translation/{Lang}/Texture',
        ('ResourceRedirector', 'PreferredStoragePath'): 'Translation/{Lang}/RedirectedResources',
    }
    for identity, value in expected.items():
        if values.get(identity, '').replace('\\', '/').rstrip('/') != value:
            raise ValueError('需要标准简中翻译配置 ' + '.'.join(identity) + '=' + value)
    updates = {('Behaviour', 'EnableTranslationScoping'): 'True'}
    if offline:
        updates.update({('Service', 'Endpoint'): '', ('Service', 'FallbackEndpoint'): ''})
    if any(key not in values for key in updates):
        raise ValueError('缺少必要的 XUnity 配置项。')
    lines, section = [], ''
    for line in text.splitlines(keepends=True):
        clean = line.strip()
        if clean.startswith('[') and clean.endswith(']'):
            section = clean[1:-1]
        if '=' in line and not clean.startswith(('#', ';')):
            left = line.split('=', 1)[0]
            identity = (section, left.strip())
            if identity in updates:
                end = '\r\n' if line.endswith('\r\n') else '\n' if line.endswith('\n') else ''
                line = left + '=' + updates[identity] + end
        lines.append(line)
    return raw, ''.join(lines).encode(encoding), ['.'.join(key) for key in updates]


def resolve_dependency(game: Path, requirement):
    candidates = [contained(game, name) for name in requirement['paths']]
    present = [path for path in candidates if path.is_file()]
    accepted = set(requirement['sha256'])
    if len(present) != 1:
        return False, 'missing_or_ambiguous'
    return digest(present[0]) in accepted, 'matched' if digest(present[0]) in accepted else 'version_mismatch'


def merge_catalog(kind: str, existing: Path, incoming: Path):
    old, new = read_json(existing), read_json(incoming)
    if not isinstance(old, list) or not isinstance(new, list):
        raise ValueError('显示目录必须是数组。')
    fields = ('pluginGuid', 'kind', 'section', 'settingKey', 'source') if kind == 'configuration_catalog' else ('owner', 'source', 'pattern')
    merged = {}
    for row in old + new:
        if not isinstance(row, dict) or any(field not in row for field in fields):
            raise ValueError('显示目录身份字段缺失。')
        identity = tuple(row[field] for field in fields)
        merged[identity] = row
    return (json.dumps(list(merged.values()), ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def owned_current_hash(game: Path, destination: str, current: str):
    root = game / BACKUPS
    if not root.is_dir():
        return False
    for path in root.glob('*/receipt.json'):
        receipt = read_json(path)
        if receipt.get('status') != 'installed' or Path(receipt.get('game', '')).resolve() != game:
            continue
        if any(row.get('destination') == destination and row.get('after') == current
               and row.get('state') == 'installed' for row in receipt.get('files', [])):
            return True
    return False


def build_plan(game: Path, manifest, package: Path, offline: bool, strict: bool):
    game, package = game.resolve(), package.resolve()
    if not (game / 'CharaStudio.exe').is_file():
        raise ValueError('所选目录没有 CharaStudio.exe。')
    prior, updated, config_keys = config_update(game / CONFIG, offline)
    verified_sources = {}
    for item in manifest['files']:
        destination = item['destination'].replace('\\', '/')
        if not destination.startswith(('BepInEx/plugins/', 'BepInEx/Translation/zh-CN/')):
            raise ValueError('安装清单目标超出插件与翻译范围 ' + destination)
        source = contained(package, item['source'])
        if not source.is_file() or digest(source) != item['sha256']:
            raise ValueError('安装包文件校验失败 ' + item['source'])
        verified_sources[item['destination']] = item
    requirements = {}
    for name, requirement in manifest.get('requirements', {}).items():
        requirements[name] = resolve_dependency(game, requirement)
        if not requirements[name][0] and requirement.get('allow_planned_replacement'):
            for relative in requirement['paths']:
                incoming = verified_sources.get(relative)
                target = contained(game, relative)
                if not incoming or incoming['sha256'] not in requirement['sha256'] or not target.is_file():
                    continue
                if digest(target) in set(incoming.get('replace_sha256', [])) | {incoming['sha256']}:
                    requirements[name] = (True, 'verified_planned_replacement')
                    break
        if requirement.get('required') and not requirements[name][0]:
            raise ValueError('依赖不匹配 ' + name + '，请运行 doctor 并按兼容说明准备版本。')
    plan, skipped = [], []
    destinations = set()
    for item in manifest['files']:
        destination = item['destination']
        if destination in destinations:
            raise ValueError('重复目标 ' + destination)
        destinations.add(destination)
        source = contained(package, item['source'])
        if not source.is_file() or digest(source) != item['sha256']:
            raise ValueError('安装包文件校验失败 ' + item['source'])
        unmet = [name for name in item.get('requires', []) if not requirements.get(name, (False,))[0]]
        if unmet:
            skipped.append({'destination': destination, 'reason': 'dependency', 'dependencies': unmet})
            continue
        target = contained(game, destination)
        before = digest(target) if target.is_file() else None
        if target.exists() and not target.is_file():
            raise ValueError('目标不是普通文件 ' + destination)
        allowed = set(item.get('replace_sha256', [])) | {item['sha256']}
        if before is not None and before not in allowed and not owned_current_hash(game, destination, before):
            raise ValueError('目标已有其他内容，保留原件并停止 ' + destination)
        candidate = dict(item, before=before)
        if before is not None and before != item['sha256'] and item.get('kind') in {'configuration_catalog', 'dynamic_catalog'}:
            content = merge_catalog(item['kind'], target, source)
            candidate['prepared_content'] = content
            candidate['sha256'] = hashlib.sha256(content).hexdigest()
        if before != candidate['sha256']:
            plan.append(candidate)
    if strict and skipped:
        raise ValueError('严格模式要求所有可选插件版本匹配，未匹配数 ' + str(len(skipped)))
    # Check only competing names in each exact scope. A global cache never adopts a helper assembly.
    for item in plan:
        if item.get('kind') != 'translation':
            continue
        target = contained(game, item['destination'])
        text_root = game / 'BepInEx/Translation/zh-CN/Text'
        relative = target.relative_to(text_root) if target.is_relative_to(text_root) else None
        if relative is not None and relative.parts[0] != 'Plugins':
            competitors = [path for path in text_root.rglob('*.txt')
                           if path.relative_to(text_root).parts[0].lower() != 'plugins']
        elif relative is not None and len(relative.parts) > 2:
            competitors = (text_root / 'Plugins' / relative.parts[1]).rglob('*.txt')
        else:
            competitors = target.parent.glob('*.txt') if target.parent.exists() else []
        for path in competitors:
            if path == target or path.name.startswith('_') or path.name.lower().endswith('resizer.txt'):
                continue
            if str(path).upper() < str(target).upper() and path.relative_to(game).as_posix() not in destinations:
                raise ValueError('同作用域已有更高优先级词表，需要核对 ' + path.relative_to(game).as_posix())
    return plan, skipped, prior, updated, config_keys


def prepare_native_candidates(game: Path, output: Path, package: Path):
    tool = package / 'tools/runtime/StudioLocalization.LocalTools.dll'
    if not tool.is_file():
        raise ValueError('本包缺少 LocalTools，请使用正式 ZIP 或按构建说明补齐。')
    subprocess.run(['dotnet', str(tool), 'native', str(game), str(output)], check=True)
    rows = read_json(output / 'native-dialog-candidates.json')
    if isinstance(rows, dict):
        rows = [rows]
    plan, skipped = [], []
    for row in rows:
        if row['status'] == 'already_patched':
            continue
        if row['status'] != 'candidate_prepared':
            skipped.append({'destination': row['path'], 'reason': row['status']})
            continue
        source = contained(output, row['candidate'])
        target = contained(game, row['path'])
        if digest(source) != row['after'] or digest(target) != row['before']:
            raise ValueError('原生候选或原件身份发生变化。')
        plan.append({'source': row['candidate'], 'source_root': str(output), 'destination': row['path'],
                     'sha256': row['after'], 'before': row['before'], 'kind': 'native_dialog'})
    return plan, skipped


def atomic_copy(source: Path, target: Path, run_id: str):
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + '.studiozh-' + run_id + '.tmp')
    if temporary.exists():
        raise ValueError('临时目标已存在。')
    shutil.copyfile(source, temporary)
    if digest(temporary) != digest(source):
        raise IOError('复制校验失败。')
    os.replace(temporary, target)


def record_directories(game: Path, parent: Path, receipt):
    game, parent = game.resolve(), parent.resolve()
    missing = []
    while parent != game and not parent.exists():
        missing.append(parent.relative_to(game).as_posix())
        parent = parent.parent
    for relative in reversed(missing):
        if relative not in receipt['created_directories']:
            receipt['created_directories'].append(relative)


def install(game: Path, plan, prior: bytes, updated: bytes, keys, manifest, package: Path):
    game, package = game.resolve(), package.resolve()
    game_stopped()
    lock = game / '.studio-localization/install.lock'
    lock.parent.mkdir(parents=True, exist_ok=True)
    try:
        lock_fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
    except FileExistsError:
        raise RuntimeError('另一次安装持有 install.lock。先确认该安装是否仍运行，并查看其回执。')
    os.close(lock_fd)
    try:
        return install_transaction(game, plan, prior, updated, keys, manifest, package)
    finally:
        lock.unlink()


def install_transaction(game: Path, plan, prior: bytes, updated: bytes, keys, manifest, package: Path):
    run_id = dt.datetime.now(dt.timezone.utc).strftime('%Y%m%dT%H%M%SZ') + '-' + uuid.uuid4().hex[:8]
    run = contained(game, BACKUPS + '/' + run_id)
    run.mkdir(parents=True, exist_ok=False)
    receipt_path = run / 'receipt.json'
    receipt = {'schema_version': 1, 'game': str(game), 'run_id': run_id, 'package_version': manifest['version'],
               'status': 'applying', 'files': [], 'config': None, 'created_directories': []}
    write_json(receipt_path, receipt)
    try:
        for item in plan:
            target = contained(game, item['destination'])
            current = digest(target) if target.is_file() else None
            if current != item['before']:
                raise ValueError('预检后文件发生变化 ' + item['destination'])
            record = {'destination': item['destination'], 'before': current, 'after': item['sha256'], 'state': 'prepared'}
            if current is not None:
                backup = contained(run, 'originals/' + item['destination'])
                backup.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(target, backup)
                if digest(backup) != current:
                    raise IOError('原件备份失败。')
            receipt['files'].append(record)
            record_directories(game, target.parent, receipt)
            write_json(receipt_path, receipt)
            source_root = Path(item.get('source_root', package))
            source = contained(source_root, item['source'])
            if 'prepared_content' in item:
                source = contained(run, 'prepared/' + item['destination'])
                source.parent.mkdir(parents=True, exist_ok=True)
                source.write_bytes(item['prepared_content'])
                if digest(source) != item['sha256']:
                    raise IOError('合并目录哈希不同。')
            atomic_copy(source, target, run_id)
            if digest(target) != item['sha256']:
                raise IOError('安装后哈希不同。')
            record['state'] = 'installed'
            write_json(receipt_path, receipt)
        if prior != updated:
            target = game / CONFIG
            if target.read_bytes() != prior:
                raise ValueError('预检后配置发生变化。')
            backup = run / 'translator-config.original'
            backup.write_bytes(prior)
            candidate = run / 'translator-config.updated'
            candidate.write_bytes(updated)
            receipt['config'] = {'before': digest(backup), 'after': digest(candidate), 'keys': keys, 'state': 'prepared'}
            write_json(receipt_path, receipt)
            atomic_copy(candidate, target, run_id)
            receipt['config']['state'] = 'installed'
        receipt['status'] = 'installed'
        write_json(receipt_path, receipt)
        return receipt_path
    except Exception:
        receipt['status'] = 'installation_failed'
        write_json(receipt_path, receipt)
        try:
            rollback(receipt_path, apply=True, package=package)
        except Exception as recovery:
            receipt['status'] = 'recovery_required'
            receipt['recovery_error'] = type(recovery).__name__
            write_json(receipt_path, receipt)
        raise


def rollback(receipt_path: Path, apply: bool, package: Path = PACKAGE):
    receipt = read_json(receipt_path)
    game, run = Path(receipt['game']).resolve(), receipt_path.parent.resolve()
    if not run.is_relative_to((game / BACKUPS).resolve()):
        raise ValueError('回执不在指定游戏的备份目录。')
    if receipt['status'] == 'rolled_back':
        return {'status': 'already_rolled_back'}
    changes = []
    for record in receipt['files']:
        target = contained(game, record['destination'])
        current = digest(target) if target.is_file() else None
        if record['state'] == 'rolled_back':
            if current != record['before']:
                raise ValueError('已恢复文件又发生变化 ' + record['destination'])
            continue
        quarantine = contained(run, 'removed/' + record['destination'])
        quarantined = quarantine.is_file() and digest(quarantine) == record['after']
        resuming = record['state'] == 'rollback_prepared' and quarantined
        if resuming and current == record['before']:
            record['state'] = 'rolled_back'
            write_json(receipt_path, receipt)
            continue
        if record['state'] == 'prepared' and current == record['before']:
            continue
        if current != record['after'] and not (resuming and current is None):
            raise ValueError('安装后文件已改变，停止回滚 ' + record['destination'])
        if record['before'] is not None:
            backup = contained(run, 'originals/' + record['destination'])
            if not backup.is_file() or digest(backup) != record['before']:
                raise ValueError('备份身份不符 ' + record['destination'])
        changes.append(record)
    config = receipt.get('config')
    config_changed = config is not None and digest(game / CONFIG) == config['after']
    if config is not None and not config_changed and digest(game / CONFIG) != config['before']:
        raise ValueError('配置已改变，需要按回执合并恢复。')
    if config_changed and digest(run / 'translator-config.original') != config['before']:
        raise ValueError('配置备份身份不符。')
    if not apply:
        return {'status': 'rollback_preflight_passed', 'files': len(changes), 'configuration': config_changed}
    game_stopped()
    for record in receipt['files']:
        target = contained(game, record['destination'])
        temporary = target.with_name(target.name + '.studiozh-' + receipt['run_id'] + '.tmp')
        if temporary.is_file():
            saved = contained(run, 'uncommitted/' + record['destination'])
            saved.parent.mkdir(parents=True, exist_ok=True)
            if saved.exists():
                raise ValueError('未完成复制的隔离目标已存在。')
            temporary.replace(saved)
    for record in reversed(changes):
        target = contained(game, record['destination'])
        quarantine = contained(run, 'removed/' + record['destination'])
        quarantine.parent.mkdir(parents=True, exist_ok=True)
        if record['state'] != 'rollback_prepared':
            record['state'] = 'rollback_prepared'
            write_json(receipt_path, receipt)
        if target.exists():
            if quarantine.exists():
                raise ValueError('回滚隔离目标已存在。')
            target.replace(quarantine)
        elif not quarantine.is_file() or digest(quarantine) != record['after']:
            raise ValueError('缺少已隔离的安装文件。')
        if record['before'] is not None:
            atomic_copy(contained(run, 'originals/' + record['destination']), target, receipt['run_id'])
        record['state'] = 'rolled_back'
        write_json(receipt_path, receipt)
    if config_changed:
        atomic_copy(run / 'translator-config.original', game / CONFIG, receipt['run_id'])
    for relative in reversed(receipt.get('created_directories', [])):
        directory = contained(game, relative)
        if directory.is_dir() and not any(directory.iterdir()):
            directory.rmdir()
    receipt['status'] = 'rolled_back'
    write_json(receipt_path, receipt)
    return {'status': 'rolled_back', 'files': len(changes), 'preserved_in': str(run / 'removed')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    for name in ('doctor', 'install'):
        command = sub.add_parser(name)
        command.add_argument('--game', type=Path, required=True)
        command.add_argument('--offline', action='store_true')
        command.add_argument('--strict', action='store_true')
        command.add_argument('--native-ui', action='store_true', help='生成并纳入受检原生对话框候选')
        if name == 'install':
            command.add_argument('--apply', action='store_true')
    command = sub.add_parser('rollback')
    command.add_argument('--receipt', type=Path, required=True)
    command.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if args.command == 'rollback':
        result = rollback(args.receipt.resolve(), args.apply)
    else:
        manifest = read_json(PACKAGE / 'manifest.json')
        game = args.game.resolve()
        with tempfile.TemporaryDirectory(prefix='studio-zh-preflight-') as directory:
            plan, skipped, old, new, keys = build_plan(game, manifest, PACKAGE, args.offline, args.strict)
            if args.native_ui:
                native, native_skipped = prepare_native_candidates(game, Path(directory) / 'native', PACKAGE)
                plan.extend(native)
                skipped.extend(native_skipped)
                if args.strict and native_skipped:
                    raise ValueError('原生对话框插件版本不匹配。')
            result = {'status': 'preflight_passed', 'version': manifest['version'], 'planned_files': len(plan),
                      'skipped': skipped, 'configuration_changes': keys if old != new else [],
                      'local_reuse': manifest.get('local_reuse_status', 'not_imported'), 'fonts': font_preflight(game)}
            if args.command == 'install' and args.apply:
                result['receipt'] = str(install(game, plan, old, new, keys, manifest, PACKAGE))
                result['status'] = 'installed'
                result['runtime_verified'] = False
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print('Stopped: ' + str(error), file=sys.stderr)
        raise SystemExit(1)
