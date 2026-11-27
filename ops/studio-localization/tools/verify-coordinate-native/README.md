# Coordinate 原生兼容离线检查

本工具由维护者显式指定现有游戏目录和候选适配器。只读插件 DLL 和嵌入资源，不读取人物卡、场景、用户配置或启动 Unity；不属于默认测试套件。

需要 .NET 10 SDK；先在仓库外构建 `sources/coordinate-native/StudioCoordinateNative.csproj`，再构建本工具，二者传入 `-p:GameRoot=<游戏目录>`。本工具引用游戏自带的 Mono.Cecil 和 Harmony，运行时从指定游戏目录解析依赖。

```powershell
dotnet <构建目录>/CoordinateNativeVerifier.dll <游戏目录> <KKS_CoordinateLoadOption.dll> <候选Codex.StudioCoordinateNative.dll>
```

输出 JSON 包含：

- 原插件／资源 SHA、24 个原生键、显式语言和未就绪回退、软依赖顺序。
- 两个消费者真实方法中的 `Replace Mode`、`Empty` 比较指令位置。
- 改前中文误判的红基线、改后替换模式通过且追加模式继续拒绝、空槽排除、缺失按钮、未知版本和结构变化拒绝。
- 原 IL 指令数量、除两个调用外的指令对象、替换调用上的分支标签和异常块均保持。
- 一次性诊断和诊断接收器失败不干扰模式决定。

测试执行的是来自实际 IL 的比较表达式与实际候选辅助方法，并用同一委托生成器读取可变的合成模式字段。它不执行完整 Unity 方法，也不证明真实服装或 DBDE／AssetImport 数据已经正确转移。
