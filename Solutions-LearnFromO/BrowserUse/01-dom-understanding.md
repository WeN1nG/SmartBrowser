# 方案：DOM 理解层面改进

> 来源：FromBrowserUse.md 第一节（1.1 ~ 1.4）
> 目标：提升 snapshot 质量，让 AI 看到的元素更接近真实页面状态

---

## 1.1 Paint Order 遮挡过滤

### 问题

当前 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 的可见性过滤仅检查 CSS 属性（`display:none`、`visibility:hidden`、`aria-hidden` 等），无法识别视觉上被高 z-index 元素遮挡的节点。AI 可能点击一个在 snapshot 中"可见"但实际上被浮层盖住的元素。

### 方案：在快照中增加 `paint_order` 字段 + 后处理去遮挡

#### 1. 修改 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` — `InjectionScript`

在 `collectElementInfo` 中增加 `paint_order` 整数字段，值为 `getComputedStyle(el).zIndex`（解析为整数，`auto` 记为 0）：

```javascript
// collectElementInfo 中新增
paint_order: function(el) {
    var cs = el.getRootNode().getComputedStyle(el);
    var z = parseInt(cs.zIndex) || 0;
    // 同时记录 stacking context 层级
    var level = 0;
    var node = el;
    while (node = node.parentElement) {
        var pCs = node.getRootNode().getComputedStyle(node);
        if (pCs.position === 'absolute' || pCs.position === 'relative' ||
            pCs.transform !== 'none' || pCs.filter !== 'none' ||
            pCs.willChange !== 'auto' || /^rgba/.test(pCs.opacity)) {
            level++;
        }
    }
    return { z: z, level: level };
}
```

实际实现可以简化为返回一个整数优先级：

```javascript
// 简化版：返回 zIndex 数值，auto 记为 0
paintOrder: function(el) {
    var cs = el.getRootNode().getComputedStyle(el);
    return parseInt(cs.zIndex) || 0;
}
```

#### 2. 遮挡检测后处理

在 `getSnapshot()` 返回数组后，增加一个 `removeOverlapped()` 函数：

```javascript
function removeOverlapped(elements) {
    // 按 paintOrder 降序排列，高 zIndex 优先保留
    var sorted = elements.slice().sort(function(a, b) {
        return (b.paintOrder || 0) - (a.paintOrder || 0);
    });
    
    var kept = [];
    var regions = []; // 记录已保留元素的 viewport 范围
    
    for (var i = 0; i < sorted.length; i++) {
        var el = sorted[i];
        if (el.paintOrder && el.paintOrder > 0) continue; // 有 zIndex 的元素直接保留
        
        // 检查是否与已保留元素重叠
        var overlapped = false;
        for (var j = 0; j < regions.length; j++) {
            if (rectsOverlap(el._viewportRect, regions[j])) {
                overlapped = true;
                break;
            }
        }
        if (!overlapped) {
            kept.push(el);
            if (el._viewportRect) regions.push(el._viewportRect);
        }
    }
    
    // 重新分配 id（因为移除了部分元素）
    for (var k = 0; k < kept.length; k++) {
        kept[k].id = k;
    }
    return kept;
}

function rectsOverlap(a, b) {
    return !(a.right <= b.left || a.left >= b.right || 
             a.bottom <= b.top || a.top >= b.bottom);
}
```

`_viewportRect` 在收集元素时通过 `getBoundingClientRect()` 缓存。

#### 3. 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`（仅 InjectionScript 内的 JS） |
| 风险 | 遮挡检测可能误杀边缘情况（如半透明覆盖） |
| 缓解 | 先保留原始 `paintOrder` 字段，调试时可观察；不立即移除被遮挡元素，而是标记 `overlapped: true`，由 AI 决定是否跳过 |
| 推荐策略 | **标记而非删除**：给可能被遮挡的元素增加 `overlapped: true` 标记，让 AI 自行判断，而不是直接剔除 |

### 推荐实现（保守方案）

不直接剔除被遮挡元素，而是在 snapshot 中为每个元素增加 `overlapped: bool` 标记：

```javascript
// 在 collectElementInfo 中增加
overlapped: false  // 初始为 false

// 在 getSnapshot 返回前，批量计算重叠
(function() {
    var elements = [...]; // 已收集的可见元素
    var rects = elements.map(function(e) { return e._rect; }).filter(Boolean);
    for (var i = 0; i < elements.length; i++) {
        for (var j = 0; j < elements.length; j++) {
            if (i === j) continue;
            if (rects[j] && rectsOverlap(rects[j], rects[i]) && 
                (elements[j].paintOrder || 0) > (elements[i].paintOrder || 0)) {
                elements[i].overlapped = true;
                break;
            }
        }
    }
    // 清理内部字段
    elements.forEach(function(e) { delete e._rect; });
    return elements;
})();
```

---

### 1.2 跨域 iframe 支持（远期）

### 问题

当前 `collectInteractive` 递归进入 iframe 时，同源 iframe 可正常遍历，跨域 iframe 会抛出 `SecurityError`。

### 方案

**当前 WebView2 路径不可行** — WebView2 的 `ExecuteScriptAsync` 同样受同源策略限制，无法直接访问跨域 iframe 内容。

**远期 CDP 路径可行**：如果将来启用 Chrome CDP 路径，`DOMSnapshot.captureSnapshot` 的 `frames` 数组天然包含所有 frame 的 `frameId`，通过 `DOM.getFrameOwner` 可以识别 iframe 节点与其所属 frame 的关系。

**本方案暂不实施**，记录为远期参考。在 `ContextBuilder` 的动态上下文中可标注"当前页面含跨域 iframe，部分内容不可见"。

---

### 1.3 复合控件内联提取

### 问题

`<select>` 下拉框在 snapshot 中只显示一个 `<select>` 节点，AI 不知道有哪些选项。虽然提供了 `browser_select_option` 工具，但 AI 不知道可选值是什么。

### 方案：在 snapshot 中对特殊控件做内联展开

修改 `collectElementInfo`，当元素为 `<select>` / `<input type=date>` / `<input type=range>` 时，附加内联描述：

```javascript
function collectElementInfo(el, id) {
    var info = { id: id, tag: el.tagName.toLowerCase() };
    
    // ... 原有字段 ...
    
    // 复合控件内联描述
    if (el.tagName === 'SELECT' && el.options.length > 0) {
        var labels = [];
        for (var i = 0; i < el.options.length; i++) {
            labels.push(el.options[i].text + '(' + el.options[i].value + ')');
        }
        info.inline_options = labels.join('  ');
    }
    
    if (el.tagName === 'INPUT') {
        var inputType = (el.type || '').toLowerCase();
        if (inputType === 'date') {
            info.format_hint = 'YYYY-MM-DD';
            if (el.min) info.min = el.min;
            if (el.max) info.max = el.max;
        }
        if (inputType === 'range') {
            info.range_desc = 'min=' + (el.min||'0') + ', max=' + (el.max||'100') + ', value=' + el.value;
        }
        if (inputType === 'color') {
            info.color_desc = '当前: ' + (el.value || '#000000');
        }
    }
    
    return info;
}
```

**输出示例：**

```json
{
    "id": 5,
    "tag": "select",
    "aria_label": "国家",
    "inline_options": "中国(CN)  美国(US)  日本(JP)"
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`（InjectionScript 中的 `collectElementInfo`） |
| 风险 | 极低，仅增加字段，不影响现有逻辑 |
| 额外工作 | 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 的 `browser_snapshot` 结果说明中，提示 AI 注意 `inline_options` 字段 |

---

## 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 增加 `paintOrder` / `overlapped` 字段（保守方案） | P1 |
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 增加 `inline_options` / `format_hint` / `range_desc` 字段 | P1 |
| `Demo/BrowserDemo/Services/ContextBuilder.cs` | 在 snapshot 工具描述中补充新字段说明 | P2 |
| `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` | （远期）增加 `overlapped` 元素点击的 dead-end 检测 | P3 |

## 预估工作量

- Paint Order 遮挡标记：0.5 天
- 复合控件内联提取：0.25 天
- 跨域 iframe：不实施（标记为远期）

## 验收标准

1. snapshot 中 `<select>` 元素携带 `inline_options` 字段，列出所有选项文本和值
2. `<input type=date/range/color>` 携带对应格式提示
3. 被高 z-index 元素遮挡的低 z-index 元素携带 `overlapped: true` 标记
4. 不影响现有 `browser_click` / `browser_type` 等行为
5. AI 在多步任务中不再点击被遮挡元素（可通过人工测试验证）
