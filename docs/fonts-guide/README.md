# KKS中文缺字修复：给用户和AI的操作资料

部分中文变成方框、空白或缺笔时，先确认字符是否存在于当前字体，再处理对应的UI框架。这里给的是一次真实缺字排查的可复用方法；不要求读者使用同一整合包、目录或插件组合。

- **想直接读教程：** [B站专栏草稿](BILIBILI-ARTICLE.zh.md)
- **想交给自己的AI排查：** 完整复制 [独立修复提示词](AI-REPAIR-PROMPT.zh.md)
- **想核对依据：** [脱敏证据与来源](evidence.json)

## 适用问题

| 现象 | 先查什么 | 字体更换能否直接解决 |
|---|---|---|
| 同一批中文字稳定缺失、方框或空白 | 原文字符、字体字形覆盖、实际UI框架 | 缺字时可尝试正确字体/fallback |
| 文本变成“锟斤拷”或错误字符 | 文件编码、读取编码、翻译内容 | 通常需要先修编码或原文 |
| 字都正确，只是小、糊或裁切 | UI缩放、DPI、字号与布局 | 不应先换整套字库 |

XUnity.AutoTranslator的`OverrideFont`仅适用于UGUI。TMP优先`FallbackFontTextMeshPro`；它只为缺失字符提供后备字体，通常比覆盖全部TMP字体更合适。IMGUI需要对应插件的GUI字体或UI缩放入口，不能认为修改OverrideFont会控制全部插件窗口；`EnableIMGUI`只是翻译框架开关，不保证更换GUIStyle字体。

## 这次有什么证据

2026-11-26的本地样本检查中，MS Gothic集合缺13个常用简体字；Microsoft YaHei缺0字。已持有的TMP资产有33932条字符记录，样本全部覆盖，但基本CJK仅20902/20992、扩展A为0/6592，因此不声称“全中文”。系统YaHei集合则覆盖这两个区块。

TMP资产1.1.0与当时运行TMP1.4.0产生版本警告，之后成功加载；bundle构建头为Unity2019.1.0f2，游戏日志为Unity2019.4.9.5308042，并非完全同版。实际菜单样本可读，只说明这个组合在已走路径可用，不证明该字库适配所有游戏、Unity或TMP版本。字体cmap、字符表和实际画面属于不同验证层次，不能互相替代。

## 官方依据

本次核对的是XUnity.AutoTranslator固定提交`692e044503f591d58c875e06bea1872b5d2dcc56`的README：

- [配置说明](https://github.com/bbepis/XUnity.AutoTranslator/blob/692e044503f591d58c875e06bea1872b5d2dcc56/README.md#L336)：OverrideFont仅UGUI；TMP推荐fallback。
- [覆盖字体的边界](https://github.com/bbepis/XUnity.AutoTranslator/blob/692e044503f591d58c875e06bea1872b5d2dcc56/README.md#L480)：TMP资产需要针对目标游戏适配，Resources与系统字体路径另有条件。
- [预制字库说明](https://github.com/bbepis/XUnity.AutoTranslator/blob/692e044503f591d58c875e06bea1872b5d2dcc56/README.md#L494)：官方也注明预制资源测试有限。

固定文档快照SHA、字体文件身份、样本字符和本地检查摘要见evidence.json。其原始本机路径、完整配置和日志不公开。当前游戏应优先核自身已安装版本的说明，不把这份快照当所有未来版本的承诺。

## 修改边界

先退出游戏并备份目标配置；只改已确认适用的字体键。任何TMP路径都是目标环境实际资源的路径，不能盲抄示例或下载未知字体包。本资料不包含字体二进制、游戏资源、第三方完整源码或自动修改脚本。

修复后必须启动游戏，回到原缺字页面验证，检查字是否显示、控件是否裁切、是否有新字体加载错误。失败时依据本次最小差异回滚；不改翻译模型、代理、账号、日志开关或游戏数据。本站其他维护记录不是本字体流程的前置条件。
