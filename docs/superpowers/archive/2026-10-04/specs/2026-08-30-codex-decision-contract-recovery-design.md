# Codex 决策终态合同修复与现有 checkpoint 恢复设计

## 背景

Codex run `f7ad64c6-12e2-4184-8c6d-7b1dcb3fa738` 为任务 `A-CHAR-BATTLE-STATIC3D-PLAYER-CONCEPT-01` 生成了干净、唯一、直接后继 checkpoint `51c40ca7f079886e9f48b9ce30b421ff5033daec`。模型返回 `needs_decision` 时保留了有效问题、A/B/C 选项、推荐项和影响说明，但 `decisionId` 与 `plainSummary` 为空。结构化输出 schema 允许这些空值，后置 `Assert-Decision` 又要求非空，最终以 `codex_decision_invalid` 进入 `attention_required`。

## 目标与非目标

目标：

- 消除 direct decision 中机械字段由模型自由填写造成的合同缝隙。
- 保持无效语义字段继续硬失败，不增加重试或猜测性兜底。
- 保留现有 checkpoint，不重新处理或生成图片。
- 用既有 `pending_decision`、回复恢复和 checkpoint replay 链完成负责人审批。

非目标：

- 不改变 maintenance decision、review rework 或其他终态协议。
- 不新增 runtime 状态、恢复队列、后台任务或事故专用兼容入口。
- 不把 checkpoint 业务文件直接合并进 `master`，也不代替负责人视觉批准。

## 决策合同设计

direct `needs_decision` 的职责重新划分如下：

- 模型负责：`question`、按 A/B/C 排序的三个非空选项、`recommendedOption`、`impactSummary`、验证证据和剩余风险。
- wrapper 负责：确定性的 `decisionId` 和 `plainSummary`。

`decisionId` 由任务 ID、runId、checkpoint SHA 和 run 启动日期生成，输出继续符合 `DEC-[0-9]{8}-[A-Z0-9]+`。同一 run 与 checkpoint 必须得到同一 ID，不读取当前时间形成漂移。

`plainSummary` 机械构造：

- `situation` 使用已校验的 `question`。
- `impact` 使用已校验的 `impactSummary`。
- `action` 使用已校验的推荐项 key 与对应非空 label。

wrapper 不再信任模型返回的 direct-decision `decisionId` 或 `plainSummary`。终态 schema 仍要求所有字段存在；提示词明确 direct `needs_decision` 的上述所有权，避免模型把机械字段误当业务输入。

`Assert-Decision` 继续核对 checkpoint SHA、唯一父链、干净工作树、精确 changed paths 和授权路径，并新增：

- 三个 option key 必须精确为 A/B/C。
- 每个 option label 必须非空。
- `recommendedOption` 必须属于 A/B/C。
- `question` 与 `impactSummary` 必须非空。

任何语义字段失败仍返回 `codex_decision_invalid`；不得生成默认选项或猜测负责人意图。

## 当前 run 恢复设计

恢复前重新执行 schema 5 `Show`，并精确核对 owner、runId、taskId、recoveryReason、taskCardDigest、baseCommit、worktree、candidate branch、HEAD、父提交、changed paths、工作树清洁度、进程和集成锁。任一事实变化立即停止。

证据一致时：

1. 依据现有会话终态中的问题、选项、推荐项、影响、验证和风险，使用修复后的确定性规则构造 decision context。
2. 保留 candidate branch 指向原 checkpoint；不修改 checkpoint 内容或提交。
3. 在现有 owner worktree 中从最新 `master` 建立状态投影分支，调用既有 `PauseDecision`，把任务从 `ready` 转为 `pending_decision`，同步移出队列和 backlog 投影，并把完整 checkpoint context 写入任务卡。
4. 运行任务卡、审核文本、whitespace 与暂存差异检查；在进程持有型集成锁下 fast-forward 状态投影。
5. 以精确 recovery reason 关闭原 `attention_required` run，保留 checkpoint branch 与 worktree。
6. 写入单一 decision request 并发送一次负责人审批卡。投递未被 provider 接受时只报告，不自动重试或回滚已准确落盘的 `pending_decision` 状态。

负责人有效回复后，既有流程恢复任务为 `ready`，创建新 run 和新 worktree，并以 `cherry-pick --no-commit` 吸收原 checkpoint。新责任方复核实际回复、完整差异和验证后形成唯一正式 candidate；旧模型会话不恢复。

## 修改范围

计划修改：

- `tools/invoke-codex-candidate.ps1`
- `tools/test-invoke-codex-candidate.ps1`
- 本设计文件

不修改 runtime schema、任务业务产物、图片、Unity、Blender、BattleSim 或自动化配置。

## 验证

- 新增与本次现场等价的 fixture：有效 checkpoint、有效语义字段、空 `decisionId`、空 `plainSummary`，应成功返回规范化的 `decision_checkpoint`。
- 断言生成 ID 格式正确且同一 run/checkpoint 稳定。
- 断言生成的三段 `plainSummary` 与问题、影响和推荐选项精确对应。
- 新增空 label、非法推荐项 fixture，必须返回 `codex_decision_invalid`。
- 运行 `tools/test-invoke-codex-candidate.ps1`。
- 运行 `tools/check-automation-workflow.ps1`、预期路径 whitespace、`git diff --check`；设计文档变化触发 `tools/check-data-chain.ps1`。

## 停止条件

- 现有 run、task digest、base、branch、checkpoint、changed paths 或工作树证据与本设计记录不一致。
- 状态投影路径与主工作区人工改动冲突，或集成锁不可取得。
- fixture 不能复现并覆盖本次空字段失败。
- 恢复需要修改 checkpoint 业务内容、直接合并未批准图片、创建新 runtime 状态或引入重试层。
