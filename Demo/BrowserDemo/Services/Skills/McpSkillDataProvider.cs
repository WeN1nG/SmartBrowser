namespace BrowserDemo.Services.Skills;

/// <summary>
/// MCP 技能数据提供者 —— 定义基于 Playwright MCP 的全部原子/组合/策略技能。
/// 13 个原子 + 8 个组合 + 6 个策略 = 27 个技能
/// </summary>
public static class McpSkillDataProvider
{
    // ====================================================================
    // 13 个原子技能（直接映射 MCP 工具）
    // ====================================================================

    public static AtomicSkillDefinition SkillNavigate => new()
    {
        Id = "browser_navigate",
        Name = "导航",
        Description = "导航到指定 URL，等待页面加载完成后返回页面快照。",
        Icon = "🌐",
        McpToolName = "browser_navigate",
        TimeoutMs = 60000,
        ParamMapping = new() { ["url"] = "url" },
        TriggerKeywords = new() { "打开", "去", "导航", "访问", "前往" },
        Tags = new() { "navigation", "core" }
    };

    public static AtomicSkillDefinition SkillSnapshot => new()
    {
        Id = "browser_snapshot",
        Name = "页面快照",
        Description = "获取当前页面的无障碍访问性快照（A11y tree），比截图更高效，可直接获取页面结构和文本内容。",
        Icon = "📋",
        McpToolName = "browser_snapshot",
        TriggerKeywords = new() { "快照", "页面结构", "有什么", "读取", "提取" },
        Tags = new() { "extraction", "core" }
    };

    public static AtomicSkillDefinition SkillClick => new()
    {
        Id = "browser_click",
        Name = "点击",
        Description = "点击页面中的元素，通过 accessibility hash 或文本定位。支持按钮、链接、复选框等所有可交互元素。",
        Icon = "👆",
        McpToolName = "browser_click",
        ParamMapping = new() { ["element"] = "element", ["selector"] = "element" },
        TriggerKeywords = new() { "点击", "点", "按", "单击" },
        Tags = new() { "interaction", "core" }
    };

    public static AtomicSkillDefinition SkillFillForm => new()
    {
        Id = "browser_fill_form",
        Name = "填充表单",
        Description = "同时填充多个表单字段，通过 accessibility hash 定位。",
        Icon = "📝",
        McpToolName = "browser_fill_form",
        TriggerKeywords = new() { "填写", "填入", "填充" },
        Tags = new() { "interaction", "form" }
    };

    public static AtomicSkillDefinition SkillType => new()
    {
        Id = "browser_type",
        Name = "逐字输入",
        Description = "在输入框中逐字输入文本，模拟真人打字效果。适合需要触发输入事件的场景。",
        Icon = "⌨️",
        McpToolName = "browser_type",
        ParamMapping = new() { ["element"] = "element", ["text"] = "text" },
        TriggerKeywords = new() { "输入", "键入", "打字" },
        Tags = new() { "interaction", "form" }
    };

    public static AtomicSkillDefinition SkillSelect => new()
    {
        Id = "browser_select_option",
        Name = "选项选择",
        Description = "从下拉框中选择一个选项。",
        Icon = "📋",
        McpToolName = "browser_select_option",
        ParamMapping = new() { ["element"] = "element", ["option"] = "option" },
        TriggerKeywords = new() { "选择", "选中", "下拉" },
        Tags = new() { "interaction", "form" }
    };

    public static AtomicSkillDefinition SkillHover => new()
    {
        Id = "browser_hover",
        Name = "悬停",
        Description = "鼠标悬停在元素上，用于展开下拉菜单或显示工具提示。",
        Icon = "🖱️",
        McpToolName = "browser_hover",
        ParamMapping = new() { ["element"] = "element" },
        TriggerKeywords = new() { "悬停", "hover", "展开菜单" },
        Tags = new() { "interaction" }
    };

    public static AtomicSkillDefinition SkillScreenshot => new()
    {
        Id = "browser_take_screenshot",
        Name = "截图",
        Description = "对当前页面截图，用于视觉验证。获取 Base64 编码的图片。",
        Icon = "📸",
        McpToolName = "browser_take_screenshot",
        TriggerKeywords = new() { "截图", "截屏", "拍照" },
        Tags = new() { "extraction", "vision" }
    };

    public static AtomicSkillDefinition SkillJs => new()
    {
        Id = "browser_evaluate",
        Name = "JS 执行",
        Description = "在页面中执行 JavaScript 脚本，获取执行结果。用于高级操作和调试。",
        Icon = "💻",
        McpToolName = "browser_evaluate",
        ParamMapping = new() { ["script"] = "script" },
        TriggerKeywords = new() { "执行js", "运行脚本", "javascript" },
        Tags = new() { "advanced", "developer" },
        RequiresUserConfirmation = true
    };

    public static AtomicSkillDefinition SkillPressKey => new()
    {
        Id = "browser_press_key",
        Name = "按键",
        Description = "模拟键盘按键操作（Enter、Tab、Escape、箭头键等）。",
        Icon = "🔑",
        McpToolName = "browser_press_key",
        ParamMapping = new() { ["key"] = "key" },
        TriggerKeywords = new() { "按回车", "按tab", "按键" },
        Tags = new() { "interaction" }
    };

    public static AtomicSkillDefinition SkillTabs => new()
    {
        Id = "browser_tabs",
        Name = "标签管理",
        Description = "管理浏览器标签页：新建标签页、关闭标签页、切换标签、列出所有标签。",
        Icon = "📑",
        McpToolName = "browser_tabs",
        TriggerKeywords = new() { "新标签", "新开", "关闭标签", "切换标签" },
        Tags = new() { "tab", "core" }
    };

    public static AtomicSkillDefinition SkillWait => new()
    {
        Id = "browser_wait_for",
        Name = "等待",
        Description = "等待页面中出现/消失指定文本，或等待指定时长。用于控制操作时序。",
        Icon = "⏳",
        McpToolName = "browser_wait_for",
        TriggerKeywords = new() { "等待", "等", "延时", "暂停" },
        Tags = new() { "timing", "core" },
        TimeoutMs = 60000
    };

    public static AtomicSkillDefinition SkillDrag => new()
    {
        Id = "browser_drag",
        Name = "拖拽",
        Description = "在两个元素之间执行拖放操作。",
        Icon = "🔄",
        McpToolName = "browser_drag",
        TriggerKeywords = new() { "拖拽", "拖放", "拖动" },
        Tags = new() { "interaction" }
    };

    // ====================================================================
    // 8 个组合技能
    // ====================================================================

    public static CompositeSkillDefinition ComposeSearch => new()
    {
        Id = "compose_search",
        Name = "搜索查询",
        Description = "打开搜索引擎（默认 Bing）→ 输入关键词 → 获取搜索结果快照。完整搜索流程。",
        Icon = "🔍",
        Steps = new()
        {
            new() { SkillId = "browser_navigate", Description = "打开搜索引擎",
                FixedParams = new() { ["url"] = "https://www.bing.com" } },
            new() { SkillId = "browser_snapshot", Description = "获取页面快照确认加载",
                OutputKey = "initial_snapshot" },
            new() { SkillId = "browser_type", Description = "输入搜索关键词",
                FixedParams = new() { ["element"] = "input[name=q]" } },
            new() { SkillId = "browser_press_key", Description = "按下回车搜索",
                FixedParams = new() { ["key"] = "Enter" } },
            new() { SkillId = "browser_wait_for", Description = "等待搜索结果加载",
                FixedParams = new() { ["text"] = "" }, IsOptional = true },
            new() { SkillId = "browser_snapshot", Description = "获取搜索结果快照",
                OutputKey = "search_results" }
        },
        ExpectedOutput = "搜索结果列表（标题、链接、摘要）",
        EstimatedDuration = "5-15秒",
        TriggerKeywords = new() { "搜索", "查一下", "搜", "查找", "bing" },
        Tags = new() { "navigation", "extraction", "common" }
    };

    public static CompositeSkillDefinition ComposeLogin => new()
    {
        Id = "compose_login",
        Name = "登录操作",
        Description = "导航到登录页 → 输入用户名/邮箱 → 输入密码 → 点击登录 → 截图确认结果。",
        Icon = "🔐",
        Steps = new()
        {
            new() { SkillId = "browser_navigate", Description = "打开登录页面" },
            new() { SkillId = "browser_snapshot", Description = "获取登录页快照",
                OutputKey = "login_page_snapshot" },
            new() { SkillId = "browser_fill_form", Description = "输入用户名/邮箱" },
            new() { SkillId = "browser_fill_form", Description = "输入密码" },
            new() { SkillId = "browser_click", Description = "点击登录按钮",
                FixedParams = new() { ["element"] = "button[type=submit]" } },
            new() { SkillId = "browser_wait_for", Description = "等待登录完成", IsOptional = true },
            new() { SkillId = "browser_snapshot", Description = "获取登录后页面快照",
                OutputKey = "post_login_snapshot" }
        },
        ExpectedOutput = "登录结果状态和页面快照",
        EstimatedDuration = "5-20秒",
        TriggerKeywords = new() { "登录", "登入", "signin", "login" },
        Tags = new() { "form", "common" },
        RequiresUserConfirmation = true,
        TimeoutMs = 60000
    };

    public static CompositeSkillDefinition ComposeExtract => new()
    {
        Id = "compose_extract",
        Name = "数据提取",
        Description = "获取页面快照 → 分析内容 → 执行 JS 提取结构化数据 → 格式化输出。",
        Icon = "📄",
        Steps = new()
        {
            new() { SkillId = "browser_snapshot", Description = "获取页面快照",
                OutputKey = "page_snapshot" },
            new() { SkillId = "browser_evaluate", Description = "提取页面结构化数据",
                FixedParams = new() { ["script"] = "JSON.stringify({title: document.title, url: location.href, text: document.body.innerText.substring(0, 10000)})" },
                OutputKey = "extracted_data", IsOptional = true }
        },
        ExpectedOutput = "结构化页面数据（标题、URL、文本内容）",
        EstimatedDuration = "2-5秒",
        TriggerKeywords = new() { "提取", "获取", "读取", "抓取", "采集" },
        Tags = new() { "extraction", "core" }
    };

    public static CompositeSkillDefinition ComposePaginate => new()
    {
        Id = "compose_paginate",
        Name = "多页采集",
        Description = "循环翻页提取数据：提取当前页 → 点击下一页 → 提取 → 直到无下一页或达到上限。",
        Icon = "📑",
        Steps = new()
        {
            new() { SkillId = "browser_snapshot", Description = "提取当前页数据",
                OutputKey = "page_data" },
            new() { SkillId = "browser_click", Description = "点击'下一页'按钮",
                FallbackSkillId = "browser_wait_for", IsOptional = true },
            new() { SkillId = "browser_wait_for", Description = "等待下一页加载",
                FixedParams = new() { ["text"] = "" }, IsOptional = true },
            new() { SkillId = "browser_snapshot", Description = "继续提取数据（循环）",
                IsOptional = true, OutputKey = "next_page_data" }
        },
        ExpectedOutput = "所有分页的采集数据汇总",
        EstimatedDuration = "10秒-2分钟",
        TriggerKeywords = new() { "采集", "爬取", "多页", "分页", "所有数据" },
        Tags = new() { "extraction", "data", "batch" }
    };

    public static CompositeSkillDefinition ComposeFillForm => new()
    {
        Id = "compose_fill_form",
        Name = "表单填充",
        Description = "逐字段填充表单 → 截图预览 → 提交 → 等待结果。",
        Icon = "📝",
        Steps = new()
        {
            new() { SkillId = "browser_snapshot", Description = "获取表单页面快照",
                OutputKey = "form_snapshot" },
            new() { SkillId = "browser_fill_form", Description = "填充表单字段" },
            new() { SkillId = "browser_screenshot", Description = "填写后截图预览" },
            new() { SkillId = "browser_click", Description = "提交表单",
                FixedParams = new() { ["element"] = "button[type=submit], input[type=submit]" } },
            new() { SkillId = "browser_wait_for", Description = "等待提交结果", IsOptional = true },
            new() { SkillId = "browser_snapshot", Description = "获取结果页快照",
                OutputKey = "result_snapshot" }
        },
        ExpectedOutput = "表单提交结果快照",
        EstimatedDuration = "5-30秒",
        TriggerKeywords = new() { "填表", "填写表单", "注册", "提交表单" },
        Tags = new() { "form", "common" },
        RequiresUserConfirmation = true
    };

    public static CompositeSkillDefinition ComposeDownload => new()
    {
        Id = "compose_download",
        Name = "文件下载",
        Description = "导航到下载页面 → 定位下载按钮 → 点击触发下载 → 等待完成 → 返回文件信息。",
        Icon = "⬇️",
        Steps = new()
        {
            new() { SkillId = "browser_navigate", Description = "打开下载页面" },
            new() { SkillId = "browser_wait_for", Description = "等待页面加载" },
            new() { SkillId = "browser_click", Description = "点击下载按钮" },
            new() { SkillId = "browser_wait_for", Description = "等待下载完成",
                FixedParams = new() { ["text"] = "" }, IsOptional = true }
        },
        ExpectedOutput = "下载完成确认",
        EstimatedDuration = "5-60秒",
        TriggerKeywords = new() { "下载", "保存", "下载文件" },
        Tags = new() { "download" },
        TimeoutMs = 120000
    };

    public static CompositeSkillDefinition ComposeCompare => new()
    {
        Id = "compose_compare",
        Name = "跨页对比",
        Description = "新建标签 A → 导航到页面 A → 提取内容 → 新建标签 B → 导航到页面 B → 提取内容 → 对比汇总。",
        Icon = "🔄",
        Steps = new()
        {
            new() { SkillId = "browser_tabs", Description = "新建标签页 A",
                FixedParams = new() { ["action"] = "new" } },
            new() { SkillId = "browser_navigate", Description = "导航到页面 A" },
            new() { SkillId = "browser_snapshot", Description = "提取页面 A 数据",
                OutputKey = "page_a_data" },
            new() { SkillId = "browser_tabs", Description = "新建标签页 B",
                FixedParams = new() { ["action"] = "new" } },
            new() { SkillId = "browser_navigate", Description = "导航到页面 B" },
            new() { SkillId = "browser_snapshot", Description = "提取页面 B 数据",
                OutputKey = "page_b_data" }
        },
        ExpectedOutput = "两个页面数据的对比结果",
        EstimatedDuration = "10-30秒",
        TriggerKeywords = new() { "对比", "比较", "对照", "两边", "两个页面", "vs" },
        Tags = new() { "tab", "comparison" }
    };

    // ====================================================================
    // 6 个策略技能
    // ====================================================================

    public static StrategySkillDefinition StrategyNavigation => new()
    {
        Id = "strategy_navigation",
        Name = "导航策略",
        Description = "目标不在当前页时决策：搜索URL、导航新URL或询问用户。",
        TriggerType = StrategyTriggerType.BeforeToolCall,
        TriggerKeywords = new() { "找不到", "无法访问", "404" },
        DecisionDimensions = "页面相关性、历史路径",
        FallbackChain = new() { "browser_navigate", "compose_search" },
        Priority = 10,
        Tags = new() { "strategy", "navigation" }
    };

    public static StrategySkillDefinition StrategyLocate => new()
    {
        Id = "strategy_locate",
        Name = "定位策略",
        Description = "元素定位降级：A11y hash → 文本匹配 → 等待重试 → 截图辅助定位。",
        TriggerType = StrategyTriggerType.OnError,
        TriggerKeywords = new() { "找不到元素", "定位失败", "not found" },
        DecisionDimensions = "元素特征、页面结构",
        FallbackChain = new() { "browser_snapshot", "browser_wait_for" },
        Priority = 20,
        Tags = new() { "strategy", "locating" }
    };

    public static StrategySkillDefinition StrategyRetry => new()
    {
        Id = "strategy_retry",
        Name = "重试策略",
        Description = "操作失败后的自适应恢复：等待重试 → 换方法 → 报告用户。",
        TriggerType = StrategyTriggerType.OnError,
        TriggerKeywords = new() { "失败", "错误", "error", "timeout", "超时" },
        DecisionDimensions = "失败类型、重试次数",
        FallbackChain = new() { "browser_wait_for", "browser_snapshot" },
        Priority = 15,
        Tags = new() { "strategy", "recovery" }
    };

    public static StrategySkillDefinition StrategyContext => new()
    {
        Id = "strategy_context",
        Name = "上下文策略",
        Description = "Token 预算管理：裁剪历史快照 → 保留关键任务状态。",
        TriggerType = StrategyTriggerType.OnTokenPressure,
        DecisionDimensions = "Token 用量、对话轮次",
        Priority = 5,
        Tags = new() { "strategy", "context", "performance" }
    };

    public static StrategySkillDefinition StrategyRecovery => new()
    {
        Id = "strategy_recovery",
        Name = "错误恢复策略",
        Description = "页面崩溃/导航失败时的整体恢复：新标签 → 重新导航 → 恢复任务。",
        TriggerType = StrategyTriggerType.OnError,
        TriggerKeywords = new() { "崩溃", "crash", "错误", "异常" },
        DecisionDimensions = "错误类型、标签状态",
        FallbackChain = new() { "browser_tabs", "browser_navigate" },
        Priority = 1,
        Tags = new() { "strategy", "recovery", "critical" }
    };

    public static StrategySkillDefinition StrategyPrivacy => new()
    {
        Id = "strategy_privacy",
        Name = "隐私保护策略",
        Description = "识别敏感页面（登录/支付/个人资料）并采用安全操作模式。",
        TriggerType = StrategyTriggerType.BeforeToolCall,
        TriggerKeywords = new() { "密码", "支付", "隐私", "敏感" },
        DecisionDimensions = "URL模式、页面内容类型",
        Priority = 0,
        Tags = new() { "strategy", "privacy", "security" }
    };

    // ====================================================================
    // 批量获取
    // ====================================================================

    public static List<AtomicSkillDefinition> GetAllAtomicSkills() => new()
    {
        SkillNavigate, SkillSnapshot, SkillClick, SkillFillForm, SkillType,
        SkillSelect, SkillHover, SkillScreenshot, SkillJs, SkillPressKey,
        SkillTabs, SkillWait, SkillDrag
    };

    public static List<CompositeSkillDefinition> GetAllCompositeSkills() => new()
    {
        ComposeSearch, ComposeLogin, ComposeExtract, ComposePaginate,
        ComposeFillForm, ComposeDownload, ComposeCompare
    };

    public static List<StrategySkillDefinition> GetAllStrategySkills() => new()
    {
        StrategyNavigation, StrategyLocate, StrategyRetry,
        StrategyContext, StrategyRecovery, StrategyPrivacy
    };

    public static List<SkillDefinition> GetAllSkills()
    {
        var all = new List<SkillDefinition>();
        all.AddRange(GetAllAtomicSkills());
        all.AddRange(GetAllCompositeSkills());
        all.AddRange(GetAllStrategySkills());
        return all;
    }
}
