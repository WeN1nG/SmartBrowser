# 功能实现模拟文档

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
