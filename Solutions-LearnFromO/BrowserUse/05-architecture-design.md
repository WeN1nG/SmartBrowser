# 方案：架构设计层面改进

> 来源：FromBrowserUse.md 第五节（5.1 ~ 5.3）
> 目标：优化架构解耦、元素过滤和安全性

---

## 5.1 事件总线解耦

### 问题

当前调用链是同步方法链：`Demo/BrowserDemo/ViewModels/ChatViewModel.cs → Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs → Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs → WebView2.ExecuteScriptAsync`。所有逻辑耦合在一起，难以扩展拦截器（自动截图、录屏、安全审计）。

### 方案：引入轻量事件总线

#### 1. 设计 `BrowserEventBus`

```csharp
// Demo/BrowserDemo/Services/Automation/IBrowserEventBus.cs
public interface IBrowserEventBus
{
    /// <summary>订阅浏览器操作事件</summary>
    IDisposable Subscribe(IBrowserEventHandler handler);
    
    /// <summary>发布浏览器操作事件</summary>
    Task PublishAsync(IBrowserEvent @event, CancellationToken ct = default);
}

// Demo/BrowserDemo/Services/Automation/BrowserEvents.cs
public interface IBrowserEvent
{
    string EventType { get; }
    DateTime Timestamp { get; }
    Guid? TabId { get; }
}

public record ClickElementEvent(int ElementId, Guid? TabId = null) : IBrowserEvent
{
    public string EventType => "click_element";
    public DateTime Timestamp => DateTime.UtcNow;
}

public record TypeTextEvent(int ElementId, string Text, bool ClearFirst, Guid? TabId = null) : IBrowserEvent
{
    public string EventType => "type_text";
    public DateTime Timestamp => DateTime.UtcNow;
}

public record NavigateEvent(string Url, int TimeoutMs, Guid? TabId = null) : IBrowserEvent
{
    public string EventType => "navigate";
    public DateTime Timestamp => DateTime.UtcNow;
}

// ... 其他事件类型
```

#### 2. 实现简单 EventBus

```csharp
// Demo/BrowserDemo/Services/Automation/BrowserEventBus.cs
public class BrowserEventBus : IBrowserEventBus
{
    private readonly List<IBrowserEventHandler> _handlers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IDisposable Subscribe(IBrowserEventHandler handler)
    {
        _handlers.Add(handler);
        return new Subscription(this, handler);
    }

    public async Task PublishAsync(IBrowserEvent @event, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var handlersCopy = _handlers.ToList();
            foreach (var handler in handlersCopy)
            {
                if (ct.IsCancellationRequested) break;
                await handler.HandleAsync(@event, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private class Subscription : IDisposable
    {
        private readonly BrowserEventBus _bus;
        private readonly IBrowserEventHandler _handler;
        
        public Subscription(BrowserEventBus bus, IBrowserEventHandler handler)
        {
            _bus = bus;
            _handler = handler;
        }
        
        public void Dispose()
        {
            _bus._handlers.Remove(_handler);
        }
    }
}
```

#### 3. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 中集成

```csharp
// Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs
public class BrowserAutomationService
{
    private readonly IBrowserEventBus _eventBus;

    public BrowserAutomationService(IBrowserEventBus eventBus, ...)
    {
        _eventBus = eventBus;
        // ...
    }

    public async Task<AutomationResult> ClickAsync(int elementId)
    {
        // 发布事件，由 watchdog 执行
        await _eventBus.PublishAsync(new ClickElementEvent(elementId, _activeTabId));
        
        // 验证执行结果
        return AutomationResult.Success($"已点击元素 #{elementId}");
    }
}
```

#### 4. 示例：自动截图 Handler

```csharp
public class AutoScreenshotHandler : IBrowserEventHandler
{
    private readonly IBrowserAutomationService _automation;
    private int _lastScreenshotCount;

    public async Task HandleAsync(IBrowserEvent @event, CancellationToken ct)
    {
        // 每次 navigate 后自动截图
        if (@event is NavigateEvent)
        {
            await Task.Delay(500, ct); // 等待页面渲染
            await _automation.TakeScreenshotAsync();
        }
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | 新增 `Demo/BrowserDemo/Services/Automation/BrowserEvents.cs`、`Demo/BrowserDemo/Services/Automation/BrowserEventBus.cs`、`Demo/BrowserDemo/Services/Automation/IBrowserEventBus.cs`，修改 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` |
| 风险 | 事件总线增加了一层间接性，调试难度增加 |
| 缓解 | 保持同步调用为默认路径，事件总线作为可选扩展 |
| 复杂度 | 中等，需要仔细设计接口 |

**推荐策略**：当前阶段不引入完整的事件总线，而是在 `BrowserAutomationService` 中预留虚方法/事件钩子（如 `OnBeforeClick`、`OnAfterNavigate`），供外部订阅。这样既能扩展又不会过度设计。

---

## 5.2 Session-Specific Exclude Attributes

### 问题

页面中大量的 toast 通知、loading spinner、modal 对话框会出现在 snapshot 中，干扰 AI 判断。

### 方案：初始化时注入 JS，给 UI 覆盖层元素打上 session-specific exclude 属性

#### 1. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 的 `InitializeAsync` 中注入

```csharp
public async Task InitializeAsync(CoreWebView2Environment environment)
{
    _sessionId = Guid.NewGuid().ToString("N")[..8]; // 短 session ID
    
    // 注入 exclude 脚本
    var excludeScript = $@"
        (function() {{
            var sessionId = '{_sessionId}';
            var excludeAttr = 'data-browser-exclude-' + sessionId;
            
            // 标记常见的覆盖层元素
            var overlaySelectors = [
                '[class*=""toast""]',
                '[class*=""modal""]',
                '[class*=""overlay""]',
                '[class*=""spinner""]',
                '[class*=""loading""]',
                '[class*=""dialog""]',
                '[role=""dialog""]',
                '[role=""alertdialog""]',
                '[role=""tooltip""]',
                '.notification',
                '.popconfirm-mask',
                '.ant-modal-mask',
                '.ant-modal-wrap',
                '.el-overlay',
                '.el-dialog'
            ];
            
            overlaySelectors.forEach(function(selector) {{
                try {{
                    document.querySelectorAll(selector).forEach(function(el) {{
                        el.setAttribute(excludeAttr, 'true');
                    }});
                }} catch(e) {{ /* cross-origin */ }}
            }});
            
            // 定期扫描新增的覆盖层
            setInterval(function() {{
                var observer = new MutationObserver(function(mutations) {{
                    mutations.forEach(function(mutation) {{
                        mutation.addedNodes.forEach(function(node) {{
                            if (node.nodeType !== 1) return; // not element
                            var tag = (node.tagName || '').toLowerCase();
                            if (['div','span','section','article'].includes(tag)) {{
                                var cls = (node.className || '').toLowerCase();
                                if (cls.includes('toast') || cls.includes('modal') || 
                                    cls.includes('overlay') || cls.includes('loading') ||
                                    cls.includes('spinner') || cls.includes('dialog')) {{
                                    node.setAttribute(excludeAttr, 'true');
                                }}
                            }}
                        }});
                    }});
                }});
                observer.observe(document.body || document.documentElement, {{
                    childList: true,
                    subtree: true
                }});
            }}, 2000);
        }})();
    ";

    // 注入到每个新导航的页面
    _environmentSettings.UserScriptAddedStateChanged += OnUserScriptAddedStateChanged;
    await _environmentSettings.AddHostObjectToScript("__browserExclude", new BrowserExcludeHandler(_sessionId));
}
```

#### 2. 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 中的 `collectInteractive` 中排除带 exclude 属性的元素

```javascript
// AutomationScripts.cs 中的 INTERACTIVE_SELECTOR 查询后
function isExcludedForSession(el) {
    // 检查是否带有 session-specific exclude 属性
    var attrs = el.attributes;
    for (var i = 0; i < attrs.length; i++) {
        if (attrs[i].name.indexOf('data-browser-exclude-') === 0) {
            return true;
        }
    }
    return false;
}

// 在 collectInteractive 过滤链中加入
if (isExcludedForSession(el)) {
    return false; // 跳过此元素
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` |
| 风险 | CSS 选择器可能匹配不到某些框架的覆盖层（如 Ant Design、Element Plus） |
| 缓解 | 使用 MutationObserver 持续扫描新增元素；提供可配置的 exclude 选择器列表 |
| 注意 | 不要排除可交互的 modal（如登录对话框），否则 AI 无法操作 |

---

## 5.3 敏感数据脱敏

### 问题

密码、信用卡等敏感信息在 snapshot 中明文出现，并长期驻留在 LLM context 中（消息历史、压缩后的摘要），增加了泄露风险。

### 方案：在 snapshot 中对敏感字段做脱敏处理

#### 1. 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 的 `collectElementInfo` 中增加脱敏逻辑

```javascript
function collectElementInfo(el, id) {
    var info = {
        id: id,
        tag: el.tagName.toLowerCase(),
        // ... 其他字段 ...
    };
    
    if (el.tagName === 'INPUT') {
        var inputType = (el.type || '').toLowerCase();
        
        // 敏感字段脱敏
        if (inputType === 'password' || inputType === 'secret') {
            info.value = '<secret>';
            info.value_length = el.value.length; // 保留长度信息
        }
        else if (inputType === 'email' || inputType === 'tel' || inputType === 'text') {
            // 对 email/电话等做部分脱敏
            var val = el.value || '';
            if (val.length > 0) {
                var isEmail = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val);
                var isPhone = /^[\d\-+() ]{7,}$/.test(val);
                
                if (isEmail) {
                    var parts = val.split('@');
                    info.value = parts[0].charAt(0) + '***@' + parts[1];
                } else if (isPhone) {
                    info.value = val.substring(0, 3) + '****' + val.substring(val.length - 3);
                } else {
                    info.value = val; // 普通文本不脱敏
                }
            }
        }
        // 信用卡相关字段名脱敏
        else if (/card|credit|cvv|ssn|social.security/i.test(el.name || '')) {
            info.value = '<secret>';
            info.value_length = el.value.length;
        }
    }
    
    // textarea 和 contenteditable 也检查
    if (el.tagName === 'TEXTAREA' || el.isContentEditable) {
        var val = el.textContent || el.value || '';
        if (/password|secret|token|key|credit.card|ssn/i.test(el.name || el.id || '')) {
            info.value = '<secret>';
            info.value_length = val.length;
        }
    }
    
    return info;
}
```

#### 2. 在消息历史中自动脱敏

```csharp
// Demo/BrowserDemo/Services/AiClient.cs 中，在向 LLM 发送消息前对 tool result 做二次脱敏
private string SanitizeToolResult(string toolName, string result)
{
    if (toolName == "browser_snapshot")
    {
        // result 已经是 JSON 字符串，在 JS 端已脱敏，此处无需处理
        return result;
    }
    
    // 对其他工具的结果做通用脱敏
    // 检测 result 中是否包含可能泄露的敏感模式
    var patterns = new[]
    {
        @"(?i)(password|passwd|pwd)\s*[:=]\s*\S+",
        @"(?i)(credit\s*card|cc)\s*[:=]\s*\d{13,19}",
        @"(?i)(ssn|social\s*security)\s*[:=]\s*\d{3}-\d{2}-\d{4}",
        @"(?i)(api[_-]?key|secret[_-]?key|token)\s*[:=]\s*[A-Za-z0-9+/=]{16,}"
    };
    
    foreach (var pattern in patterns)
    {
        result = Regex.Replace(result, pattern, "$1: ***REDACTED***");
    }
    
    return result;
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`、`Demo/BrowserDemo/Services/AiClient.cs` |
| 风险 | 脱敏可能过度（如普通 text 字段的 name 恰好叫 "key"） |
| 缓解 | 使用宽松匹配；提供可配置的敏感字段名列表 |
| 配置 | 在 `AiSettings` 中增加 `SensitiveFields` 列表 |

---

## 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | Session-specific exclude 注入 | P2 |
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 排除 session-exclude 元素 + 敏感字段脱敏 | P1 |
| `Demo/BrowserDemo/Services/AiClient.cs` | 工具结果通用脱敏 | P2 |
| `Demo/BrowserDemo/Models/AiSettings.cs` | 敏感字段配置 | P2 |

## 预估工作量

- Session-specific exclude：0.5 天
- 敏感数据脱敏：0.5 天
- 事件总线：暂不实施（预留钩子即可）

## 验收标准

1. snapshot 中 `<input type="password">` 的 value 显示为 `<secret>`，value_length 保留
2. 常见 toast/modal/loading 元素不在 snapshot 中出现
3. MutationObserver 能自动标记新出现的覆盖层元素
4. 工具结果中的敏感模式（API key、信用卡号）被自动脱敏
