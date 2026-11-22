# 本地检查与 GitHub Actions

使用 Python 3.12，在仓库根目录创建独立虚拟环境。测试依赖锁包含运行依赖、Word/PDF工具、浏览器工具和漏洞审计器：

```bash
python -m pip install --require-hashes -r requirements-test.lock
python -m pip check
python -B scripts/check_public_tree.py
python -B tests/run_tests.py --output test-results/unit
python -m pip_audit
python -B -m pipeline.kbpipe --help
```

测试运行器为每个模块创建独立进程和临时资料，避免旧的全局应用状态互相污染。普通套件排除单独运行的浏览器用例；任何未预期的 skip、错误或超时都会失败，不把“依赖没有装所以跳过”记成通过。

备份与恢复测试需要 Bash；Windows 可使用 Git for Windows 附带的 Bash，并通过 `KKS_TEST_BASH` 指定实际可执行文件。Linux CI 使用系统 Bash。

浏览器检查使用新建的无头 Chromium，不接管已有浏览器：

```bash
python -m playwright install chromium
python -B tests/run_tests.py --browser --output test-results/browser
```

Linux 首次安装浏览器时可使用 `python -m playwright install --with-deps chromium`。

## 必需检查

Word 表格的专项回归可用 `python -B tests/test_native_word.py -v` 运行。测试核对原件哈希和最终 HTML 单元格的纵横跨度。转换器遇到非零行首偏移，或纵向合并的续接格与上一行起始列或宽度不一致时，会报告表、行、列（从 1 开始），在替换已有阅读版之前停止。

- **Verify**：全套合成测试、锁定依赖安装、依赖兼容性和漏洞审计、流水线CLI与Shell语法。
- **Browser**：在临时应用数据上运行浏览器行为检查。
- **Source safety**：公开文件范围检查和 Gitleaks 提交历史扫描。检测结果脱敏输出。

每次 PR 和 `main` 推送都会运行检查。结果属于该次提交，不能借用另一个提交的绿灯。Actions 的测试工件保留7天；里面只应出现合成测试数据和脱敏结果。

## 覆盖边界

公开 CI 不访问真实服务器、教材、角色/存档、游戏安装或外部模型。它验证程序在合成资料上的行为，不证明真实语料覆盖率、线上检索质量、手机实机手势或游戏画面正确。生产发布和游戏配置变更需要单独的验收记录。

## 更新依赖

维护 `pyproject.toml` 中的版本范围，用 `uv pip compile --generate-hashes` 重新生成对应锁文件，然后重新安装、测试并执行 `pip-audit`。不要手工编写下载哈希或为过检查忽略已知漏洞。
