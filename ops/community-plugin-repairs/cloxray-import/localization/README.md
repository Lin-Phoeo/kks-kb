# CloXray 1.1.6 配置界面译文增量

`catalog-additions.json` 仅补充本版需要的 25 条显示译文：1 个中英标题、1 个分类、11 个设置名称、12 条说明。当前已有的 14 条准确显示译文直接复用；合计覆盖实际 DLL 中 17 个设置的名称与说明、4 个分类和1个标题，共39个显示身份。旧版已不再绑定的配置键不算本版设置覆盖率。

译文使用现有配置翻译器格式：`pluginGuid`、`kind`、`section`、`settingKey`、`source` 共同限定身份，`value` 仅为显示值。全部限定于 `Clo.LiquidWobbleMPB`。标题为“液体与形变 / LiquidWobbleMPB 1.1.6”，新增设置名称使用中文；说明保留必要的材质属性与配置标识，以便和插件本身对应。没有枚举映射或通用短词规则。

## 校准内容

- 主游戏专用设置明确标明“仅游戏本体生效”；液体填充量在 Studio 无效，工作室使用材质编辑器。
- 英文键名 `Womb offset to back (mm)` 容易误导。作者说明和本版实现以骨盆坐标方向为准：减小数值向背部，增大向腹部。显示名写“模型前后偏移（毫米）”。
- 新版皮肤层说明不再使用旧版写死的队列数字，按实际角色队列解释模板在器官前、皮肤层在器官后绘制。
- 配对快捷键的循环操作与肛门通道快捷键的固定操作分别说明，后者不会切换模板值组合。
- BP 覆盖参数说明保留 `name=value`、`squishThreshold`、`danLengthSquish`、`danGirthSquish` 等实际标识；提及的浅/深运动端点是内部参数，不声称存在本版没有绑定的 F1 设置。
- 复用旧设置标签与快捷键说明，不更改实际绑定的快捷键。说明中的默认按键不代表用户当前按键。

## 验证与使用接口

已从精确候选 DLL 的 `Awake` CIL 提取全部 17 次 `ConfigFile.Bind` 的 section、key、类型和说明，39 个显示源身份逐项匹配。再调用安装中配置翻译器的实际 `TranslationCatalog.TryGet`，完成 45 项检查，包含39项全覆盖、合并身份数及错误 GUID/section/source/enum 不误匹配。结果摘要见 [probe-summary.json](probe-summary.json)。这验证显示匹配，不代表运行中的 F1 已展开观察。

该文件是增量，不是整份目录。`cloxray_zh.py` 是现有 [community_zh.py](../../localization/community_zh.py) 的独立适配入口，继续使用原 0.3.0-rc.2 安装器的哈希检查、冲突拒绝、备份和回滚。它要求已审修复版 Clo DLL，并在原事务的冲突检查位置核对 `required-base-identities.json` 列出的14条复用译文；缺失或内容不同会停止，不宣称全部覆盖。

在本目录运行，替换两条本地路径；第一条只预检，第二条在 Studio 正常退出后合并：

```powershell
py -B cloxray_zh.py --game '<game-directory>' --localization-package '<unmodified-0.3.0-rc.2-package>'
py -B cloxray_zh.py --game '<game-directory>' --localization-package '<unmodified-0.3.0-rc.2-package>' --apply
```

成功安装会返回 `receipt`。需要回退时使用同一 rc.2 包内原安装器；先预检，再确认 Studio 已退出后执行：

```powershell
py -B '<unmodified-0.3.0-rc.2-package>/studio_zh.py' rollback --receipt '<returned-receipt-path>'
py -B '<unmodified-0.3.0-rc.2-package>/studio_zh.py' rollback --receipt '<returned-receipt-path>' --apply
```

若安装后目录已有其他修改，回退会拒绝覆盖，需按回执核对。不要用增量覆盖原有完整目录，或依赖后加载强行覆盖相同身份的其他译文。Clo 这批只有 F1 词表，不新增 IMGUI owner 或纹理翻译。

适配边界另通过7项临时合成安装检查：预检不写入、只更新一个目录文件、其他目录/DLL/配置值保持、回退恢复、未知 Clo 拒绝、基础14条缺失或变化拒绝、增量冲突拒绝以及重复合并无操作。运行测试不读取真实游戏：

```powershell
py -B test_cloxray_zh.py --localization-package '<unmodified-0.3.0-rc.2-package>' -v
```

合并只改变显示目录；DLL、配置值、快捷键、内部数据身份、shader 和模型资源均不在此译文增量范围。公开文件不包含真实配置值、私人路径或用户场景。当前包仍需主线合并与运行时检查，不把本地匹配结果写成已安装。
