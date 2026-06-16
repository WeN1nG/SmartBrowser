# 方案：错误恢复与自检测改进

> 来源：FromBrowserUse.md 第四节（4.1 ~ 4.6）
> 目标：增强运行时自我检测和恢复能力，减少 AI 在死胡同中浪费步数

---

## 4.1 DOM Text Hash 页面停滞检测

### 问题

当前 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 的"相同动作重复"检测只看 `{tool|args}|result_signature`，无法发现 AI 换了工具但页面内容没变化的情况。例如 AI 交替使用 `browser_click(3)` 和 `browser_hover(5)`，工具不同所以不会被检测为循环，但页面始终停留在同一个状态。

### 方案：在 snapshot 后计算 DOM text hash，连续停滞则告警

#### 1. 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 中增加 `getDomTextHash` 方法

```javascript
// 在 window.bermainA11y 中新增
getDomTextHash: function() {
    // 提取页面所有可见元素的 innerText，拼接后计算简单 hash
    var body = document.body;
    if (!body) return '0';
    
    // 只取可见文本（排除 script/style/隐藏元素）
    var walker = document.createTreeWalker(body, NodeFilter.SHOW_TEXT, {
        acceptNode: function(node) {
            var parent = node.parentElement;
            if (!parent) return NodeFilter.FILTER_REJECT;
            var tag = parent.tagName.toLowerCase();
            if (tag === 'script' || tag === 'style' || tag === 'noscript') 
                return NodeFilter.FILTER_REJECT;
            var cs = parent.getComputedStyle();
            if (cs.display === 'none' || cs.visibility === 'hidden') 
                return NodeFilter.FILTER_REJECT;
            return NodeFilter.FILTER_ACCEPT;
        }
    });
    
    var textParts = [];
    var node;
    while (node = walker.nextNode()) {
        var t = node.textContent.trim();
        if (t.length > 0) textParts.push(t);
    }
    
    var fullText = textParts.join(' ');
    return simpleHash(fullText);
},

// 简单的 DJB2 hash
simpleHash: function(str) {
    var hash = 5381;
    for (var i = 0; i < str.length; i++) {
        hash = ((hash << 5) + hash) + str.charCodeAt(i); // hash * 33 + c
    }
    // 转为正整数字符串
    return (hash >>> 0).toString(36);
}
```

#### 2. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 中暴露

```csharp
/// <summary>
/// 获取当前页面 DOM text hash，用于页面停滞检测
/// </summary>
public async Task<string> GetDomTextHashAsync()
{
    return await RunOnUiThreadAsync("GetDomTextHash", async () =>
    {
        if (_activeWebView?.CoreWebView2 == null)
            return "error:no_view";
        
        var result = await _activeWebView.CoreWebView2.ExecuteScriptAsync(
            "window.bermainA11y.getDomTextHash()");
        return result.Trim('"'); // 去掉 ExecuteScriptAsync 包裹的双引号
    });
}
```

#### 3. 在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 中集成

```csharp
// Demo/BrowserDemo/Services/AgentEventSelfHandler.cs
public class AgentEventSelfHandler
{
    private string? _previousDomTextHash;
    private int _consecutiveDomUnchanged;
    private const int MaxDomUnchangedBeforeAlert = 2; // 连续 2 次页面内容未变
    private const int MaxDomUnchangedBeforeTerminate = 4;

    /// <summary>
    /// 记录当前 snapshot 的 DOM text hash
    /// </summary>
    public void RecordDomTextHash(string domTextHash)
    {
        if (string.IsNullOrEmpty(domTextHash) || domTextHash == "0" || domTextHash.StartsWith("error"))
            return;
            
        if (_previousDomTextHash != null && _previousDomTextHash == domTextHash)
        {
            _consecutiveDomUnchanged++;
            
            if (_consecutiveDomUnchanged == MaxDomUnchangedBeforeAlert)
            {
                PendingSystemEvents.Add(new AgentEventInfo
                {
                    Code = "page_stalled",
                    Severity = "warning",
                    Message = $"[agent_event code=page_stalled severity=warning] 页面内容连续 2 次未变化（hash={domTextHash}），当前操作可能无效。请尝试导航到新页面或更换操作策略。"
                });
            }
            else if (_consecutiveDomUnchanged >= MaxDomUnchangedBeforeTerminate)
            {
                PendingSystemEvents.Add(new AgentEventInfo
                {
                    Code = "page_stalled_fatal",
                    Severity = "critical",
                    Message = $"[agent_event code=page_stalled_fatal severity=critical] 页面内容已连续 {MaxDomUnchangedBeforeTerminate} 次未变化，工具循环判定为死胡同，强制终止。"
                });
                _terminateMessage = "页面内容停滞超过阈值，强制终止。";
            }
        }
        else
        {
            _consecutiveDomUnchanged = 0;
        }
        
        _previousDomTextHash = domTextHash;
    }
    
    public void Reset()
    {
        _previousDomTextHash = null;
        _consecutiveDomUnchanged = 0;
        // ... 其他重置 ...
    }
}
```

#### 4. 在 `Demo/BrowserDemo/Services/AiClient.cs` 的 `ExecuteConversationAsync` 中调用

```csharp
// 在 snapshot 类工具执行后记录 hash
foreach (var tc in toolCallAcc.Values.OrderBy(x => x.Id))
{
    var isSnapshotTool = tc.Name is "browser_snapshot" or "observe_browser";
    
    // 执行工具...
    var toolResult = await ExecuteToolAsync(tc);
    
    // 如果是 snapshot 类工具，记录 DOM hash
    if (isSnapshotTool && eventHandler != null)
    {
        var hash = await _automation.GetDomTextHashAsync();
        eventHandler.RecordDomTextHash(hash);
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/AgentEventSelfHandler.cs`、`Demo/BrowserDemo/Services/AiClient.cs` |
| 风险 | DOM text hash 对动态内容（时钟、动画）敏感，可能产生误判 |
| 缓解 | 排除时间戳类文本（正则匹配 HH:MM:SS 模式）；提高终止阈值到 4 次 |

---

## 4.2 运行时 Replan 触发器

### 问题

`TaskStateMachine` 只在开始时强制规划，运行时不触发重新规划。AI 可能在错误的方向上浪费大量步数。

### 方案：维护 `consecutiveFailures` 计数器，达到阈值时注入 replan 提醒

#### 1. 在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 中增加失败计数

```csharp
public class AgentEventSelfHandler
{
    private int _consecutiveActionFailures;
    private const int ReplanThreshold = 3; // 连续 3 次失败后触发 replan

    public void RecordActionOutcome(bool isSuccess, string toolName, string result)
    {
        if (!isSuccess)
        {
            _consecutiveActionFailures++;
            
            if (_consecutiveActionFailures == ReplanThreshold)
            {
                PendingSystemEvents.Add(new AgentEventInfo
                {
                    Code = "replan_needed",
                    Severity = "warning",
                    Message = $"[agent_event code=replan_needed severity=warning] 已连续 {ReplanThreshold} 步操作失败或无进展。请调用 update_todo 重新规划子任务，调整执行策略。"
                });
            }
            else if (_consecutiveActionFailures > ReplanThreshold)
            {
                // 持续警告，逐步升级
                PendingSystemEvents.Add(new AgentEventInfo
                {
                    Code = "replan_critical",
                    Severity = "critical",
                    Message = $"[agent_event code=replan_critical severity=critical] 已连续 {_consecutiveActionFailures} 步失败，必须重新规划。"
                });
            }
        }
        else
        {
            _consecutiveActionFailures = 0; // 成功则重置
        }
    }
}
```

#### 2. 在 `Demo/BrowserDemo/Services/AiClient.cs` 中判定"成功/失败"

```csharp
private bool IsActionSuccessful(string toolName, string result)
{
    // 简单启发式：结果中包含 error/fail/not_found/stale 等关键词视为失败
    var lower = result.ToLowerInvariant();
    return !(lower.Contains("error") || lower.Contains("fail") || 
             lower.Contains("not_found") || lower.Contains("stale") ||
             lower.Contains("超时") || lower.Contains("失败"));
}
```

#### 3. 在 `Demo/BrowserDemo/Services/TaskStateMachine.cs` 中支持运行时 replan

```csharp
public class TaskStateMachine
{
    /// <summary>
    /// 运行时重新规划：允许在 Executing 状态下更新子任务列表
    /// </summary>
    public ReplanResult ProcessRuntimeReplan(List<AiTodoItem> newTodos)
    {
        if (_state == StateEnum.Planning)
            return ReplanResult.Error("Planning 阶段请使用 update_todo，而非 replan");
        
        if (_state == StateEnum.Complete)
            return ReplanResult.Error("任务已完成，无法重新规划");
        
        // Executing 状态下允许 replan
        _state = StateEnum.Planning; // 短暂回到 Planning，让 AI 重新规划
        _subtasks = newTodos;
        _activeSubtaskIndex = 0;
        
        return ReplanResult.Success("已重置为 Planning 状态，请调用 update_todo 重新规划");
    }
}
```

#### 4. 注册 `replan_task` 工具

```csharp
// Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs
"replan_task" => new ToolDefinition
{
    Name = "replan_task",
    Description = "当连续操作失败时，请求重新规划子任务列表。传入新的子任务数组。",
    InputSchema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["subtasks"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["status"] = new JsonObject { ["type"] = "string" }
                    }
                }
            }
        }
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs`、`Demo/BrowserDemo/Services/TaskStateMachine.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs`、`Demo/BrowserDemo/Services/AiClient.cs` |
| 风险 | `IsActionSuccessful` 启发式可能误判（某些工具的 "error" 是预期结果） |
| 缓解 | 对特定工具（如 `browser_click`）做白名单豁免；阈值可调 |

---

## 4.3 探索限制 Nudge

### 问题

AI 可能在页面上盲目点击/滚动，不关联任何已规划的子任务。

### 方案：跟踪每步是否关联 active subtask，连续游离则提醒

#### 1. 在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 中增加探索跟踪

```csharp
public class AgentEventSelfHandler
{
    private int _consecutiveExplorationSteps;
    private const int ExplorationLimit = 5; // 连续 5 步未关联子任务

    public void RecordStepWithSubtask(string toolName, string? activeSubtaskId)
    {
        if (string.IsNullOrEmpty(activeSubtaskId))
        {
            _consecutiveExplorationSteps++;
            
            if (_consecutiveExplorationSteps == ExplorationLimit)
            {
                PendingSystemEvents.Add(new AgentEventInfo
                {
                    Code = "exploration_limit",
                    Severity = "warning",
                    Message = $"[agent_event code=exploration_limit severity=warning] 已连续 {ExplorationLimit} 步操作未关联任何子任务。请调用 update_todo 制定明确计划，或调用 finish_subtask 完成任务。"
                });
            }
        }
        else
        {
            _consecutiveExplorationSteps = 0; // 有关联则重置
        }
    }
}
```

#### 2. 在 `Demo/BrowserDemo/Services/AiClient.cs` 中传递 subtask 关联信息

```csharp
// ExecuteConversationAsync 循环中
foreach (var tc in toolCallAcc.Values.OrderBy(x => x.Id))
{
    // 检查这个 tool call 是否属于当前 active subtask
    var subtaskId = GetCurrentActiveSubtaskId(); // 从 ChatViewModel 获取
    
    eventHandler.RecordStepWithSubtask(tc.Name, subtaskId);
    
    // ... 执行工具 ...
}
```

---

## 4.4 Budget 渐进警告

### 问题

当前只有硬上限 80，AI 不知道还剩多少步数，容易在最后几步才匆忙结束。

### 方案：在 50%/75%/90% 消耗点注入渐进式警告

#### 1. 在 `Demo/BrowserDemo/Services/AiClient.cs` 的 `ExecuteConversationAsync` 中增加预算检查

```csharp
public async IAsyncEnumerable<string> ExecuteConversationAsync(...)
{
    const int MaxIterations = 80;
    var budgetCheckpoints = new Dictionary<int, string>
    {
        [50] = "已使用 50% 预算（40/80），请检查当前进度，确保关键步骤已完成。",
        [75] = "已使用 75% 预算（60/80），建议开始整合结果，准备完成任务。",
        [90] = "仅剩 10% 预算（8/80），请立即总结当前进展并完成剩余操作。",
        [95] = "仅剩 5% 预算（4/80），必须在最后几步内完成任务或调用 finish_subtask。"
    };

    for (int iteration = 0; ; iteration++)
    {
        if (iteration >= MaxHardIterations) break;
        
        // 预算警告
        if (budgetCheckpoints.TryGetValue(iteration, out var warning))
        {
            PendingSystemEvents.Add(new AgentEventInfo
            {
                Code = "budget_warning",
                Severity = "info",
                Message = $"[agent_event code=budget_warning severity=info] {warning}"
            });
        }
        
        // ... 其余逻辑 ...
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/AiClient.cs` |
| 风险 | 极低，纯提示性信息 |
| 收益 | 引导 AI 提前收敛，避免最后匆忙 |

---

## 4.5 Judge 后验评估

### 问题

AI 说"完成了"就直接停止循环，可能遗漏关键步骤或错误判断任务完成。

### 方案：可选的 LLM judge 后验验证

#### 1. 新增 `Demo/BrowserDemo/Services/JudgeService.cs`

```csharp
/// <summary>
/// 任务完成度验证服务。当 AI 声称任务完成时，调用 judge model 审查执行痕迹。
/// </summary>
public class JudgeService
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _model;

    public JudgeService(string apiKey, string endpoint, string model)
    {
        _apiKey = apiKey;
        _endpoint = endpoint;
        _model = model;
    }

    public async Task<JudgeResult> EvaluateAsync(
        List<ChatMessage> conversationHistory,
        string userOriginalRequest,
        CancellationToken ct)
    {
        var judgePrompt = $@"你是任务完成度审查员。请审查以下浏览器自动化执行记录，判断任务是否真正完成。

用户原始请求：{userOriginalRequest}

执行记录：
{FormatTraceForJudge(conversationHistory)}

请输出 JSON：
{{
    ""completed"": true/false,
    ""confidence"": 0-1,
    ""reason"": "简短理由",
    ""missing_steps"": ["遗漏的步骤，如果没有则为空数组"]
}}";

        var response = await SendJudgeRequest(judgePrompt, ct);
        return ParseJudgeResponse(response);
    }
}
```

#### 2. 在 `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` 中集成

```csharp
private async Task<string> ExecuteAiToolAsync(string toolName, ...)
{
    if (toolName == "finish_subtask")
    {
        var status = args.GetString("status");
        if (status == "completed")
        {
            // 可选：触发 judge
            if (_judgeService != null && _settings?.EnableJudgeCompletion == true)
            {
                var result = await _judgeService.EvaluateAsync(
                    _conversationHistory, _originalRequest, ct);
                
                if (!result.Completed || result.Confidence < 0.7)
                {
                    // 注入系统消息，要求 AI 继续工作
                    InjectSystemMessage(
                        $"[judge_review] 后验评估认为任务未完成（置信度：{result.Confidence:F1}）。" +
                        $"理由：{result.Reason}。请继续执行。");
                }
            }
        }
    }
    
    // ... 原有逻辑 ...
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | 新增 `Demo/BrowserDemo/Services/JudgeService.cs`，修改 `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` |
| 风险 | 额外 LLM 调用增加延迟和成本 |
| 缓解 | 可选开关（`EnableJudgeCompletion`），默认关闭；使用轻量模型 |

---

## 4.6 Stable Hash 元素匹配

### 问题

当前 snapshot 的 `element_id` 是完全递增的整数，页面刷新后全部重置。AI 无法跨 snapshot 追踪同一个元素。

### 方案：为每个元素计算 `stable_hash`，基于 tag + aria-label + name + placeholder + text 的复合 hash

#### 1. 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 中增加 stable_hash 计算

```javascript
// 在 collectElementInfo 中增加
stable_hash: function(el) {
    // 基于多个属性的复合 hash
    var parts = [
        el.tagName.toLowerCase(),
        el.getAttribute('aria-label') || '',
        el.getAttribute('name') || '',
        el.getAttribute('placeholder') || '',
        (el.textContent || '').trim().substring(0, 50),
        el.getAttribute('type') || ''
    ];
    
    // 过滤空值后拼接
    var key = parts.filter(function(p) { return p.length > 0; }).join('|');
    return simpleHash(key);
}
```

#### 2. 在 snapshot 结果中暴露

```json
{
    "id": 5,
    "tag": "button",
    "aria_label": "提交",
    "stable_hash": "abc123",
    ...
}
```

#### 3. 在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 中支持 stale element 的 stable_hash 重试

```csharp
public class AgentEventSelfHandler
{
    /// <summary>
    /// 当 element_id 失效时，尝试用 stable_hash 在上一次 snapshot 中重新定位
    /// </summary>
    public string? FindElementByStableHash(string? stableHash, string? previousSnapshot)
    {
        if (string.IsNullOrEmpty(stableHash) || string.IsNullOrEmpty(previousSnapshot))
            return null;
        
        try
        {
            // 解析上一次 snapshot JSON，查找 matching stable_hash
            var elements = ParseSnapshotElements(previousSnapshot);
            var match = elements.FirstOrDefault(e => 
                e.stable_hash == stableHash);
            
            if (match != null)
                return $"stable_hash={stableHash} 匹配到 id={match.id}";
        }
        catch { }
        
        return null;
    }
}
```

#### 4. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中增加 `browser_click_by_hash` 工具

```json
{
    "name": "browser_click_by_hash",
    "description": "使用元素的 stable_hash 进行点击。当 element_id 失效时使用此工具。stable_hash 基于元素的 tag、aria-label、name、placeholder、text 计算，页面刷新后仍然有效。",
    "input_schema": {
        "type": "object",
        "properties": {
            "stable_hash": { "type": "string", "description": "元素的稳定哈希值" }
        },
        "required": ["stable_hash"]
    }
}
```

实现：

```csharp
// Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs
case "browser_click_by_hash":
    var hash = args.GetString("stable_hash");
    var result = await _automation.ClickByStableHashAsync(hash);
    return Format(result);
```

```csharp
// Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs
public async Task<AutomationResult> ClickByStableHashAsync(string stableHash)
{
    return await RunOnUiThreadAsync("ClickByStableHash", async () =>
    {
        if (_activeWebView?.CoreWebView2 == null)
            return AutomationResult.Failure("当前没有激活的浏览器视图");

        // 通过 stable_hash 查找元素并点击
        var js = $"(() => {{ " +
            $"var els = document.querySelectorAll('*'); " +
            $"for (var i = 0; i < els.length; i++) {{ " +
            $"  var el = els[i]; " +
            $"  var tag = el.tagName.toLowerCase(); " +
            $"  var al = (el.getAttribute('aria-label') || '').trim(); " +
            $"  var nm = (el.getAttribute('name') || '').trim(); " +
            $"  var ph = (el.getAttribute('placeholder') || '').trim(); " +
            $"  var tx = (el.textContent || '').trim().substring(0, 50); " +
            $"  var tp = (el.getAttribute('type') || '').toLowerCase(); " +
            $"  var key = [tag, al, nm, ph, tx, tp].filter(Boolean).join('|'); " +
            $"  if (simpleHash(key) === '{stableHash}') {{ " +
            $"    el.click(); " +
            $"    return '{{\"ok\":true, \"matched\": tag + (al ? '#' + al : \"\")}}'; " +
            $"  }} " +
            $"}} " +
            $"return '{{\"error\":\"no_match\"}}'; " +
            $"}})();";
        
        var result = await _activeWebView.CoreWebView2.ExecuteScriptAsync(js);
        return AutomationResult.Success("Stable hash 点击结果: " + result);
    });
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` |
| 风险 | stable_hash 可能有冲突（不同元素产生相同 hash） |
| 缓解 | 使用 36 进制 hash 增加唯一性；冲突时在工具结果中标注 "multiple_matches" |
| 字段膨胀 | 每个元素增加 ~8 字节，100 个元素约 +800 字节，可接受 |

---

## 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 增加 `getDomTextHash`、`stable_hash` 字段 | **P0** |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 增加 `GetDomTextHashAsync`、`ClickByStableHashAsync` | **P0** |
| `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` | 集成 DOM hash 停滞检测、失败计数、探索限制 | **P0** |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 注册 `browser_click_by_hash`、`replan_task` | P1 |
| `Demo/BrowserDemo/Services/AiClient.cs` | Budget 渐进警告、replan 触发器 | **P0** |
| `Demo/BrowserDemo/Services/TaskStateMachine.cs` | 运行时 replan 支持 | P1 |
| 新增 `Demo/BrowserDemo/Services/JudgeService.cs` | Judge 后验评估 | P2 |

## 预估工作量

- DOM text hash + 页面停滞检测：0.5 天
- Stable hash 元素匹配：0.75 天
- 运行时 Replan 触发器：0.5 天
- 探索限制 Nudge：0.25 天
- Budget 渐进警告：0.1 天
- Judge 后验评估：1 天

## 验收标准

1. `AgentEventSelfHandler` 能检测连续 2 次 DOM text hash 相同并注入 warning
2. 连续 4 次页面停滞后强制终止工具循环
3. snapshot 中每个元素携带 `stable_hash` 字段
4. `browser_click_by_hash` 能通过 stable_hash 定位并点击元素
5. 连续 3 次操作失败后注入 replan 提醒
6. 在 50%/75%/90% 预算点注入渐进警告
7. 可选的 Judge 服务在任务完成时进行后验验证
