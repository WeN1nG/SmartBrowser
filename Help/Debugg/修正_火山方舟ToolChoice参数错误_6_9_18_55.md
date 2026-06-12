# 修正_火山方舟ToolChoice参数错误_6_9_18_55

focus ：修正火山方舟 Coding Plan 正式工具循环 API 调用 BadRequest InvalidParameter

reason : 最新日志 `Demo/BrowserDemo/Log/6-9-18-51-23.log` 显示：
- provider=`volcengine-ark`，model=`kimi-k2.6`
- endpoint 已修正为 `https://ark.cn-beijing.volces.com/api/coding/v3`
- 认证方式为 Bearer Token
- 请求进入 OpenAI 格式
- 首轮规划门禁发送了强制 `tool_choice=update_todo`
- API 返回 `BadRequest / InvalidParameter`

关键日志：
```text
OpenAI: 强制下一步先调用规划工具 update_todo
API 请求失败: BadRequest — {"error":{"code":"InvalidParameter","message":"A parameter specified in the request is not valid ..."}}
```

deepreason : 火山方舟 Coding Plan 虽然是 OpenAI 兼容接口，但对强制 function `tool_choice` 参数兼容性不足。之前已经为 DeepSeek/Thinking 模型做了不发送 `tool_choice` 的降级，但 `SupportsForcedToolChoice()` 仍然把 `volcengine-ark` 当作支持强制 tool_choice，因此正式任务循环在第一轮规划门禁处被 API 拒绝。连接测试仍然成功，是因为测试请求不携带 tools/tool_choice。

solution : 将 `volcengine-ark` 加入强制 `tool_choice` 不兼容列表。后续火山方舟请求仍保留 tools，但不发送 `tool_choice` 字段；系统额外注入 `BuildPlanningToolReminder(forcedTool)`，提示模型当前轮必须先调用 `update_todo` / `start_subtask`。

change :
- `Demo/BrowserDemo/Services/AiClient.cs`
  - 修改 `SupportsForcedToolChoice(ProviderInfo?)`：`provider?.Key is "deepseek" or "volcengine-ark"` 时返回 false。
- `Help/FunctionHelp.md`
  - 更新 `SupportsForcedToolChoice` 说明，记录火山方舟不兼容强制 tool_choice。
- `Help/EffectHelp.md`
  - 更新规划门禁兼容流程，记录 DeepSeek/火山方舟/Thinking 模型省略 `tool_choice`。

keychangecode : {
```csharp
private bool SupportsForcedToolChoice(ProviderInfo? provider)
{
    if (provider?.Key is "deepseek" or "volcengine-ark")
        return false;

    var model = Settings.Model ?? string.Empty;
    if (model.Contains("reason", StringComparison.OrdinalIgnoreCase)
        || model.Contains("thinking", StringComparison.OrdinalIgnoreCase))
        return false;

    return true;
}
```
}

verification :
- `dotnet build Demo/BrowserDemo/BrowserDemo.csproj`：成功，0 警告，0 错误。

note :
- 这会避免火山方舟因强制 `tool_choice` 报 `InvalidParameter`。
- 降级后无法由 API 层强制规划工具，但仍会通过系统提示和工具描述要求模型先调用规划工具。
