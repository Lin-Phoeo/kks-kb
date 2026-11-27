# 上游与分发范围

- [Clo15/CloXray](https://github.com/Clo15/CloXray)：插件官方仓库。
- [CloXray v1.1.6](https://github.com/Clo15/CloXray/releases/tag/v1.1.6)：本补丁支持的 KKS_CloXray.zip。归档包和包内 DLL 精确哈希见 README。
- [IllusionMods/IllusionModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI)：KKAPI 场景导入契约。Import 的 `loadedItems` 用源场景原 ID 作键、实际新对象作值，不能用源 ID 直接查询全局对象表。
- [Mono.Cecil](https://github.com/jbevain/cecil)：运行时由使用者提供的程序集读写工具。本验证固定使用 0.10.4.0，以复现已检查的二进制输出。

上游 CloXray 发行包包含 GPL-3.0 许可，作者注明相关物件网格来源于 GFanon 的作品。此目录不重新分发上游 DLL、完整反编译源码、网格、shader、资源包或示例场景；也不把可下载等同于允许任意转载。使用者自行取得官方文件，补丁器在本机读取这些文件并输出派生候选。若另行分发派生二进制，应履行对应许可义务。

本目录中的补丁器、CIL 提取映射器、合成替身和检查脚本是本次自有实现。类型名、字段名及方法签名用于定位和验证兼容修复。合成环境未复制上游恢复方法正文，实际 CIL 仅在验证时从使用者的输入文件提取到外部产物目录。
