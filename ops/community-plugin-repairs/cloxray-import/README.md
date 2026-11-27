# CloXray：导入场景时仅恢复本次导入物件

CloXray KKS 1.1.6 保存物件状态后，导入场景时把源场景的物件 ID 用于查询全局对象表。假设当前场景已有 ID 10，导入源也包含 ID 10、导入后实际分配 ID 25，原版会把导入源的状态恢复给已有 ID 10，并可能漏掉 ID 25。KKAPI 已提供按源 ID 映射的新对象表，原版新增的两组保存字段没有使用它。

本补丁仅在 `Import` 时复制 `loadedItems`，让原恢复协程用本次导入的对象快照查询和枚举。重叠导入各自拥有独立快照；空映射或缺失 ID 不会回到全局对象表。普通 `Load` 继续使用原来的延迟全局查询，`Clear`、保存格式和渲染算法保持原样。代码中的字段名、类型名用于精确识别第三方插件的数据结构；本修复处理对象身份与保存数据恢复，不添加画面或内容功能。

本目录只包含自有补丁器、合成测试和验证脚本。没有原 DLL、候选 DLL、完整反编译类、游戏资源、角色、场景或本机安装路径。

## 支持的精确输入

从作者 [CloXray v1.1.6 发布页](https://github.com/Clo15/CloXray/releases/tag/v1.1.6)取得 **KKS_CloXray.zip**，在游戏安装目录之外解压。不要把 Koikatsu 的 KK 包作为输入。

| 文件 | SHA-256 |
|---|---|
| 官方 KKS_CloXray.zip | `5624893ae5094eb9fbf28cb22fe84244a9f855b699ac19cf34a5c90c93462d39` |
| 包内 BepInEx/plugins/Clo/LiquidWobbleMPB.dll | `69c953c7ed5a421db28081f6db0b7029fa60c12aaf48433ecfd07da3cf554c98` |
| 本补丁候选 | `74612e01c4d2278aca4b4d993dc4d944c223496afca63b87f6a23c1c1f68014d` |

补丁器只接受这一官方 DLL。现有旧版 1.0.0 不是补丁输入，需要先取得上述官方 1.1.6 文件。插件 GUID、显示版本和程序集版本保留 1.1.6，使用候选哈希与本地回执区分兼容补丁。无需额外 helper DLL。

## 复现验证

需要 PowerShell 7、.NET SDK 10 和已有的 Mono.Cecil **0.10.4.0**。脚本读取使用者提供的第三方文件，不下载依赖、不读取真实角色/场景、不安装插件。所有输出都写到指定的新目录，不能放在本源码目录内。

```powershell
./verify.ps1 `
  -CloXrayPath '<extracted-official-package>/BepInEx/plugins/Clo/LiquidWobbleMPB.dll' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -ArtifactsPath '<new-directory-outside-this-source-tree>'
```

验证入口会：

1. 校验官方输入哈希，构建补丁器与合成环境，生成必须与已审候选逐字相同的 DLL。
2. 核对修改范围：3 个原方法改变、776 个原方法保留；新增 1 个私有辅助方法及 1 个非静态协程字段；程序集引用和 `OnSceneSave` 不变。
3. 提取实际 DLL 中 `OnSceneLoad`、`ToIntArray`、`RestoreWombState` 工厂及完整迭代器 CIL，在相同合成输入上执行。官方对照必须得到 25 检查 / 13 项预期失败，候选必须得到 25 / 0。
4. 检查未知输入、输入输出同路径、输出已存在三种拒绝情况，并复核原输入和候选哈希未变。

证据写到 `verification-summary.json`、`metadata-diff.json` 和两个 `*-actual-cil.log`。原版的非零退出是刻意保留的缺陷对照；运行异常、检查数变化或不符预期的结果都不会算通过。更详细的替身边界见 [VALIDATION.md](VALIDATION.md)。

## 改动边界

只修改 `WobbleSceneController.OnSceneLoad`、私有 `RestoreWombState` 工厂及其 `MoveNext`。新增 `CaptureImportObjects` 私有辅助方法与迭代器 `importObjects` 字段。映射在 `OnSceneLoad` 同步调用时复制，随后作为对象引用被协程持有，避免异步查询全局 ID 再次命中其他对象。没有新增静态场景映射、文件写入或保存字段。

`wombPairStack`、`wombCollapsed` 仍使用原保存格式和原恢复代码；旧 `wobbleItemIds` 路径保留。原版在无新保存字段时从物件推断状态的逻辑也保留，但 Import 仅枚举本次导入物件。保存和实际材质状态应用均由原逻辑继续执行。

## 安装与回退

生成候选和验证通过不等于已安装或已完成真实 Studio 验证。部署应作为独立步骤：

1. 确认 Studio 正常退出。记录当前 DLL、`BepInEx/config/Clo.LiquidWobbleMPB.cfg` 和待替换配套文件的哈希，复制到新的备份目录。
2. 仅把已验候选复制到 `BepInEx/plugins/Clo/LiquidWobbleMPB.dll`。若完整升级还缺配套资源，另核官方包内两个 zipmod、`Clo.CloXray.animfit.baseline.txt` 和 `cloxray_torso_mask.png`。保留其他作者的 shader、物件和 Liquid 插件；不要整包解压覆盖示例场景。
3. 保留现有配置值与快捷键。新版本会废弃部分旧调节键并新增设置；配置存在不代表旧键继续生效。新默认值仅补缺失项，实际版本差异需另核。用户学习文件 `Clo.CloXray.animfit.txt` 与发行版 baseline 是不同文件。
4. 验证安装后哈希，记录准确文件清单；先检查空场景启动/退出，再用独立测试场景检查导入 ID 冲突、保存载入及资源装载。避免用原场景唯一副本验证。

回退时先退出 Studio，确认当前文件仍匹配部署回执，再恢复原 DLL 和 cfg。仅将本次新增且哈希仍一致的资源移出插件/mod 扫描路径；原来存在的资源恢复其备份。保护部署后的其他改动，不覆盖或移除不在回执中的文件。

当前公开源码验证不证明真实 Unity shader 支持、主游戏行为、角色 IK/FK、所有原场景或所有插件组合兼容。[来源与许可说明](SOURCES.md)记录上游身份和分发边界。

本版 17 个 F1 设置的译文校对和必要增量见 [配置界面中文增量](localization/README.md)。译文与二进制补丁分别部署和验收。
