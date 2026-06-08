# 修正指南

## 修正_Playwright选择器兼容与JS执行修复_6_6_0_20

### focus
修复 AI 使用 Playwright 非标准 CSS 选择器（`:has-text()`、`:contains()`、`:visible`、`text=`、`xpath=`、`>>`等）导致 `document.querySelector()` 抛出 `DOMException` 后静默返回空字符串，AI 误以为操作成功而继续无效工具循环的问题。同时修复 `skill_js` 返回复杂类型（数组/对象）时解析为空的问题。

### reason
从日志 `6-6-0-37-53.log` 中分析发现：
1. **AI 使用 `a:has-text('课程'), button:has-text('课程')...` 作为选择器** → WebView2 的 `querySelector` 不支持 Playwright 伪选择器 → `DOMException` → `ExecuteScriptAsync` 返回 `"null"` → `DecodeJsResult("null")` → `""` → `GetJsError("")` → `null` → **静默返回 `✅ 已点击元素: `（空字符串）** → AI 以为点击成功但没有页面变化 → 继续重试 24 轮不同 JS 代码
2. **AI 切到 `skill_js` 执行复杂返回值的脚本**（如 `Array.from(...).map(...)`）→ JS 返回 JSON 数组字符串 → `DecodeJsResult` 解码数组 JSON 为原始 JSON 字符串（`_ => json`）→ 但后续处理期望可读文本 → 日志中显示为"空"
3. **AI 被 34/35 次 `✅ 成功` 误报困住** → 不知道选择器有问题 → 不断尝试不同的选择器变体 → 直到 35 轮上限

### deepreason
1. **CSS 标准限制**：`document.querySelector()` 仅支持标准 CSS 选择器。Playwright 框架扩展了 CSS 语法（`:has-text()`、`:contains()`、`:visible`、`text=`、`xpath=`、`>>`链式操作等），这些在浏览器原生 API 中均抛出 `DOMException`
2. **静默失败链**：JS 抛异常 → `ExecuteScriptAsync` 返回 JS `null` → C# 端 `DecodeJsResult("null") → ""` → 所有下游错误检测（`GetJsError`、`IsNullOrWhiteSpace`）都认为空字符串是"正常无结果"而不是"出错"
3. **缺少选择器验证层**：原来没有任何代码能识别 `:has-text()` 是非标准语法并给出有意义的错误反馈
4. **AI 模型训练偏差**：DeepSeek 等大模型在训练数据中大量包含 Playwright 代码示例，自然倾向于生成 `:has-text()` 等语法

### solution
**修复 A — 选择器验证与适配层（核心）**：新增 `ValidateSelector()` 方法，检测所有已知 Playwright 非标准模式并降级处理：
- `:has-text('xxx')` / `:contains('xxx')` → 提取文本 → 用 `querySelectorAll` + `innerText` 匹配查找元素
- `text=` 前缀 → 明确错误提示"请使用 selector 或 text_content 参数"
- `xpath=` 前缀 → 明确错误提示"XPath 不支持，请使用 CSS 选择器"
- `>>` 链式操作 → 明确错误提示
- `:visible` → 自动忽略（非标准但可安全移除）
- `css=` 前缀 → 自动剥离前缀
- 其他标准 CSS → 包裹 try-catch，异常时返回 `{error: "CSS 选择器语法错误: ..."}`

**修复 B — 统一安全入口**：新增 `BuildSafeElementJs()` 和 `BuildSafeElementAllJs()` 方法，替换所有 14 处手工拼接的 `querySelector`/`querySelectorAll` JS 代码

**修复 C — 文本查找降级**：新增 `BuildTextFindJs()`，通过遍历候选元素匹配 `innerText` 实现 `:has-text()` 降级

**修复 D — 元素存在性检查**：新增 `BuildElementExistsJs()`，用于 `wait_for_element` 场景（非标准选择器 → 改为文本存在性检查）

### change

| 文件 | 变更 |
|------|------|
| `Services/Automation/WebView2AutomationBridge.cs` | 新增 7 个选择器安全方法：`ExtractPseudoText`、`ValidateSelector`、`RemovePseudoClass`、`BuildSafeElementJs`、`BuildSafeElementAllJs`、`BuildTextFindJs`、`BuildElementExistsJs`。更新 8 个 Execute 方法（Click/Type/Select/Scroll/Extract/Wait/Hover/Query）使用安全入口。 |
| `Models/BasicSkillDefinition.cs` | 所有 selector 参数描述标注"不支持 Playwright 伪类"；skill_click 新增 `text_content` 参数；skill_wait 新增 `selector` 和 `text` 属性描述；skill_form 新增 `fields` 描述 |

### keychangecode
```csharp
// ===== 统一安全入口 — 每个使用选择器的 Execute 方法都改为： =====
var js = BuildSafeElementJs(selector, @"
    el.click();
    return JSON.stringify({success: true, tag: el.tagName, text: (el.textContent||'').trim().substring(0, 50)});
");

// ===== 验证器检测 Playwright 模式 =====
private static (bool IsValid, string? ErrorHint, string? CleanSelector, string? TextFallback) ValidateSelector(string selector)
{
    // 1. 检测 xpath=/text=/pi=/react=/id= 引擎前缀
    // 2. 检测 ">>" 链式操作
    // 3. 检测 :has-text() / :contains() 提取文本
    // 4. 去除 :visible
    // 5. 返回处理后的安全选择器
}

// ===== 文本查找降级（替代 :has-text） =====
private static string BuildTextFindJs(string text, string candidatesSelector, string actionBody)
{
    // 遍历 a, button, span, li, div, input... 匹配 innerText
    // 精确匹配 → 模糊匹配 → 失败返回错误
}

// ===== 元素存在性检查（用于 wait_for_element） =====
private static string BuildElementExistsJs(string selector)
{
    // :has-text → 改为 document.body.innerText 包含检查
    // 标准 → querySelector + try-catch
}
```
