"""Localize only exactly matched tutorial images; callers still sanitize HTML."""
import html
from html.parser import HTMLParser
import re
from urllib.parse import unquote


def _ref(value):
    return unquote(html.unescape(value or '')).strip().replace('\\', '/')


def rewrite_images(text, images):
    refs = {}
    for image in images:
        thumb = image.get('thumb', '')
        target = image.get('orig') if image.get('catdir') == 'recover' or thumb.startswith('recover/') else thumb
        target = target or thumb
        reference = _ref(image.get('reference', ''))
        if reference and all(re.fullmatch(r'[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+', value) and '..' not in value
                             for value in (target, thumb)):
            refs[reference] = (target, thumb, image.get('w'), image.get('h'))
    if not refs:
        return text, set()
    class Rewriter(HTMLParser):
        def __init__(self):
            super().__init__(convert_charrefs=False)
            self.parts, self.used = [], set()
        def handle_starttag(self, tag, attrs):
            values = dict(attrs)
            target = None
            if tag == 'img':
                for key in ('data-original', 'data-src', 'data-lazy-src', 'data-lazy', 'src'):
                    target = refs.get(_ref(values.get(key)))
                    if target:
                        break
            if target:
                target, thumb, width, height = target
                alt = html.escape(values.get('alt') or values.get('title') or '', quote=True)
                dimensions = f' width="{width}" height="{height}"' if isinstance(width, int) and isinstance(height, int) and width > 0 and height > 0 else ''
                self.parts.append(f'<img src="/thumb/{thumb}" data-original="/orig/{target}" alt="{alt}" class="recovered-inline" loading="lazy" decoding="async"{dimensions}>')
                self.used.add(thumb)
            else:
                self.parts.append(self.get_starttag_text())
        def handle_startendtag(self, tag, attrs):
            self.handle_starttag(tag, attrs)
        def handle_endtag(self, tag): self.parts.append(f'</{tag}>')
        def handle_data(self, data): self.parts.append(data)
        def handle_entityref(self, name): self.parts.append(f'&{name};')
        def handle_charref(self, name): self.parts.append(f'&#{name};')
        def handle_comment(self, data): self.parts.append('<!--' + data + '-->')
        def handle_decl(self, decl): self.parts.append('<!' + decl + '>')
    parser = Rewriter()
    parser.feed(text)
    parser.close()
    return ''.join(parser.parts), parser.used
