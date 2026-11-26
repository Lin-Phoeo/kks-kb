# Studio 运行时修复记录

本目录保存经源码与发行包对照后形成的最小修复工具、来源和验证边界。它是独立的游戏维护工具集合，不参与知识库 Web 应用启动。游戏原件、第三方 DLL、真实日志和用户场景不在仓库中。

| 问题 | 本次处理 | 当前边界 |
| --- | --- | --- |
| KKSPE / Timeline BlendShape 数组越界、复用 FaceBlendShape 重复键 | [三处源码补丁](timeline-blendshape/README.md)，参考上游 PR #45 与后续回归 | 本机候选已安装；原 IL 对照 12/22，候选 22/22；Unity 原场景回归待完成 |
| Shallty 旧场景缺可选分组字段 | [精确输入哈希的补丁工具](compatibility-guards/README.zh-CN.md) | 候选已安装；合成与 CIL 范围核对通过；旧/新场景切换待完成 |
| SexFaces 非 OnGUI 静态样式初始化失败 | 官方 1.4.0 加最小 GUI 生命周期修复 | 候选已安装；真实 Unity 的非 GUI 类型初始化与 OnGUI 样式初始化通过，滑块操作待完成 |
| KLS 空地图诊断读取异常 | 保护无 sunLightInfo 的合法切图状态 | 候选已安装；不关闭环境功能或隐藏错误；切图回归待完成 |
| MaterialEditor 色彩辅助插件二次解码与 GPU 缓存生命周期 | [GPU 复制与 Token 释放修复](materialeditor-colorspace/README.md) | 前一候选实际 PNG 往返误差 0、3 轮 4096 释放通过；自测清理修正版已装，复验和原故障场景待完成 |
| Harvex shader 元数据节点不适用 KKS | [原件只读的注册包生成器](harvex-registration/README.md) | 已安装元数据兼容包；四个 shader 在 Unity 注册字典中加载且显卡支持 |

合成测试只验证隔离逻辑。正常启动、插件加载或 GPU 自测不能单独证明原场景已修复。检查应从精确原件哈希和备份开始，保留原游戏启动器与原生控制台，然后逐项运行真实功能。回滚仅还原 DLL / 移出新增兼容包，不改变卡片或场景。

仍未收束：PoseTools 姿势保存输入状态、Mod Bone Implantor 的具体空骨骼资源、部分场景缺失的精确 MOD。AssetBundle CAB 重名日志已观察到 XUnity 内存重试恢复，未据此删除或重打包资源。MaterialEditor 主插件没有盲目降级；此处修复的是辅助插件，非 ME5 稳定性保证。

上游核对与详细测试见各子目录。真实运行验收结果在确认后更新，未完成项保留为待验收。

初次启动还记录到 AutoKeyframe 的空 `OnPostRender(Camera)` 签名错误、CharaAnime 与 KKPE 的全局端帽名冲突、两个依赖的类型扫描警告，以及退出控制台句柄失效。这些在旧日志已有证据，尚未找到并验证适用的上游修复；不因已有而认定无影响，也不通过关闭功能或压低日志级别隐藏它们。
