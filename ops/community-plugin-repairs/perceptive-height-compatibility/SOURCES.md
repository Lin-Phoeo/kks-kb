# 来源与边界

- [PerceptiveHeightX 1.0.0 作者发布](https://github.com/bani4kaskashka/PerceptiveHeightX/releases/tag/1.0.0)：本次受检的 KKS DLL 来自 `PerceptiveHeightX.KKS_v1.0.0.zip`，包 SHA-256 `f6d7fb3a2f8c791c9a245b63139644212da4c37f051227ab216179e88d092c3e`。
- [同版本 README](https://github.com/bani4kaskashka/PerceptiveHeightX/blob/1.0.0/README.md)：解释参考身高、感知身高、可选 PerspectiveX 依赖及截图隐藏预期。
- [BepisPlugins](https://github.com/IllusionMods/BepisPlugins)：Screenshot Manager 来自使用者自己的 KKS 安装，真实 GUID、程序集名称、事件和API经准确参考DLL元数据核对；没有把其实现复制进公共工具。
- [Mono.Cecil](https://github.com/jbevain/cecil)：由使用者显式提供，用于精确原件定位、补丁和实际CIL提取。

补丁只在操作者明确执行时读取其原件；公开目录不分发第三方修订DLL或完整反编译类。合成事件源、注册表和生命周期夹具为本项目独立编写。上游及仓库各自许可不因本工具而改变。
