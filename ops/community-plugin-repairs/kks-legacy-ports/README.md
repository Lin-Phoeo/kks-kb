# 两项 KK 插件的 KKS API 适配

TimelinePOVSwitchX 1.2.0 与 KK_StudioVoice 1.3.0 的作者包引用 `KKAPI` 和旧 Unity `Texture2D.LoadImage(byte[])`。KKS 安装提供 `KKSAPI` 与 `ImageConversion.LoadImage(Texture2D, byte[])`，直接复制原包会留下缺失程序集或方法。StudioVoice 的口型补丁还选择了 KKS 不存在的 `CalcMouthOpen`，导致 Harmony 启动失败。本目录工具定向替换 API，并将口型补丁绑定到当前 KKS 的 `CalcBlend(float openValue)` 输入；原口型数值规则、GUID、配置键、资源和版本字符串保持。

作者原件与生成的修改 DLL 不随源码提供。用户应自行取得 [TimelinePOVSwitchX v1.2.0](https://github.com/AliceParadox317/TimelinePOVSwitchX/releases/tag/v1.2.0) 和 [KK_StudioVoice v1.3](https://github.com/kima789/KK_StudioVoice/releases/tag/v1.3)。自有工具/验证/说明按本目录 MIT 许可使用，不扩大第三方 DLL、游戏和 Unity 的许可。

## 精确身份

| 项目 | 作者原件 SHA-256 | 本地 KKS 候选 SHA-256 |
|---|---|---|
| TimelinePOVSwitchX 1.2.0 | `36daa827fccce2165297a0944dd4087c59ebdc02a12fa077f8739bf61eea2775` | `b96ca0ab518a98bd1039c1aeb1aace0628c937c9448aa9c2b00544bc5717f754` |
| KK_StudioVoice 1.3.0 | `1283d17f92e24d8d4b735b4d2d95c5c550b25e4b0f93ab3b36b83ae20a611df0` | `bd61e6522ae5bce52797e34069c0d427bb4fc96b0a0f38a8ba2ff70cd05b16b7` |

构建目标为 KKSAPI 1.48.1 与 Unity 2019 的 ImageConversionModule。其他依赖版本不能凭文件名视作本次相同候选；入口会因输出哈希或 API 检查不符而停止。

## 显式入口

需 Python 3.12+、.NET 10 SDK 和调用者的 Mono.Cecil/游戏引用。每项运行一次，输出必须是新的仓库外目录；工具不安装、不启动游戏、不读取角色场景、不写配置。

```powershell
py -B ops/community-plugin-repairs/kks-legacy-ports/run.py `
  --original 'E:/ReviewInputs/TimelinePOVSwitchX.dll' `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --output 'E:/BuildArtifacts/timeline-pov-kks'
```

运行会构建定向 patcher 与元数据审计器，核对输出字节、解析外部 API 和声明的 Harmony 目标，并验证未知输入、重复修补、原地覆盖和已有输出均拒绝。Voice 还拒绝不符合受检 `CalcBlend(float openValue)` 签名的游戏目标，并运行下面的实际 CIL 口型夹具；不会运行 Unity native 或完整插件。

Timeline 的 95 个方法只改变背景图加载方法中的一个调用；Voice 的 300 个方法中，指令正文只改变 `UpdateToolbarIcon` 与 `AddButtonSafe` 状态机中的两个调用。调用前的纹理/字节数组栈顺序保持，新方法是静态方法且返回同为 bool。原 API 类型与成员引用改指向 KKSAPI，不复制另一个 KKAPI 到游戏。

Voice 的 `MouthPatch` 另将 Harmony 目标名从 `CalcMouthOpen` 改成 `CalcBlend`，将原 `Postfix` 改名为 `Prefix`，并把 `ref __result` 参数改名为 `ref openValue`。当前 KKS 目标返回 void，入口参数才是原生混合所用的值；只改目标名而保留 `__result` 仍不正确。原方法正文完全保持：只有匹配角色的正口型值覆盖输入，无匹配、0 或负值保留原生输入。void 前缀不跳过原计算。

11 项检查提取实际口型补丁、闭包及当前 KKS `CalcBlend` 的 12 条 CIL，在合成角色/blend 协作者上按解析后的前缀契约分派。原件 4 项失败、当前候选 0 失败，覆盖匹配/非匹配、正值/零/负值、销毁角色、双角色独立值、移除所有权和宽度调整调用。该夹具没有安装 Harmony detour；真实运行还须用 `Harmony.GetPatchInfo` 验证准确 owner、目标、Prefix 与候选程序集。合成 FixedRate 协作者的检查也不等于真实口型画面通过。

实际安装引用解析：Timeline 279 个通过，Voice 204 个通过，均无未解析成员。独立审查重建两份候选的 SHA 一致，确认字段和内嵌资源未变；Voice 最终入口的未知输入、重复修补、原地覆盖、已有输出及目标签名变化 5 项拒绝/原件保护通过，独立重建也复现 11 项口型检查。初版端口另有已有回执拒绝的独立检查。反射名称核对、合成分派和原生行为是不同证据，不能用上述数字宣称所有功能兼容。

## 使用及验证边界

Timeline 需要 Timeline 与 PerspectiveX；作者测试版本为 PerspectiveX 1.3.4。本地 1.4.1 的核心设置属性、yaw/pitch/manualRoll、povEnabled、chara、EnablePov/DisablePov/SwitchTo/LoadViewSlot/OnCameraPreCull 的反射目标仍存在。首次试用将新配置 `[Mirror] Enable Mirror Function` 设为 false，先验收核心 POV 关键帧、停止播放恢复设置、删除目标后的行为；镜像是独立可选功能。若对应键已有用户值，先按用户决定处理，不能静默覆盖。

Voice 必须把作者包里的 `KK_StudioVoice.csv` 与候选 DLL 放在同一目录，原 CSV SHA 为 `ef34946e058c2fff5e393440998c44bf10806833267fc8f8391f4b077c9801f9`。该插件读取本机角色性格的 `abdata/sound/data/pcm/cXX/h` 与 `adm` 目录，CSV 定义音频名称；这不是现有 `StudioVoiceControl` 的升级包。作者明确未确认 KKS，且未配置 KKS 独有声音。本适配解决装配/API 缺口，不能声称全部性格/分类均有声音。只在复制角色上验收播放/停止、音量、口型及 Timeline，再决定使用范围；CSV 保持原包字节，后续本地语义补译不得改音频 ID。

安装与回退须在游戏退出后，核对同 GUID、目标是否存在和 SHA，备份原文件与配置。不要拷贝 build/audit 工具或依赖进入 plugins。安装后的新配置和用户内容由实际回执追踪；公共验证输出始终区分 `installed` 与 `runtimeVerified`。

## 参考运行记录 · 2026-11-28

第三轮空场景轨道/图标/CSV/反射与限定 UI 观察计入跨组件 53 项检查，观察器内 0 失败；但旧 `e541…` 候选的启动日志另外出现 StudioVoice `MouthPatch` 未定义 Harmony 目标错误，**该轮整体为部分通过**。这个动态目标无法由 204 项静态成员解析覆盖，旧候选不再是当前构建输出。

当前 `bd61…` 候选完成口型目标修复、独立离线验证及安装回读后，第四轮实际 Studio 空场景的跨组件 57 项检查全部通过。`Harmony.GetPatchInfo` 确认 `FBSCtrlMouth.CalcBlend(float)` 上实际附着 owner `com.kk.studiovoice.lipsync`、`KK_StudioVoice.MouthPatch.Prefix`，且方法来自准确候选程序集；三个 PerspectiveX 生命周期/相机挂钩也分别读到预期 owner/方法一次。启动检查未见原目标错误，随后进程正常关闭，临时验收器已核哈希移出。该结果证明挂钩注册与限定观察通过，实际声音播放、口型外观、Timeline 镜头切换、保存导入以及可选镜面仍未据此标为通过；历史退出控制台/临时分配信息另列未修。最终状态见[跨组件记录](../selected-installation/README.md)。
