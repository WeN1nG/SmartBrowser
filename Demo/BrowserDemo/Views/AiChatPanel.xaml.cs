using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using BrowserDemo.Models;
using BrowserDemo.ViewModels;

namespace BrowserDemo.Views;

public partial class AiChatPanel : UserControl
{
    private ChatViewModel? _vm;
    private readonly DispatcherTimer _statusClearTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5),
        IsEnabled = false
    };
    private bool _isAutoScrolling;

    public AiChatPanel()
    {
        InitializeComponent();
        _statusClearTimer.Tick += (_, _) =>
        {
            if (_vm != null && _vm.StatusMessage != "就绪" && _vm.StatusMessage != "错误"
                && !_vm.StatusMessage.StartsWith("❌") && !_vm.StatusMessage.StartsWith("⚠️"))
            {
                _vm.StatusMessage = "";
            }
            _statusClearTimer.Stop();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachViewModel(DataContext as ChatViewModel);
        ScrollToBottom();
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachViewModel();
        _statusClearTimer.Stop();
    }

    private void AttachViewModel(ChatViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;

        DetachViewModel();
        _vm = vm;
        if (_vm == null) return;

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.Messages.CollectionChanged += OnMessagesCollectionChanged;
        foreach (var message in _vm.Messages)
            message.PropertyChanged += OnMessagePropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_vm == null) return;

        _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
        foreach (var message in _vm.Messages)
            message.PropertyChanged -= OnMessagePropertyChanged;
        _vm = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm == null) return;

        if (e.PropertyName == nameof(ChatViewModel.StatusMessage)
            && !string.IsNullOrEmpty(_vm.StatusMessage))
        {
            _statusClearTimer.Stop();
            _statusClearTimer.Start();
        }
    }

    private void OnMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (ChatMessage message in e.OldItems)
                message.PropertyChanged -= OnMessagePropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (ChatMessage message in e.NewItems)
                message.PropertyChanged += OnMessagePropertyChanged;
        }

        ScrollToBottom();
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatMessage.Content))
            ScrollToBottom();
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyboardDevice.IsKeyDown(Key.LeftShift) || e.KeyboardDevice.IsKeyDown(Key.RightShift))
        {
            // Shift+Enter → 允许换行，不拦截
            return;
        }

        if (e.Key == Key.Enter)
        {
            _vm?.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>自动滚动消息列表到底部（显示最新消息）</summary>
    private void ScrollToBottom()
    {
        if (_isAutoScrolling) return;
        _isAutoScrolling = true;

        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                MessageScroller?.ScrollToBottom();
            }
            finally
            {
                _isAutoScrolling = false;
            }
        }, DispatcherPriority.ContextIdle);
    }
}
