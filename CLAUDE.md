# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with this repository.

## Project Overview

**SmartAI Browser Demo** is a Windows intelligent browser prototype built with **C# / .NET 8 / WPF**. The current Demo runs an embedded **WebView2** browser host and exposes browser-control tools to the AI assistant **Bermain（板儿面）** through hand-written function calling schemas.

The important current reality is:

- The active browser host is `BrowserHostService` + WebView2 controls embedded directly in the WPF window.
- The active AI browser automation path is `BrowserAutomationService` + `BrowserAutomationToolRouter`.
- The older Playwright MCP / external Chrome CDP path still exists in code (`SkillSystemIntegration`, `PlaywrightMcpClient`, `ChromeProcessManager`) but is not used by the current startup flow because `MainWindow.OnLoaded` no longer calls `ChatViewModel.SetChromeCdpEndpoint`.
- `WebView2AutomationBridge.cs` is fully disabled with `#if false` and must be treated as dead code.

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
│   └── ChatViewModel.cs                # AI chat, function-call loop integration, ask_user pause/resume, tool registration
├── Views/
│   ├── AiChatPanel.xaml / .cs          # Chat panel UI
│   ├── AiSecondaryWindow.xaml / .cs    # Floating AI window owned by MainWindow
│   ├── AiModelSelectionDialog.xaml / .cs # Current AI model/profile settings UI
│   ├── AiSettingsDialog.xaml / .cs     # Older settings dialog still present
│   └── DownloadsWindow.xaml / .cs      # Download list window
├── Services/
│   ├── Logger.cs                       # Static logger: console + file + in-memory buffer + Trace blocks
│   ├── IAiClient.cs / AiClient.cs       # Hand-written OpenAI-compatible + Anthropic SSE clients and tool loop
│   ├── ContextBuilder.cs               # System prompt + dynamic context + registered tool schemas
│   ├── ConversationService.cs          # JSON conversation persistence
│   ├── DownloadManager.cs              # Static observable download list
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs       # ACTIVE browser host: WebView2 environment, tabs, events, downloads
│   │   └── ChromeProcessManager.cs     # Legacy external Chromium/CDP host; not used by MainWindow current flow
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs # ACTIVE automation: WebView2 API + JS injection + UI-thread dispatch
│   │   ├── BrowserAutomationToolRouter.cs # ACTIVE function-call router for browser_* tools
│   │   ├── AutomationScripts.cs        # JavaScript snippets for snapshots/click/type/etc.
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
| Active automation | `BrowserAutomationService` runs WebView2 operations on the UI dispatcher and serializes operations with `SemaphoreSlim(1,1)` |
| AI tool exposure | `BrowserAutomationToolRouter.GetToolDefinitions()` registers `browser_*` tools into `ContextBuilder` |
| AI client | Hand-written `HttpClient` streaming SSE for OpenAI-compatible and Anthropic-native APIs |
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
  │   ├─ registers update_todo
  │   └─ registers start_subtask / finish_subtask
  └─ status: browser embedded, AI browser tools enabled
```

`MainWindow.OnLoaded` explicitly does **not** call `_vm.Chat.SetChromeCdpEndpoint(...)` in the current implementation. Do not describe external Chrome + Playwright MCP as the active path unless you are intentionally documenting legacy code.

## Active Browser Host and Automation Flow

### BrowserHostService

`Services/BrowserHost/BrowserHostService.cs` owns the WebView2 lifecycle:

- Creates one shared `CoreWebView2Environment`.
- Creates one `WebView2` control per `TabInfo.Id` and adds it to the WPF `ContentArea` panel.
- Switches active tabs by toggling each WebView2 control's `Visibility`.
- Binds WebView2 events:
  - `NavigationStarting`
  - `NavigationCompleted`
  - `DocumentTitleChanged`
  - `SourceChanged`
  - `DownloadStarting`
  - `NewWindowRequested`
  - `ProcessFailed`
  - script dialog handling
- Converts popup/new-window requests into app tabs via `NewTabRequested`.
- Records download progress through `DownloadManager`.

### BrowserAutomationService

`Services/Automation/BrowserAutomationService.cs` is the active automation layer:

- It is called from the AI/tool loop on background threads.
- It switches to the WPF UI thread using `Dispatcher.InvokeAsync` before touching WebView2.
- It serializes automation calls with a single `SemaphoreSlim` to avoid concurrent page operations.
- It targets the current active tab (`SwitchToTab(Guid)` updates active automation target).
- It exposes operations such as navigation, back/forward/reload, click/type/hover/select, scroll, key press, screenshot, JS evaluation, wait, wait-for-text, and form filling.
- Element tools use integer `element_id` values returned by `browser_snapshot`; do not use CSS selectors for the active `browser_click` / `browser_type` path unless a tool explicitly accepts them.

### BrowserAutomationToolRouter

`Services/Automation/BrowserAutomationToolRouter.cs` converts function-calling arguments to automation calls and JSON-formats results.

Currently registered browser tools:

- `browser_navigate`
- `browser_back`
- `browser_forward`
- `browser_reload`
- `browser_snapshot`
- `browser_click`
- `browser_type`
- `browser_hover`
- `browser_select_option`
- `browser_scroll`
- `browser_press_key`
- `browser_screenshot`
- `browser_js`
- `browser_wait`
- `browser_wait_for`
- `browser_fill_form`
- `browser_switch_tab`

`ChatViewModel` also registers:

- `observe_browser` — wraps `browser_snapshot` into a PageAgent-like `<browser_state>` text view.
- `ask_user` — pauses the AI tool loop for user input.
- `set_task_iterations` — adjusts the tool-loop soft reminder threshold.
- `update_todo` — updates the AI panel task list.
- `start_subtask` / `finish_subtask` — marks subtask boundaries and triggers context compression.

## AI Client and Tool Loop

`Services/AiClient.cs` supports two request formats:

1. **OpenAI-compatible chat completions**: most providers use Bearer auth and `chat/completions` streaming with `delta.tool_calls`.
2. **Anthropic native messages**: Anthropic uses `x-api-key`, `anthropic-version: 2023-06-01`, top-level `system`, and streaming `content_block_*` / `message_delta` events.

`ChatViewModel.SendAsync()` has two paths:

```text
SendAsync()
  ├─ no tools registered → AiClient.StreamMessageAsync()
  └─ tools registered    → AiClient.ExecuteConversationAsync(..., ExecuteAiToolAsync)
```

`AiClient.ExecuteConversationAsync` is intentionally an unbounded `for` loop. It stops when:

- the AI returns normal text with no tool calls;
- `ask_user` returns the special pause prefix;
- the cancellation token is cancelled;
- the stale-result detector hits 3 consecutive stale probe results from legacy `skill_extract` / `skill_query`;
- an API or streaming error is yielded.

Important loop behavior:

- `maxIterations` is a **soft reminder threshold**, not a hard cap.
- `set_task_iterations` can set the soft threshold to 1–80 for the next phase.
- When near the threshold, the loop injects a system reminder telling the model to be efficient.
- Context compression runs when estimated conversation bytes exceed 150 KB, targets ~100 KB, and also runs as a 20-round / 40-message fallback.
- `start_subtask` returns a sentinel that forces compression before the subtask proceeds.

## `ask_user` Pause/Resume Pattern

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
7. `__skip__` becomes: “用户选择跳过，请基于当前已有信息自行决定最佳方案并继续执行。”

## Skill System Status

There are two skill-model families:

1. `Models/SkillDefinition.cs` and siblings — legacy record-based models, not the active runtime path.
2. `Services/Skills/SkillModels.cs` and related files — class-based MCP skill system.

The class-based MCP skill system defines:

- 13 atomic skills
- 7 composite skills
- 6 strategy skills

However, in the **current WebView2 startup path**, this system remains uninitialized because `MainWindow` does not call `ChatViewModel.SetChromeCdpEndpoint`. Browser automation for the AI therefore comes from `BrowserAutomationToolRouter`, not from `McpSkillExecutor`.

If you work on the current browser-control feature, prefer:

- `BrowserHostService`
- `BrowserAutomationService`
- `AutomationScripts`
- `BrowserAutomationToolRouter`
- `ChatViewModel.AttachAutomationRouter` / `ExecuteAiToolAsync`

Only work on `SkillSystemIntegration`, `PlaywrightMcpClient`, `McpSkillExecutor`, or `ChromeProcessManager` when the task explicitly targets the legacy MCP/CDP path.

## Persistence and Local Data

- AI settings: `ai_settings.json` next to the built executable.
- Conversation files: `%LocalAppData%/SmartAI-Browser-Demo/conversations/*.json`.
- WebView2 browser profile: `%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/`.
- Legacy external Chrome profile: `%LocalAppData%/SmartAI-Browser-Demo/chrome-profile/` (only for `ChromeProcessManager`).
- Logs: `Log/` under the project/runtime working area, with old logs cleaned by `Logger`.
- Downloads: tracked in-memory through `DownloadManager.Items`; the actual file path is provided by WebView2's download operation.

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
- Use existing `Logger` calls and `Logger.Trace(...)` blocks for meaningful lifecycle/debug points.
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
- **Popup/new window behavior**: `BrowserHostService.NewWindowRequested` converts windows into app tabs; script dialogs are auto-handled according to `AutoDismissDialogs`.
- **MCP logs appear irrelevant**: in the current WebView2 path, MCP is legacy and normally uninitialized.
