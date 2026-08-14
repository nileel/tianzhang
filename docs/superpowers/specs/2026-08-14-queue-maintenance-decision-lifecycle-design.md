# QueueMaintenance 两阶段决策生命周期设计

日期：2026-08-14
状态：负责人已确认设计方向，已按 DeepSeek 复审修订，待书面复核
范围：Codex QueueMaintenance、任务状态投影、飞书决策桥与空队列自暂停边界

## 1. 背景与根因

`U-URP-VISUAL-BASELINE-01` 在前置 `U-URP-MIGRATE-01` 完成后，由 QueueMaintenance 移除了最后一个具名前置。维护轮次随后确认唯一剩余条件是负责人在 Universal Renderer 与 2D Renderer 替代表现之间作出选择，但任务仍被记录为普通 `blocked`。

现有共享入口对 QueueMaintenance 的所有成功结果固定跳过普通 TaskOutcome；决策卡只覆盖开发中带 checkpoint 的 `needs_decision` 与复审返工。因此，这类“解除最后一个前置后才暴露的负责人选择”虽然进入了任务事实，却没有进入飞书决策生命周期。

问题不是飞书提供方投递失败，而是 QueueMaintenance 缺少独立、可回复、可在后续轮次消费的维护型决策合同。

## 2. 目标与非目标

### 2.1 目标

- QueueMaintenance 在本轮移除最后一个具名前置后，如果负责人选择是使任务可运行的最后条件，必须发送一次飞书决策卡并把任务转为 `pending_decision`。
- 首次发现决策的 run 只建立决策、发送卡片并结束；不得在同一 run 中等待、轮询或消费回复。
- 后续 Codex RunOnce 只在 ready 队列为空时进入 QueueMaintenance 前置检查，每轮只读取一次回复快照；合法回复存在时，再创建新的 QueueMaintenance run，并按选项冻结的目标状态完成任务投影。
- 未回复期间不重复发卡、不修改仓库，也不得命中 Codex 的真实空队列自暂停条件。
- 决策与回复必须精确绑定任务、任务卡事实版本、选项和操作者证据，重复消费必须幂等。

### 2.2 非目标

- 不把所有历史 `blockedBy=[]` 的活跃卡重新分类或批量发送决策卡。
- 不改变开发中 checkpoint 决策、复审返工决策、普通 TaskOutcome、日报或周报合同。
- 不在长连接桥、飞书发送／消费管道或 automation prompt 中增加业务判断。
- 不让 QueueMaintenance 执行业务修改；实际业务仍由后续新的 execute run 完成。
- 不新增后台守护、通用重试队列、第二 runtime 或同轮等待机制。

## 3. 已批准决策

采用“两次 QueueMaintenance”状态机：

1. 第一次 QueueMaintenance 发现负责人选择时，只提交 `pending_decision` 状态、发送一次飞书决策卡、关闭并清理本轮 run。
2. 下一次及后续 Codex RunOnce 在 ready 队列为空时，先于普通选择和 claim 对该维护型决策检查一次回复快照。
3. 没有回复时保持 `pending_decision`，不写仓库、不重复通知、不暂停 Codex 小时入口。
4. 有合法 A／B 回复时，创建新的 QueueMaintenance run，完成与选项一致的任务卡准备，把任务改为 `ready` 并加入固定队列；合法 C 回复表示负责人选择保持阻塞，任务改回 `blocked`，不入队。
5. 不在首次发现决策的 run 中持续等待回复。

该方案不采用“只发通知、人工回仓库处理”，因为那会让飞书按钮与任务状态脱节；也不采用“同一 run 等待回复”，因为它会延长 owner run、阻塞后续调度并破坏小时轮次的终态边界。

## 4. 适用条件

维护型决策只在以下条件全部成立时建立：

- 当前 ready 队列为空，且 Codex 合法领取了保留任务 `QUEUE-MAINTENANCE`；
- 本轮核实一个稳定 ID 前置已完成，并从其直接下游任务移除了最后一个 `blockedBy`；
- 完整读取直接下游任务卡后，确认剩余条件是负责人在有限、明确、互斥的路线中作出选择；
- 除该选择外，任务进入 ready 所需的 route、owner、授权边界、验证、停止条件和完整路径准备可以由当前仓库事实与所选授权确定性完成；
- 决策固定提供恰好三个互斥选项 `A`、`B`、`C`：A／B 是能够确定性形成完整 ready 卡的两条获批路线，C 固定表示保持阻塞；
- 维护型决策固定 `allowCustomReply = false`，自定义回复不得进入自动恢复路径。

如果选项结果仍需另一项负责人决定、外部工作面、内容冻结、项目闸门或无法确定的业务裁决，则不得伪装成可恢复 ready 的维护型决策；任务保持准确阻塞状态，并按既有停止规则处理。

同一 QueueMaintenance run 最多建立一个维护型决策。选择对象使用直接受影响任务的既有固定顺序；不得顺带扫描其他原本 `blockedBy=[]` 的长期卡。

## 5. 状态与数据合同

### 5.1 任务卡公开事实

首次决策提交把目标卡更新为：

- `dispatchState = pending_decision`；
- `blockedBy = []`；
- `stateReason` 明确说明等待哪个负责人选择；
- 新增 `automationDecision`，至少包含：
  - `schemaVersion = 1`；
  - `kind = queue_maintenance`；
  - 稳定 `decisionId`；
  - `status = awaiting_reply`；
  - 专业问题、恰好三个按 `A`、`B`、`C` 排序的互斥选项、推荐项与通俗摘要；
  - 每个选项的 `targetState`，其中 A／B 为 `ready`，C 为 `blocked`；
  - `allowCustomReply = false`；
  - 目标 `taskId`；
  - 决策状态提交 SHA 与该提交中的任务卡摘要；
  - 创建时间。

`decisionId` 继续满足现有 `DEC-YYYYMMDD-<大写字母数字>` 合同，并固定为 `DEC-YYYYMMDD-QM<12HEX>`。`12HEX` 取 `SHA-256(taskId + NUL + baseCommit + NUL + preDecisionTaskContextDigest)` 的前 12 位大写十六进制；同一事实输入生成同一 ID，且与 `REV<sha12>` 复审返工空间区分。维护型决策不包含 `checkpointCommit`，不得复用或伪造 `automationCheckpoint`。

`automationDecision` 与 `automationCheckpoint` 在同一卡上严格互斥。维护型卡只能使用 `automationDecision`；checkpoint 卡只能使用 `automationCheckpoint`。

### 5.2 私有决策记录

共享入口在现有私有 state root 中保存与 `decisionId` 绑定的维护型决策记录，用于：

- 生成现有飞书决策请求；
- 记录一次投递的脱敏结果；
- 在后续 QueueMaintenance 中定位同一回复；
- 防止重复发送和重复消费。

记录必须保存桥返回的 `issuedAt`、`expiresAt` 与固定 7 天 TTL，并使用精确状态枚举 `awaiting_reply`、`answered`、`applied`、`expired`、`attention_required`。只有 `awaiting_reply` 参与后续自动检查；其余状态不得重复消费或重复报告。

私有记录不替代任务卡事实，不保存 provider 凭据，不进入 Git。

### 5.3 回复证据

合法回复必须继续验证：

- `decisionId` 与 `taskId` 精确匹配；
- 来源为现有签名飞书决策回复；
- 操作者身份有效；
- 结果为 `OPTION_ACCEPTED`，且选项属于恰好 `A`、`B`、`C` 的白名单；
- evidence hash 合法；
- 当前任务仍为同一 `pending_decision`；
- 当前 `automationDecision`、决策状态提交和任务卡摘要仍与请求绑定事实一致。

回复成功应用后，任务卡中的 `automationDecision.status` 改为 `resolved`，并记录选项键、目标状态、回复来源、evidence hash 与解决时间；不得复制原始私密消息或 provider 数据。A／B 的目标状态为 ready，C 的目标状态为 blocked。

### 5.4 任务投影与检查器合同

`check-task-cards.ps1` 必须新增以下断言：

- `pending_decision`／`waiting_reply` 卡必须恰有 `automationCheckpoint` 或 `automationDecision` 之一；两者同时存在或同时缺失均失败；
- `automationCheckpoint` 继续使用现有 checkpoint 字段和状态合同；
- `automationDecision.kind` 必须为 `queue_maintenance`，未解决时卡必须为 `pending_decision`，状态必须为 `awaiting_reply`，选项必须恰为 A／B／C，且 `allowCustomReply=false`；
- resolved 维护型决策允许保留在 ready 或 blocked 卡中，但必须包含选项、目标状态和完整回复证据，且目标状态与 `dispatchState` 一致；
- expired 或 attention_required 的维护型决策只允许保留在 blocked 卡中，必须包含稳定原因和终止时间，并且不得再被自动回复检查选择；
- `Find-AnsweredCheckpoint` 继续只选择含 `automationCheckpoint` 的卡，并通过负例证明维护型卡被跳过。

`set-task-automation-state.ps1` 不得复用要求 `automationCheckpoint` 的现有 `PauseDecision`／`ResumeReady`。新增独立动作：

- `PauseMaintenanceDecision`：从本轮直接受影响的 blocked 卡转为 pending_decision，并写入 `automationDecision`；
- `ResolveMaintenanceDecision`：按 A／B／C 冻结的目标状态转为 ready 或 blocked，并写入 resolved 证据；
- `ExpireMaintenanceDecision`：在卡片事实仍精确匹配时转回 blocked，标记 expired，并写明需要人工重新发起。

`check-task-cards.ps1` 同步增加 `MaintenancePendingDecision`、`MaintenanceResolvedReady`、`MaintenanceResolvedBlocked` 和 `MaintenanceExpiredBlocked` 四个精确 postcondition；维护型状态提交不得使用 checkpoint 的 `CodexClosedOrNonReady`／`ResumeReady` 作为唯一证明。

## 6. 第一次 QueueMaintenance：建立决策

数据流固定为：

1. 按现有空队列维护规则核实命名前置并移除最后一个 `blockedBy`。
2. 读取目标任务完整正文，确认命中第 4 节全部条件。
3. 在 QueueMaintenance candidate 中形成唯一维护型决策状态提交，并通过 `PauseMaintenanceDecision` 投影；该提交只修改允许的管理路径，不包含业务变化。
4. 共享入口在最新 `master` 上重放、验证任务卡投影，并在同一集成锁内 fast-forward。
5. runtime 完成并关闭本轮 run。
6. 集成后由共享入口新增的 `Send-MaintenanceDecision` 适配器构造现有请求格式，并调用既有 `send-decision.mjs` 一次；不得调用强制 checkpoint 字段的 `Send-DecisionCheckpoint`。
7. 返回结构化终态 `decision_requested`：`owner=codex`、`taskId=QUEUE-MAINTENANCE`、`decisionTaskId=<目标任务>`、decisionId、runId、formalHead、`detailCode=maintenance_decision_requested`、notification 和 cleanup。

本轮禁止：

- 调用回复消费器；
- 等待飞书回复；
- 在循环中查询回复；
- 把任务直接改为 ready；
- 发送普通 TaskOutcome；
- 因 ready 队列仍为空而自暂停 Codex automation。

步骤 5—6 明确复用现有“先集成并 CompleteRun，后发送”的顺序，但不复用 checkpoint request body。决策卡投递失败不回滚已经集成的 `pending_decision`，也不自动重发。私有记录必须转为 `attention_required`，终态必须明确为需要人工处置的投递失败，后续维护不得把未成功投递误判为正常等待回复。

## 7. 后续 QueueMaintenance：单次检查回复

回复检查固定在 `invoke-hourly-owner.ps1` 的 RunOnce 前置层：本 owner 没有活动 run、模型身份通过且 `check-task-cards.ps1 -OutputJson` 证明 `readyCount=0` 后，在普通任务选择和 claim 之前调用新的 `Find-AnsweredMaintenanceDecision`。它与 `Find-AnsweredReviewRework`、`Find-AnsweredCheckpoint` 同层，不进入业务 candidate，也不让选择器或模型读取飞书私有状态。

共享入口按创建时间、taskId 的稳定顺序选择一个 `automationDecision.status=awaiting_reply` 的维护型决策，每次 RunOnce 只调用一次现有 `consume-reply.mjs`。ready 队列非空时完全跳过该前置检查，保持既有业务顺序。

### 7.1 尚未回复

- 不 claim QueueMaintenance，不创建 owner run 或 worktree；
- 不启动 candidate；
- 不修改任务卡、backlog 或队列；
- 不重复发送决策卡；
- 返回 `waiting_decision`：`owner=codex`、`taskId=QUEUE-MAINTENANCE`、decisionTaskId、decisionId、`detailCode=maintenance_decision_no_reply`、`cleanup=none`；
- `waiting_decision` 不得满足 Codex 自暂停的五字段条件。

下一小时可再次执行相同的一次性前置检查；每轮都只读取一次并立即终止，不持有 owner run。

### 7.2 已有合法回复

1. 共享入口验证第 5.3 节全部回复证据。
2. 把签名回复写为一次性 accepted context，并强制本轮选择保留任务 `QUEUE-MAINTENANCE`；随后才原子 claim 新 run、创建新 worktree，不恢复首次决策 run。
3. 将只读回复上下文传给 QueueMaintenance candidate；candidate 不再调用回复消费器。
4. A／B：candidate 根据所选授权完成任务卡剩余准备，包括精确 expectedPaths、验证、完成条件和停止条件；不得修改业务文件。
5. A／B：通过 `ResolveMaintenanceDecision` 同步任务卡、来源 backlog 和固定 ready 队列，把 `dispatchState` 改为 `ready`。C：通过同一动作把任务改为 `blocked`，记录负责人选择保持阻塞，不入队。
6. 在最新 `master`、同一集成锁和现有投影检查下形成并集成维护提交。
7. 标记私有决策记录为 `applied`，复用既有 `maintenance_completed` status，不创建第二个同名语义；除既有字段外增加 decisionTaskId、decisionId 和 `resolutionState=<ready|blocked>`，关闭并清理本轮 run。

A／B 回复只负责解除决策 blocker。实际业务由后续新建的 `codex_execute` 或原授权 route run 领取，不在本轮顺带执行。C 明确终止自动恢复，后续只有新的负责人状态事件才能重新进入决策或 ready。

### 7.3 TTL 过期、回复无效或事实已变化

- 现有桥的 `expiresAt` 固定为发卡后 7 天。`now > expiresAt` 且仍无合法回复时，决策不得继续返回 `waiting_decision`。
- 如果当前卡片摘要仍与决策绑定事实一致，前置层通过独立状态 worktree、同一集成锁和 `ExpireMaintenanceDecision` 把任务转回 `blocked`，把公开决策标为 `expired`，私有记录标为 `expired`，并返回一次 `attention_required/detailCode=maintenance_decision_expired`。
- 回复结构无效、选项不在 A／B／C、操作者无效或 evidence hash 无效，且当前卡片仍精确匹配时，使用等价状态提交把任务转回 `blocked`，公开与私有记录标为 `attention_required`，只报告一次。
- 如果任务卡摘要或生命周期已经变化，禁止自动覆盖公开事实；只把私有记录标为 `attention_required` 并返回一次 `maintenance_decision_task_context_changed`，后续轮次跳过该非 awaiting 记录，交由人工依据新事实处置。
- 任何 attention／expired 记录均不再自动检查、不重复报告、不重新发送旧卡；不得自动改写选项或创建兼容回复路径。

## 8. 选择、自暂停与并发边界

- QueueMaintenance 仍只属于 Codex；DeepSeek 不读取、发送或消费维护型决策。
- ready 队列非空时按既有固定队列正常选题，RunOnce 前置层跳过维护型回复检查，不抢占 ready 业务任务。
- ready 队列为空时，RunOnce 前置层先检查维护型决策：无回复直接返回 waiting_decision；有合法回复才强制后续选择／claim QueueMaintenance；不存在 awaiting 决策时才进入普通选择器。
- `decision_requested`、`waiting_decision` 和维护型 `attention_required` 均不得触发空队列自暂停。
- 只有不存在 ready 任务、不存在待处理维护型决策、没有活动 owner run，且 QueueMaintenance 返回既有精确 `no_candidate/no_runnable_candidate/cleaned` 终态时，Codex 才能自暂停。
- 每次状态写入继续使用 schema 5 owner run、owner worktree、最新 `master` 重放、同一进程持有型集成锁和精确清理合同。
- 姊妹设计 `docs/superpowers/specs/2026-08-14-codex-hourly-self-pause-decoupling-design.md` 第 4.1 节的主 `functions.exec` 必须把 `decision_requested`、`waiting_decision`、带决策字段的 `maintenance_completed` 和维护型 `attention_required` 当作可解析的共享入口 JSON 原样透传；它们的 `shouldSelfPause` 必须为 false。只有 JSON 语法／shell 输出解析失败才走解析失败停止，不得用 status allowlist 拒绝这些新终态。

## 9. 幂等与失败边界

- 同一 `decisionId` 只能发送一次；已有私有发送记录或任务卡 `automationDecision` 时不得重建。
- 同一回复只能成功应用一次；任务已为 ready 或决策已 resolved 时返回已消费，不重复提交。
- 飞书无回复是正常等待状态，不是失败，不写 Git。
- 维护型决策固定恰好 A／B／C 三选项并关闭自定义回复；桥返回 custom reply 时一律视为无效证据，不自动应用。
- 提供方未接受首次决策卡时不得进入静默 `waiting_decision`；必须暴露投递失败供人工处置。
- 任务卡摘要、选项、route、owner 或生命周期发生变化时，旧回复失效并停止自动应用。
- `expiresAt` 过期后必须离开自动等待；可安全投影时转 blocked/expired，不可安全投影时进入一次性私有 attention_required。
- runtime、worktree、集成锁或主工作区前置不满足时按既有失败规则停止，不自动重试、不复用旧 run。
- 不因多个 pending decision 批量处理；一次 run 最多消费一个，避免多任务状态耦合。

结构化终态字段冻结为：

- `decision_requested`：owner、`taskId=QUEUE-MAINTENANCE`、decisionTaskId、decisionId、runId、formalHead、detailCode、notification、cleanup；
- `waiting_decision`：owner、`taskId=QUEUE-MAINTENANCE`、decisionTaskId、decisionId、`detailCode=maintenance_decision_no_reply`、`cleanup=none`；
- 回复应用继续使用既有 `maintenance_completed`，增加 decisionTaskId、decisionId、`resolutionState`，其余正式提交、通知 skipped 与清理语义不变；
- 过期／无效／事实变化使用既有 `attention_required`，增加 decisionTaskId、decisionId 和稳定 detailCode，不新增 runtime state。

## 10. 最小实现边界

预计只修改以下现有责任面：

- `开发管理/自动工作流规则.txt`：补充维护型两阶段决策、自暂停和通知合同；
- `开发管理/状态与建议维护规则.txt`：补充直接下游任务从 blocked 到 pending_decision、后续回复收口与 ready 投影规则；
- `tools/invoke-codex-candidate.ps1`：增加维护型决策终态及回复上下文约束；
- `tools/invoke-hourly-owner.ps1`：增加 `Send-MaintenanceDecision`、`Find-AnsweredMaintenanceDecision`、accepted context、首次发卡和新结构化终态；回复读取固定在 RunOnce 前置层；
- `tools/set-task-automation-state.ps1`：增加 Pause／Resolve／Expire 三个维护型状态动作，不复用 checkpoint ResumeReady；
- `tools/check-task-cards.ps1`：冻结 checkpoint／maintenance 字段互斥、维护型字段和四个精确 postcondition；
- 现有选择器：只在前置层确认合法回复后接收强制 QueueMaintenance 上下文；普通 no_candidate 选择不读取飞书状态；
- `codex-hourly-worker` 薄触发器及其测试：接受并透传新终态，保持精确自暂停五字段不变；
- 对应 PowerShell 与 Node 测试。

现有 `send-decision.mjs`、`consume-reply.mjs`、card 与 inbox 管道原样复用，不修改 kind、选项数量或 TTL 合同；维护型区别只存在于共享入口、公开任务卡和私有编排记录。只增加现有桥测试的回归调用，不预计修改桥源代码。

不新增第二发送脚本、第二回复存储、后台轮询器或持久化集成租约。

## 11. 验证矩阵

### 11.1 首轮建立决策

- 只在本轮移除最后一个命名前置的直接下游任务上触发。
- 任务转为 `pending_decision`，backlog 同步，ready 队列仍不含该任务。
- `automationDecision` 字段完整、决策 ID 符合 `DEC-YYYYMMDD-QM<12HEX>`、选项恰为 A／B／C、custom reply 关闭、无 checkpoint 字段。
- checker 拒绝同卡同时存在 automationCheckpoint 与 automationDecision，也拒绝 pending_decision 两者皆无。
- 正式提交集成后恰好发送一次飞书决策请求。
- 终态为 `decision_requested`，不调用回复消费器，不发送 TaskOutcome，不自暂停。

### 11.2 后续无回复

- readyCount=0 时 RunOnce 前置层每轮只调用一次回复读取；readyCount>0 时完全不调用。
- 返回 `waiting_decision`；不 claim runtime、零 Git 变化、零重复通知、零残留 run/worktree。
- 连续多个小时仍不自暂停，也不创建新 decisionId。

### 11.3 后续合法回复

- 精确验证 decisionId、taskId、操作者、A／B／C 选项、evidence hash 和任务卡摘要。
- A／B：新 QueueMaintenance run 按选项完成任务卡准备，任务转 ready 并进入固定位置；C：任务 resolved/blocked 且不入队。
- 决策标记 resolved，重复运行不产生第二提交。
- 本轮不执行 ready 任务的业务内容。

### 11.4 负例

- 错误 decisionId、错误任务、未知选项、无效操作者、错误 evidence hash 均不得转 ready。
- custom reply 不得转 ready。
- TTL 到期后不再返回 waiting_decision；精确转 blocked/expired 或一次性 attention_required，后续不重复检查与报告。
- 任务卡事实变化、决策已解决、首次投递失败均不得静默消费。
- 原本 `blockedBy=[]` 的其他长期 blocked 卡不得被顺带发卡。
- ready 队列存在任务时仍按既有顺序执行，不被维护型回复抢占。
- 没有 pending decision 的真实空队列仍返回既有 no_candidate 并允许自暂停。

### 11.5 现有回归

- checkpoint 决策与回复恢复测试保持通过；
- 复审返工决策测试保持通过；
- 普通 TaskOutcome、日报、周报和通知审计测试保持通过；
- schema 5 runtime、双 owner 并行、集成锁、清理和路径限制测试保持通过；
- `codex-hourly-worker` 主 JSON envelope 能解析并透传 decision_requested、waiting_decision、维护型 maintenance_completed 与 attention_required，且四者都不进入 pause cell；
- automation canary 使用私有 state root，不发送真实业务卡。

## 12. 部署与当前事件收口

部署顺序固定为：规则与数据合同、失败测试、实现、完整相关测试、Codex 私有 canary、实时配置核验。

当前 `U-URP-VISUAL-BASELINE-01` 已在旧规则下被改为 `blockedBy=[]/blocked`，不再满足“本轮移除最后一个前置”的新触发条件。不得通过扩大 QueueMaintenance 扫描范围来补发历史卡。新合同上线并通过 canary 后，应使用一次独立、精确绑定该任务的受控状态迁移，把它转为维护型 `pending_decision` 并发送唯一决策卡；该迁移不得顺带处理其他历史 blocked 卡。

该卡的三项冻结为：A 允许改用 Universal Renderer 并按批准的 3D Mesh、标准 3D 灯光与阴影基线准备 ready 卡；B 保持 Renderer2D，并按仅使用其可渲染替代表现的路线准备 ready 卡；C 暂不批准任一路线，保持 blocked。推荐项必须由生成决策时的当前技术事实支持，不在本设计中预先替负责人选择。

实时发送前必须重新核对任务卡、当前队列、automation 配置、schema 5 runs、集成锁和相关路径冲突。投递结果只按 provider 接受证据记录；失败不自动重试。

## 13. 完成标准

- QueueMaintenance 首次发现负责人选择时能提交 pending_decision 并发送唯一飞书决策卡。
- 首次 run 不等待或消费回复；后续每次符合条件的 Codex RunOnce 在 claim 前只检查一次，合法回复进入的新 QueueMaintenance run 不再次读取飞书。
- 无回复且未过 7 天 TTL 时保持小时检查且不自暂停；TTL 过期后退出自动等待并只报告一次。
- 合法 A／B 回复时在新 QueueMaintenance run 中把任务确定性恢复为 ready；合法 C 回复时精确保持 blocked。
- 无重复发送、重复消费、跨任务应用、事实过期应用或业务越界。
- 当前 Renderer 路线决策通过独立受控迁移进入同一新生命周期，不靠全局历史扫描补发。
