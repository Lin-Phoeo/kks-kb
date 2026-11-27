# KKS Studio 简中包快速开始

适用版本：**0.3.0-rc.2 预发布**。这是已有 KKS / BepInEx / 中文资源环境的增量修正包，包含插件汉化、F1 中英标题、界面排版及可选字体控制。它不是游戏整合包，不附游戏、字体或人物卡。

## 1. 下载与准备

从[发行页](https://github.com/Lin-Phoeo/kks-kb/releases/tag/studio-zh-cn-v0.3.0-rc.2)下载 `KKS-Studio-zh-CN-0.3.0-rc.2.zip` 与校验文件。解压到游戏目录以外。需要 Python 3.10+；可选社区导入和原生文件对话框修订另需 .NET 10 Runtime。

在 PowerShell 核对下载文件：

~~~powershell
Get-FileHash -Algorithm SHA256 .\KKS-Studio-zh-CN-0.3.0-rc.2.zip
Get-Content .\KKS-Studio-zh-CN-0.3.0-rc.2.zip.sha256.txt
~~~

两处哈希应相同。先保存并正常退出 Studio 和游戏。

## 2. 预检

打开解压目录，在 PowerShell 执行；把示例路径改为自己的游戏根目录：

~~~powershell
py studio_zh.py doctor --game "E:/Games/Koikatsu Sunshine" --offline
~~~

必需依赖未知会停止；可选插件缺失或版本不匹配会明确跳过。不要靠改文件名绕过检查。`--offline` 表示关闭在线翻译端点，继续使用本地译文。

## 3. 安装并保存回执

~~~powershell
py studio_zh.py install --game "E:/Games/Koikatsu Sunshine" --offline --apply
~~~

安装器先备份被替换文件，再核对新文件，输出 `receipt.json` 的位置。保留回执及整个解压目录，之后恢复要用。更新会保留已有用户设置和受检目录里的额外译文。

## 4. 使用字体面板

从原启动器进入工作室，按 F1 搜索 `Studio Font`。

- 默认保留原中文；只想加粗或略调字号，无需启用换字体。
- 展开“选择中文字体”，输入中文名或原英文家族名搜索，点名称选择；悬停可看完整名称。
- “文字加粗”增加粗体；字号用减小、增大或归零，范围 −2 到 +3。
- “恢复默认中文与字号”一起撤回本工具的三种覆盖。
- 自定义文件收在高级设置，点击应用才加载；失败时恢复上次成功设置并保留原因。

字体必须在 Windows 已安装且通过本加载器的中文覆盖、首字面和系统字面核验。图片里原本画好的字不随这些选项改变。

## 5. 按需补齐本地已有译文

公开基础包没有附带全部社区译文。需要复用自己的词表或固定社区归档时，按 [README 的本地导入步骤](README.md#复用自己的现成汉化)运行；工具会列出匹配和缺项，未匹配不会伪装成完整覆盖。重新分享导入结果前，应核对来源许可。

## 6. 回退与交接

先关闭游戏，使用实际回执路径预检，再恢复：

~~~powershell
py studio_zh.py rollback --receipt "E:/Games/Koikatsu Sunshine/.studio-localization/backups/实际运行号/receipt.json"
py studio_zh.py rollback --receipt "E:/Games/Koikatsu Sunshine/.studio-localization/backups/实际运行号/receipt.json" --apply
~~~

安装后被你另行修改的文件会阻止盲目覆盖；按回执核对，多个安装按后装先回退。遇到英文残留时记录插件版本、入口和原文；字体问题记录当前设置和控件。其他 AI 从 [AI-HANDOFF.md](AI-HANDOFF.md)接续。

本次 1.1.0 功能已有用户实机确认；1.1.1 最后面板与失败回退完成离线检查并安装，用户决定不再进行额外手测，因此以预发布分发并保留这项未验证范围。DBDE / AssetImport 真正的追加模式仍不支持；需要完整载入其饰品数据时使用替换模式。详细范围见 [VALIDATION.md](VALIDATION.md)和[排查教程](TUTORIAL.zh-CN.md)。
