namespace BrowserDemo.Services.Skills;

/// <summary>
/// 技能执行上下文 —— 在组合技能步骤间传递状态和数据。
/// </summary>
public class SkillExecutionContext
{
    /// <summary>当前页面 URL</summary>
    public string? CurrentUrl { get; set; }

    /// <summary>当前页面标题</summary>
    public string? CurrentTitle { get; set; }

    /// <summary>当前页面的 A11y 快照</summary>
    public string? CurrentSnapshot { get; set; }

    /// <summary>上一步的截图（Base64）</summary>
    public string? LastScreenshot { get; set; }

    /// <summary>共享变量存储（步骤间传递数据）</summary>
    public Dictionary<string, object?> Variables { get; set; } = new();

    /// <summary>步骤输出历史</summary>
    public List<KeyValuePair<string, string>> StepOutputs { get; set; } = new();

    /// <summary>当前迭代次数（用于多页采集循环）</summary>
    public int CurrentIteration { get; set; }

    /// <summary>最大迭代次数</summary>
    public int MaxIterations { get; set; } = 10;

    /// <summary>错误计数</summary>
    public int ErrorCount { get; set; }

    /// <summary>是否应继续执行（用于循环控制）</summary>
    public bool ShouldContinue { get; set; } = true;

    /// <summary>
    /// 解析模板字符串：{key} → Variables[key]
    /// </summary>
    public string ResolveTemplate(string template)
    {
        if (string.IsNullOrEmpty(template)) return template;

        foreach (var (key, val) in Variables)
        {
            if (val != null)
                template = template.Replace($"{{{key}}}", val.ToString());
        }

        // 也替换固定上下文
        if (CurrentUrl != null) template = template.Replace("{currentUrl}", CurrentUrl);
        if (CurrentTitle != null) template = template.Replace("{currentTitle}", CurrentTitle);

        return template;
    }

    /// <summary>记录步骤输出</summary>
    public void RecordOutput(string key, string value)
    {
        StepOutputs.Add(new KeyValuePair<string, string>(key, value));
        Variables[key] = value;
    }

    /// <summary>生成上下文摘要，用于 AI 提示</summary>
    public string ToSummary()
    {
        var parts = new List<string>();
        if (CurrentUrl != null) parts.Add($"URL: {CurrentUrl}");
        if (CurrentTitle != null) parts.Add($"标题: {CurrentTitle}");
        if (CurrentSnapshot != null) parts.Add($"快照: {CurrentSnapshot.Truncate(300)}");
        if (StepOutputs.Count > 0) parts.Add($"步骤输出: {StepOutputs.Count} 项");

        return string.Join(" | ", parts);
    }
}
