# Hourly Automation Pause and Runner Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复 CLI runner 的非 JSON 假失败、为无人值守控制器增加工具级逻辑暂停，并清理过期恢复状态后把当前生产入口安全同步为 `PAUSED`。

**Architecture:** 继续保留现有 CLI session runner、runtime schema v1 和单写入租约。`blocking.pauseRequested` 升格为工具级逻辑暂停，`Acquire` 在该状态下失败关闭，新增外部恢复动作 `ClearBlocking`；控制器自身不再调用自动化管理能力，界面状态只由普通管理上下文同步。

**Tech Stack:** PowerShell 7.6、Codex CLI JSONL、Codex App 自动化管理能力、Git、UTF-8 文本契约。

## Global Constraints

- 项目 PowerShell 脚本唯一支持 PowerShell 7；所有独立进程命令使用 `pwsh -NoProfile -ExecutionPolicy Bypass -File ...`。
- 生产 prompt、schedule、workdir 和 status 只通过 Codex 自动化管理能力变更，不直接编辑 `automation.toml`。
- 不新增第二个定时自动化、Windows 计划任务、后台守护进程、进度数据库或 runtime schema 版本。
- 不输出 CLI 模型正文、原始 JSONL、prompt、回复或 child stderr。
- 不修改队列候选、内容冻结、任务优先级、DeepSeek 配置、Unity、BattleSim 或数据链路。
- 不 stash、reset、checkout、clean、revert 或覆盖用户改动。
- 每个生产代码切片先写失败测试、确认按预期失败，再写最小实现。

---

### Task 1: 允许成功 CLI session 携带非 JSON 诊断行

**Files:**
- Modify: `tools/test-codex-cli-session.ps1`
- Modify: `tools/codex-cli-session.ps1`

**Interfaces:**
- Consumes: `codex exec --json` 或 `codex exec resume --json` 的逐行 stdout。
- Produces: 单行 JSON `{status,action,taskId,runId,sessionId,exitCode}`；成功只依赖退出码、唯一 session 和 Resume ID 匹配。

- [ ] **Step 1: 在 fake Codex 中加入非 JSON 成功 Resume 用例**

在 `tools/test-codex-cli-session.ps1` 的 fake switch 中加入：

```powershell
'resume-non-json' {
  Write-Output 'codex-cli informational diagnostic'
  [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
}
```

在正常 Resume 断言之后加入：

```powershell
$resumeWithDiagnosticPrompt = 'secret-resume-diagnostic-marker-49d3f1'
$resumeWithDiagnostic = Invoke-Runner `
  -Action Resume `
  -Prompt $resumeWithDiagnosticPrompt `
  -Case 'resume-non-json' `
  -SessionId $expectedSessionId
Assert-Equal -Actual $resumeWithDiagnostic.ExitCode -Expected 0 -Message 'Resume with diagnostic process failed'
Assert-Equal -Actual $resumeWithDiagnostic.Json.status -Expected 'ok' -Message 'Resume with diagnostic status mismatch'
Assert-Equal -Actual $resumeWithDiagnostic.Json.sessionId -Expected $expectedSessionId -Message 'Resume with diagnostic session mismatch'
Assert-Equal -Actual $resumeWithDiagnostic.StderrLines.Count -Expected 2 -Message 'Resume with diagnostic progress count mismatch'
```

- [ ] **Step 2: 运行 runner 测试并确认按预期变红**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
```

Expected: 非 0；失败信息包含 `Resume with diagnostic process failed` 或 `Resume with diagnostic status mismatch`，证明现实现因为非 JSON 行误报失败。

- [ ] **Step 3: 删除非 JSON 全局失败条件**

在 `tools/codex-cli-session.ps1` 删除：

```powershell
$script:invalidChildJson = $false
```

把解析失败分支从：

```powershell
} catch {
  $script:invalidChildJson = $true
  return
}
```

改为：

```powershell
} catch {
  return
}
```

把成功条件从：

```powershell
if ($childExitCode -eq 0 -and
    -not $script:invalidChildJson -and
    $sessionMatchesAction) {
```

改为：

```powershell
if ($childExitCode -eq 0 -and $sessionMatchesAction) {
```

- [ ] **Step 4: 运行完整 runner 直接测试**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
```

Expected: `test-codex-cli-session: OK`；Start、Resume、缺失 session、重复 session、Resume ID 错配和非 0 退出原用例全部通过。

- [ ] **Step 5: 检查并提交 runner 切片**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -Command "& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-codex-cli-session.ps1|tools/codex-cli-session.ps1'"
git diff --check
git add -- tools/test-codex-cli-session.ps1 tools/codex-cli-session.ps1
git diff --cached --check
git commit -m "fix(automation): ignore non-json CLI diagnostics"
```

Expected: 两道 whitespace/diff 检查通过，提交只包含两个 runner 文件。

---

### Task 2: 在租约工具中建立逻辑暂停硬边界

**Files:**
- Modify: `tools/test-hourly-automation-lease.ps1`
- Modify: `tools/hourly-automation-lease.ps1`

**Interfaces:**
- Consumes: runtime schema v1 的 `blocking.fingerprint/count/pauseRequested`。
- Produces: `Acquire -> SUSPENDED`；`ClearBlocking -> BLOCKING_CLEARED|BUSY|RECOVERY_PRESENT|PENDING_RESUMES`。

- [ ] **Step 1: 增加暂停后拒绝 Acquire 与显式恢复测试**

在第二次相同 blocked 结果释放租约后加入：

```powershell
$suspendedAcquire = Invoke-LeaseTool -Action Acquire -Parameters @{
  StateRoot = $stateRoot
  TaskId = 'task-must-not-start'
  Owner = 'codex'
  RepositoryRoot = $repositoryRoot
}
Assert-Equal -Actual $suspendedAcquire.Json.status -Expected 'SUSPENDED' -Message 'Paused runtime allowed a normal Acquire'
Assert-Equal -Actual $suspendedAcquire.Json.fingerprint -Expected 'fingerprint-a' -Message 'Suspended fingerprint mismatch'
Assert-Equal -Actual $suspendedAcquire.Json.count -Expected 2 -Message 'Suspended count mismatch'

$suspendedShow = Invoke-LeaseTool -Action Show -Parameters @{ StateRoot = $stateRoot }
Assert-True -Condition ($null -eq $suspendedShow.Json.state.lease) -Message 'Suspended Acquire wrote a lease'

$clearBlocking = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
Assert-Equal -Actual $clearBlocking.Json.status -Expected 'BLOCKING_CLEARED' -Message 'ClearBlocking did not succeed'
Assert-True -Condition ($null -eq $clearBlocking.Json.blocking.fingerprint) -Message 'ClearBlocking retained fingerprint'
Assert-Equal -Actual $clearBlocking.Json.blocking.count -Expected 0 -Message 'ClearBlocking retained count'
Assert-True -Condition (-not [bool]$clearBlocking.Json.blocking.pauseRequested) -Message 'ClearBlocking retained pause request'

$postClearAcquire = Invoke-LeaseTool -Action Acquire -Parameters @{
  StateRoot = $stateRoot
  TaskId = 'task-after-clear'
  Owner = 'codex'
  RepositoryRoot = $repositoryRoot
}
Assert-Equal -Actual $postClearAcquire.Json.status -Expected 'ACQUIRED' -Message 'Acquire did not resume after ClearBlocking'
```

- [ ] **Step 2: 增加 ClearBlocking 失败关闭测试**

在 `$postClearAcquire` 仍持有租约时加入并随后释放：

```powershell
$clearWithLease = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
Assert-Equal -Actual $clearWithLease.Json.status -Expected 'BUSY' -Message 'ClearBlocking ignored an active lease'
Invoke-LeaseTool -Action Release -Parameters @{ StateRoot = $stateRoot; RunId = $postClearAcquire.Json.runId } | Out-Null
```

在测试已有 recovery 已保存但尚未清除的位置加入：

```powershell
$clearWithRecovery = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
Assert-Equal -Actual $clearWithRecovery.Json.status -Expected 'RECOVERY_PRESENT' -Message 'ClearBlocking ignored recovery'
```

在测试已有 pending resume 且租约为空的位置加入：

```powershell
$clearWithPending = Invoke-LeaseTool -Action ClearBlocking -Parameters @{ StateRoot = $stateRoot }
Assert-Equal -Actual $clearWithPending.Json.status -Expected 'PENDING_RESUMES' -Message 'ClearBlocking ignored pending resumes'
```

每个断言后继续使用既有 recovery/pending 清理流程，最终 schema 与 ACL 断言保持不变。

- [ ] **Step 3: 运行租约测试并确认按预期变红**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: 非 0；首先因 `ClearBlocking` 不在 ValidateSet 或暂停后 Acquire 仍返回 `ACQUIRED` 而失败。

- [ ] **Step 4: 实现 ClearBlocking 与 Acquire 暂停闸**

把参数动作集合改为：

```powershell
[ValidateSet('Show', 'Acquire', 'SaveRecovery', 'ClearRecovery', 'QueueResume', 'TakeResume', 'RecordResult', 'Release', 'ClearBlocking')]
```

在 `Acquire` 分支完成参数和仓库根校验后、创建 lease 前加入：

```powershell
if ([bool]$state.blocking.pauseRequested) {
  $result = New-Result -Status 'SUSPENDED' -Values @{
    fingerprint = $state.blocking.fingerprint
    count = [int]$state.blocking.count
  }
  break
}
```

在 switch 中加入：

```powershell
'ClearBlocking' {
  if ($null -ne $state.lease) {
    $result = New-Result -Status 'BUSY'
    break
  }
  if ($null -ne $state.recovery) {
    $result = New-Result -Status 'RECOVERY_PRESENT'
    break
  }
  if (@($state.pendingResumes).Count -gt 0) {
    $result = New-Result -Status 'PENDING_RESUMES'
    break
  }
  $state.blocking.fingerprint = $null
  $state.blocking.count = 0
  $state.blocking.pauseRequested = $false
  Write-RuntimeState -Path $statePath -State $state
  $result = New-Result -Status 'BLOCKING_CLEARED' -Values @{
    blocking = $state.blocking
  }
}
```

不改变 schemaVersion、blocking 字段集合或 `lastResult`。

- [ ] **Step 5: 运行完整租约测试**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
```

Expected: `test-hourly-automation-lease: OK`；最终 runtime schema 仍为 v1，lease/recovery/pending 全为空。

- [ ] **Step 6: 检查并提交租约切片**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -Command "& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-hourly-automation-lease.ps1|tools/hourly-automation-lease.ps1'"
git diff --check
git add -- tools/test-hourly-automation-lease.ps1 tools/hourly-automation-lease.ps1
git diff --cached --check
git commit -m "fix(automation): enforce logical pause in lease"
```

Expected: 提交只包含租约工具和其直接测试。

---

### Task 3: 改写暂停契约，禁止控制器管理自身

**Files:**
- Modify: `tools/test-check-automation-workflow.ps1`
- Modify: `tools/check-automation-workflow.ps1`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `开发管理/自动工作流控制器提示词.txt`

**Interfaces:**
- Consumes: `Show` 返回的 `blocking.pauseRequested` 与 `Acquire -> SUSPENDED`。
- Produces: 规范提示和检查器共同保证自动化自身不调用管理能力，外部普通上下文负责 UI 状态同步。

- [ ] **Step 1: 先把契约 fixture 改成新语义**

在 `tools/test-check-automation-workflow.ps1` 的 canonical prompt/rules fixture 中加入以下逐字契约：

```text
pauseRequested=true 表示工具级逻辑暂停；立即结束，不扫描候选、不取得租约、不启动责任方。
逻辑暂停期间普通 Acquire 返回 SUSPENDED；只有自动化任务之外的普通管理上下文可先调用 ClearBlocking，再把入口设为 ACTIVE。
自动化任务不得调用自动化管理能力管理自身，也不得调用自身 view 或等待管理服务。
界面 PAUSED 只由外部普通管理上下文同步；未确认时只报告“runtime 已逻辑暂停，界面尚未同步”。
```

删除 fixture 中这些旧契约：

```text
pauseRequested=true 时只做一次完整配置更新；确认 status=PAUSED 后才汇报已暂停。
相同全阻塞指纹连续两次后 PAUSED 并通知。
```

增加一个坏 fixture，把“控制器直接更新自身为 PAUSED”写入 prompt，并断言检查器非 0 且错误包含 `manages itself`。

- [ ] **Step 2: 运行契约测试并确认按预期变红**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: 非 0；现有检查器仍要求“完整配置更新 / 确认 status=PAUSED”，或没有拒绝自管理坏 fixture。

- [ ] **Step 3: 更新工作流检查器**

在 `tools/check-automation-workflow.ps1` 的 prompt required literals 中移除：

```powershell
'历史 `pauseRequested=true` 不得作为本轮提前退出条件',
'只有本轮完成候选扫描和队列维护后再次返回 `pauseRequested=true`',
'只做一次完整配置更新',
'确认 status=PAUSED'
```

加入：

```powershell
'pauseRequested=true 表示工具级逻辑暂停',
'`SUSPENDED`',
'`ClearBlocking`',
'自动化任务不得调用自动化管理能力管理自身',
'外部普通管理上下文',
'runtime 已逻辑暂停，界面尚未同步'
```

在 active prompt/rules 负面扫描中加入：

```powershell
foreach ($forbiddenSelfManagement in @(
  '控制器直接更新自身为 PAUSED',
  '只做一次完整配置更新并等待同一调用返回',
  '调用自身 view'
)) {
  Assert-Contract `
    -Condition (-not $activeText.Contains($forbiddenSelfManagement, [StringComparison]::OrdinalIgnoreCase)) `
    -Message "active controller manages itself: $forbiddenSelfManagement"
}
```

- [ ] **Step 4: 更新规则和规范提示**

把两个文件的启动逻辑统一为：

```text
每轮先调用租约工具 Show。若 blocking.pauseRequested=true，表示工具级逻辑暂停已经生效；立即输出 suspended 并结束，不扫描候选、不取得租约、不启动责任方，也不调用任何自动化管理或自身 view。
```

把两轮阻塞逻辑统一为：

```text
第二次相同全阻塞 fingerprint 使 pauseRequested=true 后，先记录结果并释放租约，再报告“runtime 已逻辑暂停，界面尚未同步”并结束。自动化任务不得调用自动化管理能力管理自身。界面 PAUSED 只由自动化任务之外的普通管理上下文同步。
```

加入恢复规则：

```text
恢复只由外部普通管理上下文执行：确认 lease、recovery、pending resume 均为空，先调用 ClearBlocking 成功清除逻辑暂停，再通过自动化管理能力把完整现有配置设为 ACTIVE。
```

- [ ] **Step 5: 运行契约测试**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
```

Expected: `test-check-automation-workflow: OK`，坏 fixture 被检查器拒绝。

- [ ] **Step 6: 检查并提交契约切片**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -Command "& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-check-automation-workflow.ps1|tools/check-automation-workflow.ps1|开发管理/自动工作流规则.txt|开发管理/自动工作流控制器提示词.txt'"
git diff --check
git add -- tools/test-check-automation-workflow.ps1 tools/check-automation-workflow.ps1 开发管理/自动工作流规则.txt 开发管理/自动工作流控制器提示词.txt
git diff --cached --check
git commit -m "fix(automation): externalize controller pause sync"
```

Expected: 提交只包含契约检查、直接测试、规则和规范提示。

---

### Task 4: 修正项目状态并同步生产自动化

**Files:**
- Modify: `开发管理/自动工作流状态.txt`
- External state: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`，只通过 Codex 自动化管理能力更新。

**Interfaces:**
- Consumes: 提交 `c4e9db82a5a20850814136b550711fcbd6302ce0`、租约工具 `Show`、规范提示、当前生产配置。
- Produces: 无过期 recovery 的项目状态；安装 prompt 与规范提示一致；生产 status 为 `PAUSED`。

- [ ] **Step 1: 读取并冻结生产同步输入**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
git show --stat --oneline c4e9db82a5a20850814136b550711fcbd6302ce0
Get-Content -Raw 开发管理/自动工作流控制器提示词.txt
Get-Content -Raw "$env:USERPROFILE/.codex/automations/tzg-hourly-controller/automation.toml"
git status --short
```

Expected: lease/recovery/pending 为空，pauseRequested 为 true，N-GROUP-01 提交存在，工作区干净，现有自动化仍为单一生产入口。

- [ ] **Step 2: 从当前普通 root 对话同步 prompt 和 PAUSED**

调用 Codex 自动化管理能力一次，参数固定为：

```text
id=tzg-hourly-controller
kind=cron
name=TZG Hourly Controller
prompt=开发管理/自动工作流控制器提示词.txt 的完整 UTF-8 内容
rrule=FREQ=HOURLY;INTERVAL=1;BYMINUTE=15
status=PAUSED
model=gpt-5.6-terra
reasoningEffort=high
executionEnvironment=local
destination=local
projectId=local-b2d3c817de7062bf08f61ab59e276c8b
notificationPolicy=null
```

不得从自动化任务内执行，不得直接编辑 TOML，不得在调用尚未返回时发起第二次更新。

- [ ] **Step 3: 验证生产同步终态**

先用自动化管理能力 `view(id=tzg-hourly-controller)` 取得终态，再运行：

```powershell
Select-String -LiteralPath "$env:USERPROFILE/.codex/automations/tzg-hourly-controller/automation.toml" -Pattern '^status\s*=|^updated_at\s*='
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
```

Expected: status 为 `PAUSED`，安装 prompt 与规范提示一致，runtime 仍是 lease/recovery/pending 为空且 pauseRequested=true。

- [ ] **Step 4: 更新项目可见状态**

删除 `开发管理/自动工作流状态.txt` 中“N-GROUP-01 等待恢复”的两行，加入以下事实：

```text
## 当前生产保护

- N-GROUP-01 已由业务提交 `c4e9db82a5a20850814136b550711fcbd6302ce0` 完成并归档；原 CLI Resume 的非 JSON 假失败不再代表恢复状态。
- 当前 runtime 的 lease、recovery 与 pending resume 均为空；连续相同全阻塞已使 `pauseRequested=true` 成为工具级逻辑暂停，普通 Acquire 返回 `SUSPENDED`。
- 当前阻塞原因仍为权威资料不足以形成完整新任务卡；这是合法业务阻塞，不伪造 backlog 卡。
- 生产入口已由自动化任务之外的普通管理上下文确认同步为 `PAUSED`；未来恢复须先 `ClearBlocking`，再把完整现有配置设为 `ACTIVE`。
```

- [ ] **Step 5: 运行合并最小充分验证**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-codex-cli-session.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-hourly-automation-lease.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-check-automation-workflow.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -RequireLegacyRetired
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理,docs,tools
pwsh -NoProfile -ExecutionPolicy Bypass -Command "& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流状态.txt'"
git diff --check
```

Expected: 所有命令退出 0；不运行 Unity、BattleSim 或数据链路检查。

- [ ] **Step 6: 提交状态闭环并做最终核验**

Run:

```powershell
git add -- 开发管理/自动工作流状态.txt
git diff --cached --check
git commit -m "docs(automation): reconcile paused production state"
git status --short
git log -5 --oneline
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/hourly-automation-lease.ps1 -Action Show
Select-String -LiteralPath "$env:USERPROFILE/.codex/automations/tzg-hourly-controller/automation.toml" -Pattern '^status\s*='
```

Expected: 工作区干净；最近提交覆盖 runner、租约、契约和状态四个切片；runtime 逻辑暂停仍有效；生产配置为 `PAUSED`。

---

## Plan Self-Review

- Spec coverage: Tasks 1–4 分别覆盖非 JSON 假失败、工具级逻辑暂停、禁止自管理、状态和生产配置同步；合法 backlog 阻塞明确不修改。
- Placeholder scan: 计划不含未决实现项；生产配置字段使用当前已核验的固定值，prompt 唯一来源是规范提示全文。
- Interface consistency: `pauseRequested=true`、`SUSPENDED`、`ClearBlocking`、`BLOCKING_CLEARED` 在工具、测试、规则和状态中使用同一拼写；runtime schema 保持 v1。
- Verification scope: 只运行直接 PowerShell、文本和 Git 检查；Unity、BattleSim 和数据链路输入未变化。
