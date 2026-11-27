# 来源与公开边界

- [Unity Resources.FindObjectsOfTypeAll](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Resources.FindObjectsOfTypeAll.html)：只查询已加载的 UnityEngine.Object 派生对象。
- [UnityEngine.Object 的相等比较](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Object-operator_eq.html)：托管包装对象仍存在时，其 native 对象可能已被销毁，不能只用 System.Object 的 null 判断。
- [IllusionMods/HSPlugins](https://github.com/IllusionMods/HSPlugins)：KKPE 上游结构参考；实际兼容性以 README 中核验的 KKSPE DLL 元数据和方法签名为准。未在本目录复制上游源码或二进制。
- [Mono.Cecil](https://github.com/jbevain/cecil)：补丁写入和 CIL 提取所用工具库，由使用者显式提供，未下载或随仓库复制。

本目录的 helper、补丁／提取工具、合成夹具和验证脚本为本项目自行编写。完整第三方类的反编译、原始 DLL、真实日志、人物卡、表情及场景不随仓库公开。本目录不另外授予超出当前仓库条款的许可证，也不改变被修订插件或依赖的原许可。

兼容性哈希属于公开身份数据。生成候选需要使用者自己的受检原件，不能据工具公开就推定有权分发第三方修订后的完整 DLL。
