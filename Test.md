## SmartAI Browser — AI 提示词测试用例集

> 版本: 1.0 | 日期: 2026-06-07
>
> 本文档包含 45 条面向 SmartAI Browser 的 AI 提示词测试用例，用于验证 AI 助手（Bermain）在浏览器自动化场景下的各项能力。每条用例均为一条直接发送给 AI 的自然语言提示词，并附有预期行为和验证要点。

---

### 一、基础导航（5 条）

**TC-NAV-01：简单 URL 导航**
```
打开 https://www.baidu.com
```
- 预期行为：AI 调用 `browser_navigate` 导航到百度首页，完成后报告页面标题。
- 验证要点：页面成功加载，URL 正确，AI 回复中包含"百度"相关信息。

**TC-NAV-02：隐式协议补全**
```
去 github.com 看看
```
- 预期行为：AI 自动补全 `https://` 前缀并导航。
- 验证要点：不传裸 URL 时 AI 应能正确推断协议，页面成功加载为 GitHub 首页。

**TC-NAV-03：前进后退导航**
```
先打开必应，再打开谷歌，然后后退一页，最后告诉我现在在哪个页面
```
- 预期行为：AI 依次导航到 bing.com → google.com → 调用 `go_back` → 报告当前页面为 Bing。
- 验证要点：多步导航指令的顺序执行正确性，`go_back` 后页面 URL 为 bing.com。

**TC-NAV-04：刷新当前页面**
```
刷新一下当前页面
```
- 预期行为：AI 调用 `refresh` 刷新页面，等待加载完成后告知用户。
- 验证要点：刷新操作被执行，页面重新加载，AI 确认刷新完成。

**TC-NAV-05：中文搜索意图导航**
```
帮我搜一下"2026年高考作文题目"
```
- 预期行为：AI 识别搜索意图，导航到搜索引擎并执行搜索，返回搜索结果摘要。
- 验证要点：AI 使用 `compose_search` 或手动组合导航+输入+搜索，返回相关搜索结果。

---

### 二、页面元素交互（6 条）

**TC-CLICK-01：按文本点击按钮**
```
打开必应首页，点击"搜索"按钮
```
- 预期行为：AI 导航到 bing.com，定位搜索按钮（可能通过文本"搜索"或 CSS 选择器），执行点击。
- 验证要点：点击操作成功执行，AI 报告点击结果。

**TC-TYPE-01：在搜索框输入文字**
```
在百度搜索框里输入"SmartAI Browser"，然后按回车
```
- 预期行为：AI 定位百度搜索框（`#kw` 或 `input[name="wd"]`），输入文本，再发送 Enter 键。
- 验证要点：文本正确填入，回车触发搜索，页面跳转到搜索结果。

**TC-SELECT-01：下拉框选择**
```
打开 https://www.w3schools.com/tags/tryit.asp?filename=tryhtml_select，在页面里找到一个下拉选择框，选择第二个选项
```
- 预期行为：AI 导航到目标页面（可能在 iframe 中），定位 select 元素，选择指定选项。
- 验证要点：`browser_select_option` 被调用，选项切换成功。

**TC-HOVER-01：悬停展开菜单**
```
打开 https://www.jd.com ，把鼠标悬停在顶部的"我的京东"上面，看看弹出什么菜单
```
- 预期行为：AI 导航到京东首页，定位"我的京东"元素，执行 `browser_hover`，然后提取弹出菜单内容。
- 验证要点：hover 操作触发下拉菜单，AI 能提取并报告菜单项列表。

**TC-SCROLL-01：滚动页面**
```
打开百度新闻首页，往下滚动三屏，然后告诉我你看到了什么内容
```
- 预期行为：AI 导航到新闻页面，执行多次 `scroll_by` 或 `scroll_to`，然后提取当前可见区域文本。
- 验证要点：滚动操作被执行，AI 回复中包含滚动后才可见的内容。

**TC-KEY-01：键盘快捷键操作**
```
打开任意一个网页，然后按 Ctrl+A 全选，再按 Ctrl+C 复制
```
- 预期行为：AI 导航到某页面，依次调用 `browser_press_key` 发送 `Control+a` 和 `Control+c`。
- 验证要点：键盘快捷键被正确发送，AI 报告操作完成。

---

### 三、内容提取与截图（6 条）

**TC-EXTRACT-01：提取页面全文**
```
打开 https://news.ycombinator.com ，把这个页面的所有标题都提取出来
```
- 预期行为：AI 导航到 HN，调用 `browser_snapshot` 或 `browser_evaluate` 提取所有新闻标题。
- 验证要点：返回列表包含当前页面所有（或大部分）新闻标题文本。

**TC-EXTRACT-02：提取特定元素内容**
```
打开维基百科中文首页，告诉我今天的"历史上的今天"部分写了什么
```
- 预期行为：AI 导航到 zh.wikipedia.org，定位"历史上的今天"区块，提取其文本内容。
- 验证要点：AI 精准定位目标区块而非全文，回复内容对应该区块。

**TC-EXTRACT-03：提取页面链接**
```
打开 https://news.ycombinator.com ，列出页面上所有外部链接的标题和 URL
```
- 预期行为：AI 通过 `browser_evaluate` 或 `browser_snapshot` 提取所有 `<a>` 标签的 href 和文本。
- 验证要点：返回结构化的链接列表，包含标题和 URL。

**TC-EXTRACT-04：执行 JavaScript**
```
在当前页面执行一段 JavaScript：document.querySelectorAll('a').length，告诉我页面上有多少个链接
```
- 预期行为：AI 调用 `browser_evaluate`，执行 JS 表达式，返回结果数值。
- 验证要点：`browser_evaluate` 被正确调用，返回值是一个数字且合理。

**TC-SCREENSHOT-01：截取当前页面**
```
打开 https://www.apple.com ，截一张当前页面的图给我看看
```
- 预期行为：AI 导航到 Apple 官网，调用 `browser_take_screenshot` 截图。
- 验证要点：截图成功返回，图片内容对应 Apple 首页。

**TC-SCREENSHOT-02：整页长截图**
```
打开 https://en.wikipedia.org/wiki/Browser ，帮我截取整个页面的完整长图
```
- 预期行为：AI 导航到目标页面，通过 `browser_take_screenshot`（可能带 full_page 参数）或逐屏截图+拼接完成整页截图。
- 验证要点：最终截图覆盖整个页面（超出可视区域的部分也被截取）。

---

### 四、多标签页操作（4 条）

**TC-TAB-01：新建标签页**
```
新开一个标签页，打开 Google
```
- 预期行为：AI 调用 `browser_tabs`（action: new），在新标签中导航到 google.com。
- 验证要点：新标签页创建成功，Google 页面在新标签中加载。

**TC-TAB-02：切换标签页**
```
先打开百度，再新开一个标签页打开谷歌，然后切回百度那个标签
```
- 预期行为：AI 依次创建两个标签页，然后通过 `browser_tabs`（action: select）切回百度标签。
- 验证要点：标签切换成功，当前活动标签页为百度。

**TC-TAB-03：列出并关闭标签页**
```
告诉我现在打开了哪些标签页，然后把除了第一个之外的都关掉
```
- 预期行为：AI 调用 `browser_tabs`（action: list）获取标签列表，然后对非首个标签逐一调用 close。
- 验证要点：标签列表正确返回，关闭操作执行成功，只剩一个标签页。

**TC-TAB-04：跨标签页数据对比**
```
帮我开两个标签页，一个打开京东搜"iPhone 16"，一个打开淘宝搜"iPhone 16"，然后把两边的价格列出来对比一下
```
- 预期行为：AI 使用 `compose_compare` 或手动编排跨标签操作，在两个标签页分别搜索并提取价格，最终输出对比表格。
- 验证要点：两个标签页分别加载搜索结果，AI 提取到价格信息并以对比格式呈现。

---

### 五、表单与登录（4 条）

**TC-FORM-01：自动填充表单**
```
打开 https://httpbin.org/forms/post ，帮我把表单填上：用户名写 testuser，邮箱写 test@example.com，其他字段随便填合理的值
```
- 预期行为：AI 导航到表单页面，通过 `browser_snapshot` 或 `browser_evaluate` 分析表单结构，然后逐字段调用 `browser_fill_form` / `browser_select_option`。
- 验证要点：所有表单字段被填充，用户名和邮箱字段值正确，AI 可能截图确认或提交表单。

**TC-FORM-02：登录流程（需确认）**
```
帮我登录 https://the-internet.herokuapp.com/login ，用户名是 tomsmith，密码是 SuperSecretPassword!
```
- 预期行为：AI 导航到登录页，定位用户名/密码输入框和登录按钮，填入凭据并点击提交，等待跳转后确认登录状态。
- 验证要点：登录成功（页面显示 "You logged into a secure area!"），AI 报告登录结果。

**TC-FORM-03：勾选复选框**
```
打开一个有复选框的页面，帮我把所有复选框都勾选上
```
- 预期行为：AI 导航到合适的测试页面，查询所有 checkbox 元素，逐个勾选。
- 验证要点：复选框状态变更成功，AI 报告操作结果。

**TC-FORM-04：拖拽操作**
```
打开 https://jqueryui.com/droppable/ ，帮我把左边的方块拖到右边的方块上
```
- 预期行为：AI 导航到页面（注意可能在 iframe 中），定位 draggable 和 droppable 元素，调用 `browser_drag`。
- 验证要点：拖拽操作执行，目标元素状态变化（如变色）。

---

### 六、复合任务与组合技能（6 条）

**TC-COMPOSE-01：搜索并提取结果**
```
帮我在必应上搜索"ChatGPT 最新动态"，然后把前 5 条搜索结果的标题和摘要整理给我
```
- 预期行为：AI 使用 `compose_search` 执行搜索，提取搜索结果页面中的前 5 条结果。
- 验证要点：返回 5 条结果，每条含标题和摘要，内容对应搜索关键词。

**TC-COMPOSE-02：分页数据采集**
```
打开 Hacker News，帮我采集前两页的所有帖子标题
```
- 预期行为：AI 使用 `compose_paginate` 或手动导航到第 2 页，逐页提取帖子标题。
- 验证要点：返回两页（约 60 条）帖子标题，无遗漏和重复。

**TC-COMPOSE-03：表格数据提取**
```
打开 https://en.wikipedia.org/wiki/List_of_countries_by_GDP_(nominal) ，把前 10 名的国家和 GDP 数据提取出来，整理成表格
```
- 预期行为：AI 导航到维基百科页面，定位 GDP 排名表格，提取前 10 行数据，格式化为 Markdown 表格。
- 验证要点：表格包含排名、国家名、GDP 数值，数据准确。

**TC-COMPOSE-04：多步骤任务链**
```
帮我做这样一件事：先在百度搜索"天气"，看看搜索结果里第一条的天气信息，然后再新开一个标签页打开 weather.com，把两个来源的天气信息做个对比
```
- 预期行为：AI 按顺序执行搜索 → 提取 → 新建标签 → 导航 → 提取 → 汇总对比，涉及多步骤编排。
- 验证要点：两个数据源都被成功提取，最终输出对比结果。

**TC-COMPOSE-05：文件下载**
```
打开 https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf ，帮我把这个 PDF 下载下来
```
- 预期行为：AI 导航到 PDF 链接或直接触发下载，监控下载状态直到完成。
- 验证要点：下载任务成功完成，AI 报告文件保存路径。

**TC-COMPOSE-06：页面内容翻译辅助**
```
打开 https://en.wikipedia.org/wiki/Artificial_intelligence ，把第一段英文内容提取出来，然后翻译成中文
```
- 预期行为：AI 导航到英文维基百科，提取文章第一段正文文本，然后利用自身语言能力翻译为中文。
- 验证要点：返回英文原文和中文翻译，翻译内容准确通顺。

---

### 七、交互式暂停（ask_user）（4 条）

**TC-ASK-01：歧义场景下的用户确认**
```
帮我在这个页面上找到登录入口并点击
```
- 预期行为：若页面有多个登录相关元素，AI 应调用 `ask_user`（multiple_choice），让用户选择具体点击哪个。
- 验证要点：`ask_user` 被调用，问题类型为 `multiple_choice`，选项列表合理，用户选择后 AI 继续执行。

**TC-ASK-02：敏感操作确认**
```
帮我把这个表单提交出去
```
- 预期行为：AI 填充表单后，在提交前调用 `ask_user`（confirmation），让用户确认是否提交。
- 验证要点：提交操作前有暂停确认环节，用户确认后才执行提交。

**TC-ASK-03：开放式信息收集**
```
帮我在网站上搜索点东西
```
- 预期行为：AI 因用户未指定搜索内容，调用 `ask_user`（open_ended）询问用户想搜什么。
- 验证要点：`ask_user` 被调用，问题类型为 `open_ended`，用户回复后 AI 继续执行搜索。

**TC-ASK-04：跳过问题让 AI 自主决策**
```
帮我打开一个新闻网站，随便挑一条感兴趣的新闻打开看看
```
- 预期行为：AI 可能调用 `ask_user` 让用户选择哪条新闻，用户选择"跳过"后 AI 自行选择一条并打开。
- 验证要点：跳过选项可用，跳过后 AI 自主做出选择并继续执行。

---

### 八、错误处理与边界情况（5 条）

**TC-ERROR-01：无效 URL 导航**
```
打开 https://this-site-does-not-exist-12345.com
```
- 预期行为：AI 尝试导航，页面加载失败（DNS 解析错误或超时），AI 报告导航失败并给出可能原因。
- 验证要点：AI 不会卡死，能优雅处理错误并向用户说明情况。

**TC-ERROR-02：不存在的元素操作**
```
在当前页面上找到一个 id 为"nonexistent-button-xyz"的按钮并点击它
```
- 预期行为：AI 尝试定位该元素，发现不存在后报告错误，可能触发重试策略（换用其他选择器）。
- 验证要点：AI 报告元素未找到，不会产生未处理异常或无限循环。

**TC-ERROR-03：超时页面等待**
```
打开 https://httpbin.org/delay/60 ，等它加载完
```
- 预期行为：页面响应延迟 60 秒，AI 在等待超时后报告超时情况，而非无限等待。
- 验证要点：超时机制生效，AI 在合理时间内给出超时反馈。

**TC-ERROR-04：iframe 内操作**
```
打开 https://www.w3schools.com/html/html_iframe.asp ，找到页面中的 iframe，告诉我 iframe 里显示的内容是什么
```
- 预期行为：AI 识别页面包含 iframe，尝试提取 iframe 内容（可能需要切换上下文）。
- 验证要点：AI 能识别 iframe 结构并尝试提取内容，或报告跨域 iframe 限制。

**TC-ERROR-05：动态加载内容等待**
```
打开 https://infinite-scroll.com/ ，往下滚动加载更多内容，然后告诉我一共看到了多少个项目
```
- 预期行为：AI 导航到无限滚动页面，执行滚动触发懒加载，等待新内容加载后统计元素数量。
- 验证要点：AI 等待动态内容加载完成后再统计，数量大于首屏可见数量。

---

### 九、Cookie 与状态管理（3 条）

**TC-COOKIE-01：查看 Cookie**
```
打开 https://www.baidu.com ，看看这个网站设置了哪些 Cookie
```
- 预期行为：AI 导航到百度后，通过 `browser_evaluate` 读取 `document.cookie` 或通过 MCP 相关工具获取 Cookie 列表。
- 验证要点：返回百度域名下的 Cookie 信息（名称、值等）。

**TC-COOKIE-02：设置自定义 Cookie**
```
在当前页面上设置一个 Cookie，名字叫 test_cookie，值是 hello123
```
- 预期行为：AI 通过 `browser_evaluate` 执行 `document.cookie = "test_cookie=hello123"` 或调用对应工具。
- 验证要点：Cookie 设置成功，再次查询时能看到该 Cookie。

**TC-COOKIE-03：删除 Cookie**
```
把刚才设置的 test_cookie 删掉
```
- 预期行为：AI 通过设置过期时间或删除操作移除指定 Cookie。
- 验证要点：删除后再次查询，该 Cookie 不再存在。

---

### 十、综合场景与压力测试（2 条）

**TC-STRESS-01：长任务链编排**
```
帮我做以下任务：
1. 打开 Hacker News 首页
2. 提取前 10 条帖子标题
3. 点进第一条帖子的链接
4. 提取那个页面的第一段文字
5. 回到 Hacker News
6. 点开第二条帖子
7. 也提取第一段文字
8. 最后把两条帖子的内容做个简单总结
```
- 预期行为：AI 按 8 个步骤依次执行，涉及导航、提取、前进后退、汇总，考验长链路编排和上下文管理能力。
- 验证要点：全部 8 步完成，最终总结包含两条帖子的内容要点，无遗漏步骤。

**TC-STRESS-02：模糊意图理解**
```
帮我看看网上最近有什么关于 AI 的新闻
```
- 预期行为：AI 自主决定导航到某个新闻聚合站或搜索引擎，搜索 AI 相关新闻，提取并整理结果。
- 验证要点：AI 不依赖用户提供具体 URL，能自主选择合适入口完成任务，返回多条 AI 相关新闻。

---

### 附录：测试用例速查表

| 编号 | 类别 | 用例名称 | 涉及核心能力 |
|------|------|---------|------------|
| TC-NAV-01 | 基础导航 | 简单 URL 导航 | `browser_navigate` |
| TC-NAV-02 | 基础导航 | 隐式协议补全 | 协议推断 + `browser_navigate` |
| TC-NAV-03 | 基础导航 | 前进后退导航 | `go_back` + 多步编排 |
| TC-NAV-04 | 基础导航 | 刷新当前页面 | `refresh` |
| TC-NAV-05 | 基础导航 | 中文搜索意图导航 | `compose_search` |
| TC-CLICK-01 | 页面交互 | 按文本点击按钮 | `browser_click` + 文本定位 |
| TC-TYPE-01 | 页面交互 | 搜索框输入文字 | `browser_type` + `press_key` |
| TC-SELECT-01 | 页面交互 | 下拉框选择 | `browser_select_option` |
| TC-HOVER-01 | 页面交互 | 悬停展开菜单 | `browser_hover` + 内容提取 |
| TC-SCROLL-01 | 页面交互 | 滚动页面 | `scroll_by` / `scroll_to` |
| TC-KEY-01 | 页面交互 | 键盘快捷键 | `browser_press_key` |
| TC-EXTRACT-01 | 内容提取 | 提取页面全文 | `browser_snapshot` |
| TC-EXTRACT-02 | 内容提取 | 提取特定元素 | 元素定位 + 提取 |
| TC-EXTRACT-03 | 内容提取 | 提取页面链接 | `browser_evaluate` |
| TC-EXTRACT-04 | 内容提取 | 执行 JavaScript | `browser_evaluate` |
| TC-SCREENSHOT-01 | 内容提取 | 截取当前页面 | `browser_take_screenshot` |
| TC-SCREENSHOT-02 | 内容提取 | 整页长截图 | 截图 + 滚动拼接 |
| TC-TAB-01 | 多标签页 | 新建标签页 | `browser_tabs` (new) |
| TC-TAB-02 | 多标签页 | 切换标签页 | `browser_tabs` (select) |
| TC-TAB-03 | 多标签页 | 列出并关闭标签页 | `browser_tabs` (list/close) |
| TC-TAB-04 | 多标签页 | 跨标签页数据对比 | `compose_compare` |
| TC-FORM-01 | 表单登录 | 自动填充表单 | `browser_fill_form` |
| TC-FORM-02 | 表单登录 | 登录流程 | `compose_login` |
| TC-FORM-03 | 表单登录 | 勾选复选框 | `browser_click` (checkbox) |
| TC-FORM-04 | 表单登录 | 拖拽操作 | `browser_drag` |
| TC-COMPOSE-01 | 复合任务 | 搜索并提取结果 | `compose_search` |
| TC-COMPOSE-02 | 复合任务 | 分页数据采集 | `compose_paginate` |
| TC-COMPOSE-03 | 复合任务 | 表格数据提取 | 表格定位 + 提取 |
| TC-COMPOSE-04 | 复合任务 | 多步骤任务链 | 多技能编排 |
| TC-COMPOSE-05 | 复合任务 | 文件下载 | `compose_download` |
| TC-COMPOSE-06 | 复合任务 | 页面内容翻译辅助 | 提取 + LLM 翻译 |
| TC-ASK-01 | 交互暂停 | 歧义场景确认 | `ask_user` (multiple_choice) |
| TC-ASK-02 | 交互暂停 | 敏感操作确认 | `ask_user` (confirmation) |
| TC-ASK-03 | 交互暂停 | 开放式信息收集 | `ask_user` (open_ended) |
| TC-ASK-04 | 交互暂停 | 跳过让 AI 自主决策 | `ask_user` (skip) |
| TC-ERROR-01 | 错误处理 | 无效 URL 导航 | 错误恢复策略 |
| TC-ERROR-02 | 错误处理 | 不存在的元素操作 | 重试策略 + 定位策略 |
| TC-ERROR-03 | 错误处理 | 超时页面等待 | 超时机制 |
| TC-ERROR-04 | 错误处理 | iframe 内操作 | iframe 上下文处理 |
| TC-ERROR-05 | 错误处理 | 动态加载内容等待 | `browser_wait_for` |
| TC-COOKIE-01 | Cookie 管理 | 查看 Cookie | `browser_evaluate` |
| TC-COOKIE-02 | Cookie 管理 | 设置自定义 Cookie | `browser_evaluate` |
| TC-COOKIE-03 | Cookie 管理 | 删除 Cookie | `browser_evaluate` |
| TC-STRESS-01 | 综合场景 | 长任务链编排 | 多技能 + 上下文管理 |
| TC-STRESS-02 | 综合场景 | 模糊意图理解 | 意图识别 + 自主决策 |
