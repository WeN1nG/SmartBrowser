namespace BrowserDemo.Services.Skills.Strategy;

/// <summary>
/// 错误恢复策略 —— 页面崩溃/导航失败时的整体恢复方案。
/// 降级链：新标签打开 → 导航 → 恢复会话
/// </summary>
public class RecoveryStrategy : IStrategyHandler
{
    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Fallback,
            Reason = "页面状态异常，建议新建标签页并重新导航",
            FallbackSkillId = "browser_tabs"
        });
    }
}
