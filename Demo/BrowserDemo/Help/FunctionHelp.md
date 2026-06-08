# FunctionHelp.md — SmartAI Browser Demo 函数帮助文档

> 生成日期：2026-06-03
> 修订版本：1

---

## 命名空间：BrowserDemo.Services

### `Logger`
``` 
name : static void Logger.Debug(string message)
input : string message
output : void
effect : 使用 message 作为调试信息，内部进行控制台彩色输出与文件追加，返回 void 表示日志已写入
---
name : static void Logger.Info(string message)
input : string message
output : void
effect : 使用 message 作为常规信息，内部进行控制台彩色输出与文件追加，返回 void 表示日志已写入
---
name : static void Logger.Warning(string message)
input : string message
output : void
effect : 使用 message 作为警告信息，内部进行黄色控制台输出与文件追加，返回 void 表示日志已写入
---
name : static void Logger.Error(string message)
input : string message
output : void
effect : 使用 message 作为错误信息，内部进行红色控制台输出与文件追加，返回 void 表示日志已写入
---
name : static void Logger.Exception(string context, Exception ex)
input : string context, Exception ex
output : void
effect : 使用 context 和 ex 作为异常上下文与异常对象，内部进行错误日志输出并附带堆栈跟踪，返回 void 表示异常已记录
---
name : static IDisposable Logger.Trace(string signature)
input : string signature
output : IDisposable
effect : 使用 signature 作为函数签名，内部输出 ENTER 日志并返回 IDisposable 块，在 Dispose 时自动输出 EXIT 日志（含耗时），返回 IDisposable 表示追踪块
---
name : static void Logger.AllocConsole()
input : void
output : void
effect : 调用 Win32 AllocConsole 分配后台控制台窗口，设置窗口标题为调试控制台，返回 void 表示控制台已分配
---
name : static void Logger.CleanOldLogs(int keep)
input : int keep
output : void
effect : 使用 keep 作为保留数量，内部遍历 logs 目录删除最旧日志文件，返回 void 表示清理完成
```

### `AiClient`
```
name : Task<string> AiClient.SendMessageAsync(IEnumerable<ChatMessage> messages, CancellationToken ct)
input : IEnumerable<ChatMessage> messages, CancellationToken ct
output : Task<string>
effect : 使用 messages 作为对话消息列表，内部通过流式 API 合并所有回复块为完整文本，返回 Task<string> 表示完整回复文本
---
name : IAsyncEnumerable<string> AiClient.StreamMessageAsync(IEnumerable<ChatMessage> messages, CancellationToken ct)
input : IEnumerable<ChatMessage> messages, CancellationToken ct
output : IAsyncEnumerable<string>
effect : 使用 messages 作为对话消息列表，内部根据提供商类型构造 HTTP 请求并逐行解析 SSE 流，返回 IAsyncEnumerable<string> 表示流式文本块
---
name : Task<bool> AiClient.TestConnectionAsync(CancellationToken ct)
input : CancellationToken ct
output : Task<bool>
effect : 发送最小请求验证 API Key 有效性，内部根据提供商类型构造测试请求，返回 Task<bool> 表示连接是否成功
```

### `ConversationService`
```
name : static List<ConversationSummary> ConversationService.ListConversations()
input : void
output : List<ConversationSummary>
effect : 扫描对话存储目录读取所有 JSON 文件，内部解析文件名、消息数、首条预览，返回 List<ConversationSummary> 表示对话摘要列表
---
name : static void ConversationService.SaveConversation(string id, List<ChatMessage> messages)
input : string id, List<ChatMessage> messages
output : void
effect : 使用 id 作为文件名、messages 作为消息列表，内部序列化为 JSON 写入数据目录，返回 void 表示已保存
---
name : static List<ChatMessage>? ConversationService.LoadConversation(string id)
input : string id
output : List<ChatMessage>?
effect : 使用 id 作为会话标识，内部读取对应 JSON 文件并反序列化，返回 List<ChatMessage>? 表示消息列表或 null
---
name : static void ConversationService.DeleteConversation(string id)
input : string id
output : void
effect : 使用 id 作为会话标识，内部删除对应 JSON 文件，返回 void 表示已删除
```

## 命名空间：BrowserDemo.Models

### `BrowserViewModel`
```
name : TabInfo BrowserViewModel.AddNewTab(string? url)
input : string? url
output : TabInfo
effect : 使用 url 作为起始地址，内部创建 TabInfo 实例并添加到 Tabs 集合设为激活，返回 TabInfo 表示新标签页
---
name : void BrowserViewModel.CloseTab(object? id)
input : object? id
output : void
effect : 使用 id 作为标签 GUID，内部从 Tabs 集合移除并触发 TabClosed 事件、自动切换到相邻标签，返回 void 表示已关闭
---
name : void BrowserViewModel.NavigateToAddress()
input : void
output : void
effect : 获取 AddressText 智能补全 URL（补协议、转搜索），内部调用 NavigateRequested 事件，返回 void 表示已发起导航
```

### `ProviderInfo / ProviderManager`
```
name : static ObservableCollection<ProviderInfo> ProviderManager.GetAll()
input : void
output : ObservableCollection<ProviderInfo>
effect : 按预设显示顺序遍历已注册提供商字典，内部收集并返回 ObservableCollection<ProviderInfo> 表示按顺序排列的提供商列表
---
name : static ProviderInfo? ProviderManager.GetProvider(string key)
input : string key
output : ProviderInfo?
effect : 使用 key 作为提供商标识，内部在字典中查找并返回 ProviderInfo? 表示提供商元数据或 null
---
name : static List<ModelInfo> ProviderManager.GetModels(string providerKey)
input : string providerKey
output : List<ModelInfo>
effect : 使用 providerKey 作为标识，内部查询提供商并返回其模型列表，返回 List<ModelInfo> 表示模型列表
```

## 命名空间：BrowserDemo.ViewModels

### `ChatViewModel`
```
name : async void ChatViewModel.SendAsync()
input : void
output : void
effect : 获取 InputText 构造用户消息和 AI 占位消息，内部通过 IAiClient.StreamMessageAsync 流式填充 AI 回复、自动关联页面上下文、完成后自动保存，返回 void 表示消息已发送
---
name : void ChatViewModel.NewConversation()
input : void
output : void
effect : 生成新 GUID 清空消息列表，内部添加欢迎消息并刷新对话列表、触发 GC 回收，返回 void 表示对话已新建
---
name : void ChatViewModel.LoadConversation(object? id)
input : object? id
output : void
effect : 使用 id 作为会话标识字符串，内部加载 JSON 并填充消息列表，返回 void 表示对话已加载
---
name : void ChatViewModel.ApplySettings(AiSettings settings)
input : AiSettings settings
output : void
effect : 使用 settings 作为 AI 配置，内部同步到 AI 客户端并持久化到文件，返回 void 表示设置已应用
```

## 命名空间：BrowserDemo.Views

### `MainWindow`
```
name : async Task MainWindow.CreateWebViewForTabAsync(TabInfo tab, CoreWebView2Environment? env)
input : TabInfo tab, CoreWebView2Environment? env
output : Task
effect : 使用 tab 作为标签信息创建 WebView2 实例并绑定导航/标题/来源事件，内部注册 NavigationStarting/NavigationCompleted/DocumentTitleChanged/SourceChanged，返回 Task 表示 WebView 已就绪
---
name : void MainWindow.SwitchToWebView(Guid tabId)
input : Guid tabId
output : void
effect : 使用 tabId 作为目标标签，内部隐藏所有 WebView 仅显示目标并更新导航按钮状态，返回 void 表示视图已切换
---
name : void MainWindow.OpenAiSettings()
input : void
output : void
effect : 创建 AiClient 实例打开设置对话框，内部传入当前 ChatViewModel 的 AI 配置并在保存后同步，返回 void 表示设置对话框已展示
```

### `AiSettingsDialog`
```
name : AiSettings AiSettingsDialog.CollectSettings()
input : void
output : AiSettings
effect : 收集表单中服务商、API Key、模型、端点字段值，内部构造 AiSettings 实例并返回，返回 AiSettings 表示用户配置
---
name : async void AiSettingsDialog.TestConnection_Click(object sender, RoutedEventArgs e)
input : object sender, RoutedEventArgs e
output : void
effect : 点击测试按钮时临时应用表单设置调用 IAiClient.TestConnectionAsync，内部根据结果更新 TestResultText 文字与颜色，返回 void 表示测试已完成
```

---

## 命名空间：BrowserDemo.Services.Automation

### `WebView2AutomationBridge`
```
name : static string GetSelector(Dictionary subParams, Dictionary parameters)
input : Dictionary<string, object?> subParams, Dictionary<string, object?> parameters
output : string
effect : 从 subParams 和 parameters 中按优先级（selector > css_selector > element > target > id > query > css）查找 CSS 选择器字符串，若全部未找到则通过模糊推断匹配以 # . [ 开头的值，返回 string 表示解析到的选择器，空字符串表示未找到
---
name : static SkillExecutionResult Fail(string error)
input : string error
output : SkillExecutionResult
effect : 使用 error 作为错误描述文本，内部构造 Status=Failed 的 SkillExecutionResult，返回 SkillExecutionResult 表示失败结果
---
name : private static string BuildSafeElementJs(string selector, string actionBody, bool forceSelector)
input : string selector, string actionBody, bool forceSelector = false
output : string
effect : 使用 selector 作为 CSS 选择器（可能含 Playwright 非标准语法），actionBody 作为查找到元素后的操作 JS 代码（变量名 el），内部自动检测 :has-text/:contains/text=/xpath=/>>/:visible 等非标准模式并降级处理（文本查找/try-catch/错误提示），返回 string 表示完整的 JS IIFE 代码（含 try-catch 保护）
---
name : private static string BuildSafeElementAllJs(string selector, string actionBody)
input : string selector, string actionBody
output : string
effect : 功能同 BuildSafeElementJs 但使用 querySelectorAll 查找全部元素（变量名 els），返回 string 表示完整的 JS IIFE 代码
---
name : private static (bool IsValid, string? ErrorHint, string? CleanSelector, string? TextFallback) ValidateSelector(string selector)
input : string selector
output : (bool IsValid, string? ErrorHint, string? CleanSelector, string? TextFallback)
effect : 使用 selector 作为用户输入的 CSS 选择器，内部检测 {xpath=/text=/pi=/react=/id=} 引擎前缀、" >> "链式操作、:has-text(text)/:contains(text) 伪类、:visible 伪类，返回元组表示是否有效、错误提示、清理后的选择器、文本降级目标
---
name : private static string? ExtractPseudoText(string selector, string pseudoClass)
input : string selector, string pseudoClass
output : string?
effect : 使用 selector 作为 CSS 选择器、pseudoClass 作为伪类名（如 :has-text(），内部括号匹配提取引号内的文本内容，返回 string? 表示提取到的文本，null 表示未匹配
---
name : private static string BuildTextFindJs(string text, string candidatesSelector, string actionBody)
input : string text, string candidatesSelector, string actionBody
output : string
effect : 使用 text 作为目标文本、candidatesSelector 作为候选元素 CSS 选择器、actionBody 作为对匹配元素（变量名 el）的操作 JS，内部遍历所有候选元素精确+模糊匹配 innerText，返回 string 表示完整的 JS IIFE 代码
---
name : private static string BuildElementExistsJs(string selector)
input : string selector
output : string
effect : 使用 selector 作为 CSS 选择器，内部检测 :has-text/:contains 时改为 document.body.innerText 包含检查，标准选择器则用 querySelector+try-catch，返回 string 表示返回 true/false 的 JS 代码
---
name : private static string? RemovePseudoClass(string selector, string pseudoClass)
input : string selector, string pseudoClass
output : string?
effect : 使用 selector 作为 CSS 选择器、pseudoClass 作为伪类名，内部查找并移除该伪类及其括号参数，返回 string? 表示移除后的选择器，null 表示未找到该伪类
```

---

## 命名空间：BrowserDemo.Services

### `AiClient`
```
name : async IAsyncEnumerable<string> ExecuteConversationAsync(List messages, Func executeTool, int maxIterations, CancellationToken ct)
input : List<ChatMessage> messages, Func<string, Dictionary, Task<string>> executeTool, int maxIterations, CancellationToken ct
output : IAsyncEnumerable<string>
effect : 使用 messages 作为对话历史，executeTool 作为工具执行回调，maxIterations 作为最大迭代次数，ct 作为取消令牌，内部循环执行 AI 推理与工具调用直至完成文本回复或达到上限，接近上限时自动注入提醒消息，返回 IAsyncEnumerable<string> 表示流式文本块序列
---
name : private async IAsyncEnumerable<AiStreamEvent> ParseStreamRichAsync(HttpResponseMessage response, CancellationToken ct)
input : HttpResponseMessage response, CancellationToken ct
output : IAsyncEnumerable<AiStreamEvent>
effect : 使用 response 作为 HTTP SSE 流响应，ct 作为取消令牌，内部逐行读取 SSE 数据解析为 AiStreamEvent（含 30s 超时保护），返回 IAsyncEnumerable<AiStreamEvent> 表示流事件序列
```

---

## 命名空间：BrowserDemo.Services.Automation

### `AdbService`
```
name : AdbService(string? adbPath)
input : string? adbPath
output : AdbService
effect : 使用 adbPath 作为 adb 可执行文件路径（null 时自动查找），内部检查环境变量和常见 SDK 路径，返回 AdbService 实例
---
name : static string? FindAdb()
input : void
output : string?
effect : 按优先级查找 adb 可执行文件：1)PATH 环境变量 2)Android SDK platform-tools 3)常见安装目录，返回 string? 表示完整路径，null 表示未找到
---
name : async Task<(bool, string?, string?)> CheckDeviceAsync(CancellationToken ct)
input : CancellationToken ct
output : (bool Available, string? DeviceId, string? Error)
effect : 执行 adb devices 命令检查设备连接状态，内部解析输出检测 device/unauthorized 状态，返回元组表示连接是否成功、设备ID 或错误信息
---
name : async Task<(bool, List<SmsMessage>, string?)> GetRecentSmsAsync(int limit, CancellationToken ct)
input : int limit, CancellationToken ct
output : (bool Success, List<SmsMessage> Messages, string? Error)
effect : 使用 limit 作为获取数量，内部先通过 content://sms/inbox 查询系统短信数据库，失败时降级为 dumpsys notification 解析通知预览，返回元组表示是否成功、短信列表或错误信息
---
name : async Task<(bool, SmsMessage?, string?, string?)> WaitForVerificationCodeAsync(int timeoutMs, int pollIntervalMs, string? senderFilter, CancellationToken ct)
input : int timeoutMs, int pollIntervalMs, string? senderFilter, CancellationToken ct
output : (bool Success, SmsMessage? Message, string? Code, string? Error)
effect : 使用 timeoutMs 作为最大等待毫秒数，pollIntervalMs 作为轮询间隔，senderFilter 作为发送方过滤，内部持续轮询 GetRecentSmsAsync 并用正则匹配验证码模式（4-8 位数字），返回元组表示是否成功、短信消息、验证码字符串或错误信息
---
name : static string? ExtractVerificationCode(string text)
input : string text
output : string?
effect : 使用 text 作为短信正文，内部用正则匹配「验证码/校验码/code」等关键词后的 4-8 位数字串，返回 string? 表示提取到的验证码，null 表示未匹配
---
name : static bool IsLikelyVerificationCode(string text)
input : string text
output : bool
effect : 使用 text 作为短信正文，内部检查是否包含验证/校验/code/verif/登录/注册等关键词，返回 bool 表示是否可能是验证码短信
```

### `WebView2AutomationBridge`
```
name : async Task<SkillExecutionResult> ExecuteAdbSms(Dictionary<string, object?> parameters, CancellationToken ct)
input : Dictionary<string, object?> parameters, CancellationToken ct
output : Task<SkillExecutionResult>
effect : 使用 parameters 作为 AI 传入的参数（action/limit/timeout_ms/sender），ct 作为取消令牌，内部根据 action 分发到 AdbService 的对应方法（check_device/get_recent_sms/wait_for_code/get_phone_info），返回 Task<SkillExecutionResult> 表示执行结果（含验证码、短信列表等）
```

---

## 命名空间：BrowserDemo.Models

### `BasicSkillDefinition`
```
name : ToolDefinition ToToolDefinition()
input : void
output : ToolDefinition
effect : 将技能定义转换为 AI API 可用的 ToolDefinition，内部根据技能 Id 生成精确的参数提示 (description)、参数属性 (properties) 和参数描述，返回 ToolDefinition 表示 AI 可调用的函数定义
---
name : string BuildParamHint()
input : void
output : string
effect : 根据技能 Id 返回该技能 params 参数的精简使用提示（含参数名建议），包含键名与类型说明（如 selector: string），返回 string 表示参数提示文本
---
name : Dictionary BuildParamsProperties()
input : void
output : Dictionary<string, object?>
effect : 根据技能 Id 生成 params 的显式 JSON Schema properties（含 type 和 description），用于告知 AI 正确的参数键名与类型，返回 Dictionary 表示参数属性定义
```

---

## Bug 修复记录（2026-06-06）

### Bug 修复 #A: 导航到错误页（404/"页面不存在"）仍返回"✅ 成功"

```
触发条件: ExecuteNavigate 导航到 404、"您访问的页面不存在" 等错误页时，
         只记录 Warning 日志，仍返回 Success()。
         AI 收到"✅ 成功"后认为页面正常，继续发起更多工具调用（如多次尝试不同 URL），
         导致 24+ 轮无效循环而最终没有输出。
修复: 错误检测从 Logger.Warning 改为 return Fail(...)，让 AI 立即知道导航失败。
文件: Services/Automation/WebView2AutomationBridge.cs (ExecuteNavigate)
```

### Bug 修复 #B: skill_wait 的 wait_for_element/wait_for_text 忽略 selector

```
触发条件: AI 调用 wait(action=wait_for_element, selector=".card") 时，
         ExecuteWait 将其当作普通 wait，只检查 document.readyState。
         页面已加载时立即返回"✅ 成功(0ms)"，不会等待元素出现。
         AI 以为元素已存在，但在真正加载完成前提取内容，得到无搜索结果。
修复: wait_for_element 独立为单独 case，轮询 document.querySelector(sel) !== null
      wait_for_text 独立为单独 case，轮询 document.body.innerText.contains(text)
      均支持 15s 超时，超时返回 Fail。
文件: Services/Automation/WebView2AutomationBridge.cs (ExecuteWait)
```

### Bug 修复 #C: StatusMessage 指引提示框 5 秒后自动清除

```
触发条件: StatusMessage 在操作完成后持续显示最后的状态文本
         （如"AI 思考中…"、"就绪"等），没有自动清除机制，
         "指引提示框操作后没有自动删除，提示框一直存在"。
修复: 新增 DispatcherTimer，StatusMessage 设置后 5 秒自动清除
      （保留"错误"/"❌"/"⚠️"开头的重要状态）。
文件: Views/AiChatPanel.xaml.cs
```

### Bug 修复 #D: AI 回复时自动滚动到消息最下方

```
触发条件: AI 回复内容增加或新消息插入后，ScrollViewer 不自动滚动到底部，
         用户需手动拖动才能看到最新回复。
修复: 订阅 Messages.CollectionChanged 事件，
      CollectionChanged 触发时调用 MessageScroller.ScrollToBottom()。
文件: Views/AiChatPanel.xaml.cs
```

### Bug 修复 #E: Playwright 非标准选择器兼容与 JS 执行修复

```
触发条件: AI 使用 :has-text('课程')、:contains('文本')、text=、xpath=、>>、:visible
         等 Playwright 框架非标准 CSS 语法时，document.querySelector 抛出 DOMException，
         WebView2 返回 "null" → C# 端解析为空字符串 → 工具返回"✅ 成功"（静默失败）。
         AI 误以为操作成功但页面无变化，持续无效重试 24+ 轮，35 轮上限耗尽后强制结束。
修复:
  A) 新增 ValidateSelector() 检测 6 种 Playwright 引擎前缀、>>链式操作、:has-text/
     :contains/:visible 伪类，分别降级为文本查找/错误提示/自动忽略
  B) 新增 BuildSafeElementJs() 统一安全入口，替换 14 处手工 querySelector 拼接
  C) 新增 BuildTextFindJs() 通过 querySelectorAll + innerText 匹配实现文本降级
  D) 新增 BuildElementExistsJs() 用于 wait_for_element 场景
  E) DecodeJsResult() 对 "null"（JS 异常）返回空，但所有 JS 代码现均有 try-catch
     保护，返回 {error: "..."} 而非抛异常
  F) 所有 skill_* 工具描述的 selector 参数标注"不支持 Playwright 伪类"
文件: Services/Automation/WebView2AutomationBridge.cs;
      Models/BasicSkillDefinition.cs
```
