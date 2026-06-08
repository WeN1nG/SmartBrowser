namespace BrowserDemo.Services.Skills.Strategy;

/// <summary>
/// 定位策略 —— 元素找不到时的降级定位方案。
/// 降级链：A11y hash → Text 匹配 → 等待重试 → 截图 → 询问
/// </summary>
public class LocateStrategy : IStrategyHandler
{
    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        if (context.LastError?.Contains("not found") == true ||
            context.LastError?.Contains("找不到") == true)
        {
            return Task.FromResult(new StrategyDecision
            {
                Action = StrategyDecisionType.Retry,
                Reason = "元素未找到，建议先获取新快照然后通过文本匹配定位",
                ExtraData = new() { ["strategy"] = "text_fallback" }
            });
        }

        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Proceed,
            Reason = "继续使用当前定位方式"
        });
    }
}
