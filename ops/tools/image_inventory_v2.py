"""Read-only export of indexed document identities and existing image associations."""
import json
import sqlite3
from pathlib import Path

DATA = Path('/opt/kb/data')
OUT = Path('/tmp/kb-image-inventory-20261120.json')
conn = sqlite3.connect('file:/opt/kb/data/kb.db?mode=ro', uri=True)
image_map = json.loads((DATA / 'img/ocr_map.json').read_text(encoding='utf-8'))
manifest = json.loads((DATA / 'img/manifest.json').read_text(encoding='utf-8'))
records = []
for path, title, col in conn.execute('SELECT path,title,col FROM doc ORDER BY col,path'):
    source = DATA / 'col' / ('paramidx' if col == 'pidx' else col) / path
    if source.is_file():
        text = source.read_text(encoding='utf-8', errors='replace')
    else:
        text = '\n'.join(r[0] for r in conn.execute('SELECT body FROM meta WHERE path=? ORDER BY ord', (path,)))
    records.append({'col': col, 'doc_path': path, 'title': title,
                    'source_on_disk': source.is_file(), 'text': text,
                    'current_images': image_map.get('by_doc', {}).get(path, [])})
OUT.write_text(json.dumps({'documents': records, 'manifest': manifest,
                           'image_map': image_map}, ensure_ascii=False), encoding='utf-8')
print(json.dumps({'documents': len(records), 'manifest_images': len(manifest),
                  'mapped_docs': sum(bool(r['current_images']) for r in records),
                  'export_bytes': OUT.stat().st_size}, ensure_ascii=False))
