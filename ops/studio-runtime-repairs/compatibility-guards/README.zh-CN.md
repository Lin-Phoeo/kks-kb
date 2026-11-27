# KKS 三项运行时兼容修复

本目录提供可核对的最小源码差异与本地二进制补丁工具，处理三个已定位的问题。**当前完成了合成对照、补丁构建和原件/候选哈希验证；实际 Studio 功能回归尚未完成。**

| 项目 | 触发条件 | 修改范围 |
|---|---|---|
| ShalltyUtils 1.3.2 | 旧场景存在插件数据，却没有可选的 `keyframesGroupsData` 字段 | 清理旧分组后检查字典与可选字段，正常分组恢复逻辑保留 |
| SexFaces 1.4.0 官方 KKS 构建 | `RangeSlider` 在 `OnGUI` 之外提前初始化，静态字段访问 `GUI.skin`，类型初始化永久失败 | 在第一次实际绘制 `Create` 时初始化并缓存样式 |
| KLS 1.3.0 本地构建 | Map 对象存在，但空地图/切换期间 `sunLightInfo` 为空，诊断代码读取游戏 `mapEffectInfo` getter | 仅读取具有 sunLightInfo 的地图，环境功能与错误日志保留 |

不包含第三方 DLL、完整反编译文件、游戏资源或用户日志。PoseTools 保存回调的输入状态仍未查清，不在本次补丁中猜改。

## 先看上游

- [Lusiocc/ShalltyUtils v1.3.2](https://github.com/Lusiocc/ShalltyUtils/releases/tag/v1.3.2)（2026-10-19）明确提供 KKS 包，但[分组源码](https://github.com/Lusiocc/ShalltyUtils/blob/v1.3.2/ShalltyUtils.Core/ShalltyUtils.KeyframesGroups.cs#L396)仍直接取可选字典键。
- Sauceke 已迁移到 [Codeberg/Sauceke/SexFaces](https://codeberg.org/Sauceke/SexFaces)。本轮从作者 [v1.4.0 稳定发布](https://codeberg.org/Sauceke/SexFaces/releases/tag/v1.4.0)取得 KKS 专用 [KKS_SexFaces.zip](https://codeberg.org/Sauceke/SexFaces/releases/download/v1.4.0/KKS_SexFaces.zip)，发布提交短 SHA `ac43dbd990`；原 ZIP SHA-256 `e6065eea60e826992a96a5d7f7177a06abdecc64c3726ec9a147f503041ae461`，包内 DLL SHA-256 `8C3A9CED83022489AAE8A66D01A9D9307949B701E356321817831E4E2CB0E13E`，BepInPlugin 版本 `1.4.0`，依赖 `marco.kkapi >= 1.31.2`。Codeberg 页面可见的 v1.4.0 `RangeSlider.cs` 与 GitHub v1.3.0 固定快照逐字节相同，仍在静态字段读取 `GUI.skin`；所以本候选以官方 1.4.0 DLL 重新生成。
- KLS 本次尚无已确认的公开作者源码/发布链接；此处只提供针对下列已知原件的本地补丁，不冒用其他灯光插件的版本。

## 两种用法

有源码与完整原项目构建环境时，优先应用对应 `.patch` 文件并按原作者构建说明编译。`shallty-missing-groups.patch` 以 v1.3.2 的 `ShalltyUtils.Core/ShalltyUtils.KeyframesGroups.cs` 为基准；`sexfaces-lazy-gui.patch` 以 Sauceke v1.4.0 KKS 发布提交中的 `KK_SexFaces/RangeSlider.cs` 为基准。Codeberg v1.4.0 GUI 源文本与固定 GitHub v1.3.0 文件逐字节相同；源码差异对 1.4.0 源文件执行了 `git apply --check` 和实际应用。没有声称完整上游解决方案已经构建。

没有源码但持有下面准确原件时，可在自己的电脑运行补丁工具。工具只读原件，向**另一个新路径**输出候选，拒绝覆盖已有输出、覆盖输入和不匹配的原件。它不自动安装，不修改配置，不加载或保存卡片/场景。

### 构建工具

需要 .NET 10 SDK，以及本机 BepInEx `core/Mono.Cecil.dll`。不随本目录分发该依赖。

```powershell
$cecil = 'D:/YourGame/BepInEx/core/Mono.Cecil.dll'
dotnet build ./OtherRuntimeGuards.csproj "-p:CecilPath=$cecil" -c Release -o ./bin
```

### 生成候选

先从 `SUPPORTED-ARTIFACTS.json` 核对自己的原件 SHA-256。下面一次只选一个项目，所有路径换成自己的绝对路径，输出放到独立目录：

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:/YourGame/BepInEx/plugins/KKS_ShalltyUtils.dll'
dotnet ./bin/OtherRuntimeGuards.dll shallty 'D:/YourGame/BepInEx/plugins/KKS_ShalltyUtils.dll' 'D:/PatchCandidates/KKS_ShalltyUtils.dll'

dotnet ./bin/OtherRuntimeGuards.dll sexfaces 'D:/PatchCandidates/official-v1.4.0/KKS_SexFaces.dll' 'D:/PatchCandidates/guarded-v1.4.0/KKS_SexFaces.dll'

dotnet ./bin/OtherRuntimeGuards.dll kls 'D:/YourGame/BepInEx/plugins/KoikatsuSunshineLightingSuite/KoikatsuSunshineLightingSuite.dll' 'D:/PatchCandidates/KoikatsuSunshineLightingSuite.dll'
```

工具会在候选旁生成 `.json` 原件/输出哈希与变更收据。这份运行收据包含你本机路径，自己保留即可；公开反馈只需附版本、哈希和去隐私后的异常栈。

### 安装与回滚

1. 正常退出 Studio，确认进程已结束。先复制原 DLL 到游戏目录外的独立备份目录，记录哈希。每次只安装一个候选。
2. SexFaces 先从作者官方 v1.4.0 KKS ZIP 更新（满足 KKSAPI 1.31.2），再将该 DLL 放入单独暂存路径并运行上方工具；不要对 live v1.0 DLL 运行此版本锁定工具。核对候选输出 SHA 与 `SUPPORTED-ARTIFACTS.json`，再用候选替换同名原 DLL。不要把补丁工具、Cecil、候选副本或多个版本一起扔进 `BepInEx/plugins`。
3. 用原游戏启动器启动，启用原控制台；按下一节验证真正的功能。
4. 若有新异常，正常退出后把备份原 DLL 复制回原位置，核对恢复后的 SHA 等于输入 SHA，再重新启动。回滚不需要修改场景或卡片。

工具不改插件版本声明；以输入/输出 SHA 和收据判断是否已应用。对已修补 DLL 再运行会因输入哈希不匹配而拒绝。

## 可复跑的合成回归

见 [TESTING.md](TESTING.md)。公开测试只生成自己的 fixture，并与生产工具共用 `GuardPatches.cs`；19 项通过仍不代表实际 Studio 场景回归完成。

## 实际功能验收

- Shallty：载入没有分组新字段的旧场景，不应出现缺键异常；随后载入带有效分组的场景，应恢复其分组，旧场景分组不能残留。
- SexFaces：确认 KKSAPI 版本满足1.31.2，打开配置界面 `Face Switch Time Range`，拖动两端并 Reset，核对配置值实际变化，再次打开仍可操作。
- KLS：在空地图和切图后打开环境/灯光界面，核对环境与灯光仍可编辑；检查不是通过关闭功能或隐藏日志消除异常。

## 已做验证及边界

- .NET 10 补丁工具构建：0 warning、0 error。
- 使用同一补丁算法的合成 CIL 对照：19/19，包括原版失败、修补后通过、有效数据路径保留、样式首次创建后复用。
- 当前真实 DLL 的 CIL 差异：Shallty 仅 `LoadSceneData`；SexFaces 仅 `.cctor`、`Create`，另加一初始化方法和将三个样式字段改为可写；KLS 仅 `DumpEnvironmentRecordCore`。无其他方法逻辑变更。
- 公开工具重新生成三份候选，输出 SHA 与隔离候选一致；不支持的原件被拒绝，未写输出。
- 实际 Studio 的上述窗口/旧场景/切图回归仍需完成。合成测试不等于 Unity 功能验收，也不证明日志里所有其他异常均已修复。

本目录自行编写的 C# 补丁工具、PowerShell 运行器与合成 fixture 按 MIT 提供，见 `LICENSE`。`shallty-missing-groups.patch` 与 `sexfaces-lazy-gui.patch` 是对上游 GPLv3 源码的修改，继续使用 GPLv3，许可全文见 `UPSTREAM-LICENSE-GPL-3.0.txt`。该文本来自 [ShalltyUtils v1.3.2](https://github.com/Lusiocc/ShalltyUtils/blob/v1.3.2/LICENSE.txt) 与 [SexFaces 固定源码](https://github.com/Sauceke/SexFaces/blob/v1.3.0/LICENSE)，二者许可文件 Git blob 均为 `f288702d2fa16d3cdf0035b15a9fcbc552cd88e7`。源码版本对应关系见 `UPSTREAM-RESEARCH.zh-CN.md`；不授予第三方 DLL 或游戏资源的再分发权。
