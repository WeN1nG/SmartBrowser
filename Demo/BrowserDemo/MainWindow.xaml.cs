using System.IO;
using System.Windows;
using System.Windows.Input;
using BrowserDemo.Models;
using BrowserDemo.Services;
using BrowserDemo.Services.Automation;
using BrowserDemo.Services.BrowserHost;
using BrowserDemo.ViewModels;
using BrowserDemo.Views;

namespace BrowserDemo;

public partial class MainWindow : Window
{
    private readonly BrowserViewModel _vm;
    private AiSecondaryWindow? _aiWindow;
    private DownloadsWindow? _downloadsWindow;

    // ★★ V3 新架构：BrowserHostService 取代 ChromeProcessManager ★★
    private BrowserHostService? _browserHost;
    private BrowserAutomationService? _automation;

    public MainWindow()
    {
        using var _ = Logger.Trace("MainWindow::ctor");

        InitializeComponent();
        _vm = new BrowserViewModel();
        DataContext = _vm;

        _vm.NavigateRequested += OnNavigateRequested;
        _vm.GoBackRequested += OnGoBack;
        _vm.GoForwardRequested += OnGoForward;
        _vm.RefreshRequested += OnRefresh;
        _vm.DownloadRequested += ShowDownloadsWindow;
        _vm.TabClosed += OnTabClosed;
        _vm.TabActivated += OnTabActivated;
        _vm.PropertyChanged += OnVmPropertyChanged;

        _vm.Tabs.CollectionChanged += (_, args) =>
        {
            Logger.Debug($"Tabs 集合变化: {args.Action} → 共 {_vm.Tabs.Count} 个标签");
            UpdateTabCount();
        };

        _vm.Chat.OpenSettingsRequested += OpenAiSettings;
        _vm.Chat.PropertyChanged += OnChatPropertyChanged;

        Loaded += OnLoaded;

        // 主窗口关闭 → 清理全部资源
        Closing += (_, _) =>
        {
            _vm.Chat.DetachAutomationRouter();
            if (_vm.Chat.SkillSystem.IsInitialized)
                _vm.Chat.SkillSystem.Shutdown();
            _automation?.Dispose();
            _browserHost?.Dispose();
            if (_aiWindow != null && _aiWindow.IsVisible)
            {
                _aiWindow.AllowClose();
                _aiWindow.Close();
            }
            if (_downloadsWindow != null && _downloadsWindow.IsVisible)
            {
                _downloadsWindow.AllowClose();
                _downloadsWindow.Close();
            }
        };

        Logger.Info($"MainWindow 初始完毕，{_vm.Tabs.Count} 个默认标签");
    }

    // ====================================================================
    // 初始化（V3：BrowserHost + Automation 替代 Chrome 启动）
    // ====================================================================

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        using var _ = Logger.Trace("MainWindow::OnLoaded");

        try
        {
            Logger.Info("初始化 BrowserHostService（WebView2 内嵌模式）...");

            _browserHost = new BrowserHostService(Dispatcher, ContentArea)
            {
                // 用本地个人设置：cookie / 登录 / 历史持久化到 %LocalAppData%
                // 路径与旧 Chrome 路径（chrome-profile）平行，便于将来人工迁移
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SmartAI-Browser-Demo",
                    "webview2-profile")
            };
            await _browserHost.InitializeAsync();

            _automation = new BrowserAutomationService();
            _automation.Initialize(Dispatcher);
            _browserHost.Automation = _automation;

            // BrowserHost 事件 → BrowserAutomation 转发 + 同步 ViewModel
            WireBrowserHostEvents();

            // 为已有的初始 Tab 创建 WebView2（BrowserViewModel ctor 里就 AddNewTab 了一个）
            foreach (var tab in _vm.Tabs)
                await EnsureTabWebViewAsync(tab);

            if (_vm.ActiveTab != null)
                ActivateTabWebView(_vm.ActiveTab.Id);

            _vm.Chat.AttachAutomationRouter(new BrowserAutomationToolRouter(_automation));

            _vm.StatusText = "就绪 — 浏览器已嵌入，AI 浏览器工具已启用";
            Logger.Info("BrowserHostService 初始化完成，WebView2 自动化工具已挂载");

            // Phase 4b：不再调用 _vm.Chat.SetChromeCdpEndpoint，AI browser_* 工具直接走 WebView2 Automation。
        }
        catch (Exception ex)
        {
            Logger.Exception("浏览器初始化失败", ex);
            _vm.StatusText = $"浏览器初始化失败：{ex.Message}";
            MessageBox.Show($"浏览器初始化失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ====================================================================
    // BrowserHost 事件 → BrowserAutomation 转发 + 同步 VM/UI
    // ====================================================================

    private void WireBrowserHostEvents()
    {
        if (_browserHost == null) return;

        _browserHost.NavigationStarting += (tabId, url) =>
        {
            Dispatcher.Invoke(() =>
            {
                LoadingBar.Visibility = Visibility.Visible;
                LoadingBar.IsIndeterminate = true;
                _vm.StatusText = $"正在加载 {url} …";
                _automation?.NotifyLoadingStateChanged(tabId, true);
            });
        };

        _browserHost.NavigationCompleted += (tabId, info) =>
        {
            Dispatcher.Invoke(() =>
            {
                LoadingBar.IsIndeterminate = false;
                LoadingBar.Visibility = Visibility.Collapsed;
                _vm.StatusText = info.IsSuccess ? "完成" : "加载失败";
                UpdateNavButtons(tabId);

                if (_vm.ActiveTab?.Id == tabId)
                {
                    _vm.SyncAddressBar();
                    _vm.Chat.CurrentPageUrl = info.Url;
                }

                _automation?.NotifyNavigationCompleted(tabId, new NavigationEventInfo
                {
                    Url = info.Url,
                    IsSuccess = info.IsSuccess,
                    HttpStatusCode = info.HttpStatusCode,
                    WebErrorStatus = info.WebErrorStatus
                });
                _automation?.NotifyLoadingStateChanged(tabId, false);
            });
        };

        _browserHost.TitleChanged += (tabId, title) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.ActiveTab?.Id == tabId)
                    _vm.Chat.CurrentPageTitle = title;
                _automation?.NotifyTitleChanged(tabId, title);
            });
        };

        _browserHost.UrlChanged += (tabId, url) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm.ActiveTab?.Id == tabId)
                {
                    _vm.Chat.CurrentPageUrl = url;
                    _vm.SyncAddressBar();
                }
                _automation?.NotifyUrlChanged(tabId, url);
            });
        };

        _browserHost.NewTabRequested += url =>
        {
            Dispatcher.Invoke(() => _vm.AddNewTab(url));
        };

        _browserHost.WebViewCrashed += tabId =>
        {
            Logger.Error($"WebView2 标签 {tabId} 进程崩溃");
            Dispatcher.Invoke(() =>
            {
                _vm.StatusText = "浏览器进程崩溃，已关闭异常标签";
                _automation?.NotifyWebViewCrashed(tabId);
                _vm.CloseTab(tabId);
            });
        };

        _browserHost.TabClosed += tabId =>
        {
            _automation?.UnbindWebView(tabId);
        };
    }

    // ====================================================================
    // 标签 → WebView2 生命周期
    // ====================================================================

    private async Task EnsureTabWebViewAsync(TabInfo tab)
    {
        if (_browserHost == null) return;
        if (_browserHost.GetWebViewForTab(tab.Id) != null) return;

        try
        {
            // 复用 VM 的 TabInfo（保留同一个 Id），BrowserHostService 会就地把 WebView2 与该 Id 绑定
            await _browserHost.CreateTabForAsync(tab, tab.Url);

            var wv = _browserHost.GetWebViewForTab(tab.Id);
            if (wv != null)
                _automation?.BindWebView(tab.Id, wv);
        }
        catch (Exception ex)
        {
            Logger.Exception($"创建标签 {tab.Id} 的 WebView 失败", ex);
            _vm.StatusText = $"标签初始化失败：{ex.Message}";
        }
    }

    private void ActivateTabWebView(Guid tabId)
    {
        if (_browserHost == null) return;
        _browserHost.ActivateTab(tabId);
        _automation?.SwitchToTab(tabId);
        UpdateNavButtons(tabId);
    }

    private void UpdateNavButtons(Guid tabId)
    {
        if (_browserHost?.GetWebViewForTab(tabId)?.CoreWebView2 is { } core)
        {
            _vm.CanGoBack = core.CanGoBack;
            _vm.CanGoForward = core.CanGoForward;
        }
    }

    // ====================================================================
    // 导航事件（来自 ViewModel）
    // ====================================================================

    private void OnNavigateRequested(string url)
    {
        Logger.Info($"导航请求: {url}");
        var tab = _vm.ActiveTab;
        if (tab == null || _browserHost == null) return;
        var wv = _browserHost.GetWebViewForTab(tab.Id);
        try { wv?.CoreWebView2?.Navigate(url); }
        catch (Exception ex) { Logger.Exception($"Navigate({url}) 失败", ex); }
    }

    private void OnGoBack()
    {
        var tab = _vm.ActiveTab;
        if (tab == null || _browserHost == null) return;
        _browserHost.GetWebViewForTab(tab.Id)?.CoreWebView2?.GoBack();
    }

    private void OnGoForward()
    {
        var tab = _vm.ActiveTab;
        if (tab == null || _browserHost == null) return;
        _browserHost.GetWebViewForTab(tab.Id)?.CoreWebView2?.GoForward();
    }

    private void OnRefresh()
    {
        var tab = _vm.ActiveTab;
        if (tab == null || _browserHost == null) return;
        _browserHost.GetWebViewForTab(tab.Id)?.CoreWebView2?.Reload();
    }

    // ====================================================================
    // 标签激活/关闭
    // ====================================================================

    private async void OnTabActivated(Guid id)
    {
        if (_browserHost == null) return;

        // 如果该标签还没有 WebView2，先创建
        if (_browserHost.GetWebViewForTab(id) == null)
        {
            var tab = _vm.Tabs.FirstOrDefault(t => t.Id == id);
            if (tab != null) await EnsureTabWebViewAsync(tab);
        }

        ActivateTabWebView(id);

        var wv = _browserHost.GetWebViewForTab(id);
        if (wv?.CoreWebView2 != null)
        {
            _vm.Chat.CurrentPageUrl = wv.CoreWebView2.Source;
            _vm.Chat.CurrentPageTitle = wv.CoreWebView2.DocumentTitle;
        }
        else
        {
            var tab = _vm.Tabs.FirstOrDefault(t => t.Id == id);
            if (tab != null)
            {
                _vm.Chat.CurrentPageUrl = tab.Url;
                _vm.Chat.CurrentPageTitle = tab.Title;
            }
        }

        UpdateTabCount();
    }

    private async void OnTabClosed(Guid id)
    {
        if (_browserHost != null && _browserHost.GetWebViewForTab(id) != null)
            await _browserHost.CloseTabAsync(id);
        UpdateTabCount();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BrowserViewModel.ActiveTab) && _vm.ActiveTab != null)
            OnTabActivated(_vm.ActiveTab.Id);
    }

    // ====================================================================
    // AI 副窗口
    // ====================================================================

    private void OnChatPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.IsAiPanelVisible))
        {
            if (_vm.Chat.IsAiPanelVisible)
                ShowAiWindow();
            else
                HideAiWindow();
        }
    }

    private void ShowAiWindow()
    {
        using var _ = Logger.Trace("MainWindow::ShowAiWindow");

        try
        {
            if (_aiWindow == null)
                _aiWindow = new AiSecondaryWindow(this, _vm.Chat);

            if (_aiWindow.IsVisible)
            {
                _aiWindow.Activate();
                return;
            }

            _aiWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Exception("显示 AI 副窗口失败", ex);
            var result = MessageBox.Show(
                $"AI 副窗口显示失败：{ex.Message}\n\n是否移除 Owner 关系？",
                "副窗口错误", MessageBoxButton.YesNo, MessageBoxImage.Error);
            if (result == MessageBoxResult.Yes && _aiWindow != null)
            {
                _aiWindow.Owner = null;
                try { _aiWindow.Show(); } catch { }
            }
        }
    }

    private void HideAiWindow()
    {
        if (_aiWindow != null && _aiWindow.IsVisible)
            _aiWindow.Hide();
    }

    private void ShowDownloadsWindow()
    {
        try
        {
            _downloadsWindow ??= new DownloadsWindow(this);

            if (_downloadsWindow.IsVisible)
            {
                _downloadsWindow.Activate();
                return;
            }

            _downloadsWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Exception("显示下载窗口失败", ex);
        }
    }

    // ====================================================================
    // AI 设置对话框
    // ====================================================================

    private void OpenAiSettings()
    {
        Logger.Info("打开 AI 模型选择对话框");
        var dialog = new AiModelSelectionDialog(_vm.Chat, this);
        dialog.ShowDialog();
    }

    // ====================================================================
    // UI 帮助方法
    // ====================================================================

    private void UpdateTabCount()
    {
        TabCountText.Text = $"{_vm.Tabs.Count} 个标签页";
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _vm.NavigateToAddress();
            e.Handled = true;
        }
    }
}
