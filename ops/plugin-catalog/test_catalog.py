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


if __name__ == '__main__':
    unittest.main()
