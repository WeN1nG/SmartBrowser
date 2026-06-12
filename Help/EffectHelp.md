# 功能实现模拟文档

## 功能 9：全部标签关闭后重新打开收藏页的 WebView2 生命周期修复 — 2026-06-11

```
异常复现路径:
用户关闭最后一个标签
  -> BrowserViewModel.CloseTab(id)
      -> Tabs.Remove(tab)
      -> ActiveTab = null
      -> TabClosed(id)
  -> MainWindow.OnTabClosed(id)
      -> BrowserHostService.CloseTabAsync(id)
      -> 移除并 Dispose WebView2
  -> 用户点击收藏
      -> BrowserViewModel.OpenBookmark(bookmark)
          -> ActiveTab == null
          -> AddNewTab(url)
              -> Tabs.Add(tab)
              -> ActiveTab = tab
问题: MainWindow 旧逻辑只在 ActiveTab 变化时尝试创建 WebView2；若集合新增和激活事件时序未覆盖，后续导航/自动化会面对没有 WebView2 的标签。

修复后路径:
用户点击收藏 / 新建标签
  -> BrowserViewModel.AddNewTab(url)
      -> Tabs.Add(tab)
  -> MainWindow.Tabs.CollectionChanged(Add)
      -> EnsureAddedTabWebViewAsync(tab)
          -> EnsureTabWebViewAsync(tab)
              -> BrowserHostService.CreateTabForAsync(tab, tab.Url)
              -> BrowserAutomationService.BindWebView(tab.Id, webView)
          -> 如果该 tab 仍是 ActiveTab
              -> ActivateTabWebView(tab.Id)
  -> finall(): 新标签始终创建并绑定 WebView2，关闭标签异常被 OnTabClosed 捕获写入日志。
```

## 功能 10：应用未处理异常日志兜底 — 2026-06-11

```
应用启动
  -> App.OnStartup()
      -> 注册 AppDomain.CurrentDomain.UnhandledException
      -> 注册 DispatcherUnhandledException
      -> 注册 TaskScheduler.UnobservedTaskException
      -> Logger.AllocConsole()

运行期异常
  |-- UI 线程异常
  |     -> OnDispatcherUnhandledException()
  |     -> Logger.Exception("UI 线程未处理异常", ex)
  |     -> e.Handled = true
  |-- 后台 Task 未观察异常
  |     -> OnUnobservedTaskException()
  |     -> Logger.Exception("未观察到的 Task 异常", aggregateException)
  |     -> e.SetObserved()
  |-- 进程级未处理异常
        -> OnUnhandledException()
        -> Logger.Exception("AppDomain 未处理异常", ex)

应用退出
  -> App.OnExit()
      -> 输出退出代码
      -> 解除异常事件订阅
      -> finall(): 下次异常退出前日志能保留异常类型、消息和堆栈。
```


## 功能 1：聊天记录点击跳转

```
btn:[聊天记录条目]
用户点击记录条目 -> InputBindings(MouseBinding) 
    问题：ListBox内部ListBoxItem拦截了MouseLeftButtonDown事件，导致InputBindings无法触发
    修复：ListBox -> ItemsControl (无选择行为，事件直达内部元素)
    替代方案：Button包装 + Command绑定
    -> LoadConversationCommand(id) 
    -> ChatViewModel.LoadConversation()
    -> ConversationService.LoadConversation()
    -> Messages.Clear() + Messages.Add(m)
    -> StatusMessage = "已加载 N 条消息"
    -> finall()
```

## 功能 2：AI助手副窗口

```
btn:[切换 AI 面板]
用户点击按钮 -> TogglePanelCommand
    -> MainWindow.OnToggleAiPanel()
    -> AiSecondaryWindow 是否已创建？
        |-- 否：new AiSecondaryWindow() + Owner=MainWindow + Show()
        |-- 是：Visible ? Hide() : Show() + 重新定位
    -> AiSecondaryWindow.Location = MainWindow.Right + 对齐顶部
    -> AI 操作通过 AiChatPanel (UserControl) 完成
    -> MainWindow.Closed -> AiSecondaryWindow.Close()
    -> finall()
```

## 功能 3：AI 能力体系（技能系统）— 2026-06-05 实现

```
用户发送自然语言指令 -> ChatViewModel.SendAsync()
    │
    ├── (可选) ContextBuilder.BuildSystemPrompt()
    │   ├── AppendIdentity() -> 身份设定
    │   ├── AppendBehaviorGuidelines() -> 行为准则
    │   ├── AppendCapabilities() -> 能力范围（含技能工具列表）
    │   └── AppendDynamicContext() -> 当前上下文（页面URL、标题、时间）
    │
    ├── SkillSystemIntegration.Initialize()
    │   ├── RegisterAllSkills()
    │   │   ├── 13个基础技能注册到 SkillRegistry._basic
    │   │   ├── 9个组合技能注册到 SkillRegistry._composite
    │   │   └── 6个策略技能注册到 SkillRegistry._strategies
    │   ├── RegisterStrategyHandlers()
    │   │   ├── NavigationStrategy -> strategy_navigation
    │   │   ├── LocateStrategy -> strategy_locate
    │   │   ├── RetryStrategy -> strategy_retry
    │   │   ├── ContextStrategy -> strategy_context
    │   │   ├── RecoveryStrategy -> strategy_recovery
    │   │   └── PrivacyStrategy -> strategy_privacy
    │   └── Validate() -> 验证所有技能引用完整性
    │
    ├── ContextBuilder.ImportSkillsFromRegistry(SkillSystem)
    │   ├── 基础技能(13个) -> RegisteredTools (作为AI可调用的Tool)
    │   └── 组合技能(9个) -> RegisteredTools (作为AI可调用的Tool)
    │
    ├── 技能执行流程 (skill_execution_flow)
    │   ├── RecommendForIntent(userMessage)
    │   │   └── 关键词 + 名称 + 描述匹配 -> 得分排序 -> Top 5
    │   ├── 选择技能 (skill_navigate / compose_search / ...)
    │   ├── SkillExecutor.ExecuteAsync(skillId, params)
    │   │   ├── 基础技能: ExecuteBasicAsync()
    │   │   │   └── _basicExecutors[skillId](params) -> 模拟执行或真实CDP调用
    │   │   ├── 组合技能: ExecuteCompositeAsync()
    │   │   │   └── 循环步骤:
    │   │   │       ├── ExecuteAsync(step.SkillId, mergedParams)
    │   │   │       ├── 失败处理:
    │   │   │       │   ├── FallbackSkillId存在 -> 降级执行
    │   │   │       │   ├── IsOptional == true -> 跳过
    │   │   │       │   └── 否则 -> 整体失败
    │   │   │       └── OnStepStateChanged -> 实时UI更新
    │   │   └── 策略技能: ExecuteStrategyDecisionAsync()
    │   │       └── IStrategyHandler.DecideAsync(context)
    │   │           └── 返回 StrategyDecision (Proceed/Retry/Fallback/...)
    │   ├── OnSkillStateChanged -> UI更新步骤可视化
    │   └── SkillExecutionHistory.Add(result) -> 记录执行历史
    │
    └── finall() -> StatusMessage = "✅/❌ 技能'XX'执行成功/失败 (Xms)"

## 功能 4：AI面板输出优化 — 2026-06-05 实现

```
需求来源: Solve.md

需求1: 面板不输出代码 / 需求2: 一行工具调用描述 / 需求3: 输出AI执行结果
───────────────────────────────────────────────────────────────
原流程:
  ExecuteConversationAsync()
    ├── yield return "🔧 **执行 skill_query**\n\n[模拟]...\n\n✅ 完成"
    └── ChatViewModel 追加到 aiMsg.Content → 面板显示原始文本

新流程:
  ExecuteConversationAsync()
    ├── 工具执行细节只追加到 messages (供AI上下文推理)
    ├── 不再 yield 工具执行细节文本
    ├── 通过回调事件通知 UI:
    │     OnToolStatusChanged?.Invoke(toolName, status, summary)
    └── ChatViewModel 接收:
          ├── AI文本 → aiMsg.Content (UI显示)
          └── StatusMessage → 一行描述 (如 "✅ 已输入文本: 666")

需求4: AI输出框集成 md 文件显示
─────────────────────────────────
  AiChatPanel.xaml:
    TextBlock → Markdown 渲染控件
  
  方案: 自定义 MarkdownToFlowDocument 转换器
    ├── # → Heading(增大字号+加粗)
    ├── **text** → Bold
    ├── - item → BulletList
    ├── ```code``` → 代码块(灰色背景+等宽字体)
    ├── | table | → Table
    └── 绑定: Content → IValueConverter → FlowDocument
```
```

## 功能 5：AI 交互式暂停/确认机制 — 2026-06-05 设计

```
需求来源: 用户反馈——"AI回复时只能一次全部输出"

问题分析:
  SendMessageAsync() 的 IAsyncEnumerable<AiEvent> 是单向输出流，
  缺少"暂停-恢复"的双向交互通道。
  AI 在遇到岔路口时只能自己猜方向，用户无法介入。

修复方案: 新增 ask_user 工具 + Orchestrator 暂停/恢复机制

完整实现路径:
───────────────────────────────────────────────────────────────
用户: "帮我在这个页面找登录按钮并点击"
    │
    ├── [Orchestrator] SendMessageAsync()
    │   ├── ContextBuilder.Build()
    │   │   ├── 当前标签信息 (URL + 标题 + 加载状态)
    │   │   ├── 页面内容 (get_page_text 提取的可见文本)
    │   │   └── 对话历史 (按 Token 预算裁剪)
    │   │
    │   ├── [AiClient] POST → AI Provider API (带 43 个 Tool Definitions)
    │   │
    │   ├── [循环 Round 1-2: 正常工具调用]
    │   │   ├── tool_use("get_page_links") → ToolExecutor → AutomationBridge → 返回50个链接
    │   │   └── tool_use("query_selector_all", "button, a[href*='login']") → 返回3个元素
    │   │
    │   ├── [循环 Round 3: 触发暂停]
    │   │   └── tool_use("ask_user", {
    │   │         question: "找到了3个可能的登录入口，应该使用哪一个？",
    │   │         question_type: "multiple_choice",
    │   │         options: ["顶部登录按钮", "导航栏登录", "侧边栏登录"],
    │   │         context_summary: "已分析当前页面，找到3个登录相关元素。",
    │   │         default_option: "顶部登录按钮"
    │   │       })
    │   │       │
    │   │       ├── Orchestrator 拦截 ask_user (不调用 ToolExecutor)
    │   │       ├── GenerateQuestionId() → "q_20260605_143022_a1b2c3"
    │   │       ├── BuildUserQuestionInfo(toolCall) → UserQuestionInfo
    │   │       ├── yield AiEvent { Type: UserQuestion, UserQuestion: info }
    │   │       ├── yield AiEvent { Type: AwaitingUserInput }
    │   │       ├── _pauseSignal = new TaskCompletionSource<string>()
    │   │       └── await _pauseSignal.Task ← 【挂起在此】
    │   │
    │   └── [暂停中... 等待用户交互]
    │         │
    │         │  [AiPanelViewModel] 收到 UserQuestion 事件
    │         │    ├── CurrentQuestion = evt.UserQuestion
    │         │    ├── IsShowingQuestion = true
    │         │    └── AddQuestionCard(evt.UserQuestion!) → 渲染问题卡片
    │         │
    │         │  [AiChatPanel UI] 问题卡片
    │         │    ┌─────────────────────────────────┐
    │         │    │ 🤔 AI 需要你的指引              │
    │         │    │ 已分析当前页面，找到3个...      │
    │         │    │ 找到了3个可能的登录入口，       │
    │         │    │ 应该使用哪一个？                │
    │         │    │                                 │
    │         │    │ ○ 顶部登录按钮 (主登录入口)     │
    │         │    │ ○ 导航栏登录 (登录/注册链接)    │
    │         │    │ ○ 侧边栏登录 (侧边栏入口)       │
    │         │    │                                 │
    │         │    │ [跳过]              [确认 ▸]   │
    │         │    └─────────────────────────────────┘
    │         │
    │         │  [用户点击] "顶部登录按钮" → [确认]
    │         │    ├── SelectOptionCommand.Execute("顶部登录按钮 (主登录入口)")
    │         │    │
    │         │    └── [AiPanelViewModel] SelectOptionAsync("顶部登录按钮 (主登录入口)")
    │         │        ├── IsShowingQuestion = false
    │         │        └── orchestrator.RespondToQuestionAsync(qId, answer)
    │         │              │
    │         │              ├── 验证 IsAwaitingUserInput == true ✅
    │         │              ├── 验证 QuestionId 匹配 ✅
    │         │              ├── 构造 tool_result → 追加到 conversationHistory
    │         │              ├── IsAwaitingUserInput = false
    │         │              ├── _pauseSignal.SetResult("顶部登录按钮 (主登录入口)")
    │         │              └── SendMessageAsync 的 await _pauseSignal.Task 返回
    │         │                    │
    │         │                    ▼
    │         │           [恢复执行]
    │         │
    │         ├── [循环 Round 4] AI 收到 tool_result(用户回答)
    │         │   └── tool_use("click", "#btn-login") → AutomationBridge → 点击成功
    │         │
    │         ├── [循环 Round 5] tool_use("wait_for_navigation") → 等待跳转
    │         │
    │         ├── [循环 Round 6] AI 最终回复
    │         │   └── stream_text: "好的，我已经点击了顶部的登录按钮，
    │         │                    页面已跳转到登录页。登录表单包含用户名和密码字段，
    │         │                    需要我帮你填写吗？"
    │         │
    │         └── yield AiEvent { Type: Complete }
    │
    └── finall() → 对话完成

关键设计决策:
  - ask_user 是唯一不经过 IAutomationBridge 的工具（直接由 Orchestrator 拦截）
  - 暂停通过 TaskCompletionSource 实现，await 挂起 IAsyncEnumerable 枚举
  - RespondToQuestionAsync 通过 _pauseSignal.SetResult() 唤醒挂起点
  - 支持嵌套暂停：AI 收到回答后可再次调用 ask_user
  - 问题和回答均持久化到 ai_messages 表，会话恢复时可完整回放决策链
```

## 功能 6：AI任务清单右侧布局与最新输出保持可见 — 2026-06-07 实现

```
需求来源: Pro.md

目标:
  1. todolist 放到 AI 模块右方，并有足够显示区域
  2. todolist 是 AI 拆分任务时一次性设计好的完整子任务列表
  3. 后续实时更新只更新子任务完成情况
  4. AI 输出始终保持在最下方，用户持续看到最新输出
  5. 降低最后一次使用后未响应/崩溃风险

布局流程:
───────────────────────────────────────────────────────────────
AiSecondaryWindow
  ├── Width: 720 / MinWidth: 560 / MaxWidth: 1100
  └── AiChatPanel
      ├── Row 0: 标题栏
      ├── Row 1: 主工作区 Grid
      │   ├── Column 0: 最近对话 + 消息输出
      │   └── Column 1: 实时任务清单 TodoItems
      ├── Row 2: ask_user 提问卡片
      └── Row 3: 底部输入区

任务清单流程:
───────────────────────────────────────────────────────────────
用户输入复杂任务
  -> ContextBuilder 系统提示要求先拆分完整子任务
  -> AI 调用 update_todo(items=[全部子任务], summary)
      -> ChatViewModel.GetTodoItems()
      -> TodoItems.Clear()
      -> TodoItems.Add(完整列表)
      -> 右侧任务清单一次性显示全部子任务
  -> AI 依次调用 start_subtask(id)
      -> UpdateTodoItem(id, ..., in_progress)
      -> 返回 __SUBTASK_CONTEXT_COMPRESSED__ 标记
  -> AI 完成后调用 finish_subtask(id, completed/blocked)
      -> UpdateTodoItem(id, ..., completed/blocked)
      -> 右侧只更新状态，不新增计划外子任务

## 功能 7：AI 任务分解强制门禁 — 2026-06-09 实现

```
用户输入任意任务
  -> ChatViewModel.SendAsync()
      -> AiClient.BuildOpenAIRequest()/BuildAnthropicRequest()
          -> ResolveRequiredPlanningTool(messages)
              |-- 尚无工具结果：必须先调用 update_todo
              |-- 已有 update_todo 但尚无 start_subtask：必须先调用 start_subtask
              |-- 已进入子任务执行：不强制工具
          -> OpenAI 兼容请求
              |-- 支持强制 tool_choice：发送 function tool_choice
              |-- DeepSeek/火山方舟/Thinking 等不兼容强制 tool_choice：省略 tool_choice 字段，改由系统提示推动规划工具，避免 API 400 InvalidParameter
          -> Anthropic 原生请求：发送 Anthropic tool_choice
      -> AI 首轮必须调用 update_todo(items=[完整子任务清单])
          -> ChatViewModel.ExecuteAiToolAsync("update_todo")
              |-- items 为空：拒绝并提示重新拆分，保留现有清单
              |-- items 有效：TodoItems.Clear() + TodoItems.Add(...)
      -> AI 下一轮必须调用 start_subtask(id)
          -> 若 id 未预登记：返回错误，要求先 update_todo
          -> 若 id 已预登记：标为 in_progress 并触发上下文压缩
      -> 后续浏览器/信息收集工具正常执行
      -> finall()：右侧待办清单不再保持为空，且第一个动作就是任务分解

模型配置协议修正:
  AiSettingsStore.Load()/Save()
      -> NormalizeProviderProtocols()
          |-- 火山 endpoint 为 /api/coding
              -> 自动补齐为 /api/coding/v3，避免正式请求拼成 /api/coding/chat/completions 后 404
          |-- provider=anthropic 且 endpoint 为 ark/openai/chat/completions/compatible-mode
              -> 自动改为 volcengine-ark 或 custom
  AiClient.Settings setter
      -> NormalizeSettingsProtocol()
          |-- 运行时再次兜底修正火山 endpoint 与错配协议
  AiClient.ConfigureHeaders()
      |-- 真 Anthropic 原生端点：x-api-key + /v1/messages
      |-- OpenAI 兼容端点：Bearer + /chat/completions
  -> 避免“测试连接可用，但任务循环误走 Anthropic 协议导致 Unauthorized”
```

## 功能 8：AI 输出思考过程 / 结论分区 — 2026-06-09 实现

```
AiClient 流式返回 chunk
  -> ChatViewModel.aiMsg.AppendContent(chunk)
      -> ChatMessage.Content 保留完整原文，不改变 API / 会话保存格式
  -> aiMsg.NotifyContentChanged()
      -> Notify Content / ThinkingContent / ConclusionContent / HasThinkingContent
  -> AiChatPanel Assistant 消息模板
      -> AssistantResponseParser.Parse(Content)
          |-- 显式标记：[思考过程] / [结论] / <think>...</think>
          |-- 启发式：The user wants / Let me / 上一步评估 / 记忆 / 下一目标 → 思考过程
          |-- 结论起点：通过...来看 / 准确说 / 所以 / 答案是 → 结论
          |-- 代码块内部不切分
      -> [思考过程] Expander 默认折叠；无思考内容则隐藏
      -> [结论] FlowDocument 始终显示
  -> AiChatPanel.OnMessagePropertyChanged(Content)
  -> ScrollToBottom()
  -> Dispatcher.BeginInvoke(ContextIdle)
  -> MessageScroller.ScrollToBottom()
  -> 用户默认只看到最终结论，可按需展开查看思考过程
```

最新输出保持可见:
───────────────────────────────────────────────────────────────
AiClient 流式返回 chunk
  -> ChatViewModel.aiMsg.AppendContent(chunk)
  -> aiMsg.NotifyContentChanged()
  -> AiChatPanel.OnMessagePropertyChanged(Content)
  -> ScrollToBottom()
  -> Dispatcher.BeginInvoke(ContextIdle)
  -> MessageScroller.ScrollToBottom()
  -> 用户看到最新输出

未完成子任务防误结束:
────────────────────────────────
AiClient.ExecuteConversationAsync()
  -> AI 返回纯文本、没有工具调用
  -> ShouldContinueOpenSubtask(messages, fullText)
      |-- 没有开放子任务：按普通最终回复结束
      |-- 存在 start_subtask 且之后没有 finish_subtask：注入 system reminder，要求继续调用工具 / finish_subtask / ask_user
      |-- 连续 3 次仍只输出文本：返回带“当前任务尚未完成”的阶段性提示
  -> ChatViewModel.GetStatusMessageAfterToolLoop(content)
      |-- 检测到“当前任务尚未完成” -> StatusMessage = “任务未完成，等待继续…”
      |-- 否则 -> StatusMessage = “就绪”
  -> finall(): 不再把开放子任务的阶段性说明误显示为已完成状态

稳定性处理:
───────────────────────────────────────────────────────────────
AiChatPanel.Loaded
  -> AttachViewModel()
      -> 订阅 ViewModel.PropertyChanged
      -> 订阅 Messages.CollectionChanged
      -> 订阅每条 ChatMessage.PropertyChanged
AiChatPanel.Unloaded
  -> DetachViewModel()
      -> 解除全部订阅，避免副窗口反复显示造成重复回调

长回复渲染:
  MarkdownToFlowDocumentConverter.Convert()
    -> markdown.Length > 12000 ? 仅渲染末尾 12000 字符 : 全量渲染
    -> 降低 WPF FlowDocument 反复构建压力
```

## 功能 11：WebView2 标签生命周期管理 — 2026-06-11

```
用户点击新建标签
  -> BrowserViewModel.NewTabCommand
  -> BrowserViewModel.AddNewTab(url)
      -> Tabs.Add(tab)
      -> ActiveTab = tab
  -> MainWindow.Tabs.CollectionChanged(Add)
      -> EnsureAddedTabWebViewAsync(tab)
          -> EnsureTabWebViewAsync(tab)
              -> BrowserHostService.CreateTabForAsync(tab, tab.Url)
                  -> WebView2 实例化 → 加入 ContentArea 容器
                  -> wv.EnsureCoreWebView2Async(_environment)
                  -> ConfigureCoreWebView2(wv.CoreWebView2)
                      -> ScriptEnabled=true, WebMessageEnabled=true, DevTools=true
                  -> BindCoreEvents(tab, wv)
                      -> NavigationStarting/Completed/DocumentTitleChanged/SourceChanged
                      -> DownloadStarting/ScriptDialogOpening/NewWindowRequested/ProcessFailed
                  -> _webViews[tab.Id] = wv
                  -> wv.CoreWebView2.Navigate(url)
              -> BrowserAutomationService.BindWebView(tab.Id, wv)
          -> ActivateTabWebView(tab.Id)
              -> BrowserHostService.ActivateTab(tab.Id) (Visibility 切换)
              -> BrowserAutomationService.SwitchToTab(tab.Id)
  -> finall(): 新标签完整创建并绑定 WebView2 与自动化服务

用户关闭标签
  -> BrowserViewModel.CloseTab(guid)
      -> Tabs.Remove(tab)
      -> TabClosed?.Invoke(guid)
  -> MainWindow.OnTabClosed(guid)
      -> BrowserHostService.CloseTabAsync(guid)
          -> _webViews.Remove(guid)
          -> _container.Children.Remove(wv)
          -> wv.Dispose()
      -> BrowserAutomationService.UnbindWebView(guid)
  -> finall(): WebView2 控件已释放，自动化绑定已解除

用户切换标签
  -> BrowserViewModel.ActivateTab(id)
      -> ActiveTab = tab
      -> TabActivated?.Invoke(id)
  -> MainWindow.OnTabActivated(id)
      -> BrowserHostService.GetWebViewForTab(id) == null ? CreateTabForAsync : 直接激活
      -> BrowserHostService.ActivateTab(id) (Visibility 切换)
      -> BrowserAutomationService.SwitchToTab(id)
      -> 同步 URL/Title 到 ChatViewModel
  -> finall(): 目标 WebView2 Visible，其余 Collapsed
```

## 功能 12：AI 工具调用与路由 — 2026-06-11

```
AI 调用 browser_snapshot
  -> AiClient.ExecuteConversationAsync()
      -> ParseOpenAILineRich/ParseAnthropicLineRich 解析 tool_calls
      -> toolCallAcc[idx] = ToolCallData { FunctionName="browser_snapshot", ... }
  -> ChatViewModel.ExecuteAiToolAsync("browser_snapshot", args)
      -> _automationRouter.IsToolRegistered("browser_snapshot") → true
      -> _automationRouter.InvokeAsync("browser_snapshot", args)
          -> _automation.GetSnapshotAsync()
              -> InvokeJsCallAsync(AutomationScripts.GetSnapshotCall, "快照", returnRawJson=true)
                  -> RunOnUiThreadAsync(wv => wv.CoreWebView2.ExecuteScriptAsync(bermainA11y.getSnapshot()))
                      -> StripQuotes(raw) → JSON 字符串
      -> Format(AutomationResult) → JSON序列化 {ok, data, url, ms}
  -> AiClient 将结果追加为 Tool 消息 → 继续下一轮迭代
  -> finall(): 页面快照以结构化 JSON 返回 AI

AI 调用 browser_click
  -> AiClient → ChatViewModel.ExecuteAiToolAsync("browser_click", {element_id: 5})
      -> _automationRouter.InvokeAsync → ClickAsync(5)
          -> InvokeJsCallAsync(bermainA11y.clickElement(5), "点击")
              -> 定位 data-bermain-id="5" 元素 → el.click()
              -> 检查 JS 返回值中的 error 字段
          -> 解析 error/success → Format()
  -> finall(): 点击操作结果 JSON 返回 AI

ask_user 暂停流程
  -> AI 调用 ask_user(question, question_type, options)
  -> ChatViewModel.ExecuteAiToolAsync("ask_user", args)
      -> 解析 question/question_type/options/context_summary
      -> 生成 QuestionId = "q_HHmmssfff_Guid"
      -> 返回 "__ASK_USER_PAUSED__:{UserQuestionInfo JSON}"
  -> AiClient.ExecuteConversationAsync 检测 __ASK_USER_PAUSED__:
      -> messages 追加占位 Tool 消息 (Content="等待用户回答…")
      -> yield chunk("__ASK_USER_PAUSED__:...") → yield break
  -> ChatViewModel.SendAsync 检测暂停标记
      -> IsAwaitingUserInput = true
      -> PendingAskUserQuestion = questionInfo
      -> _pendingMessages = mutableMessages, _pendingAiMsg = aiMsg
      -> ShowAskUserPromptMessage(questionInfo)
  -> UI 显示问题卡片（confirmation/multiple_choice/open_ended）
  -> 用户选择选项
      -> RespondToQuestionAsync(option)
          -> DeactivateAskUserPromptMessage(answer)
          -> 替换占位 Tool 消息 Content = answer
          -> ContinueToolLoopAsync(pendingMsgs, pendingAi, ct)
              -> _aiClient.ExecuteConversationAsync(messages, ExecuteAiToolAsync, ct)
                  -> AI 收到 user 回答 → 继续执行工具调用
  -> finall(): 工具循环恢复执行

上下文压缩
  -> AiClient.ExecuteConversationAsync 每轮迭代开始
      -> EstimateConversationBytes(messages) > 120000 ?
      -> CompressHistory(messages, 90000, runtimeToolEvidence)
          -> FindCompressionBoundary: 从后往前找最近 Assistant 边界
          -> ApplyCompression: 旧消息替换为 System 摘要
              -> 记录已调用工具名 + 助手文本摘要 + 工具结果摘要
              -> 插入 tool_evidence 注释
          -> 循环直到 < 90000 字节
  -> finall(): 历史压缩到 ~90KB，保留工具执行证据
```

## 功能 13：AI 提供商协议自动修正 — 2026-06-11

```
应用启动
  -> App.OnStartup()
      -> AiSettingsStore.Load()
          -> 读取 ai_settings.json
          -> NormalizeProviderProtocols()
              -> 遍历每个 profile
              -> NormalizeProviderProtocol(profile)
                  -> NormalizeArkCodingEndpoint(profile)
                      -> endpoint 含 "volces.com" && 以 "/api/coding" 结尾
                      -> fixedEndpoint = endpoint + "/v3"
                      -> profile.Endpoint = fixedEndpoint
                  -> provider=anthropic && endpoint 是 OpenAI 兼容
                      -> providerKey = "volcengine-ark" 或 "custom"
      -> Save() → 持久化修正后的配置

AI 请求时
  -> AiClient.StreamMessageAsync()
      -> ConfigureHeaders()
          -> IsAnthropicProvider()
              -> ProviderKey="anthropic" && endpoint 不是 OpenAI 兼容
              -> x-api-key + anthropic-version: 2023-06-01
              -> 否则: Bearer Token
  -> BuildOpenAIRequest() / BuildAnthropicRequest()
      -> 根据 provider 选择请求格式
      -> 注入系统提示词 + 工具定义 + 消息历史
  -> finall(): 不同提供商使用正确的认证方式和 API 格式
```

## 功能 14：下载管理与历史记录 — 2026-06-11

```
WebView2 下载开始
  -> BrowserHostService.BindCoreEvents()
      -> core.DownloadStarting += (_, args)
          -> var item = new DownloadItem {...}
          -> DownloadManager.Add(item)
              -> Dispatcher.Invoke(() => Items.Insert(0, item))
          -> operation.BytesReceivedChanged += ()
              -> DownloadManager.Update(item, x => x.BytesReceived = ...)
          -> operation.StateChanged += ()
              -> DownloadManager.Update(item, x => x.State = ...)
                  -> Completed → DownloadItemState.Completed
                  -> Interrupted → DownloadItemState.Failed

导航完成时记录历史
  -> BrowserHostService.BindCoreEvents()
      -> core.NavigationCompleted += (_, args)
          -> _vm.RecordHistoryEntry(info.Url, tab?.Title)
              -> 最后一条同 URL → 移除重新插入（去重刷新）
              -> 新 HistoryInfo 插入头部
              -> History.Count > 500 → 尾部自动移除
          -> HistoryService.SaveHistory(_vm.History)
              -> 序列化 → history.json

用户操作书签/历史
  -> BrowserViewModel.AddCurrentPageToBookmarks()
      -> ActiveTab != null && URL 有效
      -> 检查是否已收藏（URL 去重）
      -> BookmarkInfo → Bookmarks.Add(bookmark)
      -> BookmarkService.SaveBookmarks() → bookmarks.json
  -> BrowserViewModel.OpenBookmark(bookmark)
      -> ActiveTab == null ? AddNewTab(url) : 导航到 url
  -> BrowserViewModel.OpenHistory(history)
      -> ActiveTab == null ? AddNewTab(url) : 导航到 url
  -> finall(): 书签和历史持久化到 JSON 文件
```

## 功能 15：流式输出与 UI 渲染 — 2026-06-11

```
AI 流式文本到达
  -> AiClient.ParseStreamAsync()
      -> reader.ReadLineAsync() → "data: {chunk}"
      -> ParseOpenAILine() / ParseAnthropicLine()
      -> yield chunk
  -> ChatViewModel.SendAsync()
      -> aiMsg.AppendContent(chunk)
      -> aiMsg.NotifyContentChanged()
          -> OnPropertyChanged("Content")
          -> OnPropertyChanged("ThinkingContent/ConclusionContent/HasThinkingContent/...")
  -> AiChatPanel.OnMessagePropertyChanged()
      -> ScrollToBottom()
          -> Dispatcher.BeginInvoke(ContextIdle, ScrollToBottom)
  -> finall(): UI 实时显示 AI 输出，思考/结论分区渲染

AI 回复完成
  -> FinalizeAssistantMessage(aiMsg)
      -> AssistantResponseParser.ParseAndClean(Content.Trim())
          -> StripLeadingJsonBlocks(content) → 剥离重复 JSON blob
          -> ParseExplicitSections(content) → [思考过程]/[结论] 显式标记
          -> FindHeuristicConclusionStart(content) → 启发式切分
          -> StripTrailingJsonWords(conclusion) → 剥离尾部 JSON 词
      -> aiMsg.ReplaceContentSilently(finalContent)
      -> aiMsg.NotifyContentChanged()
  -> AutoSave() → ConversationService.SaveConversation()
  -> UpdateTokenEstimate() → Messages.Sum(m.Content.Length / 2)
  -> finall(): 最终结论存入消息、对话自动保存、令牌估算更新
```
