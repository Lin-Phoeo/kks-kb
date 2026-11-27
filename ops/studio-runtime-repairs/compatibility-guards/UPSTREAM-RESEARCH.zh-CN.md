# 上游核对范围 · 2026-11-26

先核对官方原件与现成修复，再提供本地补丁。本目录的候选仍需真实 Studio 验收。

- ShalltyUtils：原作者 [v1.3](https://github.com/ShalltyB/ShalltyUtils/releases/tag/v1.3)发布于2024-09-01。KKS维护分支 [Lusiocc v1.3.2](https://github.com/Lusiocc/ShalltyUtils/releases/tag/v1.3.2) 发布于2026-10-19，含KKS专用资产；本机原件版本也是1.3.2，SHA见清单。固定标签分组源码仍直接索引可选键。
- Shallty同类issue：原作者仓库精确 `KeyNotFoundException` 和维护分支 `keyframesGroupsData` 查询均未命中。搜索得到的 [HF Patch issue113](https://github.com/ManlyMarco/KK-HF_Patch/issues/113)讨论的是Timeline1.5.6 MoveKeyframe API兼容，非本次缺键错误。其 [Mmc1xs分支](https://github.com/Mmc1xs/ShalltyUtils)源码版本标1.3.3、没有latest release、分组读取仍存在相同直接索引；不把它称为本例现成修复。
- SexFaces：GitHub已归档并指向Codeberg。作者 [v1.4.0](https://codeberg.org/Sauceke/SexFaces/releases/tag/v1.4.0)有KKS专用包，增加表情设置并修复若干头MOD行为；其RangeSlider静态GUI初始化仍相同。GitHub可见issue列表及 `RangeSlider` / `OnGUI` 精确查询没有现成修复命中；Codeberg这轮核验了release和源码，未声称穷尽其全部讨论。
- KLS1.3.0：精确产品名、GUID相关作者词的GitHub仓库搜索未找到可确认上游发行源。此处仅对指定已知原件提供自己编写的Cecil工具，不声称官方认可或已有官方新版。
- PoseTools：官方 [KK_Plugins v272](https://github.com/IllusionMods/KK_Plugins/releases/tag/v272)发布于2026-11-18，KKS源码版本仍为1.1.3；v272和当前master的PoseTools源文件SHA一致。精确 `PoseBeingSaved` issue查询没有命中。现有issue125是跨游戏姿势载入、issue124是早期版本插件列表问题，不是本例保存回调NRE。因此未对PoseTools猜改。

查询未命中只能说明本轮可见搜索范围内没有对应结果，不能证明所有社区都不存在解决方案。公开包不附账号会话、用户日志、第三方DLL或完整反编译源码。
