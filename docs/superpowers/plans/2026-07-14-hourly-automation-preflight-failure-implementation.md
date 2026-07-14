# 小时自动化写前失败收尾 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让控制器在写前参数错误时可靠回到 `IDLE`，并从项目规则、部署提示和静态检查三层固定合法 `TaskKind` 映射。

**Architecture:** 保留 schema v4 和现有恢复状态机。`Fail` 仅在完全没有可恢复工作单元时采用写前收尾；已有任务或恢复证据时继续使用过期租约与 `AUTO-BLOCKED`。部署契约通过规则文本、控制器 prompt 和 `check-automation-workflow.ps1` 同步约束。

**Tech Stack:** PowerShell 7、Git、Codex automation 配置。

---

### Task 1: 写前失败直接释放租约

**Files:**
- Modify: `tools/test-automation-controller-state.ps1`
- Modify: `tools/automation-controller-state.ps1`

- [ ] **Step 1: 写入失败用例**

在状态测试的基础 Acquire/Complete 序列中新增独立场景：

```powershell
$r = Invoke-StateTool @('Acquire', '-StatePath', $statePath, '-RunId', 'preflight-run', '-Now', '2026-07-11T06:00:00Z')
Assert-Code $r 0 'preflight acquire'
$r = Invoke-StateTool @('Fail', '-StatePath', $statePath, '-RunId', 'preflight-run', '-ErrorMessage', 'task_selected rejected invalid TaskKind', '-Now', '2026-07-11T06:01:00Z')
Assert-Code $r 0 'preflight failure cleanup'
$state = Read-TestState
if ($state.state -ne 'IDLE' -or $null -ne $state.runId -or $null -ne $state.leaseExpiresAt -or $state.recoveryCount -ne 0 -or $state.lastError -ne 'task_selected rejected invalid TaskKind') {
  throw 'preflight failure did not release the empty run while preserving its diagnostic'
}
```

- [ ] **Step 2: 运行测试并确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1`

Expected: FAIL，状态仍为 `RUNNING` 或 `runId` 未清除。

- [ ] **Step 3: 实现最小状态分支**

在 `Fail` 写入 `lastError` 后、恢复次数处理前计算是否存在可恢复工作：

```powershell
$hasRecoverableWork =
  -not [string]::IsNullOrWhiteSpace([string]$state.taskKind) -or
  -not [string]::IsNullOrWhiteSpace([string]$state.taskId) -or
  @($state.expectedPaths).Count -gt 0 -or
  -not [string]::IsNullOrWhiteSpace([string]$state.recoveryBaselinePath) -or
  -not [string]::IsNullOrWhiteSpace([string]$state.recoveryEvidencePath) -or
  -not [string]::IsNullOrWhiteSpace([string]$state.recoveryEvidenceHash)

if (-not $hasRecoverableWork) {
  $state.state = 'IDLE'
  $state.runId = $null
  $state.leaseExpiresAt = $null
  $state.taskExecutor = $null
  $state.checkpoint = $null
  $state.expectedPaths = @()
  $state.recoveryCount = 0
  Export-State $state
  break
}
```

- [ ] **Step 4: 运行状态测试并确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1`

Expected: `test-automation-controller-state: OK`。

### Task 2: 固定 TaskKind 部署契约

**Files:**
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `开发管理/自动工作流规则.txt`
- Update through automation API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`

- [ ] **Step 1: 为四种固定映射增加静态检查**

对项目规则与部署 prompt 都要求以下四个映射，并拒绝命令式 `execution`：

```powershell
foreach ($source in $v2Sources) {
  Require-Match $source.Path '普通执行[^\r\n]*execute' "$($source.Label) lacks execute TaskKind mapping"
  Require-Match $source.Path '复审[^\r\n]*review' "$($source.Label) lacks review TaskKind mapping"
  Require-Match $source.Path '维护[^\r\n]*maintenance' "$($source.Label) lacks maintenance TaskKind mapping"
  Require-Match $source.Path '恢复[^\r\n]*recovery' "$($source.Label) lacks recovery TaskKind mapping"
  Reject-Match $source.Path '(-TaskKind\s+|TaskKind\s*[=:]\s*)["''`]?execution\b' "$($source.Label) uses invalid execution TaskKind"
}
```

- [ ] **Step 2: 运行检查并确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`

Expected: FAIL，项目规则和部署 prompt 缺少固定映射。

- [ ] **Step 3: 更新项目规则与暂停的部署 prompt**

在选题登记规则中加入唯一映射：普通执行=`execute`、复审=`review`、维护=`maintenance`、恢复=`recovery`；禁止把自然语言 `execution` 传给脚本。部署配置必须通过 `codex_app__automation_update` 更新并保持 `PAUSED`，不得直接编辑 `automation.toml`。

- [ ] **Step 4: 运行检查并确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`

Expected: `check-automation-workflow: OK`，且活动写入者为 0。

### Task 3: 回归、清理本机状态与重新激活

**Files:**
- Verify: `tools/test-automation-workspace-guard.ps1`
- Verify: `tools/test-automation-finalize-commit.ps1`
- Runtime state: `%USERPROFILE%/.codex/automation-state/tzg-hourly-controller.json`

- [ ] **Step 1: 运行控制面最小完整回归**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: 四项全部 OK；不运行 BattleSim 或 Unity 测试。

- [ ] **Step 2: 提交项目文件**

提交范围仅为状态工具、状态测试、工作流检查和自动工作流规则。提交前运行预期路径行尾检查与 `git diff --cached --check`。

- [ ] **Step 3: 清理当前空恢复状态**

读取本机状态，确认 `taskKind`、`taskId`、`expectedPaths` 和三项恢复证据字段仍为空；使用状态中当前 `runId` 调用新实现的 `Fail`，验证状态变为 `IDLE`、租约为空且 `lastError` 保留。若任一恢复字段不为空则停止，不自动清理。

- [ ] **Step 4: 重新激活并执行金丝雀**

通过 `codex_app__automation_update` 保持全部字段不变，只把状态改为 `ACTIVE`。随后检查唯一活动写入者为 1、本机状态为 `IDLE`、Git 工作区无控制器遗留修改。金丝雀只验证写前路径：合法 `execute` 能完成 `task_selected` 后立即 `Complete`，不修改业务文件。
