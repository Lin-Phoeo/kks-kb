# KKS Studio 简中与界面适配包

当前源码与本地分发候选为 **0.3.0-rc.3-cm19.1**，配置中文及 AniMorph 布局 helper 仅配受检 **ConfigurationManager 19.1**；包内不含管理器，也不会自动升级它。准确组合已完成一次空 Studio 的45项绘制、搜索、中文回调及状态恢复检查，未覆盖所有交互。新候选尚未公开发布，请先读 [CM19.1 候选须知](CM19.1-READ-FIRST.zh-CN.md)。以下 rc.2 下载说明属于已发布历史版本，仍配 CM19.0，保留作原组合回退。

本包为已经安装中文资源和 BepInEx 的 Koikatsu Sunshine Studio 提供修订词表、插件设置汉化、窗口翻译兼容修复及侧栏排版。游戏目录由安装时的 --game 指定，可使用任意磁盘和带空格路径。

源码目录只包含可公开的代码、词条及来源记录。正式 ZIP 另外包含本项目辅助 DLL，以及保留 MIT 许可和修订源码的 XUnity Core。游戏文件、人物卡、场景、字体、私人自动翻译缓存和原始运行日志由使用者自行保管。

既有公开预发布为 **0.3.0-rc.2**。首次安装从[快速开始](START-HERE.zh-CN.md)进入；完整操作、回滚和兼容边界见下文及[验证记录](VALIDATION.md)。

## 安装前准备

1. 保存工作，正常关闭 Studio 和主游戏。
2. 使用已安装 HF Patch 或等价插件环境的 KKS。确认游戏目录内有 CharaStudio.exe，XUnity 目标语言为 zh-CN，翻译目录使用标准默认路径。
3. 安装 Python 3.10 或更新版本。基础安装只需 Python。本地复用导入和 --native-ui 还需 .NET 10 Runtime，正式 ZIP 不需要 .NET SDK。
4. 将 ZIP 解压到游戏目录以外，例如自己的下载目录。保留整个解压目录，回滚命令需要其中的安装器。

## 预检和安装

在解压目录打开 PowerShell。以下路径只是示例，请换成自己的游戏目录。

~~~powershell
py studio_zh.py doctor --game "E:/Games/Koikatsu Sunshine" --offline
py studio_zh.py install --game "E:/Games/Koikatsu Sunshine" --offline --apply
~~~

doctor 和不带 --apply 的 install 只预检。--offline 清空 XUnity 的在线翻译端点，保留本地词条和字体加载。默认会启用翻译作用域。

预检会显示未匹配的可选插件。对应辅助功能会跳过，并在结果中列出原因。--strict 可要求所有可选依赖都匹配。Core 属于必须匹配的依赖，未知 Core 会停止。安装器不会靠文件名判断相同版本，而是核验哈希。

安装前将每个被替换文件和配置留存，安装后核对哈希，输出 .studio-localization/backups/<运行号>/receipt.json。保留这一回执。中途失败会按回执恢复已经修改的文件，失败恢复会明确标为 recovery_required。

更新受检的既有 F1 显示目录和动态规则目录时，安装器按精确身份合并，保留已安装的额外译文，并应用本包修订。基础包更新完整本地目录时不会把它缩减成基础子集。未知内容仍需回执或列入已审哈希后才可合并。

## 复用自己的现成汉化

公开包保留已公开审核的 rc.3 基础修正、885 条新增独立窗口译文及 1,525 条独立 F1 显示上下文。其余复用词条的许可没有确认，本包提供按原文和译文哈希筛选的本地导入器。导入器读取你指定的词表目录或固定公共归档，不读取以 _ 开头的私人自动生成缓存，也不上传内容。

~~~powershell
py local_import.py --game "E:/Games/Koikatsu Sunshine" --translations "E:/Games/Koikatsu Sunshine/BepInEx/Translation/zh-CN/Text" --output "E:/KKS-Chinese-Local"
~~~

可显式选择下载固定社区归档。下载只在指定下列参数时进行，归档完整哈希必须匹配。

~~~powershell
py local_import.py --game "E:/Games/Koikatsu Sunshine" --translations "E:/Games/Koikatsu Sunshine/BepInEx/Translation/zh-CN/Text" --download-community --output "E:/KKS-Chinese-Local"
~~~

进入生成目录，使用其中的 studio_zh.py 安装。local-import-report.json 列出导入数量和仍未找到的精确身份。来源版本或译文发生变化时会跳过，保留已有汉化。参考检查中，固定社区归档独立提供了 1,775/2,420 个复用请求，结合受检现有 TXT 达到 2,305/2,420，其余 115 项来自已审核的 F1 显示目录。仅有社区归档时，包会明确保持部分覆盖。

如果已有本项目的显示目录，可显式增加 --catalog 参数。参考环境同时提供 TXT、固定归档和该目录时，2,420/2,420 个请求匹配。该参数只读取指定的显示文本，不读取设置值。

~~~powershell
py local_import.py --game "E:/Games/Koikatsu Sunshine" --translations "E:/Games/Koikatsu Sunshine/BepInEx/Translation/zh-CN/Text" --catalog "E:/Games/Koikatsu Sunshine/BepInEx/plugins/Codex.StudioConfigLocalization.json" --download-community --output "E:/KKS-Chinese-Local"
~~~

含本地复用词的输出包请按其来源权利另行判断再分发。

## 启动和实际检查

使用游戏原来的 InitSetting.exe，点工作室。由助手启动交互式启动器时应显式使用正常窗口模式。

~~~powershell
Start-Process -FilePath "E:/Games/Koikatsu Sunshine/InitSetting.exe" -WorkingDirectory "E:/Games/Koikatsu Sunshine" -WindowStyle Normal
~~~

进入后检查 F1 的场景加载器排序、插件说明悬停、左侧展开列表、Timeline 和常用插件窗口。检查中文、数字、按键、颜色标签和原对象名是否正确，按钮有没有裁切。缓存加载通过与实际显示是两个验证项目。测试结束后正常退出一次，再核对是否有新错误。

Coordinate Load Option 原生中文适配会校验已安装的 DBDE / AssetImport 版本，并让它们按实际状态识别替换模式。需要这些插件的饰品数据时使用“替换模式”；它们原本不支持的真正追加模式仍会提示并跳过。未知消费者版本会拒绝原生适配，保留既有翻译路径。

## 字体与图标

首次使用字体控制器时，统一字体默认关闭，保留现有汉化环境的默认中文字体。主动从列表选择字体后才开启统一；旧 uGUI/IMGUI 使用对应系统字体，TMP 使用对应字体文件构造，保留缺字回退。已有用户配置继续保留，更新包不会强制覆盖原字体选择。

从旧版更新且已开启统一字体的用户，如果希望恢复原中文显示，请在 F1 搜索 Studio Font 后点击“恢复默认中文与字号”。

1.1.0 起可以在保留默认中文时单独开启“文字加粗”，或将“字号微调”设为 -2 到 +3。增量作用于控件各自的原字号，紧凑输入框限制放大。恢复默认中文会同时关闭字体替换、关闭额外加粗并将字号微调归零；插件原本的粗体或斜体保持其原样。

“选择中文字体”只列出字体文件与系统实际字面均通过常用汉字检查的选项，显示中文名称及原家族名，例如“微软雅黑 / Microsoft YaHei”“微软正黑体 / Microsoft JhengHei”。列表在后台建立并复用，不逐帧扫描。系统以其他字体代替的假匹配、当前不能可靠选择的字体集非首面和未经验证的可变字体不会列入。旧配置仍保留原身份，未通过检测的旧值不会自动切换成另一字体。

1.1.1 面板将当前效果与“恢复默认中文与字号”放在最上面。字体列表可用中文名或原家族名搜索，列表用短中文名称，悬停查看完整名称；已核准的旧中文别名会正确标记为已选。字号使用“减小／增大／归零”。“高级：自定义字体文件”只在点击应用时提交，加载失败会恢复上次成功设置并显示原因，输入草稿可继续修改。

在 F1 搜索 Studio Font，展开“选择中文字体”，搜索并选择本机受检字体。高级设置可指定已安装的本地 TTF、OTF、TTC；路径优先于列表，且仍需通过文件及系统字面匹配，相对路径从 UserData/StudioFonts 查找。字体在本机读取，不随包提供。“恢复默认中文与字号”撤回本工具的字体、加粗和字号覆盖；只关闭“统一界面字体”则保留单独的加粗与字号设置。宋体与华文细黑不再强制推荐。部分窄面板使用微软雅黑可能截短，用户可恢复默认或选择其他字体。篆书需自行安装覆盖常用中文且与本加载器兼容的真实篆书字体；图片内的字仍需单独生成。

统一全部文字系统时，自定义字体还需要已安装到 Windows。TMP 从指定文件读取字体，旧 uGUI/IMGUI 使用对应的系统字体家族。路径字体能被读取但缺少有效旧字体行高时，控制器会拒绝切换并保留上一套字体。不能把文件读取成功当成全部界面支持。

图片里的文字需要本地图片生成器。texture-patches/ 使用现有受检工作室图包和本机字体生成替换图，不携带第三方底图。较新的工作面板图标需按对应原 DLL/图集版本生成，基础文字包不会替代用户原有图标。

## 回滚

关闭游戏，使用安装时输出的回执。

~~~powershell
py studio_zh.py rollback --receipt "E:/Games/Koikatsu Sunshine/.studio-localization/backups/<运行号>/receipt.json"
py studio_zh.py rollback --receipt "E:/Games/Koikatsu Sunshine/.studio-localization/backups/<运行号>/receipt.json" --apply
~~~

回滚会先核对当前文件和备份。安装后被手动改过的文件会阻止回滚，需按回执处理。新建文件保存在回执的 removed/ 目录中，可恢复。按后装先回滚的顺序操作。

## 详细资料

- [教程与排查](TUTORIAL.zh-CN.md)
- [AI 接手说明](AI-HANDOFF.md)
- [兼容性和验证范围](VALIDATION.md)
- [来源与许可](SOURCES.md)
- [维护者构建步骤](BUILDING.md)
