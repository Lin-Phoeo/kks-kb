import json
import tempfile
import unittest
from pathlib import Path
import hashlib
from app.kb.content.sources import source_for_document, source_content_disposition

class SourceLookupTests(unittest.TestCase):
    def test_chinese_original_filename_uses_ascii_fallback_and_rfc5987(self):
        header=source_content_disposition('材质教程 Word.docx',False)
        self.assertTrue(header.startswith('attachment; filename="Word.docx"; filename*=UTF-8\'\''))
        self.assertIn('%E6%9D%90',header)
        self.assertTrue(header.isascii())

    def test_chinese_only_basename_keeps_extension_in_ascii_fallback(self):
        header = source_content_disposition('教程.docx', inline=False)
        self.assertIn('filename="source.docx";', header)
        self.assertIn("filename*=UTF-8''%E6%95%99%E7%A8%8B.docx", header)

    def test_exact_key_and_hash_required(self):
        with tempfile.TemporaryDirectory() as t:
            root=Path(t)/'source-files';root.mkdir();f=root/'originals/a.docx';f.parent.mkdir();f.write_bytes(b'abc')
            (root/'index.json').write_text(json.dumps([{'doc_key':'doc2:a.md','path':'originals/a.docx','role':'original_docx','size_bytes':3,'sha256':hashlib.sha256(b'abc').hexdigest(),'original_filename':'a.docx'}]),encoding='utf8')
            self.assertIsNotNone(source_for_document(t,'doc2','a.md'))
            self.assertIsNone(source_for_document(t,'web','a.md'))
            f.write_bytes(b'abd')  # Same length as the archived payload.
            self.assertIsNone(source_for_document(t,'doc2','a.md'))

    def test_archive_without_sha256_is_not_a_verified_source(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t) / 'source-files'
            root.mkdir()
            (root / 'a.docx').write_bytes(b'abc')
            record = {'doc_key': 'doc2:a.md', 'path': 'a.docx', 'role': 'original_docx', 'size_bytes': 3}
            for missing in ({}, {'sha256': ''}, {'sha256': None}):
                with self.subTest(missing=missing):
                    (root / 'index.json').write_text(json.dumps([{**record, **missing}]), encoding='utf-8')
                    self.assertIsNone(source_for_document(t, 'doc2', 'a.md'))

if __name__=='__main__':unittest.main()
