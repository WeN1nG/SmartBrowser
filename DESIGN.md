# Volcengine ARK AI 配置失败问题解决方案

> 版本：1.0  
> 日期：2026-06-08  
> 目标代码：`Demo/BrowserDemo/`  
> 需求来源：`Pro.md`

---

## 1. 项目概述

本方案针对 SmartAI Browser Demo 的 AI 模型配置与连接测试问题：DeepSeek 按官方文档配置后可正常使用，但绑定火山引擎 ARK 相关 API 后提示无法接通。目标用户是需要在本地浏览器 Demo 中切换 DeepSeek、火山引擎 ARK 等模型服务的开发者和测试者。

核心价值是把“自定义端点配置错误导致 404 / 协议选择错误导致请求格式不匹配”变成可诊断、可修复、可验证的内置配置能力，避免用户反复试错 endpoint、provider 和 model ID。

### 1.1 已定位的根因

根据最新日志与 AI 配置文件，问题不是网络不可达，而是配置与请求路径不匹配：

1. **ARK 旧配置曾保存为 `provider_key = anthropic`**  
   这会让 `AiClient.IsAnthropicProvider()` 走 Anthropic native `/v1/messages` 请求格式和 `x-api-key` 认证头，而火山引擎 ARK 的常规兼容调用应走 OpenAI-compatible chat completions 格式。

2. **后续改为 `custom` 后，请求 endpoint 仍然错误**  
   日志显示连接测试实际请求过：

   ```text
   https://ark.cn-beijing.volces.com/api/coding
   https://ark.cn-beijing.volces.com/api/coding/v3
   ```

   服务端返回：

   ```text
   HTTP 404
   ```

   对火山方舟 Coding Plan，UI 中应填写官方 OpenAI Base URL，例如：

   ```text
   https://ark.cn-beijing.volces.com/api/coding/v3
   ```

3. **当前连接测试日志只记录 HTTP 状态码，不记录响应体摘要**  
   因此用户只看到“无法接通”，无法判断是 401、403、404、模型名错误还是 endpoint 路径错误。

4. **自定义服务商过于自由，缺少服务商级提示与自动补全**  
   火山方舟 Coding Plan 官方给的是 OpenAI Base URL：`/api/coding/v3`，而当前手写客户端此前会把配置值当作完整 chat completions endpoint 使用，导致没有自动补上 `/chat/completions`。

---

## 2. 技术栈选型

| 层面 | 选择 | 理由 |
|------|------|------|
| 编程语言 | C# / .NET 8 | 项目当前为 `net8.0-windows` WPF，保持一致，避免引入新运行时。 |
| UI 框架 | WPF | 模型配置面板已由 `AiModelSelectionDialog` + `AiSettingsDialog` 实现，继续小范围修改现有 UI。 |
| HTTP 客户端 | 现有 `HttpClient` / `AiClient` | 当前已支持 OpenAI-compatible 和 Anthropic-native SSE，不需要引入 SDK。 |
| 配置存储 | 现有 `ai_settings.json` | 已支持多 profile；ARK 只需要 `ProviderKey`、`ApiKey`、`Model`、`Endpoint`，不需要变更存储格式。 |
| 日志 | 现有 `Logger` | 用于定位连接失败状态码、endpoint、provider、model；只增强失败摘要，不记录敏感 API Key。 |
| 构建验证 | `dotnet build Demo/BrowserDemo/BrowserDemo.csproj` | 仓库无 `.sln` 和测试项目，构建是最小自动验证。 |

运行环境保持不变：Windows 10/11、.NET 8 SDK、WebView2 Runtime。

---

## 3. 系统架构

本次修复不改变整体架构，只在 AI Provider 配置层和 AI Client 诊断层做增量增强。

```text
用户打开模型配置
  ↓
AiModelSelectionDialog
  ↓ 添加/编辑 profile
AiSettingsDialog
  ├─ ProviderManager.GetAll() 提供服务商列表
  ├─ 用户选择 Volcengine ARK 或 custom
  ├─ 校验 / 自动修正 endpoint
  └─ 返回 AiSettings
  ↓ 保存
AiSettingsStore -> ai_settings.json
  ↓ 使用 / 测试连接
AiClient
  ├─ 根据 ProviderKey 选择 OpenAI-compatible 或 Anthropic-native
  ├─ ConfigureHeaders()
  ├─ BuildOpenAITestRequest() / BuildOpenAIRequest()
  └─ Logger 记录状态码与错误响应摘要
```

### 3.1 核心模块职责

| 模块 | 职责 |
|------|------|
| `ProviderManager` | 增加火山引擎 ARK 内置 provider，给出正确默认 endpoint 和模型填写提示。 |
| `AiSettingsDialog` | 在 UI 中选择 ARK；校验 endpoint；对常见错误路径给出提示或自动修正建议。 |
| `AiSettingsStore` | 继续按现有 schema 保存 profile，无需修改。 |
| `AiClient` | ARK 走 OpenAI-compatible 分支；增强连接测试失败日志，记录响应体摘要但不泄露 API Key。 |
| `Logger` | 复用现有日志能力，无需新组件。 |

### 3.2 架构选择理由

- 不引入火山 SDK：当前客户端只需要标准 chat completions，手写 HTTP 已足够。
- 不改 `AiSettings` schema：ARK 的差异可由 provider 默认 endpoint 和 UI 校验表达。
- 不扩展 Anthropic-native 路径：ARK 不是 Anthropic provider，配置成 `anthropic` 是错误来源之一。

---

## 4. 目录结构

本次只修改现有文件，不新增复杂目录。

```text
Demo/BrowserDemo/
├── Models/
│   ├── ProviderInfo.cs
│   │   # 增加 volcengine-ark / ARK provider 元数据：默认 endpoint、Badge、示例模型提示
│   ├── AiSettings.cs
│   │   # 不修改；继续使用 ProviderKey / ApiKey / Model / Endpoint
│   └── AiSettingsStore.cs
│       # 不修改；继续保存 ai_settings.json
│
├── Views/
│   ├── AiSettingsDialog.xaml
│   │   # 增强 endpoint 提示文案，说明 ARK Coding Plan 应使用 /api/coding/v3 Base URL
│   └── AiSettingsDialog.xaml.cs
│       # 增加 ARK/custom endpoint 校验与常见错误提示
│
└── Services/
    └── AiClient.cs
        # 增强连接测试失败日志；确保 ARK 走 OpenAI-compatible Bearer 分支
```

项目根目录：

```text
DESIGN.md
# 本解决方案文档
```

---

## 5. 核心接口设计

### 5.1 Provider 定义

在 `ProviderManager.RegisterAll()` 中增加内置服务商：

```csharp
Register("volcengine-ark", "Volcengine ARK（火山方舟）",
    "https://ark.cn-beijing.volces.com/api/coding/v3",
    "Bearer", "火山方舟",
    new ModelInfo("", "请输入 Endpoint/模型 ID", 0, "openai-compatible")
);
```

实际实现时可以不放空 `ModelInfo`，避免空模型被误选；更推荐不提供固定模型列表，让用户在可编辑 `ModelCombo` 中填写自己的 ARK endpoint/model ID。

### 5.2 Provider 选择规则

`AiClient` 当前规则：

```csharp
private bool IsAnthropicProvider() => Settings.ProviderKey == "anthropic";
```

因此新增的 `volcengine-ark` 会自然走 OpenAI-compatible 路径，无需额外 request builder。

推荐约定：

```text
provider_key = volcengine-ark
endpoint     = https://ark.cn-beijing.volces.com/api/coding/v3
model        = 用户在火山方舟控制台获得的模型/endpoint ID
auth         = Bearer <API Key>
```

### 5.3 Endpoint 校验接口

在 `AiSettingsDialog.xaml.cs` 中增加或扩展校验函数：

```csharp
private static bool ValidateSettings(AiSettings settings, out string error)
```

规则：

1. `Model` 必填。
2. `custom` 和 `volcengine-ark` 必须有 absolute URI endpoint。
3. scheme 必须是 `http` 或 `https`。
4. `volcengine-ark` 建议 endpoint 使用官方 Coding Plan OpenAI Base URL：`/api/coding/v3`。
5. 如果用户填入通用方舟 `/api/v3` 地址，提示该地址不会消耗 Coding Plan 额度，可能产生额外费用。

提示文案：

```text
火山方舟 Coding Plan 的 OpenAI Base URL 应填写：
https://ark.cn-beijing.volces.com/api/coding/v3
请勿填写 /api/v3（通用方舟，会产生额外费用）。
```

### 5.4 失败诊断接口

增强 `AiClient.TestConnectionAsync()`：

```csharp
using var response = await _http.SendAsync(...);
var error = ok ? "" : await response.Content.ReadAsStringAsync(ct);
Logger.Info($"连接测试: HTTP {(int)response.StatusCode} → {(ok ? "成功" : "失败")}");
if (!ok)
    Logger.Warning($"连接测试响应: {TruncateError(error)}");
```

约束：

- 不记录 API Key。
- 响应体只截断记录，避免日志过大。
- UI 仍可保持简单的“连接失败，请检查 API Key、模型名和端点”。

### 5.5 配置文件格式

无需新增字段。正确 ARK profile 示例（省略真实 API Key）：

```json
{
  "display_name": "ARK",
  "provider_key": "volcengine-ark",
  "api_key": "<redacted>",
  "model": "<your-ark-model-or-endpoint-id>",
  "endpoint": "https://ark.cn-beijing.volces.com/api/coding/v3"
}
```

---

## 6. 数据流设计

### 6.1 成功配置流程

```text
用户点击模型设置
  ↓
AiModelSelectionDialog.LoadRows()
  ↓
用户添加/编辑 ARK profile
  ↓
AiSettingsDialog.ProviderCombo 选择 Volcengine ARK
  ↓
EndpointBox 自动显示默认 endpoint 或提示用户填写
  ↓
用户填写 API Key + model
  ↓
ValidateSettings()
  ├─ provider 正确：volcengine-ark
  ├─ endpoint 正确：/api/v3/chat/completions
  └─ model 非空
  ↓
AiModelSelectionDialog.Save_Click()
  ↓
ai_settings.json
  ↓
ChatViewModel.ApplySettings(store.ResolveActive())
```

### 6.2 连接测试流程

```text
用户点击“测试连接”
  ↓
AiSettingsDialog.CollectSettings()
  ↓
ValidateSettings()
  ↓
_aiClient.Settings = settings
  ↓
AiClient.TestConnectionAsync()
  ├─ provider_key != anthropic
  ├─ ConfigureHeaders(): Authorization: Bearer <API Key>
  ├─ BuildOpenAITestRequest()
  ├─ POST https://ark.cn-beijing.volces.com/api/coding/v3/chat/completions
  └─ 2xx = 成功；非 2xx = 日志记录响应摘要
```

### 6.3 错误处理路径

| 错误 | 当前表现 | 修复后表现 |
|------|----------|------------|
| `provider_key = anthropic` | 走 Anthropic native 格式，请求协议错误 | UI 中提供 ARK provider；旧 ARK-like 配置可提示切换到 ARK/custom。 |
| endpoint 为 `/api/coding` | HTTP 404，只显示无法接通 | 保存/测试前提示 endpoint 不完整，建议 `/api/coding/v3`。 |
| endpoint 为 `/api/coding/v3` | 旧客户端会直接 POST Base URL 导致 404 | 新客户端自动补 `/chat/completions`。 |
| API Key 错误 | 连接失败 | 日志记录 401/403 响应摘要，UI 提示检查 Key。 |
| model ID 错误 | 可能 400/404 | 日志记录响应摘要，UI 提示检查模型名。 |
| 没点外层“保存” | 内层显示已保存但配置文件未更新 | 保持现有状态提示“已更新模型，点击保存后生效”；可考虑增强提示。 |

---

## 7. 数据存储设计

### 7.1 存储位置

AI 配置文件仍位于运行输出目录：

```text
<AppDomain.CurrentDomain.BaseDirectory>/ai_settings.json
```

开发调试时常见路径：

```text
Demo/BrowserDemo/bin/Debug/net8.0-windows/ai_settings.json
```

发布包路径可能是：

```text
Demo/publish/with-dotnet/ai_settings.json
```

### 7.2 存储结构

继续使用现有 `AiSettingsStore`：

```json
{
  "profiles": [
    {
      "id": "...",
      "display_name": "ARK",
      "provider_key": "volcengine-ark",
      "api_key": "<redacted>",
      "model": "<your-model>",
      "endpoint": "https://ark.cn-beijing.volces.com/api/coding/v3"
    }
  ],
  "active_id": "...",
  "default_id": "..."
}
```

### 7.3 为什么不新增数据结构

- `ProviderKey` 能表达 ARK 服务商。
- `Endpoint` 能保存 ARK chat completions 地址。
- `Model` 能保存火山方舟控制台提供的模型/endpoint ID。
- `ApiKey` 能保存访问密钥。

因此无需新增 `CustomProvider` 表、无需迁移配置文件。

### 7.4 配置迁移建议

可选增强：加载或编辑旧 profile 时，如果发现：

```text
display_name 包含 ark/ARK
endpoint 包含 ark.cn-beijing.volces.com
provider_key = anthropic
```

则在 UI 层提示用户：

```text
检测到该配置像火山方舟 ARK，但当前服务商是 Anthropic。建议切换为 Volcengine ARK。
```

不建议静默迁移，避免误改用户配置。

---

## 8. 分步实现计划

### 阶段 1：复现与诊断固化

**目标**：让日志明确说明 ARK 失败原因。

需要实现：

- 修改 `Demo/BrowserDemo/Services/AiClient.cs`
  - `TestConnectionAsync()` 在非 2xx 时读取响应体摘要并写入 `Logger.Warning`。
  - 继续避免记录 API Key。

前置依赖：无。

验收标准：

- 使用错误 endpoint 测试时，日志能看到 HTTP 404 和服务端响应摘要。
- `dotnet build` 成功。

### 阶段 2：增加 Volcengine ARK 内置 provider

**目标**：用户不再需要把 ARK 伪装成 Anthropic 或完全自定义。

需要实现：

- 修改 `Demo/BrowserDemo/Models/ProviderInfo.cs`
  - 新增 `volcengine-ark` provider。
  - 默认 endpoint：`https://ark.cn-beijing.volces.com/api/coding/v3`。
  - AuthType：`Bearer`。
  - Badge：`火山方舟`。
  - 放入 `GetAll()` 显示顺序，建议位于 DeepSeek 或 custom 附近。

前置依赖：阶段 1 可并行。

验收标准：

- 模型设置面板服务商下拉框出现 `Volcengine ARK（火山方舟）`。
- 选择后连接测试走 OpenAI-compatible 分支，而不是 Anthropic 分支。

### 阶段 3：增加 ARK endpoint 校验与提示

**目标**：在用户点击测试/保存前拦截 `/api/coding` 和通用 `/api/v3` 这类不适合 Coding Plan 的路径，并允许 `/api/coding/v3` Base URL。

需要实现：

- 修改 `Demo/BrowserDemo/Views/AiSettingsDialog.xaml.cs`
  - 扩展 `ValidateSettings()`。
  - 对 `provider_key == "volcengine-ark"` 执行 ARK 专项校验。
  - 对 `custom` 中包含 `ark.cn-beijing.volces.com` 的 endpoint 也给出同类提示。

- 修改 `Demo/BrowserDemo/Views/AiSettingsDialog.xaml`
  - endpoint 提示文字加入 ARK 示例。

前置依赖：阶段 2。

验收标准：

- 输入 `https://ark.cn-beijing.volces.com/api/coding` 时，测试/保存被阻止并提示正确地址。
- 输入 `https://ark.cn-beijing.volces.com/api/coding/v3` 时同样被阻止。
- 输入 `https://ark.cn-beijing.volces.com/api/coding/v3` 可通过本地校验。

### 阶段 4：配置文件修正与人工验证

**目标**：修正当前本机 ARK profile，并确认 DeepSeek 不受影响。

需要操作：

- 通过 UI 编辑名为 ARK/ark 的 profile：
  - Provider：`Volcengine ARK（火山方舟）`
  - Endpoint：`https://ark.cn-beijing.volces.com/api/coding/v3`
  - Model：使用火山方舟控制台给出的模型/endpoint ID
  - API Key：保持用户自己的 ARK key
- 在外层 `AiModelSelectionDialog` 点击“保存”，确保写入 `ai_settings.json`。

前置依赖：阶段 1-3。

验收标准：

- ARK profile 保存后 `provider_key` 不再是 `anthropic`。
- ARK endpoint 不再是 `/api/coding` 或 `/api/coding/v3`。
- DeepSeek profile 仍能正常连接。
- ARK 使用正确 key/model/endpoint 时连接测试返回 2xx。

### 阶段 5：最终构建验证

**目标**：确认代码可编译。

执行：

```bash
dotnet build C:/CodeSpace/Objects/Browser/Demo/BrowserDemo/BrowserDemo.csproj
```

验收标准：

- 0 errors。
- 尽量保持 0 warnings。

---

## 附：当前应避免的错误配置

不要把 ARK 配成：

```json
{
  "provider_key": "anthropic",
  "endpoint": "https://ark.cn-beijing.volces.com/api/coding"
}
```

也不要使用：

```text
https://ark.cn-beijing.volces.com/api/coding/v3
```

推荐配置形态：

```json
{
  "provider_key": "volcengine-ark",
  "model": "<your-ark-model-or-endpoint-id>",
  "endpoint": "https://ark.cn-beijing.volces.com/api/coding/v3"
}
```
