using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

/// <summary>AI 客户端实现——支持 OpenAI 兼容格式（15+ 服务商）+ Anthropic 原生格式</summary>
public class AiClient : IAiClient, IDisposable
{
    private const int ContextCompressionTriggerBytes = 150_000;
    private const int ContextCompressionTargetBytes = 100_000;

    private readonly HttpClient _http;

    /// <summary>工具循环中连续返回空/少数据的轮数（用于自诊断）</summary>
    private int _consecutiveStaleResults = 0;

    /// <summary>上次探测的 URL，用于检测重复探测</summary>
    private string? _lastProbedUrl = null;

    /// <summary>探测结果缓存，换 URL 时清除</summary>
    private string? _lastProbeResult = null;

    /// <summary>记录一次探测结果，返回是否是对同一 URL 的重复探测</summary>
    public bool ReportProbe(string url, string result)
    {
        if (_lastProbedUrl == url && !string.IsNullOrEmpty(url))
        {
            Logger.Debug($"重复探测检测: {url}");
            return true; // 重复探测
        }
        _lastProbedUrl = url;
        _lastProbeResult = result;
        return false;
    }

    /// <summary>AI 可通过 set_task_iterations 工具动态调整迭代次数上限（每次调用都生效）</summary>
    private int? _maxIterationsOverride = null;

    /// <summary>动态调整迭代次数上限。返回 true 表示生效，false 表示数值超出范围（1-80）。每次 AI 认为需要调整时均可调用</summary>
    public bool TrySetMaxIterations(int count)
    {
        if (count < 1 || count > 80) return false;
        _maxIterationsOverride = count;
        Logger.Info($"AI 动态调整最大迭代次数: {count}");
        return true;
    }

    public AiSettings Settings { get; set; } = new();

    /// <summary>上下文构建器——注入系统提示词、工具定义和动态上下文</summary>
    public ContextBuilder ContextBuilder { get; set; } = new();

    public AiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        LoadSettings();
        Logger.Debug($"AiClient: ContextBuilder 已初始化 (IsEnabled={ContextBuilder.IsEnabled})");
    }

    public AiClient(AiSettings settings) : this()
    {
        Settings = settings;
    }

    // ========== 非流式请求 ==========

    public async Task<string> SendMessageAsync(
        IEnumerable<ChatMessage> messages, CancellationToken ct = default)
    {
        using var _ = Logger.Trace("AiClient::SendMessageAsync");
        var reply = "";
        await foreach (var chunk in StreamMessageAsync(messages, ct))
            reply += chunk;
        Logger.Debug($"SendMessageAsync 合并完成，共 {reply.Length} 字符");
        return reply;
    }

    // ========== 流式请求 ==========

    public async IAsyncEnumerable<string> StreamMessageAsync(
        IEnumerable<ChatMessage> messages, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Logger.Debug("AiClient::StreamMessageAsync called");

        if (!Settings.HasKey)
        {
            Logger.Warning("API Key 未配置");
            yield return "⚠️ 请先在 AI 设置中配置 API Key。";
            yield break;
        }

        var msgs = messages.ToList();
        var provider = ProviderManager.GetProvider(Settings.ProviderKey);

        Logger.Info($"AI API 请求: provider={Settings.ProviderKey}, model={Settings.Model}, msgs={msgs.Count}");
        Logger.Debug($"端点: {Settings.ResolvedEndpoint}");

        ConfigureHeaders();

        using var request = IsAnthropicProvider()
            ? BuildAnthropicRequest(msgs)
            : BuildOpenAIRequest(msgs, provider);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false); // 后台线程处理 HTTP 流

        Logger.Debug($"HTTP 响应: {(int)response.StatusCode} {response.ReasonPhrase}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Logger.Error($"API 请求失败: {response.StatusCode} — {TruncateError(error)}");
            yield return $"⚠️ API 请求失败 ({response.StatusCode}): {TruncateError(error)}";
            yield break;
        }

        var lineCount = 0;
        await foreach (var chunk in ParseStreamAsync(response, ct).ConfigureAwait(false))
        {
            lineCount++;
            yield return chunk;
        }
        Logger.Debug($"SSE 流解析完成，共 {lineCount} 个 data 行");
    }

    // ========== 连接测试 ==========

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        using var _ = Logger.Trace("AiClient::TestConnectionAsync");
        Logger.Info($"测试连接: provider={Settings.ProviderKey}, endpoint={Settings.ResolvedEndpoint}");

        if (!Settings.HasKey)
        {
            Logger.Warning("API Key 为空");
            return false;
        }
        try
        {
            ConfigureHeaders();
            using var request = IsAnthropicProvider()
                ? BuildAnthropicTestRequest()
                : BuildOpenAITestRequest();
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var ok = response.IsSuccessStatusCode;
            Logger.Info($"连接测试: HTTP {(int)response.StatusCode} → {(ok ? "成功" : "失败")}");
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Exception("连接测试异常", ex);
            return false;
        }
    }

    // ========== 判断提供商类型 ==========

    private bool IsAnthropicProvider() => Settings.ProviderKey == "anthropic";

    private bool IsApiKeyHeaderProvider()
    {
        var info = ProviderManager.GetProvider(Settings.ProviderKey);
        return info?.AuthType == "x-api-key";
    }

    private void ConfigureHeaders()
    {
        _http.DefaultRequestHeaders.Clear();

        if (IsApiKeyHeaderProvider())
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", Settings.ApiKey);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            Logger.Debug("认证方式: x-api-key (Anthropic)");
        }
        else
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
            Logger.Debug("认证方式: Bearer Token");
        }

        if (Settings.ProviderKey == "openrouter")
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "HTTP-Referer", "https://github.com/SmartAI-Browser");
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "X-Title", "SmartAI Browser Demo");
            Logger.Debug("OpenRouter 额外头已添加");
        }
    }

    // ========== OpenAI 兼容格式请求 ==========

    private HttpRequestMessage BuildOpenAIRequest(List<ChatMessage> messages, ProviderInfo? provider)
    {
        Logger.Debug($"构造 OpenAI 格式请求: model={Settings.Model}");

        var body = new Dictionary<string, object?>
        {
            ["model"] = Settings.Model,
            ["stream"] = true,
        };

        // ── 上下文注入：系统提示词 + 工具定义 ──────────────────────
        var msgList = new List<object?>();

        // 1. ContextBuilder 的系统提示词（身份、行为准则、动态上下文）
        var sysPrompt = ContextBuilder.BuildSystemPrompt();
        if (!string.IsNullOrWhiteSpace(sysPrompt))
        {
            Logger.Debug($"OpenAI: 注入系统提示词 ({sysPrompt.Length} 字符)");
            msgList.Add(new { role = "system", content = sysPrompt });
        }

        // 2. 用户对话消息（支持 Tool 角色和 Tool Calls）
        foreach (var m in messages)
        {
            if (m.Role == MessageRole.Tool)
            {
                // Tool 角色消息：需要 tool_call_id
                msgList.Add(new { role = "tool", tool_call_id = m.ToolCallId, content = m.Content });
            }
            else if (m.Role == MessageRole.Assistant && m.HasToolCalls)
            {
                // Assistant 消息中包含 tool_calls 数组
                var tcList = m.ToolCalls!.Select(tc => new
                {
                    id = tc.Id,
                    type = tc.Type,
                    function = new { name = tc.FunctionName, arguments = tc.FunctionArguments }
                }).ToList();

                msgList.Add(new { role = "assistant", content = m.Content ?? "", tool_calls = tcList });
            }
            else
            {
                msgList.Add(new { role = m.ApiRole, content = m.Content });
            }
        }

        body["messages"] = msgList;

        // 3. 工具定义（如果已注册）
        if (ContextBuilder is { IsEnabled: true, RegisteredTools.Count: > 0 })
        {
            var toolSchemas = ContextBuilder.GetToolSchemasForOpenAI();
            Logger.Debug($"OpenAI: 注入 {toolSchemas.Count} 个工具定义");
            body["tools"] = toolSchemas;
        }
        // ─────────────────────────────────────────────────────

        if (provider?.Key is "google" or "deepseek")
        {
            body["max_tokens"] = 4096;
            Logger.Debug("max_tokens=4096 已添加 (Google/DeepSeek 需要)");
        }

        var json = JsonSerializer.Serialize(body, JsonOptions);
        Logger.Debug($"请求体大小: {Encoding.UTF8.GetByteCount(json)} 字节");
        return new HttpRequestMessage(HttpMethod.Post, Settings.ResolvedEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private HttpRequestMessage BuildOpenAITestRequest()
    {
        Logger.Debug("构造 OpenAI 测试请求");
        var body = new
        {
            model = Settings.Model,
            max_tokens = 2,
            messages = new[] { new { role = "user", content = "hi" } }
        };
        var json = JsonSerializer.Serialize(body, JsonOptions);
        return new HttpRequestMessage(HttpMethod.Post, Settings.ResolvedEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // ========== Anthropic 原生格式请求 ==========

    private HttpRequestMessage BuildAnthropicRequest(List<ChatMessage> messages)
    {
        Logger.Debug("构造 Anthropic 原生格式请求");

        var body = new Dictionary<string, object?>
        {
            ["model"] = Settings.Model,
            ["max_tokens"] = 4096,
            ["stream"] = true,
        };

        // ── 上下文注入：系统提示词 + 工具定义 ──────────────────────

        // 1. 构建 system 参数：ContextBuilder 系统提示词 + 用户对话中的系统消息
        var systemParts = new List<string>();

        var sysPrompt = ContextBuilder.BuildSystemPrompt();
        if (!string.IsNullOrWhiteSpace(sysPrompt))
            systemParts.Add(sysPrompt);

        // 收集用户对话中的系统消息（如 "当前页面：xxx"）
        var userSystemMsgs = messages
            .Where(m => m.Role == MessageRole.System)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
        systemParts.AddRange(userSystemMsgs);

        if (systemParts.Count > 0)
        {
            var combined = string.Join("\n\n", systemParts).Trim();
            Logger.Debug($"Anthropic: 注入系统提示词 ({combined.Length} 字符, {systemParts.Count} 个片段)");
            body["system"] = combined;
        }

        // 2. User/Assistant 消息（Anthropic 不支持 system role 在 messages 数组中）
        var normal = new List<object?>();
        foreach (var m in messages.Where(m => m.Role != MessageRole.System))
        {
            if (m.Role == MessageRole.Assistant && m.HasToolCalls)
            {
                normal.Add(new { role = "assistant", content = m.Content ?? "" });
            }
            else if (m.Role == MessageRole.Tool)
            {
                normal.Add(new { role = "user", content = $"工具 {m.ToolName ?? m.ToolCallId} 返回：\n{m.Content}" });
            }
            else
            {
                normal.Add(new { role = m.ApiRole, content = m.Content });
            }
        }
        body["messages"] = normal;

        // 3. 工具定义（如果已注册）
        if (ContextBuilder is { IsEnabled: true, RegisteredTools.Count: > 0 })
        {
            var toolSchemas = ContextBuilder.GetToolSchemasForAnthropic();
            Logger.Debug($"Anthropic: 注入 {toolSchemas.Count} 个工具定义");
            body["tools"] = toolSchemas;
        }
        // ─────────────────────────────────────────────────────

        var json = JsonSerializer.Serialize(body, JsonOptions);
        Logger.Debug($"Anthropic 请求体: {Encoding.UTF8.GetByteCount(json)} 字节, system 参数已整合");

        var request = new HttpRequestMessage(HttpMethod.Post, Settings.ResolvedEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return request;
    }

    private HttpRequestMessage BuildAnthropicTestRequest()
    {
        Logger.Debug("构造 Anthropic 测试请求");
        var body = new
        {
            model = Settings.Model,
            max_tokens = 2,
            messages = new[] { new { role = "user", content = "hi" } }
        };
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var request = new HttpRequestMessage(HttpMethod.Post, Settings.ResolvedEndpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        return request;
    }

    // ========== 流式响应解析 ==========

    private async IAsyncEnumerable<string> ParseStreamAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var totalChars = 0;
        var parseErrors = 0;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? content = IsAnthropicProvider()
                ? ParseAnthropicLine(line)
                : ParseOpenAILine(line);

            if (content != null)
            {
                totalChars += content.Length;
                yield return content;
            }
            else
            {
                parseErrors++;
            }
        }

        if (parseErrors > 0)
            Logger.Debug($"SSE 解析: 成功 {totalChars} 字符, 无法解析 {parseErrors} 行");
    }

    private static string? ParseOpenAILine(string line)
    {
        if (!line.StartsWith("data: ")) return null;
        var json = line[6..];
        if (json == "[DONE]") return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var c))
                    return c.GetString();
            }

            // DeepSeek reasoning_content
            if (root.TryGetProperty("choices", out var ch2) && ch2.GetArrayLength() > 0)
            {
                var delta2 = ch2[0].GetProperty("delta");
                if (delta2.TryGetProperty("reasoning_content", out var rc))
                    return rc.GetString();
            }
        }
        catch (JsonException)
        {
            // 跳过无法解析的行 — 不污染日志
        }
        return null;
    }

    private static string? ParseAnthropicLine(string line)
    {
        if (!line.StartsWith("data: ")) return null;
        var json = line[6..];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var type))
            {
                var typeStr = type.GetString();
                if (typeStr == "content_block_delta" &&
                    root.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("text", out var text))
                    return text.GetString();

                if (typeStr == "content_block_start" &&
                    root.TryGetProperty("content_block", out var cb) &&
                    cb.TryGetProperty("text", out var ct))
                    return ct.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    // ====================================================================
    // 工具调用（Tool Call / Function Calling）支持
    // ====================================================================

    /// <summary>
    /// 执行带工具调用的完整对话循环。
    /// 流式返回 AI 回复文本，工具调用在内部透明处理（检测 → 执行 → 回传 → 继续）。
    /// 对调用者表现为一个普通的流式文本接口。
    ///
    /// 不再有硬性迭代上限——只要 AI 在产出有效工作，就可以持续运行。
    /// 唯一的强制终止条件是：连续 3 轮返回空/无效数据（卡死检测）触发强制退出。
    /// </summary>
    /// <param name="messages">对话消息列表（会在循环中追加 assistant 和 tool 消息）</param>
    /// <param name="executeTool">工具执行回调：参数为 (toolName, argumentsDict)，返回工具执行结果字符串</param>
    /// <param name="maxIterations">软性提醒阈值（不是硬上限，仅用于在接近时提醒 AI 加快进度）</param>
    /// <param name="ct">取消令牌</param>
    public async IAsyncEnumerable<string> ExecuteConversationAsync(
        List<ChatMessage> messages,
        Func<string, Dictionary<string, object?>?, Task<string>> executeTool,
        int maxIterations = 100,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lastCompressionIteration = 0;

        for (int iteration = 0; ; iteration++)
        {
            Logger.Info($"工具循环 迭代 #{iteration + 1}, 消息数={messages.Count}");

            // ★★★ 上下文压缩：超过 150KB 时立即压缩到 100KB 以下，另保留 20 轮兜底压缩 ★★★
            var contextBytes = EstimateConversationBytes(messages);
            var shouldCompressBySize = contextBytes > ContextCompressionTriggerBytes;
            var shouldCompressByRounds = iteration - lastCompressionIteration >= 20 && messages.Count > 40;
            if (shouldCompressBySize || shouldCompressByRounds)
            {
                var beforeCount = messages.Count;
                var beforeBytes = contextBytes;
                CompressHistory(messages, shouldCompressBySize ? ContextCompressionTargetBytes : null);
                var afterBytes = EstimateConversationBytes(messages);
                if (messages.Count < beforeCount || afterBytes < beforeBytes)
                {
                    lastCompressionIteration = iteration;
                    Logger.Info($"上下文压缩完成: {beforeCount}条/{beforeBytes}字节 → {messages.Count}条/{afterBytes}字节");
                }
                else
                {
                    Logger.Debug($"上下文压缩跳过（无完整循环可压缩）");
                }
            }

            // ★★★ UI 防卡死：每 3 轮让出 UI 线程处理消息 ★★★
            if (iteration > 0 && iteration % 3 == 0)
                await Task.Yield();

            // ---- 收集本轮流式事件 ----
            var textChunks = new List<string>();
            var toolCallAcc = new Dictionary<int, ToolCallData>();
            string? finishReason = null;

            // 临时变量：记录当前正在处理的 tool_call index（用于 Anthropic 的 content_block 增量）
            int? pendingToolIndex = null;

            await foreach (var evt in StreamRichEventsAsync(messages, ct).ConfigureAwait(false))
            {
                switch (evt.Type)
                {
                    case "content":
                        textChunks.Add(evt.Text ?? "");
                        yield return evt.Text ?? "";
                        break;

                    case "tool_call_start":
                        var idx = evt.ToolIndex ?? (pendingToolIndex.HasValue ? pendingToolIndex.Value + 1 : 0);
                        if (!pendingToolIndex.HasValue) pendingToolIndex = 0;
                        pendingToolIndex = idx;
                        toolCallAcc[idx] = new ToolCallData
                        {
                            Id = evt.ToolId ?? "",
                            Type = evt.ToolType ?? "function",
                            FunctionName = evt.ToolName ?? "",
                            FunctionArguments = evt.ToolArgs ?? ""
                        };
                        break;

                    case "tool_call_delta":
                        var deltaIdx = evt.ToolIndex ?? pendingToolIndex ?? 0;
                        pendingToolIndex = deltaIdx;
                        if (toolCallAcc.TryGetValue(deltaIdx, out var existing))
                        {
                            existing.FunctionArguments += evt.ToolArgs ?? "";
                        }
                        else
                        {
                            // Anthropic 格式可能在 start 之前就来 delta
                            toolCallAcc[deltaIdx] = new ToolCallData
                            {
                                FunctionArguments = evt.ToolArgs ?? ""
                            };
                        }
                        break;

                    case "finish":
                        finishReason = evt.FinishReason;
                        break;

                    case "error":
                        yield return $"⚠️ {evt.Text}";
                        yield break;
                }
            }

            var fullText = string.Concat(textChunks);

            // ---- 判断本轮结果 ----
            var isToolCall = finishReason == "tool_calls" || toolCallAcc.Count > 0;

            if (!isToolCall)
            {
                // 纯文本回复 结束
                Logger.Info($"工具循环结束: AI 返回文本回复 ({fullText.Length} 字符)");
                yield break;
            }
            // ---- 处理工具调用 ----
            Logger.Info($"工具循环: AI 请求了 {toolCallAcc.Count} 个工具调用");

            // 1. 添加 assistant 消息（带 tool_calls）到历史
            var assistantMsg = new ChatMessage
            {
                Role = MessageRole.Assistant,
                Content = fullText,
                ToolCalls = toolCallAcc.Values.ToList(),
                Timestamp = DateTime.Now
            };
            messages.Add(assistantMsg);

            // 2. 依次执行每个工具
            Logger.Debug($"[Function] AiClient::ExecuteConversationAsync — 开始执行 {toolCallAcc.Count} 个工具");
            var injectedRepeatWarnings = new List<string>(); // 本轮已注入的警告
            foreach (var tc in toolCallAcc.Values.OrderBy(x => x.Id))
            {
                var args = tc.ParseArguments();
                var argsPreview = args != null
                    ? string.Join(", ", args.Where(a => a.Value != null).Select(a => $"{a.Key}={a.Value}"))
                    : "(无参数)";
                Logger.Info($"执行工具: {tc.FunctionName}({argsPreview})");

                var toolResult = await ExecuteToolWithErrorHandlingAsync(executeTool, tc.FunctionName, args);

                // ★★★ 检测 ask_user 暂停信号：直接 yield 给调用方并停止循环 ★★★
                if (toolResult != null && toolResult.StartsWith("__ASK_USER_PAUSED__:"))
                {
                    yield return toolResult; // 包含完整 JSON 的暂停信号
                    Logger.Info($"工具循环因 ask_user 暂停");
                    yield break; // 结束循环，等待调用方恢复
                }

                // ★★★ 子任务边界：执行子任务前强制压缩此前上下文 ★★★
                if (toolResult != null && toolResult.StartsWith("__SUBTASK_CONTEXT_COMPRESSED__:"))
                {
                    var beforeCount = messages.Count;
                    var beforeBytes = EstimateConversationBytes(messages);
                    CompressHistory(messages, ContextCompressionTargetBytes);
                    var afterBytes = EstimateConversationBytes(messages);
                    Logger.Info($"子任务开始前上下文压缩: {beforeCount}条/{beforeBytes}字节 → {messages.Count}条/{afterBytes}字节");
                    toolResult = toolResult["__SUBTASK_CONTEXT_COMPRESSED__:".Length..];
                }

                if (toolResult != null)
                {
                    var snippet = toolResult.Length > 200 ? toolResult[..200] + "…" : toolResult;
                    Logger.Debug($"工具 {tc.FunctionName} 执行结果: {snippet}");

                    // ★★★ 连续空结果检测：仅内容提取工具返回短内容 → 累加 ★★★
                    // skill_js 排除在外：JS 查询页面元素返回空是正常现象（元素不存在/跨域限制），不是卡死
                    var isProbeTool = tc.FunctionName is "skill_extract" or "skill_query";
                    var isShortResult = toolResult.Length < 50
                        || (toolResult.Contains("56 字符") && toolResult.Contains("导航"))
                        || (toolResult.Contains("73 字符") && toolResult.Contains("导航"));
                    if (isProbeTool && isShortResult)
                    {
                        _consecutiveStaleResults++;
                        Logger.Debug($"连续空结果计数器: {_consecutiveStaleResults} (工具={tc.FunctionName}, 长度={toolResult.Length})");
                    }
                    else
                    {
                        if (_consecutiveStaleResults > 0)
                            Logger.Debug($"连续空结果已重置（工具={tc.FunctionName} 返回了实质内容）");
                        _consecutiveStaleResults = 0;
                    }

                    // 将工具执行结果添加到消息历史（供 AI 下一步推理）
                    messages.Add(new ChatMessage
                    {
                        Role = MessageRole.Tool,
                        ToolCallId = tc.Id,
                        ToolName = tc.FunctionName,
                        Content = toolResult,
                        Timestamp = DateTime.Now
                    });

                    // 重复探测检测：同工具返回同内容时提醒 AI
                    if (tc.FunctionName == "skill_extract" && toolResult.Length > 20)
                    {
                        var sig = toolResult.Length >= 100 ? toolResult[..100] : toolResult;
                        if (ReportProbe(sig, sig))
                        {
                            var rw = "已经提取过当前页面的内容了，结果和上次几乎一样。请直接使用已有的数据决定下一步，不要重复提取。";
                            if (!injectedRepeatWarnings.Contains(rw))
                            {
                                injectedRepeatWarnings.Add(rw);
                                messages.Add(new ChatMessage
                                {
                                    Role = MessageRole.System,
                                    Content = rw,
                                    Timestamp = DateTime.Now
                                });
                                Logger.Info("重复探测提醒已注入");
                            }
                        }
                    }
                }
            }
            Logger.Debug($"[Function] AiClient::ExecuteConversationAsync — 工具执行完毕");

            // ★★★ 应用 AI 通过 set_task_iterations 设置的软性提醒阈值 ★★★
            if (_maxIterationsOverride.HasValue)
            {
                maxIterations = Math.Max(_maxIterationsOverride.Value, iteration + 1);
                _maxIterationsOverride = null;
                Logger.Info($"提醒阈值已由 AI 调整为 {maxIterations}");
            }

            // ★★★ 卡死检测：连续 3 轮返回空/无效数据 → 强制终止 ★★★
            if (_consecutiveStaleResults >= 3)
            {
                Logger.Warning($"卡死检测触发: 连续 {_consecutiveStaleResults} 轮空结果 — 强制终止工具循环");
                yield return $"\n\n⚠️ 已连续 {_consecutiveStaleResults} 轮工具调用返回空数据，可能遇到了无法解决的问题。任务已中止。请考虑换一种方式或手动操作。";
                yield break;
            }

            // ★ 软性提醒（警告 AI 注意效率，但不强制停止）
            var remaining = maxIterations - iteration - 1;
            if (remaining <= 5 && remaining > 0)
            {
                var warnMsg = remaining <= 2
                    ? $"⚠️ 前方预警：工具调用已达 {iteration + 1} 轮，请考虑是否足够。尽快收集并返回答案。"
                    : $"💡 效率提示：当前已进行 {iteration + 1} 轮工具调用，请确认进度合理、没有卡住。";
                messages.Add(new ChatMessage
                {
                    Role = MessageRole.System,
                    Content = warnMsg,
                    Timestamp = DateTime.Now
                });
                Logger.Info($"效率提示: 已进行 {iteration + 1} 轮 (提醒阈值 {maxIterations})");
            }

            // 继续下一轮迭代（AI 会基于工具返回结果继续推理）
        }
    }

    /// <summary>流式获取富事件（文本 + 工具调用 + 结束标志）</summary>
    private async IAsyncEnumerable<AiStreamEvent> StreamRichEventsAsync(
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Settings.HasKey)
        {
            yield return new AiStreamEvent { Type = "error", Text = "请先在 AI 设置中配置 API Key。" };
            yield break;
        }

        var provider = ProviderManager.GetProvider(Settings.ProviderKey);
        Logger.Debug($"StreamRichEvents: provider={Settings.ProviderKey}, model={Settings.Model}, msgs={messages.Count}");

        ConfigureHeaders();

        using var request = IsAnthropicProvider()
            ? BuildAnthropicRequest(messages)
            : BuildOpenAIRequest(messages, provider);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            Logger.Error($"API 请求失败: {response.StatusCode} — {TruncateError(error)}");
            yield return new AiStreamEvent { Type = "error", Text = $"API 请求失败 ({response.StatusCode}): {TruncateError(error)}" };
            yield break;
        }

        await foreach (var evt in ParseStreamRichAsync(response, ct).ConfigureAwait(false))
            yield return evt;
    }

    /// <summary>解析流式响应为富事件序列</summary>
    private async IAsyncEnumerable<AiStreamEvent> ParseStreamRichAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            // ★ 每行独立超时 + 可取消：创建链接到 ct 的新 CTS，30 秒超时
            //   用完即释放，避免旧代码中 readTimeoutCts 复用导致的"一次超时，行行超时"的级联问题
            AiStreamEvent? parsedEvent = null;
            bool isTimeout = false;
            using var lineTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lineTimeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var line = await reader.ReadLineAsync(lineTimeoutCts.Token)
                    .ConfigureAwait(false);

                if (line is null) break; // 流正常结束

                if (!string.IsNullOrWhiteSpace(line))
                {
                    parsedEvent = IsAnthropicProvider()
                        ? ParseAnthropicLineRich(line)
                        : ParseOpenAILineRich(line);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 30 秒行读取超时（非用户取消）
                isTimeout = true;
            }
            catch (OperationCanceledException)
            {
                // 用户取消 → 静默结束
                break;
            }
            catch (IOException ioEx)
            {
                Logger.Warning($"SSE 流读取错误 (可能服务器关闭了连接): {ioEx.Message}");
                break;
            }

            // 超时处理（yield 必须在 try-catch 之外）
            if (isTimeout)
            {
                Logger.Warning("SSE 读取超时 (30s)，服务器可能已停止响应");
                yield return new AiStreamEvent { Type = "error", Text = "服务器响应超时，请重试" };
                yield break;
            }

            if (parsedEvent != null)
                yield return parsedEvent;
        }
    }

    /// <summary>解析 OpenAI 格式单行 SSE，返回事件对象</summary>
    private static AiStreamEvent? ParseOpenAILineRich(string line)
    {
        if (!line.StartsWith("data: ")) return null;
        var json = line[6..];
        if (json == "[DONE]") return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];

            // finish_reason
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null)
                return new AiStreamEvent { Type = "finish", FinishReason = fr.GetString() };

            if (!choice.TryGetProperty("delta", out var delta))
                return null;

            // text content
            if (delta.TryGetProperty("content", out var content))
                return new AiStreamEvent { Type = "content", Text = content.GetString() ?? "" };

            // DeepSeek reasoning_content
            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind != JsonValueKind.Null)
                return new AiStreamEvent { Type = "content", Text = rc.GetString() ?? "" };

            // tool_calls
            if (delta.TryGetProperty("tool_calls", out var tcs))
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    var index = tc.GetProperty("index").GetInt32();

                    if (tc.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } tid)
                    {
                        // First chunk of this tool call (has id, type, name)
                        var evt = new AiStreamEvent
                        {
                            Type = "tool_call_start",
                            ToolIndex = index,
                            ToolId = tid,
                            ToolType = "function"
                        };
                        if (tc.TryGetProperty("function", out var func))
                        {
                            if (func.TryGetProperty("name", out var tn))
                                evt.ToolName = tn.GetString() ?? "";
                            if (func.TryGetProperty("arguments", out var targs))
                                evt.ToolArgs = targs.GetString() ?? "";
                        }
                        return evt;
                    }
                    else
                    {
                        // Continuation chunk (arguments only)
                        var evt = new AiStreamEvent
                        {
                            Type = "tool_call_delta",
                            ToolIndex = index
                        };
                        if (tc.TryGetProperty("function", out var func) &&
                            func.TryGetProperty("arguments", out var targs))
                        {
                            evt.ToolArgs = targs.GetString() ?? "";
                        }
                        return evt;
                    }
                }
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>解析 Anthropic 格式单行 SSE，返回事件对象</summary>
    private static AiStreamEvent? ParseAnthropicLineRich(string line)
    {
        if (!line.StartsWith("data: ")) return null;
        var json = line[6..];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var type)) return null;
            var typeStr = type.GetString();

            switch (typeStr)
            {
                case "content_block_delta":
                    if (root.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("text", out var text))
                            return new AiStreamEvent { Type = "content", Text = text.GetString() };
                        if (delta.TryGetProperty("type", out var dt) && dt.GetString() == "input_json_delta" &&
                            delta.TryGetProperty("partial_json", out var pj))
                            return new AiStreamEvent { Type = "tool_call_delta", ToolArgs = pj.GetString() };
                    }
                    break;

                case "content_block_start":
                    if (root.TryGetProperty("content_block", out var cb))
                    {
                        if (cb.TryGetProperty("text", out var ct))
                            return new AiStreamEvent { Type = "content", Text = ct.GetString() };
                        if (cb.TryGetProperty("type", out var cbt) && cbt.GetString() == "tool_use")
                            return new AiStreamEvent
                            {
                                Type = "tool_call_start",
                                ToolId = cb.TryGetProperty("id", out var tid) ? tid.GetString() : "",
                                ToolName = cb.TryGetProperty("name", out var tn) ? tn.GetString() : "",
                                ToolType = "function"
                            };
                    }
                    break;

                case "message_delta":
                    if (root.TryGetProperty("delta", out var delta2) &&
                        delta2.TryGetProperty("stop_reason", out var sr))
                        return new AiStreamEvent { Type = "finish", FinishReason = sr.GetString() };
                    break;
            }
        }
        catch (JsonException) { }
        return null;
    }

    // ========== 辅助方法 ==========

    /// <summary>执行工具调用并处理异常（由于不包含 yield return，可在 try-catch 中使用）</summary>
    private static async Task<string?> ExecuteToolWithErrorHandlingAsync(
        Func<string, Dictionary<string, object?>?, Task<string>> executeTool,
        string toolName,
        Dictionary<string, object?>? args)
    {
        try
        {
            return await executeTool(toolName, args);
        }
        catch (Exception ex)
        {
            Logger.Exception($"工具 {toolName} 执行异常", ex);
            return $"错误: {ex.Message}";
        }
    }

    private static string TruncateError(string error, int maxLen = 300)
        => error.Length <= maxLen ? error : error[..maxLen] + "…";

    // ========== 配置持久化 ==========

    public void SaveSettings()
    {
        Logger.Info($"保存 AI 设置到文件: {AiSettings.ConfigPath}");
        var store = AiSettingsStore.Load();
        store.Upsert(Settings);
        store.Save();
        Logger.Debug("AI 设置已持久化");
    }

    public void LoadSettings()
    {
        try
        {
            var store = AiSettingsStore.Load();
            Settings = store.ResolveActive();
            Logger.Debug($"已加载配置: provider={Settings.ProviderKey}, model={Settings.Model}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"加载配置文件失败: {ex.Message}");
            Settings = new AiSettings();
        }
    }

    /// <summary>
    /// 压缩对话历史：用摘要替换旧消息以控制上下文大小。
    ///
    /// 核心约束：API 要求每条 role=tool 的消息必须对应一条前面的
    /// assistant(tool_calls) 消息。删除时不能拆散 tool→assistant 配对。
    ///
    /// 策略：从后往前扫描，找到最近的 Assistant 消息边界作为保留起点。
    /// 保留最新消息 + 确保从 Assistant 边界开始。
    /// 之前的旧消息全部替换为一条 system 摘要。
    /// </summary>
    private static void CompressHistory(List<ChatMessage> messages, int? targetBytes = null)
    {
        int firstUserIdx = messages.FindIndex(m => m.Role == MessageRole.User);
        if (firstUserIdx < 0 || messages.Count - firstUserIdx < 10) return;

        var keepCount = Math.Min(20, messages.Count - firstUserIdx - 1);
        var safeBoundary = FindCompressionBoundary(messages, firstUserIdx, keepCount);
        if (safeBoundary <= firstUserIdx + 2) return;

        ApplyCompression(messages, firstUserIdx, safeBoundary);

        if (!targetBytes.HasValue) return;

        var guard = 0;
        while (EstimateConversationBytes(messages) > targetBytes.Value && guard++ < 8)
        {
            keepCount = Math.Max(4, keepCount / 2);
            safeBoundary = FindCompressionBoundary(messages, firstUserIdx, keepCount);
            if (safeBoundary <= firstUserIdx + 2)
            {
                TrimCompressionSummary(messages, firstUserIdx, targetBytes.Value);
                break;
            }

            ApplyCompression(messages, firstUserIdx, safeBoundary);
        }
    }

    private static int FindCompressionBoundary(List<ChatMessage> messages, int firstUserIdx, int keepCount)
    {
        keepCount = Math.Clamp(keepCount, 1, Math.Max(1, messages.Count - firstUserIdx - 1));
        int keepStart = messages.Count - keepCount;
        int safeBoundary = firstUserIdx + 1;

        // 从 keepStart 向前找最近的 Assistant 消息作为安全边界
        for (int i = keepStart; i > firstUserIdx; i--)
        {
            if (messages[i].Role == MessageRole.Assistant)
            {
                safeBoundary = i;
                break;
            }
        }

        return safeBoundary;
    }

    private static void ApplyCompression(List<ChatMessage> messages, int firstUserIdx, int safeBoundary)
    {
        var summaryParts = new List<string>();
        for (int i = firstUserIdx + 1; i < safeBoundary; i++)
        {
            var msg = messages[i];
            if (msg is { Role: MessageRole.Assistant, HasToolCalls: true })
            {
                var toolNames = msg.ToolCalls!
                    .Select(tc => tc.FunctionName)
                    .Distinct()
                    .ToList();
                summaryParts.Add($"🛠 调用: {string.Join(", ", toolNames)}");
            }
            else if (msg is { Role: MessageRole.Assistant, HasToolCalls: false } &&
                     !string.IsNullOrWhiteSpace(msg.Content))
            {
                var snippet = msg.Content.Length > 120 ? msg.Content[..120] + "…" : msg.Content;
                summaryParts.Add($"→ {snippet}");
            }
            else if (msg.Role == MessageRole.Tool &&
                     !string.IsNullOrWhiteSpace(msg.Content) &&
                     msg.Content.Length > 20)
            {
                var snippet = msg.Content.Length > 50 ? msg.Content[..50] + "…" : msg.Content;
                summaryParts.Add($"  [{msg.ToolName}] {snippet}");
            }
        }

        if (summaryParts.Count == 0) return;

        var summary = "📋 **已完成操作的摘要（已压缩历史，保留最新上下文）：**\n\n"
                    + string.Join("\n", summaryParts.TakeLast(120))
                    + "\n\n---\n📌 **任务仍在进行中。** 继续执行——以上是已完成步骤，接下来根据当前页面状态继续。";

        int removeStart = firstUserIdx + 1;
        int removeCount = safeBoundary - removeStart;
        messages.RemoveRange(removeStart, removeCount);
        messages.Insert(removeStart, new ChatMessage
        {
            Role = MessageRole.System,
            Content = summary,
            Timestamp = DateTime.Now
        });
    }

    private static void TrimCompressionSummary(List<ChatMessage> messages, int firstUserIdx, int targetBytes)
    {
        var summaryMsg = messages.Skip(firstUserIdx + 1).FirstOrDefault(m => m.Role == MessageRole.System);
        if (summaryMsg == null) return;

        while (EstimateConversationBytes(messages) > targetBytes && summaryMsg.Content.Length > 1200)
        {
            summaryMsg.Content = summaryMsg.Content[..(summaryMsg.Content.Length / 2)]
                + "\n…（摘要继续截断以满足 100KB 上下文限制）";
        }
    }

    private static int EstimateConversationBytes(IEnumerable<ChatMessage> messages)
    {
        var total = 0;
        foreach (var msg in messages)
        {
            total += Encoding.UTF8.GetByteCount(msg.ApiRole) + Encoding.UTF8.GetByteCount(msg.Content) + 64;
            if (msg.ToolCallId != null) total += Encoding.UTF8.GetByteCount(msg.ToolCallId);
            if (msg.ToolName != null) total += Encoding.UTF8.GetByteCount(msg.ToolName);
            if (msg.ToolCalls == null) continue;
            foreach (var tc in msg.ToolCalls)
            {
                total += Encoding.UTF8.GetByteCount(tc.Id)
                    + Encoding.UTF8.GetByteCount(tc.FunctionName)
                    + Encoding.UTF8.GetByteCount(tc.FunctionArguments)
                    + 96;
            }
        }
        return total;
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public void Dispose() => _http.Dispose();
}
