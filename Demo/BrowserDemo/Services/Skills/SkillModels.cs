using BrowserDemo.Services.Mcp.Models;

namespace BrowserDemo.Services.Skills;

/// <summary>技能类型</summary>
public enum SkillType { Atomic, Composite, Strategy }

/// <summary>技能执行状态</summary>
public enum SkillStatus { Pending, Running, Succeeded, Failed, Skipped }

/// <summary>
/// 技能定义基类
/// </summary>
public class SkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "⚡";
    public SkillType Type { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 30000;
    public bool RequiresUserConfirmation { get; set; }
    /// <summary>对应的 MCP 工具名（原子技能）</summary>
    public string? McpToolName { get; set; }
    /// <summary>标签</summary>
    public List<string> Tags { get; set; } = new();
    /// <summary>触发关键词（用于意图匹配）</summary>
    public List<string> TriggerKeywords { get; set; } = new();

    public override string ToString() => $"[{Icon}] {Id}: {Name}";
}

/// <summary>
/// 原子技能：对应一个 MCP 工具调用
/// </summary>
public class AtomicSkillDefinition : SkillDefinition
{
    public AtomicSkillDefinition()
    {
        Type = SkillType.Atomic;
    }

    /// <summary>参数映射：AI 参数名 → MCP 参数名</summary>
    public Dictionary<string, string> ParamMapping { get; set; } = new();
}

/// <summary>
/// 组合技能：多步原子技能编排
/// </summary>
public class CompositeSkillDefinition : SkillDefinition
{
    public CompositeSkillDefinition()
    {
        Type = SkillType.Composite;
    }

    /// <summary>步骤列表</summary>
    public List<CompositeStep> Steps { get; set; } = new();
    /// <summary>预期输出描述</summary>
    public string ExpectedOutput { get; set; } = "";
    /// <summary>预估耗时</summary>
    public string EstimatedDuration { get; set; } = "";

    public override string ToString() => $"[{Icon}] {Id}: {Name} ({Steps.Count}步)";
}

/// <summary>组合技能中的一个步骤</summary>
public class CompositeStep
{
    /// <summary>引用的技能 ID（原子或组合）</summary>
    public string SkillId { get; set; } = "";
    /// <summary>步骤描述</summary>
    public string Description { get; set; } = "";
    /// <summary>固定参数（会与 AI 传入的参数合并）</summary>
    public Dictionary<string, object?> FixedParams { get; set; } = new();
    /// <summary>是否可选（失败时可跳过）</summary>
    public bool IsOptional { get; set; }
    /// <summary>失败时的降级技能 ID</summary>
    public string? FallbackSkillId { get; set; }
    /// <summary>输出键名：将此步骤的输出存入上下文的 key</summary>
    public string? OutputKey { get; set; }
}

/// <summary>
/// 策略技能：在特定条件下触发决策
/// </summary>
public class StrategySkillDefinition : SkillDefinition
{
    public StrategySkillDefinition()
    {
        Type = SkillType.Strategy;
    }

    /// <summary>触发类型</summary>
    public StrategyTriggerType TriggerType { get; set; } = StrategyTriggerType.OnError;
    /// <summary>决策维度</summary>
    public string DecisionDimensions { get; set; } = "";
    /// <summary>降级链</summary>
    public List<string> FallbackChain { get; set; } = new();
    /// <summary>优先级（0=最高）</summary>
    public int Priority { get; set; } = 10;
}

public enum StrategyTriggerType
{
    BeforeToolCall,
    OnError,
    OnTokenPressure,
    OnNavigation
}

/// <summary>技能执行结果</summary>
public class SkillExecutionResult
{
    public string SkillId { get; set; } = "";
    public string SkillName { get; set; } = "";
    public SkillType SkillType { get; set; }
    public SkillStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public long ElapsedMs => (long)(EndTime - StartTime).TotalMilliseconds;
    public string? Summary { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object?> Outputs { get; set; } = new();
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public List<SkillExecutionStep> Steps { get; set; } = new();

    public string StatusSummary => Status switch
    {
        SkillStatus.Succeeded => $"✅ {SkillName}",
        SkillStatus.Failed => $"❌ {SkillName}: {ErrorMessage}",
        SkillStatus.Skipped => $"⏭️ {SkillName} （跳过）",
        _ => $"🔄 {SkillName}"
    };
}

public class SkillExecutionStep
{
    public int Index { get; set; }
    public string SkillId { get; set; } = "";
    public string Description { get; set; } = "";
    public SkillStatus Status { get; set; }
    public bool IsOptional { get; set; }
    public string? ResultMessage { get; set; }
    public long ElapsedMs { get; set; }
    public string ResultSummary => Status switch
    {
        SkillStatus.Succeeded => $"✅ {Description} ({ElapsedMs}ms)",
        SkillStatus.Failed => $"❌ {Description}: {ResultMessage}",
        SkillStatus.Skipped => $"⏭️ {Description} （跳过）",
        _ => $"🔄 {Description}"
    };
}
