using System.Windows;
using BrowserDemo.Services;
using BrowserDemo.ViewModels;

namespace BrowserDemo.Views;

/// <summary>
/// AI 助手副窗口 —— 显示在浏览器主窗口右侧
/// </summary>
public partial class AiSecondaryWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly ChatViewModel _chatVm;
    private bool _allowClose;

    public AiSecondaryWindow(MainWindow owner, ChatViewModel chatVm)
    {
        using var _ = Logger.Trace("AiSecondaryWindow::ctor");

        InitializeComponent();

        _mainWindow = owner;
        _chatVm = chatVm;
        Owner = owner;
        DataContext = chatVm;

        // 窗口关闭时仅隐藏而非真正关闭（保持对话状态）
        Closing += OnClosing;

        Loaded += OnWindowLoaded;

        Logger.Info("AiSecondaryWindow 初始化完毕");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs args)
    {
        if (_allowClose)
        {
            Logger.Debug("AiSecondaryWindow 允许真正关闭（Owner 关闭中）");
            return;
        }

        Logger.Debug("AiSecondaryWindow 关闭按钮被点击 → 隐藏窗口");
        args.Cancel = true;
        _chatVm.IsAiPanelVisible = false;
        Hide();
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        Logger.Debug("AiSecondaryWindow 已加载，开始定位");
        try
        {
            PositionNextToMainWindow();
        }
        catch (Exception ex)
        {
            Logger.Warning($"副窗口定位失败: {ex.Message}");
            // 兜底：显示在屏幕中央
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    /// <summary>设置允许真正关闭的标记（Owner 关闭时调用此方法再 Close）</summary>
    public void AllowClose()
    {
        _allowClose = true;
    }

    /// <summary>
    /// 将副窗口定位到主窗口右侧，与主窗口顶部对齐
    /// </summary>
    public void PositionNextToMainWindow()
    {
        using var _ = Logger.Trace("AiSecondaryWindow::PositionNextToMainWindow");

        try
        {
            if (_mainWindow.WindowState == WindowState.Maximized)
            {
                var workArea = SystemParameters.WorkArea;
                Left = workArea.Right - Width - 8;
                Top = workArea.Top + 8;
                Height = workArea.Height - 16;
            }
            else
            {
                Left = _mainWindow.Left + _mainWindow.Width + 2;
                Top = _mainWindow.Top;
                Height = _mainWindow.ActualHeight;
            }

            Logger.Debug($"定位: Left={Left:F0} Top={Top:F0} Width={Width:F0} Height={Height:F0}");
        }
        catch (Exception ex)
        {
            Logger.Warning($"定位计算失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 显示副窗口
    /// </summary>
    public new void Show()
    {
        using var _ = Logger.Trace("AiSecondaryWindow::Show");

        try
        {
            PositionNextToMainWindow();
            base.Show();
            Activate();
            Logger.Info("AI 副窗口已显示");
        }
        catch (Exception ex)
        {
            Logger.Exception("副窗口 Show 失败", ex);
        }
    }

    /// <summary>
    /// 隐藏副窗口
    /// </summary>
    public new void Hide()
    {
        using var _ = Logger.Trace("AiSecondaryWindow::Hide");

        try
        {
            base.Hide();
            Logger.Debug("AI 副窗口已隐藏");
        }
        catch (Exception ex)
        {
            Logger.Exception("副窗口 Hide 失败", ex);
        }
    }
}
