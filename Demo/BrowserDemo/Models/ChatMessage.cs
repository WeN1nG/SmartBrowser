using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserDemo.Models;

/// <summary>消息角色</summary>
public enum MessageRole
{
    User,
    Assistant,
    System,
    /// <summary>工具调用的返回结果（对应 OpenAI API 的 role="tool"）</summary>
    Tool
}

/// <summary>单条对话消息</summary>
public class ChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();
    public MessageRole Role { get; set; }

    /// <summary>消息内容（属性变更时通知 UI）</summary>
    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    /// <summary>追加内容到消息（不触发 PropertyChanged，由调用方在 UI 线程通知）</summary>
    public void AppendContent(string chunk) => _content += chunk;

    /// <summary>替换内容但不触发 PropertyChanged，由调用方在 UI 线程通知</summary>
    public void ReplaceContentSilently(string content) => _content = content;

    public DateTime Timestamp { get; set; } = DateTime.Now;

    // ========== 工具调用支持 ==========

    /// <summary>工具调用 ID（当 Role=Tool 时使用，对应 OpenAI API 的 tool_call_id）</summary>
    public string? ToolCallId { get; set; }

    /// <summary>工具名称（当 Role=Tool 时使用，仅用于日志/显示）</summary>
    public string? ToolName { get; set; }

    /// <summary>工具调用数据列表（当 Role=Assistant 且 AI 返回 tool_calls 时使用）</summary>
    public List<ToolCallData>? ToolCalls { get; set; }

    /// <summary>UI 调用此属性判断是否包含工具调用，用于显示不同的消息样式</summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };

    // ==================================

    /// <summary>用于 UI 显示的角色名</summary>
    public string RoleLabel => Role switch
    {
        MessageRole.User => "你",
        MessageRole.Assistant => "AI",
        MessageRole.System => "系统",
        MessageRole.Tool => "工具",
        _ => "未知"
    };

    /// <summary>API 请求用的 role 字符串</summary>
    public string ApiRole => Role switch
    {
        MessageRole.User => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.System => "system",
        MessageRole.Tool => "tool",
        _ => "user"
    };

    /// <summary>手动触发 Content 属性变更通知（用于流式更新节流）</summary>
    public void NotifyContentChanged() => OnPropertyChanged(nameof(Content));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
