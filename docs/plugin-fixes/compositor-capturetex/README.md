# Compositor CaptureTex 返回类型兼容修复

Compositor 1.2.0 会给同名 `CaptureTex` 方法注册接收 `Texture2D __result` 的 postfix。已核验的 Screenshot Manager 21.1.4.1 中，`AlphaShot2.CaptureTex(int, int, int, AlphaMode)` 返回 `RenderTexture`，两者类型不兼容。

本修复在注册前增加 `typeof(Texture2D).IsAssignableFrom(methodInfo.ReturnType)` 判断，跳过不兼容方法。**它跳过当前 RenderTexture 捕获路线，未实现 RenderTexture 复制、缓存、资源所有权或截图接管。** 因而不能把消除注册错误理解为 Compositor 的所有截图功能已经恢复。

## 归属与来源

Compositor 的原作者是 **RinuNalo**。[HF Patch Plugin Readme](https://raw.githubusercontent.com/ManlyMarco/KKS-HF_Patch/master/Plugin%20Readme.md) 将 Compositor v1.2.0 的更多信息指向 **KK Discord**。该链接证明作者和分发线索，不是完整源码仓库，也没有提供可核实的具体 Discord 帖子链接。

此前范围有限的 GitHub 仓库与本地源码搜索没有找到可核实的官方兼容更新或匹配完整源码；这不证明它们不存在。本目录提供独立编写的定点补丁工具、最小语义差异和脱敏验证记录；不是官方更新、上游合并或完整源码重建。未附带原插件、修改后插件、依赖 DLL、游戏资产、字体或原始日志。

## 支持的精确输入

| 文件 | SHA-256 |
| --- | --- |
| 原始 Compositor 1.2.0 DLL | `5b7f7b5f9a8659d309f849307c535a25bb18175fcdaed156f2168cd0bb346d1f` |
| 本工具已验证的候选 DLL | `e6ad0a49ed2827aecb62284d101901d582e013aec91af3e8d763e4cfc74ac9a5` |
| 核验返回类型的 KKS_Screencap.dll | `56c5f1b8c9bdee2bb85525be4730d3b0a9edc0bc516f32d01751369adc7c0468` |

输入哈希不符时工具拒绝操作。请不要删掉这个检查来适配另一版本。

## 本地生成候选

需要 .NET 10 SDK，以及本地合法取得的 Mono.Cecil **0.10.4.0**。验证使用 SDK 10.0.204；Mono.Cecil SHA-256 为 `7ae470288fff4a402899c254d0a76cefef55877f5c54f96e83c797cc5bb6e2f6`。项目不从 NuGet 获取依赖。

在本目录构建，替换占位路径：

```powershell
dotnet build ./CompositorGuard.csproj -c Release -p:CecilPath="<local-Mono.Cecil.dll>"
```

创建独立工作目录，把原始插件复制进去作为 `input.dll`；不要把运行中的游戏插件作为输出。输出目录与报告目录须已经存在，`candidate.dll` 和 `report.json` 都必须尚不存在：

```powershell
dotnet ./bin/Release/net10.0/CompositorGuard.dll `
  "<work-directory>/input.dll" `
  "<work-directory>/candidate.dll" `
  "<work-directory>/report.json" `
  "<game-directory>/BepInEx/core" `
  "<game-directory>/KoikatsuSunshine_Data/Managed" `
  "<game-directory>/BepInEx/plugins/KKS_BepisPlugins"
```

工具将三条文件路径规范化并忽略大小写检查重合，拒绝已有输出/报告、目录和 Windows 备用数据流。先在内存生成并验证候选，再以 `FileMode.CreateNew` 写入两个新文件。检查后有文件抢先出现时也不会覆盖它。写入不是双文件事务：晚期 I/O 失败可能留下新建的空文件或部分文件，失败后应检查产物并使用新的目标路径重试。成功返回码为 0；报告仅证明静态候选检查，不会自动安装或运行游戏。

如自行决定安装，先退出 Studio/游戏，将当前插件备份到新的位置，再核对候选哈希并手动替换对应插件。回滚时退出程序并恢复自己的原始备份。保留备份；本包不提供可供恢复的第三方 DLL。

## 验证与边界

- 新工具重新构建为 0 警告、0 错误；隔离生成的 DLL 与上述候选逐字节一致。
- 只改变 `DefaultNamespace.Entry::TryPatchScreencap()`，增加 6 条 IL；其余 2600 个方法指纹、程序集引用和资源保持一致。原 postfix、GUI 与渲染方法未改。
- 15 个拒绝用例覆盖三路径重合、大小写及 `..` 别名、既有文件、硬链接、目录、错误输入哈希、缺失引用目录和参数；输入及已有文件全部保持原样。
- 旧工具对“报告等于输入/输出”的隔离复现被 Windows 共享锁阻止，但失败前已经输出候选；“已有报告”则确实被覆盖。新工具对这些情况在写入前拒绝。
- 既有运行时记录确认初始化、打开 UI，以及空 Studio 的一张 RGBA 1920×1080 全透明截图，返回类型错误数为 0。本次工具加固没有重新启动游戏。未验证角色透明边缘、全部节点、全部捕获路线或复杂场景；没有加载或保存用户卡与场景。

具体脱敏数据见 [evidence.json](evidence.json)；[return-type-guard.patch](return-type-guard.patch) 是反编译视图的最小语义对照，并非可对未知上游版本直接套用的源码补丁。
