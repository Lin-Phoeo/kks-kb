"""Build a self-contained, offline plugin choice catalog from a JSON record list."""
import argparse
import hashlib
import html
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
TEXT_FIELDS = ('category', 'resource_type', 'purpose', 'compatibility', 'install_status', 'version', 'author', 'difficulty', 'recommendation')
LIST_FIELDS = ('dependencies', 'conflicts', 'archive_paths')


def validate(records):
    if not isinstance(records, list):
        raise ValueError('输入必须为插件对象数组；请先明确映射源数据，不能直接传整份来源报告。')
    ids = set()
    for index, row in enumerate(records):
        if not isinstance(row, dict):
            raise ValueError(f'第 {index + 1} 项必须为对象。')
        for field in ('id', 'name'):
            if not isinstance(row.get(field), str) or not row[field].strip():
                raise ValueError(f'第 {index + 1} 项需要非空字符串 {field}。')
        if row['id'] in ids:
            raise ValueError(f'重复插件 ID：{row["id"]}')
        ids.add(row['id'])
        for field in TEXT_FIELDS:
            if row.get(field) is not None and not isinstance(row[field], str):
                raise ValueError(f'{row["id"]}: {field} 需要字符串或 null。')
        for field in LIST_FIELDS:
            if row.get(field) is not None and (not isinstance(row[field], list) or any(not isinstance(x, str) for x in row[field])):
                raise ValueError(f'{row["id"]}: {field} 需要字符串数组或 null。')
        if row.get('tutorials') is not None:
            if not isinstance(row['tutorials'], list):
                raise ValueError(f'{row["id"]}: tutorials 需要数组。')
            for guide in row['tutorials']:
                if not isinstance(guide, dict) or any(not isinstance(guide.get(key), str) or not guide[key].strip() for key in ('title', 'content')):
                    raise ValueError(f'{row["id"]}: 每篇教程需要非空 title 和 content。')
        if row.get('sources') is not None:
            if not isinstance(row['sources'], list):
                raise ValueError(f'{row["id"]}: sources 需要数组。')
            for source in row['sources']:
                if isinstance(source, str):
                    continue
                if not isinstance(source, dict) or not isinstance(source.get('url'), str) or not isinstance(source.get('label', ''), str):
                    raise ValueError(f'{row["id"]}: source 应为字符串或 {{label, url}}。')
    return records


def script_json(value):
    return json.dumps(value, ensure_ascii=False, allow_nan=False).replace('&', '\\u0026').replace('<', '\\u003c').replace('>', '\\u003e').replace('\u2028', '\\u2028').replace('\u2029', '\\u2029')


def render(records, title='工作室插件选择目录'):
    validate(records)
    canonical = json.dumps(records, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False).encode('utf-8')
    payload = {'schema_version': 1, 'title': title, 'catalog_sha256': hashlib.sha256(canonical).hexdigest(), 'records': records}
    parts = {'TITLE': html.escape(title), 'STYLE': (HERE / 'catalog.css').read_text('utf-8'),
             'DATA': script_json(payload), 'CORE': (HERE / 'catalog-core.js').read_text('utf-8'), 'APP': (HERE / 'catalog-app.js').read_text('utf-8')}
    document = (HERE / 'template.html').read_text('utf-8')
    return re.sub(r'@@(TITLE|STYLE|DATA|CORE|APP)@@', lambda match: parts[match[1]], document)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--title', default='工作室插件选择目录')
    args = parser.parse_args()
    if args.input.resolve() == args.output.resolve():
        parser.error('输出不能覆盖输入数据。')
    records = json.loads(args.input.read_text('utf-8-sig'), parse_constant=lambda value: (_ for _ in ()).throw(ValueError('非有限JSON数字：' + value)))
    document = render(records, args.title)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open('x', encoding='utf-8', newline='\n') as stream:
        stream.write(document)
    print(json.dumps({'output': str(args.output.resolve()), 'records': len(records), 'offline': True}, ensure_ascii=False))


if __name__ == '__main__':
    main()
