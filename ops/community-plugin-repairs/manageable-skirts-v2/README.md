# ManageableSkirts V2 状态修复

此目录为 Ziglo ManageableSkirts 2.0.0 提供显式本地补丁及验证入口。V2 增加半锥形裙骨操作柄，但仍会重建共享骨骼列表、在关闭时显示原先隐藏的节点，并用固定颜色恢复操作柄。修复只管理实际改变过的可见性、颜色、激活和原 Renderer 状态，保留 V2 半锥几何及其控制流程。

唯一支持的作者原件 SHA-256：

```text
17ae25d3f4654687b6b180abc04ca7528790363ecf24b7f461081f30b2d5fc4d
```

插件 GUID 为 `com.ziglo.ManageableSkirts`，本地补丁标为 `2.0.0.1`。这不是作者发行的新版本。原件、游戏程序集和生成 DLL 不随源码发布；来源与许可见 [SOURCES.md](SOURCES.md)。

## 行为范围

- 不写 `OCIChar.listBones`：原列表对象、成员、顺序及其他插件的增删由原有系统管理。
- 仅当当前属性值仍等于本工具最后写入值时恢复。检测到其他工具写入不同值后让出该属性。
- 跟踪角色控制器、Animator、裙骨对象身份；目标删除、换衣、控制器替换时停止使用旧状态，更新时识别新增有效裙骨。
- 半锥创建、网格、相机方向与原作者更新算法保留。替换原 Renderer 开关的恢复方式，保留 Renderer 原先已禁用的状态。
- 失败恢复会记录错误；单角色逐项恢复可重试。终止清理尝试所有角色后结束跟踪，不承诺销毁后重试。

## 构建与验证

需要 .NET 10 SDK、.NET Framework 4.7.2 targeting pack、Python 3.12+ 和显式指定的 BepInEx/Unity/游戏引用。以下命令从仓库根运行，所有构建产物和测试报告写到全新外部目录：

```powershell
py -B ops/community-plugin-repairs/manageable-skirts-v2/run.py `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --original 'E:/ReviewInputs/ziglo_ManageableSkirts_KKS.dll' `
  --output 'E:/BuildArtifacts/manageable-skirts-v2-review'
```

工具拒绝未知原件、已有输出、仓库内部或游戏运行目录输出。它不安装插件、不启动游戏、不读取角色/场景、不写用户配置。验证器直接加载指定原件与候选，但 Unity/Studio/BepInEx 依赖是自造的最小夹具，属于 opt-in 维护工具而非常规 CI。

入口依次构建 helper、patcher、行为验证器、IL 审计器，执行原版失败对照、候选行为检查、实际游戏 API 元数据解析和拒绝输入测试。完整边界见 [VERIFICATION.md](VERIFICATION.md)。

## 候选与安装边界

只有以下两个输出是候选；不能将 build、fixtures 或验证器复制到游戏：

```text
candidate/ziglo_ManageableSkirts_KKS.dll
candidate/Codex.ManageableSkirtsV2State.dll
```

本目录离线验证报告的 `installed:false` 表示入口本身不执行安装。候选随后已在参考环境单独安装并核对 SHA；人物裙骨交互仍未因此完成实机验收。原 V1 和 V2 使用同一 GUID，不能并装。安装应在 Studio 退出后完成，先核对同 GUID 文件，保存原 DLL/helper 和配置备份，再将原版本移至游戏扫描目录之外，安装两份经过核对的候选。现有配置不应整份覆盖。

回退前确认 Studio 已退出、目标仍与安装回执哈希一致；移出本次两份文件，再恢复原 V1 文件。保留用户后续设置和场景修改，不靠覆盖整个目录回退。

## 实机验收

用空 Studio 和可丢弃的角色/场景副本检查：热键反复启停、前后视角的远端裙链、半锥开关与拖动、颜色/透明度变化、最后节点隐藏、换衣、新增/移除裙骨目标、删除角色、场景切换与正常退出。检查原已隐藏的节点、原禁用 Renderer 和其他工具的后续修改是否保持。该过程须由真实运行证据确认，不能用下面的合成结果替代。

值比较无法识别其他工具恰好写入相同值的意图；两次约 0.15 秒更新之间也可能出现原生状态变化。GPU 绘制、hover/collider、Mono 装配加载、复杂插件组合和性能仍需实机确认。

## 参考运行记录 · 2026-11-28

候选与 helper 的独立重建 SHA 和参考安装回读一致；33 项合成检查外，独立审阅补测替换控制器后再次启用、着色关闭再开启、活目标移除再加入及外部 Renderer 改动保留，共 37 项通过。这些仍是合成 Unity/Studio 边界。随后第三轮真实 Studio 空场景完成 helper 绑定、无角色选择时的安全切换观察，计入跨组件 53 项限定检查；第四轮跨组件 57 项检查中再次通过。没有人物半锥拖动、真实裙骨状态或换衣验收。第三轮其他组件的口型启动错误保留历史，第四轮已确认修订后挂钩实际安装；最新分层状态见[跨组件记录](../selected-installation/README.md)。
