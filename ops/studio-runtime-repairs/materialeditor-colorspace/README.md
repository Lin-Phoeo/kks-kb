# MaterialEditor ColorSpace Fix（源码候选）

为已有 MaterialEditor ColorSpace Fix 1.1.0 提供局部修正：移除 `BuildLinear` 的第二次图片解码与 PNG 静态缓存，改为经过像素探测的原始位 GPU 复制，并随 MaterialEditor Token 生命周期释放生成的贴图。

**当前状态：源码构建、托管回归已通过；当前自测清理修正版的实际 Unity 验收及原故障场景回归尚未完成。** 这是可复查的修复候选，不宣称所有闪退或画面问题已根治。它不升级或替换 MaterialEditor 本体。

## 适用范围

针对 KKS、Unity 2019.4、BepInEx 5 与具有 `KK_Plugins.TextureContainerManager.Token` 生命周期的 MaterialEditor 实现。实装目标为 ME5 的实验版本；上游 v272 将 ME5 撤出稳定分支，不能把“发行号更新”视为此环境可直接覆盖的兼容修复。不同 ME 版本缺少反射目标时会记录错误并不启用导入补丁。

仅对本地非颜色属性规则命中的、由 ME 解码得到的单采样 2D RenderTexture 生成线性副本。候选保留尺寸、格式对应关系、mip 数和采样属性。每种源格式先用4×4贴图做 GPU 复制与像素读回；不支持、格式或 mip 不匹配、复制失败时保留原贴图并避免对同一来源反复重试。`MainTex` 等颜色白名单保留原引用。

观察到的崩溃路径经过本机制插件的 `LoadImage → BuildLinear`，而旧版静态缓存没有释放钩子；这定位了执行路径与生命周期缺陷，尚未单独证明原生解码器崩溃的完整根因。详见 [VALIDATION.md](VALIDATION.md)。

## 构建

依赖由使用者现有安装提供，本目录不分发第三方程序集、不自动下载 NuGet 包。需要 Windows、PowerShell、已安装的 .NET SDK，以及 .NET Framework 4.7.2 reference assemblies。`build.ps1` 自动选择当前 SDK 的 Roslyn，支持以 `-FrameworkReferenceDirectory` 指定 reference assemblies 位置。

在本目录执行：

```powershell
$gameRoot = Read-Host 'KKS installation directory'
& ./build.ps1 -GameRoot $gameRoot
```

默认输出到系统临时目录并显示 DLL 的 SHA-256。也可用 `-OutputDirectory` 指定新的目录；已有同名 DLL 会被拒绝覆盖，输出不能写入本源码目录。不要将游戏 DLL 或生成物提交到仓库。当前自测清理修正版构建的 DLL SHA-256 为 `133f95ec4818469a8b77e1d476dcd14e391519279d2db2aff6dec33668e9e042`。

## 合成回归

```powershell
& ./tests/run-managed-regression.ps1
```

脚本使用当前 SDK 对应的目标框架，输出到系统临时目录。测试只编译公开生产方法与合成 API 替身，不读取游戏目录。旧行为夹具必须返回预期红灯标记和退出码23；候选必须通过13项断言，另通过6项自测渲染目标恢复断言。替身不执行 GPU、Harmony 实际挂载或 Unity 原生解码，不能替代实际运行验收。

## 安装、验证与回滚

构建完成不自动安装。正常退出 Studio 后，备份现有 `CodexMEColorSpaceFix.dll` 与对应配置，再把新构建的 DLL 放入 `BepInEx/plugins/`，保持同 GUID 插件只有一份。备份应放在 BepInEx 扫描目录之外，避免重复加载。

沿用原有 InitSetting 启动器与“激活控制台”。本目录不创建额外启动器、不关闭控制台、不改变场景或卡片。实际验证时可临时启用现有配置中的启动自测开关，或使用已配置的自测快捷键；自测调用和字段以 [MEColorSpaceFixPlugin.cs](MEColorSpaceFixPlugin.cs) 为准。

- `RoundTripTest`：灰阶 PNG 导入、线性副本、实际 ME 导出再读回；预期最大误差不超过1/255。
- `RuntimeStress`：256×8 ramp 和三轮4096×4096创建/缓存复用/释放，核对颜色属性保持、采样属性及 Token 计数恢复。
- 输出是游戏目录 `_codex_me_colorspacefix/` 下的 `me_roundtrip_report.txt` 与 `me_gpu_copy_runtime_report.txt`；只含合成自测结果。
- 最后重走原故障场景的角色替换和贴图操作，观察正常退出。合成测试通过仍不等于原场景通过。

自测结束后恢复启动自测开关为 false。回滚时正常退出 Studio，恢复 DLL 和配置备份；本补丁不持久修改用户贴图、卡片或场景。

## 许可与依赖边界

本目录新增/维护的自有源码、合成夹具和文档按 [MIT](LICENSE) 许可。Unity、BepInEx、HarmonyX 和 MaterialEditor 保留各自许可；它们的 DLL、游戏资产、真实日志和完整网页快照均不在此分发。来源与 advisory 检查边界见 [SOURCES.md](SOURCES.md)。
