# PerceptiveHeightX：KKS 截图隐藏兼容修复

PerceptiveHeightX 1.0.0 原版只按程序集名 `Screencap` 寻找截图管理器，KKS 实际使用 `KKS_Screencap`，导致 HUD 的截图隐藏事件没有挂接。本工具对准确受检的 KKS 原件制作补丁：优先从 BepInEx 已注册的 Screenshot Manager GUID 找到真实程序集，保留 KKS、KK 和作者旧程序集名的回退查找，并补齐启用、禁用和销毁时的成对事件管理。

候选只改变 `Awake` 最后的截图订阅段，其前面的配置初始化与其他17个原方法保持。新增3个生命周期方法、1个实例字段与7个嵌入 helper 方法；程序集引用不增加。无需安装额外 helper DLL。原有 HUD 计算、PerspectiveX 私有字段读取、设置键、默认值及插件 GUID／版本保持，以文件哈希识别本次修订。

| 身份 | SHA-256 |
| --- | --- |
| 唯一受检 KKS 原件 | `a7de711fae624aa1a780eb6e041c4e9c0c4b066f987b024ff52c4c2f067de35b` |
| 当前修订候选 | `d2016fa189a6ec3108282a47ca184ca4bcef4f6992345298b310d91237b16931` |

## 行为边界

截图前后必须都是公共静态 `Action` 事件，且支持添加和移除，才激活回调。添加一半失败时撤回两侧；禁用首先让旧回调失效，再分别取消订阅并恢复原隐藏状态。某个取消操作抛错时保留其记录，下次启用先重试，旧订阅未清完就不再添加。重复启用不会累加事件，没有截图插件或其接口不符时 HUD 可继续工作。

这是渲染截图的事件兼容修复。Screenshot Manager 的普通 UI 截图不触发这两个事件，不应据此宣称 UI 截图也会隐藏 HUD。此工具不改变视角、身高测量、默认快捷键、字体或翻译内容。

## 一次真实运行观察

2026-11-28，一套参考安装中的上述 `d2016fa1…` 候选完成7项定向检查：真实Unity组件启用时两侧各1个订阅、禁用后各0个、重新启用后各1个；公共截图前事件设置隐藏状态，实际 `ScreenshotManager.CaptureRender` 生成128×128画面，后事件恢复隐藏状态，调用结束时原渲染目标恢复。PNG为8位RGBA、530字节，全部chunk CRC有效。详见[运行摘要](runtime-verification.json)和[验证范围](VALIDATION.md)。

这次使用空Studio、自有观察器和公共事件包裹的实际渲染API，没有运行原截图快捷键协程，没有角色POV／HUD像素或热键验收。Perceptive中文增量在这次运行结束后才安装，因此本次结果也不证明该批HUD译文已实画。进程经两次正常窗口关闭请求退出，没有强制结束，观察器已移出；同次日志仍留有旧invalid handle、JobTempAlloc和ThreadAbort信息，未宣称修复。

## 显式构建与对照验证

需要 Windows、PowerShell 7、.NET SDK 10、系统 .NET Framework 4.x 编译器，以及自己已有的原插件、BepInEx、Mono.Cecil 和 KKS Unity 程序集。没有 PackageReference，不下载游戏依赖。先从[作者发布页](https://github.com/bani4kaskashka/PerceptiveHeightX/releases/tag/1.0.0)取得 KKS 包并核对原件哈希。

在本目录执行，输出目录必须是新的、且在源码目录外：

```powershell
./verify.ps1 `
  -InputPath '<原包>/PerceptiveHeightX.KKS.dll' `
  -CecilPath '<游戏>/BepInEx/core/Mono.Cecil.dll' `
  -BepInExPath '<游戏>/BepInEx/core/BepInEx.dll' `
  -UnityFacadePath '<游戏>/CharaStudio_Data/Managed/UnityEngine.dll' `
  -UnityCorePath '<游戏>/CharaStudio_Data/Managed/UnityEngine.CoreModule.dll' `
  -ArtifactsPath '<新外部目录>'
```

工具拒绝未知原件、已修订输入、原地写入及既有输出。构建候选必须命中固定 SHA；只生成输出，不修改原件或游戏。输出中的 `CaptureHook.dll` 是嵌入构建中间件，**不要安装它**；需要的候选只有 `PerceptiveHeightX.KKS.dll`。

四种截图程序集环境各运行12项实际 CIL 提取检查：KKS、KK、作者旧名和仅通过 GUID 才能发现的其他程序集名。原版48项中25个失败符合预期；候选48项全部通过，另有4项拒绝检查。详见 [VALIDATION.md](VALIDATION.md)。这些隔离检查没有运行 Unity 或实际截图，安装和真实功能验收另行进行。

## 安装与回退

保存工作并正常退出 Studio 和主游戏。确认只存在一个 `bucky.kk.perceptiveheightx` GUID 的插件，备份原 `PerceptiveHeightX.KKS.dll` 和其配置后，再用已核对哈希的候选替换。原来没有此插件时按新增文件记录安装路径与 SHA；不要把备份 DLL 留在插件扫描目录。

保留已有配置。首次安装默认 `Home` 用于重新记录参考身高，可能与相机缩放快捷键冲突；在 F1 中检查 `General / Relock reference height`，为新插件选择未占用组合，避免修改其他插件原快捷键。程序没有自动改键。

启动后检查独立事件订阅，使用 Unity 组件启用／禁用确认取消与重新挂接，再进入 POV 验证 HUD。真实渲染截图需要同时观察隐藏状态和产出的图片；手动调用委托只算事件检查。回退时核对当前候选哈希，恢复自己的旧 DLL／配置，或只移出本次新增文件。后续已改配置不要直接用旧副本覆盖。

## 公开范围

这里只提供自有 helper、补丁工具、提取器及合成测试，不提供原插件 DLL、完整反编译源码、游戏程序或私人运行日志。来源与许可边界见 [SOURCES.md](SOURCES.md)。
