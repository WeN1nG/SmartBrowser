using BrowserDemo.Services;

namespace BrowserDemo.Models;

/// <summary>
/// 组合技能步骤 —— 定义组合技能中的一个执行步骤。
/// 每个步骤可以引用基础技能或另一个组合技能，形成层级编排。
/// </summary>
public record SkillStep
{
    /// <summary>引用的技能 ID（如 "skill_navigate", "compose_search"）</summary>
    public required string SkillId { get; init; }

    /// <summary>步骤参数（可覆盖被引用技能的默认参数）</summary>
    public Dictionary<string, object?>? Params { get; init; }

    /// <summary>步骤的中文描述（供 AI 面板显示）</summary>
    public string? Description { get; init; }

    /// <summary>是否为可选步骤 —— 失败时可跳过，不影响整体流程</summary>
    public bool IsOptional { get; init; } = false;

    /// <summary>失败时的降级技能 ID —— 当前步骤失败后尝试此技能</summary>
    public string? FallbackSkillId { get; init; }

    /// <summary>当前步骤的执行状态（运行时填充）</summary>
    public SkillStatus Status { get; set; } = SkillStatus.Pending;

    /// <summary>步骤执行结果消息（运行时填充）</summary>
    public string? ResultMessage { get; set; }

    /// <summary>步骤执行耗时（运行时填充，毫秒）</summary>
    public long? ElapsedMs { get; set; }
}
