# 本地插件选择目录

把插件用途资料生成一个可直接双击打开的独立HTML：搜索、按类别/安装状态/本地原包筛选、查看详情、跨筛选勾选，再导出JSON或复制清单。它没有安装功能，不调用翻译服务或远程API，不加载远程脚本、字体、图片。来源链接只有使用者主动点击才打开。

Python生成器只依赖标准库，页面只用原生HTML/CSS/JavaScript。公开演示数据全部合成；真实插件说明、下载来源、本地原包路径应在使用者机器上生成，不提交本仓库。

## 生成

Python3.10或更新版本。在本仓库根目录运行，输出选择一个尚不存在的文件：

```powershell
py -B ops/plugin-catalog/build_catalog.py `
  --input ops/plugin-catalog/synthetic-catalog.json `
  --output 'E:/CatalogPreview/plugin-catalog.html' `
  --title '工作室插件选择目录'
```

真实资料用自己的JSON替换`--input`。输出包含全部样式、脚本和数据，不依赖旁边的源文件，可以离线移动到别处。已有输出会拒绝覆盖；保留旧版并使用新的文件名。生成器不会扫描游戏或更改输入文件。

## 输入合同

输入必须是对象数组。完整上游研究报告先明确转换为该数组，不能直接把`{projects: ...}`等整份报告塞入；类型错误会指出对应ID和字段。

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | 必填、唯一非空字符串 | 稳定项目身份，供选择与导出对应 |
| `name` | 必填、非空字符串 | 可包含中文用途名和英文插件名 |
| `category` | 可选字符串 | 主类别，缺失显示“未分类” |
| `purpose` | 可选字符串 | 说明用途；缺失明确标尚未提供 |
| `compatibility` | 可选字符串 | 原兼容边界，不根据文件名猜测 |
| `install_status` | 可选字符串 | 人可读状态，建议中文 |
| `install_status_code` | 可选字符串 | 精确机器状态，优先于显示标签分类 |
| `version`, `author` | 可选字符串 | 来源版本与作者 |
| `difficulty`, `recommendation` | 可选字符串 | 上手难度和用途建议，不自动代选 |
| `dependencies`, `conflicts`, `archive_paths` | 可选字符串数组 | 依赖、限制、本地原包记录 |
| `sources` | 可选数组 | 字符串或`{"label":"作者说明","url":"https://example.invalid/guide"}` |

未知字段完整保留到详情的“补充资料”和导出的`records`中，不自动丢弃或把未知状态当未安装。可将来源的结构化细节保留在`details`等附加字段，展示字段则显式转换为字符串/数组。

`installed`、`installed_script`、`installed_framework`及已支持的精确已装状态计入“已安装”；`not_installed`计入“未安装”。`installed_partial_bundle`、`installed_report_only`及其他未经确认状态进入“状态待核”，中文原状态仍显示。“有本地原包记录”仅依据`archive_paths`非空，不证明本机文件现在存在或可用。

## 选择与导出

勾选表示加入待讨论清单，不执行下载或安装。已安装项目同样可选，但导出意图为`review_existing_usage`；其他项目为`consider_after_review`。导出中：

- `records`是已选原始记录，未知字段与数据保持。
- `choices`单独保存每项选择意图，不覆盖原记录字段。
- `action`固定为`review_selection_only`，带目录SHA、标题与导出时间。

选择仅存入本页专属、按数据SHA隔离的localStorage键，不读取其他站点存储。浏览器不允许保存时，当前选择和导出仍工作。目录数据变化会使用新namespace，不把旧状态自动套给新目录。搜索/筛选不清除已选项目。

自动剪贴板在离线文件或权限限制下可能不可用；页面会打开已选清单文本框，供手动复制。JSON导出在浏览器本地生成Blob，不上传。额外未知字段、原包路径与资料来源会包含在导出中，实际分享前可自行检查内容。

## 安全与验证

数据嵌入转义`<`、`>`、`&`、U+2028/2029，防止`</script>`越过JSON容器；标题HTML转义，模板标记一次替换，输入中的`@@APP@@`保留文字。页面用`textContent`创建内容，不把插件说明当HTML执行。来源链接只允许HTTP/HTTPS，其余scheme作为文字。CSP禁止网络连接和外部资源加载。

运行针对本工具的检查：

```powershell
py -B ops/plugin-catalog/test_catalog.py -v
node --test ops/plugin-catalog/test_core.js
```

Node只用于检查原生JS逻辑，生成和使用目录不需要Node。Python6项、Node5项覆盖恶意HTML/脚本边界、模板标记、未知字段保留、ID与输入类型、过滤、跨筛选选择、导出意图和原记录保留、失效存储以及链接scheme。

浏览器验证使用独立本地预览页：22项搜索/筛选/选择/详情/JSON内容/复制降级/存储失败/无外部请求检查通过；另验证刷新后选择保留、390px响应布局、攻击文本保持字面值、实际168项资料完整载入。随后以`file://`打开独立HTML，内嵌CSP下脚本、筛选、勾选正常；真实点击导出按钮后回读下载JSON，两个合成选择及各自复核意图正确。该过程只验证目录页面，没有验证任何游戏插件运行或安装兼容。私有数据、浏览器日志与截图未放入公开目录。
