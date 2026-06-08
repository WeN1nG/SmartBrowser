#if false
// [已弃用] 旧的 WebView2 自动化桥接 — 已由 Playwright MCP 取代

using System.Text.Json;

using System.Windows;

using BrowserDemo.Models;

using BrowserDemo.Services.Skills;

using BrowserDemo.ViewModels;

using Microsoft.Web.WebView2.Core;

using Microsoft.Web.WebView2.Wpf;

namespace BrowserDemo.Services.Automation;

/// <summary>

/// WebView2 自动化桥接器 —— 将所有基础技能映射到真实的 WebView2 浏览器操作。

/// 替代之前的模拟执行模式，让 AI 真正控制浏览器。

/// </summary>

public class WebView2AutomationBridge

{

    private readonly Func<WebView2?> _getCurrentWebView;

    private readonly BrowserViewModel _browserVm;

    private readonly Lazy<AdbService> _adb = new(() => new AdbService());

    public WebView2AutomationBridge(

        Func<WebView2?> getCurrentWebView,

        BrowserViewModel browserVm)

    {

        _getCurrentWebView = getCurrentWebView;

        _browserVm = browserVm;

    }

    /// <summary>向 SkillExecutor 注册所有 15 个基础技能的执行器</summary>

    public void RegisterAll(SkillExecutor executor)

    {

        executor.RegisterBasicExecutor("skill_navigate", ExecuteNavigate);

        executor.RegisterBasicExecutor("skill_click", ExecuteClick);

        executor.RegisterBasicExecutor("skill_type", ExecuteType);

        executor.RegisterBasicExecutor("skill_select", ExecuteSelect);

        executor.RegisterBasicExecutor("skill_scroll", ExecuteScroll);

        executor.RegisterBasicExecutor("skill_extract", ExecuteExtract);

        executor.RegisterBasicExecutor("skill_screenshot", ExecuteScreenshot);

        executor.RegisterBasicExecutor("skill_wait", ExecuteWait);

        executor.RegisterBasicExecutor("skill_tab", ExecuteTab);

        executor.RegisterBasicExecutor("skill_cookie", ExecuteCookie);

        executor.RegisterBasicExecutor("skill_form", ExecuteForm);

        executor.RegisterBasicExecutor("skill_hover", ExecuteHover);

        executor.RegisterBasicExecutor("skill_query", ExecuteQuery);

        executor.RegisterBasicExecutor("skill_js", ExecuteJs);

        executor.RegisterBasicExecutor("skill_adb_sms", ExecuteAdbSms);

        Logger.Info("WebView2AutomationBridge: 已注册 15 个基础技能的真实执行器");

    }

    // ====================================================================

    // 辅助方法

    // ====================================================================

    /// <summary>获取当前 WebView2（自动调度到 UI 线程）</summary>

    private async Task<WebView2?> GetWebViewAsync()

    {

        if (Application.Current.Dispatcher.CheckAccess())

            return _getCurrentWebView();

        return await Application.Current.Dispatcher.InvokeAsync(_getCurrentWebView).Task;

    }

    /// <summary>在 UI 线程上执行 WebView2 操作</summary>

    private async Task<T> OnUiThreadAsync<T>(Func<WebView2, T> action, T fallback = default!)

    {

        if (Application.Current.Dispatcher.CheckAccess())

        {

            var wv = _getCurrentWebView();

            return wv?.CoreWebView2 != null ? action(wv) : fallback;

        }

        return await Application.Current.Dispatcher.InvokeAsync(() =>

        {

            var wv = _getCurrentWebView();

            return wv?.CoreWebView2 != null ? action(wv) : fallback;

        }).Task;

    }

    /// <summary>在 UI 线程上执行异步 WebView2 操作</summary>

    private async Task<T> OnUiThreadAsync<T>(Func<WebView2, Task<T>> action, T fallback = default!)

    {

        if (Application.Current.Dispatcher.CheckAccess())

        {

            var wv = _getCurrentWebView();

            return wv?.CoreWebView2 != null ? await action(wv) : fallback;

        }

        return await Application.Current.Dispatcher.InvokeAsync(async () =>

        {

            var wv = _getCurrentWebView();

            return wv?.CoreWebView2 != null ? await action(wv) : fallback;

        }).Task.Unwrap();

    }

    /// <summary>在 UI 线程上执行 void WebView2 操作</summary>

    private async Task OnUiThreadAsync(Action<WebView2> action)

    {

        if (Application.Current.Dispatcher.CheckAccess())

        {

            var wv = _getCurrentWebView();

            if (wv != null) action(wv);

            return;

        }

        await Application.Current.Dispatcher.InvokeAsync(() =>

        {

            var wv = _getCurrentWebView();

            if (wv != null) action(wv);

        }).Task;

    }

    /// <summary>执行 JavaScript 并返回解码后的结果</summary>

    private async Task<string> ExecuteJsAsync(string script)

    {

        return await OnUiThreadAsync(async wv =>

        {

            var jsonResult = await wv.CoreWebView2!.ExecuteScriptAsync(script);

            return DecodeJsResult(jsonResult);

        }, "null");

    }

    /// <summary>解码 ExecuteScriptAsync 返回的 JSON 字符串</summary>

    private static string DecodeJsResult(string json)

    {

        if (string.IsNullOrEmpty(json) || json == "null" || json == "\"\"") return "";

        try

        {

            using var doc = JsonDocument.Parse(json);

            return doc.RootElement.ValueKind switch

            {

                JsonValueKind.String => doc.RootElement.GetString() ?? "",

                JsonValueKind.True => "true",

                JsonValueKind.False => "false",

                JsonValueKind.Number => doc.RootElement.GetRawText(),

                _ => json

            };

        }

        catch

        {

            return json;

        }

    }

    /// <summary>从参数字典中解析选择器（同时支持 selector/css_selector/element/id/target 等常见键名）</summary>

    private static string GetSelector(Dictionary<string, object?> subParams, Dictionary<string, object?> parameters)

    {

        // 优先匹配已知参数名（按常用顺序排列）

        foreach (var key in new[] { "selector", "css_selector", "element", "target", "id", "query", "css" })

        {

            var val = GetParam<string>(subParams, key)

                ?? GetParam<string>(parameters, key);

            if (!string.IsNullOrWhiteSpace(val)) return val;

        }

        // 模糊推断：在所有参数值中查找看起来像 CSS 选择器的字符串

        foreach (var dict in new[] { subParams, parameters })

        {

            foreach (var (key, val) in dict)

            {

                if (val is string s && !string.IsNullOrWhiteSpace(s)

                    && (s.StartsWith("#") || s.StartsWith(".") || s.StartsWith("[") || s.Contains(">")))

                {

                    // 排除明显不是选择器的值（如 URL、长文本）

                    if (!s.StartsWith("http") && s.Length < 500)

                        return s;

                }

            }

        }

        return "";

    }

    /// <summary>从参数字典中提取指定键的值</summary>

    private static T? GetParam<T>(Dictionary<string, object?> parameters, string key)

    {

        if (parameters.TryGetValue(key, out var val) && val != null)

        {

            if (val is T t) return t;

            if (val is JsonElement je)

            {

                try

                {

                    // JsonElement -> string

                    if (typeof(T) == typeof(string) && je.ValueKind == JsonValueKind.String)

                        return (T)(object)je.GetString()!;

                    // JsonElement -> Dictionary

                    if (typeof(T) == typeof(Dictionary<string, object?>) && je.ValueKind == JsonValueKind.Object)

                    {

                        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());

                        return (T)(object?)dict!;

                    }

                    // 直接反序列化

                    var result = JsonSerializer.Deserialize<T>(je.GetRawText(), JsonOptions);

                    return result;

                }

                catch { }

            }

        }

        return default;

    }

    /// <summary>从参数字典中获取 action 字符串（未找到时返回 null）</summary>

    private static string? GetAction(Dictionary<string, object?> parameters)

    {

        return GetParam<string>(parameters, "action");

    }

    /// <summary>获取 params 子字典（AI 发送的嵌套参数）</summary>

    private static Dictionary<string, object?> GetSubParams(Dictionary<string, object?> parameters)

    {

        var sub = GetParam<Dictionary<string, object?>>(parameters, "params");

        return sub ?? new Dictionary<string, object?>();

    }

    /// <summary>创建成功结果</summary>

    private static SkillExecutionResult Success(string summary, Dictionary<string, object?>? outputs = null)

    {

        return new SkillExecutionResult

        {

            Status = SkillStatus.Succeeded,

            Summary = summary,

            Outputs = outputs ?? new Dictionary<string, object?>()

        };

    }

    /// <summary>创建失败结果</summary>

    private static SkillExecutionResult Fail(string error)

    {

        return new SkillExecutionResult

        {

            Status = SkillStatus.Failed,

            ErrorMessage = error

        };

    }

    /// <summary>检查 JS 执行结果是否包含错误。如果 JSON 中有 "error" 键，返回错误消息；否则返回 null。</summary>

    private static string? GetJsError(string result)

    {

        if (string.IsNullOrEmpty(result) || result == "null" || result == "\"\"") return null;

        try

        {

            using var doc = JsonDocument.Parse(result);

            if (doc.RootElement.TryGetProperty("error", out var err))

                return err.GetString();

        }

        catch { }

        return null;

    }

    // ====================================================================

    // 选择器安全处理 —— 检测 Playwright 非标准 CSS 并降级

    // ====================================================================

    /// <summary>文本查找时扫描的候选元素选择器</summary>

    private const string TextCandidatesSelector =

        "a, button, span, li, div, input, label, h1, h2, h3, h4, h5, h6, p, " +

        "[role=button], [role=link], [role=menuitem], [role=tab], [role=option], " +

        ".menu-list-ul li, .nav li, .head-nav li, .el-menu-item, .ant-menu-item";

    /// <summary>

    /// Playwright 引擎前缀模式列表。检测到即返回明确错误，防止 querySelector 抛 DOMException。

    /// </summary>

    private static readonly (string Prefix, string Hint)[] PlaywrightEnginePrefixes = {

        ("xpath=", "请使用 CSS 选择器代替 XPath"),

        ("text=", "请使用 selector 或 text_content 参数代替 'text=' 前缀"),

        ("pi=", "Playwright 管道选择器 (pi=) 不支持"),

        ("react=", "React 选择器不支持"),

        ("id=", "请使用 CSS ID 选择器 '#id' 代替"),

        ("data-testid=", "请使用 CSS 属性选择器 '[data-testid=value]'"),

    };

    /// <summary>

    /// 从伪选择器中提取目标文本。

    /// 支持 :has-text('xxx')、:has-text("xxx")、:contains('xxx')、:contains("xxx")。

    /// </summary>

    private static string? ExtractPseudoText(string selector, string pseudoClass)

    {

        var idx = selector.IndexOf(pseudoClass, StringComparison.OrdinalIgnoreCase);

        if (idx < 0) return null;

        var start = idx + pseudoClass.Length;

        if (start >= selector.Length) return null;

        var quote = selector[start];

        if (quote != '\'' && quote != '"') return null;

        start++;

        var end = selector.IndexOf(quote, start);

        if (end < 0) return null;

        return selector[start..end];

    }

    /// <summary>

    /// 验证并适配 CSS 选择器。返回 (IsValid, ErrorHint, CleanSelector, TextFallback)：

    /// - IsValid=false：选择器有不可恢复的语法错误 → ErrorHint 告知 AI

    /// - CleanSelector：移除已知非标准伪类后的安全选择器

    /// - TextFallback：当选择器含 :has-text/:contains 时提取的文本（后续用文本查找）

    /// </summary>

    private static (bool IsValid, string? ErrorHint, string? CleanSelector, string? TextFallback) ValidateSelector(string selector)

    {

        if (string.IsNullOrWhiteSpace(selector))

            return (false, "选择器为空", null, null);

        var s = selector.Trim();

        // 1. 检测 Playwright 引擎前缀

        foreach (var (prefix, hint) in PlaywrightEnginePrefixes)

        {

            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

                return (false, $"不支持的 Playwright 选择器前缀 '{prefix}'。{hint}", null, null);

        }

        // 2. 检测 "css=" 前缀 — 可安全去除

        if (s.StartsWith("css=", StringComparison.OrdinalIgnoreCase))

            s = s[4..].TrimStart();

        // 3. 检测 ">>" 链式操作符

        if (s.Contains(">>", StringComparison.OrdinalIgnoreCase))

            return (false, "不支持的 Playwright 链式操作符 '>>'。请使用标准 CSS 组合符如 ' '、'>'、'+'", null, null);

        // 4. 检测 :has-text() 和 :contains() — 提取文本做降级

        var pseudoText = ExtractPseudoText(s, ":has-text(") ?? ExtractPseudoText(s, ":contains(");

        if (pseudoText != null)

        {

            // 把 :has-text 从选择器中移除（如果只剩下标签/类名，就不用 cleanSelector 了）

            var clean = RemovePseudoClass(s, ":has-text") ?? RemovePseudoClass(s, ":contains") ?? s;

            return (true, null, clean, pseudoText);

        }

        // 5. 去除 :visible（非标准 CSS，可安全忽略）

        var noVisible = RemovePseudoClass(s, ":visible") ?? s;

        return (true, null, noVisible, null);

    }

    /// <summary>从 CSS 选择器中移除指定伪类（及其参数）</summary>

    private static string? RemovePseudoClass(string selector, string pseudoClass)

    {

        var idx = selector.IndexOf(pseudoClass, StringComparison.OrdinalIgnoreCase);

        if (idx < 0) return null;

        // 找到伪类结束位置（括号匹配）

        var end = idx + pseudoClass.Length;

        if (end < selector.Length && selector[end] == '(')

        {

            var depth = 1;

            end++;

            while (end < selector.Length && depth > 0)

            {

                if (selector[end] == '(') depth++;

                else if (selector[end] == ')') depth--;

                end++;

            }

        }

        return selector[..idx] + selector[end..];

    }

    /// <summary>生成安全的 JS IIFE：查找元素 → 执行操作（含 try-catch 和 Playwright 降级）</summary>

    /// <param name="selector">CSS 选择器（含可能的 Playwright 非标准语法）</param>

    /// <param name="actionBody">找到元素后执行的 JS 代码（用 el 变量引用元素）</param>

    /// <param name="forceSelector">强制使用标准 querySelector 而非文本降级</param>

    private static string BuildSafeElementJs(string selector, string actionBody, bool forceSelector = false)

    {

        var (isValid, errorHint, cleanSelector, textFallback) = ValidateSelector(selector);

        if (!isValid)

        {

            var err = JsonSerializer.Serialize(errorHint ?? "选择器无效");

            return $"(function(){{ return JSON.stringify({{error: {err}}});}})()";

        }

        // 含 :has-text/:contains 且未强制用 querySelector → 文本降级

        if (textFallback != null && !forceSelector)

        {

            return BuildTextFindJs(textFallback, TextCandidatesSelector, actionBody);

        }

        // 标准 CSS 选择器 + try-catch

        var safeSelector = cleanSelector ?? selector;

        var escaped = JsonSerializer.Serialize(safeSelector);

        return $@"(function() {{

    try {{

        var el = document.querySelector({escaped});

        if (!el) return JSON.stringify({{error: 'CSS 选择器未匹配到元素: {JsonSerializer.Serialize(safeSelector)}'}});

        {actionBody}

    }} catch(e) {{

        return JSON.stringify({{error: 'CSS 选择器语法错误: ' + e.message + ' | {JsonSerializer.Serialize(safeSelector)}'}});

    }}

}})()";

    }

    /// <summary>生成安全的 JS IIFE：查找全部元素（用于 querySelectorAll）</summary>

    private static string BuildSafeElementAllJs(string selector, string actionBody)

    {

        var safe = selector;

        // 检测 Playwright 引擎前缀

        var trimmed = selector.TrimStart();

        foreach (var (prefix, hint) in PlaywrightEnginePrefixes)

        {

            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

            {

                var err = JsonSerializer.Serialize($"不支持的 Playwright 选择器前缀 '{prefix}'。{hint}");

                return $"(function(){{ return JSON.stringify({{error: {err}}});}})()";

            }

        }

        if (trimmed.StartsWith("css=", StringComparison.OrdinalIgnoreCase))

            safe = trimmed[4..].TrimStart();

        if (safe.Contains(">>", StringComparison.OrdinalIgnoreCase))

        {

            var err = JsonSerializer.Serialize("不支持的 Playwright 链式操作符 '>>'");

            return $"(function(){{ return JSON.stringify({{error: {err}}});}})()";

        }

        var pseudoText = ExtractPseudoText(safe, ":has-text(") ?? ExtractPseudoText(safe, ":contains(");

        if (pseudoText != null)

            return BuildTextFindJs(pseudoText, TextCandidatesSelector, actionBody);

        safe = RemovePseudoClass(safe, ":visible") ?? safe;

        var escaped = JsonSerializer.Serialize(safe);

        return $@"(function() {{

    try {{

        var els = document.querySelectorAll({escaped});

        if (!els || els.length === 0) return JSON.stringify({{error: 'querySelectorAll 未匹配到元素: {JsonSerializer.Serialize(safe)}'}});

        {actionBody}

    }} catch(e) {{

        return JSON.stringify({{error: 'CSS 选择器语法错误: ' + e.message + ' | {JsonSerializer.Serialize(safe)}'}});

    }}

}})()";

    }

    /// <summary>生成通过文本查找元素并执行操作的 JS 代码</summary>

    private static string BuildTextFindJs(string text, string candidatesSelector, string actionBody)

    {

        var escapedText = JsonSerializer.Serialize(text);

        return $@"(function() {{

    var targetText = {escapedText};

    var candidates = document.querySelectorAll({JsonSerializer.Serialize(candidatesSelector)});

    for (var i = 0; i < candidates.length; i++) {{

        var t = (candidates[i].innerText || '').trim();

        if (t === targetText) {{

            var el = candidates[i];

            {actionBody}

        }}

    }}

    for (var i = 0; i < candidates.length; i++) {{

        var t = (candidates[i].innerText || '').trim();

        if (t.indexOf(targetText) !== -1) {{

            var el = candidates[i];

            {actionBody}

        }}

    }}

    return JSON.stringify({{error: '按文本查找元素失败: ' + targetText}});

}})()";

    }

    /// <summary>生成仅检查元素存在性的 JS（用于 wait_for_element）</summary>

    private static string BuildElementExistsJs(string selector)

    {

        var (isValid, errorHint, cleanSelector, textFallback) = ValidateSelector(selector);

        if (!isValid)

        {

            var err = JsonSerializer.Serialize(errorHint ?? "选择器无效");

            return $"(function(){{ return JSON.stringify({{error: {err}}});}})()";

        }

        // 含 :has-text / :contains → 改为文本存在性检查

        if (textFallback != null)

        {

            var escapedText = JsonSerializer.Serialize(textFallback);

            return $"(function(){{ return (document.body?.innerText || '').indexOf({escapedText}) !== -1 ? 'true' : 'false';}})()";

        }

        var safe = cleanSelector ?? selector;

        var escaped = JsonSerializer.Serialize(safe);

        return $@"(function() {{

    try {{

        return document.querySelector({escaped}) !== null ? 'true' : 'false';

    }} catch(e) {{

        return JSON.stringify({{error: 'CSS 选择器语法错误: ' + e.message + ' | {JsonSerializer.Serialize(safe)}'}});

    }}

}})()";

    }

    private static readonly JsonSerializerOptions JsonOptions = new()

    {

        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower

    };

    // ====================================================================

    // 1. skill_navigate — 导航漫游

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteNavigate(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "navigate";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "navigate":

                var url = GetParam<string>(subParams, "url")

                    ?? GetParam<string>(parameters, "url")

                    ?? "about:blank";

                // 补全协议

                if (!url.Contains("://") && !url.StartsWith("about:"))

                    url = "https://" + url;

                Logger.Info($"导航到: {url}");

                await OnUiThreadAsync(wv => wv.CoreWebView2!.Navigate(url));

                // 等待导航完成：轮询 document.readyState 直到 complete 或超时

                var navTimeoutMs = 15000;

                var waited = 0;

                var checkInterval = 200;

                string finalUrl = url;

                while (waited < navTimeoutMs)

                {

                    ct.ThrowIfCancellationRequested();

                    var state = await ExecuteJsAsync("document.readyState");

                    if (state == "complete")

                    {

                        // 导航完成，获取当前 URL

                        finalUrl = await ExecuteJsAsync("document.location.href") ?? url;

                        break;

                    }

                    await Task.Delay(checkInterval, ct);

                    waited += checkInterval;

                }

                // 检查导航是否被重定向到错误页或登录页

                var pageTitle = await ExecuteJsAsync("document.title") ?? "";

                var pageText = await ExecuteJsAsync("(document.body?.innerText || '').substring(0, 200)") ?? "";

                // 检查是否因登录弹窗/重定向导致的导航失败

                if (pageTitle.Contains("login", StringComparison.OrdinalIgnoreCase) ||

                    pageTitle.Contains("signin", StringComparison.OrdinalIgnoreCase) ||

                    pageTitle.Contains("登录", StringComparison.OrdinalIgnoreCase) ||

                    pageTitle.Contains("Microsoft 帐户", StringComparison.OrdinalIgnoreCase) ||

                    finalUrl.Contains("login.", StringComparison.OrdinalIgnoreCase))

                {

                    Logger.Warning($"导航被重定向到登录页: {finalUrl}");

                    return Fail($"导航被重定向到登录页，需要用户登录: {pageTitle}");

                }

                // 检查是否导航到 404 或错误页

                var errorIndicators = new[] { "404", "not found", "无法访问", "页面不存在", "页面找不到",

                    "error", "ERR_", "Connection reset", "已重置" };

                var combinedText = $"{pageTitle} {pageText}".ToLowerInvariant();

                foreach (var indicator in errorIndicators)

                {

                    if (combinedText.Contains(indicator.ToLowerInvariant()))

                    {

                        Logger.Warning($"导航失败: 页面包含错误指示符 '{indicator}' — URL={finalUrl}");

                        return Fail($"导航失败: 页面返回错误 — \"{pageTitle}\" (URL={finalUrl})");

                    }

                }

                Logger.Info($"导航完成: {finalUrl} (页面: {pageTitle})");

                return Success($"已导航到 {finalUrl}",

                    new() { ["url"] = finalUrl, ["title"] = pageTitle ?? "[加载中]" });

            case "go_back":

                Logger.Info("后退");

                await OnUiThreadAsync(wv => wv.CoreWebView2!.GoBack());

                await Task.Delay(300, ct);

                return Success("已后退到上一页");

            case "go_forward":

                Logger.Info("前进");

                await OnUiThreadAsync(wv => wv.CoreWebView2!.GoForward());

                await Task.Delay(300, ct);

                return Success("已前进到下一页");

            case "refresh":

                Logger.Info("刷新");

                await OnUiThreadAsync(wv => wv.CoreWebView2!.Reload());

                return Success("页面已刷新");

            case "stop":

                Logger.Info("停止加载");

                await OnUiThreadAsync(wv => wv.CoreWebView2!.Stop());

                return Success("页面加载已停止");

            default:

                return Fail($"未知导航操作: {action}");

        }

    }

    // ====================================================================

    // 2. skill_click — 点击交互

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteClick(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "click_element";

        var subParams = GetSubParams(parameters);

        var selector = GetSelector(subParams, parameters);

        string js;

        switch (action)

        {

            case "click_element":

                if (string.IsNullOrEmpty(selector))

                {

                    // 没有 selector 时尝试使用 text_content 参数

                    var textContent = GetParam<string>(subParams, "text_content")

                        ?? GetParam<string>(parameters, "text_content")

                        ?? GetParam<string>(subParams, "text")

                        ?? GetParam<string>(parameters, "text")

                        ?? "";

                    if (!string.IsNullOrEmpty(textContent))

                    {

                        js = BuildTextFindJs(textContent, TextCandidatesSelector, @"

                            el.click();

                            return JSON.stringify({success: true, tag: el.tagName, text: (el.textContent||'').trim().substring(0, 50)});

                        ");

                        break;

                    }

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，或使用 text_content 按文本查找");

                }

                // 统一通过 BuildSafeElementJs 处理（含 :has-text 降级、try-catch）

                js = BuildSafeElementJs(selector, @"

                    el.click();

                    return JSON.stringify({success: true, tag: el.tagName, text: (el.textContent||'').trim().substring(0, 50)});

                ");

                break;

            case "click_element_at":

                var x = GetParam<double?>(subParams, "x") ?? 0;

                var y = GetParam<double?>(subParams, "y") ?? 0;

                js = $@"(function() {{

                    try {{

                        var el = document.elementFromPoint({x}, {y});

                        if (!el) return JSON.stringify({{error: '坐标({x},{y})处无元素'}});

                        el.click();

                        return JSON.stringify({{success: true, tag: el.tagName, text: (el.textContent||'').trim().substring(0, 50)}});

                    }} catch(e) {{

                        return JSON.stringify({{error: 'JS 坐标点击异常: ' + e.message}});

                    }}

                }})()";

                break;

            default:

                return Fail($"未知点击操作: {action}");

        }

        var result = await ExecuteJsAsync(js);

        Logger.Info($"点击结果: {result}");

        var err = GetJsError(result);

        if (err != null) return Fail($"❌ 点击失败: {err}");

        if (string.IsNullOrWhiteSpace(result))

            return Fail("❌ 点击失败: JS 执行异常（选择器无效或 DOM 操作被拒绝）");

        return Success($"✅ 已点击元素: {result}");

    }

    // ====================================================================

    // 3. skill_type — 文本输入

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteType(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "type_text";

        var subParams = GetSubParams(parameters);

        var selector = GetSelector(subParams, parameters);

        var text = GetParam<string>(subParams, "text")

            ?? GetParam<string>(parameters, "text")

            ?? "";

        var key = GetParam<string>(subParams, "key")

            ?? GetParam<string>(parameters, "key")

            ?? "";

        switch (action)

        {

            case "type_text":

            case "type_text_at":

                if (string.IsNullOrEmpty(selector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器");

                var escapedText = JsonSerializer.Serialize(text);

                var js = BuildSafeElementJs(selector, $@"

                    el.focus();

                    el.value = {escapedText};

                    el.dispatchEvent(new Event('input', {{bubbles: true}}));

                    el.dispatchEvent(new Event('change', {{bubbles: true}}));

                    return JSON.stringify({{success: true, value: {escapedText}.substring(0, 50)}});

                ");

                var result = await ExecuteJsAsync(js);

                Logger.Info($"输入结果: {result}");

                var err = GetJsError(result);

                if (err != null) return Fail($"❌ 输入失败: {err}");

                var truncated = text.Length > 50 ? text[..50] + "..." : text;

                return Success($"✅ 已输入文本: \"{truncated}\"");

            case "key_press":

                // 特殊按键处理

                if (key.Equals("Enter", StringComparison.OrdinalIgnoreCase))

                {

                    // 聚焦当前 activeElement 并触发 Enter

                    var enterJs = @"(function() {

                        var el = document.activeElement;

                        if (!el) return JSON.stringify({error: '无焦点元素'});

                        el.dispatchEvent(new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter', bubbles: true}));

                        el.dispatchEvent(new KeyboardEvent('keypress', {key: 'Enter', code: 'Enter', bubbles: true}));

                        // 如果是在 form 中，尝试提交

                        var form = el.closest('form');

                        if (form) { form.dispatchEvent(new Event('submit', {bubbles: true})); return JSON.stringify({success: true, submitted: true}); }

                        el.dispatchEvent(new KeyboardEvent('keyup', {key: 'Enter', code: 'Enter', bubbles: true}));

                        return JSON.stringify({success: true, submitted: false});

                    })()";

                    var enterResult = await ExecuteJsAsync(enterJs);

                    Logger.Info($"按键 Enter 结果: {enterResult}");

                    var enterErr = GetJsError(enterResult);

                    if (enterErr != null) return Fail($"❌ 按键失败: {enterErr}");

                    return Success("✅ 已按下 Enter 键");

                }

                else if (key.Equals("Tab", StringComparison.OrdinalIgnoreCase))

                {

                    var tabJs = @"(function() {

                        var el = document.activeElement;

                        if (!el) return JSON.stringify({error: '无焦点元素'});

                        // 触发 Tab 键

                        el.dispatchEvent(new KeyboardEvent('keydown', {key: 'Tab', code: 'Tab', bubbles: true}));

                        // 尝试聚焦下一个可聚焦元素

                        var focusable = document.querySelectorAll('input, select, textarea, button, [tabindex]:not([tabindex=""-1""])');

                        var idx = Array.from(focusable).indexOf(el);

                        if (idx >= 0 && idx < focusable.length - 1) focusable[idx + 1].focus();

                        return JSON.stringify({success: true});

                    })()";

                    await ExecuteJsAsync(tabJs);

                    return Success("✅ 已按下 Tab 键");

                }

                else

                {

                    return Success($"✅ 按键 '{key}' 已模拟（非标准键）");

                }

            case "select_all":

                await ExecuteJsAsync("document.activeElement?.select()");

                return Success("✅ 已全选文本");

            case "copy":

                await ExecuteJsAsync(@"navigator.clipboard?.writeText(document.activeElement?.value || '')");

                return Success("✅ 已复制文本");

            case "paste":

                return Fail("粘贴操作需要用户交互权限，请手动 Ctrl+V");

            default:

                return Fail($"未知输入操作: {action}");

        }

    }

    // ====================================================================

    // 4. skill_select — 选项选择

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteSelect(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "select_option";

        var subParams = GetSubParams(parameters);

        var selector = GetSelector(subParams, parameters);

        var value = GetParam<string>(subParams, "value")

            ?? GetParam<string>(parameters, "value")

            ?? "";

        var label = GetParam<string>(subParams, "label")

            ?? GetParam<string>(parameters, "label")

            ?? "";

        var visibleText = GetParam<string>(subParams, "text")

            ?? GetParam<string>(parameters, "text")

            ?? "";

        switch (action)

        {

            case "select_option":

                if (string.IsNullOrEmpty(selector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，例如 {\"selector\":\".search-input\"} 或 {\"selector\":\"#submit-btn\"}");

                string selectJs;

                if (!string.IsNullOrEmpty(value))

                {

                    selectJs = BuildSafeElementJs(selector, $@"

                        el.value = {JsonSerializer.Serialize(value)};

                        el.dispatchEvent(new Event('change', {{bubbles: true}}));

                        return JSON.stringify({{success: true, value: {JsonSerializer.Serialize(value)}}});

                    ");

                }

                else if (!string.IsNullOrEmpty(visibleText))

                {

                    selectJs = BuildSafeElementJs(selector, $@"

                        var opts = Array.from(el.options);

                        var target = opts.find(o => o.text.includes({JsonSerializer.Serialize(visibleText)}));

                        if (!target) return JSON.stringify({{error: '未找到选项: ' + {JsonSerializer.Serialize(visibleText)}}});

                        el.value = target.value;

                        el.dispatchEvent(new Event('change', {{bubbles: true}}));

                        return JSON.stringify({{success: true, selected: target.text}});

                    ");

                }

                else

                {

                    return Fail("缺少 value 或 text 参数");

                }

                var result = await ExecuteJsAsync(selectJs);

                var selErr = GetJsError(result);

                if (selErr != null) return Fail($"❌ 选择失败: {selErr}");

                return Success($"✅ 已选择选项: {result}");

            case "check_element":

                var checkSelector = selector;

                if (string.IsNullOrEmpty(checkSelector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，例如 {\"selector\":\".search-input\"} 或 {\"selector\":\"#submit-btn\"}");

                var checkJs = BuildSafeElementJs(checkSelector, @"

                    if (el.type === 'checkbox' || el.type === 'radio') el.checked = true;

                    el.dispatchEvent(new Event('change', {bubbles: true}));

                    return JSON.stringify({success: true, tag: el.tagName, type: el.type});

                ");

                var checkResult = await ExecuteJsAsync(checkJs);

                var checkErr = GetJsError(checkResult);

                if (checkErr != null) return Fail($"❌ 勾选失败: {checkErr}");

                return Success($"✅ 已勾选元素: {checkResult}");

            case "file_input":

                return Fail("文件上传需要用户交互，请在页面上点击文件选择按钮手动操作");

            default:

                return Fail($"未知选择操作: {action}");

        }

    }

    // ====================================================================

    // 5. skill_scroll — 滚动浏览

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteScroll(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "scroll_to";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "scroll_to":

                var toX = GetParam<double?>(subParams, "x") ?? 0;

                var toY = GetParam<double?>(subParams, "y")

                    ?? GetParam<double?>(parameters, "y") ?? 0;

                var toSelector = GetSelector(subParams, parameters);

                if (!string.IsNullOrEmpty(toSelector))

                {

                    var scrollJs = BuildSafeElementJs(toSelector, @"

                        el.scrollIntoView({behavior:'smooth', block:'center'});

                        return JSON.stringify({success: true});

                    ");

                    var scrollResult = await ExecuteJsAsync(scrollJs);

                    var scrollErr = GetJsError(scrollResult);

                    if (scrollErr != null) return Fail($"❌ 滚动到元素失败: {scrollErr}");

                    return Success($"✅ 已滚动到元素: {toSelector}");

                }

                else

                {

                    await ExecuteJsAsync($"window.scrollTo({{top: {toY}, left: {toX}, behavior: 'smooth'}});");

                    return Success($"✅ 已滚动到 (x={toX}, y={toY})");

                }

            case "scroll_by":

                var deltaX = GetParam<double?>(subParams, "delta_x") ?? 0;

                var deltaY = GetParam<double?>(subParams, "delta_y")

                    ?? GetParam<double?>(parameters, "delta_y") ?? 500;

                await ExecuteJsAsync($"window.scrollBy({{top: {deltaY}, left: {deltaX}, behavior: 'smooth'}});");

                return Success($"✅ 已滚动 ({deltaX}, {deltaY}) 像素");

            default:

                return Fail($"未知滚动操作: {action}");

        }

    }

    // ====================================================================

    // 6. skill_extract — 内容提取

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteExtract(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "get_page_text";

        switch (action)

        {

            case "get_page_text":

                var text = await ExecuteJsAsync("document.body?.innerText || ''");

                var title = await ExecuteJsAsync("document.title || ''");

                if (text.Length > 5000) text = text[..5000] + $"\n\n... (截断，共 {text.Length} 字符)";

                                // 内容过少时自动检测并尝试提取 iframe（SPA/动态页面常见）

                var iframeNote = "";

                if (text.Length < 200)

                {

                    var iframeInfo = await ExecuteJsAsync(

                        "(function() { var f = document.querySelectorAll('iframe, frame'); " +

                        "return JSON.stringify(Array.from(f).slice(0, 5).map(function(x) " +

                        "{ return {id: x.id || '', src: (x.src || '').substring(0, 200), " +

                        "name: x.name || '', visible: x.style.display !== 'none'}; })); })()");

                    if (iframeInfo != null && iframeInfo != "[]" && iframeInfo != "null" && iframeInfo != "")

                    {

                        // Try to auto-resolve iframe content

                        var iframeContent = await ExecuteJsAsync(GetIframeResolveJs(iframeInfo));

                        if (!string.IsNullOrEmpty(iframeContent) && iframeContent != "[]")

                        {

                            text = iframeContent;

                            iframeNote = $"\nℹ️ 页面自身文本很少，已从 iframe 自动提取内容 ({iframeContent.Length} 字符)";

                            Logger.Info($"get_page_text: 自动提取 iframe 内容 ({iframeContent.Length} 字符)");

                        }

                        else

                        {

                            iframeNote = $"\n⚠️ 页面文本很少 ({text.Length} 字符)，检测到 iframe，建议直接导航到其中：\n{iframeInfo}";

                            Logger.Info($"get_page_text: 内容过少，发现 iframe -> {iframeInfo}");

                        }

                    }

                }

return Success($"✅ 页面文本已提取 ({text.Length} 字符){iframeNote}",

                    new() { ["text"] = text, ["title"] = title, ["char_count"] = text.Length, ["iframe_info"] = iframeNote });

            case "get_page_html":

                var html = await ExecuteJsAsync("document.documentElement?.outerHTML || ''");

                if (html.Length > 5000) html = html[..5000] + $"\n\n... (截断，共 {html.Length} 字符)";

                return Success($"✅ 页面 HTML 已提取 ({html.Length} 字符)",

                    new() { ["html"] = html });

            case "get_page_title":

                var pageTitle = await ExecuteJsAsync("document.title || ''");

                return Success($"✅ 页面标题: {pageTitle}",

                    new() { ["title"] = pageTitle });

            case "get_element_text":

                var elSelector = GetSelector(GetSubParams(parameters), parameters);

                if (string.IsNullOrEmpty(elSelector)) elSelector = "body";

                var elTextJs = BuildSafeElementJs(elSelector, @"

                    return JSON.stringify({text: (el.textContent || '').trim().substring(0, 5000)});

                ");

                var elTextResult = await ExecuteJsAsync(elTextJs);

                var elTextErr = GetJsError(elTextResult);

                if (elTextErr != null) return Fail($"❌ 提取元素文本失败: {elTextErr}");

                // 从返回的 JSON 中提取文本

                try { using var doc = JsonDocument.Parse(elTextResult); elTextResult = doc.RootElement.GetProperty("text").GetString() ?? ""; } catch { }

                if (elTextResult.Length > 5000) elTextResult = elTextResult[..5000] + $"\n... (截断)";

                return Success($"✅ 元素文本已提取 ({elTextResult.Length} 字符)",

                    new() { ["text"] = elTextResult });

            case "get_attribute":

                var attrSelector = GetSelector(GetSubParams(parameters), parameters);

                var attrName = GetParam<string>(GetSubParams(parameters), "attribute")

                    ?? GetParam<string>(parameters, "attribute") ?? "";

                if (string.IsNullOrEmpty(attrSelector) || string.IsNullOrEmpty(attrName))

                    return Fail("缺少 selector 或 attribute 参数");

                var attrJs = BuildSafeElementJs(attrSelector, $@"

                    return JSON.stringify({{value: (el.getAttribute({JsonSerializer.Serialize(attrName)}) || '')}});

                ");

                var attrResult = await ExecuteJsAsync(attrJs);

                var attrErr = GetJsError(attrResult);

                if (attrErr != null) return Fail($"❌ 获取属性失败: {attrErr}");

                try { using var doc = JsonDocument.Parse(attrResult); attrResult = doc.RootElement.GetProperty("value").GetString() ?? ""; } catch { }

                return Success($"✅ 属性 {attrName}={attrResult}",

                    new() { [attrName] = attrResult });

            case "probe_page":

                // Combined page probe: text + screenshot + iframe detection in one call

                var probeText = await ExecuteJsAsync("document.body?.innerText || ''");

                var probeTitle = await ExecuteJsAsync("document.title || ''");

                if (probeText.Length > 3000) probeText = probeText[..3000] + $"\n... (截断，共 {probeText.Length} 字符)";

                // Detect iframes if text is short

                var probeIframeNote = "";

                if (probeText.Length < 300)

                {

                    var probeIframes = await ExecuteJsAsync(

                        "(function() { var f = document.querySelectorAll('iframe, frame'); " +

                        "return JSON.stringify(Array.from(f).slice(0, 5).map(function(x) " +

                        "{ return {id: x.id || '', src: (x.src || '').substring(0, 200), " +

                        "name: x.name || '', visible: x.style.display !== 'none'}; })); })()");

                    if (probeIframes != null && probeIframes != "[]" && probeIframes != "null" && probeIframes != "")

                    {

                        probeIframeNote = $"\n⚠️ 检测到 iframe，内容可能在其中：\n{probeIframes}";

                    }

                }

                // Take screenshot via CDP
                var screenshotResult = await OnUiThreadAsync(async wv =>
                {
                    var cdpParams = new Dictionary<string, object> { ["format"] = "png", ["fromSurface"] = true };
                    var cdpJson = JsonSerializer.Serialize(cdpParams);
                    var result = await wv.CoreWebView2!.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", cdpJson);
                    using var doc = JsonDocument.Parse(result);
                    return doc.RootElement.TryGetProperty("data", out var data) ? data.GetString() ?? "" : "";
                }, "");
                var probeScreenshot = screenshotResult ?? "";
                var probePreview = probeScreenshot.Length > 80 ? probeScreenshot[..80] + "..." : probeScreenshot;

                var probeMsg = $"\n✅ 页面探测完成: {probeText.Length} 字符文本, {probeScreenshot.Length / 1024}KB 截图";

                return Success(probeMsg + probeIframeNote,

                    new() {

                        ["text"] = probeText,

                        ["title"] = probeTitle,

                        ["screenshot_base64"] = probeScreenshot,

                        ["screenshot_preview"] = probePreview,

                        ["char_count"] = probeText.Length,

                        ["screenshot_size_kb"] = probeScreenshot.Length / 1024,

                        ["iframe_info"] = string.IsNullOrEmpty(probeIframeNote) ? "" : probeIframeNote

                    });

            default:

                return Fail($"未知提取操作: {action}");

        }

    }

    // ====================================================================

    // 7. skill_screenshot — 页面截图

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteScreenshot(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "take_screenshot";

        switch (action)

        {

            case "take_screenshot":

                return await OnUiThreadAsync(async wv =>

                {

                    var cdpParams = new Dictionary<string, object>

                    {

                        ["format"] = "png",

                        ["fromSurface"] = true

                    };

                    var cdpJson = JsonSerializer.Serialize(cdpParams);

                    var result = await wv.CoreWebView2!.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", cdpJson);

                    using var doc = JsonDocument.Parse(result);

                    var base64 = doc.RootElement.TryGetProperty("data", out var data) ? data.GetString() ?? "" : "";

                    var preview = base64.Length > 100 ? base64[..100] + "..." : base64;

                    Logger.Info($"截图成功: {base64.Length} 字符 (Base64)");

                    return Success($"✅ 页面截图成功 ({base64.Length / 1024}KB)",

                        new() { ["screenshot_base64"] = base64, ["preview"] = preview });

                }, Fail("WebView2 不可用"));

            default:

                return Fail($"未知截图操作: {action}");

        }

    }

    // ====================================================================

    // 8. skill_wait — 等待条件

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteWait(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "wait";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "wait_for_navigation":

            case "wait":

                // 等待页面 readyState === 'complete'（最长 15 秒）

                var timeoutMs = 15000;

                var delayMs = GetParam<double?>(subParams, "delay_ms")

                    ?? GetParam<double?>(parameters, "delay_ms") ?? 0;

                if (delayMs > 0)

                {

                    await Task.Delay((int)delayMs, ct);

                    return Success($"✅ 已等待 {delayMs}ms");

                }

                var waited = 0;

                var checkInterval = 200;

                while (waited < timeoutMs)

                {

                    ct.ThrowIfCancellationRequested();

                    var state = await ExecuteJsAsync("document.readyState");

                    if (state == "complete")

                        return Success($"✅ 页面加载完成（等待约 {waited}ms）");

                    await Task.Delay(checkInterval, ct);

                    waited += checkInterval;

                }

                return Success($"✅ 页面加载等待超时（{waited}ms），继续执行");

            case "wait_for_element":

                var sel = GetSelector(subParams, parameters);

                if (string.IsNullOrEmpty(sel))

                    return Fail("缺少 selector 参数");

                var elemTimeout = 15000;

                var elemWaited = 0;

                var elemExistsJs = BuildElementExistsJs(sel);

                while (elemWaited < elemTimeout)

                {

                    ct.ThrowIfCancellationRequested();

                    var exists = await ExecuteJsAsync(elemExistsJs);

                    if (exists == "true")

                        return Success($"✅ 元素已出现（等待约 {elemWaited}ms）: {sel}");

                    // 检查是否返回了错误 JSON

                    var existsErr = GetJsError(exists);

                    if (existsErr != null)

                        return Fail($"等待元素失败: {existsErr}");

                    await Task.Delay(200, ct);

                    elemWaited += 200;

                }

                return Fail($"等待超时: 元素 '{sel}' 在 {elemTimeout}ms 内未出现");

            case "wait_for_text":

                var text = GetParam<string>(subParams, "text")

                    ?? GetParam<string>(parameters, "text") ?? "";

                if (string.IsNullOrEmpty(text))

                    return Fail("缺少 text 参数");

                var textTimeout = 15000;

                var textWaited = 0;

                while (textWaited < textTimeout)

                {

                    ct.ThrowIfCancellationRequested();

                    var pageText = await ExecuteJsAsync("document.body?.innerText || ''");

                    if (pageText.Contains(text, StringComparison.OrdinalIgnoreCase))

                        return Success($"✅ 文本已出现（等待约 {textWaited}ms）: \"{text}\"");

                    await Task.Delay(200, ct);

                    textWaited += 200;

                }

                return Fail($"等待超时: 文本 \"{text}\" 在 {textTimeout}ms 内未出现");

            default:

                return Fail($"未知等待操作: {action}");

        }

    }

    // ====================================================================

    // 9. skill_tab — 标签管理

    // ====================================================================

    private Task<SkillExecutionResult> ExecuteTab(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "get_tabs";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "create_tab":

            case "new_tab":

                var url = GetParam<string>(subParams, "url")

                    ?? GetParam<string>(parameters, "url")

                    ?? "about:blank";

                Application.Current.Dispatcher.Invoke(() =>

                {

                    _browserVm.AddNewTab(url);

                });

                Logger.Info($"新建标签页: {url}");

                return Task.FromResult(Success($"✅ 已新建标签页: {url}"));

            case "close_tab":

                var closeIdStr = GetParam<string>(subParams, "tab_id")

                    ?? GetParam<string>(parameters, "tab_id") ?? "";

                if (!string.IsNullOrEmpty(closeIdStr) && Guid.TryParse(closeIdStr, out var closeId))

                {

                    Application.Current.Dispatcher.Invoke(() => _browserVm.CloseTab(closeId));

                    return Task.FromResult(Success("✅ 已关闭标签页"));

                }

                var activeTab = _browserVm.ActiveTab;

                if (activeTab != null)

                {

                    Application.Current.Dispatcher.Invoke(() => _browserVm.CloseTab(activeTab.Id));

                    return Task.FromResult(Success("✅ 已关闭当前标签页"));

                }

                return Task.FromResult(Fail("没有可关闭的标签页"));

            case "activate_tab":

                var activateIdStr = GetParam<string>(subParams, "tab_id")

                    ?? GetParam<string>(parameters, "tab_id") ?? "";

                if (!string.IsNullOrEmpty(activateIdStr) && Guid.TryParse(activateIdStr, out var activateId))

                {

                    Application.Current.Dispatcher.Invoke(() => _browserVm.ActivateTab(activateId));

                    return Task.FromResult(Success("✅ 已切换标签页"));

                }

                var index = GetParam<double?>(subParams, "index")

                    ?? GetParam<double?>(parameters, "index");

                if (index.HasValue && index.Value >= 0)

                {

                    var tabs = _browserVm.Tabs.ToList();

                    var idx = (int)index.Value;

                    if (idx < tabs.Count)

                    {

                        Application.Current.Dispatcher.Invoke(() => _browserVm.ActivateTab(tabs[idx].Id));

                        return Task.FromResult(Success($"✅ 已切换到标签 #{idx + 1}: {tabs[idx].Title ?? tabs[idx].Url}"));

                    }

                    return Task.FromResult(Fail($"标签索引 {idx} 超出范围（共 {tabs.Count} 个标签）"));

                }

                return Task.FromResult(Fail("缺少 tab_id 或 index 参数"));

            case "get_tabs":

            case "get_active_tab":

                var tabsInfo = _browserVm.Tabs.Select(t => new

                {

                    id = t.Id.ToString(),

                    title = t.Title ?? "",

                    url = t.Url ?? "",

                    isActive = t.IsActive,

                    isLoading = t.IsLoading

                }).ToList();

                var summary = string.Join("\n", tabsInfo.Select((t, i) =>

                    $"  {(t.isActive ? "▶" : " ")} #{i + 1}: {(t.isLoading ? "⏳" : "✅")} {t.title} ({t.url})"));

                return Task.FromResult(Success($"当前有 {tabsInfo.Count} 个标签页:\n{summary}",

                    new() { ["tabs"] = tabsInfo }));

            default:

                return Task.FromResult(Fail($"未知标签操作: {action}"));

        }

    }

    // ====================================================================

    // 10. skill_cookie — Cookie 管理

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteCookie(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "get_cookies";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "get_cookies":

                return await OnUiThreadAsync(async wv =>

                {

                    var currentUrl = wv.CoreWebView2!.Source;

                    var cookieList = await wv.CoreWebView2.CookieManager.GetCookiesAsync(currentUrl);

                    var cookies = cookieList.Select(c => new

                    {

                        name = c.Name,

                        value = c.Value.Length > 50 ? c.Value[..50] + "..." : c.Value,

                        domain = c.Domain,

                        path = c.Path,

                        secure = c.IsSecure,

                        httpOnly = c.IsHttpOnly

                    }).ToList();

                    var summary = cookies.Count > 0

                        ? string.Join("\n", cookies.Select(c => $"  🍪 {c.name}={c.value} ({c.domain}{c.path})"))

                        : "  （无 Cookie）";

                    return Success($"当前域名 Cookie ({cookies.Count} 个):\n{summary}",

                        new() { ["cookies"] = cookies, ["count"] = cookies.Count });

                }, Fail("WebView2 不可用"));

            case "set_cookie":

                return await OnUiThreadAsync(wv =>

                {

                    var name = GetParam<string>(subParams, "name")

                        ?? GetParam<string>(parameters, "name") ?? "";

                    var value = GetParam<string>(subParams, "value")

                        ?? GetParam<string>(parameters, "value") ?? "";

                    if (string.IsNullOrEmpty(name))

                        return Fail("缺少 name 参数");

                    var cookie = wv.CoreWebView2!.CookieManager.CreateCookie(name, value, ".", "/");

                    wv.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);

                    return Success($"✅ Cookie 已设置: {name}={value}");

                }, Fail("WebView2 不可用"));

            case "delete_cookie":

                return await OnUiThreadAsync(async wv =>

                {

                    var name = GetParam<string>(subParams, "name")

                        ?? GetParam<string>(parameters, "name") ?? "";

                    if (string.IsNullOrEmpty(name))

                        return Fail("缺少 name 参数");

                    var currentUrl = wv.CoreWebView2!.Source;

                    var cookies = await wv.CoreWebView2.CookieManager.GetCookiesAsync(currentUrl);

                    var target = cookies.FirstOrDefault(c => c.Name == name);

                    if (target != null)

                    {

                        wv.CoreWebView2.CookieManager.DeleteCookie(target);

                    return Success($"✅ Cookie 已删除: {name}");

                    }

                    return Success($"Cookie '{name}' 不存在，无需删除");

                }, Fail("WebView2 不可用"));

            default:

                return Fail($"未知 Cookie 操作: {action}");

        }

    }

    // ====================================================================

    // 11. skill_form — 表单操作

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteForm(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "fill_form";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "fill_form":

            case "type_text":

                var fields = GetParam<Dictionary<string, object?>>(subParams, "fields")

                    ?? GetParam<Dictionary<string, object?>>(parameters, "fields")

                    ?? new();

                if (fields.Count == 0)

                {

                    // 尝试从 params 中提取单个字段

                    var singleSelector = GetSelector(subParams, parameters);

                    var singleValue = GetParam<string>(subParams, "value")

                        ?? GetParam<string>(subParams, "text")

                        ?? GetParam<string>(parameters, "value") ?? "";

                    if (!string.IsNullOrEmpty(singleSelector))

                    {

                        fields[singleSelector] = singleValue;

                    }

                }

                if (fields.Count == 0)

                    return Fail("缺少 fields 或 selector+value 参数");

                var filled = 0;

                var errors = new List<string>();

                foreach (var (sel, val) in fields)

                {

                    var strVal = val?.ToString() ?? "";

                    var formJs = BuildSafeElementJs(sel, $@"

                        el.focus();

                        el.value = {JsonSerializer.Serialize(strVal)};

                        el.dispatchEvent(new Event('input', {{bubbles: true}}));

                        el.dispatchEvent(new Event('change', {{bubbles: true}}));

                        return 'OK';

                    ");

                    var result = await ExecuteJsAsync(formJs);

                    var formErr = GetJsError(result);

                    if (formErr != null)

                        errors.Add($"{sel}: {formErr}");

                    else if (result != "OK")

                        errors.Add($"元素未找到: {sel}");

                    else

                        filled++;

                }

                var summary = $"✅ 表单填充完成: {filled} 个字段成功";

                if (errors.Count > 0) summary += $", {errors.Count} 个失败";

                return Success(summary);

            case "drag_and_drop":

                var source = GetParam<string>(subParams, "source")

                    ?? GetParam<string>(parameters, "source") ?? "";

                var target = GetParam<string>(subParams, "target")

                    ?? GetParam<string>(parameters, "target") ?? "";

                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))

                    return Fail("缺少 source 或 target 参数");

                var dndJs = $@"(function() {{

                    var src = document.querySelector({JsonSerializer.Serialize(source)});

                    var tgt = document.querySelector({JsonSerializer.Serialize(target)});

                    if (!src || !tgt) return JSON.stringify({{error: '元素未找到'}});

                    // 模拟 HTML5 drag and drop

                    src.dispatchEvent(new DragEvent('dragstart', {{bubbles: true, dataTransfer: new DataTransfer()}}));

                    tgt.dispatchEvent(new DragEvent('drop', {{bubbles: true}}));

                    src.dispatchEvent(new DragEvent('dragend', {{bubbles: true}}));

                    return JSON.stringify({{success: true}});

                }})()";

                // 注意: drag_and_drop 的双选择器场景较特殊，暂不套用 BuildSafeElementJs；此处加 try-catch 基础保护

                dndJs = $@"(function() {{

                    try {{

                        var src = document.querySelector({JsonSerializer.Serialize(source)});

                        if (!src) return JSON.stringify({{error: '拖拽源元素未找到: {JsonSerializer.Serialize(source)}'}});

                        var tgt = document.querySelector({JsonSerializer.Serialize(target)});

                        if (!tgt) return JSON.stringify({{error: '拖拽目标元素未找到: {JsonSerializer.Serialize(target)}'}});

                        src.dispatchEvent(new DragEvent('dragstart', {{bubbles: true, dataTransfer: new DataTransfer()}}));

                        tgt.dispatchEvent(new DragEvent('drop', {{bubbles: true}}));

                        src.dispatchEvent(new DragEvent('dragend', {{bubbles: true}}));

                        return JSON.stringify({{success: true}});

                    }} catch(e) {{

                        return JSON.stringify({{error: '拖放操作异常: ' + e.message}});

                    }}

                }})()";

                var dndResult = await ExecuteJsAsync(dndJs);

                var dndErr = GetJsError(dndResult);

                if (dndErr != null) return Fail($"❌ 拖放失败: {dndErr}");

                return Success($"✅ 拖放操作完成: {dndResult}");

            default:

                return Fail($"未知表单操作: {action}");

        }

    }

    // ====================================================================

    // 12. skill_hover — 悬停展开

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteHover(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "hover_element";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "hover_element":

                var selector = GetSelector(subParams, parameters);

                if (string.IsNullOrEmpty(selector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，例如 {\"selector\":\".search-input\"} 或 {\"selector\":\"#submit-btn\"}");

                var hoverJs = BuildSafeElementJs(selector, @"

                    el.dispatchEvent(new MouseEvent('mouseover', {bubbles: true, cancelable: true}));

                    el.dispatchEvent(new MouseEvent('mouseenter', {bubbles: true, cancelable: true}));

                    return JSON.stringify({success: true, tag: el.tagName, text: (el.textContent||'').trim().substring(0, 50)});

                ");

                var result = await ExecuteJsAsync(hoverJs);

                var err = GetJsError(result);

                if (err != null) return Fail($"❌ 悬停失败: {err}");

                return Success($"✅ 已悬停元素: {result}");

            case "focus_element":

                var focusSelector = GetSelector(subParams, parameters);

                if (string.IsNullOrEmpty(focusSelector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，例如 {\"selector\":\".search-input\"} 或 {\"selector\":\"#submit-btn\"}");

                var focusJs = BuildSafeElementJs(focusSelector, @"

                    el.focus();

                    return JSON.stringify({success: true, tag: el.tagName});

                ");

                var focusResult = await ExecuteJsAsync(focusJs);

                var focusErr = GetJsError(focusResult);

                if (focusErr != null) return Fail($"❌ 聚焦失败: {focusErr}");

                return Success($"✅ 已聚焦元素: {focusResult}");

            default:

                return Fail($"未知悬停操作: {action}");

        }

    }

    // ====================================================================

    // 13. skill_query — DOM 查询

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteQuery(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "query_selector_all";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "query_selector":

            case "query_selector_all":

                var selector = GetSelector(subParams, parameters);

                if (string.IsNullOrEmpty(selector))

                    return Fail("缺少 selector 参数。请使用 'selector' 键名传入 CSS 选择器，例如 {\"selector\":\".search-input\"} 或 {\"selector\":\"#submit-btn\"}");

                var isSingle = action == "query_selector";

                var queryJs = isSingle

                    ? BuildSafeElementJs(selector, @"

                        return JSON.stringify({tag: el.tagName, id: el.id, class: el.className, text: (el.textContent||'').trim().substring(0, 100), visible: el.offsetParent !== null});

                    ")

                    : BuildSafeElementAllJs(selector, @"

                        return JSON.stringify(Array.from(els).slice(0, 20).map(function(el, i) {

                            return {index: i, tag: el.tagName, id: el.id, class: el.className, text: (el.textContent||'').trim().substring(0, 80), visible: el.offsetParent !== null};

                        }));

                    ");

                var result = await ExecuteJsAsync(queryJs);

                return Success($"✅ DOM 查询 '{selector}': {result}",

                    new() { ["result"] = result });

            case "get_page_links":

                var linksJs = @"(function() {

                    var links = Array.from(document.querySelectorAll('a[href]')).slice(0, 30);

                    return JSON.stringify(links.map((a, i) => ({index: i, text: (a.textContent||'').trim().substring(0, 60), href: a.href})));

                })()";

                var linksResult = await ExecuteJsAsync(linksJs);

                return Success($"✅ 页面链接: {linksResult}",

                    new() { ["links"] = linksResult });

            case "get_form_fields":

                var formJs = @"(function() {

                    var fields = Array.from(document.querySelectorAll('input, select, textarea')).slice(0, 30);

                    return JSON.stringify(fields.map((el, i) => ({index: i, tag: el.tagName, type: el.type || el.tagName, name: el.name || '', id: el.id || '', placeholder: el.placeholder || '', value: (el.value||'').substring(0, 50)})));

                })()";

                var formResult = await ExecuteJsAsync(formJs);

                return Success($"✅ 表单字段: {formResult}",

                    new() { ["fields"] = formResult });

            case "get_page_structure":

                var structJs = @"(function() {

                    function getStructure(el, depth) {

                        if (depth > 5 || !el || el.children.length === 0) return '';

                        var result = '';

                        for (var i = 0; i < Math.min(el.children.length, 20); i++) {

                            var child = el.children[i];

                            var tag = child.tagName.toLowerCase();

                            var id = child.id ? '#' + child.id : '';

                            var cls = child.className && typeof child.className === 'string' ? '.' + child.className.split(' ').filter(Boolean).join('.') : '';

                            var info = child.children.length > 0 ? '' : (' ' + (child.textContent || '').trim().substring(0, 30));

                            result += '  '.repeat(depth) + '<' + tag + id + cls + '>' + info + '\n';

                            result += getStructure(child, depth + 1);

                        }

                        return result;

                    }

                    return getStructure(document.body, 0).substring(0, 3000);

                })()";

                var structure = await ExecuteJsAsync(structJs);

                return Success($"✅ 页面结构:\n{structure}",

                    new() { ["structure"] = structure });

            default:

                return Fail($"未知查询操作: {action}");

        }

    }

    // ====================================================================

    // 14. skill_js — JS 执行

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteJs(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        var action = GetAction(parameters) ?? "execute_javascript";

        var subParams = GetSubParams(parameters);

        switch (action)

        {

            case "execute_javascript":

            case "execute_js":

                var code = GetParam<string>(subParams, "code")

                    ?? GetParam<string>(subParams, "script")

                    ?? GetParam<string>(parameters, "code")

                    ?? GetParam<string>(parameters, "script")

                    ?? "";

                if (string.IsNullOrEmpty(code))

                {

                    // 如果 action params 中有 code 字段

                    if (parameters.TryGetValue("code", out var codeVal) && codeVal is string codeStr)

                        code = codeStr;

                }

                if (string.IsNullOrEmpty(code))

                    return Fail("缺少 code/script 参数");

                var result = await ExecuteJsAsync(code);

                var truncated = result.Length > 2000 ? result[..2000] + $"\n... (截断，共 {result.Length} 字符)" : result;

                Logger.Info($"JS 执行结果: {truncated}");

                return Success($"✅ JS 执行完成:\n{truncated}",

                    new() { ["result"] = result });

            default:

                return Fail($"未知 JS 操作: {action}");

        }

    }

    // ====================================================================

    // 15. skill_adb_sms — ADB 短信验证码

    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteAdbSms(

        Dictionary<string, object?> parameters, CancellationToken ct)

    {

        using var _ = Logger.Trace("WebView2AutomationBridge::ExecuteAdbSms");

        var action = GetAction(parameters) ?? "check_device";

        var subParams = GetSubParams(parameters);

        var adb = _adb.Value;

        if (!adb.IsAvailable)

        {

            return Fail("ADB 不可用：未找到 adb 可执行文件。" +

                "请安装 Android SDK Platform Tools 并确保 adb 在系统 PATH 中。" +

                "\n下载地址: https://developer.android.com/studio/releases/platform-tools");

        }

        switch (action)

        {

            case "check_device":

            {

                var (available, deviceId, error) = await adb.CheckDeviceAsync(ct);

                if (available)

                    return Success($"✅ 设备已连接: {deviceId}", new() { ["device_id"] = deviceId! });

                return Fail(error ?? "设备检测失败");

            }

            case "get_recent_sms":

            {

                var limit = GetParam<int?>(subParams, "limit")

                    ?? GetParam<int?>(parameters, "limit") ?? 10;

                var (success, messages, error) = await adb.GetRecentSmsAsync(limit, ct);

                if (!success)

                    return Fail(error ?? "获取短信失败");

                var formatted = string.Join("\n", messages.Select((m, i) =>

                    $"[{i + 1}] {m.Date:MM-dd HH:mm} | {m.Address}\n    {m.Body}"));

                var truncated = formatted.Length > 4000 ? formatted[..4000] + $"\n... (截断，共 {formatted.Length} 字符)" : formatted;

                Logger.Info($"AdbService: 获取到 {messages.Count} 条短信");

                return Success($"✅ 获取到 {messages.Count} 条短信:\n{truncated}",

                    new()

                    {

                        ["count"] = messages.Count,

                        ["messages"] = formatted,

                        ["latest"] = messages.FirstOrDefault()?.Body ?? ""

                    });

            }

            case "wait_for_code":

            {

                var timeoutMs = GetParam<int?>(subParams, "timeout_ms")

                    ?? GetParam<int?>(parameters, "timeout_ms") ?? 60000;

                var sender = GetParam<string>(subParams, "sender")

                    ?? GetParam<string>(parameters, "sender");

                Logger.Info($"AdbService: 等待验证码 (timeout={timeoutMs}ms, sender={sender ?? "any"})");

                var (success, msg, code, error) = await adb.WaitForVerificationCodeAsync(

                    timeoutMs, senderFilter: sender, ct: ct);

                if (success && code != null)

                {

                    Logger.Info($"AdbService: 验证码获取成功 [{code}]");

                    return Success($"✅ 验证码: **{code}**\n来源: {msg!.Address}\n内容: {msg.Body}",

                        new()

                        {

                            ["code"] = code,

                            ["sender"] = msg.Address,

                            ["full_message"] = msg.Body

                        });

                }

                return Fail(error ?? "未收到验证码");

            }

            case "get_phone_info":

            {

                var (success, info, error) = await adb.GetPhoneInfoAsync(ct);

                if (!success)

                    return Fail(error ?? "获取设备信息失败");

                var summary = string.Join("\n", info.Select(kv => $"- {kv.Key}: {kv.Value}"));

                return Success($"✅ 设备信息:\n{summary}", new() { ["info"] = summary });

            }

            default:

                return Fail($"未知 ADB SMS 操作: {action}。" +

                    "支持的操作: check_device, get_recent_sms, wait_for_code, get_phone_info");

        }

    }

    /// <summary>Resolve iframe content by trying contentDocument and fetch</summary>

    private static string GetIframeResolveJs(string iframeJson)

    {

        var sb = new System.Text.StringBuilder();

        sb.Append("(async() => {");

        sb.Append("try { var data = JSON.parse('");

        sb.Append(iframeJson.Replace("'", "'"));

        sb.Append("'); if (!data || !data.length) return ''; ");

        sb.Append("var iframe = document.getElementById(data[0].id) || document.querySelector('iframe'); if (!iframe) return ''; ");

        sb.Append("try { var doc = iframe.contentDocument || iframe.contentWindow?.document; if (doc && doc.body) { var t = (doc.body.innerText || '').trim(); if (t.length > 10) return t.substring(0, 5000); } } catch(e) {} ");

        sb.Append("if (iframe.src && iframe.src.startsWith('http')) { try { var resp = await window.fetch(iframe.src, {credentials: 'include'}); var html = await resp.text(); var div = document.createElement('div'); div.innerHTML = html; div.querySelectorAll('script,style,link').forEach(function(el) { el.remove(); }); var t = (div.innerText || '').trim(); if (t.length > 10) return t.substring(0, 5000); } catch(e2) {} } ");

        sb.Append("return ''; } catch(e) { return ''; } })()");

        return sb.ToString();

    }

}
#endif

