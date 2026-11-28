# PPEv3 空选择事件修复

取消Studio选择或删除最后一个选中对象时，ObjectsSelected事件可携带空列表。目标PPEv3在原try/catch之前直接调用SelectedObjects.First，导致 `Sequence contains no elements` 被API事件隔离层记录。

本工具只在该First调用前检查已有List.Count：空列表直接返回；不清除最后有效的charapos焦点，不改变有选择时“只取第一项”的原语义，不新增catch吞掉其他异常，也不修改外部对象或画面参数。原非角色/缺失控制器的已有catch保持，真实角色找不到hips时原null赋值保持。

唯一支持输入是已经含此前本地修复的准确DLL，不能用其他上游版本替代：

- 输入SHA-256：`864b641f5458b8099980f7ced378e1df7023fe60b70195f8de6272dee3d790d0`。
- 候选SHA-256：`2fdf17fb9a2d2748c489bae3e80464d286adf237702183f405d51c81a42f1c14`。
- 原302方法仅ObjectsSelected变化，其余301的实际body与异常区保持；程序集身份、原字段、资源、特性及引用保持，无新增helper或程序集依赖。

## 验证

需要.NET 10 SDK、Python、用户现有精确DLL、游戏程序集及Mono.Cecil。只包含自有变换工具和合成协作者，不包含第三方DLL/完整源码。CIL复制器的自有固定副本位于tool/Clone.cs，仅用于隔离验证构建，不依赖未发布的其他修复目录；运行时没有新增插件依赖。

```powershell
py ops/community-plugin-repairs/ppev3-empty-selection/run.py `
  --original /path/to/KKS_PostProcessingEffect.dll `
  --game-root /path/to/KoikatsuSunshine `
  --output /path/to/new/private-ppe-verification
```

实际原回调CIL在10项检查中8通过/2空选择失败；候选10项全部通过。覆盖有/无旧焦点的空选择、单角色、多角色首项、文件夹/null首项、缺控制器、缺hips、非Studio以及null事件参数仍明确报错。7项未知输入/重复修补/覆盖/游戏目录/仓库目录/未知提取输入保护通过。

工具只输出新私有目录，不安装、不启动Studio、不广播选择事件。候选经独立复核后由维护主线备份安装，再单独运行空Studio验收：26项断言全部通过，实际观察3次原生选择回调，其中1次为空，回调异常0；保留旧焦点，场景、历史、对象索引、变换、相机、光标与配置均恢复。测试仅使用一个临时原生Folder及瞬时自有焦点Transform，不加载人物或场景。正常关闭后临时观察器移出。画面效果与复杂人物层级没有因此得到验证，既有控制台退出错误仍未修复。

观察器独审曾拦下一个假设错误：`selectNodes = []`只循环传入数组，本身不会清空旧选择。最终验证先用原生清选择方法并断言实际为空，再触发KKSAPI现有多选事件，明确计数空回调；没有以删除日志或吞异常制造通过。回退时恢复安装前准确输入DLL，保留用户配置和场景。
