"""Non-destructive sidecar metadata, keyed by collection and exact indexed path."""
import hashlib
import json
from pathlib import Path
from urllib.parse import urlsplit

TYPES={'tutorial':'学习教程','how_to':'操作指南','reference':'参数与参考','explanation':'原理解释','research_note':'研究整理','troubleshooting':'排错指南','raw_archive':'原始资料'}
SOURCES={'official_doc':'官方文档','author_experience':'作者经验','source_inference':'源码推断','site_curation':'本站整理','unknown':'来源类型待核查'}
IMAGES={'complete':'已对照原件恢复','partial_missing':'部分图片待恢复','pdf_original_only':'PDF 原页展示','text_only':'已检查原件无图','unknown':'配图完整性待核查'}
_CACHE={'mtime':None,'data':{}}
_VIEW_CACHE={'mtime':None,'data':{}}


def resolve_metadata(data,col,path):
    raw=(data.get('documents') or {}).get(col+':'+path,{})
    kind=raw.get('content_type','raw_archive')
    if kind not in TYPES:kind='raw_archive'
    compatibility=raw.get('compatibility') or {}
    games=[g for g in compatibility.get('games',[]) if g in ('KK','KKS')]
    status=raw.get('image_status','unknown')
    if status not in IMAGES:status='unknown'
    verification=raw.get('verification') or {}
    checked=raw.get('verification_status')=='verified' and all(verification.get(k) for k in ('tester','date','environment'))
    source_type=raw.get('source_type','unknown')
    if source_type not in SOURCES:source_type='unknown'
    sources=[]
    for source in raw.get('sources',[]):
        url=source.get('url','')
        try:p=urlsplit(url)
        except ValueError:continue
        if p.scheme in ('https','http') and p.hostname:
            sources.append({'url':url,'label':source.get('label') or p.hostname})
    return {'title':raw.get('title',''), 'doc_id':raw.get('doc_id') or 'doc-'+hashlib.sha256((col+'\0'+path).encode()).hexdigest()[:20],
            'content_type':kind,'type_label':TYPES[kind],
            'curation_level':raw.get('curation_level','raw'),
            'compatibility':{'games':games,'note':compatibility.get('note',''),'requirements':compatibility.get('requirements',[])},
            'game_label':' / '.join(games) if games else '适用游戏未核定',
            'source_type':source_type,'source_label':SOURCES[source_type],
            'verification_status':'verified' if checked else 'unverified',
            'verification_label':'有环境记录的实测' if checked else '本站未做游戏实测',
            'verification':verification if checked else {},
            'image_status':status,'image_label':IMAGES[status],
            'image_warning':status in ('partial_missing','pdf_original_only'),
            'image_note':raw.get('image_note',''),
            'scope_note':raw.get('scope_note',''), 'sources':sources,
            'reviewed_at':raw.get('reviewed_at',''), 'series':raw.get('series') or {}}


def load_doc_metadata(data_dir,col,path):
    file=Path(data_dir)/'doc_metadata.json'
    stamp=file.stat().st_mtime_ns if file.is_file() else None
    if stamp != _CACHE['mtime']:
        try:data=json.loads(file.read_text(encoding='utf-8')) if stamp is not None else {}
        except (OSError,ValueError):data={}
        _CACHE.update(mtime=stamp,data=data)
    return resolve_metadata(_CACHE['data'],col,path)


def load_doc_view(data_dir,col,path):
    file=Path(data_dir)/'doc_views.json'
    stamp=file.stat().st_mtime_ns if file.is_file() else None
    if stamp != _VIEW_CACHE['mtime']:
        try:data=json.loads(file.read_text(encoding='utf-8')) if stamp is not None else {}
        except (OSError,ValueError):data={}
        _VIEW_CACHE.update(mtime=stamp,data=data)
    view=(_VIEW_CACHE['data'].get('documents') or {}).get(col+':'+path,{})
    if not isinstance(view,dict) or not isinstance(view.get('html'),str):return {}
    if view.get('kind')=='word_original_flow':return view
    if view.get('kind') == 'discuz_post_flow' and view.get('enabled') is True:
        if hashlib.sha256(view['html'].encode()).hexdigest() == view.get('revision'):
            return view
    if view.get('kind')=='word_semantic_flow' and view.get('enabled') is True:
        digest=hashlib.sha256(view['html'].encode()).hexdigest()
        if digest==view.get('revision') and view.get('checks',{}).get('full_text_order_equal') and view.get('checks',{}).get('images_in_source_order'):
            return view
    return {}
