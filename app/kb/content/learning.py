"""Curated learning entry points; source documents remain unchanged."""
import copy
import re

PREFIX = '06-新目标研究/'
GROUPS = {
    '01': {71, 73},
    '03': {51, 53, 54, 55, 56, 57, 61, 62, 63, 64, 65, 67},
    '04': {69, 70, 77, 78},
    '05': {48, 49, 58, 59, 60, 74, 75, 79, 80},
    '06': {50, 52},
    '07': {66, 68, 72, 76, 81},
}
FEATURED = [
    (62, 'Az v5.6：贴图与通道', '先区分 DetailMask、DrawnMap 与蓝通道遮罩，避免套错旧教程。', '材质'),
    (70, '检查灯光和阴影链路', '核对 LightSettings、ObjectMap、Shader Fixer 与光照层。', '灯光'),
    (74, 'Timeline 镜头与关键帧', '从对象属性、关键帧时间和值，到镜头插值。', '动画'),
    (80, '导出前的时间轴检查', '区分 Capture / Export FPS，核查首帧、物理和帧文件。', '导出'),
    (72, '定位场景与角色缺失资源', '按作者依赖、KKManager 与 UAR 日志逐项定位。', '排错'),
    (75, '机位与场景检查点', '保存候选相机、场景版本与可回退检查点。', '工作流'),
]


def reader_title(title):
    return re.sub(r'（新目标第\s*\d+\s*轮）', '', title or '').strip()


def _number(path):
    if not path.startswith(PREFIX): return None
    match = re.match(r'(\d+)-', path[len(PREFIX):])
    return int(match.group(1)) if match else None


def enrich_manual(data, rows):
    result = copy.deepcopy(data)
    chapters = {g['id']: g for g in result.get('chapters', [])}
    known = {d['path'] for g in result.get('chapters', []) + result.get('reference', []) for d in g.get('docs', [])}
    default = chapters.get('06') or next(iter(chapters.values()), None)
    additions = []
    for path, title, chars in rows:
        if not path.startswith(PREFIX) or path in known: continue
        group_id = next((key for key, nums in GROUPS.items() if _number(path) in nums), None)
        group = chapters.get(group_id) or default
        if group is None: continue
        entry = {'path':path,'title':reader_title(title),'chars':chars,'col':'kks','recent':True,'kind':'研究整理'}
        additions.append((group,entry)); known.add(path)
    for group, entry in reversed(additions): group.setdefault('docs',[]).insert(0,entry)
    for group in chapters.values():
        group['count'] = len(group.get('docs',[]))
        group['chars'] = sum(x.get('chars',0) for x in group.get('docs',[]))
    result['manual_total'] = sum(g['count'] for g in chapters.values())
    result['total'] = len(known)
    return result


def featured_guides(rows):
    by_number = {_number(path): (path,title,chars) for path,title,chars in rows if _number(path) is not None}
    cards=[]
    for num,title,description,topic in FEATURED:
        if num not in by_number: continue
        path,_,chars = by_number[num]
        cards.append({'path':path,'title':title,'description':description,'topic':topic,'chars':chars,'kind':'研究整理'})
    return cards
