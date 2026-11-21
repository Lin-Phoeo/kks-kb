# KKS 知识库

面向 Koikatsu Sunshine 创作与技术学习的资料库应用源码。使用 FastAPI、Jinja2、SQLite FTS5 和 NumPy，支持文档检索、阅读目录、原图浏览、原件归档映射和可配置的 AI 问答。

本仓库提供程序、测试和自造演示资料。实际教程、Word/PDF、游戏本体、MOD、人物卡、存档、数据库及部署凭据由使用者自行管理，不包含在仓库中。

## 本机运行

推荐 Python 3.12。以下命令在仓库根目录执行：

```bash
python -m venv .venv
# Windows PowerShell
.venv\Scripts\Activate.ps1
# macOS / Linux 则执行：source .venv/bin/activate
python -m pip install --require-hashes -r requirements.lock
python scripts/create_demo.py
python -m uvicorn app:app --app-dir app --host 127.0.0.1 --port 8000
```

打开 `http://127.0.0.1:8000`。演示只使用合成资料，不调用外部模型服务；AI 问答需另行配置。生成器发现已有 `data/` 或 `config.json` 会拒绝覆盖。

导入自己的资料前，阅读 [数据与配置](docs/data-and-config.md)。Word 原生转换工具的可选依赖在 `requirements.native-reader.lock` 中。

## 用 GitHub 配合 AI 开发

```text
Issue 写明目标 → 功能分支 → AI 修改与本地验证 → PR → Actions 检查 → 审阅并合并
```

你负责描述期望和验收结果，AI 负责实现、解释差异与提供证据。PR 是检查改动的地方；Actions 是每次提交都运行的自动检查。合并代码不会自动修改服务器或游戏安装。

- [贡献流程](CONTRIBUTING.md)：从任务到合并的操作。
- [AI 协作约定](AGENTS.md)：不同编码助手共用的工作规则。
- [项目结构](docs/architecture.md)：源码、数据和运行时的关系。
- [验证说明](docs/verification.md)：本地与 CI 的相同入口及覆盖边界。

## 源码范围与许可

这是经过公开范围整理的源码基线，不携带旧的私人运维历史或聊天导出。安全问题请先看 [SECURITY.md](SECURITY.md)。

第三方依赖保留各自许可，仓库内 Three.js 的 MIT 许可位于 `app/static/vendor/three/LICENSE`，详见 [第三方声明](THIRD_PARTY_NOTICES.md)。本项目自有代码暂未指定统一开源许可证；公开可见不代表第三方资料的授权发生改变。
