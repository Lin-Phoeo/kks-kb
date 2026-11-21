"""Public route contracts for the cinematic home and shader lab; isolated test data."""
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

from bs4 import BeautifulSoup
from fastapi.testclient import TestClient

ROOT = Path(__file__).resolve().parents[1]


class ShaderLabTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        root = Path(cls.tmp.name)
        shutil.copytree(ROOT / 'app', root / 'app', ignore=shutil.ignore_patterns('__pycache__'))
        (root / 'data').mkdir()
        (root / 'docs').mkdir()
        (root / 'docs/a.md').write_text('# Shader test\n\nRimLight material notes.\n', encoding='utf-8')
        (root / 'config.json').write_text(json.dumps({
            'llm_url': 'http://127.0.0.1:1/v1', 'llm_model': 'test', 'llm_key': 'test',
            'auth': {'secret': 's' * 40, 'admin_email': 'viewer@test.local',
                     'admin_password': 'local-test-password-123'}
        }), encoding='utf-8')
        cls.app_path = str(root / 'app')
        sys.path.insert(0, cls.app_path)
        for name in list(sys.modules):
            if name in {'app', 'kb', 'indexer'} or name.startswith('kb.'):
                del sys.modules[name]
        import indexer
        indexer.build(str(root / 'data/kb.db'), str(root / 'docs'), do_embed=False, collection='kks')
        import app
        cls.mod = app

    @classmethod
    def tearDownClass(cls):
        cls.mod.KBX.con.close()
        cls.mod.STATE.close()
        sys.path.remove(cls.app_path)
        for name in list(sys.modules):
            if name in {'app', 'kb', 'indexer'} or name.startswith('kb.'):
                del sys.modules[name]
        cls.tmp.cleanup()

    def client(self, authenticated=True):
        client = TestClient(self.mod.app, follow_redirects=False)
        client.headers['Origin'] = 'http://testserver'
        if authenticated:
            response = client.post('/login', data={
                'email': 'viewer@test.local', 'password': 'local-test-password-123'
            })
            self.assertEqual(response.status_code, 303)
        return client

    def test_lab_is_an_authenticated_page_with_accessible_controls(self):
        anonymous = self.client(False).get('/shader-lab')
        self.assertEqual((anonymous.status_code, anonymous.headers.get('location')), (303, '/login'))
        response = self.client().get('/shader-lab')
        self.assertEqual(response.status_code, 200)
        self.assertIn('Shader 展厅', response.text)
        self.assertIn('aria-label="材质预设"', response.text)
        self.assertIn('aria-label="动态速度"', response.text)
        self.assertIn('暂停动效', response.text)
        self.assertIn('重置场景', response.text)
        self.assertIn('/static/arcane-stage.js', response.text)

    def test_runtime_modules_are_served_as_javascript(self):
        client = self.client()
        for path in ('/static/arcane-stage.js', '/static/vendor/three/three.module.js',
                     '/static/vendor/three/three.core.js', '/static/vendor/three/Reflector.js'):
            response = client.get(path)
            self.assertEqual(response.status_code, 200, path)
            self.assertIn(response.headers['content-type'].split(';')[0],
                          ('text/javascript', 'application/javascript'), path)

    def test_home_keeps_question_controls_and_links_to_lab(self):
        response = self.client().get('/')
        self.assertEqual(response.status_code, 200)
        self.assertIn('KKS.ARCANE', response.text)
        page = BeautifulSoup(response.content, 'html.parser')
        self.assertIn('learning-home', page.body.get('class', []))
        main = page.find('main')
        self.assertIsNotNone(main.select_one('form[action="/search"] input[name="q"]'))
        for link in ('/manual', '/shader-lab'):
            self.assertIsNotNone(main.find('a', href=link), link)
        assistant = main.select_one('details#ask-entry:not([open])')
        self.assertIsNotNone(assistant)
        for control in ('summary', 'textarea#q', 'button#send', 'input#useweb', 'input#recipe'):
            self.assertIsNotNone(assistant.select_one(control), control)


if __name__ == '__main__':
    unittest.main()
