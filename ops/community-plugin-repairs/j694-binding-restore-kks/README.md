# J694 Binding Restore 1.3.0：KKS API 适配

原始 J694 Binding Restore 1.3.0 引用 `KKAPI, Version=1.43.0.0`，当前 KKS 提供 `KKSAPI, Version=1.48.1.0`。本工具只修改这一条 AssemblyReference，让事件、加载状态等 API 指向实际 KKS 程序集。作者的恢复规则、全部110个方法、字段、资源、GUID、配置键及版本保持。

本地输入来自用户提供的技术包，原说明称面向原 KK/.NET3.5，未提供可认证作者仓库地址。有限公开来源核对没有找到可认证的现成 KKS 发行，但不据此断言不存在。这里只公开自有定向工具和合成测试，不发布原DLL、修改DLL、第三方完整源码/反编译或游戏程序集，也不改变来源/许可判断。

| 身份 | SHA-256 |
| --- | --- |
| 唯一支持的原DLL | `1187a494a9f9d467c07db5c41d014c72f7a0771a7b31f1eb9b2fe92de832a033` |
| 本地KKS候选 | `2c10f57c6405e800a6a95d2fbf972f2e7b8891e14f49706dc3779ab04c6e6c20` |
| 已核KKSAPI 1.48.1 | `866464809d1bd04450a35850c23f9d17a0d1e5a166ed9f5dc525c506fc04ef01` |

GUID仍为 `local.koikatsu.j694bindingrestore`；区别本地适配版本要看哈希，不能只看1.3.0显示版本。

## 保持原意的恢复规则

插件在普通场景载入后等待20帧，记录已载入的J694物品与角色之间的动态骨碰撞勾选。之后原位换人、换整套衣装或切换坐标时，跟随KKPE刷新，在有限90帧窗口内复查并恢复。

恢复以**读场景时的快照**为准。临时手工勾选即时生效，但下一次换人/换衣仍会回到该快照；需要新基准时由用户保存并重新读取。这个端口没有将语义改成“后来手工编辑永远优先”。它不存自己的场景扩展数据、不主动保存角色/场景，也不将导入追加、新建角色、新增J694自动纳入旧基准。

唯一匹配的原身体/资源槽位骨骼恢复原值；新发型、衣服或饰品即使沿用同名骨骼路径，只要资源owner变化就视为新骨骼。默认新骨骼保持不勾；关闭 `Keep New Bones Unchecked` 后未知骨骼沿用KKPE当前状态。无法唯一匹配的骨骼不强行配对。

F1搜索 **J694 Binding Restore → Bindings**：`Enable Automatic Restore` 关闭后立即清空待处理恢复队列，已应用绑定保留。`Keep New Bones Unchecked` 控制未知骨骼策略。两项配置及原默认值不由工具覆盖。

## 复现验证

需要 .NET10 SDK、PowerShell、明确提供的原DLL、当前KKS参考目录以及其中 Mono.Cecil0.10.4。输出必须是仓库外的新目录；入口拒绝仓库任意位置、游戏根以及BepInEx/UserData/mods/abdata/*_Data运行或内容树，可使用独立工作目录或游戏下的私有.scratch。工具只读取传入依赖以核元数据；行为测试在完全合成的Unity/KKPE协作者中运行，不访问真实安装数据或卡片/场景。

```powershell
./ops/community-plugin-repairs/j694-binding-restore-kks/verify.ps1 `
  -OriginalPath '/path/to/reviewed/J694BindingRestore.dll' `
  -GameRoot '/path/to/Koikatsu Sunshine' `
  -ArtifactsPath '/path/to/new/private-verification-directory'
```

验证结果：原件284个managed member references中14项缺失；候选284项全部解析。当前KKPE2.21.5的6个所需类型、10个活动字段和3个反射方法匹配；四个动态Harmony目标的声明/参数匹配。候选的110个方法全部未变，字段和内嵌资源未变，其他程序集引用保持。

行为验证将104个原始/候选实际CIL方法提取到合成fixture；六个启动、配置绘制和Harmony装配入口在fixture里显式拒绝，不伪装运行了真实游戏启动。原件与候选各 **45检查/0失败**，覆盖快照定时、多个角色/碰撞器、同资源换装、不同owner、新/缺slot、Ver02身体标识、外部碰撞器、手工编辑与原基准、Import/Clear/删除/禁用、KKPE刷新、歧义匹配及旧版排除表。Unity原生组件和KKPE写入边界是合成协作者，快照/匹配/排程/恢复算法来自实际DLL，不是重写一份算法当测试。

未知原件、已修补输入、相同输入输出、已存在输出、错误API和未知提取输入共六个拒绝检查通过，输入字节保留。另有10个禁用输出路径与2个允许路径检查，不创建目录。证据写入 `verification-summary.json`、`contract.json`、`original.log` 与 `candidate.log`。

当前KKPE已会刷新新组件并按“Enable New Dynamic Bones”填值，但它使用组件引用及默认值，没有此插件的资源/slot/路径场景快照恢复。因此不把它称为完全重复功能，也不建议因缺一个程序集就修改KKPE本身。

## 安装、真实验证与回退边界

本入口不安装、不启动游戏。单独安装时先正常退出Studio，核同GUID只加载一次，备份准确旧DLL/已有配置，并只复制候选DLL。不得复制fixture、工具或旧KKAPI到游戏目录。

元数据与合成检查不证明真实人物替换、衣装载入、骨骼生成时序或碰撞效果。空Studio观察可检查Start成功、四个真实Harmony owner/patch方法以及当前反射类型，仍不能替代普通着装人物副本的手动测试。20帧捕获/90帧重试是原时间窗，更晚生成的骨骼可能不在本轮恢复范围内。

回退时关闭Studio，仅移动准确候选DLL出扫描目录；保留用户配置、卡片和场景。参考安装在本轮前没有该DLL，不能把依赖旧KKAPI的原KK输入直接放回KKS来充当回退。该插件的“关闭自动恢复”会保留已经应用的勾选，并不还原所有此前操作。不要把这种设计误写成卸载可逆转所有运行期间绑定。

## 参考运行记录 · 2026-11-28

准确候选已在参考KKS环境另行部署。空Studio观察29项/0失败：4个Harmony目标各挂载一次正确候选postfix，4个KKSAPI事件各订阅一次正确候选实例，反射adapter和两个开关默认值可读；真实自建、未激活DynamicBone/Collider上的绑定状态辅助方法和资源owner匹配通过，配置与原队列未变，自有组件已清理。检查没有创建人物、读取卡片/场景或广播换衣事件；用于验证集合写入的KKPE回调是自有协作者，不代表真实人物编辑器操作。

已有J694资源的Studio清单和Collider组件元数据也已确认，无需为了这项辅助插件新增资源。真实换人/换衣后的勾选与物理效果、超慢骨骼生成和磁盘场景回读仍未验；29项不能解释为这些人物功能已通过。最终运行回执确认进程正常退出、临时观察器已移出；没有新增资源或用户场景保存/加载。
