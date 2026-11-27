"""Remove machine-specific RSDS PDB paths from unsigned, IL-only release copies."""
from __future__ import annotations

import argparse
import hashlib
import json
import ntpath
from pathlib import Path
import re
import struct


OWNED_DLLS = frozenset({
    'Codex.StudioAniMorphLayoutCompat.dll', 'Codex.StudioConfigLocalization.dll',
    'Codex.StudioCoordinateNative.dll', 'Codex.StudioFontController.dll',
    'Codex.StudioImGuiDynamicBridge.dll', 'Codex.StudioImGuiRouteAdapter.dll',
    'Codex.StudioNativePluginText.dll', 'Codex.StudioPanelTypography.dll',
    'StudioLocalization.LocalTools.dll',
})


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _range(data: bytes, offset: int, size: int) -> bytes:
    if offset < 0 or size < 0 or offset + size > len(data):
        raise ValueError('PE range outside file')
    return data[offset:offset + size]


def _unpack(fmt: str, data: bytes, offset: int):
    return struct.unpack(fmt, _range(data, offset, struct.calcsize(fmt)))


def _inspect(data: bytes, allow_managed_executable: bool = False):
    if _range(data, 0, 2) != b'MZ':
        raise ValueError('Missing MZ header')
    pe, = _unpack('<I', data, 0x3c)
    if _range(data, pe, 4) != b'PE\0\0':
        raise ValueError('Missing PE header')
    machine, section_count = _unpack('<HH', data, pe + 4)
    optional_size, characteristics = _unpack('<HH', data, pe + 20)
    if machine not in (0x14c, 0x8664) or not characteristics & 2 or (not characteristics & 0x2000 and not allow_managed_executable) or not 1 <= section_count <= 96:
        raise ValueError('Unsupported PE machine or non-DLL image')
    optional = pe + 24
    _range(data, optional, optional_size)
    magic, = _unpack('<H', data, optional)
    if magic not in (0x10b, 0x20b):
        raise ValueError('Unsupported optional header')
    directory = optional + (96 if magic == 0x10b else 112)
    directory_count, = _unpack('<I', data, directory - 4)
    if directory_count < 15 or directory + 15 * 8 > optional + optional_size:
        raise ValueError('Missing managed data directories')
    sections = []
    for index in range(section_count):
        row = optional + optional_size + index * 40
        _range(data, row, 40)
        virtual_size, rva, raw_size, raw = _unpack('<IIII', data, row + 8)
        _range(data, raw, raw_size)
        sections.append((rva, virtual_size, raw_size, raw))

    def locate(rva: int, size: int) -> int:
        matches = [raw + rva - start for start, virtual_size, raw_size, raw in sections
                   if start <= rva and rva + size <= start + raw_size]
        if len(matches) != 1:
            raise ValueError('Unmapped or ambiguous PE RVA')
        _range(data, matches[0], size)
        return matches[0]

    def entry(index: int):
        return _unpack('<II', data, directory + index * 8)

    if entry(4) != (0, 0):
        raise ValueError('Authenticode-signed image refused')
    cli_rva, cli_size = entry(14)
    if cli_rva == 0 or cli_size < 72:
        raise ValueError('Native or missing CLR image refused')
    cli = locate(cli_rva, cli_size)
    cli_header_size, = _unpack('<I', data, cli)
    flags, = _unpack('<I', data, cli + 16)
    if cli_header_size < 72 or not flags & 1 or flags & (8 | 16):
        raise ValueError('Non-IL-only, strong-name-signed or native entry point refused')
    if not characteristics & 0x2000:
        token, = _unpack('<I', data, cli + 20)
        if token & 0xff000000 != 0x06000000 or token & 0x00ffffff == 0:
            raise ValueError('Missing managed executable entry-point token')
    if _unpack('<II', data, cli + 32) != (0, 0):
        raise ValueError('Strong-name signature or reserved signing area refused')
    metadata_rva, metadata_size = _unpack('<II', data, cli + 8)
    if not metadata_rva or metadata_size < 4:
        raise ValueError('Missing CLR metadata')
    metadata = locate(metadata_rva, metadata_size)
    if _range(data, metadata, 4) != b'BSJB':
        raise ValueError('Invalid CLR metadata signature')
    debug_rva, debug_size = entry(6)
    if not debug_rva or not debug_size or debug_size % 28:
        raise ValueError('Missing or malformed debug directory')
    debug = locate(debug_rva, debug_size)
    records = []
    debug_spans = []
    for index in range(debug_size // 28):
        record = debug + index * 28
        kind, size, payload_rva, raw = _unpack('<IIII', data, record + 12)
        if kind == 16:
            if size or payload_rva or raw:
                raise ValueError('Malformed reproducible-build debug marker')
            continue
        if kind not in (2, 19):
            raise ValueError('Unknown or embedded debug record refused')
        if not payload_rva or locate(payload_rva, size) != raw:
            raise ValueError('Debug RVA/raw pointer mismatch')
        payload = _range(data, raw, size)
        debug_spans.append((raw, raw + size))
        if kind == 19:
            if not ((payload.startswith(b'SHA256\0') and len(payload) == 39) or
                    (payload.startswith(b'SHA1\0') and len(payload) == 25)):
                raise ValueError('Unknown PDB checksum debug record')
            continue
        if size < 26 or payload[:4] != b'RSDS':
            raise ValueError('Unknown CodeView record refused')
        path, separator, tail = payload[24:].partition(b'\0')
        if not separator or any(tail):
            raise ValueError('Malformed CodeView string padding')
        try:
            text = path.decode('utf-8', errors='strict')
        except UnicodeDecodeError as error:
            raise ValueError('Invalid UTF-8 CodeView path') from error
        absolute = bool(re.match(r'^[A-Za-z]:[/\\]', text) or text.startswith(('\\\\', '//')))
        basename = ntpath.basename(text)
        if not basename or not basename.lower().endswith('.pdb') or ':' in basename:
            raise ValueError('Invalid PDB basename')
        if not absolute and text != basename:
            raise ValueError('Nonportable relative CodeView path refused')
        records.append({'start': raw + 24, 'end': raw + size, 'absolute': absolute,
                        'basename': basename, 'guid_age_sha256': digest(payload[4:24])})
    if not records:
        raise ValueError('No RSDS record found')
    if len(set(debug_spans)) != len(debug_spans):
        raise ValueError('Duplicate debug payloads refused')
    # The only writable bytes must be standalone debug payload, never executable code,
    # CLR metadata/resources, or another PE data directory.
    for start, end in debug_spans:
        if any(start < other_end and other_start < end for other_start, other_end in debug_spans
               if (other_start, other_end) != (start, end)):
            raise ValueError('Overlapping debug payloads refused')
        if start < optional + optional_size + section_count * 40:
            raise ValueError('Debug payload overlaps PE headers')
        for index in range(min(directory_count, 16)):
            rva, size = entry(index)
            if not rva or not size or index == 4:
                continue
            other = locate(rva, size)
            if start < other + size and other < end:
                raise ValueError('Debug payload overlaps another PE directory')
        for offset in (8, 24):
            rva, size = _unpack('<II', data, cli + offset)
            if rva and size:
                other = locate(rva, size)
                if start < other + size and other < end:
                    raise ValueError('Debug payload overlaps CLR metadata/resources')
    return records


def sanitize_debug_paths(data: bytes, dll_name: str):
    """Return a same-length release copy and a report without original paths."""
    if dll_name not in OWNED_DLLS:
        raise ValueError('Only explicitly owned helper/tool assemblies are permitted')
    allow_executable = dll_name == 'StudioLocalization.LocalTools.dll'
    records = _inspect(data, allow_executable)
    result = bytearray(data)
    edited = []
    for record in records:
        if record['basename'].casefold() != (Path(dll_name).stem + '.pdb').casefold():
            raise ValueError('CodeView identity differs from owned DLL')
        if not record['absolute']:
            continue
        value = record['basename'].encode('utf-8') + b'\0'
        length = record['end'] - record['start']
        if len(value) > length:
            raise ValueError('Portable name does not fit existing debug region')
        result[record['start']:record['end']] = value.ljust(length, b'\0')
        edited.append({key: record[key] for key in ('start', 'end', 'basename', 'guid_age_sha256')})
    output = bytes(result)
    changed = [index for index, (before, after) in enumerate(zip(data, output)) if before != after]
    if len(output) != len(data) or any(not any(row['start'] <= index < row['end'] for row in edited) for index in changed):
        raise AssertionError('Unexpected change outside CodeView path regions')
    rechecked = _inspect(output, allow_executable)
    if any(row['absolute'] for row in rechecked):
        raise AssertionError('Absolute CodeView path remains')
    return output, {'assembly': dll_name, 'before_sha256': digest(data), 'after_sha256': digest(output),
                    'bytes': len(data), 'changed_bytes': len(changed), 'path_regions': edited,
                    'all_other_bytes_identical': True, 'unsigned_il_only': True,
                    'original_paths_recorded': False}


def sanitize_file(source: Path, target: Path):
    if source.resolve() == target.resolve() or target.exists():
        raise ValueError('Output must be a new separate file')
    original = source.read_bytes()
    output, report = sanitize_debug_paths(original, source.name)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as stream:
        stream.write(output)
    if source.read_bytes() != original or target.read_bytes() != output:
        raise IOError('Source preservation or output verification failed')
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('target', type=Path)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    if args.report.exists() or args.report.resolve() in (args.source.resolve(), args.target.resolve()):
        raise ValueError('Report must be a new separate file')
    report = sanitize_file(args.source, args.target)
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(report, indent=2))
