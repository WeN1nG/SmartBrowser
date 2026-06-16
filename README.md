# SmartAI Browser Demo

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blue" alt=".NET 8">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey" alt="Platform">
  <img src="https://img.shields.io/badge/license-MIT-green" alt="License">
</p>

**SmartAI Browser Demo** 是一个 Windows 智能浏览器原型，将 **WebView2 浏览器引擎** 与 **AI 函数调用（Function Calling）** 深度整合。AI 助手能够自主阅读页面结构、执行点击输入、导航切换、完成表单等操作，同时通过**任务状态机**强制 AI 按子任务清单顺序执行，并通过 `ask_user` 机制在关键节点让人工介入确认，实现"AI 自动执行 + 人工适时干预 + 强制任务规划"的协作模式。

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
| 上下文自动压缩 | 对话超 50KB 自动压缩至 ~40KB，子任务完成时压缩至 ~30KB |
| 会话持久化 | 自动保存对话 JSON，支持加载历史对话 |
| **任务状态机** | 强制 AI 按 Planning → Executing → Complete 顺序执行子任务，不可跳序 |
| **死胡同自检测** | 自动检测过期元素复用、重复导航失败、无进展循环、页面停滞、连续失败、探索限制，注入纠正提示或终止工具循环 |
| **AI 复读检测** | 连续多轮返回高度相似文本时自动终止，防止 AI 原地打转 |
| **硬迭代上限** | 工具循环最多 80 轮，超出强制终止 |
| **预算渐进警告** | 在 50%/75%/90%/95% 消耗点注入提醒，引导 AI 整合结果 |
| **DOM 页面停滞检测** | 通过 `browser_snapshot` 的 DOM text hash 追踪页面变化，连续 2 次无变化告警，4 次终止 |
| **连续失败 replan 触发** | 工具执行连续失败 3 步后提示 AI 重新规划子任务 |
| **探索步数限制** | 连续 5 步未关联任何子任务时提醒 AI 制定明确计划 |

### AI 可调用的浏览器工具（18 个）

| 工具 | 功能 |
|------|------|
| `browser_navigate` | 打开指定 URL |
| `browser_back` / `browser_forward` / `browser_reload` | 后退 / 前进 / 刷新 |
| `browser_snapshot` | 获取页面可交互元素快照（Playwright 风格可见性过滤 + 重要性评分） |
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

### 任务规划工具

| 工具 | 功能 |
|------|------|
| `update_todo` | 创建完整子任务清单（仅 Planning 状态允许） |
| `start_subtask` | 开始执行某个子任务，触发上下文压缩 |
| `finish_subtask` | 结束当前子任务（completed/blocked），自动推进下一个 |
| `set_task_iterations` | 动态调整迭代次数软提醒阈值（1-80） |

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
| 任务管理 | 自建 TaskStateMachine（Planning → Executing → Complete） |
| 自检测机制 | AgentEventSelfHandler（过期元素、重复导航、无进展循环、页面停滞、连续失败、探索限制 + 自动阻断） |
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
│   └── ChatViewModel.cs                 # AI 聊天、工具循环、ask_user、任务状态机集成
├── Views/
│   ├── AiChatPanel.xaml / .cs           # AI 聊天面板（UserControl）
│   ├── AiSecondaryWindow.xaml / .cs     # 独立 AI 浮窗
│   ├── AiModelSelectionDialog.xaml / .cs # 模型配置窗口
│   └── DownloadsWindow.xaml / .cs       # 下载记录窗口
├── Services/
│   ├── Logger.cs                        # 日志服务：控制台 + 文件 + 内存缓存
│   ├── AiClient.cs                      # OpenAI/Anthropic 流式 SSE 客户端 + 工具循环安全机制
│   ├── ContextBuilder.cs                # 系统提示词 + 动态上下文 + 工具 Schema 聚合
│   ├── ConversationService.cs           # 会话 JSON 持久化
│   ├── DownloadManager.cs               # 下载列表管理（静态 Observable）
│   ├── AgentEventSelfHandler.cs         # AI 工具循环自检测：死胡同识别 + 自动阻断
│   ├── BrowserHost/
│   │   ├── BrowserHostService.cs        # ★ 当前活跃的 WebView2 浏览器宿主
│   │   └── ChromeProcessManager.cs      # 旧外部 Chrome/CDP 宿主（默认不启用）
│   ├── Automation/
│   │   ├── BrowserAutomationService.cs      # ★ 当前活跃的浏览器自动化核心
│   │   ├── BrowserAutomationToolRouter.cs   # ★ browser_* AI 工具路由
│   │   ├── AutomationScripts.cs             # ★ 页面快照 JS：Playwright 风格过滤 + 重要性评分
│   │   ├── AdbService.cs                    # Android SMS 助手（未暴露为 AI 工具）
│   │   └── WebView2AutomationBridge.cs      # ❌ 已 #if false 禁用，死代码
│   ├── Mcp/                             # 旧 MCP JSON-RPC / Playwright MCP 客户端
│   │   ├── JsonRpcClient.cs
│   │   └── PlaywrightMcpClient.cs
│   └── Skills/                          # 旧 MCP 技能系统（当前默认不初始化）
├── BrowserSkills/                     # 独立提取的浏览器自动化技能库（net8.0-windows Class Library）
│   ├── Core/                          # 自动化引擎 + 工具路由器 + JS 脚本 + 日志接口
│   ├── Models/                        # 对话消息、工具定义、任务清单、回复解析
│   ├── Skills/                        # 原子/组合/策略技能定义与注册
│   ├── Strategy/                      # 导航/定位/重试/上下文/恢复/隐私策略
│   └── Intelligence/                  # 上下文构建、任务状态机、自检测
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
      ├─ 注册 18 个 browser_* 工具
      ├─ 注册 observe_browser（一次性结构化快照）
      ├─ 注册 ask_user（暂停/恢复人工确认）
      ├─ 注册 set_task_iterations（调整工具循环阈值）
      ├─ 注册 update_todo（→ 连接 TaskStateMachine）
      ├─ 注册 start_subtask / finish_subtask（→ 连接 TaskStateMachine）
      └─ ContextBuilder.TaskStateMachine = _taskStateMachine
  ↓
✅ 浏览器嵌入完成，AI 工具已就绪，任务状态机已激活
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
- 通过 `Automation` 属性链接到 `BrowserAutomationService`

### BrowserAutomationService — 浏览器自动化

AI 调用浏览器操作的底层执行层：

- 后台线程进入时自动切换到 WPF UI Dispatcher 访问 WebView2
- 使用 `SemaphoreSlim(1, 1)` 串行化操作，防止并发页面操作互相干扰
- 通过 WebView2 API 和注入 JavaScript 完成全部操作
- 始终面向当前激活标签页执行

### AutomationScripts — 页面快照 JS 引擎

注入到页面的 JavaScript 代码，负责采集可交互元素：

- **Playwright 风格可见性过滤**：只保留真正"看得见且能交互"的元素
  - 过滤 `display:none`、`visibility:hidden`、`aria-hidden`、`[hidden]`、`role=presentation`
  - 通过 `getBoundingClientRect()` 验证元素具有非零尺寸
- **元素重要性评分**：按钮 / CTA 等关键元素优先出现在快照前列
  - 按标签优先级（button=100, a=80, input=65 …）打分
  - 短文本按钮加分，空标签降权，纯 JS 链接大幅降权
- **精简字段**：移除 rect/visible/css_selector/disabled/readonly，减少 LLM 上下文污染
- **无上限采集**：不再限制元素数量（`MaxSnapshotElements = 0`），依靠重要性排序和上下文压缩控制大小

### BrowserAutomationToolRouter — AI 工具路由

将 AI 的 function calling 参数转换为浏览器自动化调用：

- 参数容错解析（支持 `element_id` / `id` / `element` 别名）
- 返回紧凑 JSON 格式结果，避免将大体积数据（如 base64 截图）注入 AI 上下文
- 页面元素操作统一使用 `browser_snapshot` 返回的整数 `element_id`

### AgentEventSelfHandler — AI 自检测

在工具循环中实时监控 AI 行为，自动识别死胡同：

- **过期元素复用检测**：追踪已知无效的 element_id，重复 2 次后阻断，累计 3 次终止循环
- **重复导航失败**：同一 URL 失败 2 次或同主机失败 4 次后阻断
- **无进展循环**：被动工具（observe/snapshot/wait/reload）连续产生相同结果时注入警告
- **相同动作重复**：同一工具+参数产生相同结果 3 次后阻断
- **ask_user 建议追踪**：工具结果多次建议 ask_user 但未采纳时终止
- 所有检测通过注入 `[agent_event code=xxx severity=warning|block]` 系统消息通知 AI

### TaskStateMachine — 任务状态机

强制 AI 按子任务清单顺序执行，防止跳跃式操作：

- **三态流转**：`Planning` → `Executing` → `Complete`
- **update_todo**：仅在 Planning 状态允许，建立完整子任务清单
- **start_subtask**：仅可对当前 `ActiveSubtaskId` 调用，触发上下文压缩
- **finish_subtask**：仅可对当前 `ActiveSubtaskId` 调用，完成后自动推进下一个 pending 子任务
- **压缩策略**：start 时 Standard 压缩，finish(completed) 时 Max 压缩，finish(blocked) 时不压缩

### ChatViewModel & AiClient — AI 聊天与工具循环

**AiClient** 支持两类协议：

1. **OpenAI-compatible**：`chat/completions` 流式接口，Bearer 认证
2. **Anthropic-native**：`messages` 流式接口，`x-api-key` + `anthropic-version`

**工具循环安全机制** (`ExecuteConversationAsync`)：

- **意图为无界 `for` 循环**：AI 持续返回工具调用 → 执行 → 结果回传 → 继续
- **硬上限**：最多 80 轮迭代，超出强制终止
- **工具结果截断**：超过 2000 字符自动截断（保留首 2000 + 尾 500）
- **上下文压缩**：50KB 触发压缩至 40KB，子任务完成时压缩至 30KB
- **预算渐进警告**：50%/75%/90%/95% 消耗点注入 `[agent_event code=budget_warning]` 提醒
- **子任务门禁**：有未完成子任务时 AI 输出纯文本 → 回传提醒；连续 5 次不执行工具 → 终止
- **规划门禁**：基于 TaskStateMachine 状态强制要求 `update_todo` / `start_subtask`
- **AI 复读检测**：连续 2 轮返回指纹相同的文本（>30 字符）→ 终止
- **browser_js null 诊断**：连续 2 次 JS 查询返回 null → 注入策略变更提示
- **set_task_iterations**：设置软提醒阈值（1-80），接近阈值时注入效率提示
- **上下文压缩**：40 轮 / 40 消息兜底
- **工具参数校验**：`SanitizeToolArguments()` 确保流式响应的不完整 JSON 参数不污染对话历史

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

重新调用 `browser_snapshot` 或 `observe_browser`，使用最新快照中的整数 `element_id`。如果 AI 连续复用过期元素，`AgentEventSelfHandler` 会自动阻断并终止循环。

### WebView2 操作卡住

重点检查 Dispatcher 调用、`BrowserAutomationService` 的操作超时配置，以及是否存在未释放的串行化等待。

### AI 流式输出导致 UI 卡顿

检查 `ChatViewModel` 中的 UI 刷新节流和 Markdown 转换逻辑。长回复（>12000 字符）仅渲染末尾部分。

### AI 工具循环被终止

查看日志中的 `agent_event` 记录，确定触发原因：
- `stale_element_reuse` → 元素过期，刷新页面后重试
- `repeated_navigation_failure` → URL 错误，从页面入口进入或 ask_user
- `repeat_same_action` → 动作重复，换策略
- `no_progress_observe_wait_loop` → 观察无进展，点击明确入口
- `js_null_hint` → JS 查询连续返回空，换查询逻辑
- `page_stalled_fatal` → 页面内容连续 4 次未变化，死胡同终止
- `replan_critical` → 连续 3+ 步操作失败，需调用 `update_todo` 重新规划
- `exploration_limit` → 连续 5 步未关联子任务，需制定明确计划
- `budget_warning` → 预算消耗警告（非终止），50%/75%/90%/95% 四档提醒

### 看到 MCP 日志但功能无关

MCP / Playwright / 外部 Chrome 是旧路径。当前默认浏览器控制来自 WebView2 自动化服务。

---

## BrowserSkills 独立库

`BrowserSkills/` 是从 Demo 项目中提取的独立 C# Class Library（`net8.0-windows`），依赖仅 `Microsoft.Web.WebView2`。

**包含模块**：

| 模块 | 内容 |
|------|------|
| `Core` | 浏览器自动化引擎、AI 工具路由器、JS 脚本、日志接口 |
| `Models` | 对话消息、工具定义、任务清单、回复解析器 |
| `Skills` | 原子/组合/策略技能定义与注册中心 |
| `Strategy` | 导航、定位、重试、上下文、恢复、隐私 6 种策略 |
| `Intelligence` | 上下文构建、任务状态机、自检测器 |

**构建**：

```bash
dotnet build BrowserSkills/BrowserSkills.csproj
```

> **注意**：BrowserSkills 是独立提取版本，Demo 项目当前保留自己的服务副本。修改 BrowserSkills 不会自动影响 Demo 项目。

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
| 自检测机制 | `AgentEventSelfHandler.cs` |
| 任务状态机 | `TaskStateMachine.cs` |
| UI 界面 | `Views/` 下的 XAML 文件 |
| **独立库（BrowserSkills）** | 同步修改 `BrowserSkills/` 下对应文件 |

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
