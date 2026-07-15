# 自动工作流待决策生命周期修复设计

日期：2026-07-15
状态：已获用户实施授权

## 背景与根因

任务 `019f620f-3f05-75c3-bd8f-24bafe6ddb65` 暴露的故障并非单点参数错误，而是待决策生命周期在本机状态、项目可见状态、邮件通知和恢复协议之间缺少确定性闭环：

1. `CreateDecision` 只写入本机 JSON，返回 `publish_pending_decision`，但没有修改已经登记为 `expectedPaths` 的 `开发管理/自动工作流状态.txt`。
2. `MarkDecisionNotified` 不要求邮件投递回执；该轮没有发送邮件便把本机状态标为 `NOTIFIED`。
3. `Finish` 随后要求从项目状态路径捕获恢复证据；路径没有变化，workspace guard 返回 `recovery_expected_missing`。
4. `Fail` 无法为不存在的项目变化生成证据，却保留了任务身份和 `mutation_started` 检查点；后续两次恢复将状态推进到 `AUTO-BLOCKED`。
5. 控制器 JSON 没有公开 `CreateDecision` 的参数格式，执行模型实际猜测了三次才成功创建决策。
6. 项目状态文件直到决策创建后才被读取，导致新决策与已经记录的 TQ-057 决策重复。
7. `ResolveDecisionReply` 当前返回 `nextCommand=Finish`，但 fresh 待决策轮次仍处于 `identity_checked`，也没有重新登记原任务路径；即使收到有效回信也无法合法收尾。

现有 `tools/test-automation-controller.ps1` 只验证本机 `pendingDecision` 被创建，没有验证项目状态文件发布、邮件回执、决策提交、回信后的原任务重登记或清理，因此测试通过但真实链路失败。

## 目标

- 待决策创建、通知状态和最终清理都必须在本机状态与项目可见状态之间保持一致。
- 模型不得猜测决策命令参数，也不得在未获得真实投递回执时标记通知成功。
- 创建新决策前必须先暴露并读取既有项目决策历史，降低重复决策风险。
- 有效回复必须回到原任务的候选检查、路径登记和实施流程，不得跨阶段直接 `Finish`。
- 状态发布或通知失败不得制造空提交、缺失恢复证据或 `AUTO-BLOCKED`。
- 修复后清理本次错误产生的重复决策并解除当前 `AUTO-BLOCKED`，留下可审计的人工取消原因。

## 非目标

- 不把 Gmail 发送或搜索实现搬进 PowerShell 控制器。
- 不接受自由格式回复，不改变允许发件人和私有邮件配置规则。
- 不扩大当前队列、复审或 DeepSeek/Claude 主责边界。
- 不放宽普通任务的 workspace guard、恢复证据或提交隔离要求。

## 采用方案

采用“确定性控制器门面 + 专用项目状态发布器”。提示词仍负责调用 Gmail 连接器和语义判断；所有可机械验证的顺序、参数、状态发布、回信重登记和失败关闭由控制器负责。

仅修改提示词不能防止模型漏步骤；允许无项目变化的决策分支直接完成会造成项目负责人看不到待决策。因此这两种方案不采用。

## 组件边界

### `tools/automation-decision-status.ps1`

新增专用发布器，只负责原子更新 `开发管理/自动工作流状态.txt` 的 `## 当前待决策` 区段。它接收控制器提供的决策 JSON，不读取私有邮件配置，不修改其他区段，不负责 Git、租约或状态机。

发布器支持：

- `Publish`：写入决策编号、任务、问题、选项、推荐项、创建时间、通知状态和严格回复格式；
- `Clear`：恢复为“当前无待决策项。”；
- 严格校验区段唯一存在、JSON 字段完整、状态受支持；
- 保留原文件的 UTF-8 BOM、换行风格和区段外字节内容；
- 使用同目录临时文件和原子替换，失败时不留下半写文件。

### `tools/automation-controller-state.ps1`

本机状态继续是决策事实源，升级 schema 并补充：

- `MarkDecisionNotified` 必须接收非空投递回执；本机只保存其 SHA-256，不保存邮件正文或地址；
- `CancelDecision` 仅允许人工以 `-ManualOverride` 取消错误、重复或失效的未解决决策，并记录决策编号、任务、时间和精简原因；自动化模型不得通过控制器门面调用它；
- `Complete` 继续保留未解决或已解决但尚未消费的决策；
- `ClearResolvedDecision` 只在原任务成果和项目状态清理已成功提交后调用。

### `tools/automation-controller.ps1`

控制器新增或调整以下契约：

1. `Contract` 公开 `PrepareDecision`、`CreateDecision`、两种通知结果和 `ResolveDecisionReply` 的准确命令模板、必填参数与 `A=标签|B=标签` 选项格式。
2. `RegisterCandidate` 发现 `expectedPaths` 包含项目状态文件时，把该文件加入 `requiredSources`。
3. `PrepareDecision` 只在任务已登记且 `mutation_started` 后接受调用；验证状态路径已登记，记录当前文件指纹，并返回既有决策上下文与创建命令契约。它不改变任务检查点，因此模型发现已有结论后可继续原语义工作。
4. `CreateDecision` 必须证明 `PrepareDecision` 已执行且状态文件指纹未变。创建本机决策后，立即通过专用发布器写入项目状态。只有项目状态发布成功才返回通知动作。
5. 如果项目状态发布失败，控制器撤销本轮新建决策、确认项目没有残留变化、释放任务和租约并返回稳定错误；不得留下不可恢复的 `mutation_started`。
6. `MarkDecisionNotified` 缺少投递回执时拒绝且不改变状态；成功或 `MarkDecisionDeliveryFailed` 后都重新发布项目状态，再返回 `Finish`。
7. `Finish` 对待决策创建轮次沿用正常 guard、恢复证据、finalizer 和提交链。因为项目状态必然已变化，不再出现 `recovery_expected_missing`。
8. fresh `Start` 发现未解决决策时继续返回其摘要和严格回复契约；没有有效回复时仍可按既有规则选择不受影响的候选。
9. `ResolveDecisionReply` 成功后把当前 session 恢复为候选检查阶段，返回原 `taskId` 和 `nextCommand=InspectCandidate`，不再返回 `Finish`。
10. 原任务重登记时必须把项目状态文件包含在完整 `expectedPaths` 中。原任务 `Finish` 前，控制器把项目状态区段清为“当前无待决策项。”；提交成功后才调用 `ClearResolvedDecision`。

## 数据流

### 创建与通知

`Start → InspectCandidate → RegisterCandidate（含状态路径）→ BeginMutation → PrepareDecision → CreateDecision → Gmail send → MarkDecisionNotified/MarkDecisionDeliveryFailed → Finish`

- `PrepareDecision` 之前不能创建决策。
- Gmail 成功必须把连接器返回的投递标识传给 `MarkDecisionNotified`。
- Gmail 不可用或发送失败必须调用 `MarkDecisionDeliveryFailed`，仍提交项目可见的待决策状态。

### 回复与恢复原任务

`Start（inspect_pending_decision）→ Gmail search → ResolveDecisionReply → InspectCandidate（原 taskId）→ RegisterCandidate（业务路径 + 状态路径）→ BeginMutation → 实施已有选择 → Finish`

- 回复解析仍只接受同一编号、允许发件人和单选严格格式。
- `Finish` 成功提交业务成果与状态清理后，才清除本机已解决决策。
- 提交失败时保留已解决决策和精确恢复证据，下一轮按原任务恢复。

## 错误处理

- 决策上下文未暴露：返回 `decision_context_not_prepared`，保持原任务可继续。
- 项目状态文件在准备后变化：返回 `decision_context_changed`，不创建决策。
- 项目状态发布失败：撤销新决策并以无项目残留方式关闭当前轮次。
- 缺少邮件投递回执：返回 `notification_receipt_missing`，不得标记 `NOTIFIED`。
- 通知失败：记录受限错误类别、发布 `DELIVERY_FAILED`，正常提交可见状态。
- 有效回复后的原任务缺少状态路径：`RegisterCandidate` 拒绝并要求补全路径，不允许隐藏清理动作。
- 普通业务修改、路径冲突和恢复异常继续使用现有失败关闭规则。

## 当前故障状态修复

实现和验证通过后执行一次人工维护：

1. 使用 `ResetBlocked`，原因明确记录为待决策发布协议缺陷已修复；
2. 获取人工维护租约；
3. 使用不对自动化模型开放的 `CancelDecision -ManualOverride` 取消 `DEC-20260714-87F6870C80C3`，原因为它重复覆盖已有 TQ-057 决策且未实际投递；
4. `Complete` 释放租约；
5. 确认状态为 `IDLE`、无 `pendingDecision`、无恢复指针，并在自动化 memory 留下精简审计记录。

已有项目状态已明确记录：11 项古修术法按 DEC-20260714-6E0E6BCF974C 选择补齐，`realm_lianshen` 按 DEC-20260714-29A5D1356CC8 选择补语言键并保留引用。因此取消本次组合型重复决策不产生新的业务选择。

## 测试与验收

先写失败测试并确认失败原因，再实现：

1. 状态发布器测试：发布、通知状态更新、清理、BOM/换行保留、区段外内容不变、非法输入失败不写。
2. 控制器创建链回归：未 `PrepareDecision` 时拒绝；准备响应公开完整参数；创建后项目状态确实变化。
3. 通知回执回归：无回执不能标记成功；有回执后状态文件显示 `NOTIFIED`；投递失败显示 `DELIVERY_FAILED`。
4. 决策收尾回归：`Finish` 生成包含项目状态文件的提交，状态回到 `IDLE`，决策保留且不存在恢复残留。
5. 回复恢复回归：有效回复返回 `InspectCandidate`；重新登记原任务且包含状态路径后，业务变化与状态清理同一提交完成，本机决策随后清除。
6. 人工取消回归：非人工调用被拒绝；人工取消记录审计摘要且不伪造为用户选择。
7. 运行自动化控制面完整验证：controller、state、workspace guard、finalizer、workflow checker、review text、行尾检查和 Git 差异检查。
8. 通过 Codex 自动化更新接口部署与版本化提示词逐字一致的 prompt，并再次运行 workflow checker。
9. 用隔离状态和临时 Git 仓库完成真实决策创建、通知失败、提交、有效回复、原任务恢复和清理的端到端演练；不发送生产邮件。

验收条件：所有测试通过；部署 prompt 与版本源一致；当前控制器从 `AUTO-BLOCKED` 恢复到干净 `IDLE`；错误重复决策已审计取消；工作区无意外变化；下一次小时轮次可正常进入选题或待决策检查。

## 自审

- 所有接口和错误分支均已明确，无占位内容。
- 本机状态、项目状态、邮件结果和 Git 提交的职责边界明确。
- 不以放宽 guard 或空提交规避故障。
- 不改变任务主责、内容冻结、Gmail 白名单或自动推送规则。
- 当前错误决策的取消依据来自已记录的两个既有决定，不引入新的业务语义。
