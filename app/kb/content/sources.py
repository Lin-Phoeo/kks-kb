"""Look up archived originals by exact document identity, never by title."""
import hashlib
import json
from pathlib import Path
import re
from urllib.parse import quote


def source_for_document(data_dir, col, path):
    root=(Path(data_dir)/'source-files').resolve()
    try:
        records=json.loads((root/'index.json').read_text(encoding='utf-8'))
    except (OSError,ValueError):
        return None
    if not isinstance(records,list):return None
    for item in records:
        if not isinstance(item,dict) or item.get('doc_key')!=col+':'+path or item.get('role') not in ('original_docx','original_pdf','original_file'):
            continue
        relative=item.get('path','')
        file=(root/relative).resolve()
        if not relative or root not in file.parents or not file.is_file():continue
        if item.get('size_bytes')!=file.stat().st_size:continue
        expected = item.get('sha256')
        if not expected or hashlib.sha256(file.read_bytes()).hexdigest() != expected:continue
        return {**item,'href':'/source/file?path='+quote(relative,safe=''),'filename':item.get('original_filename') or file.name}
    return None


def source_content_disposition(filename, inline=True):
    name=Path(filename).name or 'source.bin'
    stem, suffix = Path(name).stem, Path(name).suffix
    fallback = (re.sub(r'[^A-Za-z0-9._-]+', '_', stem).strip('._') or 'source') + re.sub(r'[^A-Za-z0-9.]', '_', suffix)
    mode='inline' if inline else 'attachment'
    return f'{mode}; filename="{fallback}"; filename*=UTF-8\'\'{quote(name,safe="")}'
