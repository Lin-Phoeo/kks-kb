# RopeThis 与 BlendshapeCreator：已核基础界面增量

这批只处理实际已核的 F1 设置和常用窗口文本，不将所有插件功能或所有状态文字标成完成。StudioBustAdjuster 使用原生工作室控件，不能套用 IMGUI owner 词表；其显示适配单独处理。

| 插件 | 已核内容 | 匹配身份 |
|---|---|---|
| GIN Rope This 1.1.3 | 1 个 F1 设置；32 条窗口/状态原文；5 个数值标签规则 | GUID `local.kkworkshop.0a52b20541f5.gin-rope-this`；owner `GIN Rope This` |
| BlendshapeCreator 1.1 KKS | 8 个 F1 设置；37 条主窗口/提示原文 | GUID `com.shallty.blendshapecreator`；owner `KKS_BlendshapeCreator` |

F1 增量合计22条，含中英标题、分类、设置名称和说明。`imgui-additions.json` 含69条精确规则和5条仅限 Rope owner 的数字规则；数字原值完整保留，没有改模型名称、配置值、快捷键、保存键或轨道 ID。

两个 `Text/Plugins/<owner>/0000_ReviewedCandidates.zh-CN.txt` 各包含对应精确译文，不能省略：现有 XUnity 核心先读取非空插件词表建立 owner 缓存，再路由对应 OnGUI 和窗口回调。只放动态 JSON 不能证明新 owner 已注册。

## 现有译文复核

检查现有1061个文本文件和3个压缩译文包内5247份文本，未找到这两个程序集的完整专属词表；已有命中主要是通用短词和其他插件上下文。比如通用 `Toggle panel` 已有“切换表情面板”，不适合 Rope，因此这批使用 GUID 限定的“切换绳索面板”。BSC 只保留通用词的正确语义，在自己的 owner 中明确限定。

69条精确原文逐项对应实际已装 DLL 的方法字串；Rope 数字规则对应 `Slider` 的固定 `label + ": " + value.ToString("0.00")` 格式。调用实际已装 `TranslationCatalog.TryGet`、`RuleMatcher.TryTranslate` 和 XUnity 编码/解析方法完成178项检查，包括错误 owner 和多行文本不能误命中。结果摘要见 [probe-summary.json](probe-summary.json)。

## 校对要点与剩余边界

- Rope 的 Remove 恢复物品但保留 A/B 手柄，译文明确保留约束用途；没有写成删除整个对象。
- Rope 的5个动态数字规则只翻译“长度、重力、阻尼、弯曲刚度、求解质量”，不改格式化数值。对象名、活动数量、其他动态状态和异常消息未在本批批量套规则。
- BSC `Inverse X` 实际用于导入，`Bake mesh` 用于按当前姿势导出；二者不混淆。
- BSC Studio 窗口明确说本窗口滑块值不保存，要使用 KKPE 滑块并可接 Timeline；译文以实际 DLL 为准。旧帮助段落中与当前 `.bscd` 导入流程不完全一致的 OBJ 说明未机械翻译为新功能承诺。
- BSC 原生文件选择器、完整 Blender 配套流程、TextureBaker 子窗体和其他动态输出尚未全部校对。
- 字号继承现有字体控制流程，本增量不创建更小的独立字号。Rope 默认窗口宽440，BSC 使用其原皮肤；中文后的裁切/换行仍需真实窗口观察。
- Rope 可在空 Studio 显示面板；BSC 虽有开关，Studio OnGUI 仍要求选中对象，纯空场景不能证明其窗口 owner 已绘制。未通过虚构对象绕过该条件。

## 合并边界

使用已有社区事务分别合并两份 JSON 并安装两份专属 TXT，先备份、拒绝同身份冲突，保持 rc.2 原安装器回执和回滚。预检要求这两个精确插件身份：

| DLL | SHA-256 |
|---|---|
| GIN Rope This.dll | `6554f04badf59acf97441f3070b6a119c6f31b0eb3e984e5baa39b87a7f94a17` |
| KKS_BlendshapeCreator.dll | `c05bffae6400a730d1ae3a1d7a2476ce78bf254afa6cec4f92714c1be0711fb9` |

目标仅为 `Codex.StudioConfigLocalization.json`、`Codex.StudioImGuiDynamicBridge.json` 和对应两个插件目录下的 TXT。不要覆盖完整词表，也不改其他插件的同名短词。部署和真实窗口证据见下节，不据文件合并推断功能通过。

## 一次真实启动与作用域观察

2026-11-28，参考安装中Rope This、KKS BlendshapeCreator、Studio Bust Adjuster与PerceptiveHeightX四个插件均在空Studio加载。Rope空面板实际打开，专属翻译hook在真实绘制路径被观察到，补充了此前仅解析词表和执行匹配器的证据；没有据此把所有Rope状态、中文像素宽度或绳索功能标为通过。

BSC没有选中对象，真实窗口未绘制，因此其窗口owner绘制、文字裁切和完整创作流程仍未验收；8项既有配置值语义保持。Bust只完成隐藏原生组件的13处中文读回，Perceptive截图兼容完成真实渲染API检查；这两项分别由自己的适配记录负责，不能混作本词表覆盖结果。Perceptive的中文增量在此次运行后才安装。

Studio对象数为0，未读写用户角色卡／场景，没有新增用户手工测试。进程收到2次正常窗口关闭请求后退出，未强制结束，临时观察器已移出；旧invalid handle、JobTempAlloc、ThreadAbort信息仍存在。统一范围见[候选运行记录](../README.md#2026-11-28-空studio定向观察)及其链接的脱敏摘要。

## 预检、安装与回滚

需要Python3.10或更新版本、已准备的[Studio简中0.3.0-rc.2基础包](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)，以及本仓库完整目录。入口复用相邻 `../../localization/community_zh.py`，请勿只复制一个Python文件。它还会检查基础辅助DLL、ConfigurationManager和修订XUnity Core的已审哈希；不会自动安装任何插件DLL。

进入本目录后，先运行只读预检。以下路径仅为示例，请换成自己的路径：

```powershell
$Package = 'E:/Downloads/KKS-Studio-zh-CN-0.3.0-rc.2'
$Game = 'E:/Games/Koikatsu Sunshine'
py -B reviewed_zh.py --localization-package $Package --game $Game
```

输出应只列上文四个目标。默认doctor不会向游戏目录写文件；临时来源目录检查结束后清理。两个专属TXT虽然同名，入口会给**临时来源**加不同编号，目标名字和作用域保持，从而避免临时复制时互相覆盖。

确认预检通过，保存工作并正常退出Studio及主游戏，再实际安装：

```powershell
py -B reviewed_zh.py --localization-package $Package --game $Game --apply
```

`--apply`通过原rc.2安装事务执行：检查游戏进程、取得安装锁、复核预检SHA、备份原文件、按身份合并、校验写入结果并生成回执。两个JSON保留已有其他插件和用户译文；同身份异值拒绝。TXT同名异内容和同owner更高优先级的竞争词表会停止，不能为通过检查删除既有词表。并发改动保留，重复安装已完成的增量为无操作。

插件配置、快捷键、DLL和资源不在此工具写入范围。原rc.2安装器可能补齐基础翻译器的作用域设置，会记录并备份；服务端点及字体偏好保持。成功返回的`receipt`是本次回退入口，请保留整个备份子目录。

回退前同样保存并退出游戏，使用原rc.2安装器：

```powershell
$Receipt = 'E:/Games/Koikatsu Sunshine/.studio-localization/backups/<本次编号>/receipt.json'
py -B "$Package/studio_zh.py" rollback --receipt $Receipt
py -B "$Package/studio_zh.py" rollback --receipt $Receipt --apply
```

只恢复该回执内的文件；若安装后又改过词表，原事务会拒绝覆盖，需要先核对后续改动。不要删除整个Translation目录或生成的插件数据。

## 适配器验证

```powershell
py -B test_reviewed_zh.py --localization-package $Package -v
```

9项适配测试仅使用临时合成安装：四个精确目标、两份同名TXT不同内容保持、其他译文保留、DLL/cfg/mod不变、原事务回滚、重复执行、两个插件分别错误哈希拒绝、F1/动态冲突、两作用域TXT冲突、竞争词表、并发修改和源/目标路径逃逸。测试不读取真实游戏文件或进程。

独立复核另有177项源码/owner和实际匹配器检查全部通过：22个F1身份、69个窗口原文、69次精确匹配、15个数字样例与2个错误owner反例。两scope文件与前述实际XUnity解析的69条内容逐项相同。它们补充已有178项检查，不将两个数字混成一套用例。真实doctor已在受检安装中通过，仅计划四文件、配置变更为0；尚不等于界面展开、字体和所有插件功能实测通过。
