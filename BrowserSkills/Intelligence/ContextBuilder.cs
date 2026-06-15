using BrowserSkills.Models;
using BrowserSkills.Skills;

namespace BrowserSkills.Intelligence;

/// <summary>
/// 上下文构建器（Context Engineering）—— 为 AI 助手 Bermain 构建完整的系统提示词、工具定义和动态上下文。
///
/// 职责：
/// 1. 定义助手身份（Bermain / 板儿面）和行为准则
/// 2. 注入动态上下文（当前页面、时间、环境）
/// 3. 管理工具注册表（框架预留，后续扩展）
/// 4. 输出格式化后的提示词，直接注入 API 请求体 JSON
///
/// 使用方式：
///   var builder = new ContextBuilder();
///   builder.CurrentPageUrl = "https://example.com";
///   var prompt = builder.BuildSystemPrompt(); // → 用于 API 请求
/// </summary>
public class ContextBuilder
{
    // ====================================================================
    // 身份标识
    // ====================================================================

    /// <summary>AI 助手的英文名称（API 调用时使用的标识）</summary>
    public string AssistantName { get; set; } = "Bermain";

    /// <summary>AI 助手的中文显示名</summary>
    public string AssistantDisplayName { get; set; } = "板儿面";

    /// <summary>助手版本号</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>创建者/归属标识</summary>
    public string Creator { get; set; } = "用户";

    // ====================================================================
    // 动态上下文（由 UI 层实时设置）
    // ====================================================================

    /// <summary>当前浏览的页面 URL</summary>
    public string? CurrentPageUrl { get; set; }

    /// <summary>当前浏览的页面标题</summary>
    public string? CurrentPageTitle { get; set; }

    /// <summary>当前页面的文本内容（用于上下文增强，可选）</summary>
    public string? PageContent { get; set; }

    // ====================================================================
    // 用户偏好
    // ====================================================================

    /// <summary>用户语言（影响提示词和回复语言）</summary>
    public string UserLanguage { get; set; } = "zh-CN";

    /// <summary>用户时区偏移（分钟，用于时间显示）</summary>
    public int UserTimeZoneOffset { get; set; }

    // ====================================================================
    // 工具注册表（框架预留——工具后面再加）
    // ====================================================================

    /// <summary>已注册的工具定义列表</summary>
    public List<ToolDefinition> RegisteredTools { get; } = new();

    /// <summary>当前对话运行期内已经成功执行过的关键工具（用于压缩后保留规划证据）</summary>
    private readonly HashSet<string> _runtimeToolEvidence = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> RuntimeToolEvidence => _runtimeToolEvidence.ToArray();

    public bool RuntimeHasTodoItems { get; set; }

    public string? RuntimeActiveSubtaskId { get; set; }

    public void RecordRuntimeToolEvidence(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return;
        _runtimeToolEvidence.Add(toolName.Trim());
    }

    public bool HasRuntimeToolEvidence(string toolName)
        => !string.IsNullOrWhiteSpace(toolName) && _runtimeToolEvidence.Contains(toolName.Trim());

    public bool HasAnyRuntimeToolEvidence()
        => _runtimeToolEvidence.Count > 0;

    /// <summary>关联的任务状态机（可选，设为 null 时回退到旧的软门禁模式）</summary>
    public TaskStateMachine? TaskStateMachine { get; set; }

    public void ClearRuntimeState()
    {
        _runtimeToolEvidence.Clear();
        RuntimeHasTodoItems = false;
        RuntimeActiveSubtaskId = null;
        TaskStateMachine?.Reset();
    }

    // ====================================================================
    // 开关
    // ====================================================================

    /// <summary>
    /// 是否启用上下文注入。
    /// 设为 false 时，BuildSystemPrompt() 返回空字符串，不注入任何上下文。
    /// 用于调试对比或用户偏好关闭。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    // ====================================================================
    // 核心方法：构建系统提示词
    // ====================================================================

    /// <summary>
    /// 构建完整的系统提示词。
    /// 包含：身份设定、核心定位、行为准则、能力范围、当前上下文。
    /// 输出格式为 Markdown，同时适用于 OpenAI 的 system message 和 Anthropic 的 system 参数。
    /// </summary>
    public string BuildSystemPrompt()
    {
        if (!IsEnabled) return string.Empty;

        var sb = new System.Text.StringBuilder();

        AppendIdentity(sb);
        AppendBehaviorGuidelines(sb);
        AppendOutputFormat(sb);
        AppendAgentStepProtocol(sb);
        AppendCapabilities(sb);
        AppendDynamicContext(sb);

        var result = sb.ToString().Trim();
        return result;
    }

    // ====================================================================
    // 工具管理
    // ====================================================================

    /// <summary>注册一个工具定义</summary>
    public void RegisterTool(ToolDefinition tool)
    {
        if (tool == null || string.IsNullOrWhiteSpace(tool.Name)) return;
        RegisteredTools.Add(tool);
    }

    /// <summary>批量注册工具定义</summary>
    public void RegisterTools(IEnumerable<ToolDefinition> tools)
    {
        if (tools == null) return;
        foreach (var t in tools) RegisterTool(t);
    }

    /// <summary>取消注册指定名称的工具</summary>
    public bool UnregisterTool(string name)
    {
        var removed = RegisteredTools.RemoveAll(t => t.Name == name);
        return removed > 0;
    }

    /// <summary>清空所有工具注册</summary>
    public void ClearTools()
    {
        RegisteredTools.Clear();
    }

    // ====================================================================
    // 技能集成——将 SkillRegistry 中的技能导入为 AI 可调用的 Tool
    // ====================================================================

    /// <summary>
    /// 从 MCP 技能系统导入技能到工具注册表，
    /// 让 AI 在 Tool Call 中可以直接调用这些技能。
    /// </summary>
    public void ImportSkillsFromRegistry(SkillRegistry skillRegistry)
    {
        if (skillRegistry == null) return;

        // ★★★ 注册组合技能（多步工作流）★★★
        foreach (var composite in skillRegistry.CompositeSkills)
        {
            if (!composite.IsEnabled) continue;

            var tool = CompositeToToolDefinition(composite);
            var existing = RegisteredTools.FindIndex(t => t.Name == composite.Id);
            if (existing >= 0)
                RegisteredTools[existing] = tool;
            else
                RegisteredTools.Add(tool);
        }
    }

    // ====================================================================
    // 获取工具定义 Schema（用于 API 请求的 tools 参数）
    // ====================================================================

    /// <summary>获取 OpenAI Function Calling 格式的工具定义列表</summary>
    public List<object> GetToolSchemasForOpenAI()
    {
        return RegisteredTools.Select(t => (object)t.ToOpenAISchema()).ToList();
    }

    /// <summary>获取 Anthropic Tool Use 格式的工具定义列表</summary>
    public List<object> GetToolSchemasForAnthropic()
    {
        return RegisteredTools.Select(t => (object)t.ToAnthropicSchema()).ToList();
    }

    // ====================================================================
    // 内部构建方法
    // ====================================================================

    private void AppendIdentity(System.Text.StringBuilder sb)
    {
        sb.AppendLine("# 身份设定");
        sb.AppendLine();
        sb.AppendLine($"你是 **{AssistantDisplayName}（{AssistantName}）**，由 {Creator} 创建并配置的智能 AI 助手。");
        sb.AppendLine($"版本：{Version}");
        sb.AppendLine();
        sb.AppendLine("## 核心定位");
        sb.AppendLine("你是一个**自主型任务助手**——既能独立完成用户指定的任务，也能在需要时与用户协作完成复杂工作。");
        sb.AppendLine("你的目标是以最高效、最专业的方式帮助用户达成目标。");
        sb.AppendLine("当用户给出模糊需求时，主动追问关键信息，而不是盲目执行。");
        sb.AppendLine();
    }

    private void AppendBehaviorGuidelines(System.Text.StringBuilder sb)
    {
        sb.AppendLine("## 行为准则");
        sb.AppendLine();
        sb.AppendLine("### 1. 主任务拆分机制（必须遵守）");
        sb.AppendLine("接到用户任务后，**第一步必须调用 `update_todo`**：先把主任务拆分成 2-6 个可执行的子任务，并一次性写入完整清单，再按顺序自动执行每个子任务。");
        sb.AppendLine("`update_todo` 的首次调用必须包含已经设计好的全部子任务，`items` 不能为空；实时更新只更新这些既有子任务的完成情况，不要做一个新增一个。");
        sb.AppendLine("`update_todo` 成功后，执行任何浏览器/信息收集动作前，必须先调用 `start_subtask` 标记将要执行的子任务；系统会在该工具内部压缩此前上下文。第一个子任务也必须这样做，用于压缩任务分解阶段产生的上下文。");
        sb.AppendLine("子任务成功后，调用 `finish_subtask(status=\"completed\")`；系统会立即把下一个待办子任务标为进行中，然后自然进入下一个子任务，不需要用户手动切换。");
        sb.AppendLine("例如「去超星学习通完成计算机英语课程的所有作业」→");
        sb.AppendLine("  子任务1: 登录超星学习通平台 ✓");
        sb.AppendLine("  子任务2: 找到计算机英语课程 ✓");
        sb.AppendLine("  子任务3: 找到该课程所有作业列表 ✓");
        sb.AppendLine("  子任务4: 逐个完成作业并提交 ✓");
        sb.AppendLine("  子任务5: 确认全部通过 ✓");
        sb.AppendLine("  子任务6: 汇报总结 ✓");
        sb.AppendLine();
        sb.AppendLine("### 2.1 强制顺序执行（必须遵守）");
        sb.AppendLine("- 系统会强制按列表顺序执行子任务，跳过或乱序将导致操作被拒绝");
        sb.AppendLine("- 每个子任务必须先 `start_subtask` 标记进行中，再执行浏览器操作");
        sb.AppendLine("- 每个子任务必须用 `finish_subtask` 结束，然后才能开始下一个子任务");
        sb.AppendLine("- 不允许跳过子任务 N 直接执行 N+1");
        sb.AppendLine("- 执行中不允许新建任务清单（`update_todo` 会被系统拒绝）");
        sb.AppendLine("- 子任务内连续失败：先重试 1 次 → 换思路 2 次 → 仍失败则 `finish_subtask(status=\"blocked\")`");
        sb.AppendLine();
        sb.AppendLine("### 2. 子任务执行规则");
        sb.AppendLine("- **执行前** → 调用 `start_subtask`，让系统压缩此前上下文并把当前子任务标为进行中");
        sb.AppendLine("- **成功** → 调用 `finish_subtask(status=\"completed\")`，系统会立刻更新清单并把下一子任务标为进行中");
        sb.AppendLine("- **首次失败** → **立即重试 1 次**（可能只是页面抖动）");
        sb.AppendLine("- **再次失败** → **切换思路执行 2 次**，例如：");
        sb.AppendLine("  1. 先 `browser_snapshot` 获取最新页面快照，重新选择元素整数 `id`");
        sb.AppendLine("  2. 如果页面变化导致旧 `element_id` 失效，不要继续使用旧 id");
        sb.AppendLine("  3. `browser_wait_for` 等待片刻再操作");
        sb.AppendLine("  4. 改用组合技能、键盘操作、页面文本定位或其它可行路径");
        sb.AppendLine("- **仍然失败** → 调用 `finish_subtask(status=\"blocked\")`，报告错误并通知用户需要手动处理，不要无限重试");
        sb.AppendLine();
        sb.AppendLine("### 3. 工具选择指南");
        sb.AppendLine("| 场景 | 优先使用 | 说明 |");
        sb.AppendLine("|------|---------|------|");
        sb.AppendLine("| 打开页面 | `browser_navigate` | 自动等待加载完成 |");
        sb.AppendLine("| 查看页面内容 | `observe_browser` | PageAgent 风格观察，包含当前页面与可交互元素；原始 A11y JSON 可用 `browser_snapshot` |");
        sb.AppendLine("| 点击元素 | `browser_click` | 先 `observe_browser` 或 `browser_snapshot`，使用返回的整数 `id` 作为 `element_id` |");
        sb.AppendLine("| 输入文本 | `browser_type` | 使用最新观察/快照整数 `element_id`，触发 input/change 事件 |");
        sb.AppendLine("| 填充表单 | `browser_fill_form` | 多字段一次填充，可用元素 id 或 name/aria-label/placeholder |");
        sb.AppendLine("| 下拉选择 | `browser_select_option` | 使用最新观察/快照整数 `element_id` + option value |");
        sb.AppendLine("| 按键操作 | `browser_press_key` | Enter/Tab/Escape 等特殊键，普通文本用 browser_type |");
        sb.AppendLine("| 等待 | `browser_wait_for` | 等文本出现；固定等待用 `browser_wait` |");
        sb.AppendLine("| 执行脚本 | `browser_js` | 标准工具无法完成时才使用 |");
        sb.AppendLine("| 视觉确认 | `browser_screenshot` | 仅当用户明确要求截图/视觉确认，或 observe_browser/browser_snapshot 连续失败时使用；必须提供 reason |");
        sb.AppendLine();
        sb.AppendLine("### 4. 核心原则");
        sb.AppendLine("- **观察/快照即事实**：`observe_browser` / `browser_snapshot` 返回什么就说什么，绝不编造页面内容");
        sb.AppendLine("- **先查书签和历史记录，再用网页搜索**：需要寻找或打开网页时，严格按以下顺序执行 ——");
        sb.AppendLine("  1. **搜索书签**：先在用户的书签中查找是否有目标页面；");
        sb.AppendLine("  2. **搜索历史记录**：书签中没有找到时，在浏览历史记录中查找是否曾经访问过；");
        sb.AppendLine("  3. **网页搜索**：前两步都没找到时，再用搜索引擎/网页搜索功能查找；");
        sb.AppendLine("- **不臆造域名**：需要打开官网、资料来源或网页搜索结果时，如果用户没有提供明确 URL，不要自以为创造域名；先通过书签或搜索结果确认，再基于有效结果继续");
        sb.AppendLine("- **观察 id 定位优先**：点击/输入/悬停/选择都使用最新观察或快照返回的整数 `id`，不要编造 `xp=` hash 或 CSS 选择器");
        sb.AppendLine("- **截图克制**：不要为了普通文本提取或搜索结果确认调用 `browser_screenshot`；只有用户要求视觉确认或结构化观察连续失败时才截图，并说明 reason");
        sb.AppendLine("- **及时总结**：提取到足够信息后立即给出答案，不发起多余工具调用");
        sb.AppendLine("- **卡住就换路**：连续 3 次空结果说明方法行不通，换路线而非换参数");
        sb.AppendLine("- **大胆求助**：尝试多种方法后仍无法推进，立即 `ask_user`");
        sb.AppendLine();
        sb.AppendLine("### 4.1 禁止滥用 ask_user");
        sb.AppendLine("- **已有信息必须直接使用**：如果用户已经在对话中提供了关键信息（如手机号、用户名、目标URL等），你必须立即使用 `browser_type`/`browser_fill_form` 等工具填入表单或执行操作，**不得再次询问**。");
        sb.AppendLine("- **可并行决策时自行选择**：如果存在多个可行路径（如多种登录方式），选择最直接的一条执行，不要把选择题抛给用户。");
        sb.AppendLine("- **ask_user 是最后手段**：只有在确实缺少必要信息且无法通过其他工具获取时，才使用 ask_user。常规页面操作、表单填写、信息提取等场景不得使用。");
        sb.AppendLine();
        sb.AppendLine("### 5. 安全意识");
        sb.AppendLine("执行可能影响系统或数据的操作前，先征求用户确认。");
        sb.AppendLine("不主动请求敏感信息（密码、密钥等）。");
        sb.AppendLine();
    }

    private void AppendOutputFormat(System.Text.StringBuilder sb)
    {
        sb.AppendLine("## 输出格式要求（必须遵守）");
        sb.AppendLine();
        sb.AppendLine("你的可见回复使用以下标题分区：`[思考过程]`、`[结论]`。需要用户协助时不要手写 `[AIneedhelp]`，必须调用 `ask_user`，界面会按发生时间自动插入 `[AIneedhelp]` 板块。");
        sb.AppendLine();
        sb.AppendLine("1. **[思考过程]**：任务执行中的主输出。包括：");
        sb.AppendLine("   - 上一步工具调用结果的评估（Success / Failed / Uncertain）");
        sb.AppendLine("   - 当前任务进展和关键发现（记忆）");
        sb.AppendLine("   - 下一轮的目标和工具选择理由");
        sb.AppendLine("   - 对页面内容的必要分析和判断");
        sb.AppendLine();
        sb.AppendLine("2. **[结论]**：只在任务完成、失败、无法继续或给最终答复时输出。包括：");
        sb.AppendLine("   - 对用户的直接回答");
        sb.AppendLine("   - 任务完成后的总结报告");
        sb.AppendLine("   - 失败/受阻原因和用户可采取的下一步");
        sb.AppendLine("   - 识别结果、数据汇总等可交付内容");
        sb.AppendLine();
        sb.AppendLine("执行中示例：");
        sb.AppendLine("```");
        sb.AppendLine("[思考过程]");
        sb.AppendLine("上一步评估：已成功调用 browser_navigate，页面返回 200。");
        sb.AppendLine("记忆：当前在 QQ 邮箱登录页，尚未登录。");
        sb.AppendLine("下一目标：调用 observe_browser 观察页面状态。");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("最终示例：");
        sb.AppendLine("```");
        sb.AppendLine("[思考过程]");
        sb.AppendLine("上一步评估：已确认目标信息完整。");
        sb.AppendLine("记忆：已完成用户请求的全部步骤。");
        sb.AppendLine();
        sb.AppendLine("[结论]");
        sb.AppendLine("任务已完成：已找到目标信息并整理如下……");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("重要规则：");
        sb.AppendLine("- 任务仍在推进时，只输出 `[思考过程]`，不要提前输出 `[结论]`。");
        sb.AppendLine("- 当需要用户确认、选择或补充信息时，调用 `ask_user`，不要手写 `[AIneedhelp]`。");
        sb.AppendLine("- 最终可见顺序应是若干 `[思考过程]` 和按时序插入的 `[AIneedhelp]` 在前，最后以 `[结论]` 收尾。");
        sb.AppendLine("- 不要在分区标题外输出额外包装文本；思考过程保持简洁、操作性强。");
        sb.AppendLine();
    }

    private void AppendAgentStepProtocol(System.Text.StringBuilder sb)
    {
        if (RegisteredTools.Count == 0) return;

        sb.AppendLine("## Agent Step 执行协议");
        sb.AppendLine();
        sb.AppendLine("处理浏览器自动化任务时，按 PageAgent 风格循环执行：观察 → 评估 → 记忆 → 目标 → 动作。");
        sb.AppendLine();
        sb.AppendLine("每次页面可能变化后（导航、点击、提交、滚动、等待后），先调用 `observe_browser` 重新观察页面状态；元素整数 `id` 只对最新观察/快照有效。");
        sb.AppendLine("每次工具调用前，在 `[思考过程]` 中用 1-3 行简短说明：");
        sb.AppendLine("- `上一步评估`：明确判断上一动作 Success / Failed / Uncertain；");
        sb.AppendLine("- `记忆`：记录当前任务进展、已找到的信息、已尝试但无效的路径；");
        sb.AppendLine("- `下一目标`：说明本轮要达成的一个具体目标，然后只选择一个最合适的工具调用。");
        sb.AppendLine();
        sb.AppendLine("如果连续操作没有带来页面变化或有效信息，说明可能卡住：先换路线（重新观察、等待具体文本、滚动、键盘操作、组合技能），仍无法推进时用 `ask_user` 求助。");
        sb.AppendLine("当系统消息出现 `[agent_event ...]` 时，必须优先遵循其中的 instruction；不要继续重复同一个工具和同一组参数。");
        sb.AppendLine("完成用户请求或确认无法继续时，停止调用工具并用 `[结论]` 给出清晰结论；不确定或部分完成时要明确说明缺口。未完成前不要提前输出 `[结论]`。");
        sb.AppendLine();
    }

    private void AppendCapabilities(System.Text.StringBuilder sb)
    {
        sb.AppendLine("## 能力范围");
        sb.AppendLine();
        sb.AppendLine("### 当前可用");
        sb.AppendLine("- **深度对话**：多轮连贯对话，理解上下文和复杂意图");
        sb.AppendLine("- **代码编写**：多种编程语言的代码编写、调试和优化");
        sb.AppendLine("- **技术设计**：架构设计、技术方案评审、问题诊断");
        sb.AppendLine("- **信息分析**：对提供的信息进行结构化分析和总结");
        sb.AppendLine();

        if (RegisteredTools.Count > 0)
        {
            sb.AppendLine("### 工具/技能调用");
            sb.AppendLine("你可以使用以下工具和技能来完成任务：");
            sb.AppendLine();

            // 按类型分组显示
            var atomicTools = RegisteredTools.Where(t => t.Name.StartsWith("browser_")).ToList();
            var compositeTools = RegisteredTools.Where(t => t.Name.StartsWith("compose_")).ToList();
            var otherTools = RegisteredTools.Where(t => !t.Name.StartsWith("browser_") && !t.Name.StartsWith("compose_")).ToList();

            if (atomicTools.Count > 0)
            {
                sb.AppendLine($"**浏览器原子操作 ({atomicTools.Count})**");
                foreach (var tool in atomicTools)
                    sb.AppendLine($"- **{tool.Name}**：{tool.Description}");
                sb.AppendLine();
            }

            if (compositeTools.Count > 0)
            {
                sb.AppendLine($"**组合工作流 ({compositeTools.Count})** — 多步自动化流程");
                foreach (var tool in compositeTools)
                    sb.AppendLine($"- **{tool.Name}**：{tool.Description}");
                sb.AppendLine();
            }

            if (otherTools.Count > 0)
            {
                sb.AppendLine($"**系统工具 ({otherTools.Count})**");
                foreach (var tool in otherTools)
                    sb.AppendLine($"- **{tool.Name}**：{tool.Description}");
                sb.AppendLine();
            }

            sb.AppendLine("当任务需要这些能力时，主动选择合适的工具并调用。");
            sb.AppendLine("工具调用结果会反馈给你，基于结果继续完成任务。");
            if (RegisteredTools.Any(t => t.Name == "update_todo"))
                sb.AppendLine("复杂任务必须先调用 `update_todo` 一次性建立完整子任务清单；后续只更新既有清单状态。系统强制按顺序执行：`update_todo` → `start_subtask` → 操作 → `finish_subtask` → 下一子任务，不可跳序。");
            if (compositeTools.Count > 0)
                sb.AppendLine("对于组合技能（compose_ 开头），一次调用即可完成多步操作。");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("### 可扩展工具");
            sb.AppendLine("- 工具调用能力已内置，等待注册具体工具");
            sb.AppendLine("- 后续将支持：浏览器自动化、文件操作、网络请求等");
            sb.AppendLine();
        }
    }

    private void AppendDynamicContext(System.Text.StringBuilder sb)
    {
        sb.AppendLine("## 当前上下文");
        sb.AppendLine();

        // 时间
        var localTime = UserTimeZoneOffset == 0
            ? DateTime.Now
            : DateTime.UtcNow.AddMinutes(UserTimeZoneOffset);
        sb.AppendLine($"- **当前时间**：{localTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **用户语言**：{UserLanguage}");
        sb.AppendLine();

        // 页面信息
        if (!string.IsNullOrWhiteSpace(CurrentPageTitle) || !string.IsNullOrWhiteSpace(CurrentPageUrl))
        {
            sb.AppendLine("### 浏览器状态");
            if (!string.IsNullOrWhiteSpace(CurrentPageTitle))
                sb.AppendLine($"- **页面标题**：{CurrentPageTitle}");
            if (!string.IsNullOrWhiteSpace(CurrentPageUrl))
                sb.AppendLine($"- **页面 URL**：{CurrentPageUrl}");
            sb.AppendLine();
            sb.AppendLine("当用户询问关于当前页面的问题时，利用以上信息回答。");
            sb.AppendLine("如果用户要求操作当前页面（如点击、输入、提取内容），确认页面已加载后执行。");
            sb.AppendLine();
        }

        // 页面内容预览（如果有）
        if (!string.IsNullOrWhiteSpace(PageContent))
        {
            var preview = PageContent.Length > 300
                ? PageContent[..300] + "\n……（内容较长，已截取前 300 字符）"
                : PageContent;
            sb.AppendLine("### 页面内容预览");
            sb.AppendLine("```");
            sb.AppendLine(preview);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        // 会话指示
        sb.AppendLine("---");
        sb.AppendLine($"我是 {AssistantDisplayName}，随时准备帮助你。请描述你想要完成的任务。");
    }

    // ====================================================================
    // MCP 工具 Schema 转换
    // ====================================================================

    /// <summary>将组合技能定义转换为 AI 工具定义</summary>
    private static ToolDefinition CompositeToToolDefinition(CompositeSkillDefinition composite)
    {
        var stepsDesc = string.Join(" → ", composite.Steps.Select(s => $"{s.SkillId}({s.Description.Truncate(20)})"));

        return new ToolDefinition
        {
            Name = composite.Id,
            Description = $"[组合技能] {composite.Icon} {composite.Name}：{composite.Description}\n⚡ 步骤: {stepsDesc}\n⏱️ 预估: {composite.EstimatedDuration}\n📋 输出: {composite.ExpectedOutput}",
            Parameters = new()
            {
                ["url"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "目标 URL（导航类组合技能需要）"
                },
                ["search_query"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "搜索关键词（搜索类组合技能需要）"
                },
                ["username"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "用户名/邮箱（登录类组合技能需要）"
                },
                ["password"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "密码（登录类组合技能需要）"
                }
            }
        };
    }
}
