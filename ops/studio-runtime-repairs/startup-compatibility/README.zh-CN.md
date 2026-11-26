# Studio 启动兼容补丁候选

工具只把使用者提供的两个已知哈希 DLL 写成新文件，拒绝原地覆盖、未知版本和已有输出。它不安装插件、不启动游戏、不删插件、不调整日志，也不下载游戏程序集。公开目录只含原创补丁工具、合成测试和去除安装路径的结果。

## 具体变化

- AutoKeyframe：当前插件 `org.fox.autokeyframe` 1.3.0.0 的私有 `OnPostRender(Camera)` 只有一条 `ret`。Unity 把这个名称当作无参消息检查。本地全部 355 个可读托管插件中，该方法的直接 IL 引用为 0；4 个含同名字符串的位置已逐个核对，分别针对 Studio.BoneLineCtrl、Python 回调和 Camera 静态字段。候选只把该方法改名 `UnusedPostRenderCallback`，保留 Camera 参数、可见性与空方法体。NativeHelper 原生 DLL 不适用 Cecil，不计为托管扫描通过。未找到对应当前 GUID 的公开作者源码或现成修复。
- CharaAnime：官方 master 与两个公开 fork 仍共用全局 `vector` 名称。本机支持库的程序集版本是 2.9.0.0、BepInPlugin 版本是 2.8.0；官方源码也使用这对不同版本号。候选把 GizmosManager.Start 的注册名及 CubeGizmos 的三轴引用共 4 处同步改为 `Countd360.CharaAnime.KKS.vector`。注册的白纹理、端帽类型、尺寸参数均保持；KKSPE 的 `vector` 及其自带纹理不变。没有直接跳过第二次注册。

## 构建、合成测试与生成候选

需 .NET SDK 10 和使用者现有 Mono.Cecil。测试不访问游戏、真实场景或数据库。所有输出请置于仓库外；示例路径由使用者选择。

```powershell
dotnet build ./StartupRepairs.csproj -c Release -p:CecilPath="<existing-Mono.Cecil.dll>" -p:BaseIntermediateOutputPath="<new-temp-directory>/obj/" -o "<new-temp-directory>/bin"
dotnet "<new-temp-directory>/bin/StartupRepairs.dll" selftest
dotnet "<new-temp-directory>/bin/StartupRepairs.dll" patch autokeyframe "<original-AutoKeyframe.KKS.dll>" "<new-output>/AutoKeyframe.KKS.dll"
dotnet "<new-temp-directory>/bin/StartupRepairs.dll" patch charaanime "<original-CharaAnime.dll>" "<new-output>/CharaAnime.dll"
```

工具针对输入 SHA256 和精确结构双重检查，并比较写回后的方法签名、IL、异常处理、类型/程序集属性和嵌入资源。修改超出允许的一个方法名或四个字符串会失败。支持输入与本轮输出哈希见 `EVIDENCE.json`。

## 验证与限制

`selftest` 有 11 项回归：原消息签名的失败条件、重命名后的发现规则、非空方法/已有调用者拒绝、重复补丁拒绝、Vectrosity 的同名先到先得拒绝规则、双向注册次序、4 处一致性及缺失/错位引用拒绝。

消息检查和注册次序使用合成规则模型；Cecil 夹具只测试元数据/IL 变换，不是可运行的 Unity 插件。它们没有证明真实 Studio 不再报错、MMDD 三轴渲染、KKSPE 箭头外观或 AutoKeyframe 编辑功能。生成候选时未安装到真实插件目录。上述真实功能仍须下一次 Studio 启动后核验；不得把合成绿灯写成实机完成。

上游检索范围、提交锚点与许可边界见 `SOURCES.md`。
