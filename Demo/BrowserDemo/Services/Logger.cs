using System.IO;
using System.Runtime.InteropServices;

namespace BrowserDemo.Services;

/// <summary>日志级别</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>全局日志服务 —— 控制台 + 文件 + 内存缓存</summary>
public static class Logger
{
    private static readonly string LogDir;
    private static readonly string LogFilePath;
    private static readonly object _lock = new();
    private static readonly List<string> _buffer = new();
    private static bool _consoleAllocated;

    /// <summary>日志写入时触发（用于实时 UI 显示）</summary>
    public static event Action<string>? OnLog;

    /// <summary>当前日志级别（低于此级别的不输出）</summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    // 修改计数（满足第4条要求）
    public static int Revision { get; set; } = 5;

    static Logger()
    {
        // 日志目录：项目根目录下的 /Log（从 bin/Debug/net8.0-windows/ 上移三级）
        LogDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\Log"));
        Directory.CreateDirectory(LogDir);
        LogFilePath = Path.Combine(LogDir, $"{DateTime.Now:M-d-H-m-s}.log");

        // 自动删除非最近三天的日志
        CleanOldLogs(TimeSpan.FromDays(3));

        Info("┌──────────────────────────────────────────────┐");
        Info("│  SmartAI Browser Demo  —  Log Started        │");
        Info($"│  {DateTime.Now:yyyy-MM-dd HH:mm:ss}                    │");
        Info($"│  Revision: {Revision}                               │");
        Info("└──────────────────────────────────────────────┘");
    }

    // ========== 核心输出方法 ==========

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Exception(string context, Exception ex)
    {
        Write(LogLevel.Error, $"{context} → {ex.GetType().Name}: {ex.Message}");
        Write(LogLevel.Debug, $"  StackTrace: {ex.StackTrace}");
    }

    // ========== 函数追踪 ==========

    /// <summary>在函数入口调用，返回 IDisposable，using 包裹自动在 exit 打印</summary>
    public static IDisposable Trace(string signature)
    {
        Write(LogLevel.Debug, $"▶ ENTER  {signature}");
        return new TraceBlock(signature);
    }

    private sealed class TraceBlock : IDisposable
    {
        private readonly string _sig;
        private readonly DateTime _start;
        public TraceBlock(string sig) { _sig = sig; _start = DateTime.Now; }
        public void Dispose()
        {
            var elapsed = (DateTime.Now - _start).TotalMilliseconds;
            Write(LogLevel.Debug, $"◀ EXIT   {_sig}  ({elapsed:F1}ms)");
        }
    }

    // ========== 分配控制台 ==========

    public static void AllocConsole()
    {
        if (_consoleAllocated) return;
        _consoleAllocated = NativeMethods.AllocConsole();
        if (_consoleAllocated)
        {
            Console.Title = "SmartAI Browser — Debug Console";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("═══ SmartAI Browser Debug Console ═══");
            Console.ResetColor();
            Info("Win32 控制台已分配");
        }
    }

    // ========== 内部实现 ==========

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var levelStr = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            _ => "???"
        };
        var line = $"[{timestamp}][{levelStr}] {message}";

        lock (_lock)
        {
            // 内存缓存
            _buffer.Add(line);

            // 控制台输出
            if (_consoleAllocated)
            {
                var currentColor = level switch
                {
                    LogLevel.Debug => ConsoleColor.Gray,
                    LogLevel.Info => ConsoleColor.White,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };
                Console.ForegroundColor = currentColor;
                Console.WriteLine(line);
                Console.ResetColor();
            }

            // 文件追加
            try { File.AppendAllText(LogFilePath, line + Environment.NewLine); }
            catch { /* 忽略 IO 错误 */ }
        }

        // 通知 UI 订阅者
        OnLog?.Invoke(line);
    }

    /// <summary>获取缓存的日志行</summary>
    public static string[] GetBuffer() { lock (_lock) return _buffer.ToArray(); }

    /// <summary>自动删除非最近指定天数的日志文件</summary>
    private static void CleanOldLogs(TimeSpan maxAge)
    {
        try
        {
            var cutoff = DateTime.Now - maxAge;
            var deleted = 0;
            foreach (var f in Directory.GetFiles(LogDir, "*.log"))
            {
                try
                {
                    if (File.GetCreationTime(f) < cutoff)
                    {
                        File.Delete(f);
                        deleted++;
                    }
                }
                catch { /* 跳过无法访问的文件 */ }
            }
            if (deleted > 0)
                Info($"/Log 自动清理: 删除了 {deleted} 个超过 {maxAge.TotalDays:F0} 天的旧日志文件");
        }
        catch { /* 忽略目录不存在等错误 */ }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FreeConsole();
    }
}
