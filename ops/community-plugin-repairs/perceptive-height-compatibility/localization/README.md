# PerceptiveHeightX 中文显示增量

用于本目录上一级的准确修订候选 `d2016fa1…`，补充 **19条F1译文、2种HUD动态文本和1条插件名译文**。没有安装新的插件 DLL，也不修改身高算法、缩放、快捷键、显示位置、单位保存值或字体设置。全部译文按实际受检程序集核对，未找到可直接复用的既有 PerceptiveHeightX 专属词条。

## 译文含义

| 显示 | 中文含义 |
| --- | --- |
| `POV: 180.0 cm` → `基准: 180.0 cm` | 第一人称视角角色记住的参考身高，并非始终等于角色当前实际身高。 |
| `Alice to POV: 157.5 cm` → `Alice 看似: 157.5 cm` | 该视角角色眼中 Alice 的感知身高。实际算法仍为另一角色实际身高×参考身高÷视角角色实际身高。 |
| `Centimeters`／`Feet Inches` | F1显示为“厘米”／“英尺与英寸”，保存值仍为原枚举身份。 |

人物名字、数字、小数点／逗号和 `cm`／英尺英寸符号原样保留。HUD采用短标签，减少原插件先计算文字尺寸再绘制时的长度变化；实际字体宽度与裁切仍需在游戏里看。没有翻译日志输出并冒充新增界面。

F1覆盖7项真实Bind设置的名称与说明、2个分类、1个中英对照标题及2个单位枚举。描述区分“本次运行保持参考值”和“每次进入视角重设”。位置说明按实际可移动范围解释，不声称修改人物尺寸。

## 安装

需要 Python3.10+、完整仓库和已解压的 [Studio 简中 rc.2 基础包](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)。保留相邻公共工具 `../../localization/community_zh.py`；本入口使用独立模块实例复用其准确依赖检查、冲突判断及 rc.2 安装事务。

额外插件依赖仅接受 `PerceptiveHeightX.KKS.dll` SHA-256 `d2016fa189a6ec3108282a47ca184ca4bcef4f6992345298b310d91237b16931`。原版或其他版本停止预检，先按上一级说明处理截图兼容候选。此工具不替换 DLL。

进入本目录后执行以下示例，改为自己的路径：

```powershell
$Package = 'E:/Downloads/KKS-Studio-zh-CN-0.3.0-rc.2'
$Game = 'E:/Games/Koikatsu Sunshine'
py -B perceptive_zh.py --localization-package $Package --game $Game
```

默认doctor只读游戏目录。保存工作并正常退出游戏后，再执行实际安装：

```powershell
py -B perceptive_zh.py --localization-package $Package --game $Game --apply
```

只合并两个显示目录JSON，并安装一份 `Text/Plugins/PerceptiveHeightX.KKS/0000_PerceptiveHeight.zh-CN.txt`。该非空TXT包含实际插件名的精确中英映射，用于建立此程序集的独立XUnity缓存；2条带人物名／数值的HUD规则仅在动态JSON中生效。程序集owner包含 `.KKS`，不能缩短为 `PerceptiveHeightX` 或改为 `global`。

按完整运行时身份拒绝异值冲突，保留其他插件和后续自定义译文；同名TXT有其他内容时停止。原安装器会先备份，拒绝预检后的并发改动，并输出可回滚receipt。重复应用已安装内容为无操作。必要的XUnity作用域配置由基础安装器处理和备份；PerceptiveHeightX自己的cfg与单位/快捷键不写入。

回滚前退出游戏，使用本次返回的receipt：

```powershell
$Receipt = 'E:/Games/Koikatsu Sunshine/.studio-localization/backups/<本次编号>/receipt.json'
py -B "$Package/studio_zh.py" rollback --receipt $Receipt
py -B "$Package/studio_zh.py" rollback --receipt $Receipt --apply
```

## 验证

8项安装回归使用临时合成游戏目录，覆盖doctor只读、跨作用域保留、插件DLL/cfg原样、准确TXT写入、回滚、重复执行、错误候选、两类目录冲突、TXT冲突和路径逃逸：

```powershell
py -B test_perceptive_zh.py --localization-package $Package -v
```

另有63项实际 TranslationCatalog／RuleMatcher 方法检查，包括19条F1查找、错误GUID/分类、两种单位格式、中文与含特殊字符的人物名、不匹配文本和错误owner拒绝。对使用者已有程序集显式复跑：

```powershell
dotnet build tests/Verify.csproj -c Release --artifacts-path '<外部输出>/build'
dotnet '<外部输出>/build/bin/Verify/release/Verify.dll' `
  "$Game/BepInEx/plugins/Codex.StudioConfigLocalization.dll" `
  "$Game/BepInEx/plugins/Codex.StudioConfigLocalization.json" `
  catalog-additions.json `
  "$Game/BepInEx/plugins/Codex.StudioImGuiDynamicBridge.dll" `
  imgui-additions.json '<外部输出>/result.json'
```

此命令只在内存合并现有目录，不修改游戏。作用域TXT另经真实XUnity编码／解析器往返核对，解析owner与实际程序集一致。独立复核重新读取当前候选与ConfigurationManager枚举显示，另执行32项实际目录／匹配器检查及8项安装回归，均通过。以上不证明真实OnGUI路由、文字像素宽度、人物POV或截图隐藏；需要启动后分别检查F1、HUD和截图。本工具仍输出 `runtime_verified: false`。

依据：[作者1.0.0说明](https://github.com/bani4kaskashka/PerceptiveHeightX/blob/1.0.0/README.md)、准确修订候选中未改变的7个Config.Bind／Update／FormatLength／OnGUI，以及受检ConfigurationManager的枚举ToProperCase显示。初始检查在1.0.0原件完成，修订只改截图订阅段，所有这些文本身份保持。
