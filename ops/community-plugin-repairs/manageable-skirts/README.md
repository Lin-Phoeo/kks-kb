# ManageableSkirts 兼容修复源码

此目录提供针对 Ziglo ManageableSkirts 1.1.0 的本地修复工具。原版隐藏远端裙链时会重建共享骨骼列表，关闭时会把之前已隐藏的其他节点设为可见，并用固定颜色替代原颜色。本修复保留快捷键、裙子 FK 前提与远端三链排序，只管理当前有效裙链的 guide 属性。

作者原 DLL、游戏程序集、字体、社区聊天和用户作品均不在本目录。调用者必须自行取得受支持原件及游戏依赖。这里的 `1.1.0.1` 是本项目的本地补丁版本，不是作者新发行。来源和许可范围见 [SOURCES.md](SOURCES.md) 与 [LICENSE](LICENSE)。

## 支持身份与实际范围

唯一受支持原件的插件 GUID 为 `com.ziglo.ManageableSkirts`，插件版本 `1.1.0`，14,848 字节，SHA-256：

```text
81f517809512b1d5a0971befa40a4c4124ea5fb3e7f0e80339944b56f96f652f
```

未知 SHA、同名其他版本、覆盖原件都会被拒绝。补丁只改六个原方法和本地版本标记；其他十五个方法 IL 保持不变。六个调用使用目标程序集原有的核心类型，不引入第二个核心库引用。原件新增的程序集引用仅为本目录构建的辅助程序集。

修复不修改 `OCIChar.listBones`、骨架节点或 Transform，原生删除和可见控制仍能遍历完整列表。只有实际改变过的 guide 属性保留记录。恢复时当前值仍等于本工具输出才还原；检测到不同外部值时保留外部结果。衣装替换与角色销毁按对象身份清理，不创造替代对象。

对单角色的 `RestoreCharacter` 会逐 guide 尝试，汇总报告失败，保留失败项供后续重试。`RestoreAll` 当前只由终止生命周期 `OnDestroy` 调用；它尝试所有会话后结束状态管理，有失败会抛出汇总异常，不承诺销毁后还能重试。

## 显式构建与验证

需要 Python 3.12+、.NET 10 SDK、.NET Framework 4.7.2 targeting pack，以及调用者提供的 BepInEx / Unity 2019 游戏程序集。工具仅读取这些依赖；不启动游戏、不读取卡片、不写真实配置、不执行安装。

从仓库根目录运行 PowerShell，例如：

```powershell
py -B ops/community-plugin-repairs/manageable-skirts/run.py `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --original 'E:/ReviewInputs/ziglo_ManageableSkirts.dll' `
  --output 'E:/BuildArtifacts/manageable-skirts-review'
```

`--output` 必须不存在且位于仓库之外，也不能是游戏插件、用户数据或资源目录。所有构建中间文件、依赖副本、测试输出和生成的 DLL 都写入该外部目录。`--prior-helper` 可选，用于与以前的自有辅助程序集逐方法比较 IL；不据此放宽原作者 DLL 哈希要求。

一次运行完成：helper / patcher / 合成验证器 / IL 审计器构建，原版失败对照，候选行为检查，精确改动范围和实际游戏 API 元数据解析，未知输入与原件保护检查。结果见输出目录 `verification.json`、`il-audit.json`、`baseline.json` 和 `candidate.json`。详细覆盖及不能证明的内容见 [VERIFICATION.md](VERIFICATION.md)。

`tools/verify-managed` 虽使用自造对象，仍需显式提供第三方 DLL 作为被测输入，因此属于 opt-in 工具，不是自动运行的常规仓库测试。`fixtures/` 是最小合成接口，只表达本次用到的形状和行为，不含原游戏类的反编译实现。不能把 fixture DLL 当游戏依赖安装。

## 参考安装状态

受检参考环境已按回执完成两文件试装，目标SHA与本目录记录的最终候选一致，安装前两个目标均不存在；没有改场景、卡片或配置。尚未观察自然运行中的Unity效果，因此只标安装完成，`runtimeVerified=false`。源码工具仍不自动安装。

## 输出和回退

真正候选仅有：

```text
candidate/ziglo_ManageableSkirts.dll
candidate/Codex.ManageableSkirtsState.dll
```

生成不等于安装。安装前应退出 Studio，查同 GUID 重复项、核对两个候选文件 SHA，并备份原 DLL 与相关配置。目标通常是使用者游戏目录内的 `BepInEx/plugins/`，具体安装回执应记录每个目标的原存在状态和哈希。不要复制 `build/`、合成依赖或验证工具到游戏中。

回退时先退出 Studio，确认当前目标字节仍匹配自己的安装回执；移走本次新增文件，或恢复安装前备份的原作者 DLL。按配置差异恢复自己改变的键，保留用户后续修改。不要通过覆盖整个插件目录、恢复整份旧配置或改角色/场景实现回退。

## 已知限制

- 验证调用真实原版/候选方法，但 Unity、游戏及 BepInEx 对象为合成依赖；不是 Unity Mono 运行、原生绘制、hover、collider、GPU 或复杂场景实测。
- 值比较无法辨别第三方恰好写入同值的意图。保证范围是不同外部值不被恢复逻辑覆盖，没有引入全局 setter 拦截。
- 原生可见操作可能在两次约 0.15 秒更新之间暂时改变 guide；视觉节奏及所有插件组合需要实机检查。
- 补丁 generation 2 使用目标原核心类型。早期含额外核心库引用的中间候选不能与本目录验证结果混用。必须核对插件和 helper 两个哈希，不能只凭文件名或插件版本。
