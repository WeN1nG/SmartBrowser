# 方案：LLM 集成层面改进

> 来源：FromBrowserUse.md 第三节（3.1 ~ 3.6）
> 目标：优化 AI 决策质量和长任务连贯性

---

## 3.1 结构化状态表示（XML 标签分段）

### 问题

当前 `Demo/BrowserDemo/Services/ContextBuilder.cs` 的 `Build()` 方法将系统提示 + 动态上下文拼接在一个 string 里，靠自然语言分隔。LLM 难以精确区分"任务要求"、"历史记录"、"当前页面状态"等区块。

### 方案：在 `Build()` 中使用 XML 标签分隔信息块

#### 修改 `Demo/BrowserDemo/Services/ContextBuilder.cs` — `Build()` 方法

```csharp
public string Build()
{
    if (!IsEnabled) return string.Empty;

    var sb = new StringBuilder();

    // 1. 系统角色和身份
    sb.AppendLine("<system_identity>");
    sb.Append(BuildSystemIdentity());
    sb.AppendLine("</system_identity>");

    // 2. 行为准则和工具使用规则
    sb.AppendLine("<behavior_rules>");
    sb.Append(BuildBehaviorGuidelines());
    sb.AppendLine("</behavior_rules>");

    // 3. 输出格式要求
    sb.AppendLine("<output_format>");
    sb.Append(BuildOutputFormat());
    sb.AppendLine("</output_format>");

    // 4. 动态上下文：当前浏览器状态
    sb.AppendLine("<browser_state>");
    sb.AppendLine($"  <current_url>{HtmlEncode(CurrentPageUrl)}</current_url>");
    sb.AppendLine($"  <current_title>{HtmlEncode(CurrentPageTitle)}</current_title>");
    if (!string.IsNullOrEmpty(PageContentPreview))
    {
        sb.AppendLine($"  <page_content_preview>{HtmlEncode(PageContentPreview)}</page_content_preview>");
    }
    sb.AppendLine($"  <tabs>{string.Join(", ", _browserHost?.Tabs.Select(t => t.Title ?? t.Url) ?? new string[0])}</tabs>");
    sb.AppendLine("</browser_state>");

    // 5. 用户原始请求（第一条 user 消息）
    sb.AppendLine("<user_request>");
    sb.AppendLine(HtmlEncode(_originalUserRequest ?? ""));
    sb.AppendLine("</user_request>");

    // 6. 历史对话（由调用方在 messages 中传入，此处不拼接）
    // ContextBuilder 只负责系统提示，历史消息由 AiClient 管理

    var result = sb.ToString().Trim();
    return result;
}
```

#### 新增辅助方法

```csharp
private string HtmlEncode(string text)
{
    if (string.IsNullOrEmpty(text)) return "";
    // 简单 HTML 编码，防止 XML 标签注入
    return text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
```

#### 系统提示词中增加 XML 使用说明

在 `AppendOutputFormat` 中添加：

```
你的输入信息使用 XML 标签分隔：
- `<system_identity>` — 你的身份和角色
- `<behavior_rules>` — 行为准则和工具使用规则
- `<browser_state>` — 当前浏览器状态（URL、标题、标签页）
- `<user_request>` — 用户的原始请求

在回复时，请在结论前输出结构化思考字段（见 3.2 节）。
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/ContextBuilder.cs` |
| 风险 | XML 标签增加系统提示词长度，但对 LLM 理解有帮助 |
| 兼容性 | 不影响现有消息结构，仅改变系统提示词的格式 |

---

## 3.2 强制结构化思考框架

### 问题

当前 AI 输出格式为 `[思考过程]` / `[结论]` 自由文本，缺乏结构化字段追踪任务进度和上一步评估。

### 方案：在 prompt 中强制 AI 输出三个结构化字段

#### 修改 `Demo/BrowserDemo/Services/ContextBuilder.cs` — `AppendOutputFormat`

```csharp
private void AppendOutputFormat(StringBuilder sb)
{
    sb.AppendLine("## 输出格式要求");
    sb.AppendLine();
    sb.AppendLine("每次回复必须按以下格式输出：");
    sb.AppendLine();
    sb.AppendLine("```");
    sb.AppendLine("[上步评估] 简要评价上一步操作的执行结果和目标达成情况。");
    sb.AppendLine("  - 操作：做了什么（哪个工具，对哪个元素）");
    sb.AppendLine("  - 结果：成功/失败/部分成功");
    sb.AppendLine("  - 问题：是否有异常或需要调整的地方");
    sb.AppendLine();
    sb.AppendLine("[进度记忆] 记录当前任务进度。");
    sb.AppendLine("  - 已完成：已经完成了哪些子任务/步骤");
    sb.AppendLine("  - 进行中：当前正在执行哪一步");
    sb.AppendLine("  - 待完成：还有哪些步骤需要做");
    sb.AppendLine();
    sb.AppendLine("[下步目标] 明确下一步要做什么，以及为什么。");
    sb.AppendLine("  - 目标：下一步的具体操作");
    sb.AppendLine("  - 依据：基于什么判断需要做这一步");
    sb.AppendLine();
    sb.AppendLine("[结论]");
    sb.AppendLine("  工具调用 1: {\"name\": \"browser_click\", \"arguments\": {\"element_id\": 5}}");
    sb.AppendLine("  工具调用 2: ...");
    sb.AppendLine("  或直接输出文本（如果是最终回答）");
    sb.AppendLine("```");
    sb.AppendLine();
    sb.AppendLine("注意：");
    sb.AppendLine("- 每个字段都必须填写，不要省略");
    sb.AppendLine("- [上步评估] 必须引用具体的工具调用结果");
    sb.AppendLine("- [进度记忆] 要与 TaskStateMachine 的子任务状态保持一致");
    sb.AppendLine("- 如果不需要工具调用，在 [结论] 中直接给出最终回答");
}
```

#### 在 `AgentEventSelfHandler` 中增加结构化思考缺失检测

当 AI 输出不包含 `[上步评估]` / `[进度记忆]` / `[下步目标]` 标记时，注入提醒：

```csharp
// 在 BeforeToolExecution 中
public ToolSelfHandlingDecision BeforeToolExecution(
    string toolName, 
    Dictionary<string, object?> args,
    string aiTextResponse)
{
    // 新增：检查结构化思考字段
    if (!_structuredThinkingInjected && 
        !aiTextResponse.Contains("[上步评估]") &&
        !aiTextResponse.Contains("[进度记忆]"))
    {
        // 第一次缺失，注入提醒
        PendingSystemEvents.Add(new AgentEventInfo
        {
            Code = "missing_structured_thinking",
            Severity = "info",
            Message = "请按照要求的格式输出结构化思考字段：[上步评估]、[进度记忆]、[下步目标]"
        });
        _structuredThinkingInjected = true;
    }
    
    // ... 原有逻辑 ...
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/ContextBuilder.cs`、`Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` |
| 风险 | 极低，仅改变 prompt 格式要求 |
| 收益 | AI 在多步任务中的连贯性和可追踪性显著提升 |

---

## 3.3 截图作为视觉辅助（Browser Vision）

### 问题

当前 `browser_screenshot` 仅返回 base64 长度等元数据，不将图片发给 AI。对于表格、图表、弹窗等视觉信息丰富的页面，纯文本 snapshot 丢失了大量上下文。

### 方案：可选的多模态截图注入

#### 1. 在 `Demo/BrowserDemo/Models/AiSettings.cs` 中增加截图偏好配置

```csharp
// Models/AiSettings.cs 中新增
public class BrowserVisionSettings
{
    /// <summary>是否启用截图辅助（仅对多模态模型有效）</summary>
    public bool EnableVision { get; set; } = false;
    
    /// <summary>触发截图的条件：never / always / on_demand</summary>
    public string TriggerMode { get; set; } = "on_demand";
    
    /// <summary>on_demand 模式下，AI 需要提供的截图理由关键词</summary>
    public string[] ReasonKeywords { get; set; } = 
        { "视觉", "截图", "screenshot", "visual", "表格", "图表", "弹窗", "验证码" };
}
```

#### 2. 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 中强化截图理由验证

当前已有 `ScreenshotWithReasonAsync`，改为根据 `TriggerMode` 动态调整：

```csharp
private async Task<string> ScreenshotWithReasonAsync(Dictionary<string, object?> args)
{
    var mode = _settings?.Vision?.TriggerMode ?? "on_demand";
    
    if (mode == "always")
    {
        // 无条件截图，但控制频率（每 5 轮最多 1 次）
        if (_screenshotCount % 5 != 0)
            return "{\"data\":\"截图已跳过（频率限制：每5轮1次）\", \"skipped\": true}";
    }
    else if (mode == "on_demand")
    {
        // 需要 AI 提供合理理由（已有逻辑）
        var reason = args.GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return "{\"error\": \"on_demand 模式下必须提供截图理由 (reason)\"}";
    }
    
    // ... 原有截图逻辑 ...
}
```

#### 3. 在 `Demo/BrowserDemo/Services/AiClient.cs` 中支持多模态消息构造

当使用多模态模型时，将截图 base64 作为 `image_url` 注入消息：

```csharp
// 在 StreamRichEventsAsync 构造请求时
if (_providerInfo?.SupportsVision == true && _settings?.Vision?.EnableVision == true)
{
    // 将 screenshot 结果中的 base64 数据转换为 content_block
    var screenshotBase64 = ExtractScreenshotBase64(toolResult);
    if (!string.IsNullOrEmpty(screenshotBase64))
    {
        messages.Add(new ChatMessage
        {
            Role = MessageRole.Tool,
            ToolCallId = toolCallId,
            Content = $"[截图元数据: {toolResult}]\n\n视觉辅助: 已附加截图"
        });
        // 在请求体中附加 image_url content block
        AttachVisionImage(toolCallId, screenshotBase64);
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Models/AiSettings.cs`、`Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs`、`Demo/BrowserDemo/Services/AiClient.cs`、`Demo/BrowserDemo/Services/ContextBuilder.cs` |
| 风险 | 截图 base64 占用大量上下文空间；仅多模态模型受益 |
| 缓解 | 频率限制（每 5 轮 1 次）、分辨率缩放、按需触发 |
| 成本 | 多模态请求的 token 消耗更高，需在 AiSettings 中告知用户 |

---

## 3.4 LLM 摘要压缩替代简单截断

### 问题

当前 `CompressHistory` 采用"保留首条 + 最近 N 条"的截断策略。对于长任务，早期的重要信息（如用户原始请求、子任务规划）被保留，但中间的执行细节全部丢失。

### 方案：在压缩时调用 LLM 生成摘要

#### 1. 新增 `SummarizeHistoryAsync` 方法

```csharp
// Demo/BrowserDemo/Services/AiClient.cs 中新增
private async Task<string> SummarizeHistoryAsync(
    List<ChatMessage> messagesToSummarize,
    CancellationToken ct)
{
    var summaryPrompt = new StringBuilder();
    summaryPrompt.AppendLine("你是一个任务进度摘要助手。请阅读以下浏览器自动化执行历史，生成一段简洁的任务进展摘要。");
    summaryPrompt.AppendLine();
    summaryPrompt.AppendLine("要求：");
    summaryPrompt.AppendLine("1. 保留用户原始请求的核心目标");
    summaryPrompt.AppendLine("2. 列出已完成的关键步骤和结果");
    summaryPrompt.AppendLine("3. 指出当前遇到的困难或阻塞");
    summaryPrompt.AppendLine("4. 建议下一步方向");
    summaryPrompt.AppendLine("5. 控制在 500 字以内");
    summaryPrompt.AppendLine();
    summaryPrompt.AppendLine("执行历史：");
    
    foreach (var msg in messagesToSummarize)
    {
        summaryPrompt.AppendLine($"{msg.Role}: {msg.Content}");
    }
    
    // 使用当前 provider 的轻量模型（如 haiku/gpt-4o-mini）发送摘要请求
    var summaryRequest = new ChatCompletionRequest
    {
        Model = _lightweightModel ?? _currentModel,
        Messages = new[]
        {
            new ChatMessage { Role = MessageRole.User, Content = summaryPrompt.ToString() }
        },
        MaxTokens = 1000,
        Temperature = 0.3
    };
    
    return await SendRequestAsync(summaryRequest, ct);
}
```

#### 2. 修改 `CompressHistory` 使用摘要

```csharp
private void CompressHistory(List<ChatMessage> messages, int targetBytes, ISet<string> toolEvidence)
{
    var totalBytes = EstimateConversationBytes(messages);
    if (totalBytes <= ContextCompressionTriggerBytes) return;
    
    // 分离需要保留的消息和可以摘要的消息
    var keepMessages = new List<ChatMessage>();  // 首条 user + 证据消息
    var summarizeMessages = new List<ChatMessage>();
    
    for (int i = 1; i < messages.Count - 1; i++) // 跳过首条和最后一条
    {
        if (toolEvidence.Contains(messages[i].Id))
            keepMessages.Add(messages[i]);
        else
            summarizeMessages.Add(messages[i]);
    }
    
    // 对 summarizeMessages 做 LLM 摘要
    if (summarizeMessages.Count > 0 && _summarizeFunc != null)
    {
        var summary = _summarizeFunc(summarizeMessages); // 同步版本，避免在压缩时阻塞
        messages.Clear();
        messages.Add(messages[0]); // 原始 user 请求
        messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"[任务进展摘要] {summary}"
        });
        messages.AddRange(keepMessages);
        messages.AddRange(messages.Skip(messages.Count - 2)); // 最近的 2 条
    }
    else
    {
        // 降级到原有截断策略
        TruncateHistory(messages, targetBytes, toolEvidence);
    }
}
```

#### 3. 在 `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` 中注入摘要函数

```csharp
// ChatViewModel 构造函数或初始化时
_aiClient.SetSummarizer(async (messages) =>
{
    var text = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
    // 使用同步的 HTTP 调用（不依赖 AiClient 自身的流式管道）
    using var http = new HttpClient();
    var request = new { model = LightweightModel, messages = new[] { 
        new { role = "system", content = SummaryPrompt },
        new { role = "user", content = text }
    }, max_tokens = 1000 };
    var response = await http.PostAsync(ApiUrl, 
        new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
    var result = await response.Content.ReadAsStringAsync();
    return ExtractSummary(result);
});
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/AiClient.cs`、`Demo/BrowserDemo/ViewModels/ChatViewModel.cs` |
| 风险 | 摘要调用本身消耗 token 和时间；可能丢失关键细节 |
| 缓解 | 仅在压缩触发时调用；使用轻量模型；提供开关让用户控制 |
| 降级 | 如果摘要调用失败，回退到原有截断策略 |

---

## 3.5 结构化输出 Schema

### 问题

当前 AI 输出自由文本 + tool_calls，缺乏结构化决策字段。AI 可能忘记评估上一步结果或更新任务进度。

### 方案：利用 OpenAI/Anthropic 的 JSON mode 强制结构化输出

#### 1. 定义结构化输出 Schema

```json
{
    "type": "object",
    "properties": {
        "thinking": {
            "type": "string",
            "description": "当前推理过程和观察"
        },
        "evaluation": {
            "type": "string",
            "description": "上一步操作结果评估"
        },
        "memory": {
            "type": "string",
            "description": "当前任务进度记忆"
        },
        "next_goal": {
            "type": "string",
            "description": "下一步目标和依据"
        },
        "actions": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "tool": { "type": "string" },
                    "arguments": { "type": "object" }
                },
                "required": ["tool", "arguments"]
            }
        }
    },
    "required": ["thinking", "actions"]
}
```

#### 2. 在 `Demo/BrowserDemo/Services/ContextBuilder.cs` 中提示使用 JSON 输出

```
请使用 JSON 格式输出你的决策：
{
  "thinking": "你的推理...",
  "evaluation": "上步结果评估...",
  "memory": "进度记忆...",
  "next_goal": "下一步目标...",
  "actions": [
    {"tool": "browser_click", "arguments": {"element_id": 5}},
    ...
  ]
}
```

#### 3. 在 `Demo/BrowserDemo/Services/AiClient.cs` 中解析结构化输出

```csharp
// 在 ParseStreamRichAsync 中，当检测到 JSON mode 时
if (responseFormat == "json_schema" || responseFormat == "json")
{
    // 解析 JSON 中的 actions 数组
    var parsed = JsonSerializer.Deserialize<StructuredOutput>(finishContent);
    if (parsed?.actions != null)
    {
        foreach (var action in parsed.actions)
        {
            toolCalls.Add(new ToolCallAccumulator
            {
                Name = action.tool,
                Arguments = action.arguments
            });
        }
    }
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Services/AiClient.cs`、`Demo/BrowserDemo/Services/ContextBuilder.cs` |
| 风险 | 不同 provider 的 JSON mode 支持程度不同；部分模型可能输出无效 JSON |
| 缓解 | 提供 fallback：如果 JSON 解析失败，回退到自由文本解析 |
| 兼容性 | 仅在使用 JSON mode 的 provider 配置下生效 |

---

## 3.6 降级 LLM（Fallback Provider）

### 问题

当前 `AiClient` 遇到 rate limit / 401 / 402 / 5xx 时仅重试当前 provider，没有切换到备用 provider 的机制。

### 方案：在 `AiSettings` 中配置 backup provider，连续失败后自动切换

#### 1. 扩展 `Demo/BrowserDemo/Models/AiSettings.cs` 支持 fallback

```csharp
// Models/AiSettings.cs
public class FallbackSettings
{
    /// <summary>备用 provider 名称（对应 ProviderManager 中的 key）</summary>
    public string? FallbackProvider { get; set; }
    
    /// <summary>连续失败多少次后触发切换</summary>
    public int FailureThreshold { get; set; } = 3;
    
    /// <summary>触发切换后冷却时间（秒）</summary>
    public int CoolDownSeconds { get; set; } = 60;
}
```

#### 2. 在 `Demo/BrowserDemo/Services/AiClient.cs` 中实现切换逻辑

```csharp
// Demo/BrowserDemo/Services/AiClient.cs
private int _consecutiveFailures;
private DateTime? _lastFallbackSwitch;
private string? _primaryProvider;

private async IAsyncEnumerable<string> StreamRichEventsAsync(...)
{
    try
    {
        await foreach (var evt in InnerStreamAsync(messages, ct))
        {
            yield return evt;
        }
        _consecutiveFailures = 0; // 成功后重置
    }
    catch (HttpRequestException ex) when (IsRetryableError(ex))
    {
        _consecutiveFailures++;
        
        if (_consecutiveFailures >= _settings?.Fallback?.FailureThreshold &&
            CanSwitchFallback())
        {
            Logger.Warn($"AI 连续 {_consecutiveFailures} 次请求失败，切换到备用 provider");
            SwitchToFallbackProvider();
            _consecutiveFailures = 0;
            
            // 重试一次
            yield return await foreach (var evt in InnerStreamAsync(messages, ct))
            {
                yield return evt;
            }
        }
        else
        {
            throw;
        }
    }
}

private bool CanSwitchFallback()
{
    if (_settings?.Fallback?.FallbackProvider == null) return false;
    if (_lastFallbackSwitch == null) return true;
    return (DateTime.UtcNow - _lastFallbackSwitch.Value).TotalSeconds > _settings.Fallback.CoolDownSeconds;
}
```

### 实施要点

| 项目 | 说明 |
|------|------|
| 影响文件 | `Demo/BrowserDemo/Models/AiSettings.cs`、`Demo/BrowserDemo/Services/AiClient.cs` |
| 风险 | 切换 provider 可能改变可用模型和功能（如 vision 支持） |
| 缓解 | 冷却时间防止频繁切换；切换时记录日志 |

---

## 修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|---------|--------|
| `Demo/BrowserDemo/Services/ContextBuilder.cs` | XML 标签分段 + 结构化思考字段 prompt | **P0** |
| `Demo/BrowserDemo/Services/ContextBuilder.cs` | 结构化输出 schema 提示 | P1 |
| `Demo/BrowserDemo/Services/AiClient.cs` | LLM 摘要压缩 | P1 |
| `Demo/BrowserDemo/Services/AiClient.cs` | Fallback provider 切换 | P2 |
| `Demo/BrowserDemo/Models/AiSettings.cs` | FallbackSettings + BrowserVisionSettings | P1 |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 截图频率限制 | P2 |

## 预估工作量

- 结构化思考框架 + XML 分段：0.5 天
- LLM 摘要压缩：1 天
- 截图视觉辅助：0.5 天
- 结构化输出 Schema：0.5 天
- Fallback Provider：0.5 天

## 验收标准

1. 系统提示词使用 XML 标签分隔不同信息块
2. AI 回复中包含 `[上步评估]`、`[进度记忆]`、`[下步目标]` 字段
3. 压缩时可选择使用 LLM 摘要而非简单截断（可配置开关）
4. 多模态模型可选项启用截图辅助
5. 主 provider 连续失败 3 次后自动切换到备用 provider
