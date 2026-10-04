# Automation Closeout Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保留短队列、独立任务卡和按需 recovery，同时用一套启动前约束、提交后事实证据和结果分类，修复当前自动化的空队列误失败、Recovery 漏检、路由错配、UTF-8 乱码及完成状态误报。

**Architecture:** 不新增 runtime category、任务状态、recovery 字段、脚本或重试层。`tools/check-task-cards.ps1` 继续是唯一任务投影检查器，但增加启动前路由检查和可选 JSON 证据；`tools/invoke-codex-responsibility.ps1` 只消费这份证据完成统一收尾；提交元数据的 `State` 仍只有 `completed|pending_review`，任务真实结果从该提交中的任务卡或归档事实读取。

**Tech Stack:** PowerShell 7、Git、Codex CLI session runner、现有 schema-3 runtime、Codex automation management API。

## Global Constraints

- 保留 `开发管理/当前任务队列.txt` 的短有序索引、`开发管理/任务卡/<ID>.txt` 的独立事实源和按事件读取 `开发管理/自动工作流恢复规则.txt` 的现状。
- 不新增持久化状态、兼容分支、第二检查器、队列扫描器、重试、自动补录或另一套 recovery。
- `Automation State: completed` 只表示该自动责任方提交已闭环，不再被解释为任务 `dispatchState=completed`；任务真实状态必须来自同一提交里的任务卡或归档。
- 当前 `tzg-hourly-controller` 保持 `PAUSED`，直到全部离线验证通过。实现前后都先运行 `Show`；若出现非空 lease，手动实现必须转入 `.worktrees/`，不得与自动责任方争用主工作区。
- 每个任务只运行列出的最小测试；相关输入未变化时不重复同范围检查。
- 如果实现需要修改 schema-3 runtime、增加 route/state、创建新脚本，或连续突破下列文件边界，立即停止并重新确认根因。

## Confirmed Problem Coverage

| 已确认问题 | 当前证据 | 本计划修复点 |
|---|---|---|
| QueueMaintenance 在没有合法候选、没有事实变化时被记为 `failed/no_verified_outcome`，每小时重复 | 生产 run `7b4b1367-3b8a-4be5-a7d8-a4323b220b6a`；规则明确禁止制造任务或无事实提交 | Task 4：干净空结果使用既有 `blocked` 与稳定 fingerprint；不要求伪造提交 |
| QueueMaintenance 只要有匹配提交就固定记为 `refilled` | `invoke-codex-responsibility.ps1` 当前按特殊 TaskId 直接选择 `refilled`；测试用零 ready 夹具仍期待 `refilled` | Task 3/4：以检查器返回的 `readyCount` 分类；0 ready 的事实修正是 `success`，大于 0 才是 `refilled` |
| Recovery 只跑全局投影，能在任务仍为 ready 时清 recovery 并报成功 | `Test-TaskCardCloseout` 仅对 Execution/Review 添加 TaskId 后置条件 | Task 4：所有非 `QUEUE-MAINTENANCE` TaskId，包括 Recovery，统一要求同一任务非 ready 或完成归档 |
| Execution/Review 启动前不证明 TaskId 与 route/owner/ready 匹配 | 当前固定调用器先启动 runner，提交后才检查任务投影 | Task 3/4：启动 runner 前运行同一检查器的 `CodexDispatchReady` |
| 中文责任提示在父子 pwsh stdin 边界乱码 | 当前 `tools/test-codex-cli-session.ps1` 失败：`中文传输` 变成 `涓枃浼犺緭`；真实 rollout 同样损坏 | Task 2：显式 UTF-8 写端与 `OpenStandardInput()` 读端，并用真实进程边界测试 |
| 非 ready 状态提交被控制器和日报描述为“任务完成” | `5ac0fcb` 的 card 为 `pending_decision`，提交和简报却归为 `completed` | Task 3/5：固定调用器返回 `taskState`；简报从提交快照读取真实 lifecycle |
| 词法检查和正向夹具放过了上述错误 | 工作流检查只在 invoker 中搜索 UTF-8 token；QueueMaintenance/Recovery 测试固化了错误结果 | Task 2–6：把真实负例改为契约测试，删除错误期待 |
| 维护责任方把 `rg` 无匹配短暂当作失败 | 实际责任方随后纠正，且已有 `check-task-cards` 能给出 ready 数 | Task 6：维护验收只依赖结构化 `readyCount`，不再要求重复 `rg` 证明不存在 |
| `81dbf47`、`db251fc` 只补普通路由且继续叠加条件 | 两次提交合计约 `+706/-84`，仍遗漏上述边界 | Task 3/4：将布尔 `Test-TaskCardCloseout` 收拢为单一证据函数，不在其上继续增加 route 特判 |

---

## Task 1: Freeze the Baseline and Record the Approved Contract

**Files:**

- Modify: `docs/superpowers/specs/2026-07-24-event-driven-task-context-optimization-design.md`
- Modify: `开发管理/自动工作流规则.txt`
- Test: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: Confirm production is paused and has no writer**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git status --short
```

Expected:

- automation management view shows `tzg-hourly-controller` is `PAUSED`;
- `lease=null` and `recovery=null`;
- working tree is clean.

If the lease is non-null, stop main-workspace editing and follow the project worktree rule. Do not clear or steal the lease.

- [ ] **Step 2: Add the approved simplification contract to the design**

Append a focused “2026-07-25 收尾简化修订” section covering exactly:

```text
1. 一个检查器输出启动约束和收尾事实，不建立第二套模型。
2. Execution/Review 启动前必须与 ready 卡的 route/owner 精确匹配。
3. 所有普通 TaskId 的 Recovery 与 Execution/Review 使用同一 lifecycle closeout。
4. QueueMaintenance 以 readyCount 分类；没有合法候选且无变化是既有 blocked，不制造提交。
5. Automation State 与任务 dispatchState 分离；报告读取同一提交中的 task lifecycle。
6. responsibility prompt 与 decision reply 的 stdin 都是显式 UTF-8 协议。
```

同时在 `开发管理/自动工作流规则.txt` 先写目标契约，不写实现细节或新增状态。

- [ ] **Step 3: Add a contract-level failing test**

在 `tools/test-check-automation-workflow.ps1` 中要求规则和控制器提示最终包含：

- 启动前 `CodexDispatchReady`；
- Recovery 的 task-bearing closeout；
- QueueMaintenance 的 `readyCount` 结果分类；
- 最终报告包含 `taskState` 或 `readyCount`；
- runner 自身的显式 UTF-8 stdin 读取。

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: FAIL，明确缺失新的契约 token；不得先放宽断言让旧实现通过。

- [ ] **Step 4: Commit the contract**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'docs/superpowers/specs/2026-07-24-event-driven-task-context-optimization-design.md|开发管理/自动工作流规则.txt|tools/test-check-automation-workflow.ps1'
git add -- docs/superpowers/specs/2026-07-24-event-driven-task-context-optimization-design.md 开发管理/自动工作流规则.txt tools/test-check-automation-workflow.ps1
git diff --cached --check
git commit -m "test: define simplified automation closeout contract"
```

---

## Task 2: Repair the UTF-8 Responsibility Prompt Boundary

**Files:**

- Modify: `tools/codex-cli-session.ps1`
- Modify: `tools/invoke-codex-responsibility.ps1`
- Modify: `tools/test-codex-cli-session.ps1`
- Modify: `tools/test-codex-cli-session-canary.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Test: `tools/test-codex-cli-session.ps1`
- Test: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: Preserve the current failing process-boundary assertion**

The unit test must continue to launch a real child `pwsh` and assert exact stdin equality for:

```powershell
$startPrompt = "secret-start-marker-7ee5f0`n模型核验证明`nD:\天章游戏开发`nliteral `` backtick"
```

Make the parent protocol explicit:

```powershell
$startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
```

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
```

Expected before implementation: FAIL with exact stdin mismatch or invalid UTF-8.

- [ ] **Step 2: Replace the system-codepage reader**

In `tools/codex-cli-session.ps1`, replace:

```powershell
$prompt = [Console]::In.ReadToEnd()
```

with one strict reader:

```powershell
$stdinReader = [IO.StreamReader]::new(
  [Console]::OpenStandardInput(),
  [Text.UTF8Encoding]::new($false, $true),
  $false
)
try {
  $prompt = $stdinReader.ReadToEnd()
} finally {
  $stdinReader.Dispose()
}
```

Do not add `chcp`, global console mutation, encoding detection, retry, or fallback decoding.

- [ ] **Step 3: Make the fixed invoker writer explicit**

In the existing `ProcessStartInfo` used by `Invoke-SessionRunner`, add:

```powershell
$startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
```

The decision reply reader remains the existing strict UTF-8 reader; do not create a second decision protocol.

- [ ] **Step 4: Strengthen the real canary**

Make the start-turn continuity marker include both Chinese and the Windows repository path:

```powershell
$transportMarker = "$marker|模型核验证明|D:\天章游戏开发"
```

The resume turn must reproduce that exact marker into `continuity.txt`. This proves the actual `pwsh -> runner -> codex exec` boundary, not only an in-process fake.

- [ ] **Step 5: Point the workflow checker at the correct files**

`tools/check-automation-workflow.ps1` must verify:

- `tools/codex-cli-session.ps1` contains `OpenStandardInput` and strict `UTF8Encoding`;
- `tools/invoke-codex-responsibility.ps1` sets `StandardInputEncoding`;
- the old assertion that found unrelated decision-reader tokens only inside the invoker is removed.

- [ ] **Step 6: Verify**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected:

```text
test-codex-cli-session: OK
test-check-automation-workflow: OK
```

Run `tools/test-codex-cli-session-canary.ps1` only once after all code changes in Task 2 are complete; it invokes real Codex and is not an iterative unit test.

- [ ] **Step 7: Commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/codex-cli-session.ps1|tools/invoke-codex-responsibility.ps1|tools/test-codex-cli-session.ps1|tools/test-codex-cli-session-canary.ps1|tools/check-automation-workflow.ps1'
git add -- tools/codex-cli-session.ps1 tools/invoke-codex-responsibility.ps1 tools/test-codex-cli-session.ps1 tools/test-codex-cli-session-canary.ps1 tools/check-automation-workflow.ps1
git diff --cached --check
git commit -m "fix: make responsibility stdin explicitly UTF-8"
```

---

## Task 3: Make the Existing Task-Card Checker Return One Outcome Evidence

**Files:**

- Modify: `tools/check-task-cards.ps1`
- Modify: `tools/test-check-task-cards.ps1`
- Modify: `开发管理/状态与建议维护规则.txt`
- Test: `tools/test-check-task-cards.ps1`

- [ ] **Step 1: Add failing route-binding and JSON evidence fixtures**

Add tests for:

1. `CodexDispatchReady + ExpectedRoute=codex_execute` accepts an exact ready Codex execution card.
2. The same check rejects `codex_review`, wrong owner, non-ready state, wrong TaskId case, and a missing card.
3. `CodexClosedOrNonReady -OutputJson` returns the exact active state such as `pending_decision`.
4. A completed archive returns `taskState=completed`.
5. A global `-OutputJson` call returns exact `cardCount` and `readyCount`.

Expected JSON shape:

```json
{
  "status": "ok",
  "cardCount": 3,
  "readyCount": 0,
  "taskId": "N-GROUP-02C",
  "taskState": "pending_decision",
  "postcondition": "CodexClosedOrNonReady"
}
```

No value in this object is persisted to runtime.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
```

Expected: FAIL because `CodexDispatchReady`, `ExpectedRoute` and `OutputJson` do not yet exist.

- [ ] **Step 2: Extend the existing checker, not its responsibility**

Add parameters:

```powershell
[ValidateSet('CodexDispatchReady', 'CodexClosedOrNonReady', 'ExternalPendingReview')]
[string]$Postcondition,
[ValidateSet('codex_execute', 'codex_review')]
[string]$ExpectedRoute,
[switch]$OutputJson
```

Rules:

- `CodexDispatchReady` requires `TaskId` and `ExpectedRoute`;
- it accepts only exact `route=$ExpectedRoute`, `owner=codex`, `dispatchState=ready`;
- existing closeout modes keep their semantics;
- a successful task-specific check records the actual `taskState`;
- omitted postcondition remains the same global projection check;
- plain output remains backward compatible for human/script callers;
- `-OutputJson` emits exactly one JSON line.

Do not add another task-card parser to the invoker.

- [ ] **Step 3: Update the rule entry**

Document only the three checker modes and the optional evidence output. Do not describe implementation branches or add a new workflow phase.

- [ ] **Step 4: Verify**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
```

Expected:

- unit suite prints `test-check-task-cards: OK`;
- live project JSON reports `readyCount=0` at the current baseline.

- [ ] **Step 5: Commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-task-cards.ps1|tools/test-check-task-cards.ps1|开发管理/状态与建议维护规则.txt'
git add -- tools/check-task-cards.ps1 tools/test-check-task-cards.ps1 开发管理/状态与建议维护规则.txt
git diff --cached --check
git commit -m "feat: expose task lifecycle evidence from one checker"
```

---

## Task 4: Collapse Invoker Preflight, Recovery Closeout, and Queue Outcomes

**Files:**

- Modify: `tools/invoke-codex-responsibility.ps1`
- Modify: `tools/test-invoke-codex-responsibility.ps1`
- Test: `tools/test-invoke-codex-responsibility.ps1`
- Test: `tools/test-hourly-automation-lease.ps1`

- [ ] **Step 1: Replace the wrong positive fixtures with negative and state-aware cases**

Before implementation, add or change tests so that:

- Execution on a `codex_review` card never starts the fake runner.
- Review on a `codex_execute` card never starts the fake runner.
- Execution/Review on a non-ready card never starts the fake runner.
- interruption Recovery with a matching commit but unchanged ready card is rejected and recovery remains.
- decision Recovery with a matching commit but unchanged ready card is rejected and recovery remains.
- Recovery to `blocked`, `pending_decision`, or completed archive succeeds and clears recovery.
- QueueMaintenance with no commit, clean workspace, valid projection and `readyCount=0` returns `blocked/no_runnable_candidate`, not failed.
- two identical queue-empty results use the same fingerprint and set the existing `pauseRequested=true`.
- QueueMaintenance commit with `readyCount>0` returns `refilled`.
- QueueMaintenance commit with `readyCount=0` but a valid fact correction returns `success`, not `refilled`.
- normal Execution with no commit still returns `failed/no_verified_outcome`.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

Expected: FAIL in the new cases; the existing suite currently passes because it encodes the incomplete behavior.

- [ ] **Step 2: Add preflight before launching the runner**

Map routes without adding a route field:

```powershell
$expectedRoute = switch ($Route) {
  'Execution' { 'codex_execute' }
  'Review'    { 'codex_review' }
  default     { $null }
}
```

For Execution/Review, call the existing checker with:

```powershell
-TaskId $TaskId -Postcondition CodexDispatchReady -ExpectedRoute $expectedRoute -OutputJson
```

Reject a mismatch as existing `failed` with `detailCode=route_precondition_failed`, release the lease, and do not start `codex-cli-session.ps1`.

Also enforce:

```powershell
if ($Route -ceq 'QueueMaintenance') {
  Assert-Contract ($TaskId -ceq 'QUEUE-MAINTENANCE')
} elseif ($Route -cne 'Recovery') {
  Assert-Contract ($TaskId -cne 'QUEUE-MAINTENANCE')
}
```

`Route=Recovery` may legitimately carry the special maintenance TaskId.

- [ ] **Step 3: Replace `Test-TaskCardCloseout` with one evidence reader**

Create one helper that invokes `check-task-cards.ps1 -OutputJson`, validates one JSON line, and returns the parsed evidence.

Post-run mapping:

```text
TaskId != QUEUE-MAINTENANCE
  -> CodexClosedOrNonReady, regardless of Execution/Review/Recovery

TaskId == QUEUE-MAINTENANCE
  -> global projection evidence with readyCount
```

This removes the current `if ($Route -cin @('Execution','Review'))` hole and avoids a separate Recovery branch.

- [ ] **Step 4: Classify accepted commits from evidence**

After exactly one matching commit, no new uncommitted changes, and valid evidence:

```text
ordinary task or task-bearing Recovery
  category=success
  taskState=<checker taskState>

QueueMaintenance, readyCount > 0
  category=refilled
  readyCount=<count>

QueueMaintenance, readyCount == 0
  category=success
  readyCount=0
```

Clear matching recovery only after the relevant evidence succeeds. Return `taskState` or `readyCount` in the invoker JSON, but do not persist either as a new runtime field.

- [ ] **Step 5: Handle the legal no-candidate outcome without a commit**

Only for `TaskId=QUEUE-MAINTENANCE`, when all are true:

- no new commit;
- no new changed path;
- global projection valid;
- `readyCount=0`;
- no decision recovery was saved;

close with:

```powershell
Close-Run `
  -Category 'blocked' `
  -DetailCode 'no_runnable_candidate' `
  -BlockingFingerprint 'queue:no_runnable_candidate'
```

Return existing status/category `blocked`; do not create a commit, recovery, retry or new state. The existing repeated-blocker counter performs the second-run pause.

- [ ] **Step 6: Keep all unrelated failure paths unchanged**

Do not change:

- decision recovery creation;
- interruption recovery for real changed paths;
- unverified-commit blocking;
- normal task no-outcome failure;
- lease release and cleanup ordering.

- [ ] **Step 7: Verify**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected:

```text
test-invoke-codex-responsibility: OK
test-hourly-automation-lease: OK
```

- [ ] **Step 8: Review the diff for subtraction**

Run:

```powershell
git diff --stat HEAD~1 -- tools/invoke-codex-responsibility.ps1 tools/test-invoke-codex-responsibility.ps1
rg -n "Test-TaskCardCloseout|Route -cin" tools/invoke-codex-responsibility.ps1
```

Expected:

- the old boolean helper and its route-only condition are gone;
- implementation has one preflight mapping and one closeout evidence mapping;
- no new runtime schema or recovery branch appears.

- [ ] **Step 9: Commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/invoke-codex-responsibility.ps1|tools/test-invoke-codex-responsibility.ps1'
git add -- tools/invoke-codex-responsibility.ps1 tools/test-invoke-codex-responsibility.ps1
git diff --cached --check
git commit -m "fix: unify responsibility preflight and closeout"
```

---

## Task 5: Report Task Lifecycle Instead of Calling Every Commit Completed

**Files:**

- Modify: `tools/get-automation-briefing-source.ps1`
- Modify: `tools/test-get-automation-briefing-source.ps1`
- Modify: `开发管理/自动化简报提示词.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `tools/test-check-automation-workflow.ps1`
- Test: `tools/test-get-automation-briefing-source.ps1`
- Test: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: Add lifecycle-backed briefing fixtures**

Each Codex automation fixture must commit a real task projection:

- active `blocked`;
- active `pending_decision`;
- completed archive;
- external `pending_review`;
- queue maintenance.

Add the regression corresponding to `5ac0fcb`: metadata `State: completed` plus committed task card `dispatchState=pending_decision` must produce category `pending_decision`, never `completed`.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-automation-briefing-source.ps1
```

Expected: FAIL because the source currently maps every Codex `State: completed` commit to category `completed`.

- [ ] **Step 2: Read lifecycle from each commit snapshot**

For Codex commits:

1. If `开发管理/任务归档/<TaskId>.txt` exists in that commit, strictly parse its metadata and require `dispatchState=completed`.
2. Otherwise read `开发管理/任务卡/<TaskId>.txt` from that commit and require one of `blocked|frozen|pending_decision|waiting_reply`.
3. If neither exact fact exists, emit `outcome_unverifiable`; do not fall back to free-text `Result`.

Keep:

- external `State: pending_review` -> `pending_review`;
- `QUEUE-MAINTENANCE` -> `queue_maintenance`;
- commit metadata structure and finalizer vocabulary unchanged.

For multiple commits of the same TaskId, the newest commit’s verified lifecycle category wins. Remove the current “any completed wins forever” precedence.

- [ ] **Step 3: Make controller and briefing wording state-aware**

The hourly controller final line must include:

```text
route、TaskId、category、taskState 或 readyCount、sessionId、commitSha 或 recovery 状态
```

The daily briefing prompt must describe:

- `completed` only for a completed archive;
- `blocked/frozen/pending_decision/waiting_reply` using the committed task state;
- `pending_review` for external work;
- queue maintenance as maintenance, not automatically as refill.

Do not add another report database or parse model prose.

- [ ] **Step 4: Verify**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-automation-briefing-source.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected:

```text
test-get-automation-briefing-source: OK
test-check-automation-workflow: OK
```

- [ ] **Step 5: Commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/get-automation-briefing-source.ps1|tools/test-get-automation-briefing-source.ps1|开发管理/自动化简报提示词.txt|开发管理/自动工作流控制器提示词.txt|tools/test-check-automation-workflow.ps1'
git add -- tools/get-automation-briefing-source.ps1 tools/test-get-automation-briefing-source.ps1 开发管理/自动化简报提示词.txt 开发管理/自动工作流控制器提示词.txt tools/test-check-automation-workflow.ps1
git diff --cached --check
git commit -m "fix: report committed task lifecycle"
```

---

## Task 6: Align Canonical Rules and Remove Redundant Absence Checks

**Files:**

- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`

- [ ] **Step 1: Make the final prose match the implemented contract**

Replace the current statement that “QueueMaintenance 与 Recovery 只运行全局投影检查” with:

```text
- task-bearing Recovery 与普通 Codex 任务使用同一 TaskId lifecycle closeout；
- maintenance recovery 和 QueueMaintenance 使用全局 readyCount；
- clean zero-candidate maintenance records existing blocked/no_runnable_candidate and creates no commit；
- only readyCount > 0 is refilled。
```

Clarify:

- `AutomationState=completed` is a responsibility commit state, not the task-card lifecycle;
- controller/report consumers use `taskState` or committed task projection;
- QueueMaintenance accepts `check-task-cards -OutputJson` as the absence proof and does not need another `rg` no-match check.

- [ ] **Step 2: Delete stale or duplicate contract assertions**

In the workflow checker and its tests:

- remove the old Recovery-global-only line;
- remove the old invoker-only UTF-8 token check;
- avoid asserting the same lifecycle sentence in multiple files when one canonical rule plus one prompt reference is sufficient;
- keep structural checks for the actual runner, checker mode and controller reporting fields.

- [ ] **Step 3: Run management checks**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -OutputJson
```

Expected: all pass; task-card output remains the current valid projection.

- [ ] **Step 4: Commit**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流规则.txt|开发管理/状态与建议维护规则.txt|开发管理/AI协作规则.txt|开发管理/自动工作流控制器提示词.txt|tools/check-automation-workflow.ps1|tools/test-check-automation-workflow.ps1'
git add -- 开发管理/自动工作流规则.txt 开发管理/状态与建议维护规则.txt 开发管理/AI协作规则.txt 开发管理/自动工作流控制器提示词.txt tools/check-automation-workflow.ps1 tools/test-check-automation-workflow.ps1
git diff --cached --check
git commit -m "docs: align simplified automation outcome contract"
```

---

## Task 7: Final Verification and Controlled Production Handoff

**Files:**

- Verify only; do not edit runtime JSON or automation TOML directly.

- [ ] **Step 1: Run the minimum complete regression set once**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-get-automation-briefing-source.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
```

Expected: every command exits 0.

- [ ] **Step 2: Run the expensive real session canary once**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session-canary.ps1
```

Expected:

```text
test-codex-cli-session-canary: OK sessionId=<real-id> commits=1
```

The canary must prove the exact Chinese/path marker survived Start -> Resume without leaking it in output.

- [ ] **Step 3: Confirm complexity did not expand**

```powershell
git diff --stat 5ac0fcb..HEAD
rg -n "schemaVersion|ValidateSet\\(|SaveRecovery|SaveInterruption|retry|fallback" tools/invoke-codex-responsibility.ps1 tools/check-task-cards.ps1
```

Review criteria:

- no new runtime schema, recovery type, state, retry or fallback;
- one task-card parser;
- one preflight route mapping;
- one post-run evidence mapping;
- no second checker/script;
- implementation changes stay inside the planned files.

If these criteria fail, stop and simplify before production validation.

- [ ] **Step 4: Synchronize canonical prompt only through automation management**

Use the Codex automation management API to update the existing `tzg-hourly-controller` with the complete canonical prompt from `开发管理/自动工作流控制器提示词.txt`. Preserve its schedule, project, model, reasoning and notification fields. Do not edit `automation.toml`.

Keep status `PAUSED` for the controlled validation because the current project has `readyCount=0`.

- [ ] **Step 5: Validate the real fixed-invoker empty-queue outcome without inventing work**

With `lease=null` and `recovery=null`, use the existing lease tool to acquire one real `QUEUE-MAINTENANCE` run, then call `tools/invoke-codex-responsibility.ps1 -Action Start -Route QueueMaintenance` once with:

- the acquired Run ID;
- the exact current repository root;
- the model value already verified through Node REPL;
- no hand-built prompt or alternate process launcher.

This is the production responsibility boundary the controller uses, while the scheduled automation itself remains paused.

Expected:

- responsibility prompt contains intact `模型核验证明` and `D:\天章游戏开发`;
- no project commit is created when no legal candidate exists;
- invoker returns `category=blocked`, `detailCode=no_runnable_candidate`, `readyCount=0`;
- runtime blocker count becomes 1 with fingerprint `queue:no_runnable_candidate`;
- lease is released and recovery remains null;
- automation remains `PAUSED` after the canary.

Do not run a second production no-candidate cycle merely to test the pause threshold; that behavior is already covered by `tools/test-invoke-codex-responsibility.ps1` and `tools/test-hourly-automation-lease.ps1`.

- [ ] **Step 6: Activation decision**

Only activate the existing hourly automation when at least one authoritative ready candidate exists or the user explicitly requests active monitoring of the blocked state. If the queue is still legally empty, leave it paused and report that this is a correct terminal state, not a failure.

- [ ] **Step 7: Final handoff**

Report:

- commit SHAs for Tasks 1–6;
- exact test commands and results;
- real canary session ID;
- controlled production run ID and `no_runnable_candidate` result;
- final automation status, lease, recovery and blocker count;
- confirmation that no runtime schema/state/checker/retry layer was added.
