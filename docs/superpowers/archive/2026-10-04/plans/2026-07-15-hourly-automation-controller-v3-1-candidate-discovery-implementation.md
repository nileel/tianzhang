# 小时自动化控制器 v3.1 候选发现修复 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增加写前 `InspectCandidate` 阶段和准确命令契约，使小时控制器能从真实队列完成只读候选发现，再以完整路径登记任务。

**Architecture:** `Start` 只建立租约和 baseline；模型从队列选出任务后调用 `InspectCandidate`，入口把暂定任务身份写入用户级 session 并返回分支事实源与只读发现策略。`RegisterCandidate` 从 session 取得任务身份，只接收完整 `expectedPaths`，workspace guard、恢复证据和 finalizer 保持不变。

**Tech Stack:** PowerShell 7、JSON 用户级 session、Git、Codex automation API、现有 controller/state/workspace-guard/finalizer 测试脚本。

---

## 文件职责

- `tools/automation-controller.ps1`：新增 `InspectCandidate` 协议动作、暂定候选 session 状态和命令契约。
- `tools/test-automation-controller.ps1`：覆盖检查前禁止登记、分支事实源、只读发现策略、冲突回选与 DeepSeek backoff。
- `开发管理/自动工作流控制器提示词.txt`：给出准确的命名参数和两阶段选题流程。
- `开发管理/自动工作流规则.txt`：把 `InspectCandidate` 纳入唯一权威工作流规则。
- `tools/check-automation-workflow.ps1`：拒绝缺少准确启动签名或两阶段协议的部署 prompt。
- `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`：只通过 automation API 更新 prompt，保持 PAUSED 到所有验证完成。

### Task 1: 用控制器测试复现候选发现回退

**Files:**
- Modify: `tools/test-automation-controller.ps1:97-168`
- Modify: `tools/test-automation-controller.ps1:229-349`
- Modify: `tools/test-automation-controller.ps1:373-446`
- Test: `tools/test-automation-controller.ps1`

- [ ] **Step 1: 修改 fresh start 断言并增加协议契约断言**

把 fresh start 的 `nextCommand` 期望改成 `InspectCandidate`，并在 `Contract` 断言动作和启动模板：

```powershell
if (-not $startJson.ok -or $startJson.action -ne 'select_candidate' -or
    $startJson.branchKind -ne 'selection' -or $startJson.nextCommand -ne 'InspectCandidate' -or
    -not (Test-Path -LiteralPath $startJson.baselinePath)) {
  throw "fresh start protocol mismatch: $($start.Output)"
}

$contractJson = $contract.Output | ConvertFrom-Json
if ($contractJson.actions -notcontains 'InspectCandidate' -or
    $contractJson.commandTemplates.Start -notmatch 'Start -RepositoryRoot .* -RunId .* -ActualModel') {
  throw "protocol contract does not expose the exact candidate discovery entry: $($contract.Output)"
}
```

- [ ] **Step 2: 增加“未检查不得登记”的失败断言**

在第一次候选操作前调用旧式登记，并要求稳定的 `invalid_phase`：

```powershell
$uninspected = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'task.txt'
)
if ($uninspected.Code -eq 0 -or ($uninspected.Output | ConvertFrom-Json).errorCode -ne 'invalid_phase' -or
    ($uninspected.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
    (Read-State).checkpoint -ne 'identity_checked') {
  throw "candidate registration succeeded without inspection: $($uninspected.Output)"
}
```

- [ ] **Step 3: 增加候选检查、冲突回选和 session 身份断言**

先检查冲突候选，再只传路径登记；冲突必须回到 `InspectCandidate`。随后检查第二候选并成功登记：

```powershell
$inspectedConflict = Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
  '-TaskId', 'conflict-task', '-Executor', 'codex'
)
Assert-Code $inspectedConflict 0 'inspect conflict candidate'
$inspectJson = $inspectedConflict.Output | ConvertFrom-Json
if ($inspectJson.action -ne 'inspect_candidate' -or $inspectJson.nextCommand -ne 'RegisterCandidate' -or
    $inspectJson.requiredSources -notcontains '开发管理/AI协作规则.txt' -or
    -not $inspectJson.discoveryPolicy.readOnlyProjectDiscovery) {
  throw "candidate inspection contract mismatch: $($inspectedConflict.Output)"
}

$conflict = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'human.txt'
)
Assert-Code $conflict 20 'candidate conflict'
$conflictJson = $conflict.Output | ConvertFrom-Json
if ($conflictJson.nextCommand -ne 'InspectCandidate' -or $conflictJson.failurePolicy -ne 'skip_candidate' -or
    $conflictJson.errorCode -ne 'candidate_conflict' -or (Read-State).checkpoint -ne 'identity_checked') {
  throw "candidate conflict was not safely routed back to inspection: $($conflict.Output)"
}

$inspected = Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
  '-TaskId', 'task-1', '-Executor', 'codex'
)
Assert-Code $inspected 0 'inspect execution candidate'

$registered = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-ExpectedPaths', 'task.txt|second-task.txt'
)
```

- [ ] **Step 4: 让所有独立 fixture 先检查再登记**

在下列现有 `RegisterCandidate` 调用前分别插入对应 `InspectCandidate`，登记调用只保留 `ExpectedPaths`：

```powershell
# invalid-state fixture
Assert-Code (Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $invalidStatePath,
  '-RunRoot', $invalidRunRoot, '-RunId', $invalidRunId, '-WorkType', 'execution',
  '-TaskId', 'invalid-task', '-Executor', 'codex'
)) 0 'inspect invalid-path candidate'

# baseline-state fixture
Assert-Code (Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $baselineStatePath,
  '-RunRoot', $baselineRunRoot, '-RunId', $baselineRunId, '-WorkType', 'execution',
  '-TaskId', 'baseline-task', '-Executor', 'codex'
)) 0 'inspect baseline candidate'

# decision-state fixture
Assert-Code (Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $decisionStatePath,
  '-RunRoot', $decisionRunRoot, '-RunId', $decisionRunId, '-WorkType', 'maintenance',
  '-TaskId', 'decision-task', '-Executor', 'codex'
)) 0 'inspect decision candidate'

# finish-multi, finish-single fixtures
Assert-Code (Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $finishRepo, '-StatePath', $finishStatePath,
  '-RunRoot', $finishRunRoot, '-RunId', $finishRunId, '-WorkType', 'execution',
  '-TaskId', 'finish-multi', '-Executor', 'codex'
)) 0 'inspect multi-path finish candidate'
```

对 `finish-single` 使用其现有 state/run ID 和 `TaskId=finish-single` 重复同一检查；`CompleteNoChange` fixture 保持 `Start → CompleteNoChange`，用于证明没有候选时仍可安静关闭。

- [ ] **Step 5: 增加候选身份不一致的失败断言**

新建独立 mismatch fixture，先检查 `mismatch-task`，再故意传入不同任务 ID：

```powershell
$mismatchStatePath = Join-Path $sandbox 'mismatch-state.json'
$mismatchRunRoot = Join-Path $sandbox 'mismatch-runs'
$mismatchRunId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
Assert-Code (Invoke-Controller @(
  'Start', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
  '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId,
  '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
)) 0 'mismatch start'
Assert-Code (Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
  '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId, '-WorkType', 'execution',
  '-TaskId', 'mismatch-task', '-Executor', 'codex'
)) 0 'inspect mismatch candidate'
$mismatch = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $mismatchStatePath,
  '-RunRoot', $mismatchRunRoot, '-RunId', $mismatchRunId,
  '-TaskId', 'different-task', '-ExpectedPaths', 'task.txt'
)
if ($mismatch.Code -eq 0 -or ($mismatch.Output | ConvertFrom-Json).errorCode -ne 'candidate_identity_mismatch' -or
    (Invoke-State @('Show', '-StatePath', $mismatchStatePath)).state -ne 'IDLE') {
  throw "candidate identity mismatch did not close the empty run: $($mismatch.Output)"
}
```

- [ ] **Step 6: 把 DeepSeek backoff 移到候选检查阶段**

`RecordWorkerFailure` 后调用：

```powershell
$backoffCandidate = Invoke-Controller @(
  'InspectCandidate', '-RepositoryRoot', $repo, '-StatePath', $workerStatePath,
  '-RunRoot', $workerRunRoot, '-RunId', $workerRunId, '-WorkType', 'execution',
  '-TaskId', 'deepseek-task', '-Executor', 'deepseek', '-Now', '2026-07-15T00:02:00Z'
)
if ($backoffCandidate.Code -eq 0 -or ($backoffCandidate.Output | ConvertFrom-Json).errorCode -ne 'worker_backoff' -or
    ($backoffCandidate.Output | ConvertFrom-Json).nextCommand -ne 'InspectCandidate' -or
    (Invoke-State @('Show', '-StatePath', $workerStatePath)).checkpoint -ne 'identity_checked') {
  throw "DeepSeek backoff did not exclude inspection: $($backoffCandidate.Output)"
}
```

清除 backoff 后先成功 `InspectCandidate`，断言返回 `DeepSeek工作提示词.txt`，再用 `RegisterCandidate -ExpectedPaths 'second-task.txt'` 登记。

- [ ] **Step 7: 运行控制器测试并确认 RED**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: FAIL；旧实现的 fresh `nextCommand` 为 `RegisterCandidate`，且 `InspectCandidate` 不在 ValidateSet 中。

- [ ] **Step 8: 提交 RED 测试证据**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/test-automation-controller.ps1'
git add -- tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "test(automation): reproduce candidate discovery regression"
```

### Task 2: 实现 `InspectCandidate` 和 session 驱动登记

**Files:**
- Modify: `tools/automation-controller.ps1:1-31`
- Modify: `tools/automation-controller.ps1:134-159`
- Modify: `tools/automation-controller.ps1:458-558`
- Test: `tools/test-automation-controller.ps1`

- [ ] **Step 1: 扩展动作集合和统一 JSON 字段**

将 ValidateSet 增加 `InspectCandidate`，并在 `New-ProtocolResult` 的 ordered map 中加入：

```powershell
discoveryPolicy = $null
```

- [ ] **Step 2: 让 Start 和 Contract 暴露准确下一步**

fresh `Start` 改为：

```powershell
$result.nextCommand = 'InspectCandidate'
```

`Contract` 增加稳定动作与命令模板：

```powershell
$result.actions = @(
  'Contract','Start','InspectCandidate','RegisterCandidate','BeginMutation','Renew','Finish',
  'CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
  'CreateDecision','MarkDecisionNotified','MarkDecisionDeliveryFailed','ResolveDecisionReply'
)
$result.commandTemplates = [ordered]@{
  Start = "Start -RepositoryRoot 'D:\天章游戏开发' -RunId `$runId -ActualModel `$actualModel"
  InspectCandidate = 'InspectCandidate -RepositoryRoot $RepositoryRoot -RunId $runId -WorkType $workType -TaskId $taskId -Executor $executor'
  RegisterCandidate = 'RegisterCandidate -RepositoryRoot $RepositoryRoot -RunId $runId -ExpectedPaths $expectedPaths'
}
```

- [ ] **Step 3: 实现只读候选检查动作**

在 `RegisterCandidate` 分支前加入：

```powershell
'InspectCandidate' {
  $session = Read-Session
  if ($session.phase -notin @('identity_checked', 'candidate_inspection')) {
    $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'InspectCandidate requires a pre-task phase.'
    Write-ProtocolResult $result 13
  }
  if ($WorkType -notin @('execution', 'review', 'maintenance')) {
    Close-EmptyRun 'invalid_arguments' 'WorkType must be execution, review, or maintenance.' 15
  }
  if ([string]::IsNullOrWhiteSpace($TaskId)) { Close-EmptyRun 'invalid_arguments' 'TaskId is required.' 15 }
  if ($Executor -notin @('codex', 'deepseek')) { Close-EmptyRun 'invalid_arguments' 'Executor must be codex or deepseek.' 15 }
  if ($Executor -eq 'deepseek') {
    $workerState = Get-StateSnapshot
    if (Test-DeepSeekBackoffActive $workerState) {
      $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'worker_backoff' 'DeepSeek worker is in backoff.'
      $result.nextCommand = 'InspectCandidate'
      $result.workerBackoff = $workerState.workerState.deepseek
      Write-ProtocolResult $result 23
    }
  }

  $session.phase = 'candidate_inspection'
  $session.branchKind = $WorkType
  $session.workType = $WorkType
  $session.taskId = $TaskId
  $session.executor = $Executor
  Save-Session $session

  $result = New-ProtocolResult $true 'inspect_candidate' $WorkType 'close_empty_run' $null 'Candidate facts may be inspected read-only before path registration.'
  $result.taskId = $TaskId
  $result.executor = $Executor
  $result.requiredSources = @(Get-BranchSources $WorkType $Executor)
  $result.discoveryPolicy = [ordered]@{
    readOnlyProjectDiscovery = $true
    allowedCommands = @('rg', 'rg --files', 'Get-Content', 'git status', 'git diff', 'task-card required checks')
    prohibitedOperations = @('project writes', 'worker dispatch', 'stage', 'commit', 'controller helper calls')
  }
  $result.nextCommand = 'RegisterCandidate'
  Write-ProtocolResult $result
}
```

- [ ] **Step 4: 改为从 session 登记候选**

`RegisterCandidate` 必须要求 `candidate_inspection`，并把 session 身份作为唯一默认值：

```powershell
if ($session.phase -ne 'candidate_inspection') {
  $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'invalid_phase' 'RegisterCandidate requires InspectCandidate first.'
  $result.nextCommand = 'InspectCandidate'
  Write-ProtocolResult $result 13
}
$selectedWorkType = [string]$session.workType
$selectedTaskId = [string]$session.taskId
$selectedExecutor = [string]$session.executor
if ((-not [string]::IsNullOrWhiteSpace($WorkType) -and $WorkType -cne $selectedWorkType) -or
    (-not [string]::IsNullOrWhiteSpace($TaskId) -and $TaskId -cne $selectedTaskId) -or
    (-not [string]::IsNullOrWhiteSpace($Executor) -and $Executor -cne $selectedExecutor)) {
  Close-EmptyRun 'candidate_identity_mismatch' 'RegisterCandidate identity does not match the inspected candidate.' 15
}
if ([string]::IsNullOrWhiteSpace($ExpectedPaths)) { Close-EmptyRun 'invalid_arguments' 'ExpectedPaths is required.' 15 }
```

后续 backoff、TaskKind 映射、checkpoint、session 和 result 均使用 `$selectedWorkType/$selectedTaskId/$selectedExecutor`。候选冲突分支改为：

```powershell
$result.nextCommand = 'InspectCandidate'
```

- [ ] **Step 5: 运行控制器测试并确认 GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
```

Expected: `test-automation-controller: OK`。

- [ ] **Step 6: 提交协议实现**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller.ps1'
git add -- tools/automation-controller.ps1
git diff --cached --check
git commit -m "fix(automation): add candidate inspection phase"
```

### Task 3: 加固 prompt、规则和部署 checker

**Files:**
- Modify: `tools/check-automation-workflow.ps1:201-228`
- Modify: `开发管理/自动工作流控制器提示词.txt:1-17`
- Modify: `开发管理/自动工作流规则.txt:10-38`
- Test: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: 先让 checker 要求准确命令契约**

在 v3 入口检查后加入：

```powershell
Require-Match $controller 'Start\s+-RepositoryRoot\s+''D:\\天章游戏开发''\s+-RunId\s+"\$runId"\s+-ActualModel\s+"\$actualModel"' 'controller prompt lacks the exact Start parameter contract'
Require-Match $controller 'InspectCandidate' 'controller prompt lacks candidate inspection'
Require-Match $controller 'RegisterCandidate[^\r\n]*-ExpectedPaths' 'controller prompt does not register discovered paths explicitly'
```

把必需入口数组增加 `InspectCandidate`。

- [ ] **Step 2: 运行 checker 并确认 RED**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: FAIL，报告缺少准确 `Start` 参数契约和 `InspectCandidate`。

- [ ] **Step 3: 更新薄 prompt**

第 3 至 5 步替换为以下准确流程，同时保持总步骤不超过 10、字符数不超过 3,000：

```text
3. 设置 `$runId=[guid]::NewGuid().Guid`，并把 Node REPL 返回模型赋给 `$actualModel`，准确调用：pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-controller.ps1 Start -RepositoryRoot 'D:\天章游戏开发' -RunId "$runId" -ActualModel "$actualModel"。只解析单个 JSON；按 failurePolicy 退出。

4. select_candidate 时先读 requiredSources 选出候选，把任务卡的工作类型、任务 ID 和执行器分别赋给 `$workType/$taskId/$executor`，再准确调用 InspectCandidate -RepositoryRoot 'D:\天章游戏开发' -RunId "$runId" -WorkType "$workType" -TaskId "$taskId" -Executor "$executor"。仅在返回 discoveryPolicy.readOnlyProjectDiscovery=true 时，按任务卡和 requiredSources 使用允许的只读发现命令推导完整项目相对路径；不得修改项目或派发 worker。

5. 路径完整后把以 `|` 分隔的项目相对路径赋给 `$expectedPaths`，准确调用 RegisterCandidate -RepositoryRoot 'D:\天章游戏开发' -RunId "$runId" -ExpectedPaths "$expectedPaths"。candidate_conflict 按 nextCommand 回到 InspectCandidate；登记成功后才可设置标题、调用 BeginMutation 并实施。
```

- [ ] **Step 4: 更新权威规则**

在“v3 确定性入口”和“每轮顺序”中明确：

```text
- fresh Start 只返回队列入口；模型选定暂定候选后必须先调用 InspectCandidate。该动作只保存用户级暂定身份并开放受任务卡约束的只读发现，不形成 task_selected 或恢复授权。
- 只有候选检查完成并推导完整 expectedPaths 后才能调用 RegisterCandidate；candidate_conflict 返回 InspectCandidate 继续下一候选。
```

并把“模型只能通过”的动作列表加入 `InspectCandidate`。

- [ ] **Step 5: 通过 automation API 部署 PAUSED prompt**

读取当前 automation 的完整字段和版本化 prompt，通过 `codex_app__automation_update` 只替换 prompt，保持：

```text
id=tzg-hourly-controller
status=PAUSED
rrule=每小时第 15 分钟
model=gpt-5.6-terra
reasoningEffort=high
executionEnvironment=local
projectId=D:\天章游戏开发
```

不得直接编辑 `automation.toml`。

- [ ] **Step 6: 运行 checker 并确认 GREEN**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: `check-automation-workflow: OK`，活动写入者为 0，部署 prompt 与版本化源逐字一致。

- [ ] **Step 7: 提交 prompt、规则与 checker**

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/check-automation-workflow.ps1|开发管理/自动工作流控制器提示词.txt|开发管理/自动工作流规则.txt'
git add -- tools/check-automation-workflow.ps1 开发管理/自动工作流控制器提示词.txt 开发管理/自动工作流规则.txt
git diff --cached --check
git commit -m "fix(automation): deploy two-phase candidate prompt"
```

### Task 4: 控制面回归、真实 canary 与恢复激活

**Files:**
- Verify: `tools/test-automation-controller.ps1`
- Verify: `tools/test-automation-controller-state.ps1`
- Verify: `tools/test-automation-workspace-guard.ps1`
- Verify: `tools/test-automation-finalize-commit.ps1`
- Verify: `tools/check-automation-workflow.ps1`
- Read through API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml`

- [ ] **Step 1: 合并运行控制面最小充分回归一次**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: 五项全部 `OK`。不运行 Unity、BattleSim 或数据链检查。

- [ ] **Step 2: 执行 PAUSED 真实队列 canary**

使用真实 state 和项目创建新 run，记录 canary 前 HEAD/status，然后执行：

```powershell
$runId = [guid]::NewGuid().Guid
$start = & tools/automation-controller.ps1 Start -RepositoryRoot 'D:\天章游戏开发' -RunId $runId -ActualModel 'gpt-5.6-terra' | ConvertFrom-Json
$inspect = & tools/automation-controller.ps1 InspectCandidate -RepositoryRoot 'D:\天章游戏开发' -RunId $runId -WorkType execution -TaskId 'TQ-057' -Executor codex | ConvertFrom-Json
$complete = & tools/automation-controller.ps1 CompleteNoChange -RepositoryRoot 'D:\天章游戏开发' -RunId $runId -NoCandidate -ErrorMessage 'v3.1 paused candidate inspection canary' | ConvertFrom-Json
```

断言 `$start.nextCommand -eq 'InspectCandidate'`、`$inspect.nextCommand -eq 'RegisterCandidate'`、`$inspect.requiredSources` 含队列和 AI 协作规则、`$inspect.discoveryPolicy.readOnlyProjectDiscovery` 为 true、`$complete.action -eq 'completed_no_change'`，且前后 HEAD/status 完全一致、state 为 `IDLE`。

- [ ] **Step 3: 通过 automation API 恢复唯一控制器**

保留 Task 3 的全部字段，只把 `status` 从 `PAUSED` 改为 `ACTIVE`。WF1、WF3、WF4 保持 PAUSED，不创建第二个写入型自动化。

- [ ] **Step 4: 最终只读验证**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive
git status --short --branch
git log -5 --oneline
```

Expected: checker OK；控制器 ACTIVE 且活动写入者恰好 1；state 为 `IDLE`；工作树干净；没有远端 push。

- [ ] **Step 5: 汇报结果**

列出 RED 失败、GREEN 测试、canary JSON 摘要、提交号、当前自动化状态和残留风险。明确这次只证明候选检查链可执行，首次真实业务修改仍需后续定时轮次验证。
