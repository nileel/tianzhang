# 天章每小时自动工作流精简 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把当前无法稳定执行的中央编排器替换为一个每小时薄路由、一个最小租约/恢复工具和现有安全组件，使 Codex、Codex 复审、Claude/DeepSeek 与队列维护各自端到端完成一个工作，并在连续两轮全部阻塞时自动暂停。

**Architecture:** 自动化提示负责只读汇总候选、统一排序、取得租约并进入既有纯 `1`、纯 `2`、外部 AI 或队列维护入口；当前责任方自己修改、验证、更新状态和提交。`tools/hourly-automation-lease.ps1` 只管理租约、恢复指针、待续跑队列和阻塞计数；现有 workspace guard、finalizer、whitespace 与飞书签名消息协议保持独立。建设在 linked worktree 完成，生产自动化保持 `PAUSED`，先迁移 TQ-057 业务事实，后切换，最后在一次真实任务和一个自然整点轮次成功后退役旧编排。

**Tech Stack:** PowerShell 7、Git、Codex CLI `exec resume`、Claude CLI `--session-id/--resume`、Node.js `>=20`、现有飞书长连接桥、Codex 自动化管理能力。

---

## Authority and Supersession

- 权威设计：`docs/superpowers/specs/2026-07-18-hourly-automation-simplification-design.md`。
- 本计划完整替代 `docs/superpowers/plans/2026-07-17-hourly-controller-orchestration-rebuild-implementation.md` 的未完成任务；不得继续执行旧计划的 Task 11/12，也不得恢复 manifest、发现网关、机器任务注册表或十五 Action 状态机。
- 实际执行使用用户指定的 `superpowers:executing-plans`，从 Task 0 开始顺序执行；不并行派发、不使用 subagent-driven 路径。
- 实施分支固定为 `codex/hourly-automation-simplification`，建设 worktree 固定为 `D:\天章游戏开发-worktrees\hourly-controller-v2`。
- 生产工作区固定为 `D:\天章游戏开发`；建设期不得在该工作区修改、暂存、提交、stash、reset、checkout 或 clean 用户改动。
- 当前已确认的建设基线是提交 `23f88c1854f0e416a0f18237a87730380df030ab`，五个现存自动化均为 `PAUSED`，其中生产入口为 `tzg-hourly-controller`。

## Non-negotiable Invariants

- Task 0–7 期间 `tzg-hourly-controller` 必须保持 `PAUSED`；Task 8 只更新现有自动化并继续保持 `PAUSED`；Task 9 才允许手动运行；Task 10 才允许恢复定时。
- 任一时刻最多一个项目写入者。候选汇总只读；取得租约后才能启动 Codex、复审、外部 AI 或队列维护写入。
- 所有项目文件创建、修改和删除都使用 `apply_patch`；格式化工具只用于机械格式化，不用 PowerShell/Python 写文件替代补丁。
- 调度器不实施、不做业务验证、不 stage、不 commit、不复审；它只选择、取得/释放租约、启动责任方并记录结果类别。
- 当前责任方端到端完成任务。外部 AI 的业务检查和提交不由外层 Codex重跑或代做。
- 所有独立 PowerShell 进程只使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File`。
- 不编辑 `%USERPROFILE%\.codex\automations\**\automation.toml`；生产自动化的 prompt、schedule、workdir 和 status 只通过 Codex 自动化管理能力更新。
- 不删除任何旧私有状态或决定备份。至少在 TQ-057 完成且验证前，`tzg-hourly-controller.json`、`tzg-hourly-controller-v2.state.json` 及其时间戳备份全部保留。
- 活动提示、规则、租约工具和静态检查器不得出现具体 `TQ-*`、`HANDOFF-*` 或复审编号。TQ-057 只存在于正式业务任务卡、历史规格/计划和退役前旧实现中。
- 若主工作区人工改动与本分支的自动化目标文件重叠，合并立即停止；不得自动覆盖或替用户解决。
- 任一验证失败只修复受影响切片并重跑该检查；不得用“保险起见”为理由追加全量 Unity、BattleSim、旧控制器或完整飞书回归。

## Necessary Git SHA Clarification

Git 提交无法在自己的文件内容中记录自己的 commit SHA，因为写入 SHA 会改变该提交。外部 AI 的可执行边界因此固定为两个连续、均由同一外部 AI 创建的本地提交：

1. `businessCommit`：业务修改、任务状态改为待复审、直接相关验证结果；不含事后才能知道的 SHA。
2. `handoffCommit`：只修改 `开发管理/AI合作沟通.txt`，记录真实 `businessCommit` SHA、已验证、未验证和残留风险。

外层 Codex不代做任何一个提交，也不重跑业务验证。后续纯 `2` 以 `businessCommit` 为审核目标，并把 `handoffCommit` 作为交接材料。第二次 finalizer 只检查交接文件，不重复领域验证。这是对不可实现细节的最小技术消歧，不改变“一个责任方端到端完成”的设计。

## Validation Budget

| 切片 | 允许的验证 | 禁止重复 |
|------|------------|----------|
| Task 1 | 私有 ledger 五决定一致性断言、任务卡直接文本断言、一次 `check-review-text.ps1` | 不运行数据链、Unity 或旧 v2 套件 |
| Task 2 | 一次红灯、一次 `tools/test-hourly-automation-lease.ps1` 绿灯 | 不运行旧 controller state 测试 |
| Task 3 | 一次红灯、一次 `node --test tools/feishu-decision-bridge/test/resume-trigger.test.mjs` | 不运行完整飞书 Node 套件 |
| Task 4 | 静态规则检查；Task 6 中仅一次真实临时仓库 Claude/DeepSeek 金丝雀 | 不在每次文档修改后调用外部模型 |
| Task 5 | 一次红灯、一次 `tools/test-check-automation-workflow.ps1` 绿灯、一次静态 checker | 不运行旧 v2 模块套件 |
| Task 6 | 汇总运行新边界各一次；输入未变的既有 Git/飞书协议不重跑 | 不运行 Unity、BattleSim、全量项目检查 |
| Task 7–8 | 合并后只跑静态工作流入口检查 | 不重复 Task 2–6 单元检查 |
| Task 9 | 真实任务自己的领域验证由责任方运行一次；观察者只核对提交、租约和人工脏改 | 外层不重跑 TQ-057 领域检查 |
| Task 10 | 只观察一个自然整点结果及状态差异 | 不手动制造第二次等价运行 |
| Task 11 | 退役后运行 `test-check-pwsh-runtime.ps1` 和带退役开关的工作流 checker | 不再运行已删除的旧测试 |

## Stable Runtime Contract

### Project paths

```text
Thin prompt:       开发管理/自动工作流控制器提示词.txt
Short rules:       开发管理/自动工作流规则.txt
Lease/recovery:    tools/hourly-automation-lease.ps1
Lease tests:       tools/test-hourly-automation-lease.ps1
Workflow checker:  tools/check-automation-workflow.ps1
Checker tests:     tools/test-check-automation-workflow.ps1
Resume relay:      tools/feishu-decision-bridge/src/resume-trigger.mjs
Relay tests:       tools/feishu-decision-bridge/test/resume-trigger.test.mjs
External canary:   tools/test-external-ai-self-commit.ps1
```

### Private paths

```text
Runtime root:  %USERPROFILE%/.codex/automation-state/tzg-hourly-controller-runtime/
Runtime state: %USERPROFILE%/.codex/automation-state/tzg-hourly-controller-runtime/runtime.json
Bridge state:  %USERPROFILE%/.codex/automation-state/tzg-feishu-decision-bridge/
```

所有私有目录和文件继续通过 `tools/private-path-acl.ps1` 设置并验证 ACL。运行时状态使用 UTF-8 无 BOM、原子替换，禁止写入 provider token、tenant key、open/chat/message/event ID 原文。

### Lease tool actions

唯一入口：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 `
  -Action Show `
  -StateRoot $runtimeRoot
```

固定 Action 只有八个，不接收 request JSON：

```text
Show Acquire SaveRecovery ClearRecovery QueueResume TakeResume RecordResult Release
```

固定状态只有以下字段，不存在业务 phase、manifest、requiredChecks 或机器任务表：

```json
{
  "schemaVersion": 1,
  "lease": null,
  "recovery": null,
  "pendingResumes": [],
  "blocking": {
    "fingerprint": null,
    "count": 0,
    "pauseRequested": false
  },
  "lastResult": null
}
```

`lease` 只含 `runId/taskId/owner/repositoryRoot/startedAt/expiresAt`。`recovery` 只含 `taskId/owner/repositoryRoot/resumeKind/resumeId/decisionId/decisionRequestPath/hasUncommittedChanges/changedPaths`。`pendingResumes` 只保存 `decisionId/replyPath/queuedAt`。`lastResult` 只保存 `category/taskId/detailCode/recordedAt`。

`RecordResult` 的 category 只允许 `success/refilled/blocked/failed/waiting_decision`。相同 blocking fingerprint 连续两次把 `pauseRequested` 置为 `true`；`success`、`refilled` 或实质变化的 fingerprint 重置计数。租约过期但存在 `hasUncommittedChanges=true` 的恢复指针时，普通 `Acquire` 必须返回 `RECOVERY_ONLY`，不能覆盖原任务。

### Resume relay contract

- 飞书卡片或文本回复先按现有协议写入签名 inbox；成功响应格式完全不变。
- 新 post-accept hook 只把 `decisionId` 和签名 inbox 文件路径交给租约工具的 `QueueResume`。
- 无租约时 `QueueResume` 原子取得原任务租约并返回 `DISPATCH`；有租约时返回 `QUEUED` 后进程结束，不 sleep、不轮询。
- 真正 dispatch 时复用现有 `consume-reply.mjs` 验签并消费；relay 只选择 `optionKey` 或 `customText` 原文，丢弃传输哈希，不解释含义。
- Codex 使用 `codex exec resume SESSION_ID -`；Claude/DeepSeek 使用 `claude --resume SESSION_ID --print`。回复通过 stdin 传入，避免出现在进程命令行。
- bridge 只启动隐藏的 detached relay helper，不等待模型结束；helper 或恢复线程负责最终 `RecordResult/Release`。启动失败时释放本次租约并保留待续跑项供下个整点恢复。

---

## Task 0: 重新确认隔离、暂停与私有证据

**Files:**

- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-07-18-hourly-automation-simplification-design.md`
- Read: `docs/superpowers/plans/2026-07-18-hourly-automation-simplification-implementation.md`
- Read: `开发管理/自动工作流规则.txt`
- Read: `开发管理/自动工作流状态.txt`
- Read: `开发管理/当前任务队列.txt`
- Private copy: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.pre-simplification.TIMESTAMP.json`
- Private copy: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2.pre-simplification.TIMESTAMP.json`

- [ ] **Step 1: 重新读取权威文件，不依赖旧对话摘要**

完整读取上列文件，确认新设计明确替代旧计划。若设计状态、分支或生产自动化已经被其他工作修改，停止并先报告差异。

- [ ] **Step 2: 证明隔离分支干净且主工作区不被接管**

```powershell
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' branch --show-current
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' rev-parse HEAD
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' status --short
git -C 'D:\天章游戏开发' status --short
```

Expected: 分支为 `codex/hourly-automation-simplification`；建设 worktree 干净；主工作区可以脏，但只记录，不修改。

- [ ] **Step 3: 通过自动化管理能力确认全部写入型自动化仍暂停**

读取 `tzg-hourly-controller`、`tzg-wf1-queue-and-review-maintenance`、`tzg-wf3-claude-execute-1`、`tzg-wf4-codex-execute-2`。Expected: 全部 `PAUSED`。任一为 `ACTIVE` 时先用自动化管理能力暂停，再继续；不得直接编辑 TOML。

- [ ] **Step 4: 创建只增不删的私有备份**

在 PowerShell 7 中生成 UTC 时间戳，分别复制 `tzg-hourly-controller.json` 和 `tzg-hourly-controller-v2.state.json`；用 `tools/private-path-acl.ps1` 收紧并验证 ACL，计算 SHA-256，设置备份只读。若源文件不存在，停止，不伪造空备份。

- [ ] **Step 5: 确认没有项目写入**

```powershell
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' status --short
```

Expected: 空输出。本任务没有 Git 提交。

---

## Task 1: 把 TQ-057 五项决定归位到正式任务卡

**Files:**

- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/当前任务队列.txt`
- Read only: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2.state.json`

- [ ] **Step 1: 用私有 ledger 做迁移前一致性断言**

解析 `decisionLedger`，要求以下五个 ID 各恰好一条，且 `resolutionText`、`affectedRoots`、`migrationFacts`、`compatibilityFacts` 均非空：

```text
DEC-20260715-35ACB87E6C10
DEC-20260715-75D7BA2AF210
DEC-20260714-29A5D1356CC8
DEC-20260714-320075D033A5
DEC-20260713-A07FA708DB22
```

Expected: 五条全部通过。不得把旧 ledger 写回 Git，也不得迁移 provider 哈希或消息证据。

- [ ] **Step 2: 写迁移前红灯断言**

运行只读 PowerShell 断言：`数据链路任务.txt` 必须包含上述五个 ID 各一次，并包含 `physicalDamageMultiplier`、`soulDamageMultiplier`、`tools/check-data-chain.ps1` 和“保留 11 份古修术法文档”。

Expected: 非零，因为当前正式任务卡尚未包含完整决定。

- [ ] **Step 3: 扩充正式 TQ-057 任务卡**

在 `开发管理/任务列表/数据链路任务.txt` 增加独立的 `TQ-057 最终批准口径` 小节。每项必须保存：决定 ID、ledger 的完整 `resolutionText`、影响根、迁移事实、兼容事实和 required checks。第二项还必须显式加入 ledger 后经负责人确认的边界：

- `src/Assets/DataConfig/Spells.csv`
- `src/Assets/Scripts/Editor/DataConfigImporter.cs`
- `src/Assets/Scripts/Combat/SpellData.cs`
- `src/Assets/Scripts/Combat/CombatResolver.cs`
- `src/Assets/Tests/EditMode`
- 全部现存 `src/Assets/Data/Spells/*.asset`
- 任何硬编码术法 schema 的 `tools/check-data-chain.ps1`

五项口径必须分别表达：保留 11 份文档并补齐数据链；双伤害倍率迁移；补 `realm_lianshen` 语言键并保留有效引用；六部功法只删 `realm_lianxu` 段；删除无效境界引用但保留有效数据。

- [ ] **Step 4: 把当前队列收缩为短入口**

`开发管理/当前任务队列.txt` 的 TQ-057 卡只保留 ID、P0、Codex 主责、待处理、依赖、总体范围、完成条件、验证命令和指向正式数据链任务卡的链接；不得复制五项决定正文。

- [ ] **Step 5: 运行迁移后直接验证**

重新运行 Step 2 断言，并运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths '开发管理/任务列表/数据链路任务.txt,开发管理/当前任务队列.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/任务列表/数据链路任务.txt|开发管理/当前任务队列.txt'
```

Expected: 五决定各一次、短队列无 `DEC-` 正文、两个脚本均为 0。

- [ ] **Step 6: 创建仅含业务事实迁移的提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2' `
  -ExpectedPaths '开发管理/任务列表/数据链路任务.txt|开发管理/当前任务队列.txt' `
  -CommitMessage 'docs(automation): restore TQ-057 decisions to task card'
```

Expected: 输出新 commit SHA；提交只含两份任务文件。

---

## Task 2: 用 TDD 建立最小租约与恢复工具

**Files:**

- Create: `tools/hourly-automation-lease.ps1`
- Create: `tools/test-hourly-automation-lease.ps1`

- [ ] **Step 1: 先写失败测试**

测试在独立临时目录加载工具，并覆盖：

1. 首次 `Acquire` 返回 `ACQUIRED` 和新 `runId`。
2. 未过期租约拒绝第二写入者，状态文件字节不变。
3. 错误 `runId` 不能 `RecordResult`、`SaveRecovery` 或 `Release`。
4. 正确释放后可由下一任务取得。
5. 过期且无恢复修改时可回收；过期且 `hasUncommittedChanges=true` 时只返回 `RECOVERY_ONLY`。
6. `SaveRecovery` 保存 Codex thread 或 Claude session 二选一，拒绝两者同时出现。
7. `QueueResume` 在锁占用时只落盘且进程结束；同一 `replyPath` 重放不重复排队。
8. 无锁时 `QueueResume/TakeResume` 原子取得原任务租约并只 dispatch 一次。
9. 相同阻塞 fingerprint 第一次 count=1，第二次 count=2 且 `pauseRequested=true`。
10. 成功、补任务或不同 fingerprint 重置阻塞计数。
11. 私有状态文件为 UTF-8 无 BOM、原子替换、ACL 合格，不含 forbidden secret fields。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: 非零，原因是 `tools/hourly-automation-lease.ps1` 尚不存在，而不是测试语法错误。

- [ ] **Step 3: 实现最小状态和八个固定 Action**

实现本计划 Stable Runtime Contract。所有写入在命名 mutex 内完成；状态根必须是绝对路径，默认位于用户级 automation-state；`decisionRequestPath` 和 `replyPath` 必须解析在批准的私有根内；`repositoryRoot` 必须是现存 Git root。stdout 只输出一行压缩 JSON，日志写 stderr。

工具不得导入旧 `tools/hourly-controller-v2/`，不得读取项目队列、审核文件或任务卡，不得调用 Git 验证、finalizer、飞书、Codex 或 Claude。

- [ ] **Step 4: 运行直接测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: 最后一行为 `test-hourly-automation-lease: OK`。

- [ ] **Step 5: 提交最小租约工具**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2' `
  -ExpectedPaths 'tools/hourly-automation-lease.ps1|tools/test-hourly-automation-lease.ps1' `
  -CommitMessage 'feat(automation): add minimal lease and recovery state'
```

---

## Task 3: 在不改飞书协议的前提下增加原会话续跑边界

**Files:**

- Create: `tools/feishu-decision-bridge/src/resume-trigger.mjs`
- Create: `tools/feishu-decision-bridge/test/resume-trigger.test.mjs`
- Modify: `tools/feishu-decision-bridge/src/bridge.mjs`
- Modify: `tools/feishu-decision-bridge/src/message-core.mjs`

- [ ] **Step 1: 写 post-accept 边界失败测试**

使用依赖注入的 fake lease process、fake consumer、fake child spawn 和 fake timers 覆盖：

1. 卡片 A/B/C 和自定义回复仍先成功写签名 inbox，原响应对象逐字段不变。
2. 文本自定义回复的飞书确认文本不变。
3. accepted reply 只触发一次隐藏 relay；重复 event 不重复启动。
4. lease 返回 `QUEUED` 时不启动 Codex/Claude，函数立即结束。
5. lease 返回 `DISPATCH` 时只消费对应签名回复，选项传原 `optionKey`，自定义传原 `customText`。
6. Codex 命令固定为 `codex exec resume SESSION_ID -`；Claude 命令固定为 `claude --resume SESSION_ID --print`；原回复只通过 stdin。
7. 不把 provider 哈希、配置 secret 或消息 ID 传给模型或 stdout。
8. child start 失败时释放租约并保留一次待续跑记录。

- [ ] **Step 2: 确认红灯**

```powershell
node --test tools/feishu-decision-bridge/test/resume-trigger.test.mjs
```

Expected: 非零，原因是 `resume-trigger.mjs` 不存在。

- [ ] **Step 3: 实现最小 relay helper**

`resume-trigger.mjs` 提供可注入函数和一个隐藏 CLI 模式：

- bridge accepted 后用 `setImmediate`/等价调度启动 detached helper，不能延长飞书 callback 响应。
- helper 调用 `hourly-automation-lease.ps1 QueueResume`；`QUEUED` 立即退出。
- `DISPATCH` 时调用现有 `consume-reply.mjs` 验签消费；只把原始选项或自定义文本包在固定恢复前缀中，并附带私有 `runId` 供原线程继续。
- 每小时入口和租约 `Release` 返回 ready resume 时，可调用同一个 CLI 的 `--dispatch-ready` 模式；不创建第二套恢复代码。
- helper 不读任务卡、不判断回复、不修改项目、不验证、不提交。

卡片路径直接使用 `bridge.mjs` 已解析的 `normalized.action.decisionId`；文本路径只在 `message-core.mjs` 的内部 accepted result 增加 `decisionId` 供 bridge post-accept hook 使用。不得修改 `callback-core.mjs`。外部响应、签名 envelope、binding 和 inbox schema 不变。

- [ ] **Step 4: 运行新边界测试**

```powershell
node --test tools/feishu-decision-bridge/test/resume-trigger.test.mjs
```

Expected: 全部通过。此处不运行 `npm test`，因为消息协议输入没有改变。

- [ ] **Step 5: 提交决策续跑边界**

使用 `automation-finalize-commit.ps1`，ExpectedPaths 只列本任务实际修改的 2–5 个文件，提交信息固定为：

```text
feat(automation): relay decisions to original sessions
```

---

## Task 4: 把外部 AI 改为自行验证和提交

**Files:**

- Modify: `AGENTS.md`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Create: `tools/test-external-ai-self-commit.ps1`

- [ ] **Step 1: 写规则红灯断言和临时仓库金丝雀脚本骨架**

静态断言必须先证明三份规则仍包含“外部子进程不得 stage/commit、控制器代提交”的旧口径，并以非零结束。

金丝雀脚本只在显式运行时调用一次外部 CLI；它负责在系统临时目录创建 Git repo、复制 workspace guard/finalizer/whitespace、创建一个明确授权给 Claude/DeepSeek 的任务卡和三份 fixture 文件。脚本自身不得访问或修改生产工作区。

- [ ] **Step 2: 修改三份权威规则**

统一改为：

- 外部 AI 只领取明确授权给 DeepSeek V4 Pro、Claude Code 或 Claude/DeepSeek 的待处理非复审任务。
- 调度器必须先选出合法任务并取得租约，再启动外部 CLI；无外部候选时不得预检或空转调用。
- 外部 AI 自己调用 workspace guard、实施、最小充分验证、标记未审核、更新任务状态并创建 `businessCommit`。
- 外部 AI 随后只改 `开发管理/AI合作沟通.txt`，记录业务 SHA，并创建 `handoffCommit`。
- 两个提交都必须使用 `automation-finalize-commit.ps1` 和路径限定提交；第二次不得重跑领域检查。
- 外层 Codex只接收 `completed/needs_decision/blocked/failed`、两个 SHA 或恢复 session；不读业务 diff、不重验、不 stage、不 commit。
- 外部 AI 不得自审、扩大 expected paths、并行派发或推送远端。

保留当前身份判断、主责边界、未审核标记和纯 `1/2` 规则。

- [ ] **Step 3: 完成金丝雀脚本断言**

脚本固定执行两次同一 session：

1. `claude --session-id UUID --print`：外部 AI 必须在修改前返回 `needs_decision` 并退出，临时 repo 仍干净。
2. `claude --resume UUID --print`：通过 stdin 输入负责人原始回复 A；同一 session 修改授权文件、运行 fixture 直接验证、创建 business commit，再创建只含 handoff 的第二提交。

脚本必须断言：恰好新增两个提交；business commit 只含业务/队列 fixture；handoff commit 只含交接 fixture；交接内容含真实 business SHA；repo 干净；未授权文件哈希未变；两个提交作者均为外部 AI。成功输出 `test-external-ai-self-commit: OK`。

- [ ] **Step 4: 只运行静态规则验证，不运行真实外部金丝雀**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'AGENTS.md,开发管理/AI协作规则.txt,开发管理/DeepSeek工作提示词.txt'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'AGENTS.md|开发管理/AI协作规则.txt|开发管理/DeepSeek工作提示词.txt|tools/test-external-ai-self-commit.ps1'
```

Expected: 通过；旧“控制器代提交”口径不存在。真实外部调用保留到 Task 6，仅执行一次。

- [ ] **Step 5: 提交外部责任边界**

使用 finalizer 提交上述四个文件，提交信息：

```text
docs(automation): make external workers own their commits
```

---

## Task 5: 用薄提示和短规则替代活动中央协议

**Files:**

- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流状态.txt`
- Rewrite: `tools/check-automation-workflow.ps1`
- Create: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: 写静态契约失败测试**

测试先要求 checker 支持：

```text
-RepositoryRoot
-AutomationRoot
-RequireActive
-RequireLegacyRetired
```

测试在临时项目和临时 automation root 中覆盖：全部暂停时默认检查通过；只有 `tzg-hourly-controller` 活跃时 `-RequireActive` 通过；第二个写入型自动化活跃时失败；prompt 出现具体任务 ID、manifest、planOnly、SubmitManifest、DiscoverRead、任务注册表或 `hourly-controller-v2` 时失败；缺少统一排序、每轮一个、无候选才补任务、两轮全阻塞暂停、外部自提交和无 token 等待时失败；`-RequireLegacyRetired` 在旧文件存在时失败、删除后通过。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: 非零，因为旧 checker 没有新参数且活动提示仍含旧协议。

- [ ] **Step 3: 重写薄路由提示**

提示只保留以下顺序，不复制业务规则正文：

1. 读取 `自动工作流规则.txt`、当前队列、审核入口/交接和必要状态。
2. 如果 runtime 有 task-owned uncommitted recovery，只恢复原责任方；否则先处理已回复的 pending resume。
3. 汇总 Codex 执行、Codex 复审、外部 AI 三类合法候选。
4. 按 P 优先级、已回复续跑、解锁高优先级下游数量、等待时间、稳定 ID 排序。
5. 三类均无合法候选时才做队列维护；优先提升 backlog，没有才新增 1–3 项；本轮不执行新任务。
6. 取得租约；每轮只启动一个责任方。
7. 路由到纯 `1`、纯 `2`、外部 AI 或队列维护；责任方端到端完成。
8. 决策等待保存 thread/session，释放租约并退出；回复占锁只排队，不等待。
9. 记录最终类别并释放租约；处理 Release 返回的一个 ready resume。
10. 相同全阻塞指纹连续两次时，通过自动化管理能力暂停 `tzg-hourly-controller` 并经飞书通知。

提示不得内嵌具体任务、具体决定、文件 manifest、request JSON、业务检查列表或硬编码模型名称。模型身份继续按 `AGENTS.md` 的 request metadata 规则确认。

- [ ] **Step 4: 重写短稳定规则**

规则只定义：单写入租约、候选资格、统一排序、四种路由责任、人工脏改避让、外部两提交、决策恢复、两轮阻塞暂停、队列补充顺序、私有状态边界和回滚方式。领域规则只引用既有入口，不复制。

- [ ] **Step 5: 更新人读状态**

`自动工作流状态.txt` 删除 v2 phase、AUTHORIZED、plan-only manifest、TQ 专用决定正文和旧租约命令；保留：生产仍暂停、精简设计/实施分支、TQ-057 决定已归位、旧私有备份保留、尚未取得生产成功。不得把离线测试写成生产成果。

- [ ] **Step 6: 实现新的静态 checker**

checker 只验证：

- canonical prompt/rules/status 存在且 UTF-8 有效。
- 活动入口引用新租约工具和既有纯 `1/2`/外部/队列入口。
- prompt/rules 无具体任务编号和旧协议词。
- 保留 workspace guard/finalizer/whitespace/飞书桥文件存在。
- automation root 中最多一个 `ACTIVE` 写入型自动化；`-RequireActive` 时它必须是 `tzg-hourly-controller`。
- `-RequireLegacyRetired` 时本计划 Task 11 的旧文件全部不存在。

checker 不读取业务任务内容，不维护候选列表，不调用 AI，不修改 automation。

- [ ] **Step 7: 运行直接测试和建设期 checker**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2'
```

Expected: 测试最后输出 `test-check-automation-workflow: OK`；checker 最后输出 `check-automation-workflow: OK`。默认模式允许旧文件暂存，但活动入口不得引用它们。

- [ ] **Step 8: 提交薄路由切片**

使用 finalizer 提交本任务五个文件，提交信息：

```text
refactor(automation): replace controller protocol with thin router
```

---

## Task 6: 在隔离环境执行一次预算内集成验证

**Files:**

- Verify only: Task 1–5 的所有新增/修改文件
- Temporary only: 系统临时目录中的外部 AI 金丝雀 repo

- [ ] **Step 1: 确认生产仍暂停且两个工作区没有被误用**

通过自动化管理能力确认 `tzg-hourly-controller` 仍为 `PAUSED`。运行两个工作区的 `git status --short`；建设 worktree 应干净，主工作区状态与 Task 0 以来的用户工作相容。

- [ ] **Step 2: 汇总运行新边界各一次**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
node --test tools/feishu-decision-bridge/test/resume-trigger.test.mjs
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2'
```

Expected: 四项全部为 0。不得追加旧 `hourly-controller-v2/tests/run-tests.ps1` 或完整 `npm test`。

- [ ] **Step 3: 运行唯一一次真实外部 session/self-commit 金丝雀**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-external-ai-self-commit.ps1
```

Expected: `test-external-ai-self-commit: OK`；临时 repo 证明请求决定后进程退出、同 session resume、业务提交和交接提交均由外部 AI 创建、外层没有代验证或代提交。该结果只证明执行边界，不是 TQ-057 或生产自动化成功。

- [ ] **Step 4: 做计划范围内最终文本检查**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 'AGENTS.md,开发管理,tools/hourly-automation-lease.ps1,tools/test-hourly-automation-lease.ps1,tools/test-external-ai-self-commit.ps1'
git diff --check
git status --short
```

Expected: 全部通过，分支干净。若任何测试导致项目文件变化，视为失败并停下调查。

---

## Task 7: 安全同步并合并建设分支

**Files:**

- No planned content changes
- Merge target: `D:\天章游戏开发` 当前主分支

- [ ] **Step 1: 建立主工作区重叠清单**

分别收集：主工作区 staged、unstaged、untracked 路径；主分支相对 Task 0 基线的新提交路径；本功能分支相对共同祖先的变更路径。按文件和父目录重叠检查。

Expected: 自动化目标文件无人工重叠。存在重叠时停止并列出路径，不 stash、不覆盖、不自动合并内容。

- [ ] **Step 2: 在隔离 worktree 吸收主分支最新提交**

```powershell
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' merge master
```

Expected: 无冲突。若主分支名已变化，使用 Task 0 记录的真实主分支名；若任何自动化文件冲突，停止。

- [ ] **Step 3: 只复验受同步影响的静态入口**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2'
git status --short
```

Expected: checker 通过，建设 worktree 干净。

- [ ] **Step 4: 在主工作区执行非破坏性 merge**

```powershell
git -C 'D:\天章游戏开发' merge --no-ff codex/hourly-automation-simplification
```

Expected: merge 成功；Task 0 记录的人工 staged/unstaged/untracked 路径及内容保持不变。失败或冲突时停止，不 abort 后重试其他策略，先报告现场。

- [ ] **Step 5: 合并后只跑入口检查**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发'
```

Expected: 通过；生产自动化仍 `PAUSED`。

---

## Task 8: 更新现有生产自动化但保持暂停

**Files:**

- Read: `开发管理/自动工作流控制器提示词.txt`
- External update: automation `tzg-hourly-controller`

- [ ] **Step 1: 用自动化管理能力更新同一个 automation**

更新现有 `tzg-hourly-controller`：

- 名称继续为 `TZG Hourly Controller`。
- schedule 继续每小时一次，使用当前已验证的 Asia/Hong_Kong 时区语义。
- workdir 为 `D:\天章游戏开发`，不是建设 worktree。
- prompt 精确使用合并后的 canonical 薄提示。
- status 保持 `PAUSED`。

不得新建替代 automation，不得启用 WF1/WF3/WF4，不得手改 TOML。

- [ ] **Step 2: 运行暂停态契约检查**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发'
```

Expected: 通过；automation prompt 与 canonical 一致；所有写入型 automation 均暂停。

- [ ] **Step 3: 记录切换待金丝雀状态**

如果自动化管理更新没有修改项目文件，则不制造状态提交。生产事实先保存在自动化 memory/private runtime；只有 Task 9 成功后再更新人读状态。

---

## Task 9: 在暂停态手动运行一个真实任务

**Files:**

- Runtime-selected business files only
- Private create: manual-run workspace baseline and runtime state

- [ ] **Step 1: 运行前保存观察基线**

记录主工作区 HEAD、staged index entries、unstaged/untracked 路径及内容哈希；用 `automation-workspace-guard.ps1 Snapshot` 把基线写到私有 runtime root。不要要求主工作区先清洁。

- [ ] **Step 2: 由负责人在 Codex 自动化界面点击现有 `TZG Hourly Controller` 的“立即运行”**

自动化仍保持 `PAUSED`；只运行这一轮。不得临时改 schedule、创建一次性 automation 或运行旧 controller CLI。

- [ ] **Step 3: 让当前责任方完整结束，不由观察者接手**

按全局优先级，若无人工路径冲突，首轮预计选择普通 P0 Codex 任务 TQ-057。该自动化任务自己读取正式任务卡、实施、运行数据链及相关 Unity 验证、更新队列/归档并创建路径限定提交。观察者不重跑这些检查、不代提交。

如果选中外部任务，则预期由外部 AI 创建 business + handoff 两个提交；如果全部候选冲突，则本轮应记录阻塞而不是强行选 TQ-057。

- [ ] **Step 4: 只核对生产边界**

检查：

- runtime 只出现一个 lease 且最后释放。
- 本轮只选择一种路由和一个任务。
- Codex 普通任务产生一个业务提交；外部任务产生两个外部自有提交。
- 提交路径属于任务授权范围。
- 运行前人工 staged/unstaged/untracked 内容和 staged 状态保持不变。
- 没有 manifest、plan-only、机器注册表或外层重复验证。
- 若等待决定，模型/CLI 已退出，runtime 保存原 thread/session，自动化仍暂停。

可用提交路径作为 `automation-workspace-guard.ps1 Verify -ExpectedPaths` 的允许集合验证观察基线；不得重跑业务领域检查。

- [ ] **Step 5: 失败关闭**

任何边界失败都保持 automation `PAUSED`，保留 commit、runtime 和日志；不 reset/revert/clean。只回到对应实现 Task 修复并重跑受影响检查。未成功前不得进入 Task 10，也不得删除旧文件。

---

## Task 10: 恢复每小时调度并观察一个自然轮次

**Files:**

- External update: automation `tzg-hourly-controller`
- Private runtime result only

- [ ] **Step 1: 仅在 Task 9 成功后设为 ACTIVE**

通过 Codex 自动化管理能力把现有 `tzg-hourly-controller` 设为 `ACTIVE`。WF1/WF3/WF4 继续 `PAUSED`。

- [ ] **Step 2: 验证唯一活动写入入口**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发' `
  -RequireActive
```

Expected: 唯一活动写入 automation 是 `tzg-hourly-controller`。

- [ ] **Step 3: 等待一个真实 schedule 触发，不手动补跑**

观察下一个自然每小时轮次。等待期间不启动模型轮询；使用自动化运行记录或一次性状态读取确认结果。

可接受结果：成功执行一个任务、成功复审、外部 AI 成功自提交、成功补充队列，或在确无候选且无法补任务时干净结束。不可接受：第二写入者、重复验证、人工脏改变化、卡死进程、旧协议调用或无证据超时。

- [ ] **Step 4: 检查两轮全阻塞行为但不人为制造阻塞**

若自然轮次为全阻塞，只应记录 count=1，不视为稳定观察成功；继续等下一自然轮次。相同 fingerprint 第二次出现必须自动把 `tzg-hourly-controller` 设为 `PAUSED` 并发飞书摘要，此时计划停止等待负责人处理。不得为了测试主动制造生产冲突。

- [ ] **Step 5: 满足退役门禁**

只有 Task 9 真实任务成功且至少一个自然轮次正常结束，才进入 Task 11。

---

## Task 11: 暂停后退役旧编排，再恢复唯一自动化

**Files:**

- Delete: `tools/hourly-controller-v2/`
- Delete: `tools/check-hourly-controller-v2.ps1`
- Delete: `tools/automation-controller.ps1`
- Delete: `tools/automation-controller-state.ps1`
- Delete: `tools/automation-controller-repair.ps1`
- Delete: `tools/automation-decision-status.ps1`
- Delete: `tools/test-automation-controller.ps1`
- Delete: `tools/test-automation-controller-state.ps1`
- Delete: `tools/test-automation-controller-repair.ps1`
- Delete: `tools/test-automation-decision-status.ps1`
- Delete: `tools/fixtures/automation-controller-v5-chained-decision-stuck.json`
- Delete: `开发管理/自动工作流任务注册表.json`
- Delete: `开发管理/自动工作流控制器v2提示词.txt`
- Delete: `开发管理/自动工作流v2规则.txt`
- Modify: `tools/check-pwsh-runtime.ps1`
- Modify: `tools/test-check-pwsh-runtime.ps1`
- Modify: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: 先暂停唯一生产自动化**

通过自动化管理能力把 `tzg-hourly-controller` 设为 `PAUSED`，等待当前运行结束并确认无 lease。若存在任务自有未提交修改，不得退役；只恢复原任务。

- [ ] **Step 2: 把观察期间主分支提交同步回隔离 worktree**

重复 Task 7 的路径重叠检查；无重叠后在隔离 worktree merge 最新主分支。任何自动化文件冲突都停止。

- [ ] **Step 3: 删除只服务旧编排的文件**

删除前先用 `git ls-files -- tools/hourly-controller-v2` 捕获该目录内的完整 tracked 文件列表，供 finalizer 逐文件限定路径。随后用 `apply_patch` 严格删除 File Map 所列路径；不调用递归删除命令，不删除任何私有 automation-state。历史设计、历史计划、TQ-057 正式任务事实和所有私有备份保留。

- [ ] **Step 4: 更新 PowerShell runtime 检查和状态**

`check-pwsh-runtime.ps1` 与测试删除对旧 controller/state 的要求，新增 `tools/hourly-automation-lease.ps1` 的 PowerShell 7 入口断言。`自动工作流状态.txt` 记录真实手动任务、自然整点观察和旧编排退役事实；只引用真实 commit/run 证据，不复制每小时流水。

- [ ] **Step 5: 只运行退役直接验证**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发-worktrees\hourly-controller-v2' `
  -RequireLegacyRetired
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths '开发管理/自动工作流状态.txt,开发管理/自动工作流规则.txt,开发管理/自动工作流控制器提示词.txt'
```

Expected: 三项通过；`rg` 在活动代码和管理入口中找不到旧 controller、registry、manifest 或 v2 prompt 引用。

- [ ] **Step 6: 创建退役提交**

使用 `automation-finalize-commit.ps1`，ExpectedPaths 精确列出全部删除和三份修改文件，提交信息：

```text
chore(automation): retire legacy controller protocol
```

- [ ] **Step 7: 合并退役提交并做最后入口检查**

重复 Task 7 的无重叠合并流程，把退役提交合入主分支；随后运行 `check-automation-workflow.ps1 -RequireLegacyRetired`。不重复外部金丝雀或新模块单元测试。

- [ ] **Step 8: 归档旧暂停 automation 并恢复生产入口**

通过自动化管理能力归档或删除 `tzg-wf1-queue-and-review-maintenance`、`tzg-wf3-claude-execute-1`、`tzg-wf4-codex-execute-2`；保留与本工作流无关且暂停的 daily briefing。确认只有 `tzg-hourly-controller` 作为写入入口，再将其设为 `ACTIVE`。

- [ ] **Step 9: 最终验收**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 `
  -RepositoryRoot 'D:\天章游戏开发' `
  -RequireActive `
  -RequireLegacyRetired
git -C 'D:\天章游戏开发' status --short
```

Expected: checker 通过；用户原有未提交改动仍存在且内容/暂存状态不变；旧私有备份仍存在；生产只有一个活动写入自动化。

---

## Stop Conditions and Recovery

- 不能证明生产暂停：立即停止，不建设或切换。
- 建设 worktree 出现不属于本计划的改动：停止，先确认归属。
- 主工作区目标路径与本分支重叠：停止，列出冲突路径，不覆盖。
- 租约工具状态损坏或 ACL 不合格：失败关闭，不回退旧状态机写入。
- 外部 AI 金丝雀无法自行完成两个提交：停止；不得恢复“外层 Codex代提交”。
- Codex/Claude session resume 不能证明同会话：停止；不得用新会话重新规划原任务。
- 飞书 relay 需要改变现有签名 envelope、binding 或回复协议：停止并重新请求设计批准。
- 手动生产轮次失败：保持 `PAUSED`，旧文件不退役。
- 存在 task-owned uncommitted recovery：普通调度立即停止，只允许原责任方恢复。
- 连续两轮全阻塞：自动 `PAUSED`，飞书通知，等待负责人，不继续 Task 11。

## Spec Coverage Review

| 设计要求 | 实施任务 |
|----------|----------|
| 一个自动化、薄提示、短规则 | Task 5、8、10 |
| 统一优先级与每轮一个 | Task 5、9、10 |
| 一责任方端到端 | Task 4、5、6、9 |
| 外部 AI 自验证自提交 | Task 4、6 |
| 现有 Git 安全组件复用一次 | Task 4、6、9 |
| 最小租约/恢复、无业务状态机 | Task 2 |
| 飞书原样中继、原 thread/session 恢复 | Task 3、6 |
| 占锁排队且不耗 token | Task 2、3、10 |
| 两轮全部阻塞自动暂停 | Task 2、5、10 |
| 无候选才补任务 | Task 5、10 |
| TQ-057 五决定归位且不重问 | Task 1、9 |
| linked worktree 建设、主区可开发 | Task 0、7、11 |
| 真实任务和自然轮次后才退役 | Task 9、10、11 |
| 保留私有备份 | Task 0、11 |
| Validation Budget | Task 1–11 各自验证段 |

## Plan Self-review Checklist

- [ ] 权威设计的 16 节均在 Spec Coverage Review 中有实施落点。
- [ ] 活动 runtime 没有具体任务 ID、manifest、registry、发现 action 或业务 phase。
- [ ] 所有 Create/Modify/Delete 路径均为现有项目真实路径或本计划明确新建路径。
- [ ] 所有 PowerShell 独立命令使用 `pwsh` 7。
- [ ] 所有生产 automation 变更都通过自动化管理能力，未指示编辑 TOML。
- [ ] 所有提交都路径限定，未要求暂存主工作区人工改动。
- [ ] 外部 SHA 循环已用同责任方的 business/handoff 两提交消除。
- [ ] 旧私有状态只备份和保留，没有删除步骤。
- [ ] Task 9 前没有真实项目写入自动化，Task 11 前没有旧编排删除。
- [ ] 文档没有未解释的临时标记、占位符或未锁定接口。
