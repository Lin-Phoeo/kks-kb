# 贡献与 AI 协作

## 一次改动的流程

1. 在 Issue 写清问题、预期效果和验收方法。小修复可以直接在 PR 描述这些内容。
2. 从最新 `main` 创建分支，例如 `feat/reader-search` 或 `fix/image-order`。
3. 让 AI 阅读 `AGENTS.md`，只实现该任务；需要判断产品取舍时由人决定。
4. 运行 [验证命令](docs/verification.md)，检查 `git diff`，确认没有资料原件、密钥或无关改动。
5. 提交并推送分支，创建 PR。等待必需检查通过，再审阅代码、截图或测试证据。
6. 使用 squash merge 把一项任务合成一个清楚的提交，删除已合并分支。需回退时使用新的 revert 提交。

```bash
git switch main
git pull --ff-only
git switch -c feat/reader-search
# 实现并验证
git add <本次改动的文件>
git diff --cached
git commit -m "feat(reader): add document search"
git push -u origin feat/reader-search
gh pr create
```

## AI 任务提示词示例

> 阅读 AGENTS.md。实现 Issue #编号，先确认现状与验收要求，在独立分支完成。保护已有改动，添加能验证用户行为的必要测试。运行相应检查，报告改了什么、为何改、测试结果和未验证部分，创建 PR。

## 提交和审阅

- 提交前缀可用 `feat`、`fix`、`docs`、`test`、`ci`、`chore`。
- 一次 PR 解决一件可验收的事情。涉及数据库、权限或公开原件下载时说明兼容性与回滚方式。
- 当前维护者可以审阅自己的 AI 产出；仓库要求自动检查，但不强制等待不存在的第二位维护者。
- 依赖更新由 Dependabot 提 PR，仍需测试和审阅，不自动合并。
- GitHub Actions 只做验证。服务器发布、模型密钥、游戏插件安装不放入默认 CI。
