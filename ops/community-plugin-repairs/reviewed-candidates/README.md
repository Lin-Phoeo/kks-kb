# 可选创作插件：来源与使用边界

以下是已取得准确KKS发行包并核过当前依赖合同的创作工具。每个功能是否需要安装取决于使用者工作流；已有类似功能、同GUID版本和作者平台限制都应分别核对。这里不提供原作者二进制，原包从对应作者链接取得。

| 插件 | 用途 | 来源与版本 |
|---|---|---|
| GIN Rope This | 给Studio物品现有或自动生成的骨骼链增加端点、重力、阻尼和弯曲控制 | [GoriGinsan/GIN_Rope_This v1.1.3](https://github.com/GoriGinsan/GIN_Rope_This/releases/tag/v1.1.3) |
| BlendshapeCreator | 与配套Blender流程协作，创建及导入保持顶点身份的自定义形变 | [ShalltyB/BlendshapeCreator v1.1](https://github.com/ShalltyB/BlendshapeCreator/releases/tag/v1.1) |
| Studio Bust Adjuster | 在工作室当前状态页编辑Pushup参数，并提供对应Timeline/ABMX轨道 | [Vaaskr/StudioBustAdjuster 1.0.1](https://github.com/Vaaskr/StudioBustAdjuster/releases/tag/StudioBustAdjusterV1.0.1) |
| PerceptiveHeightX | 为PerspectiveX视角显示相对参考身高HUD，读取身高而不修改角色尺度 | [bani4kaskashka/PerceptiveHeightX 1.0.0](https://github.com/bani4kaskashka/PerceptiveHeightX/releases/tag/1.0.0)；先看[截图兼容修复](../perceptive-height-compatibility/README.md) |

## Rope This

作者1.1.3说明针对帧率和Video Export多次相机渲染的时间推进作过处理；作者测试是上游证据，不能代替使用者环境的实际渲染验收。工具没有碰撞，绳索可能自相交。KKS自动绑定拒绝不可读网格和每顶点超过4权重等不支持输入。

核过KKSAPI、ExtendedSave以及可选NodesConstraints和Timeline的准确调用入口。自动绑定在副本网格上工作，恢复逻辑保留原renderer和骨骼；同一物件已经被GIN Liquid Threads或PhysicsChain接管时会拒绝。先用新建原生物品测试创建、端点移动、移除与场景副本保存，不能让两个控制器同时接管同一物品。

## BlendshapeCreator

此处是KKS 1.1独立创作入口。参考环境的AdvancedLoader2.0细粒度桥要求本版不存在的Owner等数据结构，因此不能把安装1.1写成支持该桥。独立使用与AdvancedLoader集成是两项不同的功能。

原DLL的窗口提示明确说明：工作室本插件滑杆值不能直接视为已保存，应使用KKPE滑杆；自定义形变定义保存与当前权重保存需分别验证。OBJ与 `.bscd` 工作流也要按对应版本核对，不能将旧README一句话解释为任意OBJ都可直接导入。

首次启动会注册场景/角色控制器和坐标切换钩子，并在DLL同级建立工作目录。保留既有cfg和快捷键；移出插件不删除已保存的扩展数据或用户导出目录。验证使用自造网格或副本，检查顶点顺序、导入拒绝、权重、复制与保存读回。

## Studio Bust Adjuster

本插件依赖KKSAPI、ABMX和Pushup，是现有数据的编辑入口。12个滑杆对所有已选角色写Pushup数据；Timeline缩放轨道写ABMX。它没有独立的撤销快照，保存沿用这些插件的数据机制，卸载不会自动撤销角色上已经保存的值。

从单角色副本开始，改一项、还原，再检查保存读回和多选范围；避免其他窗口与Timeline同时写同一字段。中文适配见[原生控件适配](../bust-native-localization/README.md)，只改可见文字，不改原插件、节点名、轨道ID或数值。

## 界面与版本核对

[Rope/BSC基础中文](localization/README.md)提供22条F1、69条精确窗口文字和5条数字规则。它们不是两个插件所有状态、文件对话框和外部Blender界面的完整翻译。BSC空Studio没有选中对象时不会绘制窗口，不能用打开开关代替真正窗口观察。

三份原KKS DLL的准确身份分别为：

| DLL | SHA-256 |
|---|---|
| GIN Rope This.dll | `6554f04badf59acf97441f3070b6a119c6f31b0eb3e984e5baa39b87a7f94a17` |
| KKS_BlendshapeCreator.dll | `c05bffae6400a730d1ae3a1d7a2476ce78bf254afa6cec4f92714c1be0711fb9` |
| KKS_StudioBustAdjuster.dll | `a07e33d8768b6a1540aee3b727cf3ad43cd62335fc3ecad04f6256e5ac53098f` |

静态依赖解析通过的非框架成员数分别330、531、54。这只证明受检依赖合同，不是完整功能验收。真实观察应以相应运行摘要为准。

## 2026-11-28 空Studio定向观察

参考安装一次真实启动确认Rope This1.1.3、KKS BlendshapeCreator1.1、Studio Bust Adjuster1.0.1和PerceptiveHeightX1.0.0修订候选均已加载。没有读写用户角色卡或场景，Studio对象数为0；观察范围分别如下：

| 插件 | 本次实际观察 | 仍未验证 |
| --- | --- | --- |
| Rope This | 打开空面板，观察到其真实绘制路径的专属翻译hook | 创建／移动／移除绳索、物品恢复、保存读回、全部文字和布局 |
| BlendshapeCreator | 插件加载，8项既有配置值语义保持 | 没有选中对象，窗口未绘制；不能据启动说窗口翻译、Blender往返或保存已通过 |
| Studio Bust Adjuster | 原生中文标题＋12标签共13处组件读回，节点身份保持；12标签字号14且均不在活动层级 | 可见字形、裁切、滑杆交互、角色参数与Timeline；见[组件运行摘要](../bust-native-localization/runtime-verification.json) |
| PerceptiveHeightX | 真实Unity订阅生命周期与公共事件包裹CaptureRender的7项检查通过，生成128×128有效RGBA PNG | 原截图快捷键协程、人物POV／HUD视觉及热键；中文增量在运行后才安装，见[截图运行摘要](../perceptive-height-compatibility/runtime-verification.json) |

此轮为自动观察，没有新增用户手工验收。进程在2次正常窗口关闭请求后退出，未强制结束，临时观察器已移出。旧invalid handle提及10次、JobTempAlloc警告31次、ThreadAbort提及1次仍出现；定向检查通过不等于整套环境无错误或这些问题已修复。
