# CameraFXSlots 的 KKS、导入和动态相机槽修复

CameraFXSlots 1.0.0 在相机槽旁保存/还原特定物品的景深和双色雾材质设置。原版把场景“导入”和整场景“读取”接到同一回调，先清空全部20个槽，再读取源场景扩展数据；即使导入不含相关数据，也会丢失当前场景的槽位设置。

KKS 原生 `SceneInfo.Import` 只导入对象，保留当前场景的相机槽。因此修复让导入同时保留当前20个 FX 槽，**不导入源场景的相机 FX**。正常整场景读取、保存格式及材质操作方法保持原 CIL。没有添加新的槽位合并规则，也不改用户资源或翻译。

另将原件的 `ExtensibleSaveFormat` 引用适配为当前 KKS 的 `KKS_ExtensibleSaveFormat`，并同时移除导入订阅和相应解绑。现装 MoarCamz 在普通读取/清空时会删除第11个以后的槽，之后可能重建同数量的新槽；原插件仅比较历史数量，会漏掉这些新对象。此次增加内嵌生命周期管理，最多每0.25秒检查20个槽的对象/按钮/事件身份，只重建失效的自有FX按钮，并仅解除自己登记的加载回调，保留其他监听器。

布局恢复使用启动时捕获的高度，且只在当前高度仍等于本插件写入值时恢复；外部后来更改的高度保留。父容器整体替换、已销毁父容器变成Unity空引用、Load按钮/onClick替换及不完整模板均有针对性检查。MoarCamz克隆首槽模板时也会复制已有FX按钮；候选给自建按钮附带有 `Component Owner` 字段的内部归属标记，只禁用并销毁同一runner被模板继承的按钮，再生成新槽回调。同名但未标记的外部按钮保留。helper与标记嵌入候选，不需要额外helper DLL。唯一受支持输入：

```text
GUID: com.ziglo.camerafxslots
Original version: 1.0.0
Original SHA-256: 432aa2935d257b11d7fff680a2fb357b0ac6cca08f72f9e24780083b28e7685b
Local version: 1.0.0.2
Candidate SHA-256: 5cf6b7b8f410055067f81c2c013eeca5f8bf47c5f7117e7ad2745564c0e4cfd8
```

## 显式构建与验证

需 .NET 10 SDK、.NET Framework4.7.2 targeting pack、Python3.12+、调用者的 Mono.Cecil 与 KKS 依赖。输出必须为新的仓库外目录，入口禁止把输出写到游戏的 plugins/config/UserData/mods/abdata/Managed 运行目录，不安装或启动游戏。

```powershell
py -B ops/community-plugin-repairs/camerafxslots-kks-import/run.py `
  --original 'E:/ReviewInputs/ziglo_CameraFXSlots.dll' `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --output 'E:/BuildArtifacts/camerafxslots-review'
```

入口从作者输入一次完成构建、内部两阶段修补、对照测试、范围审计和拒绝测试。所有 DLL/fixture/日志/报告写入显式外部输出目录。真正候选只有 `candidate/ziglo_CameraFXSlots.dll`，不能把 `control` 中间DLL、helper构建产物、合成依赖或验证工具放进游戏。

41项包含原件/候选的13个实际入口/方法以及其UI闭包：导入空/无关/合法/旧格式/非法数据时保留20槽及对象身份；正常读取、数值兼容、保存往返、重复事件订阅与清理；同数量换槽、缩后扩、父容器替换/销毁、Load按钮/事件替换、外部监听与布局、模板缺组件、20槽上限、节流后刷新，以及继承FX模板后新槽回调正确、外部同名按钮保留。作者原件26项失败；仅导入修复的中间版14项失败；最终候选41/41。无归属标记的中间生命周期候选在模板克隆用例中失败，不能安装。不把程序集无法加载当作预期失败。

原33方法中6个必要方法变化，27个保持，包括原Load/Save/材质参数逻辑；原字段及内嵌资源保持。自有helper与标记增加20个方法，嵌入同一程序集；70个外部成员在真实KKS依赖元数据中解析通过。新增引用只包括KKS ExtendedSave及已有Unity CoreModule，没有独立helper引用。写盘前在内存中核对完整候选SHA；helper构建文件的调试/SourceLink身份变化不放宽实际部署字节。

13项保护覆盖未知原件、重复修补、覆盖已有候选/回执、原地覆盖、目标事件签名漂移、生命周期阶段来源及错误helper输入、相同输出/回执拒绝；原件和哨兵回执字节保持。详细报告见输出 `verification.json`、`il-audit.json`、`original-results.json`、`control-results.json`、`candidate-results.json`。这些是合成协作者中的真实托管方法与元数据证据，不等于 Unity 实机、GPU、界面点击或场景磁盘往返通过。

## 依赖物品与使用边界

功能读取工作区节点名包含 `Depth of Field` 或 `Two Tone Fog` 的**第一个物品**，再读第一个 MeshRenderer 的实例材质。它不是游戏原生景深或 Lighting Suite 后期预设接口。

已核的现有 xukmi KKS FX 资源中，`Depth of Field Bokeh`、HQ Bokeh、LQ Bokeh具有 `_Blur`、`_BokehRadius`、`_FocusDistance`、`_FocusRange`、`_FocusHardness`；`Two Tone Fog`具有 `_Density`、`_TwoToneDensity`、`_TwoToneHardness`及两种颜色。普通 Gauss/Box 版本缺 `_BokehRadius`，不能直接称为等价；本工具不提供或安装资源。

如果节点翻译/重命名后不再包含英文子串，或场景有多个同类物品，原版查找方式仍有限制。首次验证只用一个已存在的 Bokeh 景深和一个双色雾实例。保存FX后，通过原相机槽加载按钮恢复；导入物品保留当前槽，整场景读取则加载该场景自己的FX槽。

该修复针对正常Studio/MoarCamz槽生命周期；不是任意第三方UI热卸载框架。原生事件的重复Hook/Unhook、Cleanup、私有按钮/回调清理已测；外部强禁组件但不销毁、动态修改插件内部数据结构，以及未见过的UI布局仍不承诺兼容。实际父容器/按钮对象身份变化会在下一次节流检查处理，最多约0.25秒。

## 已有运行证据与限制

中间版 `0aa7bb2db66b86b4e03251d7506c734e4f3c046fccb9188c36cceddc27d834ef` 已在实际KKS空场景确认加载、事件订阅、原生资源名称、Bokeh/Fog shader参数、材质保存恢复和自有对象清理；修正观察器后38项通过。前两轮观察器把新建材质与renderer实际克隆材质混同，并用输入字面量比较Unity颜色读回，留下3个失败；诊断记录证明实际恢复目标正确，测试已改为真实attached材质与Set后的Get基线，原失败保留。该38项结果属于中间版，不自动升级为最终动态槽修复已验。

最终版本已独立重建复核，41项对照、13项保护、精确候选SHA以及两个观察器的隔离与清理逻辑通过。2026-11-28 Run 9 在真实 KKS 中对上列最终 `5cf6…` 字节完成两组受控验证：

- **MoarCamz 58/58**：真实已安装 MoarCamz 的列表登记、`AddCameraSlot` 与 `DelCameraSlot` 配对执行一次，确认原模板确实继承了自有FX标记，继承按钮立即停用并在下一帧销毁；新槽只留下一个自有FX按钮，其回调绑定新槽编号，原生和外部监听器及外部同名按钮保留。更广的同数量替换、重排、增减、事件替换、20槽上限、父容器销毁与布局归属检查使用真实Unity控件克隆的临时非活动Canvas，**不是重复读取用户场景**。
- **材料流程40/40**：包含2项前置门，确保Moar观察器已完成且通过，再等待一帧；之后复验原38项材料路径。真实添加本机既有Bokeh和双色雾物品，未强改节点英文名，核对材质、参数、显隐与Save/Restore；景深/雾浮点最大误差为0，颜色最大绝对误差约`2.98e-8`，在声明的`1e-6`容差内。观察器按renderer实际挂载材质与Set后Get基线比较，临时材质和物品全部销毁。

两组结束时原相机槽、既有槽对象/监听器、FX数组、选择、扩展数据、配置、布局均保持或恢复，最终工作区为空；该次退出前日志没有Error/Fatal。没有安装新素材，没有触发真实场景Load/Save事件或读取用户场景。**尚未验证GPU像素效果、物理鼠标点击、完整用户场景的磁盘保存/载入/导入往返、任意热卸载和所有第三方组合。** 58与40是不同路径的限定检查，不能解读为所有Moar场景操作已经穷尽。

## 来源与安装状态

输入来自用户提供的本地归档字节。已有作者目录可见 `CameraFXSlots - plugin for Koikatsu Studio` 与附件名，但准确作者帖链接未保存；有限官方搜索未取得可核对的现成KKS发行。不能据此宣称作者从未提供KKS版本，也不独立认证用户提供字节的作者真实性。作者主页：[ziglomods](https://www.patreon.com/ziglomods)。

本目录只公开自有定向补丁工具、合成协作者、测试和说明，不含第三方原件、修改DLL、游戏反编译原类或素材。自有部分按 [LICENSE](LICENSE) 使用，不扩大作者插件、Unity、游戏或依赖的许可。

本次参考安装已部署最终字节并完成上述限定实机验证；构建入口本身仍不安装。其他安装应在游戏退出后确认同GUID不存在/核对原版本、备份原件和配置，并写安装回执。没有取得作者/用户资源授权时，不通过补装素材来补验。

回退按实际安装回执移出本次候选，保留用户后续作品和配置。无FX归属标记的中间版 `c183…` 有已证模板克隆缺陷，不能作为安全回退目标；若要撤回最终版本，应选择停用插件或退回已受控验证的仅导入修复版 `0aa7…`，并同时接受其不支持Moar动态槽重建的已知限制。不要机械恢复紧邻上一批的坏中间件。
