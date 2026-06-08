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

    // 导航事件 — View 订阅
    public event Action<string>? NavigateRequested;
    public event Action? GoBackRequested;
    public event Action? GoForwardRequested;
    public event Action? RefreshRequested;
    public event Action? DownloadRequested;
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
