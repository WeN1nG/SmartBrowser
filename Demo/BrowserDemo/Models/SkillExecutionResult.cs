using BrowserDemo.Services;

namespace BrowserDemo.Models;

/// <summary>技能执行结果 —— 记录一次技能调用的完整执行信息</summary>
public class SkillExecutionResult
{
    /// <summary>执行的技能 ID</summary>
    public string SkillId { get; init; } = string.Empty;

    /// <summary>技能名称</summary>
    public string SkillName { get; init; } = string.Empty;

    /// <summary>技能类型</summary>
    public SkillType SkillType { get; init; }

    /// <summary>执行状态</summary>
    public SkillStatus Status { get; set; } = SkillStatus.Pending;

    /// <summary>开始时间</summary>
    public DateTime StartTime { get; init; } = DateTime.Now;

    /// <summary>结束时间</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>耗时（毫秒）</summary>
    public long ElapsedMs => EndTime is not null
        ? (long)(EndTime.Value - StartTime).TotalMilliseconds
        : (long)(DateTime.Now - StartTime).TotalMilliseconds;

    /// <summary>执行结果摘要</summary>
    public string? Summary { get; set; }

    /// <summary>错误消息（如有）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>详细日志步骤</summary>
    public List<SkillExecutionStep> Steps { get; set; } = new();

    /// <summary>输出数据（提取的内容、截图等）</summary>
    public Dictionary<string, object?> Outputs { get; set; } = new();

    /// <summary>执行的原始参数</summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    /// <summary>获取可读的状态摘要</summary>
    public string StatusSummary => Status switch
    {
        SkillStatus.Pending => "⏳ 等待中",
        SkillStatus.Running => "🔄 执行中",
        SkillStatus.Succeeded => "✅ 成功",
        SkillStatus.Failed => "❌ 失败",
        SkillStatus.Skipped => "⏭️ 已跳过",
        _ => "❓ 未知"
    };

    /// <summary>获取持续时间文本</summary>
    public string DurationText => EndTime is not null
        ? $"{(long)(EndTime.Value - StartTime).TotalMilliseconds}ms"
        : $"{ElapsedMs}ms (进行中)";
}

/// <summary>技能执行的单步记录</summary>
public class SkillExecutionStep
{
    /// <summary>步骤序号</summary>
    public int Index { get; init; }

    /// <summary>步骤引用的技能 ID</summary>
    public string SkillId { get; init; } = string.Empty;

    /// <summary>步骤描述</summary>
    public string? Description { get; init; }

    /// <summary>执行状态</summary>
    public SkillStatus Status { get; set; } = SkillStatus.Pending;

    /// <summary>结果消息</summary>
    public string? ResultMessage { get; set; }

    /// <summary>耗时（毫秒）</summary>
    public long? ElapsedMs { get; set; }

    /// <summary>是否为可选步骤</summary>
    public bool IsOptional { get; init; }

    /// <summary>获取可读步骤文本</summary>
    public string ToStepText()
    {
        var icon = Status switch
        {
            SkillStatus.Running => "🔄",
            SkillStatus.Succeeded => "✅",
            SkillStatus.Failed => "❌",
            SkillStatus.Skipped => "⏭️",
            _ => "  "
        };
        var time = ElapsedMs.HasValue ? $" ({ElapsedMs}ms)" : "";
        return $"{icon} {Index}. {Description ?? SkillId}{time}";
    }
}
