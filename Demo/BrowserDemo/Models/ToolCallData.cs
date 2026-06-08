namespace BrowserDemo.Models;

/// <summary>
/// AI API 返回的工具调用数据。
/// 对应 OpenAI streaming 响应中 delta.tool_calls 的内容。
/// </summary>
public class ToolCallData
{
    /// <summary>工具调用唯一标识（如 "call_abc123"）</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>工具类型（目前固定为 "function"）</summary>
    public string Type { get; set; } = "function";

    /// <summary>工具/函数名称（对应 ToolDefinition.Name）</summary>
    public string FunctionName { get; set; } = string.Empty;

    /// <summary>工具参数的 JSON 字符串（across streaming chunks 累加）</summary>
    public string FunctionArguments { get; set; } = string.Empty;

    /// <summary>反序列化参数为字典</summary>
    public Dictionary<string, object?>? ParseArguments()
    {
        if (string.IsNullOrWhiteSpace(FunctionArguments))
            return new Dictionary<string, object?>();
        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(
                FunctionArguments,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
}

/// <summary>
/// 流式响应中的事件类型 —— 用于在解析 SSE 时区分文本块和工具调用数据块。
/// </summary>
public class AiStreamEvent
{
    /// <summary>事件类型: "content" | "tool_call_start" | "tool_call_delta" | "finish"</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>文本内容（Type=content 时有效）</summary>
    public string? Text { get; set; }

    /// <summary>工具调用索引（Type=tool_call 时有效）</summary>
    public int? ToolIndex { get; set; }

    /// <summary>工具调用 ID（Type=tool_call_start 时有效）</summary>
    public string? ToolId { get; set; }

    /// <summary>工具类型（Type=tool_call_start 时有效，如 "function"）</summary>
    public string? ToolType { get; set; }

    /// <summary>工具名称（Type=tool_call_start 时有效）</summary>
    public string? ToolName { get; set; }

    /// <summary>工具参数字符串片段（Type=tool_call_start/delta 时有效）</summary>
    public string? ToolArgs { get; set; }

    /// <summary>结束原因（Type=finish 时有效，如 "stop" | "tool_calls" | "length"）</summary>
    public string? FinishReason { get; set; }
}
