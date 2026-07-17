# 每小时自动工作流编排层重建 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改写飞书通信链路和 Git 安全底座的前提下，旁路重建可确定执行、可恢复、不会漏范围的每小时控制器 v2，并在首次 TQ-057 生产写入前交付一份由负责人明确批准的只读工作清单。

**Architecture:** `tools/hourly-controller-v2/` 是唯一新编排入口；版本控制中的 JSON 注册表定义可执行任务，受限发现网关产生证据，清单验证器把决策范围映射到完整路径，schema v1 状态机控制阶段，飞书适配器和 Git 安全脚本作为外部黑盒。新旧控制器全程旁路隔离，生产自动化只在离线测试和真实 `plan-only` 金丝雀通过后切换。

**Tech Stack:** PowerShell 7、Git、JSON schema-style validation、Node.js `>=20`、现有 `@larksuiteoapi/node-sdk@1.71.1` 飞书桥接、Codex 任务标题工具、Unity 6 EditMode 运行器。

## Global Constraints

- 权威设计是 `docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md`；本计划只拆解该设计，不允许重新设计。
- 只在独立 linked worktree 和 `codex/hourly-controller-v2-rebuild` 分支实施。不得在当前主工作区修改代码，不得触碰用户已有的远程附件、两份 2026-07-15 金丹规格、`.agents/summary_state.json` 或 `设计总结.txt` 改动。
- 建设期间 `tzg-hourly-controller` 必须保持 `PAUSED`；不得编辑任何私有 automation TOML，不得同时启用旧控制器和 v2 写入能力。
- 保留且不重写 `tools/feishu-decision-bridge/` 的消息协议、飞书配置脚本、`tools/private-path-acl.ps1`、`tools/automation-workspace-guard.ps1`、`tools/automation-finalize-commit.ps1` 与 `tools/check-pending-whitespace.ps1`。只允许按批准规格窄改飞书安装/启动宿主，解决登录可见窗口并增加生命周期动作。
- 所有私有状态、运行请求、baseline、证据和决策收件箱都位于 `%USERPROFILE%\.codex\automation-state\`，不得提交 Git；所有新建私有目录/文件复用 `private-path-acl.ps1` 收紧 ACL。
- 每个实现切片遵循 TDD：先补直接失败测试，确认失败原因，再写最小实现，只跑该切片直接测试。完成控制器集成后合并运行一次 v2 套件；最终切换前再对保留组件运行一次相关回归，不做重复全量验证。
- 每个提交前只对本提交路径运行 `tools/check-pending-whitespace.ps1`，再 `git add -- <paths>`，再运行一次 `git diff --cached --check`。不得暂存路径外文件。
- 所有独立 PowerShell 进程必须使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`。
- 发现阶段不接受任意 Shell 字符串；只允许 `DiscoverRead`、`DiscoverSearch`、`DiscoverList`、`DiscoverCheck` 四个固定动作。
- TQ-057 的五项已批准事实必须完整迁移，不能重问、改选、缩成 A/B 字母或泄露 provider/tenant/open_id/chat_id/message_id/event_id/证据哈希。
- 首次真实 TQ-057 写入必须等待负责人批准 `plan-only` 清单；批准前控制器没有进入 `MUTATING` 的路径。
- 任一事实源与本计划冲突时停止当前任务并向负责人报告；不得用旧控制器行为补齐含义。

## Validation Budget

本计划的验证预算固定如下，执行者不得自行叠加重复全量检查：

1. Task 1–8：每个任务只运行该模块的一个直接测试文件；Task 8 只运行安装器测试，不重复飞书 Node 套件。
2. Task 9：运行一次 `tools/hourly-controller-v2/tests/run-tests.ps1`，覆盖新编排层集成。
3. Task 10：只运行一次新的静态工作流契约检查，不重复 v2 模块套件。
4. Task 11：切换前运行一次保留组件回归、一次真实只读金丝雀和一次真实无窗口宿主检查；不得运行 Unity，因为此阶段没有改 Unity 业务数据。
5. 首次 TQ-057 写入时，才按注册表运行 `data-chain`、相关 Unity EditMode、pending whitespace 和 cached diff check；同一输入未变化时不重复。

## Stable Paths

```text
Project registry:
  开发管理/自动工作流任务注册表.json

New implementation:
  tools/hourly-controller-v2/

New local state:
  %USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2.json

Per-run private root:
  %USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2-runs/<runId>/

Legacy state (read-only migration source):
  %USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json

Legacy backups:
  %USERPROFILE%/.codex/automation-state/tzg-hourly-controller.v8.pre-v2.<yyyyMMdd-HHmmss>.json
```

## Stable Controller Contract

唯一外部入口：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/controller.ps1 `
  -Action <Action> `
  -RepositoryRoot <absolute-project-root> `
  -StatePath <absolute-private-state-json> `
  -RequestPath <absolute-private-request-json>
```

`-Action` 固定为：

```text
Start
RecordTitleResult
DiscoverRead
DiscoverSearch
DiscoverList
DiscoverCheck
SubmitManifest
BeginMutation
Finish
Abort
CreateDecision
SendDecision
ConsumeDecision
MigrateLegacy
Show
```

每次调用 stdout 只能有一行 UTF-8 JSON。所有响应始终具有这些字段，即使值为空：

```json
{
  "schemaVersion": 1,
  "ok": true,
  "action": "Start",
  "runId": "00000000-0000-0000-0000-000000000000",
  "taskId": "TQ-057",
  "phase": "DISCOVERING",
  "nextAction": "RecordTitleResult",
  "errorCode": null,
  "changedPaths": [],
  "requiredSources": [],
  "requiredChecks": [],
  "decisionConstraints": [],
  "result": {}
}
```

稳定错误码只允许：

```text
invalid_request
invalid_state
metadata_missing
thread_id_mismatch
registry_invalid
task_not_found
task_not_executable
discovery_denied
discovery_incomplete
source_changed
manifest_invalid
decision_coverage_incomplete
baseline_changed
head_changed
path_outside_scope
check_failed
decision_invalid
feishu_unavailable
migration_invalid
internal_error
```

标题失败不是控制器失败：`RecordTitleResult` 写入 `result.titleStatus = "FAILED"` 和脱敏摘要，随后继续返回 `nextAction = "DiscoverRead"`。

## Common Commit Procedure

每个任务最后的“提交”都指以下固定流程；`<paths>` 只能替换为该任务 File Map 中的路径：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '<path1>|<path2>'
git add -- <paths>
git diff --cached --check
git commit -m '<本任务指定提交信息>'
```

---

## Task 0: 建立隔离执行环境并冻结私有证据

**Files:**

- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md`
- Read: `docs/superpowers/plans/2026-07-17-hourly-controller-orchestration-rebuild-implementation.md`
- Read: `开发管理/自动工作流规则.txt`
- Read: `开发管理/自动工作流状态.txt`
- Read: `开发管理/当前任务队列.txt`
- Read: `开发管理/AI协作规则.txt`
- Private create: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.v8.pre-v2.<timestamp>.json`
- Private create: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2-freeze-evidence.json`

- [ ] **Step 1: 确认主工作区状态并建立 worktree**

在 `D:\天章游戏开发` 运行：

```powershell
git status --short
git worktree add -b codex/hourly-controller-v2-rebuild 'D:\天章游戏开发-worktrees\hourly-controller-v2' master
git -C 'D:\天章游戏开发-worktrees\hourly-controller-v2' log -2 --oneline
```

Expected: 新 worktree 干净；最近提交同时包含设计和本实施计划。若目标目录或分支已存在，先检查其归属，不得删除或覆盖。

- [ ] **Step 2: 确认生产控制器暂停**

通过 Codex 自动化管理工具或界面读取 `tzg-hourly-controller`，确认状态为 `PAUSED`。若不能证明暂停，停止本计划；不得通过编辑私有 TOML 代替。

- [ ] **Step 3: 备份旧状态并记录脱敏冻结证据**

复制旧 schema v8 状态到带时间戳的私有备份，设置只读属性，计算 SHA-256。冻结证据只写：旧 schema、旧 phase、是否有 pending decision、备份文件名、备份 SHA-256、当前 Git HEAD、自动化暂停状态和飞书健康枚举；不得写任何原始身份/消息标识。

- [ ] **Step 4: 保持分支无项目改动**

```powershell
git status --short
```

Expected: 干净。本任务不创建 Git 提交。

---

## Task 1: 建立测试入口与稳定协议

**Files:**

- Create: `tools/hourly-controller-v2/protocol.psm1`
- Create: `tools/hourly-controller-v2/tests/test-helpers.ps1`
- Create: `tools/hourly-controller-v2/tests/protocol.tests.ps1`
- Create: `tools/hourly-controller-v2/tests/run-tests.ps1`

**Interfaces:**

- `Read-ControllerRequest -Path <absolute>`：只接受 UTF-8 JSON object、`schemaVersion: 1`、私有状态根内绝对路径。
- `New-ControllerResponse`：总是产生 Stable Controller Contract 的 12 个固定字段。
- `Write-ControllerResponse`：stdout 单行压缩 JSON；日志写 stderr；递归脱敏禁止字段。
- `Normalize-ProjectPath`：拒绝绝对路径、`..`、`.` 段、空段、反斜杠歧义、仓库逃逸和符号链接逃逸，返回 `/` 分隔项目相对路径。
- `Get-Sha256Text`：UTF-8 无 BOM 字节的 lowercase SHA-256。

- [ ] **Step 1: 写失败的协议测试**

测试必须覆盖：固定字段齐全、未知错误码被拒绝、stdout 只有一行、禁止字段在嵌套对象中被替换为 `[REDACTED]`、非法相对路径拒绝、合法中文/空格路径保留。禁止字段固定为：

```text
appSecret tenantKey openId chatId messageId eventId providerMessageId providerEventId evidenceHash rawEvent
```

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/protocol.tests.ps1
```

Expected: 非零，原因是 `protocol.psm1` 尚未提供所需导出函数，而不是测试语法错误。

- [ ] **Step 3: 实现最小协议模块和测试入口**

`run-tests.ps1` 只按文件名排序执行 `*.tests.ps1`，任一失败立即非零；成功输出一行 `hourly-controller-v2-tests: OK`。不得扫描 v2 目录外测试。

- [ ] **Step 4: 运行直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/protocol.tests.ps1
```

Expected: `protocol.tests: OK`。

Commit: `feat(automation-v2): define stable controller protocol`

---

## Task 2: 建立机器任务注册表与选择规则

**Files:**

- Create: `开发管理/自动工作流任务注册表.json`
- Create: `tools/hourly-controller-v2/registry.psm1`
- Create: `tools/hourly-controller-v2/tests/registry.tests.ps1`

**Registry schema:**

根对象固定为 `schemaVersion`、`tasks`。每个任务固定包含：

```text
taskId title priority owner executor status dependencies dependencyEvidence executionEnabled
requiredSources allowedRoots discoveryChecks requiredChecks
completionEvidence decisionIds coverageRules
```

当前注册表必须列出 Markdown 队列表格中的五个 ID：`TQ-057`、`TQ-059`、`TQ-069`、`N-SLOT-01`、`N-DIST-01`。只有 `TQ-057.executionEnabled = true`；其他四个先设为 `false`，防止控制器执行尚未建立范围契约的任务。状态、主责、依赖与当前队列表格逐字一致；任务卡中的显示别名不作为机器 owner 枚举来源。

TQ-057 最低契约固定为：

```json
{
  "taskId": "TQ-057",
  "title": "D-TRUST-02：清理现存数据矛盾",
  "priority": "P0",
  "owner": "Codex / gpt-5.5",
  "executor": "codex",
  "status": "待处理",
  "dependencies": ["TQ-056"],
  "dependencyEvidence": [{
    "taskId": "TQ-056",
    "status": "completed",
    "source": "开发管理/当前任务队列.txt",
    "match": "依赖：TQ-056 已由 Codex 完成并验证"
  }],
  "executionEnabled": true,
  "requiredSources": [
    "开发管理/当前任务队列.txt",
    "开发管理/自动工作流状态.txt",
    "开发管理/开发-技术经验.txt",
    "docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md"
  ],
  "allowedRoots": [
    "src/Assets/DataConfig",
    "src/Assets/Data/Spells",
    "src/Assets/Data/GongFa",
    "src/Assets/Scripts/Combat",
    "src/Assets/Scripts/Editor",
    "src/Assets/Tests/EditMode",
    "docs/角色养成/术法",
    "docs/角色养成/功法",
    "开发管理"
  ],
  "discoveryChecks": ["data-chain-readonly"],
  "requiredChecks": ["data-chain", "unity-editmode-related", "pending-whitespace", "cached-diff-check"],
  "decisionIds": [
    "DEC-20260715-35ACB87E6C10",
    "DEC-20260715-75D7BA2AF210",
    "DEC-20260714-29A5D1356CC8",
    "DEC-20260714-320075D033A5",
    "DEC-20260713-A07FA708DB22"
  ]
}
```

`coverageRules` 必须明确：双倍率决定强制覆盖 `Spells.csv`、`DataConfigImporter.cs`、`SpellData.cs`、`CombatResolver.cs`、至少一个 EditMode 测试文件，以及发现阶段列出的全部现存 `src/Assets/Data/Spells/*.asset`；其余四项决定分别覆盖其语言、CSV、文档和资产影响面。

- [ ] **Step 1: 写失败的注册表测试**

测试使用临时 Markdown fixture 和真实 JSON 注册表，断言：五个任务齐全；只有 TQ-057 可执行；状态/owner/dependencies 不一致时确定性失败；TQ-056 完成证据缺失或 source/match 不成立时 TQ-057 不可选；TQ-057 缺任一决定 ID、核心双倍率路径或 required check 时失败；重复 ID 和未知字段失败。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/registry.tests.ps1
```

Expected: 非零，原因是注册表或模块不存在。

- [ ] **Step 3: 实现注册表读取、交叉检查和确定性选题**

导出：

```powershell
Read-TaskRegistry -Path <absolute>
Assert-RegistryMatchesQueue -Registry <object> -QueuePath <absolute>
Select-ExecutableTask -Registry <object>
Get-TaskContract -Registry <object> -TaskId <id>
```

选择顺序固定为 priority 数字升序，再按 JSON 中出现顺序；阻塞、非 `待处理`、`executionEnabled=false`、`executor != codex` 或依赖完成证据未在其 source 中逐字命中的任务不可选。实际模型字符串只从本轮 metadata 记录，不拿显示用 owner 字符串做脆弱的模型名等值判断。

- [ ] **Step 4: 运行直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/registry.tests.ps1
```

Expected: `registry.tests: OK`。

Commit: `feat(automation-v2): add machine task registry`

---

## Task 3: 建立 schema v1 状态与幂等 v8 迁移

**Files:**

- Create: `tools/hourly-controller-v2/state.psm1`
- Create: `tools/hourly-controller-v2/tests/state.tests.ps1`
- Create: `tools/hourly-controller-v2/tests/fixtures/legacy-v8-tq057.json`
- Create: `tools/hourly-controller-v2/tests/fixtures/migrated-v1-tq057.expected.json`

**State schema:**

根字段固定为：

```text
schemaVersion controllerVersion phase activeRun decisionLedger migration
```

阶段只允许：

```text
IDLE DISCOVERING AUTHORIZED MUTATING VERIFYING COMMITTED
WAITING_DECISION IMPLEMENTATION_PENDING
```

`decisionLedger` 每项固定保存：`decisionId`、`taskId`、`question`、`resolutionKind`、`selectedOptionId`、`resolutionText`、`impactSummary`、`scopeContract`、`resolvedAt`、`source`、`migratedFrom`。不得保存任何 provider 私有标识。

迁移 fixture 必须用脱敏值完整表达五项 TQ-057 决定，包含完整中文正文和范围契约；expected fixture 的 JSON 属性顺序、数组顺序和换行固定，供字节级幂等比较。

- [ ] **Step 1: 写失败的状态/迁移测试**

断言：默认状态是 schema v1/IDLE；非法阶段迁移失败；状态写入使用临时文件后原子替换；重复导入同一 v8 fixture 两次得到与 expected fixture 字节一致；五个 decisionId 全部存在；第二项 A 的 scopeContract 包含四个核心代码/数据文件、EditMode tests 和全部 spell asset inventory 规则；输出不存在禁止字段。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/state.tests.ps1
```

Expected: 非零，原因是 `state.psm1` 不存在。

- [ ] **Step 3: 实现最小状态模块**

导出：

```powershell
New-ControllerState
Read-ControllerState -Path <absolute>
Write-ControllerStateAtomic -Path <absolute> -State <object>
Move-ControllerPhase -State <object> -From <phase[]> -To <phase>
Import-LegacyV8State -LegacyPath <absolute> -DestinationPath <absolute> -FixtureContract <object>
```

迁移只读旧文件，不修改或重命名旧状态；目标已存在且 migration source hash 相同时返回现有字节；hash 不同时以 `migration_invalid` 拒绝。

- [ ] **Step 4: 运行直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/state.tests.ps1
```

Expected: `state.tests: OK`。

Commit: `feat(automation-v2): add isolated state and legacy migration`

---

## Task 4: 建立受限只读发现网关

**Files:**

- Create: `tools/hourly-controller-v2/discovery.psm1`
- Create: `tools/hourly-controller-v2/tests/discovery.tests.ps1`

**Interfaces:**

```powershell
Invoke-DiscoverRead   -Context <run> -Path <project-relative>
Invoke-DiscoverSearch -Context <run> -Root <allowed-root> -Pattern <literal-or-regex> -Glob <optional-fixed-glob>
Invoke-DiscoverList   -Context <run> -Root <allowed-root> -Glob <fixed-glob>
Invoke-DiscoverCheck  -Context <run> -CheckId <registered-id>
```

固定限制：单次 read 最大 1 MiB；search 最多 500 条；list 最多 5000 条；所有结果使用项目相对 `/` 路径；拒绝 reparse point/symlink 逃逸；`DiscoverCheck` 只映射 `data-chain-readonly` 到 `tools/check-data-chain.ps1` 的只读执行，不能接受 command/arguments 字段。

每次成功动作追加私有 `discovery-log.jsonl`：`sequence`、`action`、规范化输入、source SHA-256、时间；不记录 provider 信息。失败动作也记录 errorCode，但不能伪造已满足 source/check。

- [ ] **Step 1: 写失败的发现测试**

临时仓库 fixture 覆盖：`requiredSources` 中的精确文件和 `allowedRoots` 下文件可以合法读取/搜索/列举；两者之外路径、`..`、绝对路径、未知检查、任意 command 字段、symlink/reparse 逃逸被拒绝；超过限额截断并标 `truncated=true`；日志 sequence 单调；直接在仓库调用 `Get-ChildItem` 不属于任何网关动作且不能生成发现证据。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/discovery.tests.ps1
```

Expected: 非零，原因是 `discovery.psm1` 不存在。

- [ ] **Step 3: 实现四个固定动作**

内部可使用 .NET 文件 API 和 `rg` 参数数组；禁止 `Invoke-Expression`、`ScriptBlock::Create`、`cmd /c` 和字符串拼接 Shell。

- [ ] **Step 4: 运行直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/discovery.tests.ps1
```

Expected: `discovery.tests: OK`。

Commit: `feat(automation-v2): add bounded discovery gateway`

---

## Task 5: 建立工作清单验证与决策覆盖

**Files:**

- Create: `tools/hourly-controller-v2/manifest.psm1`
- Create: `tools/hourly-controller-v2/tests/manifest.tests.ps1`
- Create: `tools/hourly-controller-v2/tests/fixtures/tq057-valid-manifest.json`
- Create: `tools/hourly-controller-v2/tests/fixtures/tq057-incomplete-manifest.json`

**Manifest schema:**

```json
{
  "schemaVersion": 1,
  "runId": "UUID",
  "taskId": "TQ-057",
  "model": "actual-model",
  "threadId": "UUID",
  "planOnly": true,
  "sourceEvidence": [{"path": "project/relative", "sha256": "64 lowercase hex"}],
  "decisionCoverage": [{
    "decisionId": "DEC-...",
    "resolutionText": "完整已批准正文",
    "paths": ["project/relative"],
    "implementation": "具体实施说明"
  }],
  "expectedPaths": ["project/relative"],
  "intendedChanges": [{"path": "project/relative", "operation": "create|modify|delete", "summary": "具体说明"}],
  "requiredChecks": ["registered-check-id"],
  "completionEvidence": ["registered evidence item"]
}
```

`tq057-valid-manifest.json` 使用 fixture 仓库中的 3 个 spell assets 代表动态 inventory；验证器必须比较 discovery log 的实际 inventory，而不是把 3 写死到生产规则。

- [ ] **Step 1: 写失败的清单测试**

有效 fixture 必须通过。以下每种变体必须以稳定 errorCode 拒绝：漏一个 required source；source hash 与发现时不同；漏一个决定；只写 A/B 不写完整 resolutionText；漏 `Spells.csv`、导入器、`SpellData`、`CombatResolver`、测试或任一已发现 spell asset；path 不在 allowedRoots；expectedPath 无 intendedChange；decision path 不在 expectedPaths；删除注册表 required check；使用未登记 check；baseline/HEAD 已变化。

baseline 失败断言必须包含具体 `changedPaths`，例如 `src/Assets/DataConfig/Spells.csv`，不能只返回 `baseline_changed`。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/manifest.tests.ps1
```

Expected: 非零，原因是 `manifest.psm1` 不存在。

- [ ] **Step 3: 实现最小清单验证器**

导出：

```powershell
Read-WorkManifest -Path <absolute-private-json>
Test-WorkManifest -Manifest <object> -TaskContract <object> -DecisionLedger <array> -DiscoveryLogPath <absolute> -BaselinePath <absolute>
```

最后一步调用保留的 workspace guard `Check`；解析其 JSON，把 `conflictingPaths` 与 `<HEAD>` 映射为稳定 `changedPaths`，不得吞掉原路径，也不得自动重拍 baseline。

- [ ] **Step 4: 运行直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/manifest.tests.ps1
```

Expected: `manifest.tests: OK`。

Commit: `feat(automation-v2): validate complete work manifests`

---

## Task 6: 修复标题元数据契约并隔离标题失败

**Files:**

- Create: `tools/hourly-controller-v2/title.psm1`
- Create: `tools/hourly-controller-v2/tests/title.tests.ps1`

**Interfaces:**

```powershell
New-TitleRequest -Model <string> -ThreadId <uuid> -MetadataThreadId <uuid> -TaskTitle <string>
Get-TitleToolPayload -TitleRequest <object>
Record-TitleResult -State <object> -Succeeded <bool> -Diagnostic <sanitized-string>
```

薄启动提示在后续 Task 10 中只读取：

```javascript
const meta = nodeRepl.requestMeta;
const turnMeta = meta && meta['x-codex-turn-metadata'];
nodeRepl.write(JSON.stringify({
  model: turnMeta && turnMeta.model,
  threadId: meta && meta.threadId,
  metadataThreadId: turnMeta && turnMeta.thread_id
}));
```

两个 thread ID 必须为非空 UUID 且逐字一致；严禁再读取不存在的 `meta.turn.thread_id` 或 `tzgTurn.turn.thread_id`。标题固定为 `TZG｜<注册表中文标题>`，不得让模型生成简述。

- [ ] **Step 1: 写失败的标题测试**

覆盖：两个真实字段一致时生成固定标题与 app tool payload；顶层缺失、metadata 缺失、格式错误、不一致时分别返回 `metadata_missing` 或 `thread_id_mismatch`；工具失败只记 `FAILED` 并保持控制器可继续发现；诊断经过脱敏。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/title.tests.ps1
```

Expected: 非零，原因是 `title.psm1` 不存在。

- [ ] **Step 3: 实现标题助手并运行直接测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/title.tests.ps1
```

Expected: `title.tests: OK`。

Commit: `fix(automation-v2): use actual thread metadata for titles`

---

## Task 7: 适配飞书决策账本与人工清单批准

**Files:**

- Create: `tools/hourly-controller-v2/decision-adapter.psm1`
- Create: `tools/hourly-controller-v2/tests/decision-adapter.tests.ps1`

**Interfaces:**

```powershell
New-DecisionRequest -TaskId <id> -Question <text> -Options <array-with-scope-contracts>
Send-DecisionRequest -Decision <object> -RunRoot <private> -BridgeRoot <absolute-preserved-bridge>
Consume-DecisionReply -Decision <object> -RunRoot <private> -BridgeRoot <absolute-preserved-bridge>
New-ManifestApprovalDecision -Manifest <object>
```

桥接调用只允许现有入口：

```text
node tools/feishu-decision-bridge/src/send-decision.mjs --request-file <absolute-private-json>
node tools/feishu-decision-bridge/src/consume-reply.mjs --request-file <absolute-private-json>
```

选项正文与 `scopeContract` 一起冻结；卡片按钮只显示 `选择 A/B/C`，完整说明留在正文，并展示可复制格式 `DEC-编号：自定义 <你的方案>`。自定义回复不能直接授权写入；它只让状态进入 `IMPLEMENTATION_PENDING`，新 manifest 必须再次经过清单批准。

- [ ] **Step 1: 写失败的适配器测试**

使用假的 Node bridge，不访问网络。覆盖：OPTION_ACCEPTED、CUSTOM_ACCEPTED、NO_REPLY；首个有效回复胜出；重复幂等；冲突不覆盖；选项 scopeContract 持久化；自定义转 `IMPLEMENTATION_PENDING`；manifest approval 的正文列出任务、路径摘要、五项决定覆盖、检查与复制回复格式；桥接被停止或禁用时稳定返回 `feishu_unavailable`、保留决定且不产生授权；禁止字段不进入项目可见输出。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/decision-adapter.tests.ps1
```

Expected: 非零，原因是 `decision-adapter.psm1` 不存在。

- [ ] **Step 3: 实现窄适配器并运行直接测试**

适配器只能翻译协议和持久化账本；不得修改 `tools/feishu-decision-bridge/` 内部文件。

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/decision-adapter.tests.ps1
```

Expected: `decision-adapter.tests: OK`。

Commit: `feat(automation-v2): adapt feishu decisions without bridge rewrite`

---

## Task 8: 修复飞书桥登录可见窗口并增加生命周期管理

**Regression source:** Codex 任务 `019f701a-32b2-70b2-a13a-26565ea9a6fb` 已证明当前登录任务以 interactive principal 直接启动长期 `pwsh.exe`；`Hidden=true` 与 `-WindowStyle Hidden` 仍不能保证登录时无控制台闪现。

**Files:**

- Create: `tools/start-feishu-decision-bridge-hidden.vbs`
- Modify: `tools/install-feishu-decision-bridge.ps1`
- Modify: `tools/test-install-feishu-decision-bridge.ps1`

**Interfaces:**

- `Get-TaskPlan` 输出增加 `launchMode = "WINDOWLESS_WSCRIPT"`，`execute` 为系统 `wscript.exe` 绝对路径，`arguments` 固定为 `//B //NoLogo "<hidden-launcher>" "<pwsh>" "<start-script>"`。
- `install-feishu-decision-bridge.ps1 -Action` 固定支持 `Plan|Install|Start|Stop|Enable|Disable|Status|Uninstall`。
- `Status` 在现有字段之外固定返回 `enabled: bool|null` 和 `launchMode: "WINDOWLESS_WSCRIPT"`；未安装时 `enabled = null`。
- `SchedulerAdapter` 保留现有键，并增加 `IsTaskEnabled`、`EnableTask`、`DisableTask`、`StartTask` 四个 scriptblock；测试 adapter 仍只允许临时配置路径。

**Lifecycle semantics:**

```text
Install  = validate config/runtime -> stop old task/processes -> upsert enabled WINDOWLESS_WSCRIPT task -> start
Start    = require installed and enabled -> idempotent start
Stop     = require installed -> stop scheduled task/process tree -> preserve enabled=true and all private state
Disable  = require installed -> stop -> disable -> preserve config/pairing/state
Enable   = require installed -> enable -> idempotent start
Status   = read only
Uninstall = stop/remove task -> preserve config/pairing/state
```

VBS 启动器必须恰好接收 PowerShell 7 和固定 start script 两个绝对路径参数；任一参数为空、含双引号/控制字符或脚本文件名不等于 `start-feishu-decision-bridge.ps1` 时以 64 退出。合法时使用 `WScript.Shell.Run(command, 0, True)`，命令固定包含 `-NoProfile -NonInteractive -ExecutionPolicy Bypass -File`，不读取或写入任何私有配置值。

- [ ] **Step 1: 扩展失败的安装器测试**

在现有 fake scheduler fixture 中增加 enabled/state 字段和四个 adapter 动作。新增断言：

- `Plan.execute` 是绝对 `wscript.exe`，不是 `pwsh.exe`；
- 参数含 `//B //NoLogo`、固定 VBS、PowerShell 7 和固定 start script，且不含配置路径或私有值；
- `Install` 原位升级旧 direct-pwsh task 后只剩一个 enabled/running task；
- `Stop`、`Disable`、`Enable`、`Start` 按 Lifecycle semantics 幂等执行；
- `Disable`/`Uninstall` 后私有配置和 state root 仍存在；
- 停止清理只命中固定 bridge 进程树，不结束无关 `pwsh.exe`/`node.exe`；
- `Status` 的 `enabled` 与 `launchMode` 正确；
- VBS 文本包含 `Run(command, 0, True)`，不含 secret、recipient、config path 或动态脚本名。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
```

Expected: 非零，首个失败原因是 `start-feishu-decision-bridge-hidden.vbs` 缺失或 Plan 仍直接执行 `pwsh.exe`。

- [ ] **Step 3: 实现固定无窗口启动器和生命周期动作**

`Get-TaskPlan` 必须用 `Get-Command wscript.exe` 与 `Get-Command pwsh` 解析绝对路径；继续拒绝含双引号的路径。真实 adapter 的 `Disable` 顺序必须是 StopTask → 清理受验证遗留 bridge process → DisableTask；不得用名称批量停止 PowerShell/Node。

`tools/start-feishu-decision-bridge-hidden.vbs` 的完整内容固定为：

```vbscript
Option Explicit

Const EXIT_INVALID = 64

Dim arguments, fileSystem, shell, pwshPath, startPath, command, exitCode
Set arguments = WScript.Arguments
If arguments.Count <> 2 Then WScript.Quit EXIT_INVALID

pwshPath = arguments(0)
startPath = arguments(1)
If Not IsSafeAbsoluteFile(pwshPath, "pwsh.exe") Then WScript.Quit EXIT_INVALID
If Not IsSafeAbsoluteFile(startPath, "start-feishu-decision-bridge.ps1") Then WScript.Quit EXIT_INVALID

command = QuoteArgument(pwshPath) & " -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " & QuoteArgument(startPath)
Set shell = CreateObject("WScript.Shell")
exitCode = shell.Run(command, 0, True)
WScript.Quit exitCode

Function IsSafeAbsoluteFile(value, expectedName)
  Dim index, code
  IsSafeAbsoluteFile = False
  If Len(value) = 0 Or InStr(value, Chr(34)) > 0 Then Exit Function
  For index = 1 To Len(value)
    code = AscW(Mid(value, index, 1))
    If code < 0 Then code = code + 65536
    If code < 32 Or code = 127 Then Exit Function
  Next
  Set fileSystem = CreateObject("Scripting.FileSystemObject")
  If Not fileSystem.FileExists(value) Then Exit Function
  If StrComp(fileSystem.GetAbsolutePathName(value), value, vbTextCompare) <> 0 Then Exit Function
  If StrComp(fileSystem.GetFileName(value), expectedName, vbTextCompare) <> 0 Then Exit Function
  IsSafeAbsoluteFile = True
End Function

Function QuoteArgument(value)
  QuoteArgument = Chr(34) & value & Chr(34)
End Function
```

- [ ] **Step 4: 只运行安装器直接测试并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-install-feishu-decision-bridge.ps1
```

Expected: `test-install-feishu-decision-bridge: OK`。本任务不运行 `npm test`，因为 Node 消息协议未改。

Commit: `fix(feishu): launch bridge without visible console`

---

## Task 9: 集成验证、路径限定提交与完整状态机

**Files:**

- Create: `tools/hourly-controller-v2/verification.psm1`
- Create: `tools/hourly-controller-v2/controller.ps1`
- Create: `tools/hourly-controller-v2/tests/controller.tests.ps1`

**Fixed check map:**

```text
data-chain
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-data-chain.ps1

unity-editmode-related
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1

pending-whitespace
  delegated once to tools/automation-finalize-commit.ps1 after it determines the actually changed expected files

cached-diff-check
  delegated once to tools/automation-finalize-commit.ps1 after its path-limited git add and before commit --only
```

`unity-editmode-related` 使用现有 runner 的相关程序集/fixture能力；若 runner 当前没有过滤参数，v2 不伪造过滤，而是调用一次现有 EditMode 套件并把结果记为该 check 的证据。

**State transitions:**

```text
Start: IDLE -> DISCOVERING
SubmitManifest(planOnly=true): DISCOVERING -> IMPLEMENTATION_PENDING
Manifest approval: IMPLEMENTATION_PENDING -> AUTHORIZED
BeginMutation: AUTHORIZED -> MUTATING
Finish: MUTATING -> VERIFYING -> COMMITTED -> IDLE
Decision needed: DISCOVERING -> WAITING_DECISION -> IMPLEMENTATION_PENDING
Abort: any non-COMMITTED active phase -> IDLE
```

- [ ] **Step 1: 写失败的控制器端到端测试**

使用临时 Git 仓库、临时状态、假标题工具结果、假飞书 bridge 和假检查映射，覆盖：

- 完整 `Start -> RecordTitleResult -> Discover* -> SubmitManifest(planOnly)` 只读路径；
- 未批准清单不能 `BeginMutation`；
- 批准后才能修改 fixture expectedPaths；
- `Finish` 先跑注册表检查，再调用保留 finalizer，提交仅包含实际变化的 expectedPaths；
- 路径外人工脏文件和预存 staged 文件保持原样且不进入提交；
- baseline/HEAD 变化返回精确 `changedPaths` 并回到 IDLE；
- check 失败不提交、不清理人工文件、记录脱敏摘要并回到 IDLE；
- 飞书宿主停止或禁用时，待决策分支返回 `feishu_unavailable`、保留决定并拒绝 `BeginMutation`；
- 任意阶段中断的 recoverable/unsafe 分类转译自保留 workspace guard；
- stdout 始终符合稳定协议且无私有标识。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/controller.tests.ps1
```

Expected: 非零，原因是 `controller.ps1`/`verification.psm1` 不存在。

- [ ] **Step 3: 实现验证器和唯一控制器入口**

`Finish` 的固定顺序：workspace guard `Check` → 执行非 delegated 的 requiredChecks → finalizer 内部各执行一次 pending whitespace 与 cached diff check → workspace guard `Verify` → 记录 commit SHA → `COMMITTED` → `IDLE`。控制器把 finalizer 成功作为两个 delegated check 的证据，不得在外部重复执行。任一步失败都不得自动 stash/reset/checkout/clean、不得重拍 baseline、不得创建部分提交。

- [ ] **Step 4: 运行直接控制器测试**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/controller.tests.ps1
```

Expected: `controller.tests: OK`。

- [ ] **Step 5: 合并运行一次 v2 套件**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/run-tests.ps1
```

Expected: 每个 v2 测试文件各运行一次，末行 `hourly-controller-v2-tests: OK`。

- [ ] **Step 6: 提交**

Commit: `feat(automation-v2): integrate guarded controller state machine`

---

## Task 10: 编写薄启动提示、v2 规则和契约检查

**Files:**

- Create: `开发管理/自动工作流控制器v2提示词.txt`
- Create: `开发管理/自动工作流v2规则.txt`
- Create: `tools/check-hourly-controller-v2.ps1`
- Create: `tools/hourly-controller-v2/tests/workflow-contract.tests.ps1`
- Modify: `开发管理/自动工作流状态.txt`

**Prompt constraints:**

提示词只允许做以下动作：读取一次真实元数据；调用 `Start`；按响应 `nextAction` 原样调用控制器；当 `nextAction=RecordTitleResult` 时把控制器给出的 payload 原样交给 `tools.codex_app__set_thread_title`；发现只能走四个 Discover 动作；生成 manifest request；输出控制器最终摘要。禁止在发现阶段调用 Shell，禁止自行解析 Markdown 选任务，禁止自行宣称检查通过，禁止改状态枚举，禁止编辑 automation TOML。

状态文档只新增 v2 建设摘要：旧生产控制器仍暂停、v2 未切换、TQ-057 五项决策保持实施待定；不能把离线测试写成生产成功。

- [ ] **Step 1: 写失败的工作流契约测试**

断言提示词包含真实 `threadId` 与 metadata `thread_id`，不包含错误的 `meta.turn.thread_id` 或 `tzgTurn.turn.thread_id`；只列固定动作；规则明确单写入控制器、plan-only 批准门禁、私有状态边界、最小验证预算；飞书宿主离线只返回 `feishu_unavailable`、不丢决定、不回退 Gmail；注册表/状态/提示词中的 TQ-057 决策数量一致；旧控制器文件在此阶段仍存在且未被 v2 引用为实现模块。

- [ ] **Step 2: 确认红灯**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-controller-v2/tests/workflow-contract.tests.ps1
```

Expected: 非零，原因是 v2 提示词/规则/检查器不存在。

- [ ] **Step 3: 写薄提示、规则、检查器和状态摘要**

`tools/check-hourly-controller-v2.ps1` 只做静态契约检查，不重复 Task 9 已通过的 v2 模块套件，也不调用保留组件回归。

- [ ] **Step 4: 运行一次工作流契约检查并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-hourly-controller-v2.ps1
```

Expected: 末行 `check-hourly-controller-v2: OK`。

Commit: `docs(automation-v2): define thin startup and workflow contract`

---

## Task 11: 迁移演练、真实只读金丝雀、无窗口宿主更新与切换门禁

**Files:**

- Private read: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.v8.pre-v2.<timestamp>.json`
- Private create: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2-migration-rehearsal.json`
- Private create: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller-v2-runs/<runId>/manifest.json`
- Modify after approval only: `开发管理/自动工作流控制器提示词.txt`
- Modify after approval only: `开发管理/自动工作流规则.txt`
- Modify after approval only: `tools/check-automation-workflow.ps1`
- Modify after approval only: `开发管理/自动工作流状态.txt`
- System update after merge only: scheduled task `TianZhang-Feishu-Decision-Bridge`

- [ ] **Step 1: 对备份做两次迁移演练**

两次 `MigrateLegacy` 都写临时目标；比较文件 SHA-256 必须相同。用 `Show` 验证五个 decisionId、完整正文、影响摘要、scopeContract 和迁移来源齐全，且没有禁止字段。失败则回 Task 3，不修改生产状态。

- [ ] **Step 2: 运行一次保留组件回归**

这是切换前唯一一次保留组件回归：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
Push-Location tools/feishu-decision-bridge
npm test
Pop-Location
```

Expected: workspace guard、finalizer 和飞书桥接均通过。不要在之后无输入变化时重复。

- [ ] **Step 3: 在真实仓库运行一次只读 plan-only 金丝雀**

使用当前真实 Codex 任务元数据执行：

```text
Start -> RecordTitleResult -> DiscoverRead/Search/List/Check -> SubmitManifest(planOnly=true)
```

验收：标题使用两个真实字段并成功或非阻断失败；清单包含五项决定；双倍率覆盖 CSV、导入器、SpellData、CombatResolver、相关测试和发现到的全部 spell assets；没有项目写入、提交、租约残留或第二个写入控制器。

- [ ] **Step 4: 只读检查无窗口宿主计划**

```powershell
$bridgePlan = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Plan | ConvertFrom-Json
if ($bridgePlan.launchMode -cne 'WINDOWLESS_WSCRIPT') { throw 'unexpected launch mode' }
if ([IO.Path]::GetFileName([string]$bridgePlan.execute) -cne 'wscript.exe') { throw 'scheduled task still launches PowerShell directly' }
if ([string]$bridgePlan.arguments -notmatch '^//B //NoLogo ') { throw 'windowless host arguments are invalid' }
```

Expected: 成功且不更新真实计划任务；输出不含 config path 或私有值。

- [ ] **Step 5: 把 plan-only 清单发到飞书并停止**

卡片正文必须包含：任务标题、所有预期路径（正文可分组，附件/严格文本可复制完整列表）、五项决定的完整口径、逐组改动意图、requiredChecks、明确的“批准后才允许首次写入”，以及回复格式：

```text
DEC-<批准决定编号>：选择 A
DEC-<批准决定编号>：自定义 <修改意见>
```

控制器进入 `IMPLEMENTATION_PENDING`。负责人未明确批准前，不执行后续 Step 6。

- [ ] **Step 6: 负责人批准后，替换 canonical 提示与规则**

把已验证的 v2 提示/规则内容写入 canonical 文件；更新 `tools/check-automation-workflow.ps1` 使其验证 v2 注册表、薄提示和旧控制器暂停，不再把旧 schema v8 当活动协议。Git 历史保留旧内容，不删除保留组件。

- [ ] **Step 7: 最小切换验证并提交**

只运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: 新 canonical 契约通过、旧生产控制器仍为暂停状态说明、没有自动触发写入。

Commit: `feat(automation): cut over to guarded controller v2`

- [ ] **Step 8: 合并到 master 后复验一次同一检查**

合并使用非交互 Git；合并结果只重复 Step 7 的一个检查。若失败，保持自动化暂停并修复，不启用调度。

- [ ] **Step 9: 原位更新真实飞书宿主并做一次窗口检查**

保持 `tzg-hourly-controller` 为 `PAUSED`，执行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Install
$bridgeStatus = pwsh -NoProfile -ExecutionPolicy Bypass -File tools/install-feishu-decision-bridge.ps1 -Action Status | ConvertFrom-Json
if (-not $bridgeStatus.installed -or -not $bridgeStatus.enabled) { throw 'Feishu bridge task is not enabled' }
if ($bridgeStatus.launchMode -cne 'WINDOWLESS_WSCRIPT') { throw 'Feishu bridge is not using the windowless host' }
```

随后只读核对计划任务 action 为 `wscript.exe`，相关 `wscript.exe`、`pwsh.exe` 的 `MainWindowHandle` 均为 `0`，且固定 bridge `node.exe` 只有一个实例、健康状态恢复。若任一条件失败，立即执行 `-Action Disable`，保持私有配置/状态，不回退到 direct-pwsh 登录动作。本步骤不重复 `npm test`。

- [ ] **Step 10: 通过自动化管理能力更新现有任务但保持 PAUSED**

更新 `tzg-hourly-controller` 使用 canonical v2 提示；不得编辑私有 TOML。手动触发一次 `plan-only`，核对它与已批准清单一致。只有负责人再次确认一致，才允许单次受控 TQ-057 写入；此步骤不自动恢复每小时调度。

---

## Task 12: 首次受控写入、观察和旧编排退役

**Files:**

- Modify only within approved TQ-057 manifest paths
- Modify after successful run: `开发管理/自动工作流状态.txt`
- Modify after three successful runs: old controller files listed in the design, plus migration/retirement record

- [ ] **Step 1: 执行一次已批准的 TQ-057 写入**

控制器只能使用批准清单进入 `BeginMutation`。修改完成后 `Finish` 按注册表执行一次 `data-chain`、一次相关 Unity EditMode、一次 pending whitespace、一次 cached diff check，再调用路径限定 finalizer。不要在模型侧重复这些检查。

验收：提交路径严格等于 approved expectedPaths 的实际变化子集；路径外人工状态不变；提交记录含任务 ID；状态回到 IDLE；飞书收到完成摘要。

- [ ] **Step 2: 核对提交与 TQ-057 完成证据**

只检查该提交的 name-only 路径、控制器 check evidence 和任务状态；不再重复运行已通过检查。任何路径遗漏、额外路径或业务失败都保持调度暂停，以显式修复提交处理，不自动 reset/revert。

- [ ] **Step 3: 恢复小时调度并观察三次真实运行**

只有 Step 1–2 通过后才恢复 `tzg-hourly-controller` 每小时调度。三次运行均必须满足：单写入控制器、标题字段正确、飞书任务仍为 enabled/`WINDOWLESS_WSCRIPT` 且没有可见控制台、无私有信息、无残留租约、无部分提交、无路径外变更；无可执行任务时允许 clean skip，不能伪造提交。

- [ ] **Step 4: 三次通过后退役旧编排文件**

按设计第 8/10 节归档或删除：

```text
tools/automation-controller.ps1
tools/automation-controller-state.ps1
tools/automation-decision-status.ps1
与旧状态机耦合且已被 v2 覆盖的旧测试
```

不得删除飞书桥接、无窗口启动器、安装/启动脚本、私有 ACL、workspace guard、finalizer 或 whitespace checker。旧 schema v8 私有备份至少保留到 TQ-057 完成且三次观察通过。

- [ ] **Step 5: 运行一次退役契约检查并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: 活动入口只指向 v2，保留组件仍存在，旧编排不再被 canonical 提示/规则引用。

Commit: `chore(automation): retire legacy orchestration after observation`

---

## Final Acceptance Checklist

- [ ] 生产环境任何时刻最多一个写入控制器；WF1、WF3、WF4 的既有暂停约束未被扩大。
- [ ] 标题读取顶层 `threadId` 和 metadata `thread_id`，不再访问不存在的 `meta.turn.thread_id` 或 `tzgTurn.turn.thread_id`。
- [ ] TQ-057 五项决定以完整正文和 scopeContract 迁移；没有重问、改选或只存字母。
- [ ] 双倍率范围包含 CSV、导入器、`SpellData`、`CombatResolver`、测试和发现到的全部 spell assets。
- [ ] 发现只经过四个固定动作；没有任意 Shell 协议。
- [ ] 任一 baseline/HEAD/path 冲突返回精确 `changedPaths`，不自动重拍 baseline。
- [ ] requiredChecks 只由控制器运行，相关输入无变化时不重复。
- [ ] 飞书按钮、卡片自定义输入、严格文本三种回复继续有效；卡片携带可复制回复格式。
- [ ] 飞书登录任务使用 `WINDOWLESS_WSCRIPT`，不会显示或闪现控制台，并支持幂等 `Start / Stop / Enable / Disable / Status`。
- [ ] 停止或禁用飞书只让待决策流程返回 `feishu_unavailable`；配置、配对、状态和未完成决定不丢失，也不会授权写入或回退 Gmail。
- [ ] 首次 TQ-057 写入存在负责人明确批准的 plan-only 清单证据。
- [ ] 私有状态、身份、消息/事件 ID、签名和证据哈希未进入 Git 或 stdout。
- [ ] 用户原有未跟踪/已修改文件没有被暂存、删除或改写。
- [ ] 三次真实运行通过后才退役旧编排；保留组件未删除。

## New Conversation Start Prompt

复制下面内容到执行新对话：

```text
完整读取并严格执行：
1. docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md
2. docs/superpowers/plans/2026-07-17-hourly-controller-orchestration-rebuild-implementation.md

使用 superpowers:executing-plans（或按计划允许的 subagent-driven-development）逐任务执行，从 Task 0 开始，不重新设计。只在独立 linked worktree 工作；生产 tzg-hourly-controller 全程保持 PAUSED，直到计划中的切换门禁。保留飞书消息协议和 Git 安全底座；按 Task 8 窄改飞书宿主为 WINDOWLESS_WSCRIPT 并增加显式启停，不编辑私有 automation TOML，不触碰主工作区现有未跟踪或已修改文件。严格遵守 Validation Budget：每个切片只跑直接测试，Task 9 合并跑一次 v2 套件，切换前保留组件只回归一次。每完成一个 Task 汇报提交、直接验证结果和下一门禁；遇到事实源冲突立即停止询问。
```
