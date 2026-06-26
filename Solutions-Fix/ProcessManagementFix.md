# 浏览器自动化进程管理代码 — 修复方案

## Context

浏览器自动化系统的工具循环中存在 4 个正确性和健壮性问题，需要在 `AiClient`、`BrowserAutomationService`、`BrowserAutomationToolRouter` 等核心文件中修复。

---

## Fix 1: 修复 `actionOk` 判断逻辑（P0-3 — 严重）

**文件**: `Demo/BrowserDemo/Services/AiClient.cs`
**位置**: 约第 1282-1285 行

### 问题

当前代码用字符串包含判断成功/失败：

```csharp
var actionOk = !string.IsNullOrEmpty(toolResult) &&
               !toolResult.Contains("error", StringComparison.OrdinalIgnoreCase) &&
               !toolResult.Contains("失败", StringComparison.OrdinalIgnoreCase);
```

`BrowserAutomationToolRouter.Format()` 返回的 JSON 格式为：
```json
{"ok":true, "data":"...", "error":null, "url":"...", "ms":123}
```

当 `error` 字段存在但值为 `null` 时，`Contains("error")` 会**误判为失败**。成功的 `browser_snapshot` 结果会被误计入连续失败计数，在 3 次后触发 `replan_needed` 警告。

### 修复

**第 1 步** — 替换判断行（约第 1282-1285 行）：

```diff
-                    var actionOk = !string.IsNullOrEmpty(toolResult) &&
-                                   !toolResult.Contains("error", StringComparison.OrdinalIgnoreCase) &&
-                                   !toolResult.Contains("失败", StringComparison.OrdinalIgnoreCase);
+                    var actionOk = DetermineActionOk(toolResult);
```

**第 2 步** — 在 `AiClient` 类的 `// ========== 辅助方法` 区域新增私有方法：

```csharp
/// <summary>
/// 判断工具执行结果是否代表成功。
/// 使用 JSON 解析而非字符串包含，避免 {"ok":true,"error":null} 被误判为失败。
/// </summary>
private static bool DetermineActionOk(string? toolResult)
{
    if (string.IsNullOrWhiteSpace(toolResult))
        return false;

    // 快速路径：纯文本结果中明确包含失败关键词
    if (toolResult.Contains("失败", StringComparison.OrdinalIgnoreCase)
        && !toolResult.Contains("\"error\":null", StringComparison.OrdinalIgnoreCase)
        && !toolResult.Contains("\"error\": null", StringComparison.OrdinalIgnoreCase))
        return false;

    // JSON 路径：解析 ok 字段和 error 字段
    try
    {
        using var doc = JsonDocument.Parse(toolResult);
        var root = doc.RootElement;

        // 有 ok=false → 失败
        if (root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.False)
            return false;

        // 没有 ok 字段但有 error 字段且有值 → 失败
        if (root.TryGetProperty("error", out var errorEl)
            && errorEl.ValueKind != JsonValueKind.Null
            && errorEl.ValueKind != JsonValueKind.Undefined)
            return false;

        // 其他情况视为成功
        return true;
    }
    catch
    {
        // 非 JSON 文本，回退到旧逻辑
        return !toolResult.Contains("失败", StringComparison.OrdinalIgnoreCase);
    }
}
```

---

## Fix 2: 规划门禁终止时返回用户消息（P0-2 — 严重）

**文件**: `Demo/BrowserDemo/Services/AiClient.cs`
**位置**: 约第 928-936 行

### 问题

规划门禁连续 5 次触发后直接 `yield break`，**不返回任何提示给用户**。用户会看到 AI 突然消失，没有解释。

### 修复

```diff
-                    if (_consecutivePlanningGateTrips >= 5)
-                    {
-                        Logger.Warning($"规划门禁连续 {_consecutivePlanningGateTrips} 次触发仍未生效，终止请求");
-                        _consecutivePlanningGateTrips = 0;
-                        yield break;
-                    }
+                    if (_consecutivePlanningGateTrips >= 5)
+                    {
+                        var msg = "⛔ 系统已中止本次工具调用：AI 连续 5 轮未按要求调用规划工具。请检查任务状态或重新开始。";
+                        Logger.Warning($"规划门禁连续 {_consecutivePlanningGateTrips} 次触发仍未生效，终止请求");
+                        _consecutivePlanningGateTrips = 0;
+                        yield return msg;
+                        yield break;
+                    }
```

---

## Fix 3: 清理 `AllToolNames` 中的僵尸条目（P2-1 — 低优）

**文件**: `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`
**位置**: 第 67-76 行

### 问题

`AllToolNames` 包含 20 个工具名，但 `browser_get_dom_text_hash` 未在 Router 中注册，也没有对应的 `InvokeAsync` case，是僵尸条目。

### 修复

```diff
     public static IReadOnlySet<string> AllToolNames => new HashSet<string>
     {
         "browser_navigate", "browser_back", "browser_forward", "browser_reload",
         "browser_snapshot", "browser_click", "browser_type", "browser_hover",
         "browser_select_option", "browser_scroll", "browser_press_key",
         "browser_screenshot", "browser_js", "browser_wait",
         "browser_wait_for", "browser_fill_form", "browser_switch_tab",
-        "browser_click_by_hash", "browser_scroll_to_element", "browser_click_at",
-        "browser_get_dom_text_hash"
+        "browser_click_by_hash", "browser_scroll_to_element", "browser_click_at"
     };
```

---

## Fix 4: 消除 `RunOnUITimeoutAsync` 中 `.GetResult()` 死锁风险（P1-1 — 中等）

**文件**: `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`
**位置**: 第 994-1024 行

### 问题

当前代码在 UI 线程 lambda 中使用 `.GetAwaiter().GetResult()` 同步等待异步操作：

```csharp
var op = wv.Dispatcher.InvokeAsync(() =>
{
    ...
    return operation(wv).GetAwaiter().GetResult();  // ← 同步阻塞
}, DispatcherPriority.Normal);
```

如果 `operation` 内部尝试 `Dispatcher.InvokeAsync`（嵌套调度），会导致**死锁**。虽然当前所有调用方通过 `SemaphoreSlim` 串行化且不会嵌套调度，但这是脆弱反模式。

### 修复

替换整个 `RunOnUITimeoutAsync` 方法（第 994-1024 行）：

```diff
-    private static async Task<AutomationResult> RunOnUITimeoutAsync(
-        WebView2 wv,
-        Func<WebView2, Task<AutomationResult>> operation,
-        int timeoutMs)
-    {
-        // ★ 关键：这里使用 Func<AutomationResult> 重载（不是 async () => {}）
-        // 这样 InvokeAsync 匹配 InvokeAsync<AutomationResult> → 返回 DispatcherOperation<AutomationResult>
-        // op.Task 的类型就是 Task<AutomationResult>，cast 不会失败
-        var op = wv.Dispatcher.InvokeAsync(() =>
-        {
-            if (wv.CoreWebView2 == null)
-                return AutomationResult.Fail("WebView2 CoreWebView2 未就绪");
-
-            // 焦点切换到 WebView2（AI 操作前）
-            try { wv.Focus(); Keyboard.Focus(wv); } catch { }
-
-            return operation(wv).GetAwaiter().GetResult();
-        }, DispatcherPriority.Normal);
-
-        // op 是 DispatcherOperation<AutomationResult>，op.Task 是 Task<AutomationResult>
-        var opTask = op.Task;
-        var delayTask = Task.Delay(timeoutMs);
-        var completed = await Task.WhenAny(opTask, delayTask);
-
-        if (completed == opTask)
-        {
-            return await opTask;
-        }
-
-        throw new TimeoutException();
-    }
+    private static async Task<AutomationResult> RunOnUITimeoutAsync(
+        WebView2 wv,
+        Func<WebView2, Task<AutomationResult>> operation,
+        int timeoutMs)
+    {
+        if (wv.CoreWebView2 == null)
+            return AutomationResult.Fail("WebView2 CoreWebView2 未就绪");
+
+        try { wv.Focus(); Keyboard.Focus(wv); } catch { }
+
+        // ★ 使用 async lambda 确保匹配 InvokeAsync<Task<AutomationResult>>，
+        //   避免 .GetResult() 同步阻塞导致的潜在死锁
+        var op = wv.Dispatcher.InvokeAsync(async () => await operation(wv), DispatcherPriority.Normal);
+
+        var opTask = op.Task;
+        var delayTask = Task.Delay(timeoutMs);
+        var completed = await Task.WhenAny(opTask, delayTask);
+
+        if (completed == opTask)
+            return await opTask;
+
+        throw new TimeoutException();
+    }
```

关键变化：
1. 移除 `() => operation(wv).GetAwaiter().GetResult()` 的同步阻塞
2. 改为 `async () => await operation(wv)` — 匹配 `Dispatcher.InvokeAsync(Func<Task<T>>)` 重载
3. 将 CoreWebView2 检查和 Focus 移到 InvokeAsync 外部，避免在 UI 线程 lambda 中做前置检查

---

## 验证

1. **编译验证**:
   ```bash
   cd Demo
   dotnet build BrowserDemo/BrowserDemo.csproj
   dotnet format BrowserDemo/BrowserDemo.csproj --verify-no-changes
   ```

2. **功能验证**:
   - 启动应用，让 AI 执行浏览器操作，确认成功的快照结果（`{"ok":true,"error":null}`）不会被误判为失败
   - 触发规划门禁场景（AI 不调用 update_todo），确认 5 轮后返回用户可见的终止消息
   - 确认 `browser_get_dom_text_hash` 不再出现在 `AllToolNames` 中
   - 长时间运行自动化任务，确认无 UI 线程死锁
