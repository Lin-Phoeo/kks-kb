# 来源与变换边界

- [Njaecha / BlendShapeEditor](https://github.com/Njaecha/BlendShapeEditor) 与 [v0.6.0 预发行](https://github.com/Njaecha/BlendShapeEditor/releases/tag/v0.6.0)：原插件与 BSC2 依赖。没有把预发行称为稳定版。
- [BSC2 v2.0 官方发行](https://gitgoon.dev/Njaecha/BlendshapeCreator/releases/tag/v2.0)：KKS 与 KK 分包；旧形态键 owner 迁移需要逐项核对。
- 使用者提供的准确 ME5 程序集：用于读取属性、presenter 和目标释放方法的元数据；未知同版本字节仍拒绝。

变换只包含原创的程序集重写与测试代码。输入程序集、原作者实现、素材和完整反编译输出不随本目录提供。`receipt.json` 列出准确输入/输出哈希、四个修改的原方法、四个新增方法、一个被替换的旧 transpiler 以及最终 AssemblyReferences。

BSC2 原件不修改。BSE 原嵌入资源逐字核对不变，原构建核心库作用域保持 v4。变换验证通过不改变第三方程序许可或再分发条件。
