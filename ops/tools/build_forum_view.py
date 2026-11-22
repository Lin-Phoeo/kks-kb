"""Prepare a reading view for one archived Discuz thread, without fetching resources."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import parse_qs, urlsplit

from bs4 import BeautifulSoup, Comment, NavigableString

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'app'))
from kb import sanitize
from kb.content.doc_images import rewrite_images
from recover_images_v2 import image_reference, remove_discuz_controls


def _archive_link(href):
    try:
        url = urlsplit(href)
        if url.scheme not in ('http', 'https') or not url.hostname:
            return None
    except ValueError:
        return None
    if url.path.rsplit('/', 1)[-1].lower() == 'forum.php' and 'attachment' in parse_qs(url.query).get('mod', []):
        return None
    return href


def _open_spoilers(block):
    for spoiler in block.select('.spoiler'):
        header = spoiler.select_one(':scope > .spoilerheader')
        body = spoiler.select_one(':scope > .spoilerbody')
        button = header.select_one('input.spoilerbutton[value]') if header else None
        if not button or not body or not button['value'].strip():
            continue
        for label in header.find_all(string=True):
            label.replace_with(re.sub(r'[（(]\s*点击展开\s*/\s*收起\s*[）)]', '', str(label)))
        button.replace_with(button['value'].strip())
        header.name = 'h3'
        # Only unwrap the exact one-cell layout shell. Nested or labelled data tables remain intact.
        for table in body.select(':scope > table'):
            shell = [table]
            for names in (('tbody', 'tr'), ('tr', 'td'), ('td',)):
                children = [n for n in shell[-1].contents if not isinstance(n, NavigableString) or n.strip()]
                if len(children) != 1 or children[0].name not in names:
                    break
                shell.append(children[0])
                if children[0].name == 'td':
                    break
            if [n.name for n in shell] in (['table', 'tr', 'td'], ['table', 'tbody', 'tr', 'td']):
                for node in reversed(shell):
                    node.unwrap()


def _visible_text(value):
    return value.replace('\u200b', '').replace('\ufeff', '').strip()


def _compact_editor_spacing(rendered):
    # An empty editor paragraph between two words is still a line boundary.
    for box in reversed(rendered.find_all(['p', 'div'])):
        if not box.find_parent(['pre', 'code']) and not _visible_text(box.get_text()) and not box.find(['img', 'hr', 'table', 'pre', 'code']):
            box.replace_with(rendered.new_tag('br'))

    # Format tags are transparent to line flow. Block edges already separate lines.
    blocks = {'p', 'div', 'ul', 'ol', 'li', 'table', 'tr', 'td', 'th', 'blockquote',
              'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'details', 'summary', 'hr', 'pre'}

    def flow(parent):
        for node in list(parent.contents):
            if isinstance(node, Comment):
                continue
            if node.name in blocks:
                yield None
            if isinstance(node, NavigableString) or node.name in ('br', 'img', 'pre', 'code'):
                yield node
            else:
                yield from flow(node)
            if node.name in blocks:
                yield None

    pending = []
    seen_content = False
    for node in flow(rendered):
        if node is not None and node.name == 'br':
            pending.append(node)
            continue
        if isinstance(node, NavigableString) and not _visible_text(node):
            if pending and str(node).strip(' \t\r\n'):
                node.replace_with(' ')
            continue
        keep = 2 if seen_content and node is not None else 0
        for br in pending[keep:]:
            br.extract()
        pending.clear()
        seen_content = node is not None
    for br in pending:
        br.extract()


def build_forum_view(source, images, indexed_text):
    raw = Path(source).read_bytes()
    try:
        text = raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        text = raw  # Let the HTML parser honor the archive's declared encoding.
    soup = BeautifulSoup(text, 'html.parser')
    remove_discuz_controls(soup)
    result = BeautifulSoup('', 'html.parser')
    posts = soup.select('.pcb')
    if not posts or not all(post.select_one('.t_f') for post in posts):
        raise ValueError('Unrecognized or incomplete Discuz post layout')
    for number, post in enumerate(posts, 1):
        heading = result.new_tag('h2')
        wrapper = post.find_parent(id=lambda value: value and value.startswith('post_'))
        author = wrapper.select_one('.pls .authi a') if wrapper else None
        floor = wrapper.select_one('.plc .pi a[id^=postnum]') if wrapper else None
        title = floor.get_text(strip=True) if floor else ''
        heading.string = (title or f'帖子 {number}') + (f' · {author.get_text(strip=True)}' if author else '')
        result.append(heading)
        blocks = [node for node in post.select('.t_f, .pattl')
                  if node.find_parent(class_='pcb') is post and not node.find_parent(class_=['t_f', 'pattl'])]
        for block in blocks:
            _open_spoilers(block)
            for control in block.select('script, style, form, input, button, iframe, object, video, audio, source, .tip'):
                if control.parent:
                    control.decompose()
            for img in block.select('img'):
                ref = image_reference(img)
                label = img.get('alt') or img.get('title') or ''
                img.attrs = {'src': ref, 'alt': label}
            # The archive can contain personal links, relative site controls and active CSS.
            for node in block.find_all(True):
                if node.name == 'img':
                    continue
                keep = {k: node[k] for k in ('rowspan', 'colspan') if node.name in ('td', 'th') and k in node.attrs}
                if node.name == 'a':
                    href = _archive_link(node.get('href', ''))
                    if not href:
                        node.unwrap()
                        continue
                    keep['href'] = href
                node.attrs = keep
            section = result.new_tag('div')
            for child in list(block.contents):
                section.append(child.extract())
            result.append(section)
    localized, _ = rewrite_images(str(result), images)
    rendered = BeautifulSoup(localized, 'html.parser')
    for img in rendered.select('img'):
        if img.get('class') != ['recovered-inline']:
            placeholder = rendered.new_tag('span')
            placeholder.string = '［配图缺失' + ('：' + img['alt'] if img.get('alt') else '') + '］'
            img.replace_with(placeholder)
    _compact_editor_spacing(rendered)
    content = sanitize.clean_html(str(rendered))
    return {'kind': 'discuz_post_flow', 'enabled': True, 'html': content,
            'revision': hashlib.sha256(content.encode()).hexdigest(),
            'source_sha256': hashlib.sha256(raw).hexdigest(),
            'indexed_body_sha256': hashlib.sha256(indexed_text.encode()).hexdigest()}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request', required=True, help='JSON with source, images and indexed_text')
    parser.add_argument('--output', required=True, help='Candidate JSON for one doc_views.json entry')
    args = parser.parse_args()
    request = json.loads(Path(args.request).read_bytes())
    view = build_forum_view(request['source'], request['images'], request['indexed_text'])
    Path(args.output).write_text(json.dumps(view, ensure_ascii=False), encoding='utf-8')
