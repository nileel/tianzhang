# 自动工作流待决策生命周期 v6 完整修复设计

日期：2026-07-15
状态：已获用户逐节设计确认，待实施计划
取证对话：`019f63c5-f73c-70a0-9773-5592a3e03194`

## 文档关系

本文取代本文件早先记录的单决策生命周期修复方案。早先方案解决了项目状态发布、提供方回执和回复后重登记原任务等问题，但只验证了“一次决策 → 一次实施 → 成功 Finish”的理想路径。本次真实对话证明它没有覆盖错收件人、聊天选择来源、同一任务连续决策，以及 `mutation_started` 阶段失败但尚无恢复证据的组合场景。

## 事故摘要与根因

本次故障由五个相互放大的问题组成：

1. 决策邮件调用 Gmail `to: "me"`，没有使用本机私有配置中的负责人目标地址。连接器返回的 `SENT/INBOX/UNREAD` 只证明邮件被创建在已连接 Gmail 账户中，不能证明发给了负责人。
2. 用户报告未收到邮件后，流程没有检查 Sent 中的精确消息，而是以空文本调用 `ResolveDecisionReply`。无效回复被错误写入通知状态，原提供方回执也被覆盖。
3. 用户在聊天中明确选择 `B` 后，流程把它改写为严格邮件格式；控制器又固定记录 `ReplySource=email`，导致人工选择被伪造为邮件回复。
4. 第一次创建决策前只检查了数据数量、历史归档和检查器错误，没有读取 11 份术法文档及当前 `SpellData/CSV` 表达能力。选择 B 后才发现扶摇、演卦需要同时保存物理与神魂倍率，于是同一任务必须产生第二项架构/数据语义决策。
5. 本机状态只容纳一个 `pendingDecision`。第一项虽已标为 `RESOLVED`，仍要等原任务成功 `Finish` 才清除；`CreateDecision` 又在对象非空时无条件拒绝。失败发生在 `mutation_started`，控制器返回 `preserve_recovery`，但恢复证据尚未生成。下一小时轮次因此稳定报 `recovery_state_incomplete`。

## 目标

- 每个任务最多只有一个未解决决策，但同一任务可以按顺序产生多项决策。
- 已做出的选择在业务实施完成前不会丢失，也不会继续伪装为待决策。
- 邮件必须发往私有配置指定的目标；提供方接受发送不得表述为用户已收到。
- 邮件回复和聊天人工选择使用不同入口，并保存真实来源证据。
- 空回复、模糊回复、错误编号和未知发件人不得修改通知状态或历史回执。
- `Fail` 必须区分无项目变化的干净终止和已有变化的可恢复中断。
- 不允许产生“过期 RUNNING + 无 recovery evidence”的状态。
- 通过受控迁移修复当前现场，保留第一项 B 选择并创建第二项真实待决策。

## 非目标

- 不修改 TQ-057 的术法数据、运行时模型或双倍率业务口径。
- 不替负责人选择扶摇、演卦的实现方案。
- 不引入通用事件溯源系统或重写全部自动化架构。
- 不放宽 workspace guard、路径限定提交、主责映射、内容冻结或两次恢复上限。
- 不把私有邮件地址写入 Git、项目状态、automation memory 或最终回复。

## 采用方案

采用状态 schema v6：“单一活动决策 + 当前任务决策链 + 受限历史摘要”。

没有采用两种替代方案：

- 原地清空 `RESOLVED` 再创建下一项决策，虽然改动小，但会丢失实施前审计上下文，并让重启后的任务归属不清。
- 把所有状态改造成追加式事件日志，审计能力最强，但会显著扩大控制器、迁移和恢复范围，超过当前故障的最小充分修复。

## 状态模型 v6

### `pendingDecision`

`pendingDecision` 只表示当前尚未解决的一项决策。允许状态为：

- `PENDING`：已创建，尚未尝试发送；
- `PROVIDER_ACCEPTED`：提供方已接受发送请求，不代表收件人已收到；
- `DELIVERY_FAILED`：连接器或发送过程失败，可在上限内重试；
- `MISADDRESSED`：Sent 消息目标与私有配置不一致；
- `RETRY_EXHAUSTED`：已达到三次发送上限。

`RESOLVED` 不再作为活动决策状态长期保存在 `pendingDecision`。

### `decisionFlow`

`decisionFlow` 表示一个尚未完成业务实施的决策链：

```text
decisionFlow
├─ taskKind
├─ taskId
├─ openedAt
├─ status: AWAITING_DECISION | IMPLEMENTATION_PENDING
└─ resolvedDecisions[]
   ├─ decisionId
   ├─ question/options/recommendedOption
   ├─ notificationAttempts[]
   └─ resolution
      ├─ optionKey
      ├─ source: email | manual
      ├─ resolvedAt
      └─ evidenceHash
```

决策解析时执行一个原子状态转换：

1. 把完整决策及其通知尝试复制到 `decisionFlow.resolvedDecisions`；
2. 写入真实 `resolution.source` 和证据哈希；
3. 清空 `pendingDecision`；
4. 把 `decisionFlow.status` 设为 `IMPLEMENTATION_PENDING`；
5. 返回原 `taskId`，要求重新执行 `InspectCandidate`。

当同一任务恢复实施并发现新的必要选择时，只有以下条件全部满足才允许创建下一项决策：

- `pendingDecision` 为空；
- `decisionFlow.taskId` 与当前任务完全一致；
- 当前任务已重新登记项目状态路径并进入合法 mutation 阶段；
- 新问题不能由 `resolvedDecisions` 中已有选择唯一推出。

不同任务不得复用或覆盖活动 `decisionFlow`。原任务及其传递后继继续被阻塞，其他无依赖任务仍可按现有规则运行。

### 完成与历史

原任务成功 `Finish` 后，控制器才清除活动 `decisionFlow`。本机保留一个受限、无地址正文的 `lastCompletedDecisionFlow` 摘要，完整业务证据继续由 Git 提交、项目状态历史和 automation memory 承担。历史字段不得保存原始邮件地址、正文或提供方消息 ID，只保存哈希和决策摘要。

## 项目可见状态

`tools/automation-decision-status.ps1` 扩展为三个发布模式：

- `PublishPending`：显示当前最新问题、选项、推荐项和发送状态；
- `PublishImplementationPending`：显示选择已登记、来源为邮件或人工、等待原任务实施；
- `Clear`：原任务成功提交后恢复“当前无待决策项”。

当同一任务出现第二项决策时，项目状态显示第二项为当前问题，并列出第一项已选 B 的精简摘要。不得继续显示旧决策为 `NOTIFIED` 并要求重复回复。

## 通知寻址与投递语义

### 准备通知

新增 `PrepareDecisionNotification` 控制器动作：

- 验证本机私有配置存在且 `recipientEmail` 非空；
- 计算并在 session 中保存目标地址 SHA-256；
- 返回决策编号、主题、正文和目标配置引用；
- 禁止以 `"me"`、历史消息收件人或硬编码地址替代配置目标；
- 地址只允许用于连接器调用，不得写入项目文件、本机公共状态、memory 或最终回复。

### 标记发送结果

Gmail 返回成功后，必须读取精确 Sent 消息并取得实际 `To` 字段，再调用 `MarkDecisionSubmitted`。控制器比较实际目标哈希与 session 中的配置目标哈希：

- 相同：记录 `PROVIDER_ACCEPTED`；
- 不同：记录 `MISADDRESSED`，不得声称已通知；
- 无法读取 Sent 消息：保留原状态并返回可重试错误。

每次通知尝试追加不可覆盖的记录：时间、结果、目标哈希、提供方消息 ID 哈希和精简错误类别。重试不会清除早先回执。

### 未收到与重试

用户报告未收到时走显式 `RetryDecisionNotification`：

1. 按原消息 ID 检查 Sent 中的实际目标；
2. 如果目标错误，记录 `MISADDRESSED`；
3. 如果目标正确但用户仍未收到，按私有配置重新发送；
4. 总尝试次数最多三次；
5. 达到上限后标记 `RETRY_EXHAUSTED` 并保持待决策，不自动选择。

提供方接受发送只允许表述为“发送请求已被提供方接受”，不得表述为“用户已收到”。

## 回复来源与验证

### 邮件回复

`ResolveDecisionEmailReply` 只消费 Gmail 搜索得到的真实邮件：

- 在全邮箱按同一 `decisionId` 搜索；
- 校验 `allowedReplyFrom` 与别名；
- 校验严格单选格式；
- 要求提供方消息 ID；
- 保存消息 ID 哈希和发件人哈希；
- 原子记录 `resolution.source=email`。

### 人工聊天选择

`ResolveDecisionManual` 只在负责人当前对话明确选择单一选项时使用：

- 要求 `-ManualOverride`；
- 要求决策编号、选项键、当前 thread ID；
- turn ID 存在时一并纳入证据，不存在时不得伪造；
- 原子记录 `resolution.source=manual`；
- 不把聊天文本改写为邮件格式，不调用邮件回复入口。

### 无效输入

空文本、模糊选择、错误编号、多个选项、未知发件人或缺少消息 ID 时只返回验证错误。它们不得：

- 改变 `pendingDecision` 状态；
- 增加发送尝试次数；
- 清空或覆盖已有提供方回执；
- 写入伪造的回复来源。

## 失败关闭与恢复

### 失败分类

控制器 `Fail` 在持有任务身份时先调用 workspace guard 比较原始 baseline：

1. **无项目变化**：调用 `AbortClean`，释放租约，清空任务检查点和恢复字段，保留 `pendingDecision/decisionFlow`，回到 `IDLE`。
2. **只有 expectedPaths 内变化**：生成专用 interruption evidence，原子记录 baseline、实际变化路径、指纹和哈希，再进入 `RECOVERABLE`。
3. **路径外变化或基线漂移**：按现有安全策略阻塞并报告冲突，不扩大 expectedPaths 隐藏人工变化。

普通错误不得在没有 interruption evidence 时返回 `preserve_recovery`。

### 启动恢复

`Start` 只在状态明确为 `RECOVERABLE` 时进入恢复分支，并要求完整 baseline 与 evidence。非空 `taskId`、`expectedPaths` 或 `decisionFlow` 本身不再等同于可恢复工作。

必须持续满足以下不变量：

- `IDLE` 不持有运行租约或恢复指针；
- `RUNNING` 只表示当前有效租约；
- `RECOVERABLE` 必须同时拥有 baseline、evidence、hash、taskId 和 expectedPaths；
- 不允许过期 `RUNNING` 在缺少 evidence 时被反复恢复；
- 两次真实恢复失败后进入 `AUTO-BLOCKED` 的上限保持不变。

## 现场 v5→v6 迁移

实现通过全部隔离测试后，使用操作员专用修复脚本执行，脚本支持 `-DryRun` 和 `-Apply -ManualOverride`，自动化模型不得调用。

现场步骤：

1. 暂停 `tzg-hourly-controller`，确认没有活动子进程或写入租约。
2. 备份本机状态、运行 session、automation memory，并记录项目 HEAD 与完整工作区基线。
3. 在状态副本上迁移 schema v5→v6，运行事故复现测试。
4. 验证 TQ-057 预期路径没有业务变化后，把当前 `mutation_started` 残留分类为 clean abort。
5. 将 `DEC-20260715-35ACB87E6C10` 的 B 选择移入 `decisionFlow.resolvedDecisions`。
6. 把错误来源从 `email` 更正为 `manual`，同时追加审计更正：旧值、新值、取证对话 ID、时间和原因；不得静默覆盖。
7. 清除过期 runId、租约、错误恢复指针和 `recoveryCount`，状态回到 `IDLE`。
8. 通过修复后的正常控制器恢复 TQ-057，创建“扶摇/演卦双倍率数据表示”的第二项决策。
9. 通过私有配置实际目标发送通知并验证 Sent 目标，路径限定提交更新后的项目状态。
10. 恢复每小时控制器。

迁移完成后的验收状态：

- 控制器为 `IDLE`；
- `recoveryCount=0`，无租约和恢复残留；
- 第一项 B 选择保存在 `decisionFlow` 且来源为 `manual`；
- 第二项问题是唯一 `pendingDecision`；
- 项目状态显示第二项问题和第一项选择摘要；
- TQ-057 业务数据没有被修改。

## 组件与文件边界

- `tools/automation-controller-state.ps1`：schema v6、决策链、来源、发送尝试和状态转换。
- `tools/automation-controller.ps1`：新通知/回复入口、链式决策守卫、失败分类和恢复路由。
- `tools/automation-decision-status.ps1`：三种项目可见发布模式。
- `tools/automation-workspace-guard.ps1`：中断变化分类和 interruption evidence。
- `tools/automation-controller-repair.ps1`：操作员专用 dry-run/apply 迁移与审计更正。
- `开发管理/自动工作流规则.txt`：v6 唯一规则源。
- `开发管理/自动工作流控制器提示词.txt`：薄提示词动作契约和来源边界。
- `tools/test-automation-controller-state.ps1`、`tools/test-automation-controller.ps1`、`tools/test-automation-decision-status.ps1`、`tools/test-automation-workspace-guard.ps1`：对应回归。

不拆分或重构与本事故无关的控制器组件。

## 测试设计

先写失败测试并确认失败原因，再实施最小代码：

1. 单一决策解析后从 `pendingDecision` 移入 `decisionFlow`。
2. 同一任务可以创建第二项决策，其他任务不能覆盖活动决策链。
3. 第二项决策解决并实施成功后，决策链按提交边界清理。
4. `to: "me"` 或目标哈希不匹配不能标记提供方接受。
5. Sent 目标匹配时追加 `PROVIDER_ACCEPTED` 尝试记录。
6. 三次重试上限与 `MISADDRESSED/RETRY_EXHAUSTED` 状态正确。
7. 聊天选择记录 `manual`，邮件回复记录 `email`，两者证据字段不混用。
8. 空回复和错误回复不改变状态、次数或原回执。
9. `mutation_started` 但无文件变化时 `Fail` 干净回到 `IDLE`。
10. 预期路径有变化时失败会先生成完整 interruption evidence，再进入 `RECOVERABLE`。
11. 缺少 evidence 的旧残留不会继续伪装成可恢复任务。
12. schema v5 现场 fixture 可 dry-run、迁移、纠正来源并保持业务文件不变。
13. 以本次对话完整时间线建立端到端回归：错收件人报告、聊天选 B、发现第二问题、创建第二决策、失败和下一轮恢复。

共享控制面改动必须运行 controller、state、decision status、workspace guard、finalizer、workflow checker、review text、暂存前行尾检查和 Git cached diff 检查。Unity 与 BattleSim 未发生相关变化，不运行其回归。

## 验收标准

- 所有新增与既有自动化控制面测试通过。
- 真实通知不会再使用 `"me"` 替代私有配置目标。
- 人工聊天选择不会被记录成邮件来源。
- 同一任务连续决策不会触发 `A pending decision already exists`。
- 无变化失败不会留下不可恢复状态；有变化失败必有证据。
- 现场迁移后达到本文定义的干净状态，并成功投递第二项决策。
- 项目只提交预期路径，不包含用户现有未跟踪或路径外修改。

## 自审

- 文档没有占位符或未决接口。
- 状态机、项目状态、通知、回复来源、恢复与迁移边界一致。
- 修复保持一个活动未解决决策，同时允许同一任务连续决策。
- 现场更正使用追加审计，不静默改写历史。
- 设计不替负责人决定双倍率业务口径，不扩展到无关重构。
