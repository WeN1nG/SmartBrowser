using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BrowserDemo.Services;

namespace BrowserDemo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 分配后台控制台 —— 所有日志输出到此窗口
        Logger.AllocConsole();
        Logger.Info("═══════════════════════════════════════");
        Logger.Info("  SmartAI Browser Demo 启动");
        Logger.Info($"  时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Logger.Info($"  .NET: {Environment.Version}");
        Logger.Info("═══════════════════════════════════════");

        // 显示启动参数
        if (e.Args.Length > 0)
            Logger.Debug($"启动参数: {string.Join(" ", e.Args)}");

        base.OnStartup(e);

        Logger.Info("UI 主窗口已创建");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info("═══════════════════════════════════════");
        Logger.Info("  应用退出");
        Logger.Info($"  修订版本: {Logger.Revision}");
        Logger.Info($"  退出代码: {e.ApplicationExitCode}");
        Logger.Info("═══════════════════════════════════════");

        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Exception("UI 线程未处理异常", e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Exception("AppDomain 未处理异常", ex);
        else
            Logger.Error($"AppDomain 未处理异常: {e.ExceptionObject}");
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Logger.Exception("未观察到的 Task 异常", e.Exception);
        e.SetObserved();
    }
}
