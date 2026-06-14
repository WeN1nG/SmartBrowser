# Playwright MCP 页面快照压缩策略总结

## 核心策略对比

| 策略 | Playwright MCP | 我们的实现（SmartAI Browser） |
|------|---------------|-------------------------------|
| **元素选择** | 基于可见性 + 接收鼠标事件（`refs: "interactable"`），非交互元素不入树 | `querySelectorAll` 全量收集，无差别包含导航/筛选项等噪声 |
| **数量限制** | **无全局数量上限** | 快照 1000 / observe_browser 默认 120 |
| **深度限制** | `depth` 参数控制树深度（primary truncation knob） | 无深度概念，遍历整个 DOM |
| **输出格式** | YAML 树形结构，`- [ref=e1] Button: 立即沟通` | JSON 扁平数组 |
| **增量压缩** | 提供 `previousSnapshot` 时未变化节点压缩为 `ref=e1 [unchanged]` | 无增量，每次全量 |
| **名称截断** | 可访问名称超过 900 字符截断 | text 截断在 100 字符 |
| **结果截断** | 服务端无硬截断 | **2000 字符头部+尾部截断**，破坏 JSON 结构 |

## 核心差异详解

### 1. 元素过滤粒度不同

Playwright 只给"可见 + 接收鼠标事件"的元素分配 ref（元素 ID），纯展示性元素（如 `<div>公司简介</div>`）根本不进可操作列表。而我们的实现用 `querySelectorAll` 把 `<a>`, `<button>`, `[tabindex]`, `<label>` 全部收集，Boss直聘 上侧边栏筛选器、导航链接等都排在职位按钮前面，导致"立即沟通"被挤出前 120 个。

### 2. 增量快照（Diff-Aware）

Playwright 支持传入 `previousSnapshot`，未变化的子树直接压缩为 `- ref=e1 [unchanged]`，极大减少重复调用时的输出量。我们的 `observe_browser` 每次全量重新序列化所有元素。

### 3. 深度限制 vs 数量限制

Playwright 用 `depth` 控制树深度来限制输出大小，这是结构化的截断（保留结构完整性）。我们用 `max_elements` 和固定 2000 字符做非结构化截断——2000 字符刚好卡在某个无关元素的中间，JSON 被截成废纸。

## ARIA Tree 快照生成流程

```
页面 DOM
  │
  ▼
isElementHiddenForAria() 可见性过滤
  - display: none / visibility: hidden / aria-hidden 排除
  - role="presentation" / "none" 排除
  - "ai" 模式: aria可见 OR CSS可见 即包含（更宽松）
  │
  ▼
toAriaNode() 元素转 ARIA 节点
  - 收集: role, accessibleName, box, receivesPointerEvent, checked/disabled/expanded 等状态
  - 跳过无 role 的元素及纯文本内联元素（减少噪声）
  │
  ▼
computeAriaRef() ID 分配
  - refs: "interactable" 模式: 仅 visible + pointer-receiving 元素分配 e1, e2, e3...
  - 非交互元素不分配 ref
  │
  ▼
renderAriaTree() YAML 渲染
  - depth 参数控制树深度（无深度限制则全部输出）
  - 可访问名称 > 900 字符截断
  │
  ▼
增量模式（可选）
  - 对比 previousSnapshot
  - 未变化节点渲染为: - ref=e1 [unchanged]
  - 变化节点完整渲染
```

## 对我们的启示

如果我们想改进当前的 snapshot/observe 机制，可以参考的方向：

1. **不再用 2000 字符硬截断**，改为按元素分段截断（保留完整元素 JSON）
2. **在 `observe_browser` 中支持按文本关键词过滤**元素再输出
3. **增加增量快照能力**——如果上一次和这次元素没变，直接告诉 AI
4. **过滤非交互元素**——只给真正能点击的元素分配 ID，减少噪声
5. **考虑使用 ARIA Tree 替代 DOM querySelectorAll**，天然过滤不可见/非交互元素
