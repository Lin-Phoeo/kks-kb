# PerspectiveX 1.4.1 升级与中文增量

本目录用于把已审的 PerspectiveX 1.4.1 设置显示为中文，并保留原有三种预设、三个视角槽位和说明的翻译。它提供独立编写的 **46条F1译文与4条动态提示**，以及带预检、备份和回滚的增量入口。第三方插件DLL、游戏文件、人物卡、场景和用户预设不包含在仓库中。

## 版本变化

从1.3.4升级到1.4.1不只是VR修补。1.4.0加入右键平移镜头、相对躯干观察、扩大俯仰范围，以及VR支持；1.4.1再修复VR退出后重新进入、移动后返回位置不正确及映射快捷键的问题。新版也修正与其他头部隐藏工具共存时的renderer状态接管。

首次升级可保持原操作习惯：保留所有已有配置，只在对应键**原先不存在**时，将 `Comfort / Right-drag moves the camera` 和 `VR / Use POV in VR (Studio)` 设为 `false`。以后可在F1开启。若这些键已有用户值，不覆盖；其他36项旧配置、快捷键、视角槽位和自定义预设都保留。

“相对身体观察”作者默认关闭。新近裁剪面默认值只影响新配置，不必改已有值。右键平移是临时状态，退出或换角色会重置；原六字段视角槽位格式没有加入这项平移，不能理解为所有新位移都能随槽位保存。

## 获取作者插件并备份

下载作者 [v1.4.1 KKS发行包](https://github.com/bani4kaskashka/KK_PerspectiveX/releases/download/v1.4.1/KKS_PerspectiveX_v1.4.1.zip)，同时阅读[发行说明](https://github.com/bani4kaskashka/KK_PerspectiveX/releases/tag/v1.4.1)。详细来源见 [SOURCES.md](SOURCES.md)。准确身份如下：

| 文件 | SHA-256 |
|---|---|
| 官方 `KKS_PerspectiveX_v1.4.1.zip` | `285fa1173b94d5198a770866e3e396b4f777064dbcf1612ea68a493b91961bb8` |
| 包内 `KKS_PerspectiveX.dll` 1.4.1 | `bfca7b2371b598a6908b3ded8d6cb8c02fc8b6e65b68465b40239798657f9e7c` |
| 本次比对的旧1.3.4 DLL | `2caac417509dfc0799f76afe959db4b171597d5b5cf8483191c9fe91d6b6ba0d` |

PowerShell可用 `Get-FileHash -LiteralPath '实际文件路径' -Algorithm SHA256` 核对。旧文件不同不表示损坏，但不属于本次已比对的基线，应先重新确认来源。

1. 保存工作，正常退出Studio与主游戏。
2. 将现有 `BepInEx/plugins/KKS_PerspectiveX.dll` 和 `BepInEx/config/bucky.kk.perspectivex.cfg` 备份到**插件扫描目录之外**，记录两者SHA。不要把旧DLL副本放在plugins子目录。
3. 用官方包内的KKS DLL替换对应旧文件，保留原cfg。避免同时留下KK与KKS两个相同GUID的DLL；RealPOV与PerspectiveX有明确互斥，不能一起启用。
4. 如需保持桌面旧操作，在已有对应配置节补上上文两个缺失键的 `false`，不要重复添加配置节，也不要覆盖已有用户值。翻译安装工具不会修改这些插件设置。

DLL升级是本步骤的明确手工操作；下面的Python入口**只处理译文**，不会替换DLL或自动开关新功能。

## 安装中文增量

需要Python3.10或更新版本、已安装的[Studio简中0.3.0-rc.2基础包](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)，以及本仓库完整目录。保留相邻的 `../localization/community_zh.py`：本入口复用它的冲突检查和rc.2事务，不支持只复制单个Python文件使用。

基础包应已建立 `Text/Plugins/KK_PerspectiveX/` 的插件作用域。入口接受rc.2清单词表，或已审的早期PerspectiveX专属28行词表，均核对准确路径和SHA；后者SHA为 `43eb5aa24e06c42167f3da38827695a31656353da4bbbcf9ac822a4764c41e36`。这样可接续先前已经验收的安装，而无需为文件名变化覆盖译文。空文件、纯注释或改写的基线仍被拒绝；自定义补译请保留在独立文件，不覆盖基础词表。辅助DLL、ConfigurationManager与XUnity Core使用已审准确哈希；本入口额外要求上述**官方1.4.1 DLL哈希**，其他版本先停止。基于实际程序集名的翻译owner是 `KK_PerspectiveX`，不能按DLL文件名前缀把它改成 `KKS_PerspectiveX`。

以下为示例路径，先进入本目录：

```powershell
$Package = 'E:/Downloads/KKS-Studio-zh-CN-0.3.0-rc.2'
$Game = 'E:/Games/Koikatsu Sunshine'
py -B perspectivex_zh.py --localization-package $Package --game $Game
```

默认只预检，游戏目录无写入。通过后执行：

```powershell
py -B perspectivex_zh.py --localization-package $Package --game $Game --apply
```

`--apply`复用rc.2安装器的进程检查、锁、写前SHA检查、原件备份、合并与回执。只向两个显示目录JSON增量加入词条，保留其他插件和用户追加的译文。相同运行时身份存在不同译值时停止，不选一个覆盖；并发修改时保留新修改并停止。重复执行已完成的增量为无操作。基础译文作用域所需的翻译器设置由rc.2安装器处理并备份，字体偏好、服务端点及PerspectiveX自己的配置不由该增量修改。

成功输出中的 `receipt` 用于回退译文，请保存完整备份目录。操作日志仍写 `runtime_verified: false`，不会把文件复制等同于真实相机功能通过。

## 覆盖范围

| 内容 | 增量 |
|---|---:|
| F1中英对照插件标题 | 1 |
| VR与控制器映射类别 | 2 |
| 新设置名称 | 20 |
| 新说明及3处旧说明改文 | 23 |
| KKS自绘“启用／停用／缺VR插件”提示 | 4 |

所有F1条目以原GUID、分类、设置键与原文定位；动态4条仅归属 `KK_PerspectiveX`，没有全局替换。设置保存值、英文配置键、用户预设名称均保持。VR控制器下拉中的键名和按钮标识保留原文，本目录没有加入1166条枚举展开映射，不能宣称这些下拉也已全部中文。

现有 `Cozy (60)`、`Natural (90)`、`Action (110)` 及槽位/底部说明仍使用基础包的规则。检查过的39个样例全部命中；相关绘制与视角保存/载入方法的实际IL不变。来源与验证边界见 [VALIDATION.md](VALIDATION.md)。

## 验收与回退

重新进入Studio，核对版本、F1搜索框、字体、三种预设、槽位及新增设置中文。角色POV应在新建测试角色上检查进入/退出、左拖、滚轮、相机锁、切换角色和鼠标释放；需要时再主动开启右键平移验证。桌面检查不能证明头显体验，旧视角数据和用户场景不用于破坏性试验。

只回退译文时，先退出游戏，再用同一rc.2安装器：

```powershell
$Receipt = 'E:/Games/Koikatsu Sunshine/.studio-localization/backups/<本次编号>/receipt.json'
py -B "$Package/studio_zh.py" rollback --receipt $Receipt
py -B "$Package/studio_zh.py" rollback --receipt $Receipt --apply
```

DLL回退与译文回退分开：核当前DLL仍是本次1.4.1后，恢复自己的旧版备份。配置若后来修改过，不用旧备份抹掉新预设；保留新编辑并逐项核对。回执也会拒绝覆盖安装后已变动的译文文件。

## 可复跑检查

```powershell
py -B test_perspectivex_zh.py --localization-package $Package -v
```

12项测试只使用临时合成安装，涵盖默认无写入、已有译文和其他owner保留、DLL/cfg不变、备份回退、重复执行、错误版本拒绝、基础作用域缺失或空/注释/改写内容、已审早期词表接续、两类冲突、并发修改和路径逃逸。测试替换合成依赖哈希和进程检测，不访问真实游戏。
