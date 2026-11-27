# 验证与失败历史

下表是当前 `0d983e64…` 候选的隔离回归：执行实际原插件和候选的 CIL，Unity 纹理、渲染和 native FBX 是合成实现；System.IO 只访问独立临时目录。当前候选尚未安装或进行真实游戏验证。前一版 `530dc95e…` 的真实普通立方体检查作为历史证据单独记录如下。

| 范围 | 结果与含义 |
| --- | --- |
| 原件提取 CIL | 62项符合旧行为及缺陷预期，不代表原件安全 |
| 候选提取 CIL | 107项通过，涵盖截断、资源清理、蒙皮状态和输出目录 |
| 前一版普通路径对照 | 17项中5个失败、12个通过；精确重现根节点空判断及命名材质漏绑定 |
| 当前普通路径回归 | 17项全部通过，保持PNG命名、分类与材质开关行为 |
| 输入／输出拒绝 | 未知输入、已修订输入、既有输出三项拒绝，原件和旧输出保持 |
| 方法范围 | 只改9个原方法，其他338个原方法保持；helper6方法；程序集引用不增加 |
| 原生依赖静态检查 | x64库包含58个所需导出；参考机器具备VC++2010依赖；未执行LoadLibrary |

普通路径回归直接提取 ScanHierarchy、GetTexture、ExportTextures、两种分类方法、AddTextureToMaterial 和实际 Data 静态初始化器。17项覆盖：直接 Renderer 根有／无可选属性、无 Renderer 根、四种材质名配合三种属性、两项材质开关。材质名包括普通名称、空名、含 `_MainTex` 的名称和中文名称；属性包括 `_MainTex`、`_NormalMap` 和未知属性。旧版5个失败分别是无组件空引用、有组件未应用，以及三个非空材质名称未绑定 Albedo。未知属性即使回落到 Diffuse 通道仍不得当成 Albedo 绑定。

该夹具的层级预处理和原生 FBX 委托仍是替身，不证明真实场景层级的所有权或原生导出效果。方法范围通过原件 token 对应的方法对象、重写后 token 与签名映射检查，避免 Cecil 重排 MethodDef 表造成误判；另以声明类型内方法顺序独立核对。唯一签名变化为私有 AddTextureToMaterial 增加属性名参数，DLL 中唯一直接调用点同步修改，未找到方法名字符串反射或委托引用；未对外部插件的动态反射作保证。原 Data 初始化器和程序集引用保持。

另一次独立复核重新读取原件、旧版和当前候选，确认9/338及3/350方法边界、私有签名与唯一调用点、原 Data 初始化器不变；独立重跑旧版17项（5个预期失败）、当前17项及107项清理回归。未发现阻止进入真实 Unity 定向验收的问题；复核未安装或启动游戏。

完整原算法方法 SaveTexture、WriteAllBytes、EnsureFolderExists、ConvertToRGBNormalMap、Grey.Export 被提取，helper 从候选导入。合成异常包括临时RT分配、Blit、ReadPixels、Apply、EncodeToPNG、空编码结果、PNG文件IO、转换GetPixels／SetPixels以及converted输出IO。每个候选故障仍传播，旧active被还原，取得的临时RT被归还，新建Texture2D被释放，输入纹理和旧目标不释放。

合法1×1 PNG载荷用于验证较长旧文件覆盖后逐字节相同，IEND位于文件末尾；这不是Unity颜色空间往返。转换only／both模式的基础和converted文件名保持。Grey.Export的完整控制流用合成FBX保存器验证所有目标与Textures目录一致、既有导出保留；它不证明native FBX内容正确。

目录使用GUID后缀降低同名碰撞，并检查目录／文件是否已存在；它不改变默认BasePath，也不清理原输出。测试不代表对恶意并发创建文件的安全沙箱承诺。

## 保留的失败

第一轮候选在catch改为rethrow后保留了不可达的尾部指令，提取运行遭InvalidProgramException拒绝；该候选没有安装。移除尾部后得到8a633候选，局部42/57检查通过，但独立审阅发现其新传播的错误会跳过上层蒙皮权重恢复，因此同样没有安装。最终候选增加最窄蒙皮finally和两处零网格检查，原件62/候选107对照通过。生成工具也曾在共同目录local门控上拒绝错误假设，确认实际local2后才继续输出。失败历史保留，不将其算为通过。

完整ExportSkinnedMeshOrCloth原CIL也在合成运行时执行，覆盖正常、材料／编码／Bake／ProcessMesh抛错、零网格、部分权重捕获、清零失败、cloth及无BlendShape分支。候选恢复已捕获前缀、清理自有Mesh和static引用；原异常继续传播。两调用处RequireMesh的精确位置由独立IL检查确认。

## 前一版的真实普通立方体导出（历史证据）

受检托管候选为 `530dc95e…`，原生x64库为 `7aac45fd…`。空Studio对象数为0，一次性观察器只创建自己的wrapper根、子cube、克隆Mesh、材质和2×2纹理，调用真实Grey.Export及原生FBX库；没有载入卡片、保存场景或导出用户对象。

- 生成二进制FBX 7400，21,232字节；独立节点解析394个节点、1个Mesh geometry、2个Model、24顶点、36索引、12多边形，坐标有限、索引有效，贴图相对引用对应实际PNG。
- PNG为2×2，IEND位于文件末尾；Unity LoadImage读取黑／红／绿／白四角，RGBA最大字节误差为0。
- 导出器在观察器清理前已自行恢复原RenderTexture.active；ConfigEntry引用恢复失败0、配置文件字节前后相同、自有对象全部销毁、清理失败0，Studio对象数仍0。
- 随后通过正常主窗口关闭退出，最终进程数0，临时观察器已移出插件目录。

精确结果文件SHA-256为 `30d45559112a8dd9e8ba6718ef162f0bc6aeb026c00bb3b8f9da1c9c2ee46fc1`；公开仅保留这个身份和脱敏指标，不附私人路径、运行令牌或原日志。

wrapper根无renderer（createRoot=false），用于避开前一版直接renderer根的FbxNodeCustomProperties空判断缺陷；自有材质名为空，使旧material-name+property-name的exact albedo比较正确命中_MainTex。该夹具明确限制了历史结论，其他命名材质绑定不算真实通过。当前候选的下一项实测应使用直接带Renderer的cube根和非空材质名。FBX节点／拓扑解析不是Blender或Assimp完整导入；未覆盖真实人物、蒙皮／cloth、导出失败后的游戏状态或真实GPU故障注入。原插件的空sharedMesh.name赋名、BuildTextureExporter前置失败和创建Textures子目录失败后busy标志仍保留为既存边界，导出主线程阻塞也不改变。

## 当前候选真实普通路径对照

0d983e候选使用直接MeshRenderer根与CodexNamedMaterial，原长隔离目录导致260字符PNG路径失败；目录本身存在，错误在File.Open。只缩短输出根后相同候选/fixture通过：FBX7400、395节点、24顶点/36索引/12多边形，命名贴图相对链接正确；PNG2×2经UnityLoadImage回读最大RGBA差0。RT由导出器恢复，配置字节不变，临时cube/克隆mesh/材质/纹理全部销毁，Studio对象数0。该对照不含人物、蒙皮、cloth、复杂材质或Blender导入；Windows长路径支持未实现。
