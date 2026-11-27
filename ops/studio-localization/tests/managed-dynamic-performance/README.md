# 动态匹配器行为和分配检查

需要 .NET 10 SDK，不依赖游戏、Unity 或第三方测试包。从仓库根目录运行：

```powershell
dotnet run --project ops/studio-localization/tests/managed-dynamic-performance -c Release -- --rules ops/studio-localization/translations/dynamic-original.json
```

检查作用域隔离、原文边界、回退开关、精确词和正则优先级、同文规则阻止回退、捕获内容保真、负例缓存、LRU 最近使用和淘汰、长度限制及超时规则熔断。指定 `--rules` 时额外逐项核对公开精确词。

基准对预热后的精确命中、正则缓存命中、负例命中、全局回退命中和首次未命中后淘汰分别测量五次，报告每次操作分配字节数和耗时中位数。默认要求前四类预热查询零托管分配。时间只作同机比较，不设跨机器耗时门槛。

改前对照可先保存源文件到仓库外，再通过 `-p:RuleMatcherSource=<源文件绝对路径>` 和独立 `--artifacts-path <目录>` 构建这个相同探针；运行生成的 DLL 时加 `--allow-allocations --label baseline --output <JSON路径>`。候选版本使用默认源文件和另一个构建目录，省略 `--allow-allocations`。不要把本机对照文件或日志提交进仓库。

比较耗时时，两次进程应使用相同运行条件。可在运行前设置 `$env:DOTNET_TieredCompilation = '0'`，避免测量中途发生分层 JIT 编译；结束后恢复原环境变量。JSON 记录该开关，默认模式的结果仍可检查分配，但不应与另一种 JIT 条件混比。

这些结果只证明 .NET 10 中的纯托管匹配器行为和分配，不包含 Unity 的 Mono、Harmony、作用域反射、真实窗口排版或绘制，也不代表游戏 FPS 改善。
