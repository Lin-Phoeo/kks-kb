"""Semantic reading projection; originals and retrieval text remain untouched."""
import hashlib
import re
from bs4 import BeautifulSoup
from .. import sanitize


def prepare_reading_html(text, col, path):
    soup = BeautifulSoup(text, 'html.parser')
    prefix = 'rd-' + hashlib.sha256((col + '\0' + path).encode()).hexdigest()[:10] + '-'
    anchors, counts = {}, {}
    headings = soup.find_all(re.compile(r'^h[1-6]$'))
    lift = any(h.name == 'h1' for h in headings)
    for h in headings:
        if not h.get_text(strip=True):
            # Empty headings can still contain bookmarks or an image used elsewhere.
            h.name = 'p'
        elif lift:
            h.name = 'h' + str(min(6, int(h.name[1]) + 1))
    for node in soup.select('[id]'):
        old = str(node['id'])
        base = old if old.startswith(prefix) and re.fullmatch(r'[a-zA-Z0-9_-]+', old) else prefix + hashlib.sha256(old.encode()).hexdigest()[:12]
        counts[base] = counts.get(base, 0) + 1
        node['id'] = base if counts[base] == 1 else base + '-' + str(counts[base])
        anchors.setdefault(old, node['id'])
    for heading in soup.find_all(re.compile(r'^h[2-6]$')):
        title = heading.get_text().strip()
        legacy = 'h-' + hashlib.md5(title.encode()).hexdigest()[:8]
        if not heading.get('id'):
            base = prefix + legacy
            counts[base] = counts.get(base, 0) + 1
            heading['id'] = base if counts[base] == 1 else base + '-' + str(counts[base])
        anchors.setdefault(legacy, heading['id'])
    for link in soup.select('a[href]'):
        href = link['href']
        if href.startswith('#') and href[1:] in anchors:
            link['href'] = '#' + anchors[href[1:]]
    for node in soup.find_all():
        node.attrs.pop('name', None)
        # Source classes must not impersonate application controls or visual badges.
        if 'class' in node.attrs:
            node['class'] = [c for c in node['class'] if c in ('source-figure', 'source-caption', 'recovered-inline')]
            if not node['class']:
                del node['class']
    images = soup.select('img')
    for i, image in enumerate(images, 1):
        if not image.get('id'):
            image['id'] = prefix + 'img-' + str(i)
        image['loading'] = 'lazy'
        image['decoding'] = 'async'
        paragraph = image.find_parent('p')
        if paragraph and not paragraph.get_text(strip=True) and not paragraph.find(['table', 'ul', 'ol']):
            paragraph.name = 'figure'
            paragraph['class'] = ['source-figure']
        figure = image.find_parent('figure')
        if figure and not figure.find('figcaption'):
            caption = soup.new_tag('figcaption')
            caption['class'] = ['source-caption']
            caption.string = '原文插图 · ' + str(i)
            figure.append(caption)
    safe = sanitize.clean_html(str(soup), reader=True)
    shown = BeautifulSoup(safe, 'html.parser')
    toc = [{'title':h.get_text().strip(), 'level':min(int(h.name[1]), 3), 'anchor':h['id']}
           for h in shown.find_all(re.compile(r'^h[2-6]$')) if h.get('id') and h.get_text(strip=True)]
    valid_ids = {n['id'] for n in shown.select('[id]')}
    return {'html':safe, 'toc':toc, 'anchor_map':{old:new for old,new in anchors.items() if new in valid_ids}, 'image_count':len(shown.select('img'))}
