═══ SmartAI Browser Debug Console ═══
[11:26:00.742][INF] Win32 控制台已分配
[11:26:00.742][INF] ═══════════════════════════════════════
[11:26:00.799][INF]   SmartAI Browser Demo 启动
[11:26:00.799][INF]   时间: 2026-06-08 11:26:00
[11:26:00.800][INF]   .NET: 8.0.27
[11:26:00.801][INF] ═══════════════════════════════════════
[11:26:00.801][INF] UI 主窗口已创建
[11:26:00.833][DBG] ? ENTER  MainWindow::ctor
[11:26:00.955][DBG] 已加载配置: provider=openai, model=gpt-4o
[11:26:00.956][DBG] AiClient: ContextBuilder 已初始化 (IsEnabled=True)
[11:26:01.034][INF] [Mcp] Node.js: F:\Nodejs\node.exe
[11:26:01.034][INF] [Mcp] MCP 目录: C:\CodeSpace\Objects\BrowserDemo\Tools\playwright-mcp\playwright-mcp-0.0.75
[11:26:01.035][INF] [Mcp] CDP 端点: http://localhost:9222
[11:26:01.037][DBG] ? EXIT   MainWindow::ctor  (201.5ms)
Unhandled exception. System.Windows.Markup.XamlParseException: 对类型“BrowserDemo.MainWindow”的构造函数执行符合指定的 绑定约束的调用时引发了异常。
 ---> System.IO.DirectoryNotFoundException: Playwright MCP 目录未找到: C:\CodeSpace\Objects\BrowserDemo\Tools\playwright-mcp\playwright-mcp-0.0.75
   at BrowserDemo.Services.Mcp.PlaywrightMcpClient..ctor(String cdpEndpointUrl) in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\Services\Mcp\PlaywrightMcpClient.cs:line 43
   at BrowserDemo.Services.Mcp.PlaywrightMcpClient..ctor() in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\Services\Mcp\PlaywrightMcpClient.cs:line 55
   at BrowserDemo.Services.Skills.SkillSystemIntegration..ctor(String cdpEndpointUrl) in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\Services\Skills\SkillSystemIntegration.cs:line 34
   at BrowserDemo.ViewModels.ChatViewModel..ctor(IAiClient aiClient) in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\ViewModels\ChatViewModel.cs:line 39
   at BrowserDemo.ViewModels.ChatViewModel..ctor() in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\ViewModels\ChatViewModel.cs:line 145
   at BrowserDemo.Models.BrowserViewModel..ctor() in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\Models\BrowserViewModel.cs:line 66
   at BrowserDemo.MainWindow..ctor() in C:\CodeSpace\Objects\Browser\Demo\BrowserDemo\MainWindow.xaml.cs:line 28
   at System.RuntimeType.CreateInstanceDefaultCtor(Boolean publicOnly, Boolean wrapExceptions)
   --- End of inner exception stack trace ---
   at System.Windows.Markup.XamlReader.RewrapException(Exception e, IXamlLineInfo lineInfo, Uri baseUri)
   at System.Windows.Markup.WpfXamlLoader.Load(XamlReader xamlReader, IXamlObjectWriterFactory writerFactory, Boolean skipJournaledProperties, Object rootObject, XamlObjectWriterSettings settings, Uri baseUri)
   at System.Windows.Markup.WpfXamlLoader.LoadBaml(XamlReader xamlReader, Boolean skipJournaledProperties, Object rootObject, XamlAccessLevel accessLevel, Uri baseUri)
   at System.Windows.Markup.XamlReader.LoadBaml(Stream stream, ParserContext parserContext, Object parent, Boolean closeStream)
   at System.Windows.Application.LoadBamlStreamWithSyncInfo(Stream stream, ParserContext pc)
   at System.Windows.Application.DoStartup()
   at System.Windows.Application.<.ctor>b__1_0(Object unused)
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)
   at System.Windows.Threading.DispatcherOperation.InvokeImpl()
   at MS.Internal.CulturePreservingExecutionContext.CallbackWrapper(Object obj)
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
--- End of stack trace from previous location ---
   at System.Threading.ExecutionContext.RunInternal(ExecutionContext executionContext, ContextCallback callback, Object state)
   at MS.Internal.CulturePreservingExecutionContext.Run(CulturePreservingExecutionContext executionContext, ContextCallback callback, Object state)
   at System.Windows.Threading.DispatcherOperation.Invoke()
   at System.Windows.Threading.Dispatcher.ProcessQueue()
   at System.Windows.Threading.Dispatcher.WndProcHook(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)
   at MS.Win32.HwndWrapper.WndProc(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam, Boolean& handled)
   at MS.Win32.HwndSubclass.DispatcherCallbackOperation(Object o)
   at System.Windows.Threading.ExceptionWrapper.InternalRealCall(Delegate callback, Object args, Int32 numArgs)
   at System.Windows.Threading.ExceptionWrapper.TryCatchWhen(Object source, Delegate callback, Object args, Int32 numArgs, Delegate catchHandler)
   at System.Windows.Threading.Dispatcher.LegacyInvokeImpl(DispatcherPriority priority, TimeSpan timeout, Delegate method, Object args, Int32 numArgs)
   at MS.Win32.HwndSubclass.SubclassWndProc(IntPtr hwnd, Int32 msg, IntPtr wParam, IntPtr lParam)
   at MS.Win32.UnsafeNativeMethods.DispatchMessage(MSG& msg)
   at System.Windows.Threading.Dispatcher.PushFrameImpl(DispatcherFrame frame)
   at System.Windows.Application.RunDispatcher(Object ignore)
   at System.Windows.Application.RunInternal(Window window)
   at BrowserDemo.App.Main()