#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""kbpipe：可复现的知识库数据流水线（阶段 F）。

只依赖标准库 + numpy；httpx 仅在真实嵌入器里延迟导入。
切块与建库逻辑复用 app/indexer，不重写。
"""
__version__ = "0.1.0"
