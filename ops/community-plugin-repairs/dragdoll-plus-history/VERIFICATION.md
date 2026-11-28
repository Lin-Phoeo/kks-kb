# 验证范围

入口为本目录 `run.py`。它将构建、提取、对照、审计和保护日志写到显式输出目录；`verification.json` 的 `installed` 和 `runtimeVerified` 固定为 false，构建和独立Mono绑定测试不能代表安装或真实人物操作。

## 真实方法与合成协作者

对照从精确原件/候选提取原 `FkHistoryCommand.Apply/Undo/Redo/Do`，以及原 `Commit` 与对应闭包。辅助方法体取实际 net472 产物，再嵌到合成探针；不是仅测试同源 net10 重编译实现。原 Commit 的成功、无效提前返回、异常回滚和 IK/FK 切换控制流保留。

Unity、人物控制器、FK 几何、快照构造、Bake 几何与 Timeline 是合成协作者；旋转使用数值四元数，不能证明真实 Unity 的逐帧求解、所有父级剪切、物理效果或真人姿势一致。Stiletto 用例证明 canonical 输入不被额外可见角度覆盖，没有运行真实 Stiletto。手型恢复探针使用简化值；实际原类型的五个只读值字段及构造/匹配/恢复签名另由元数据审计核对。

## 本次通过的检查

- 33项对照：活动 Undo/Redo；关闭后 FK 恢复及模式保持；连续两次 Undo/Redo；pending 旋转；骨盆、身高、旋转缩放父级；双角色；手型；未求解 canonical 输入；正常烘焙；失败 Commit；写入与 FK 评估异常回滚。
- 拒绝范围：控制器/衣装/骨架/父级/Guide 失效；重开会话；IK 模式变化；外部 FK、手型、输入位置、缩放；身高效果关闭；Timeline 播放/原轨道所有权；拖动后关闭前额外根、身高或手型修改。
- 原版33项中20项失败，候选33项全部通过；这20项包含可见撤销缺陷和新增拒绝语义，并非20个互不相关的原作者缺陷。
- 8项输出保护：未知输入、已修改输入、原地写入、已有输出、已有回执、已有辅助文件、错误辅助输入和哨兵完整性。原输入哈希保持。
- 元数据边界：原1921方法中只改3个；字段、资源保持；新增单个辅助程序集引用；56个非框架外部成员解析；正常公共 Commit 返回必须经过成功 hook，烘焙前捕获在 ResolvePendingEdits 之后。
- 精确反射契约：state/command/session 字段、cache、height、手型构造/匹配/恢复、身高重载、FK 评估与 Session 查找。
- 序列化PE作用域：helper TypeRef必须唯一且指向非零、已登记的AssemblyRef，并在Cecil解析到真实helper身份。
- 独立真实Mono绑定：用游戏Mono DLL在新进程加载候选与外部helper，从候选四个实际MemberRef token解析方法并用null哨兵调用。PreparingCommit/TryApply必须实际进入后按预期拒绝null，Capture/Committed沿各自边界处理；不创建角色，不调用Unity求解。新候选四项全过；故意把Commit/Apply的helper scope改回本模块的控制为四项三败，失败类型为TypeLoadException。旧45fb原试验字节另跑同探针，也四项三败。control不安装。

首次简单端点实现曾在独审“最后拖动后、关闭前另改根位置或手型”两例失败，已由完整烘焙前端点保护替换。后补缩放和身高状态保护。旧试验产物不准部署。

旧45fb后在真实游戏出现类型解析失败，原因是手工构造了未登记的AssemblyNameReference，写盘后部分TypeRef变成本模块scope。之前合成探针把helper反嵌到同一程序集中，消除了原本需要验证的外部边界，故未发现。现在普通ImportReference修复scope，审计检查写盘后实际元数据，另增真实Mono外部绑定红绿对照；旧45fb已撤销准入并回退原件。不能沿用它的“离线通过”结论证明真实运行成功。

## 复现与实机后续

构建禁用 Git 来源查询和 SourceLink，保持映射后的确定性源码路径。仓库构建和脱 Git 副本应生成 README 中同一对哈希；此要求限于受检 SDK 与精确依赖，不声称更换编译器仍必然同字节。

2026-11-28 Run11 在真实游戏空Studio对最终fe52/813d完成15项检查，0失败：初始空场景、插件已加载、两文件hash、四个真实MemberRef的解析与helper入口调用、配置保持、光标保持、最终空场景。观察器使用null哨兵，不建角色、不修改用户场景或Undo栈。日志中Committed的ArgumentNullException与Capture的NullReferenceException是这次封闭null调用产生的预期诊断，位于对应DD_BINDING解析/调用成功记录之间；未复现helper TypeLoad。结果只确认游戏内程序集边界及入口可执行，不能推导正常人物状态快照/Commit/Undo业务已通过。正常退出结论与真人视觉结论分别记录，不由这15项代替。

实机需用户在普通着装角色上暂停 Timeline，先确认活动拖动 Undo/Redo，再重新轻拖、松开、关闭 PLUS，确认 Undo/Redo 恢复对应可见姿势且仍为 FK、相机正常；最好复验连续两次拖动。日志中的 `DD_HISTORY_INACTIVE_UNDO/REDO` 是 hook 命中证据，不能独立证明画面正确。正常关闭退出和用户视觉确认分别记录。尚未完成这些检查前维持实机待验状态。
