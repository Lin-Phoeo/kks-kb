# 维护者构建

源码需要 .NET SDK 和 .NET Framework 4.7.2 targeting pack，引用用户本机已装 BepInEx/Unity 程序集。GameRoot 必须显式传入。终端从本目录运行。

~~~powershell
dotnet build sources/configuration/StudioConfigLocalization.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/configuration"
dotnet build sources/imgui-route/StudioImGuiRouteAdapter.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/imgui-route"
dotnet build sources/imgui-dynamic/StudioImGuiDynamicBridge.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/imgui-dynamic"
dotnet build sources/native-text/StudioNativePluginText.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/native-text"
dotnet build sources/sidebar/StudioPanelTypography.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/sidebar"
dotnet build sources/font-controller/StudioFontController.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/font-controller"
dotnet build sources/animorph-layout/AniMorphLayoutCompat.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/animorph-layout"
dotnet build sources/coordinate-native/StudioCoordinateNative.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/coordinate-native"
~~~

将审核过的输出 DLL 放到一个游戏目录外的 artifacts 目录。release-components.json 记录预期哈希，重新编译后必须先验收并更新该清单。

## 官方 Core 修订

从 XUnity 官方 v5.6.3.1 BepInEx 发布包提取 Core，再运行精确补丁工具。工具验证官方输入 SHA，仅将错误的 System.Type.GetType 调用替换为 Nop，保持官方 GUILayout.Window 解包。官方包来源和许可在 SOURCES.md。

~~~powershell
dotnet build sources/core-patch/TypeFix.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/core-tool"
dotnet "E:/StudioZhBuild/core-tool/TypeFix.dll" patch "E:/OfficialXUnity/XUnity.AutoTranslator.Plugin.Core.dll" "E:/StudioZhBuild/core-output"
~~~

## 安装器回归与发布 ZIP

~~~powershell
py -B -m unittest discover -s tests -p "test_*.py" -v
py -B build_release.py --artifacts "E:/StudioZhBuild/artifacts" --output "E:/StudioZhRelease" --version "0.3.0-rc.1"
~~~

LocalTools 使用 .NET 10，引用用户已有 Mono.Cecil，运行时从游戏目录解析该依赖。正式 ZIP 附本项目工具 DLL、deps 和 runtimeconfig，用户只需 .NET 10 Runtime。

~~~powershell
dotnet build tools/LocalTools/LocalTools.csproj -c Release -p:GameRoot="E:/Games/Koikatsu Sunshine" -o "E:/StudioZhBuild/artifacts/tools"
~~~

LocalTools 的 import/encode/rules 入口调用实际 XUnity parser，native 入口仅修改受检插件常量。公开动态桥目录只从清洁包中的词表重建。导入本地复用值后，local_import.py 自动重新生成桥接目录，避免读取私人缓存。

发布前扫描 ZIP 清单和源代码差异。公开源树不包含游戏目录结构、真实日志、截图、字体和第三方图标资源。正式 ZIP 里的目标路径由 manifest 字段指定，neutral payload 路径只保存本包可发布内容。

## 性能回归与可选安装检查

从仓库根目录运行以下纯托管回归；不读取游戏、配置或字体：

~~~powershell
dotnet run --project ops/studio-localization/tests/managed-dynamic-performance -c Release -- --rules ops/studio-localization/translations/dynamic-original.json
dotnet run --project ops/studio-localization/tests/managed-font-performance -c Release
dotnet run --project ops/studio-localization/tests/managed-font-catalog -c Release
dotnet run --project ops/studio-localization/tests/managed-typography -c Release
dotnet run --project ops/studio-localization/tests/managed-font-panel -c Release
~~~

耗时只比较相同运行条件下的函数，不代表游戏帧率。字形缓存保持缺字回退，动态缓存保持作用域、优先级、长度和容量边界。

中文目录测试用内存生成的 SFNT/name/cmap，样式测试用实际状态与嵌套作用域实现；两者不访问用户安装。`tools/verify-font-catalog/` 是单独显式运行的 Windows 字体/GDI 核验工具，不属于常规测试，不复制字体文件。

`tools/verify-configuration-performance/` 和 `tools/verify-coordinate-native/` 是显式安装验收工具，需要使用者授权提供本机依赖，常规自动测试不运行它们。前者隔离调用配置适配器并对比查询分配，后者只读核对特定插件的资源及 IL、调用候选前缀；两者均不替代真实游戏窗口验收。

`tools/verify-font-faults/` 显式读取指定的受检依赖及候选 DLL，以合成配置调用实际故障路径，不修改用户配置，也不代替真实 Unity 字体／GPU 释放验收。
