# 函数帮助文档

## MainWindow

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `OnToggleAiPanel()` | 无 | void | 切换AI助手副窗口的显示/隐藏 |
| `OnSecondaryWindowClosing()` | sender, CancelEventArgs | void | 阻止副窗口关闭（仅隐藏），保存位置 |
| `PositionSecondaryWindow()` | 无 | void | 重新定位副窗口到主窗口右侧 |

## ChatViewModel

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `SendAsync()` | 无 | void | 使用当前输入框文本构造请求，调用AiClient发起流式请求 |
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
| `TestConnectionAsync(CancellationToken)` | 取消令牌 | Task<bool> | 发送测试请求检查API连接是否正常 |

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

### MarkdownToFlowDocumentConverter

| 函数名 | 输入 | 输出 | 作用 |
|--------|------|------|------|
| `Convert(value, targetType, parameter, culture)` | string markdown | FlowDocument? | 将 Markdown 文本转换为 WPF FlowDocument，支持标题/粗体/斜体/代码块/列表/表格/链接 |
| `ConvertBack(...)` | - | NotImplemented | 不支持反向转换 |

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
| `ExecuteAiToolAsync("start_subtask", args)` | id/title/plan | string | 将对应 todo 标为进行中；若子任务未在完整清单中预登记则记录警告；返回压缩上下文内部标记 |
| `ExecuteAiToolAsync("finish_subtask", args)` | id/status/summary/next_step | string | 将对应 todo 标为 completed 或 blocked，并返回用户可读的完成/受阻说明 |
| `NewConversation()` / `ClearConversation()` / `LoadConversation()` | 可选会话 id | void | 取消旧请求并清理 ask_user 挂起状态，避免上一次使用后残留等待状态导致无响应 |

### AiChatPanel

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `AttachViewModel(ChatViewModel?)` | ViewModel | void | 绑定 PropertyChanged、Messages.CollectionChanged、ChatMessage.Content 变化事件 |
| `DetachViewModel()` | 无 | void | 面板卸载时解除事件订阅，避免副窗口反复显示后重复订阅导致 UI 压力 |
| `OnMessagePropertyChanged()` | ChatMessage.Content 变化 | void | 流式输出期间触发自动滚动，保持最新 AI 输出可见 |
| `ScrollToBottom()` | 无 | void | 使用 `DispatcherPriority.ContextIdle` 滚动到底部，避免抢占 UI 渲染 |

### MarkdownToFlowDocumentConverter

| 函数/成员 | 输入 | 输出 | 作用 |
|-----------|------|------|------|
| `Convert()` | markdown string | FlowDocument? | 当消息超过 12000 字符时仅渲染末尾内容，降低长回复流式 Markdown 转换造成的 UI 卡顿 |
