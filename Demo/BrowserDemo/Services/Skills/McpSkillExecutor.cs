using System.Text.Json;
using BrowserDemo.Services.Mcp;
using BrowserDemo.Services.Skills.Strategy;

namespace BrowserDemo.Services.Skills;

/// <summary>
/// MCP 技能执行引擎 —— 通过 Playwright MCP 执行所有浏览器操作。
/// 处理：原子技能 → 直接 MCP 调用
///       组合技能 → 多步骤编排
///       策略技能 → 决策调用
/// </summary>
public class McpSkillExecutor
{
    private readonly SkillRegistry _registry;
    private readonly PlaywrightMcpClient _mcp;

    /// <summary>技能执行状态变更时触发</summary>
    public event Action<SkillExecutionResult>? OnSkillStateChanged;

    /// <summary>技能执行步骤变更时触发</summary>
    public event Action<SkillExecutionResult, SkillExecutionStep>? OnStepStateChanged;

    public McpSkillExecutor(SkillRegistry registry, PlaywrightMcpClient mcp)
    {
        _registry = registry;
        _mcp = mcp;
    }

    // ====================================================================
    // 公开入口
    // ====================================================================

    /// <summary>执行一个技能（原子/组合/策略）</summary>
    public async Task<SkillExecutionResult> ExecuteAsync(
        string skillId,
        Dictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var skill = _registry.GetSkill(skillId);
        if (skill == null)
        {
            return new SkillExecutionResult
            {
                SkillId = skillId,
                SkillName = skillId,
                SkillType = SkillType.Atomic,
                Status = SkillStatus.Failed,
                ErrorMessage = $"技能 '{skillId}' 未注册",
                EndTime = DateTime.Now
            };
        }

        parameters ??= new Dictionary<string, object?>();

        return skill.Type switch
        {
            SkillType.Atomic => await ExecuteAtomicAsync((AtomicSkillDefinition)skill, parameters, ct),
            SkillType.Composite => await ExecuteCompositeAsync((CompositeSkillDefinition)skill, parameters, ct),
            SkillType.Strategy => await ExecuteStrategyAsync((StrategySkillDefinition)skill, parameters, ct),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    // ====================================================================
    // 原子技能执行
    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteAtomicAsync(
        AtomicSkillDefinition skill,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var result = new SkillExecutionResult
        {
            SkillId = skill.Id,
            SkillName = skill.Name,
            SkillType = SkillType.Atomic,
            Status = SkillStatus.Running,
            StartTime = DateTime.Now,
            Parameters = parameters
        };

        try
        {
            // 参数映射：AI 参数名 → MCP 参数名
            var mcpArgs = new Dictionary<string, object?>();
            foreach (var (aiKey, val) in parameters)
            {
                // 如果有关联映射，转换键名
                var mcpKey = skill.ParamMapping.TryGetValue(aiKey, out var mapped) ? mapped : aiKey;
                mcpArgs[mcpKey] = val;
            }

            // 如果有固定参数，合并（AI 参数优先）
            // 原子技能没有固定参数，但这里留作扩展

            // 调用 MCP 工具
            var mcpResult = await _mcp.CallToolAsync(
                skill.McpToolName ?? skill.Id,
                mcpArgs);

            result.Status = SkillStatus.Succeeded;
            result.Summary = $"{skill.Name} 执行成功";
            result.Outputs["result"] = mcpResult;
            result.Outputs["raw_result"] = mcpResult;
        }
        catch (Exception ex)
        {
            result.Status = SkillStatus.Failed;
            result.ErrorMessage = $"{ex.GetType().Name}: {ex.Message}";
            Logger.Exception($"原子技能执行失败: {skill.Id}", ex);
        }

        result.EndTime = DateTime.Now;
        OnSkillStateChanged?.Invoke(result);
        Logger.Info($"原子技能完成: [{skill.Id}] {result.StatusSummary} ({result.ElapsedMs}ms)");
        return result;
    }

    // ====================================================================
    // 组合技能执行
    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteCompositeAsync(
        CompositeSkillDefinition skill,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var result = new SkillExecutionResult
        {
            SkillId = skill.Id,
            SkillName = skill.Name,
            SkillType = SkillType.Composite,
            Status = SkillStatus.Running,
            StartTime = DateTime.Now,
            Parameters = parameters
        };

        var ctx = new SkillExecutionContext();
        if (parameters.TryGetValue("url", out var url) && url is string u) ctx.CurrentUrl = u;

        Logger.Info($"组合技能开始: {skill.Name} ({skill.Steps.Count} 步)");

        for (int i = 0; i < skill.Steps.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (!ctx.ShouldContinue) break;

            var step = skill.Steps[i];
            var stepResult = new SkillExecutionStep
            {
                Index = i + 1,
                SkillId = step.SkillId,
                Description = step.Description,
                Status = SkillStatus.Running,
                IsOptional = step.IsOptional
            };

            result.Steps.Add(stepResult);
            OnStepStateChanged?.Invoke(result, stepResult);
            Logger.Debug($"  步骤 [{i + 1}/{skill.Steps.Count}]: {step.SkillId} — {step.Description}");

            var stepStart = DateTime.Now;

            try
            {
                // 合并参数：固定参数 + AI 参数 + 上下文变量
                var mergedParams = new Dictionary<string, object?>(parameters);
                foreach (var (k, v) in step.FixedParams)
                {
                    if (!mergedParams.ContainsKey(k))
                        mergedParams[k] = v;
                }

                // 解析模板值（{key} → ctx.Variables[key]）
                var resolvedParams = new Dictionary<string, object?>();
                foreach (var (k, v) in mergedParams)
                {
                    if (v is string sv)
                        resolvedParams[k] = ctx.ResolveTemplate(sv);
                    else
                        resolvedParams[k] = v;
                }

                // 在 if-else 外声明，两个分支内赋值
                SkillExecutionResult? subResult = null;

                // 特殊处理：snapshot 步骤后自动保存到上下文
                if (step.SkillId == "browser_snapshot")
                {
                    subResult = await ExecuteAtomicInternal(step.SkillId, resolvedParams);
                    stepResult.Status = subResult.Status;
                    stepResult.ResultMessage = subResult.Summary ?? subResult.ErrorMessage;
                    stepResult.ElapsedMs = subResult.ElapsedMs;

                    if (subResult.Status == SkillStatus.Succeeded)
                    {
                        ctx.CurrentSnapshot = subResult.Outputs.GetValueOrDefault("result")?.ToString();
                    }
                }
                else
                {
                    // 递归执行子步骤
                    subResult = await ExecuteAsync(step.SkillId, resolvedParams, ct);
                    stepResult.Status = subResult.Status;
                    stepResult.ResultMessage = subResult.Summary ?? subResult.ErrorMessage;
                    stepResult.ElapsedMs = subResult.ElapsedMs;

                    // 存储步骤输出到上下文
                    if (subResult.Status == SkillStatus.Succeeded && step.OutputKey != null)
                    {
                        var outputText = subResult.Outputs.GetValueOrDefault("result")?.ToString() ?? "";
                        ctx.RecordOutput(step.OutputKey, outputText);
                    }
                }

                // 错误处理
                if (stepResult.Status == SkillStatus.Failed)
                {
                    if (step.FallbackSkillId != null)
                    {
                        Logger.Warning($"  步骤 [{i + 1}] 失败，尝试降级: {step.SkillId} → {step.FallbackSkillId}");
                        var fbResult = await ExecuteAsync(step.FallbackSkillId, resolvedParams, ct);
                        stepResult.Status = fbResult.Status;
                        stepResult.ResultMessage = $"{step.SkillId} 失败 → 降级到 {step.FallbackSkillId}: {fbResult.Summary ?? fbResult.ErrorMessage}";
                        stepResult.ElapsedMs += fbResult.ElapsedMs;
                    }
                    else if (step.IsOptional)
                    {
                        stepResult.Status = SkillStatus.Skipped;
                        stepResult.ResultMessage = "可选步骤已跳过";
                    }
                    else
                    {
                        result.Status = SkillStatus.Failed;
                        result.ErrorMessage = $"步骤 {i + 1}（{step.SkillId}）失败: {subResult?.ErrorMessage}";
                        result.EndTime = DateTime.Now;
                        OnStepStateChanged?.Invoke(result, stepResult);
                        OnSkillStateChanged?.Invoke(result);
                        return result;
                    }
                }

                // 合并输出到主结果
                if (subResult != null)
                {
                    foreach (var (k, v) in subResult.Outputs)
                        result.Outputs[k] = v;
                }
            }
            catch (Exception ex)
            {
                Logger.Exception($"组合技能步骤异常 [{i + 1}]", ex);
                stepResult.ResultMessage = $"异常: {ex.Message}";

                if (step.IsOptional)
                    stepResult.Status = SkillStatus.Skipped;
                else
                {
                    stepResult.Status = SkillStatus.Failed;
                    result.Status = SkillStatus.Failed;
                    result.ErrorMessage = $"步骤 {i + 1} 异常: {ex.Message}";
                    result.EndTime = DateTime.Now;
                    OnStepStateChanged?.Invoke(result, stepResult);
                    OnSkillStateChanged?.Invoke(result);
                    return result;
                }
            }

            stepResult.ElapsedMs = (long)(DateTime.Now - stepStart).TotalMilliseconds;
            OnStepStateChanged?.Invoke(result, stepResult);
        }

        result.Status = ct.IsCancellationRequested ? SkillStatus.Failed : SkillStatus.Succeeded;
        if (result.Status == SkillStatus.Succeeded)
            result.Summary = $"组合技能 '{skill.Name}' 完成，{skill.Steps.Count} 个步骤";

        result.EndTime = DateTime.Now;
        OnSkillStateChanged?.Invoke(result);
        Logger.Info($"组合技能完成: {skill.Name} ({result.ElapsedMs}ms)");
        return result;
    }

    /// <summary>直接执行原子步骤（内部使用，不触发事件）</summary>
    private async Task<SkillExecutionResult> ExecuteAtomicInternal(
        string skillId, Dictionary<string, object?> parameters)
    {
        var skill = _registry.GetSkill<AtomicSkillDefinition>(skillId);
        if (skill == null)
        {
            return new SkillExecutionResult
            {
                SkillId = skillId, Status = SkillStatus.Failed,
                ErrorMessage = $"技能 {skillId} 未注册", EndTime = DateTime.Now
            };
        }

        var result = new SkillExecutionResult
        {
            SkillId = skillId, Status = SkillStatus.Running, StartTime = DateTime.Now
        };

        try
        {
            var mcpArgs = new Dictionary<string, object?>();
            foreach (var (k, v) in parameters)
            {
                var mcpKey = skill.ParamMapping.TryGetValue(k, out var m) ? m : k;
                mcpArgs[mcpKey] = v;
            }

            var mcpResult = await _mcp.CallToolAsync(
                skill.McpToolName ?? skillId, mcpArgs);

            result.Status = SkillStatus.Succeeded;
            result.Summary = $"{skill.Name} 成功";
            result.Outputs["result"] = mcpResult;
        }
        catch (Exception ex)
        {
            result.Status = SkillStatus.Failed;
            result.ErrorMessage = ex.Message;
        }

        result.EndTime = DateTime.Now;
        return result;
    }

    // ====================================================================
    // 策略技能执行
    // ====================================================================

    private async Task<SkillExecutionResult> ExecuteStrategyAsync(
        StrategySkillDefinition strategy,
        Dictionary<string, object?> parameters,
        CancellationToken ct)
    {
        var result = new SkillExecutionResult
        {
            SkillId = strategy.Id,
            SkillName = strategy.Name,
            SkillType = SkillType.Strategy,
            Status = SkillStatus.Running,
            StartTime = DateTime.Now,
            Parameters = parameters
        };

        try
        {
            var handler = _registry.GetStrategyHandler(strategy.Id);
            if (handler != null)
            {
                var ctx = new StrategyContext
                {
                    CurrentUrl = parameters.GetValueOrDefault("current_url")?.ToString(),
                    LastError = parameters.GetValueOrDefault("last_error")?.ToString(),
                    ToolCallCount = parameters.GetValueOrDefault("tool_call_count") is int c ? c : 0,
                    EstimatedTokens = parameters.GetValueOrDefault("estimated_tokens") is int t ? t : 0,
                    CurrentTitle = parameters.GetValueOrDefault("current_title")?.ToString()
                };

                var decision = await handler.DecideAsync(ctx, ct);
                result.Outputs["decision"] = decision.Action.ToString();
                result.Outputs["reason"] = decision.Reason;
                result.Summary = $"[策略] {strategy.Name} → {decision.Action} — {decision.Reason}";
                Logger.Info($"策略决策: [{strategy.Id}] {decision.Action} — {decision.Reason}");
            }
            else
            {
                result.Summary = $"[策略] {strategy.Name} → 无处理器，默认继续";
                result.Outputs["decision"] = StrategyDecisionType.Proceed.ToString();
            }

            result.Status = SkillStatus.Succeeded;
        }
        catch (Exception ex)
        {
            result.Status = SkillStatus.Failed;
            result.ErrorMessage = ex.Message;
        }

        result.EndTime = DateTime.Now;
        OnSkillStateChanged?.Invoke(result);
        return result;
    }

    /// <summary>获取统计信息</summary>
    public Dictionary<string, object> GetStats()
    {
        return new Dictionary<string, object>
        {
            ["skills_registered"] = _registry.Count,
            ["mcp_connected"] = _mcp.IsConnected,
            ["mcp_tools"] = _mcp.Tools.Count
        };
    }
}
