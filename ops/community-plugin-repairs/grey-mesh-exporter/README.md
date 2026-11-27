# Grey Mesh Exporter KKS：导出、材质绑定与清理修复

这是 Grey Mesh Exporter KKS 0.1.5.6 的精确补丁工具，修复直接带 Renderer 的根节点遇到可选组件空引用、命名材质的 Albedo 贴图未绑定、较短 PNG 覆盖时保留旧尾部、纹理失败后未恢复渲染目标，以及同秒同名导出复用目录的问题。仅公开本项目的 helper、补丁／CIL 提取工具和合成夹具，不提供第三方完整 DLL 或反编译源码。

当前候选从受检原件通过公开入口精确重建，完成206项隔离观察检查并已安装。真实Unity/native导出直接Renderer根和正常命名材质的自造立方体通过：FBX拓扑和相对贴图正确，PNG颜色误差0，原配置与RT恢复，自有对象清理。前一版 `530dc95e…` 曾完成一次真实普通立方体导出，但使用无 Renderer 的 wrapper 根、子 cube 和空材质名；这次检查暴露的两个普通路径缺陷由当前候选修复。历史真实结果不作为当前候选验收。原插件导出仍阻塞主线程，详细证据与边界见[验证记录](VALIDATION.md)。

| 身份 | SHA-256 |
| --- | --- |
| 唯一受检原件 KKSMeshExporter.dll | `be9a83e1619a7bf332f7c0a54c3f88a5b920135b3c0096ebd2326c3d33238736` |
| 当前候选 | `0d983e64424b5baed8cf6d81adc6752876c9e77dd04b0645417958040d5247ad` |
| 前一版清理修复／普通路径对照 | `530dc95e00cfb34486687a3de85cb995d4f6ae1cacc7169d58c07b97dacd15d7` |
| 配套 x64 原生库（不修改） | `7aac45fdd42bd28145c15bdd8bc1b0d5ef8c4fe1112be0156d2f1d9a23b2b5e7` |

只改变九个原方法：WriteAllBytes、SaveTexture、ConvertToRGBNormalMap、Grey.Export、Fbx.ExportSkinnedMeshOrCloth、Fbx.ExportMesh，以及 ScanHierarchy、ExportTextures、AddTextureToMaterial；其他338个原方法保持，添加六个 helper 方法，不增加程序集引用。helper 嵌入候选，不能额外安装它的构建 DLL。

ScanHierarchy 仅修正根节点可选 FbxNodeCustomProperties 的存在判断，组件存在时应用属性。ExportTextures 将原 shader 属性名传给私有 AddTextureToMaterial，让 Albedo 分类按原始属性名判断；输出 PNG 仍保留材质名前缀及相对路径，两项材质开关的作用保持。唯一私有签名新增一个参数，唯一直接调用点同步修改；不按 Diffuse 通道笼统绑定，因为未知属性也会落到该通道。

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

脚本编译 helper、工具和自造运行时，生成候选并校验准确哈希，再执行原件62项、候选清理回归107项、旧版普通路径17项、当前普通路径17项和3项拒绝检查，总计206个观察结果。旧版普通路径必须重现5个失败，当前17项全部通过。原件62项刻意确认旧行为与缺陷，不能将原件组绿色理解为无缺陷。未知输入、已修订输入、现有输出／回执拒绝，不修改原件，不安装候选。

## 安装与回退边界

候选审核通过后，关闭游戏，确认目标没有同GUID／同名冲突，再备份任何既有目标和配置并核对哈希。64位KKS需要托管插件与匹配的 `UnityFbxExporter_x86_64_v16.dll`；原生库不被此工具修改。启动可能创建插件自己的配置，但不会自动导出。

首次导出应使用不覆盖现有作品的目标。回退只恢复有回执的旧插件／原生库／配置，或移出本次新增且哈希仍匹配的文件；不要删除整个 Export 目录或用户作品。安装成功不等于实际导出验证完成。详见 [验证范围](VALIDATION.md)与[来源边界](SOURCES.md)。

## 输出路径长度

参考Unity/Mono环境中，PNG绝对路径恰为260字符时File.Open失败；保持同一候选和测试内容，只缩短输出根后通过。请选较短的导出根目录，并避免过长的物件/材质名。本修复尚未提供完整Windows长路径支持；失败仍准确记录，不把未生成文件算成功。
