using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BrowserDemo.Models;
using BrowserDemo.Services;
using BrowserDemo.Services.Automation;
using BrowserDemo.Services.Skills;
// 别名：区分 Models 和 Skills 中的同名类型
using SkillDef = BrowserDemo.Services.Skills.SkillDefinition;
using SkillExecResult = BrowserDemo.Services.Skills.SkillExecutionResult;
using SkillStat = BrowserDemo.Services.Skills.SkillStatus;
using CompositeSkill = BrowserDemo.Services.Skills.CompositeSkillDefinition;

namespace BrowserDemo.ViewModels;

public class ChatViewModel : INotifyPropertyChanged
{
    private readonly IAiClient _aiClient;
    private readonly ContextBuilder _contextBuilder;
    private readonly Dispatcher _uiDispatcher;
    private string _inputText = string.Empty;
    private bool _isLoading;
    private bool _isAiPanelVisible;
    private string _statusMessage = string.Empty;
    private string _currentConversationId = Guid.NewGuid().ToString("N");
    private int _tokenEstimate;
    private string? _currentPageUrl;
    private string? _currentPageTitle;
    private string _askUserDraftResponse = string.Empty;

    // ====== MCP 浏览器自动化技能系统 ======

    /// <summary>MCP 技能系统集成器（在初始化时创建）</summary>
    public SkillSystemIntegration SkillSystem { get; private set; } = new();

    /// <summary>Chrome CDP 端点（由 MainWindow 设置）</summary>
    private string? _chromeCdpEndpoint;

    /// <summary>WebView2 自动化工具路由器（Phase 4b 主路径）</summary>
    private BrowserAutomationToolRouter? _automationRouter;

    /// <summary>技能执行历史</summary>
    public ObservableCollection<SkillExecResult> SkillExecutionHistory { get; } = new();

    /// <summary>推荐的技能（基于用户输入意图）</summary>
    public ObservableCollection<SkillDef> RecommendedSkills { get; } = new();

    /// <summary>当前正在执行的技能</summary>
    private SkillExecResult? _currentSkillExecution;

    /// <summary>工具调用重试追踪：toolName → (attemptCount, lastError)</summary>
    private readonly Dictionary<string, (int Count, string? LastError)> _toolRetryTracker = new();

    /// <summary>防止主请求和 ask_user 续流并发操作同一组消息</summary>
    private readonly SemaphoreSlim _aiLoopGate = new(1, 1);

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<ConversationSummary> ConversationList { get; } = new();
    public ObservableCollection<AiTodoItem> TodoItems { get; } = new();

    /// <summary>上下文构建器引用（供外部配置工具和环境）</summary>
    public ContextBuilder ContextBuilder => _contextBuilder;

    /// <summary>浏览器当前页面 URL（设置时同步到 ContextBuilder）</summary>
    public string? CurrentPageUrl
    {
        get => _currentPageUrl;
        set
        {
            _currentPageUrl = value;
            _contextBuilder.CurrentPageUrl = value;
        }
    }

    /// <summary>浏览器当前页面标题（设置时同步到 ContextBuilder）</summary>
    public string? CurrentPageTitle
    {
        get => _currentPageTitle;
        set
        {
            _currentPageTitle = value;
            _contextBuilder.CurrentPageTitle = value;
        }
    }

    // ====== 属性 ======

    public string InputText
    {
        get => _inputText;
        set { _inputText = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public bool IsAiPanelVisible
    {
        get => _isAiPanelVisible;
        set { _isAiPanelVisible = value; OnPropertyChanged(); OnPropertyChanged(nameof(ToggleLabel)); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public int TokenEstimate
    {
        get => _tokenEstimate;
        set { _tokenEstimate = value; OnPropertyChanged(); }
    }

    public string AskUserDraftResponse
    {
        get => _askUserDraftResponse;
        set { _askUserDraftResponse = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public string ToggleLabel => IsAiPanelVisible ? "◀ 收起" : "▶ AI";

    public AiSettings AiSettings => _aiClient.Settings;

    // ====== 命令 ======

    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand NewConversationCommand { get; }
    public ICommand LoadConversationCommand { get; }
    public ICommand DeleteConversationCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    /// <summary>执行特定技能的命令</summary>
    public ICommand ExecuteSkillCommand { get; }
    /// <summary>显示技能系统状态</summary>
    public ICommand ShowSkillStatusCommand { get; }
    /// <summary>用户回答 AI 的 ask_user 问题（带选项参数）</summary>
    public ICommand RespondToQuestionCommand { get; }
    /// <summary>跳过当前问题，让 AI 自行决定</summary>
    public ICommand SkipQuestionCommand { get; }
    /// <summary>提交 ask_user 开放问题的内联回复</summary>
    public ICommand SubmitAskUserDraftCommand { get; }

    // 事件：通知 View 打开设置对话框
    public event Action? OpenSettingsRequested;

    public ChatViewModel() : this(new AiClient())
    {
    }

    public ChatViewModel(IAiClient aiClient)
    {
        using var _ = Logger.Trace("ChatViewModel::ctor");

        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _aiClient = aiClient;
        _contextBuilder = aiClient.ContextBuilder;
        Logger.Debug($"当前 AI 配置: provider={_aiClient.Settings.ProviderKey}, model={_aiClient.Settings.Model}");
        Logger.Debug($"ChatViewModel: ContextBuilder 已绑定 (IsEnabled={_contextBuilder.IsEnabled}, 已有 {_contextBuilder.RegisteredTools.Count} 个工具)");

        SendCommand = new RelayCommand(_ => SendAsync(), _ => !IsLoading && !string.IsNullOrWhiteSpace(InputText));
        ClearCommand = new RelayCommand(_ => ClearConversation());
        NewConversationCommand = new RelayCommand(_ => NewConversation());
        LoadConversationCommand = new RelayCommand(id => LoadConversation(id));
        DeleteConversationCommand = new RelayCommand(id => DeleteConversation(id));
        TogglePanelCommand = new RelayCommand(_ => IsAiPanelVisible = !IsAiPanelVisible);
        OpenSettingsCommand = new RelayCommand(_ => OpenSettingsRequested?.Invoke());
        ExecuteSkillCommand = new RelayCommand(async skillId => await ExecuteSkillProxy(skillId));
        ShowSkillStatusCommand = new RelayCommand(_ => ShowSkillStatusProxy());
        RespondToQuestionCommand = new RelayCommand(option => {
            if (option is string opt && CanRespondToAskUser)
                RespondToQuestionAsync(opt);
        }, option => option is string opt && !string.IsNullOrWhiteSpace(opt) && CanRespondToAskUser);
        SkipQuestionCommand = new RelayCommand(_ => {
            if (CanRespondToAskUser)
                RespondToQuestionAsync("__skip__");
        }, _ => CanRespondToAskUser);
        SubmitAskUserDraftCommand = new RelayCommand(_ => {
            var answer = AskUserDraftResponse.Trim();
            if (!string.IsNullOrWhiteSpace(answer) && CanRespondToAskUser)
                RespondToQuestionAsync(answer);
        }, _ => !string.IsNullOrWhiteSpace(AskUserDraftResponse) && CanRespondToAskUser);

        // ★★★ MCP 技能系统初始化在 SetChromeCdpEndpoint 被调用后开始（否则等待）★★★

        // 初始化自动添加欢迎消息
        AddSystemMessage("欢迎使用 Bermain（板儿面）智能助手！在下方输入问题开始对话。\n💡 点击 ⚙ 配置 API Key 后即可开始使用。");
        RefreshConversationList();

        Logger.Info($"ChatViewModel 初始完毕，{ConversationList.Count} 个历史对话");
    }

    // ====== MCP 浏览器自动化技能系统 ======

    /// <summary>挂载 WebView2 自动化工具路由器（Phase 4b 主路径）</summary>
    public void AttachAutomationRouter(BrowserAutomationToolRouter router)
    {
        _automationRouter = router ?? throw new ArgumentNullException(nameof(router));

        foreach (var tool in router.GetToolDefinitions())
            _contextBuilder.RegisterTool(tool);

        RegisterObserveBrowserTool();
        RegisterAskUserTool();
        RegisterSetIterationsTool();
        RegisterUpdateTodoTool();
        RegisterSubtaskTools();

        Logger.Info($"[Automation] WebView2 工具路由器已挂载: {router.GetToolDefinitions().Count} 个浏览器工具，总工具数 {_contextBuilder.RegisteredTools.Count}");
    }

    /// <summary>卸载 WebView2 自动化工具路由器</summary>
    public void DetachAutomationRouter()
    {
        _automationRouter = null;
        Logger.Info("[Automation] WebView2 工具路由器已卸载");
    }

    /// <summary>由 MainWindow 在 Chrome 启动后调用，传入 CDP 端点以初始化 MCP</summary>
    public void SetChromeCdpEndpoint(string cdpEndpoint)
    {
        _chromeCdpEndpoint = cdpEndpoint;
        Logger.Info($"[MCP] CDP 端点已设置: {cdpEndpoint}");

        // 用正确的 CDP 端点重建 SkillSystem
        SkillSystem = new SkillSystemIntegration(cdpEndpoint);

        // 启动 MCP 初始化（不阻塞 UI 线程）
        _ = InitializeMcpSkillSystemAsync();
    }

    /// <summary>异步初始化 MCP 技能系统</summary>
    private async Task InitializeMcpSkillSystemAsync()
    {
        using var _ = Logger.Trace("ChatViewModel::InitializeMcpSkillSystem");

        Logger.Info("[MCP] 正在初始化 Playwright MCP 浏览器自动化...");

        try
        {
            await SkillSystem.InitializeAsync();

            if (SkillSystem.IsInitialized)
            {
                // 导入技能到 ContextBuilder
                _contextBuilder.ImportSkillsFromRegistry(SkillSystem);

                // 注册 observe_browser 工具
                RegisterObserveBrowserTool();

                // 注册 ask_user 工具
                RegisterAskUserTool();

                // 注册 set_task_iterations 工具
                RegisterSetIterationsTool();

                // 注册 update_todo 工具
                RegisterUpdateTodoTool();

                // 注册子任务状态工具
                RegisterSubtaskTools();

                Logger.Info($"[MCP] 技能系统就绪: {_contextBuilder.RegisteredTools.Count} 个工具");
            }
            else
            {
                Logger.Warning("[MCP] 初始化失败，运行纯对话模式");
            }
        }
        catch (Exception ex)
        {
            Logger.Exception("[MCP] 初始化异常", ex);
        }
    }

    /// <summary>注册 observe_browser 浏览器观察工具</summary>
    private void RegisterObserveBrowserTool()
    {
        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "observe_browser",
            Description = "[浏览器观察] PageAgent 风格观察当前页面状态。浏览器任务中，导航/点击/滚动/等待后优先调用它重新锚定页面；返回的 [id] 可作为 browser_click/browser_type/browser_hover/browser_select_option 的 element_id。",
            Parameters = new()
            {
                ["max_elements"] = new Dictionary<string, object?>
                {
                    ["type"] = "integer",
                    ["description"] = "最多返回的可交互元素数量，默认 120，上限 200"
                }
            }
        });
    }

    /// <summary>注册 ask_user 交互工具</summary>
    private void RegisterAskUserTool()
    {
        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "ask_user",
            Description = "[用户交互] 在执行过程中暂停并向用户提问。当你遇到以下情况时使用此工具："
                + "(1) 不确定该采用哪种方案，需用户引导；(2) 需要确认潜在风险操作；"
                + "(3) 发现多个有效选项需要用户选择；(4) 需要只有用户才能提供的信息。"
                + "调用此工具后，执行会暂停，用户回答后自动恢复。",
            Parameters = new()
            {
                ["question"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "要问用户的问题，清晰明确"
                },
                ["question_type"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["enum"] = new[] { "confirmation", "multiple_choice", "open_ended" },
                    ["description"] = "问题类型"
                },
                ["options"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" },
                    ["description"] = "预设选项列表（用于 multiple_choice）"
                },
                ["context_summary"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "当前进度和发现总结"
                }
            },
            Required = new() { "question", "question_type" }
        });
    }

    /// <summary>注册 set_task_iterations 工具</summary>
    private void RegisterSetIterationsTool()
    {
        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "set_task_iterations",
            Description = "[任务规划] 动态调整剩余的迭代次数上限（1-80）。按阶段调用。",
            Parameters = new()
            {
                ["iterations"] = new Dictionary<string, object?>
                {
                    ["type"] = "integer",
                    ["description"] = "新的总迭代次数上限（1-80）"
                },
                ["reason"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "当前进度总结 + 剩余步骤估算理由"
                }
            },
            Required = new() { "iterations", "reason" }
        });
    }

    /// <summary>注册 update_todo 实时任务清单工具</summary>
    private void RegisterUpdateTodoTool()
    {
        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "update_todo",
            Description = "[实时任务清单] 创建或更新右侧 AI 面板中的任务清单。用户提出任务后的第一步必须调用本工具；items 不能为空，必须一次性写入完整子任务列表；后续实时更新只更新既有子任务的 pending、in_progress、completed 或 blocked 状态，不要做一个新增一个。",
            Parameters = new()
            {
                ["items"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["description"] = "完整任务清单，不能为空。首次调用必须包含拆分出的全部子任务；后续每次调用仍传当前完整列表，仅更新状态和说明，保持顺序与 ID 稳定。",
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "稳定短 ID，如 step1" },
                            ["title"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "任务标题" },
                            ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "pending", "in_progress", "completed", "blocked" } },
                            ["notes"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "可选进展说明" }
                        },
                        ["required"] = new[] { "id", "title", "status" }
                    }
                },
                ["summary"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["description"] = "这次更新的简短说明"
                }
            },
            Required = new() { "items" }
        });
    }

    /// <summary>注册子任务状态工具</summary>
    private void RegisterSubtaskTools()
    {
        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "start_subtask",
            Description = "[主任务拆分] 开始执行某个子任务。每个子任务执行前必须调用；系统会先压缩此前上下文，再把对应 todo 标为 in_progress。第一个子任务也必须调用。子任务开始后应先用 observe_browser 重新锚定当前页面状态。",
            Parameters = new()
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "子任务 ID，需与 update_todo 中的 id 一致" },
                ["title"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "子任务标题" },
                ["plan"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "执行该子任务的简短计划" }
            },
            Required = new() { "id", "title" }
        });

        _contextBuilder.RegisterTool(new ToolDefinition
        {
            Name = "finish_subtask",
            Description = "[主任务拆分] 结束当前子任务并更新状态。成功传 completed 后系统会立即把下一个 pending 子任务标为 in_progress；失败按 1 次重试 + 2 次换思路后仍失败时传 blocked 并报告需要用户手动处理。",
            Parameters = new()
            {
                ["id"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "子任务 ID" },
                ["status"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { "completed", "blocked" }, ["description"] = "子任务结果状态" },
                ["summary"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "成功结果或失败原因" },
                ["next_step"] = new Dictionary<string, object?> { ["type"] = "string", ["description"] = "下一步计划；blocked 时说明需要用户手动处理什么" }
            },
            Required = new() { "id", "status", "summary" }
        });
    }

    /// <summary>执行指定技能</summary>
    private async Task ExecuteSkillProxy(object? skillId)
    {
        if (skillId is not string id || string.IsNullOrWhiteSpace(id))
        {
            Logger.Warning("ExecuteSkillProxy: 无效的技能 ID");
            return;
        }

        Logger.Info($"手动执行技能: {id}");
        var skill = SkillSystem.Registry.GetSkill(id);
        if (skill == null)
        {
            StatusMessage = $"❌ 技能 '{id}' 不存在";
            return;
        }

        StatusMessage = $"🔄 执行技能: {skill.Icon} {skill.Name}...";
        var result = await Task.Run(() => SkillSystem.Executor.ExecuteAsync(id));

        SkillExecutionHistory.Add(result);
        _currentSkillExecution = result;

        StatusMessage = result.Status == SkillStat.Succeeded
            ? $"✅ 技能 '{skill.Name}' 执行成功 ({result.ElapsedMs}ms)"
            : $"❌ 技能 '{skill.Name}' 执行失败: {result.ErrorMessage}";

        Logger.Info($"技能执行完成: [{id}] {result.StatusSummary} ({result.ElapsedMs}ms)");
    }

    /// <summary>显示技能系统状态</summary>
    private void ShowSkillStatusProxy()
    {
        var summary = SkillSystem.GetStatusSummary();
        Logger.Info($"技能状态: {summary}");
        StatusMessage = summary;

        Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = $"## 🛠️ MCP 浏览器自动化状态\n\n{summary}\n\n```\n{SkillSystem.GetAllSkillsFormatted()}\n```",
            Timestamp = DateTime.Now
        });
    }

    // ====== ask_user 交互式暂停/确认机制 ======

    /// <summary>AI 是否正在等待用户回答</summary>
    public bool IsAwaitingUserInput { get; private set; }

    /// <summary>当前等待回答的问题信息</summary>
    public UserQuestionInfo? PendingAskUserQuestion { get; private set; }

    /// <summary>暂停时正在使用的消息列表（用于恢复）</summary>
    private List<ChatMessage>? _pendingMessages;

    /// <summary>暂停时的 AI 回复消息对象</summary>
    private ChatMessage? _pendingAiMsg;

    /// <summary>暂停时的工具调用 ID</summary>
    private string? _pendingToolCallId;

    /// <summary>AI 已发送的累计字符串（用于 ask_user 后追加）</summary>
    private int _pendingAiMsgInitialLength;

    /// <summary>当前可交互的 ask_user 提示消息</summary>
    private ChatMessage? _pendingAskUserPromptMsg;

    /// <summary>用户回答后继续执行流</summary>
    private bool _isResponding;

    public bool CanRespondToAskUser => IsAwaitingUserInput && !_isResponding;

    private List<ChatMessage> BuildApiConversationMessages()
        => Messages.Where(m => !m.IsAskUserPrompt).ToList();

    private static void FinalizeAssistantMessage(ChatMessage aiMsg)
    {
        // Content 保留原始分区文本；UI 通过 ThinkingContent / ConclusionContent 派生显示。
        aiMsg.NotifyContentChanged();
    }

    private void ShowAskUserPromptMessage(UserQuestionInfo questionInfo)
    {
        if (_pendingAskUserPromptMsg?.IsAskUserActive == true)
        {
            _pendingAskUserPromptMsg.IsAskUserActive = false;
            _pendingAskUserPromptMsg.NotifyContentChanged();
        }

        AskUserDraftResponse = string.Empty;

        var promptMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            DisplayRoleLabelOverride = "[AIneedhelp]",
            Content = questionInfo.Question,
            Timestamp = DateTime.Now,
            IsAskUserPrompt = true,
            IsAskUserActive = true,
            AskUserQuestionId = questionInfo.QuestionId,
            AskUserQuestionType = questionInfo.QuestionType,
            AskUserOptions = questionInfo.Options,
            AskUserContextSummary = questionInfo.ContextSummary
        };

        Messages.Add(promptMsg);
        _pendingAskUserPromptMsg = promptMsg;
        CommandManager.InvalidateRequerySuggested();
    }

    private void DeactivateAskUserPromptMessage(string? answerText)
    {
        if (_pendingAskUserPromptMsg == null) return;

        _pendingAskUserPromptMsg.IsAskUserActive = false;
        var displayAnswer = answerText == "__skip__"
            ? "让 AI 自行决定"
            : answerText?.Trim();
        if (!string.IsNullOrWhiteSpace(displayAnswer))
        {
            _pendingAskUserPromptMsg.Content = $"{_pendingAskUserPromptMsg.Content.TrimEnd()}\n\n---\n已收到你的回复：{displayAnswer}";
        }
        else
        {
            _pendingAskUserPromptMsg.NotifyContentChanged();
        }

        CommandManager.InvalidateRequerySuggested();
    }

    public async void RespondToQuestionAsync(string userResponse)
    {
        if (!IsAwaitingUserInput || _pendingMessages == null || _pendingAiMsg == null)
        {
            Logger.Warning("RespondToQuestionAsync 被调用但不在暂停状态");
            return;
        }

        // 防止快速重复点击导致重入
        if (_isResponding)
        {
            Logger.Warning("RespondToQuestionAsync 已在执行中，忽略重复调用");
            return;
        }
        _isResponding = true;
        if (!await _aiLoopGate.WaitAsync(0))
        {
            Logger.Warning("AI 工具循环正在执行，忽略重复回答");
            _isResponding = false;
            return;
        }

        var request = StartContinuationRequest();
        IsLoading = true;
        StatusMessage = "AI 继续执行中…";
        CommandManager.InvalidateRequerySuggested();
        DeactivateAskUserPromptMessage(userResponse);
        AskUserDraftResponse = string.Empty;

        // ★ 在 try 之前捕获 pending 引用，供工具循环使用 ★
        var pendingMsgs = _pendingMessages;
        var continuationAiMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };
        Messages.Add(continuationAiMsg);
        UserQuestionInfo? pausedQuestion = null;

        try
        {
            // 处理"跳过"指令
            if (userResponse == "__skip__")
            {
                userResponse = "用户选择跳过，请基于当前已有信息自行决定最佳方案并继续执行。";
            }

            Logger.Info($"用户回答了 AI 提问: {userResponse?.Truncate(100)}");
            // ★ 不在这里清除暂停状态：在 ContinueToolLoopAsync 运行期间保持
            // IsAwaitingUserInput = true，防止 UI 提前隐藏问题面板导致用户点击被忽略 ★

            // 将用户回答写回 ask_user 的 tool_result；暂停时已写入占位 Tool 消息，优先替换它
            var askUserToolResult = pendingMsgs.LastOrDefault(m =>
                m.Role == MessageRole.Tool &&
                m.ToolName == "ask_user" &&
                m.Content == "等待用户回答…");
            if (askUserToolResult != null)
            {
                askUserToolResult.Content = userResponse ?? "";
                askUserToolResult.Timestamp = DateTime.Now;
            }
            else
            {
                pendingMsgs.Add(new ChatMessage
                {
                    Role = MessageRole.Tool,
                    ToolCallId = _pendingToolCallId ?? "",
                    ToolName = "ask_user",
                    Content = userResponse ?? "",
                    Timestamp = DateTime.Now
                });
            }

            // 继续执行工具循环（使用同上的 mutableMessages + 用户回答），续流内容显示在 [AIneedhelp] 之后
            pausedQuestion = await ContinueToolLoopAsync(pendingMsgs, continuationAiMsg, request.Cts.Token);

            // ★ 如果工具循环因 ask_user 再次暂停，在此更新新问题 ★
            // 如果工具循环正常结束（pausedQuestion == null），在此清除暂停状态
            // 必须在 finally 块之前完成，确保 finally 不会过早清空 _pending*
            if (pausedQuestion != null)
            {
                PendingAskUserQuestion = pausedQuestion;
                _pendingMessages = pendingMsgs;
                _pendingAiMsg = continuationAiMsg;
                _pendingAiMsgInitialLength = continuationAiMsg.Content.Length;
                // _pendingToolCallId 需要从消息中重新提取
                var lastToolMsg = pendingMsgs.LastOrDefault(m => m.Role == MessageRole.Tool);
                _pendingToolCallId = lastToolMsg?.ToolCallId;
                ShowAskUserPromptMessage(pausedQuestion);
                OnPropertyChanged(nameof(PendingAskUserQuestion));
                OnPropertyChanged(nameof(CanRespondToAskUser));
                StatusMessage = "AI 正在等待你的回答…";
                // IsAwaitingUserInput 保持 true（已在 ContinueToolLoopAsync 运行期间保持）
            }
            else
            {
                // 工具循环正常结束 — 清除暂停状态
                IsAwaitingUserInput = false;
                PendingAskUserQuestion = null;
                _pendingMessages = null;
                _pendingAiMsg = null;
                _pendingToolCallId = null;
                _pendingAskUserPromptMsg = null;
                AskUserDraftResponse = string.Empty;
                OnPropertyChanged(nameof(IsAwaitingUserInput));
                OnPropertyChanged(nameof(PendingAskUserQuestion));
                OnPropertyChanged(nameof(CanRespondToAskUser));
            }
        }
        finally
        {
            if (request.Generation == _sendGeneration)
                _sendCts = null;
            request.Cts.Dispose();
            _aiLoopGate.Release();
            _isResponding = false;
            OnPropertyChanged(nameof(CanRespondToAskUser));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// 返回 UserQuestionInfo 表示 ask_user 暂停（Caller 需负责设置 IsAwaitingUserInput / _pending* 状态）；
    /// null 表示工具循环正常结束。
    /// </summary>
    private async Task<UserQuestionInfo?> ContinueToolLoopAsync(List<ChatMessage> messages, ChatMessage aiMsg,
        CancellationToken ct = default)
    {
        UserQuestionInfo? pausedResult = null;
        var initialLength = _pendingAiMsgInitialLength > 0 ? _pendingAiMsgInitialLength : aiMsg.Content.Length;
        var chunkCount = 0;
        var appendedChars = 0;

        Logger.Debug("[Function] ChatViewModel::ContinueToolLoopAsync start");

        try
        {
            await foreach (var chunk in _aiClient.ExecuteConversationAsync(
                    messages, ExecuteAiToolAsync, ct: ct))
            {
                // __ASK_USER_PAUSED__: 不在这里设置状态，改为捕获后返回给调用方处理
                // 避免被调用方（RespondToQuestionAsync）的 finally 块过早清空 _pending*
                if (chunk.StartsWith("__ASK_USER_PAUSED__:"))
                {
                    var json = chunk["__ASK_USER_PAUSED__:".Length..];
                    try
                    {
                        var qi = JsonSerializer.Deserialize<UserQuestionInfo>(json);
                        if (qi != null) pausedResult = qi;
                    }
                    catch (JsonException ex)
                    {
                        Logger.Warning($"解析 ask_user 续流暂停信号失败: {ex.Message}");
                    }
                    continue;
                }

                aiMsg.AppendContent(chunk);
                chunkCount++;
                appendedChars += chunk.Length;
            }

            if (pausedResult == null)
            {
                if (chunkCount == 0 && aiMsg.Content.Length <= initialLength)
                {
                    aiMsg.AppendContent("\n\n[结论]\n⚠️ AI 恢复后未返回最终结论，请重试。");
                    Logger.Warning("续流正常结束但没有收到新的文本内容");
                }

                FinalizeAssistantMessage(aiMsg);
            }
            else
            {
                aiMsg.NotifyContentChanged();
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("续流已被取消");
        }
        catch (Exception ex)
        {
            Logger.Exception("续流失败", ex);
            aiMsg.AppendContent($"\n\n[结论]\n⚠️ AI 恢复失败：{ex.Message}");
            aiMsg.NotifyContentChanged();
        }
        finally
        {
            StatusMessage = pausedResult != null ? "AI 正在等待你的回答…" : "就绪";
            IsLoading = false;
            Logger.Info($"续流完成: paused={pausedResult != null}, chunks={chunkCount}, chars={appendedChars}, total={aiMsg.Content.Length}");
            Logger.Debug($"[Function] ChatViewModel::ContinueToolLoopAsync end — paused={pausedResult != null}");
        }

        // 同步工具循环中自动追加的纯文本消息（过滤 Tool 和带 ToolCalls 的 Assistant）
        var resumedExistingIds = new HashSet<Guid>(Messages.Select(m => m.Id));
        foreach (var msg in messages)
        {
            if (resumedExistingIds.Contains(msg.Id)) continue;
            if (msg.Role == MessageRole.Tool) continue;
            if (msg is { Role: MessageRole.Assistant, HasToolCalls: true }) continue;
            Messages.Add(msg);
        }

        return pausedResult; // null = 正常结束；UserQuestionInfo = ask_user 暂停
    }

    // ====== ExecuteAiToolAsync（修改——支持 ask_user） ======

    /// <summary>执行 AI 发起的工具调用</summary>
    private async Task<string> ExecuteAiToolAsync(string toolName, Dictionary<string, object?>? args)
    {
        Logger.Debug($"[Function] ChatViewModel::ExecuteAiToolAsync({toolName}) start");
        Logger.Info($"AI 工具调用: {toolName}");

        // ★★★ 处理 observe_browser 工具：包装页面快照为 PageAgent 风格 browser_state ★★★
        if (toolName == "observe_browser")
        {
            if (_automationRouter == null)
                return "❌ observe_browser 不可用：浏览器自动化工具尚未初始化，请等待浏览器加载完成。";

            var maxElements = Math.Clamp(GetArg<int?>(args ?? new(), "max_elements") ?? 120, 1, 200);
            var snapshot = await _automationRouter.InvokeAsync("browser_snapshot", new Dictionary<string, object?>());
            return FormatBrowserObservation(snapshot, maxElements);
        }

        // ★★★ 处理 ask_user 工具：暂停循环，等待用户回答 ★★★
        if (toolName == "ask_user" && args != null)
        {
            Logger.Info($"AI 请求用户指引 → 暂停执行");
            var question = GetArg<string>(args, "question") ?? "（无问题）";
            var questionType = GetArg<string>(args, "question_type") ?? "confirmation";
            var optionsJson = GetArg<object?>(args, "options");
            var contextSummary = GetArg<string>(args, "context_summary");
            var defaultOption = GetArg<string>(args, "default_option");

            List<string>? options = null;
            if (optionsJson is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
                options = System.Text.Json.JsonSerializer.Deserialize<List<string>>(je.GetRawText());

            _pendingToolCallId = args.TryGetValue("tool_call_id", out var tid) ? tid?.ToString() : null;

            var questionInfo = new UserQuestionInfo
            {
                QuestionId = $"q_{DateTime.Now:HHmmssfff}_{Guid.NewGuid():N}",
                Question = question,
                QuestionType = questionType,
                Options = options?.ToArray(),
                ContextSummary = contextSummary,
                DefaultOption = defaultOption
            };

            var json = System.Text.Json.JsonSerializer.Serialize(questionInfo);
            Logger.Debug($"[Function] ChatViewModel::ExecuteAiToolAsync end — ask_user 暂停");
            return $"__ASK_USER_PAUSED__:{json}";
        }

        // ★★★ 处理 set_task_iterations 工具 ★★★
        if (toolName == "set_task_iterations" && args != null)
        {
            var iterations = GetArg<int?>(args, "iterations") ?? 30;
            var reason = GetArg<string>(args, "reason") ?? "（未说明）";
            iterations = Math.Clamp(iterations, 1, 80);

            var success = _aiClient.TrySetMaxIterations(iterations);
            var msg = success
                ? $"✅ 已调整迭代次数上限为 {iterations} 次。当前进度：{reason}"
                : $"❌ 设置失败：迭代次数必须在 1-80 之间，当前值 {iterations} 无效。";
            Logger.Info($"set_task_iterations: {msg}");
            return msg;
        }

        // ★★★ 处理 update_todo 工具：实时刷新 UI 任务清单 ★★★
        if (toolName == "update_todo" && args != null)
        {
            var items = GetTodoItems(args);
            var summary = GetArg<string>(args, "summary") ?? $"已更新 {items.Count} 个任务";
            if (items.Count == 0)
            {
                Logger.Warning("update_todo 收到空任务清单，已拒绝清空 UI");
                return "❌ update_todo 参数无效：items 不能为空。请先将用户任务拆分为 2-6 个可执行子任务，并一次性传入完整清单。";
            }

            RunOnUiThread(() =>
            {
                var existingMap = TodoItems.ToDictionary(x => x.Id, x => x);
                TodoItems.Clear();
                foreach (var item in items)
                {
                    // 合并策略：如果该 ID 的任务已存在且新 status 为默认 pending，则保留原有状态
                    // 防止 AI 未发送 status 字段时把已完成的任务误重置为待办
                    if (existingMap.TryGetValue(item.Id, out var existing) && item.Status == "pending")
                    {
                        item.Status = existing.Status;
                    }
                    TodoItems.Add(item);
                }
                OnPropertyChanged(nameof(TodoItems));
                StatusMessage = $"🧭 {summary}";
            });

            _contextBuilder.RecordRuntimeToolEvidence("update_todo");
            _contextBuilder.RuntimeHasTodoItems = true;

            Logger.Info($"update_todo: {summary} ({items.Count}项)");
            return $"✅ Todo list updated: {summary}";
        }

        // ★★★ 处理子任务边界工具：标记状态并触发上下文压缩 ★★★
        if (toolName == "start_subtask" && args != null)
        {
            var id = GetArg<string>(args, "id")?.Trim();
            var title = GetArg<string>(args, "title")?.Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
                return "❌ start_subtask 参数无效：id 和 title 不能为空。";

            var plan = GetArg<string>(args, "plan") ?? "开始执行子任务";
            var exists = RunOnUiThread(() => TodoItems.Any(x => x.Id == id));
            if (!exists)
            {
                Logger.Warning($"start_subtask 收到未在完整清单中预先登记的子任务: {id} — {title}");
                return $"❌ start_subtask 未找到子任务：{id}。必须先通过 update_todo 一次性建立完整任务清单，再开始第一个子任务。";
            }
            UpdateTodoItem(id, title, "in_progress", plan);
            _contextBuilder.RecordRuntimeToolEvidence("start_subtask");
            _contextBuilder.RuntimeActiveSubtaskId = id;

            var msg = $"▶️ 开始子任务：{title}。执行前已压缩此前上下文。计划：{plan}";
            Logger.Info($"start_subtask: {msg}");
            return $"__SUBTASK_CONTEXT_COMPRESSED__:{msg}";
        }

        if (toolName == "finish_subtask" && args != null)
        {
            var id = GetArg<string>(args, "id")?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                return "❌ finish_subtask 参数无效：id 不能为空。";

            // finish_subtask 默认表示完成，未传 status 时默认为 completed 而非 pending
            var rawStatus = GetArg<string>(args, "status") ?? "completed";
            var status = NormalizeTodoStatus(rawStatus);
            var summary = GetArg<string>(args, "summary") ?? "（无总结）";
            var nextStep = GetArg<string>(args, "next_step");
            var title = RunOnUiThread(() => TodoItems.FirstOrDefault(x => x.Id == id)?.Title);
            if (string.IsNullOrWhiteSpace(title))
                return $"❌ finish_subtask 未找到子任务：{id}。请先通过 update_todo 建立完整任务清单。";

            UpdateTodoItem(id, title, status, summary);
            _contextBuilder.RecordRuntimeToolEvidence("finish_subtask");
            var nextTodo = status == "completed" ? TryStartNextPendingTodo(id, nextStep) : null;
            _contextBuilder.RuntimeActiveSubtaskId = nextTodo?.Id;

            var msg = status == "blocked"
                ? $"❌ 子任务受阻：{title}。原因：{summary}。请用户手动处理：{nextStep ?? "请检查当前页面或操作环境。"}"
                : nextTodo != null
                    ? $"✅ 子任务完成：{title}。结果：{summary}\n▶️ 已自动开始下一子任务：{nextTodo.Title}。计划：{nextTodo.Notes}"
                    : $"✅ 子任务完成：{title}。结果：{summary}";
            Logger.Info($"finish_subtask: {msg}");
            return msg;
        }

        // ★★★ WebView2 自动化工具（Phase 4b 主路径）★★★
        if (_automationRouter != null && _automationRouter.IsToolRegistered(toolName))
        {
            var (attemptCount, _) = _toolRetryTracker.GetValueOrDefault(toolName);
            attemptCount++;
            _toolRetryTracker[toolName] = (attemptCount, null);

            var retryDelay = attemptCount switch
            {
                1 => 0,
                2 => 1000,
                3 => 2000,
                _ => 3000
            };

            if (retryDelay > 0)
            {
                Logger.Debug($"[Automation] 第 {attemptCount} 次重试 \"{toolName}\"，等待 {retryDelay}ms");
                await Task.Delay(retryDelay);
            }

            var callArgs = args ?? new Dictionary<string, object?>();

            if (attemptCount >= 3 && callArgs.ContainsKey("element_id"))
            {
                Logger.Debug("[Automation] 元素 id 可能已过期，刷新页面快照");
                var snapshot = await _automationRouter.InvokeAsync("browser_snapshot", new Dictionary<string, object?>());
                return "⚠️ 工具执行前检测到 element_id 可能已过期。已刷新页面快照，请从下面的新快照中重新选择 elements[*].id 后再次调用工具，不要继续使用旧 id。\n\n" + snapshot;
            }

            try
            {
                Logger.Info($"[Automation] 调用工具: {toolName} (尝试 #{attemptCount})");
                var result = await _automationRouter.InvokeAsync(toolName, callArgs);
                Logger.Debug($"[Automation] {toolName} 返回: {result.Truncate(300)}");

                if (result.Contains("\"ok\":false", StringComparison.OrdinalIgnoreCase))
                {
                    _toolRetryTracker[toolName] = (attemptCount, result);
                    if (attemptCount < 4)
                    {
                        var hint = attemptCount switch
                        {
                            1 => "重试 1 次看看是否能恢复",
                            2 => "先 browser_snapshot 获取最新页面，再换新的 element_id 操作",
                            _ => "如果多种方法仍不行，调用 ask_user 向用户求助"
                        };
                        return $"⚠️ 工具执行失败 (第{attemptCount}次): {result}\n建议: {hint}";
                    }

                    _toolRetryTracker.Remove(toolName);
                    return $"❌ 工具执行失败 (已尝试{attemptCount}次): {result}\n" +
                           "已连续失败多次，请使用 ask_user 向用户描述已尝试的方法和现象，获取用户指引。";
                }

                _toolRetryTracker.Remove(toolName);
                return result;
            }
            catch (Exception ex)
            {
                _toolRetryTracker[toolName] = (attemptCount, ex.Message);
                Logger.Warning($"[Automation] 工具 {toolName} 第 {attemptCount} 次异常: {ex.Message}");

                if (attemptCount < 4)
                    return $"⚠️ 工具执行异常 (第{attemptCount}次): {ex.Message}\n建议: 先 browser_snapshot 获取最新页面状态，再换新 element_id 重试。";

                _toolRetryTracker.Remove(toolName);
                return $"❌ 工具执行异常 (已尝试{attemptCount}次): {ex.Message}\n请使用 ask_user 向用户求助。";
            }
        }

        // ★★★ 检查是否 MCP 工具（直接调用，旧路径备用）★★★
        if (SkillSystem.IsInitialized && SkillSystem.McpClient.Tools.Any(t => t.Name == toolName))
        {
            // ===== Solve.md 重试逻辑 =====
            // 尝试 1: 直接执行
            // 尝试 2: 立即重试 1 次（页面可能抖动）
            // 尝试 3-4: 换方法重试 2 次
            // 失败: 返回详细错误信息 + ask_user 提示

            // 获取或初始化重试状态
            var (attemptCount, lastError) = _toolRetryTracker.GetValueOrDefault(toolName);
            attemptCount++;
            _toolRetryTracker[toolName] = (attemptCount, null);

            // 根据尝试次数切换策略
            var retryDelay = attemptCount switch
            {
                1 => 0,          // 第一次：不等待
                2 => 1000,       // 第二次：等 1 秒后重试
                3 => 2000,       // 第三次：等 2 秒，换方法
                _ => 3000        // 第四次及以上
            };

            if (retryDelay > 0)
            {
                Logger.Debug($"[Solve] 第 {attemptCount} 次重试 \"{toolName}\"，等待 {retryDelay}ms");
                await Task.Delay(retryDelay);
            }

            // 准备参数 — 换方法时调整策略
            var callArgs = args ?? new Dictionary<string, object?>();

            // 第 3/4 次重试时自动切换定位方式
            if (attemptCount >= 3 && callArgs.ContainsKey("element"))
            {
                // 如果之前用 xp= hash，现在尝试用 text 描述
                Logger.Debug($"[Solve] 换方法：尝试文本描述定位替代 hash");
            }
            if (attemptCount >= 3 && callArgs.ContainsKey("target"))
            {
                Logger.Debug($"[Solve] 换方法：尝试先 snapshot 获取最新状态");
                try { await SkillSystem.McpClient.CallToolAsync("browser_snapshot"); } catch { }
            }

            try
            {
                Logger.Info($"[MCP] 调用工具: {toolName} (尝试 #{attemptCount})");
                var result = await SkillSystem.McpClient.CallToolAsync(toolName, callArgs);
                Logger.Debug($"[MCP] {toolName} 返回: {result.Truncate(300)}");

                // 成功 → 清除重试记录
                _toolRetryTracker.Remove(toolName);
                return result;
            }
            catch (Exception ex)
            {
                _toolRetryTracker[toolName] = (attemptCount, ex.Message);
                Logger.Warning($"[MCP] 工具 {toolName} 第 {attemptCount} 次失败: {ex.Message}");

                if (attemptCount < 4)
                {
                    // 未达上限，返回失败信息让 AI 自己决定是否继续重试
                    var hint = attemptCount switch
                    {
                        1 => "重试 1 次看看是否能恢复",
                        2 => "换个方法试试（换选择器、先获取快照、等待片刻）",
                        _ => "如果尝试了多种方法仍不行，调用 ask_user 向用户求助"
                    };
                    return $"⚠️ 工具执行失败 (第{attemptCount}次): {ex.Message}\n建议: {hint}";
                }

                // 超过 4 次 → 清除记录，返回严重错误提示 AI 求助用户
                _toolRetryTracker.Remove(toolName);
                return $"❌ 工具执行失败 (已尝试{attemptCount}次): {ex.Message}\n" +
                       "已连续失败多次，请使用 ask_user 向用户描述已尝试的方法和现象，获取用户指引。";
            }
        }

        // ★★★ 检查是否组合技能 ★★★
        if (SkillSystem.IsInitialized)
        {
            var compositeSkill = SkillSystem.Registry.GetSkill<CompositeSkill>(toolName);
            if (compositeSkill != null)
            {
                Logger.Info($"[MCP] 执行组合技能: {toolName}");
                var result = await SkillSystem.Executor.ExecuteAsync(toolName, args ?? new());
                if (result.Status == SkillStat.Succeeded)
                {
                    var outputText = result.Outputs.GetValueOrDefault("result")?.ToString();
                    return result.Summary + (outputText != null ? $"\n\n{outputText.Truncate(8000)}" : "");
                }
                return $"组合技能执行失败: {result.ErrorMessage}";
            }
        }

        Logger.Warning($"AI 调用了未注册的工具: {toolName}");
        return $"错误: 工具 '{toolName}' 未注册";
    }

    /// <summary>当前请求的取消令牌源（新请求会取消上一个未完成的请求）</summary>
    private CancellationTokenSource? _sendCts;
    private int _sendGeneration;

    private (CancellationTokenSource Cts, int Generation) StartSendRequest()
    {
        _sendCts?.Cancel();
        var cts = new CancellationTokenSource();
        var generation = ++_sendGeneration;
        _sendCts = cts;
        return (cts, generation);
    }

    private (CancellationTokenSource Cts, int Generation) StartContinuationRequest()
    {
        var cts = new CancellationTokenSource();
        var generation = ++_sendGeneration;
        _sendCts = cts;
        return (cts, generation);
    }

    /// <summary>取消当前正在进行的 AI 流式请求（由 MainWindow 关闭时调用）</summary>
    public void CancelActiveRequest()
    {
        if (_sendCts == null) return;
        Logger.Debug("取消当前 AI 请求");
        _sendCts.Cancel();
        _sendCts = null;
        _sendGeneration++;
    }

    public async void SendAsync()
    {
        var text = InputText?.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsLoading) return;

        if (IsAwaitingUserInput && _pendingMessages != null && _pendingAiMsg != null)
        {
            InputText = string.Empty;
            IsLoading = true;
            StatusMessage = "AI 继续执行中…";
            RespondToQuestionAsync(text);
            return;
        }

        var loopGateHeld = await _aiLoopGate.WaitAsync(0);
        if (!loopGateHeld)
        {
            Logger.Warning("AI 工具循环正在执行，忽略重复发送");
            return;
        }

        IsAwaitingUserInput = false;
        PendingAskUserQuestion = null;
        _pendingMessages = null;
        _pendingAiMsg = null;
        _pendingToolCallId = null;
        _pendingAskUserPromptMsg = null;
        AskUserDraftResponse = string.Empty;
        _isResponding = false;
        OnPropertyChanged(nameof(IsAwaitingUserInput));
        OnPropertyChanged(nameof(PendingAskUserQuestion));
        OnPropertyChanged(nameof(CanRespondToAskUser));
        CommandManager.InvalidateRequerySuggested();

        // 取消上次未完成的请求，防止并发请求
        var request = StartSendRequest();
        var ct = request.Cts.Token;

        Logger.Info($"══════ AI 请求 ══════");
        Logger.Info($"用户输入: {text}");
        Logger.Debug($"══════ 用户输入 ({text.Length}字符) ══════");
        Logger.Debug(text);
        Logger.Debug($"══════ 用户输入 结束 ══════");
        Logger.Debug($"当前 AI 配置: provider={_aiClient.Settings.ProviderKey}, model={_aiClient.Settings.Model}");

        InputText = string.Empty;
        IsLoading = true;
        StatusMessage = "AI 思考中…";

        // 添加用户消息
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.User,
            Content = text,
            Timestamp = DateTime.Now
        });

        // 占位 AI 回复
        var aiMsg = new ChatMessage
        {
            Role = MessageRole.Assistant,
            Content = "",
            Timestamp = DateTime.Now
        };
        Messages.Add(aiMsg);

        // 捕获 UI 线程 SynchronizationContext，用于跨线程推送 UI 更新
        var uiContext = SynchronizationContext.Current;
        var chunkCount = 0;
        // 节流：至少 40ms 才更新一次 UI，避免 UI 线程被流式更新淹没
        var lastUiUpdate = Stopwatch.StartNew();

        // 动态节流：内容越长越降低 UI 更新频率，减轻 Markdown 转换器压力
        int GetUiThrottleMs(int contentLength) => contentLength switch
        {
            < 500 => 80,
            < 2000 => 150,
            < 5000 => 300,
            < 12000 => 600,
            _ => 1200
        };

        // ===== 流式累计日志：只记录新增片段，避免长回复反复写入完整内容导致 UI/磁盘卡顿 =====
        var lastInfoLog = Stopwatch.StartNew();
        const int infoLogIntervalMs = 1500;
        var maxContentLogged = 0;
        const int infoLogMinChars = 4;
        const int maxLogSnippetChars = 500;

        void LogStreamingProgress()
        {
            if (lastInfoLog.ElapsedMilliseconds < infoLogIntervalMs) return;

            var currentContent = aiMsg.Content;
            if (currentContent.Length >= infoLogMinChars && currentContent.Length > maxContentLogged)
            {
                var newPart = currentContent.Length - maxContentLogged > 3
                    ? currentContent.Substring(maxContentLogged)
                    : "";
                Logger.Info($"  AI回复累计 ({currentContent.Length}字符){(!string.IsNullOrEmpty(newPart) ? $" +\"{newPart.Truncate(maxLogSnippetChars)}\"" : "")}");
                maxContentLogged = currentContent.Length;
            }
            lastInfoLog.Restart();
        }

        var hasTools = _contextBuilder.RegisteredTools.Count > 0;

        try
        {
            if (hasTools)
            {
                // ★★★ 支持工具调用的完整循环 ★★★
                // 使用可变的 List 以支持 ExecuteConversationAsync 的内部追加；过滤 ask_user UI 提示卡片
                var mutableMessages = BuildApiConversationMessages();

                await foreach (var chunk in _aiClient.ExecuteConversationAsync(
                        mutableMessages, ExecuteAiToolAsync, ct: ct))
                    {
                        if (request.Generation != _sendGeneration || ct.IsCancellationRequested)
                            throw new OperationCanceledException(ct);

                        // ★★★ 检测 ask_user 暂停信号 ★★★
                        if (chunk.StartsWith("__ASK_USER_PAUSED__:"))
                        {
                            var json = chunk["__ASK_USER_PAUSED__:".Length..];
                            try
                            {
                                var questionInfo = System.Text.Json.JsonSerializer.Deserialize<UserQuestionInfo>(json);
                                if (questionInfo != null)
                                {
                                    IsAwaitingUserInput = true;
                                    PendingAskUserQuestion = questionInfo;
                                    _pendingMessages = mutableMessages;
                                    _pendingAiMsg = aiMsg;
                                    _pendingAiMsgInitialLength = aiMsg.Content.Length;
                                    _pendingToolCallId = null;
                                    ShowAskUserPromptMessage(questionInfo);
                                    Logger.Info($"AI 向用户提问: {questionInfo.Question}");
                                    uiContext?.Post(_ => OnPropertyChanged(nameof(IsAwaitingUserInput)), null);
                                    uiContext?.Post(_ => OnPropertyChanged(nameof(PendingAskUserQuestion)), null);
                                    uiContext?.Post(_ => OnPropertyChanged(nameof(CanRespondToAskUser)), null);
                                    break; // 跳出 foreach，下面的同步代码仍会执行
                                }
                            }
                            catch (System.Text.Json.JsonException ex)
                            {
                                Logger.Warning($"解析 ask_user 暂停信号失败: {ex.Message}");
                            }
                            continue;
                        }

                        aiMsg.AppendContent(chunk);
                        chunkCount++;

                        LogStreamingProgress();

                        var throttleMs = GetUiThrottleMs(aiMsg.Content.Length);
                        if (lastUiUpdate.ElapsedMilliseconds >= throttleMs)
                        {
                            aiMsg.NotifyContentChanged();
                            lastUiUpdate.Restart();
                        }
                    }

                // 将工具循环中自动追加的 assistant/tool 消息同步到 ObservableCollection
                // ★★★ 严格只同步纯文本回复：跳过 Tool 角色和带 ToolCalls 的 Assistant 消息 ★★★
                {
                    var existingIds = new HashSet<Guid>(Messages.Select(m => m.Id));
                    foreach (var msg in mutableMessages)
                    {
                        if (existingIds.Contains(msg.Id)) continue;
                        if (msg.Role == MessageRole.Tool) continue;
                        if (msg is { Role: MessageRole.Assistant, HasToolCalls: true }) continue;
                        Messages.Add(msg);
                    }
                }

                // 刷新分区显示；ask_user 暂停不是最终结论
                if (IsAwaitingUserInput)
                {
                    aiMsg.NotifyContentChanged();
                }
                else
                {
                    FinalizeAssistantMessage(aiMsg);
                    TryFinalizeTodoAfterAssistantReply(aiMsg.Content);
                }
                StatusMessage = IsAwaitingUserInput ? "AI 正在等待你的回答…" : GetStatusMessageAfterToolLoop(aiMsg.Content);
                Logger.Info($"AI 请求完成（工具循环）: {chunkCount} 个数据块, {aiMsg.Content.Length} 字符");
                if (aiMsg.Content.Length > maxContentLogged + 3)
                {
                    Logger.Info($"  AI回复最终 ({aiMsg.Content.Length}字符) 尾部预览: {aiMsg.Content.Truncate(maxLogSnippetChars)}");
                }
                else
                {
                    Logger.Info($"  AI回复 完成 — 以上为最终累计内容 ({aiMsg.Content.Length}字符)");
                }
            }
            else
            {
                // 无工具 — 原始流式文本路径
                await foreach (var chunk in _aiClient.StreamMessageAsync(Messages, ct))
                {
                    if (request.Generation != _sendGeneration || ct.IsCancellationRequested)
                        throw new OperationCanceledException(ct);

                    aiMsg.AppendContent(chunk);
                    chunkCount++;

                    Logger.Debug($"  AI回复流 chunk#{chunkCount}: +{chunk.Length} 字符 → \"{chunk}\"");

                    LogStreamingProgress();

                    var throttleMs = GetUiThrottleMs(aiMsg.Content.Length);
                    if (lastUiUpdate.ElapsedMilliseconds >= throttleMs)
                    {
                        aiMsg.NotifyContentChanged();
                        lastUiUpdate.Restart();
                    }
                }

                // 兜底清洗
                FinalizeAssistantMessage(aiMsg);
                StatusMessage = "就绪";
                Logger.Info($"AI 请求完成: {chunkCount} 个数据块, {aiMsg.Content.Length} 字符");
                if (aiMsg.Content.Length > maxContentLogged + 3)
                {
                    Logger.Info($"  AI回复最终 ({aiMsg.Content.Length}字符) 尾部预览: {aiMsg.Content.Truncate(maxLogSnippetChars)}");
                }
                else
                {
                    Logger.Info($"  AI回复 完成 — 以上为最终累计内容 ({aiMsg.Content.Length}字符)");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("AI 请求已被取消");
            if (request.Generation == _sendGeneration)
            {
                aiMsg.AppendContent("\n\n[结论]\n⏸️ 请求已被取消");
                aiMsg.NotifyContentChanged();
                StatusMessage = "就绪";
            }
        }
        catch (Exception ex)
        {
            Logger.Exception("AI 流式请求失败", ex);
            var errMsg = $"\n\n[结论]\n⚠️ 请求失败：{ex.Message}";
            aiMsg.AppendContent(errMsg);
            aiMsg.NotifyContentChanged();
            StatusMessage = "错误";
            Logger.Error($"══════ AI 回复(错误) ══════");
            Logger.Error(errMsg);
            Logger.Error($"══════ AI 回复(错误) 结束 ══════");
        }
        finally
        {
            if (request.Generation == _sendGeneration)
            {
                _sendCts = null;
                IsLoading = false;
                CommandManager.InvalidateRequerySuggested();
                if (!IsAwaitingUserInput)
                {
                    UpdateTokenEstimate();
                    AutoSave();
                }
            }

            request.Cts.Dispose();
            if (loopGateHeld)
                _aiLoopGate.Release();
        }
    }

    // ====== 对话管理 ======

    public void NewConversation()
    {
        Logger.Info("新建对话");
        CancelActiveRequest();
        IsLoading = false;
        IsAwaitingUserInput = false;
        PendingAskUserQuestion = null;
        _pendingMessages = null;
        _pendingAiMsg = null;
        _pendingToolCallId = null;
        _pendingAskUserPromptMsg = null;
        AskUserDraftResponse = string.Empty;
        OnPropertyChanged(nameof(IsAwaitingUserInput));
        OnPropertyChanged(nameof(PendingAskUserQuestion));
        OnPropertyChanged(nameof(CanRespondToAskUser));
        CommandManager.InvalidateRequerySuggested();
        _currentConversationId = Guid.NewGuid().ToString("N");
        Messages.Clear();
        TodoItems.Clear();
        _contextBuilder.ClearRuntimeState();
        AddSystemMessage("新的对话已开始。有什么需要帮忙的吗？我是 Bermain（板儿面）。");
        RefreshConversationList();
        // GC.Collect() 已移除：在 UI 线程上触发 Gen2 阻塞回收会导致界面卡死
        Logger.Debug("对话已重置");
    }

    public void ClearConversation()
    {
        Logger.Debug("清空当前对话");
        CancelActiveRequest();
        IsLoading = false;
        IsAwaitingUserInput = false;
        PendingAskUserQuestion = null;
        _pendingMessages = null;
        _pendingAiMsg = null;
        _pendingToolCallId = null;
        _pendingAskUserPromptMsg = null;
        AskUserDraftResponse = string.Empty;
        OnPropertyChanged(nameof(IsAwaitingUserInput));
        OnPropertyChanged(nameof(PendingAskUserQuestion));
        OnPropertyChanged(nameof(CanRespondToAskUser));
        CommandManager.InvalidateRequerySuggested();
        Messages.Clear();
        TodoItems.Clear();
        _contextBuilder.ClearRuntimeState();
        AddSystemMessage("对话已清空。");
    }

    public void LoadConversation(object? id)
    {
        if (id is not string convId)
        {
            Logger.Warning($"LoadConversation: 无效参数 {id?.GetType().Name ?? "null"}");
            return;
        }

        Logger.Info($"加载对话: {convId}");
        CancelActiveRequest();
        IsLoading = false;
        IsAwaitingUserInput = false;
        PendingAskUserQuestion = null;
        _pendingMessages = null;
        _pendingAiMsg = null;
        _pendingToolCallId = null;
        _pendingAskUserPromptMsg = null;
        AskUserDraftResponse = string.Empty;
        OnPropertyChanged(nameof(IsAwaitingUserInput));
        OnPropertyChanged(nameof(PendingAskUserQuestion));
        OnPropertyChanged(nameof(CanRespondToAskUser));
        CommandManager.InvalidateRequerySuggested();
        var msgs = ConversationService.LoadConversation(convId);
        if (msgs == null)
        {
            Logger.Warning($"对话 {convId} 不存在");
            return;
        }

        _currentConversationId = convId;
        Messages.Clear();
        TodoItems.Clear();
        _contextBuilder.ClearRuntimeState();
        foreach (var m in msgs)
            if (m.Role != MessageRole.Tool)
                Messages.Add(m);
        StatusMessage = $"已加载 {msgs.Count} 条消息";
        UpdateTokenEstimate();
        Logger.Info($"对话已加载: {msgs.Count} 条消息, {msgs.Sum(m => m.Content.Length)} 字符");
    }

    public void DeleteConversation(object? id)
    {
        if (id is not string convId) return;
        Logger.Info($"删除对话: {convId}");
        ConversationService.DeleteConversation(convId);
        RefreshConversationList();
    }

    public void RefreshConversationList()
    {
        ConversationList.Clear();
        foreach (var c in ConversationService.ListConversations())
            ConversationList.Add(c);
    }

    private void AutoSave()
    {
        if (Messages.Count > 1)
        {
            Logger.Debug($"自动保存对话: {_currentConversationId} ({Messages.Count} 条消息)");
            ConversationService.SaveConversation(_currentConversationId, Messages.ToList());
            RefreshConversationList();
        }
    }

    // ====== 设置管理 ======

    public void ApplySettings(AiSettings settings)
    {
        Logger.Info($"应用 AI 设置: provider={settings.ProviderKey}, model={settings.Model}");
        _aiClient.Settings = settings;
        _aiClient.SaveSettings();
    }

    public async Task<bool> TestConnectionAsync()
    {
        StatusMessage = "测试连接中…";
        Logger.Info("手动测试 AI 连接");
        var ok = await _aiClient.TestConnectionAsync();
        Logger.Info($"连接测试结果: {(ok ? "成功" : "失败")}");
        StatusMessage = ok ? "✅ 连接成功" : "❌ 连接失败，请检查 API Key";
        return ok;
    }

    private static string FormatBrowserObservation(string snapshotResult, int maxElements)
    {
        try
        {
            using var outerDoc = JsonDocument.Parse(snapshotResult);
            var outer = outerDoc.RootElement;
            var ok = outer.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var error = GetJsonString(outer, "error") ?? "未知错误";
                return $"❌ observe_browser 获取失败: {error}\n原始返回: {snapshotResult.Truncate(2000)}";
            }

            var data = GetJsonString(outer, "data");
            if (string.IsNullOrWhiteSpace(data))
                return $"⚠️ observe_browser 未获得页面快照数据。原始返回: {snapshotResult.Truncate(2000)}";

            using var snapshotDoc = JsonDocument.Parse(data);
            var snapshot = snapshotDoc.RootElement;
            if (snapshot.ValueKind != JsonValueKind.Object)
            {
                var currentUrl = GetJsonString(outer, "url") ?? "";
                return "⚠️ observe_browser 暂时没有可用页面结构。页面可能仍在加载、脚本尚未注入，或当前站点暂不允许结构化读取。"
                    + $"\nCurrent URL: {currentUrl}"
                    + "\n建议: 先调用 browser_wait(ms=1000) 或等待页面标题稳定后再次 observe_browser；如果仍失败，再用 browser_screenshot 做最后视觉确认。";
            }

            var title = GetJsonString(snapshot, "title") ?? "";
            var url = GetJsonString(snapshot, "url") ?? GetJsonString(outer, "url") ?? "";
            var snapshotAt = GetJsonString(snapshot, "snapshotAt") ?? DateTime.Now.ToString("O");
            var elementCount = GetJsonInt(snapshot, "elementCount") ?? 0;
            var truncated = GetJsonBool(snapshot, "truncated") ?? false;

            var sb = new StringBuilder();
            sb.AppendLine("<browser_state>");
            sb.AppendLine($"Current Page: [{EscapeXml(title)}]({url})");
            sb.AppendLine($"Snapshot time: {snapshotAt}");
            sb.AppendLine($"Interactive elements: {elementCount}, truncated: {truncated.ToString().ToLowerInvariant()}");
            sb.AppendLine();
            sb.AppendLine("Interactive Elements:");

            var rendered = 0;
            if (snapshot.TryGetProperty("elements", out var elements) && elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in elements.EnumerateArray())
                {
                    if (rendered >= maxElements) break;
                    var line = FormatObservedElement(element);
                    if (sb.Length + line.Length > 16000)
                    {
                        sb.AppendLine("... output truncated to keep context compact ...");
                        break;
                    }
                    sb.AppendLine(line);
                    rendered++;
                }
            }

            if (elementCount > rendered)
                sb.AppendLine($"... {elementCount - rendered} more elements omitted; call observe_browser with a focused task or use browser_snapshot if raw JSON is needed ...");

            sb.AppendLine("</browser_state>");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"⚠️ observe_browser 已获取原始快照，但格式化失败: {ex.Message}\n\n原始返回:\n{snapshotResult.Truncate(12000)}";
        }
    }

    private static string FormatObservedElement(JsonElement element)
    {
        var id = GetJsonInt(element, "id")?.ToString() ?? "?";
        var tag = GetJsonString(element, "tag") ?? "element";
        var role = GetJsonString(element, "role");
        var type = GetJsonString(element, "type");
        var name = GetJsonString(element, "name");
        var ariaLabel = GetJsonString(element, "aria_label");
        var placeholder = GetJsonString(element, "placeholder");
        var value = string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)
            ? "******"
            : GetJsonString(element, "value");
        var text = GetJsonString(element, "text");
        var visible = GetJsonBool(element, "visible");
        var disabled = GetJsonBool(element, "disabled") == true;
        var readOnly = GetJsonBool(element, "readonly") == true;

        var attrs = new List<string>();
        AddAttr(attrs, "role", role);
        AddAttr(attrs, "type", type);
        AddAttr(attrs, "name", name);
        AddAttr(attrs, "aria-label", ariaLabel);
        AddAttr(attrs, "placeholder", placeholder);
        AddAttr(attrs, "value", value);
        if (visible == false) attrs.Add("visible=\"false\"");
        if (disabled) attrs.Add("disabled=\"true\"");
        if (readOnly) attrs.Add("readonly=\"true\"");

        var label = FirstNonEmpty(text, ariaLabel, placeholder, name, value)?.Truncate(160) ?? "";
        var attrText = attrs.Count > 0 ? " " + string.Join(" ", attrs) : "";
        return $"[{id}]<{tag}{attrText}>{EscapeXml(label)}</{tag}>";
    }

    private static void AddAttr(List<string> attrs, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            attrs.Add($"{name}=\"{EscapeXml(value.Truncate(80))}\"");
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.GetRawText();
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
        return prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out value) ? value : null;
    }

    private static bool? GetJsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var value) => value,
            _ => null
        };
    }

    private static readonly System.Text.Json.JsonSerializerOptions _argJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>从参数字典中提取指定类型的值（支持 JsonElement 自动转换）</summary>
    private static T? GetArg<T>(Dictionary<string, object?> args, string key)
    {
        if (args.TryGetValue(key, out var val) && val != null)
        {
            if (val is T t) return t;
            if (val is System.Text.Json.JsonElement je)
            {
                try { return System.Text.Json.JsonSerializer.Deserialize<T>(je.GetRawText(), _argJsonOptions); }
                catch { }
            }
        }
        return default;
    }

    private string GetStatusMessageAfterToolLoop(string content)
    {
        if (content.Contains("当前任务尚未完成", StringComparison.OrdinalIgnoreCase))
            return "任务未完成，等待继续…";

        return "就绪";
    }

    private void TryFinalizeTodoAfterAssistantReply(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        RunOnUiThread(() =>
        {
            if (TodoItems.Count == 0 || TodoItems.Any(x => x.Status == "blocked"))
                return;

            var hasFinalAnswer = content.Contains("总结", StringComparison.OrdinalIgnoreCase)
                || content.Contains("重要", StringComparison.OrdinalIgnoreCase)
                || content.Contains("已完成", StringComparison.OrdinalIgnoreCase)
                || content.Contains("以下", StringComparison.OrdinalIgnoreCase)
                || content.Contains("found", StringComparison.OrdinalIgnoreCase)
                || content.Contains("important", StringComparison.OrdinalIgnoreCase);
            if (!hasFinalAnswer)
                return;

            var changed = false;
            foreach (var item in TodoItems.Where(x => x.Status != "completed").ToList())
            {
                item.Status = "completed";
                if (string.IsNullOrWhiteSpace(item.Notes))
                    item.Notes = "AI 已返回最终结果。";
                changed = true;
            }

            if (!changed) return;
            _contextBuilder.RuntimeActiveSubtaskId = null;
            OnPropertyChanged(nameof(TodoItems));
            Logger.Info("最终回复后自动将未完成任务清单标记为 completed");
        });
    }

    private static List<AiTodoItem> GetTodoItems(Dictionary<string, object?> args)
    {
        var items = GetArg<List<AiTodoItem>>(args, "items");
        if (items == null || items.Count == 0) return new();

        var validItems = new List<AiTodoItem>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Title))
                continue;

            item.Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N")[..8] : item.Id.Trim();
            item.Title = item.Title.Trim();
            item.Status = NormalizeTodoStatus(item.Status);
            validItems.Add(item);
        }

        return validItems;
    }

    private void UpdateTodoItem(string id, string title, string status, string? notes)
    {
        RunOnUiThread(() =>
        {
            var item = TodoItems.FirstOrDefault(x => x.Id == id);
            if (item == null)
            {
                item = new AiTodoItem { Id = id, Title = title };
                TodoItems.Add(item);
            }

            item.Title = title;
            item.Status = NormalizeTodoStatus(status);
            item.Notes = notes;
            OnPropertyChanged(nameof(TodoItems));
            StatusMessage = $"🧭 {title}：{item.StatusLabel}";
        });
    }

    private AiTodoItem? TryStartNextPendingTodo(string completedId, string? nextStep)
    {
        return RunOnUiThread(() =>
        {
            var completedIndex = TodoItems
                .Select((item, index) => new { item, index })
                .FirstOrDefault(x => string.Equals(x.item.Id, completedId, StringComparison.OrdinalIgnoreCase))
                ?.index ?? -1;

            if (completedIndex < 0)
                return null;

            var next = TodoItems
                .Skip(completedIndex + 1)
                .FirstOrDefault(x => x.Status == "pending");
            if (next == null)
                return null;

            var plan = string.IsNullOrWhiteSpace(nextStep) ? "继续执行下一子任务" : nextStep.Trim();
            next.Status = "in_progress";
            next.Notes = plan;
            OnPropertyChanged(nameof(TodoItems));
            StatusMessage = $"🧭 {next.Title}：{next.StatusLabel}";
            Logger.Info($"finish_subtask: 自动开始下一子任务 {next.Id} — {next.Title}");
            return new AiTodoItem
            {
                Id = next.Id,
                Title = next.Title,
                Status = next.Status,
                Notes = next.Notes
            };
        });
    }

    private static string NormalizeTodoStatus(string? status) => status switch
    {
        "pending" or "in_progress" or "completed" or "blocked" => status,
        "doing" or "active" or "running" => "in_progress",
        "done" or "success" or "finished" => "completed",
        "failed" or "error" => "blocked",
        _ => "pending"
    };

    private void RunOnUiThread(Action action)
    {
        if (_uiDispatcher.CheckAccess())
            action();
        else
            _uiDispatcher.Invoke(action);
    }

    private T RunOnUiThread<T>(Func<T> action)
        => _uiDispatcher.CheckAccess() ? action() : _uiDispatcher.Invoke(action);

    // ====== 辅助 ======

    private void AddSystemMessage(string text)
    {
        Messages.Add(new ChatMessage
        {
            Role = MessageRole.System,
            Content = text,
            Timestamp = DateTime.Now
        });
    }

    public void UpdateTokenEstimate()
    {
        var total = Messages.Sum(m => m.Content.Length / 2);
        TokenEstimate = total;
    }

    /// <summary>通知 UI 某条消息内容已变化（流式更新）</summary>
    public void NotifyMessageChanged()
    {
        OnPropertyChanged(nameof(Messages));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>AI 调用 ask_user 时生成的问题信息</summary>
public class UserQuestionInfo
{
    public string QuestionId { get; set; } = "";
    public string Question { get; set; } = "";
    public string QuestionType { get; set; } = "confirmation"; // confirmation | multiple_choice | open_ended
    public string[]? Options { get; set; }
    public string? ContextSummary { get; set; }
    public string? DefaultOption { get; set; }
}
