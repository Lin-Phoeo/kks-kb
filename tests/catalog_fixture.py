"""Temporary synthetic corpus for catalogue/search presentation checks."""
from contextlib import contextmanager
import json
import os
from pathlib import Path
import secrets
import shutil
import sys
import tempfile

from fastapi.testclient import TestClient

ROOT = Path(__file__).resolve().parents[1]
LONG_PATH = '材质与贴图/' + '很长的中文归档路径用于确认手机阅读不会横向撑开页面' * 3 + '.md'


class CatalogFixture:
    def __init__(self):
        self.tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
        root = Path(self.tmp.name)
        shutil.copytree(ROOT / 'app', root / 'app', ignore=shutil.ignore_patterns('__pycache__'))
        (root / 'data').mkdir()
        docs = root / 'docs'
        examples = {
            LONG_PATH: '# 边缘光与材质参数\n\nRimLight 控制轮廓明暗。先保留参数，再核对材质在当前灯光中的表现。\n\n<script>window.catalogInjected=true</script> RimLight 原文应仅作为文字显示。\n',
            '材质与贴图/normal-map.md': '# 法线贴图核对（新目标第15轮）\n\n检查法线方向，保留原始贴图和参数。\n',
            '灯光与画面/rim-light.md': '# 灯光下的边缘光\n\nRimLight 的外观随灯光与观察角度改变，请对照原文。\n',
        }
        for name, text in examples.items():
            path = docs / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding='utf-8')
        extra = root / 'extra'
        extra.mkdir()
        (extra / 'external.md').write_text('# 另一个资料分区\n\n这一项不应出现在指定分区中。\n', encoding='utf-8')
        password = secrets.token_urlsafe(24)
        (root / 'config.json').write_text(json.dumps({'auth': {'secret': 's' * 40,
            'admin_email': 'catalog@example.invalid', 'admin_password': password},
            'gitee_key': '', 'llm_key': '', 'llm_url': 'http://127.0.0.1:1/v1'}), encoding='utf-8')
        self.old_path = sys.path[:]
        for name in list(sys.modules):
            if name in {'app', 'kb', 'indexer'} or name.startswith('kb.'):
                del sys.modules[name]
        sys.path.insert(0, str(root / 'app'))
        import indexer
        indexer.build(str(root / 'data/kb.db'), str(docs), do_embed=False, collection='demo')
        indexer.build(str(root / 'data/kb.db'), str(extra), do_embed=False, collection='other', append=True)
        import app
        self.app = app
        self.client = TestClient(app.app, follow_redirects=False)
        self.client.headers['Origin'] = 'http://testserver'
        login = self.client.post('/login', data={'email': 'catalog@example.invalid', 'password': password})
        if login.status_code != 303 or not self.client.cookies.get('kb_session'):
            raise RuntimeError('Synthetic catalogue login failed')

    def close(self):
        self.client.close()
        self.app.KBX.con.close()
        self.app.STATE.close()
        sys.path[:] = self.old_path
        self.tmp.cleanup()

    @contextmanager
    def browser(self, width, height, theme):
        from urllib.parse import urlsplit
        from playwright.sync_api import sync_playwright
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True, channel=os.environ.get('KKS_TEST_BROWSER_CHANNEL', 'chromium'))
            context = browser.new_context(viewport={'width': width, 'height': height}, reduced_motion='reduce', color_scheme=theme)
            context.add_init_script(f'localStorage.setItem("theme",{json.dumps(theme)})')
            def serve(route):
                url = urlsplit(route.request.url)
                if url.netloc != 'testserver' or route.request.method != 'GET':
                    return route.abort()
                response = self.client.get(url.path + ('?' + url.query if url.query else ''))
                route.fulfill(status=response.status_code, body=response.content, headers=dict(response.headers))
            context.route('**/*', serve)
            page = context.new_page()
            errors = []
            page.on('pageerror', lambda error: errors.append(str(error)))
            try:
                yield page, errors
            finally:
                context.close()
                browser.close()
