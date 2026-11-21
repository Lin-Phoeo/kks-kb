"""Object permissions using an isolated application and database only."""
import unittest
from unittest.mock import patch
if __package__:
    from . import test_oauth_linuxdo as fixture
else:
    import test_oauth_linuxdo as fixture
from fastapi.testclient import TestClient


def setUpModule():
    fixture.setUpModule()


def tearDownModule():
    fixture.tearDownModule()


class PermissionsTests(unittest.TestCase):
    def setUp(self):
        from kb.web import routes
        self.mod = routes
        self.db = self.mod.STATE
        for email in ('alice@test.local', 'bob@test.local'):
            self.db.execute('INSERT OR IGNORE INTO users(email,pass) VALUES(?,?)', (email, 'unused'))
        self.db.commit()
        self.a, self.b = [self.db.fetchone('SELECT id FROM users WHERE email=?', (e,))[0]
                          for e in ('alice@test.local', 'bob@test.local')]
        self.client = TestClient(self.mod.app, follow_redirects=False)
        self.client.headers['Origin'] = 'http://testserver'
        self.client.cookies.set('kb_session', self.mod.make_token('bob@test.local'))

    def test_other_user_and_legacy_conversation_are_invisible(self):
        cid = self.mod.KBX.new_conv('private', actor=self.a)
        self.mod.KBX.add_msg(cid, 'user', 'secret', actor=self.a)
        legacy = self.db.execute("INSERT INTO conv(title) VALUES('legacy')").lastrowid
        self.db.commit()
        for target in (cid, legacy):
            self.assertNotIn(target, [r[0] for r in self.mod.KBX.convs(actor=self.b)])
            for suffix in ('', '/export'):
                self.assertEqual(self.client.get(f'/c/{target}{suffix}').status_code, 404)
            for suffix, data in (('/rename', {'title': 'stolen'}), ('/delete', {})):
                self.assertEqual(self.client.post(f'/c/{target}{suffix}', data=data).status_code, 404)
            with patch.object(self.mod.KBX, 'hybrid_async', side_effect=AssertionError('retrieval before authorization')):
                self.assertEqual(self.client.post('/ask_stream', data={'q': 'x', 'cid': target}).status_code, 404)
        self.assertEqual(self.mod.KBX.conv_title(cid, actor=self.a), 'private')

    def test_feedback_ownership_idempotency_and_withdrawal(self):
        cid = self.mod.KBX.new_conv('private', actor=self.a)
        mid = self.mod.KBX.add_msg(cid, 'assistant', 'answer', {'chunks': [{'path': 'doc.md'}]}, actor=self.a)
        payload = {'msg_id': mid, 'path': 'doc.md', 'vote': 1}
        self.assertEqual(self.client.post('/api/feedback', json=payload).status_code, 404)
        self.client.cookies.set('kb_session', self.mod.make_token('alice@test.local'))
        for vote, expected in [(1, (1, 0)), (1, (1, 0)), (-1, (0, 1)), (0, (0, 0)), (0, (0, 0))]:
            payload['vote'] = vote
            self.assertEqual(self.client.post('/api/feedback', json=payload).status_code, 200)
            row = self.db.fetchone('SELECT up,down FROM doc_votes WHERE path=?', ('doc.md',))
            self.assertEqual(row, expected)
        self.assertEqual(self.client.post('/api/feedback/reset').status_code, 403)

    def test_owner_can_create_continue_export_rename_and_delete(self):
        from unittest.mock import AsyncMock
        with patch.object(self.mod.KBX, 'hybrid_async', new=AsyncMock(return_value=([], {}))), patch.object(self.mod, 'llm_chain', return_value=[]):
            response = self.client.post('/ask_stream', data={'q': 'owner question'})
            self.assertEqual(response.status_code, 200)
            import json
            cid = json.loads(response.text.split('data: ', 1)[1].split('\n', 1)[0])['conv_id']
            self.assertEqual(self.client.post('/ask_stream', data={'q': 'follow up', 'cid': cid}).status_code, 200)
        self.assertEqual(len(self.mod.KBX.msgs(cid, actor=self.b)), 4)
        self.assertEqual(self.client.get(f'/c/{cid}').status_code, 200)
        self.assertIn('owner question', self.client.get(f'/c/{cid}/export').text)
        self.assertEqual(self.client.post(f'/c/{cid}/rename', data={'title': 'renamed'}).status_code, 303)
        self.assertEqual(self.mod.KBX.conv_title(cid, actor=self.b), 'renamed')
        self.assertEqual(self.client.post(f'/c/{cid}/delete').status_code, 303)
        self.assertIsNone(self.mod.KBX.conv_title(cid, actor=self.b))

    def test_store_methods_fail_closed_without_mutations(self):
        cid = self.mod.KBX.new_conv('private', actor=self.a)
        mid = self.mod.KBX.add_msg(cid, 'user', 'secret', actor=self.a)
        before = self.db.fetchall('SELECT * FROM msg')
        for operation in (lambda: self.mod.KBX.msgs(cid, actor=self.b),
                          lambda: self.mod.KBX.add_msg(cid, 'user', 'attack', actor=self.b),
                          lambda: self.mod.KBX.rename_conv(cid, 'attack', actor=self.b),
                          lambda: self.mod.KBX.del_conv(cid, actor=self.b),
                          lambda: self.mod.KBX.new_conv('attack', actor=None)):
            with self.assertRaises(PermissionError):
                operation()
        self.assertEqual(self.db.fetchall('SELECT * FROM msg'), before)
        self.assertEqual(self.mod.KBX.convs(), [])
        self.assertIsNone(self.mod.KBX.conv_title(cid, actor=self.b))

    def test_feedback_paths_isolation_and_transaction_rollback(self):
        cid = self.mod.KBX.new_conv('private', actor=self.b)
        mid = self.mod.KBX.add_msg(cid, 'assistant', 'answer', {'chunks': [{'path': 'one.md'}, {'path': 'two.md'}]}, actor=self.b)
        for path in ('', 'one.md', 'two.md'):
            self.assertTrue(self.client.post('/api/feedback', json={'msg_id': mid, 'vote': 1, 'path': path}).json()['ok'])
        self.assertEqual(self.db.fetchone('SELECT COUNT(*) FROM feedback WHERE msg_id=?', (mid,))[0], 3)
        before = self.db.fetchall('SELECT * FROM feedback WHERE msg_id=?', (mid,))
        original = self.db.execute
        def broken(sql, params=()):
            if sql.startswith('INSERT INTO doc_votes'):
                raise RuntimeError('injected aggregate failure')
            return original(sql, params)
        with patch.object(self.db, 'execute', side_effect=broken):
            with self.assertRaises(RuntimeError):
                self.client.post('/api/feedback', json={'msg_id': mid, 'vote': -1, 'path': 'one.md'})
        self.assertEqual(self.db.fetchall('SELECT * FROM feedback WHERE msg_id=?', (mid,)), before)
        self.assertEqual(self.db.fetchone("SELECT up,down FROM doc_votes WHERE path='one.md'"), (1, 0))

    def test_feedback_rejects_user_messages_and_uncited_paths_without_writes(self):
        cid = self.mod.KBX.new_conv('owned', actor=self.b)
        user = self.mod.KBX.add_msg(cid, 'user', 'question', actor=self.b)
        answer = self.mod.KBX.add_msg(cid, 'assistant', 'answer', {'chunks': [{'path': 'cited.md'}]}, actor=self.b)
        before = self.db.fetchall('SELECT * FROM feedback'), self.db.fetchall('SELECT * FROM doc_votes')
        for mid, path in ((user, ''), (user, 'cited.md'), (answer, 'uncited.md')):
            self.assertEqual(self.client.post('/api/feedback', json={'msg_id': mid, 'vote': 1, 'path': path}).status_code, 400)
            self.assertEqual((self.db.fetchall('SELECT * FROM feedback'), self.db.fetchall('SELECT * FROM doc_votes')), before)
        for path in ('', 'cited.md'):
            self.assertEqual(self.client.post('/api/feedback', json={'msg_id': answer, 'vote': 1, 'path': path}).status_code, 200)

    def test_delete_cleans_only_related_feedback_and_rolls_back_on_failure(self):
        cid = self.mod.KBX.new_conv('delete-owned', actor=self.b)
        other = self.mod.KBX.new_conv('keep-other', actor=self.a)
        mid = self.mod.KBX.add_msg(cid, 'assistant', 'answer', {'chunks': [{'path': 'delete.md'}]}, actor=self.b)
        keep = self.mod.KBX.add_msg(other, 'assistant', 'answer', actor=self.a)
        self.assertEqual(self.client.post('/api/feedback', json={'msg_id': mid, 'path': 'delete.md', 'vote': 1}).status_code, 200)
        for target in (mid, keep):
            self.db.execute("INSERT INTO feedback(msg_id,email,vote,path) VALUES(?,'legacy@test.local',-1,'delete.md')", (target,))
        self.db.execute("UPDATE doc_votes SET down=2 WHERE path='delete.md'")
        self.db.commit()
        before = {t: self.db.fetchall('SELECT * FROM ' + t) for t in ('conv', 'msg', 'feedback', 'doc_votes')}
        original = self.db.execute
        def broken(sql, params=()):
            if sql.startswith('DELETE FROM conv'):
                raise RuntimeError('delete fault')
            return original(sql, params)
        with patch.object(self.db, 'execute', side_effect=broken):
            with self.assertRaises(RuntimeError):
                self.mod.KBX.del_conv(cid, actor=self.b)
        self.assertEqual({t: self.db.fetchall('SELECT * FROM ' + t) for t in before}, before)
        self.mod.KBX.del_conv(cid, actor=self.b)
        self.assertEqual(self.db.fetchall('SELECT id FROM feedback WHERE msg_id=?', (mid,)), [])
        self.assertEqual(self.db.fetchone('SELECT COUNT(*) FROM feedback WHERE msg_id=?', (keep,)), (1,))
        self.assertEqual(self.db.fetchone("SELECT up,down FROM doc_votes WHERE path='delete.md'"), (0, 1))
        self.assertIsNotNone(self.mod.KBX.conv_title(other, actor=self.a))

    def test_me_and_feedback_are_personal(self):
        cid = self.mod.KBX.new_conv('alice-private', actor=self.a)
        mid = self.mod.KBX.add_msg(cid, 'user', 'private question', actor=self.a)
        self.db.execute("INSERT INTO feedback(msg_id,email,vote,note,path,actor_user_id) VALUES(?, 'alice@test.local',1,'private-note','private-path',?)", (mid, self.a))
        self.db.commit()
        self.assertNotIn('private-note', self.client.get('/feedback').text)
        self.assertNotIn('private-path', self.client.get('/feedback').text)
        from types import SimpleNamespace
        with patch.object(self.mod.templates, 'TemplateResponse', side_effect=lambda **kw: kw['context']):
            req = SimpleNamespace(cookies={'kb_session': self.mod.make_token('bob@test.local')})
            context = self.mod.me_page(req)
        expected = self.db.fetchone("SELECT count(*) FROM msg m JOIN conv c ON c.id=m.conv_id WHERE c.owner_user_id=? AND m.role='user' AND m.created>=datetime('now','-7 days')", (self.b,))[0]
        self.assertEqual(context['recent_qa'], expected)

    def test_oauth_binding_survives_email_changes_and_separates_subjects(self):
        from kb import oauth_linuxdo
        first = oauth_linuxdo.resolve_account(self.db, {'sub': 'stable', 'email': fixture.ADMIN})
        self.assertEqual(first, oauth_linuxdo.resolve_account(self.db, {'sub': 'stable', 'email': 'changed@test.local'}))
        other = oauth_linuxdo.resolve_account(self.db, {'sub': 'different', 'email': fixture.ADMIN})
        self.assertNotEqual(first, other)
        self.assertNotEqual(first, fixture.ADMIN)

    def test_oauth_never_merges_email_and_requires_subject(self):
        for info in ({'email': fixture.ADMIN}, {'id': 'attacker', 'email': fixture.ADMIN}):
            self.client.cookies.set('kb_oauth_state', 'state-xyz')
            before = self.db.fetchone('SELECT pass FROM users WHERE email=?', (fixture.ADMIN,))
            with patch('kb.web.routes.oauth_linuxdo.exchange_code', return_value={'access_token': 'fixture'}), patch('kb.web.routes.oauth_linuxdo.fetch_userinfo', return_value=info):
                res = self.client.get('/oauth/linuxdo/callback?code=c&state=state-xyz')
            token = res.cookies.get('kb_session')
            self.assertNotEqual(self.mod.check_token(token) if token else None, fixture.ADMIN)
            self.assertEqual(self.db.fetchone('SELECT pass FROM users WHERE email=?', (fixture.ADMIN,)), before)
            if 'id' not in info:
                self.assertIsNone(token)
