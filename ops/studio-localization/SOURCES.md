# 来源与许可

本包自有安装器、适配器、文档和独立新增译文使用根目录 LICENSE 的 MIT 许可。该许可不改变依赖插件、游戏资源、字体和外部译表的权利。

| 内容 | 来源与身份 | 分发方式 |
| --- | --- | --- |
| 已公开基础修订 | KKS-Studio-zh-CN 0.2.0-rc.3，SHA-256 8471dc4f94c32116ea8d929e7f2d5fa7aadbdc7c6ce0cc28cd2e2f3bb7a46cd3 | 保留原公开范围与来源说明 |
| XUnity AutoTranslator | [官方 v5.6.3.1](https://github.com/bbepis/XUnity.AutoTranslator/releases/tag/v5.6.3.1)，tag commit 08324a58dcb26b76fc08e0fb2121098164579e73 | MIT Core 可在 ZIP 分发，原许可在 licenses/，修订源码在 sources/core-patch/ |
| 窗口回调修复 | [官方 PR 876](https://github.com/bbepis/XUnity.AutoTranslator/pull/876) | 官方实现保留在 Core，另修正共享 Type 选择 |
| ConfigurationManager | [BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager) 19.0 | 使用者自行安装，包中只含本项目适配器 |
| 社区词表 | [固定归档](https://github.com/angel12538/KoikatsuSunshine-zh-CN/tree/aad584eecb25d5c3988abbdc9f2ad0981ffef9c6) | 未确认再分发许可，值不附包，本地导入器需显式选择来源 |
| 字体、游戏图集、第三方插件 DLL | 使用者现有安装 | 不随本包源树或基础 ZIP 复制 |
| 原生对话框补丁 | 对已安装受检 DLL 的四个常量修改 | 在使用者本机生成候选，原件须备份 |

官方 XUnity ZIP SHA-256 为 6c3ff03aafe9708f25c2115c0c04c255b6a9d88e716896ae7bacb590a8777609，官方 Core SHA-256 为 e5c95cb8f78bf208cbf5c552e437d4f51048d9bff6452394ad135e6ca75c8286，单指令修订 Core 为 9700854b20f25b7a81cff2c46da08c12bd5f27cc2544efdfe62dc4cb548bd002。

插件语义参考包括 IllusionMods 的 Modding API、KK_Plugins、HSPlugins，StarPlugins 的 Better Scaling / Mass Shader Editor，Aktymel 的 Advanced Loader / Accessories Menu，Clo15 的时间轴播放条，以及 SuIT-Ji 的动画和姿势浏览器。

动画和姿势浏览器帮助的作者为 SuIT-Ji，来源为 [HS2-Sandbox 固定版本](https://github.com/SuitIThub/HS2-Sandbox/tree/3393038b5e20e01d3ae52b2a9a764220ad2a68f1)，原许可为 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。rc.3 基础包中相应界面帮助是中文翻译和术语调整，保留作者与原项目归属。这些改编内容继续遵循 CC BY 4.0，本项目 MIT 仅适用于自有部分。

optional-import.json 只包含精确匹配所需的原文、目标身份及预期译文 SHA，不附来源许可未核实的译文值。成功本地导入后的输出可能包含这些值，不能据基础包 MIT 直接宣布可自由再分发。
