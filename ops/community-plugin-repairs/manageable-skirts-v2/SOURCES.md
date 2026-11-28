# 来源及许可边界

原插件作者为 Ziglo，V2 Sunshine 原件来自作者 [ManageableSkirts 发布页](https://www.patreon.com/ziglomods/posts/manageableskirts-154479217)。支持文件名 `ziglo_ManageableSkirts_KKS.dll`，插件版本 `2.0.0`，完整字节哈希见 README。查阅与本地静态验证日期为 2026-11-28。

此目录仅提供自有 helper、定向 patcher、最小合成接口与验证说明。自有部分沿用 [MIT LICENSE](LICENSE)；许可不扩展到作者原件、生成的第三方修改 DLL、游戏、Unity 或其资源。作者免费提供附件不代表允许重新分发修改版本；使用者应自行从作者渠道取得受支持原件。

构建依赖来自显式指定的本机安装：

- [BepInEx 5](https://github.com/BepInEx/BepInEx)
- [Mono.Cecil](https://github.com/jbevain/cecil)
- Unity 2019 和目标游戏程序集

这些依赖只被引用或解析，不随公共源码复制。fixture 是为验证状态行为编写的最小接口，不含作者完整反编译类或游戏完整实现。
