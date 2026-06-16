# 方案汇总：按投入产出比排序

> 来源：FromBrowserUse.md 第六节（P0 ~ P4 改进建议汇总）
> 目标：快速定位各改进项的实施文件和步骤索引

---

## 优先级矩阵

| 优先级 | 改进项 | 涉及文件 | 预估工作量 | 预期收益 | 对应详细方案 |
|--------|--------|---------|-----------|---------|-------------|
| **P0** | 结构化思考框架 | `Demo/BrowserDemo/Services/ContextBuilder.cs` | 0.5 天 | 显著提升多步任务连贯性 | [03-llm-integration.md](./03-llm-integration.md#32-强制结构化思考框架) |
| **P0** | DOM text hash 页面停滞检测 | `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs`, `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 0.5 天 | 覆盖"相同工具"检测盲区 | [04-error-recovery.md](./04-error-recovery.md#41-dom-text-hash-页面停滞检测) |
| **P1** | LLM 摘要压缩 | `Demo/BrowserDemo/Services/AiClient.cs` | 1 天 | 延长长任务可用步数 | [03-llm-integration.md](./03-llm-integration.md#34-llm-摘要压缩替代简单截断) |
| **P1** | Stable hash 元素匹配 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs`, `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 0.75 天 | 大幅减少 stale element 错误 | [04-error-recovery.md](./04-error-recovery.md#46-stable-hash-元素匹配) |
| **P1** | 复合控件内联提取 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 0.25 天 | 减少 AI 猜测 | [01-dom-understanding.md](./01-dom-understanding.md#14-复合控件内联提取) |
| **P1** | Fallback Provider | `Demo/BrowserDemo/Services/AiClient.cs`, `Demo/BrowserDemo/Models/AiSettings.cs` | 0.5 天 | 提高可用性 | [03-llm-integration.md](./03-llm-integration.md#36-降级-llmfallback-provider) |
| **P2** | 多动作批处理 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs`, `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` | 1 天 | 有效步数翻倍 | [02-element-interaction.md](./02-element-interaction.md#22-多动作批处理multi-action-batching) |
| **P2** | 输入值不匹配检测 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs`, `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 0.25 天 | 减少表单试错 | [02-element-interaction.md](./02-element-interaction.md#23-输入值不匹配检测) |
| **P2** | 运行时 Replan 触发器 | `Demo/BrowserDemo/Services/AiClient.cs`, `Demo/BrowserDemo/Services/TaskStateMachine.cs` | 0.5 天 | 引导卡住的 AI 重新规划 | [04-error-recovery.md](./04-error-recovery.md#42-运行时-replan-触发器) |
| **P2** | 截图视觉辅助 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 0.5 天 | 表格/图表/验证码辅助 | [03-llm-integration.md](./03-llm-integration.md#33-截图作为视觉辅助browser-vision) |
| **P2** | Session-specific exclude | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 0.5 天 | 减少 snapshot 噪声 | [05-architecture-design.md](./05-architecture-design.md#52-session-specific-exclude-attributes) |
| **P2** | 敏感数据脱敏 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 0.5 天 | 降低泄露风险 | [05-architecture-design.md](./05-architecture-design.md#53-敏感数据脱敏) |
| **P3** | Budget 渐进警告 | `Demo/BrowserDemo/Services/AiClient.cs` | 0.1 天 | 引导 AI 提前收敛 | [04-error-recovery.md](./04-error-recovery.md#44-budget-渐进警告) |
| **P3** | 探索限制 Nudge | `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` | 0.25 天 | 引导 AI 制定计划 | [04-error-recovery.md](./04-error-recovery.md#43-探索限制-nudge) |
| **P3** | 按元素滚动 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 0.25 天 | 精准定位 | [02-element-interaction.md](./02-element-interaction.md#24-按元素滚动--分页滚动) |
| **P4** | 坐标点击兜底 | `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 0.5 天 | element_id 失效兜底 | [02-element-interaction.md](./02-element-interaction.md#21-坐标点击兜底) |
| **P4** | Paint Order 遮挡标记 | `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 0.5 天 | 避免点击被遮挡元素 | [01-dom-understanding.md](./01-dom-understanding.md#12-paint-order-遮挡过滤) |
| **P4** | Judge 后验评估 | 新增 `Demo/BrowserDemo/Services/JudgeService.cs` | 1 天 | 提高完成准确率 | [04-error-recovery.md](./04-error-recovery.md#45-judge-后验评估) |

---

## 推荐实施顺序

### Phase 1：快速见效（P0，约 1 天）

1. **结构化思考框架** — 修改 `Demo/BrowserDemo/Services/ContextBuilder.cs` 的 prompt，增加 `[上步评估]` / `[进度记忆]` / `[下步目标]` 字段要求
2. **DOM text hash 页面停滞检测** — 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 增加 `getDomTextHash`，在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 中集成

### Phase 2：稳定性提升（P1，约 2 天）

3. **Stable hash 元素匹配** — 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 增加 `stable_hash` 字段，在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 注册 `browser_click_by_hash` 工具
4. **复合控件内联提取** — 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 的 snapshot 中展开 `<select>` 选项、标注日期格式
5. **输入值不匹配检测** — 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 的 `browser_type` 结果中增加 expected/actual 对比

### Phase 3：效率优化（P1-P2，约 2 天）

6. **Budget 渐进警告** — 在 `Demo/BrowserDemo/Services/AiClient.cs` 中 50%/75%/90% 注入提醒
7. **坐标点击兜底** — 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 新增 `browser_click_at(x, y)` + 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 增加 snapshot viewport_center
8. **运行时 Replan 触发器** — 在 `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` 连续失败 3 次后引导重新规划，在 `Demo/BrowserDemo/Services/TaskStateMachine.cs` 支持运行时 replan
9. **Session-specific exclude** — 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` 注入过滤 toast/modal 等覆盖层的脚本

### Phase 4：进阶功能（P2-P4，约 3 天）

10. **LLM 摘要压缩** — 在 `Demo/BrowserDemo/Services/AiClient.cs` 用 LLM 摘要替代简单截断
11. **截图视觉辅助** — 在 `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` 支持多模态模型可选截图
12. **Fallback Provider** — 在 `Demo/BrowserDemo/Services/AiClient.cs` + `Demo/BrowserDemo/Models/AiSettings.cs` 实现备用 provider 自动切换
13. **敏感数据脱敏** — 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 对密码/信用卡字段脱敏
14. **Paint Order 遮挡标记** — 在 `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` 对被高 z-index 遮挡的元素标记
15. **Judge 后验评估** — 新增 `Demo/BrowserDemo/Services/JudgeService.cs`，在任务完成时 LLM judge 验证

---

## 文件修改总览

| 文件 | 涉及的改进项 |
|------|-------------|
| `Demo/BrowserDemo/Services/ContextBuilder.cs` | 3.1 XML 分段、3.2 结构化思考、3.5 结构化输出 |
| `Demo/BrowserDemo/Services/Automation/AutomationScripts.cs` | 1.2 遮挡标记、1.4 控件内联、2.1 viewport_center、2.3 值验证、4.1 DOM hash、4.6 stable_hash、5.2 exclude、5.3 脱敏 |
| `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` | 4.1 DOM hash、4.2 失败计数、4.3 探索限制 |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationService.cs` | 2.1 坐标点击、2.3 值验证、2.4 按元素滚动、4.1 DOM hash、4.6 stable_hash、5.2 exclude 注入 |
| `Demo/BrowserDemo/Services/Automation/BrowserAutomationToolRouter.cs` | 2.1 坐标点击工具、2.4 按元素滚动工具、4.6 browser_click_by_hash、4.2 replan_task、3.3 截图频率 |
| `Demo/BrowserDemo/Services/AiClient.cs` | 3.4 LLM 摘要、3.6 Fallback、4.4 Budget 警告、4.2 Replan、5.3 结果脱敏 |
| `Demo/BrowserDemo/ViewModels/ChatViewModel.cs` | 2.2 多动作批处理、4.5 Judge 集成 |
| `Demo/BrowserDemo/Services/TaskStateMachine.cs` | 4.2 运行时 replan |
| `Demo/BrowserDemo/Models/AiSettings.cs` | 3.3 VisionSettings、3.6 FallbackSettings、5.3 SensitiveFields |
| 新增 `Demo/BrowserDemo/Services/JudgeService.cs` | 4.5 Judge 后验评估（新增） |

---

## 与本项目已有优势的互补关系

以下改进项与本项目已有的特色功能形成互补：

| 已有特色 | 互补的 Browser-Use 改进 | 效果 |
|---------|----------------------|------|
| `Demo/BrowserDemo/Services/TaskStateMachine.cs` | 4.2 运行时 Replan | 状态机不仅控制开始，也支持中途调整 |
| `Demo/BrowserDemo/Services/AgentEventSelfHandler.cs` | 4.1 DOM hash + 4.4 Budget | 自检测维度从"工具重复"扩展到"页面停滞"，预算感知 |
| WebView2 内嵌 | 5.2 Session-exclude + 5.3 脱敏 | 内嵌架构下更安全、更干净的 snapshot |
| ask_user 暂停恢复 | 3.2 结构化思考 | 恢复后 AI 能快速理解中断前的进度 |
| 中文系统提示 | 3.1 XML 分段 | 结构化提示词 + 中文语义 = 更好的中文 LLM 适配 |
