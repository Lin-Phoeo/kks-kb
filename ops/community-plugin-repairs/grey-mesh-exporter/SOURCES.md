# 来源与代码边界

- 原作者分发目录：[Grey Mesh Exporter Builds](https://www.mediafire.com/folder/erkqga33v7deu/Grey_Mesh_Exporter_Builds)。参考原包 `GME-KKS-Quick_Install-BEPINEX.zip` SHA-256 为 `e9b7094cee5bd4c4b84b87f6e385d2e7eae4143b83151461c4f5e1c893534c14`。包内实际 BepInPlugin GUID 为 `Grey.MeshExporter.KKS`，版本0.1.5.6；未据搜索结果推定它是当前最新版本。
- [System.IO.FileMode](https://learn.microsoft.com/dotnet/api/system.io.filemode)：OpenOrCreate不会自动截断已有文件；Create截断目标。
- [Unity RenderTexture.active](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/RenderTexture-active.html)、[ReleaseTemporary](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/RenderTexture.ReleaseTemporary.html)：保存并恢复调用前状态，归还本次取得的临时目标。
- [Mono.Cecil](https://github.com/jbevain/cecil)：由操作者显式提供，不下载或复制到公开目录。

本项目只公开新编写的 helper、定位／修改／提取工具及合成夹具。工具按准确原件哈希识别少量 IL 调用点，执行时才从使用者原件提取待测算法；公开源码不包含完整第三方反编译类或原始资源。第三方DLL、native库、字体、人物卡、日志和本机路径均不分发。

本目录不额外授予超出仓库条款的许可证，也不改变原插件或依赖的许可。公开补丁工具不等于允许重新分发第三方完整修订DLL。
