# 字形覆盖缓存回归

从仓库根目录运行 `dotnet run --project ops/studio-localization/tests/managed-font-performance -c Release`。

测试使用合成字符覆盖函数，不读取字体文件或游戏。检查缺字回退、空白与控制字符、同值不同字符串、字体实例隔离、缓存容量及长文本不驻留。基准比较上一版逐字符缓存与当前整段文本缓存；五轮交替测量，每轮 200,000 次重复标签查询，检查预热查询零分配。

耗时仅用于相同机器和 JIT 条件的对比，不设置跨机器时间门槛。对比时可对两个路径共同设置 `DOTNET_TieredCompilation=0`，结果记录环境。它不验证真实 Font.HasCharacter、Unity 字体绘制、销毁顺序或游戏帧率，仍需真实窗口验收。
