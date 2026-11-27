# AI 接手与维护入口

任务目录为本文件所在目录。先读 README.md、VALIDATION.md、SOURCES.md。当前所有权与安装步骤以 manifest.json、release-components.json、payload-index.json 为准。

## 可直接复制的任务说明

~~~text
请维护这份 KKS Studio 简中包。游戏根目录由我提供，先运行 doctor 并读取结果。
保留已有文件和用户配置，使用 receipt.json 安装/回滚。
先查现成汉化和插件说明，按实际 DLL 版本、可见控件及回调校对。
F1 显示按 pluginGuid、section、settingKey、source 匹配，不能改配置原键和选项值。
插件窗口使用精确程序集作用域，新增共享 API 作用域前检查调用栈是否抢走别的插件控件。
通过 parser 和缓存仅说明译文加载，实际字体、布局、悬停和正常退出必须另验。
不要把人物卡、场景、私人自动翻译缓存、日志、凭据或未授权第三方资产提交到仓库。
每次留下当前结果、未验证范围、文件哈希和回滚入口。结束时更新本包的验证记录。
~~~

## 文件映射

| 路径 | 用途 |
| --- | --- |
| studio_zh.py | 预检、原子安装、回执、回滚 |
| local_import.py | 按键和值哈希复用本地词表，显式选项下载固定归档 |
| optional-import.json | 复用身份和输出上下文，不含许可未确认的中文值 |
| translations/baseline/ | 已公开 rc.3 基础修订 |
| translations/windows/ | 新增独立窗口译文 |
| translations/configuration-original.json | 可公开的独立 F1 显示上下文 |
| sources/configuration/ | 临时显示适配器与目录匹配 |
| sources/imgui-route/ | 已知 Unity/HiDPI 回调解包 |
| sources/imgui-dynamic/ | IMGUI 高频文本的有界动态规则与长文查询 |
| sources/native-text/ | VideoExport 25 个原生中文键修订 |
| sources/sidebar/ | 侧栏排版与字体回退 |
| sources/font-controller/ | 系统或本地字体选择、动态字体构造与资源所有权 |
| sources/animorph-layout/ | 受检 AniMorph 与 ConfigurationManager 的布局事件兼容 |
| sources/core-patch/ | 官方 MIT Core 单条 IL 修正和验证 |
| tests/ | 合成安装与回滚回归 |
| tools/LocalTools/ | 实际 parser 本地导入、规则重建、原生常量候选工具 |

## 后续变更的验证顺序

1. 核对安装版 DLL 身份和现有词表，不把旧报告当当前版本证据。
2. 复核原文调用位置，区分可见文案、对象 ID、格式模板、用户数据与资源文本。
3. 使用实际 XUnity parser 验证转义，动态规则同时测试数字模板及不匹配样例。
4. 对新增插件作用域检查共享 UI 工厂。Timeline.UILib 和 KKSAPI 是已知案例。
5. 打包到游戏目录外，先预检，再安装，保存回执。
6. 从原 InitSetting 启动并真实检查原故障入口，最后正常退出。
7. 审阅公开差异，更新版本、许可、统计和验证范围。

本包的默认安装不调用第三方在线翻译。optional-import 的来源值在用户本地解析，分享导入后包前需再核来源权利。
