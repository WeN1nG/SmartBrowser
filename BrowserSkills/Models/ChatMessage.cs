using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace BrowserSkills.Models;

/// <summary>单条对话消息</summary>
public class ChatMessage : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private string? _sectionsSource;
    private AssistantResponseSections _sections;
    private string? _displayRoleLabelOverride;
    private bool _isAskUserActive;

    public Guid Id { get; set; } = Guid.NewGuid();
    public MessageRole Role { get; set; }

    /// <summary>消息内容（属性变更时通知 UI）</summary>
    public string Content
    {
        get => _content;
        set
        {
            _content = value;
            InvalidateSections();
            NotifyContentAndSectionsChanged();
        }
    }

    /// <summary>追加内容到消息（不触发 PropertyChanged，由调用方在 UI 线程通知）</summary>
    public void AppendContent(string chunk)
    {
        _content += chunk;
        InvalidateSections();
    }

    /// <summary>替换内容但不触发 PropertyChanged，由调用方在 UI 线程通知</summary>
    public void ReplaceContentSilently(string content)
    {
        _content = content;
        InvalidateSections();
    }

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

    // ========== ask_user 内联提示 UI ==========

    /// <summary>覆盖 UI 角色名，例如 [AI need help]</summary>
    public string? DisplayRoleLabelOverride
    {
        get => _displayRoleLabelOverride;
        set
        {
            if (_displayRoleLabelOverride == value) return;
            _displayRoleLabelOverride = value;
            OnPropertyChanged(nameof(DisplayRoleLabelOverride));
            OnPropertyChanged(nameof(RoleLabel));
        }
    }

    private bool _isAskUserPrompt;

    /// <summary>是否为 ask_user 生成的内联提示消息</summary>
    public bool IsAskUserPrompt
    {
        get => _isAskUserPrompt;
        set
        {
            if (_isAskUserPrompt == value) return;
            _isAskUserPrompt = value;
            NotifyContentAndSectionsChanged();
            OnPropertyChanged(nameof(IsAskUserPrompt));
            OnPropertyChanged(nameof(IsRegularAssistant));
            OnPropertyChanged(nameof(IsPlainContentMessage));
        }
    }

    public string? AskUserQuestionId { get; set; }
    public string? AskUserQuestionType { get; set; }
    public string[]? AskUserOptions { get; set; }
    public string? AskUserContextSummary { get; set; }

    /// <summary>该提示是否仍等待用户交互</summary>
    public bool IsAskUserActive
    {
        get => _isAskUserActive;
        set
        {
            if (_isAskUserActive == value) return;
            _isAskUserActive = value;
            OnPropertyChanged(nameof(IsAskUserActive));
            OnPropertyChanged(nameof(IsAskUserInactive));
        }
    }

    [JsonIgnore]
    public bool HasAskUserOptions => AskUserOptions is { Length: > 0 };

    [JsonIgnore]
    public bool IsAskUserConfirmation => IsAskUserPrompt && AskUserQuestionType == "confirmation";

    [JsonIgnore]
    public bool IsAskUserMultipleChoice => IsAskUserPrompt && AskUserQuestionType == "multiple_choice";

    [JsonIgnore]
    public bool IsAskUserOpenEnded => IsAskUserPrompt && AskUserQuestionType == "open_ended";

    [JsonIgnore]
    public bool IsAskUserInactive => IsAskUserPrompt && !IsAskUserActive;

    // ==================================

    [JsonIgnore]
    public bool IsAssistant => Role == MessageRole.Assistant;

    [JsonIgnore]
    public bool IsNotAssistant => !IsAssistant;

    /// <summary>是否为真正的 AI 消息（排除 ask_user UI 提示卡片）</summary>
    [JsonIgnore]
    public bool IsRegularAssistant => Role == MessageRole.Assistant && !IsAskUserPrompt;

    /// <summary>是否为适用于单块内容显示的简单消息（非普通 AI 或 ask_user 提示）</summary>
    [JsonIgnore]
    public bool IsPlainContentMessage => Role != MessageRole.Assistant || IsAskUserPrompt;

    [JsonIgnore]
    public bool IsVisibleInChat => Role != MessageRole.System;

    [JsonIgnore]
    public string ThinkingContent => GetSections().Thinking;

    [JsonIgnore]
    public string ConclusionContent => GetSections().Conclusion;

    [JsonIgnore]
    public bool HasThinkingContent => IsRegularAssistant && GetSections().HasThinking;

    [JsonIgnore]
    public bool HasConclusionContent => IsRegularAssistant && GetSections().HasConclusion;

    /// <summary>用于 UI 显示的角色名</summary>
    public string RoleLabel => !string.IsNullOrWhiteSpace(DisplayRoleLabelOverride)
        ? DisplayRoleLabelOverride
        : Role switch
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
    public void NotifyContentChanged() => NotifyContentAndSectionsChanged();

    private AssistantResponseSections GetSections()
    {
        if (!IsAssistant)
            return new AssistantResponseSections(string.Empty, _content);

        if (_sectionsSource == _content)
            return _sections;

        _sections = AssistantResponseParser.ParseAndClean(_content);
        _sectionsSource = _content;
        return _sections;
    }

    private void InvalidateSections() => _sectionsSource = null;

    private void NotifyContentAndSectionsChanged()
    {
        OnPropertyChanged(nameof(Content));
        OnPropertyChanged(nameof(ThinkingContent));
        OnPropertyChanged(nameof(ConclusionContent));
        OnPropertyChanged(nameof(HasThinkingContent));
        OnPropertyChanged(nameof(HasConclusionContent));
        OnPropertyChanged(nameof(IsRegularAssistant));
        OnPropertyChanged(nameof(IsPlainContentMessage));
        OnPropertyChanged(nameof(IsAskUserInactive));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
