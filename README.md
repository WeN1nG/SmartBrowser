# SmartAI Browser Demo

SmartAI Browser Demo 是一个基于 **C# / .NET 8 / WPF** 的 Windows 智能浏览器原型。项目当前使用内嵌 **WebView2** 作为浏览器宿主，并将浏览器操作能力以手写 function calling 工具的形式暴露给 AI 助手 **Bermain（板儿面）**。

> 当前可运行主线是：`BrowserHostService` + `BrowserAutomationService` + `BrowserAutomationToolRouter`。仓库中仍保留旧的 Playwright MCP / 外部 Chrome CDP 相关代码，但它不是当前启动流程的默认路径。

## 功能概览

- WPF 深色风格浏览器窗口
- 多标签页浏览与地址栏导航
- WebView2 内嵌网页渲染
- 下载记录窗口
- AI 聊天侧边栏 / 独立 AI 浮窗
- 多 AI 服务商配置与模型档案管理
- AI function calling 工具循环
- AI 可调用的浏览器自动化能力：
  - 打开网页、前进、后退、刷新
  - 获取页面结构快照
  - 点击、输入、悬停、选择下拉项
  - 滚动、按键、等待文本
  - 执行 JavaScript
  - 截图
  - 切换标签页
- AI 任务列表 UI
- `ask_user` 暂停 / 恢复式人工确认流程
- 会话持久化与本地日志

## 技术栈

| 类型 | 技术 |
| --- | --- |
| 语言 | C# |
| 运行时 | .NET 8 |
| UI | WPF |
| 浏览器内核 | Microsoft Edge WebView2 |
| AI 接入 | 手写 OpenAI-compatible / Anthropic-native SSE 客户端 |
| 浏览器自动化 | WebView2 API + JavaScript 注入 |
| 项目文件 | `Demo/BrowserDemo/BrowserDemo.csproj` |

## 环境要求

- Windows 10 / Windows 11
- .NET 8 SDK，并具备 Windows Desktop / WPF 支持
- Microsoft Edge WebView2 Evergreen Runtime
- 如需使用 AI 聊天或模型能力，需要配置对应 AI 服务商的 API Key
- Node.js 和仓库内 Playwright MCP 文件仅在测试或恢复旧 MCP/CDP 路径时需要，当前默认运行路径不依赖它们

## 快速开始

仓库当前没有 `.sln` 文件，请直接使用 `Demo/BrowserDemo/BrowserDemo.csproj`。

```bash
cd Demo

dotnet build BrowserDemo/BrowserDemo.csproj

dotnet run --project BrowserDemo/BrowserDemo.csproj
```

可选清理：

```bash
dotnet clean BrowserDemo/BrowserDemo.csproj
```

如果安装了 `dotnet-format`，可选执行格式检查：

```bash
dotnet format BrowserDemo/BrowserDemo.csproj --verify-no-changes
```

## 项目结构

```text
Demo/BrowserDemo/
├── BrowserDemo.csproj                  # net8.0-windows WPF 项目；依赖 Microsoft.Web.WebView2
├── App.xaml / App.xaml.cs               # 应用入口、控制台与日志生命周期
├── MainWindow.xaml / MainWindow.xaml.cs # 主窗口、标签页、地址栏、WebView2 宿主、AI 面板
├── Models/                              # 浏览器、聊天、工具、设置、任务列表等模型
├── ViewModels/
│   └── ChatViewModel.cs                 # AI 聊天、工具注册、工具循环、ask_user 暂停恢复
├── Views/                               # AI 面板、设置弹窗、下载窗口等 WPF 视图
├── Services/
│   ├── AiClient.cs                      # OpenAI-compatible / Anthropic-native 流式客户端
│   ├── ContextBuilder.cs                # 系统提示词、动态上下文、工具 schema 聚合
│   ├── ConversationService.cs           # 会话 JSON 持久化
│   ├── DownloadManager.cs               # 下载列表管理
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs        # 当前活跃的 WebView2 浏览器宿主
│   │   └── ChromeProcessManager.cs      # 旧外部 Chrome/CDP 路径，当前默认不使用
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs      # 当前活跃的浏览器自动化服务
│   │   ├── BrowserAutomationToolRouter.cs   # 当前活跃的 browser_* 工具路由
│   │   ├── AutomationScripts.cs             # 页面快照、点击、输入等 JS 片段
│   │   └── WebView2AutomationBridge.cs      # 已 #if false 禁用，视为死代码
│   ├── Mcp/                            # 旧 MCP JSON-RPC / Playwright MCP 客户端
│   └── Skills/                         # 旧 MCP 技能系统，当前默认启动路径不初始化
└── Converters/                         # Markdown 等 UI 转换器

Tools/
├── playwright-mcp/                     # 旧 Playwright MCP 服务器文件
└── platform-tools/                     # Android ADB 工具
```

## 当前启动流程

当前应用启动后使用 WebView2 内嵌浏览器，而不是外部 Chrome。

```text
App 启动
  ↓
MainWindow 构造
  ├─ 创建 BrowserViewModel
  ├─ 绑定导航、标签页、下载事件
  ├─ 绑定 ChatViewModel 与 AI 面板事件
  └─ 注册 Loaded / Closing 事件
  ↓
MainWindow.OnLoaded
  ├─ 创建 BrowserHostService
  ├─ 初始化 WebView2 CoreWebView2Environment
  ├─ 创建 BrowserAutomationService
  ├─ 为已有标签页创建并绑定 WebView2
  ├─ 激活当前标签页
  └─ ChatViewModel.AttachAutomationRouter(...)
      ├─ 注册 browser_* 工具
      ├─ 注册 observe_browser
      ├─ 注册 ask_user
      ├─ 注册 set_task_iterations
      ├─ 注册 update_todo
      └─ 注册 start_subtask / finish_subtask
```

`MainWindow.OnLoaded` 当前不会调用 `ChatViewModel.SetChromeCdpEndpoint(...)`，因此旧 Playwright MCP / 外部 Chrome CDP 代码不是默认运行路径。

## 核心模块说明

### BrowserHostService

`Services/BrowserHost/BrowserHostService.cs` 负责 WebView2 生命周期：

- 创建共享 `CoreWebView2Environment`
- 为每个 `TabInfo.Id` 创建独立 WebView2 控件
- 将 WebView2 控件挂载到主窗口内容区域
- 通过显隐切换当前标签页
- 处理导航、标题、地址、下载、新窗口、进程失败、脚本弹窗等事件
- 将网页 popup / new window 转换为应用内新标签页

### BrowserAutomationService

`Services/Automation/BrowserAutomationService.cs` 是当前 AI 浏览器自动化核心：

- 从后台 AI 工具循环进入时，切换到 WPF UI Dispatcher 后再访问 WebView2
- 使用 `SemaphoreSlim(1, 1)` 串行化浏览器操作，避免并发页面操作互相干扰
- 始终面向当前激活标签页执行操作
- 通过 WebView2 API 和注入 JavaScript 完成点击、输入、滚动、页面快照等动作

### BrowserAutomationToolRouter

`Services/Automation/BrowserAutomationToolRouter.cs` 将 AI function calling 参数转换为浏览器自动化调用，并返回紧凑 JSON / 文本结果。

当前注册的浏览器工具包括：

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

页面元素操作主要使用 `browser_snapshot` 返回的整数 `element_id`，而不是直接依赖 CSS selector。

### ChatViewModel 与 AI 工具循环

`ViewModels/ChatViewModel.cs` 负责：

- 用户消息发送
- AI 流式响应展示
- 工具定义注册
- function calling 调用分发
- `ask_user` 暂停 / 恢复
- AI 任务列表更新
- 上下文压缩与子任务边界处理

当没有注册工具时，聊天走普通流式输出；当存在工具时，进入 `AiClient.ExecuteConversationAsync(...)` 的工具循环。

### AiClient

`Services/AiClient.cs` 支持两类 API 协议：

1. OpenAI-compatible `chat/completions` 流式接口
2. Anthropic native `messages` 流式接口

项目内置多个服务商元数据，包括 OpenAI、Anthropic、Google、DeepSeek、xAI、Groq、Cerebras、Mistral、Together、Fireworks、OpenRouter、Alibaba、Zhipu、Moonshot、SiliconFlow、Ollama、DeepInfra 等。

## 本地数据位置

| 数据 | 位置 |
| --- | --- |
| AI 设置 | 程序输出目录下的 `ai_settings.json` |
| 会话记录 | `%LocalAppData%/SmartAI-Browser-Demo/conversations/` |
| WebView2 浏览器配置 | `%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/` |
| 旧外部 Chrome 配置 | `%LocalAppData%/SmartAI-Browser-Demo/chrome-profile/` |
| 日志 | 运行目录 / 项目目录下的 `Log/` |
| 下载文件 | 由 WebView2 下载操作提供实际路径；应用内通过 `DownloadManager.Items` 记录状态 |

## AI 模型配置

首次使用 AI 功能时，请在应用内模型配置窗口中添加服务商、模型和 API Key。设置会保存到程序输出目录旁的 `ai_settings.json`。

如果 AI 提示没有 API Key，通常需要检查：

- 是否已创建模型配置档案
- 当前档案是否被选中
- API Key 是否保存成功
- 运行目录下的 `ai_settings.json` 是否符合预期

## 开发说明

- 当前浏览器控制功能应优先修改：
  - `BrowserHostService`
  - `BrowserAutomationService`
  - `AutomationScripts`
  - `BrowserAutomationToolRouter`
  - `ChatViewModel.AttachAutomationRouter` / `ExecuteAiToolAsync`
- 不要将 `WebView2AutomationBridge.cs` 作为可用代码；它已通过 `#if false` 禁用。
- 不要假设 `ChromeProcessManager` 或 Playwright MCP 在当前运行路径中已启用。
- 访问 WebView2 时必须遵守 WPF UI Dispatcher 线程要求。
- 浏览器自动化操作默认需要保持串行化。
- 新增 AI 浏览器工具时通常需要：
  1. 在 `BrowserAutomationService` 和 / 或 `AutomationScripts` 中实现能力；
  2. 在 `BrowserAutomationToolRouter` 中添加工具 schema 和 dispatch 分支；
  3. 确认 `ChatViewModel.AttachAutomationRouter` 会注册该工具；
  4. 如模型需要新的使用规则，更新 `ContextBuilder` 中的提示词上下文。

## 常见问题

### AI 说没有浏览器工具

检查 `MainWindow.OnLoaded` 是否完成、`AttachAutomationRouter` 是否执行、`ContextBuilder.RegisteredTools` 是否包含工具定义。

### 浏览器元素 ID 无效或过期

重新调用 `browser_snapshot` 或 `observe_browser`，使用最新快照中的整数 `element_id`。

### WebView2 操作卡住

重点检查 Dispatcher 调用、`BrowserAutomationService` 的操作超时配置，以及是否存在未释放的串行化等待。

### AI 流式输出导致 UI 卡顿

检查 `ChatViewModel.SendAsync` / `ContinueToolLoopAsync` 中的 UI 刷新节流，以及 Markdown 转换逻辑。

### 看到 MCP 日志但当前功能无关

MCP / Playwright / 外部 Chrome 是旧路径。当前默认浏览器控制来自 WebView2 自动化服务。

## 当前状态

这是一个浏览器 + AI 自动化能力的原型项目。当前没有独立测试项目，也没有解决方案文件。主要验证方式是构建并运行 WPF 应用：

```bash
cd Demo
dotnet build BrowserDemo/BrowserDemo.csproj
dotnet run --project BrowserDemo/BrowserDemo.csproj
```
