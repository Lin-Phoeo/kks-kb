# 验证与失败历史

参考检查执行实际原插件和候选的 CIL，Unity 纹理、渲染和 native FBX 是合成实现；System.IO 只访问独立临时目录。没有在游戏中执行真实导出或读取用户卡片。

| 范围 | 结果与含义 |
| --- | --- |
| 原件提取 CIL | 62项符合旧行为及缺陷预期，不代表原件安全 |
| 候选提取 CIL | 107项通过，涵盖截断、资源清理、蒙皮状态和输出目录 |
| 输入／输出拒绝 | 未知输入、已修订输入、既有输出三项拒绝，原件和旧输出保持 |
| 方法范围 | 只改6个原方法，其他341个原方法保持；helper6方法；程序集引用不增加 |
| 原生依赖静态检查 | x64库包含58个所需导出；参考机器具备VC++2010依赖；未执行LoadLibrary |

完整原算法方法 SaveTexture、WriteAllBytes、EnsureFolderExists、ConvertToRGBNormalMap、Grey.Export 被提取，helper 从候选导入。合成异常包括临时RT分配、Blit、ReadPixels、Apply、EncodeToPNG、空编码结果、PNG文件IO、转换GetPixels／SetPixels以及converted输出IO。每个候选故障仍传播，旧active被还原，取得的临时RT被归还，新建Texture2D被释放，输入纹理和旧目标不释放。

合法1×1 PNG载荷用于验证较长旧文件覆盖后逐字节相同，IEND位于文件末尾；这不是Unity颜色空间往返。转换only／both模式的基础和converted文件名保持。Grey.Export的完整控制流用合成FBX保存器验证所有目标与Textures目录一致、既有导出保留；它不证明native FBX内容正确。

目录使用GUID后缀降低同名碰撞，并检查目录／文件是否已存在；它不改变默认BasePath，也不清理原输出。测试不代表对恶意并发创建文件的安全沙箱承诺。

## 保留的失败

第一轮候选在catch改为rethrow后保留了不可达的尾部指令，提取运行遭InvalidProgramException拒绝；该候选没有安装。移除尾部后得到8a633候选，局部42/57检查通过，但独立审阅发现其新传播的错误会跳过上层蒙皮权重恢复，因此同样没有安装。最终候选增加最窄蒙皮finally和两处零网格检查，原件62/候选107对照通过。生成工具也曾在共同目录local门控上拒绝错误假设，确认实际local2后才继续输出。失败历史保留，不将其算为通过。

完整ExportSkinnedMeshOrCloth原CIL也在合成运行时执行，覆盖正常、材料／编码／Bake／ProcessMesh抛错、零网格、部分权重捕获、清零失败、cloth及无BlendShape分支。候选恢复已捕获前缀、清理自有Mesh和static引用；原异常继续传播。两调用处RequireMesh的精确位置由独立IL检查确认。

尚未观察真实Unity的正常导出、异常后的窗口状态、FBX导入建模工具、GPU资源或真实颜色误差；不能用172个合成观察结果替代这些结论。原插件的空sharedMesh.name赋名、BuildTextureExporter前置失败和创建Textures子目录失败后busy标志仍是既存边界，本轮未宣称覆盖这些路径。导出主线程阻塞也不改变。
