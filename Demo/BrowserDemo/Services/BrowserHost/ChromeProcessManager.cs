using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BrowserDemo.Services.BrowserHost;

/// <summary>
/// Chrome 进程管理器 —— 启动 Chromium 浏览器实例（带 CDP 端口），
/// 保持顶层窗口但无边框，通过 MainWindow 精确覆盖定位。
/// ★ 不调用 SetParent，键盘输入完全正常 ★
/// </summary>
public class ChromeProcessManager : IDisposable
{
    private Process? _chromeProcess;
    private readonly string _userDataDir;
    private bool _disposed;

    /// <summary>CDP 调试端口</summary>
    public int DebugPort { get; } = 9222;

    /// <summary>Chrome 主窗口句柄</summary>
    public IntPtr MainWindowHandle { get; private set; }

    /// <summary>CDP 端点 URL</summary>
    public string CdpEndpointUrl => $"http://localhost:{DebugPort}";

    /// <summary>Chrome 是否正在运行</summary>
    public bool IsRunning => _chromeProcess != null && !_chromeProcess.HasExited;

    /// <summary>初始窗口尺寸</summary>
    public int WindowWidth { get; private set; } = 1280;
    public int WindowHeight { get; private set; } = 720;

    /// <summary>Chrome 窗口原始样式（用于局部修改后恢复部分属性）</summary>
    private int _originalStyle;

    public ChromeProcessManager()
    {
        _userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartAI-Browser-Demo", "chrome-profile");
        Directory.CreateDirectory(_userDataDir);
    }

    /// <summary>
    /// 启动 Chromium 浏览器（headed，带 CDP 端口）。
    /// 启动后剥离窗口装饰，但保持顶层窗口状态（不 SetParent）。
    /// </summary>
    public async Task StartAsync(int x, int y, int width, int height)
    {
        if (IsRunning) return;

        WindowWidth = width;
        WindowHeight = height;

        var chromePath = FindChromeExecutable();
        if (chromePath == null)
            throw new FileNotFoundException("未找到 Chromium 浏览器，请先运行 npx playwright install chromium");

        Logger.Info($"[Chrome] 启动浏览器: {chromePath}");
        Logger.Info($"[Chrome] CDP 端口: {DebugPort}");
        Logger.Info($"[Chrome] 初始位置: ({x},{y}) {width}x{height}");

        var psi = new ProcessStartInfo(chromePath)
        {
            Arguments = $"--remote-debugging-port={DebugPort} " +
                        $"--user-data-dir=\"{_userDataDir}\" " +
                        $"--no-first-run --no-default-browser-check " +
                        $"--window-size={width},{height} " +
                        $"--window-position={x},{y} " +
                        $"--disable-features=TranslateUI,ChromeWhatsNewUI " +
                        $"https://www.bing.com",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _chromeProcess = new Process { StartInfo = psi };
        _chromeProcess.Start();

        _ = Task.Run(() => ReadOutputAsync());

        // 等待窗口出现
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            _chromeProcess.Refresh();

            if (_chromeProcess.HasExited)
                throw new Exception($"Chromium 启动失败，ExitCode={_chromeProcess.ExitCode}");

            // 尝试 MainWindowHandle
            if (_chromeProcess.MainWindowHandle != IntPtr.Zero)
            {
                MainWindowHandle = _chromeProcess.MainWindowHandle;
                Logger.Info($"[Chrome] 窗口就绪 (MainWindowHandle), HWND=0x{MainWindowHandle:X8}");
                StripWindowFrame(MainWindowHandle);
                return;
            }

            // Fallback: FindWindow
            MainWindowHandle = FindChromeWindowByPid(_chromeProcess.Id);
            if (MainWindowHandle != IntPtr.Zero)
            {
                Logger.Info($"[Chrome] 窗口就绪 (FindWindow), HWND=0x{MainWindowHandle:X8}");
                StripWindowFrame(MainWindowHandle);
                return;
            }

            Logger.Debug($"[Chrome] 等待窗口... ({i + 1}/30)");
        }

        throw new TimeoutException("Chromium 窗口在 15 秒内未出现");
    }

    /// <summary>
    /// 剥离窗口装饰（标题栏、边框、系统菜单），但保持顶层窗口状态。
    /// ★ 不设置 WS_CHILD，确保键盘输入正常 ★
    /// </summary>
    private void StripWindowFrame(IntPtr hwnd)
    {
        _originalStyle = GetWindowLong(hwnd, GWL_STYLE);

        // ★★ 关键：只移除装饰，不添加 WS_CHILD ★★
        var style = _originalStyle;
        style = style & ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
        SetWindowLong(hwnd, GWL_STYLE, style);

        // 移除扩展样式中的边框
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        exStyle = exStyle & ~(WS_EX_DLGMODALFRAME | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
        exStyle = exStyle | WS_EX_TOOLWINDOW; // 防止出现在任务栏
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);

        // 刷新
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, WindowWidth, WindowHeight,
            SWP_FRAMECHANGED | SWP_NOZORDER | SWP_NOMOVE);

        Logger.Debug($"[Chrome] 窗口装饰已剥离（保持顶层窗口）");
    }

    /// <summary>移动和调整 Chrome 窗口位置</summary>
    public void MoveWindow(int x, int y, int width, int height, bool repaint = true)
    {
        if (MainWindowHandle == IntPtr.Zero) return;
        WindowWidth = width;
        WindowHeight = height;
        SetWindowPos(MainWindowHandle, HwndTop, x, y, width, height,
            SWP_FRAMECHANGED | (repaint ? 0 : SWP_NOREDRAW));
    }

    /// <summary>最小化 Chrome</summary>
    public void Minimize()
    {
        if (MainWindowHandle != IntPtr.Zero)
            ShowWindow(MainWindowHandle, SW_MINIMIZE);
    }

    /// <summary>还原 Chrome</summary>
    public void Restore()
    {
        if (MainWindowHandle != IntPtr.Zero)
            ShowWindow(MainWindowHandle, SW_RESTORE);
    }

    /// <summary>将其带到前台</summary>
    public void BringToFront()
    {
        if (MainWindowHandle != IntPtr.Zero)
        {
            SetForegroundWindow(MainWindowHandle);
        }
    }

    /// <summary>查找 Chrome 可执行文件</summary>
    private string? FindChromeExecutable()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pwChrome = Path.Combine(localAppData, "ms-playwright", "chromium-1224", "chrome-win64", "chrome.exe");
        if (File.Exists(pwChrome)) return pwChrome;

        var programFiles = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            @"C:\Program Files\Google\Chrome\Application\chrome.exe"
        };
        foreach (var pf in programFiles)
            if (File.Exists(pf)) return pf;

        var edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(edgePath)) return edgePath;

        return null;
    }

    private static IntPtr FindChromeWindowByPid(int pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hwnd, lParam) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid == pid)
            {
                var sb = new StringBuilder(256);
                GetClassName(hwnd, sb, 256);
                if (sb.ToString() == "Chrome_WidgetWin_1" && IsWindowVisible(hwnd))
                {
                    found = hwnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private async Task ReadOutputAsync()
    {
        try { if (_chromeProcess?.StandardOutput != null) await _chromeProcess.StandardOutput.ReadToEndAsync(); } catch { }
        try { if (_chromeProcess?.StandardError != null) await _chromeProcess.StandardError.ReadToEndAsync(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_chromeProcess != null && !_chromeProcess.HasExited)
            {
                // 先发 WM_CLOSE 优雅关闭
                if (MainWindowHandle != IntPtr.Zero)
                    SendMessage(MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                // 等 3 秒再强杀
                _chromeProcess.WaitForExit(3000);
                if (!_chromeProcess.HasExited)
                    _chromeProcess.Kill(entireProcessTree: true);
                _chromeProcess.Dispose();
            }
        }
        catch { }
    }

    // ====================================================================
    // Win32 API
    // ====================================================================

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const int WS_EX_CLIENTEDGE = 0x00000200;
    private const int WS_EX_STATICEDGE = 0x00020000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOREDRAW = 0x0008;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int WM_CLOSE = 0x0010;
    private static readonly IntPtr HwndTop = new IntPtr(0); // HWND_TOP

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}
