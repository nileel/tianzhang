# 自动化提交摘要与日报设计

## 目标

每日自动化简报只报告可核验的自动化产出及其项目影响，不再从控制器对话、memory、租约或当前运行快照推断历史成果。

## 所有自动化责任方的业务提交格式

由 `tzg-hourly-controller` 派发给 Codex 或外部 AI 的每一项业务提交，以及队列维护提交，均保留现有 Conventional Commit 标题，并在正文追加以下字段：

```text
Automation: tzg-hourly-controller
Task: <稳定任务 ID 或 QUEUE-MAINTENANCE>
Result: <本次实际完成的工作，一句>
Impact: <该产出对后续任务、可用能力或已解除约束的影响，一句>
Verify: <直接验证命令及结果，或“未运行：<原因>”>
```

- 字段顺序固定，均为单行；`Result`、`Impact` 和 `Verify` 必须基于本轮事实，不得猜测。
- 无法真实填写任一字段时，不得创建标有 `Automation` 的完成提交，应按既有失败或待决定流程结束。
- 外部 AI 的每个 `businessCommit` 与 Codex 的业务提交使用完全相同的格式；这是外部 AI 实际工作成果的唯一日报统计入口。
- 仅记录交接的 `handoffCommit` 不使用 `Automation` 标记，继续引用对应 `businessCommit` SHA；它不是第二项业务产出，日报不得重复归因。
- 人工提交与非控制器提交不使用该标记。

## 日报生成

日报扫描上一自然日（Asia/Hong_Kong）中正文含 `Automation: tzg-hourly-controller` 的提交，而非遍历控制器对话或读取 runtime/memory。

按提交时间输出最多三项成果卡片，每项包含：

1. `Result`
2. `Impact`
3. 提交短哈希与 `Verify`

没有匹配提交时，日报仅说明“昨日未确认自动化产出”。未标记提交不参与归因，不以标题、任务名或历史 memory 猜测来源。

## 实施边界

- 更新控制器派发提示与 Codex / 外部 AI 的提交约束，使责任方在提交前生成上述正文。
- 更新日报 automation prompt，使其按提交正文生成成果卡片。
- 不修改 `automation-finalize-commit.ps1`：它已将完整 `CommitMessage` 传给 `git commit -m`，可保留多行正文。
- 保留现有调度、租约、恢复、验证和外部 AI 双提交边界。

## 验证

1. 检查受影响规则和提示词均含相同字段及外部 AI 交接排除规则。
2. 使用现有自动化工作流检查验证规则仍符合控制器边界。
3. 读取更新后的日报 automation 配置，确认其以提交标签为唯一历史归因来源，且 cron、模型、执行环境和状态未改变。
