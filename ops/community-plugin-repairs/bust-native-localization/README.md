# StudioBustAdjuster：保留控件身份的原生中文显示

StudioBustAdjuster 的标题和12个滑杆由 KKAPI 创建为原生工作室控件。它没有自己的 OnGUI，也没有 F1 设置；给插件放 IMGUI owner 词表不能保证这些文字被翻译。直接改构造字符串还会改变 KKAPI 的类别名称及生成的 UI 节点名，因此本实现保留原插件文件，在控件创建完成时只改 `Text.text` 或 `TMP_Text.text`。

## 精确范围

新增自有 `Codex.StudioBustNativeText.dll`，只挂两个 KKAPI 创建完成回调：

- `CurrentStateCategory.CreateCategory(GameObject)`：仅处理原名 `Studio Bust Adjuster`，定位其直属 `Studio Bust Adjuster_Header_SAPI` 标题。
- `CurrentStateCategorySlider.CreateItem(GameObject)`：仅处理 `Studio Bust Adjuster_Items_SAPI` 下的12个已核原标签，并核对原 `CustomSlider …`、`Text …` 节点名。

普通启动时先登记控件、稍后生成界面的路径，与 Studio 已加载后新增控件的路径均被覆盖。不设 `Update`、不搜索全场景、不改字体大小，也不改类别名、控件 Name、任何 GameObject 名称、滑杆值、Pushup/ABMX 数据或 Timeline 轨道 ID。原作者 DLL 和 KKAPI 均为零字节变化，未新增其程序集引用。

`NativeTextPolicy.cs` 是显示映射的唯一来源。标题为“胸部调节 / Studio Bust Adjuster”；字段按实际 Pushup 数据校准，例如 `Pointiness` 对应 Depth，译为“前凸程度”，`Shape` 对应 Roundness，译为“圆润度”，`Nipple Circumference` 实际写 NippleWidth，译为“乳头宽度”。原文的三个缩进空格保留在显示译文中。

## 身份门与构建

构建前和真实启动时均要求下列精确文件，否则停止，不挂钩子：

| 输入 | SHA-256 |
|---|---|
| KKS_StudioBustAdjuster.dll 1.0.1 | `a07e33d8768b6a1540aee3b727cf3ad43cd62335fc3ecad04f6256e5ac53098f` |
| KKSAPI.dll 1.48.1 | `866464809d1bd04450a35850c23f9d17a0d1e5a166ed9f5dc525c506fc04ef01` |
| 已核 helper 输出 | `9903e7226f4a3521ba7411634822968c03e3a9f309b32d5e6937e024011ba9eb` |

需要 Windows、PowerShell7、.NET SDK10、本机 .NET Framework4 引用和使用者已有的游戏依赖。用 SDK 的 Roslyn 编译器以 C#5/.NET Framework 引用生成确定性程序集，不升级或下载游戏 DLL。输出必须是源码目录之外的新目录：

```powershell
./verify.ps1 -GameRoot '<installation>' -ArtifactsPath '<new-external-artifacts-directory>'
```

只构建可用 `build.ps1`，参数相同。已核环境中从公开源码两次独立构建得到相同的上述 helper 哈希。更换编译器后仍应复核输出身份；不得因版本号相同就直接替换已审文件。

## 验证边界

68项合成检查编译并执行与 helper 共用的源文件，使用独立的 Unity/KKAPI 组件替身和 Harmony 调用记录器。覆盖13处文字、全部类别/节点/控件身份、字体属性不变，延迟生成控件、其他类别和未知标签不命中、重复回调不抢回文字、已有中文不认领、其他翻译器后改文字不覆盖、销毁目标跳过、重复销毁无副作用以及未知哈希不挂钩子。实际依赖另有70个成员引用全部解析通过。

这是自有回调逻辑和实际依赖签名验证，没有把替身视为真实 Unity 画面。后续真实启动及组件字段读回见下节；字形和排版仍需可见界面验收。没有选中角色、改胸部数值或保存原场景的测试。字体由现有工作室字体流程处理，本 helper 保持已有字体属性。

`OnDestroy` 只撤销本 helper 自己的两个 postfix。每个改过的组件通过弱引用记录原文，且只在当前文字仍等于本 helper 写入的译文时还原；已被其他翻译器后来更改的文字保留。组件 `enabled=false` 不等于卸载，本版不宣称该操作会撤钩子。

## 一次真实原生组件读回

2026-11-28，参考安装的 `9903e722…` helper与准确原版BustAdjuster1.0.1在空Studio内加载，观察器读回中文标题和12个中文滑杆标签，共13处原生文字，原节点身份保持。12个滑杆Text的字号为14，读回时均为 `active_in_hierarchy=false`。这是实际创建组件后的字段证据，没有把隐藏控件称为肉眼可见或交互验收；字形、裁切、布局和滑杆操作仍未验证。[运行摘要](runtime-verification.json)保存各标签与边界。

该次运行没有选中或编辑角色，没有胸部数值、Pushup／ABMX／Timeline修改或场景保存。4个待验插件在同次空Studio加载；进程经过2次正常窗口关闭请求退出，未强制结束，临时观察器已移出。原有invalid handle提及10次、JobTempAlloc警告31次、ThreadAbort提及1次仍保留，不能将本次文字读回解释为这些旧问题已解决。

## 部署与回退

生成和验证不会写入游戏目录。退出 Studio 后核对原两项输入与候选哈希，将候选单独放入 `BepInEx/plugins/Codex.StudioBustNativeText.dll`，记录部署回执；已存在同名文件时先备份并核身份，不覆盖未知版本。

回退时退出 Studio，只把本次安装且仍匹配回执的 helper 移出插件扫描目录；若此前已有该 helper，则恢复其备份。原 StudioBustAdjuster、KKAPI、配置及场景数据无需替换。运行时验证至少应确认 `BUST_NATIVE_TEXT_READY labels=13`、13项显示和原节点名，再按原方式正常退出；空场景观测不证明胸部功能或 Timeline 行为。

上游：[Vaaskr/StudioBustAdjuster](https://github.com/Vaaskr/StudioBustAdjuster)、[IllusionModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI)。本目录仅发布自有适配器、映射和合成测试，不包含原插件、KKAPI、完整反编译类或游戏资源。
