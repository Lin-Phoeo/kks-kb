# SplashScreen 每次启动提示 This application could not be started

Studio 主程序可以继续打开，但 `CharaStudio.SplashScreen.GUI.exe` 每次启动都弹出 “This application could not be started”。本例的 SplashScreen 3.0 已将辅助程序重命名，旁边的运行时配置却仍保留原名。

## 已确认的机制

[官方控制器](https://github.com/BepInEx/BepInEx.SplashScreen/blob/2a248a54dc049e278c0a4b37c23ecaa155bc73dd/BepInEx.SplashScreen.Patcher.Common/SplashScreenController.cs#L54) 根据游戏进程名重命名 EXE，未同步移动或复制 `.exe.config`。[官方项目](https://github.com/BepInEx/BepInEx.SplashScreen/blob/2a248a54dc049e278c0a4b37c23ecaa155bc73dd/BepInEx.SplashScreen.GUI/BepInEx.SplashScreen.GUI.csproj#L11) 目标是 .NET Framework 3.5，而[原配置](https://github.com/BepInEx/BepInEx.SplashScreen/blob/2a248a54dc049e278c0a4b37c23ecaa155bc73dd/BepInEx.SplashScreen.GUI/App.config#L3) 允许优先使用 CLR v4，再尝试 v2。配置名称不匹配时，不能按这份配置选择运行时。

本例观察到：

```text
BepInEx/patchers/BepInEx.SplashScreen/
  charastudio.SplashScreen.GUI.exe
  BepInEx.SplashScreen.GUI.exe.config
```

缺少 `charastudio.SplashScreen.GUI.exe.config`。截至核验时作者正式版仍为 [v3.0](https://github.com/BepInEx/BepInEx.SplashScreen/releases/tag/v3.0)。没有据此断言用户缺少 .NET、需要重装系统或需要修改系统 DNS。

## 最小修复

1. 找到实际游戏目录，核对辅助 EXE 的真实名称与 SplashScreen 版本；不要只照抄示例大小写或路径。
2. 退出游戏和进度窗口，将原 EXE、原 `.exe.config` 备份到新的目录。如果同名目标配置已经存在，先比较内容，不覆盖用户配置。
3. 复制原 `BepInEx.SplashScreen.GUI.exe.config`，将副本命名为实际 EXE 文件名加 `.config`。本例得到 `charastudio.SplashScreen.GUI.exe.config`，原配置保留。
4. 对两份配置计算 SHA-256，确认副本字节一致，EXE 没有改变。若原配置缺失，先从作者对应版本包核实原件，不从不明整合包拷贝。
5. 使用正常 Studio 入口重新启动，确认原宿主报错不再出现、Studio 可进入。查看本次日志，把其他错误分别记录。

这次只新增一份 263 字节的配置，没有修改 EXE、patcher 或系统运行库。它只覆盖当前程序名；若另一个游戏入口让辅助程序再次改名，需要为那个实际名称单独核对。另一种配置方案是备份后关闭官方 `RenameExe`，让辅助 EXE 回到原名称；本次没有使用该方案。

## 实测与限制

单机核验中，新增匹配配置后重新启动，用户确认原报错不再弹、Studio 正常打开；之后安装另一批插件再次启动，用户再次确认没有原弹窗。EXE 哈希前后不变。

本次没有捕获到辅助进程实际加载的 CLR 模块，不能把元数据或配置顺序说成已观测到 CLR v4。日志仍出现 Splash 通信线程 `IOException` / Win32 232；该管道警告没有因此被宣布修复。完整进度条动画、自动关闭行为和其他游戏入口尚未全部验收。

回滚时先退出游戏，仅将本次新添的同名配置移到自己的备份目录；保留原 EXE 和原配置。回滚会恢复原状态，也可能恢复原弹窗。

本方法来自本机配置对照与作者源码核验，不是上游已合并的修复；原项目为 [BepInEx/BepInEx.SplashScreen](https://github.com/BepInEx/BepInEx.SplashScreen)。脱敏身份见 [evidence.json](evidence.json)。
