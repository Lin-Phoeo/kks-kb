# Grey Mesh Exporter KKS：导出与纹理清理保护

这是 Grey Mesh Exporter KKS 0.1.5.6 的精确补丁工具，修复较短 PNG 覆盖时保留旧尾部、纹理失败后未恢复渲染目标，以及同秒同名导出复用目录的问题。仅公开本项目的 helper、补丁／CIL 提取工具和合成夹具，不提供第三方完整 DLL 或反编译源码。

当前候选已完成独立复核，并在一套参考安装中按精确哈希新增托管插件与x64依赖；未触发导出。实际Unity导出、FBX可用性及GPU颜色往返仍待验证。原插件在导出期间会阻塞主线程，本修复不改变该性能特征。

| 身份 | SHA-256 |
| --- | --- |
| 唯一受检原件 KKSMeshExporter.dll | `be9a83e1619a7bf332f7c0a54c3f88a5b920135b3c0096ebd2326c3d33238736` |
| 候选 | `530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7` |
| 配套 x64 原生库（不修改） | `7aac45fdd42bd28145c15bdd8bc1b0d5ef8c4fe1112be0156d2f1d9a23b2b5e7` |

只改变六个原方法：WriteAllBytes、SaveTexture、ConvertToRGBNormalMap、Grey.Export、Fbx.ExportSkinnedMeshOrCloth、Fbx.ExportMesh；其他341个方法保持，添加六个 helper 方法，不增加程序集引用。helper 嵌入候选，不能额外安装它的构建 DLL。

PNG 写入使用 Create 截断已有目标。IO／编码失败传播到原有的导出失败处理，不再忽略贴图失败后继续报告导出完成。纹理读取仍用原格式、线性选项和法线转换算法，清理在 finally／fault 执行：还原旧 active、归还临时 RenderTexture、只释放本函数新建的 Texture2D。

蒙皮导出在捕获每个权重后、清零前登记捕获数量；发生材料／编码／网格失败时，finally恢复已捕获的原权重、释放自有烘焙Mesh，并只清理本次使用的静态引用。未读到的权重不以默认0覆盖。普通和蒙皮两条网格路径都拒绝ProcessMesh返回的零句柄，避免内部失败后仍保存成功。

唯一目录在原 Grey.Export 的共同目录入口创建，增加随机后缀；FBX 与 Textures 共用此目录，文件名及 FBX 内部相对贴图路径保持。旧导出作品不删除或复用。

## 显式构建和复跑

需要 Windows、PowerShell 7、.NET SDK 10、系统 .NET Framework 4.x 编译器，以及使用者自己的受检插件原件、Mono.Cecil 和 KKS UnityEngine.CoreModule。项目没有 PackageReference，不会下载游戏依赖。参考环境使用 Mono.Cecil 0.10.4.0；不同构建结果由固定候选 SHA 拒绝，不能静默生成另一个未审版本。

从本目录执行，输出到一个不存在的外部目录：

```powershell
./verify.ps1 `
  -InputPath '<supported-original>/KKSMeshExporter.dll' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -UnityCorePath '<installation>/CharaStudio_Data/Managed/UnityEngine.CoreModule.dll' `
  -ArtifactsPath '<new-external-directory>'
```

脚本编译 helper、工具和自造运行时，生成候选并校验准确哈希，再执行原件62项、候选107项和3项拒绝检查，总计172个观察结果。原件用例刻意确认旧缺陷，不能将原件组绿色理解为无缺陷。未知输入、已修订输入、现有输出／回执拒绝，不修改原件，不安装候选。

## 安装与回退边界

候选审核通过后，关闭游戏，确认目标没有同GUID／同名冲突，再备份任何既有目标和配置并核对哈希。64位KKS需要托管插件与匹配的 `UnityFbxExporter_x86_64_v16.dll`；原生库不被此工具修改。启动可能创建插件自己的配置，但不会自动导出。

首次导出应使用不覆盖现有作品的目标。回退只恢复有回执的旧插件／原生库／配置，或移出本次新增且哈希仍匹配的文件；不要删除整个 Export 目录或用户作品。安装成功不等于实际导出验证完成。详见 [验证范围](VALIDATION.md)与[来源边界](SOURCES.md)。
