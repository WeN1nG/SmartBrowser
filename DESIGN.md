# SmartAI Browser Demo — 实际实现设计文档

> 版本：2.0（按当前 Demo 代码重写）  
> 更新：2026-06-08  
> 适用代码：`Demo/BrowserDemo/`

---

## 1. 项目概述

**SmartAI Browser Demo** 是一个 Windows 智能浏览器原型，使用 **C# / .NET 8 / WPF / WebView2** 构建。它把可见浏览器、AI 对话面板、函数调用工具循环整合在同一个桌面应用中，让 AI 助手 **Bermain（板儿面）** 能通过自然语言任务调用浏览器工具，完成打开网页、观察页面、点击、输入、等待、截图、执行 JS、填写表单等操作。

当前 Demo 的核心不是“完整商业浏览器”，而是一个可运行的 **AI 控制浏览器演示系统**：

- 主窗口显示多标签 WebView2 浏览器；
- AI 面板独立浮动在主窗口右侧；
- 用户配置自己的模型 API Key；
- AI 客户端以 OpenAI-compatible 或 Anthropic-native 协议流式请求模型；
- 模型返回 Tool Call 后，由本地 WebView2 自动化服务执行；
- 工具结果回传给模型，模型继续推理，直到输出最终答案或需要用户确认。

### 1.1 当前实现与早期蓝图的差异

早期设计中曾规划：

- .NET 9 / Native AOT；
- ModernWpf；
- SQLite；
- DI 容器；
- WebView2 + 自研 AutomationBridge；
- 或外部 Chrome + Playwright MCP / CDP；
- 书签、历史、下载、扩展、隐私模式等完整浏览器功能。

当前 Demo 实际实现为：

- `.NET 8` WPF；
- 仅 NuGet 依赖 `Microsoft.Web.WebView2`；
- 无 DI 容器，手动 new 服务；
- 无 SQLite，设置和会话使用 JSON 文件；
- 浏览器为嵌入式 WebView2；
- AI 浏览器工具走 `BrowserAutomationService` + `BrowserAutomationToolRouter`；
- Playwright MCP / 外部 Chrome CDP 代码仍保留，但当前启动路径不使用；
- 旧 `WebView2AutomationBridge.cs` 被 `#if false` 整体禁用。

---

## 2. 技术栈与运行环境

| 层面 | 当前选择 | 说明 |
|------|----------|------|
| 语言 | C# | Nullable + implicit usings |
| 运行时 | .NET 8 | `net8.0-windows` |
| UI | WPF | 手写暗色 UI，无 ModernWpf |
| 浏览器 | WebView2 | 多个 WebView2 控件共享一个 `CoreWebView2Environment` |
| 自动化 | WebView2 API + JS 注入 | UI Dispatcher 执行 WebView2 调用，JS 负责页面元素快照与 DOM 操作 |
| AI API | 手写 `HttpClient` SSE | 支持 OpenAI-compatible 和 Anthropic native |
| 工具协议 | Function Calling / Tool Use | `ToolDefinition` 转换为 OpenAI / Anthropic schema |
| 数据存储 | JSON 文件 | AI 设置、对话会话 |
| 日志 | 自研 `Logger` | 控制台、文件、内存缓冲、Trace scope |
| 构建 | `dotnet build BrowserDemo/BrowserDemo.csproj` | 无 `.sln`，无测试项目 |

运行要求：

- Windows 10/11；
- .NET 8 SDK（带 Windows Desktop workload）；
- WebView2 Runtime；
- 使用 AI 功能时需要用户自己的 API Key；
- Node.js / Playwright MCP 只在测试旧 MCP 路径时需要。

---

## 3. 实际目录结构

```text
Demo/BrowserDemo/
├── BrowserDemo.csproj
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── AssemblyInfo.cs
├── Converters.cs
├── StringExtensions.cs
│
├── Models/
│   ├── BrowserViewModel.cs
│   ├── TabInfo.cs
│   ├── DownloadItem.cs
│   ├── ChatMessage.cs
│   ├── ToolCallData.cs
│   ├── ToolDefinition.cs
│   ├── AiSettings.cs
│   ├── AiSettingsStore.cs
│   ├── ProviderInfo.cs
│   ├── AiTodoItem.cs
│   ├── SkillDefinition.cs              # 旧 record 技能模型
│   ├── BasicSkillDefinition.cs         # 旧 record 技能模型
│   ├── CompositeSkillDefinition.cs     # 旧 record 技能模型
│   ├── StrategySkillDefinition.cs      # 旧 record 技能模型
│   ├── SkillStep.cs                    # 旧 record 技能模型
│   └── SkillExecutionResult.cs         # 旧 record 技能模型
│
├── ViewModels/
│   └── ChatViewModel.cs
│
├── Views/
│   ├── AiChatPanel.xaml / .cs
│   ├── AiSecondaryWindow.xaml / .cs
│   ├── AiModelSelectionDialog.xaml / .cs
│   ├── AiSettingsDialog.xaml / .cs
│   └── DownloadsWindow.xaml / .cs
│
├── Services/
│   ├── Logger.cs
│   ├── IAiClient.cs
│   ├── AiClient.cs
│   ├── ContextBuilder.cs
│   ├── ConversationService.cs
│   ├── DownloadManager.cs
│   │
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs       # 当前 WebView2 宿主
│   │   └── ChromeProcessManager.cs     # 旧外部 Chrome/CDP 路径
│   │
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs # 当前浏览器自动化服务
│   │   ├── BrowserAutomationToolRouter.cs
│   │   ├── AutomationScripts.cs
│   │   ├── AdbService.cs
│   │   └── WebView2AutomationBridge.cs # #if false 死代码
│   │
│   ├── Mcp/
│   │   ├── JsonRpcClient.cs            # 旧 MCP JSON-RPC 客户端
│   │   ├── PlaywrightMcpClient.cs      # 旧 Playwright MCP 包装
│   │   └── Models/McpMessage.cs
│   │
│   └── Skills/
│       ├── SkillModels.cs
│       ├── SkillRegistry.cs
│       ├── SkillSystemIntegration.cs   # 旧 MCP 技能系统入口
│       ├── McpSkillDataProvider.cs
│       ├── McpSkillExecutor.cs
│       ├── SkillExecutionContext.cs
│       └── Strategy/
│           ├── IStrategyHandler.cs
│           ├── NavigationStrategy.cs
│           ├── LocateStrategy.cs
│           ├── RetryStrategy.cs
│           ├── ContextStrategy.cs
│           ├── RecoveryStrategy.cs
│           └── PrivacyStrategy.cs
│
└── Converters/
    └── MarkdownToFlowDocumentConverter.cs

Tools/
├── playwright-mcp/playwright-mcp-0.0.75/
└── platform-tools/
```

---

## 4. 总体架构

当前 Demo 可以分为五个主要层次：

```text
┌─────────────────────────────────────────────────────────────┐
│                         WPF UI                              │
│ MainWindow | AiSecondaryWindow | AiChatPanel | Downloads UI │
└───────────────┬─────────────────────────────┬───────────────┘
                │                             │
                ▼                             ▼
┌─────────────────────────────┐   ┌───────────────────────────┐
│ BrowserViewModel            │   │ ChatViewModel              │
│ tabs/navigation/status       │   │ chat/tools/pause/resume    │
└───────────────┬─────────────┘   └──────────────┬────────────┘
                │                                │
                ▼                                ▼
┌─────────────────────────────┐   ┌───────────────────────────┐
│ BrowserHostService          │   │ AiClient + ContextBuilder  │
│ WebView2 lifecycle/tabs      │   │ SSE + tool schemas         │
└───────────────┬─────────────┘   └──────────────┬────────────┘
                │                                │
                ▼                                ▼
┌─────────────────────────────┐   ┌───────────────────────────┐
│ BrowserAutomationService    │◄──│ BrowserAutomationToolRouter│
│ WebView2 API + JS injection  │   │ browser_* tool dispatch    │
└─────────────────────────────┘   └───────────────────────────┘
```

### 4.1 关键对象职责

| 对象 | 职责 |
|------|------|
| `MainWindow` | 应用主壳；创建 `BrowserViewModel`；初始化 WebView2 宿主和自动化服务；打开 AI 副窗口和下载窗口 |
| `BrowserViewModel` | 管理 Tab 集合、地址栏、导航命令、状态文本 |
| `BrowserHostService` | 创建/关闭/激活 WebView2 标签；绑定 WebView2 事件；处理下载、弹窗、新窗口、崩溃 |
| `BrowserAutomationService` | 当前 AI 浏览器操作执行器；所有 WebView2 操作切回 UI 线程；串行化自动化操作 |
| `AutomationScripts` | 生成注入页面的 JS：快照、点击、输入、悬停、选择、滚动等 |
| `BrowserAutomationToolRouter` | 定义 AI 可见的 `browser_*` 工具 schema，并把调用参数转给 `BrowserAutomationService` |
| `ChatViewModel` | 聊天 UI 状态、消息列表、工具注册、工具调度、`ask_user` 暂停恢复、todo UI |
| `AiClient` | AI API 请求、SSE 流解析、OpenAI/Anthropic 工具调用循环、上下文压缩 |
| `ContextBuilder` | 构建系统提示词、动态页面上下文、工具 schema |
| `ConversationService` | 对话 JSON 文件保存/加载/删除 |
| `ProviderManager` | AI 服务商和模型列表 |

---

## 5. 启动与浏览器初始化流程

### 5.1 启动流程

```text
App 启动
  ↓
MainWindow 构造
  ├─ InitializeComponent()
  ├─ new BrowserViewModel()
  ├─ 绑定 BrowserViewModel 事件：导航/后退/前进/刷新/下载/标签关闭/标签激活
  ├─ 绑定 ChatViewModel 事件：打开设置、AI 面板显示状态
  ├─ 注册 Loaded
  └─ 注册 Closing 清理资源
  ↓
MainWindow.OnLoaded
  ├─ new BrowserHostService(Dispatcher, ContentArea)
  ├─ 设置 UserDataFolder = %LocalAppData%/SmartAI-Browser-Demo/webview2-profile
  ├─ await BrowserHostService.InitializeAsync()
  ├─ new BrowserAutomationService(); Initialize(Dispatcher)
  ├─ _browserHost.Automation = _automation
  ├─ WireBrowserHostEvents()
  ├─ 为 BrowserViewModel 里已有 Tab 创建 WebView2
  ├─ 激活当前 Tab
  ├─ _vm.Chat.AttachAutomationRouter(new BrowserAutomationToolRouter(_automation))
  └─ 状态显示：浏览器已嵌入，AI 浏览器工具已启用
```

### 5.2 当前不走外部 Chrome/CDP

`MainWindow.OnLoaded` 里有明确注释：

> Phase 4b：不再调用 `_vm.Chat.SetChromeCdpEndpoint`，AI browser_* 工具直接走 WebView2 Automation。

因此当前运行时：

- 不启动 `ChromeProcessManager`；
- 不打开独立 Chrome 进程；
- 不通过 Playwright MCP 控制浏览器；
- 不依赖 CDP 9222 端口；
- WebView2 是可见浏览器本体，也是自动化目标。

---

## 6. WebView2 浏览器宿主设计

### 6.1 `BrowserHostService`

`BrowserHostService` 是当前浏览器宿主。它接收：

- WPF `Dispatcher`；
- 作为 WebView2 容器的 `Panel`（`MainWindow.ContentArea`）。

核心字段：

- `_webViews: Dictionary<Guid, WebView2>`：Tab ID 到 WebView2 控件的映射；
- `_environment: CoreWebView2Environment?`：共享浏览器环境；
- `_activeTabId: Guid?`：当前激活标签；
- `UserDataFolder`：Cookie、缓存、LocalStorage 等浏览器数据目录。

### 6.2 初始化

`InitializeAsync()` 创建共享 `CoreWebView2Environment`：

- 如果设置了 `UserDataFolder`，用该目录作为 WebView2 profile；
- 设置 `AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection"`；
- 重复调用幂等。

### 6.3 标签生命周期

```text
CreateTabForAsync(TabInfo tab, string url)
  ├─ 创建 WebView2 控件
  ├─ 加入 WPF 容器，默认 Collapsed
  ├─ EnsureCoreWebView2Async(_environment)
  ├─ ConfigureCoreWebView2(core)
  ├─ BindCoreEvents(tab, webView)
  ├─ _webViews[tab.Id] = webView
  ├─ tab.CoreId = BrowserProcessId
  └─ 如果 url != about:blank，则 Navigate(url)
```

激活标签只切换 `Visibility`：

```text
ActivateTab(tabId)
  ├─ 目标 WebView2 = Visible
  ├─ 其他 WebView2 = Collapsed
  └─ _activeTabId = tabId
```

关闭标签：

```text
CloseTabAsync(tabId)
  ├─ 从字典移除
  ├─ 从 WPF 容器移除
  ├─ Dispose WebView2
  └─ 触发 TabClosed
```

### 6.4 WebView2 设置

`ConfigureCoreWebView2` 当前设置：

- `IsScriptEnabled = true`；
- `AreDefaultScriptDialogsEnabled = false`；
- `IsWebMessageEnabled = true`；
- `IsZoomControlEnabled = true`；
- `IsStatusBarEnabled = false`；
- `AreDevToolsEnabled = true`；
- `IsBuiltInErrorPageEnabled = true`；
- `IsPasswordAutosaveEnabled = false`；
- `IsGeneralAutofillEnabled = false`。

### 6.5 事件桥接

`BrowserHostService` 把 WebView2 事件转成自己的事件，`MainWindow.WireBrowserHostEvents()` 再同步给 UI 和自动化服务。

关键事件：

- 导航开始：显示加载条，更新状态，通知自动化服务加载中；
- 导航完成：隐藏加载条，更新地址栏、当前页面 URL，通知自动化服务导航结果；
- 标题变化：同步 `ChatViewModel.CurrentPageTitle`；
- URL 变化：同步 `ChatViewModel.CurrentPageUrl`；
- 新窗口请求：转换为应用内新 Tab；
- WebView 崩溃：关闭异常标签；
- 下载开始：创建并更新 `DownloadItem`。

---

## 7. 浏览器自动化设计

### 7.1 `BrowserAutomationService`

`BrowserAutomationService` 是当前 AI 工具真正执行浏览器操作的地方。

设计目标：

- 后台 AI 工具循环可以安全调用；
- WebView2 调用必须在 UI 线程执行；
- 自动化操作必须串行，避免同时点击/输入/导航导致状态错乱；
- 每次操作返回结构化 `AutomationResult`。

线程模型：

```text
AI 工具循环线程
  ↓ 调用 BrowserAutomationService.*Async
SemaphoreSlim(1,1) 串行化
  ↓ Dispatcher.InvokeAsync
WPF UI 线程执行 WebView2 / JS
  ↓
返回 AutomationResult
```

关键状态：

- `_dispatcher`：WPF UI Dispatcher；
- `_webViews`：已绑定 WebView2；
- `_activeTabId`：当前自动化目标；
- `_operationLock`：全局串行操作锁；
- `CurrentUrl`：当前 URL；
- `DefaultOperationTimeoutMs = 30000`。

### 7.2 当前自动化能力

| 能力 | 方法 / 工具 |
|------|-------------|
| 导航 | `NavigateAsync` / `browser_navigate` |
| 后退/前进/刷新 | `GoBackAsync` / `GoForwardAsync` / `ReloadAsync` |
| 页面快照 | `GetSnapshotAsync` / `browser_snapshot` |
| 点击 | `ClickAsync(elementId)` / `browser_click` |
| 输入 | `TypeAsync(elementId, text, clearFirst)` / `browser_type` |
| 悬停 | `HoverAsync(elementId)` / `browser_hover` |
| 下拉选择 | `SelectOptionAsync(elementId, value)` / `browser_select_option` |
| 滚动 | `ScrollAsync(deltaX, deltaY)` / `browser_scroll` |
| 特殊按键 | `PressKeyAsync(key)` / `browser_press_key` |
| 截图 | `TakeScreenshotAsync` / `browser_screenshot` |
| JS 执行 | `EvaluateJavaScriptAsync` / `browser_js` |
| 固定等待 | `WaitAsync(ms)` / `browser_wait` |
| 等待文本 | `WaitForTextAsync(text, timeout)` / `browser_wait_for` |
| 表单批量填充 | `FillFormAsync(fields)` / `browser_fill_form` |
| 切换自动化目标标签 | `SwitchToTab(Guid)` / `browser_switch_tab` |

### 7.3 元素定位约定

当前 WebView2 工具优先使用页面快照返回的整数 `element_id`。

典型流程：

```text
AI 调用 observe_browser 或 browser_snapshot
  ↓
返回页面交互元素列表，每个元素有 id
  ↓
AI 调用 browser_click / browser_type / browser_hover / browser_select_option
  ↓
参数使用 element_id = 快照中的整数 id
```

注意：

- `element_id` 可能因为页面刷新或 DOM 更新而失效；
- 工具连续失败时，`ChatViewModel.ExecuteAiToolAsync` 会提示重新获取快照；
- 第 3 次以上带旧 `element_id` 的失败路径会直接刷新快照并要求模型选新 id。

### 7.4 `AutomationScripts`

`AutomationScripts` 负责生成注入页面的 JavaScript。它承担：

- 给可交互元素生成稳定的 `data-bermain-id` / id 映射；
- 收集元素文本、role、type、name、aria-label、placeholder、value、visible、disabled、readonly 等信息；
- 点击元素；
- 输入文本，绕过前端框架对 value setter 的拦截，并分发 `input` / `change` 事件；
- 悬停、滚动、选择 option；
- 等待文本出现。

---

## 8. AI 工具注册与执行

### 8.1 工具注册入口

当前工具注册发生在：

```text
MainWindow.OnLoaded
  → ChatViewModel.AttachAutomationRouter(router)
```

`AttachAutomationRouter` 做五件事：

1. 保存 `_automationRouter`；
2. 注册 `router.GetToolDefinitions()` 返回的全部 `browser_*` 工具；
3. 注册 `observe_browser`；
4. 注册 `ask_user`；
5. 注册任务规划和上下文管理工具：`set_task_iterations`、`update_todo`、`start_subtask`、`finish_subtask`。

这些工具都进入 `ContextBuilder.RegisteredTools`，随后由 `AiClient` 转换成 OpenAI 或 Anthropic 的工具 schema。

### 8.2 当前 AI 可见工具

#### 浏览器工具

| 工具 | 说明 |
|------|------|
| `browser_navigate` | 打开指定 URL 并等待导航完成 |
| `browser_back` | 后退 |
| `browser_forward` | 前进 |
| `browser_reload` | 刷新 |
| `browser_snapshot` | 获取当前页面结构化快照，返回可交互元素 id |
| `browser_click` | 点击 `element_id` 对应元素 |
| `browser_type` | 在 `element_id` 对应输入元素中输入文本 |
| `browser_hover` | 悬停到元素 |
| `browser_select_option` | 选择下拉框 option value |
| `browser_scroll` | 按像素滚动页面 |
| `browser_press_key` | 发送 Enter、Tab、Escape、方向键等特殊按键 |
| `browser_screenshot` | 截图；结果只返回摘要，不把完整 base64 注入上下文 |
| `browser_js` | 执行自定义 JavaScript |
| `browser_wait` | 固定等待毫秒数 |
| `browser_wait_for` | 等待页面出现指定文本 |
| `browser_fill_form` | 批量填充表单字段 |
| `browser_switch_tab` | 切换自动化目标 Tab |

#### 包装与协作工具

| 工具 | 说明 |
|------|------|
| `observe_browser` | 调用 `browser_snapshot` 并格式化为 PageAgent 风格 `<browser_state>`，适合模型阅读 |
| `ask_user` | AI 遇到岔路口时暂停并向用户提问 |
| `set_task_iterations` | 设置本阶段工具循环软提醒阈值，范围 1–80 |
| `update_todo` | 在 AI 面板显示完整任务列表和状态 |
| `start_subtask` | 开始子任务，触发上下文压缩并标记 in_progress |
| `finish_subtask` | 结束子任务，标记 completed 或 blocked |

### 8.3 工具执行路由

`ChatViewModel.ExecuteAiToolAsync` 按顺序处理：

1. `observe_browser`；
2. `ask_user`；
3. `set_task_iterations`；
4. `update_todo`；
5. `start_subtask` / `finish_subtask`；
6. 当前 WebView2 自动化工具（`_automationRouter.IsToolRegistered(toolName)`）；
7. 旧 MCP 直接工具（仅 `SkillSystem.IsInitialized` 时）；
8. 旧组合技能（仅 `SkillSystem.IsInitialized` 时）；
9. 未注册工具错误。

当前正常运行路径命中第 1–6 类。

---

## 9. AI 客户端设计

### 9.1 Provider 与设置

`ProviderManager` 注册多个服务商及其默认 endpoint / auth type / 模型列表：

- OpenAI
- Anthropic
- Google Gemini（OpenAI-compatible endpoint）
- DeepSeek
- xAI
- Groq
- Cerebras
- Mistral
- Together AI
- Fireworks
- OpenRouter
- Alibaba / Qwen
- Zhipu
- Moonshot
- SiliconFlow
- Ollama
- DeepInfra

`AiSettings` 包含：

- `Id`
- `DisplayName`
- `ProviderKey`
- `ApiKey`
- `Model`
- `Endpoint`
- `ResolvedEndpoint`
- `DefaultModel`

`AiSettingsStore` 支持多 profile：

- `Profiles`
- `ActiveId`
- `DefaultId`

设置文件位置：

```text
<AppDomain.CurrentDomain.BaseDirectory>/ai_settings.json
```

### 9.2 OpenAI-compatible 请求

OpenAI-compatible 路径使用：

- Bearer token；
- `model`；
- `stream = true`；
- `messages`；
- `tools`；
- `tool_choice` 等按实现构造。

系统提示词由 `ContextBuilder.BuildSystemPrompt()` 注入为 `role=system` 消息。

流式解析主要处理：

- `delta.content`；
- `delta.tool_calls`；
- `finish_reason`。

### 9.3 Anthropic native 请求

Anthropic 路径使用：

- `x-api-key`；
- `anthropic-version: 2023-06-01`；
- endpoint `/v1/messages`；
- 顶层 `system` 字段；
- `messages`；
- `tools`。

流式解析主要处理：

- `content_block_start`；
- `content_block_delta`；
- `input_json_delta`；
- `content_block_stop`；
- `message_delta`。

### 9.4 工具循环

`AiClient.ExecuteConversationAsync` 是核心工具循环。

简化流程：

```text
for iteration = 0; ; iteration++
  ├─ 如上下文过大，CompressHistory
  ├─ StreamRichEventsAsync(messages)
  │   ├─ content → 立即 yield 给 UI
  │   ├─ tool_call_start / tool_call_delta → 累积 ToolCallData
  │   └─ finish → 记录 finish_reason
  ├─ 如果没有 tool call → yield break
  ├─ 添加 assistant(tool_calls) 消息
  ├─ 对每个 tool call:
  │   ├─ ParseArguments()
  │   ├─ executeTool(toolName,args)
  │   ├─ ask_user sentinel → yield sentinel + yield break
  │   ├─ start_subtask sentinel → CompressHistory 后去掉 sentinel
  │   └─ 添加 tool result 消息
  ├─ 应用 set_task_iterations 的软提醒阈值
  ├─ stale result >= 3 → 强制终止
  ├─ 接近软阈值 → 注入系统效率提醒
  └─ 下一轮
```

停止条件：

- 模型返回纯文本；
- `ask_user` 暂停；
- 用户取消；
- AI API 报错；
- 连续 3 轮 legacy `skill_extract` / `skill_query` 返回短或无效结果。

重要：这里没有硬性的最大工具轮数；`maxIterations` 是提醒阈值，不是强制上限。

### 9.5 上下文压缩

压缩触发：

- 估算对话大小超过 `150_000` bytes；
- 或距离上次压缩 20 轮以上且消息数超过 40；
- 或 `start_subtask` 返回压缩 sentinel。

压缩目标：

- 尽量压到 `100_000` bytes 以下；
- 保留最近约 20 条消息；
- 尽量从安全边界压缩，避免破坏 assistant/tool 配对；
- 旧消息替换为摘要。

---

## 10. `ask_user` 人机协作暂停机制

`ask_user` 是当前 Demo 中最重要的人机协作能力。它允许 AI 在任务中途暂停，询问用户，再继续原工具循环。

### 10.1 schema 概念

AI 可传：

- `question`：要问用户的问题；
- `question_type`：`confirmation` / `multiple_choice` / `open_ended`；
- `options`：多选项；
- `context_summary`：当前背景；
- `default_option`：推荐选项。

### 10.2 暂停流程

```text
AI tool_call: ask_user(...)
  ↓
ChatViewModel.ExecuteAiToolAsync
  ├─ 生成 UserQuestionInfo
  └─ 返回 __ASK_USER_PAUSED__:{json}
  ↓
AiClient.ExecuteConversationAsync
  ├─ yield sentinel
  └─ yield break
  ↓
ChatViewModel.SendAsync / ContinueToolLoopAsync
  ├─ 解析 UserQuestionInfo
  ├─ IsAwaitingUserInput = true
  ├─ PendingAskUserQuestion = question
  ├─ 保存 _pendingMessages / _pendingAiMsg / _pendingToolCallId
  └─ UI 显示问题卡片
```

### 10.3 恢复流程

```text
用户点击选项或跳过
  ↓
RespondToQuestionAsync(userResponse)
  ├─ 防重入 _isResponding
  ├─ __skip__ 转成“用户选择跳过...”
  ├─ 追加 MessageRole.Tool / ToolName=ask_user
  └─ ContinueToolLoopAsync(_pendingMessages, _pendingAiMsg)
      └─ 继续 ExecuteConversationAsync
```

该机制保留原来的消息列表，因此模型能看到自己之前的工具调用、用户回答和后续工具结果。

---

## 11. 任务拆分与 Todo UI

当前 `ContextBuilder` 的系统提示词要求模型：

1. 接到任务后先拆成子任务；
2. 用 `update_todo` 一次性写入完整任务清单；
3. 每个子任务开始前调用 `start_subtask`；
4. 成功后调用 `finish_subtask(status="completed")`；
5. 阻塞时调用 `finish_subtask(status="blocked")` 并说明用户需要处理什么。

`ChatViewModel.TodoItems` 是一个 `ObservableCollection<AiTodoItem>`，由 `update_todo`、`start_subtask`、`finish_subtask` 更新，用于 AI 面板显示实时进度。

`start_subtask` 还会返回特殊前缀：

```text
__SUBTASK_CONTEXT_COMPRESSED__:
```

`AiClient` 收到后会先压缩上下文，再把普通工具结果写回消息历史。

---

## 12. 数据存储设计

### 12.1 AI 设置

当前设置不是 SQLite，而是 JSON 文件：

```text
<AppDomain.CurrentDomain.BaseDirectory>/ai_settings.json
```

支持两种格式：

1. 新格式：`AiSettingsStore`，包含多 profile；
2. 旧格式：单个 `AiSettings`，加载时自动迁移为多 profile。

### 12.2 对话存储

对话存储目录：

```text
%LocalAppData%/SmartAI-Browser-Demo/conversations/
```

每个会话一个 JSON 文件：

```json
{
  "id": "conversation-id",
  "savedAt": "...",
  "messages": [ ... ]
}
```

`ConversationService` 支持：

- `ListConversations()`：扫描目录生成摘要；
- `SaveConversation(id, messages)`；
- `LoadConversation(id)`；
- `DeleteConversation(id)`。

### 12.3 WebView2 Profile

WebView2 用户数据目录：

```text
%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/
```

该目录由 WebView2 管理，保存 cookie、缓存、local storage 等。

### 12.4 下载状态

下载记录当前是内存态：

```text
DownloadManager.Items : ObservableCollection<DownloadItem>
```

WebView2 `DownloadStarting` 创建 `DownloadItem`，下载进度事件更新状态。`DownloadsWindow` 显示该集合。

---

## 13. 旧 MCP / 外部 Chrome 路径

代码中仍保留一套 Playwright MCP / 外部 Chrome 方案：

- `Services/BrowserHost/ChromeProcessManager.cs`
- `Services/Mcp/JsonRpcClient.cs`
- `Services/Mcp/PlaywrightMcpClient.cs`
- `Services/Skills/SkillSystemIntegration.cs`
- `Services/Skills/McpSkillDataProvider.cs`
- `Services/Skills/McpSkillExecutor.cs`
- `Tools/playwright-mcp/playwright-mcp-0.0.75/`

这套系统定义：

- 13 个 atomic skills；
- 7 个 composite skills；
- 6 个 strategy skills。

但是当前启动流没有初始化它：

- `MainWindow.OnLoaded` 不调用 `SetChromeCdpEndpoint`；
- `SkillSystemIntegration.IsInitialized` 通常为 false；
- `ContextBuilder.ImportSkillsFromRegistry` 不会在当前路径执行；
- AI 工具来自 WebView2 Automation Router。

因此：

- 写当前功能时，不要把 MCP 当作运行时主路径；
- 如果要恢复 MCP，需要重新设计启动流、外部 Chrome 管理、CDP endpoint 生命周期以及与 WebView2 当前路径的关系；
- `ChromeProcessManager` 的存在不代表当前 App 会启动 Chrome。

---

## 14. 死代码与遗留模型

### 14.1 `WebView2AutomationBridge.cs`

该文件顶部为：

```csharp
#if false
```

整文件不参与编译。不要在新实现中引用或修改它作为当前功能基础。

### 14.2 两套 SkillDefinition

当前代码中存在两套技能定义：

1. `Models/SkillDefinition.cs` 等 record 类型：旧模型，非当前运行路径；
2. `Services/Skills/SkillModels.cs` 等 class 类型：MCP 技能系统使用。

`ChatViewModel.cs` 用 alias 区分：

```csharp
using SkillDef = BrowserDemo.Services.Skills.SkillDefinition;
using SkillExecResult = BrowserDemo.Services.Skills.SkillExecutionResult;
using SkillStat = BrowserDemo.Services.Skills.SkillStatus;
using CompositeSkill = BrowserDemo.Services.Skills.CompositeSkillDefinition;
```

但由于 MCP 技能系统当前未初始化，主运行路径依然是 `BrowserAutomationToolRouter`。

---

## 15. 错误处理与可靠性策略

### 15.1 WebView2 初始化失败

`MainWindow.OnLoaded` 捕获异常：

- 写入日志；
- 更新状态文本；
- 弹出 MessageBox。

当前没有旧文档中的“自动 fallback 到 WebView2”，因为 WebView2 本身就是主路径。

### 15.2 浏览器工具失败重试

`ChatViewModel.ExecuteAiToolAsync` 对 WebView2 自动化工具维护 `_toolRetryTracker`：

- 第 1 次失败：建议重试；
- 第 2 次失败：建议重新 `browser_snapshot`，换新的 `element_id`；
- 第 3 次起，如果参数里有 `element_id`，直接刷新快照并要求模型不要继续用旧 id；
- 第 4 次仍失败：建议 `ask_user` 向用户求助。

### 15.3 截图控制

`browser_screenshot` 必须提供明确 `reason`。Router 会拒绝不充分的截图原因，避免模型频繁把大截图数据引入上下文。

当前截图工具成功时只返回摘要，例如 base64 长度，不返回完整 base64。

### 15.4 UI 卡顿控制

`ChatViewModel.SendAsync` / `ContinueToolLoopAsync` 根据当前内容长度动态调整 UI 更新频率：

- 短内容更新更频繁；
- 长内容更新更慢，减轻 Markdown 渲染压力。

`AiClient.ExecuteConversationAsync` 每 3 轮工具循环 `Task.Yield()`，减少 UI 长时间无响应风险。

### 15.5 上下文过大控制

通过：

- 150 KB 自动压缩；
- 20 轮 / 40 消息兜底压缩；
- 子任务开始强制压缩；
- 压缩摘要和保留近期消息。

---

## 16. 添加新浏览器工具的推荐流程

如果要给当前 Demo 增加新的 AI 浏览器能力，按当前主路径修改：

1. **实现底层能力**  
   在 `BrowserAutomationService` 中添加方法；如果需要页面 DOM 操作，在 `AutomationScripts` 中添加 JS 生成方法。

2. **注册工具 schema**  
   在 `BrowserAutomationToolRouter.GetToolDefinitions()` 中添加 `ToolDefinition`。

3. **添加 dispatch case**  
   在 `BrowserAutomationToolRouter.InvokeAsync` 的 switch 中添加新工具名到服务方法的映射。

4. **更新提示词**  
   如模型需要特殊使用规则，在 `ContextBuilder.AppendBehaviorGuidelines` 或能力说明中补充。

5. **验证运行**  
   使用：

   ```bash
   cd C:/CodeSpace/Objects/Browser/Demo
   dotnet build BrowserDemo/BrowserDemo.csproj
   dotnet run --project BrowserDemo/BrowserDemo.csproj
   ```

6. **不要误改旧 MCP 路径**  
   除非需求明确要恢复/替换为 Playwright MCP，否则不要把新工具只加到 `McpSkillDataProvider`。

---

## 17. 当前功能边界

已实现或基本可用：

- WPF 主窗口；
- WebView2 多标签；
- 地址栏导航；
- 后退/前进/刷新；
- AI 独立副窗口；
- AI Provider 配置；
- OpenAI-compatible / Anthropic-native SSE；
- Function Calling / Tool Use；
- WebView2 自动化浏览器工具；
- 页面观察与元素 id 交互；
- ask_user 暂停恢复；
- AI todo list；
- 对话 JSON 持久化；
- WebView2 下载进度窗口。

部分实现 / 遗留 / 非主路径：

- Playwright MCP 技能系统；
- 外部 Chrome CDP 嵌入；
- ADB SMS 服务；
- 旧 WebView2AutomationBridge。

未实现为完整产品能力：

- SQLite 历史/书签数据库；
- 完整书签管理；
- 完整历史记录 UI；
- 隐私模式；
- 密码管理；
- 插件市场；
- 安装器/自动更新；
- 单元测试项目。

---

## 18. 构建与验证标准

当前最小验证：

```bash
cd C:/CodeSpace/Objects/Browser/Demo
dotnet build BrowserDemo/BrowserDemo.csproj
```

人工验证建议：

1. 启动应用；
2. WebView2 内容区成功加载；
3. 地址栏输入 URL 可导航；
4. 打开 AI 面板；
5. 配置模型 API Key；
6. 让 AI 执行简单浏览器任务，例如：
   - “打开 https://www.bing.com”；
   - “观察当前页面”；
   - “搜索 hello world”；
7. 检查 AI 是否先调用 `observe_browser` / `browser_snapshot`，再用整数 `element_id` 操作页面；
8. 检查对话是否保存到 `%LocalAppData%/SmartAI-Browser-Demo/conversations/`。

---

## 19. 维护原则

- 当前主线是 **WebView2 内嵌 + BrowserAutomationService**。
- 不要把旧文档中的“外部 Chrome + Playwright MCP”描述为当前运行事实。
- 不要把 `WebView2AutomationBridge.cs` 作为可用代码。
- 保持工具结果紧凑，避免污染 LLM 上下文。
- 对 WebView2 的所有访问必须尊重 UI 线程要求。
- 自动化操作默认串行，除非重新设计并发模型。
- 任何模型/工具 schema 修改都要同时考虑 OpenAI-compatible 和 Anthropic-native 两种 API 格式。
- 修改 AI 工具循环时，要保持 assistant/tool 消息配对合法，避免破坏后续请求格式。
