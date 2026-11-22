"""Recover complete local tutorial illustrations with provenance and original formats.

No network requests. Run against the exported indexed identities, not basenames.
"""
import argparse
import base64
import hashlib
import io
import json
from pathlib import Path
import posixpath
import re
import xml.etree.ElementTree as ET
import zipfile
from urllib.parse import unquote, urlparse

from bs4 import BeautifulSoup
import markdown
from PIL import Image
import pymupdf

FORMATS = {'PNG': '.png', 'JPEG': '.jpg', 'GIF': '.gif', 'WEBP': '.webp'}


def store_image(raw, output, reference, label='', role='source_image', page=None):
    if len(raw) > 64 * 1024 * 1024:
        raise ValueError('image_exceeds_64MiB')
    with Image.open(io.BytesIO(raw)) as im:
        im.load()
        if im.width < 2 or im.height < 2:
            return None
        fmt = im.format
        digest = hashlib.sha256(raw).hexdigest()
        if fmt not in FORMATS:
            converted = io.BytesIO()
            im.convert('RGBA').save(converted, format='PNG')
            raw = converted.getvalue()
            digest = hashlib.sha256(raw).hexdigest()
            fmt = 'PNG'
        name = digest + FORMATS[fmt]
        output = Path(output)
        (output / 'orig').mkdir(parents=True, exist_ok=True)
        (output / 'thumbs').mkdir(parents=True, exist_ok=True)
        op = output / 'orig' / name
        if not op.exists():
            op.write_bytes(raw)
        thumb_name = digest + '.jpg'
        tp = output / 'thumbs' / thumb_name
        if not tp.exists():
            thumb = im.convert('RGB')
            thumb.thumbnail((720, 720))
            thumb.save(tp, format='JPEG', quality=85, optimize=True)
        return {'thumb': 'recover/' + thumb_name, 'orig': 'recover/' + name,
                'content_sha256': digest, 'w': im.width, 'h': im.height,
                'name': label or reference, 'reference': reference,
                'role': role, 'page': page, 'cat': '教程原件恢复', 'catdir': 'recover',
                'kb': round(tp.stat().st_size / 1024, 1), 'okb': round(len(raw) / 1024, 1)}


def image_reference(tag):
    ref = next((tag.get(k) for k in ('data-original', 'data-src', 'data-lazy-src', 'data-lazy', 'src') if tag.get(k)), '')
    if tag.find_parent(class_='pcb') and posixpath.basename(urlparse(ref).path).lower() == 'none.gif':
        ref = tag.get('file') or tag.get('zoomfile') or ref
    srcset = tag.get('data-srcset') or tag.get('srcset')
    if srcset and not str(ref).startswith('data:'):
        candidates = [x.strip().split() for x in srcset.split(',') if x.strip()]
        def rank(x):
            try: return float(x[-1][:-1]) if len(x) > 1 else 0
            except ValueError: return 0
        if candidates:
            ref = max(candidates, key=rank)[0]
    return ref


def remove_discuz_controls(soup):
    # Discuz puts the rating panel and attachment controls inside the post wrapper.
    for decoration in soup.select('.pcb .ratl, .pcb .tattl > dt, .pcb .tattl .tip'):
        if decoration.parent and not decoration.find_parent(class_='t_f'):
            decoration.decompose()


def html_images(text):
    soup = BeautifulSoup(text, 'html.parser')
    result = []
    section = ''
    if soup.select_one('.pcb'):
        remove_discuz_controls(soup)
        tags = soup.select('.pcb :is(h1,h2,h3,img)')
    elif soup.select_one('.t_f'):
        tags = soup.select('.t_f :is(h1,h2,h3,img)')
    else:
        tags = soup.find_all(['h1', 'h2', 'h3', 'img'])
    for tag in tags:
        if tag.name != 'img':
            section = tag.get_text(' ', strip=True)
            continue
        ref = image_reference(tag)
        if ref:
            result.append((str(ref), tag.get('alt') or tag.get('title') or '', section))
    return result


def extract_document(source, output):
    source = Path(source)
    images, unresolved = [], []
    visual_count = 0
    def add(raw, reference, label='', role='source_image', page=None, section='', blank_page=False):
        nonlocal visual_count
        visual_count += 1
        try:
            entry = store_image(raw, output, reference, label, role, page)
            if entry:
                entry['sequence'] = len(images) + 1
                entry['section'] = section
                if blank_page:
                    entry['blank_page'] = True
                images.append(entry)
        except Exception as e:
            unresolved.append({'reference': reference, 'reason': type(e).__name__ + ': ' + str(e)[:120]})
    suffix = source.suffix.lower()
    if suffix == '.pdf':
        with pymupdf.open(source) as doc:
            for num, page in enumerate(doc, 1):
                pix = page.get_pixmap(dpi=144, alpha=False)
                blank = pix.is_unicolor and pix.pixel(0, 0) == (255, 255, 255) and not page.get_text().strip()
                add(pix.tobytes('png'), f'page:{num}', f'原 PDF 第 {num} 页', 'pdf_page', num, blank_page=blank)
    elif suffix == '.docx':
        with zipfile.ZipFile(source) as z:
            rels = ET.fromstring(z.read('word/_rels/document.xml.rels'))
            targets = {r.attrib['Id']: r.attrib['Target'] for r in rels if r.attrib.get('TargetMode') != 'External'}
            document = ET.fromstring(z.read('word/document.xml'))
            for node in document.iter():
                if node.tag.rsplit('}', 1)[-1] not in ('blip', 'imagedata'):
                    continue
                rid = node.attrib.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}embed') or node.attrib.get('{http://schemas.openxmlformats.org/officeDocument/2006/relationships}id')
                if rid in targets:
                    target = posixpath.normpath(posixpath.join('word', targets[rid]))
                    try: add(z.read(target), target, Path(target).name)
                    except KeyError: unresolved.append({'reference': target, 'reason': 'missing_docx_media'})
    elif suffix in ('.md', '.markdown', '.html', '.htm', '.txt'):
        text = source.read_text(encoding='utf-8-sig', errors='replace')
        if suffix not in ('.html', '.htm'):
            text = markdown.markdown(text, extensions=['tables', 'fenced_code'])
        for ref, label, section in html_images(text):
            parsed = urlparse(ref)
            if parsed.scheme in ('http', 'https') or ref.startswith('//'):
                visual_count += 1
                unresolved.append({'reference': ref, 'reason': 'remote_not_fetched'})
                continue
            if ref.startswith('data:image/'):
                try:
                    raw = base64.b64decode(ref.split(',', 1)[1], validate=True)
                    add(raw, ref[:32], label, section=section)
                except Exception as e:
                    visual_count += 1
                    unresolved.append({'reference': ref[:32], 'reason': 'invalid_data_image'})
                continue
            decoded = unquote(parsed.path)
            fp = Path(decoded) if Path(decoded).is_absolute() else source.parent / decoded.replace('\\', '/')
            if not fp.is_file():
                visual_count += 1
                unresolved.append({'reference': ref, 'reason': 'local_file_missing'})
                continue
            raw = fp.read_bytes()
            if fp.suffix.lower() == '.svg':
                try:
                    with pymupdf.open(stream=raw, filetype='svg') as svg:
                        raw = svg[0].get_pixmap(dpi=144, alpha=False).tobytes('png')
                except Exception:
                    visual_count += 1
                    unresolved.append({'reference': ref, 'reason': 'svg_render_failed'})
                    continue
            add(raw, ref, label, section=section)
    else:
        return {'images': [], 'unresolved': [], 'visual_count': 0, 'status': 'unsupported_source_type'}
    status = ('partial' if unresolved else 'restored') if images else ('pending_source' if unresolved else 'source_checked_no_visuals')
    return {'images': images, 'unresolved': unresolved, 'visual_count': visual_count, 'status': status}


def local_source(doc, root):
    if doc['col'] == 'ref':
        return root / '_外部迁入' / doc['doc_path']
    match = re.search(r'^\s*>\s*来源\s*[:：]\s*`([^`]+)`', doc['text'], re.M)
    if match:
        p = Path(match.group(1).replace('\\', '/'))
        return p if p.is_absolute() else root / p
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--inventory', required=True)
    ap.add_argument('--output', required=True)
    ap.add_argument('--game-root', required=True, help='Local root containing the source documents')
    args = ap.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    docs = json.loads(Path(args.inventory).read_text(encoding='utf-8'))['documents']
    results = []
    for doc in docs:
        if doc['col'] not in ('ref', 'doc2', 'kks'):
            continue
        source = local_source(doc, Path(args.game_root))
        base = {'col': doc['col'], 'doc_path': doc['doc_path'], 'title': doc['title']}
        if source is None or not source.is_file():
            results.append(dict(base, status='source_not_located', images=[], unresolved=[]))
            continue
        base['source_file'] = str(source)
        try:
            result = extract_document(source, output)
        except Exception as e:
            result = {'status': 'extraction_failed', 'images': [], 'unresolved': [{'reason': type(e).__name__ + ': ' + str(e)[:160]}]}
        results.append(dict(base, **result))
        if result['images']:
            print(json.dumps({'col': doc['col'], 'title': doc['title'], 'images': len(result['images']), 'status': result['status']}, ensure_ascii=False), flush=True)
        if len(results) % 20 == 0:
            (output / 'recovery.json').write_text(json.dumps(results, ensure_ascii=False), encoding='utf-8')
    (output / 'recovery.json').write_text(json.dumps(results, ensure_ascii=False), encoding='utf-8')
    from collections import Counter
    print(json.dumps({'processed': len(results), 'with_images': sum(bool(x['images']) for x in results), 'occurrences': sum(len(x['images']) for x in results), 'statuses': dict(Counter(x['status'] for x in results))}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
