using BrowserDemo.Services.Mcp;
using BrowserDemo.Services.Skills.Strategy;

namespace BrowserDemo.Services.Skills;

/// <summary>
/// MCP 技能系统集成器 —— 一键初始化 Playwright MCP + 全部技能 + 策略处理器。
/// </summary>
public class SkillSystemIntegration
{
    private readonly string? _cdpEndpointUrl;
    private PlaywrightMcpClient? _mcpClient;

    /// <summary>MCP 客户端（Playwright 通信）</summary>
    public PlaywrightMcpClient McpClient => _mcpClient ?? throw new InvalidOperationException("MCP 客户端尚未初始化");

    /// <summary>技能注册中心</summary>
    public SkillRegistry Registry { get; } = new();

    /// <summary>技能执行引擎</summary>
    public McpSkillExecutor Executor { get; private set; } = null!;

    /// <summary>是否已完成初始化</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>初始化时间</summary>
    public DateTime? InitializedAt { get; private set; }

    /// <summary>MCP 工具数量</summary>
    public int McpToolCount => McpClient.Tools.Count;

    public SkillSystemIntegration(string? cdpEndpointUrl = null)
    {
        _cdpEndpointUrl = cdpEndpointUrl;
    }

    /// <summary>
    /// 初始化 MCP 连接 + 注册所有技能。
    /// </summary>
    public async Task InitializeAsync()
    {
        using var trace = Logger.Trace("SkillSystemIntegration::InitializeAsync");
        Logger.Info("═══════════════════════════════════════════");
        Logger.Info("   MCP 浏览器自动化系统初始化开始");
        Logger.Info("═══════════════════════════════════════════");

        try
        {
            _mcpClient = _cdpEndpointUrl != null
                ? new PlaywrightMcpClient(_cdpEndpointUrl)
                : new PlaywrightMcpClient();

            // 1. 初始化 MCP 连接（启动 Playwright 浏览器）
            // 首次连接可能需要浏览器启动，重试一次以增加成功率
            try
            {
                await McpClient.InitializeAsync();
            }
            catch (Exception ex)
            {
                Logger.Warning($"[MCP] 首次连接失败，等待 2 秒后重试: {ex.Message}");
                await Task.Delay(2000);
                await McpClient.InitializeAsync();
            }

            // 2. 创建执行引擎
            Executor = new McpSkillExecutor(Registry, McpClient);

            // 3. 注册所有内置技能
            RegisterAllSkills();

            // 4. 注册策略处理器
            RegisterStrategyHandlers();

            // 5. 验证引用完整性
            ValidateSkills();

            // 6. 连接执行引擎事件
            ConnectExecutorEvents();

            IsInitialized = true;
            InitializedAt = DateTime.Now;

            Logger.Info($"✅ MCP 浏览器自动化系统初始化完成");
            Logger.Info($"   MCP 工具: {McpToolCount} 个");
            Logger.Info($"   技能: {Registry.Count} 个 (原子 {Registry.AtomicSkills.Count()} + 组合 {Registry.CompositeSkills.Count()} + 策略 {Registry.StrategySkills.Count()})");
        }
        catch (Exception ex)
        {
            Logger.Exception("[MCP] 初始化失败", ex);
            Logger.Warning("[MCP] 将使用纯对话模式运行（无浏览器自动化能力）");
            IsInitialized = false;
        }
    }

    private void RegisterAllSkills()
    {
        // 注册原子技能
        foreach (var skill in McpSkillDataProvider.GetAllAtomicSkills())
            Registry.Register(skill);

        // 注册组合技能
        foreach (var skill in McpSkillDataProvider.GetAllCompositeSkills())
            Registry.Register(skill);

        // 注册策略技能
        foreach (var skill in McpSkillDataProvider.GetAllStrategySkills())
            Registry.Register(skill);

        Logger.Info($"已注册 {Registry.Count} 个技能");
    }

    private void RegisterStrategyHandlers()
    {
        var handlers = new Dictionary<string, IStrategyHandler>
        {
            ["strategy_navigation"] = new NavigationStrategy(),
            ["strategy_locate"] = new LocateStrategy(),
            ["strategy_retry"] = new RetryStrategy(),
            ["strategy_context"] = new ContextStrategy(),
            ["strategy_recovery"] = new RecoveryStrategy(),
            ["strategy_privacy"] = new PrivacyStrategy()
        };

        foreach (var (id, handler) in handlers)
        {
            if (Registry.GetSkill(id) != null)
            {
                Registry.RegisterStrategyHandler(id, handler);
                Logger.Debug($"策略处理器已关联: {id} → {handler.GetType().Name}");
            }
            else
            {
                Logger.Warning($"策略处理器关联失败：策略 {id} 不存在");
            }
        }
    }

    private void ValidateSkills()
    {
        if (Registry.Validate(out var errors))
        {
            Logger.Info("技能引用验证: ✅ 全部通过");
        }
        else
        {
            Logger.Warning($"技能引用验证: ⚠️ 发现 {errors.Count} 个问题");
            foreach (var err in errors)
                Logger.Warning($"  - {err}");
        }
    }

    private void ConnectExecutorEvents()
    {
        Executor.OnSkillStateChanged += result =>
        {
            Logger.Debug($"技能状态变更: [{result.SkillId}] {result.StatusSummary} ({result.ElapsedMs}ms)");
        };

        Executor.OnStepStateChanged += (result, step) =>
        {
            Logger.Debug($"  步骤: [{step.SkillId}] {step.ResultSummary}");
        };
    }

    /// <summary>
    /// 根据用户意图推荐技能。
    /// </summary>
    public List<SkillDefinition> RecommendSkills(string userMessage)
    {
        if (!IsInitialized) return new();
        return Registry.RecommendForIntent(userMessage).ToList();
    }

    /// <summary>获取技能系统状态摘要</summary>
    public string GetStatusSummary()
    {
        if (!IsInitialized)
            return "❌ MCP 浏览器自动化未连接（纯对话模式）";

        var atomic = Registry.AtomicSkills.Count();
        var composite = Registry.CompositeSkills.Count();
        var strategy = Registry.StrategySkills.Count();
        var enabled = Registry.AllSkills.Count(s => s.IsEnabled);

        return $"✅ MCP 已连接 ({InitializedAt:HH:mm:ss}) | " +
               $"MCP 工具: {McpToolCount} | " +
               $"原子: {atomic} | 组合: {composite} | 策略: {strategy} | 启用: {enabled}/{Registry.Count}";
    }

    /// <summary>获取所有技能的格式化信息</summary>
    public string GetAllSkillsFormatted()
    {
        if (!IsInitialized) return "MCP 浏览器自动化未初始化";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("  MCP 浏览器自动化 — 技能总览");
        sb.AppendLine($"  MCP 服务器: {McpClient.ServerInfo?.Name} v{McpClient.ServerInfo?.Version}");
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        sb.AppendLine($"\n⚡ 原子技能 ({Registry.AtomicSkills.Count()})");
        foreach (var s in Registry.AtomicSkills)
            sb.AppendLine($"  {s.Icon} [{s.Id}] {s.Name} — {s.Description.Truncate(60)}");

        sb.AppendLine($"\n🔗 组合技能 ({Registry.CompositeSkills.Count()})");
        foreach (var s in Registry.CompositeSkills)
            sb.AppendLine($"  {s.Icon} [{s.Id}] {s.Name} ({s.Steps.Count}步) — {s.Description.Truncate(60)}");

        sb.AppendLine($"\n🧠 策略技能 ({Registry.StrategySkills.Count()})");
        foreach (var s in Registry.StrategySkills)
            sb.AppendLine($"  {s.Icon} [{s.Id}] {s.Name} — {s.Description.Truncate(60)}");

        sb.AppendLine($"\n总计: {Registry.Count} 个技能 | MCP 工具: {McpToolCount} 个");
        return sb.ToString();
    }

    /// <summary>从容关闭 MCP 连接</summary>
    public void Shutdown()
    {
        Logger.Info("[MCP] 正在关闭...");
        _mcpClient?.Dispose();
        _mcpClient = null;
        IsInitialized = false;
        Logger.Info("[MCP] 已关闭");
    }
}
