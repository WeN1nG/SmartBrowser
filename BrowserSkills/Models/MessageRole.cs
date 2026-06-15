using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BrowserSkills.Models;

/// <summary>消息角色</summary>
public enum MessageRole
{
    User,
    Assistant,
    System,
    /// <summary>工具调用的返回结果（对应 OpenAI API 的 role="tool"）</summary>
    Tool
}
