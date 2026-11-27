# 字体失败路径检查

由维护者显式提供游戏目录和候选 DLL，只读取依赖，不修改游戏或真实配置。

```powershell
dotnet run --project ops/studio-localization/tools/verify-font-faults -c Release -- <游戏目录> <候选DLL> <报告JSON>
```

通过真实编译后的 Switch／Restore／候选释放／TMP 基准方法注入缺失字体、两个损坏控件、未使用候选和同字体自赋值。配置使用独立合成对象并关闭保存；Unity 类型仅创建未初始化托管壳和指针哨兵，避免原生 null 检查，不执行 Unity 生命周期或 native 方法。

报告不能代表真实字体、GPU 释放或像素排版通过。它验证失败请求回写最后成功设置、事件抑制、恢复逐项继续且计数、无引用候选移出、幂等释放和原 TMP 自定义材质不被同字体赋值抹掉。
