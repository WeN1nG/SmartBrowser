using BrowserDemo.Services.Mcp;
using BrowserDemo.Services.Skills.Strategy;

namespace BrowserDemo.Services.Skills;

/// <summary>
/// 技能注册中心 —— 管理所有技能的注册、查找和引用验证。
/// </summary>
public class SkillRegistry
{
    private readonly Dictionary<string, SkillDefinition> _skills = new();
    private readonly Dictionary<string, IStrategyHandler> _strategyHandlers = new();

    /// <summary>所有技能</summary>
    public IReadOnlyCollection<SkillDefinition> AllSkills => _skills.Values;

    /// <summary>所有原子技能</summary>
    public IEnumerable<AtomicSkillDefinition> AtomicSkills =>
        _skills.Values.OfType<AtomicSkillDefinition>();

    /// <summary>所有组合技能</summary>
    public IEnumerable<CompositeSkillDefinition> CompositeSkills =>
        _skills.Values.OfType<CompositeSkillDefinition>();

    /// <summary>所有策略技能</summary>
    public IEnumerable<StrategySkillDefinition> StrategySkills =>
        _skills.Values.OfType<StrategySkillDefinition>();

    // ====================================================================
    // 注册
    // ====================================================================

    public void Register(SkillDefinition skill)
    {
        if (string.IsNullOrEmpty(skill.Id))
        {
            Logger.Warning("尝试注册 ID 为空的技能");
            return;
        }

        _skills[skill.Id] = skill;
        Logger.Debug($"技能已注册: [{skill.Id}] {skill.Name}");
    }

    public void RegisterAll(IEnumerable<SkillDefinition> skills)
    {
        foreach (var s in skills) Register(s);
    }

    public void RegisterStrategyHandler(string strategyId, IStrategyHandler handler)
    {
        _strategyHandlers[strategyId] = handler;
        Logger.Debug($"策略处理器已注册: {strategyId} → {handler.GetType().Name}");
    }

    // ====================================================================
    // 查询
    // ====================================================================

    public SkillDefinition? GetSkill(string id)
    {
        _skills.TryGetValue(id, out var skill);
        return skill;
    }

    public T? GetSkill<T>(string id) where T : SkillDefinition
    {
        return GetSkill(id) as T;
    }

    public IStrategyHandler? GetStrategyHandler(string strategyId)
    {
        _strategyHandlers.TryGetValue(strategyId, out var handler);
        return handler;
    }

    /// <summary>根据用户意图推荐技能</summary>
    public IEnumerable<SkillDefinition> RecommendForIntent(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage)) yield break;

        var lower = userMessage.ToLower();

        foreach (var skill in _skills.Values.Where(s => s.IsEnabled))
        {
            if (skill.TriggerKeywords.Any(kw =>
                lower.Contains(kw.ToLower())))
            {
                yield return skill;
            }
        }
    }

    // ====================================================================
    // 验证
    // ====================================================================

    public bool Validate(out List<string> errors)
    {
        errors = new();

        foreach (var composite in CompositeSkills)
        {
            foreach (var step in composite.Steps)
            {
                if (!_skills.ContainsKey(step.SkillId))
                {
                    errors.Add($"组合技能 [{composite.Id}] 引用了不存在的技能: {step.SkillId}");

                    if (step.FallbackSkillId != null && !_skills.ContainsKey(step.FallbackSkillId))
                    {
                        errors.Add($"  降级引用不存在: {step.FallbackSkillId}");
                    }
                }
            }
        }

        return errors.Count == 0;
    }

    public int Count => _skills.Count;
}
