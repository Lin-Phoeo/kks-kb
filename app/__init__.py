#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""app 包入口：让 `import app`（包上下文）给出与 app.app 相同的公共接口。

app.app 的装配需要 config.json / kb.db；没有配置时（例如本机跑
`py -3.14 -m app.migrate_state`），包仍要能导入，因此这里的 re-export 是尽力而为。
"""
import sqlite3

try:
    from .app import (CODE_SENDS_IP, CFG, KBX, LOGIN_FAILS_ACCT, LOGIN_FAILS_IP,
                      SYS_RECIPE, STATE, _user_exists, app, build_prompt,
                      build_prompt_recipe, hybrid_recipe, llm_chain, validate_params)
except (OSError, sqlite3.OperationalError):
    # 缺 config.json / kb.db：允许 -m app.migrate_state 这类工具仍然可跑
    pass
