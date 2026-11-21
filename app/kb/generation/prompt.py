#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""提示词与片段裁剪：系统提示、改法骨架、正文片段。"""
import jieba


def _is_tbl(s):
    return s.lstrip().startswith("|")


def _tbl_block(lines, idx):
    """idx 所在表格块的行范围 (a, b)：a=表头行，b=块后第一行"""
    if idx < 0 or idx >= len(lines) or not _is_tbl(lines[idx]):
        return idx, idx + 1
    a = idx
    while a > 0 and _is_tbl(lines[a - 1]):
        a -= 1
    b = idx
    while b + 1 < len(lines) and _is_tbl(lines[b + 1]):
        b += 1
    return a, b + 1


def snippet(body, query, size=2000, head=400):
    """优先保留查询词附近的内容；表格不切断，切断时明确标注"""
    if len(body) <= size:
        return body

    terms = [t for t in jieba.cut_for_search(query) if len(t) >= 2][:8]
    pos = -1
    for t in terms:
        k = body.find(t)
        if k >= 0:
            pos = k
            break
    if pos < 0:
        return body[:size] + "\n…（后续略）"

    lines = body.split("\n")

    # 查询词落在第几行
    acc, hit = 0, 0
    for n, ln in enumerate(lines):
        if acc + len(ln) >= pos:
            hit = n
            break
        acc += len(ln) + 1

    # 向上扩展（最多占一半预算）
    a = hit
    up_budget = size // 2
    up_used = len(lines[hit])
    while a > 0 and up_used + len(lines[a - 1]) + 1 <= up_budget:
        a -= 1
        up_used += len(lines[a]) + 1

    # 向下扩展（总预算）
    b = hit + 1
    used = sum(len(x) + 1 for x in lines[a:max(hit + 1, a + 1)])
    while b < len(lines) and used + len(lines[b]) + 1 <= size:
        used += len(lines[b]) + 1
        b += 1

    # 表格对齐：起点在表内就退到表头
    ta, _ = _tbl_block(lines, a)
    if ta != a:
        a = ta
    # 终点在表内：短表整块纳入，长表留到行尾并标注
    if _is_tbl(lines[b - 1]):
        ta2, tb2 = _tbl_block(lines, b - 1)
        if tb2 - ta2 <= 60:
            b = tb2

    seg = "\n".join(lines[a:b])
    cut = (b < len(lines)) and _is_tbl(lines[b - 1]) and _is_tbl(lines[b])
    if cut:
        hdr = ""
        if _is_tbl(lines[a]):
            # 该表块的表头（同块的前两行）
            ha, _ = _tbl_block(lines, a)
            hdr = " / ".join(x.strip() for x in lines[ha:ha + 2])

        seg += ("\n\n> ⚠ 该表格在本片段处截断（原文后面还有行）。"
                "完整表格请看同一文档的其他片段。表头：" + hdr[:140])

    prefix = body[:head] + "\n…\n" if a > 0 else ""
    suffix = "\n…（后续略）" if b < len(lines) else ""
    return prefix + seg + suffix


SYS = """你是恋活（Koikatsu Sunshine）渲染与制作的技术助手。
规则：
1. 优先使用【本地知识库】内容回答，每条引用标 [L1][L2]…；用到联网结果标 [W1][W2]…
2. 知识库里没有的，明确说"本地库未覆盖"，再用联网结果补充。
3. 引用具体数值时原样写出，不要改写数字。
4. 参数名保留英文原文，用反引号包裹。
5. 本地库与联网结果冲突时并列说明，不要静默择一。
6. 用 Markdown：结论放最前，参数用表格，条目用列表。
7. 不要复述推理过程，不要写"让我想想"，直接给结论。
7. 答案写完后输出一行 %%% ，然后给 3 条追问，每行一条，不要编号。"""


def _crumb(c):
    """把 path / heading / section 拼成可追溯的面包屑"""
    parts = []
    p = (c.get("path") or "").replace("\\", "/")
    if p:
        parts.append(p)
    h = (c.get("heading") or "").strip()
    if h:
        parts.append(h)
    s = (c.get("section") or "").strip()
    # section 常是 slug（纯 ascii+连字符），有 heading 时就不再重复
    if s and (not h or (s != h and not s.replace("-", "").isalnum())):
        parts.append(s)
    return "｜".join(parts)


def build_prompt(q, chunks, web, history=None):
    parts = []
    if history:
        parts.append("【对话上文】")
        for h in history[-4:]:
            parts.append(f"{'用户' if h['role']=='user' else '助手'}：{h['content'][:400]}")
        parts.append("")
    parts.append(f"问题：{q}\n")
    if chunks:
        parts.append("\n【本地知识库】")
        # 总预算约 7000 字符：推理模型读得越少，思考越快
        per = max(900, 7000 // max(1, len(chunks)))
        for i, c in enumerate(chunks, 1):
            sib = c.get("siblings") or 0
            tag = f"（另有 {sib} 个变体共用此段）" if sib else ""
            parts.append(f"\n[L{i}] {_crumb(c)}{tag}\n"
                         f"{snippet(c['body'], q, size=per)}")
    if web:
        parts.append("\n【联网结果】")
        for i, w in enumerate(web, 1):
            parts.append(f"\n[W{i}] {w['title']}\n{w['url']}\n{w['snippet']}")
    parts.append("\n请基于以上材料回答，标注引用编号。回答控制在 900 字以内，不要复述推理过程。")
    return "\n".join(parts)


SYS_RECIPE = """你是恋活（Koikatsu Sunshine）shader 与渲染的改造顾问。
用户想要一个具体效果，你要给出可以直接执行、可以直接粘贴的改动方案。

硬规则：
1. 每个改动都要指明【文件路径 + 参数名或代码片段】，路径必须来自【本地知识库】里的 [L#]，
   绝对不许编造路径、不许编造不存在的参数名。
2. 参数名用反引号包，数值原样给出，不许改写数字。
3. 知识库里找不到确切落点时，直接在「落点」一栏写「本地库未覆盖」，并说明需要用户提供什么文件；
   不要拿别的 shader 的参数冒充。
4. 如果材料里有源码，引用具体代码行（原样抄 1-3 行），不要凭印象写。
5. 涉及改代码的，给出「改前 / 改后」两段对照，用 ```hlsl 或 ```csharp 代码块包裹。
6. 说清副作用：改这个会不会影响其他部位、要不要同步改别的参数。

输出结构（五段必须全部出现，一个字都不能省；没有内容就写「无」）：

## 结论
一句话，不超过 40 字。

## 改动清单
必须用 Markdown 表格，表头固定这六列，至少一行：
| 文件 | 参数或位置 | 原值 | 建议值 | 作用 |
|---|---|---|---|---|

## 改前改后
必须给代码块，块内用「// 改前」和「// 改后」标注：
```hlsl
// 文件：<取自 [L#] 的路径>
// 改前
<原样抄源码里的 1-3 行>
// 改后
<改完的 1-3 行>
```

## 副作用
用无序列表，至少一条；实在没有就写「- 无」。

## 还需确认
用无序列表，至少一条；实在没有就写「- 无」。

【格式硬约束】上面五个标题必须**逐字照抄**，就是以「## 结论」「## 改动清单」
「## 改前改后」「## 副作用」「## 还需确认」这五个字符串开头。

不许改写成「## 一、xxx」「## 二、xxx」或任何其他标题，
不许合并，不许增删，不许调整顺序。内容可以有分点，但必须落在这五个标题之下。

反面例子（绝对不要这样输出）：
    ## 一、按 shader 家族选参数
    ## 二、操作步骤

正确做法：
    ## 改动清单
    ### 按 shader 家族选参数

答案写完后输出一行 %%% ，然后给 3 条追问，每行一条，不要编号。"""


def build_prompt_recipe(q, chunks, web, history=None):
    parts = []
    if history:
        parts.append("【对话上文】")
        for h in history[-4:]:
            parts.append(f"{'用户' if h['role'] == 'user' else '助手'}：{h['content'][:400]}")
        parts.append("")
    parts.append(f"要实现的效果：{q}\n")
    if chunks:
        parts.append("\n【本地知识库】（源码片段原样给出，可直接引用行）")
        per = max(800, 8000 // max(1, len(chunks)))
        for i, c in enumerate(chunks, 1):
            kind = "源码" if c.get("col") in ("shader", "src") else "资料"
            parts.append(f"\n[L{i}] [{kind}] {_crumb(c)}\n"
                         f"{snippet(c['body'], q, size=per)}")
    if web:
        parts.append("\n【联网结果】")
        for i, w in enumerate(web, 1):
            parts.append(f"\n[W{i}] {w['title']}\n{w['url']}\n{w['snippet']}")
    parts.append(
        "\n请严格按下面这个骨架逐段填写，**五个标题原样保留**，"
        "不要改写成「一、二、三」，不要合并或增删。\n"
        "**每个论断后面都要带引用编号**，格式 [L3]；每行至少一个。"
        "文件路径只能来自上面的 [L#]，不许编造。总长 2000 字以内。\n"
        "\n## 结论\n（一句话说明改哪里，句末带 [L#]）\n"
        "\n## 改动清单\n"
        "| 文件 [L#] | 参数或位置 | 原值 | 建议值 | 作用 |\n"
        "|---|---|---|---|---|\n"
        "| 抄 [L#] 的路径 | `_参数名` | 默认值 | 新值 | 干什么 [L#] |\n"
        "\n## 改前改后\n```hlsl\n"
        "// 文件：<抄 [L#] 的路径>\n// 改前\n…\n// 改后\n…\n```\n"
        "（代码后面补一行「依据 [L#]」）\n"
        "\n## 副作用\n- 影响什么 [L#]\n"
        "\n## 还需确认\n- 库里没覆盖的部分 [L#]")
    return "\n".join(parts)
