namespace BrowserDemo.Services.Skills.Strategy;

/// <summary>
/// 隐私保护策略 —— 识别敏感页面并采用安全操作模式。
/// </summary>
public class PrivacyStrategy : IStrategyHandler
{
    private static readonly string[] SensitivePatterns = {
        "login", "signin", "sign-in", "password", "pay", "payment",
        "checkout", "card", "bank", "privacy", "account",
        "登录", "密码", "支付", "付款", "银行卡", "账号"
    };

    public Task<StrategyDecision> DecideAsync(StrategyContext context, CancellationToken ct = default)
    {
        var url = context.CurrentUrl?.ToLower() ?? "";
        var title = context.CurrentTitle?.ToLower() ?? "";

        var isSensitive = SensitivePatterns.Any(p =>
            url.Contains(p) || title.Contains(p));

        if (isSensitive)
        {
            return Task.FromResult(new StrategyDecision
            {
                Action = StrategyDecisionType.Proceed,
                Reason = "检测到敏感页面，操作后将清除敏感数据",
                ExtraData = new() { ["clear_after"] = true }
            });
        }

        return Task.FromResult(new StrategyDecision
        {
            Action = StrategyDecisionType.Proceed,
            Reason = "当前页面无敏感信息"
        });
    }
}
