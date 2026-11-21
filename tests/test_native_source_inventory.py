import importlib.util
from pathlib import Path
import tempfile
import unittest
import hashlib

s=importlib.util.spec_from_file_location('source_inventory',Path(__file__).resolve().parents[1]/'ops/tools/native_source_inventory.py')
m=importlib.util.module_from_spec(s);s.loader.exec_module(m)

class SourceInventoryTests(unittest.TestCase):
    def test_inventory_uses_exact_document_key_and_does_not_guess_same_basename(self):
        with tempfile.TemporaryDirectory() as t:
            f=Path(t)/'original.docx';f.write_bytes(b'original bytes')
            rows=[{'col':'doc2','doc_path':'a/README.md'}, {'col':'web','doc_path':'b/README.md'}]
            recovery=[{'col':'doc2','doc_path':'a/README.md','source_file':str(f)}]
            report=m.inventory_records(rows,recovery,Path(t),{})
            self.assertEqual(report['documents'][0]['status'],'source_candidate')
            self.assertEqual(report['documents'][1]['status'],'source_not_located')
            self.assertEqual(report['total_documents'],2)
            self.assertNotIn('coverage_percentage',report)

    def test_original_archive_bytes_and_manifest_are_idempotent_and_preserve_old_records(self):
        with tempfile.TemporaryDirectory() as t:
            f=Path(t)/'原件.docx';f.write_bytes(b'original bytes');out=Path(t)/'source-files'
            report=m.archive_sample(f,out,'doc2','a.md','title')
            dest=out/report['path']
            self.assertEqual(dest.read_bytes(),f.read_bytes())
            self.assertEqual(report['sha256'],hashlib.sha256(b'original bytes').hexdigest())
            self.assertEqual(report['role'],'original_docx')
            self.assertEqual(m.archive_sample(f,out,'doc2','a.md','title')['path'],report['path'])

if __name__=='__main__':unittest.main()
