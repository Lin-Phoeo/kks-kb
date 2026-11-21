#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""G1 参数查阅页冒烟：临时目录里搭迷你知识库 + 迷你参数索引，
用 TestClient 走真实路由。不碰线上数据；缺依赖时整体跳过。
运行：py -3.14 -W ignore::ResourceWarning -m unittest tests.test_params_pages -v
"""
import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
SRC_APP = os.path.join(HERE, "..", "app")

try:
    import fastapi, httpx, jieba, markdown, numpy, jinja2, multipart  # noqa: F401,E401
    from fastapi.testclient import TestClient
    HAVE_DEPS = True
except Exception:   # pragma: no cover
    HAVE_DEPS = False

ADMIN = "admin@test.local"
ADMIN_PW = "admin-password-123"

# 迷你参数索引：覆盖精确命中（_Cutoff）、无下划线变体（Cutoff）、
# 默认值分歧（dv 多值）与非分歧。
PARAMS = {
    "_Cutoff": {"c": 8, "t": ["Float"], "d": "0.5", "dv": {"0.5": 7},
                "f": ["KoikatsuModdingTools", "UnlitWF"]},
    "Cutoff": {"c": 159, "t": ["Float", "Float(0,1)"], "d": "0.5",
               "dv": {"0.5": 53, "0.01,*explicit*": 1},
               "f": ["D盘_KKS资源库", "KKLT", "shader参数"]},
    "_EmissionColor": {"c": 134, "t": ["Color", "Vector"], "d": "(1,1,1,1)",
                       "dv": {"(1,1,1,1)": 108, "(0,0,0,0)": 11,
                              "(0,0,0,1)": 4, "(0,0,0)": 1},
                       "f": ["Az", "KoikatsuModdingTools", "lilToon"]},
    "_RimLight": {"c": 110, "t": ["Float"], "d": "0", "dv": {"0": 108, "1": 1},
                  "f": ["Az", "UTS-卡通"]},
}

_tmp = None
_mod = None
client = None


def setUpModule():
    global _tmp, _mod, client
    if not HAVE_DEPS:
        return
    _tmp = tempfile.TemporaryDirectory(ignore_cleanup_errors=True)
    root = _tmp.name
    shutil.copytree(SRC_APP, os.path.join(root, "app"),
                    ignore=shutil.ignore_patterns("__pycache__"))
    os.makedirs(os.path.join(root, "data"))
    docs = os.path.join(root, "docs")
    os.makedirs(docs)
    with open(os.path.join(docs, "a.md"), "w", encoding="utf-8") as f:
        f.write("# 测试文档\n\n## 边缘光\n\nRimLight 参数 `_RimColor` 控制边缘光颜色。\n")
    pidx = os.path.join(root, "data", "col", "paramidx")
    os.makedirs(pidx)
    with open(os.path.join(pidx, "params.json"), "w", encoding="utf-8") as f:
        json.dump(PARAMS, f, ensure_ascii=False)
    with open(os.path.join(root, "config.json"), "w", encoding="utf-8") as f:
        json.dump({"llm_url": "http://127.0.0.1:1/v1", "llm_model": "x", "llm_key": "x",
                   "auth": {"secret": "s" * 40, "admin_email": ADMIN,
                            "admin_password": ADMIN_PW}}, f)
    sys.path.insert(0, os.path.join(root, "app"))
    # kb 是进程内单例：其它测试模块已缓存仓库版 kb，这里必须清掉，
    # 让临时副本里的 app/kb 真正被加载。
    for _k in list(sys.modules):
        if _k == "app" or _k == "kb" or _k.startswith("kb."):
            del sys.modules[_k]
    import indexer
    indexer.build(os.path.join(root, "data", "kb.db"), docs, do_embed=False, collection="kks")
    import app as mod
    _mod = mod
    client = TestClient(mod.app, follow_redirects=False)
    client.headers["Origin"] = "http://testserver"


def tearDownModule():
    if _tmp:
        try:
            _mod.KBX.con.close()
            _mod.STATE.close()
        except Exception:
            pass
        for _k in list(sys.modules):
            if _k == "app" or _k == "kb" or _k.startswith("kb."):
                del sys.modules[_k]
        _tmp.cleanup()


def _login(c, email=ADMIN, pw=ADMIN_PW):
    return c.post("/login", data={"email": email, "password": pw})


@unittest.skipUnless(HAVE_DEPS, "缺依赖，跳过参数页冒烟")
class ParamPageTests(unittest.TestCase):
    def setUp(self):
        global client   # 每个用例一个全新的匿名客户端，避免 cookie 串用例
        client = TestClient(_mod.app, follow_redirects=False)
        client.headers["Origin"] = "http://testserver"
        _mod.LOGIN_FAILS_IP._d.clear()
        _mod.LOGIN_FAILS_ACCT._d.clear()
        _mod.CODE_SENDS_IP._d.clear()

    def authed_client(self):
        c = TestClient(_mod.app, follow_redirects=False)
        c.headers["Origin"] = "http://testserver"
        r = _login(c)
        self.assertEqual(r.status_code, 303, r.text)
        return c

    # ---- 鉴权 ----
    def test_anonymous_params_redirects_to_login(self):
        r = client.get("/params")
        self.assertEqual((r.status_code, r.headers.get("location")), (303, "/login"))

    def test_anonymous_param_detail_redirects_to_login(self):
        r = client.get("/param/_Cutoff")
        self.assertEqual((r.status_code, r.headers.get("location")), (303, "/login"))

    # ---- 索引页 ----
    def test_authed_params_contains_known_name(self):
        r = self.authed_client().get("/params")
        self.assertEqual(r.status_code, 200)
        self.assertIn("_Cutoff", r.text)
        self.assertIn("_EmissionColor", r.text)

    def test_params_marks_divergent_badge(self):
        r = self.authed_client().get("/params")
        self.assertEqual(r.status_code, 200)
        self.assertIn("默认值分歧", r.text)   # _EmissionColor / Cutoff / _RimLight 均分歧

    # ---- 详情页 ----
    def test_authed_param_detail_cutoff(self):
        r = self.authed_client().get("/param/_Cutoff")
        self.assertEqual(r.status_code, 200)
        self.assertIn("_Cutoff", r.text)
        self.assertIn("UnlitWF", r.text)

    def test_authed_param_detail_divergence_callout(self):
        r = self.authed_client().get("/param/_EmissionColor")
        self.assertEqual(r.status_code, 200)
        self.assertIn("默认值分歧", r.text)

    def test_param_nonexistent_404(self):
        r = self.authed_client().get("/param/NoSuchParam_xyz")
        self.assertEqual(r.status_code, 404)

    def test_param_special_chars_not_500(self):
        c = self.authed_client()
        for p in ("/param/abc%20def", "/param/%5BGamma%5D", "/param/1-2-3"):
            r = c.get(p)
            self.assertIn(r.status_code, (200, 404), p)

    # ---- 搜索 ----
    def test_search_filters_and_empty_state(self):
        c = self.authed_client()
        r = c.get("/params?q=Cutoff")
        self.assertEqual(r.status_code, 200)
        self.assertIn("_Cutoff", r.text)
        self.assertIn("Cutoff", r.text)
        self.assertIn("默认值分歧", r.text)      # Cutoff 有 2 种默认值
        # 不相关的参数不应作为结果行出现（placeholder 里可能提到名字，故查链接）
        self.assertNotIn("/param/_EmissionColor", r.text)
        self.assertNotIn("/param/_RimLight", r.text)
        r2 = c.get("/params?q=zzzzzz")
        self.assertEqual(r2.status_code, 200)
        self.assertIn("没有匹配的参数", r2.text)


if __name__ == "__main__":
    unittest.main()
