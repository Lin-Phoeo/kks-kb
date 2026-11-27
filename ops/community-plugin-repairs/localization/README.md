# 社区插件中文显示增量

本目录补充已审插件的 F1 设置页和 Grey 导出窗口中文。安装依赖已经发布的 [Studio 简中 0.3.0-rc.2 包](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)，不替换该发布包，也不安装社区插件本体。翻译为本次独立编写，未复制第三方翻译资源表。

## 覆盖内容

| 文件 | 内容与用途 |
| --- | --- |
| `catalog-additions.json` | 48 条 F1 显示译文：5 个双语插件标题、7 个分类标题，以及18项设置的名称与说明。覆盖 ManageableSkirts、DragDoll、Grey、Vanity Dress-Up 和 Vanity Make-Up 五个 GUID。 |
| `grey-imgui-additions.json` | Grey 窗口17条精确规则和1条以 `Export <角色标签>` 为格式的动态规则，归属程序集限于 `KKSMeshExporter`。动态标签原样保留。 |
| `Text/Plugins/KKSMeshExporter/0000_CommunityGrey.zh-CN.txt` | 与前述17条精确规则一致的 XUnity 插件词表，用于建立 Grey 的独立翻译作用域。 |

配置身份、保存值、快捷键、文件路径与插件行为保持。Vanity 自带的简体中文窗口继续由插件自身处理；Make-Up 在此指外观编辑，不局限于化妆。不存在的 KK 专用裙摆末端节点选项没有加入 KKS 设置页。

## 安装前提

解压官方发布的 `KKS-Studio-zh-CN-0.3.0-rc.2.zip`，保留其中的 `studio_zh.py` 和 `manifest.json`。工具核对这两个文件的准确哈希，再复用该安装器的合并、进程检查、锁、备份、文件校验与回滚事务，不复制整套安装器源码。

游戏目录需要已有受检 ConfigurationManager、修订 XUnity Core 以及两个中文辅助 DLL。两个辅助 DLL 分别接受 rc.2 发布版与已审原构建的准确哈希；其他版本停止预检。准确身份见 [community_zh.py](community_zh.py) 中的 `HELPERS`，ConfigurationManager 与 Core 的身份沿用 rc.2 清单。缺依赖时按 rc.2 包说明先准备基础安装，再运行本增量；本工具不自动下载或替换 DLL。

需要 Python 3.10 或更新版本。以下 PowerShell 命令中的路径是示例，请改为自己的实际路径。先进入本目录，`$Package` 指向解压包中直接包含 `studio_zh.py` 的那一层：

```powershell
$Package = 'E:/Downloads/KKS-Studio-zh-CN-0.3.0-rc.2'
$Game = 'E:/Games/Koikatsu Sunshine'
py -B community_zh.py --localization-package $Package --game $Game
```

不加 `--apply` 时只做 doctor：游戏目录不写入任何文件，临时增量目录在检查结束后自动清理。输出列出计划修改的三个目标文件和必要的作用域配置变更；全部已安装时计划数为0。

确认预检通过，保存工作并正常退出 Studio 与主游戏，再执行：

```powershell
py -B community_zh.py --localization-package $Package --game $Game --apply
```

`--apply` 明确授权合并本目录的三个文件。已有两个 JSON 目录即使包含后续新增的自定义译文，也按本次读取的准确哈希纳入允许合并的范围；原安装器在写入前备份原文件、再次核对哈希并生成回执。按完整配置身份 `(pluginGuid, kind, section, settingKey, source)` 或动态规则身份 `(owner, source, pattern)` 合并，保留所有其他条目。按运行时匹配规则，相同身份译文不同会停止，旧目录自身的冲突重复项也会停止，不擅自选择覆盖版本。F1 冲突判定将原文 CRLF／CR 换行统一为 LF，并视空分类／设置键与 null 等价；动态精确规则按 owner 与 source 判定，不因无效的 pattern 附带值而漏查冲突。这些规范化只用于检查，不重写已有原文。

已有同名 Grey TXT 内容不一致会停止，其他更高优先级的同作用域词表也由原安装器拒绝。不要通过扩大到 `global` 作用域掩盖路由缺失。若安装期间目录内容被其他程序修改，事务停止并保留并发修改。重复执行已完成的安装返回 `already_current`，不会新增回执或重复条目。

原安装器可能将 `EnableTranslationScoping` 设为 `True`，相关配置同样先备份。翻译服务端点、字体偏好和社区插件配置不由本增量更改。成功输出中的 `receipt` 是本次回滚入口，存放于游戏目录 `.studio-localization/backups/<本次编号>/receipt.json`；请保留整个备份子目录。

## 验证与回滚

重新进入 Studio 后，检查 F1 中上述插件的标签和说明；打开 Grey 窗口检查按钮及带角色名的导出标签。动态规则依赖真正进入 Grey.OnGUI 后的作用域路由，文件安装成功不代表所有窗口、字体大小和裁切均已实际验证。命令输出始终保留 `runtime_verified: false`。

需要回滚时，保存并正常退出游戏，使用同一个 rc.2 安装器。先预检，再实际恢复：

```powershell
$Receipt = 'E:/Games/Koikatsu Sunshine/.studio-localization/backups/<本次编号>/receipt.json'
py -B "$Package/studio_zh.py" rollback --receipt $Receipt
py -B "$Package/studio_zh.py" rollback --receipt $Receipt --apply
```

回滚以本次回执为边界，恢复安装前文件；文件安装后被手动改过会拒绝覆盖，需先校对后续修改。不要直接删除完整翻译目录或其他安装批次的备份。

## 语义校对依据

48条 F1 原文均核对受检 DLL 的 BepInPlugin 元数据或 ConfigFile.Bind 字符串；标题版本随受检插件元数据固定，升级后需要重新校对。此批没有枚举翻译：Vanity 语言设置是自由字符串，快捷键为 KeyboardShortcut，布尔开关使用 ConfigurationManager 共用控件；添加虚构语言枚举会误述实际类型。

Grey 作者原描述称灰度凹凸图转RGB法线图，实际受检转换器将源 alpha/green 复制到 red/green，并将 blue/alpha 设为1。中文说明按此描述通道转换，没有声称执行高度转法线计算；材质生成说明也不主张未经实际材质缓存路径证明的逐网格数量。实验性 cloth 与作者建议均保留。

ManageableSkirts 的远侧链隐藏指隐藏操作导引，并非删除骨骼。Vanity 的 T1/T2 是编辑会话开始时读取的参考坐标卡，不表示自动替换当前角色。

## 可复跑的合成检查

```powershell
py -B test_community_zh.py --localization-package $Package -v
```

18项工具回归全部使用临时合成安装：doctor无写入、未知既有目录内容保留、实际原安装器备份与回滚、重复执行、F1和动态词条冲突、既有重复身份冲突、辅助DLL与Core哈希拒绝、未知及已有回执的TXT冲突、作用域优先级、路径逃逸、并发修改、未知安装器拒绝、换行与空身份规范化、同值原文保留以及动态精确规则身份。测试加载受检 rc.2 安装器代码，但不读取或写入真实游戏；依赖文件使用明确的合成字节，进程检查在夹具内替代。

另有72项实际 TranslationCatalog／RuleMatcher 方法验证，涵盖48项 F1 查找、合并／错误作用域、17项 Grey 精确匹配及动态名称与反例。17条TXT经已安装 XUnity 编码器／解析器往返，包含换行的结果与JSON一致；所有源文本也已独立核对。详见 [probe-summary.json](probe-summary.json)。这些结果证明的范围是文本、身份、匹配和安装事务，不替代真实 GUI 绘制、字体排版或所有插件功能验收。
