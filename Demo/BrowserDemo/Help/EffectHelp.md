# EffectHelp.md — 功能实现模拟与路径追踪

> 生成日期：2026-06-03
> 修订版本：1

---

## 1. 启动流程

```
App.OnStartup()
  -> Logger.AllocConsole()           // 分配后台控制台
  -> Logger.Info("启动应用")          // 日志标记
  -> MainWindow.OnLoaded()
       -> CoreWebView2Environment.CreateAsync()
       -> CreateWebViewForTabAsync()  // 初始化 WebView2
       -> Logger.Info("WebView2 就绪")
```

## 2. 浏览器导航

```
用户输入地址 -> 回车
  -> AddressBar_KeyDown()
       -> BrowserViewModel.NavigateToAddress()
            -> Logger.Info("导航: {url}")
            -> NavigateRequested(url)
                 -> MainWindow.OnNavigateRequested(url)
                      -> WebView2.CoreWebView2.Navigate(url)
                           -> NavigationStarting (Logger.Debug)
                           -> NavigationCompleted (Logger.Info)
                                -> UpdateNavButtons()
                                -> 同步当前页面到 AI 上下文
```

## 3. 标签管理

```
btn:[新建标签]
  -> NewTabCommand
       -> BrowserViewModel.AddNewTab()
            -> Logger.Info("新建标签: {url}")
            -> TabInfo 添加到 Tabs
            -> TabActivated 事件
                 -> MainWindow.OnTabActivated()
                      -> CreateWebViewForTabAsync()
                      -> Logger.Debug("WebView已关联标签 {id}")

btn:[关闭标签]
  -> CloseTabCommand
       -> BrowserViewModel.CloseTab()
            -> Logger.Info("关闭标签: {title}")
            -> 从 Tabs 移除
            -> TabClosed 事件
                 -> MainWindow.OnTabClosed()
                      -> WebView2.Dispose()
                      -> Logger.Debug("WebView已释放")
```

## 4. AI 对话

```
用户输入消息 -> 发送
  -> ChatViewModel.SendAsync()
       -> Logger.Info("AI 请求开始")
       -> 添加用户消息到 Messages
       -> 添加当前页面上文（首次）
       -> 添加 AI 占位消息
       -> AiClient.StreamMessageAsync(messages)
            -> Logger.Debug("流式请求: provider={key} model={model}")
            -> BuildOpenAIRequest() / BuildAnthropicRequest()
            -> HttpClient.SendAsync()
                 -> 成功: ParseStreamAsync() 逐行解析
                 -> 失败: Logger.Error("API错误: {status}")
            -> 逐 chunk 填充 aiMsg.Content
       -> Logger.Info("AI 请求完成 ({token} tokens)")
       -> AutoSave() -> ConversationService.SaveConversation()
```

## 5. AI 设置

```
btn:[⚙ 设置]
  -> ChatViewModel.OpenSettingsRequested
       -> MainWindow.OpenAiSettings()
            -> new AiSettingsDialog()
            -> Logger.Info("打开设置对话框")

用户选择服务商 -> ProviderCombo_SelectionChanged
  -> Logger.Debug("切换服务商: {key}")
  -> 更新模型列表 UpdateModelList()
  -> 更新端点预览 / 认证方式

btn:[测试连接]
  -> TestConnection_Click()
       -> Logger.Info("测试连接: provider={key}")
       -> AiClient.TestConnectionAsync()
       -> Logger.Info("测试结果: {ok}")

btn:[保存]
  -> Save_Click()
       -> CollectSettings()
       -> AiClient.SaveSettings()
       -> Logger.Info("设置已保存: provider={key} model={model}")
```

## 6. 对话记录

```
自动触发 AutoSave()
  -> ConversationService.SaveConversation(id, messages)
       -> Logger.Debug("保存对话: {id} ({count}条)")
       -> 写 JSON 文件

加载历史对话
  -> LoadConversationCommand
       -> ConversationService.LoadConversation(id)
       -> Logger.Info("加载对话: {preview}")
       -> 填充 Messages
```

## 7. 日志系统自身

```
Logger.AllocConsole()
  -> Win32 AllocConsole()
  -> Console.Title = "SmartAI Browser — Debug Console"
  -> Logger.Write() 每次调用
       -> 控制台彩色输出
       -> File.AppendAllText(logfile)
       -> OnLog 事件通知 UI
```

## 8. AI 客户端 HTTP 流程

```
AiClient.StreamMessageAsync(messages)
  -> ConfigureHeaders()
       -> Bearer / x-api-key / OpenRouter 特殊头
  -> IsAnthropicProvider()
       -> true  : BuildAnthropicRequest()  (Anthropic 原生格式)
       -> false : BuildOpenAIRequest()     (OpenAI 兼容格式)
  -> HttpClient.SendAsync(..., ResponseHeadersRead)
       -> ReadStreamAsync()
            -> 逐行读取 ("data: {...}")
            -> ParseOpenAILine() / ParseAnthropicLine()
            -> yield return text chunk
```

## 9. 导航错误检测修复

```
AI 导航到 404/错误页
  -> ExecuteNavigate() 在 WebView2AutomationBridge
  -> 获取 pageTitle + pageText
  -> 匹配 errorIndicators[] ("404","页面不存在","无法访问"...)
  -> 之前: 只 Logger.Warning() 然后继续返回 Success()
  -> 修复后: 检测到错误指示符 → return Fail("导航失败: 页面返回错误 — ...")
       -> AI 收到 "❌ 失败: 导航失败: 页面返回错误 — "您访问的页面不存在""
       -> AI 知道导航出错了 → 不再继续无效尝试 → 节约工具调用机会
```

## 10. skill_wait wait_for_element/text 修复

```
AI 调用 wait(action=wait_for_element, selector=".card")
  -> ExecuteWait() 在 WebView2AutomationBridge
  -> 之前: 所有 case 都归入 wait_for_navigation → 只检查 readyState
       -> 页面已加载 = 立即返回 "✅ 成功(0ms)"
       -> AI 以为元素已存在 → 提取内容 → 得到空结果
  -> 修复后: wait_for_element 独立 case
       -> 轮询 document.querySelector(sel) !== null
       -> 15s 超时 → 找到返回 "✅ 元素已出现" / 超时返回 "❌ 等待超时"
     wait_for_text 独立 case
       -> 轮询 document.body.innerText.contains(text)
       -> 15s 超时 → 找到返回 "✅ 文本已出现" / 超时返回 "❌ 等待超时"
```

## 11. StatusMessage 指引提示框自动清除

```
AI 操作完成 → StatusMessage 被设置为 "就绪"/"AI 思考中…"
  -> AiChatPanel Loaded 时订阅 vm.PropertyChanged
  -> PropertyChanged(StatusMessage) 触发
       -> _statusClearTimer.Stop() + _statusClearTimer.Start()
       -> 5 秒后 Timer.Tick 触发
            -> 如果 StatusMessage 不是 "错误"/"❌"/"⚠️" 开头
                 -> StatusMessage = ""
            -> _statusClearTimer.Stop()
  -> 效果: 普通状态提示 5 秒后自动消失
         错误/警告状态保持不自动清除
```

## 12. AI 回复自动滚动

```
AI 开始回复 / 新消息到达
  -> Messages.CollectionChanged 事件触发
  -> AiChatPanel 订阅该事件
       -> Dispatcher.BeginInvoke(DispatcherPriority.Background)
            -> MessageScroller.ScrollToBottom()
  -> 效果: AI 回复的每个新字符/新消息到达时
          消息列表自动滚到最底部，用户无需手动拖动
```

## 13. Playwright 非标准 CSS 选择器兼容层

```
AI 使用 :has-text('课程') / text= / xpath= / >> / :visible 等 Playwright 语法
  -> ValidateSelector() 检测到非标准模式
       -> :has-text('课程') / :contains('xxx')
            -> ExtractPseudoText() 提取 "课程"
            -> BuildTextFindJs() 生成遍历 querySelectorAll + innerText 匹配的 JS
            -> 遍历 a/button/span/li/div/input... 候选元素
                 -> 精确匹配: 找到 → 执行操作（click/type/hover/focus/scroll）
                 -> 模糊匹配: 找到 → 执行操作
                 -> 未匹配: 返回 "{error: '按文本查找元素失败: 课程'}"
       -> text=xxx / text='xxx'
            -> 明确错误: "请使用 selector 或 text_content 参数代替 'text=' 前缀"
       -> xpath=...
            -> 明确错误: "不支持 XPath 选择器，请使用 CSS 选择器"
       -> css=xxx
            -> 自动剥离 css= 前缀，保留 xxx 作为标准 CSS 选择器
       -> >> 链式操作
            -> 明确错误: "不支持的 Playwright 链式操作符 '>>'"
       -> :visible
            -> RemovePseudoClass() 移除 :visible，保留其余 CSS
       -> 标准 CSS 选择器
            -> BuildSafeElementJs() 包裹 try-catch
                 -> document.querySelector() 成功 → 执行操作
                 -> querySelector 抛出 DOMException → 返回 "{error: 'CSS 选择器语法错误: ...'}"
                 -> querySelector 返回 null → 返回 "{error: '选择器未匹配到元素: ...'}"
  -> 效果: AI 使用任何选择器语法均不会静默失败
          非标准语法 → 降级/错误提示 → AI 收到明确负反馈 → 自动切换策略
          不再浪费 35 轮工具循环在无效选择器上
```

**覆盖的 Playwright 模式清单：**

| 模式 | 检测方法 | 处理方式 |
|------|----------|---------|
| `:has-text('xxx')` | `ExtractPseudoText(sel, ":has-text(")` | 文本查找降级 |
| `:has-text("xxx")` | 同上（双引号版本） | 文本查找降级 |
| `:contains('xxx')` | `ExtractPseudoText(sel, ":contains(")` | 文本查找降级 |
| `text=xxx` | `PlaywrightEnginePrefixes` 匹配 | 返回错误提示 |
| `xpath=...` | 同上 | 返回错误提示 |
| `css=...` | 同上（前缀剥离） | 剥离后标准 CSS 处理 |
| `>>` 链式 | `selector.Contains(">>")` | 返回错误提示 |
| `:visible` | `RemovePseudoClass(sel, ":visible")` | 自动移除并继续 |
| `:has()` | 不拦截（CSS 标准已支持） | 标准 querySelector |

**覆盖的 Execute 方法清单：**

| 方法 | 之前行为 | 修复后 |
|------|---------|--------|
| `ExecuteClick` | `:has-text` → DOMException → 返回空 "✅ 已点击" | 文本查找 + try-catch |
| `ExecuteType` | `:has-text` → DOMException → 抛异常 | `BuildSafeElementJs` |
| `ExecuteSelect` | `:has-text` → DOMException → 抛异常 | `BuildSafeElementJs` |
| `ExecuteScroll` | `:has-text` → DOMException → 返回空 | `BuildSafeElementJs` |
| `ExecuteExtract` | `:has-text` → DOMException → 返回空 | `BuildSafeElementJs` |
| `ExecuteWait` | `:has-text` → DOMException → 返回空 | `BuildElementExistsJs`（文本兜底） |
| `ExecuteForm` | `:has-text` → DOMException → 抛异常 | `BuildSafeElementJs` |
| `ExecuteHover` | `:has-text` → DOMException → 抛异常 | `BuildSafeElementJs` |
| `ExecuteQuery` | `:has-text` → DOMException → 抛异常 | `BuildSafeElementJs/AllJs` |
