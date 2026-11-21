#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""KKS 知识库 v3 — 装配层：加载配置/检索/内容/生成/Web，并 re-export 公共接口。

两种入口都成立：
- 模块入口：以仓库 app/ 为工作目录，uvicorn 用 `app:app` 加载本文件。
- 本地包：仓库根在 sys.path，`import app` 加载 app 包，再由 __init__ 落到 app.app。
"""
import jieba

try:
    from .kb.config import CFG, STATE
    from .kb.retrieval import KBX
    from .kb.generation.llm import llm_chain
    from .kb.generation.prompt import build_prompt, build_prompt_recipe, SYS_RECIPE
    from .kb.generation.validate import validate_params
    from .kb.web import middleware, routes
    from .kb.web.routes import (CODE_SENDS_IP, LOGIN_FAILS_ACCT, LOGIN_FAILS_IP,
                                _user_exists, app, hybrid_recipe)
except ImportError:
    from kb.config import CFG, STATE
    from kb.retrieval import KBX
    from kb.generation.llm import llm_chain
    from kb.generation.prompt import build_prompt, build_prompt_recipe, SYS_RECIPE
    from kb.generation.validate import validate_params
    from kb.web import middleware, routes
    from kb.web.routes import (CODE_SENDS_IP, LOGIN_FAILS_ACCT, LOGIN_FAILS_IP,
                               _user_exists, app, hybrid_recipe)

jieba.setLogLevel(60)
