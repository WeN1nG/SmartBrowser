using System.IO;
using System.Text.Json;
using BrowserDemo.Services.Mcp.Models;

namespace BrowserDemo.Services.Mcp;

/// <summary>
/// Playwright MCP 客户端 —— 封装 JSON-RPC 通信，提供强类型的浏览器操作方法。
/// </summary>
public class PlaywrightMcpClient : IDisposable
{
    private readonly JsonRpcClient _rpc;
    private readonly string _mcpDir;
    private bool _initialized;
    private List<McpToolDefinition> _cachedTools = new();

    /// <summary>MCP 服务器信息</summary>
    public McpServerInfo? ServerInfo { get; private set; }

    /// <summary>注册的 MCP 工具列表</summary>
    public IReadOnlyList<McpToolDefinition> Tools => _cachedTools;

    /// <summary>是否已连接到 MCP 服务器</summary>
    public bool IsConnected => _initialized;

    /// <summary>
    /// 使用指定的 CDP 端点（由 MainWindow 启动的 Chrome 暴露）。
    /// </summary>
    public PlaywrightMcpClient(string cdpEndpointUrl)
    {
        _mcpDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..",
            "BrowserDemo", "Tools", "playwright-mcp", "playwright-mcp-0.0.75");
        _mcpDir = Path.GetFullPath(_mcpDir);

        var nodeExe = FindNodeExe();
        Logger.Info($"[Mcp] Node.js: {nodeExe}");
        Logger.Info($"[Mcp] MCP 目录: {_mcpDir}");
        Logger.Info($"[Mcp] CDP 端点: {cdpEndpointUrl}");

        if (!Directory.Exists(_mcpDir))
            throw new DirectoryNotFoundException($"Playwright MCP 目录未找到: {_mcpDir}");

        var cliJs = Path.Combine(_mcpDir, "cli.js");
        if (!File.Exists(cliJs))
            throw new FileNotFoundException($"Playwright MCP cli.js 未找到: {cliJs}");

        var extraArgs = new[] { "--cdp-endpoint=" + cdpEndpointUrl };
        _rpc = new JsonRpcClient(nodeExe, cliJs, _mcpDir, extraArgs);
        _rpc.OnNotification += OnMcpNotification;
    }

    /// <summary>无参构造（默认 localhost:9222，用于测试或降级）</summary>
    public PlaywrightMcpClient() : this("http://localhost:9222") { }

    /// <summary>
    /// 初始化 MCP 连接：握手 + 获取工具列表。
    /// </summary>
    public async Task InitializeAsync()
    {
        Logger.Info("[Mcp] 正在初始化 Playwright MCP...");

        var initResult = await _rpc.InvokeAsync("initialize", new
        {
            protocolVersion = "2025-03-26",
            capabilities = new { },
            clientInfo = new { name = "BrowserDemo", version = "1.0.0" }
        });

        if (initResult != null)
        {
            var info = JsonSerializer.Deserialize<McpInitializeResult>(initResult.Value.GetRawText());
            ServerInfo = info?.ServerInfo;
            Logger.Info($"[Mcp] 已连接: {info?.ServerInfo?.Name} v{info?.ServerInfo?.Version}");
        }

        _rpc.SendNotification("notifications/initialized");

        await RefreshToolsAsync();

        _initialized = true;
        Logger.Info($"[Mcp] 初始化完成，{_cachedTools.Count} 个工具可用");
    }

    /// <summary>刷新 MCP 工具列表</summary>
    public async Task RefreshToolsAsync()
    {
        var toolsResult = await _rpc.InvokeAsync("tools/list", new { });
        if (toolsResult != null &&
            toolsResult.Value.TryGetProperty("tools", out var toolsArray))
        {
            _cachedTools = JsonSerializer.Deserialize<List<McpToolDefinition>>(toolsArray.GetRawText()) ?? new();
            Logger.Debug($"[Mcp] 已加载 {_cachedTools.Count} 个 MCP 工具");
        }
    }

    private void OnMcpNotification(string method, JsonElement? paramsEl)
    {
        Logger.Debug($"[Mcp] 通知: {method}");
    }

    // ====================================================================
    // 浏览器操作方法
    // ====================================================================

    public async Task<string> NavigateAsync(string url)
        => await CallToolAsync("browser_navigate", new() { ["url"] = url });

    public async Task<string> GetSnapshotAsync()
        => await CallToolAsync("browser_snapshot");

    public async Task<string> ClickAsync(string element)
        => await CallToolAsync("browser_click", new() { ["element"] = element });

    // ... 通用调用入口

    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object?>? args = null)
    {
        Logger.Info($"[Mcp] 调用工具: {toolName}");
        Logger.Debug($"[Mcp]  参数: {JsonSerializer.Serialize(args ?? new())}");

        var result = await _rpc.InvokeAsync("tools/call", new McpToolCallRequest
        {
            Name = toolName,
            Arguments = args
        });

        if (result == null)
        {
            Logger.Warning($"[Mcp]  返回: null");
            return "（无返回）";
        }

        var toolResult = JsonSerializer.Deserialize<McpToolResult>(result.Value.GetRawText());
        var text = toolResult?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? "（无文本内容）";

        Logger.Debug($"[Mcp]  返回: {text.Truncate(200)}");

        return text;
    }

    public void Dispose()
    {
        _rpc.Dispose();
    }

    // ====================================================================
    // Node.js 路径查找
    // ====================================================================

    private static string FindNodeExe()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
            @"C:\Program Files\nodejs\node.exe",
            @"C:\Program Files (x86)\nodejs\node.exe"
        };

        try
        {
            var which = new System.Diagnostics.ProcessStartInfo("where", "node")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(which);
            if (p != null)
            {
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(1000);
                if (p.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    var firstLine = output.Split('\n')[0].Trim();
                    if (File.Exists(firstLine))
                        return firstLine;
                }
            }
        }
        catch { }

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return "node";
    }
}
