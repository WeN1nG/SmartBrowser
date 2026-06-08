using BrowserDemo.Services;

namespace BrowserDemo.Models;

/// <summary>技能类型枚举</summary>
public enum SkillType
{
    /// <summary>基础技能 —— 原子操作</summary>
    Basic,
    /// <summary>组合技能 —— 固定流程编排</summary>
    Composite,
    /// <summary>策略技能 —— 自适应决策</summary>
    Strategy
}

/// <summary>技能执行状态</summary>
public enum SkillStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Skipped
}

/// <summary>技能定义基类 —— 所有技能类型的公共基类</summary>
public abstract record SkillDefinition
{
    /// <summary>技能唯一标识（如 "skill_navigate", "compose_search"）</summary>
    public required string Id { get; init; }

    /// <summary>技能中文名称（如 "导航漫游", "搜索查询"）</summary>
    public required string Name { get; init; }

    /// <summary>技能描述 —— 告诉 AI 这个技能的功能和使用场景</summary>
    public required string Description { get; init; }

    /// <summary>技能类型</summary>
    public abstract SkillType Type { get; }

    /// <summary>典型触发短语 —— 用户自然语言中哪些词句会触发此技能</summary>
    public List<string> TriggerKeywords { get; init; } = new();

    /// <summary>技能图标（用于 AI 面板显示，Emoji 字符串）</summary>
    public string Icon { get; init; } = "⚡";

    /// <summary>默认超时（毫秒）</summary>
    public int TimeoutMs { get; init; } = 30000;

    /// <summary>是否需要在执行前请求用户确认</summary>
    public bool RequiresUserConfirmation { get; init; } = false;

    /// <summary>是否已启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>标签分类（如 "navigation", "extraction", "form"）</summary>
    public List<string> Tags { get; init; } = new();
}
