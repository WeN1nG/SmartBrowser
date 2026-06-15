# BrowserSkills

浏览器自动化技能库 —— 从 SmartAI Browser Demo 中提取的独立 C# Class Library。

## 概述

提供 AI 驱动的浏览器自动化全套能力：

- **Core** — WebView2 自动化引擎（导航、点击、输入、快照、截图等）
- **Models** — 对话消息、工具定义、任务清单等数据模型
- **Skills** — 技能系统（原子/组合/策略技能定义与注册）
- **Strategy** — 策略处理器（导航、定位、重试、上下文、恢复、隐私）
- **Intelligence** — AI 智能层（上下文构建、任务状态机、自检测）

## 项目结构

```
BrowserSkills/
├── BrowserSkills.csproj          # net8.0-windows, 依赖 Microsoft.Web.WebView2
├── README.md
├── Core/
│   ├── BrowserAutomationService.cs       # WebView2 自动化服务
│   ├── BrowserAutomationToolRouter.cs    # AI 工具路由器（schema + 分发）
│   ├── AutomationScripts.cs              # JS 脚本生成器
│   └── ILogger.cs                        # 日志接口
├── Models/
│   ├── ToolDefinition.cs                 # AI 工具定义
│   ├── AiTodoItem.cs                     # 任务清单项
│   ├── ChatMessage.cs                    # 对话消息
│   ├── ToolCallData.cs                   # 工具调用数据
│   ├── UserQuestionInfo.cs               # ask_user 问题信息
│   ├── MessageRole.cs                    # 消息角色枚举
│   ├── AssistantResponseSections.cs      # 回复分区
│   ├── AssistantResponseParser.cs        # 回复解析器
│   └── StringExtensions.cs              # 字符串扩展
├── Skills/
│   ├── SkillModels.cs                    # 技能模型基类
│   ├── SkillRegistry.cs                  # 技能注册中心
│   ├── McpSkillDataProvider.cs           # 技能数据定义
│   └── SkillExecutionContext.cs         # 执行上下文
├── Strategy/
│   ├── IStrategyHandler.cs               # 策略接口
│   ├── NavigationStrategy.cs             # 导航策略
│   ├── LocateStrategy.cs                 # 定位策略
│   ├── RetryStrategy.cs                  # 重试策略
│   ├── ContextStrategy.cs                # 上下文策略
│   ├── RecoveryStrategy.cs               # 恢复策略
│   └── PrivacyStrategy.cs                # 隐私策略
└── Intelligence/
    ├── ContextBuilder.cs                 # 系统提示词构建器
    ├── TaskStateMachine.cs               # 子任务状态机
    └── AgentEventSelfHandler.cs          # 自检测器
```

## 构建

```bash
dotnet build BrowserSkills/BrowserSkills.csproj
```

## 使用示例

```csharp
using BrowserSkills.Core;
using BrowserSkills.Intelligence;
using BrowserSkills.Models;
using Microsoft.Web.WebView2.Wpf;

// 1. 创建自动化服务
var automation = new BrowserAutomationService();
automation.Initialize(dispatcher); // UI 线程

// 2. 绑定 WebView2
automation.BindWebView(tabId, webView);

// 3. 创建工具路由器
var router = new BrowserAutomationToolRouter(automation);

// 4. 获取工具定义并注册到 AI 上下文
var tools = router.GetToolDefinitions();
var contextBuilder = new ContextBuilder();
foreach (var tool in tools) contextBuilder.RegisterTool(tool);

// 5. 构建系统提示词
var prompt = contextBuilder.BuildSystemPrompt();

// 6. 执行自动化操作
var result = await automation.NavigateAsync("https://www.example.com");
var snapshot = await automation.GetSnapshotAsync();
```

## 与 Demo 项目的关系

BrowserSkills 是从 `Demo/BrowserDemo/` 中提取的独立库。Demo 项目可以引用它来复用浏览器自动化能力，而无需复制代码。
