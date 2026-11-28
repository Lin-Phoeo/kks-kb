# LiquidFluid / SoftFlesh：Studio启动兼容修复

这组工具针对两份准确的KKS插件原件。原版共同加载时，现有镜头插件与LiquidFluid反复包装相机委托形成递归；启动阶段还会在Timeline界面尚未初始化时刷新其控件。SoftFlesh另在Awake无条件给其它插件的LateUpdate安装诊断计时器。

变换按一个事务依次处理：

1. 两份DLL只停用各自`LateUpdate`中的相机委托包装。该方法不包含物理解算；Update、OnGUI及其它原功能保持。**代价是这两个插件面板悬停时不再由它们阻拦相机旋转/缩放**，用户操作面板时可能仍影响相机。其它插件已有的相机控制保持，不增加全局委托管理器或逐帧反射扫描。
2. SoftFlesh仅把Awake的一次无参void诊断启用调用改为5字节NOP，保留其Remove清理方法。详见相邻`softflesh-optional-stall-profiling`工具。只证明不再启用此计时路径，未量测帧率改善。
3. 两份Timeline桥的EnsureReady最前添加就绪门：从BepInEx注册表定位Timeline实例，缓存该类型的3个字段，核`_self`为同实例、`_loaded=true`且`_allToggle`为有效Unity对象。未就绪时返回false，不写原ready/registered、不触发早发现和初始化；原有重试保持。正常成功注册后，原Update外层状态门会停止调用，不新增持续每帧反射。

| 文件 | 唯一原件SHA256 | 组合候选SHA256 |
|---|---|---|
| LiquidFluid.KKS.dll1.6.5 | `edefaaa0418663182b04bf74cbaa59132e1b610014ababb5a467d69c778b74f2` | `5df105e5b4e6d5db59473a1a99fa5d3b9c0c1a027fb48cc2f676b19f18a98ae8` |
| SoftFlesh.KKS.dll1.1.0 | `4ef0d800e979a0b597d95dfadee41d546fd17fef8936d9ec3b007bcddf337cf1` | `716cef2c94ee9680ca39db8102e8c258dd735708980a801c22f4859921a7f7a6` |

Camera与计时器步骤直接修改准确PE的代码字节区，区域外全部字节保持。Timeline步骤由Cecil新增1个自有helper方法，文件重新序列化，**不再声称完整文件其它字节相同**；核对的是全部原字段/特性/引用/资源、原方法IL/局部变量/异常区域及MaxStack保持，只有列出的原方法改变。Liquid原258方法中2个改变，Soft原535方法中3个改变，各新增1个helper方法。构建用`Readiness.BuildOnly.dll`仅供注入，不能安装该中间文件。

## 重现验证

需要.NET10 SDK、Python、Windows.NET Framework参考程序集、自己已有的准确原插件、游戏程序集与Mono.Cecil。工具没有PackageReference，不下载游戏或第三方插件。

```powershell
py ops/community-plugin-repairs/physics-camera-delegate-compatibility/run.py `
  --liquid '<原件>/LiquidFluid.KKS.dll' `
  --soft '<原件>/SoftFlesh.KKS.dll' `
  --game-root '<KKS目录>' `
  --output '<新的私有验证目录>'
```

输出目录不得位于仓库、plugins、mods、UserData或游戏内容区。工具只在新目录生成阶段文件、候选和日志，不修改输入或安装目录。每一步有输入/输出SHA，最终hash必须符合上表，不允许把较早camera-only当组合成品。

- 原双LateUpdate和所有相机闭包的实际CIL在22检查中13失败，组合候选22/0。外部合成包装器使用可变previous指针并设递归计数上限，避免用真实StackOverflow终止测试；覆盖包装顺序、反复接管、原相机禁用、换相机及多轮移除/重建。
- 原Awake前段与实际Remove方法提取执行：原8/0预期Apply1次，候选8/0预期Apply0次；原日志初始化与空/重复/异常清理合同保持。
- 实际原/最终EnsureReady及最终注入helper：40/0，覆盖未初始化、缺插件/字段、字段类型不符、销毁UI、实例更换、原异常与重试；不执行第三方物理求解。
- 独立宿主调用本机游戏的Mono运行库，真实装载最终程序集并JIT/调用helper和EnsureReady（Timeline未注册）以及JIT相机方法，两份各4/0。BepInEx路径在子进程指向私有临时目录，避免写游戏配置；没有实例化插件或启动Unity。此证据能发现错误程序集引用，不能替代游戏主进程验收。
- 8项未知输入、重复修补、原地、已有输出、live目录、仓库目录和错误阶段保护拒绝并保持目标不变。

合成readiness夹具的3个编译警告来自故意定义的错误类型私有字段，它们通过反射被检查；没有忽略测试失败。早期未安装camera草稿末端全部NOP，被真实CLR执行以InvalidProgramException拒绝；最终保留末RET。早期Cecil序列化重算MaxStack被回读发现，最终回填全部原方法头值。首次Mono隔离未初始化BepInEx路径而失败，后续用私有配置根重跑通过；这些失败不当作原插件缺陷或成功记录。

## 2026-11-28一次真实空Studio验证

上表两个准确组合候选完成64项检查，0失败：两个面板各观察到4次真实OnGUI原方法执行；Timeline实际UI就绪及注册标记通过；SoftFlesh诊断Harmony owner不存在；Liquid无模拟器/发射目标，Soft控制器无登记解算对象。控制器相机前/后渲染事件在两轮禁用/重新启用后数量为0/1，没有重复；面板、原相机/光标状态及该次运行后的配置快照恢复检查通过。Awake、Start和Run均只执行1次，清理完成。

进程经2次正常窗口关闭请求退出，观察器记录OnApplicationQuit和OnDestroy，随后移出临时观察器，gate不存在。启动阶段Error头0，整个最终日志仍有10条旧控制台`IOException / InvalidHandle`退出错误；Fatal0，原相机StackOverflow0、Timeline早期UI空引用0、stall-spy日志0。**不能把本次结果写成全日志无错或所有退出问题已修复。**

没有创建角色、载入用户卡或场景、添加物理目标，也没有实际热键和具体物理效果验收。打开面板采用与热键相同的显示字段后由Unity实际绘制，不等同于物理键盘输入证明。事件计数只覆盖相机前/后渲染回调；目标插件的退出回调没有被直接逐次插桩。完整视觉效果、性能改善及所有组合仍未验证。脱敏结果见`verification.json`。

## 安装和真实验证边界

只选择KKS DLL，不并装KK DLL或SoftFlesh1.0.1。退出Studio、复核原件和同GUID唯一性后备份两份DLL及现配置，按精确SHA替换；作者附cfg不覆盖用户配置。先用无角色、无用户场景的空Studio验证准确注册、Timeline实际就绪/注册、面板显示、无解算目标、诊断Harmony owner不存在和正常退出，再移出临时观察器。具体物理效果、全部场景/组合、性能及真实快捷键另行记录，不能由静态绿灯推出。

作者DLL、完整反编译、素材和配置不在公开源码中。原作者保留全部权利；本目录仅提供自有变换程序、合成夹具和验证说明，用户需合法持有准确输入。回退恢复备份原件或暂时移出两插件，保留配置/场景；不能以自动重装原版重新引入已观察递归后宣称恢复正常。
