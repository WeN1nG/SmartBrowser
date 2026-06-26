using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BrowserDemo.Models;
using BrowserDemo.Services.Automation;  // AutomationScripts
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BrowserDemo.Services.BrowserHost;

/// <summary>
/// 浏览器宿主服务 — 管理 WebView2 实例生命周期。
/// 多个标签共享同一个 <see cref="CoreWebView2Environment"/>（节省进程数）。
/// 处理弹窗自动确认、新窗口转新标签、渲染进程崩溃。
/// ★ 所有公共方法必须在 UI 线程调用（除特别标注）★
/// </summary>
public class BrowserHostService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Panel _container;
    private readonly Dictionary<Guid, WebView2> _webViews = new();
    private CoreWebView2Environment? _environment;
    private Guid? _activeTabId;
    private bool _disposed;

    /// <summary>
    /// 是否自动确认页面 alert/confirm/prompt 弹窗（默认 true）。
    /// 设为 false 时弹窗将默认拒绝（confirm 返回 false，prompt 返回空）。
    /// </summary>
    public bool AutoDismissDialogs { get; set; } = true;

    /// <summary>用户数据目录（Cookie/缓存/LocalStorage 等）。null = 使用默认</summary>
    public string? UserDataFolder { get; set; }

    public BrowserHostService(Dispatcher dispatcher, Panel container)
    {
        Logger.Trace("BrowserHostService..ctor");
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _container = container ?? throw new ArgumentNullException(nameof(container));
    }

    // ====================================================================
    // 状态属性
    // ====================================================================

    /// <summary>当前活跃 WebView2（无活跃标签时为 null）</summary>
    public WebView2? ActiveWebView
        => _activeTabId.HasValue && _webViews.TryGetValue(_activeTabId.Value, out var wv) ? wv : null;

    /// <summary>当前活跃标签 ID</summary>
    public Guid? ActiveTabId => _activeTabId;

    /// <summary>当前标签数</summary>
    public int TabCount => _webViews.Count;

    /// <summary>是否已完成 Environment 初始化</summary>
    public bool IsInitialized => _environment != null;

    /// <summary>
    /// 自动化服务的延迟注入槽位。
    /// BrowserAutomationService 在 BrowserHostService 之后构造，需要回填到此（Phase 3 实现）。
    /// 一旦设置，导航完成事件会通过该引用通知自动化服务。
    /// 临时用 object? 占位，避免引入循环依赖与编译时强耦合。
    /// </summary>
    public object? Automation { get; set; }

    // ====================================================================
    // 事件
    // ====================================================================

    /// <summary>标签关闭后触发（已从字典移除、WebView2 已释放）</summary>
    public event Action<Guid>? TabClosed;

    /// <summary>导航开始（NavigationStarting）— tabId + 目标 URL</summary>
    public event Action<Guid, string>? NavigationStarting;

    /// <summary>导航完成（NavigationCompleted）— tabId + 结果</summary>
    public event Action<Guid, NavigationResultInfo>? NavigationCompleted;

    /// <summary>文档标题变更（DocumentTitleChanged）</summary>
    public event Action<Guid, string>? TitleChanged;

    /// <summary>URL 变更（SourceChanged）</summary>
    public event Action<Guid, string>? UrlChanged;

    /// <summary>加载状态切换（true=加载中，false=已完成或失败）</summary>
    public event Action<Guid, bool>? LoadingStateChanged;

    /// <summary>渲染进程崩溃（ProcessFailed）— 出错后建议关闭该标签</summary>
    public event Action<Guid>? WebViewCrashed;

    /// <summary>请求新建标签（NewWindowRequested 转化）— 返回的标签 ID 由调用方决定</summary>
    public event Action<string>? NewTabRequested;

    // ====================================================================
    // 初始化
    // ====================================================================

    /// <summary>
    /// 创建共享 CoreWebView2Environment。必须在创建任何标签前调用一次。
    /// 重复调用幂等。
    /// </summary>
    public async Task InitializeAsync()
    {
        using var _ = Logger.Trace("BrowserHostService.InitializeAsync");
        if (_environment != null)
        {
            Logger.Debug("BrowserHostService 已初始化，跳过");
            return;
        }

        var options = new CoreWebView2EnvironmentOptions
        {
            // 允许某些站点要求的功能（如 autoplay）
            // 忽略 SSL 证书错误：许多企业内部站/学习平台使用自签名证书，
            // 直接拒绝会导致 browser_navigate 永远失败（如 linehelp.cn 案例）
            AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection --ignore-certificate-errors"
        };

        _environment = string.IsNullOrEmpty(UserDataFolder)
            ? await CoreWebView2Environment.CreateAsync(null, null, options)
            : await CoreWebView2Environment.CreateAsync(null, UserDataFolder, options);
    }

    // ====================================================================
    // 标签管理
    // ====================================================================

    /// <summary>
    /// 创建新标签。返回 TabInfo（含 Id/初始 Url/Title="新标签页"）。
    /// 内部完成：实例化 WebView2 → 加入容器 → 初始化 CoreWebView2 → 绑定事件 → 导航。
    /// 必须在 UI 线程调用。
    /// </summary>
    public Task<TabInfo> CreateTabAsync(string url = "about:blank")
        => CreateTabForAsync(new TabInfo { Url = url }, url);

    /// <summary>
    /// 用调用方提供的 TabInfo（保留其 Id）创建关联的 WebView2。
    /// 适用于 ViewModel 已先创建 TabInfo 的场景（避免 Id 不同步）。
    /// 必须在 UI 线程调用。
    /// </summary>
    public async Task<TabInfo> CreateTabForAsync(TabInfo tab, string url = "about:blank")
    {
        using var _ = Logger.Trace("BrowserHostService.CreateTabForAsync");
        ThrowIfDisposed();
        if (_environment == null)
            throw new InvalidOperationException("BrowserHostService 未初始化，请先 await InitializeAsync()");
        if (tab == null) throw new ArgumentNullException(nameof(tab));
        if (_webViews.ContainsKey(tab.Id))
            throw new InvalidOperationException($"标签 {tab.Id} 已存在 WebView2");

        var wv = new WebView2
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        _container.Children.Add(wv);

        try
        {
            await wv.EnsureCoreWebView2Async(_environment);
        }
        catch (Exception ex)
        {
            _container.Children.Remove(wv);
            throw new InvalidOperationException($"WebView2 初始化失败: {ex.Message}", ex);
        }

        ConfigureCoreWebView2(wv.CoreWebView2);
        BindCoreEvents(tab, wv);

        _webViews[tab.Id] = wv;
        tab.CoreId = wv.CoreWebView2.BrowserProcessId.ToString();

        if (!string.IsNullOrEmpty(url) && url != "about:blank")
        {
            try { wv.CoreWebView2.Navigate(url); }
            catch (Exception) { /* 非法 URL：交由 NavigationCompleted 处理 */ }
        }

        return tab;
    }

    /// <summary>
    /// 激活标签：切换 Visibility（其他标签 Collapsed，目标 Visible）。
    /// 必须在 UI 线程调用。
    /// </summary>
    public void ActivateTab(Guid tabId)
    {
        using var _ = Logger.Trace("BrowserHostService.ActivateTab");
        ThrowIfDisposed();
        if (!_webViews.TryGetValue(tabId, out var target))
            return;

        foreach (var (id, wv) in _webViews)
            wv.Visibility = id == tabId ? Visibility.Visible : Visibility.Collapsed;

        _activeTabId = tabId;
    }

    /// <summary>
    /// 关闭标签：移除事件、从容器移除、释放 WebView2 资源。
    /// 必须在 UI 线程调用。
    /// </summary>
    public async Task CloseTabAsync(Guid tabId)
    {
        using var _ = Logger.Trace("BrowserHostService.CloseTabAsync");
        ThrowIfDisposed();
        if (!_webViews.TryGetValue(tabId, out var wv))
            return;

        _webViews.Remove(tabId);
        if (_activeTabId == tabId) _activeTabId = null;

        try
        {
            _container.Children.Remove(wv);
            wv.Dispose();
        }
        catch (Exception) { /* 容错：清理失败不阻塞流程 */ }

        await Task.CompletedTask;
        TabClosed?.Invoke(tabId);
    }

    /// <summary>获取标签对应的 WebView2 控件（不存在返回 null）</summary>
    public WebView2? GetWebViewForTab(Guid tabId)
    {
        var found = _webViews.TryGetValue(tabId, out var wv);
        Logger.Debug($"[GetWebViewForTab] tabId={tabId}, found={found}");
        return wv;
    }

    // ====================================================================
    // 内部：配置与事件绑定
    // ====================================================================

    private void ConfigureCoreWebView2(CoreWebView2 core)
    {
        Logger.Debug("[ConfigureCoreWebView2] 配置 CoreWebView2 设置");
        var s = core.Settings;
        s.IsScriptEnabled = true;
        s.AreDefaultScriptDialogsEnabled = false; // 关闭原生弹窗 UI（由 ScriptDialogOpening 接管）
        s.IsWebMessageEnabled = true;
        s.IsZoomControlEnabled = true;
        s.IsStatusBarEnabled = false;
        s.AreDevToolsEnabled = true; // CDP 需要
        s.IsBuiltInErrorPageEnabled = true;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
    }

    private void BindCoreEvents(TabInfo tab, WebView2 wv)
    {
        Logger.Debug($"[BindCoreEvents] tabId={tab.Id}, 绑定导航/下载/标题/URL/崩溃事件");
        var core = wv.CoreWebView2;

        // ★ 导航开始 ★
        core.NavigationStarting += (_, args) =>
        {
            tab.IsLoading = true;
            tab.Url = args.Uri;
            LoadingStateChanged?.Invoke(tab.Id, true);
            NavigationStarting?.Invoke(tab.Id, args.Uri);
        };

        // ★ 下载开始：记录状态供下载窗口展示 ★
        core.DownloadStarting += (_, args) =>
        {
            var operation = args.DownloadOperation;
            var item = new DownloadItem
            {
                FileName = Path.GetFileName(operation.ResultFilePath),
                Uri = operation.Uri,
                ResultFilePath = operation.ResultFilePath,
                BytesReceived = operation.BytesReceived,
                TotalBytesToReceive = operation.TotalBytesToReceive.HasValue ? (long?)operation.TotalBytesToReceive.Value : null,
                State = DownloadItemState.InProgress
            };
            DownloadManager.Add(item);

            operation.BytesReceivedChanged += (_, _) => DownloadManager.Update(item, x =>
            {
                x.BytesReceived = operation.BytesReceived;
                x.TotalBytesToReceive = operation.TotalBytesToReceive.HasValue ? (long?)operation.TotalBytesToReceive.Value : null;
            });

            operation.StateChanged += (_, _) => DownloadManager.Update(item, x =>
            {
                x.BytesReceived = operation.BytesReceived;
                x.TotalBytesToReceive = operation.TotalBytesToReceive.HasValue ? (long?)operation.TotalBytesToReceive.Value : null;
                x.State = operation.State switch
                {
                    CoreWebView2DownloadState.Completed => DownloadItemState.Completed,
                    CoreWebView2DownloadState.Interrupted => DownloadItemState.Failed,
                    _ => DownloadItemState.InProgress
                };
            });
        };

        // ★ 导航完成：注入自动化脚本 ★
        core.NavigationCompleted += async (_, args) =>
        {
            tab.IsLoading = false;
            LoadingStateChanged?.Invoke(tab.Id, false);

            // 注入 bermainA11y JS API（每次导航后重新注入，确保 SPA 路由切换也覆盖）
            try
            {
                if (core != null)
                    await core.ExecuteScriptAsync(AutomationScripts.InjectionScript);
            }
            catch (Exception) { /* 容错：注入失败不阻断导航回调 */ }

            var info = new NavigationResultInfo
            {
                Url = core?.Source ?? "",
                IsSuccess = args.IsSuccess,
                HttpStatusCode = (int)args.HttpStatusCode,
                WebErrorStatus = args.WebErrorStatus.ToString()
            };
            NavigationCompleted?.Invoke(tab.Id, info);
        };

        // ★ 文档标题变化 ★
        core.DocumentTitleChanged += (_, _) =>
        {
            var title = core?.DocumentTitle ?? "";
            tab.Title = string.IsNullOrEmpty(title) ? "新标签页" : title;
            TitleChanged?.Invoke(tab.Id, tab.Title);
        };

        // ★ URL 变化（含 SPA history.pushState 触发） ★
        core.SourceChanged += (_, _) =>
        {
            var src = core?.Source ?? "";
            tab.Url = src;
            UrlChanged?.Invoke(tab.Id, src);
        };

        // ★ 新窗口请求 → 转新标签 ★
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            var uri = args.Uri;
            if (!string.IsNullOrWhiteSpace(uri) && uri != "about:blank")
                NewTabRequested?.Invoke(uri);
        };

        // ★ 自动确认/拒绝弹窗 ★
        core.ScriptDialogOpening += (_, args) =>
        {
            if (AutoDismissDialogs)
            {
                // confirm/prompt: Accept；alert: Accept（关闭即可）
                args.Accept();
                if (args.Kind == CoreWebView2ScriptDialogKind.Prompt)
                {
                    // prompt 自动接受时填入默认值（已经是默认行为）
                }
            }
            // 不接受时 args 自动析构 → 等同 Cancel
        };

        // ★ 渲染进程崩溃 ★
        core.ProcessFailed += (_, _) =>
        {
            WebViewCrashed?.Invoke(tab.Id);
        };
    }

    // ====================================================================
    // 释放
    // ====================================================================

    public void Dispose()
    {
        Logger.Info("[Dispose] BrowserHostService 正在释放，关闭所有标签页");
        if (_disposed) return;
        _disposed = true;

        // 复制一份避免遍历中修改
        var ids = _webViews.Keys.ToList();
        foreach (var id in ids)
        {
            if (_webViews.TryGetValue(id, out var wv))
            {
                try { _container.Children.Remove(wv); } catch { }
                try { wv.Dispose(); } catch { }
            }
        }
        _webViews.Clear();
        _activeTabId = null;
        _environment = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BrowserHostService));
    }
}

/// <summary>导航完成事件的载荷</summary>
public class NavigationResultInfo
{
    public string Url { get; set; } = "";
    public bool IsSuccess { get; set; }
    public int HttpStatusCode { get; set; }
    public string? WebErrorStatus { get; set; }
}
