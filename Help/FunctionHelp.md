# 函数帮助文档

## MainWindow

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `OnToggleAiPanel()` | 无 | void | 切换AI助手副窗口的显示/隐藏 |
| `OnSecondaryWindowClosing()` | sender, CancelEventArgs | void | 阻止副窗口关闭（仅隐藏），保存位置 |
| `PositionSecondaryWindow()` | 无 | void | 重新定位副窗口到主窗口右侧 |
| `EnsureAddedTabWebViewAsync(TabInfo tab)` | 新加入的标签模型 | Task | 使用形参 tab 作为新标签 WebView2 绑定目标，内部创建缺失的 WebView2 并在该标签仍为活动标签时激活它，返回 Task 表示异步绑定完成 |
| `EnsureTabWebViewAsync(TabInfo tab)` | 标签模型 | Task | 使用形参 tab 作为 WebView2 绑定目标，内部复用 TabInfo.Id 创建 BrowserHostService 标签并绑定 BrowserAutomationService，返回 Task 表示初始化完成 |
| `OnTabClosed(Guid id)` | 标签 Id | void | 使用形参 id 作为待关闭标签，内部异步释放 WebView2 并捕获记录关闭异常，返回 void 表示事件处理完成 |

## App

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)` | WPF 未处理异常事件参数 | void | 使用形参 e 作为 UI 线程异常信息，内部写入日志并标记已处理，返回 void 表示异常已被应用层兜底处理 |
| `OnUnhandledException(object sender, UnhandledExceptionEventArgs e)` | AppDomain 未处理异常事件参数 | void | 使用形参 e 作为进程级异常信息，内部写入异常类型、消息与堆栈，返回 void 表示日志记录完成 |
| `OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)` | 未观察 Task 异常事件参数 | void | 使用形参 e 作为后台 Task 异常信息，内部写入日志并 SetObserved，返回 void 表示异常已记录并观察 |

## ChatViewModel

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `SendAsync()` | 无 | void | 使用当前输入框文本构造请求，调用AiClient发起流式请求；工具循环正常结束且已有最终回复时会兜底同步右侧任务清单完成状态 |
| `LoadConversation(object? id)` | string id | void | 加载指定ID的历史对话到消息列表 |
| `NewConversation()` | 无 | void | 清空消息，创建新对话ID |
| `DeleteConversation(object? id)` | string id | void | 删除指定ID的历史对话存档 |
| `TogglePanelCommand` | 无 | ICommand | 切换AI面板可见性的命令 |

## ChatViewModel — 日志增强

| 函数名 | 变化 | 说明 |
|--------|------|------|
| `SendAsync()` | 日志增强 | 用户输入完整输出到 Info 日志；流式每块 chunk 输出到 Debug 日志（`AI回复流 chunk#N: +M字符 → "内容"`）；每 500ms 输出一次 Info 累计日志（`AI回复累计 (N字符) +"新增内容"` + `内容: 完整累计文本`）；结束时补最终内容输出 |

## AiClient

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `StreamMessageAsync(IEnumerable<ChatMessage>, CancellationToken)` | 消息列表、取消令牌 | IAsyncEnumerable<string> | 使用流式API向AI发送消息，逐块返回响应内容 |
| `SendMessageAsync(IEnumerable<ChatMessage>, CancellationToken)` | 消息列表、取消令牌 | Task<string> | 使用非流式API发送消息，等待完整响应 |
| `TestConnectionAsync(CancellationToken)` | 取消令牌 | Task<bool> | 发送测试请求检查API连接是否正常，失败时记录服务商返回的错误摘要 |
| `NormalizeSettingsProtocol(AiSettings)` | AI 设置 | AiSettings | 检测并修正火山方舟 Coding Plan endpoint 缺少 `/v3`、provider=anthropic 但 endpoint 是 OpenAI 兼容地址等错配配置，避免误用 x-api-key/Anthropic 请求格式或请求到 404 地址 |
| `NormalizeArkCodingEndpoint(AiSettings)` | AI 设置 | void | 将 `https://ark.cn-beijing.volces.com/api/coding` 自动修正为 `https://ark.cn-beijing.volces.com/api/coding/v3`，并修正缺失 `/v3` 的 chat/completions 地址 |
| `IsAnthropicProvider()` | 无 | bool | 同时检查 ProviderKey 与 endpoint 形态，只有真正 Anthropic Messages 端点才走 Anthropic 原生协议 |
| `GetOpenAIChatCompletionsEndpoint()` | 无 | string | 将 OpenAI 兼容 Base URL 规范化为 `/chat/completions` 请求地址 |
| `BuildPlanningToolReminder(string)` | 工具名 | string | 生成兼容模式下的系统提醒，要求模型当前轮先调用指定规划工具 |
| `SupportsForcedToolChoice(ProviderInfo?)` | 服务商信息 | bool | 判断当前 OpenAI 兼容服务商/模型是否适合发送强制 tool_choice；DeepSeek、火山方舟和 reasoning/thinking 模型不兼容时省略该字段，改由系统提示推动规划工具 |
| `ResolveRequiredPlanningTool(List<ChatMessage>)` | 对话消息列表 | string? | 检查当前工具循环是否尚未建立任务清单或尚未开始第一个子任务，返回必须调用的规划工具名 |
| `ShouldContinueOpenSubtask(List<ChatMessage>, string, out string)` | 对话消息列表、AI 本轮文本、提醒输出 | bool | 检查当前是否存在已 start_subtask 但尚未 finish_subtask 的开放子任务；若 AI 只输出阶段性文本则返回 true 并生成系统提醒继续执行，防止未完成任务被当作最终完成 |
| `LooksLikeExplicitFinalOrBlockedReport(string)` | AI 本轮文本 | bool | 判断 AI 文本是否明确表示全部完成、无法继续或需要用户手动处理，用于开放子任务门禁的例外放行 |
| `BuildOpenSubtaskProgressText(string)` | AI 阶段性文本 | string | 将阶段性文本包装为”当前任务尚未完成”的用户可见提示，避免 UI 显示就绪时误判任务已完成 |
| `StreamRichEventsAsync(IEnumerable<ChatMessage>, CancellationToken)` | 消息列表、取消令牌 | IAsyncEnumerable<AiStreamEvent> | 构建并发送 API 请求（OpenAI 兼容或 Anthropic 原生），支持 429/TooManyRequests 指数退避重试（最多 3 次，基础 2s）和 90s HTTP 超时保护；遇到 ContextWindowExceeded 时停止继续请求，避免把任务成功后的上下文上限问题追加为最终 API 失败 |
| `IsContextWindowExceeded(string)` | API 错误正文 | bool | 判断错误是否为上下文窗口超限，用于将模型上下文溢出视为工具循环停止信号而非用户可见 API 请求失败 |

## AiClient — 速率限制配置

| 常量 | 值 | 说明 |
|------|------|------|
| `MaxRateLimitRetries` | 3 | 429/TooManyRequests 最大重试次数 |
| `RateLimitBaseDelayMs` | 2000 | 指数退避基础延迟（2s, 4s, 8s） |

## AiSettingsStore

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `NormalizeProviderProtocols()` | 无 | void | 遍历多模型配置并修正 provider/endpoint 协议错配，防止 OpenAI 兼容端点被当作 Anthropic 原生端点请求，或火山方舟 endpoint 缺少 `/v3` |
| `NormalizeProviderProtocol(AiSettings)` | AI 设置 | void | 将 anthropic + OpenAI 兼容 endpoint 自动改写为 `volcengine-ark` 或 `custom`，并调用火山 endpoint 规范化 |
| `NormalizeArkCodingEndpoint(AiSettings)` | AI 设置 | void | 持久化修正火山方舟 Coding Plan endpoint：`/api/coding` → `/api/coding/v3` |
| `LooksLikeOpenAICompatibleEndpoint(string)` | endpoint | bool | 识别 `/chat/completions`、`/compatible-mode/`、`/openai/`、火山方舟等 OpenAI 兼容端点 |

## ConversationService

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `ListConversations()` | 无 | List<ConversationSummary> | 扫描对话存储目录，返回所有对话摘要 |
| `SaveConversation(string id, List<ChatMessage>)` | 对话ID、消息列表 | void | 将对话消息序列化保存为JSON文件 |
| `LoadConversation(string id)` | 对话ID | List<ChatMessage>? | 从JSON文件反序列化加载对话消息 |
| `DeleteConversation(string id)` | 对话ID | void | 删除对话的JSON存档文件 |

## AI 能力体系（AI Skills System） — DESIGN.md 第6章实现

### SkillRegistry

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `RegisterBasic(BasicSkillDefinition skill)` | 基础技能定义 | void | 注册一个基础技能到技能中心 |
| `RegisterComposite(CompositeSkillDefinition skill)` | 组合技能定义 | void | 注册一个组合技能到技能中心 |
| `RegisterStrategy(StrategySkillDefinition skill, IStrategyHandler? handler)` | 策略技能定义、处理器 | void | 注册一个策略技能及其决策处理器 |
| `RegisterAll(IEnumerable<SkillDefinition> skills)` | 技能定义集合 | void | 批量注册多个技能 |
| `GetSkill(string skillId)` | 技能ID | SkillDefinition? | 按ID查询任意技能 |
| `GetBasic(string skillId)` | 技能ID | BasicSkillDefinition? | 按ID查询基础技能 |
| `GetComposite(string skillId)` | 技能ID | CompositeSkillDefinition? | 按ID查询组合技能 |
| `GetStrategy(string strategyId)` | 策略ID | StrategySkillDefinition? | 按ID查询策略技能 |
| `GetAllSkills()` | 无 | IReadOnlyList | 获取所有已注册技能 |
| `RecommendForIntent(string userMessage)` | 用户输入 | IReadOnlyList | 根据用户输入推荐最匹配的技能列表 |
| `Validate(out List<string> errors)` | 错误列表输出 | bool | 验证所有技能引用的完整性 |
| `SetSkillEnabled(string skillId, bool enabled)` | 技能ID、启用状态 | void | 启用/禁用一个技能 |
| `GetStrategyHandler(string strategyId)` | 策略ID | IStrategyHandler? | 获取策略处理器 |
| `Clear()` | 无 | void | 清空所有注册 |

### SkillExecutor

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `RegisterBasicExecutor(string skillId, Func<...> executor)` | 技能ID、执行器函数 | void | 注册基础技能的实际执行器（映射到IAutomationBridge） |
| `ExecuteAsync(string skillId, params?, CancellationToken)` | 技能ID、参数、取消令牌 | Task<SkillExecutionResult> | 执行指定技能，自动处理步骤编排、降级和超时 |
| `GetStats()` | 无 | Dictionary | 获取执行引擎统计信息 |

### 策略处理器

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `NavigationStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 导航策略：当目标信息不在当前页时决策搜索/导航/等待 |
| `LocateStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 定位策略：CSS→XPath→文本→坐标→视觉 降级链 |
| `RetryStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 重试策略：失败类型分析→自适应恢复→最多3次 |
| `ContextStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 上下文策略：Token使用率>75%→裁剪、>90%→紧急裁剪 |
| `RecoveryStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 恢复策略：严重错误→中止、API错误→询问用户 |
| `PrivacyStrategy.DecideAsync(StrategyContext, CancellationToken)` | 上下文、取消令牌 | StrategyDecision | 隐私策略：敏感URL/字段检测→限制操作→清除痕迹 |

### DefaultSkillDataProvider

| 属性/函数 | 值 | 作用 |
|-----------|-----|------|
| `GetAllBasicSkills()` | 13个基础技能 | 提供DESIGN.md 6.1节定义的全部基础技能 |
| `GetAllCompositeSkills()` | 9个组合技能 | 提供DESIGN.md 6.2节定义的全部组合技能 |
| `GetAllStrategySkills()` | 6个策略技能 | 提供DESIGN.md 6.3节定义的全部策略技能 |
| `GetAllSkills()` | 28个技能 | 获取所有内置技能 |

### SkillSystemIntegration

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Initialize()` | 无 | void | 一键初始化技能系统：注册所有技能、关联处理器、验证引用 |
| `RecommendSkills(string userMessage)` | 用户输入 | List<SkillDefinition> | 根据用户意图推荐技能 |
| `GetStatusSummary()` | 无 | string | 获取技能系统状态摘要 |
| `GetAllSkillsFormatted()` | 无 | string | 获取所有技能的格式化信息 |

## AI 输出优化（2026-06-05）

### IAiClient / AiClient

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `OnToolCallStatus` | string toolName, string status("calling"&#124;"result"&#124;"error"), string? summary | event | 工具调用状态事件，用于 UI 显示一行简短描述而不污染 AI 回复流 |

### WebView2AutomationBridge

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `RegisterAll(SkillExecutor)` | SkillExecutor | void | 向执行引擎注册 14 个基础技能的 WebView2 真实浏览器操作执行器 |
| `ExecuteNavigate(params, ct)` | action="navigate"&#124;"go_back"&#124;"go_forward"&#124;"refresh"&#124;"stop" | Task<SkillExecutionResult> | 使用 CoreWebView2.Navigate()/GoBack()/GoForward()/Reload() 导航 |
| `ExecuteClick(params, ct)` | action="click_element"(selector)&#124;"click_element_at"(x,y) | Task<SkillExecutionResult> | 使用 JS querySelector + click() / elementFromPoint() 点击元素 |
| `ExecuteType(params, ct)` | action="type_text"(selector,text)&#124;"key_press"(key)&#124;"select_all"&#124;"copy"&#124;"paste" | Task<SkillExecutionResult> | 使用 JS 聚焦元素并设置 value + dispatchEvent(input/change/keyboard) |
| `ExecuteSelect(params, ct)` | action="select_option"(selector,value&#124;text)&#124;"check_element"(selector)&#124;"file_input" | Task<SkillExecutionResult> | 使用 JS 选择下拉框/value 设置 + change 事件 |
| `ExecuteScroll(params, ct)` | action="scroll_to"(selector&#124;x,y)&#124;"scroll_by"(delta_x,delta_y) | Task<SkillExecutionResult> | 使用 JS window.scrollTo()/scrollBy() |
| `ExecuteExtract(params, ct)` | action="get_page_text"&#124;"get_page_html"&#124;"get_page_title"&#124;"get_element_text"(selector)&#124;"get_attribute"(selector,attribute) | Task<SkillExecutionResult> | 使用 JS document.body.innerText/outerHTML 提取页面内容 |
| `ExecuteScreenshot(params, ct)` | action="take_screenshot" | Task<SkillExecutionResult> | 使用 CDP Page.captureScreenshot 截取浏览器视口 |
| `ExecuteWait(params, ct)` | action="wait_for_navigation"&#124;"wait"(delay_ms)&#124;"wait_for_element"&#124;"wait_for_text" | Task<SkillExecutionResult> | 轮询 document.readyState 等待页面加载完成 |
| `ExecuteTab(params, ct)` | action="create_tab"(url)&#124;"close_tab"(tab_id)&#124;"activate_tab"(tab_id&#124;index)&#124;"get_tabs" | Task<SkillExecutionResult> | 调用 BrowserViewModel 的标签操作方法 |
| `ExecuteCookie(params, ct)` | action="get_cookies"&#124;"set_cookie"(name,value)&#124;"delete_cookie"(name) | Task<SkillExecutionResult> | 使用 CoreWebView2.CookieManager 管理 Cookie |
| `ExecuteForm(params, ct)` | action="fill_form"(fields)&#124;"drag_and_drop"(source,target) | Task<SkillExecutionResult> | 使用 JS 多字段填充 / DragEvent 模拟拖放 |
| `ExecuteHover(params, ct)` | action="hover_element"(selector)&#124;"focus_element"(selector) | Task<SkillExecutionResult> | 使用 JS dispatchEvent(MouseEvent) 模拟悬停 |
| `ExecuteQuery(params, ct)` | action="query_selector"(selector)&#124;"get_page_links"&#124;"get_form_fields"&#124;"get_page_structure" | Task<SkillExecutionResult> | 使用 JS querySelectorAll / 页面结构遍历 |
| `ExecuteJs(params, ct)` | action="execute_javascript"(code/script) | Task<SkillExecutionResult> | 使用 CoreWebView2.ExecuteScriptAsync 执行任意 JS |

### AssistantResponseParser / AssistantResponseSections

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `AssistantResponseParser.Parse(string)` | Assistant 原始 Content | AssistantResponseSections | 将 AI 可见输出按显式标记或保守启发式拆成 `Thinking` 与 `Conclusion`，不改变原始消息内容 |
| `ParseExplicitSections(string)` | Content | AssistantResponseSections? | 优先识别 `[思考过程]`、`[结论]`、`<think>...</think>` 等显式分区标记 |
| `FindHeuristicConclusionStart(string)` | Content | int | 在非代码块区域查找「通过...来看 / 准确说 / 所以 / 答案是」等结论起点，支持同一行内切分 |
| `IsThinkingLine(string)` | 单行文本 | bool | 识别 `The user wants`、`Let me`、`上一步评估：`、`记忆：`、`下一目标：` 等过程性语句 |
| `AssistantResponseSections` | Thinking/Conclusion | record struct | 保存 UI 分区结果，并提供 `HasThinking` / `HasConclusion` 判断 |

### ChatMessage — AI 输出分区派生属性

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `ThinkingContent` | 无 | string | UI-only 派生属性，从 Assistant 的 `Content` 中提取思考过程，用于折叠区显示 |
| `ConclusionContent` | 无 | string | UI-only 派生属性，从 Assistant 的 `Content` 中提取最终结论；无可拆分思考时等于原始内容 |
| `HasThinkingContent` | 无 | bool | 控制 `[思考过程]` 折叠区是否显示 |
| `IsAssistant` / `IsNotAssistant` | 无 | bool | 供 XAML 区分 Assistant 分区模板和普通消息模板 |
| `NotifyContentChanged()` | 无 | void | 触发 `Content` 与思考/结论派生属性的 PropertyChanged，保证流式输出刷新两个板块 |

### MarkdownToFlowDocumentConverter

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Convert(value, targetType, parameter, culture)` | string markdown | FlowDocument? | 将 Markdown 文本转换为 WPF FlowDocument，支持标题/粗体/斜体/代码块/列表/表格/链接 |
| `ConvertBack(...)` | - | NotImplemented | 不支持反向转换 |

## 浏览器宿主服务（BrowserHostService）

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `InitializeAsync()` | 无 | Task | 创建共享 CoreWebView2Environment，配置浏览器启动参数（禁用 SmartScreen、忽略证书错误），返回 Task 表示环境初始化完成 |
| `CreateTabAsync(string url)` | 目标 URL | Task\<TabInfo\> | 使用形参 url 作为新标签导航地址，内部实例化 WebView2 → 加入容器 → 初始化 CoreWebView2 → 绑定事件 → 导航，返回 TabInfo 表示新标签信息 |
| `CreateTabForAsync(TabInfo tab, string url)` | 已有 TabInfo、目标 URL | Task\<TabInfo\> | 使用形参 tab 作为已有标签模型（保留其 Id），内部复用 Id 创建关联的 WebView2 并在导航完成后返回，返回 TabInfo 表示初始化完成的标签 |
| `ActivateTab(Guid tabId)` | 标签 Id | void | 使用形参 tabId 作为目标激活标签，内部遍历所有 WebView2 控件切换 Visibility（目标 Visible，其余 Collapsed），返回 void 表示切换完成 |
| `CloseTabAsync(Guid tabId)` | 标签 Id | Task | 使用形参 tabId 作为待关闭标签，内部移除事件订阅、从容器移除、释放 WebView2 资源并触发 TabClosed 事件，返回 Task 表示异步清理完成 |
| `GetWebViewForTab(Guid tabId)` | 标签 Id | WebView2? | 使用形参 tabId 作为查找键，内部从字典返回对应 WebView2 控件（不存在返回 null），返回 WebView2 可引用或 null |
| `Dispose()` | 无 | void | 遍历释放所有 WebView2 实例、清空字典、重置环境引用，返回 void 表示全部资源已释放 |

| 事件 | 参数 | 作用 |
|------|------|------|
| `TabClosed` | Guid tabId | 标签关闭后触发（已从字典移除、WebView2 已释放） |
| `NavigationStarting` | Guid tabId, string url | 导航开始时触发，携带标签 Id 与目标 URL |
| `NavigationCompleted` | Guid tabId, NavigationResultInfo | 导航完成时触发，携带标签 Id 与成功状态/HTTP 状态码/错误信息 |
| `TitleChanged` | Guid tabId, string title | 文档标题变更时触发 |
| `UrlChanged` | Guid tabId, string url | URL 变更时触发（含 SPA history.pushState） |
| `LoadingStateChanged` | Guid tabId, bool isLoading | 加载状态切换时触发（true=加载中，false=已完成或失败） |
| `WebViewCrashed` | Guid tabId | 渲染进程崩溃时触发 |
| `NewTabRequested` | string url | 新窗口请求转化时触发（返回的标签 Id 由调用方决定） |

| 属性 | 类型 | 作用 |
|------|------|------|
| `ActiveWebView` | WebView2? | 当前活跃 WebView2（无活跃标签时为 null） |
| `ActiveTabId` | Guid? | 当前活跃标签 Id |
| `TabCount` | int | 当前标签数量 |
| `IsInitialized` | bool | 是否已完成 Environment 初始化 |
| `AutoDismissDialogs` | bool | 是否自动确认页面 alert/confirm/prompt 弹窗（默认 true） |
| `UserDataFolder` | string? | 用户数据目录（Cookie/缓存/LocalStorage 等） |

## 浏览器自动化服务（BrowserAutomationService）

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Initialize(Dispatcher dispatcher)` | WPF Dispatcher | void | 使用形参 dispatcher 作为 UI 线程调度器，内部绑定到服务以支持后台线程切换 UI 执行 WebView2 操作，返回 void 表示初始化完成 |
| `BindWebView(Guid tabId, WebView2 webView)` | 标签 Id, WebView2 控件 | void | 使用形参 tabId 和 webView 建立绑定关系，内部注册到字典并自动设为活跃标签（若首个），返回 void 表示绑定完成 |
| `UnbindWebView(Guid tabId)` | 标签 Id | void | 使用形参 tabId 从字典移除 WebView2 并清空活跃标签与 URL，返回 void 表示解绑完成 |
| `SwitchToTab(Guid tabId)` | 标签 Id | void | 使用形参 tabId 作为目标标签，内部验证绑定状态后设置活跃标签并同步当前 URL，返回 void 表示切换完成 |
| `NavigateAsync(string url, int timeoutMs)` | 目标 URL, 超时毫秒 | Task\<AutomationResult\> | 使用形参 url 作为导航目标，内部订阅 NavigationCompleted 事件、执行导航、等待完成或超时后返回成功/失败结果与 URL，返回 AutomationResult 表示操作结果 |
| `GoBackAsync()` | 无 | Task\<AutomationResult\> | 调用 CoreWebView2.GoBack() 后退到历史上一页，返回结果与当前 URL |
| `GoForwardAsync()` | 无 | Task\<AutomationResult\> | 调用 CoreWebView2.GoForward() 前进到历史下一页，返回结果与当前 URL |
| `ReloadAsync()` | 无 | Task\<AutomationResult\> | 调用 CoreWebView2.Reload() 刷新当前页面，返回结果与当前 URL |
| `ClickAsync(int elementId)` | 元素整数 Id | Task\<AutomationResult\> | 使用形参 elementId 定位 data-bermain-id 标记的元素并执行 click，内部注入 JS 点击并返回结果 |
| `TypeAsync(int elementId, string text, bool clearFirst)` | 元素 Id, 输入文本, 是否先清空 | Task\<AutomationResult\> | 使用形参 elementId 定位输入框、形参 text 作为输入内容，内部使用 NativeInputValueSetter 绕过框架拦截、设置 value 并 dispatch input/change 事件，返回结果 |
| `HoverAsync(int elementId)` | 元素整数 Id | Task\<AutomationResult\> | 使用形参 elementId 定位元素并模拟 MouseEvent mouseover 悬停事件，返回结果 |
| `SelectOptionAsync(int elementId, string value)` | 下拉元素 Id, 选项值 | Task\<AutomationResult\> | 使用形参 elementId 定位 select 元素、形参 value 作为选项 value，内部设置 value + dispatch input/change 事件，返回结果 |
| `ScrollAsync(int deltaX, int deltaY)` | 横向像素, 纵向像素 | Task\<AutomationResult\> | 使用形参 deltaX/deltaY 调用 window.scrollBy() 滚动页面，返回结果 |
| `FillFormAsync(Dictionary\<string, string\> formData)` | 表单字段映射 | Task\<AutomationResult\> | 使用形参 formData 作为字段名→值的映射，内部对整数 key 按 data-bermain-id 匹配，文本 key 按 name/aria-label/placeholder 查找并填值，返回每项结果汇总 |
| `GetSnapshotAsync()` | 无 | Task\<AutomationResult\> | 注入 bermainA11y JavaScript 获取页面 A11y 快照（重新分配 data-bermain-id），返回 JSON 格式的结构化页面元素列表 |
| `TakeScreenshotAsync()` | 无 | Task\<AutomationResult\> | 调用 CoreWebView2.CapturePreviewAsync() 截取视口 PNG 并返回 base64，返回结果与当前 URL |
| `EvaluateJavaScriptAsync(string script)` | JavaScript 代码 | Task\<AutomationResult\> | 使用形参 script 通过 CoreWebView2.ExecuteScriptAsync() 执行，返回字符串结果 |
| `WaitAsync(int ms)` | 等待毫秒数 | Task\<AutomationResult\> | 使用形参 ms 执行 Task.Delay() 固定等待（上限 60000ms），返回结果 |
| `WaitForTextAsync(string text, int timeoutMs)` | 等待文本, 超时毫秒 | Task\<AutomationResult\> | 使用形参 text 注入 JS 轮询检测文本出现（100ms 间隔），超时返回失败，返回 JSON 结果 |
| `WaitForNavigationAsync(int timeoutMs)` | 超时毫秒 | Task\<AutomationResult\> | 订阅 NavigationCompleted 事件等待下一次导航完成，返回结果 |
| `PressKeyAsync(string key)` | 按键名称 | Task\<AutomationResult\> | 使用形参 key 通过 CDP Input.dispatchKeyEvent 模拟按键（rawKeyDown → keyUp），支持 Enter/Tab/Escape/Arrow* 等 16 种按键 |

| 事件 | 参数 | 作用 |
|------|------|------|
| `NavigationCompleted` | Guid, NavigationEventInfo | 导航完成事件（由 BrowserHostService 转发） |
| `TitleChanged` | Guid, string | 标题变更事件 |
| `UrlChanged` | Guid, string | URL 变更事件 |
| `LoadingStateChanged` | Guid, bool | 加载状态切换事件 |
| `WebViewCrashed` | Guid | WebView2 进程崩溃事件 |

| 属性 | 类型 | 作用 |
|------|------|------|
| `DefaultOperationTimeoutMs` | int | 默认操作超时（毫秒），默认 30000 |
| `CurrentUrl` | string? | 当前活跃标签 URL（内部维护，外部只读） |
| `IsReady` | bool | 是否已初始化且至少绑定了一个 WebView2 |
| `RegisteredToolNames` | IReadOnlySet\<string\> | 已注册的 AI 可见浏览器工具名集合（17 个） |
| `AutoDismissDialogs` | bool | 自动接受弹窗的语义标志 |

| 方法 | 输入 | 输出 | 作用 |
|------|------|------|------|
| `IsToolRegistered(string)` | 工具名 | bool | 判断指定工具名是否在已注册集合中（O(1) 查询） |
| `NotifyNavigationCompleted(Guid, NavigationEventInfo)` | 标签 Id, 导航信息 | void | 由 BrowserHostService 调用，更新内部 CurrentUrl 并外发 NavigationCompleted 事件 |
| `NotifyTitleChanged(Guid, string)` | 标签 Id, 标题 | void | 转发标题变更事件 |
| `NotifyUrlChanged(Guid, string)` | 标签 Id, URL | void | 更新 CurrentUrl 并转发 URL 变更事件 |
| `NotifyLoadingStateChanged(Guid, bool)` | 标签 Id, 加载状态 | void | 转发加载状态事件 |
| `NotifyWebViewCrashed(Guid)` | 标签 Id | void | 处理崩溃：清除活跃标签、移除 WebView、触发崩溃事件 |

## 浏览器工具路由器（BrowserAutomationToolRouter）

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `GetToolDefinitions()` | 无 | IReadOnlyList\<ToolDefinition\> | 返回 17 个 browser_* 工具的 AI 函数定义，包含名称、描述、参数 JSON Schema 和必需参数列表，供 ContextBuilder 注入到 AI 请求中 |
| `InvokeAsync(string toolName, Dictionary\<string, object?>? args)` | 工具名, 参数字典 | Task\<string\> | 使用形参 toolName 匹配对应的自动化操作方法，内部解析参数容错（支持 element_id/id/element 别名、JsonElement 自动转换），调用 BrowserAutomationService 执行后返回 JSON 格式结果 |
| `IsToolRegistered(string)` | 工具名 | bool | 委托给 BrowserAutomationService.IsToolRegistered 检查工具是否已注册 |

## 日志服务（Logger）

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `AllocConsole()` | 无 | void | 调用 Win32 AllocConsole API 分配后台控制台窗口，设置标题为 "SmartAI Browser — Debug Console"，输出启动横幅，返回 void 表示控制台已分配 |
| `Trace(string signature)` | 函数签名 | IDisposable | 使用形参 signature 作为函数标识记录 ENTER 日志，返回 IDisposable（TraceBlock），在 using Dispose 时自动记录 EXIT 日志并输出执行耗时 |
| `CleanOldLogs(TimeSpan maxAge)` | 最大保留天数 | void | 使用形参 maxAge 作为清理阈值，内部扫描 Log 目录删除超过该天数的 .log 文件，返回 void 表示清理完成 |
| `Debug(string)` | 消息文本 | void | 输出 Debug 级别日志（时间戳 + DBG + 消息）到控制台(灰色)、文件(追加)和内存缓存 |
| `Info(string)` | 消息文本 | void | 输出 Info 级别日志（时间戳 + INF + 消息）到控制台(白色)、文件和内存缓存 |
| `Warning(string)` | 消息文本 | void | 输出 Warning 级别日志（时间戳 + WRN + 消息）到控制台(黄色)、文件和内存缓存 |
| `Error(string)` | 消息文本 | void | 输出 Error 级别日志（时间戳 + ERR + 消息）到控制台(红色)、文件和内存缓存 |
| `Exception(string context, Exception ex)` | 上下文描述、异常对象 | void | 使用形参 context 作为错误上下文描述，内部输出错误类型+消息到 Error 级别、堆栈跟踪到 Debug 级别 |
| `GetBuffer()` | 无 | string[] | 返回内存日志缓存的快照副本 |

| 属性 | 类型 | 作用 |
|------|------|------|
| `MinimumLevel` | LogLevel | 当前日志级别阈值（低于此级别不输出），默认 Debug |
| `Revision` | int | 修改计数，当前值 5 |

## 数据持久化服务

### BookmarkService

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `LoadBookmarks()` | 无 | List\<BookmarkInfo\> | 从 %LocalAppData%/SmartAI-Browser-Demo/bookmarks.json 反序列化加载书签列表，过滤空 URL 项，返回书签集合 |
| `SaveBookmarks(IEnumerable\<BookmarkInfo\>)` | 书签集合 | bool | 将书签集合序列化为 JSON 写入 bookmarks.json 文件，成功返回 true，失败记录异常日志后返回 false |

### HistoryService

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `LoadHistory()` | 无 | List\<HistoryInfo\> | 从 history.json 反序列化加载历史记录，过滤空 URL、按 VisitedAt 倒序排序，返回历史集合 |
| `SaveHistory(IEnumerable\<HistoryInfo\>)` | 历史集合 | bool | 将历史集合序列化为 JSON 写入 history.json 文件，成功返回 true，失败记录异常后返回 false |

### DownloadManager

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Add(DownloadItem item)` | 下载项 | void | 使用形参 item 作为新下载记录，内部通过 Dispatcher 同步插入到 ObservableCollection 头部（新下载排在最前），返回 void |
| `Update(DownloadItem item, Action\<DownloadItem\> update)` | 下载项、更新操作 | void | 使用形参 update 作为更新回调，内部通过 Dispatcher 同步执行对 DownloadItem 的更新操作 |
| `ClearCompleted()` | 无 | void | 从 ObservableCollection 中移除所有非 InProgress 状态的下载项（已完成/已取消/失败），返回 void |

## 值转换器（Converters）

| 类名 | 转换方法 | 输入 | 输出 | 作用 |
|------|---------|------|------|------|
| `BoolToVisibilityConverter` | Convert | bool | Visibility | 使用形参 value 作为布尔值，true → Visible，false → Collapsed |
| `InverseBoolToVisibilityConverter` | Convert | bool | Visibility | 反转布尔值：true → Collapsed，false → Visible |
| `BoolToGridLengthConverter` | Convert | bool | GridLength | true → GridLength(1, Star)/"*"，false → GridLength(0) |
| `ActiveTabBgConverter` | Convert | bool | Brush | true → #2D2D32（激活标签背景），false → #252529（未激活） |
| `ActiveTabBorderConverter` | Convert | bool | Brush | true → #45454A（激活标签边框），false → #3A3A3E（未激活） |
| `ActiveTabTextConverter` | Convert | bool | Brush | true → #FFFFFF（激活标签文字白），false → #AAAAAA（未激活灰） |
| `MessageRoleBgConverter` | Convert | MessageRole | Brush | User → #1A5CB5（蓝），Assistant → #2C6E3C（绿），其他 → #6B4C3A（棕） |
| `MessageRoleBorderConverter` | Convert | MessageRole | Brush | User → #2A6CC5（亮蓝），Assistant → #3C7E4C（亮绿），其他 → #7B5C4A（亮棕） |

## 字符串扩展

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Truncate(this string s, int maxLen)` | 源字符串、最大长度 | string | 使用形参 maxLen 作为截断阈值，内部判断 s.Length <= maxLen ? s : s[..maxLen] + "…"，返回截断后的字符串 |

## 段落扩展

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Also<T>(this T obj, Action<T> action)` | 对象、内联操作 | T | 使用形参 action 作为内联操作，内部执行 action(obj) 后返回 obj，用于链式初始化 WPF 对象 |

## 模型类

### BookmarkInfo

| 属性 | 类型 | 作用 |
|------|------|------|
| `Id` | string | 书签唯一标识（Guid 的 N 格式） |
| `Title` | string | 书签标题 |
| `Url` | string | 书签 URL |
| `CreatedAt` | DateTime | 收藏创建时间 |

### HistoryInfo

| 属性 | 类型 | 作用 |
|------|------|------|
| `Id` | string | 历史记录唯一标识 |
| `Title` | string | 访问页面标题 |
| `Url` | string | 访问 URL |
| `VisitedAt` | DateTime | 访问时间 |

### DownloadItem

| 属性 | 类型 | 作用 |
|------|------|------|
| `Id` | Guid | 下载项唯一标识 |
| `FileName` | string | 下载文件名 |
| `Uri` | string | 下载源 URL |
| `ResultFilePath` | string | 结果文件路径 |
| `BytesReceived` | long | 已接收字节数 |
| `TotalBytesToReceive` | long? | 总字节数（可能未知） |
| `State` | DownloadItemState | 下载状态枚举（InProgress/Completed/Canceled/Failed） |
| `ProgressPercent` | int | 下载进度百分比（0-100，派生属性） |
| `SizeText` | string | 可读大小文本（如 "1.5 MB / 5.2 MB"，派生属性） |
| `StateText` | string | 状态中文文本（"下载中"/"已完成"/"已取消"/"失败"，派生属性） |
| `StartedAt` | DateTime | 开始下载时间 |

### AiTodoItem

| 属性 | 类型 | 作用 |
|------|------|------|
| `Id` | string | 子任务稳定 ID |
| `Title` | string | 子任务标题 |
| `Status` | string | 状态：pending/in_progress/completed/blocked |
| `Notes` | string? | 进展说明 |
| `StatusLabel` | string | 状态中文文本（派生属性："待办"/"进行中"/"已完成"/"受阻"） |

### AssistantResponseSections

| 属性 | 类型 | 作用 |
|------|------|------|
| `Thinking` | string | 思考过程文本 |
| `Conclusion` | string | 结论文本 |
| `HasThinking` | bool | 是否包含思考内容 |
| `HasConclusion` | bool | 是否包含结论内容 |

### AutomationResult

| 属性 | 类型 | 作用 |
|------|------|------|
| `IsSuccess` | bool | 操作是否成功 |
| `Data` | string? | 成功时的数据内容 |
| `ErrorMessage` | string? | 失败时的错误信息 |
| `ElapsedMs` | long | 操作耗时毫秒 |
| `CurrentUrl` | string? | 操作完成时当前页面 URL |
| `Success(...)` | 静态工厂 | 创建成功结果 |
| `Fail(...)` | 静态工厂 | 创建失败结果 |

### NavigationResultInfo

| 属性 | 类型 | 作用 |
|------|------|------|
| `Url` | string | 导航后的 URL |
| `IsSuccess` | bool | 是否成功完成 |
| `HttpStatusCode` | int | HTTP 状态码 |
| `WebErrorStatus` | string? | Web 错误状态描述 |

### NavigationEventInfo

| 属性 | 类型 | 作用 |
|------|------|------|
| `Url` | string | 导航 URL |
| `IsSuccess` | bool | 导航是否成功 |
| `HttpStatusCode` | int | HTTP 状态码 |
| `WebErrorStatus` | string? | Web 错误状态 |

### ConversationSummary

| 属性 | 类型 | 作用 |
|------|------|------|
| `Id` | string | 对话 ID（文件名） |
| `FilePath` | string | 完整文件路径 |
| `CreatedAt` | DateTime | 创建时间 |
| `MessageCount` | int | 消息数量 |
| `Preview` | string | 首条用户消息预览 |

## AI 交互式暂停/确认机制（2026-06-05 新增设计）

### IAiOrchestrator 接口变更

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `SendMessageAsync(string userMessage, CancellationToken ct)` | userMessage=用户自然语言, ct=取消令牌 | IAsyncEnumerable\<AiEvent\> | **行为变更**：当 AI 调用 ask_user 工具时，流在 UserQuestion + AwaitingUserInput 事件后暂停，等待 RespondToQuestionAsync() 回复后继续 |
| `RespondToQuestionAsync(string questionId, string userResponse, CancellationToken ct)` [新增] | questionId=问题标识, userResponse=用户回答, ct=取消令牌 | IAsyncEnumerable\<AiEvent\> | 使用用户回答作为 tool_result 返回给 AI，恢复被 ask_user 暂停的工具调用循环 |
| `IsAwaitingUserInput { get; }` [新增] | 无 | bool | 返回当前 Orchestrator 是否正在等待用户回答（ask_user 后暂停中） |
| `CurrentQuestion { get; }` [新增] | 无 | UserQuestionInfo? | 返回当前等待回答的问题详情，供 UI 渲染问题卡片，无等待时返回 null |

### AiEvent / AiEventType 变更

| 枚举值/字段 | 类型 | 作用 |
|-------------|------|------|
| `AiEventType.UserQuestion` [新增] | enum | AI 调用 ask_user 工具向用户提问时发出，携带 UserQuestionInfo |
| `AiEventType.AwaitingUserInput` [新增] | enum | 等待用户输入中——流在此暂停，UI 应显示输入控件 |
| `AiEvent.UserQuestion` [新增] | UserQuestionInfo? | 携带 ask_user 工具发起的问题详情 |

### UserQuestionInfo 记录 [新增]

| 字段名 | 类型 | 作用 |
|--------|------|------|
| `QuestionId` | string | 唯一问题标识，用于 RespondToQuestionAsync 回调 |
| `Question` | string | AI 提出的问题文本 |
| `QuestionType` | string | 问题模式："confirmation"（是/否）\| "multiple_choice"（多选）\| "open_ended"（自由回答） |
| `Options` | string[]? | 预设选项列表（multiple_choice 模式时使用） |
| `ContextSummary` | string? | 上下文摘要，帮助用户理解 AI 当前进度和决策背景 |
| `DefaultOption` | string? | 推荐的默认选项 |

### AiPanelViewModel 新增方法

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `SelectOptionAsync(string option)` [新增] | option=用户选择的预设选项文本 | Task | 将用户选择的选项作为回答提交给 Orchestrator，隐藏问题卡片，消费剩余 AI 事件流 |
| `SubmitCustomResponseAsync()` [新增] | 无（读取 CustomResponse 绑定属性） | Task | 将用户的自由文本回答提交给 Orchestrator，清空输入，隐藏卡片，消费剩余事件流 |
| `IsShowingQuestion { get; set; }` [新增] | bool | - | 绑定属性：是否在 UI 中显示问题卡片 |
| `CurrentQuestion { get; set; }` [新增] | UserQuestionInfo? | - | 绑定属性：当前等待回答的问题信息 |
| `CustomResponse { get; set; }` [新增] | string | - | 绑定属性：用户在 open_ended 模式下的文本输入 |

### ask_user Tool [新增 - 第43个工具]

| 字段 | 值 | 作用 |
|------|-----|------|
| `Name` | "ask_user" | 工具名称 |
| `Category` | "用户交互" | 新增工具类别 |
| `ToolType` | "interactive_pause" | 特殊类型：由 Orchestrator 直接拦截，不经过 ToolExecutor/IAutomationBridge |

### Orchestrator 内部关键变更

```
name : async Task AiOrchestrator::PauseAndWaitForUserAsync(ToolUseBlock askUserCall)
input : askUserCall (ask_user 工具调用块)
output : Task<string> (用户回答字符串)
effect : 使用形参 askUserCall 作为 AI 的提问，内部解析参数构造 UserQuestionInfo → 通过 AiEvent
         发出 → 创建 TaskCompletionSource 挂起 → 等待 RespondToQuestionAsync 通过
         _pauseSignal.SetResult(answer) 唤醒 → 返回用户的回答字符串。
```

## Bug 修复记录（2026-06-05）

### Bug 1：操作失败但报告"✅ 成功"

```
触发条件: ExecuteType / ExecuteClick / ExecuteHover 等 JS 注入方法返回 {"error":"元素未找到..."}
         时，代码未检查 error 字段直接调用 Success()。
修复: 在 WebView2AutomationBridge.cs 中新增 GetJsError() 方法，
     在所有 JS 执行后检查 result 是否包含 "error" 字段。
     涉及方法: ExecuteType, ExecuteClick, ExecuteSelect, ExecuteHover, ExecuteForm
```

### Bug 3：skill_navigate 不报告导航失败

```
触发条件: Navigate(url) 后只等待 500ms 就返回 Success()，不检查页面是否加载成功。
修复: 改为轮询 document.readyState 直到 'complete' 或超时(15s)，
     然后检查页面标题和内容是否包含错误关键词（404, error, 无法访问等），
     以及检测是否被重定向到登录页。
```

### Bug 4：PositionNextToMainWindow CPU 风暴

```
触发条件: 窗口移动时 LocationChanged/SizeChanged 事件密集触发(60+次/240ms)，
         每次直接调用 PositionNextToMainWindow()。
修复: 新增 DebouncePositionWindow() 方法，100ms 防抖窗口。
     使用 CancellationTokenSource 进行任务取消。
```

### Bug 5：ChatViewModel 上下文在标签切换后未更新

```
触发条件: 切换到已有 WebView 的标签时，OnTabActivated 未同步 URL/Title 到 ChatViewModel。
修复: OnTabActivated 中切换 WebView 后，立即从 wv.CoreWebView2.Source/DocumentTitle
     同步到 _vm.Chat.CurrentPageUrl/CurrentPageTitle。
     如果 WebView 尚未创建则从 TabInfo.Url/Title 同步。
```

### Bug 6：ask_user 暂停机制（新功能实现）

```
实现内容:
  1. 注册 ask_user Tool 定义（43个工具之一）
  2. ChatViewModel.ExecuteAiToolAsync 检测 ask_user → 返回 __ASK_USER_PAUSED__ 标记
  3. AiClient.ExecuteConversationAsync 检测标记 → yield 给调用方 → yield break
  4. ChatViewModel.SendAsync/ContinueToolLoopAsync 检测标记 → 设置 PendingAskUserQuestion
  5. ChatViewModel.RespondToQuestionAsync 添加 tool_result → 继续工具循环
  6. AiChatPanel.xaml 问题卡片 UI（confirmation/multiple_choice 模式）
```

## AI 任务清单与输出稳定性（2026-06-07）

### ChatViewModel

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `RegisterUpdateTodoTool()` | 无 | void | 注册 `update_todo`，要求 AI 在任务拆分阶段一次性写入完整子任务清单，后续仅更新既有项状态 |
| `RegisterSubtaskTools()` | 无 | void | 注册 `start_subtask` / `finish_subtask`，用于子任务边界状态更新和上下文压缩触发 |
| `ExecuteAiToolAsync("update_todo", args)` | items/summary | string | 将 AI 一次性拆分出的完整子任务清单写入右侧 TodoItems；拒绝空 items，避免清空任务清单 |
| `ExecuteAiToolAsync("start_subtask", args)` | id/title/plan | string | 将对应 todo 标为进行中；若子任务未在完整清单中预登记则返回错误，要求先调用 update_todo；返回压缩上下文内部标记 |
| `ExecuteAiToolAsync("finish_subtask", args)` | id/status/summary/next_step | string | 将对应 todo 标为 completed 或 blocked，并返回用户可读的完成/受阻说明 |
| `NewConversation()` / `ClearConversation()` / `LoadConversation()` | 可选会话 id | void | 取消旧请求并清理 ask_user 挂起状态，避免上一次使用后残留等待状态导致无响应 |

### AiModelSelectionDialog

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `LoadRows()` | 无 | void | 从 ai_settings.json 加载模型配置，加载时通过 AiSettingsStore 自动修正 provider/endpoint 协议错配，并记录加载日志 |
| `Save_Click(...)` | 按钮事件 | void | 保存多模型配置，重新解析活动配置后应用到 ChatViewModel，确保运行时使用修正后的 provider 协议 |

### AiChatPanel

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `AttachViewModel(ChatViewModel?)` | ViewModel | void | 绑定 PropertyChanged、Messages.CollectionChanged、ChatMessage.Content 变化事件 |
| `DetachViewModel()` | 无 | void | 面板卸载时解除事件订阅，避免副窗口反复显示后重复订阅导致 UI 压力 |
| `OnMessagePropertyChanged()` | ChatMessage.Content 变化 | void | 流式输出期间触发自动滚动，保持最新 AI 输出可见 |
| `AiChatPanel.xaml Assistant 模板` | ChatMessage | UI | 将 Assistant 原单一 Content 区拆为默认折叠的 `[思考过程]` Expander 与始终可见的 `[结论]` FlowDocument |
| `ScrollToBottom()` | 无 | void | 使用 `DispatcherPriority.ContextIdle` 滚动到底部，避免抢占 UI 渲染 |

### MarkdownToFlowDocumentConverter

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `Convert()` | markdown string | FlowDocument? | 当消息超过 12000 字符时仅渲染末尾内容，降低长回复流式 Markdown 转换造成的 UI 卡顿 |
