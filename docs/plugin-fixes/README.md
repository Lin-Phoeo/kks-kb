# KKS 插件维护记录

这些记录提供可复核的本地修复方法和边界，不是原作者的官方发行包。仅发布必要源码、配置步骤与脱敏证据，不附第三方程序、私人日志或游戏资源。

- [SplashScreen 启动弹窗](splashscreen-config/README.md)：补齐重命名程序对应的配置；本机两次启动后由用户确认原弹窗消失。
- [Compositor CaptureTex 类型兼容](compositor-capturetex/README.md)：精确输入哈希约束的定点补丁工具，跳过不兼容返回类型；不实现 RenderTexture 截获。

逐项复核自己的版本，备份后最小修改，再验证原现象。游戏更新、插件重新解压或切换启动程序后应重新检查文件身份。
