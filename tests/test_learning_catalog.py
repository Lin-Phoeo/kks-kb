import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('learning', Path(__file__).resolve().parents[1] / 'app/kb/content/learning.py')
learning = importlib.util.module_from_spec(spec)
spec.loader.exec_module(learning)


class CatalogTests(unittest.TestCase):
    def test_recent_research_is_added_once_without_losing_original_navigation(self):
        original = {'chapters': [{'id':'03','title':'材质与贴图','docs':[{'path':'old.md','chars':10}], 'count':1,'chars':10}, {'id':'05','title':'动画与镜头','docs':[], 'count':0,'chars':0}], 'reference':[]}
        before = copy.deepcopy(original)
        rows = [('06-新目标研究/61-AzStandard一手教程与材质规则.md','Az Standard',120), ('06-新目标研究/80-VideoExport时间轴与帧调度预检.md','帧调度',180)]
        result = learning.enrich_manual(original, rows)
        result = learning.enrich_manual(result, rows)
        paths = [x['path'] for g in result['chapters'] for x in g['docs']]
        self.assertEqual(paths.count(rows[0][0]),1)
        self.assertEqual(paths.count(rows[1][0]),1)
        self.assertIn('old.md',paths)
        self.assertEqual(original,before)
        self.assertEqual(result['chapters'][0]['chars'],130)

    def test_does_not_guess_placement_for_unrelated_sources(self):
        original={'chapters':[{'id':'03','title':'材质与贴图','docs':[]}],'reference':[]}
        result=learning.enrich_manual(original,[('other/61-Az.md','other',100)])
        self.assertEqual(result['chapters'][0]['docs'],[])

    def test_featured_guides_only_link_existing_documents(self):
        rows=[('06-新目标研究/80-VideoExport时间轴与帧调度预检.md','帧调度',120)]
        cards=learning.featured_guides(rows)
        self.assertEqual([c['path'] for c in cards],[rows[0][0]])
        self.assertEqual(cards[0]['kind'],'研究整理')


if __name__=='__main__': unittest.main()
