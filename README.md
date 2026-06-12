# SmartAI Browser Demo

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue" alt=".NET 8">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey" alt="Platform">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
</p>

**SmartAI Browser Demo** 是一个 Windows 智能浏览器原型，将 **WebView2 浏览器引擎** 与 **AI 函数调用（Function Calling）** 深度整合。AI 助手能够自主阅读页面结构、执行点击输入、导航切换、完成表单等操作，同时用户可通过 `ask_user` 机制在关键节点介入确认，实现"AI 自动执行 + 人工适时干预"的协作模式。

> **联系 & 反馈**：欢迎通过邮箱 [3266038380@qq.com](mailto:3266038380@qq.com) 提出意见、建议或报告问题。如果你有类似项目或想一起完善这个 Demo，非常期待与你交流！

---

## 功能概览

### 浏览器核心

| 功能 | 说明 |
|------|------|
| 多标签页浏览 | 新建、关闭、切换标签页，每个标签独立 WebView2 实例 |
| 地址栏导航 | 手动输入 URL、前进 / 后退 / 刷新 |
| 新窗口转标签 | 网页 popup / new window 自动转换为应用内新标签页 |
| 书签 & 历史 | 本地 JSON 持久化，URL 去重，最多保留 500 条历史 |
| 下载管理 | 实时进度追踪，独立下载记录窗口 |
| 弹窗处理 | 自动处理页面 alert / confirm / prompt 脚本弹窗 |

### AI 集成

| 功能 | 说明 |
|------|------|
| 多服务商支持 | OpenAI、Anthropic、Google、DeepSeek、xAI、Groq、Ollama 等 16+ 服务商 |
| AI 聊天面板 | 内嵌侧边栏 + 独立浮窗双模式 |
| Function Calling 工具循环 | AI 自主调用浏览器工具完成复杂任务 |
| 思考过程 / 结论分区 | AI 输出自动拆分为可折叠思考区与可见结论区 |
| `ask_user` 人工确认 | AI 遇到歧义时暂停，用户选择后恢复执行 |
| AI 任务清单 | 复杂任务自动拆分子任务，右侧面板实时跟踪进度 |
| 上下文自动压缩 | 对话超 150KB 自动压缩至 ~100KB，防止上下文溢出 |
| 会话持久化 | 自动保存对话 JSON，支持加载历史对话 |

### AI 可调用的浏览器工具（17 个）

| 工具 | 功能 |
|------|------|
| `browser_navigate` | 打开指定 URL |
| `browser_back` / `browser_forward` / `browser_reload` | 后退 / 前进 / 刷新 |
| `browser_snapshot` | 获取页面可访问性快照（结构化元素列表 + `element_id`） |
| `browser_click` | 通过 `element_id` 点击元素 |
| `browser_type` | 通过 `element_id` 向输入框键入文本 |
| `browser_hover` | 悬停触发下拉/tooltip |
| `browser_select_option` | 选择下拉框选项 |
| `browser_scroll` | 页面滚动 |
| `browser_press_key` | 模拟按键（Enter/Tab/Escape/方向键等） |
| `browser_screenshot` | 截取当前视口 |
| `browser_js` | 执行任意 JavaScript |
| `browser_wait` / `browser_wait_for` | 固定等待 / 等待文本出现 |
| `browser_fill_form` | 多字段自动填充 |
| `browser_switch_tab` | 切换标签页 |
| `observe_browser` | 一次性快照 → 返回 `<browser_state>` 结构化文本 |

---

## 技术栈

| 层面 | 技术 |
|------|------|
| 语言 | C# 12 |
| 运行时 | .NET 8 (net8.0-windows) |
| UI 框架 | WPF（手写深色主题，无第三方 UI 库） |
| 浏览器内核 | Microsoft Edge WebView2 (`Microsoft.Web.WebView2`) |
| AI 协议 | OpenAI-compatible SSE / Anthropic-native Messages SSE |
| 浏览器自动化 | WebView2 CoreWebView2 API + JavaScript 注入 |
| 日志 | 自研 Logger（控制台 + 文件 + 内存缓存 + TraceBlock） |
| 数据持久化 | JSON 文件（书签 / 历史 / 会话 / 模型配置） |

---

## 环境要求

- **操作系统**：Windows 10 / Windows 11
- **SDK**：.NET 8 SDK，含 Windows Desktop / WPF 工作负载
- **浏览器运行时**：Microsoft Edge WebView2 Evergreen Runtime（Windows 10+ 通常已预装）
- **AI API Key**：使用 AI 聊天或模型能力时，需在应用内配置对应服务商的 API Key
- **Node.js / Playwright MCP**：仅在测试或恢复旧 MCP/CDP 路径时需要，当前默认运行路径不依赖

---

## 快速开始

> 注意：本项目没有 `.sln` 解决方案文件，直接操作 `.csproj` 文件。

```bash
# 进入项目目录
cd Demo

# 构建
dotnet build BrowserDemo/BrowserDemo.csproj

# 运行
dotnet run --project BrowserDemo/BrowserDemo.csproj

# 清理
dotnet clean BrowserDemo/BrowserDemo.csproj

# 代码格式检查（需安装 dotnet-format）
dotnet format BrowserDemo/BrowserDemo.csproj --verify-no-changes
```

---

## 项目结构

```
Demo/BrowserDemo/
├── BrowserDemo.csproj                  # .NET 8 WPF 项目；NuGet: Microsoft.Web.WebView2
├── App.xaml / App.xaml.cs               # 应用入口、控制台/日志生命周期、未处理异常兜底
├── MainWindow.xaml / .cs                # 主窗口 Shell（标签页、地址栏、WebView2 容器、AI 面板）
├── Converters/
│   └── MarkdownToFlowDocumentConverter.cs # Markdown → WPF FlowDocument
├── Models/
│   ├── BrowserViewModel.cs              # 标签页管理、导航命令、下载模型
│   ├── TabInfo.cs                       # 标签元数据（Guid Id 为全局身份）
│   ├── ChatMessage.cs                   # 对话角色、内容、工具调用数据
│   ├── ToolCallData.cs                  # 流式工具调用累积器
│   ├── ToolDefinition.cs                # AI 工具 Schema（OpenAI + Anthropic 转换）
│   ├── AiSettings.cs / AiSettingsStore.cs  # 多服务商/多模型配置
│   ├── AiTodoItem.cs                    # AI 任务列表 UI 模型
│   └── DownloadItem.cs                  # 下载项 UI 模型
├── ViewModels/
│   └── ChatViewModel.cs                 # AI 聊天、工具循环、ask_user 暂停恢复、任务清单
├── Views/
│   ├── AiChatPanel.xaml / .cs           # AI 聊天面板（UserControl）
│   ├── AiSecondaryWindow.xaml / .cs     # 独立 AI 浮窗
│   ├── AiModelSelectionDialog.xaml / .cs # 模型配置窗口
│   └── DownloadsWindow.xaml / .cs       # 下载记录窗口
├── Services/
│   ├── Logger.cs                        # 日志服务：控制台 + 文件 + 内存缓存
│   ├── AiClient.cs                      # OpenAI/Anthropic 流式 SSE 客户端
│   ├── ContextBuilder.cs                # 系统提示词 + 动态上下文 + 工具 Schema 聚合
│   ├── ConversationService.cs           # 会话 JSON 持久化
│   ├── DownloadManager.cs               # 下载列表管理（静态 Observable）
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs        # ★ 当前活跃的 WebView2 浏览器宿主
│   │   └── ChromeProcessManager.cs      # 旧外部 Chrome/CDP 宿主（默认不启用）
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs      # ★ 当前活跃的浏览器自动化核心
│   │   ├── BrowserAutomationToolRouter.cs   # ★ browser_* AI 工具路由
│   │   ├── AutomationScripts.cs             # 页面快照/点击/输入等 JS 片段
│   │   ├── AdbService.cs                    # Android SMS 助手（未暴露为 AI 工具）
│   │   └── WebView2AutomationBridge.cs      # ❌ 已 #if false 禁用，死代码
│   ├── Mcp/                             # 旧 MCP JSON-RPC / Playwright MCP 客户端
│   │   ├── JsonRpcClient.cs
│   │   └── PlaywrightMcpClient.cs
│   └── Skills/                          # 旧 MCP 技能系统（当前默认不初始化）
└── Tools/
    ├── playwright-mcp/                  # 旧 Playwright MCP 服务器（不依赖）
    └── platform-tools/                  # Android ADB 工具

Help/                                  # 项目文档
├── FunctionHelp.md                    # 函数级 API 文档
├── EffectHelp.md                      # 功能实现与设计文档
└── Debugg/                            # 逐项修复记录
```

---

## 启动流程

```
App 启动
  ↓
MainWindow 构造函数
  ├─ 创建 BrowserViewModel（标签、导航、下载、书签、历史）
  ├─ 绑定导航/标签页/下载事件
  ├─ 绑定 ChatViewModel 与 AI 面板事件
  └─ 注册 Loaded / Closing 生命周期事件
  ↓
MainWindow.OnLoaded（核心初始化）
  ├─ 创建 BrowserHostService
  │   └─ 配置 UserDataFolder = %LocalAppData%/SmartAI-Browser-Demo/webview2-profile/
  ├─ await BrowserHostService.InitializeAsync()
  │   └─ 创建共享 CoreWebView2Environment
  ├─ 创建 BrowserAutomationService
  ├─ 为已有标签页创建并绑定 WebView2
  ├─ 激活当前激活标签
  └─ ChatViewModel.AttachAutomationRouter(BrowserAutomationToolRouter)
      ├─ 注册 17 个 browser_* 工具
      ├─ 注册 observe_browser（一次性结构化快照）
      ├─ 注册 ask_user（暂停/恢复人工确认）
      ├─ 注册 set_task_iterations（调整工具循环阈值）
      ├─ 注册 update_todo（AI 任务清单）
      └─ 注册 start_subtask / finish_subtask（子任务边界 + 上下文压缩触发）
  ↓
✅ 浏览器嵌入完成，AI 工具已就绪
```

**重要**：当前默认启动路径 **不** 启用 Playwright MCP / 外部 Chrome CDP。这些旧路径代码保留在仓库中，除非手动调用 `ChatViewModel.SetChromeCdpEndpoint(...)` 才会激活。

---

## 核心模块

### BrowserHostService — 浏览器宿主

管理 WebView2 的完整生命周期：

- 创建共享 `CoreWebView2Environment`
- 每个 `TabInfo.Id` 对应一个独立 `WebView2` 控件
- 通过 Visibility 切换实现标签页切换（避免重复创建/销毁）
- 处理导航、标题、URL、加载状态、下载、新窗口、进程崩溃等事件
- 将网页 popup / new window 请求转换为应用内新标签页

### BrowserAutomationService — 浏览器自动化

AI 调用浏览器操作的底层执行层：

- 后台线程进入时自动切换到 WPF UI Dispatcher 访问 WebView2
- 使用 `SemaphoreSlim(1, 1)` 串行化操作，防止并发页面操作互相干扰
- 通过 WebView2 API 和注入 JavaScript 完成全部操作
- 始终面向当前激活标签页执行

### BrowserAutomationToolRouter — AI 工具路由

将 AI 的 function calling 参数转换为浏览器自动化调用：

- 参数容错解析（支持 `element_id` / `id` / `element` 别名）
- 返回紧凑 JSON 格式结果，避免将大体积数据（如 base64 截图）注入 AI 上下文
- 页面元素操作统一使用 `browser_snapshot` 返回的整数 `element_id`

### ChatViewModel & AiClient — AI 聊天与工具循环

**AiClient** 支持两类协议：

1. **OpenAI-compatible**：`chat/completions` 流式接口，Bearer 认证
2. **Anthropic-native**：`messages` 流式接口，`x-api-key` + `anthropic-version`

**工具循环** (`ExecuteConversationAsync`)：

- 意图为无界 `for` 循环：AI 持续返回工具调用 → 执行 → 结果回传 → 继续
- `set_task_iterations` 设置软提醒阈值（1-80），接近阈值时注入效率提示
- 上下文自动压缩：超过 150KB 压缩至 ~100KB，40 轮 / 40 消息兜底
- `ask_user` 触发暂停，等待用户输入后恢复

### ask_user 暂停/恢复机制

```
AI 调用 ask_user(question, options?)
  → 工具循环暂停，UI 显示问题卡片
  → 用户选择选项或输入文本
  → tool_result 追加到对话历史
  → 工具循环恢复执行
```

支持三种模式：`confirmation`（确认）、`multiple_choice`（多选）、`open_ended`（自由回答）。

---

## 本地数据位置

| 数据 | 位置 |
|------|------|
| AI 模型配置 | 程序输出目录 `ai_settings.json` |
| 会话记录 | `%LocalAppData%/SmartAI-Browser-Demo/conversations/` |
| WebView2 浏览器配置 | `%LocalAppData%/SmartAI-Browser-Demo/webview2-profile/` |
| 旧外部 Chrome 配置 | `%LocalAppData%/SmartAI-Browser-Demo/chrome-profile/` |
| 书签 / 历史 | `%LocalAppData%/SmartAI-Browser-Demo/` 下的 JSON 文件 |
| 日志 | 运行目录下的 `Log/` |

---

## AI 模型配置

首次使用时，点击应用内**模型配置**窗口添加服务商、模型和 API Key。设置保存到 `ai_settings.json`。

内置支持的服务商：

> OpenAI、Anthropic、Google、DeepSeek、xAI、Groq、Cerebras、Mistral、Together、Fireworks、OpenRouter、Alibaba（通义千问）、Zhipu（智谱）、Moonshot（月之暗面）、SiliconFlow、Ollama（本地）、DeepInfra

配置会自动修正常见协议错配（如火山方舟 Coding Plan endpoint 补全 `/v3`、Anthropic 端点误用 OpenAI 格式等）。

---

## 常见问题

### AI 说没有浏览器工具

检查 `MainWindow.OnLoaded` 是否完成、`AttachAutomationRouter` 是否执行、`ContextBuilder.RegisteredTools` 是否包含工具定义。

### 浏览器元素 ID 无效或过期

重新调用 `browser_snapshot` 或 `observe_browser`，使用最新快照中的整数 `element_id`。

### WebView2 操作卡住

重点检查 Dispatcher 调用、`BrowserAutomationService` 的操作超时配置，以及是否存在未释放的串行化等待。

### AI 流式输出导致 UI 卡顿

检查 `ChatViewModel` 中的 UI 刷新节流和 Markdown 转换逻辑。长回复（>12000 字符）仅渲染末尾部分。

### 看到 MCP 日志但功能无关

MCP / Playwright / 外部 Chrome 是旧路径。当前默认浏览器控制来自 WebView2 自动化服务。

---

## 开发说明

- 本项目无 DI 容器，采用直接 WPF 事件绑定 + 手动 `INotifyPropertyChanged`
- 新增 AI 浏览器工具的步骤：
  1. 在 `BrowserAutomationService` / `AutomationScripts` 中实现操作
  2. 在 `BrowserAutomationToolRouter` 中添加工具 Schema 和 dispatch 分支
  3. 确认 `ChatViewModel.AttachAutomationRouter` 会注册该工具
  4. 如需新规则，更新 `ContextBuilder` 中的提示词
- 改变 AI 服务商行为时，需同时考虑 OpenAI-compatible 和 Anthropic-native 两条路径
- WebView2 操作必须通过 UI Dispatcher 访问
- 浏览器自动化操作默认保持串行化

### 需要修改的文件

| 功能 | 优先修改的文件 |
|------|---------------|
| 浏览器宿主 | `BrowserHostService.cs` |
| 自动化操作 | `BrowserAutomationService.cs`、`AutomationScripts.cs` |
| AI 工具 | `BrowserAutomationToolRouter.cs` |
| 聊天与工具循环 | `ChatViewModel.cs`、`AiClient.cs` |
| 系统提示词 | `ContextBuilder.cs` |
| UI 界面 | `Views/` 下的 XAML 文件 |

---

## 当前状态

这是一个 **浏览器 + AI 自动化能力** 的原型项目。当前没有独立测试项目和解决方案文件。主要验证方式是构建并运行 WPF 应用。

```bash
cd Demo
dotnet build BrowserDemo/BrowserDemo.csproj
dotnet run --project BrowserDemo/BrowserDemo.csproj
```

---

## 许可

MIT License

---

## 反馈与交流

如果你对这个项目有任何意见、建议、想法，或者想一起完善它，欢迎联系我：

- **邮箱**：[3266038380@qq.com](mailto:3266038380@qq.com)

期待听到你的声音！
