using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using BrowserDemo.Services;
using BrowserDemo.ViewModels;

namespace BrowserDemo.Models;

public class BrowserViewModel : INotifyPropertyChanged
{
    private TabInfo? _activeTab;
    private string _addressText = string.Empty;
    private bool _canGoBack;
    private bool _canGoForward;
    private string _statusText = "就绪";

    public ObservableCollection<TabInfo> Tabs { get; } = new();
    public ObservableCollection<BookmarkInfo> Bookmarks { get; } = new();
    public ObservableCollection<HistoryInfo> History { get; } = new();

    public TabInfo? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab != value)
            {
                if (_activeTab != null) _activeTab.IsActive = false;
                _activeTab = value;
                if (_activeTab != null) _activeTab.IsActive = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTabs));
                SyncAddressBar();
            }
        }
    }

    public string AddressText
    {
        get => _addressText;
        set { _addressText = value; OnPropertyChanged(); }
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        set { _canGoBack = value; OnPropertyChanged(); }
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        set { _canGoForward = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool HasTabs => Tabs.Count > 0;
    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasHistory => History.Count > 0;

    // AI 对话 ViewModel
    public ChatViewModel Chat { get; } = new();

    // 命令
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand GoForwardCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DownloadCommand { get; }
    public ICommand ActivateTabCommand { get; }
    public ICommand AddBookmarkCommand { get; }
    public ICommand OpenBookmarkCommand { get; }
    public ICommand ShowHistoryCommand { get; }
    public ICommand OpenHistoryCommand { get; }

    // 导航事件 — View 订阅
    public event Action<string>? NavigateRequested;
    public event Action? GoBackRequested;
    public event Action? GoForwardRequested;
    public event Action? RefreshRequested;
    public event Action? DownloadRequested;
    public event Action? ShowHistoryRequested;
    public event Action<Guid>? TabActivated;
    public event Action<Guid>? TabClosed;

    public BrowserViewModel()
    {
        using var _ = Logger.Trace("BrowserViewModel::ctor");

        NewTabCommand = new RelayCommand(_ => AddNewTab());
        CloseTabCommand = new RelayCommand(id => CloseTab(id));
        NavigateCommand = new RelayCommand(_ => NavigateToAddress());
        GoBackCommand = new RelayCommand(_ => GoBackRequested?.Invoke());
        GoForwardCommand = new RelayCommand(_ => GoForwardRequested?.Invoke());
        RefreshCommand = new RelayCommand(_ => RefreshRequested?.Invoke());
        DownloadCommand = new RelayCommand(_ => DownloadRequested?.Invoke());
        ActivateTabCommand = new RelayCommand(id => ActivateTab(id));
        AddBookmarkCommand = new RelayCommand(_ => AddCurrentPageToBookmarks());
        OpenBookmarkCommand = new RelayCommand(bookmark => OpenBookmark(bookmark));
        ShowHistoryCommand = new RelayCommand(_ => ShowHistoryRequested?.Invoke());
        OpenHistoryCommand = new RelayCommand(history => OpenHistory(history));

        foreach (var bookmark in BookmarkService.LoadBookmarks())
            Bookmarks.Add(bookmark);
        Bookmarks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBookmarks));

        foreach (var item in HistoryService.LoadHistory())
            History.Add(item);
        History.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHistory));

        // 默认打开一页
        AddNewTab("https://www.bing.com");
        Logger.Info("BrowserViewModel 初始完毕");
    }

    public TabInfo AddNewTab(string? url = null)
    {
        Logger.Info($"新建标签: url=\"{url}\"");
        var tab = new TabInfo
        {
            Url = url ?? "https://www.bing.com",
            Title = url ?? "新标签页"
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        Logger.Debug($"标签 {tab.Id} 已创建，当前共 {Tabs.Count} 个标签");
        return tab;
    }

    public void CloseTab(object? id)
    {
        if (id is not Guid guid)
        {
            Logger.Warning($"CloseTab 收到无效参数: {id?.GetType().Name ?? "null"}");
            return;
        }
        var tab = Tabs.FirstOrDefault(t => t.Id == guid);
        if (tab == null)
        {
            Logger.Warning($"CloseTab: 标签 {guid} 不存在");
            return;
        }

        Logger.Info($"关闭标签: \"{tab.Title}\" ({guid})");
        var idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            ActiveTab = null;
            AddressText = string.Empty;
            Logger.Debug("所有标签已关闭");
        }
        else if (tab.IsActive)
        {
            ActiveTab = Tabs[Math.Min(idx, Tabs.Count - 1)];
            Logger.Debug($"自动切换到标签 {ActiveTab.Title}");
        }
        TabClosed?.Invoke(guid);
    }

    public void ActivateTab(object? id)
    {
        if (id is not Guid guid) return;
        var tab = Tabs.FirstOrDefault(t => t.Id == guid);
        if (tab != null)
        {
            Logger.Debug($"激活标签: \"{tab.Title}\" ({guid})");
            ActiveTab = tab;
        }
        TabActivated?.Invoke(guid);
    }

    public void NavigateToAddress()
    {
        if (ActiveTab == null)
        {
            Logger.Warning("NavigateToAddress: 无激活标签");
            return;
        }

        var url = AddressText.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        // 智能补全 URL
        if (!url.Contains("://") && !url.StartsWith("about:"))
            url = url.Contains('.') ? "https://" + url : "https://www.google.com/search?q=" + Uri.EscapeDataString(url);

        Logger.Info($"导航到: {url}");
        ActiveTab.Url = url;
        NavigateRequested?.Invoke(url);
    }

    public void AddCurrentPageToBookmarks()
    {
        Logger.Debug("[AddCurrentPageToBookmarks] 尝试收藏当前页面");
        if (ActiveTab == null)
        {
            StatusText = "没有可收藏的页面";
            return;
        }

        var url = ActiveTab.Url.Trim();
        if (string.IsNullOrWhiteSpace(url) || string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "当前页面无法收藏";
            return;
        }

        if (Bookmarks.Any(x => string.Equals(x.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "该页面已在收藏夹中";
            return;
        }

        var title = ActiveTab.Title.Trim();
        if (string.IsNullOrWhiteSpace(title) || title == "新标签页")
            title = url;

        var bookmark = new BookmarkInfo
        {
            Title = title,
            Url = url,
            CreatedAt = DateTime.Now
        };
        Bookmarks.Add(bookmark);

        if (BookmarkService.SaveBookmarks(Bookmarks))
            StatusText = $"已收藏：{title}";
        else
            StatusText = "收藏保存失败";
    }

    public void OpenBookmark(object? parameter)
    {
        var url = parameter switch
        {
            BookmarkInfo bookmark => bookmark.Url,
            string text => text,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url)) return;

        Logger.Info($"打开书签: {url}");
        if (ActiveTab == null)
        {
            AddNewTab(url);
        }
        else
        {
            AddressText = url;
            ActiveTab.Url = url;
            NavigateRequested?.Invoke(url);
        }
        StatusText = $"打开收藏：{url}";
    }

    /// <summary>记录一条历史记录（去重、最多保留 500 条）</summary>
    public void RecordHistoryEntry(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        // 如果最后一条就是同一 URL，移除后重新插入（避免连续记录相同页面，同时刷新 UI）
        var existing = History.FirstOrDefault();
        if (existing != null && existing.Url == url)
            History.RemoveAt(0);

        var entry = new HistoryInfo
        {
            Title = string.IsNullOrEmpty(title) ? url : title,
            Url = url,
            VisitedAt = DateTime.Now
        };
        History.Insert(0, entry);

        // 自动限容
        while (History.Count > 500)
            History.RemoveAt(History.Count - 1);
    }

    public void OpenHistory(object? parameter)
    {
        var url = parameter switch
        {
            HistoryInfo h => h.Url,
            string text => text,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(url)) return;

        Logger.Info($"打开历史记录: {url}");
        if (ActiveTab == null)
        {
            AddNewTab(url);
        }
        else
        {
            AddressText = url;
            ActiveTab.Url = url;
            NavigateRequested?.Invoke(url);
        }
        StatusText = $"打开历史记录：{url}";
    }

    public void SyncAddressBar()
    {
        if (ActiveTab != null)
            AddressText = ActiveTab.Url;
        else
            AddressText = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>简单 ICommand 实现</summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter)
    {
        Logger.Debug($"RelayCommand.Execute: {_execute.Method.Name}({parameter})");
        _execute(parameter);
    }
}
