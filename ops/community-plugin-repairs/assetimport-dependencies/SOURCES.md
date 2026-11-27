# 固定来源和许可证

以下仅保存公开官方链接、身份与摘要；不包含原日志、个人路径、游戏或第三方DLL。

- [AssetImport 4.0.2作者发行](https://github.com/Njaecha/AssetImport/releases/tag/v4.0.2)。ZIP SHA-256 `391474cdfcfb7b1eed160e5d5fb14752cd1d863fc283f29fd951b29d9f17a4e9`。包实际含定制net462、unsigned AssimpNetter，当前安装与包成员哈希一致。缺失文件并非原ZIP中已有而本机漏复制。
- [AssetImport固定项目文件](https://github.com/Njaecha/AssetImport/blob/62c3f7238c1700c49c4716282dc6a24ce6b66631/src/AssetImport/AssetImport.csproj)引用Custom Dependencies中的AssimpNetter。官方AssimpNetter同6.0.4 NuGet是不同目标框架与公钥身份，不能直接互换。
- [System.ValueTuple 4.5.0官方包](https://api.nuget.org/v3-flatcontainer/system.valuetuple/4.5.0/system.valuetuple.4.5.0.nupkg)，[官方catalog摘要](https://api.nuget.org/v3/catalog0/data/2018.12.18.00.44.46/system.valuetuple.4.5.0.json)。使用lib/net47运行时转发层，不用ref目录、不用另一游戏进程中身份4.0.0.0的转发层，也不用net461独立类型实现。
- [Microsoft.Bcl.Memory 9.0.14官方包](https://api.nuget.org/v3-flatcontainer/microsoft.bcl.memory/9.0.14/microsoft.bcl.memory.9.0.14.nupkg)，[官方catalog摘要](https://api.nuget.org/v3/catalog0/data/2026.03.10.17.54.49/microsoft.bcl.memory.9.0.14.json)。使用lib/net462，程序集身份9.0.0.14。

两个完整下载包的SHA-512均已与官方catalog相等，清单同时固定SHA-256及目标成员SHA-256。程序集版本不等于NuGet包版本。

两包的LICENSE.TXT明确MIT、Copyright .NET Foundation and Contributors，并有THIRD-PARTY-NOTICES.TXT。工具将这些原声明本地保留在批次回执旁；[MIT许可](https://licenses.nuget.org/MIT)。此处不给游戏、AssetImport原DLL或其他未包括文件重新指定许可，也不把官方包二进制加入源码分发。

Microsoft.Bcl.Memory net462 nuspec还声明System.Memory4.5.5、Unsafe6.0.0、ValueTuple4.5.0等依赖。参考Studio已有API成员经静态核对可解析，实际类型加载亦已通过，故不增补或替换其全局Memory/Unsafe/Buffers；其他安装环境不满足精确原插件门控时必须重新评估。
