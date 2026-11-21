import importlib.util
from pathlib import Path
import unittest

p=Path(__file__).resolve().parents[1]/'app/kb/content/doc_metadata.py'
spec=importlib.util.spec_from_file_location('doc_metadata',p)
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)

class MetadataTests(unittest.TestCase):
    def test_unknown_archive_never_becomes_verified_or_kks(self):
        meta=m.resolve_metadata({},'web','a.md')
        self.assertEqual(meta['verification_status'],'unverified')
        self.assertEqual(meta['compatibility']['games'],[])
        self.assertEqual(meta['image_status'],'unknown')
        self.assertNotIn('已实测',meta['verification_label'])

    def test_same_basename_in_other_collection_cannot_inherit_metadata(self):
        data={'documents':{'ref:dir/README.md':{'content_type':'reference','compatibility':{'games':['KKS']}}}}
        meta=m.resolve_metadata(data,'web','dir/README.md')
        self.assertEqual(meta['compatibility']['games'],[])
        self.assertEqual(meta['content_type'],'raw_archive')

    def test_partial_source_has_warning_and_does_not_get_complete_label(self):
        meta=m.resolve_metadata({'documents':{'ref:a.md':{'image_status':'partial_missing','content_type':'reference'}}},'ref','a.md')
        self.assertTrue(meta['image_warning'])
        self.assertEqual(meta['image_status'],'partial_missing')
        self.assertNotEqual(meta['image_label'],'配图完整')

    def test_invalid_verification_and_source_links_fail_closed(self):
        meta=m.resolve_metadata({'documents':{'ref:a.md':{'verification_status':'verified','sources':[{'url':'javascript:bad()'},{'url':'https://example.com/guide','label':'原文'}]}}},'ref','a.md')
        self.assertEqual(meta['verification_status'],'unverified')
        self.assertEqual([s['url'] for s in meta['sources']],['https://example.com/guide'])

if __name__=='__main__':unittest.main()
