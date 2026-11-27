# 配置翻译适配器托管验证工具

这是需要维护者显式运行的本机验证工具，不属于常规测试套件，不会由常规测试入口自动执行。

构建和运行需要通过 `GameRoot` / 第一个命令行参数提供实际游戏目录，用于只读加载 ConfigurationManager、BepInEx、Harmony、Mono.Cecil 和 Unity 托管程序集。适配器 DLL 和可选词表也由调用者显式指定。工具不启动游戏，不安装或修改插件，不读取或写入配置值、场景或存档。

验证状态全部来自合成数据，包括设置对象、身份组合和查词负载。工具用一个非零托管指针哨兵构造 Unity 对象包装，以避免调用原生空值检查；不会调用 Unity 原生生命周期或渲染方法。可选词表只验证身份与译文回读，不证明实际界面可见性、排版或语义完整性。

报告记录程序集哈希、运行环境、回归结果、每轮耗时与分配量。它测量 .NET 10 托管运行时，不代表游戏所用 Unity Mono、Harmony 绘制开销或游戏帧率。

## 显式运行

从仓库根目录运行 PowerShell。将两个目录变量改为自己的位置；构建产物和报告应放在仓库外，基线与候选 DLL 必须使用不同路径。

```powershell
$gameRoot = 'D:/YourGameDirectory'
$artifactRoot = 'D:/YourVerificationOutput/configuration'

dotnet build ops/studio-localization/tools/verify-configuration-performance/ConfigurationPerformance.csproj `
  -c Release "-p:GameRoot=$gameRoot" `
  "-p:BaseIntermediateOutputPath=$artifactRoot/obj/" `
  -o "$artifactRoot/harness" --nologo

$env:DOTNET_TieredCompilation = '0'
dotnet "$artifactRoot/harness/ConfigurationPerformance.dll" `
  "$gameRoot" "$artifactRoot/candidate/Codex.StudioConfigLocalization.dll" `
  "$artifactRoot/candidate-report.json" `
  ops/studio-localization/translations/configuration-original.json
```

参数顺序为 `GAME ADAPTER REPORT [CATALOG]`。不提供 `CATALOG` 时只运行合成回归；提供公开词表或本机词表时，工具会只读逐条验证。对基线和候选分别启动进程，采用相同参数、词表和运行环境比较。报告父目录须预先存在。
