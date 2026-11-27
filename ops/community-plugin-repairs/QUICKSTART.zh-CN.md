# 社区插件修复与中文增量：使用步骤

本工具集帮助使用者从自己的官方插件原件生成精确兼容修订，并为已审版本追加中文。它与[Studio简中0.3.0-rc.2](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)基础包配合使用。基础包负责已有工作室界面、字体控制及翻译引擎；本目录处理指定社区插件的后续修复和词表。

工具集没有游戏本体、第三方插件DLL、人物卡、场景或系统字体。每项教程链接到作者来源，并给出准确输入身份。应使用KKS发行包，不能凭相同文件名把KK或HS2包混入。

## 先准备一次

1. 保存需要保留的编辑，正常退出工作室与游戏。确认相关进程已结束。
2. 将当前将被修改的DLL和cfg备份到游戏插件扫描目录之外，记录SHA-256。不要把旧DLL改名后继续留在plugins子目录。
3. 解压本仓库源码或工具集，保留完整目录结构。下载并解压上面的rc.2基础包，保留其原始 `studio_zh.py` 与 `manifest.json`。不要混用另一个发行版的安装器。
4. 运行Python工具需要Python3.10或更新版本；编译修复候选另按对应README准备PowerShell、.NET SDK和使用者自己的Mono.Cecil/游戏依赖。源码验证输出必须指向新的独立目录。

可用以下命令核文件身份：

```powershell
Get-FileHash -LiteralPath '实际文件路径' -Algorithm SHA256
```

## 按实际需求选入口

| 需求 | 教程 | 操作结果 |
|---|---|---|
| 裙链工具恢复时影响其他节点 | [ManageableSkirts](manageable-skirts/README.md) | 从精确原件生成修复插件和配套状态helper |
| FBX导出失败、贴图/临时渲染状态不恢复 | [Grey](grey-mesh-exporter/README.md) | 从精确原件生成候选；使用者另备官方x64原生库 |
| DragDoll错误识别基础移动轴 | [DragDoll](dragdoll-compatibility/README.md) | 仅精确基础版本跳过Plus专用桥 |
| Vanity新PseudoMaker保护未注册 | [Vanity](vanity-pseudomaker/README.md) | 两组件各增加一个精确版本门控 |
| AssimpNetter类型加载缺程序集 | [AssetImport](assetimport-dependencies/README.md) | 局部安装两个准确官方依赖，不替全局框架 |
| 场景导入后Clo物件状态恢复错对象 | [CloXray](cloxray-import/README.md) | 候选按本次Import对象映射恢复 |
| PerspectiveX升级并补新设置中文 | [PerspectiveX](perspectivex-update/README.md) | 作者DLL升级步骤与独立译文事务 |
| 已审社区插件F1及Grey窗口中文 | [社区中文](localization/README.md) | 合并限定插件身份的词表，保留已有内容 |
| CloXray1.1.6新设置中文 | [Clo中文](cloxray-import/localization/README.md) | 25条增量与14条基础译文共同覆盖当前39显示身份 |
| 可选创作插件选型和用途边界 | [新候选说明](reviewed-candidates/README.md) | Rope、BlendshapeCreator与Bust的来源、实际用途及数据保持边界 |
| Rope与BlendshapeCreator窗口中文 | [两插件中文](reviewed-candidates/localization/README.md) | 22条F1、74条窗口规则与两个专属作用域文件 |
| Bust原生侧栏标题和滑杆中文 | [原生控件适配](bust-native-localization/README.md) | 创建控件时改13处可见文字，保留原节点和保存身份 |
| PerceptiveHeightX截图隐藏失效 | [截图兼容修复](perceptive-height-compatibility/README.md) | 精确候选正确定位截图插件并成对管理事件 |
| PerceptiveHeightX设置与HUD中文 | [身高HUD中文](perceptive-height-compatibility/localization/README.md) | 19条F1与限定owner的HUD规则，人物名与数字保持 |

每项修复工具都先验证准确原件，不是任意版本的自动更新器。运行 `verify.ps1` 生成候选后，按照该项README核对候选哈希和修改范围，再执行独立的游戏安装步骤。源码编译成功不表示已经替换游戏文件。

## 中文增量的通用流程

进入所选词表目录，先用其CLI做doctor。以社区中文为例：

```powershell
$Package = 'E:/Downloads/KKS-Studio-zh-CN-0.3.0-rc.2'
$Game = 'E:/Games/Koikatsu Sunshine'
py -B community_zh.py --localization-package $Package --game $Game
```

确认依赖身份和计划文件正确，游戏已退出后，加 `--apply`：

```powershell
py -B community_zh.py --localization-package $Package --game $Game --apply
```

默认doctor不写游戏；`--apply`才调用同一安装事务，执行进程检查、锁、原件备份、写入前SHA复核和回执生成。重复安装为无操作。同一运行时词条身份存在不同译文时会停止，需先校对，不靠加载顺序覆盖。F1显示名称和保存用英文键是不同内容，翻译不会改键值、快捷键或用户命名。

保存输出里的 `receipt` 路径及整个备份子目录。回退时先关闭游戏：

```powershell
$Receipt = 'E:/Games/Koikatsu Sunshine/.studio-localization/backups/本次编号/receipt.json'
py -B "$Package/studio_zh.py" rollback --receipt $Receipt
py -B "$Package/studio_zh.py" rollback --receipt $Receipt --apply
```

如果文件安装后又被修改，回退会拒绝覆盖；保留新内容，按各次回执比较后再处理。插件DLL修复、作者版本升级与词表回退各有自己的边界，不要用一个词表回执推断可以回退全部插件。

## 核对结果

先检查空工作室能否加载与正常退出，再检查需要用到的插件窗口。涉及角色、骨骼或场景保存的工具，使用独立副本做功能检查。Rope、Bust、Blendshape等编辑结果可能保存在其他插件的角色/场景扩展数据里，移出DLL不会自动撤销已经保存的编辑。

各README和验证记录会区分：准确来源与静态依赖、提取实际方法的合成测试、真实空启动、真实功能操作。Grey已记录普通自造立方体的FBX/PNG导出；这不能外推到所有角色、蒙皮或cloth。长输出路径、控制台退出遗留问题及完整游戏组合的未验范围仍明确保留。

其他AI接续可直接阅读 [AI-HANDOFF.md](AI-HANDOFF.md)。版本、哈希及操作细节以对应目录源码与来源说明为准，避免复制过时的完整游戏安装清单。
