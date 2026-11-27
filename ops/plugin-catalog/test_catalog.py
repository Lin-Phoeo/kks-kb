import importlib.util
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('build_catalog', ROOT / 'build_catalog.py')
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class CatalogTests(unittest.TestCase):
    def test_html_and_script_boundary_cannot_be_injected(self):
        attack = '</script><img src=x onerror="globalThis.compromised=1"> & <tag>\u2028'
        records = [{'id':'x','name':attack,'purpose':attack,'extra':{'raw':attack}}]
        document = builder.render(records, attack)
        raw = re.search(r'<script id="catalog-data" type="application/json">(.*?)</script>', document, re.S).group(1)
        self.assertNotIn('<', raw)
        self.assertNotIn(attack, document)
        self.assertEqual(json.loads(raw)['records'], records)
        self.assertIn('&lt;/script&gt;', document)

    def test_unknown_fields_are_preserved(self):
        records = [{'id':'x','name':'例子','unknown':{'audit':['keep', 1]}}]
        raw = re.search(r'type="application/json">(.*?)</script>', builder.render(records), re.S).group(1)
        self.assertEqual(json.loads(raw)['records'][0]['unknown'], records[0]['unknown'])

    def test_template_markers_inside_data_are_literal(self):
        document = builder.render([{'id':'x','name':'@@APP@@','purpose':'@@CORE@@'}], '@@DATA@@')
        raw = re.search(r'type="application/json">(.*?)</script>', document, re.S).group(1)
        self.assertEqual(json.loads(raw)['records'][0]['name'], '@@APP@@')
        self.assertIn('<title>@@DATA@@</title>', document)

    def test_duplicate_or_missing_identity_rejected(self):
        for records in [[{'id':'x','name':'A'},{'id':'x','name':'B'}],[{'name':'A'}],[{'id':'x','name':''}]]:
            with self.subTest(records=records), self.assertRaises(ValueError):
                builder.render(records)

    def test_structured_source_needs_explicit_mapping(self):
        for records in [{'projects':[]},[{'id':'x','name':'A','recommendation':{'rank':1}}],[{'id':'x','name':'A','archive_paths':'some-path'}]]:
            with self.subTest(records=records), self.assertRaises(ValueError):
                builder.render(records)

    def test_stable_data_namespace_and_offline_dependencies(self):
        a = [{'id':'x','name':'A'}]
        first = builder.render(a); second = builder.render(a)
        self.assertEqual(first, second)
        self.assertNotRegex(first, r'<(?:script|link|img)[^>]+(?:src|href)="https?://')
        self.assertIn("connect-src 'none'", first)

    def test_tutorials_preserve_text_and_validate_shape(self):
        guide = {'title': '法线工作流', 'content': '1. 保留输入\n2. <script>alert(1)</script>\n3. 检查结果'}
        record = {'id': 'guide', 'name': '制作工具', 'resource_type': '独立工具', 'tutorials': [guide]}
        document = builder.render([record])
        raw = re.search(r'type="application/json">(.*?)</script>', document, re.S).group(1)
        self.assertEqual(json.loads(raw)['records'][0]['tutorials'], [guide])
        self.assertNotIn('<script>alert(1)</script>', document)
        for tutorials in ['text', [{}], [{'title': 'A', 'content': ''}]]:
            with self.subTest(tutorials=tutorials), self.assertRaises(ValueError):
                builder.render([dict(record, tutorials=tutorials)])


if __name__ == '__main__':
    unittest.main()
