# Browser-Use 可学习之处分析

> 对比对象：`C:\CodeSpace\Objects\OthersObjects\browser-use`
> 被对比项目：SmartAI Browser Demo（本项目）
> 分析日期：2026-06-15

---

## 一、DOM 理解层面

### 1.1 CDP 四路并行 DOM 捕获

**Browser-Use 做法：** 通过 Chrome DevTools Protocol 同时发起四次并行调用：

- `DOMSnapshot.captureSnapshot` — DOM 树 + 计算样式 + 绘制顺序 + DOM 矩形
- `DOM.getDocument` — 完整 DOM 树（nodeId 层级）
- `Accessibility.getFullAXTree` — 无障碍树（含所有 frames，包括 iframe）
- `Page.getLayoutMetrics` — 视口尺寸

**本项目现状：** JS 注入遍历 DOM，仅走 accessibility tree。

**可学之处：** CDP `DOMSnapshot` 拿到的信息更底层、更全面（computed styles、paint order、DOM rects），比纯 JS 遍历更可靠。未来若改用 CDP 通信，可替代当前 `window.bermainA11y.getSnapshot()` 的 JS 注入方式。

### 1.2 Paint Order 遮挡过滤

**Browser-Use 做法：** `PaintOrderRemover` 模块——后绘制的元素（更高 z-index）会遮挡先绘制元素的部分区域，自动剔除被遮挡的节点，避免 AI 看到并点击实际上不可见的元素。

**本项目现状：** 仅过滤 `display:none` / `visibility:hidden` / `aria-hidden` / `role=presentation` / `[hidden]`。

**可学之处：** 在 `AutomationScripts.cs` 的 `getSnapshot` 中增加重叠元素检测。思路：利用 `DOMSnapshot` 返回的 `paintOrder` 字段，对同一视口区域内的元素按绘制顺序排列，后绘制的元素遮挡先绘制的元素时，剔除被遮挡者。

### 1.3 跨域 iframe 支持

**Browser-Use 做法：** 通过 CDP `all_frames` 字段 + URL 匹配懒加载合并跨域 iframe 树。

**本项目现状：** 仅遍历同域 iframe，跨域 iframe 不可达。

**可学之处：** 若迁移到 CDP 路径，可利用 `DOMSnapshot` 的 `frames` 数组天然获取所有 frame 信息，无需 CORS 限制。

### 1.4 复合控件内联提取

**Browser-Use 做法：** `<select>` 元素的选项在 snapshot 中内联展开；`<input type=date>` 标注格式提示（YYYY-MM-DD）；range/number/color 输入有虚拟子组件描述。

**本项目现状：** 仅有 `browser_select_option` 工具，snapshot 中 `<option>` 不内联展开。

**可学之处：** 在 snapshot 中对 `<select>` 的每个 `<option>` 输出 `label="选项文本" value="选项值"` 内联文本，减少 AI 猜测。例如：

```
[5]<select aria-label=国家>
    中国(CN)  美国(US)  日本(JP)
[8]</select>
```

---

## 二、元素交互层面

### 2.1 坐标点击兜底

**Browser-Use 做法：** 除了基于 index 的 selector_map 查找元素外，还支持 viewport 坐标点击——当 index 查找失败（元素已变化）时，用鼠标坐标直接点击。

**本项目现状：** 完全依赖 `element_id`，失效后只能重新 snapshot。

**可学之处：** 在 `BrowserAutomationService` 中增加 `ClickAtAsync(x, y)` 方法，通过 CDP `Input.dispatchMouseEvent` 实现坐标点击。当 index 查找失败时，AI 可从 snapshot 中提取元素中心坐标作为兜底。

### 2.2 多动作批处理（Multi-Action Batching）

**Browser-Use 做法：** `max_actions_per_step` 默认 5，LLM 一次输出多个动作列表，框架顺序执行，每步之间有 page-change guard——检测到导航/URL 变化则终止后续动作。

**本项目现状：** 每轮 LLM 调用最多返回一个 tool call（或多参数合并为一个），执行完等下一轮 LLM 响应。

**可学之处：** 扩展 `AgentOutput` 支持返回 `Action[]` 数组，配合 `multi_act()` 风格的 page-change guard，可将有效步数翻倍。例如 AI 一次输出 `[click(3), type(5, "hello"), click(8)]`，三个动作在同一个 snapshot 上下文内串行执行，遇到导航则提前终止。

### 2.3 输入值不匹配检测

**Browser-Use 做法：** `input` 工具执行后读取实际 value 与预期对比，发现不匹配（如日期输入框自动格式化、autocomplete 覆盖）时发出告警。

**本项目现状：** `typeInElement` 写入值后不验证。

**可学之处：** 在 `BrowserAutomationToolRouter` 的 `browser_type` 结果中增加 `actual_value` 字段，对比写入值与实际值，不一致时在结果中标注 `WARNING: value mismatch, expected="..." got="..."`。

### 2.4 按元素滚动 + 分页滚动

**Browser-Use 做法：** 支持 `scroll_to_element(index)` 和 `scroll_page_down`；多页顺序滚动时每页间隔 150ms 等待渲染。

**本项目现状：** 仅 `browser_scroll(delta_x, delta_y)` 固定像素滚动。

**可学之处：** 增加 `browser_scroll_to_element(element_id)` 工具——先通过 snapshot 获取元素 viewport 坐标，再调用 `scrollIntoView` 或计算 delta。

---

## 三、LLM 集成层面

### 3.1 结构化状态表示（XML 标签分段）

**Browser-Use 做法：** 每次请求按结构化 XML 标签组织信息：

```xml
<user_request>...</user_request>
<agent_history>...</agent_history>
<agent_state>...</agent_state>
<browser_state>...</browser_state>
<browser_vision>...</browser_vision>
<step_info>...</step_info>
```

**本项目现状：** 系统提示 + 动态上下文拼在一个 string 里，靠自然语言分隔。

**可学之处：** 在 `ContextBuilder.Build()` 中用 XML 标签分隔不同信息块，帮助 LLM 更好区分"任务要求"、"历史步骤"、"当前页面"。

### 3.2 强制结构化思考框架

**Browser-Use 做法：** 要求 LLM 在每个步骤输出：

- `thinking` — 当前想法
- `evaluation_previous_goal` — 上一步执行结果的评估
- `memory` — 进度记忆（已完成什么、还剩什么）
- `next_goal` — 下一步目标
- `plan_update` — 计划调整

**本项目现状：** 有 `[思考过程]` / `[结论]` 格式，但无结构化字段。

**可学之处：** 在 prompt 中强制 AI 输出三个结构化字段：

```
[上步评估] ...
[进度记忆] ...
[下步目标] ...
```

这比自由文本显著提升多步任务的连贯性和可追踪性。

### 3.3 截图作为视觉辅助（Browser Vision）

**Browser-Use 做法：** 每个步骤附带一张带 bounding-box 索引的截图（`<browser_vision>`），视觉模型可据此进行图像推理。

**本项目现状：** `browser_screenshot` 仅返回 base64 长度等元数据，不将图片发给 AI。

**可学之处：** 如果使用的模型支持多模态（如 GPT-4o、Claude），可在关键步骤附带截图——特别是表格、图表、弹窗、验证码等结构化文本难以表达的页面。

### 3.4 LLM 摘要压缩

**Browser-Use 做法：** 当历史超过 `trigger_char_count`（默认 40k）时，调用另一个 LLM 将旧历史总结为 compact memory block，保留关键信息而非简单截断。

**本项目现状：** 截断策略为"保留首条 + 最近 6 条消息"。

**可学之处：** 当前 50KB 触发压缩时，可尝试用 LLM 摘要替代简单截断。思路：提取前 N 轮的工具调用 + 结果，用一个 summarize prompt 生成一段"任务进展摘要"，替换原始消息。这能更好地保留长期任务上下文。

### 3.5 结构化输出 Schema

**Browser-Use 做法：** 使用 Pydantic `AgentOutput` 强类型结构化输出，三个模式（with thinking / without thinking / flash），action 列表通过 `create_model` 动态注入。

**本项目现状：** AI 输出自由文本 + tool_calls。

**可学之处：** 利用 OpenAI/Anthropic 的 JSON mode / response schema，强制 AI 输出结构化决策字段而非自由文本。例如：

```json
{
  "thinking": "...",
  "evaluation": "...",
  "memory": "...",
  "next_goal": "...",
  "actions": [...]
}
```

### 3.6 降级 LLM（Fallback Provider）

**Browser-Use 做法：** 主 LLM 遇到 rate limit / 401 / 402 / 5xx 时自动切换到 `fallback_llm`。

**本项目现状：** 无降级机制，仅重试当前 provider。

**可学之处：** 在 `AiClient` 中支持配置 backup provider，主 provider 连续 N 次失败后自动切换。

---

## 四、错误恢复与自检测

### 4.1 DOM Text Hash 页面停滞检测

**Browser-Use 做法：** `ActionLoopDetector` 跟踪两个维度：

1. 动作循环——20 动作滚动窗口 + normalized hash，重复 >= 5 次触发 nudge
2. 页面停滞——同 URL + 同 DOM text hash（页面内容未变）连续出现，注入 nudge

**本项目现状：** 仅检测"相同工具+相同参数+相同结果"重复 3 次，无法发现换工具但页面不变的情况。

**可学之处：** 在 `AgentEventSelfHandler` 中增加 `previousDomTextHash` 字段，每次 snapshot 后计算 `innerText` 的 hash。连续两步 hash 相同即判定页面停滞，无论 AI 用了什么工具。

### 4.2 运行时 Replan 触发器

**Browser-Use 做法：** 连续 `planning_replan_on_stall`（默认 3）次失败后，注入 replan nudge，强制要求 AI 输出新的 `plan_update`。

**本项目现状：** `TaskStateMachine` 只在开始时强制规划，运行时不触发重新规划。

**可学之处：** 在 `AiClient.ExecuteConversationAsync` 循环中维护 `consecutiveFailures` 计数器，达到阈值时注入系统消息："已连续失败 N 步，请调用 update_todo 重新规划子任务"。

### 4.3 探索限制 Nudge

**Browser-Use 做法：** 连续 `planning_exploration_limit`（默认 5）步没有关联任何 `plan_item` 时，提醒 AI 制定计划。

**本项目现状：** 无此检测。

**可学之处：** 当 `TaskStateMachine` 处于 Executing 状态时，跟踪每步是否关联了 active subtask，连续多步游离则提醒。

### 4.4 Budget 渐进警告

**Browser-Use 做法：** 达到 75% 步数预算时注入醒目警告，要求整合结果准备结束。

**本项目现状：** 无百分比提示，只有硬上限 80。

**可学之处：** 在 50%/75%/90% 消耗点注入渐进式警告：

```
[步骤 40/80] 已使用 50% 预算，请检查进度
[步骤 60/80] 已使用 75% 预算，建议开始整合结果
[步骤 72/80] 仅剩 8 步，请尽快完成
```

### 4.5 Judge 后验评估

**Browser-Use 做法：** AI 调用 `done()` 声称完成后，用另一个 LLM call（judge model）审查整个执行 trace，给出 pass/fail 判决和理由。

**本项目现状：** AI 说完成了就停止循环。

**可学之处：** 可选的后验模块——当 AI 调用 `finish_subtask("completed")` 或输出终止文本时，额外调一次 judge LLM 验证是否真的完成。可用轻量模型（如 flash/haiku）降低成本。

### 4.6 Stable Hash 元素匹配（Rerun 基础）

**Browser-Use 做法：** 重放保存的历史时，用多级匹配策略适应 DOM 变化：

```
exact hash → stable hash → xpath → ax_name → attribute
```

**本项目现状：** 完全依赖 snapshot 分配的递增 `element_id`，页面一刷新 ID 全变。

**可学之处：** 在 snapshot 中为每个元素计算 `stable_hash`——基于 `tag + aria-label + name + placeholder + text` 的复合 hash。当工具调用报 stale element 时，AI 可尝试用 stable_hash 在上一轮 snapshot 中重新定位元素。

---

## 五、架构设计层面

### 5.1 事件总线解耦

**Browser-Use 做法：** 所有浏览器操作通过 `bubus` 事件总线分发。工具层只发事件（如 `ClickElementEvent`），watchdog 监听并执行实际 CDP 调用。

**本项目现状：** 同步方法调用链：`ChatViewModel → BrowserAutomationToolRouter → BrowserAutomationService → WebView2.ExecuteScriptAsync`。

**可学之处：** 引入轻量事件总线可将"动作意图"和"执行细节"解耦，便于添加拦截器（自动截图、录屏、安全审计、日志）。当前项目已有 `SemaphoreSlim` 序列化，在此基础上加一层事件分发并不复杂。

### 5.2 Session-Specific Exclude Attributes

**Browser-Use 做法：** 给 UI 覆盖层（toast、loading spinner、modal）注入 `data-browser-use-exclude-{session_id}` 属性，snapshot 时自动跳过这些元素。

**可学之处：** 在 `BrowserAutomationService` 初始化时注入一段 JS，给全局 overlay 元素加上 session-specific exclude 属性，避免 snapshot 返回大量干扰项。

### 5.3 敏感数据脱敏

**Browser-Use 做法：** `sensitive_data` 配置项定义哪些字段是敏感的，snapshot 中用 `<secret>真实值</secret>` 标签包裹，执行时注入真实值，历史消息中自动脱敏。

**可学之处：** 在 snapshot 中对 `type=password` / `type=creditcard` 等字段的 value 做脱敏处理，避免敏感数据长期驻留 LLM context。

---

## 六、最高优先级可落地改进建议（按投入产出比排序）

| 优先级 | 改进项 | 涉及文件 | 预估工作量 | 预期收益 |
|--------|--------|---------|-----------|---------|
| **P0** | 结构化思考框架（上步评估/进度记忆/下步目标） | `ContextBuilder.cs`, `BrowserAutomationToolRouter.cs` | 小 | 显著提升多步任务连贯性 |
| **P0** | DOM text hash 页面停滞检测 | `AgentEventSelfHandler.cs`, `AutomationScripts.cs` | 小 | 覆盖当前"相同工具"检测的盲区 |
| **P1** | LLM 摘要压缩替代简单截断 | `AiClient.cs` | 中 | 延长长任务可用步数 |
| **P1** | Stable hash 元素匹配 | `AutomationScripts.cs`, `BrowserAutomationToolRouter.cs` | 中 | 大幅减少 stale element 错误 |
| **P2** | 多动作批处理 | `BrowserAutomationToolRouter.cs`, `ChatViewModel.cs` | 中大 | 有效步数翻倍 |
| **P2** | 输入值不匹配检测 | `BrowserAutomationService.cs`, `AutomationScripts.cs` | 小 | 减少 AI 在表单页面的反复试错 |
| **P3** | 运行时 Replan 触发器 | `AiClient.cs`, `TaskStateMachine.cs` | 小 | 引导卡住的 AI 重新规划 |
| **P3** | Budget 渐进警告 | `AiClient.cs` | 极小 | 引导 AI 提前收敛 |
| **P4** | 坐标点击兜底 | `BrowserAutomationService.cs` | 小 | element_id 失效时的最后手段 |
| **P4** | Judge 后验评估 | 新增 `JudgeService.cs` | 中 | 提高任务完成准确率（需额外 LLM 调用成本） |

---

## 七、本项目已有但 Browser-Use 没有的特色

学习是双向的，本项目也有自己的优势：

1. **TaskStateMachine** — 强制 Planning → Executing → Complete 三态流转，子任务不可跳过。Browser-Use 的 plan 是软性的，AI 可随时忽略。
2. **AgentEventSelfHandler** — 多层自检测（过期元素复用、重复导航失败、相同动作重复、无进展循环、dead-end 评分），全部在本地运行无需额外 LLM 调用。
3. **WebView2 内嵌架构** — 零外部依赖，无需安装 Chrome，适合桌面端分发。Browser-Use 依赖本地 Chrome 实例。
4. **ask_user 暂停/恢复机制** — 完整的 `_pendingMessages` + `__ASK_USER_PAUSED__` 状态管理，支持人机协作中断。
5. **中文系统提示** — 全部 prompt 和自检测消息为中文，对中文 LLM 友好。
