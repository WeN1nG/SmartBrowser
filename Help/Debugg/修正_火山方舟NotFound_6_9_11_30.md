# 修正_火山方舟NotFound_6_9_11_30

focus ：修正火山方舟 Coding Plan 配置后正式请求 404 NotFound，但 DeepSeek 正常工作

reason : 最新日志 (6-9-11-28-15) 对比两条路径：
- DeepSeek 正常：provider=deepseek, Bearer, OpenAI 格式, 无 tool_choice, 200 OK
- 火山方舟失败：provider=volcengine-ark, Bearer, OpenAI 格式, 强制 tool_choice, 404 NotFound
测试连接时 endpoint 显示 `https://ark.cn-beijing.volces.com/api/coding/v3` (200 OK)，但配置文件中保存的是 `https://ark.cn-beijing.volces.com/api/coding`（缺少 `/v3`），导致 `GetOpenAIChatCompletionsEndpoint()` 拼出 `https://ark.cn-beijing.volces.com/api/coding/chat/completions` → 404。

deepreason : 用户在设置对话框中填写的 endpoint 是 `https://ark.cn-beijing.volces.com/api/coding`（漏了 `/v3`）。`LoadSettings()` 中虽然有 ARK endpoint 自动补齐到 `/api/coding/v3` 的逻辑，但 `CollectSettings()` 从 `EndpointBox.Text` 收集时会拿到对话框显示的已补齐文本；然而如果用户最初保存的就是 `/api/coding`，持久化文件中就是缺 `/v3` 的版本。`NormalizeProviderProtocols()` 只修正了 provider 协议错配，没有修正 endpoint 路径不完整。`GetOpenAIChatCompletionsEndpoint()` 对 `/api/coding` 追加 `/chat/completions`，得到 `/api/coding/chat/completions`（应为 `/api/coding/v3/chat/completions`），所以 404。

solution : 在 `NormalizeProviderProtocol` 和 `NormalizeSettingsProtocol` 中增加火山方舟 Coding Plan endpoint 路径规范化：
- `https://ark.cn-beijing.volces.com/api/coding` → `https://ark.cn-beijing.volces.com/api/coding/v3`
- `https://ark.cn-beijing.volces.com/api/coding/chat/completions` → `https://ark.cn-beijing.volces.com/api/coding/v3/chat/completions`
同时保留 `GetOpenAIChatCompletionsEndpoint()` 的自动追加逻辑不变。

change :
- `Demo/BrowserDemo/Services/AiClient.cs`
  - 新增 `NormalizeArkCodingEndpoint(AiSettings)`，运行时自动修正缺 `/v3` 的火山 endpoint。
  - 在 `NormalizeSettingsProtocol()` 中调用，修正发生在 provider 协议判断之前。
- `Demo/BrowserDemo/Models/AiSettingsStore.cs`
  - 新增 `NormalizeArkCodingEndpoint(AiSettings)`，持久化修正缺 `/v3` 的火山 endpoint。
  - 在 `NormalizeProviderProtocol()` 中调用，Load/Save 时自动修正并持久化。
- `Help/FunctionHelp.md`
  - 记录 `NormalizeArkCodingEndpoint` 函数。
- `Help/EffectHelp.md`
  - 补充火山 endpoint 路径规范化流程。

keychangecode : {
```csharp
private static void NormalizeArkCodingEndpoint(AiSettings settings)
{
    var endpoint = settings.Endpoint?.Trim();
    if (string.IsNullOrWhiteSpace(endpoint)
        || !endpoint.Contains("ark.cn-beijing.volces.com", StringComparison.OrdinalIgnoreCase))
        return;

    var normalized = endpoint.TrimEnd('/');
    string? fixedEndpoint = null;
    if (normalized.EndsWith("/api/coding", StringComparison.OrdinalIgnoreCase))
        fixedEndpoint = normalized + "/v3";
    else if (normalized.EndsWith("/api/coding/chat/completions", StringComparison.OrdinalIgnoreCase))
        fixedEndpoint = normalized.Replace("/api/coding/chat/completions", "/api/coding/v3/chat/completions", StringComparison.OrdinalIgnoreCase);

    if (fixedEndpoint == null || string.Equals(endpoint, fixedEndpoint, StringComparison.OrdinalIgnoreCase))
        return;

    Logger.Warning($"火山方舟 Coding Plan endpoint 自动修正: {endpoint} → {fixedEndpoint}");
    settings.Endpoint = fixedEndpoint;
}
```
}

verification :
- `dotnet build Demo/BrowserDemo/BrowserDemo.csproj`：成功，0 警告，0 错误。
