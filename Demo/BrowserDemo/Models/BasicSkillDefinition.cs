using BrowserDemo.Services;

namespace BrowserDemo.Models;

/// <summary>
/// 基础技能定义 —— 对应一个原子浏览器操作，直接映射到 IAutomationBridge 的方法。
/// 基础技能是 AI 控制浏览器的"最小可执行单元"。
/// </summary>
public record BasicSkillDefinition : SkillDefinition
{
    public override SkillType Type => SkillType.Basic;

    /// <summary>关联的浏览器操作方法名列表（如 ["navigate", "go_back", "go_forward"]）</summary>
    public List<string> RelatedToolNames { get; init; } = new();

    /// <summary>默认参数值（如 type 操作的默认文本）</summary>
    public Dictionary<string, object?> DefaultParams { get; init; } = new();

    /// <summary>执行此技能是否会对页面产生副作用</summary>
    public bool IsDestructive { get; init; } = false;

    /// <summary>是否需要页面完全加载才能执行</summary>
    public bool RequiresPageLoaded { get; init; } = true;

    /// <summary>
    /// 获取此技能的 AI 工具定义（ToolDefinition），供 AI API 调用。
    /// 基础技能可暴露给 AI 作为可调用的 Function/Tool。
    /// </summary>
    public ToolDefinition ToToolDefinition()
    {
        // ★ 根据技能类型生成精准的参数提示，避免 AI 使用错误的参数名
        var paramHint = BuildParamHint();
        var tool = new ToolDefinition
        {
            Name = Id,
            Description = $"{Icon} {Name}：{Description}{paramHint}",
            Parameters = new Dictionary<string, object?>
            {
                ["action"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "具体操作子类型，如 'navigate', 'click', 'type'",
                    ["enum"] = RelatedToolNames
                },
                ["params"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = BuildParamsDescription(),
                    ["properties"] = BuildParamsProperties()
                }
            },
            Required = new List<string> { "action" }
        };
        return tool;
    }

    /// <summary>为 tool description 生成参数使用提示</summary>
    private string BuildParamHint()
    {
        return Id switch
        {
            "skill_navigate" => "\n⚡ params: { url: string }",
            "skill_click" => "\n⚡ params: { selector: string, text_content: string } — selector=标准CSS选择器如 '#id', '.class'(不支持:has-text伪选择器)；text_content=按文本内容查找元素",
            "skill_type" => "\n⚡ params: { selector: string, text: string, key: string } — selector=目标输入框标准CSS选择器(不支持:has-text)，text=输入内容，key=Enter/Tab等按键",
            "skill_select" => "\n⚡ params: { selector: string, value: string, text: string } — selector=标准CSS选择器",
            "skill_scroll" => "\n⚡ params: { delta_y: number, selector: string } — selector=标准CSS选择器(可选)",
            "skill_extract" => "\n⚡ params: { selector: string } — 可选，标准CSS选择器，默认提取整个页面",
            "skill_screenshot" => "\n⚡ params: { selector: string } — 可选，默认截取整页",
            "skill_wait" => "\n⚡ params: { timeout_ms: number } — 可选，或 selector(等待元素出现)/text(等待文本出现)",
            "skill_tab" => "\n⚡ params: { url: string, tab_id: string, index: number }",
            "skill_cookie" => "\n⚡ params: { name: string, value: string, domain: string }",
            "skill_form" => "\n⚡ params: { fields: object, selector: string, value: string }",
            "skill_hover" => "\n⚡ params: { selector: string }",
            "skill_query" => "\n⚡ params: { selector: string }",
            "skill_js" => "\n⚡ params: { code: string } — JS代码最后一行表达式的值即为返回值（不要用console.log），示例: return document.title;",
            "skill_adb_sms" => "\n⚡ params: { action: string, timeout_ms: number, sender: string } — action=check_device|get_recent_sms|wait_for_code|get_phone_info",
            _ => ""
        };
    }

    /// <summary>为 params 字段生成描述</summary>
    private string BuildParamsDescription()
    {
        return Id switch
        {
            "skill_navigate" => "导航参数，包含 url(目标网址)",
            "skill_click" => "点击参数，必须包含 selector(标准CSS选择器如'#id' '.class'，不支持:has-text伪选择器) 或 text_content(按元素文本内容查找，如'课程' '提交')",
            "skill_type" => "输入参数，必须包含 selector(目标元素CSS选择器)，可选 text(输入文本) key(按键名)",
            "skill_extract" => "提取参数，selector 可选(默认为body，提取整个页面)",
            _ => "操作参数，键名请使用 selector(元素选择器) url(网址) text(文本) key(按键名)"
        };
    }

    /// <summary>为 params 生成显式的 JSON Schema properties，防止 AI 用错参数名</summary>
    private Dictionary<string, object?> BuildParamsProperties()
    {
        return Id switch
        {
            "skill_click" => new()
            {
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "标准CSS选择器用于定位目标元素，如 '#submit-btn', '.search-input', 'input[name=q]'。注意: 不支持 Playwright 风格的 :has-text() 伪选择器。如需按文本查找请使用 text_content 参数。"
                },
                ["text_content"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "按元素文本内容精确查找并点击，适用于 selector 无法定位的场景。例如 '课程', '提交', '确定'"
                }
            },
            "skill_type" => new()
            {
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "标准CSS选择器用于定位输入框元素，如 '#search', 'input[name=q]'。不支持 :has-text 伪选择器，如需按标签文本查找请直接用页面文本提取确定元素后使用其CSS选择器"
                },
                ["text"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "要输入的文本内容"
                },
                ["key"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "特殊按键名，如 'Enter', 'Tab'"
                }
            },
            "skill_navigate" => new()
            {
                ["url"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "目标网页的完整 URL 地址"
                }
            },
            "skill_extract" => new()
            {
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "可选，CSS 选择器用于提取特定元素的内容，不传则提取整个页面"
                }
            },
            "skill_query" => new()
            {
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "标准CSS选择器用于查询页面元素，如 '#id', '.class'。不支持 Playwright :has-text/:contains 等非标准伪类"
                }
            },
            "skill_hover" => new()
            {
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "标准CSS选择器用于定位要悬停的元素，如 '#id', '.class'。不支持 Playwright :has-text 伪选择器"
                }
            },
            "skill_scroll" => new()
            {
                ["delta_y"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["description"] = "垂直滚动像素数，正数向下，负数向上"
                },
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "可选，标准CSS选择器用于滚动到指定元素位置。不支持 :has-text 等Playwright伪类"
                }
            },
            "skill_wait" => new()
            {
                ["timeout_ms"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["description"] = "等待超时毫秒数，默认 15000"
                },
                ["selector"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "等待元素出现时的CSS选择器，如 '.loading'。不支持 :has-text 等Playwright伪类"
                },
                ["text"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "等待文本出现时的文本内容"
                }
            },
            "skill_form" => new()
            {
                ["fields"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["description"] = "表单字段映射，键为CSS选择器、值为填充文本。选择器不支持 :has-text 等Playwright伪类"
                }
            },
            "skill_adb_sms" => new()
            {
                ["timeout_ms"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["description"] = "等待验证码的超时毫秒数，默认 60000 (60秒)"
                },
                ["sender"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "可选，过滤特定发送方号码或关键词"
                },
                ["limit"] = new Dictionary<string, object?>
                {
                    ["type"] = "number",
                    ["description"] = "获取最近短信的条数，默认 10"
                }
            },
            _ => new() // 其他技能使用默认行为
        };
    }
}
