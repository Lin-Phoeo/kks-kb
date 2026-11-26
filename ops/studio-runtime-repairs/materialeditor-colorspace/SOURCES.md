# 来源与依赖边界

核对日期：2026-11-26。本目录发布自有修正源码和合成测试，不复制第三方 DLL 或完整上游源码。

| 来源 | 用途与边界 |
| --- | --- |
| [Unity 2019.4 Graphics.CopyTexture](https://docs.unity.cn/2019.4/Documentation/ScriptReference/Graphics.CopyTexture.html) | 格式兼容、尺寸和 GPU 复制约束；文档不保证所有平台 sRGB/linear 组合 |
| [Unity 2019.4 RenderTextureReadWrite](https://docs.unity.cn/2019.4/Documentation/ScriptReference/RenderTextureReadWrite.html) | 非颜色数据使用 Linear RT，Gamma/Linear 项目行为差异 |
| [Unity 2019.4 GL.sRGBWrite](https://docs.unity.cn/2019.4/Documentation/ScriptReference/GL-sRGBWrite.html) | 避免依赖绘制时隐式色彩转换 |
| [Microsoft D3D11 CopyResource](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-copyresource) | 同格式组的 GPU 原始复制规则，不能代替实际 Unity 验证 |
| [KK_Plugins ImageHelper 固定 blob](https://api.github.com/repos/IllusionMods/KK_Plugins/git/blobs/30ea759b39ee004effc98effe6c645af0e1993ee) | 核对默认 Texture2D 导入没有按属性切换 linear 的接口 |
| [KK_Plugins TextureContainerManager 固定 blob](https://api.github.com/repos/IllusionMods/KK_Plugins/git/blobs/dfb66c21805d78af4cbec47499448c08abfdb3ca) | 核对 Token.Destroy 生命周期、ME 返回 RenderTexture 的路径 |
| [KK_Plugins v272](https://github.com/IllusionMods/KK_Plugins/releases/tag/v272) | 上游撤回 ME5 稳定发行的说明，不作为盲降级指令 |

## 自有源码来源

`MEColorSpaceFixPlugin.cs`、`GpuCopyGate.cs`、`RuntimeStress.cs`、`SelfTestRenderTargetScope.cs` 与 `RoundTripTest.cs` 来自本项目本机制插件的修正工作。RoundTripTest 是此前自有测试方法的可读恢复版本；未复制游戏或第三方插件实现。`tests/LegacyDecodeFixture.cs` 是只重现旧调用/持有模式的合成夹具，不是旧 DLL 或完整历史日志。

## 第三方运行依赖

- UnityEngine：使用者已有游戏安装中的程序集，按 Unity/游戏原有条款使用；不随包再分发。
- BepInEx 5 与 HarmonyX：从使用者已有安装读取引用，保留各自上游许可。
- MaterialEditor：仅在运行时通过既有 API/反射目标连接，源码包不分发其程序集。不同版本兼容性需要实际验证。
- .NET SDK 与 .NET Framework reference assemblies：使用本机已安装工具，不自动安装或下载。

## Advisory 检查范围

公开生产源码和合成测试没有 NuGet `PackageReference`；合成测试的 `NuGet.Config` 清空包源，验证构建未下载或更新依赖。审阅了新增依赖面，未新增第三方包。

这不构成对使用者游戏中 Unity/BepInEx/HarmonyX/ME 二进制的完整漏洞扫描或“无已知漏洞”声明。版本和供应链由现有安装提供；没有锁定包版本可供 NuGet advisory 审计器识别。安装者仍应核对这些上游当前安全公告与其实际 DLL 身份，不能把本目录的13项功能断言当作依赖安全审计。
