using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace BrowserDemo.Services.Automation;

/// <summary>
/// ADB（Android Debug Bridge）服务 —— 通过 adb 命令与连接的 Android 设备交互，
/// 主要功能：获取手机短信验证码，用于辅助浏览器端手机号登录。
///
/// 前置条件：
///   1. 电脑安装 Android SDK Platform Tools（adb 在 PATH 中可用）
///   2. 手机开启「开发者选项」→「USB 调试」
///   3. 手机通过 USB 连接电脑，并授权调试
/// </summary>
public class AdbService
{
    private readonly string? _adbPath;
    private static readonly Regex SmsCodePattern = new(@"\b(\d{4,8})\b", RegexOptions.Compiled);
    private static readonly Regex VerificationPattern = new(
        @"(验证码|校验码|确认码|动态码|校验|code|verif|验证|check|确认)\D{0,10}?(\d{4,8})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>最近一次执行的 adb 命令的标准输出</summary>
    public string? LastStdOut { get; private set; }

    /// <summary>最近一次执行的 adb 命令的错误输出</summary>
    public string? LastStdErr { get; private set; }

    public AdbService(string? adbPath = null)
    {
        _adbPath = adbPath ?? FindAdb();
        Logger.Debug($"AdbService: 使用 adb 路径 = {(_adbPath ?? "null")}");
    }

    /// <summary>自动查找 adb 可执行文件</summary>
    private static string? FindAdb()
    {
        var adbName = IsWindows() ? "adb.exe" : "adb";

        // 1. 项目自带的 Tools\platform-tools\（最高优先级）
        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(exeDir))
        {
            // 逐级向上查找（处理 bin/Debug/net8.0-windows 等多层输出问题）
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Tools", "platform-tools", adbName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        // 2. 环境变量 PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in pathVar.Split(Path.PathSeparator))
        {
            var full = Path.Combine(p.Trim(), adbName);
            if (File.Exists(full)) return full;
        }

        // 3. 常见 Android SDK 安装路径
        var candidates = IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Android", "Sdk", "platform-tools", adbName),
                @"C:\Android\platform-tools\" + adbName,
                @"C:\adb\" + adbName
            }
            : new[]
            {
                "/usr/local/bin/adb",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Android", "Sdk", "platform-tools", "adb")
            };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        Logger.Warning("AdbService: 未在任何路径找到 adb。将 Tools\\platform-tools 放在项目目录下即可自动发现。");
        return null;
    }

    // ====================================================================
    // 公共 API
    // ====================================================================

    /// <summary>检查 ADB 是否可用且有设备连接</summary>
    public async Task<(bool Available, string? DeviceId, string? Error)> CheckDeviceAsync(CancellationToken ct = default)
    {
        using var _ = Logger.Trace("AdbService::CheckDeviceAsync");

        if (_adbPath == null)
            return (false, null, "未找到 adb 可执行文件。请安装 Android SDK Platform Tools 并将 adb 加入 PATH。");

        // 执行 adb devices
        var (ok, stdout, stderr) = await RunAdbAsync("devices", ct);
        if (!ok)
            return (false, null, $"adb 执行失败: {stderr}");

        // 解析设备列表（跳过第一行 "List of devices attached"）
        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines.Skip(1))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var parts = trimmed.Split('\t');
            if (parts.Length >= 2 && parts[1] == "device")
            {
                Logger.Info($"AdbService: 检测到设备 {parts[0]}");
                return (true, parts[0], null);
            }
            if (parts.Length >= 2 && parts[1] == "unauthorized")
            {
                return (false, null, "设备未授权此电脑的调试请求。请在手机上确认「允许 USB 调试」对话框。");
            }
        }

        return (false, null, "未检测到已连接的 Android 设备。请用 USB 连接手机并开启「USB 调试」。");
    }

    /// <summary>
    /// 获取最近的短信列表。
    /// 从系统短信数据库中读取，返回包含发送方、内容、时间的列表。
    /// </summary>
    /// <param name="limit">获取条数，默认 10</param>
    /// <param name="ct">取消令牌</param>
    public async Task<(bool Success, List<SmsMessage> Messages, string? Error)> GetRecentSmsAsync(
        int limit = 10, CancellationToken ct = default)
    {
        using var _ = Logger.Trace($"AdbService::GetRecentSmsAsync(limit={limit})");

        // 方法1：通过 content provider 查询短信数据库
        var (ok, stdout, stderr) = await RunAdbAsync(
            $"shell content query --uri content://sms/inbox " +
            $"--projection address:body:date --sort \"date DESC\" " +
            $"--limit {limit}",
            ct);

        if (ok && !string.IsNullOrWhiteSpace(stdout))
        {
            var messages = ParseSmsContentQuery(stdout);
            if (messages.Count > 0)
            {
                Logger.Info($"AdbService: 从 content://sms 获取到 {messages.Count} 条短信");
                return (true, messages, null);
            }
        }

        // 方法2：通过 dumpsys notification 解析通知中的短信预览
        Logger.Debug("AdbService: content://sms 失败，尝试 dumpsys notification");
        (ok, stdout, stderr) = await RunAdbAsync(
            "shell dumpsys notification --noredact | grep -iE \"(sms|验证码|code|verif|text=|tickerText)\"",
            ct);

        if (ok && !string.IsNullOrWhiteSpace(stdout))
        {
            var messages = ParseNotificationDumpsys(stdout);
            if (messages.Count > 0)
            {
                Logger.Info($"AdbService: 从 dumpsys notification 获取到 {messages.Count} 条短信通知");
                return (true, messages, null);
            }
        }

        return (false, new(), "无法获取短信数据。请确认手机已授权短信读取权限。");
    }

    /// <summary>
    /// 等待并获取最新的验证码。
    /// 持续轮询短信数据库，直到发现包含验证码模式的新短信或超时。
    /// </summary>
    /// <param name="timeoutMs">超时时间（毫秒），默认 60 秒</param>
    /// <param name="pollIntervalMs">轮询间隔（毫秒），默认 2000</param>
    /// <param name="senderFilter">发送方过滤（如特定号码或关键词），可选</param>
    /// <param name="ct">取消令牌</param>
    public async Task<(bool Success, SmsMessage? Message, string? Code, string? Error)> WaitForVerificationCodeAsync(
        int timeoutMs = 60000, int pollIntervalMs = 2000, string? senderFilter = null, CancellationToken ct = default)
    {
        using var _ = Logger.Trace($"AdbService::WaitForVerificationCodeAsync(timeout={timeoutMs}ms)");
        Logger.Info($"开始等待短信验证码 (timeout={timeoutMs}ms, poll={pollIntervalMs}ms)");

        var sw = Stopwatch.StartNew();
        var seenIds = new HashSet<string>();

        while (sw.ElapsedMilliseconds < timeoutMs && !ct.IsCancellationRequested)
        {
            var (success, messages, _) = await GetRecentSmsAsync(10, ct);

            if (success)
            {
                foreach (var msg in messages)
                {
                    // 跳过已处理的短信
                    var id = $"{msg.Address}|{msg.Body}|{msg.Date}";
                    if (!seenIds.Add(id)) continue;

                    // 发送方过滤
                    if (!string.IsNullOrEmpty(senderFilter) &&
                        !msg.Address.Contains(senderFilter, StringComparison.OrdinalIgnoreCase) &&
                        !msg.Body.Contains(senderFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 查找验证码
                    var code = ExtractVerificationCode(msg.Body);
                    if (code != null)
                    {
                        Logger.Info($"AdbService: 发现验证码 [{code}] 来自 {msg.Address} (耗时 {sw.ElapsedMilliseconds}ms)");
                        return (true, msg, code, null);
                    }

                    // 也尝试匹配纯数字（非验证码场景）
                    var match = SmsCodePattern.Match(msg.Body);
                    if (match.Success && IsLikelyVerificationCode(msg.Body))
                    {
                        Logger.Info($"AdbService: 疑似验证码 [{match.Groups[1].Value}] 来自 {msg.Address}");
                        return (true, msg, match.Groups[1].Value, null);
                    }
                }
            }

            try { await Task.Delay(pollIntervalMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        return (false, null, null,
            ct.IsCancellationRequested ? "等待已取消" :
            $"超时 ({timeoutMs / 1000}s)，未收到验证码短信");
    }

    /// <summary>获取手机基本信息（型号、Android 版本等）</summary>
    public async Task<(bool Success, Dictionary<string, string> Info, string? Error)> GetPhoneInfoAsync(
        CancellationToken ct = default)
    {
        using var _ = Logger.Trace("AdbService::GetPhoneInfoAsync");

        var info = new Dictionary<string, string>();
        var props = new[] { "ro.product.model", "ro.build.version.release", "ro.product.manufacturer" };

        foreach (var prop in props)
        {
            var (ok, stdout, _) = await RunAdbAsync($"shell getprop {prop}", ct);
            if (ok)
            {
                var key = prop.Replace("ro.product.", "").Replace("ro.build.version.", "android_");
                info[key] = stdout.Trim();
            }
        }

        return info.Count > 0
            ? (true, info, null)
            : (false, info, "无法获取设备信息");
    }

    // ====================================================================
    // 内部方法
    // ====================================================================

    /// <summary>执行 adb 命令</summary>
    private async Task<(bool Success, string StdOut, string StdErr)> RunAdbAsync(
        string arguments, CancellationToken ct)
    {
        if (_adbPath == null)
        {
            LastStdErr = "adb 可执行文件未找到";
            return (false, "", LastStdErr);
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _adbPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // 10 秒超时保护
            var readTask = Task.WhenAll(
                process.StandardOutput.ReadToEndAsync(),
                process.StandardError.ReadToEndAsync()
            );
            var timeoutTask = Task.Delay(10000, ct);
            var completed = await Task.WhenAny(readTask, timeoutTask);

            if (completed == timeoutTask)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                LastStdErr = "adb 命令执行超时 (10s)";
                return (false, "", LastStdErr);
            }

            await process.WaitForExitAsync(ct);

            var results = await readTask;
            LastStdOut = results[0]?.Trim() ?? "";
            LastStdErr = results[1]?.Trim() ?? "";

            Logger.Debug($"AdbService: adb {arguments.Split(' ')[0]} → exit={process.ExitCode}, stdout={LastStdOut.Length} chars");

            return process.ExitCode == 0 || !string.IsNullOrEmpty(LastStdOut)
                ? (true, LastStdOut, LastStdErr)
                : (false, LastStdOut, LastStdErr);
        }
        catch (Exception ex)
        {
            Logger.Exception("AdbService::RunAdbAsync 异常", ex);
            LastStdErr = ex.Message;
            return (false, "", ex.Message);
        }
    }

    /// <summary>解析 content query 输出的 Row 行</summary>
    private static List<SmsMessage> ParseSmsContentQuery(string output)
    {
        var messages = new List<SmsMessage>();

        // 格式: Row: 0 address=10690..., body=【XX】验证码123456..., date=1717000000000
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.TrimStart().StartsWith("Row:")) continue;

            var address = ExtractColumn(line, "address=");
            var body = ExtractColumn(line, "body=");
            var dateStr = ExtractColumn(line, "date=");

            if (string.IsNullOrEmpty(body)) continue;

            // 解析毫秒时间戳
            DateTime? date = null;
            if (long.TryParse(dateStr, out var ms))
            {
                try { date = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime; }
                catch { }
            }

            messages.Add(new SmsMessage
            {
                Address = address ?? "未知",
                Body = body,
                Date = date ?? DateTime.Now
            });
        }

        return messages;
    }

    /// <summary>解析 dumpsys notification 输出</summary>
    private static List<SmsMessage> ParseNotificationDumpsys(string output)
    {
        var messages = new List<SmsMessage>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        string? currentBody = null;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("tickerText=") || trimmed.StartsWith("android.title=") ||
                trimmed.StartsWith("android.text="))
            {
                var val = trimmed[(trimmed.IndexOf('=') + 1)..].Trim();
                if (!string.IsNullOrEmpty(val) && !val.StartsWith("null"))
                {
                    if (currentBody == null)
                        currentBody = val;
                    else
                        currentBody += " " + val;
                }
            }

            if (currentBody != null && (trimmed.StartsWith("---") || trimmed.StartsWith("  uid=")))
            {
                if (ExtractVerificationCode(currentBody) != null ||
                    currentBody.Any(char.IsDigit))
                {
                    messages.Add(new SmsMessage
                    {
                        Address = "通知",
                        Body = currentBody,
                        Date = DateTime.Now
                    });
                }
                currentBody = null;
            }
        }

        // 处理最后一条
        if (currentBody != null)
        {
            messages.Add(new SmsMessage
            {
                Address = "通知",
                Body = currentBody,
                Date = DateTime.Now
            });
        }

        return messages;
    }

    /// <summary>从文本中提取验证码</summary>
    private static string? ExtractVerificationCode(string text)
    {
        var match = VerificationPattern.Match(text);
        return match.Success ? match.Groups[2].Value : null;
    }

    /// <summary>判断给定文本是否像是包含验证码的短信</summary>
    private static bool IsLikelyVerificationCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains("验证") || lower.Contains("code") || lower.Contains("verif")
            || lower.Contains("check") || lower.Contains("校验") || lower.Contains("确认")
            || lower.Contains("动态") || lower.Contains("登录") || lower.Contains("注册")
            || lower.Contains("修改密码") || lower.Contains("绑定");
    }

    /// <summary>从 content query 输出行中提取列值</summary>
    private static string? ExtractColumn(string line, string prefix)
    {
        var idx = line.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0) return null;

        var start = idx + prefix.Length;
        // 找到下一个列分隔（逗号）或行尾
        var end = line.IndexOf(", ", start, StringComparison.Ordinal);
        if (end < 0) end = line.Length;

        return line[start..end].Trim();
    }

    private static bool IsWindows() =>
        Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>adb 是否存在</summary>
    public bool IsAvailable => _adbPath != null;
}

/// <summary>短信消息</summary>
public class SmsMessage
{
    /// <summary>发送方号码</summary>
    public string Address { get; init; } = "";

    /// <summary>短信正文</summary>
    public string Body { get; init; } = "";

    /// <summary>接收时间</summary>
    public DateTime Date { get; init; } = DateTime.Now;

    public override string ToString() =>
        $"[{Date:MM-dd HH:mm}] {Address}: {Body}";
}
