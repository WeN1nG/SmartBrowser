namespace BrowserSkills.Strategy;

/// <summary>
/// 重试策略 —— 操作失败后自适应恢复。
/// 降级链：等待重载 → 换选择器 → 重新截图 → 换方法 → 报告用户
/// </summary>
public class RetryStrategy : IStrategyHandler
{
    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        if (context.ToolCallCount > 3)
        {
            return Task.FromResult(new StrategyDecision
            {
                Action = StrategyDecisionType.AskUser,
                Reason = $"已重试 {context.ToolCallCount} 次仍未成功，需要用户确认下一步操作"
            });
        }

        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Retry,
            Reason = $"第 {context.ToolCallCount + 1} 次重试，等待短暂延迟后重新尝试",
            ExtraData = new() { ["retry_delay_ms"] = (context.ToolCallCount + 1) * 1000 }
        });
    }
}
