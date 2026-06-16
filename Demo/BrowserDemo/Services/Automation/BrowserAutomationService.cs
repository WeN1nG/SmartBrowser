using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BrowserDemo.Services.Automation;

/// <summary>
/// 浏览器自动化服务 — V3 Phase 3 核心。
/// 封装 WebView2 API + JS 注入 + CDP 键盘。
///
/// ★ 线程模型 ★
/// - 后台线程（AI 工具循环）调用本服务的 *Async 方法
/// - 服务内部用 Dispatcher.InvokeAsync 切换到 UI 线程执行 WebView2 调用
/// - 用 SemaphoreSlim(1,1) 串行化所有自动化操作（防并发冲突）
/// - 每次操作前对比 ExpectedUrl 与实际 Source（用户手动导航检测）
///
/// ★ 不要在 UI 线程上 .Wait() 本服务的方法（会死锁）★
/// </summary>
public class BrowserAutomationService : IDisposable
{
    // ============================================================
    // 字段
    // ============================================================

    private Dispatcher? _dispatcher;
    private readonly Dictionary<Guid, WebView2> _webViews = new();
    private Guid? _activeTabId;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// 默认每次操作的 UI 线程超时（毫秒）。
    /// 不含 JS 内部异步等待（如 waitForText 自己控制超时）。
    /// </summary>
    public int DefaultOperationTimeoutMs { get; set; } = 30_000;

    /// <summary>当前 URL（用于状态校验、AI 上下文）— 由内部维护，外部只读</summary>
    public string? CurrentUrl { get; private set; }

    /// <summary>是否已初始化且至少绑定了一个 WebView2</summary>
    public bool IsReady => _dispatcher != null && _webViews.Count > 0 && _activeTabId.HasValue;

    /// <summary>已注册的 AI 可见浏览器工具名（供 ChatViewModel 路由 + ContextBuilder 导入）</summary>
    public IReadOnlySet<string> RegisteredToolNames { get; } = new HashSet<string>
    {
        "browser_navigate", "browser_back", "browser_forward", "browser_reload",
        "browser_snapshot", "browser_click", "browser_type", "browser_hover",
        "browser_select_option", "browser_scroll", "browser_press_key",
        "browser_screenshot", "browser_js", "browser_wait",
        "browser_wait_for", "browser_fill_form", "browser_switch_tab",
        "browser_click_by_hash"
    };

    /// <summary>工具是否已注册（O(1) 查询）</summary>
    public bool IsToolRegistered(string toolName) => RegisteredToolNames.Contains(toolName);

    /// <summary>已注册工具名称集合（含稳定 hash 点击等）</summary>
    public static IReadOnlySet<string> AllToolNames => new HashSet<string>
    {
        "browser_navigate", "browser_back", "browser_forward", "browser_reload",
        "browser_snapshot", "browser_click", "browser_type", "browser_hover",
        "browser_select_option", "browser_scroll", "browser_press_key",
        "browser_screenshot", "browser_js", "browser_wait",
        "browser_wait_for", "browser_fill_form", "browser_switch_tab",
        "browser_click_by_hash", "browser_scroll_to_element", "browser_click_at",
        "browser_get_dom_text_hash"
    };

    // ============================================================
    // 初始化与 WebView2 绑定
    // ============================================================

    /// <summary>
    /// 初始化服务，绑定 WPF Dispatcher。必须在 UI 线程调用一次。
    /// </summary>
    public void Initialize(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <summary>
    /// 绑定一个 WebView2 到指定标签 ID。若是首个标签自动设为活跃。
    /// 必须在 UI 线程调用。
    /// </summary>
    public void BindWebView(Guid tabId, WebView2 webView)
    {
        ThrowIfDisposed();
        if (webView == null) throw new ArgumentNullException(nameof(webView));
        _webViews[tabId] = webView;
        if (_activeTabId == null) SwitchToTab(tabId);
    }

    /// <summary>解绑（标签关闭时由 BrowserHostService 调用）</summary>
    public void UnbindWebView(Guid tabId)
    {
        _webViews.Remove(tabId);
        if (_activeTabId == tabId)
        {
            _activeTabId = null;
            CurrentUrl = null;
        }
    }

    /// <summary>切换活跃标签（后续 *Async 操作目标）</summary>
    public void SwitchToTab(Guid tabId)
    {
        ThrowIfDisposed();
        if (!_webViews.TryGetValue(tabId, out var wv))
            throw new InvalidOperationException($"标签 {tabId} 未绑定 WebView2");
        _activeTabId = tabId;
        try { CurrentUrl = wv.CoreWebView2?.Source; }
        catch { CurrentUrl = null; }
    }

    // ============================================================
    // 浏览器导航
    // ============================================================

    /// <summary>导航到 URL，等待 NavigationCompleted 或超时</summary>
    public Task<AutomationResult> NavigateAsync(string url, int timeoutMs = 30_000)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult(AutomationResult.Fail("url 参数为空"));

        return RunOnUiThreadAsync(async wv =>
        {
            var core = wv.CoreWebView2!;
            var tcs = new TaskCompletionSource<(bool ok, int status, string? err)>();

            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null!;
            handler = (_, e) =>
            {
                core.NavigationCompleted -= handler;
                tcs.TrySetResult((e.IsSuccess, (int)e.HttpStatusCode, e.WebErrorStatus.ToString()));
            };
            core.NavigationCompleted += handler;

            try { core.Navigate(url); }
            catch (Exception ex)
            {
                core.NavigationCompleted -= handler;
                return AutomationResult.Fail($"Navigate 抛出异常: {ex.Message}");
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            using (cts.Token.Register(() =>
            {
                core.NavigationCompleted -= handler;
                tcs.TrySetResult((false, 0, "timeout"));
            }))
            {
                var (ok, status, err) = await tcs.Task;
                CurrentUrl = core.Source;
                return ok
                    ? AutomationResult.Success($"导航成功，HTTP {status}", core.Source)
                    : AutomationResult.Fail($"导航失败: {err}", core.Source);
            }
        });
    }

    /// <summary>后退</summary>
    public Task<AutomationResult> GoBackAsync() => RunOnUiThreadAsync(async wv =>
    {
        await Task.Yield();
        var core = wv.CoreWebView2!;
        if (!core.CanGoBack) return AutomationResult.Fail("没有可后退的历史");
        core.GoBack();
        CurrentUrl = core.Source;
        return AutomationResult.Success("后退执行", core.Source);
    });

    /// <summary>前进</summary>
    public Task<AutomationResult> GoForwardAsync() => RunOnUiThreadAsync(async wv =>
    {
        await Task.Yield();
        var core = wv.CoreWebView2!;
        if (!core.CanGoForward) return AutomationResult.Fail("没有可前进的历史");
        core.GoForward();
        CurrentUrl = core.Source;
        return AutomationResult.Success("前进执行", core.Source);
    });

    /// <summary>刷新</summary>
    public Task<AutomationResult> ReloadAsync() => RunOnUiThreadAsync(async wv =>
    {
        await Task.Yield();
        wv.CoreWebView2!.Reload();
        return AutomationResult.Success("刷新执行", wv.CoreWebView2.Source);
    });

    // ============================================================
    // DOM 交互
    // ============================================================

    /// <summary>点击元素（data-bermain-id）</summary>
    public Task<AutomationResult> ClickAsync(int elementId)
        => InvokeJsCallAsync(AutomationScripts.ClickElementCall(elementId), "点击");

    /// <summary>
    /// 输入文本。
    /// ★ 使用 NativeInputValueSetter 绕过框架 value 拦截 ★
    /// ★ dispatch input + change 事件 ★
    /// </summary>
    public Task<AutomationResult> TypeAsync(int elementId, string text, bool clearFirst = true)
        => InvokeJsCallAsync(AutomationScripts.TypeInElementCall(elementId, text, clearFirst), "输入");

    /// <summary>悬停</summary>
    public Task<AutomationResult> HoverAsync(int elementId)
        => InvokeJsCallAsync(AutomationScripts.HoverCall(elementId), "悬停");

    /// <summary>滚动页面</summary>
    public Task<AutomationResult> ScrollAsync(int deltaX = 0, int deltaY = 300)
        => InvokeJsCallAsync(AutomationScripts.ScrollCall(deltaX, deltaY), "滚动");

    /// <summary>选择下拉选项</summary>
    public Task<AutomationResult> SelectOptionAsync(int elementId, string value)
        => InvokeJsCallAsync(AutomationScripts.SelectOptionCall(elementId, value), "选择");

    /// <summary>
    /// 填充表单：依次对每个字段执行 type。
    /// key 优先按 data-bermain-id（整数）匹配；否则按 name/aria-label/placeholder 文本匹配。
    /// </summary>
    public async Task<AutomationResult> FillFormAsync(Dictionary<string, string> formData)
    {
        if (formData == null || formData.Count == 0)
            return AutomationResult.Fail("formData 为空");

        var sw = Stopwatch.StartNew();
        var results = new List<string>();

        foreach (var (key, value) in formData)
        {
            if (int.TryParse(key, out var directId))
            {
                var r = await TypeAsync(directId, value, clearFirst: true);
                results.Add($"字段 '{key}'(id={directId}): {(r.IsSuccess ? "✓" : "✗ " + r.ErrorMessage)}");
                continue;
            }

            // 按 name/aria-label/placeholder 查找并填值（一次 JS 调用完成）
            var fillJs = BuildFillByNameJs(key, value);
            var typeResult = await EvaluateJavaScriptAsync(fillJs);
            results.Add($"字段 '{key}': {(typeResult.IsSuccess ? "✓" : "✗ " + typeResult.ErrorMessage)}");
        }

        sw.Stop();
        return AutomationResult.Success(string.Join("; ", results), CurrentUrl) with { ElapsedMs = sw.ElapsedMilliseconds };
    }

    /// <summary>
    /// 构造按 name/aria-label/placeholder 查找并填值的 JS。
    /// 用普通字符串拼接 — 因为 JS 模板字面量 ${} 与 C# 原始字符串 $""" 冲突。
    /// </summary>
    private static string BuildFillByNameJs(string fieldKey, string value)
    {
        var keyLit = EncodeJsString(fieldKey);
        var valLit = EncodeJsString(value);
        return
            "(function(){" +
            "  var k=" + keyLit + ";" +
            "  var sel='[name=\"'+k+'\"],[aria-label=\"'+k+'\"],[placeholder=\"'+k+'\"]';" +
            "  try{" +
            "    var el=document.querySelector(sel);" +
            "    if(!el) return JSON.stringify({error:'not_found',field:k});" +
            "    if(el.tagName==='INPUT'||el.tagName==='TEXTAREA'){" +
            "      var proto=(el.tagName==='INPUT'?window.HTMLInputElement:window.HTMLTextAreaElement).prototype;" +
            "      var s=Object.getOwnPropertyDescriptor(proto,'value').set;" +
            "      s.call(el," + valLit + ");" +
            "      el.dispatchEvent(new Event('input',{bubbles:true,composed:true}));" +
            "      el.dispatchEvent(new Event('change',{bubbles:true}));" +
            "    } else if(el.isContentEditable){" +
            "      el.textContent=" + valLit + ";" +
            "      el.dispatchEvent(new Event('input',{bubbles:true,composed:true}));" +
            "    } else { return JSON.stringify({error:'not_typeable',tag:el.tagName}); }" +
            "    return JSON.stringify({success:true});" +
            "  }catch(e){ return JSON.stringify({error:e.message}); }" +
            "})()";
    }

    // ============================================================
    // 数据提取
    // ============================================================

    /// <summary>获取 A11y 快照（重新分配 data-bermain-id 并返回 JSON）</summary>
    public Task<AutomationResult> GetSnapshotAsync()
        => InvokeJsCallAsync(AutomationScripts.GetSnapshotCall, "快照", returnRawJson: true);

    /// <summary>获取当前页面 DOM text hash（用于页面停滞检测）</summary>
    public Task<AutomationResult> GetDomTextHashAsync()
        => RunOnUiThreadAsync(async wv =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var raw = await wv.CoreWebView2!.ExecuteScriptAsync("window.bermainA11y.getDomTextHash()");
                sw.Stop();
                var hash = StripQuotes(raw)?.Trim() ?? "0";
                return AutomationResult.Success($"dom_text_hash={hash}", wv.CoreWebView2.Source)
                    with { ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"获取 DOM text hash 失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });

    /// <summary>截图，返回 base64 PNG</summary>
    public Task<AutomationResult> TakeScreenshotAsync() => RunOnUiThreadAsync(async wv =>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var ms = new MemoryStream();
            await wv.CoreWebView2!.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png, ms);
            sw.Stop();
            var base64 = Convert.ToBase64String(ms.ToArray());
            return AutomationResult.Success(base64, wv.CoreWebView2.Source)
                with { ElapsedMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            return AutomationResult.Fail($"截图失败: {ex.Message}", wv.CoreWebView2?.Source);
        }
    });

    /// <summary>执行自定义 JS 并返回 ExecuteScriptAsync 的字符串结果</summary>
    public Task<AutomationResult> EvaluateJavaScriptAsync(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return Task.FromResult(AutomationResult.Fail("script 为空"));

        return RunOnUiThreadAsync(async wv =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var raw = await wv.CoreWebView2!.ExecuteScriptAsync(script);
                sw.Stop();
                return AutomationResult.Success(raw, wv.CoreWebView2.Source)
                    with { ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"JS 执行失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });
    }

    // ============================================================
    // 等待
    // ============================================================

    /// <summary>固定时长等待</summary>
    public async Task<AutomationResult> WaitAsync(int ms)
    {
        if (ms <= 0) return AutomationResult.Fail("等待时长必须 > 0");
        if (ms > 60_000) ms = 60_000;  // 上限保护
        await Task.Delay(ms);
        return AutomationResult.Success($"已等待 {ms}ms", CurrentUrl);
    }

    /// <summary>
    /// 等待页面文本出现。JS 内 100ms 轮询，超时返回 success:false。
    /// </summary>
    public async Task<AutomationResult> WaitForTextAsync(string text, int timeoutMs = 10_000)
    {
        if (string.IsNullOrEmpty(text))
            return AutomationResult.Fail("text 不能为空");

        var result = await InvokeJsCallAsync(
            AutomationScripts.WaitForTextCall(text, timeoutMs),
            "等待文本",
            returnRawJson: true);

        // waitForText 内部用 Promise + setTimeout，ExecuteScriptAsync 已正确 await
        return result;
    }

    /// <summary>等待下一次 NavigationCompleted（无目标 URL 比较）</summary>
    public Task<AutomationResult> WaitForNavigationAsync(int timeoutMs = 30_000)
        => RunOnUiThreadAsync(async wv =>
        {
            var core = wv.CoreWebView2!;
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> h = null!;
            h = (_, e) => { core.NavigationCompleted -= h; tcs.TrySetResult(e.IsSuccess); };
            core.NavigationCompleted += h;

            using var cts = new CancellationTokenSource(timeoutMs);
            using (cts.Token.Register(() => { core.NavigationCompleted -= h; tcs.TrySetResult(false); }))
            {
                var ok = await tcs.Task;
                return ok
                    ? AutomationResult.Success("导航完成", core.Source)
                    : AutomationResult.Fail("等待导航超时或失败", core.Source);
            }
        });

    // ============================================================
    // 键盘（CDP）
    // ============================================================

    private static readonly Dictionary<string, (int Vk, string Key)> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = (13, "Enter"),
        ["Tab"] = (9, "Tab"),
        ["Escape"] = (27, "Escape"),
        ["Esc"] = (27, "Escape"),
        ["ArrowUp"] = (38, "ArrowUp"),
        ["ArrowDown"] = (40, "ArrowDown"),
        ["ArrowLeft"] = (37, "ArrowLeft"),
        ["ArrowRight"] = (39, "ArrowRight"),
        ["Backspace"] = (8, "Backspace"),
        ["Delete"] = (46, "Delete"),
        ["Del"] = (46, "Delete"),
        ["Home"] = (36, "Home"),
        ["End"] = (35, "End"),
        ["PageUp"] = (33, "PageUp"),
        ["PageDown"] = (34, "PageDown"),
        ["Space"] = (32, " "),
    };

    /// <summary>通过 CDP Input.dispatchKeyEvent 模拟按键</summary>
    public Task<AutomationResult> PressKeyAsync(string key)
    {
        if (string.IsNullOrEmpty(key))
            return Task.FromResult(AutomationResult.Fail("key 不能为空"));

        if (!KeyMap.TryGetValue(key, out var info))
            return Task.FromResult(AutomationResult.Fail($"不支持的按键: {key}（支持: {string.Join(", ", KeyMap.Keys)}）"));

        return RunOnUiThreadAsync(async wv =>
        {
            try
            {
                var core = wv.CoreWebView2!;
                var down = JsonSerializer.Serialize(new
                {
                    type = "rawKeyDown",
                    windowsVirtualKeyCode = info.Vk,
                    key = info.Key,
                    code = info.Key
                });
                var up = JsonSerializer.Serialize(new
                {
                    type = "keyUp",
                    windowsVirtualKeyCode = info.Vk,
                    key = info.Key,
                    code = info.Key
                });
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", down);
                await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", up);
                return AutomationResult.Success($"按键 {key} 已发送", core.Source);
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"按键失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });
    }

    // ============================================================
    // 高级定位：stable hash + 坐标 + 按元素滚动
    // ============================================================

    /// <summary>
    /// 使用 stable_hash 查找并点击元素。
    /// stable_hash 基于 tag+aria-label+name+placeholder+text+type 计算，页面刷新后仍然有效。
    /// </summary>
    public async Task<AutomationResult> ClickByStableHashAsync(string stableHash)
    {
        if (string.IsNullOrWhiteSpace(stableHash))
            return AutomationResult.Fail("stableHash 参数为空");

        return await RunOnUiThreadAsync(async wv =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var hashLit = EncodeJsString(stableHash);
                var js =
                    "(function(){" +
                    "  function h(s){var h=5381;for(var i=0;i<s.length;i++){h=((h<<5)+h)+s.charCodeAt(i);}return(h>>>0).toString(36);}" +
                    "  var els=document.querySelectorAll('*');" +
                    "  var results=[];" +
                    "  for(var i=0;i<els.length;i++){" +
                    "    var el=els[i]," +
                    "    tag=el.tagName.toLowerCase()," +
                    "    al=(el.getAttribute('aria-label')||'').trim()," +
                    "    nm=(el.getAttribute('name')||'').trim()," +
                    "    ph=(el.getAttribute('placeholder')||'').trim()," +
                    "    tx=(el.textContent||'').trim().substring(0,50)," +
                    "    tp=(el.getAttribute('type')||'').toLowerCase()," +
                    "    key=[tag,al,nm,ph,tx,tp].filter(Boolean).join('|');" +
                    "    if(h(key)==='" + stableHash + "'){results.push(tag+(al?'#'+al:''));el.click();}" +
                    "  }" +
                    "  if(results.length>1) return JSON.stringify({ok:true,multiple_matches:results});" +
                    "  return JSON.stringify({ok:true,matched:(results[0]||'unknown')});" +
                    "})()";
                var raw = await wv.CoreWebView2!.ExecuteScriptAsync(js);
                sw.Stop();
                var unwrapped = StripQuotes(raw) ?? "";
                return AutomationResult.Success(unwrapped, wv.CoreWebView2.Source)
                    with { ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"Stable hash 点击失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });
    }

    /// <summary>滚动页面使指定元素出现在视口中</summary>
    public Task<AutomationResult> ScrollToElementAsync(int elementId)
        => RunOnUiThreadAsync(async wv =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var js =
                    "(function(){" +
                    "  var el=null;" +
                    "  var all=document.querySelectorAll('*');" +
                    "  for(var i=0;i<all.length;i++){" +
                    "    if((all[i].dataset.bermainId||'')==='" + elementId + "'){el=all[i];break;}" +
                    "  }" +
                    "  if(!el) return JSON.stringify({error:'element_not_found'});" +
                    "  el.scrollIntoView({behavior:'instant',block:'center',inline:'nearest'});" +
                    "  return JSON.stringify({ok:true,tag:el.tagName.toLowerCase()});" +
                    "})()";
                var raw = await wv.CoreWebView2!.ExecuteScriptAsync(js);
                sw.Stop();
                var unwrapped = StripQuotes(raw) ?? "";
                return AutomationResult.Success(unwrapped, wv.CoreWebView2.Source)
                    with { ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"滚动到元素失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });

    /// <summary>在视口绝对坐标 (x, y) 处点击（元素定位失败时的兜底）</summary>
    public Task<AutomationResult> ClickAtAsync(int x, int y)
        => RunOnUiThreadAsync(async wv =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                // 使用 CDP Input.dispatchMouseEvent 在绝对坐标处点击
                var down = JsonSerializer.Serialize(new
                {
                    type = "mousePressed",
                    buttons = "Left",
                    x,
                    y,
                    clickCount = 1
                });
                var up = JsonSerializer.Serialize(new
                {
                    type = "mouseReleased",
                    buttons = "Left",
                    x,
                    y,
                    clickCount = 1
                });
                await wv.CoreWebView2!.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", down);
                await wv.CoreWebView2!.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", up);
                sw.Stop();
                return AutomationResult.Success(
                    $"在坐标 ({x}, {y}) 点击成功",
                    wv.CoreWebView2.Source)
                    with { ElapsedMs = sw.ElapsedMilliseconds };
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"坐标点击失败: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });

    /// <summary>获取 DOM text hash</summary>
    public Task<AutomationResult> GetDomTextHashAsyncDirect()
        => GetDomTextHashAsync();

    // ============================================================
    // 弹窗设置
    // ============================================================

    /// <summary>
    /// 是否自动接受 alert/confirm/prompt。
    /// 实际生效在 BrowserHostService.AutoDismissDialogs；本属性仅传递语义意图。
    /// 调用方可在 BindWebView 时主动同步该属性到 host。
    /// </summary>
    public bool AutoDismissDialogs { get; set; } = true;

    // ============================================================
    // 事件（由外部 BrowserHostService 转发，本服务暂不主动触发）
    // ============================================================

    public event Action<Guid, NavigationEventInfo>? NavigationCompleted;
    public event Action<Guid, string>? TitleChanged;
    public event Action<Guid, string>? UrlChanged;
    public event Action<Guid, bool>? LoadingStateChanged;
    public event Action<Guid>? WebViewCrashed;

    /// <summary>由 BrowserHostService 在导航完成时调用，更新内部 CurrentUrl 并外发事件</summary>
    public void NotifyNavigationCompleted(Guid tabId, NavigationEventInfo info)
    {
        if (tabId == _activeTabId) CurrentUrl = info.Url;
        NavigationCompleted?.Invoke(tabId, info);
    }

    public void NotifyTitleChanged(Guid tabId, string title) => TitleChanged?.Invoke(tabId, title);
    public void NotifyUrlChanged(Guid tabId, string url)
    {
        if (tabId == _activeTabId) CurrentUrl = url;
        UrlChanged?.Invoke(tabId, url);
    }
    public void NotifyLoadingStateChanged(Guid tabId, bool isLoading) => LoadingStateChanged?.Invoke(tabId, isLoading);
    public void NotifyWebViewCrashed(Guid tabId)
    {
        if (tabId == _activeTabId) { _activeTabId = null; CurrentUrl = null; }
        _webViews.Remove(tabId);
        WebViewCrashed?.Invoke(tabId);
    }

    // ============================================================
    // 核心：UI 线程调度 + 串行化 + 状态校验
    // ============================================================

    /// <summary>
    /// 在 UI 线程上、串行化地执行一个 WebView2 操作。
    /// 调用方在后台线程 await 即可。
    /// </summary>
    private async Task<AutomationResult> RunOnUiThreadAsync(
        Func<WebView2, Task<AutomationResult>> operation)
    {
        ThrowIfDisposed();
        if (_dispatcher == null)
            return AutomationResult.Fail("服务未初始化（Dispatcher = null）");
        if (!_activeTabId.HasValue || !_webViews.TryGetValue(_activeTabId.Value, out var wv))
            return AutomationResult.Fail("没有活跃的 WebView2 实例");

        await _operationLock.WaitAsync();
        var sw = Stopwatch.StartNew();
        try
        {
            var op = _dispatcher.InvokeAsync(async () =>
            {
                if (wv.CoreWebView2 == null)
                    return AutomationResult.Fail("WebView2 CoreWebView2 未就绪");

                // 焦点切换到 WebView2（AI 操作前）
                try { wv.Focus(); Keyboard.Focus(wv); } catch { }

                return await operation(wv);
            }, DispatcherPriority.Background);

            var result = await await op.Task;
            sw.Stop();
            if (result.ElapsedMs == 0) result = result with { ElapsedMs = sw.ElapsedMilliseconds };
            return result;
        }
        catch (Exception ex)
        {
            return AutomationResult.Fail($"UI 线程调度异常: {ex.Message}");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    /// <summary>
    /// 调用 bermainA11y.* 方法，统一解析 ExecuteScriptAsync 返回的字符串。
    /// ExecuteScriptAsync 返回的是 JSON.stringify(JS 返回值)，再外套一层 JSON 字符串引号。
    /// JS 函数都返回 JSON.stringify({...}) — 所以这里需要剥一层。
    /// </summary>
    private Task<AutomationResult> InvokeJsCallAsync(
        string call, string actionLabel, bool returnRawJson = false)
    {
        return RunOnUiThreadAsync(async wv =>
        {
            try
            {
                var raw = await wv.CoreWebView2!.ExecuteScriptAsync(call);

                // raw 是 JSON 字符串。JS 返回的就是 JSON.stringify(obj)，
                // ExecuteScriptAsync 再 stringify 一次，所以是双层 JSON 字符串。
                var unwrapped = StripQuotes(raw);

                if (returnRawJson)
                {
                    return AutomationResult.Success(unwrapped, wv.CoreWebView2.Source);
                }

                // 检查 error 字段
                if (!string.IsNullOrEmpty(unwrapped))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(unwrapped);
                        if (doc.RootElement.TryGetProperty("error", out var errEl))
                        {
                            var msg = errEl.GetString() ?? "未知错误";
                            return AutomationResult.Fail($"{actionLabel}失败: {msg}", wv.CoreWebView2.Source);
                        }
                        if (doc.RootElement.TryGetProperty("success", out var sucEl) && sucEl.GetBoolean())
                        {
                            return AutomationResult.Success(unwrapped, wv.CoreWebView2.Source);
                        }
                    }
                    catch (JsonException)
                    {
                        // 不是 JSON，直接返回
                    }
                }

                return AutomationResult.Success(unwrapped, wv.CoreWebView2.Source);
            }
            catch (Exception ex)
            {
                return AutomationResult.Fail($"{actionLabel}JS 执行异常: {ex.Message}", wv.CoreWebView2?.Source);
            }
        });
    }

    /// <summary>
    /// ExecuteScriptAsync 返回的字符串外层带 JSON 引号，需要解一层。
    /// 例如：JS 返回 'hello' → 实际拿到 '"hello"'（含引号的 4 字符）
    /// 实际场景：JS 返回 JSON.stringify({a:1}) → 拿到 '"{\"a\":1}"'
    /// </summary>
    private static string StripQuotes(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        try
        {
            // 用 JsonDocument 解一层
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return doc.RootElement.GetString() ?? "";
            // 已经是对象/数组（不应发生），原样返回
            return raw;
        }
        catch
        {
            return raw;
        }
    }

    private static string EncodeJsString(string value)
        => JsonSerializer.Serialize(value ?? string.Empty);

    // ============================================================
    // 释放
    // ============================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _operationLock.Dispose();
        _webViews.Clear();
        _activeTabId = null;
        CurrentUrl = null;
        _dispatcher = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrowserAutomationService));
    }
}

/// <summary>自动化操作通用返回结构</summary>
public record AutomationResult
{
    public bool IsSuccess { get; init; }
    public string? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public long ElapsedMs { get; init; }
    public string? CurrentUrl { get; init; }

    public static AutomationResult Success(string? data = null, string? url = null)
        => new() { IsSuccess = true, Data = data, CurrentUrl = url };

    public static AutomationResult Fail(string error, string? url = null)
        => new() { IsSuccess = false, ErrorMessage = error, CurrentUrl = url };
}

/// <summary>导航事件载荷（与 BrowserHostService.NavigationResultInfo 对齐）</summary>
public record NavigationEventInfo
{
    public string Url { get; init; } = "";
    public bool IsSuccess { get; init; }
    public int HttpStatusCode { get; init; }
    public string? WebErrorStatus { get; init; }
}
