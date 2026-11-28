# PovX 光标归属修复

KKS_PovX 1.2.1 的 `Update_Cursor` 无论自身相机是否开启，都会响应光标快捷键和左键松开。因此即使没有开启 PovX，仍可能解开其他相机插件锁定的鼠标。本地修复只在自身 POV 或自由视角有效时运行原有光标处理；其他方法、相机系统和原有快捷键逻辑保持。

作者来源：[okatodev/KKS_PovX](https://github.com/okatodev/KKS_PovX)，[latest DLL](https://github.com/okatodev/KKS_PovX/releases/download/latest/KKS_PovX.dll)。本工具只接受 SHA `b1fa80dc3147b071daf90a91ae2bff4ff1216919ab168bd56f30b976cbc1c789`。候选 SHA 为 `e73fb5eebe2913817d89981e798953f1011deafb43e8bffa34e57d46db6d115b`。GUID `OkatoDev.KKS_PovX` 和版本标识保留，区分本地修复必须看 SHA，不能只看文件名。

## 验证

需 .NET 10 SDK、Python 和调用者提供的 Mono.Cecil。入口不安装、不启动游戏、不写配置：

```powershell
py -B ops/community-plugin-repairs/povx-cursor-ownership/run.py `
  --original 'E:/ReviewInputs/KKS_PovX.dll' `
  --cecil 'E:/Games/Koikatsu Sunshine/BepInEx/core/Mono.Cecil.dll' `
  --output 'E:/BuildArtifacts/povx-cursor-review'
```

保留相邻 `perceptive-height-compatibility/tool/Clone.cs`，本工具复用该实际方法抽取器。所有输出位于新的仓库外目录。18 项直接执行实际 `Update_Cursor` 与 `ToggleCursorLock` CIL，输入和鼠标环境使用最小合成协作者。原件 6 项失败，候选全通过；无 GameCursor 与有 GameCursor 两种路径、非活动状态保留他人光标、活动模式原行为、键盘优先级均覆盖。未知输入、重复修补、原地覆盖与已有输出拒绝，原件字节保留。

独立审查在另一外部目录完整重建，候选 SHA 相同，18 项原版 6 失败/候选 0 失败和 4 项拒绝保护均复现，未发现本次改动的阻断问题。

结果只验证托管方法和状态，没有 Unity native、游戏装配加载、真实鼠标、场景切换或所有相机组合验证。完整输出的 `verification.json` 和原方法失败日志应随本地安装回执留存。

## 与 PerspectiveX 并存

首次安装建议在退出游戏并备份后，将新配置 `[Hotkeys]` 下 `PoV Toggle Key` 和 `Free-Roam Toggle Key` 设为 `None`，避免默认逗号、斜线与 PerspectiveX 操作相撞。仅补缺失设置，不能覆盖用户已自定义的按键。要使用 PovX 时先关闭 PerspectiveX POV，再给这两个入口设置独立组合键；关闭 PovX 后才切回 PerspectiveX。该补丁没有自动仲裁两个正在运行的相机控制器。

候选仅为 `candidate/KKS_PovX.dll`。安装/回退必须核对实际文件 SHA 和同 GUID 项，备份配置与原 DLL到扫描目录之外；不要复制 fixture 或 build 依赖。此目录记录生成与验证，真实安装由维护者另行记录。

自有定向补丁工具、合成测试与说明采用本目录 MIT 许可；不包含或重新许可作者 DLL、游戏、Unity、BepInEx、Mono.Cecil。作者原件和修改 DLL 不随公共源码发布。

## 参考运行记录 · 2026-11-28

准确候选已在参考环境另行安装并回读 SHA，入口本身仍不安装。第三轮真实 Studio 空场景观察了未启用 PovX 时调用光标更新前后的 Unity/GameCursor 状态，保留检查通过，计入跨组件 53 项限定检查；第四轮跨组件 57 项检查中再次通过。未模拟物理快捷键或鼠标输入，也未同时启用多个镜头控制器。第三轮其他组件的口型启动错误保留历史，第四轮已确认修订后挂钩实际安装；分层结果统一维护在[跨组件记录](../selected-installation/README.md)。
