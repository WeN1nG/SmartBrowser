namespace BrowserDemo.Models;

/// <summary>
/// AI 工具定义——描述一个可供 AI 调用的函数/能力。
/// 遵循 OpenAI Function Calling 和 Anthropic Tool Use 的 JSON Schema 规范。
/// </summary>
public class ToolDefinition
{
    /// <summary>工具名称（AI 调用时使用的标识符，如 "navigate", "click"）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工具描述——告诉 AI 这个工具的功能和使用场景</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 参数的 JSON Schema 定义（OpenAI 格式的 properties 对象）。
    /// Key 为参数名，Value 为描述该参数的 JSON Schema 对象。
    /// 例如：{ "url": { "type": "string", "description": "目标 URL" } }
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>必需参数名称列表</summary>
    public List<string> Required { get; set; } = new();

    /// <summary>转换为 OpenAI Function Calling 格式</summary>
    public Dictionary<string, object?> ToOpenAISchema()
    {
        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = Name,
                ["description"] = Description,
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = Parameters,
                    ["required"] = Required
                }
            }
        };
    }

    /// <summary>转换为 Anthropic Tool Use 格式</summary>
    public Dictionary<string, object?> ToAnthropicSchema()
    {
        return new Dictionary<string, object?>
        {
            ["name"] = Name,
            ["description"] = Description,
            ["input_schema"] = new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["properties"] = Parameters,
                ["required"] = Required
            }
        };
    }
}
