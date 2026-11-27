# Harvex ShaderPack KKS 注册兼容

HarvexARC.ShaderPack 1.0.1 的四份 shader 声明位于旧 `KK_MaterialEditor` 节点；本次核对的 KKS MaterialEditor 读取 `MaterialEditor` / `KKS_MaterialEditor`。生成器从使用者已持有的精确原件读取声明，写入一个独立注册包，保留四份声明、20 个属性及其顺序。原包只读，生成包仅含 `manifest.xml`，不附 shader 二进制。

输入 SHA-256 固定为 `32a6264fb2e9e8a40ce61dd18a37921f4f5fe87fa3a692c04904c5dbb1021c30`。其他版本应重新查证，不能盲套。功能依赖原包仍被 Sideloader 正常加载；这不是缺失 shader 资源的替代品，也不是作者发布的更新。

需要 Python 3.12 或更高版本，仅用标准库：

```powershell
py generate.py 'D:/YourGame/mods/original.zipmod' 'D:/PatchCandidates/harvex-registration.zipmod'
py -B -m unittest discover -s . -p 'test_*.py' -v
```

生成器拒绝未知原件及覆盖输出。退出 Studio 后，备份配置及原件身份，把唯一生成包放入本机 MOD 目录，再从原启动器验证 ME 中四个 shader 的实际对象与支持状态，最后打开原故障场景确认画面。回滚时移出这个注册包；原包无改动。

已完成合成声明保留/缺文件/重复迁移/输入身份拒绝测试。公开生成器的 manifest 与已安装候选字节相同；实际 Unity 注册字典中的四个 shader 均已加载且显卡支持。原场景画面仍待复测。ZIP 固定时间戳便于相同工具环境下复算；生成包 SHA 与早期时间戳不同的本机候选可能不同，应同时核对 manifest 内容。

生成器和合成测试为本项目自有代码；第三方 shader 声明来自使用者本机输入，未包含在仓库中。
