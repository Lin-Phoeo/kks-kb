# DragDoll PLUS 关闭后的姿势撤销

DragDoll PLUS 0.2.42 把拖动命令放进 Studio 原生撤销栈。关闭插件会把可见姿势烘焙到 FK 并释放会话；旧命令随后退回原生 IK 目标写入，可能出现撤销栈已移动而人物姿势没有恢复。本修复让已捕获且仍属于该角色的历史在正常关闭后恢复对应 FK 姿势，保持关闭后的 FK 模式，不重启旧 IK 求解器。

唯一支持的输入：GUID `com.freelancer604.dragdoll`，版本 `0.2.42`。同版本其他字节也拒绝，必须重新审查。

```text
原版 KKS_DragDollPLUS.dll
2d5ce76b35347e64f5fa6b5df65ab8ad90c343f043407160fc6b76dad4cec065
候选 KKS_DragDollPLUS.dll
fe52ab2b851d301232d51803dc72be70400262b9b4a5906bd92096f0c2c64a4f
配套 Codex.DragDollPlusHistory.dll
813d8eed6575c4bceec3e292f6d76e17f6b058267423699645f169385ed0dfdf
```

## 边界与取舍

补丁仅在原历史快照构造、正常 Commit 和历史 Apply 三处接入。活动会话继续原方法；正常关闭前记完整端点，关闭后记实际 FK 基线。连续 Undo/Redo 只在端点相邻且实际状态仍匹配时执行，恢复骨骼 FK、输入位置、根位置、身高通道、骨盆位置、IK 目标和原命令捕获的手型。

原作者的 canonical cache 用于 FK 旋转，避免把高跟鞋等额外可见修正重复烘焙。手型比较仅针对已核对的原 `NativePeHandShape` 值类型：三个整数、一个布尔和一个浮点只读字段；不承诺任意插件手型对象都可按同样方式比较。当前 FK 评估沿用作者的 `NativeFk.Evaluate` 入口，不复活已释放的会话。

以下变化会使该会话的关闭后历史失效：角色、控制器、身体、衣装对象、骨架或 Guide 身份变化；重新开启 PLUS；FK/IK 模式变化；外部旋转、位置、手型、缩放或父级变换变化；身高效果启用状态变化；Timeline 播放或历史捕获时已有相关轨道。额外修改发生在最后一次拖动与关闭之间时，也拒绝覆盖。拒绝后记录 `DD_HISTORY_INACTIVE_REFUSED`，不会执行该命令的原 IK 回退；Studio 原生栈仍会移动，因此保护性拒绝不等于撤销成功。

写入中异常会尝试恢复写前值并停用这份关闭后历史。`DD_HISTORY_ROLLBACK_FAILED` 表示恢复本身也失败，应停止继续操作并保留日志。保护针对已捕获的身份和值；任意第三方插件、动画、物理和渲染修正不能由离线测试穷尽。helper不继承MonoBehaviour，没有Update、LateUpdate、协程或轮询；快照仅在历史构造、Commit及Undo/Redo事件边界捕获。每次捕获会按角色骨数复制字典/位置/手型并作反射调用，存在分配与CPU成本，尚无真实典型角色耗时数据，不能声称零开销。

## 构建与验证

需要 Windows x64、.NET 10 SDK、.NET Framework 4.7.2 targeting pack、Python 3.12+ 和调用者合法持有的 KKS/BepInEx/Mono.Cecil 依赖，以及游戏附带的 `MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll`。

```powershell
py -B ops/community-plugin-repairs/dragdoll-plus-history/run.py `
  --original 'E:/ReviewInputs/KKS_DragDollPLUS.dll' `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --output 'E:/BuildArtifacts/dragdoll-history-review'
```

输出必须为新的仓库外目录，不能是游戏运行、配置或内容目录。脚本只构建和验证，不安装、不启动游戏、不读取人物卡。部署文件仅为输出 `candidate/` 中上述两份 DLL，不能把 Probe、工具或合成依赖装进游戏。

当前离线证据为33项真实历史命令及 Commit CIL 对照：原版20项失败，候选0失败；8项拒绝/保留保护通过。原1921方法仅3个变化，1918个保持，原字段和资源保持；新增引用仅为辅助程序集，56个外部成员在真实依赖元数据中解析成功。序列化PE中的helper TypeRef必须指向唯一已登记非零AssemblyRef，并解析到精确helper；构造和反射签名另行核验。详见 [验证边界](VERIFICATION.md)。

旧 `45fbd442…30663` 试验候选曾通过反嵌helper的合成用例，却在真实游戏触发TypeLoadException；它的部分TypeRef错误指向本模块，必须禁止部署并退回原版。现修复使用Cecil的正常ImportReference，并新增独立进程的游戏Mono加载验证：读取真实候选四个MemberRef并解析、调用外部helper入口（null哨兵，无角色）；错误scope控制及旧45fb均四项中三项失败，新fe52四项全部通过。该测试保留真实程序集边界，但不执行真实人物Commit/拖动业务。

2026-11-28 Run11 已在真实游戏空 Studio 安装上列 fe52/813d pair，并完成15项入口检查：精确文件身份、四个MemberRef解析与对应helper入口调用均通过，配置和光标保持、场景前后为空，0项失败。使用null哨兵，没有创建人物或操作撤销栈；Committed和Capture产生的两条预期空参数诊断已按观察器调用范围记录，未出现helper类型解析失败。不能将这两条诊断解释成真实人物失败，也不能把整段游戏日志概括为无错误。

真实用户已确认原版“开启时可撤销，关闭后不能恢复可见姿势”。**新pair的真实游戏程序集入口已验，人物姿势Undo/Redo仍待用户复验，尚未完成实机修复验收。** 其他安装应在 Studio 退出后备份原件，再把两份 DLL 作为一组替换；回退恢复上列原版，并移出该辅助 DLL。不得恢复旧的试验候选，也不修改用户角色、场景或配置以取得通过结果。

## 来源

见 [SOURCES.md](SOURCES.md)。本目录仅含自有辅助代码、精确补丁工具、合成协作者和说明，不包含原插件、修改后的插件 DLL、游戏反编译类、人物或资源。许可范围见 [LICENSE](LICENSE)。
