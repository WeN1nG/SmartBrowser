using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using BrowserDemo.Services.Mcp.Models;

namespace BrowserDemo.Services.Mcp;

/// <summary>
/// JSON-RPC 2.0 客户端 —— 通过 stdio 与 MCP 服务器进程通信。
/// 使用原始管道读写，完全避免 StreamWriter/StreamReader 的编码/缓冲/BOM 问题。
/// </summary>
public class JsonRpcClient : IDisposable
{
    private readonly Process _process;
    private readonly Stream _stdinStream;
    private readonly Stream _stdoutStream;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _nextId = 1;
    private readonly Dictionary<int, TaskCompletionSource<string>> _pending = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>收到服务端通知时触发</summary>
    public event Action<string, JsonElement?>? OnNotification;

    public JsonRpcClient(string nodeExe, string scriptPath, string workingDir, string[]? extraArgs = null)
    {
        var args = new List<string> { $"\"{scriptPath}\"" };
        if (extraArgs != null) args.AddRange(extraArgs);

        var psi = new ProcessStartInfo(nodeExe, string.Join(" ", args))
        {
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = psi };
        _process.Start();

        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) =>
            Logger.Warning($"[JsonRpc] MCP 进程已退出, ExitCode={_process.ExitCode}");

        _stdinStream = _process.StandardInput.BaseStream;
        _stdoutStream = _process.StandardOutput.BaseStream;

        _ = Task.Run(() => ReadStdErrAsync(_disposeCts.Token));
        _ = Task.Run(() => ReadStdOutLoopAsync(_disposeCts.Token));
    }

    /// <summary>发送请求并等待响应</summary>
    public async Task<JsonElement?> InvokeAsync(string method, object? parameters = null, int timeoutMs = 30000)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock) _pending[id] = tcs;

        var request = new JsonRpcRequest
        {
            Id = id,
            Method = method,
            Params = parameters ?? new { }  // 不能传 null，MCP 需要 {} 而不是 null
        };

        var jsonStr = request.ToJson();
        Logger.Debug($"[JsonRpc] >> {method} (id={id}, json={jsonStr.Truncate(200)})");

        // 写原始 UTF-8 字节 + \n（完全控制编码，无 BOM，无 \r\n）
        var bytes = Encoding.UTF8.GetBytes(jsonStr + "\n");
        await _stdinStream.WriteAsync(bytes, _disposeCts.Token);
        await _stdinStream.FlushAsync(_disposeCts.Token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        cts.CancelAfter(timeoutMs);

        string resultJson;
        try
        {
            resultJson = await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            lock (_lock) _pending.Remove(id);
            Logger.Warning($"[JsonRpc] 请求超时: {method} (id={id}, {timeoutMs}ms)");
            throw new TimeoutException($"JSON-RPC 调用超时 ({timeoutMs}ms): {method}");
        }

        var response = JsonSerializer.Deserialize<JsonRpcResponse>(resultJson);
        if (response?.Error != null)
            throw new InvalidOperationException($"JSON-RPC 错误 [{response.Error.Code}]: {response.Error.Message}");

        return response?.Result;
    }

    /// <summary>发送通知（fire-and-forget，无 id 无响应）</summary>
    public void SendNotification(string method, object? parameters = null)
    {
        var json = parameters != null
            ? $"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{JsonSerializer.Serialize(parameters)}}}"
            : $"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\"}}";

        Logger.Debug($"[JsonRpc] >> 通知: {method}");
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        _stdinStream.Write(bytes, 0, bytes.Length);
        _stdinStream.Flush();
    }

    // ====================================================================
    // stdout 原始字节读取（替代 StreamReader.ReadLineAsync）
    // ====================================================================

    private readonly byte[] _readBuf = new byte[8192];
    private readonly List<byte> _lineBuf = new();

    private async Task ReadStdOutLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await ReadLineFromPipeAsync(ct);
                if (line == null) break;

                Logger.Debug($"[JsonRpc] << 收到行 ({line.Length}字节): {line.Truncate(150)}");
                ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning($"[JsonRpc] stdout 异常: {ex.Message}");
        }
    }

    /// <summary>从管道读取一行（以 \n 分隔），返回 UTF-8 解码后的字符串</summary>
    private async Task<string?> ReadLineFromPipeAsync(CancellationToken ct)
    {
        // 如果 _lineBuf 还有剩余数据（上次读取多出的部分），先检查里面是否有 \n
        if (_lineBuf.Count > 0)
        {
            var nlIdx = _lineBuf.IndexOf((byte)'\n');
            if (nlIdx >= 0)
            {
                var lineBytes = _lineBuf.GetRange(0, nlIdx).ToArray();
                _lineBuf.RemoveRange(0, nlIdx + 1); // 保留 \n 之后的数据
                return Encoding.UTF8.GetString(lineBytes);
            }
        }

        while (!ct.IsCancellationRequested)
        {
            int read = await _stdoutStream.ReadAsync(_readBuf, 0, _readBuf.Length, ct);
            if (read == 0) return null; // 流关闭

            // 查找 \n
            for (int i = 0; i < read; i++)
            {
                if (_readBuf[i] == (byte)'\n')
                {
                    // 找到行尾：之前累加的数据 + 当前行数据
                    var lineLen = _lineBuf.Count + i;
                    var lineBytes = new byte[lineLen];
                    if (_lineBuf.Count > 0)
                    {
                        _lineBuf.CopyTo(lineBytes, 0);
                        Array.Copy(_readBuf, 0, lineBytes, _lineBuf.Count, i);
                    }
                    else
                    {
                        Array.Copy(_readBuf, 0, lineBytes, 0, i);
                    }

                    // 剩余数据保存起来
                    _lineBuf.Clear();
                    int remaining = read - i - 1;
                    if (remaining > 0)
                    {
                        for (int j = 0; j < remaining; j++)
                            _lineBuf.Add(_readBuf[i + 1 + j]);
                    }

                    return Encoding.UTF8.GetString(lineBytes);
                }
            }

            // 没有找到 \n，累加本次读取的数据
            for (int i = 0; i < read; i++)
                _lineBuf.Add(_readBuf[i]);
        }

        return null;
    }

    // ====================================================================
    // 处理一行 JSON
    // ====================================================================

    private void ProcessLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                var id = idEl.GetInt32();
                TaskCompletionSource<string>? tcs = null;
                lock (_lock)
                {
                    if (_pending.TryGetValue(id, out tcs))
                        _pending.Remove(id);
                }
                if (tcs != null)
                {
                    tcs.TrySetResult(line);
                    Logger.Debug($"[JsonRpc] << response id={id}");
                }
                else
                {
                    Logger.Debug($"[JsonRpc] << 孤儿响应 id={id}");
                }
            }
            else if (root.TryGetProperty("method", out var methodEl))
            {
                var method = methodEl.GetString() ?? "";
                JsonElement? paramsEl = root.TryGetProperty("params", out var p) ? p : null;
                OnNotification?.Invoke(method, paramsEl);
                Logger.Debug($"[JsonRpc] << notification: {method}");
            }
        }
        catch (JsonException ex)
        {
            Logger.Warning($"[JsonRpc] JSON 解析失败: {ex.Message} | line={line.Truncate(200)}");
        }
    }

    // ====================================================================
    // stderr 读取
    // ====================================================================

    private async Task ReadStdErrAsync(CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(_process.StandardError.BaseStream, Encoding.UTF8);
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                Logger.Debug($"[MCP] {line}");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning($"[JsonRpc] stderr 异常: {ex.Message}");
        }
    }

    // ====================================================================
    // 清理
    // ====================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();

        try { _process.Kill(entireProcessTree: true); } catch { }
        try { _process.Dispose(); } catch { }
        try { _stdinStream.Dispose(); } catch { }
        try { _stdoutStream.Dispose(); } catch { }

        lock (_lock)
        {
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
        }
    }
}
