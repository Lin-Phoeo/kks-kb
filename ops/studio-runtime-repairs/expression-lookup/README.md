# ExpressionStudio：角色编辑器查找与失效对象保护

v3 已在一套参考安装中备份安装，候选哈希核对通过；真实 Unity 自然使用结果仍待观察。v2 会在已销毁 Transform 上逸出异常，已拒绝安装。这里仅提供本项目编写的补丁工具、辅助逻辑、合成夹具和验证说明，不包含插件 DLL、反编译文件、用户表情或日志。

原插件把普通托管类型 `HSPE.AMModules.BlendShapesEditor` 传入 `Resources.FindObjectsOfTypeAll`，但该 Unity API 只接受 UnityEngine.Object 子类。当前 KKSPE 中该编辑器继承 AdvancedModeModule，再继承 System.Object。

修复只改变 `KkpeBridge.EditorsFor` 和 `ScanUnity`：先保留已有 face-map 结果；没有结果时通过请求角色的 guide transform 获取 PoseController，再读取它的 `_blendShapesEditor`。角色、GuideObject、Transform 和返回的控制器都先经过 Unity 假空检查。只有确属 UnityEngine.Object 的编辑器类型才进入原场景扫描。

未就绪或已销毁对象不贡献结果，既有回退链继续。helper 无字段、无缓存、无异常吞并；不保留角色或控制器引用。原来的字符、静态、场景和其他回退逻辑保持，因此不能声称所有回退结果都限定于请求角色。该补丁不修改表情应用、保存或场景格式。

## 受检身份

| 输入或产物 | SHA-256 |
| --- | --- |
| ExpressionStudio 0.9.69 原件 | `a3a8422471cfd71b44d4199caac932809b74d48c8abd809678c580d295e58893` |
| 已拒绝的 v2 对照 | `512b889ca694f973ceab440f49a88a74978fed988f8ae3e4f45012a4ba21f387` |
| v3 候选 | `55ec9023c84aaa775cb460d38e95d42348d652411b0431084c124eac8172f356` |
| 配套核对的 KKSPE 2.21.5 | `1c8e2b66ccd613b0a0fb6cb794fb0221b8bc427356c5f30724ba50cf0a4ee92f` |

未知输入、已修订输入或已经存在的输出／回执会被拒绝。工具核对 v3 输出哈希后才写文件。辅助类嵌入候选内部，不需要另装 helper DLL；CLR 仍为 v4.0.30319，程序集引用不增加。

## 显式构建和验证

需要 Windows、PowerShell、.NET SDK 10、系统 .NET Framework 4.x 编译器，以及使用者自行提供的受检 ExpressionStudio 原件、Unity CoreModule 和 Mono.Cecil。项目不下载游戏依赖；参考构建使用 BepInEx 随附的 Mono.Cecil 0.10.4.0。其他构建输入若产生不同输出，将被候选哈希检查拒绝。

从本目录运行，产物放到一个新的外部目录，不直接指向游戏安装：

```powershell
./verify.ps1 `
  -InputPath '<supported-original>/KKS_ExpressionStudio.dll' `
  -CecilPath '<installation>/BepInEx/core/Mono.Cecil.dll' `
  -UnityCorePath '<installation>/CharaStudio_Data/Managed/UnityEngine.CoreModule.dll' `
  -ArtifactsPath '<new-external-directory>'
```

默认运行原件和 v3 两组各 25 项，共 50 项。维护者如果已经留存准确 v2 对照，可额外传 `-V2InputPath '<archived-v2>/KKS_ExpressionStudio.dll'`，运行三组共 75 项。v2 不随包提供，也不是普通用户使用修复的前提。

脚本编译自有 helper／工具／合成夹具，在外部目录生成 v3 和回执，然后提取实际 primary-prefix、完整 EditorsFor 与 ScanUnity 的 CIL 到合成运行时执行。原件和 v2 的绿色结果表示准确复现它们各自的已知缺陷，不表示旧代码安全。详见 [验证范围](VALIDATION.md)。

## 安装与回退

构建不会安装或启动 Studio。安装前关闭 Studio，确认目标原件仍匹配受检原件哈希，复制到新的备份位置并核对备份，再只替换该 ExpressionStudio DLL 并核对 v3 哈希。回执记录原件、候选和备份身份。人物卡、场景、配置及表情预设均不改动。

回退同样要求 Studio 关闭且目标仍是受检候选，恢复准确的原件备份后再次核对哈希。未知目标哈希应停止，避免覆盖后续更改。

当前参考安装已完成上述备份与替换，后续从自然使用日志记录新证据。用户已经结束字体手工验收，本修复不自动触发新一轮真人验收请求，也不把安装成功写成 Unity 功能通过。来源与代码分发边界见 [SOURCES.md](SOURCES.md)。
