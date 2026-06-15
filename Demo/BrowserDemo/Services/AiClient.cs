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
    /// <summary>上下文压缩触发阈值（字节）——超过此值自动压缩</summary>
    private const int ContextCompressionTriggerBytes = 50_000;
    /// <summary>上下文压缩目标值（字节）——压缩后维持在此值以下</summary>
    private const int ContextCompressionTargetBytes = 40_000;
    /// <summary>子任务完成时最大压缩目标值（字节）——子任务完成时压缩到此值以下</summary>
    private const int ContextCompressionMaxTargetBytes = 30_000;

    /// <summary>工具结果最大字符数 —— 超出后自动截断并替换为摘要</summary>
    private const int MaxToolResultChars = 2_000;
    /// <summary>截断后保留尾部字符数（用于看到最新输出）</summary>
    private const int TruncateTailChars = 500;

    /// <summary>速率限制重试最大次数</summary>
    private const int MaxRateLimitRetries = 3;
    /// <summary>速率限制退避基础延迟（毫秒）</summary>
    private const int RateLimitBaseDelayMs = 2000;
    /// <summary>上游/网关瞬时错误重试最大次数</summary>
    private const int MaxTransientErrorRetries = 3;
    /// <summary>上游/网关瞬时错误退避基础延迟（毫秒）</summary>
    private const int TransientErrorBaseDelayMs = 2000;

    private readonly HttpClient _http;

    /// <summary>工具循环中连续返回空/少数据的轮数（用于自诊断）</summary>
    private int _consecutiveStaleResults = 0;

    /// <summary>规划门禁连续触发次数——超过阈值则放弃强制要求，避免无限循环</summary>
    private int _consecutivePlanningGateTrips = 0;

    /// <summary>子任务仍未结束时，AI 连续输出普通文本的次数——用于防止阶段性文字被当成最终完成</summary>
    private int _consecutiveOpenSubtaskTextReplies = 0;
    /// <summary>子任务门禁连续触发上限——超过阈值则终止本轮请求</summary>
    private const int MaxSubtaskGateTrips = 5;

    /// <summary>工具循环硬上限——防止无限迭代（即使 AI 不响应提醒也强制终止）</summary>
    private const int MaxHardIterations = 80;

    /// <summary>browser_js 连续返回 null 的轮数（用于自诊断）</summary>
    private int _consecutiveJsNullResults = 0;

    /// <summary>上次探测的 URL，用于检测重复探测</summary>
    private string? _lastProbedUrl = null;

    /// <summary>探测结果缓存，换 URL 时清除</summary>
    private string? _lastProbeResult = null;

    /// <summary>AI 最近几轮纯文本回复摘要（用于复读检测）</summary>
    private readonly Queue<(string Hash, string Preview)> _recentAiTextFingerprints = new();
    /// <summary>复读检测阈值：连续 N 轮返回高度相似的文本 → 视为复读</summary>
    private const int MaxConsecutiveAiRepeats = 2;

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

    private AiSettings _settings = new();

    public AiSettings Settings
    {
        get => _settings;
        set => _settings = NormalizeSettingsProtocol(value ?? new AiSettings());
    }

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

        var requestFactory = () => IsAnthropicProvider()
            ? BuildAnthropicRequest(msgs)
            : BuildOpenAIRequest(msgs, provider);
        using var request = requestFactory();

        // ★ 独立的 HTTP 请求超时（90s），与用户取消令牌分离
        //   避免 HttpClient.Timeout（120s）抛出 OperationCanceledException 后被误判为"用户取消"
        using var httpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        httpCts.CancelAfter(TimeSpan.FromSeconds(90));

        bool isHttpTimeout = false;
        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, httpCts.Token)
                .ConfigureAwait(false); // 后台线程处理 HTTP 流
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 90s HTTP 超时（非用户主动取消）
            isHttpTimeout = true;
        }

        if (isHttpTimeout)
        {
            Logger.Warning("HTTP 请求超时（90s）：服务器未在时限内响应");
            yield return "⚠️ 服务器响应超时，请重试";
            yield break;
        }

        Logger.Debug($"HTTP 响应: {(int)response!.StatusCode} {response.ReasonPhrase}");

        if (!response!.IsSuccessStatusCode)
        {
            var retryResponse = await RetryTransientFailureAsync(response, requestFactory, ct).ConfigureAwait(false);
            if (retryResponse != null)
            {
                response = retryResponse;
            }
            else
            {
                var error = await response!.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                Logger.Error($"API 请求失败: {response.StatusCode} — {TruncateError(error)}");
                yield return $"⚠️ API 请求失败 ({response!.StatusCode}): {TruncateError(error)}";
                yield break;
            }
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
            using var httpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            httpCts.CancelAfter(TimeSpan.FromSeconds(90));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, httpCts.Token);
            var ok = response.IsSuccessStatusCode;
            Logger.Info($"连接测试: HTTP {(int)response.StatusCode} → {(ok ? "成功" : "失败")}");
            if (!ok)
            {
                var error = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.Warning($"连接测试响应: {TruncateError(error)}");
            }
            return ok;
        }
        catch (Exception ex)
        {
            Logger.Exception("连接测试异常", ex);
            return false;
        }
    }

    // ========== 判断提供商类型 ==========

    private static AiSettings NormalizeSettingsProtocol(AiSettings settings)
    {
        NormalizeArkCodingEndpoint(settings);

        if (settings.ProviderKey == "anthropic" && IsOpenAICompatibleEndpoint(settings.ResolvedEndpoint))
        {
            Logger.Warning($"AI 设置协议不匹配: provider=anthropic 但 endpoint={settings.ResolvedEndpoint} 是 OpenAI 兼容端点，已自动改为 custom/Bearer 协议");
            settings.ProviderKey = settings.ResolvedEndpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase)
                ? "volcengine-ark"
                : "custom";
        }
        return settings;
    }

    private static void NormalizeArkCodingEndpoint(AiSettings settings)
    {
        var endpoint = settings.Endpoint?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint)
            || !endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase))
            return;

        var normalized = endpoint.TrimEnd('/');
        string? fixedEndpoint = null;
        if (normalized.EndsWith("/api/coding", StringComparison.OrdinalIgnoreCase))
            fixedEndpoint = normalized + "/v3";
        else if (normalized.EndsWith("/api/coding/chat/completions", StringComparison.OrdinalIgnoreCase))
            fixedEndpoint = normalized.Replace("/api/coding/chat/completions", "/api/coding/v3/chat/completions", StringComparison.OrdinalIgnoreCase);

        if (fixedEndpoint == null || string.Equals(endpoint, fixedEndpoint, StringComparison.OrdinalIgnoreCase))
            return;

        Logger.Warning($"火山方舟 Coding Plan endpoint 自动修正: {endpoint} → {fixedEndpoint}");
        settings.Endpoint = fixedEndpoint;
    }

    private bool IsAnthropicProvider()
    {
        if (Settings.ProviderKey != "anthropic") return false;
        var endpoint = Settings.ResolvedEndpoint;
        return !IsOpenAICompatibleEndpoint(endpoint);
    }

    private bool IsApiKeyHeaderProvider() => IsAnthropicProvider();

    private static bool IsOpenAICompatibleEndpoint(string endpoint)
        => endpoint.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/compatible-mode/", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("/openai/", StringComparison.OrdinalIgnoreCase)
            || endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase);

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

    private string GetOpenAIChatCompletionsEndpoint()
    {
        var endpoint = Settings.ResolvedEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return endpoint;
        return endpoint + "/chat/completions";
    }

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

        var forcedTool = ResolveRequiredPlanningTool(messages);

        // 1. ContextBuilder 的系统提示词（身份、行为准则、动态上下文）
        var sysPrompt = ContextBuilder.BuildSystemPrompt();
        if (!string.IsNullOrWhiteSpace(sysPrompt))
        {
            Logger.Debug($"OpenAI: 注入系统提示词 ({sysPrompt.Length} 字符)");
            msgList.Add(new { role = "system", content = sysPrompt });
        }

        if (forcedTool != null && !SupportsForcedToolChoice(provider))
        {
            var planningReminder = BuildPlanningToolReminder(forcedTool);
            msgList.Add(new { role = "system", content = planningReminder });
            Logger.Debug($"OpenAI: 注入规划工具提醒 ({forcedTool})");
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
                    function = new { name = tc.FunctionName, arguments = SanitizeToolArguments(tc.FunctionArguments) }
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

            if (forcedTool != null)
            {
                if (SupportsForcedToolChoice(provider))
                {
                    body["tool_choice"] = new
                    {
                        type = "function",
                        function = new { name = forcedTool }
                    };
                    Logger.Info($"OpenAI: 强制下一步先调用规划工具 {forcedTool}");
                }
                else
                {
                    Logger.Warning($"OpenAI: 当前服务商/模型可能不支持 tool_choice 参数，省略该字段并改用系统提示要求先调用 {forcedTool}");
                }
            }
        }
        // ─────────────────────────────────────────────────────

        if (provider?.Key is "google" or "deepseek")
        {
            body["max_tokens"] = 4096;
            Logger.Debug("max_tokens=4096 已添加 (Google/DeepSeek 需要)");
        }

        var json = JsonSerializer.Serialize(body, JsonOptions);
        Logger.Debug($"请求体大小: {Encoding.UTF8.GetByteCount(json)} 字节");
        return new HttpRequestMessage(HttpMethod.Post, GetOpenAIChatCompletionsEndpoint())
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
        return new HttpRequestMessage(HttpMethod.Post, GetOpenAIChatCompletionsEndpoint())
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

        var forcedTool = ResolveRequiredPlanningTool(messages);

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

            if (forcedTool != null)
            {
                body["tool_choice"] = new
                {
                    type = "tool",
                    name = forcedTool
                };
                Logger.Info($"Anthropic: 强制下一步先调用规划工具 {forcedTool}");
            }
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
        var eventHandler = new AgentEventSelfHandler();

        // 每次新的 ExecuteConversationAsync 调用视为一次独立的对话轮次，
        // 重置子任务门禁计数器，让 AI 有机会重新推理而不被上一轮的计数牵连
        _consecutiveOpenSubtaskTextReplies = 0;

        for (int iteration = 0; ; iteration++)
        {
            // ★★★ 硬上限检测：无论任何提醒都强制终止，防止无限循环 ★★★
            if (iteration >= MaxHardIterations)
            {
                Logger.Warning($"工具循环硬上限触发: 已达到 {MaxHardIterations} 轮，强制终止");
                yield return $"\n\n⛔ 工具调用已超过 {MaxHardIterations} 轮，系统强制终止本次请求。AI 可能陷入了无法自行解决的循环，请尝试重新开始任务或手动操作当前页面。";
                yield break;
            }

            Logger.Info($"工具循环 迭代 #{iteration + 1}, 消息数={messages.Count}");

            // ★★★ 上下文压缩：超过阈值时立即压缩到目标值以下 ★★★
            var contextBytes = EstimateConversationBytes(messages);
            if (contextBytes > ContextCompressionTriggerBytes)
            {
                var beforeCount = messages.Count;
                var beforeBytes = contextBytes;
                CompressHistory(messages, ContextCompressionTargetBytes, ContextBuilder.RuntimeToolEvidence);
                var afterBytes = EstimateConversationBytes(messages);
                if (messages.Count < beforeCount || afterBytes < beforeBytes)
                {
                    Logger.Info($"上下文压缩完成: {beforeCount}条/{beforeBytes}字节 → {messages.Count}条/{afterBytes}字节");
                }
                else
                {
                    Logger.Debug($"上下文压缩跳过（无完整循环可压缩）");
                }
            }

            InjectAgentEvents(messages, eventHandler.DrainPendingSystemEvents());

            // ★★★ UI 防卡死：每 3 轮让出 UI 线程处理消息 ★★★
            if (iteration > 0 && iteration % 3 == 0)
                await Task.Yield();

            // ---- 收集本轮流式事件 ----
            var textChunks = new List<string>();
            var toolCallAcc = new Dictionary<int, ToolCallData>();
            string? finishReason = null;
            var requiredPlanningTool = ResolveRequiredPlanningTool(messages);

            // 临时变量：记录当前正在处理的 tool_call index（用于 Anthropic 的 content_block 增量）
            int? pendingToolIndex = null;

            int eventCount = 0;
            await foreach (var evt in StreamRichEventsAsync(messages, ct).ConfigureAwait(false))
            {
                eventCount++;
                // ★ 调试：记录每个事件的类型，帮助诊断工具调用丢失
                if (eventCount <= 10)
                    Logger.Debug($"  StreamRichEvents 事件 #{eventCount}: type={evt.Type}, finishReason={evt.FinishReason}, toolName={evt.ToolName}, toolArgsLen={(evt.ToolArgs ?? "").Length}");

                switch (evt.Type)
                {
                    case "content":
                        textChunks.Add(evt.Text ?? "");
                        if (requiredPlanningTool == null)
                            yield return evt.Text ?? "";
                        break;

                    case "tool_call_start":
                        var idx = evt.ToolIndex ?? (pendingToolIndex.HasValue ? pendingToolIndex.Value + 1 : 0);
                        if (!pendingToolIndex.HasValue) pendingToolIndex = 0;
                        pendingToolIndex = idx;
                        Logger.Debug($"  tool_call_start: idx={idx}, id={evt.ToolId}, name={evt.ToolName}, argsLen={evt.ToolArgs?.Length}");
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
                        // ★ 收到 finish_reason 后停止读取 SSE 流，防止服务器半开连接导致永久等待
                        //   ★ 注意：必须用 break 跳出 await foreach，不能用 yield break！
                        //   yield break 会直接结束整个 ExecuteConversationAsync，
                        //   导致后面的工具调用检测和规划门禁逻辑完全不被执行。
                        if (finishReason is "stop" or "end_turn" or "tool_calls")
                        {
                            Logger.Debug($"SSE 流收到 finish_reason={finishReason}，停止读取（共收到 {eventCount} 个事件，toolCallAcc={toolCallAcc.Count}）");
                            break;  // 跳出 SSE 读取循环，继续执行后续的 tool_calls 检测和工具执行
                        }
                        break;

                    case "error":
                        yield return $"⚠️ {evt.Text}";
                        yield break;
                }
            }

            var fullText = string.Concat(textChunks);

            // ---- 判断本轮结果 ----
            var isToolCall = finishReason == "tool_calls" || toolCallAcc.Count > 0;
            var gateTool = isToolCall ? null : ResolveRequiredPlanningTool(messages);
            if (!isToolCall && gateTool != null && TryPromotePlanningTextToToolCall(gateTool, fullText, out var promotedArgs))
            {
                Logger.Warning($"规划门禁: AI 将 {gateTool} 参数作为普通文本输出，已兼容转换为工具调用");
                toolCallAcc[0] = new ToolCallData
                {
                    Id = $"call_planning_{gateTool}_{Guid.NewGuid():N}",
                    Type = "function",
                    FunctionName = gateTool,
                    FunctionArguments = promotedArgs
                };
                _consecutivePlanningGateTrips = 0;
                isToolCall = true;
            }

            if (!isToolCall)
            {
                // ★★★ 规划门禁降级：如果本轮要求调用工具但 AI 没调用，回传系统提醒继续循环 ★★★
                if (gateTool != null)
                {
                    _consecutivePlanningGateTrips++;
                    if (_consecutivePlanningGateTrips >= 5)
                    {
                        Logger.Warning($"规划门禁连续 {_consecutivePlanningGateTrips} 次触发仍未生效，终止请求");
                        _consecutivePlanningGateTrips = 0;
                        yield break;
                    }
                    else
                    {
                        Logger.Warning($"规划门禁降级: AI 未调用强制工具 {gateTool}，回传系统提醒继续 (第 {_consecutivePlanningGateTrips} 次)");
                        messages.RemoveAll(m => m.Role == MessageRole.System &&
                            m.Content.Contains("你必须在当前轮调用", StringComparison.OrdinalIgnoreCase));
                        messages.Add(new ChatMessage
                        {
                            Role = MessageRole.System,
                            Content = $"你必须在当前轮调用 `{gateTool}` 工具，不要直接输出文本回复。",
                            Timestamp = DateTime.Now
                        });
                        continue; // 不结束循环，下一轮继续要求调用工具
                    }
                }

                if (ShouldContinueOpenSubtask(messages, fullText, out var subtaskReminder))
                {
                    _consecutiveOpenSubtaskTextReplies++;
                    if (_consecutiveOpenSubtaskTextReplies <= MaxSubtaskGateTrips)
                    {
                        // 清理上一轮的子任务提醒，避免堆积污染上下文
                        messages.RemoveAll(m => m.Role == MessageRole.System &&
                            m.Content.StartsWith("当前仍有已开始但尚未结束的子任务", StringComparison.OrdinalIgnoreCase));
                        messages.Add(new ChatMessage
                        {
                            Role = MessageRole.System,
                            Content = subtaskReminder,
                            Timestamp = DateTime.Now
                        });
                        Logger.Warning($"子任务门禁: 当前仍有未完成子任务，回传提醒继续 (第 {_consecutiveOpenSubtaskTextReplies} 次)");
                        continue;
                    }

                    Logger.Warning($"子任务门禁连续 {_consecutiveOpenSubtaskTextReplies} 次仍未生效，AI 持续输出文本而不执行工具，终止本轮请求");
                    yield break;
                }

                _consecutiveOpenSubtaskTextReplies = 0;

                // 纯文本回复 结束
                // 清理 AI 回复中回显的重复 JSON blob
                var cleanedText = StripRedundantJsonBlocks(fullText);
                if (!string.IsNullOrEmpty(cleanedText) && cleanedText != fullText)
                {
                    Logger.Info($"AI 最终回复清理: {fullText.Length} → {cleanedText.Length} 字符（剥离了 {fullText.Length - cleanedText.Length} 字符的重复 JSON）");
                }

                // ★★★ 复读检测：连续多轮返回高度相似的文本 → 视为复读，注入提醒 ★★★
                var fingerprint = ComputeAiTextFingerprint(cleanedText);
                var prevHash = _recentAiTextFingerprints.FirstOrDefault().Hash;
                if (!string.IsNullOrEmpty(prevHash) && fingerprint == prevHash && cleanedText.Length > 30)
                {
                    _recentAiTextFingerprints.Enqueue((fingerprint, cleanedText));
                    // 保留最近 3 轮的指纹
                    while (_recentAiTextFingerprints.Count > 3)
                        _recentAiTextFingerprints.Dequeue();

                    var consecutiveRepeats = _recentAiTextFingerprints.Count(t => t.Hash == fingerprint);
                    if (consecutiveRepeats >= MaxConsecutiveAiRepeats)
                    {
                        Logger.Warning($"AI 复读检测: 连续 {consecutiveRepeats} 轮返回高度相似的文本（{cleanedText.Length} 字符）");
                        _recentAiTextFingerprints.Clear();
                        yield return cleanedText;
                        yield break;
                    }
                }
                else
                {
                    _recentAiTextFingerprints.Enqueue((fingerprint, cleanedText));
                    while (_recentAiTextFingerprints.Count > 3)
                        _recentAiTextFingerprints.Dequeue();
                }

                if (!string.IsNullOrEmpty(cleanedText) && cleanedText != fullText)
                {
                    Logger.Info($"AI 最终回复清理: {fullText.Length} → {cleanedText.Length} 字符（剥离了 {fullText.Length - cleanedText.Length} 字符的重复 JSON）");
                    yield return cleanedText;
                }
                else
                {
                    yield return fullText;
                }
                yield break;
            }
            // ---- 处理工具调用 ----
            _consecutivePlanningGateTrips = 0;
            if (_consecutiveOpenSubtaskTextReplies > 0)
            {
                // 重置计数器并清理之前堆积的子任务提醒消息
                messages.RemoveAll(m => m.Role == MessageRole.System &&
                    m.Content.StartsWith("当前仍有已开始但尚未结束的子任务", StringComparison.OrdinalIgnoreCase));
            }
            _consecutiveOpenSubtaskTextReplies = 0;
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

                var selfDecision = eventHandler.BeforeToolExecution(tc.FunctionName, args, messages);
                var toolResult = selfDecision.ShouldExecute
                    ? await ExecuteToolWithErrorHandlingAsync(executeTool, tc.FunctionName, args)
                    : selfDecision.SyntheticToolResult;
                if (!selfDecision.ShouldExecute)
                    Logger.Warning($"agent_event 拦截工具调用: {tc.FunctionName}({argsPreview})");

                // ★★★ 检测 ask_user 暂停信号：先把 tool 调用占位结果写入历史，再暂停 ★★★
                // 恢复时用户回答会作为同一个 tool_call_id 的 Tool 消息替换该占位，
                // 这样上层 UI 即使退出当前请求，也能保存完整的 assistant(tool_calls) 链路。
                if (toolResult != null && toolResult.StartsWith("__ASK_USER_PAUSED__:"))
                {
                    messages.Add(new ChatMessage
                    {
                        Role = MessageRole.Tool,
                        ToolCallId = tc.Id,
                        ToolName = tc.FunctionName,
                        Content = "等待用户回答…",
                        Timestamp = DateTime.Now
                    });

                    yield return toolResult; // 包含完整 JSON 的暂停信号
                    Logger.Info($"工具循环因 ask_user 暂停");
                    yield break; // 结束循环，等待调用方恢复
                }

                // ★★★ 子任务边界：执行子任务前强制压缩此前上下文 ★★★
                if (toolResult != null && toolResult.StartsWith("__SUBTASK_CONTEXT_COMPRESSED__:"))
                {
                    var beforeCount = messages.Count;
                    var beforeBytes = EstimateConversationBytes(messages);
                    CompressHistory(messages, ContextCompressionTargetBytes, ContextBuilder.RuntimeToolEvidence);
                    var afterBytes = EstimateConversationBytes(messages);
                    Logger.Info($"子任务开始前上下文压缩: {beforeCount}条/{beforeBytes}字节 → {messages.Count}条/{afterBytes}字节");
                    toolResult = toolResult["__SUBTASK_CONTEXT_COMPRESSED__:".Length..];
                }

                // ★★★ 子任务完成：最大强度压缩——清理已完成历史，只保留关键证据和最新上下文 ★★★
                if (toolResult != null && toolResult.StartsWith("__SUBTASK_COMPLETED_COMPRESSED__:"))
                {
                    var beforeCount = messages.Count;
                    var beforeBytes = EstimateConversationBytes(messages);
                    CompressHistory(messages, ContextCompressionMaxTargetBytes, ContextBuilder.RuntimeToolEvidence);
                    var afterBytes = EstimateConversationBytes(messages);
                    Logger.Info($"子任务完成最大压缩: {beforeCount}条/{beforeBytes}字节 → {messages.Count}条/{afterBytes}字节");
                    toolResult = toolResult["__SUBTASK_COMPLETED_COMPRESSED__:".Length..];
                }

                if (toolResult != null)
                {
                    var snippet = toolResult.Length > 200 ? toolResult[..200] + "…" : toolResult;
                    Logger.Debug($"工具 {tc.FunctionName} 执行结果: {snippet}");

                    // ★★★ browser_js 返回 null 检测：JS 执行成功但结果为空 → 提醒 AI 换策略 ★★★
                    if (tc.FunctionName == "browser_js" && IsJsonNullResult(toolResult))
                    {
                        var jsNullCount = IncrementJsNullCount(tc.FunctionName);
                        if (jsNullCount >= 2)
                        {
                            // 连续多次 JS 查询返回 null，注入系统提示建议换策略
                            Logger.Warning($"browser_js 连续 {jsNullCount} 次返回 null，注入策略提示");
                            var jsHint = $"[agent_event code=js_null_hint severity=warning]\nJS 查询连续 {jsNullCount} 次返回空结果，说明页面结构或元素与你预期的不同。instruction: 换一种 JS 查询逻辑（例如改用 querySelectorAll 遍历、检查父容器内容），或先调用 observe_browser 获取完整快照再决定。";
                            messages.Add(new ChatMessage
                            {
                                Role = MessageRole.System,
                                Content = jsHint,
                                Timestamp = DateTime.Now
                            });
                        }
                    }

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

                    // ★★★ 截断过大的工具结果，防止污染 LLM 上下文 ★★★
                    // browser_snapshot 可能返回整个 DOM 的数百 KB 数据，必须限制大小
                    var displayResult = toolResult;
                    if (toolResult.Length > MaxToolResultChars)
                    {
                        displayResult = TruncateToolResult(tc.FunctionName, toolResult);
                    }

                    // 将工具执行结果添加到消息历史（供 AI 下一步推理）
                    messages.Add(new ChatMessage
                    {
                        Role = MessageRole.Tool,
                        ToolCallId = tc.Id,
                        ToolName = tc.FunctionName,
                        Content = displayResult,
                        Timestamp = DateTime.Now
                    });
                    eventHandler.AfterToolExecution(tc.FunctionName, args, toolResult);

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

            if (eventHandler.ShouldTerminate(out var eventStopMessage))
            {
                Logger.Warning($"agent_event 中止工具循环: {eventStopMessage}");
                yield return $"\n\n{eventStopMessage}";
                yield break;
            }

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
                messages.RemoveAll(m => m.Role == MessageRole.System &&
                    (m.Content.StartsWith("⚠️ 前方预警", StringComparison.OrdinalIgnoreCase) ||
                     m.Content.StartsWith("💡 效率提示", StringComparison.OrdinalIgnoreCase)));
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

        var requestFactory = () => IsAnthropicProvider()
            ? BuildAnthropicRequest(messages)
            : BuildOpenAIRequest(messages, provider);
        using var request = requestFactory();

        // ★ 独立的 HTTP 请求超时（90s），与用户取消令牌分离
        //   避免 HttpClient.Timeout（120s）抛出 OperationCanceledException 后被误判为"用户取消"
        using var httpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        httpCts.CancelAfter(TimeSpan.FromSeconds(90));

        bool isHttpTimeout = false;
        HttpResponseMessage? response = null;
        try
        {
            response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, httpCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 90s HTTP 超时（非用户主动取消）
            isHttpTimeout = true;
        }

        if (isHttpTimeout)
        {
            Logger.Warning("HTTP 请求超时（90s）：服务器未在时限内响应");
            yield return new AiStreamEvent { Type = "error", Text = "服务器响应超时，请重试" };
            yield break;
        }

        if (!response!.IsSuccessStatusCode)
        {
            var retryResponse = await RetryTransientFailureAsync(response, requestFactory, ct).ConfigureAwait(false);
            if (retryResponse != null)
            {
                response = retryResponse;
            }
            else
            {
                var error = await response!.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (IsContextWindowExceeded(error))
                {
                    Logger.Warning($"API 上下文窗口已满，停止继续请求: {response.StatusCode} — {TruncateError(error)}");
                    yield break;
                }

                Logger.Error($"API 请求失败: {response.StatusCode} — {TruncateError(error)}");
                yield return new AiStreamEvent { Type = "error", Text = $"API 请求失败 ({response.StatusCode}): {TruncateError(error)}" };
                yield break;
            }
        }

        await foreach (var evt in ParseStreamRichAsync(response, ct).ConfigureAwait(false))
            yield return evt;
    }

    /// <summary>对 429 和上游网关 5xx 瞬时错误做指数退避重试；每次都重新构造 HttpRequestMessage。</summary>
    private async Task<HttpResponseMessage?> RetryTransientFailureAsync(
        HttpResponseMessage response,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken ct)
    {
        var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!IsRetryableApiFailure(response.StatusCode, errorBody, out var maxRetries, out var baseDelayMs, out var reason))
            return null;

        Logger.Warning($"API 返回可重试错误 ({reason})，尝试重试… [{TruncateError(errorBody)}]");

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var delayMs = baseDelayMs * (int)Math.Pow(2, attempt - 1);
            await Task.Delay(delayMs, ct).ConfigureAwait(false);

            using var retryRequest = requestFactory();
            var retryResponse = await _http.SendAsync(
                retryRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (retryResponse.IsSuccessStatusCode)
            {
                Logger.Info($"API 可重试错误已恢复: 第 {attempt} 次重试成功");
                return retryResponse;
            }

            var retryError = await retryResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!IsRetryableApiFailure(retryResponse.StatusCode, retryError, out _, out _, out var retryReason))
            {
                Logger.Error($"API 请求失败 (重试 {attempt} 次后): {retryResponse.StatusCode} — {TruncateError(retryError)}");
                return null;
            }

            Logger.Warning($"API 重试 #{attempt} 仍失败 ({retryReason})，下一次继续退避… [{TruncateError(retryError)}]");
            response = retryResponse;
        }

        var finalError = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        Logger.Error($"API 请求失败 (重试 {maxRetries} 次后): {response.StatusCode} — {TruncateError(finalError)}");
        return null;
    }

    private static bool IsContextWindowExceeded(string errorBody)
        => errorBody.Contains("ContextWindowExceeded", StringComparison.OrdinalIgnoreCase)
           || errorBody.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
           || errorBody.Contains("max context", StringComparison.OrdinalIgnoreCase);

    private static bool IsRetryableApiFailure(
        System.Net.HttpStatusCode statusCode,
        string errorBody,
        out int maxRetries,
        out int baseDelayMs,
        out string reason)
    {
        if (statusCode is System.Net.HttpStatusCode.TooManyRequests || statusCode == (System.Net.HttpStatusCode)429)
        {
            maxRetries = MaxRateLimitRetries;
            baseDelayMs = RateLimitBaseDelayMs;
            reason = "429/TooManyRequests";
            return true;
        }

        var isGatewayOrServerError = statusCode is System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout;
        var looksLikeUpstreamTransient = errorBody.Contains("upstream error", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("do_request_failed", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("overloaded", StringComparison.OrdinalIgnoreCase)
            || errorBody.Contains("temporarily", StringComparison.OrdinalIgnoreCase);

        if (isGatewayOrServerError && looksLikeUpstreamTransient)
        {
            maxRetries = MaxTransientErrorRetries;
            baseDelayMs = TransientErrorBaseDelayMs;
            reason = $"{statusCode}/upstream transient";
            return true;
        }

        maxRetries = 0;
        baseDelayMs = 0;
        reason = string.Empty;
        return false;
    }

    /// <summary>解析流式响应为富事件序列</summary>
    private async IAsyncEnumerable<AiStreamEvent> ParseStreamRichAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        // ★ 整体流超时：从流开始读取起算，防止服务器半开连接导致永久等待
        //   如果流中长时间没有数据到达（连续行超时已触发），说明连接已死
        using var streamTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        streamTimeoutCts.CancelAfter(TimeSpan.FromSeconds(120));

        int lineNum = 0;
        int parsedCount = 0;
        int droppedCount = 0;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            // ★ 每行独立超时 + 可取消：创建链接到 ct 的新 CTS，30 秒超时
            //   用完即释放，避免旧代码中 readTimeoutCts 复用导致的"一次超时，行行超时"的级联问题
            AiStreamEvent? parsedEvent = null;
            string? readLine = null;
            bool isTimeout = false;
            using var lineTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(streamTimeoutCts.Token);
            lineTimeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                readLine = await reader.ReadLineAsync(lineTimeoutCts.Token)
                    .ConfigureAwait(false);

                if (readLine is null) break; // 流正常结束

                lineNum++;

                if (!string.IsNullOrWhiteSpace(readLine))
                {
                    // ★ 调试：记录前 5 行非空 SSE 行的内容（截断），帮助诊断解析问题
                    if (lineNum <= 5)
                    {
                        var preview = readLine.Length > 300 ? readLine[..300] + "…" : readLine;
                        Logger.Debug($"SSE 行 #{lineNum}: {preview}");
                    }

                    parsedEvent = IsAnthropicProvider()
                        ? ParseAnthropicLineRich(readLine)
                        : ParseOpenAILineRich(readLine);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && !streamTimeoutCts.IsCancellationRequested)
            {
                // 30 秒行读取超时（非用户取消、非流超时）
                isTimeout = true;
            }
            catch (OperationCanceledException)
            {
                // 用户取消或流超时 → 静默结束
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
            {
                parsedCount++;
                yield return parsedEvent;
            }
            else if (!string.IsNullOrWhiteSpace(readLine))
            {
                droppedCount++;
            }
        }

        Logger.Debug($"SSE 流解析统计: 共 {lineNum} 行, 成功解析 {parsedCount} 个事件, 丢弃 {droppedCount} 行");
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

            // ★ 先提取 delta 中的 content/tool_calls，再检查 finish_reason
            //   某些服务端会在同一 chunk 中同时包含 delta.tool_calls 和 finish_reason，
            //   如果先检查 finish_reason 会直接 return，丢弃工具调用数据。
            if (choice.TryGetProperty("delta", out var delta))
            {
                // tool_calls — 优先于普通 content 提取，因为 tool_calls 存在时 content 通常为空
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
                            // 同一 chunk 可能同时包含 finish_reason，先返回 tool_call 事件
                            // 调用方会在收到 tool_call 后继续读取，最终也会收到 finish 事件
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

                // text content
                if (delta.TryGetProperty("content", out var content))
                    return new AiStreamEvent { Type = "content", Text = content.GetString() ?? "" };

                // DeepSeek reasoning_content
                if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind != JsonValueKind.Null)
                    return new AiStreamEvent { Type = "content", Text = rc.GetString() ?? "" };
            }

            // finish_reason — 在所有 delta 数据提取之后检查
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null)
                return new AiStreamEvent { Type = "finish", FinishReason = fr.GetString() };
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

    /// <summary>
    /// 校验并修复工具调用参数 JSON。
    /// 某些模型在流式响应中会返回不完整的参数 JSON（如单个 "{" 或缺少闭合括号），
    /// 这类数据写入历史消息后再发给上游代理时，代理会尝试解析 arguments 字段报 JSON 错误。
    /// 本方法确保返回的字符串一定是合法的 JSON 对象：非法则降级为 "{}"。
    /// </summary>
    private static string SanitizeToolArguments(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        // 快速预检：以 { 开头、以 } 结尾是合法 JSON 对象的基本前提
        var trimmed = raw.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            return string.Empty;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                return raw; // 合法 JSON 对象，原样返回
        }
        catch
        {
            // 解析失败，走下面的降级路径
        }

        Logger.Warning($"工具参数 JSON 不合法，已降级为空对象: {raw.Truncate(80)}");
        return string.Empty;
    }

    private static string BuildPlanningToolReminder(string forcedTool)
        => $"当前轮必须优先调用 `{forcedTool}`，不要先输出普通文本，也不要先调用浏览器或信息收集工具。";

    private static void InjectAgentEvents(List<ChatMessage> messages, IEnumerable<ChatMessage> events)
    {
        foreach (var evt in events)
        {
            var code = ExtractAgentEventCode(evt.Content);
            if (!string.IsNullOrWhiteSpace(code))
            {
                messages.RemoveAll(m => m.Role == MessageRole.System &&
                    ExtractAgentEventCode(m.Content).Equals(code, StringComparison.OrdinalIgnoreCase));
            }
            messages.Add(evt);
        }
    }

    private static string ExtractAgentEventCode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || !content.StartsWith("[agent_event", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var marker = "code=";
        var start = content.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return string.Empty;
        start += marker.Length;
        var end = content.IndexOfAny(new[] { ' ', ']' }, start);
        return end > start ? content[start..end].Trim() : content[start..].Trim();
    }

    private bool ShouldContinueOpenSubtask(List<ChatMessage> messages, string text, out string reminder)
    {
        reminder = string.Empty;

        // ★★★ 状态机优先 ★★★
        var sm = ContextBuilder.TaskStateMachine;
        if (sm != null)
        {
            if (sm.CurrentState != TaskState.Executing || sm.IsComplete)
                return false;
            if (LooksLikeExplicitFinalOrBlockedReport(text))
                return false;
            reminder = "当前仍有已开始但尚未结束的子任务。不要把阶段性进展当成最终回复。请继续执行下一步工具调用；如果该子任务已经完成，先调用 `finish_subtask(status=\"completed\")`；如果无法继续，调用 `finish_subtask(status=\"blocked\")` 并说明原因；如果需要用户提供信息，调用 `ask_user`。";
            return true;
        }

        // ★★★ 兜底：旧消息历史扫描逻辑 ★★★
        var hasTodoCreated = HasToolEvidence(messages, "update_todo") || ContextBuilder.RuntimeHasTodoItems;
        var lastStarted = messages
            .Where(m => m.Role == MessageRole.Tool && string.Equals(m.ToolName, "start_subtask", StringComparison.OrdinalIgnoreCase))
            .LastOrDefault();
        var hasRuntimeOpenSubtask = !string.IsNullOrWhiteSpace(ContextBuilder.RuntimeActiveSubtaskId);
        if (!hasTodoCreated || (lastStarted == null && !hasRuntimeOpenSubtask))
            return false;

        if (lastStarted != null)
        {
            var startedIndex = messages.LastIndexOf(lastStarted);
            var hasFinishAfterStart = messages
                .Skip(startedIndex + 1)
                .Any(m => m.Role == MessageRole.Tool && string.Equals(m.ToolName, "finish_subtask", StringComparison.OrdinalIgnoreCase));
            if (hasFinishAfterStart && !hasRuntimeOpenSubtask)
                return false;
        }

        if (LooksLikeExplicitFinalOrBlockedReport(text))
            return false;

        reminder = "当前仍有已开始但尚未结束的子任务。不要把阶段性进展当成最终回复。请继续执行下一步工具调用；如果该子任务已经完成，先调用 `finish_subtask(status=\"completed\")`；如果无法继续，调用 `finish_subtask(status=\"blocked\")` 并说明原因；如果需要用户提供信息，调用 `ask_user`。";
        return true;
    }

    private static bool LooksLikeExplicitFinalOrBlockedReport(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains("无法继续", StringComparison.OrdinalIgnoreCase)
               || text.Contains("需要用户", StringComparison.OrdinalIgnoreCase)
               || text.Contains("请用户", StringComparison.OrdinalIgnoreCase)
               || text.Contains("手动", StringComparison.OrdinalIgnoreCase)
               || text.Contains("已完成所有", StringComparison.OrdinalIgnoreCase)
               || text.Contains("全部完成", StringComparison.OrdinalIgnoreCase)
               || text.Contains("任务已完成", StringComparison.OrdinalIgnoreCase)
               || text.Contains("blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildOpenSubtaskProgressText(string text)
    {
        var progress = StripRedundantJsonBlocks(text).Trim();
        if (string.IsNullOrWhiteSpace(progress))
            progress = "AI 当前只返回了阶段性进展，尚未结束当前子任务。";

        return $"{progress}\n\n⚠️ 当前任务尚未完成：仍有已开始但未 finish_subtask 的子任务。请发送“继续”让 AI 继续执行，或手动处理当前页面后再继续。";
    }

    private static bool TryPromotePlanningTextToToolCall(string forcedTool, string text, out string argumentsJson)
    {
        argumentsJson = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var candidate in EnumerateJsonObjects(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                var isMatch = forcedTool switch
                {
                    "update_todo" => root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array,
                    "start_subtask" => root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        && root.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String,
                    _ => false
                };

                if (!isMatch)
                    continue;

                argumentsJson = root.GetRawText();
                return true;
            }
            catch (JsonException)
            {
                // 继续尝试后续 JSON 片段
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateJsonObjects(string text)
    {
        var start = -1;
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;
                depth++;
            }
            else if (ch == '}' && depth > 0)
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    yield return text[start..(i + 1)];
                    start = -1;
                }
            }
        }
    }

    private bool SupportsForcedToolChoice(ProviderInfo? provider)
    {
        if (provider?.Key is "deepseek" or "volcengine-ark" or "custom")
            return false;

        var model = Settings.Model ?? string.Empty;
        if (model.Contains("reason", StringComparison.OrdinalIgnoreCase)
            || model.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private bool HasAnyToolEvidence(List<ChatMessage> messages)
        => ContextBuilder.HasAnyRuntimeToolEvidence() ||
           messages.Any(m => m.Role == MessageRole.Tool) ||
           messages.Any(m => m.Role == MessageRole.System && ContainsToolEvidenceMarker(m.Content));

    private bool HasToolEvidence(List<ChatMessage> messages, string toolName)
    {
        return ContextBuilder.HasRuntimeToolEvidence(toolName) ||
               messages.Any(m =>
                   m.Role == MessageRole.Tool &&
                   string.Equals(m.ToolName, toolName, StringComparison.OrdinalIgnoreCase)) ||
               messages.Any(m =>
                   m.Role == MessageRole.System &&
                   ContainsToolEvidence(m.Content, toolName));
    }

    private static bool ContainsToolEvidenceMarker(string? content)
        => !string.IsNullOrWhiteSpace(content) &&
           content.Contains("tool_evidence:", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsToolEvidence(string? content, string toolName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        if (ExtractToolEvidenceNames(content)
            .Any(t => string.Equals(t, toolName, StringComparison.OrdinalIgnoreCase)))
            return true;

        return content.Contains($"调用: {toolName}", StringComparison.OrdinalIgnoreCase) ||
               content.Contains($"[{toolName}]", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ExtractToolEvidenceNames(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || !ContainsToolEvidenceMarker(content))
            yield break;

        var markerStart = content.IndexOf("tool_evidence:", StringComparison.OrdinalIgnoreCase);
        var markerEnd = content.IndexOf("-->", markerStart, StringComparison.Ordinal);
        var marker = markerEnd >= 0
            ? content[markerStart..markerEnd]
            : content[markerStart..Math.Min(content.Length, markerStart + 300)];

        foreach (var token in marker.Split(new[] { ':', ',', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!token.Equals("tool_evidence", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
                yield return token.Trim();
        }
    }

    /// <summary>决定当前轮是否必须先调用任务规划工具</summary>
    private string? ResolveRequiredPlanningTool(List<ChatMessage> messages)
    {
        if (ContextBuilder is not { IsEnabled: true, RegisteredTools.Count: > 0 })
            return null;

        var sm = ContextBuilder.TaskStateMachine;
        if (sm != null)
        {
            switch (sm.CurrentState)
            {
                case TaskState.Planning:
                    Logger.Debug("规划门禁(状态机): Planning → 必须先调用 update_todo");
                    return "update_todo";
                case TaskState.Executing when string.IsNullOrEmpty(sm.ActiveSubtaskId):
                    Logger.Debug("规划门禁(状态机): Executing 但无 ActiveSubtaskId → 必须先调用 start_subtask");
                    return "start_subtask";
                case TaskState.Complete:
                    Logger.Debug("规划门禁(状态机): Complete → 无需强制工具");
                    return null;
                case TaskState.Executing:
                    // 正在执行子任务，不需要强制规划工具
                    return null;
            }
        }

        // ★★★ 兜底：状态机未初始化或为空时的旧消息历史扫描逻辑 ★★★
        var hasUpdateTodo = ContextBuilder.RegisteredTools.Any(t => t.Name == "update_todo");
        var hasStartSubtask = ContextBuilder.RegisteredTools.Any(t => t.Name == "start_subtask");
        if (!hasUpdateTodo)
            return null;

        var hasToolResult = HasAnyToolEvidence(messages);
        if (!hasToolResult)
        {
            Logger.Debug("规划门禁(兜底): 首轮必须先建立任务清单");
            return "update_todo";
        }

        if (!hasStartSubtask)
            return null;

        var hasTodoCreated = HasToolEvidence(messages, "update_todo");
        if (!hasTodoCreated)
        {
            Logger.Debug("规划门禁(兜底): 尚未建立任务清单，继续要求 update_todo");
            return "update_todo";
        }

        var hasSubtaskStarted = HasToolEvidence(messages, "start_subtask");
        if (!hasSubtaskStarted)
        {
            Logger.Debug("规划门禁(兜底): 任务清单已建立，必须先开始第一个子任务");
            return "start_subtask";
        }

        return null;
    }

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

    /// <summary>剥离 AI 最终回复中回显的重复 JSON blob（update_todo/start_subtask 参数）</summary>
    private static string StripRedundantJsonBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var lines = text.Split('\n');
        var jsonLines = new List<int>();
        var textLines = new List<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // 检测是否是 JSON 行
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                jsonLines.Add(i);
            }
            else
            {
                textLines.Add(i);
            }
        }

        var jsonRatio = (double)jsonLines.Count / Math.Max(1, jsonLines.Count + textLines.Count);
        if (jsonRatio < 0.3 || jsonLines.Count < 3)
            return text; // JSON 占比不高，不需要清理

        // 收集所有有意义的文本行
        if (textLines.Count == 0)
            return string.Empty; // 没有纯文本结论

        // 拼接有意义的文本（去重连续重复行）
        var sb = new StringBuilder();
        var lastKey = string.Empty;
        foreach (var idx in textLines)
        {
            var line = lines[idx].Trim();
            if (string.IsNullOrEmpty(line)) continue;
            // 检测重复：去掉与最后一条已添加行相同的内容
            var key = line.Length > 80 ? line[..80] : line;
            if (key == lastKey) continue;
            lastKey = key;

            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine(line);
        }

        var result = sb.ToString().Trim();
        return result.Length < 5 ? string.Empty : result;
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
    private static void CompressHistory(List<ChatMessage> messages, int? targetBytes = null, IEnumerable<string>? runtimeToolEvidence = null)
    {
        int firstUserIdx = messages.FindIndex(m => m.Role == MessageRole.User);
        if (firstUserIdx < 0 || messages.Count - firstUserIdx < 10) return;

        var keepCount = Math.Min(15, messages.Count - firstUserIdx - 1);
        var safeBoundary = FindCompressionBoundary(messages, firstUserIdx, keepCount);
        if (safeBoundary <= firstUserIdx + 2) return;

        ApplyCompression(messages, firstUserIdx, safeBoundary, runtimeToolEvidence);

        if (!targetBytes.HasValue) return;

        var guard = 0;
        while (EstimateConversationBytes(messages) > targetBytes.Value && guard++ < 8)
        {
            keepCount = Math.Max(5, keepCount / 2);
            safeBoundary = FindCompressionBoundary(messages, firstUserIdx, keepCount);
            if (safeBoundary <= firstUserIdx + 2)
            {
                TrimCompressionSummary(messages, firstUserIdx, targetBytes.Value);
                break;
            }

            ApplyCompression(messages, firstUserIdx, safeBoundary, runtimeToolEvidence);
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

    private static void ApplyCompression(List<ChatMessage> messages, int firstUserIdx, int safeBoundary, IEnumerable<string>? runtimeToolEvidence = null)
    {
        var summaryParts = new List<string>();
        var toolNames = new HashSet<string>(runtimeToolEvidence ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        for (int i = firstUserIdx + 1; i < safeBoundary; i++)
        {
            var msg = messages[i];
            if (msg is { Role: MessageRole.Assistant, HasToolCalls: true })
            {
                var names = msg.ToolCalls!
                    .Select(tc => tc.FunctionName)
                    .Distinct()
                    .ToList();
                summaryParts.Add($"🛠 调用: {string.Join(", ", names)}");
                foreach (var n in names) toolNames.Add(n);
            }
            else if (msg is { Role: MessageRole.System } && ContainsToolEvidenceMarker(msg.Content))
            {
                foreach (var name in ExtractToolEvidenceNames(msg.Content))
                    toolNames.Add(name);
            }
            else if (msg is { Role: MessageRole.Assistant, HasToolCalls: false } &&
                     !string.IsNullOrWhiteSpace(msg.Content))
            {
                var snippet = msg.Content.Length > 80 ? msg.Content[..80] + "…" : msg.Content;
                summaryParts.Add($"→ {snippet}");
            }
            else if (msg.Role == MessageRole.Tool &&
                     !string.IsNullOrWhiteSpace(msg.Content) &&
                     msg.Content.Length > 20)
            {
                var snippet = msg.Content.Length > 80 ? msg.Content[..80] + "…" : msg.Content;
                summaryParts.Add($"  [{msg.ToolName}] {snippet}");
                if (!string.IsNullOrWhiteSpace(msg.ToolName)) toolNames.Add(msg.ToolName);
            }
        }

        if (summaryParts.Count == 0) return;

        var evidenceLine = toolNames.Count > 0
            ? $"<!-- tool_evidence: {string.Join(",", toolNames.OrderBy(n => n))} -->\n\n"
            : string.Empty;
        var summary = "📋 **已完成操作的摘要（已压缩历史，保留最新上下文）：**\n\n"
                    + evidenceLine
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

        var evidenceLine = string.Empty;
        var markerStart = summaryMsg.Content.IndexOf("<!-- tool_evidence:", StringComparison.OrdinalIgnoreCase);
        if (markerStart >= 0)
        {
            var markerEnd = summaryMsg.Content.IndexOf("-->", markerStart, StringComparison.Ordinal);
            if (markerEnd >= 0)
                evidenceLine = summaryMsg.Content[markerStart..(markerEnd + 3)];
        }

        while (EstimateConversationBytes(messages) > targetBytes && summaryMsg.Content.Length > 1200)
        {
            var trimmed = summaryMsg.Content[..(summaryMsg.Content.Length / 2)];
            if (!string.IsNullOrEmpty(evidenceLine) && !trimmed.Contains(evidenceLine, StringComparison.OrdinalIgnoreCase))
                trimmed = evidenceLine + "\n\n" + trimmed;
            summaryMsg.Content = trimmed + "\n…（摘要继续截断以满足上下文限制）";
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
        // 乘以 1.4 系数估算 JSON 结构开销（键名、括号、逗号、转义字符等）
        // 使估算值更接近实际 JsonSerializer.Serialize 后的字节数
        return (int)(total * 1.4);
    }

    /// <summary>紧凑 JSON 序列化选项（用于 API 请求体，减少体积）</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>美化 JSON 序列化选项（仅用于调试日志，不用于 API 请求）</summary>
    public static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// 截断过大的工具结果，防止污染 LLM 上下文。
    /// 对 browser_snapshot 等可能返回巨大 DOM 快照的工具特别重要。
    /// </summary>
    private static string TruncateToolResult(string toolName, string result)
    {
        if (result.Length <= MaxToolResultChars)
            return result;

        var head = result[..MaxToolResultChars];
        var tailStart = result.Length - TruncateTailChars;
        var tail = result[tailStart..];

        var truncated = $"{head}\n\n...[已截断，原始内容 {result.Length} 字符，保留首 {MaxToolResultChars} + 尾 {TruncateTailChars} 字符]...\n\n{tail}";
        Logger.Debug($"工具 {toolName} 结果截断: {result.Length} → {truncated.Length} 字符");
        return truncated;
    }

    /// <summary>
    /// 计算 AI 纯文本回复的指纹（用于复读检测）。
    /// 仅取前 512 字符、移除空白、转为小写，生成简单哈希。
    /// </summary>
    private static string ComputeAiTextFingerprint(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";

        var normalized = text.Trim();
        if (normalized.Length > 512)
            normalized = normalized[..512];

        // 移除多余空白并转小写，使不同格式但内容相同的文本匹配
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", "");
        normalized = normalized.ToLowerInvariant();

        // 简单快速哈希
        int hash = 5381;
        foreach (char c in normalized)
        {
            hash = ((hash << 5) + hash) ^ c;
        }
        return hash.ToString("x8");
    }

    /// <summary>
    /// 检测工具结果是否为 {"data": "null"} 形式（JSON null 被序列化为了字符串 "null"）
    /// 这种情况表示 JS 执行成功但结果为空/undefined，不是有效数据。
    /// </summary>
    private static bool IsJsonNullResult(string result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return false;

        // 精确匹配: {"ok":true,"data":"null","error":null,"url":"...","ms":123}
        if (result.Contains("\"data\":\"null\"") || result.Contains("\"data\": \"null\""))
            return true;

        // 也兼容 JSON 解析后的 null 值字符串形式
        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.String)
            {
                var dataStr = dataEl.GetString() ?? "";
                if (dataStr.Equals("null", StringComparison.Ordinal))
                    return true;
            }
            if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True)
            {
                // {"ok":true,"data":null,...} — 真正的 JSON null
                if (root.TryGetProperty("data", out var dataEl2) && dataEl2.ValueKind == JsonValueKind.Null)
                    return true;
            }
        }
        catch
        {
            // 非 JSON 结果不匹配
        }

        return false;
    }

    /// <summary>
    /// 递增 browser_js null 计数器：非 browser_js 调用时重置为 0。
    /// 返回递增后的值。
    /// </summary>
    private int IncrementJsNullCount(string toolName)
    {
        if (toolName != "browser_js")
        {
            if (_consecutiveJsNullResults > 0)
                Logger.Debug("browser_js null 计数器已重置（其他工具返回了结果）");
            _consecutiveJsNullResults = 0;
            return 0;
        }

        return ++_consecutiveJsNullResults;
    }

    public void Dispose() => _http.Dispose();
}
