# BlendShapeEditor 0.6 与 Material Editor 5 兼容变换

BSE 0.6 的材质筛选桥接读取旧 `CurrentGameObject` 字段，并依靠旧 `PopulateList` 方法内部指令。经过重构的 ME5 使用属性和独立 presenter，导致 BSE 桥接初始化失败。这里提供针对准确输入的离线变换工具，保留 BSE 的形态键、笔刷和保存实现。

变换从 ME5 `SelectRenderers` 的真实返回值更新 BSE 列表，处理空结果与目标释放。只有启用材质编辑器筛选且 renderer 序列改变时才清理失效主选/多选；相同列表或关闭筛选时保留手选。当前对象属性元数据缓存一次。

## 使用

需要 Windows .NET Framework 4 编译器、已有 Mono.Cecil 和 Harmony DLL。测试另外需要 .NET 10 SDK。输入文件由使用者自行提供；仓库不包含第三方 DLL、反编译源码或用户数据。

```powershell
./prepare.ps1 `
  -InputBse '<original KKS_BlendShapeEditor.dll>' `
  -MaterialEditorPath '<reviewed KKS_MaterialEditor.dll>' `
  -OutputDirectory '<new directory outside the game and repository>' `
  -CecilPath '<Mono.Cecil.dll>' `
  -HarmonyPath '<0Harmony.dll>'

./verify.ps1 `
  -Candidate '<output directory>/KKS_BlendShapeEditor.dll' `
  -CecilPath '<Mono.Cecil.dll>' `
  -ArtifactsPath '<another new directory outside the game and repository>'
```

输入 BSE 必须为 `2212e3d27114fc83a578ffb6dec313642ea31e72381efc3f6a2c5427e96fbdfa`，ME 必须为 `9e72d250ca262df8a3c3aa93922113e3cdc0103ca1b22ee6cf9c44b04981278b`。其它构建不适用，不能单改哈希绕过检查。prepare与verify的输出均必须为新目录，拒绝仓库、识别到的游戏安装目录以及链接/联接路径，不执行安装。

输出 SHA256 固定为 `cf2c8a6b08c5148e2253dd8d6f32563f2320f70a7184b81ae3e80ac1372239dd`。插件版本仍为上游 0.6.0，准确本地变换身份以哈希和 `receipt.json` 区分。保留原始程序集引用，另加准确 ME5 引用；核心库维持原 `mscorlib 4.0`，没有将工具宿主核心库带入目标。

## 验证和限制

`verify.ps1` 从准确候选抽取四个桥接方法的实际 IL，放进自造 renderer/window 边界执行。12 项覆盖泛型列表构造、复制不别名、相同序列、重排、筛选开关、多选清理、空结果、空窗口及事件次数。抽取后的 `Probe.dll` 在单独目录运行，避免加载器误用未抽取的 fixture。

离线测试不执行Unity、真实ME presenter或BSC API。另有准确候选在参考KKS空Studio的45项原生验证，实际注册/更新、生产payload回读、旧owner和ME筛选均通过，清理后原状态恢复；两类证据分开保存。仍未自动迁移、保存用户旧卡或验证完整场景文件、真实衣装槽与笔刷操作。详见[VALIDATION.md](VALIDATION.md)、[原生摘要](runtime-validation.json)和[SOURCES.md](SOURCES.md)。
