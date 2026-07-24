# Event-Driven Task Context Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将当前每轮汇总候选的自动工作流迁移为事件驱动的有序 `ready` 队列，使普通执行只读取短队列和一张权威任务卡，同时保留现有恢复、租约、路径冲突、复审、提交和暂停边界。

**Architecture:** `开发管理/当前任务队列.txt` 只保存按执行顺序排列的 `ready` 投影；`开发管理/任务卡/<ID>.txt` 保存活跃任务唯一结构化元数据和正文；分线 backlog 保存未完成任务的短投影。队列只在任务进入、状态变化、完成、复审转换或空队列事件中更新。现有固定调用器继续承载 Codex 责任方，恢复细节拆到独立规则，并且只在 `Show` 返回 existing recovery 或普通责任方实际到达新的用户决定事件时按需读取；不新增调度器、数据库、缓存、依赖求解器或长期 runtime 状态。

**Tech Stack:** PowerShell 7、UTF-8 JSON 元数据、Markdown 表格、现有 Codex Desktop automation、Git、现有 automation lease / invoker / finalizer。

## Global Constraints

- 实施前完整读取批准规格：`docs/superpowers/specs/2026-07-24-event-driven-task-context-optimization-design.md`。
- 开始写入前，必须通过 Codex automation 管理能力将 `tzg-hourly-controller` 设为 `PAUSED`；禁止直接编辑 `%USERPROFILE%\.codex\automations\...\automation.toml`。
- 暂停时保留现有 automation 的 `id`、名称、kind、RRULE、模型、reasoning effort、project、execution environment、destination 和通知配置，只改变 `status`；更新 canonical prompt 时仍使用 automation 管理能力提交完整配置。
- 暂停后调用：

  ```powershell
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
  ```

  只有 `leaseStatus=none`、`lease=null`、`recovery=null` 时开始修改。存在 active lease 时等待原轮次结束；存在 recovery 时先停止本计划并完成原恢复链，不得清除或覆盖。
- 本计划是手动实施工作。若开始写入前 `Show` 已返回非空 lease，按 `AGENTS.md` 使用 `.worktrees/` 隔离；不得与自动责任方共享写入面。
- 控制器启动的 Codex 自动责任方仍只能在固定调用器传入的 `RepositoryRoot` 当前分支与当前 `HEAD` 工作；不得调用 `using-git-worktrees`、`git worktree add`，不得创建或切换 linked worktree / 任务分支。
- 保留 `tools/invoke-codex-responsibility.ps1` 当前显式 UTF-8 `StreamReader` stdin 实现和对应中文自定义决定回复回归；不得改回系统代码页或 `[Console]::In.ReadToEnd()`。
- 任务卡检查器只读。它不得排序、生成、修复或执行任务，不得修改队列、任务卡、backlog、Git 或 runtime。
- 不新增中央 manifest、第二套队列、阶段数据库、依赖求解器、缓存、重试层或兼容格式解析。
- 首次迁移只为 `N-GROUP-02C`、`N-SLOT-01`、`U-2_5D-01D` 建立活跃任务卡；不得顺手拆分其余 backlog。
- 不修改 Unity、BattleSim、CSV、asset 或游戏设计事实；本计划不运行 Unity、BattleSim 或数据链路检查，除非实施意外触及这些路径。若发生，停止并重新确认范围。
- 每个任务提交前仅 stage 该任务列出的文件。先运行 `tools/check-pending-whitespace.ps1`，stage 后运行 `git diff --cached --check` 和 `git diff --cached --name-status`。
- 任何预期旧文本、现行任务状态或 automation 配置与本计划不一致时，先查明新事实并更新计划，不叠加兼容分支。

## Production Preflight

- [ ] **Step 1: Inspect and pause the live writer through the automation API**

Use Codex automation management to view `tzg-hourly-controller`, record its complete current configuration, then update the same ID to `PAUSED` while preserving every other field.

Expected stable fields at planning time:

```text
id: tzg-hourly-controller
name: TZG Hourly Controller
kind: cron
rrule: FREQ=HOURLY;INTERVAL=1;BYMINUTE=15
model: gpt-5.6-terra
reasoningEffort: high
executionEnvironment: local
destination: local
projectId: local-b2d3c817de7062bf08f61ab59e276c8b
```

If any field differs, preserve the live value and use it in the final update. Do not create another automation.

- [ ] **Step 2: Prove there is no in-flight writer or recovery**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git status --short --branch
git log -5 --oneline
```

Expected: automation is paused; lease and recovery are null; the current branch contains the approved design commits; unrelated user changes, if any, are identified and left untouched.

---

### Task 1: Add one read-only task-card consistency checker

**Files:**
- Create: `tools/check-task-cards.ps1`
- Create: `tools/test-check-task-cards.ps1`

**Interfaces:**
- Command:

  ```powershell
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 `
    -RepositoryRoot 'D:\天章游戏开发'
  ```

- Optional fixture overrides remain repository-relative:

  ```powershell
  -TaskCardRoot '开发管理/任务卡'
  -QueuePath '开发管理/当前任务队列.txt'
  -BacklogRoot '开发管理/任务列表'
  ```

- Success output:

  ```text
  check-task-cards: OK (cards=<n> ready=<n>)
  ```

- Failure: write a stable diagnostic to stderr and exit nonzero. Never mutate files.

- [ ] **Step 1: Write the fixture-driven tests before the checker**

Create `tools/test-check-task-cards.ps1` with `#requires -Version 7.0`, temp-root safety checks, UTF-8 fixture writers, and process-based assertions.

The canonical valid fixture must contain:

```text
开发管理/任务卡/T-READY-01.txt
开发管理/任务卡/T-BLOCKED-01.txt
开发管理/当前任务队列.txt
开发管理/任务列表/数值与战斗任务.txt
```

Use this exact task metadata shape:

```json
{
  "schemaVersion": 1,
  "id": "T-READY-01",
  "title": "合法 ready 卡",
  "priority": "P2",
  "route": "codex_execute",
  "owner": "codex",
  "domain": "battlesim",
  "stage": "implementation",
  "dispatchState": "ready",
  "blockedBy": [],
  "stateReason": null,
  "expectedPaths": [
    "simulations/BattleSim/Combat.cs",
    "开发管理/任务卡/T-READY-01.txt"
  ],
  "sourceBacklog": "开发管理/任务列表/数值与战斗任务.txt"
}
```

The card file must place that JSON between `---TASK-META---` and `---TASK-BODY---`, then include:

```markdown
# T-READY-01 · 合法 ready 卡

## 来源与当前边界
## 必查范围
## 实施范围
## 禁止项
## 验证
## 完成条件
## 停止条件
```

The valid queue fixture must use this exact header:

```text
| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |
```

The valid backlog fixture must use this exact header:

```text
| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |
```

Add independent failing cases for:

1. missing task-card directory;
2. invalid UTF-8 or invalid JSON;
3. duplicate or missing metadata delimiter;
4. missing required metadata field;
5. filename / `id` mismatch and duplicate ID;
6. illegal `schemaVersion`, priority, route, owner, domain, stage or dispatch state;
7. rooted, backslash, wildcard, `.` / `..` or directory-like `expectedPaths`;
8. route/owner mismatch (`codex_*` not owned by `codex`, or `external_execute` owned by `codex`);
9. missing required body heading or mismatched `# <ID> · <title>`;
10. queue row/card projection mismatch;
11. non-`ready` card in the queue;
12. a `ready` card missing from the queue or duplicated in it;
13. card missing from `sourceBacklog`;
14. backlog priority, owner, state projection, blocker list, title or card path mismatch;
15. `completed` card left in the active task-card directory;
16. self-dependency and a two-card dependency cycle.

Also add a passing transition case that first validates:

```text
route=external_execute, owner=deepseek, dispatchState=ready
```

then rewrites the same card and same queue row to:

```text
route=codex_review, owner=codex, dispatchState=ready
```

The test must prove no second task-card ID is created.

- [ ] **Step 2: Run the RED test**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
```

Expected: nonzero because `tools/check-task-cards.ps1` does not exist. If it passes, the test is not exercising the intended command.

- [ ] **Step 3: Implement strict UTF-8 card and Markdown-table parsing**

Start `tools/check-task-cards.ps1` with:

```powershell
#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$TaskCardRoot = '开发管理/任务卡',
  [string]$QueuePath = '开发管理/当前任务队列.txt',
  [string]$BacklogRoot = '开发管理/任务列表'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
```

Implement only these helpers:

```powershell
function Assert-Contract {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Read-Utf8Text {
  param([string]$Path)
  try {
    [Text.UTF8Encoding]::new($false, $true).GetString(
      [IO.File]::ReadAllBytes($Path)
    ).TrimStart([char]0xFEFF)
  } catch {
    throw "invalid UTF-8: $Path"
  }
}

function Normalize-Cell {
  param([string]$Value)
  $Value.Trim().Trim([char]96)
}
```

For each `开发管理/任务卡/*.txt`:

1. require exactly one metadata delimiter and one body delimiter;
2. parse the substring between them with `ConvertFrom-Json -Depth 100`;
3. require these fields:

   ```powershell
   @(
     'schemaVersion', 'id', 'title', 'priority', 'route', 'owner',
     'domain', 'stage', 'dispatchState', 'blockedBy', 'stateReason',
     'expectedPaths', 'sourceBacklog'
   )
   ```

4. require exact enum membership:

   ```powershell
   $routes = @('codex_execute', 'external_execute', 'codex_review')
   $owners = @('codex', 'deepseek', 'claude')
   $domains = @('unity', 'battlesim', 'data', 'content', 'management', 'automation')
   $stages = @('discovery', 'decision', 'design', 'implementation', 'migration', 'verification')
   $states = @('ready', 'blocked', 'frozen', 'pending_decision', 'waiting_reply', 'completed')
   ```

5. require `priority -match '^P[0-3]$'`;
6. require exact repository-relative forward-slash paths in `expectedPaths` and `sourceBacklog`;
7. reject an empty path, rooted path, backslash, wildcard, trailing slash, `.` segment or `..` segment;
8. require `codex_execute` and `codex_review` to use owner `codex`; require `external_execute` to use owner `deepseek` or `claude`;
9. require the exact H1 and seven body headings listed in Step 1;
10. reject `dispatchState=completed` in the active directory.

Do not require `expectedPaths` to exist, because new files are legal task outputs.

- [ ] **Step 4: Implement exact queue/backlog projection validation and cycle detection**

Parse only the first table matching each exact header. Do not support the legacy six-column queue or five-column backlog as a fallback.

Queue rules:

```text
- every row has exactly 8 cells;
- every row references one existing active task card;
- card path is exactly 开发管理/任务卡/<ID>.txt;
- route, owner, priority, domain, stage and title equal the card header;
- card dispatchState is ready;
- every ready card appears exactly once;
- no non-ready card appears.
```

Backlog rules for every card:

```text
- sourceBacklog exists below 开发管理/任务列表/;
- exactly one row has the same ID and card path;
- priority, owner and title equal the card header;
- ready -> 状态投影=已排队;
- blocked -> 状态投影=阻塞;
- frozen -> 状态投影=冻结;
- pending_decision -> 状态投影=待决定;
- waiting_reply -> 状态投影=等待回复;
- 阻塞于 is — for an empty blockedBy list;
- otherwise 阻塞于 equals blockedBy joined with 、 in metadata order.
```

Legacy backlog rows with `任务卡=—` are allowed and are not interpreted as structured cards.

Use a three-color depth-first walk over dependencies that resolve to active card IDs:

```powershell
$visitState = @{} # 0/unseen, 1/visiting, 2/done
```

Unknown `blockedBy` IDs remain legal because the first migration intentionally does not card every backlog item. Reject self-links and any cycle visible among carded tasks.

Finish with:

```powershell
Write-Output "check-task-cards: OK (cards=$($cards.Count) ready=$($readyCards.Count))"
```

- [ ] **Step 5: Run GREEN tests and prove the checker is read-only**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
git status --short
```

Expected: test exits zero; only the two new scripts are untracked; no fixture survives outside the validated temp directory.

- [ ] **Step 6: Commit the checker**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 `
  -ExpectedPaths 'tools/check-task-cards.ps1|tools/test-check-task-cards.ps1'
git add -- tools/check-task-cards.ps1 tools/test-check-task-cards.ps1
git diff --cached --check
git diff --cached --name-status
git commit -m "tools: add task card consistency checker"
```

Expected: staged set contains exactly the two scripts and the commit succeeds.

---

### Task 2: Migrate the current queue into three active cards and one short index

**Files:**
- Create: `开发管理/任务卡/N-GROUP-02C.txt`
- Create: `开发管理/任务卡/N-SLOT-01.txt`
- Create: `开发管理/任务卡/U-2_5D-01D.txt`
- Create: `开发管理/任务归档/2026-07-24-当前任务队列已完成卡归档.txt`
- Modify: `开发管理/当前任务队列.txt`
- Modify: `开发管理/任务列表/场景与Unity任务.txt`
- Modify: `开发管理/任务列表/内容设计任务.txt`
- Modify: `开发管理/任务列表/审核与交接任务.txt`
- Modify: `开发管理/任务列表/数据链路任务.txt`
- Modify: `开发管理/任务列表/数值与战斗任务.txt`

**Interfaces:**
- One authoritative active card per ID.
- Queue contains only `dispatchState=ready`.
- Non-ready active cards remain discoverable from their `sourceBacklog`.
- Completed legacy queue cards move to one dated archive and disappear from queue and unfinished backlog.

- [ ] **Step 1: Prove the old queue does not satisfy the new contract**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1
```

Expected: nonzero because `开发管理/任务卡/` and the new queue schema do not yet exist. This is the migration RED state.

- [ ] **Step 2: Create the three exact metadata headers**

Use this header for `N-GROUP-02C`:

```json
{
  "schemaVersion": 1,
  "id": "N-GROUP-02C",
  "title": "临时团队 AI 的确定性候选优先级",
  "priority": "P3",
  "route": "codex_execute",
  "owner": "codex",
  "domain": "battlesim",
  "stage": "implementation",
  "dispatchState": "ready",
  "blockedBy": [],
  "stateReason": null,
  "expectedPaths": [
    "simulations/BattleSim/Combat.cs",
    "simulations/BattleSim/BattleSimSelfTests.cs",
    "simulations/BattleSim/Program.cs",
    "开发管理/2v2范围技能协同走位与团队AI参数事实源及决策输入.txt",
    "开发管理/任务列表/数值与战斗任务.txt",
    "开发管理/当前任务队列.txt",
    "开发管理/任务卡/N-GROUP-02C.txt"
  ],
  "sourceBacklog": "开发管理/任务列表/数值与战斗任务.txt"
}
```

Use this header for `N-SLOT-01`:

```json
{
  "schemaVersion": 1,
  "id": "N-SLOT-01",
  "title": "金丹槽位与丹枢加成数值方案",
  "priority": "P2",
  "route": "codex_execute",
  "owner": "codex",
  "domain": "battlesim",
  "stage": "design",
  "dispatchState": "blocked",
  "blockedBy": [
    "N-FPD-DANXIANG-01",
    "N-FPD-MANSION-01"
  ],
  "stateReason": "等待丹相主辅预算与五府府体预算完成",
  "expectedPaths": [
    "simulations/BattleSim/Program.cs",
    "docs/角色养成/术法槽位设计.txt",
    "docs/基础设定/角色数值设计.txt",
    "开发管理/金丹槽位与丹枢加成数值验证记录.txt",
    "开发管理/任务列表/数值与战斗任务.txt",
    "开发管理/当前任务队列.txt",
    "开发管理/任务卡/N-SLOT-01.txt"
  ],
  "sourceBacklog": "开发管理/任务列表/数值与战斗任务.txt"
}
```

Use this header for `U-2_5D-01D`:

```json
{
  "schemaVersion": 1,
  "id": "U-2_5D-01D",
  "title": "共享空间查询契约实现与旧范围逻辑迁移",
  "priority": "P2",
  "route": "codex_execute",
  "owner": "codex",
  "domain": "unity",
  "stage": "implementation",
  "dispatchState": "frozen",
  "blockedBy": [],
  "stateReason": "前置已完成，但负责人尚未重新授予实施授权",
  "expectedPaths": [
    "src/Assets/Scripts/Core/SpatialRules.meta",
    "src/Assets/Scripts/Core/SpatialRules/SpatialHexCoord.cs",
    "src/Assets/Scripts/Core/SpatialRules/SpatialHexCoord.cs.meta",
    "src/Assets/Scripts/Core/SpatialRules/SpatialQueryTypes.cs",
    "src/Assets/Scripts/Core/SpatialRules/SpatialQueryTypes.cs.meta",
    "src/Assets/Scripts/Core/SpatialRules/SpatialQueryBoard.cs",
    "src/Assets/Scripts/Core/SpatialRules/SpatialQueryBoard.cs.meta",
    "src/Assets/Scripts/Grid/SpatialQueryBoardFactory.cs",
    "src/Assets/Scripts/Grid/SpatialQueryBoardFactory.cs.meta",
    "src/Assets/Tests/EditMode/SpatialQueryBoardTests.cs",
    "src/Assets/Tests/EditMode/SpatialQueryBoardTests.cs.meta",
    "src/Assets/Scripts/Core/HexGrid.cs",
    "src/Assets/Scripts/Grid/TacticalGridModel.cs",
    "src/Assets/Scripts/Grid/EnvironmentProfileData.cs",
    "src/Assets/Scripts/Editor/DataConfigImporter.cs",
    "src/Assets/DataConfig/EnvironmentProfiles.csv",
    "src/Assets/DataConfig/README.txt",
    "src/Assets/Tests/EditMode/EnvironmentProfileDataTests.cs",
    "src/Assets/Scripts/Combat/CombatResolver.cs",
    "src/Assets/Scripts/Combat/TacticalCombatController.cs",
    "src/Assets/Scripts/Combat/EnemyAI.cs",
    "src/Assets/Scripts/Map/ExplorationController.cs",
    "simulations/BattleSim/BattleSim.csproj",
    "simulations/BattleSim/Battlefield.cs",
    "simulations/BattleSim/Combat.cs",
    "simulations/BattleSim/BattleSimSelfTests.cs",
    "开发管理/任务列表/场景与Unity任务.txt",
    "开发管理/当前任务队列.txt",
    "开发管理/任务卡/U-2_5D-01D.txt"
  ],
  "sourceBacklog": "开发管理/任务列表/场景与Unity任务.txt"
}
```

Each file must use:

```text
---TASK-META---
<JSON>
---TASK-BODY---
```

Move the corresponding full logic unit from `开发管理/当前任务队列.txt` into the seven required body sections. Preserve all source facts, exact symbol/path reads, scope, prohibitions, validation, completion conditions and stop conditions. For `U-2_5D-01D`, preserve the rollback record in `## 来源与当前边界`. Do not duplicate priority, route, owner, domain, stage, state, blocker list or expected-path list in the body.

- [ ] **Step 3: Convert all five backlog top tables to one projection schema**

Replace each top task table with:

```text
| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |
|----|--------|------|----------|--------|------|--------|
```

For uncarded legacy rows:

- preserve existing priority, owner, status wording and summary;
- where the old table has no owner column, use an already-established owner rather than guessing; the only such retained row in the reviewed fixture is `M-MGMT-01`, whose owner is `codex`;
- set `阻塞于` to `—`;
- set `任务卡` to `—`;
- do not create cards or reinterpret their dependencies in this migration.

For the three carded rows, use exactly:

```text
| N-GROUP-02C | P3 | codex | 已排队 | — | 临时团队 AI 的确定性候选优先级 | 开发管理/任务卡/N-GROUP-02C.txt |
| N-SLOT-01 | P2 | codex | 阻塞 | N-FPD-DANXIANG-01、N-FPD-MANSION-01 | 金丹槽位与丹枢加成数值方案 | 开发管理/任务卡/N-SLOT-01.txt |
| U-2_5D-01D | P2 | codex | 冻结 | — | 共享空间查询契约实现与旧范围逻辑迁移 | 开发管理/任务卡/U-2_5D-01D.txt |
```

Add `U-2_5D-01D` to `场景与Unity任务.txt`; it is currently absent from that backlog.

Keep the uncarded parent `N-GROUP-02`, but change its projection to:

```text
进行中（子任务 N-GROUP-02C 已排队）
```

and keep `任务卡=—`. It is not independently ready.

- [ ] **Step 4: Archive six completed legacy cards and remove completed backlog residue**

Create:

```text
开发管理/任务归档/2026-07-24-当前任务队列已完成卡归档.txt
```

Copy the complete current queue card bodies for:

```text
C-ENV-PROFILE-01
D-REALM-02B
N-GROUP-02B
N-AI-01A
M-MGMT-01A
M-MGMT-01B
```

Add a short archive header stating that these were legacy full cards completed or reviewed before the event-driven migration and are not active task-card files.

Remove:

- `D-REALM-02B` row and full section from `数据链路任务.txt`;
- `N-GROUP-02B` row and full section from `数值与战斗任务.txt`;
- `N-AI-01A` completed full section from `数值与战斗任务.txt`;
- `M-MGMT-01B` completed row from `审核与交接任务.txt`.

Do not remove historical references that merely state a dependency was completed.

- [ ] **Step 5: Replace the current queue with the short ordered index**

The whole file must contain only a short contract note and this table:

```text
# 当前任务队列（✅ 已审核）

> 本表只保存已排好固定顺序的 `dispatchState=ready` 工作。行顺序就是调度顺序；正文、阻塞与冻结状态读取对应任务卡和分线 backlog。
> 普通轮次按顺序选择第一项当前可安全执行的工作；队列只在任务进入、状态变化、完成、复审转换、负责人重排或空队列事件中维护。

| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |
|----|------|------|--------|------|------|------|--------|
| N-GROUP-02C | codex_execute | codex | P3 | battlesim | implementation | 临时团队 AI 的确定性候选优先级 | 开发管理/任务卡/N-GROUP-02C.txt |
```

Do not retain completed, blocked or frozen rows in this file.

- [ ] **Step 6: Run the migration GREEN checks**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1

$queueLength = (Get-Content -LiteralPath '开发管理/当前任务队列.txt' -Raw).Length
if ($queueLength -gt 2500) { throw "Queue remains too large: $queueLength" }

$queueRows = @(rg '^\| [A-Z0-9_/-]+ \|' '开发管理/当前任务队列.txt')
if ($queueRows.Count -ne 1 -or -not $queueRows[0].Contains('N-GROUP-02C')) {
  throw 'Current queue is not the expected one-row ready index.'
}

rg -n '^### (D-REALM-02B|N-GROUP-02B|N-AI-01A)' `
  '开发管理/任务列表/数据链路任务.txt' `
  '开发管理/任务列表/数值与战斗任务.txt'
if ($LASTEXITCODE -eq 0) { throw 'Completed full section remains in an unfinished backlog.' }
if ($LASTEXITCODE -ne 1) { throw 'rg failed while checking completed sections.' }
```

Expected: task-card tests and production checker pass; queue is below 2,500 characters and has only `N-GROUP-02C`; no completed full section remains in the unfinished backlogs.

- [ ] **Step 7: Run text checks and commit the migration**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 `
  -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 `
  -ExpectedPaths '开发管理/任务卡/N-GROUP-02C.txt|开发管理/任务卡/N-SLOT-01.txt|开发管理/任务卡/U-2_5D-01D.txt|开发管理/任务归档/2026-07-24-当前任务队列已完成卡归档.txt|开发管理/当前任务队列.txt|开发管理/任务列表/场景与Unity任务.txt|开发管理/任务列表/内容设计任务.txt|开发管理/任务列表/审核与交接任务.txt|开发管理/任务列表/数据链路任务.txt|开发管理/任务列表/数值与战斗任务.txt'
git add -- `
  '开发管理/任务卡/N-GROUP-02C.txt' `
  '开发管理/任务卡/N-SLOT-01.txt' `
  '开发管理/任务卡/U-2_5D-01D.txt' `
  '开发管理/任务归档/2026-07-24-当前任务队列已完成卡归档.txt' `
  '开发管理/当前任务队列.txt' `
  '开发管理/任务列表/场景与Unity任务.txt' `
  '开发管理/任务列表/内容设计任务.txt' `
  '开发管理/任务列表/审核与交接任务.txt' `
  '开发管理/任务列表/数据链路任务.txt' `
  '开发管理/任务列表/数值与战斗任务.txt'
git diff --cached --check
git diff --cached --name-status
git commit -m "docs: migrate active tasks to ordered card index"
```

Expected: staged paths exactly match the list and the commit succeeds.

---

### Task 3: Make event-driven queue transitions the only management contract

**Files:**
- Modify: `AGENTS.md`
- Modify: `开发管理/状态与建议维护规则.txt`
- Modify: `开发管理/AI协作规则.txt`
- Modify: `开发管理/审核入口.txt`
- Modify: `开发管理/DeepSeek工作提示词.txt`
- Modify: `开发管理/管理目录唯一旧新路径映射与批准输入.txt`

**Interfaces:**
- Pure `1`: first matching execution route in the ordered queue.
- Pure `2`: first `codex_review` route in the ordered queue, then load review evidence.
- External completion: same card changes from `external_execute` to `codex_review`.
- Queue maintenance occurs only on explicit state events or an empty queue.

- [ ] **Step 1: Record the legacy selection and depth wording that must disappear**

Run:

```powershell
rg -n '三类均无|至少包含 2 张|单次最多新增 3 张|依次从当前队列|风险最高的待复审项|复制为完整任务卡|推送后再移除' `
  AGENTS.md `
  '开发管理/状态与建议维护规则.txt' `
  '开发管理/AI协作规则.txt' `
  '开发管理/审核入口.txt' `
  '开发管理/DeepSeek工作提示词.txt'
```

Expected: the current state and collaboration rules still contain per-round multi-source selection or fixed-depth queue wording. Save the output only as implementation evidence; do not add it to runtime state.

- [ ] **Step 2: Make `状态与建议维护规则.txt` the authoritative card/transition contract**

Update its file-layer table:

```text
当前任务队列.txt -> 有序 ready 调度索引；普通 1 / 2 和自动选题时读取
任务卡/ -> 已进入近期调度或转为非 ready 的活跃任务唯一事实；选中 ID 或发生状态事件时读取
任务列表/ -> 未完成 backlog 与任务卡短投影；空队列、拆任务、解阻塞判断时读取
自动工作流恢复规则.txt -> decision / interruption 恢复细则；Show 返回 existing recovery 时由 Recovery route 读取，或普通责任方实际到达新的用户决定事件时只即时读取“创建决定恢复”
```

Replace the obsolete runtime wording `pending resume` with the existing schema-3 fact: runtime stores only the lease, conditional recovery pointer, blocking fingerprint/count/pause flag and last result.

Replace the fixed-depth maintenance rule with these exact semantics:

```text
1. 队列只在新任务/切片进入、解除阻塞、解冻、优先级/主责/授权/固定顺序变化、完成、阻塞、待决定、等待回复、复审转换、取消或队列为空时维护。
2. 新任务或重新 ready 的任务一次性按“用户明确顺序 → P 优先级 → 高优先级下游解锁量 → 等待时间 → 稳定 ID”插入固定位置；普通轮次不重算顺序。
3. 明确逻辑阻塞立即写入任务卡和 backlog，并从 ready 队列移除；执行器暂不可用或实时路径冲突只是本轮临时跳过，不改卡、不改顺序。
4. 队列为空时只提升或建立当前能够安全形成的 ready 卡并排序、提交；本轮不执行新任务。没有合法来源时不制造任务，也不制造无事实变化提交。
```

Replace the old queue-card field description with:

```text
当前队列只保存 ID、route、owner、priority、domain、stage、title 和 task-card path；独立卡头是结构化字段唯一事实源，正文保存来源、必查范围、实施范围、禁止项、验证、完成条件和停止条件。
```

Document exact card enums and these state locations:

```text
ready -> active card + backlog + ordered queue
blocked/frozen/pending_decision/waiting_reply -> active card + backlog, never queue
completed -> card moved to 任务归档 and removed from queue/backlog
```

Add the checker trigger:

```text
只在创建/修改任务卡、改变任务状态、改变队列、队列补位或提交相关管理文件前运行 tools/check-task-cards.ps1；普通每小时轮次不运行全量检查器。
```

- [ ] **Step 3: Route `AGENTS.md` and pure `1` / `2` through the ordered index**

In `AGENTS.md`:

- identify `开发管理/任务卡/<ID>.txt` as the active task authority;
- change pure `1` to select the first current-AI `codex_execute` / `external_execute` row in queue order and skip `codex_review`;
- change pure `2` to select the first `codex_review` row in queue order, then read `审核入口.txt`;
- state that an empty queue alone routes to backlog maintenance;
- state that normal dispatch reads `自动工作流规则.txt`; a non-null recovery returned by `Show` routes Recovery to `自动工作流恢复规则.txt`, while a normal responsibility reads only its `创建决定恢复` section if and when it actually reaches a new user-decision event;
- preserve the manual-vs-automated worktree distinction already added after commit `f02a808`.

In `AI协作规则.txt`, replace the old independent priority scans with:

```text
1. 纯 1 从队首向后选择当前 AI 可执行的第一项 codex_execute 或 external_execute；不消费 codex_review。
2. 纯 2 从队首向后选择第一项 codex_review；DeepSeek / Claude 不得自审。
3. 路径冲突或执行器暂不可用时可在本轮继续检查下一行，但不修改顺序；明确逻辑阻塞必须先转状态并移出队列。
4. 队列为空时才读取分线 backlog 进行一次维护，本轮不执行新任务。
```

Keep title setting, identity checking, minimal reads, validation cost control, finalizer, lease and role restrictions unchanged.
Replace its claim that one file contains all recovery details with the same two-condition route: normal dispatch reads `自动工作流规则.txt`; `Show` returning existing recovery routes Recovery to `自动工作流恢复规则.txt`; a normal responsibility additionally reads only `创建决定恢复` if and when it actually reaches a new user-decision event.

- [ ] **Step 4: Make review and external handoff use the same card**

In `审核入口.txt`:

- state that actionable review selection comes from queue route `codex_review`;
- keep `AI合作沟通.txt` as evidence, not a second schedulable pool;
- on review pass, archive the same task card and remove its queue/backlog projection;
- on review failure, set the same card to `blocked`, remove it from queue and write the existing not-passed entry.

In `DeepSeek工作提示词.txt`:

- accept only a selected `external_execute` card;
- after successful business implementation and validation, update that same card to:

  ```text
  route=codex_review
  owner=codex
  dispatchState=ready
  ```

- update the same queue row in place and keep the same ID/title/body;
- create the existing `businessCommit` with `State: pending_review`;
- then create the handoff-only commit exactly as today;
- do not create a second review card or self-review.

In `AI协作规则.txt`, make the same external transition explicit so the wrapper rule is not a second authority.

- [ ] **Step 5: Extend the approved future path mapping without performing the migration**

Add these source/target mappings:

```text
开发管理/自动工作流恢复规则.txt -> 开发管理/规则/自动工作流恢复规则.txt
开发管理/任务卡/ -> 开发管理/当前/任务卡/
开发管理/任务卡/N-GROUP-02C.txt -> 开发管理/当前/任务卡/N-GROUP-02C.txt
开发管理/任务卡/N-SLOT-01.txt -> 开发管理/当前/任务卡/N-SLOT-01.txt
开发管理/任务卡/U-2_5D-01D.txt -> 开发管理/当前/任务卡/U-2_5D-01D.txt
```

Add `tools/check-task-cards.ps1` and `tools/test-check-task-cards.ps1` to the direct-reference impact table. State that a future M-MGMT-01 migration must change queue, card root, backlog root and checker fixtures in one slice with no old-path fallback.

Do not move any management directory in this plan.

- [ ] **Step 6: Verify the management contract has one selection source**

Run:

```powershell
$stateRules = Get-Content -LiteralPath '开发管理/状态与建议维护规则.txt' -Raw
$aiRules = Get-Content -LiteralPath '开发管理/AI协作规则.txt' -Raw
$audit = Get-Content -LiteralPath '开发管理/审核入口.txt' -Raw
$external = Get-Content -LiteralPath '开发管理/DeepSeek工作提示词.txt' -Raw
$agents = Get-Content -LiteralPath 'AGENTS.md' -Raw

foreach ($token in @(
  '开发管理/任务卡/<ID>.txt',
  'codex_execute',
  'external_execute',
  'codex_review',
  '队列为空',
  '本轮不执行新任务',
  'tools/check-task-cards.ps1'
)) {
  if (-not ($stateRules + $aiRules + $agents).Contains($token)) {
    throw "Missing ordered-queue contract: $token"
  }
}

foreach ($token in @('同一任务卡', 'route=codex_review', 'owner=codex', 'dispatchState=ready')) {
  if (-not ($external + $aiRules + $audit).Contains($token)) {
    throw "Missing same-card review transition: $token"
  }
}

foreach ($legacy in @('至少包含 2 张合法可执行任务卡', '单次最多新增 3 张', '汇总执行、复审和外部 AI 合法候选后统一排序')) {
  if (($stateRules + $aiRules + $agents).Contains($legacy)) {
    throw "Legacy per-round queue contract remains: $legacy"
  }
}
```

Expected: exit zero.

- [ ] **Step 7: Run focused checks and commit the management rules**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 `
  -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 `
  -ExpectedPaths 'AGENTS.md|开发管理/状态与建议维护规则.txt|开发管理/AI协作规则.txt|开发管理/审核入口.txt|开发管理/DeepSeek工作提示词.txt|开发管理/管理目录唯一旧新路径映射与批准输入.txt'
git add -- `
  AGENTS.md `
  '开发管理/状态与建议维护规则.txt' `
  '开发管理/AI协作规则.txt' `
  '开发管理/审核入口.txt' `
  '开发管理/DeepSeek工作提示词.txt' `
  '开发管理/管理目录唯一旧新路径映射与批准输入.txt'
git diff --cached --check
git diff --cached --name-status
git commit -m "docs: define event driven task transitions"
```

Expected: staged set contains exactly the six files and the commit succeeds.

---

### Task 4: Split recovery details from normal dispatch and update the fixed invoker

**Files:**
- Create: `开发管理/自动工作流恢复规则.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`
- Modify: `tools/invoke-codex-responsibility.ps1`
- Modify: `tools/test-invoke-codex-responsibility.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `tools/test-check-automation-workflow.ps1`

**User-approved implementation decision A (2026-07-24):**

- The recovery-rule file has exactly two read conditions: `Show` returned existing recovery, or a normal responsibility actually reached a new user-decision event.
- Execution, Review and QueueMaintenance do not eagerly read the file and do not carry detailed decision/interruption protocol. Their prompt carries only a generic conditional instruction: on a real new decision event, read only `创建决定恢复`; without that event, do not read the file.
- Recovery routes continue to read the applicable existing-recovery rules normally.
- This correction additionally authorizes amendments to this plan and `docs/superpowers/specs/2026-07-24-event-driven-task-context-optimization-design.md`; it does not change other design scope.

**Interfaces:**
- `Show.recovery != null` -> Recovery reads the applicable rules in `自动工作流恢复规则.txt`.
- A normal responsibility actually reaches a new user-decision event -> just-in-time read of `创建决定恢复` only.
- No existing recovery and no new decision event -> do not read recovery details.
- `Execution`, `Review`, `QueueMaintenance`, `Recovery` remain the only invoker routes.
- Decision recovery remains `Start`; interruption recovery alone may `Resume`.
- Custom decision text remains explicit UTF-8 stdin.

- [ ] **Step 1: Change the invoker tests first**

In `tools/test-invoke-codex-responsibility.ps1`, cover Execution, Review and QueueMaintenance with a shared normal-route assertion:

```powershell
Assert-True `
  -Condition $transportedPrompt.Contains('开发管理/自动工作流恢复规则.txt') `
  -Message 'Normal route did not carry the conditional recovery-rule route'
Assert-True `
  -Condition $transportedPrompt.Contains('创建决定恢复') `
  -Message 'Normal route did not name the just-in-time decision section'
Assert-True `
  -Condition $transportedPrompt.Contains('实际到达新的用户决定事件') `
  -Message 'Normal route did not require an actual decision event'
Assert-True `
  -Condition $transportedPrompt.Contains('未到达决定事件时不得读取该文件') `
  -Message 'Normal route did not prohibit eager recovery-rule reads'
```

For each normal-route prompt, reject detailed protocol tokens:

```powershell
send-decision.mjs
PROVIDER_ACCEPTED
SaveRecovery
consume-reply.mjs
-Action Start -Route Recovery
Resume 原 session
```

After fresh decision recovery and interruption recovery, respectively:

```powershell
Assert-True `
  -Condition $decisionPrompt.Contains('开发管理/自动工作流恢复规则.txt') `
  -Message 'Recovery route did not load recovery rules'
Assert-True `
  -Condition $decisionPrompt.Contains('这是新的 CLI-native 责任方会话。') `
  -Message 'Decision recovery did not Start a fresh session'
Assert-True `
  -Condition $interruptionPrompt.Contains('这是原 CLI session 的续跑，不创建新责任方。') `
  -Message 'Interruption recovery did not Resume the original session'
```

Keep the existing exact Chinese stdin assertion:

```powershell
$customDecision = '保持原任务边界，不新增兼容分支'
```

and the fixed-root/worktree assertion unchanged.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
```

Expected: nonzero because the current common responsibility prompt still embeds detailed `send-decision.mjs` / `PROVIDER_ACCEPTED` / `SaveRecovery` protocol and does not carry the approved conditional `创建决定恢复` instruction.

- [ ] **Step 2: Change workflow checker fixtures and negative tests before production rules**

In `tools/test-check-automation-workflow.ps1`:

- add `$recoveryRulesPath`;
- add canonical fixture `开发管理/自动工作流恢复规则.txt`;
- move all decision/interruption detail tokens from `$canonicalPrompt` and `$canonicalRules` into `$canonicalRecoveryRules`;
- replace queue-depth fixtures and negative tests with ordered-queue/event tests;
- keep prompt drift, wait-cell, timeout, external closeout, identity, runtime schema and retired-token tests.

The canonical prompt fixture must contain:

```text
Show before any queue source
controller prompt routes 自动工作流恢复规则.txt only when Show returned existing recovery; normal responsibility JIT decision creation is outside the controller prompt
当前任务队列.txt when no recovery
按行顺序
第一项当前可安全执行
QueueMaintenance only when queue empty
Execution / Review route mapping
temporary conflict skips this round without reordering
functions.wait and the existing timeout contract
external two-commit closeout
```

The canonical core-rule fixture must contain:

```text
dispatchState=ready
codex_execute
external_execute
codex_review
临时运行条件
同一稳定 fingerprint 连续两轮
明确任务阻塞
事件发生时
队列为空
本轮不执行新任务
固定 RepositoryRoot / current branch / no worktree
```

The canonical recovery fixture must contain:

```text
只有两个读取条件
Show.recovery != null
普通责任方实际到达新的用户决定事件
创建决定恢复
PROVIDER_ACCEPTED
SaveRecovery
decision recovery
consume-reply.mjs
Acquire -ResumeRecovery
-Action Start -Route Recovery
interruption recovery
Resume 原 session
RECOVERY_ONLY
UTF-8
```

Add negative tests proving:

1. prompt reads queue before `Show`;
2. prompt omits the recovery-rule route;
3. core rules reintroduce per-round unified sorting;
4. core rules omit fixed queue order;
5. core rules omit temporary skip without reorder;
6. core rules omit same-fingerprint two-round pause;
7. core rules omit empty-queue maintenance-only behavior;
8. recovery detail leaks back into normal prompt/core;
9. recovery file loses fresh decision `Start` or interruption-only `Resume`;
10. fixed root/worktree prohibition disappears;
11. invoker fixture loses explicit UTF-8 stdin tokens;
12. recovery file loses the existing-recovery read condition;
13. recovery file loses the just-in-time new-decision condition;
14. recovery file loses the `创建决定恢复` section.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: nonzero until the checker and production contract are updated.

- [ ] **Step 3: Create the recovery-only rule file**

Move, without semantic change, the current decision and interruption rules from `自动工作流规则.txt` into `开发管理/自动工作流恢复规则.txt`.

Required sections:

```markdown
# 自动工作流恢复规则

## 读取条件与共同边界
## 创建决定恢复
## 决定恢复
## 中断恢复
## UTF-8 决定回复
## 失败关闭
```

The read contract must explicitly state that `Show.recovery != null` routes Recovery to the applicable rules, while a normal responsibility reads only `创建决定恢复` after it actually reaches a new user-decision event. Without either condition, the file is not read. Initial `send-decision.mjs` / `PROVIDER_ACCEPTED` / `SaveRecovery` creation protocol belongs inside `创建决定恢复`; reply consumption and fresh recovery Start remain in `决定恢复`.

The UTF-8 section must explicitly preserve:

```text
自定义回复由 tools/invoke-codex-responsibility.ps1 使用
[IO.StreamReader] + [Console]::OpenStandardInput() +
[Text.UTF8Encoding]::new($false) 读取。
测试驱动器以 StandardInputEncoding=UTF-8 写入。
不得改为系统代码页或普通 [Console]::In.ReadToEnd()。
```

Do not move pause, lease, normal blocking fingerprint, external two-commit closeout or fixed-root rules into this file.

- [ ] **Step 4: Slim the normal workflow rules to event-driven dispatch**

Keep these existing capabilities in `自动工作流规则.txt`:

- facts and live-config authority;
- `Show`, logical pause and lease semantics;
- recovery priority with a link to the new recovery file;
- one responsibility per round;
- fixed queue order and route mapping;
- current executor availability and current path-conflict checks;
- temporary skip without queue mutation;
- same-fingerprint two-round logical pause;
- explicit state transition for logical blockers;
- fixed invoker, timeout, deferred wait;
- fixed `RepositoryRoot` current branch and no automated worktree/branch;
- workspace guard, finalizer and metadata;
- external business/handoff two-commit closeout;
- failure preservation and no destructive Git recovery.

Remove:

- per-round aggregation of execution/review/external candidates;
- per-round unified sorting;
- fixed queue depth;
- ordinary reads of review/communication/backlog sources while queue is nonempty;
- duplicated decision/interruption protocol details.

Normal routing must say:

```text
1. Read current short queue.
2. Walk rows in stored order.
3. Verify row/card ready projection, route/owner, executor availability and live path conflict.
4. Select the first currently safe row.
5. Temporary runtime conflict: continue to the next row for this round, without changing card or order.
6. Explicit task blocker or projection mismatch: stop business execution and perform the state correction event.
7. Empty queue: invoke QueueMaintenance once; do not execute a newly added task in the same round.
```

- [ ] **Step 5: Replace the canonical controller prompt with the thin route**

The prompt must:

1. call `Show` before reading any project routing source;
2. stop on logical pause;
3. at the controller layer, read `自动工作流恢复规则.txt` only when `Show` returned existing recovery; normal responsibilities use the separate just-in-time `创建决定恢复` condition described below;
4. otherwise read `自动工作流规则.txt` and `当前任务队列.txt`;
5. walk fixed row order and choose the first safe row;
6. map `codex_execute -> Execution`, `codex_review -> Review`, empty queue -> `QueueMaintenance`, recovery -> `Recovery`, external route -> existing wrapper;
7. preserve timeout/wait-cell behavior and external closeout;
8. report only route, TaskId, category, sessionId, commitSha or recovery status;
9. avoid concrete task IDs, business paths, implementation or automation self-management.

Do not copy detailed decision/interruption steps back into the prompt.

- [ ] **Step 6: Keep Recovery routing and add normal just-in-time decision creation**

Change `Get-RouteInstruction` in `tools/invoke-codex-responsibility.ps1`:

```powershell
'Execution' {
  '按 开发管理/AI协作规则.txt 的纯 1 入口执行，但只处理本次指定 TaskId 和其独立任务卡。'
}
'Review' {
  '按 开发管理/审核入口.txt 与纯 2 入口复审，但只处理本次指定 TaskId 和其独立任务卡。'
}
'QueueMaintenance' {
  '按 开发管理/状态与建议维护规则.txt 维护空队列或状态事件；本轮不执行新增业务任务。'
}
'Recovery' {
  if (-not [string]::IsNullOrWhiteSpace($DecisionId)) {
    '读取 开发管理/自动工作流恢复规则.txt；这是带决定回复的新责任方会话，处理同一 TaskId，先核对 durable recovery 与决定，再继续工作。'
  } else {
    '读取 开发管理/自动工作流恢复规则.txt；这是中断恢复，恢复原责任方的同一 TaskId，先核对现有改动与 recovery，再继续未完成工作。'
  }
}
```

Replace the common unconditional detailed decision instruction with:

```text
Execution、Review、QueueMaintenance 责任方仅在实际到达新的用户决定事件时，才读取 开发管理/自动工作流恢复规则.txt 的“创建决定恢复”一节；未到达决定事件时不得读取该文件。
```

The common prompt must not embed `send-decision.mjs`, `PROVIDER_ACCEPTED`, `SaveRecovery`, `consume-reply.mjs` or Start-vs-Resume protocol. Recovery route wording above continues to load the applicable recovery rules normally.

Do not change the route ValidateSet, fixed root instruction, finalizer instruction, recovery acquisition checks or stdin reader.

- [ ] **Step 7: Update the workflow checker implementation**

In `tools/check-automation-workflow.ps1`:

- read `开发管理/自动工作流恢复规则.txt` separately;
- assert its two read conditions and `创建决定恢复` section, and assert that `PROVIDER_ACCEPTED` / `SaveRecovery` remain inside that section;
- assert normal prompt/core event-driven tokens;
- assert recovery tokens only in recovery rules;
- reject `consume-reply.mjs`, `PROVIDER_ACCEPTED`, `SaveRecovery` and detailed session-resume prose in normal prompt/core;
- replace queue-depth assertions with event/ordered-queue assertions;
- read `tools/invoke-codex-responsibility.ps1` and assert:

  ```text
  RepositoryRoot
  using-git-worktrees
  git worktree add
  IO.StreamReader
  Console]::OpenStandardInput
  Text.UTF8Encoding
  ```

- require `tools/check-task-cards.ps1` and `开发管理/自动工作流恢复规则.txt` to exist;
- preserve all existing live automation prompt equality, unique writer, active status, retired path/action, timeout, wait and external closeout checks.

- [ ] **Step 8: Run the focused automation GREEN suite**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1
```

Expected: all four commands exit zero. Do not run production `check-automation-workflow.ps1` yet: the live paused automation still intentionally contains the old installed prompt until Task 6.

- [ ] **Step 9: Commit the automation contract**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 `
  -Paths AGENTS.md,CLAUDE.md,开发管理
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 `
  -ExpectedPaths '开发管理/自动工作流恢复规则.txt|开发管理/自动工作流规则.txt|开发管理/自动工作流控制器提示词.txt|tools/invoke-codex-responsibility.ps1|tools/test-invoke-codex-responsibility.ps1|tools/check-automation-workflow.ps1|tools/test-check-automation-workflow.ps1'
git add -- `
  '开发管理/自动工作流恢复规则.txt' `
  '开发管理/自动工作流规则.txt' `
  '开发管理/自动工作流控制器提示词.txt' `
  tools/invoke-codex-responsibility.ps1 `
  tools/test-invoke-codex-responsibility.ps1 `
  tools/check-automation-workflow.ps1 `
  tools/test-check-automation-workflow.ps1
git diff --cached --check
git diff --cached --name-status
git commit -m "refactor: route automation through ordered task cards"
```

Expected: exactly seven paths are staged and the commit succeeds.

---

### Task 5: Include recovery rules and task cards in the PowerShell 7 policy

**Files:**
- Modify: `tools/check-pwsh-runtime.ps1`
- Modify: `tools/test-check-pwsh-runtime.ps1`

**Interfaces:**
- Default document scan includes the recovery rule and every active task-card `.txt`.
- `tools/check-task-cards.ps1` is required to declare PowerShell 7.
- Historical task archives remain excluded from default document discovery.

- [ ] **Step 1: Extend the runtime test fixture first**

In `tools/test-check-pwsh-runtime.ps1`:

- add `开发管理/自动工作流恢复规则.txt` to `$defaultDocumentPaths`;
- add `tools/check-task-cards.ps1` to `$defaultRequiredVersionPaths`;
- add a fixture file `开发管理/任务卡/T-READY-01.txt`;
- assert the clean fixture passes;
- replace that card content with a noncanonical PowerShell command and assert:

  ```text
  PW7_NONCANONICAL_PWSH_COMMAND 开发管理/任务卡/T-READY-01.txt:1
  ```

- restore the card and prove a file under `开发管理/任务归档/` is still not discovered.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
```

Expected: nonzero because the production checker does not yet discover the new recovery/card paths.

- [ ] **Step 2: Update the default document and version lists**

In `tools/check-pwsh-runtime.ps1`:

```powershell
$defaultDocuments += '开发管理/自动工作流恢复规则.txt'
$defaultRequiredVersions += 'tools/check-task-cards.ps1'
```

After the existing dynamic `任务列表` discovery, add parallel active-card discovery:

```powershell
$taskCardRoot = Join-Path $root '开发管理/任务卡'
if (Test-Path -LiteralPath $taskCardRoot -PathType Container) {
  $defaultDocuments += @(
    Get-ChildItem -LiteralPath $taskCardRoot -Filter '*.txt' -File |
      Sort-Object -Property FullName |
      ForEach-Object {
        [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
      }
  )
}
```

Do not recurse into `开发管理/任务归档/`.

- [ ] **Step 3: Run the runtime GREEN suite**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1
```

Expected: both commands exit zero.

- [ ] **Step 4: Commit the runtime policy**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 `
  -ExpectedPaths 'tools/check-pwsh-runtime.ps1|tools/test-check-pwsh-runtime.ps1'
git add -- tools/check-pwsh-runtime.ps1 tools/test-check-pwsh-runtime.ps1
git diff --cached --check
git diff --cached --name-status
git commit -m "tools: scan active task cards in pwsh policy"
```

Expected: staged set contains exactly the two runtime-check files and the commit succeeds.

---

### Task 6: Update the installed automation, verify the complete slice, and resume

**Files:**
- No repository file changes expected.
- Installed configuration: `tzg-hourly-controller`, updated only through Codex automation management.

**Interfaces:**
- Installed prompt equals `开发管理/自动工作流控制器提示词.txt`.
- Controller is the unique active writer after rollout.
- Lease and recovery remain empty at handoff.

- [ ] **Step 1: Reconfirm the rollout is still safe**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git status --short --branch
git log -5 --oneline
```

Expected: automation remains paused; lease and recovery are null; repository worktree is clean; the five implementation commits are visible.

- [ ] **Step 2: Update the installed prompt while keeping the controller paused**

Read the complete UTF-8 content of:

```text
开发管理/自动工作流控制器提示词.txt
```

Use Codex automation management to update the existing `tzg-hourly-controller` with that prompt and `status=PAUSED`, preserving every live field recorded in Production Preflight.

Do not edit TOML and do not create a second automation.

- [ ] **Step 3: Run the full minimal sufficient verification once**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-invoke-codex-responsibility.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pwsh-runtime.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 `
  -Paths AGENTS.md,CLAUDE.md,开发管理
git diff --check
git status --short
```

Expected:

- all tests/checks exit zero;
- installed prompt matches canonical while paused;
- queue/card/backlog projections are consistent;
- fixed-root and UTF-8 recovery regressions pass;
- Git worktree is clean.

Do not run Unity, BattleSim or `check-data-chain.ps1`; their inputs did not change.

- [ ] **Step 4: Resume the same automation through the automation API**

Use Codex automation management to update `tzg-hourly-controller` with the same complete fields and canonical prompt, changing only:

```text
status=ACTIVE
```

If the update fails or any field cannot be preserved, leave it paused and report the exact mismatch. Do not repair by editing TOML.

- [ ] **Step 5: Verify active production state**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireActive
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git status --short --branch
```

Expected:

- `tzg-hourly-controller` is the unique active writer;
- installed and canonical prompts match;
- `leaseStatus=none`, `recovery=null`;
- worktree is clean.

- [ ] **Step 6: Final acceptance audit**

Confirm:

```text
- 当前队列小于 2,500 字符，只含 ready 工作；
- 普通队首可执行时，路由只需短队列 + 一张任务卡；
- 临时路径/执行器冲突只在本轮跳过，不重排；
- 明确阻塞从队列移除但仍能从 backlog + card 读取；
- 空队列只维护，不在同轮执行；
- external_execute 完成后同卡转 codex_review；
- recovery 细节只在两个批准条件下读取：existing recovery 由 Recovery route 读取对应规则，普通责任方实际到达新决定事件时只即时读取 `创建决定恢复`；
- 自动 Codex 仍固定在 invoker RepositoryRoot/current HEAD；
- UTF-8 自定义决定回复回归通过；
- 没有第二队列、数据库、缓存、求解器、自动生成器或兼容解析；
- 没有 Unity、BattleSim、CSV、asset 或游戏事实修改。
```

If any item fails, pause the controller through automation management and stop. Preserve Git commits, runtime and evidence; do not reset, revert, clean or invent a fallback.
