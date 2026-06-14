# Version Change Record

## DemoV2 - DemoV3

### VCR-1

**影响范围**：🔴 高 — 核心AI工具循环机制重构，新增任务状态机与自检测机制

**变更文件**：
- `Services/TaskStateMachine.cs` （新增）
- `Services/AiClient.cs`
- `Services/AgentEventSelfHandler.cs`
- `Services/Automation/AutomationScripts.cs`
- `ViewModels/ChatViewModel.cs`
- `Services/ContextBuilder.cs`
- `CLAUDE.md`
- `README.md`
- `Help/playwrightMCPScreenShot.md` （新增）
- `.gitignore`

**关键变更**：

1. **TaskStateMachine（新增）**：强制AI按子任务顺序执行，三态流转 `Planning → Executing → Complete`；`update_todo` 仅 Planning 允许，`start_subtask`/`finish_subtask` 仅对当前 ActiveSubtaskId 允许，子任务不可跳过，finish completed 时自动推进下一子任务。

2. **AiClient 安全增强**：硬迭代上限 80 轮；子任务门禁从"连续3次接受文本"改为"连续5次终止"；规划门禁集成 TaskStateMachine（优先状态机判断，兜底旧消息扫描）；新增 AI 复读检测（连续2轮相同 normalized hash >30字符即终止）；browser_js 连续2次 null 注入策略变更提示；工具结果截断至 2000+500 字符。

3. **上下文压缩阈值下调**：触发点 80KB→50KB，目标值 60KB→40KB；新增子任务完成时最大压缩至 30KB。

4. **AgentEventSelfHandler 自检测**：过期元素复用检测（2次阻断+3次强制终止）；重复导航失败（同URL 2次/同主机 4次）；相同动作重复（3次阻断）；无进展循环（4次相同结果注入警告）；死胡同评分≥4终止。

5. **AutomationScripts 快照引擎重构**：Playwright风格可见性过滤（display:none, visibility:hidden, aria-hidden, role=presentation, 父级样式遍历）；重要性评分（tag 优先级 + 文本长度 + aria-label）；移除 rect/visible/css_selector/checked/disabled/readonly 字段；text 200→100字符，value 100→50字符；MaxSnapshotElements 1000→0（无上限）。

6. **ChatViewModel 集成**：新增 `_taskStateMachine` 字段；连接至 ContextBuilder；重构 update_todo/start_subtask/finish_subtask 工具处理，映射状态机结果到 UI TodoItems；新增前置 Planning 检测（提示调用 update_todo）和后置兜底检测；ask_user 工具描述收紧为严格使用条件。

7. **ContextBuilder 更新**：新增 `TaskStateMachine?` 属性；ClearRuntimeState 调用 Reset()；系统提示词新增"强制顺序执行"和"禁止滥用 ask_user"章节。

8. **文档**：CLAUDE.md 新增快照引擎、自检测、状态机、安全层章节；README.md 新增特性说明与流程图；新增 Playwright MCP 快照参考文档；删除旧 DESIGN.md。
