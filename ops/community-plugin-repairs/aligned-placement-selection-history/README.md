# Aligned Placement 的混合选择撤销修复

KKS Aligned Placement Helper 1.0.1 在同时选择可移动和不可移动 Guide 时，只缓存可移动对象旧值，却为所有选中对象生成 Undo 命令，导致 KeyNotFoundException。补丁仅让两个命令构造方法投影已缓存位置和旋转的 Guide；无缓存时不加入空命令。原三种排列计算、正常可移动选择的命令内容/顺序以及插件GUI保持。

原件SHA：`0b9a21faf851f8aebcd8eda15d8fc519a6174bde8561c84d6a19959070f2c779`。候选SHA：`1a844bd061599d80889b39e01687e695faf29e2a8727503eefa230e786abed32`。插件GUID/1.0.1保持，候选用哈希区分本地修订，没有独立helper DLL。

## 验证

```powershell
py -B ops/community-plugin-repairs/aligned-placement-selection-history/run.py --original E:/Inputs/KKS_AlignedPlacement.dll --game-root 'E:/Games/KKS' --output E:/BuildArtifacts/aligned-review
```

需要.NET10 SDK、Python和调用者本机Mono.Cecil。入口不安装或启动游戏。十项实际原CIL对照覆盖三种Apply正常命令/Undo、混合不可移动选择、全部不可移动和单选择无操作：原六项KeyNotFound失败，候选全部通过。两原方法变更，新增一私有谓词；原字段、资源、程序集引用保持。未知原件、二次修补、原地与已有输出覆盖均拒绝。这组Unity/Studio协作者为合成对象；真实Studio验证单独记录如下。

输出门覆盖整个仓库、游戏根目录、BepInEx/UserData/mods/abdata及游戏根下所有`*_Data`目录，并按Windows大小写规则比较。允许明确的外部构建目录与游戏`.scratch`；路径检查不创建文件。可单独执行`py -B ops/community-plugin-repairs/aligned-placement-selection-history/output_paths.py`，4项拒绝和2项允许检查应全部通过。

## 限定实机验证

2026-11-28，在真实KKS空Studio中安装上列`1a844…bed32`字节，临时观察器完成142项断言，0失败。该数字包含每步重复的自有对象/撤销命令归属检查，不代表142个独立产品功能。具体路径包括真实工具栏PointerClick事件打开/还原窗口，三份原生Folder的直线位置、旋转、半圆和整圆排列，以及原生Undo/Redo后ChangeAmount与下一帧Transform读回。混选一个禁止位置编辑的Guide时，候选正确排除它的撤销条目，保持其位置，其他对象正常撤销。

结束时三个临时对象已销毁，场景、选择和撤销栈恢复为空；插件字段、scene索引、ChangeAmount集合、相机槽、光标与配置保持。该运行随后经两次正常关闭请求退出，临时观察器已移出。没有载入人物或用户场景，没有保存场景；动画速度随机化、真实人物/模型层级、物理鼠标输入、GUI文本输入与视觉排版不在这组检查内。

清理空选择时，现装PostProcessingEffectsV3的ObjectsSelected回调对空集合调用First并报InvalidOperationException。此错误已单列为既有回调问题，观察器继续完成清理并保留完整日志；没有伪造非空选择来隐藏它，也不称本次整段日志无错误。同批另一插件的首次观察器存在独立失败，不能用Aligned的142/0代替整批验收。

## 使用语义

- 线性位置和线性旋转各生成一条原生Undo；圆形排列生成位置、旋转两条命令，完整撤销需要两步，即使未启用旋转也保留原行为。
- 基准优先使用当前operationTarget，否则selectObject；多选集合顺序由Studio决定。
- 动画速度随机化没有Undo。本地原件实际把负数归零并交换反序的min/max，与README写反序不执行不一致；本补丁不改该功能。
- 只处理撤销缓存筛选，不扩大任意不可移动Guide的旋转/排列语义。没有新增Update或轮询。

## 来源和许可

[官方仓库](https://github.com/AruiKashiwagi/KKS-AlignedPlacement)；[1.0.1发行](https://github.com/AruiKashiwagi/KKS-AlignedPlacement/releases/tag/1.0.1)。官方README明确允许使用、再分发和修改，但未提供正式许可证标识。本目录只包含自有定向补丁、合成协作者和审计，不包含作者DLL、游戏DLL或反编译原类。原包由调用者持有并保持不变。
