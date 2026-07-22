# 自动化提交摘要与日报设计

## 目标

每日自动化简报只报告可核验的自动化产出及其项目影响。Git 提交元数据只负责定位候选成果；提交实际差异、已提交的任务状态和外部交接记录负责支持 `Result` 与 `Impact`，不得把责任方自述直接当成独立证明。

日报不再遍历全天控制器对话，也不使用 memory、租约或当前 runtime 快照重建历史。失败、阻塞和待决定仍由既有控制器任务与通知负责；本设计不把成果日报扩展成第二套运行状态系统。

## 所有自动化责任方的提交格式

由 `tzg-hourly-controller` 派发给 Codex 或外部 AI 的每一项业务提交、复审提交和队列维护提交，均保留现有 Conventional Commit 标题，并在正文末尾追加以下固定字段：

```text
Automation: tzg-hourly-controller
Task: <稳定任务 ID 或 QUEUE-MAINTENANCE>
State: <completed 或 pending_review>
Result: <本次实际完成的工作，一句>
Impact: <已确认的直接影响，一句；没有时写“无已确认的下游影响”>
Verify: <本轮已通过的直接检查及结果摘要>
```

- 六个字段均只出现一次、各占一行；字段值不得换行，不得根据预期效果猜测事实。
- Codex 已闭环的执行、复审和队列维护提交使用 `State: completed`。
- 外部 AI 的每个 `businessCommit` 使用完全相同的格式，但固定使用 `State: pending_review`；外部 AI 的直接验证不等于 Codex 复审通过。
- 外部成果后续由 Codex 自动复审并形成新提交时，复审提交使用 `State: completed`，并在 `Result` 中明确通过、返工或关闭的实际结论。
- `Impact` 只写提交后已经成立的能力、状态变化或明确解除的依赖。没有可核验证据时写“无已确认的下游影响”，不得为了满足格式扩大推断。
- `Verify` 只记录本轮真实通过的最小充分检查；日报展示该记录但不把它描述成独立复验，也不在生成日报时重跑领域检查。
- 仅记录交接的 `handoffCommit` 不使用 `Automation` 标记，继续引用对应 `businessCommit` SHA；它不是第二项业务产出，日报不得重复归因。
- 人工提交与非控制器提交不使用该标记。

## 提交边界与防漏门禁

所有由控制器启动的 Codex 执行、Codex 复审、队列维护和外部 AI 业务提交统一使用现有 `tools/automation-finalize-commit.ps1`。该工具增加可选的 `RequireAutomationMetadata` 门禁；控制器责任方必须启用，其他既有调用保持原行为。

启用门禁时，调用方只以 `CommitMessage` 传递单行 Conventional Commit 标题，并分别传入 `AutomationTask`、`AutomationState`、`AutomationResult`、`AutomationImpact` 和 `AutomationVerify`。finalizer 固定写入 `Automation: tzg-hourly-controller` 并在进程内组装多行正文，避免外部 CLI 通过 shell 传递多行参数或扩大工具权限。

门禁在修改 Git index 前检查：

1. 六个字段完整且不重复。
2. `Automation` 精确等于 `tzg-hourly-controller`。
3. `Task` 非空；队列维护固定使用 `QUEUE-MAINTENANCE`。
4. `State` 只能是 `completed` 或 `pending_review`。
5. `Result`、`Impact` 和 `Verify` 均为非空单行文本。

门禁只验证结构，不判断业务语义。结构错误时不得提交，沿用当前责任方的失败或恢复流程，不自动清理、回退或另建补录状态。现有 expected paths、人工改动隔离和路径限定提交边界保持不变；不新增脚本、数据库、manifest、runtime 字段或阶段状态机。

## 日报生成

日报以 Asia/Hong_Kong 的上一自然日为时间边界，扫描正文含 `Automation: tzg-hourly-controller` 的可达提交，并按以下顺序处理：

1. 解析六个固定字段。带 `Automation` 标记但缺字段、重复字段或字段值非法的提交进入“统计完整性错误”，不得静默忽略。
2. 读取候选提交的实际 diff，并按 `Task` 读取该提交中已更新的任务、归档或交接事实。`Result` 或 `Impact` 与已提交事实矛盾时，标为“元数据与提交事实不一致”，不得提升为已确认成果。
3. 外部 `businessCommit` 继续检查对应交接记录；交接缺失时仍列为“已产出待复审”，同时增加完整性警告，不把它写成已闭环成果。
4. 不重跑验证，不读取控制器对话、memory、租约或 runtime 来补写历史结果。

输出不设置成果数量上限。相同 `Task` 的多个提交合并为一张卡片，但必须保留全部不同的 `Result`、状态变化和提交短哈希；无法无损合并时分行列出。成果按以下栏目组织：

1. 已闭环业务产出
2. 外部 AI 已产出待复审
3. 队列维护
4. 统计完整性错误（仅有异常时出现）

每项至少包含 `Result`、`Impact`、`State`、提交短哈希与 `Verify`。队列维护不得与业务成果混排，也不得占用或挤掉业务成果。未标记提交不按标题或作者猜测为自动化成果。

只有当整段统计区间都处于新门禁启用之后、没有匹配提交且没有完整性错误时，才输出“昨日未确认自动化产出”。Git 查询或事实核对失败时应准确报告简报生成失败，不得把读取失败写成没有成果。

## 启用顺序与首日边界

1. 先更新项目规则、责任方提示、finalizer 门禁和直接测试。
2. 验证门禁后，再更新 `tzg-hourly-controller` 的完整现有配置，使它向所有责任方传递统一提交要求。
3. 最后更新日报 automation prompt，并在配置中记录实际启用时间作为首日统计边界。
4. 首份跨越启用时间的日报明确标注“新格式仅覆盖启用后的提交”；不回写、amend 或重写历史提交。
5. 从首个完整自然日起，日报按正常规则生成，不再显示切换提示。

启用时间只用于日报判断首个不完整统计日，不进入项目状态文件或新增 runtime；不改变现有 cron、模型、执行环境、租约、恢复、验证和外部 AI 双提交边界。

## 实施范围

- 更新 `开发管理/自动工作流规则.txt`、`开发管理/AI协作规则.txt`、`开发管理/DeepSeek工作提示词.txt` 和控制器提示词中的提交约束。
- 最小扩展 `tools/automation-finalize-commit.ps1`，复用现有提交入口实现可选元数据门禁。
- 扩展现有 finalizer 测试，不创建第二个提交工具或日报数据库。
- 通过自动化管理能力更新控制器和日报的完整现有配置，不直接编辑 automation TOML。
- 不改变任务选择、单写入租约、CLI session、决策恢复、外部 AI 权限、审核或失败关闭逻辑。

## 验证

1. 扩展 `tools/test-automation-finalize-commit.ps1`，证明结构化中文参数生成的多行提交正文原样保留；启用门禁时缺字段、非法 `State` 和多行字段均在修改 index 前失败，且 `Automation` 只能由 finalizer 固定生成；未启用门禁的既有用例保持通过。
2. 检查项目规则、Codex 入口、外部 AI 入口和控制器提示词均使用相同字段、状态语义及 `handoffCommit` 排除规则。
3. 使用临时 Git fixture 覆盖 Codex 完成、外部待复审、交接提交、队列维护、同任务多提交和异常元数据，确认候选查询不漏项、不重复统计交接提交，且不受三项上限约束。
4. 运行现有自动化工作流检查，确认薄路由、单写入和外部双提交边界未改变。
5. 读取更新后的两项 automation 配置，确认 prompt 已更新，而 cron、模型、执行环境和状态均保留原值。
