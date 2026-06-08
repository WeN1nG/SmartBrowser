namespace BrowserDemo.Models;

/// <summary>
/// 组合技能定义 —— 多个基础/组合技能的固定编排，用于完成一个常见的完整子任务。
/// AI Orchestrator 将组合技能暴露给 AI 作为可调用的 Tool，
/// AI 只需选择"我要做什么"，而不需要编排每一个步骤。
/// </summary>
public record CompositeSkillDefinition : SkillDefinition
{
    public override SkillType Type => SkillType.Composite;

    /// <summary>编排步骤列表（有序）</summary>
    public required List<SkillStep> Steps { get; init; }

    /// <summary>预期输出描述 —— AI 执行完此技能后能得到什么</summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>预估执行时间范围描述（如 "5-10秒", "30秒+"）</summary>
    public string? EstimatedDuration { get; init; }

    /// <summary>
    /// 获取此组合技能的 AI 工具定义。
    /// 组合技能以单个 Tool 暴露给 AI，降低 AI 的编排复杂度。
    /// </summary>
    public ToolDefinition ToToolDefinition()
    {
        var stepDescriptions = Steps
            .Select((s, i) => $"  {i + 1}. [{s.SkillId}] {s.Description ?? ""}")
            .ToList();

        // 组合技能是自包含的固定步骤序列，不暴露内部步骤参数给 AI 的 tool call。
        // 步骤参数（Params）是内部默认值，不是 JSON Schema 定义，无法直接用作 API 的 tools 参数。
        var tool = new ToolDefinition
        {
            Name = Id,
            Description = $"{Icon} {Name}：{Description}\n执行步骤：\n{string.Join("\n", stepDescriptions)}",
            Parameters = new Dictionary<string, object?>(),
            Required = new List<string>()
        };
        return tool;
    }
}
