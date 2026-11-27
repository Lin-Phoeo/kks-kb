# 验证范围与证据

当前候选 `d2016fa1…` 从准确原件经公开工具重建。原版最后的截图订阅 CIL、原回调，或候选的订阅入口、三个生命周期方法与嵌入 helper 均直接提取执行。没有用重新编写的查找公式替代原故障路径。

| 合成程序集环境 | 原版检查／失败 | 候选检查／失败 |
| --- | --- | --- |
| KKS_Screencap | 12／7 | 12／0 |
| KK_Screencap | 12／7 | 12／0 |
| Screencap（作者旧名） | 12／5 | 12／0 |
| CaptureProbe（GUID 路由／无名字回退） | 12／6 | 12／0 |

12个场景覆盖 GUID 发现、已知名字回退／可选依赖缺失、重复启用、截图期间禁用、再次启用、销毁、第二个事件添加后抛错、取消失败后的停用与重试、两个独立实例、缺少事件接口、错误委托类型以及依赖后来可用。原版在旧程序集名下能够订阅，但没有取消与重复启用管理；其失败不能笼统解释为原版任何环境都不能截图。

原版共48检查、25预期失败；候选48检查全过。未知原件、已修订输入、原地输出和已有输出4项拒绝检查保持原文件。合计100个观察结果，包含刻意保留的失败基线。

静态核查确认18个原方法中只改Awake，17个保持；Awake截图块之前的配置初始化逐指令保持。新增3个插件生命周期方法和7个helper方法，无新增AssemblyRef；插件标识与版本保持。精确参考安装的 Screenshot Manager GUID 为 `com.bepis.bepinex.screenshotmanager`，真实程序集名 `KKS_Screencap`，版本21.1.4.1，公共静态OnPreCapture/OnPostCapture为Action且均有add/remove入口。

隔离运行使用合成 BepInEx 注册表和截图事件源，生命周期由夹具显式调用；未运行 Unity 调度、角色 POV、GPU 或图片编码。实际截图插件的 `CaptureRender` 需要调用者成对执行 `FirePreCapture`／`FirePostCapture`，而原 `TakeRenderScreenshot` 协程包含完整事件、渲染与写出链。两种验收方式应明确记录，不能把只触发事件写成实际截图。

失败历史：初版嵌入器错误引入重复 BepInEx／mscorlib 引用，在输出前被拒绝；另一未安装产物因原始类型签名映射错误在实际提取用例中遭 TypeLoadException 拒绝。修正为目标模块的 primitive TypeSystem 后得到当前固定候选。保留这些失败，不以最终通过覆盖历史。

构建合成夹具会报告一个 `_captureHook` 尚未使用的编译警告：该字段在后续实际 CIL 提取时使用。没有忽略运行失败或跳过测试。当前源码验证不能代替真实安装验收，参考安装后续结果以明确候选哈希和实际观察为准。

## 2026-11-28 真实Unity与渲染API观察

受检插件SHA为 `d2016fa189a6ec3108282a47ca184ca4bcef4f6992345298b310d91237b16931`。空Studio内的一次性观察器检查真实组件的订阅和隐藏状态，并调用真实Screenshot Manager渲染API；7项全部通过：

| 检查 | 观察结果 |
| --- | --- |
| 初始订阅 | OnPreCapture／OnPostCapture各1个本候选handler |
| Unity组件禁用 | 两侧本候选handler均为0 |
| Unity组件重新启用 | 两侧各1个，无重复 |
| 截图前事件 | 原 `_forceHide` 为true |
| 实际渲染 | CaptureRender返回128×128纹理，并产生可校验PNG |
| 截图后事件 | 原 `_forceHide` 恢复false |
| 结束时渲染状态 | RenderTexture.active与调用前一致 |

PNG为128×128、8位RGBA（color type6），530字节；IHDR、IDAT、IEND的CRC全部有效，解压像素流为65,664字节。文件SHA-256为 `543b3969b999c5d272ea461a2426c0aac35ad4862340da8e669e6a96657b037e`。这证明真实渲染API产出了结构有效的图片，不证明角色HUD的视觉构图或中文裁切。

本次明确使用公共 `FirePreCapture` → `CaptureRender` → finally中的 `FirePostCapture`，不是原截图快捷键协程，也不是仅手动调用委托的合成测试。没有加载、修改或保存用户角色卡／场景，Studio对象数为0。Perceptive初始快捷键配置语义保留，BepInEx补全7项设置并调整了修饰键的序列化顺序，不能称配置字节不变；未进行实际按键验收。

进程收到2次正常窗口关闭请求后退出，没有强制结束；临时观察器已移出。运行摘要保留旧invalid handle提及10次、JobTempAlloc警告31次、ThreadAbort提及1次，不把定向7项通过写成全日志无错。其原因和修复不在本补丁结论中。

Perceptive F1／HUD中文增量是在该次运行结束后安装，本节不作为其真实绘制验收。仍未验证角色POV切换、实际身高显示、字体／位置／裁切、截图快捷键或所有第三方组合。脱敏可读回结果见 [runtime-verification.json](runtime-verification.json)；公开仅保留身份和结果指标，不附私人原日志或图片。
