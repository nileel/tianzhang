# QueueMaintenance 重新 ready 的 schema 2 过渡门设计

日期：2026-09-01

状态：负责人已批准“尽量不复杂化流程”的最小设计方向；本文等待负责人对书面规格复核。

## 1. 问题与已证实根因

`47842f23ddcb88f15f379be402c7d9069fd19257` 的 QueueMaintenance 提交把 `M-EXP-READY-SCHEMA2-ACTIVATE-01` 从 `blocked` 恢复为 `ready`，但保留了 `schemaVersion=1` 且没有生成 `riskPreflight`。随后 DeepSeek run `33e3e59e-d132-4c64-b0e1-3b39aa9c63e9` 在共享写前预检中以 `experience_preflight_schema_invalid` 失败并停在 `attention_required`。

根因不是匹配器或 schema 2 投影计算错误，而是 QueueMaintenance 的两层完成检查都只调用了普通全局 `check-task-cards.ps1`。激活前合同有意允许“本来已经是 ready”的 schema 1 卡继续存在，因此普通全局检查无法区分：

- 合法的、输入前后都保持 ready 的既有 schema 1 卡；
- 非法的、本轮由非 ready 新转成 ready 却仍为 schema 1 的卡。

QueueMaintenance 提示词也没有明确要求在重新 ready 的同一提交中调用现有预检入口并写入 schema 2 投影。于是模型生成了结构上仍被激活前兼容规则接受、但不能进入小时责任方写前预检的结果。

## 2. 目标与非目标

### 目标

- QueueMaintenance 新建 ready 卡或把活动卡从非 ready 恢复为 ready 时，必须在同一提交中写成 schema 2，并写入由现有 `tools/get-experience-risk-preflight.ps1` 计算的实时 `riskPreflight`。
- candidate 结果和最新 `master` 上的 canonical 重放都必须机械验证该过渡；任何失败均整轮失败关闭，不留下半完成的 ready 状态。
- 保持激活前对“输入前后一直是 ready”的既有 schema 1 卡的兼容，不提前实施全局 schema 2 激活。
- 用现有依赖与 QueueMaintenance 流程恢复本次事故，不新增临时状态或第二套修复入口。

### 非目标

- 不修改风险索引、经验卡或预检匹配语义。
- 不新增脚本、runtime 字段、feature flag、重试、兼容分支或长期迁移模式。
- 不把本卡扩大为全局所有 ready 状态入口的重构；本轮只覆盖 QueueMaintenance 的普通解阻塞和维护型决策恢复。
- 不在同一卡实施 `M-EXP-READY-SCHEMA2-ACTIVATE-01` 的全量 schema 2 激活迁移。

## 3. 选定方案

建立 P1 Codex 修复卡 `M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01`，只修改现有 QueueMaintenance 路线的提示和过渡检查。

### 3.1 单一检查所有者

扩展现有 `tools/check-task-cards.ps1`，增加一个 QueueMaintenance 专用 postcondition，并接收明确的完整 base commit。base 必须能解析为当前仓库提交且是当前 HEAD 的祖先，否则直接失败。检查器读取 base 中的活动任务卡与当前工作树中的活动任务卡，只选择当前为 `ready` 且满足以下任一条件的卡：

1. base 中不存在该活动卡；
2. base 中该卡的 `dispatchState` 不是 `ready`。

对每张命中卡要求：

- `schemaVersion=2`；
- 存在且只存在 `riskPreflight.explicitRefs/matched/gates` 三个数组；
- 投影内容继续由现有全局 schema 2 ready 校验路径重算并验证，不复制匹配器逻辑。

输入前后都为 ready 的既有 schema 1 卡不属于本 postcondition 的命中集合，继续由激活前兼容合同处理。检查器不负责写卡或自动修补。

### 3.2 QueueMaintenance 写入责任

在 `tools/invoke-codex-candidate.ps1` 的现有 QueueMaintenance 提示中增加一条明确合同：建立或重新 ready 前，调用现有 `get-experience-risk-preflight.ps1`，在同一提交写入 schema 2 和实时投影；预检失败、过宽或缺少门禁指针时不得置为 ready。

不新增自动改写器。QueueMaintenance 仍由当前责任方完成任务卡、backlog 和队列的同一原子修改；新增 postcondition 只负责拒绝不完整结果。

### 3.3 两个现有验证点复用同一 postcondition

- Candidate wrapper：以 owner run 的 `baseCommit` 对 candidate HEAD 执行专用 postcondition；失败沿用 `codex_candidate_postcondition_failed`。
- Canonical 重放：以最新 `master` 重放前的 base 对 formal HEAD 执行同一 postcondition；失败沿用 `hourly_postcondition_failed`。
- 正式 fast-forward 后继续由共享入口现有的 postcondition 复查调用同一检查逻辑和同一 canonical base；不增加另一套判断或新 detail code。

canonical 阶段必须重新比较最新 base，不能复用 candidate 阶段的命中列表；这样主分支在两阶段之间发生无冲突变化时仍以正式提交事实为准。

## 4. 数据流与失败关闭

1. QueueMaintenance 识别一个具名前置已经完成，并完整读取直接下游任务卡。
2. 若该卡应进入 ready，先运行现有风险预检，写入 schema 2 与投影，再同步 backlog 和队列。
3. Candidate wrapper 对比 run base 与 candidate HEAD，只检查本轮新进入 ready 的卡；随后运行现有全局投影检查。
4. 共享入口在最新 `master` 重放 candidate，并以 canonical base 与 formal HEAD 重做相同检查。
5. 任一预检、结构或投影不一致时，现有候选／canonical 流程停止，不 fast-forward，不写 runtime 新状态，不保留部分 ready 投影。

维护型决策的 A／B 回复仍由共享入口先准备卡、再应用现有 `ResolveMaintenanceDecision`。如果该操作把任务恢复为 ready，canonical postcondition 必须以应用前 base 识别该过渡并执行同样的 schema 2 要求。

## 5. 本次事故恢复

书面规格复核后，建立修复卡时使用既有任务依赖完成一次性恢复：

- 按 `开发管理/自动工作流恢复规则.txt` 核对当前 DeepSeek run 的 base、任务 digest、worktree、提交和 runtime 记录；只有确认没有 candidate／canonical、worktree 无业务差异且满足精确 `CompleteRun` 合同时，才以失败结果关闭并保留 `experience_preflight_schema_invalid` 证据。任一证据不满足就保留 run 和 worktree；不恢复旧模型会话。
- 将 `M-EXP-READY-SCHEMA2-ACTIVATE-01` 恢复为 blocked，并把直接 blocker 设为 `M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01`；从 ready 队列移除。
- 将修复卡作为 P1 `codex_execute/codex/ready` 放入队列。
- 修复卡完成归档后，由正常 QueueMaintenance 移除该 blocker。新过渡门会迫使同一维护提交为激活卡生成 schema 2 实时投影后才能重新入队。

这条恢复路径复用现有任务依赖、队列和 QueueMaintenance，不在实现中加入针对当前任务 ID、日期或 runId 的特例。

## 6. 修改边界

修复卡的实现上界冻结为：

- `tools/check-task-cards.ps1`
- `tools/test-check-task-cards.ps1`
- `tools/invoke-codex-candidate.ps1`
- `tools/test-invoke-codex-candidate.ps1`
- `tools/invoke-hourly-owner.ps1`
- `tools/test-queue-maintenance-completion.ps1`
- `tools/check-automation-workflow.ps1`
- `开发管理/任务列表/管理与自动化任务.txt`
- `开发管理/当前任务队列.txt`
- `开发管理/任务卡/M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01.txt`
- `开发管理/任务归档/M-QUEUE-MAINT-READY-SCHEMA2-GUARD-01.txt`
- `开发管理/任务卡/M-EXP-READY-SCHEMA2-ACTIVATE-01.txt`

若实现证明必须新增脚本、runtime schema、自动修补器、全局 ready 入口重构或第二个迁移模式，立即停止并重新判断根因，不继续叠加补丁。

## 7. 验证

在现有测试文件中增加以下确定性回归：

1. base 为 blocked schema 1、当前为 ready schema 1：拒绝；
2. base 为 blocked schema 1、当前为 ready schema 2 且投影实时一致：通过；
3. base 不存在、当前新建 ready schema 1：拒绝；
4. base 与当前都为 ready schema 1：激活前继续通过；
5. 当前为 schema 2 ready 但 matched／gates 投影失配：由现有全局校验拒绝；
6. canonical 重放对最新 base 重新判断过渡，不复用 candidate 阶段结果；
7. QueueMaintenance 提示必须包含“新建／重新 ready、现有预检、schema 2、同提交投影、失败不得 ready”的完整合同。

执行现有最小充分验证：

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-candidate.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-queue-maintenance-completion.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,tools`
- 预期路径 whitespace、`git diff --check` 和暂存后 `git diff --cached --check`

## 8. 完成条件

- QueueMaintenance 每次新建或重新 ready 的任务卡均在同一提交成为 schema 2，并携带实时一致的 `riskPreflight`。
- Candidate 与 canonical 两层都使用同一过渡检查，任一层不能让 schema 1 新 ready 结果进入 master。
- 既有未变化的 schema 1 ready 卡在激活提交前仍合法；本修复不改变全局激活时点。
- 本次失败 run 已按现有 runtime 合同结束，激活卡通过修复卡 blocker 回到正常 QueueMaintenance 解阻塞路径，不保留特例或半状态。
- `A-CHAR-PORTRAIT-STYLE-01` 的输入冻结前置不属于本设计；只有本修复闭环后才按已确认顺序单独建立。
