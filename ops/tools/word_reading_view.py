"""Preserve original Word paragraph/table/image order for selected reading views."""
import html
from pathlib import Path
import posixpath
import xml.etree.ElementTree as ET
import zipfile

W='{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
R='{http://schemas.openxmlformats.org/officeDocument/2006/relationships}'


def word_html(source):
    with zipfile.ZipFile(source) as z:
        rels=ET.fromstring(z.read('word/_rels/document.xml.rels'))
        targets={r.attrib['Id']:posixpath.normpath(posixpath.join('word',r.attrib['Target'])) for r in rels if r.attrib.get('TargetMode')!='External'}
        body=ET.fromstring(z.read('word/document.xml')).find(W+'body')
        def inline(node):
            tag=node.tag.rsplit('}',1)[-1]
            if tag=='t':return html.escape(node.text or '')
            if tag=='tab':return ' '
            if tag in ('br','cr'):return '<br>'
            if tag in ('blip','imagedata'):
                rid=node.attrib.get(R+'embed') or node.attrib.get(R+'id')
                target=targets.get(rid)
                return '<img src="'+html.escape(target,quote=True)+'" alt="原文插图">' if target else ''
            if tag in ('pPr','rPr','instrText','del'):return ''
            result=''.join(inline(c) for c in node)
            if tag=='r':
                props=node.find(W+'rPr')
                if props is not None and props.find(W+'b') is not None:result='<strong>'+result+'</strong>'
                if props is not None and props.find(W+'i') is not None:result='<em>'+result+'</em>'
            return result
        def block(node):
            tag=node.tag.rsplit('}',1)[-1]
            if tag=='p':
                text=inline(node)
                return '<p>'+text+'</p>' if text.strip() else ''
            if tag=='tbl':
                rows=[]
                for tr in node.findall(W+'tr'):
                    cells=[]
                    for tc in tr.findall(W+'tc'):
                        span=tc.find(W+'tcPr/'+W+'gridSpan')
                        colspan=' colspan="'+str(int(span.get(W+'val','1')))+'"' if span is not None else ''
                        cells.append('<td'+colspan+'>'+''.join(block(c) for c in tc)+'</td>')
                    rows.append('<tr>'+''.join(cells)+'</tr>')
                return '<table><tbody>'+''.join(rows)+'</tbody></table>'
            return ''
        return ''.join(block(node) for node in body)
