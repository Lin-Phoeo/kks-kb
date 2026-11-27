# AssetImport 4.0.2 缺失依赖修复

受检 AssetImport 4.0.2 包中的定制 AssimpNetter 6.0.4 依赖两个未随包提供的程序集。参考 Studio 运行中 `GetTypes` 原为390/394，四个类型因缺失依赖失败。只在 AssetImport 插件目录新增下列官方文件后，同一观察方式已得到394/394；没有替换游戏 Managed、BepInEx core、System.Memory、Unsafe 或 Buffers。

| 新增文件 | 官方包内成员 | 程序集身份 |
|---|---|---|
| System.ValueTuple.dll | System.ValueTuple 4.5.0 / lib/net47 | 4.0.3.0，token cc7b13ffcd2ddd51；转发到现有 core 类型 |
| Microsoft.Bcl.Memory.dll | Microsoft.Bcl.Memory 9.0.14 / lib/net462 | 9.0.0.14，token cc7b13ffcd2ddd51 |

精确官方URL、包SHA-512/SHA-256、成员SHA-256及原插件身份门控在 [manifest.json](manifest.json)。本目录不分发任何二进制。两包许可证及来源见 [SOURCES.md](SOURCES.md)。

## 检查与安装

Python 3.12+，安装/回滚要求 Windows 并检查游戏进程已退出。默认命令只读，不下载、不创建回执或修改任何文件：

```powershell
py -B dependency_tool.py --game-root 'E:/Games/Koikatsu Sunshine'
```

`doctor` 分别报告三个原插件哈希门控和两个目标的 absent / matching / conflict，不把文件存在当成功运行。若要用自行下载的两个官方 nupkg：

```powershell
py -B dependency_tool.py install --game-root 'E:/Games/Koikatsu Sunshine' --package-dir 'E:/ReviewPackages'
py -B dependency_tool.py install --game-root 'E:/Games/Koikatsu Sunshine' --package-dir 'E:/ReviewPackages' --apply
```

第一条仅预览。第二条验证完整包SHA-512和SHA-256、ZIP路径、成员长度与SHA后，才独占创建两目标。已有不同文件或只有一个目标时整批拒绝；两个目标都已是精确文件则返回 `no_change`，不重新安装或伪造安装回执。

只有显式增加 `--download-official` 才从清单中两个固定官方NuGet URL下载缺少的包，不下载任何其他依赖：

```powershell
py -B dependency_tool.py install --game-root 'E:/Games/Koikatsu Sunshine' --package-dir 'E:/ReviewPackages' --download-official --apply
```

包目录建议放在游戏和源码之外。下载先核字节摘要，再保存；不会运行包中代码。现有包摘要不符会拒绝，不替换或重新下载覆盖它。许可证和第三方声明随安装回执本地保留。

## 回执与回滚

安装回执在调用者游戏目录 `.community-plugin-repairs/assetimport-dependencies/<批次>/receipt.json`。两个文件只允许从原先不存在状态新增，因此没有旧DLL可备份；回执记录这个前置状态。失败安装会把本批已创建文件保存在批次 `failed/`，恢复原先目标不存在的状态。正常回滚将新增文件移到批次 `removed/`，不删除它们。若失败收尾中的某次移动也失败，工具继续尝试其余本批文件，回执写 `recovery_required` 和未能移动的文件名；这时先检查回执、`failed/`和目标字节，不把部分恢复称为成功。

```powershell
py -B dependency_tool.py rollback --game-root 'E:/Games/Koikatsu Sunshine' --receipt 'E:/Games/Koikatsu Sunshine/.community-plugin-repairs/assetimport-dependencies/<批次>/receipt.json'
py -B dependency_tool.py rollback --game-root 'E:/Games/Koikatsu Sunshine' --receipt 'E:/Games/Koikatsu Sunshine/.community-plugin-repairs/assetimport-dependencies/<批次>/receipt.json' --apply
```

回滚先核**两个**当前文件与回执，任何一项后来被改动则整批拒绝。已完成回滚可以重复执行；中途中断时可按原回执继续，但不会覆盖已有冲突备份。不同游戏或内容被改写的回执拒绝。

工具使用排他锁阻止并发变更；崩溃留下锁时不会自动删锁或猜测未完成状态，应先确认相关进程停止并检查回执/文件。回滚只接受本工具生成的回执，不接管其他安装器或手工安装的所有权。不要为给已安装参考机器生成新回执而重复安装。

## 验证范围

[VERIFICATION.md](VERIFICATION.md)区分21项合成工具测试、静态依赖核验和真实Studio类型加载证据。参考机器当前两DLL加载身份/位置正确、AssimpNetter394/394成功；**AssetImport真实模型导入仍没有因此通过**。

Microsoft.Dynamic 的725/726与可选RemoteConsoleHost缺System.Runtime.Remoting2.0是另一问题，本工具不修改它，也不升级IronPython/DLR。若后续出现新绑定或方法错误，按回执回退这两新增文件，禁止为消除警告全局替换System依赖。
