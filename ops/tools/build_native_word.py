"""Offline, bounded Word conversion. This module is never run in a web request."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from bs4 import BeautifulSoup

sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'app'))
from kb.content.reading_view import prepare_reading_html

W='{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
STYLE_MAP='\n'.join([f'p[style-name=\'heading {i}\'] => h{min(i+1,6)}:fresh' for i in range(1,7)] + [f'p.Heading{i} => h{min(i+1,6)}:fresh' for i in range(1,7)])


def table_children(parent, tag):
    # Mammoth unwraps content controls and tracked insertions before merging cells.
    pending = list(reversed(parent))
    while pending:
        node = pending.pop()
        if node.tag == W + tag:
            yield node
        elif node.tag in (W + 'sdt', W + 'sdtContent', W + 'ins', W + 'moveTo',
                          W + 'smartTag', W + 'customXml'):
            pending.extend(reversed(node))
        elif node.tag == '{http://schemas.openxmlformats.org/markup-compatibility/2006}AlternateContent':
            fallback = node.find('{http://schemas.openxmlformats.org/markup-compatibility/2006}Fallback')
            if fallback is not None:
                pending.extend(reversed(fallback))


def validate_word_table_grid(document):
    for table_no, table in enumerate(document.iter(W + 'tbl'), start=1):
        previous = {}
        for row_no, row in enumerate(table_children(table, 'tr'), start=1):
            if row.find(f'{W}trPr/{W}del') is not None:
                continue
            location = f'Word table {table_no}, row {row_no}'
            before = row.find(f'{W}trPr/{W}gridBefore')
            if before is not None and int(before.get(W + 'val', '0')) != 0:
                raise ValueError(f'{location}, column 1: unsupported gridBefore; keep the original reading view')
            current = {}
            column = 1
            for cell in table_children(row, 'tc'):
                width = cell.find(f'{W}tcPr/{W}gridSpan')
                span = int(width.get(W + 'val', '1')) if width is not None else 1
                if span < 1:
                    raise ValueError(f'{location}, column {column}: invalid gridSpan')
                merge = cell.find(f'{W}tcPr/{W}vMerge')
                if merge is not None:
                    kind = merge.get(W + 'val', 'continue')
                    if kind == 'continue' and previous.get(column) != span:
                        raise ValueError(f'{location}, column {column}: vMerge must continue the same grid span')
                    if kind not in ('restart', 'continue'):
                        raise ValueError(f'{location}, column {column}: invalid vMerge')
                    current[column] = span
                column += span
            previous = current


def build_word_view(source, images, col, path, indexed_text=None):
    import mammoth
    if importlib.metadata.version('mammoth')!='1.13.0':
        raise ValueError('This reviewed converter requires mammoth==1.13.0')
    source=Path(source);raw=source.read_bytes();source_sha=hashlib.sha256(raw).hexdigest()
    with zipfile.ZipFile(source) as archive:
        if len(archive.infolist())>10000 or sum(i.file_size for i in archive.infolist())>128*1024*1024:
            raise ValueError('DOCX archive exceeds conversion budget')
        xml=ET.fromstring(archive.read('word/document.xml'))
        validate_word_table_grid(xml)
        text=''.join(t.text or '' for t in xml.findall('.//'+W+'t'))
    occurrences=[]
    def convert_image(image):
        with image.open() as stream: payload=stream.read()
        digest=hashlib.sha256(payload).hexdigest()
        # Match this occurrence, not only a deduplicated filename or title.
        n=len(occurrences)
        if n>=len(images) or images[n]['content_sha256']!=digest:
            raise ValueError('Original media order or bytes do not match the reviewed image map')
        asset=images[n];occurrences.append(digest)
        target=asset.get('reader_thumb') or asset['thumb']
        if not all(re.fullmatch(r'[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+',p) and '..' not in p for p in (target,asset['orig'])):
            raise ValueError('Unsafe media path')
        alt=image.alt_text or '原文插图'
        if len(alt)>150 or alt.startswith(('http:', 'https:')):alt='原文插图'
        return {'src':'/thumb/'+target,'data-original':'/orig/'+asset['orig'], 'alt':alt,
                'width':str(asset['w']),'height':str(asset['h'])}
    with source.open('rb') as file:
        result=mammoth.convert_to_html(file,style_map=STYLE_MAP,include_embedded_style_map=False,
            external_file_access=False,convert_image=mammoth.images.img_element(convert_image))
    normalize=lambda s:re.sub(r'\s+','',s)
    before=BeautifulSoup(result.value,'html.parser')
    if normalize(text)!=normalize(before.get_text('')):
        raise ValueError('Full document text/order changed; keep the original reading view')
    if occurrences!=[i['content_sha256'] for i in images]:
        raise ValueError('Some original image occurrences were lost')
    view=prepare_reading_html(result.value,col,path)
    rendered=BeautifulSoup(view['html'],'html.parser')
    for caption in rendered.select('figcaption.source-caption'):caption.decompose()
    if normalize(rendered.get_text(''))!=normalize(text):
        raise ValueError('Sanitization changed source text')
    if [i.get('data-original') for i in rendered.select('img')]!=['/orig/'+i['orig'] for i in images]:
        raise ValueError('Rendered source media changed')
    if hashlib.sha256(source.read_bytes()).hexdigest()!=source_sha:
        raise ValueError('Source document changed during conversion')
    view.update(kind='word_semantic_flow', enabled=True, source_sha256=source_sha,
        revision=hashlib.sha256(view['html'].encode()).hexdigest(),
        checks={'full_text_order_equal':True,'images_in_source_order':True,'image_occurrences':len(images)},
        warnings=[{'type':m.type,'message':m.message} for m in result.messages], converter='mammoth-1.13.0',
        note='由所存 Word 原件生成阅读版；保留原文图序，未知版本与未实测状态不变。')
    if indexed_text is not None:view['indexed_body_sha256']=hashlib.sha256(indexed_text.encode()).hexdigest()
    return view


def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--request',required=True);ap.add_argument('--output',required=True)
    args=ap.parse_args();request=json.loads(Path(args.request).read_bytes())
    view=build_word_view(request['source'],request['images'],request['col'],request['path'],request.get('indexed_text'))
    output=Path(args.output);output.parent.mkdir(parents=True,exist_ok=True)
    tmp=output.with_suffix(output.suffix+'.pending');tmp.write_text(json.dumps(view,ensure_ascii=False,indent=2),encoding='utf8');tmp.replace(output)
    print(json.dumps({'headings':len(view['toc']),'images':view['image_count'],'source_sha256':view['source_sha256'],'checks':view['checks']},ensure_ascii=False))

if __name__=='__main__':main()
