# ExpressionStudio v3 验证范围

参考版本已备份安装；安装前无 Studio 进程，原件备份及目标新哈希均核对。未另装 helper、未改人物卡、配置或用户表情。真实 Unity 自然使用验证待观察。

## 实际 CIL 和生命周期对照

早期查找验证没有覆盖已销毁 Transform；扩展为每组 25 项后，v2 在 primary-prefix 和完整 EditorsFor 两条路径都会逸出异常，因此没有安装 v2。v3 使用实际 UnityEngine.Object.op_Equality 保护四个 Unity 对象，并调用 Component.GetComponent(Type)；它没有增加 catch 来吞掉业务故障。

| 对照 | 用例数 | 两角色 owner 返回数 | 已销毁 Transform 逸出 | 完整 EditorsFor 逸出 |
| --- | ---: | --- | --- | --- |
| 原件 | 25 | 0 / 0 | 否，原件没有 owner 查找 | 否 |
| v2（拒绝） | 25 | 1 / 1 | 是 | 是 |
| v3 | 25 | 1 / 1 | 否 | 否 |

三组共 75 项符合各自预期，公开脚本默认不依赖 v2，运行原件与 v3 共 50 项。覆盖已有 face-map 优先、两角色隔离、暂未就绪后重试、null／已销毁角色与 guide／transform／controller、错误 editor 类型、合法 Unity 类型扫描和去重、字符／静态／场景回退顺序，以及非生命周期业务异常仍传播。

夹具执行候选内的实际 helper、完整 EditorsFor CIL、primary-prefix 和完整 ScanUnity CIL；Unity 假空、native 调用、face-map 及 fallback callees 是合成实现。没有执行真实 Unity／Mono、编辑器变更、表情应用／捕获或场景重载。

## 独立元数据复核

比较 629 个原方法的指令、局部变量和异常区域，规范化短分支后仅 EditorsFor、ScanUnity 改变，其余 627 个不变。新增 helper 两方法、零字段、零异常处理器；程序集引用与 CLR 身份保持，未删除原方法。helper 含四次 Unity 假空比较和一次直接 GetComponent(Type)，不再通过 MethodInfo.Invoke 访问 Transform。

当前 KKSPE 元数据确认 BlendShapesEditor 是普通托管类，PoseController 继承 MonoBehaviour 且具有 `_blendShapesEditor` 字段。其 Awake 按自身 gameObject 与目标 guide transform 的 gameObject 关联角色，与新增查找路径一致。旧全局／静态／场景／Timeline／反射 fallback 保持，不将候选宣传为所有回退的角色隔离改造。

## 真实运行边界

正常安装只证明目标 DLL 身份和备份可回退。自然使用时仍需观察原非法类型扫描是否消失、角色更换和销毁后是否存在新异常，以及表情应用／捕获是否符合预期。当前没有这些新的实际功能结论。不得用 75 项提取测试、启动成功或其他字体版本的验收代替本项 Unity 运行证据。
