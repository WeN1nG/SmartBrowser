using BrowserDemo.Models;

namespace BrowserDemo.Services;

/// <summary>AI 客户端接口（支持 OpenAI / Anthropic）</summary>
public interface IAiClient
{
    /// <summary>发送消息并获取完整回复</summary>
    Task<string> SendMessageAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>发送消息并流式获取回复</summary>
    IAsyncEnumerable<string> StreamMessageAsync(IEnumerable<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// 执行带工具调用的完整对话循环。
    /// 自动处理：发送请求 → 检测 tool_calls → 执行工具（回调）→ 回传结果 → 继续对话，直到 AI 返回纯文本。
    /// </summary>
    /// <param name="messages">对话消息列表（会在循环中自动追加 assistant 和 tool 消息）</param>
    /// <param name="executeTool">工具执行回调：参数为 (toolName, argumentsDict)，返回执行结果字符串</param>
    /// <param name="maxIterations">最大工具调用迭代次数</param>
    /// <param name="ct">取消令牌</param>
    IAsyncEnumerable<string> ExecuteConversationAsync(
        List<ChatMessage> messages,
        Func<string, Dictionary<string, object?>?, Task<string>> executeTool,
        int maxIterations = 100,
        CancellationToken ct = default);

    /// <summary>当前设置</summary>
    AiSettings Settings { get; set; }

    /// <summary>上下文构建器——用于注入系统提示词、工具定义和动态上下文</summary>
    ContextBuilder ContextBuilder { get; }

    /// <summary>测试连接是否可用</summary>
    Task<bool> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>AI 可通过此方法一次性设置迭代次数上限（只能设置一次）</summary>
    bool TrySetMaxIterations(int count);

    /// <summary>记录一次探测结果，返回 true 表示是对同一 URL 的重复探测（AI 可能卡住了）</summary>
    bool ReportProbe(string url, string result);

    /// <summary>保存设置到文件</summary>
    void SaveSettings();

    /// <summary>从文件加载设置</summary>
    void LoadSettings();
}
