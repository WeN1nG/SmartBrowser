using System.Text.Json;
using BrowserDemo.Models;

namespace BrowserDemo.Services.Automation;

/// <summary>
/// AI 浏览器工具路由器 —— 将 function calling 的 browser_* 工具调用转发到 WebView2 自动化服务。
/// 这里负责 AI 侧的工具 schema、参数容错解析、返回值 JSON 化；实际浏览器操作由 BrowserAutomationService 完成。
/// </summary>
public class BrowserAutomationToolRouter
{
    private readonly BrowserAutomationService _automation;

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
        _automation = automation ?? throw new ArgumentNullException(nameof(automation));
    }

    public bool IsToolRegistered(string toolName) => _automation.IsToolRegistered(toolName);

    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => new List<ToolDefinition>
    {
        Tool("browser_navigate", "[浏览器] 打开指定 URL，并等待页面导航完成。", new()
        {
            ["url"] = StringParam("目标 URL，例如 https://www.bing.com"),
            ["timeout_ms"] = IntParam("导航超时时间，毫秒，默认 30000")
        }, "url"),

        Tool("browser_back", "[浏览器] 后退到浏览器历史中的上一页。"),
        Tool("browser_forward", "[浏览器] 前进到浏览器历史中的下一页。"),
        Tool("browser_reload", "[浏览器] 刷新当前页面。"),

        Tool("browser_snapshot", "[浏览器] 获取当前页面的结构化快照。返回的 elements[*].id 是后续 browser_click/browser_type/browser_hover/browser_select_option 的 element_id。不要使用 xp= hash 或 CSS 选择器定位。"),

        Tool("browser_click", "[浏览器] 点击页面元素。必须先调用 browser_snapshot，并使用快照中元素的整数 id 作为 element_id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数")
        }, "element_id"),

        Tool("browser_type", "[浏览器] 在页面输入框或可编辑元素中输入文本。必须使用 browser_snapshot 返回的整数 element_id。", new()
        {
            ["element_id"] = IntParam("browser_snapshot 返回的 elements[*].id 整数"),
            ["text"] = StringParam("要输入的文本"),
            ["clear_first"] = BoolParam("输入前是否先清空原内容，默认 true")
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
        }, "tab_id")
    };

    public async Task<string> InvokeAsync(string toolName, Dictionary<string, object?>? args)
    {
        args ??= new Dictionary<string, object?>();

        try
        {
            return toolName switch
            {
                "browser_navigate" => Format(await _automation.NavigateAsync(
                    RequiredString(args, "url"), GetInt(args, "timeout_ms") ?? 30_000)),

                "browser_back" => Format(await _automation.GoBackAsync()),
                "browser_forward" => Format(await _automation.GoForwardAsync()),
                "browser_reload" => Format(await _automation.ReloadAsync()),
                "browser_snapshot" => Format(await _automation.GetSnapshotAsync()),

                "browser_click" => Format(await _automation.ClickAsync(RequiredElementId(args))),
                "browser_type" => Format(await _automation.TypeAsync(
                    RequiredElementId(args), RequiredString(args, "text"), GetBool(args, "clear_first") ?? true)),
                "browser_hover" => Format(await _automation.HoverAsync(RequiredElementId(args))),
                "browser_select_option" => Format(await _automation.SelectOptionAsync(
                    RequiredElementId(args), RequiredString(args, "value"))),

                "browser_scroll" => Format(await _automation.ScrollAsync(
                    GetInt(args, "delta_x") ?? 0, GetInt(args, "delta_y") ?? 300)),

                "browser_press_key" => Format(await _automation.PressKeyAsync(RequiredString(args, "key"))),
                "browser_screenshot" => await ScreenshotWithReasonAsync(RequiredString(args, "reason")),
                "browser_js" => Format(await _automation.EvaluateJavaScriptAsync(RequiredString(args, "script"))),
                "browser_wait" => Format(await _automation.WaitAsync(GetInt(args, "ms") ?? throw new ArgumentException("缺少必需参数: ms"))),
                "browser_wait_for" => Format(await _automation.WaitForTextAsync(
                    RequiredString(args, "text"), GetInt(args, "timeout_ms") ?? 10_000)),
                "browser_fill_form" => Format(await _automation.FillFormAsync(RequiredStringDictionary(args, "fields"))),
                "browser_switch_tab" => SwitchTab(RequiredString(args, "tab_id")),

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
