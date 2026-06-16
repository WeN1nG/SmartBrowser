# 方案：元素交互层面改进

> 来源：FromBrowserUse.md 第二节（2.1 ~ 2.4）
> 目标：增强元素交互能力，减少对 element_id 的单一依赖

---

## 2.1 坐标点击兜底

### 问题

当前 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中的 `browser_click` 完全依赖 `element_id`（snapshot 分配的递增整数）。当页面发生 AJAX 更新、SPA 路由切换、或 DOM 重建后，element_id 全部失效。AI 只能重新调用 `browser_snapshot`，但此时页面可能已经变了。

### 方案：增加 `browser_click_at(x, y)` 工具

#### 1. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 中增加方法

```csharp
/// <summary>
/// 在视口绝对坐标处执行点击（0,0 为视口左上角）
/// </summary>
public async Task<AutomationResult> ClickAtAsync(int x, int y)
{
    return await RunOnUiThreadAsync("ClickAt", async () =>
    {
        if (_activeWebView?.CoreWebView2 == null)
            return AutomationResult.Failure("当前没有激活的浏览器视图");

        // 通过 CDP Input.dispatchMouseEvent 实现精确坐标点击
        var eventArgs = new
        {
            type = "mousePressed",
            x = x,
            y = y,
            button = "left",
            clickCount = 1
        };
        await _activeWebView.CoreWebView2.ExecuteScriptAsync(
            $"(() => {{ " +
            $"var e = new MouseEvent('mousedown', {{view: window, bubbles: true, cancelable: true, " +
            $"clientX: {x}, clientY: {y}, button: 0, buttons: 1}}); " +
            $"document.elementFromPoint({x}, {y})?.dispatchEvent(e); " +
            $"var clickE = new MouseEvent('click', {{view: window, bubbles: true, cancelable: true, " +
            $"clientX: {x}, clientY: {y}, button: 0, buttons: 0}}); " +
            $"document.elementFromPoint({x}, {y})?.dispatchEvent(clickE); " +
            $"}})();"
        );
        return AutomationResult.Success("已在坐标 (" + x + "," + y + ") 点击");
    });
}
```

**注意**：WebView2 不支持 `Input.dispatchMouseEvent` CDP 调用（那是 ChromeExtension 才有的），所以改用 JS `MouseEvent` 方式。更可靠的方式是利用 `document.elementFromPoint(x, y)` 找到目标元素再操作。

#### 2. 更稳健的实现：elementFromPoint + click

```csharp
public async Task<AutomationResult> ClickAtAsync(int x, int y)
{
    return await RunOnUiThreadAsync("ClickAt", async () =>
    {
        if (_activeWebView?.CoreWebView2 == null)
            return AutomationResult.Failure("当前没有激活的浏览器视图");

        var js = $"(() => {{ " +
            $"var el = document.elementFromPoint({x}, {y}); " +
            $"if (!el) return '{{\"error\":\"no_element_at_point\"}}'; " +
            $"el.dispatchEvent(new MouseEvent('mouseover', {{bubbles:true}})); " +
            $"el.dispatchEvent(new MouseEvent('mousedown', {{bubbles:true, cancelable:true}})); " +
            $"el.dispatchEvent(new MouseEvent('mouseup', {{bubbles:true, cancelable:true}})); " +
            $"el.dispatchEvent(new MouseEvent('click', {{bubbles:true, cancelable:true}})); " +
            $"return '{{\"ok\":true, \"tag\":el.tagName, \"id\":el.id}}'; " +
            $"}})();";
        
        var result = await _activeWebView.CoreWebView2.ExecuteScriptAsync(js);
        return AutomationResult.Success("坐标点击结果: " + result);
    });
}
```

#### 3. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中注册

```
"browser_click_at" => Format(await _automation.ClickAtAsync(
    args.GetInt("x"), args.GetInt("y")))
```

参数 schema：

```json
{
    "name": "browser_click_at",
    "description": "在视口绝对坐标 (x, y) 处执行点击。x 为距左边的像素距离，y 为距顶部的像素距离。用于 element_id 失效时的兜底点击。",
    "input_schema": {
        "type": "object",
        "properties": {
            "x": { "type": "integer", "description": "视口内 X 坐标（像素，从左到右）" },
            "y": { "type": "integer", "description": "视口内 Y 坐标（像素，从上到下）" }
        },
        "required": ["x", "y"]
    }
}
```

#### 4. 配合 snapshot 增加元素坐标信息

当前 snapshot 已移除 `rect` 字段以减少上下文大小。但为了坐标点击兜底，需要在 snapshot 中返回元素的 viewport 相对位置。

**方案 A**：在 snapshot 中增加 `viewport_x` / `viewport_y` 字段（元素左上角相对于视口的位置）：

```javascript
// collectElementInfo 中增加
viewport_x: function(el) {
    var rect = el.getBoundingClientRect();
    return Math.round(rect.left);
},
viewport_y: function(el) {
    var rect = el.getBoundingClientRect();
    return Math.round(rect.top);
}
```

**方案 B**：增加 `viewport_center_x` / `viewport_center_y`（元素中心坐标，更适合点击）：

```javascript
viewport_center: function(el) {
    var rect = el.getBoundingClientRect();
    return {
        x: Math.round(rect.left + rect.width / 2),
        y: Math.round(rect.top + rect.height / 2)
    };
}
```

**推荐方案 B**：返回中心坐标，AI 可以直接使用而不必手动计算。

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs`、`Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` |
| 风险 | `elementFromPoint` 可能命中覆盖层而非目标元素 |
| 缓解 | 在 snapshot 中同时提供 `viewport_center` 和 `overlapped` 标记，AI 可以选择未被遮挡的元素坐标 |
| 字段膨胀 | `viewport_center` 是两个整数，每个元素约 +20 字节，对 100 个元素约 +2KB，可控 |

---

## 2.2 多动作批处理（Multi-Action Batching）

### 问题

当前每轮 LLM 调用最多返回一组 tool calls，执行完一轮后等待下一轮 LLM 响应。对于连续独立操作（如点击 3 个链接），效率低。

### 方案：扩展 `AgentOutput` 支持动作数组 + page-change guard

#### 1. 修改 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 支持批量执行

在 `BrowserAutomationToolRouter` 中新增 `browser_batch` 工具（或由 AI 在一次 tool call 中发出多个动作）：

**方案 A：独立的 `browser_batch` 工具**

```json
{
    "name": "browser_batch",
    "description": "一次性执行多个浏览器动作。每个动作之间自动检测页面变化，如果发生导航则终止后续动作。",
    "input_schema": {
        "type": "object",
        "properties": {
            "actions": {
                "type": "array",
                "items": {
                    "type": "object",
                    "properties": {
                        "type": { "type": "string", "enum": ["click", "type", "hover", "press_key", "scroll"] },
                        "element_id": { "type": "integer" },
                        "text": { "type": "string" },
                        "key": { "type": "string" },
                        "delta_x": { "type": "integer" },
                        "delta_y": { "type": "integer" }
                    },
                    "required": ["type"]
                }
            }
        },
        "required": ["actions"]
    }
}
```

**方案 B（推荐）：利用现有 tool call 数组，在 router 层自动批处理**

当前 AI 可以在一轮中发出多个 tool calls（如 `[browser_click(3), browser_type(5, "hello")]`），这些 call 目前是并行执行的。改为**串行执行 + page-change guard**：

```csharp
// 在 Demo/BrowserDemo/ViewModels/ChatViewModel.cs ExecuteAiToolAsync 中，对非交互型工具（navigate, press_key, scroll）做串行批处理
private async Task<string> ExecuteBatchedToolsAsync(List<ToolCallAccumulator> toolCalls)
{
    var results = new List<string>();
    string? baseUrl = null;
    
    foreach (var tc in toolCalls.OrderBy(x => x.Index))
    {
        // 检测页面是否变化
        if (baseUrl != null && tc.Tool != "browser_navigate")
        {
            var currentUrl = _browserHost?.CurrentUrl;
            if (currentUrl != null && !currentUrl.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase))
            {
                // 页面已导航，终止批处理
                results.Add($"[PAGE_CHANGED] 页面从 {baseUrl} 导航到 {currentUrl}，后续动作已终止");
                break;
            }
        }
        
        var result = await ExecuteSingleToolAsync(tc);
        results.Add(result);
        
        // 记录当前 URL 供后续检测
        baseUrl = _browserHost?.CurrentUrl ?? baseUrl;
    }
    
    return string.Join("\n", results);
}
```

#### 2. 限制批处理数量

为避免单轮 tool call 过多导致上下文爆炸，限制每轮最多 5 个连续动作：

```csharp
const int MaxBatchActions = 5;
```

在 `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` 中检测 tool call 数量，超过时拆分多轮。

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/ViewModels/ChatViewModel.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` |
| 风险 | 批处理可能导致一轮 tool call 过多，压缩时更难定位边界 |
| 缓解 | 在压缩时保留 batch 结果中的关键信息（如成功点击的元素 ID） |
| 复杂度 | 中等，需要仔细处理 page-change guard 和结果拼接 |

---

## 2.3 输入值不匹配检测

### 问题

`browser_type` 写入值后不验证实际输入框的值。某些场景下（日期格式化、autocomplete、Vue/React 绑定），写入的值可能被框架覆盖或修改。

### 方案：在 `browser_type` 结果中增加 `actual_value` 对比

#### 1. 修改 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 中的 `TypeAsync`

```csharp
public async Task<AutomationResult> TypeAsync(int elementId, string text, bool clearFirst = true)
{
    return await RunOnUiThreadAsync("TypeAsync", async () =>
    {
        // ... 现有逻辑 ...
        
        // 写入完成后读取实际值
        var verifyJs = $"(() => {{ " +
            $"var el = document.querySelector('[data-bermain-id=\"{elementId}\"]'); " +
            $"return {{ " +
            $"  expected: {EscapeJs(text)}, " +
            $"  actual: el ? el.value : 'not_found', " +
            $"  tag: el ? el.tagName.toLowerCase() : 'unknown' " +
            $"}}; " +
            $"}})();";
        
        var verifyResult = await _activeWebView.CoreWebView2.ExecuteScriptAsync(verifyJs);
        // 解析 verifyResult，对比 expected vs actual
        // 如果不一致，返回 WARNING
    });
}
```

#### 2. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中格式化结果

```csharp
case "browser_type":
    var clear = args.GetBool("clear_first", true);
    var result = await _automation.TypeAsync(elementId, text, clear);
    
    // 检查结果中是否包含 actual_value 字段
    if (result.DataContains("actual_value"))
    {
        var expected = result.ExtractField("expected");
        var actual = result.ExtractField("actual_value");
        if (expected != actual)
        {
            return $"{{\"ok\":true, \"data\":\"已输入文本，但值不匹配。expected={EscapeJson(expected)}, actual={EscapeJson(actual)}\"}}";
        }
    }
    return Format(result);
```

#### 3. 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 的 typeInElement 中也做验证

```javascript
typeInElement: function(id, text, clearFirst) {
    var el = this.getElementById(id);
    if (!el) return { success: false, error: 'element_not_found' };
    
    if (clearFirst) {
        el.value = '';
        el.dispatchEvent(new Event('input', { bubbles: true }));
    }
    
    // 原生值设置
    this.setNativeValue(el, text);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('blur', { bubbles: true }));
    
    // 验证最终值
    var finalValue = el.value;
    return {
        success: true,
        expected: text,
        actual: finalValue,
        mismatch: text !== finalValue
    };
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` |
| 风险 | 极低，仅增加一次 value 读取 |
| 收益 | 可及时发现日期格式化、autocomplete 等值修改场景 |

---

## 2.4 按元素滚动

### 问题

当前 `browser_scroll(delta_x, delta_y)` 只能按固定像素滚动，AI 无法精准定位到某个元素的位置。

### 方案：增加 `browser_scroll_to_element(element_id)` 工具

#### 1. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 中增加方法

```csharp
public async Task<AutomationResult> ScrollToElementAsync(int elementId)
{
    return await RunOnUiThreadAsync("ScrollToElement", async () =>
    {
        if (_activeWebView?.CoreWebView2 == null)
            return AutomationResult.Failure("当前没有激活的浏览器视图");

        var js = $"(() => {{ " +
            $"var el = document.querySelector('[data-bermain-id=\"{elementId}\"]'); " +
            $"if (!el) return '{{\"error\":\"element_not_found\"}}'; " +
            $"el.scrollIntoView({{behavior:'smooth', block:'center', inline:'nearest'}}); " +
            $"return '{{\"ok\":true, \"scrolled_to\": el.tagName.toLowerCase() + (el.id ? '#' + el.id : '')}}'; " +
            $"}})();";
        
        var result = await _activeWebView.CoreWebView2.ExecuteScriptAsync(js);
        return AutomationResult.Success("滚动到元素结果: " + result);
    });
}
```

#### 2. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中注册

```json
{
    "name": "browser_scroll_to_element",
    "description": "滚动页面使指定元素出现在视口中。元素由 snapshot 中的 element_id 指定。",
    "input_schema": {
        "type": "object",
        "properties": {
            "element_id": { "type": "integer", "description": "snapshot 中的元素 ID" }
        },
        "required": ["element_id"]
    }
}
```

#### 3. 配合 `browser_wait` 使用

滚动后页面可能需要时间渲染，建议在工具描述中提示 AI 在 `scroll_to_element` 后跟 `browser_wait(300)` 等待渲染。

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` |
| 风险 | `scrollIntoView` 在某些页面可能触发 resize 事件导致布局变化 |
| 缓解 | 使用 `behavior: 'smooth'` 而非瞬间滚动，给页面布局调整时间 |

---

## 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 新增 `ClickAtAsync`、`ScrollToElementAsync` | P0 |
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | snapshot 中增加 `viewport_center` 字段 | P0 |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 注册 `browser_click_at`、`browser_scroll_to_element` 工具 | P0 |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 实现 `browser_type` 结果中的值不匹配检测 | P0 |
| `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` | 多动作批处理串行执行 + page-change guard | P1 |

## 预估工作量

- 坐标点击兜底（含 viewport_center）：0.5 天
- 输入值不匹配检测：0.25 天
- 按元素滚动：0.25 天
- 多动作批处理：1 天

## 验收标准

1. `browser_click_at(x, y)` 能在视口任意坐标触发点击事件
2. snapshot 中每个元素携带 `viewport_center: {x, y}` 字段
3. `browser_type` 结果中显示 expected/actual 对比，不一致时标注 WARNING
4. `browser_scroll_to_element` 能将目标元素滚动到视口中央
5. 多轮 LLM 调用中，连续 3+ 个独立操作能在同一轮批处理完成
