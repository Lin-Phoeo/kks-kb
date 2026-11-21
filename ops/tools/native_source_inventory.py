"""Bounded local source inventory and byte-preserving sample archive; no network."""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import xml.etree.ElementTree as ET
import zipfile

W='{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'


def inventory_records(documents, recovery, root, views):
    known={(r['col'],r['doc_path']):r for r in recovery}
    rows=[];root=Path(root).resolve()
    for d in documents:
        key=d['col']+':'+d['doc_path'];r=known.get((d['col'],d['doc_path']),{})
        source=r.get('source_file')
        if not source:
            match=re.search(r'^\s*>\s*来源\s*[:：]\s*`([^`]+)`',d.get('text',''),re.M)
            source=match.group(1) if match else None
        f=Path(source) if source else None
        if f and not f.is_absolute():f=root/f
        inside=f is not None and (f.resolve()==root or root in f.resolve().parents)
        item={'doc_key':key,'status':'source_not_located','original_format':'unknown'}
        if inside and f.is_file():
            digest=hashlib.sha256(f.read_bytes()).hexdigest();expected=views.get(key,{}).get('source_sha256')
            item.update(status='verified_view_source' if expected==digest else 'source_candidate',original_format=f.suffix.lower(),sha256=digest)
        rows.append(item)
    return {'total_documents':len(rows),'statuses':dict(Counter(x['status'] for x in rows)),
            'located_formats':dict(Counter(x['original_format'] for x in rows if x['status']!='source_not_located')),
            'scope':'Exact source paths from exported text/recovery only. Candidates are not proof of matching source version; no complete disk crawl.',
            'documents':rows}


def archive_sample(source, output, col, path, title):
    source=Path(source);output=Path(output);payload=source.read_bytes();digest=hashlib.sha256(payload).hexdigest()
    name='originals/'+digest+source.suffix.lower();target=output/name;target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists():
        if target.read_bytes()!=payload:raise ValueError('Archive hash collision or modified artifact')
    else:
        shutil.copy2(source,target)
    if hashlib.sha256(target.read_bytes()).hexdigest()!=digest:raise ValueError('Archive copy failed')
    return {'path':name,'title':title,'original_filename':source.name,'size_bytes':len(payload),'sha256':digest,
            'doc_key':col+':'+path,'derived_md':path,'role':{'.docx':'original_docx','.pdf':'original_pdf'}.get(source.suffix.lower(),'original_file'),
            'archived_at':datetime.now(timezone.utc).isoformat(),'source_url':'','author':'','license':'unknown',
            'notes':'Original bytes retained; local availability is not public redistribution permission.'}


def main():
    ap=argparse.ArgumentParser(description=__doc__);ap.add_argument('--inventory',required=True);ap.add_argument('--recovery',required=True);ap.add_argument('--views',required=True);ap.add_argument('--root',required=True);ap.add_argument('--output',required=True)
    a=ap.parse_args();documents=json.loads(Path(a.inventory).read_bytes())['documents'];recovery=json.loads(Path(a.recovery).read_bytes());views=json.loads(Path(a.views).read_bytes())['documents'];out=Path(a.output);out.mkdir(parents=True,exist_ok=True)
    report=inventory_records(documents,recovery,Path(a.root),views)
    (out/'inventory').mkdir(exist_ok=True);(out/'inventory/documents.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
    selected=[];structure=[]
    for r in recovery:
        key=r['col']+':'+r['doc_path']
        if key not in views and not (r['col']=='doc2' and r['doc_path']=='PDF教程/ScenePlayer v2.0 操作手册 (chn).md'):continue
        f=Path(r.get('source_file',''))
        if not f.is_file():continue
        item=archive_sample(f,out/'source-files',r['col'],r['doc_path'],r['title'])
        item['source_version_status']='matches_reviewed_view' if views.get(key,{}).get('source_sha256')==item['sha256'] else 'candidate_unverified'
        selected.append(item)
        if f.suffix.lower()=='.docx':
            with zipfile.ZipFile(f) as z:
                doc=ET.fromstring(z.read('word/document.xml'));styles=ET.fromstring(z.read('word/styles.xml'))
                names={s.get(W+'styleId'):s.find(W+'name').get(W+'val') for s in styles.findall(W+'style') if s.find(W+'name') is not None}
                ps=doc.findall('.//'+W+'p');hs=[p for p in ps if (s:=p.find(W+'pPr/'+W+'pStyle')) is not None and names.get(s.get(W+'val'),'').lower().startswith('heading')]
                structure.append({'doc_key':key,'heading_style_paragraphs':len(hs),'nonempty_heading_paragraphs':sum(bool(''.join(t.text or '' for t in p.findall('.//'+W+'t')).strip()) for p in hs),'tables':len(doc.findall('.//'+W+'tbl')),'numbered_paragraphs':sum(p.find(W+'pPr/'+W+'numPr') is not None for p in ps)})
    index=out/'source-files/index.json';old=json.loads(index.read_bytes()) if index.exists() else []
    by_key={x['doc_key']:x for x in selected}
    merged=[x for x in old if x.get('doc_key') not in by_key]+selected
    index.write_text(json.dumps(merged,ensure_ascii=False,indent=2),encoding='utf8')
    summary={k:v for k,v in report.items() if k!='documents'};summary.update(sample_originals=len(selected),word_structure=structure)
    (out/'inventory/summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8');print(json.dumps(summary,ensure_ascii=False))

if __name__=='__main__':main()
