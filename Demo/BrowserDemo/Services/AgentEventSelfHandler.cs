using System.Text;
using System.Text.Json;
using BrowserDemo.Models;

namespace BrowserDemo.Services;

internal sealed class AgentEventSelfHandler
{
    private const int MaxRecentEvents = 12;

    private readonly Queue<ToolEventRecord> _recentEvents = new();
    private readonly Dictionary<string, int> _sameActionCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _failedUrlCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _failedHostCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownStaleElementIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _staleElementReuseCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChatMessage> _pendingSystemEvents = new();
    private readonly HashSet<string> _pendingCodes = new(StringComparer.OrdinalIgnoreCase);

    // DOM text hash 页面停滞检测
    private string? _previousDomTextHash;
    private int _consecutiveDomUnchanged;
    private const int MaxDomUnchangedBeforeAlert = 2;
    private const int MaxDomUnchangedBeforeTerminate = 4;

    // 连续失败计数（运行时 replan 触发器）
    private int _consecutiveActionFailures;
    private const int ReplanThreshold = 3;

    // 探索限制跟踪
    private int _consecutiveExplorationSteps;
    private const int ExplorationLimit = 5;

    private int _noProgressCycles;
    private int _ignoredAskUserRecommendations;
    private int _deadEndScore;
    private int _staleElementTerminations;
    private string? _terminateMessage;

    public ToolSelfHandlingDecision BeforeToolExecution(string toolName, Dictionary<string, object?>? args, IReadOnlyList<ChatMessage> messages)
    {
        var actionKey = BuildActionKey(toolName, args);

        if (IsElementTool(toolName) && TryGetArgString(args, "element_id", out var elementId) &&
            _knownStaleElementIds.Contains(elementId))
        {
            var reuseCount = Increment(_staleElementReuseCounts, elementId);
            AddEvent("stale_element_reuse", "block",
                $"已检测到继续复用过期 element_id={elementId}。instruction: 不要继续调用相同参数；先 observe_browser 获取最新 id，换元素或 ask_user。");
            if (reuseCount >= 2)
            {
                _staleElementTerminations++;
                _deadEndScore++;
                return ToolSelfHandlingDecision.Block(
                    $"⛔ agent_self_handled: 已拦截重复使用过期 element_id={elementId}。请先调用 observe_browser 获取最新页面快照，使用新的 elements[*].id；如果没有可行动作，请调用 ask_user。");
            }
        }

        if (toolName.Equals("browser_navigate", StringComparison.OrdinalIgnoreCase) &&
            TryNormalizeUrl(args, out var normalizedUrl, out _))
        {
            if (_failedUrlCounts.TryGetValue(normalizedUrl, out var failures) && failures >= 2)
            {
                AddEvent("repeated_navigation_failure", "block",
                    $"同一 URL 已连续导航失败：{normalizedUrl}。instruction: 停止猜 URL；从当前页面入口/搜索/历史链接寻找，或 ask_user 获取正确入口。");
                _deadEndScore++;
                return ToolSelfHandlingDecision.Block(
                    $"⛔ agent_self_handled: 已拦截重复导航失败 URL：{normalizedUrl}。不要继续猜测相同地址；请改从页面入口/搜索结果进入，或调用 ask_user 询问正确入口。");
            }
        }

        if (_sameActionCounts.TryGetValue(actionKey, out var sameCount) && sameCount >= 3)
        {
            AddEvent("repeat_same_action", "block",
                $"同一工具和参数已重复且无有效变化：{actionKey}。instruction: 不要再次调用相同参数；换路线、重新观察，或 ask_user。");
            _deadEndScore++;
            return ToolSelfHandlingDecision.Block(
                $"⛔ agent_self_handled: 已拦截重复无进展动作 {actionKey}。请换一个明确策略；不要继续调用相同工具和参数。必要时调用 ask_user。");
        }

        return ToolSelfHandlingDecision.Execute;
    }

    public void AfterToolExecution(string toolName, Dictionary<string, object?>? args, string? result)
    {
        var argsSignature = BuildArgsSignature(args);
        var resultInfo = ResultInfo.From(result);
        var actionKey = $"{toolName}|{argsSignature}";
        var actionResultKey = $"{actionKey}|{resultInfo.Signature}";
        var sameCount = Increment(_sameActionCounts, actionResultKey);

        EnqueueRecent(new ToolEventRecord(toolName, argsSignature, resultInfo.Signature, resultInfo.Ok, resultInfo.Url, resultInfo.Error));

        if (sameCount == 2)
        {
            AddEvent("repeat_same_action", "warning",
                $"最近重复执行同一工具且结果相同：{actionKey}。instruction: 下一步必须换策略，不要继续同参数重试。");
        }
        else if (sameCount >= 5)
        {
            _terminateMessage = "⚠️ AI 已连续重复同一无进展动作，系统已中止工具循环以避免死胡同。请提供新的页面线索，或让 AI 改用 ask_user 获取指引。";
        }

        if (resultInfo.RequiresAskUser)
        {
            _ignoredAskUserRecommendations++;
            if (_ignoredAskUserRecommendations >= 2)
            {
                AddEvent("ask_user_recommended", "block",
                    "工具结果已多次建议 ask_user。instruction: 现在不要继续尝试同类浏览器动作；必须调用 ask_user 或 finish_subtask(status=\"blocked\")。 ");
            }
            if (_ignoredAskUserRecommendations >= 3)
            {
                _terminateMessage = "⚠️ AI 多次遇到无法推进的问题但未向用户求助，系统已中止以避免继续死循环。请提供正确入口或手动处理当前页面后再继续。";
            }
        }
        else if (resultInfo.Ok)
        {
            _ignoredAskUserRecommendations = 0;
        }

        TrackStaleElement(toolName, args, resultInfo);
        TrackNavigationFailure(toolName, args, resultInfo);
        TrackNoProgressCycle(toolName, resultInfo);
    }

    /// <summary>
    /// 记录 snapshot 返回的 DOM text hash，用于页面停滞检测。
    /// 由 AiClient.ExecuteConversationAsync 在 browser_snapshot/observe_browser 后调用。
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
                AddEvent("page_stalled", "warning",
                    $"页面内容连续 {MaxDomUnchangedBeforeAlert} 次未变化（hash={domTextHash}），当前操作可能无效。请尝试导航到新页面或更换操作策略。");
            }
            else if (_consecutiveDomUnchanged >= MaxDomUnchangedBeforeTerminate)
            {
                _terminateMessage = $"页面内容已连续 {MaxDomUnchangedBeforeTerminate} 次未变化，工具循环判定为死胡同，强制终止。";
                AddEvent("page_stalled_fatal", "critical",
                    $"[agent_event code=page_stalled_fatal severity=critical] 页面内容已连续 {MaxDomUnchangedBeforeTerminate} 次未变化（hash={domTextHash}），工具循环判定为死胡同，强制终止。");
            }
        }
        else
        {
            _consecutiveDomUnchanged = 0;
        }

        _previousDomTextHash = domTextHash;
    }

    /// <summary>记录工具执行的成功/失败结果，用于连续失败 replan 检测。</summary>
    public void RecordActionOutcome(bool isSuccess)
    {
        if (!isSuccess)
        {
            _consecutiveActionFailures++;

            if (_consecutiveActionFailures == ReplanThreshold)
            {
                AddEvent("replan_needed", "warning",
                    $"已连续 {ReplanThreshold} 步操作失败或无进展。请调用 update_todo 重新规划子任务，调整执行策略。");
            }
            else if (_consecutiveActionFailures > ReplanThreshold)
            {
                AddEvent("replan_critical", "critical",
                    $"已连续 {_consecutiveActionFailures} 步失败，必须重新规划。");
            }
        }
        else
        {
            _consecutiveActionFailures = 0;
        }
    }

    /// <summary>记录每一步是否关联了 active subtask，连续游离则提醒。</summary>
    public void RecordStepWithSubtask(bool hasSubtaskAssociation)
    {
        if (!hasSubtaskAssociation)
        {
            _consecutiveExplorationSteps++;

            if (_consecutiveExplorationSteps == ExplorationLimit)
            {
                AddEvent("exploration_limit", "warning",
                    $"已连续 {ExplorationLimit} 步操作未关联任何子任务。请调用 update_todo 制定明确计划，或调用 finish_subtask 完成任务。");
            }
        }
        else
        {
            _consecutiveExplorationSteps = 0;
        }
    }

    public IEnumerable<ChatMessage> DrainPendingSystemEvents()
    {
        var events = _pendingSystemEvents.ToList();
        _pendingSystemEvents.Clear();
        _pendingCodes.Clear();
        return events;
    }

    public bool ShouldTerminate(out string userFacingMessage)
    {
        if (_staleElementTerminations >= 3)
        {
            _terminateMessage = $"⛔ AI 已连续 {_staleElementTerminations} 次尝试复用过期元素导致死胡同，系统已中止工具循环。页面结构可能已发生变化，请刷新页面后重新开始任务。";
        }

        if (string.IsNullOrWhiteSpace(_terminateMessage) && _deadEndScore >= 4)
            _terminateMessage = "⚠️ AI 已触发多次无进展自处理事件，系统已中止工具循环以避免上下文继续膨胀。请换一种路线或提供用户指引。";

        userFacingMessage = _terminateMessage ?? string.Empty;
        return !string.IsNullOrWhiteSpace(_terminateMessage);
    }

    private void TrackStaleElement(string toolName, Dictionary<string, object?>? args, ResultInfo resultInfo)
    {
        if (!IsElementTool(toolName) || !TryGetArgString(args, "element_id", out var elementId))
            return;

        if (!resultInfo.StaleElement)
            return;

        _knownStaleElementIds.Add(elementId);
        AddEvent("stale_element", "warning",
            $"工具结果显示 element_id={elementId} 可能已过期。instruction: 先 observe_browser 重新获取最新 id，不要复用旧 id。");
    }

    private void TrackNavigationFailure(string toolName, Dictionary<string, object?>? args, ResultInfo resultInfo)
    {
        if (!toolName.Equals("browser_navigate", StringComparison.OrdinalIgnoreCase) || resultInfo.Ok)
            return;

        if (!TryNormalizeUrl(args, out var normalizedUrl, out var host))
            normalizedUrl = resultInfo.Url ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUrl))
            return;

        var urlFailures = Increment(_failedUrlCounts, normalizedUrl);
        var hostFailures = string.IsNullOrWhiteSpace(host) ? 0 : Increment(_failedHostCounts, host);
        if (urlFailures >= 2 || hostFailures >= 3)
        {
            AddEvent("repeated_navigation_failure", urlFailures >= 2 ? "block" : "warning",
                $"导航失败重复出现：{normalizedUrl}。instruction: 不要继续猜测相同站点路径；从当前页面入口/搜索/历史链接寻找，或 ask_user。");
        }
        if (hostFailures >= 4)
            _deadEndScore++;
    }

    private void TrackNoProgressCycle(string toolName, ResultInfo resultInfo)
    {
        if (!IsPassiveBrowserTool(toolName))
        {
            if (resultInfo.Ok)
                _noProgressCycles = 0;
            return;
        }

        var passiveCount = _recentEvents.Count(e => IsPassiveBrowserTool(e.ToolName));
        var distinctResults = _recentEvents
            .Where(e => IsPassiveBrowserTool(e.ToolName))
            .Select(e => e.ResultSignature)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (_recentEvents.Count >= 4 && passiveCount >= 4 && distinctResults <= 2)
        {
            _noProgressCycles++;
            AddEvent("no_progress_observe_wait_loop", _noProgressCycles >= 2 ? "block" : "warning",
                "观察/等待/刷新循环没有带来页面变化。instruction: 下一步必须换策略：点击明确入口、搜索、键盘操作、后退/重新导航；没有可行动作就 ask_user。");
        }

        if (_noProgressCycles >= 4)
            _terminateMessage = "⚠️ AI 陷入观察/等待/刷新无进展循环，系统已中止以避免继续消耗上下文。请提供下一步入口或手动调整页面后再继续。";
    }

    private void AddEvent(string code, string severity, string summary)
    {
        if (!_pendingCodes.Add(code))
            return;

        var content = $"[agent_event code={code} severity={severity}]\n{summary}";
        _pendingSystemEvents.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = content,
            Timestamp = DateTime.Now
        });
        Logger.Warning($"agent_event: {code}/{severity} — {summary}");
    }

    private void EnqueueRecent(ToolEventRecord record)
    {
        _recentEvents.Enqueue(record);
        while (_recentEvents.Count > MaxRecentEvents)
            _recentEvents.Dequeue();
    }

    private static int Increment(Dictionary<string, int> dict, string key)
    {
        dict.TryGetValue(key, out var count);
        count++;
        dict[key] = count;
        return count;
    }

    private static bool IsElementTool(string toolName)
        => toolName is "browser_click" or "browser_type" or "browser_hover" or "browser_select_option";

    private static bool IsPassiveBrowserTool(string toolName)
        => toolName is "observe_browser" or "browser_snapshot" or "browser_wait" or "browser_wait_for" or "browser_reload";

    private static string BuildActionKey(string toolName, Dictionary<string, object?>? args)
        => $"{toolName}|{BuildArgsSignature(args)}";

    private static string BuildArgsSignature(Dictionary<string, object?>? args)
    {
        if (args == null || args.Count == 0)
            return "(无参数)";

        var parts = args
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={NormalizeValue(kv.Value)}");
        return string.Join(",", parts);
    }

    private static string NormalizeValue(object? value)
    {
        if (value == null) return "null";
        if (value is JsonElement je) return NormalizeJsonElement(je);
        var text = value.ToString() ?? string.Empty;
        text = text.Trim().Replace("\r", " ").Replace("\n", " ");
        return text.Length > 160 ? text[..160] + "…" : text;
    }

    private static string NormalizeJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => Truncate(element.GetString() ?? string.Empty, 160),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => element.ToString(),
            _ => Truncate(element.GetRawText(), 160)
        };

    private static bool TryGetArgString(Dictionary<string, object?>? args, string name, out string value)
    {
        value = string.Empty;
        if (args == null || !args.TryGetValue(name, out var raw) || raw == null)
            return false;

        value = raw is JsonElement je ? NormalizeJsonElement(je) : raw.ToString() ?? string.Empty;
        value = value.Trim().Trim('"');
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryNormalizeUrl(Dictionary<string, object?>? args, out string normalizedUrl, out string host)
    {
        normalizedUrl = string.Empty;
        host = string.Empty;
        if (!TryGetArgString(args, "url", out var url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            normalizedUrl = url.Trim().TrimEnd('/');
            return !string.IsNullOrWhiteSpace(normalizedUrl);
        }

        host = uri.Host.ToLowerInvariant();
        normalizedUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(uri.Query))
            normalizedUrl += uri.Query;
        return true;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private sealed record ToolEventRecord(string ToolName, string ArgsSignature, string ResultSignature, bool Ok, string? Url, string? Error);

    private sealed record ResultInfo(bool Ok, string Signature, string? Url, string? Error, bool StaleElement, bool RequiresAskUser)
    {
        public static ResultInfo From(string? result)
        {
            if (string.IsNullOrWhiteSpace(result))
                return new ResultInfo(false, "empty", null, null, false, false);

            var ok = !result.Contains("\"ok\":false", StringComparison.OrdinalIgnoreCase) &&
                     !result.StartsWith("错误", StringComparison.OrdinalIgnoreCase) &&
                     !result.StartsWith("❌", StringComparison.OrdinalIgnoreCase);
            string? url = null;
            string? error = null;
            var data = result;

            try
            {
                using var doc = JsonDocument.Parse(result);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("ok", out var okEl))
                        ok = okEl.ValueKind == JsonValueKind.True;
                    if (root.TryGetProperty("url", out var urlEl) && urlEl.ValueKind == JsonValueKind.String)
                        url = urlEl.GetString();
                    if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.String)
                        error = errorEl.GetString();
                    if (root.TryGetProperty("data", out var dataEl))
                        data = dataEl.ValueKind == JsonValueKind.String ? dataEl.GetString() ?? string.Empty : dataEl.GetRawText();
                }
            }
            catch (JsonException)
            {
                // 非 JSON 工具结果按文本处理
            }

            var combined = string.Join("\n", result, error, data);
            var stale = ContainsAny(combined, "element_id 可能已过期", "元素 id 可能已过期", "stale", "找不到元素", "not_found");
            var askUser = ContainsAny(combined, "请使用 ask_user", "调用 ask_user", "向用户求助", "获取用户指引");
            var signatureSeed = error ?? data ?? result;
            var signature = Truncate(signatureSeed.Replace("\r", " ").Replace("\n", " ").Trim(), 220);

            return new ResultInfo(ok, signature, url, error, stale, askUser);
        }

        private static bool ContainsAny(string text, params string[] needles)
            => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed record ToolSelfHandlingDecision(bool ShouldExecute, string? SyntheticToolResult)
{
    public static ToolSelfHandlingDecision Execute { get; } = new(true, null);

    public static ToolSelfHandlingDecision Block(string syntheticToolResult)
        => new(false, syntheticToolResult);
}
