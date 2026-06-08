using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserDemo.Services.Mcp.Models;

/// <summary>JSON-RPC 2.0 请求</summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("params")] public object? Params { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this);
}

/// <summary>JSON-RPC 2.0 响应（含 result）</summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("result")] public JsonElement? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}

/// <summary>JSON-RPC 2.0 通知（服务端主动推送）</summary>
public class JsonRpcNotification
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
}

public class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>MCP 工具定义</summary>
public class McpToolDefinition
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("inputSchema")] public JsonElement? InputSchema { get; set; }
}

/// <summary>MCP 工具调用结果</summary>
public class McpToolResult
{
    [JsonPropertyName("content")] public List<McpContentItem>? Content { get; set; }
    [JsonPropertyName("isError")] public bool IsError { get; set; }
}

public class McpContentItem
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    /// <summary>Base64 编码的图片数据（type=image 时）</summary>
    [JsonPropertyName("data")] public string? Data { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
}

/// <summary>MCP 工具调用请求参数</summary>
public class McpToolCallRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public Dictionary<string, object?>? Arguments { get; set; }
}

/// <summary>MCP 初始化结果</summary>
public class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "";
    [JsonPropertyName("capabilities")] public McpCapabilities? Capabilities { get; set; }
    [JsonPropertyName("serverInfo")] public McpServerInfo? ServerInfo { get; set; }
}

public class McpCapabilities
{
    [JsonPropertyName("tools")] public object? Tools { get; set; }
    [JsonPropertyName("resources")] public object? Resources { get; set; }
}

public class McpServerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

/// <summary>MCP Session 信息</summary>
public class McpSessionInfo
{
    public int SessionId { get; set; }
    public string? BrowserType { get; set; }
    public bool IsConnected { get; set; }
    public int TabCount { get; set; }
}
