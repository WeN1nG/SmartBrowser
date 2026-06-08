namespace BrowserDemo.Models;

/// <summary>
/// 策略技能定义 —— AI 在复杂任务中的自适应决策能力。
/// 策略技能不作为 Tool 暴露给 AI，而是内建于 AI Orchestrator 中作为系统级行为，
/// 在每次 Tool Call 循环中自动触发。
/// </summary>
public record StrategySkillDefinition : SkillDefinition
{
    public override SkillType Type => SkillType.Strategy;

    /// <summary>策略触发的条件类型</summary>
    public StrategyTriggerType TriggerType { get; init; } = StrategyTriggerType.BeforeToolCall;

    /// <summary>决策维度描述（如 "页面相关性、历史成功路径、用户偏好"）</summary>
    public string? DecisionDimensions { get; init; }

    /// <summary>降级策略列表 —— 决策路径的优先级顺序</summary>
    public List<string> FallbackChain { get; init; } = new();

    /// <summary>策略优先级（数字越小优先级越高）</summary>
    public int Priority { get; init; } = 100;

    /// <summary>当前策略是否处于激活状态</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>策略触发条件</summary>
public enum StrategyTriggerType
{
    /// <summary>每次 Tool Call 前触发</summary>
    BeforeToolCall,
    /// <summary>Tool Call 执行出错后触发</summary>
    OnError,
    /// <summary>TOken 预算紧张时触发</summary>
    OnTokenPressure,
    /// <summary>页面状态变化时触发</summary>
    OnPageStateChange,
    /// <summary>用户主动请求时触发</summary>
    OnUserRequest,
    /// <summary>周期性地在循环中触发</summary>
    OnLoopCycle
}
