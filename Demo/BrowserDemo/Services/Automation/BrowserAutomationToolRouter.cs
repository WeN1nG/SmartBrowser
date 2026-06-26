using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using BrowserDemo.Models;

namespace BrowserDemo.Services.Automation;

/// <summary>
/// AI 浏览器工具路由器 —— 将 function calling 的 browser_* 工具调用转发到 WebView2 自动化服务。
/// 这里负责 AI 侧的工具 schema、参数容错解析、返回值 JSON 化；实际浏览器操作由 BrowserAutomationService 完成。
/// </summary>
public class BrowserAutomationToolRouter
{
    private readonly BrowserAutomationService _automation;
    private int _consecutiveNavFailures = 0;

    /// <summary>内部自动化服务引用（供外部注入 AiClient）</summary>
    public BrowserAutomationService Automation => _automation;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    private static readonly string[] SupportedKeys =
    {
        "Enter", "Tab", "Escape", "Esc",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
        "Backspace", "Delete", "Del", "Home", "End",
        "PageUp", "PageDown", "Space"
    };

    public BrowserAutomationToolRouter(BrowserAutomationService automation)
    {
        Logger.Trace("BrowserAutomationToolRouter..ctor");
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
    }

    public bool IsToolRegistered(string toolName)
    {
        Logger.Debug($"[IsToolRegistered] toolName={toolName}");
        return _automation.IsToolRegistered(toolName);
    }

    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        // 工具定义是静态的，共 21 个
        Logger.Debug("[GetToolDefinitions] 返回 21 个工具定义");
        return new List<ToolDefinition>
    {
        Tool("browser_navigate", "[浏览器] 打开指定 URL，并等待页面导航完成。", new()
        {
            ["url"] = StringParam("目标 URL，例如 https://www.bing.com"),
            ["timeout_ms"] = IntParam("导航超时时间，毫秒，默认 30000")
        }, "url"),

        Tool("browser_back", "[浏览器] 后退到浏览器历史中的上一页。"),
        Tool("browser_forward", "[浏览器] 前进到浏览器历史中的下一页。"),
        Tool("browser_reload", "[浏览器] 刷新当前页面。"),

        Tool("browser_snapshot", "[浏览器] 获取当前页面的结构化快照，结果保存到本地 JSON 文件。返回文件路径和元素统计信息，不直接返回完整 JSON。获取快照后，必须使用 browser_find_element 工具按条件查询具体元素。不要使用 xp= hash 或 CSS 选择器定位。新字段：paint_order=zIndex 数值（0=auto），overlapped=true 表示可能被高 z-index 元素遮挡，AI 应优先选择 overlapped=false 的元素；select 元素携带 inline_options 列出选项；date/range/color input 携带格式提示；sensitive=true 表示该元素包含敏感数据（value 已脱敏）；viewport_center={x,y} 提供元素中心坐标用于点击兜底；toast/modal/loading 等动态覆盖层元素已被自动过滤。"),

        Tool("browser_find_element", "[浏览器] 从当前页面的本地快照 JSON 文件中查找匹配条件的元素。获取快照后，必须用此工具查询具体元素，不要假设你能直接看到快照 JSON 内容。支持按关键词（tag/aria-label/text/name/placeholder）搜索，也可限定标签类型和元素 ID 范围。", new()
        {
            ["query"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "搜索关键词，匹配 tag/aria-label/text/name/placeholder 字段。例如 '登录按钮', 'input email', '提交'"
            },
            ["tag"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "按 HTML 标签过滤，如 'button', 'a', 'input', 'select'"
            },
            ["ids"] = new Dictionary<string, object?>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object?> { ["type"] = "integer" },
                ["description"] = "限定只搜索这些 element_id，用于缩小范围"
            }
        }, "query"),

        Tool("browser_snapshot_info", "[浏览器] 查看当前快照文件的元信息（URL、标题、元素数量、快照时间等），不返回元素详情。", new()
        {
            ["path"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["description"] = "快照文件路径（来自 browser_snapshot 返回的 saved_to 字段）。如果省略，使用最近一次快照"
            }
        }),

        Tool("browser_click", "[浏览器] 点击页面元素。必须先调用 browser_snapshot，并使用快照中元素的整数 id 作为 element_id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数")
        }, "element_id"),

        Tool("browser_type", "[浏览器] 在页面输入框或可编辑元素中输入文本。必须使用 browser_snapshot 返回的整数 element_id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数"),
            ["text"] = StringParam("要输入的文本"),
            ["clear_first"] = BoolParam("输入前是否先清空原内容，默认 false（在光标位置插入）")
        }, "element_id", "text"),

        Tool("browser_hover", "[浏览器] 将鼠标悬停到页面元素上。必须使用 browser_snapshot 返回的整数 element_id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数")
        }, "element_id"),

        Tool("browser_select_option", "[浏览器] 选择下拉框选项。必须使用 browser_snapshot 返回的整数 element_id，并提供 option value。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 select 元素 id 整数"),
            ["value"] = StringParam("要选择的 option value")
        }, "element_id", "value"),

        Tool("browser_scroll", "[浏览器] 滚动当前页面。正 delta_y 向下滚动，负 delta_y 向上滚动。", new()
        {
            ["delta_x"] = IntParam("横向滚动像素，默认 0"),
            ["delta_y"] = IntParam("纵向滚动像素，默认 300")
        }),

        Tool("browser_scroll_to_element", "[浏览器] 滚动页面使指定元素出现在视口中心。必须先调用 browser_snapshot，并使用快照中元素的整数 id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数")
        }, "element_id"),

        Tool("browser_click_at", "[浏览器] 在视口绝对坐标 (x, y) 处点击。仅在 element_id / stable_hash 均失效时使用；优先从 snapshot 的 viewport_center 获取坐标。", new()
        {
            ["x"] = IntParam("视口 X 坐标（像素）"),
            ["y"] = IntParam("视口 Y 坐标（像素）")
        }, "x", "y"),

        Tool("browser_press_key", $"[浏览器] 向当前页面发送特殊按键。支持: {string.Join(", ", SupportedKeys)}。输入普通文本请用 browser_type。", new()
        {
            ["key"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = SupportedKeys,
                ["description"] = "要按下的特殊按键"
            }
        }, "key"),

        Tool("browser_screenshot", "[浏览器] 截取当前页面用于视觉确认。仅在用户明确要求截图/视觉确认，或 observe_browser/browser_snapshot 连续失败且必须靠视觉判断时使用；为避免污染上下文，工具结果只返回截图完成摘要，不返回完整 base64。", new()
        {
            ["reason"] = StringParam("必须说明为什么需要截图；常规页面读取、搜索结果提取应优先使用 observe_browser/browser_snapshot")
        }, "reason"),

        Tool("browser_js", "[浏览器] 在当前页面执行自定义 JavaScript。仅在快照/点击/输入等标准工具无法完成时使用。", new()
        {
            ["script"] = StringParam("要执行的 JavaScript 表达式或 IIFE")
        }, "script"),

        Tool("browser_wait", "[浏览器] 固定等待一段时间。优先使用 browser_wait_for 等待具体文本出现。", new()
        {
            ["ms"] = IntParam("等待毫秒数，最大 60000")
        }, "ms"),

        Tool("browser_wait_for", "[浏览器] 等待页面中出现指定文本。", new()
        {
            ["text"] = StringParam("要等待出现的文本"),
            ["timeout_ms"] = IntParam("超时时间，毫秒，默认 10000")
        }, "text"),

        Tool("browser_fill_form", "[浏览器] 批量填充表单字段。fields 的 key 可以是 snapshot 元素 id 字符串，或 name/aria-label/placeholder。", new()
        {
            ["fields"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["description"] = "字段映射，例如 {\"3\": \"alice\", \"password\": \"secret\"}",
                ["additionalProperties"] = new Dictionary<string, object?> { ["type"] = "string" }
            }
        }, "fields"),

        Tool("browser_switch_tab", "[浏览器] 切换自动化目标标签页。tab_id 必须是应用内部标签 Guid；通常不需要主动调用。", new()
        {
            ["tab_id"] = StringParam("目标标签页 Guid")
        }, "tab_id"),

        Tool("browser_click_by_hash", "[浏览器] 使用元素的 stable_hash 进行点击。当 element_id 失效时使用此工具。stable_hash 基于元素的 tag、aria-label、name、placeholder、text 计算，页面刷新后仍然有效。", new()
        {
            ["stable_hash"] = StringParam("元素的稳定哈希值（来自 snapshot elements[*].stable_hash）")
        }, "stable_hash")
    };
    }

    public async Task<string> InvokeAsync(string toolName, Dictionary<string, object?>? args)
    {
        using var _ = Logger.Trace($"BrowserAutomationToolRouter.InvokeAsync::{toolName}");
        args ??= new Dictionary<string, object?>();
        Logger.Debug($"[InvokeAsync] toolName={toolName}, argCount={args.Count}");

        try
        {
            return toolName switch
            {
                "browser_navigate" =>
                    await NavigateWithFailureDetection(args),

                "browser_back" => Format(await _automation.GoBackAsync()),
                "browser_forward" => Format(await _automation.GoForwardAsync()),
                "browser_reload" => Format(await _automation.ReloadAsync()),

                "browser_snapshot" => await SnapshotWithSaveAsync(args),
                "browser_find_element" => FindElementAsync(args),
                "browser_snapshot_info" => SnapshotInfo(args),

                "browser_click" => Format(await _automation.ClickAsync(RequiredElementId(args))),
                "browser_type" => Format(await _automation.TypeAsync(
                    RequiredElementId(args), RequiredString(args, "text"), GetBool(args, "clear_first") ?? false)),
                "browser_hover" => Format(await _automation.HoverAsync(RequiredElementId(args))),
                "browser_select_option" => Format(await _automation.SelectOptionAsync(
                    RequiredElementId(args), RequiredString(args, "value"))),

                "browser_scroll" => Format(await _automation.ScrollAsync(
                    GetInt(args, "delta_x") ?? 0, GetInt(args, "delta_y") ?? 300)),

                "browser_scroll_to_element" => Format(await _automation.ScrollToElementAsync(RequiredElementId(args))),

                "browser_press_key" => Format(await _automation.PressKeyAsync(RequiredString(args, "key"))),
                "browser_screenshot" => await ScreenshotWithReasonAsync(RequiredString(args, "reason")),
                "browser_js" => Format(await _automation.EvaluateJavaScriptAsync(RequiredString(args, "script"))),
                "browser_wait" => Format(await _automation.WaitAsync(GetInt(args, "ms") ?? throw new ArgumentException("缺少必需参数: ms"))),
                "browser_wait_for" => Format(await _automation.WaitForTextAsync(
                    RequiredString(args, "text"), GetInt(args, "timeout_ms") ?? 10_000)),
                "browser_fill_form" => Format(await _automation.FillFormAsync(RequiredStringDictionary(args, "fields"))),
                "browser_switch_tab" => SwitchTab(RequiredString(args, "tab_id")),
                "browser_click_by_hash" => Format(await _automation.ClickByStableHashAsync(RequiredString(args, "stable_hash"))),
                "browser_click_at" => Format(await _automation.ClickAtAsync(
                    GetInt(args, "x") ?? throw new ArgumentException("缺少必需参数: x"),
                    GetInt(args, "y") ?? throw new ArgumentException("缺少必需参数: y"))),

                _ => Error($"工具 '{toolName}' 未注册")
            };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private string SwitchTab(string tabId)
    {
        Logger.Debug($"[SwitchTab] tabId={tabId}");
        if (!Guid.TryParse(tabId, out var guid))
            return Error($"tab_id 不是有效 Guid: {tabId}");

        try
        {
            _automation.SwitchToTab(guid);
            return JsonSerializer.Serialize(new
            {
                ok = true,
                data = $"已切换到标签 {guid}",
                url = _automation.CurrentUrl,
                ms = 0
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    /// <summary>快照保存到本地 JSON，返回摘要信息</summary>
    private async Task<string> SnapshotWithSaveAsync(Dictionary<string, object?> args)
    {
        var conversationId = GetString(args, "conversation_id") ?? _automation.CurrentSnapshotConversationId ?? "default";
        Logger.Debug($"[SnapshotWithSaveAsync] conversationId={conversationId}");
        try
        {
            var (filePath, elementCount, url, title) = await _automation.SaveSnapshotToJsonAsync(conversationId);
            return JsonSerializer.Serialize(new
            {
                ok = true,
                data = new
                {
                    saved_to = filePath,
                    elementCount,
                    url = url ?? _automation.CurrentUrl ?? "",
                    title = title ?? "",
                    message = $"快照已保存到本地文件（{elementCount} 个元素）。请使用 browser_find_element 查询具体元素。"
                },
                error = (string?)null,
                url = url ?? _automation.CurrentUrl ?? "",
                ms = 0
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                data = (object?)null,
                error = $"快照保存失败: {ex.Message}",
                url = _automation.CurrentUrl,
                ms = 0
            }, JsonOptions);
        }
    }

    /// <summary>从本地快照文件中查找匹配元素</summary>
    private string FindElementAsync(Dictionary<string, object?> args)
    {
        var query = GetString(args, "query");
        var tag = GetString(args, "tag");
        Logger.Debug($"[FindElementAsync] query='{query}', tag='{tag}'");
        var idsRaw = args.GetValueOrDefault("ids");

        int[]? ids = null;
        if (idsRaw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            ids = je.EnumerateArray().Select(e => e.GetInt32()).ToArray();
        }
        else if (idsRaw is IEnumerable<int> intList)
        {
            ids = intList.ToArray();
        }

        // 优先使用当前快照文件
        var snapshotPath = _automation.CurrentSnapshotPath;
        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            // 尝试从 args 中获取显式路径
            snapshotPath = GetString(args, "path");
        }

        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "当前没有可用的快照文件。请先调用 browser_snapshot 获取页面快照。"
            }, JsonOptions);
        }

        var snapshotJson = _automation.LoadSnapshotFromJson(snapshotPath);
        if (snapshotJson == null)
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "读取快照文件失败"
            }, JsonOptions);
        }

        var result = _automation.FindElementsInSnapshot(snapshotJson, query, tag, ids);
        return result;
    }

    /// <summary>查看快照元信息</summary>
    private string SnapshotInfo(Dictionary<string, object?> args)
    {
        Logger.Debug("[SnapshotInfo] 查看快照元信息");
        var snapshotPath = _automation.CurrentSnapshotPath;
        var explicitPath = GetString(args, "path");
        if (!string.IsNullOrWhiteSpace(explicitPath))
            snapshotPath = explicitPath;

        if (string.IsNullOrWhiteSpace(snapshotPath) || !File.Exists(snapshotPath))
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "当前没有可用的快照文件。请先调用 browser_snapshot 获取页面快照。"
            }, JsonOptions);
        }

        var content = File.ReadAllText(snapshotPath);
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            return JsonSerializer.Serialize(new
            {
                ok = true,
                data = new
                {
                    file = snapshotPath,
                    fileSize = content.Length,
                    url = GetJsonString(root, "url"),
                    title = GetJsonString(root, "title"),
                    elementCount = GetJsonInt(root, "elementCount") ?? 0,
                    snapshotAt = GetJsonString(root, "snapshotAt"),
                    truncated = GetJsonBool(root, "truncated", false)
                }
            }, JsonOptions);
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                ok = false,
                error = "解析快照文件失败"
            }, JsonOptions);
        }
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText();
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
        return prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value) ? value : null;
    }

    private static bool GetJsonBool(JsonElement element, string propertyName, bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return defaultValue;
        return prop.ValueKind == JsonValueKind.True;
    }

    private static ToolDefinition Tool(
        string name,
        string description,
        Dictionary<string, object?>? parameters = null,
        params string[] required)
        => new()
        {
            Name = name,
            Description = description,
            Parameters = parameters ?? new Dictionary<string, object?>(),
            Required = required.ToList()
        };

    private static Dictionary<string, object?> StringParam(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static Dictionary<string, object?> IntParam(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description
    };

    private static Dictionary<string, object?> BoolParam(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description
    };

    private async Task<string> ScreenshotWithReasonAsync(string reason)
    {
        Logger.Debug($"[ScreenshotWithReasonAsync] reason='{reason.Truncate(80)}'");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 8)
            return Error("browser_screenshot 需要明确 reason。常规页面读取/搜索结果提取请优先使用 observe_browser 或 browser_snapshot；只有用户要求视觉确认或结构化读取失败时才截图。");

        var normalized = reason.ToLowerInvariant();
        var allowed = normalized.Contains("视觉")
                      || normalized.Contains("截图")
                      || normalized.Contains("screenshot")
                      || normalized.Contains("visual")
                      || normalized.Contains("observe_browser")
                      || normalized.Contains("browser_snapshot")
                      || normalized.Contains("失败")
                      || normalized.Contains("不可用")
                      || normalized.Contains("确认");

        if (!allowed)
            return Error("browser_screenshot 被拒绝：reason 未说明为什么必须视觉确认。请先使用 observe_browser/browser_snapshot；只有结构化读取失败或用户明确要求截图时再调用。原因: " + reason);

        return FormatScreenshot(await _automation.TakeScreenshotAsync());
    }

    /// <summary>导航带连续失败检测：3 次失败后注入 fatal 信号终止 AI 请求</summary>
    private async Task<string> NavigateWithFailureDetection(Dictionary<string, object?> args)
    {
        var url = RequiredString(args, "url");
        var timeout = GetInt(args, "timeout_ms") ?? 10_000;
        Logger.Debug($"[NavigateWithFailureDetection] url={url}, timeout={timeout}ms, 连续失败={_consecutiveNavFailures}");
        var result = await _automation.NavigateAsync(url, timeout);

        if (!result.IsSuccess)
        {
            _consecutiveNavFailures++;
            Logger.Warning($"[Automation] 导航连续失败（第 {_consecutiveNavFailures} 次）：{result.ErrorMessage}");

            // 连续失败 3 次 → 注入 fatal 信号，让 AI 终止任务
            if (_consecutiveNavFailures >= 3)
            {
                Logger.Warning($"[Automation] 导航连续失败 3 次，注入 fatal 终止信号: {url}");
                return JsonSerializer.Serialize(new
                {
                    ok = false,
                    data = (string?)null,
                    error = $"导航连续失败 3 次已终止任务: {url}",
                    url = url,
                    ms = result.ElapsedMs,
                    fatal = "navigation_loop_terminated"
                }, JsonOptions);
            }
        }
        else
        {
            _consecutiveNavFailures = 0;  // 成功 → 重置计数器
        }

        return Format(result);
    }

    private static string Format(AutomationResult result)
        => JsonSerializer.Serialize(new
        {
            ok = result.IsSuccess,
            data = result.IsSuccess ? result.Data : null,
            error = result.IsSuccess ? null : result.ErrorMessage,
            url = result.CurrentUrl,
            ms = result.ElapsedMs
        }, JsonOptions);

    private static string FormatScreenshot(AutomationResult result)
        => JsonSerializer.Serialize(new
        {
            ok = result.IsSuccess,
            data = result.IsSuccess
                ? $"截图已完成，PNG base64 长度 {result.Data?.Length ?? 0} 字符。为避免污染 LLM 上下文，未返回完整 base64；如需人工查看，请在应用界面观察当前页面，或后续改为保存到临时文件。"
                : null,
            error = result.IsSuccess ? null : result.ErrorMessage,
            url = result.CurrentUrl,
            ms = result.ElapsedMs
        }, JsonOptions);

    private static string Error(string message)
        => JsonSerializer.Serialize(new
        {
            ok = false,
            error = message,
            url = (string?)null,
            ms = 0
        }, JsonOptions);

    private static int RequiredElementId(Dictionary<string, object?> args)
        => GetInt(args, "element_id")
           ?? GetInt(args, "id")
           ?? GetInt(args, "element")
           ?? throw new ArgumentException("缺少必需参数: element_id。请先调用 browser_snapshot，并使用 elements[*].id 整数。不要使用 xp= hash 或 CSS 选择器。");

    private static string RequiredString(Dictionary<string, object?> args, string key)
        => GetString(args, key) ?? throw new ArgumentException($"缺少必需参数: {key}");

    private static string? GetString(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null) return null;
        if (value is string s) return s;
        if (value is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.String => je.GetString(),
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => je.GetRawText()
            };
        }
        return value.ToString();
    }

    private static int? GetInt(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null) return null;

        if (value is int i) return i;
        if (value is long l) return checked((int)l);
        if (value is double d) return (int)d;
        if (value is float f) return (int)f;
        if (value is decimal m) return (int)m;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n)) return n;
            if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var sn)) return sn;
            return null;
        }

        return int.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool? GetBool(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null) return null;
        if (value is bool b) return b;
        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var sb)) return sb;
            return null;
        }
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static Dictionary<string, string> RequiredStringDictionary(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null)
            throw new ArgumentException($"缺少必需参数: {key}");

        if (value is Dictionary<string, string> stringDict) return stringDict;
        if (value is Dictionary<string, object?> objectDict)
            return objectDict.ToDictionary(kv => kv.Key, kv => CoerceToString(kv.Value));

        if (value is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, string>();
            foreach (var prop in je.EnumerateObject())
                result[prop.Name] = CoerceJsonElementToString(prop.Value);
            return result;
        }

        throw new ArgumentException($"参数 {key} 必须是对象，例如 {{\"3\": \"alice\"}}");
    }

    private static string CoerceToString(object? value)
    {
        if (value == null) return string.Empty;
        if (value is string s) return s;
        if (value is JsonElement je) return CoerceJsonElementToString(je);
        return value.ToString() ?? string.Empty;
    }

    private static string CoerceJsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.GetRawText()
        };
    }
}
