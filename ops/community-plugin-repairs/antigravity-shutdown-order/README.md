# AntiGravity 退出时的挂钩拆除顺序

AntiGravity 的 `TimelinePhysics.Dispose` 直接调用 `UnpatchSelf`。目标版本的 Harmony 在拆除其 DynamicBone 前置挂钩后，会重新生成仍带 `AfterBone` 收尾挂钩的方法；后者的 `__runOriginal` 此时没有对应的局部变量，退出日志报 `Parameter __runOriginal does not contain a valid index`。

候选仅改变 `Dispose`：按构造时相同的签名定位 `UpdateDynamicBones(float)`，先用准确的 `AfterBone` 方法句柄拆除该挂钩，再执行原来的 `UnpatchSelf`。目标方法缺失时仍执行原有清理。所有异常继续向调用者传播。

物理算法、Timeline 插值、重力/外力恢复及 `AfterBone` 的 `__runOriginal` 条件保持原样。451 个方法中只改 1 个；不改字段、配置、资源、版本、插件 GUID、现有程序集引用或全局 Harmony。方法签名使用目标插件现有的框架作用域，避免从旧 Harmony 额外引入 `mscorlib 2.0` 引用。

## 精确输入与输出

- 原插件 SHA-256：`99b38b09709e09a65a4e3e78f4f11ba3745ced0e075f8f06476e66fed8a929ab`。
- 配套 Harmony SHA-256：`1a21cc03424fc82c3dd1346905d16494536b9595ae4162228d99fb7c285c1031`。
- 当前候选 SHA-256：`6ca7290a08415ed9cd88226eaaf13caf3a4b5ac70c66b33617b7a4847e12d913`。

只提供自有变换工具与合成测试，不包含第三方 DLL 或完整反编译源码。需要 .NET 10 SDK、.NET Framework 4.7.2 targeting pack、Python、用户现有精确输入及游戏自带 Mono 运行库。构建复用相邻 CameraFX 工具的自有 CIL 复制器和 DragDoll 工具的独立 Mono 宿主，不产生运行时插件依赖。

```powershell
py ops/community-plugin-repairs/antigravity-shutdown-order/run.py `
  --original /path/to/KKS_AntiGravity.dll `
  --game-root /path/to/KoikatsuSunshine `
  --output /path/to/new/private-verification
```

工具只输出到新的私有目录，拒绝覆盖输入、已有输出、游戏运行/内容目录、仓库目录、未知插件及未知 Harmony。它不会安装、启动或停止游戏。

## 证据边界

验证直接提取原件与候选的 `Dispose` CIL，在合成 Harmony 边界运行，覆盖拆除顺序、准确目标/准确 finalizer、重复清理、目标或挂钩缺失、其他 owner 保留及两种异常传播。原件 4 项通过、11 项失败；候选 15 项全部通过。另有 8 项拒绝检查。此处的 Harmony 类明确是合成协作者；不能据此声称真实 Harmony 机制或 Unity 退出已通过。

此外，入口把原件与候选的实际 `Dispose` CIL 分别复制到 .NET Framework 合成程序集，在独立进程内嵌入游戏自带 Mono，加载精确版本的真实 Harmony。目标骨骼类和 finalizer 都由夹具定义；不加载真实 AntiGravity、游戏角色或 Unity 对象。原件的 `UnpatchSelf` 稳定重现相同的 `__runOriginal` 异常，5 项观察检查通过（包含预期错误）；候选实际 `Dispose` 的 9 项检查通过，包括准确拆除、其他 owner 保留、目标可继续调用、重复卸载与清理。运行日志明确区分预期失败和修复结果。

这份证据来自真实 Harmony 在游戏 Mono 中执行实际候选 CIL，覆盖退出拆挂钩机制；实际 Studio/插件生命周期仍需单独验证。此前独立 .NET Framework/.NET 6/.NET 8 宿主在 Harmony 初始化时失败，不能用于解释目标异常，也不计入通过项。无需为此机制验证向游戏安装临时探针。

独立审查已复跑同一入口，核对原件预期异常、实际候选 CIL 和哈希。安装需等待使用者正常退出、备份准确原 DLL，再在单次协调启动/退出中核验。出现回归时仅恢复准确旧 DLL，保留用户配置和内容。

## 参考 Studio 退出验证 · 2026-11-28

准确候选 `6ca7290a…` 已在参考安装中另行部署并核对磁盘哈希。一次空 Studio 启动和正常关闭后，进程确已退出，最终日志没有再次出现 AntiGravity 的 `__runOriginal invalid index` 拆挂钩错误。关闭发出过两次正常窗口关闭请求；没有强制结束进程，也没有加载人物来扩大测试范围。

这次记录支持“该参考环境正常退出时未再触发目标异常”。它不证明所有物理功能、所有场景生命周期或所有控制台问题已经修复：同一日志仍保留此前的控制台 Invalid handle 和 JobTempAlloc 提示，未计入本修复成效。使用者停止了后续人物测试，没有新增角色功能通过结论；临时观察器已在退出后移出。
