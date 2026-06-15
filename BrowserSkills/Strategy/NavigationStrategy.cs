namespace BrowserSkills.Strategy;

/// <summary>
/// 导航策略 —— 当目标页面不在当前上下文中时，决定如何处理。
/// 降级链：当前 URL 搜索 → 导航新 URL → 组合搜索 → 询问用户
/// </summary>
public class NavigationStrategy : IStrategyHandler
{
    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(context.CurrentUrl))
        {
            return Task.FromResult(new StrategyDecision
            {
                Action = StrategyDecisionType.AskUser,
                Reason = "当前无页面，请提供目标 URL"
            });
        }

        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Fallback,
            Reason = $"当前页面 ({context.CurrentUrl}) 可能不包含目标信息，建议导航到新页面",
            FallbackSkillId = "compose_search"
        });
    }
}
