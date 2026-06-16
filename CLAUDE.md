# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## Project Overview

**SmartAI Browser Demo** is a Windows intelligent browser prototype built with **C# / .NET 8 / WPF**. The app runs an embedded **WebView2** browser host and exposes browser-control tools to the AI assistant **Bermain（板儿面）** through hand-written function calling schemas. A task state machine forces the AI to break complex tasks into ordered subtasks and execute them sequentially.

**BrowserSkills** is a standalone C# Class Library (`BrowserSkills/`) extracted from the demo project. It packages the full browser automation capability chain — core services, models, skills, strategies, and intelligence layers — as a reusable `net8.0-windows` library depending only on `Microsoft.Web.WebView2`. The Demo project currently keeps its own copies of the services; BrowserSkills exists as a reference extraction for future reuse or decoupling.

The important current reality is:

- The active browser host is `BrowserHostService` + WebView2 controls embedded directly in the WPF window.
- The active AI browser automation path is `BrowserAutomationService` + `BrowserAutomationToolRouter`.
- A **TaskStateMachine** enforces a Planning → Executing → Complete lifecycle for subtask-based task execution.
- An **AgentEventSelfHandler** performs autonomous dead-end detection: stale element reuse, repeated navigation failures, no-progress loops, and repeated same-action blocking.
- The older Playwright MCP / external Chrome CDP path still exists in code (`SkillSystemIntegration`, `PlaywrightMcpClient`, `ChromeProcessManager`) but is not used by the current startup flow because `MainWindow.OnLoaded` no longer calls `ChatViewModel.SetChromeCdpEndpoint`.
- `WebView2AutomationBridge.cs` is fully disabled with `#if false` and must be treated as dead code.
- `AiClient.ExecuteConversationAsync` has multiple anti-loop mechanisms: hard iteration cap (80), subtask gate, planning gate, AI repetition detection, stale result detector, context compression, and budget progressive warnings (50%/75%/90%/95%).
- New self-detection capabilities: DOM text hash page-stalled detection, consecutive action failure replan trigger, and exploration step limit (disconnected from subtasks).

## Build, Run, and Verification

Use the project under `Demo/BrowserDemo/`. There is no `.sln` file and no C# test project in the current repository.

```bash
# From C:\CodeSpace\Objects\Browser\Demo

dotnet build BrowserDemo/BrowserDemo.csproj
dotnet run --project BrowserDemo/BrowserDemo.csproj
dotnet clean BrowserDemo/BrowserDemo.csproj

# Optional if dotnet-format is installed
dotnet format BrowserDemo/BrowserDemo.csproj --verify-no-changes
dotnet format BrowserDemo/BrowserDemo.csproj
```

Requirements:

- Windows 10/11
- .NET 8 SDK with Windows desktop workload support
- WebView2 Runtime / Edge WebView2 Evergreen Runtime
- AI API key for the selected provider if using chat/model features
- Node.js and bundled Playwright MCP files are only needed if intentionally reviving or testing the legacy MCP path

## Current Project Structure

```text
Demo/BrowserDemo/
├── BrowserDemo.csproj                  # net8.0-windows WPF; NuGet dep: Microsoft.Web.WebView2
├── App.xaml / App.xaml.cs               # App entry, console/log lifecycle setup
├── MainWindow.xaml / .cs                # WPF shell, tabs/address bar, WebView2 host setup, AI side window
├── AssemblyInfo.cs / Converters.cs / StringExtensions.cs
├── Models/
│   ├── BrowserViewModel.cs             # Tabs, navigation commands, RelayCommand
│   ├── TabInfo.cs                      # Tab metadata; Guid Id is the app-level tab identity
│   ├── DownloadItem.cs                 # Download UI model
│   ├── ChatMessage.cs                  # Conversation roles/content/tool call data
│   ├── ToolCallData.cs                 # Streaming tool-call accumulator + AiStreamEvent
│   ├── ToolDefinition.cs               # Tool schema conversion for OpenAI + Anthropic
│   ├── AiSettings.cs / AiSettingsStore.cs # Multi-profile provider/key/model settings
│   ├── ProviderInfo.cs                 # ProviderManager registry
│   ├── AiTodoItem.cs                   # Realtime AI task-list UI model
│   └── SkillDefinition*.cs / SkillStep.cs / SkillExecutionResult.cs
│       # Legacy record-based skill model; not the active skill system
├── ViewModels/
│   └── ChatViewModel.cs                # AI chat, tool loop, ask_user, task state machine integration
├── Views/
│   ├── AiChatPanel.xaml / .cs          # Chat panel UI
│   ├── AiSecondaryWindow.xaml / .cs    # Floating AI window owned by MainWindow
│   ├── AiModelSelectionDialog.xaml / .cs # Current AI model/profile settings UI
│   ├── AiSettingsDialog.xaml / .cs     # Older settings dialog still present
│   └── DownloadsWindow.xaml / .cs      # Download list window
├── Services/
│   ├── Logger.cs                       # Static logger: console + file + in-memory buffer + Trace blocks
│   ├── IAiClient.cs / AiClient.cs       # Hand-written OpenAI-compatible + Anthropic SSE clients and tool loop
│   ├── ContextBuilder.cs               # System prompt + dynamic context + tool schemas + TaskStateMachine link
│   ├── ConversationService.cs          # JSON conversation persistence
│   ├── DownloadManager.cs              # Static observable download list
│   ├── AgentEventSelfHandler.cs        # Autonomous dead-end detection and self-correction during tool loops
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs       # ACTIVE browser host: WebView2 environment, tabs, events, downloads
│   │   │   └─ Automation property       # Links to BrowserAutomationService
│   │   └── ChromeProcessManager.cs     # Legacy external Chromium/CDP host; not used by MainWindow current flow
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs # ACTIVE automation: WebView2 API + JS injection + UI-thread dispatch
│   │   ├── BrowserAutomationToolRouter.cs # ACTIVE function-call router for browser_* tools
│   │   ├── AutomationScripts.cs        # JavaScript snippets for snapshots/click/type/etc.
│   │   │   └─ Playwright-style snapshot  # Visibility filtering, importance scoring, simplified element fields
│   │   ├── AdbService.cs               # Android SMS helper; not exposed as an AI tool
│   │   └── WebView2AutomationBridge.cs # Disabled with #if false; dead code
│   ├── Mcp/
│   │   ├── JsonRpcClient.cs            # Legacy MCP stdio JSON-RPC client
│   │   ├── PlaywrightMcpClient.cs      # Legacy Playwright MCP wrapper
│   │   └── Models/McpMessage.cs
│   └── Skills/
│       ├── SkillModels.cs              # Class-based skill model for legacy MCP skill system
│       ├── SkillRegistry.cs
│       ├── SkillSystemIntegration.cs   # Legacy Playwright MCP + skill registration
│       ├── McpSkillDataProvider.cs     # 13 atomic + 7 composite + 6 strategy skill definitions
│       ├── McpSkillExecutor.cs
│       ├── SkillExecutionContext.cs
│       └── Strategy/                   # IStrategyHandler + 6 strategy implementations
└── Converters/
    └── MarkdownToFlowDocumentConverter.cs

BrowserSkills/                         # Standalone extracted browser automation library
├── BrowserSkills.csproj               # net8.0-windows, depends on Microsoft.Web.WebView2
├── README.md
├── Core/
│   ├── BrowserAutomationService.cs    # WebView2 automation engine
│   ├── BrowserAutomationToolRouter.cs # AI tool router (schema + dispatch)
│   ├── AutomationScripts.cs           # JS script generator
│   └── ILogger.cs                     # Logging interface
├── Models/
│   ├── ToolDefinition.cs              # AI tool definitions
│   ├── AiTodoItem.cs                  # Task list items
│   ├── ChatMessage.cs                 # Conversation messages
│   ├── ToolCallData.cs                # Tool call data
│   ├── UserQuestionInfo.cs            # ask_user question info
│   ├── MessageRole.cs                 # Message role enum
│   ├── AssistantResponseSections.cs   # Response sections
│   ├── AssistantResponseParser.cs     # Response parser
│   └── StringExtensions.cs           # String utilities
├── Skills/
│   ├── SkillModels.cs                 # Skill model base classes
│   ├── SkillRegistry.cs               # Skill registry
│   ├── McpSkillDataProvider.cs        # Skill data definitions
│   └── SkillExecutionContext.cs      # Execution context
├── Strategy/
│   ├── IStrategyHandler.cs            # Strategy interface
│   ├── NavigationStrategy.cs          # Navigation strategy
│   ├── LocateStrategy.cs              # Locate strategy
│   ├── RetryStrategy.cs               # Retry strategy
│   ├── ContextStrategy.cs             # Context strategy
│   ├── RecoveryStrategy.cs            # Recovery strategy
│   └── PrivacyStrategy.cs             # Privacy strategy
└── Intelligence/
    ├── ContextBuilder.cs              # System prompt builder
    ├── TaskStateMachine.cs            # Subtask state machine
    └── AgentEventSelfHandler.cs       # Self-detector

Tools/
├── playwright-mcp/playwright-mcp-0.0.75/ # Bundled legacy Playwright MCP server
└── platform-tools/                       # Android ADB binaries
```

## Current Architecture Facts

| Aspect | Current implementation |
|--------|------------------------|
| Target | `net8.0-windows` WPF |
| UI framework | Hand-written WPF dark UI; no DI container |
| Browser rendering | Embedded WebView2 controls managed by `BrowserHostService` |
| Browser profile | `%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/` |
| Active automation | `BrowserAutomationService` runs WebView2 operations on the UI dispatcher, serialized with `SemaphoreSlim(1,1)` |
| AI tool exposure | `BrowserAutomationToolRouter.GetToolDefinitions()` registers `browser_*` tools into `ContextBuilder` |
| AI client | Hand-written `HttpClient` streaming SSE for OpenAI-compatible and Anthropic-native APIs |
| Task state machine | `TaskStateMachine` (Planning → Executing → Complete) forces ordered subtask execution |
| Agent self-handling | `AgentEventSelfHandler` detects dead-ends: stale elements, repeated failures, no-progress loops |
| AI client safety | Hard iteration cap (80), context compression triggers at 50KB, tool result truncation at 2000 chars, AI repetition detection |
| Budget warnings | Progressive alerts at 50%/75%/90%/95% iteration consumption |
| Page stalled detection | DOM text hash tracking via `RecordDomTextHash()` — alerts at 2 consecutive unchanged, terminates at 4 |
| Consecutive failure replan | `RecordActionOutcome()` — warns at 3 failures, requires replanning |
| Exploration limit | `RecordStepWithSubtask()` — warns after 5 steps without subtask association |
| Providers | `ProviderManager` registers OpenAI, Anthropic, Google, DeepSeek, xAI, Groq, Cerebras, Mistral, Together, Fireworks, OpenRouter, Alibaba, Zhipu, Moonshot, SiliconFlow, Ollama, DeepInfra |
| Settings storage | `ai_settings.json` next to the executable (`AppDomain.CurrentDomain.BaseDirectory`) with multi-profile support via `AiSettingsStore` |
| Conversations | JSON files under `%LocalAppData%/SmartAI-Browser-Demo/conversations/` |
| Downloads | WebView2 `DownloadStarting` events update static `DownloadManager.Items` for `DownloadsWindow` |
| Legacy MCP | Present but not active in current startup unless `SetChromeCdpEndpoint` is called manually |
| Tests | None currently |

## Active Startup Flow

```text
App startup
  ↓
MainWindow constructor
  ├─ creates BrowserViewModel
  ├─ wires navigation/tab/download events
  ├─ wires ChatViewModel settings + AI panel events
  └─ registers Loaded/Closing handlers
  ↓
MainWindow.OnLoaded
  ├─ create BrowserHostService(Dispatcher, ContentArea)
  │   └─ UserDataFolder = %LocalAppData%/SmartAI-Browser-Demo/webview2-profile
  ├─ await BrowserHostService.InitializeAsync()
  │   └─ creates shared CoreWebView2Environment
  ├─ create BrowserAutomationService and Initialize(Dispatcher)
  ├─ assign _browserHost.Automation = _automation
  ├─ WireBrowserHostEvents()
  ├─ EnsureTabWebViewAsync(...) for existing BrowserViewModel tabs
  │   ├─ BrowserHostService.CreateTabForAsync(tab, tab.Url)
  │   └─ BrowserAutomationService.BindWebView(tab.Id, webView)
  ├─ ActivateTabWebView(activeTab.Id)
  ├─ ChatViewModel.AttachAutomationRouter(new BrowserAutomationToolRouter(_automation))
  │   ├─ registers browser_* tools
  │   ├─ registers observe_browser
  │   ├─ registers ask_user
  │   ├─ registers set_task_iterations
  │   ├─ registers update_todo (linked to TaskStateMachine)
  │   ├─ registers start_subtask / finish_subtask (linked to TaskStateMachine)
  │   └─ ContextBuilder.TaskStateMachine = ChatViewModel._taskStateMachine
  └─ status: browser embedded, AI browser tools enabled, state machine active
```

`MainWindow.OnLoaded` explicitly does **not** call `_vm.Chat.SetChromeCdpEndpoint(...)` in the current implementation. Do not describe external Chrome + Playwright MCP as the active path unless you are intentionally documenting legacy code.

## Active Browser Host and Automation Flow

### BrowserHostService

`Services/BrowserHost/BrowserHostService.cs` owns the WebView2 lifecycle:

- Creates one shared `CoreWebView2Environment`.
- Creates one `WebView2` control per `TabInfo.Id` and adds it to the WPF `ContentArea` panel.
- Switches active tabs by toggling each WebView2 control's `Visibility`.
- Binds WebView2 events: `NavigationStarting`, `NavigationCompleted`, `DocumentTitleChanged`, `SourceChanged`, `DownloadStarting`, `NewWindowRequested`, `ProcessFailed`, script dialog handling.
- Converts popup/new-window requests into app tabs via `NewTabRequested`.
- Records download progress through `DownloadManager`.
- Exposes an `Automation` property that links to `BrowserAutomationService`.

### BrowserAutomationService

`Services/Automation/BrowserAutomationService.cs` is the active automation layer:

- Called from the AI/tool loop on background threads; switches to WPF UI thread via `Dispatcher.InvokeAsync`.
- Serializes operations with `SemaphoreSlim(1,1)`.
- Targets the current active tab (`SwitchToTab(Guid)`).
- Exposes: navigation, back/forward/reload, click/type/hover/select, scroll, key press, screenshot, JS evaluation, wait, wait-for-text, form filling, DOM text hash extraction.
- Element tools use integer `element_id` from `browser_snapshot`.
- `GetDomTextHashAsync()` — returns a hash of page text content for page-stalled detection (used by `AgentEventSelfHandler`).

### AutomationScripts — JavaScript Snapshot Engine

`Services/Automation/AutomationScripts.cs` contains the JS injected into pages for accessibility snapshots:

- **Playwright-style visibility filtering**: Only elements that are truly visible and receive pointer events are included. Filters by `display:none`, `visibility:hidden`, `aria-hidden`, `[hidden]`, `role=presentation`.
- **Importance scoring**: Interactive elements are scored by tag priority (button > a > input …), label length, aria-label presence, href type. Key elements (CTAs, buttons) appear first.
- **Simplified element fields**: Removed rect/visible/css_selector/disabled/readonly from snapshot output to reduce context pollution. Retains: id, tag, text, role, type, name, href, aria_label, placeholder, value.
- **Text field shortened**: text field limited to 100 chars (from 200), value to 50 chars (from 100).
- **No element limit**: `MaxSnapshotElements` changed from 1000 to 0 (unlimited), relying on importance scoring and context compression instead.

### BrowserAutomationToolRouter

`Services/Automation/BrowserAutomationToolRouter.cs` converts function-calling arguments to automation calls and JSON-formats results.

Currently registered browser tools:

- `browser_navigate`, `browser_back`, `browser_forward`, `browser_reload`
- `browser_snapshot`, `browser_click`, `browser_type`, `browser_hover`, `browser_select_option`
- `browser_scroll`, `browser_press_key`, `browser_screenshot`, `browser_js`
- `browser_wait`, `browser_wait_for`, `browser_fill_form`, `browser_switch_tab`

### AgentEventSelfHandler — Autonomous Dead-End Detection

`Services/AgentEventSelfHandler.cs` monitors tool execution in real-time:

- **Stale element reuse detection**: Tracks element IDs known to be invalid; blocks after 2 reuses. After 3 total stale element terminations, the tool loop is forcibly terminated.
- **Repeated navigation failure**: Blocks URLs that fail navigation 2+ times, hosts that fail 4+ times.
- **Same-action repetition**: Blocks when the same tool+parameter combo produces the same result 3+ times.
- **No-progress observe/wait loop**: Detects when passive tools (observe/snapshot/wait/reload) produce identical results across 4+ consecutive calls.
- **Ask_user recommendation tracking**: When tool results repeatedly suggest ask_user, injects system prompts and eventually terminates.
- **Dead-end score accumulation**: Independent scoring; at score 4, the tool loop terminates.
- **DOM text hash page-stalled detection** (new): Tracks `dom_text_hash` from snapshots; warns at 2 consecutive unchanged, terminates at 4.
- **Consecutive action failure replan trigger** (new): `RecordActionOutcome(false)` increments counter; at 3 warns to replan via `update_todo`.
- **Exploration step limit** (new): `RecordStepWithSubtask(false)` tracks steps without subtask association; warns at 5 disconnected steps.
- Injects `[agent_event code=... severity=...]` system messages before each blocked/terminated action.

### TaskStateMachine — Forced Subtask Execution

`Services/TaskStateMachine.cs` enforces ordered subtask lifecycle:

- **States**: `Planning` (waiting for update_todo) → `Executing` (running subtasks) → `Complete` (all done).
- **Rules**:
  1. `update_todo` only allowed in Planning state.
  2. `start_subtask` / `finish_subtask` only on `ActiveSubtaskId`.
  3. Subtasks execute in list order — no skipping.
  4. `finish_subtask("completed")` auto-promotes the next pending subtask.
  5. `update_todo` during execution is rejected.
- **Compression hints**: `start_subtask` returns `CompressionLevel.Standard`; `finish_subtask("completed")` returns `CompressionLevel.Max`.
- **Result**: `TransitionResult.Valid + Compression + TodoItems` for the AI client to act on.

## AI Client and Tool Loop

`Services/AiClient.cs` supports two request formats:

1. **OpenAI-compatible chat completions**: Bearer auth, `chat/completions` streaming with `delta.tool_calls`.
2. **Anthropic native messages**: `x-api-key`, `anthropic-version`, top-level `system`, streaming `content_block_*` / `message_delta`.

### ExecuteConversationAsync — Safety Layers

`ChatViewModel.ExecuteConversationAsync` is an unbounded `for` loop with multiple safety mechanisms:

**Hard limits**:
- `MaxHardIterations = 80` — absolute cap, no exceptions.
- `MaxToolResultChars = 2000` (tail: 500) — tool results truncated to prevent context pollution.
- Context compression triggers at 50KB, targets 40KB, subtask completion targets 30KB.

**Budget progressive warnings** (new): At 50%/75%/90%/95% of hard iteration cap, injects `[agent_event code=budget_warning]` system messages to prompt the AI to consolidate and finish.

**Subtask gate**: When subtasks exist but AI returns text without tool calls, injects a system reminder. After 5 consecutive misses, terminates the request.

**Planning gate**: Uses `TaskStateMachine` state — forces `update_todo` in Planning, `start_subtask` in Executing with no active subtask. Falls back to old message-scan logic if state machine is null.

**AI repetition detection**: Fingerprints last 3 AI text replies (normalized hash); 2 consecutive identical fingerprints (>30 chars) → terminates.

**browser_js null detection**: Tracks consecutive `browser_js` calls returning `{"data":"null"}` or `{"data":null}`. After 2 consecutive nulls, injects a strategy-change hint.

**Stale result detector**: Legacy `skill_extract`/`skill_query` probe — 3 consecutive identical short results → terminate.

**Tool retry in ChatViewModel**: Up to 4 attempts with increasing delay (0ms → 1s → 2s → 3s). Element tools auto-refresh snapshot on 3rd attempt.

**Flow**:

```text
SendAsync()
  ├─ no tools registered → AiClient.StreamMessageAsync()
  └─ tools registered    → AiClient.ExecuteConversationAsync(..., ExecuteAiToolAsync)

ExecuteConversationAsync loop stops when:
  ├─ AI returns text with no tool calls (and no gates force more)
  ├─ ask_user returns __ASK_USER_PAUSED__ sentinel
  ├─ cancellation token cancelled
  ├─ hard iteration cap (80) reached
  ├─ 3 consecutive stale probe results
  ├─ AI repetition detected (2+ identical text fingerprints)
  ├─ page_stalled_fatal (DOM text hash unchanged 4 consecutive)
  ├─ exploration_limit (5 steps without subtask association)
  └─ API/streaming error
```

## ask_user Pause/Resume Pattern

`ask_user` is handled directly in `ChatViewModel.ExecuteAiToolAsync`, not by the skill system.

Flow:

1. AI calls `ask_user(question, question_type, options?, context_summary?, default_option?)`.
2. `ExecuteAiToolAsync` returns `__ASK_USER_PAUSED__:{UserQuestionInfo JSON}`.
3. `AiClient.ExecuteConversationAsync` yields that sentinel and stops the current loop.
4. `ChatViewModel.SendAsync` or `ContinueToolLoopAsync` stores:
   - `_pendingMessages`
   - `_pendingAiMsg`
   - `_pendingToolCallId`
   - `PendingAskUserQuestion`
5. UI displays the question and sets `IsAwaitingUserInput = true`.
6. `RespondToQuestionAsync(option)` appends a `MessageRole.Tool` message for `ask_user` and resumes with `ContinueToolLoopAsync`.
7. `__skip__` becomes: "用户选择跳过，请基于当前已有信息自行决定最佳方案并继续执行。"

## Skill System Status

Two skill-model families:

1. `Models/SkillDefinition.cs` and siblings — legacy record-based models.
2. `Services/Skills/SkillModels.cs` and related — class-based MCP skill system (13 atomic + 7 composite + 6 strategy skills).

In the **current WebView2 startup path**, this system remains uninitialized because `MainWindow` does not call `ChatViewModel.SetChromeCdpEndpoint`. Browser automation comes from `BrowserAutomationToolRouter`.

Only work on `SkillSystemIntegration`, `PlaywrightMcpClient`, `McpSkillExecutor`, or `ChromeProcessManager` when the task explicitly targets the legacy MCP/CDP path.

## Persistence and Local Data

- AI settings: `ai_settings.json` next to the built executable.
- Conversation files: `%LocalAppData%/SmartAI-Browser-Demo/conversations/*.json`.
- WebView2 browser profile: `%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/`.
- Legacy external Chrome profile: `%LocalAppData%/SmartAI-Browser-Demo/chrome-profile/` (only for `ChromeProcessManager`).
- Logs: `Log/` under the project/runtime working area.
- Downloads: tracked in-memory through `DownloadManager.Items`.

## Codegraph MCP Retrieval

`.claude/settings.json` is configured for codegraph MCP. For project understanding and code lookup, prefer codegraph before raw scanning:

- Architecture / data flow / component relationships → `mcp__codegraph__codegraph_context`
- Symbol or file lookup → `mcp__codegraph__codegraph_search`
- Call-chain tracing → `mcp__codegraph__codegraph_trace`
- Single symbol source → `mcp__codegraph__codegraph_node`
- Several related symbols/source survey → `mcp__codegraph__codegraph_explore`
- Use `Glob` / `Grep` / `Read` only when codegraph does not cover the needed text or non-code resource.

## Coding Guidelines for This Repo

- Match the existing style: direct WPF event wiring, manual `INotifyPropertyChanged`, no DI container.
- Use existing `Logger` calls and `Logger.Trace(...)` blocks for lifecycle/debug points.
- Any WebView2 access from background code must go through the UI dispatcher pattern used in `BrowserAutomationService`.
- Keep automation operations serialized unless there is a deliberate design change.
- Preserve `Guid` tab identity from `TabInfo.Id` when binding WebView2 controls.
- For AI tools, return compact JSON/text results. Avoid returning large base64 screenshots into the LLM context.
- When adding a current browser tool:
  1. implement the operation in `BrowserAutomationService` and/or `AutomationScripts`;
  2. add the function schema and dispatch case in `BrowserAutomationToolRouter`;
  3. ensure `ChatViewModel.AttachAutomationRouter` registers it via `router.GetToolDefinitions()`;
  4. update `ContextBuilder` guidance if the model needs tool-use rules.
- When changing AI provider behavior, update both OpenAI-compatible and Anthropic-native paths where applicable.
- Do not revive or depend on `WebView2AutomationBridge.cs`; it is `#if false` dead code.
- Do not assume `ChromeProcessManager` or Playwright MCP is active in the running app.
- Changes to `AiClient` tool loop safety must preserve the ordering: hard cap → budget warning → compression → subtask gate → planning gate → tool execution → stale result detection → repetition check → page-stalled/failure/exploration self-detection.
- When modifying `AgentEventSelfHandler`, ensure all new detection logic returns `ToolSelfHandlingDecision.Block` or adds events via `AddEvent`.
- **BrowserSkills library** (`BrowserSkills/`) is a standalone extracted copy of the automation chain. Changes to it do NOT affect the Demo project — the Demo keeps its own copies. Use BrowserSkills for documentation/reference or as a reusable library basis.

## 项目理解准则

每次执行任务前，先执行以下步骤以更新对项目的理解：

1. **刷新 codegraph 索引**：在 `.claude/settings.json` 已配置 codegraph MCP 的前提下，优先使用 `mcp__codegraph__codegraph_context` 获取项目整体上下文，再通过 `mcp__codegraph__codegraph_trace` / `mcp__codegraph__codegraph_explore` 追溯具体流程，确保对 `.codegraph` 索引下的符号和调用关系有最新理解后再进行修改。
2. **查阅 Help 目录下的理解文档**：`Help/` 目录下包含对项目功能说明（`FunctionHelp.md`、`EffectHelp.md`）和逐项修复记录（`Help/Debugg/`）的详细文档。修改功能前应先阅读相关文件，了解历史背景、已知问题和修复脉络，避免重复踩坑。

## Common Debugging Notes

- **AI says no API key**: configure a model profile through `AiModelSelectionDialog`; settings save to `ai_settings.json` beside the executable.
- **AI has no browser tools**: check `MainWindow.OnLoaded` completed, `AttachAutomationRouter` ran, and `ContextBuilder.RegisteredTools.Count > 0`.
- **Browser tool says element id is invalid/stale**: call `browser_snapshot` or `observe_browser` again and use a fresh integer `element_id`.
- **WebView2 operation hangs**: inspect dispatcher usage and `DefaultOperationTimeoutMs` in `BrowserAutomationService`.
- **UI freezes during streaming**: check `GetUiThrottleMs` in `ChatViewModel.SendAsync` / `ContinueToolLoopAsync` and the Markdown converter.
- **Tool loop grows too large**: inspect context compression in `AiClient.CompressHistory` and subtask boundaries.
- **AI tool loop terminated by self-handling**: check `AgentEventSelfHandler` log — look for `stale_element`, `repeat_same_action`, `repeated_navigation_failure`, `no_progress_observe_wait_loop`, `page_stalled_fatal`, `replan_critical`, `exploration_limit` events.
- **Popup/new window behavior**: `BrowserHostService.NewWindowRequested` converts windows into app tabs; script dialogs are auto-handled according to `AutoDismissDialogs`.
- **MCP logs appear irrelevant**: in the current WebView2 path, MCP is legacy and normally uninitialized.
