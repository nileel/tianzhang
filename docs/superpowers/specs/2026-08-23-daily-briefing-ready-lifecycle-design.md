# 每日自动化简报 ready 生命周期修复设计

## 背景与根因

`tools/get-automation-briefing-source.ps1` 会在每个带有效 Automation 元数据的提交上读取同一提交快照中的任务卡，并据此确定生命周期分类。现有实现只接受归档任务的 `completed`，以及活跃任务的 `blocked`、`frozen`、`pending_decision`、`waiting_reply`；`State: pending_review` 和 `QUEUE-MAINTENANCE` 由提交元数据单独分类。

复审返工决策提交会把同一卡从 `codex_review/codex/blocked` 恢复为 `external_execute/deepseek/ready`，同时把任务重新加入当前队列。`ready` 是有效且可核验的任务生命周期，但不在日报分类白名单中，因此这类提交被提前记为 `outcome_unverifiable`，无法进入后续的按 Task 合并。即使同一窗口内后来出现归档 `completed` 提交，早先错误也不会被消除。

## 目标

- 将具备完整任务事实和队列投影的 `ready` 识别为可核验的未闭环生命周期。
- 同一 Task 在时间窗内出现多个正式提交时，只以最新正式提交的分类作为任务组最终分类；中间 `ready` 不单列异常。
- 保持真正缺少或冲突的生命周期事实继续 fail closed。
- 测试通过后，将项目内权威提示词同步到现有 `tzg-daily-automation-briefing`，不改变其其他配置。

## 主责与原子边界

整项实现固定由 Codex 单一主责，范围同时包含：仓库内源脚本、测试、权威提示词、正式集成，以及通过 Codex automation 管理能力同步现有日报 automation。不得把仓库修改单独路由给 DeepSeek，也不得把 automation 同步遗留给另一张卡或后续轮次；否则本项不能原子闭环。

DeepSeek 可以提供只读审核证据，但不得执行 automation 配置读取后的写入、创建替代 automation 或代替 Codex 完成同步。

## 非目标

- 不修改任务卡 schema、当前业务队列、审核规则、runtime schema 或 Automation 提交元数据合同。
- 不改写历史提交或为五个历史提交补造交接记录。
- 不根据提交标题推断生命周期，不新增兼容分支或第二套日报数据源。
- 不改变日报时间窗、飞书投递、runtime 检查或当前队首读取逻辑。

## 设计

### ready 事实验证

在 `Get-CommittedTaskState` 读取到活跃任务卡的 `dispatchState=ready` 时，继续读取同一提交快照中的 `开发管理/当前任务队列.txt`。只有满足以下全部条件时返回分类 `ready`：

1. 活跃卡存在、归档卡不存在；
2. 任务卡元数据可解析，且 `id` 与提交的 `Task` 精确一致；
3. `dispatchState` 精确为 `ready`；
4. 当前队列中恰好存在一条首列 ID 精确匹配该 Task 的 Markdown 表格行。

任务卡为 `ready` 但队列缺行、重复行或无法读取时仍返回空分类，最终保留 `outcome_unverifiable`。其他现有分类和失败条件不变。

`ready` 分类刻意不检查任务卡的 `route` 或 `owner`。`ready` 表示任务已经进入有序队列、等待卡片声明的对应 owner 新轮次领取；它同时适用于 `external_execute/deepseek/ready`、`codex_execute/codex/ready` 和 `codex_review/codex/ready`。日报只陈述已提交生命周期，不在数据源中复制调度路由规则或根据执行／复审类型过滤 ready。

### 同 Task 合并

不新增第二次状态推断。`ready` 提交通过单提交验证后成为普通 candidate，沿用当前按提交时间正序加入 Task 组、每次用最新 candidate 覆盖 `group.category` 的逻辑：

- `ready → completed`：组最终分类为 `completed`，中间 ready 提交保留在 `commits` 证据数组中，不进入 errors；
- `ready → pending_review`：组最终分类为 `pending_review`；
- 窗口结束时仍为 `ready`：组最终分类为 `ready`，进入未闭环；
- 任何单提交的元数据或任务事实仍不可验证：继续进入 errors，不用后续提交掩盖真正损坏的证据。

### 提示词

更新 `开发管理/自动化简报提示词.txt`：

- 生命周期分类列表增加 `ready`；
- “未闭环”明确包含 `ready`，解释为任务已经排队、等待对应 owner 的新轮次领取；
- 继续要求按 Task 的窗口内最新正式提交分类，不把中间 requeue 当作独立成果或异常。

### Automation 同步

仓库实现和相关测试通过后，通过 Codex automation 管理接口查看现有 `tzg-daily-automation-briefing`，使用完整现值执行更新，只替换 `prompt` 为项目内权威提示词。必须原样保留：

- ID、名称与 cron 类型；
- `ACTIVE` 状态；
- 每日 01:00 的 recurrence；
- `gpt-5.6-terra`、`medium` 推理强度；
- local execution environment、项目目标和通知策略。

同步后运行现有自动工作流一致性检查；不立即触发日报业务运行。

### 实施与同步顺序

仓库权威提示词和用户级 automation prompt 无法跨 Git 与 automation 管理接口原子更新，因此允许一个受控、同任务内的短暂不一致窗口。顺序固定为：

1. 在隔离 worktree 完成源脚本、测试和权威提示词修改；运行日报源测试及仓库内静态检查，但此时不运行或不要求 `check-automation-workflow.ps1 -RequireActive` 通过，因为主分支权威提示词和线上 prompt 尚未同时切换；
2. 按项目手动集成规则核验活动 run、集成锁、最新任务事实和路径冲突，将仓库提交正式集成到 `master`；若集成条件不满足则停止，不先改 automation；
3. 仓库集成成功后，由同一 Codex 任务立即查看现有 automation 全字段并只替换 prompt；不得把同步延后给下一轮；
4. 同步成功后从最新 `master` 运行 `tools/check-automation-workflow.ps1 -RequireActive`，以 prompt 逐字一致和 ACTIVE 状态作为最终闭环证据。

步骤 2 完成到步骤 3 完成之间，`check-automation-workflow.ps1` 的 prompt 一致性断言预期失败，这是已知过渡态而非新的根因。若步骤 3 失败，立即停止并报告“仓库新提示词／automation 旧提示词”的明确不一致，不继续其他自动化修改、不创建重复 automation，也不宣称任务完成。

## 测试矩阵

扩充 `tools/test-get-automation-briefing-source.ps1`：

1. `external_execute/deepseek/ready` 卡且同提交队列恰有一行：产生 `ready` group，无 error；
2. 同一 Task 先 ready、后移动到归档 completed：组保留两个 commit，最终 category 为 `completed`，无 ready 异常；
3. `codex_review/codex/ready` 卡且队列匹配：同样产生 `ready`，证明分类刻意与 route/owner 无关；
4. ready 卡但同提交队列缺少该 Task：产生 `outcome_unverifiable`；
5. ready 卡存在但当前队列文件缺失：产生 `outcome_unverifiable`；
6. ready 卡且队列含两条同 Task 行：产生 `outcome_unverifiable`；
7. 同一提交中活动卡与归档卡并存：产生 `outcome_unverifiable`；
8. 任务卡元数据 `id` 与提交 `Task` 不匹配：产生 `outcome_unverifiable`；
9. 现有 blocked、pending_decision、completed、pending_review、queue maintenance 分类继续通过；
10. 现有非法元数据、无任务事实、时间窗、handoff 排除测试继续通过。

最小验证集：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-automation-briefing-source.ps1`
- 用覆盖 2026-08-22 五个目标提交的只读时间窗运行日报源，确认五个 SHA 均不再进入 errors，且 `C-SECT-ALIGN-01C` 最终 category 为 `completed`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理`
- 对本轮路径运行 pending-whitespace 检查；暂存后运行 `git diff --cached --check`
- 正式集成并同步 automation 后，运行 `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireActive`

## 验收条件

- `f32da8ff`、`8f9f5808`、`9f114537`、`3c714769`、`cbd4ea4c` 不再因其提交快照中的有效 ready 生命周期产生 `outcome_unverifiable`。
- `C-SECT-ALIGN-01C` 在包含后续归档提交的窗口内最终分类为 `completed`。
- 当前仍处于 ready 的任务最终分类为 `ready`，不会被误写为 `completed`。
- 缺失任务卡、活动卡与归档卡并存、卡片 ID 不匹配、ready 缺少精确队列投影时继续报 `outcome_unverifiable`。
- 现有 automation 除 prompt 外的所有字段保持不变。

## 回滚

仓库修改仅涉及日报源脚本、对应测试和权威提示词，可用单一提交回退。若 automation 同步后的项目一致性检查失败，停止且报告差异，不修改调度或其他 automation 字段，也不创建重复 automation。

若 automation prompt 已同步成功后需要回退仓库提交，回退同样由 Codex 执行，并在仓库回退集成后立即把现有 automation prompt 再次同步为回退后的权威提示词，最后重跑 `check-automation-workflow.ps1 -RequireActive`。不得只回退 Git 而把 automation 留在新 prompt。
