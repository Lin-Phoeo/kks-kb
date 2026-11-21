"""Error pages must survive database failures without exposing exceptions."""
import unittest
from unittest.mock import patch
from fastapi.testclient import TestClient
if __package__:
    from . import test_oauth_linuxdo as fixture
else:
    import test_oauth_linuxdo as fixture


def setUpModule():
    fixture.setUpModule()


def tearDownModule():
    fixture.tearDownModule()


class ErrorPageTests(unittest.TestCase):
    def test_internal_error_uses_template_without_database(self):
        from kb.web import routes
        client = TestClient(routes.app, raise_server_exceptions=False)
        with patch.object(routes.KBX, 'counts', side_effect=RuntimeError('PRIVATE_EXCEPTION_MARKER')):
            response = client.get('/login')
        self.assertEqual(response.status_code, 500)
        self.assertIn('服务开了个小差', response.text)
        self.assertNotIn('PRIVATE_EXCEPTION_MARKER', response.text)
        self.assertEqual(response.headers['cache-control'], 'no-store')
        self.assertIn('content-security-policy', response.headers)

    def test_static_not_found_does_not_query_database(self):
        from kb.web import routes
        client = TestClient(routes.app, raise_server_exceptions=False)
        with patch.object(routes.KBX, 'counts', side_effect=RuntimeError('DATABASE_DOWN')):
            response = client.get('/static/does-not-exist.css')
        self.assertEqual(response.status_code, 404)
        self.assertIn('这里什么都没找到', response.text)
        self.assertNotIn('DATABASE_DOWN', response.text)
