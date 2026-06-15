namespace BrowserSkills.Strategy;

/// <summary>策略上下文</summary>
public class StrategyContext
{
    public string? CurrentUrl { get; set; }
    public string? LastError { get; set; }
    public int ToolCallCount { get; set; }
    public int EstimatedTokens { get; set; }
    public string? CurrentTitle { get; set; }
}

/// <summary>策略决策结果</summary>
public class StrategyDecision
{
    public StrategyDecisionType Action { get; set; } = StrategyDecisionType.Proceed;
    public string Reason { get; set; } = "";
    public string? FallbackSkillId { get; set; }
    public Dictionary<string, object?>? ExtraData { get; set; }
}

public enum StrategyDecisionType
{
    Proceed,
    Retry,
    Fallback,
    Stop,
    AskUser
}

/// <summary>策略处理器接口</summary>
public interface IStrategyHandler
{
    /// <summary>根据上下文做出决策</summary>
    Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default);
}
