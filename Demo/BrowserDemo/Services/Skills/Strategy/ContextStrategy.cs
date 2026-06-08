namespace BrowserDemo.Services.Skills.Strategy;

/// <summary>
/// 上下文策略 —— Token 预算管理。
/// 当 Token 用量接近上限时，裁剪上下文保留关键状态。
/// </summary>
public class ContextStrategy : IStrategyHandler
{
    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        // Token 超过 80% 时触发压缩
        if (context.EstimatedTokens > 64000)
        {
            return Task.FromResult(new StrategyDecision
            {
                Action = StrategyDecisionType.Proceed,
                Reason = $"Token 用量较高 ({context.EstimatedTokens})，建议压缩历史快照"
            });
        }

        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Proceed,
            Reason = "Token 用量正常"
        });
    }
}
