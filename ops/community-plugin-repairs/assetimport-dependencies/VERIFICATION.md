# 验证记录与边界

## 工具合成测试

从仓库根目录执行：

```powershell
py -B -m unittest discover -s ops/community-plugin-repairs/assetimport-dependencies/tests -v
```

21项通过。所有包、哈希、游戏目录和进程结果都使用临时自造数据或mock，不读写真实安装，不连接网络。覆盖默认只读、显式变更/下载、原身份拒绝、包SHA-256/SHA-512和成员哈希拒绝、路径穿越、重复ZIP成员、已有文件保护、半批/写入中途失败恢复及清理失败明确记录、完整回滚、被修改文件导致整批拒绝、外来/篡改回执、回滚中断续跑、并发锁、游戏运行拒绝及不支持平台保守拒绝。

实际安装由先前受控操作完成；本工具交付后只对参考安装执行只读doctor，三个原件门控和两个新增目标均匹配，没有重装。

## 依赖静态证据

- 官方ValueTuple转发层11个导出类型全部存在于实际Studio mscorlib。
- Bcl.Memory的52个相关成员、现装AssimpNetter的84个相关成员全部解析到实际现装程序集及两个候选，不使用宿主框架/GAC替代。
- 本机BepInEx解析器会在现有加载集和plugins子目录中寻找依赖，插件局部路径可由其正常机制发现，无额外全局Resolve补丁。

## 真实Studio结果

同一观察器对已经加载的AssimpNetter调用GetTypes：补齐前390/394、4个LoaderExceptions；补齐后394/394、0个LoaderExceptions。运行中两个新DLL的identity/location都与局部安装一致，原Memory/Buffers/Unsafe保持原加载版本。参考原插件GUID/字节范围与实际输入哈希见manifest。

Microsoft.Dynamic仍为725/726，缺失可选RemoteConsoleHost的Remoting2.0；未修改或声称修复。此结果证明当前程序集类型可加载，**不证明AssetImport模型导入、native assimp文件兼容或复杂场景功能**。正常运行中的模型导入仍需独立观察。

## 回退与发布边界

默认doctor只读，安装与回滚必须--apply；不会修改Managed/core、配置、角色、场景或已有不同文件。候选文件、下载包、测试临时产物及私有运行日志均不进入公开仓库。源码检查不取代每次实际安装后的身份和运行结果。
