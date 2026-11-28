# AutoKeyframe 自动读取失效目标时保留关键帧

AutoKeyframe 的自动当前值更新按顺序读取轨道。现装版本在某个 KKPE 表情轨道的 Renderer/目标失效时抛出空引用，整轮更新被中断，后面的有效轨道也不再处理。

此工具只保护自动更新中的一次 `GetValue` 调用：遇到 `NullReferenceException` 或 Unity `MissingReferenceException`，保留该关键帧现有值和对象引用，继续后续轨道；同一个轨道连续失效只报告一次警告，读取恢复时记录恢复，再次失效可以重新报告。日志使用插件 owner/id，不读取人物名、轨道别名或可能再次出错的显示名称回调。其它异常继续传播，正常返回的零、false、null 等保留原语义。

这不是修复缺失的 Renderer，也没有删除无效轨道、写入零数组、关闭自动更新、改变手动赋值或改写 KKPE/Timeline。现有值不会因为读取失败而被覆盖。警告仍指出待处理的源，只有源恢复后该轨道才恢复自动更新。

## 精确输入与输出

- 输入 SHA-256：`0ee625fec99a884aff322bb0e87b33d7105f531ccd88f99b4818711873edc0c7`。它是已带 Unity 回调签名修复的受检版本；本工具保留该先前修复。
- 候选 SHA-256：`703c6d58acf73d000ea0566dc78fd352e5f30d95b19980331b58d861e4700df5`。
- 76 个原方法只改变 `AutoKeyframe.KeyframeService.UpdateChangedKeyframes` 的读取调用，其余 75 个方法及原字段、资源、插件身份保持。helper 嵌入候选，没有额外运行 DLL。

## 构建和验证

需要 Python 3.12+、.NET 10 SDK、.NET Framework 4.7.2 targeting pack，以及使用者自己持有的准确输入、游戏引用和 Mono.Cecil。仅使用包装入口；底层补丁器是内部构建步骤，不负责游戏目录边界。

```powershell
py ops/community-plugin-repairs/autokeyframe-value-availability/run.py `
  --original 'E:/Inputs/AutoKeyframe.KKS.dll' `
  --game-root 'E:/Games/Koikatsu Sunshine' `
  --output 'E:/BuildArtifacts/new-autokeyframe-check'
```

入口只写新的仓库外输出目录，拒绝游戏运行/内容树，不安装、不启动游戏。只复制最终 `candidate/AutoKeyframe.KKS.dll`，不能安装 helper 构建文件、夹具或工具。实际安装前退出 Studio，按原路径和 GUID 检查当前身份、备份原 DLL/配置；回退同样先退出，只恢复本次准确备份，保留后来编辑的数据和设置。

验证提取实际原件/候选更新循环和候选 helper CIL，在合成 Timeline 回调与 Unity 日志边界中运行。11 项原件 7 失败、候选 0 失败；覆盖原值引用保持、后续健康轨道、每实例连续失败去重、恢复后再次失败、正常零/false/null、其它异常不吞。两条同 owner/id 的不同实例也分别报告。输入、重复修补、已有输出/回执、原地覆盖和错误 helper 等拒绝保护通过；95 个非框架成员引用解析成功。独立重建得到相同候选字节。

合成 `DeepEqualityComparer` 使用对象相等边界；真实现装比较器在引用相同时立即返回 true，已独立核对，但没有宣称测试执行了它的全部深比较实现。真实人物/场景、失效表情目标的后续重建、持续运行性能和所有第三方回调仍需另行实测。

公开只含自有工具、测试和说明；不包含作者 DLL、游戏程序集、用户场景或完整反编译源码。
