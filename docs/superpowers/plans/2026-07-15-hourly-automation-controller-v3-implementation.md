# Hourly Automation Controller v3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用一个返回结构化 JSON 的确定性 PowerShell 门面替代 7,844 字符自然语言状态机，并在不削弱现有状态、workspace guard、恢复证据和路径限定提交语义的前提下部署薄 prompt。

**Architecture:** 新增 `tools/automation-controller.ps1` 作为自动化唯一入口，内部通过参数数组调用现有 `automation-controller-state.ps1`、`automation-workspace-guard.ps1` 和 `automation-finalize-commit.ps1`。用户级 run session 保存 baseline、evidence、恢复标志和协议阶段；模型只提供身份结果、语义候选、业务修改与领域验证结论。部署 prompt 以项目内版本化文本为唯一源，通过自动化管理接口写入本机 TOML。

**Tech Stack:** PowerShell 7、Git、Codex automation API、JSON 协议。

---

## 文件结构

- Create: `tools/automation-controller.ps1` — v3 唯一确定性入口、子工具适配、session、协议结果和失败关闭。
- Create: `tools/test-automation-controller.ps1` — 临时仓库中的 v3 集成测试。
- Create: `开发管理/自动工作流控制器提示词.txt` — 可版本化的薄 prompt 唯一部署源。
- Modify: `tools/check-automation-workflow.ps1` — 验证入口契约、薄 prompt 指标、部署 prompt 与版本化源一致。
- Modify: `开发管理/自动工作流规则.txt` — 声明 v3 门面是唯一调用入口，现有细则成为门面实现契约。
- Update through automation API: `%USERPROFILE%/.codex/automations/tzg-hourly-controller/automation.toml` — 只替换 prompt，保持 PAUSED 直至回归通过。

### Task 1: 建立协议骨架与 fresh run

**Files:**
- Create: `tools/test-automation-controller.ps1`
- Create: `tools/automation-controller.ps1`

- [ ] **Step 1: 写 fresh run RED**

测试创建临时 Git 仓库、临时 state/run root，并调用：

```powershell
$start = Invoke-Controller @(
  'Start', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', '11111111-1111-4111-8111-111111111111',
  '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
)
Assert-Code $start 0 'fresh start'
$json = $start.Output | ConvertFrom-Json
if (-not $json.ok -or $json.action -ne 'select_candidate' -or $json.branchKind -ne 'selection' -or
    $json.nextCommand -ne 'RegisterCandidate' -or -not (Test-Path -LiteralPath $json.baselinePath)) {
  throw "fresh start protocol mismatch: $($start.Output)"
}
```

- [ ] **Step 2: 运行测试确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: FAIL，因为入口脚本不存在。

- [ ] **Step 3: 实现最小协议与子进程适配**

入口定义公共动作、稳定结果和子工具调用：

```powershell
[ValidateSet('Contract','Start','RegisterCandidate','BeginMutation','Renew','Finish','CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','CreateDecision','ResolveDecisionReply')]
[string]$Action

function New-ProtocolResult {
  param([bool]$Ok, [string]$NextAction, [string]$BranchKind, [string]$FailurePolicy, [string]$ErrorCode)
  [ordered]@{
    protocolVersion = 1; ok = $Ok; action = $NextAction; runId = $RunId
    branchKind = $BranchKind; taskId = $null; executor = $null
    expectedPaths = @(); requiredSources = @(); requiredChecks = @()
    nextCommand = $null; failurePolicy = $FailurePolicy
    errorCode = $ErrorCode; message = $null
  }
}

function Invoke-ChildPowerShell {
  param([string]$ScriptPath, [string[]]$Arguments)
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = (Get-Process -Id $PID).Path
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.CreateNoWindow = $true
  foreach ($argument in @('-NoProfile','-ExecutionPolicy','Bypass','-File',$ScriptPath) + $Arguments) {
    $startInfo.ArgumentList.Add([string]$argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  [void]$process.Start()
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $result = [pscustomobject]@{
    Code = $process.ExitCode
    Output = $stdoutTask.GetAwaiter().GetResult().Trim()
    Error = $stderrTask.GetAwaiter().GetResult().Trim()
  }
  $process.Dispose()
  $result
}
```

`Start` 验证 `ActualModel` 非空且不为 `unknown`，生成或验证 UUID runId，调用 Acquire、Snapshot、`identity_checked` checkpoint，写入原子 session，并返回 selection JSON。Acquire 退出 10、11 和其他错误分别映射为 `lease_busy`、`auto_blocked` 和 `state_error`。

- [ ] **Step 4: 运行测试确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: fresh run 场景通过并继续在下一个尚未实现断言处失败，或输出 `test-automation-controller: OK`。

- [ ] **Step 5: 提交协议骨架**

Run:

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller.ps1|tools/test-automation-controller.ps1'
git add -- tools/automation-controller.ps1 tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "feat(automation): add controller v3 protocol"
```

### Task 2: 候选登记、映射和冲突关闭

**Files:**
- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller.ps1`

- [ ] **Step 1: 写候选 RED**

增加四类映射和两个失败分支：

```powershell
$contract = Invoke-Controller @('Contract')
$mapping = ($contract.Output | ConvertFrom-Json).taskKindMapping
if ($mapping.execution -ne 'execute' -or $mapping.review -ne 'review' -or
    $mapping.maintenance -ne 'maintenance' -or $mapping.recovery -ne 'recovery') {
  throw 'TaskKind mapping is not canonical'
}

$conflict = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
  '-TaskId', 'conflict-task', '-Executor', 'codex', '-ExpectedPaths', 'human.txt'
)
if ($conflict.Code -ne 20 -or ($conflict.Output | ConvertFrom-Json).failurePolicy -ne 'skip_candidate') {
  throw 'candidate conflict did not remain skippable'
}

$invalid = Invoke-Controller @(
  'RegisterCandidate', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-WorkType', 'execution',
  '-TaskId', 'invalid-task', '-Executor', 'codex', '-ExpectedPaths', '../escape.txt'
)
if (($invalid.Output | ConvertFrom-Json).failurePolicy -ne 'close_empty_run' -or (Read-State).state -ne 'IDLE') {
  throw 'pre-task failure did not close the empty run'
}
```

- [ ] **Step 2: 运行确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: FAIL，入口尚未实现 Contract 或 RegisterCandidate。

- [ ] **Step 3: 实现映射、Check 与 task_selected**

实现内部映射，不把状态工具枚举暴露给 prompt：

```powershell
$script:TaskKindMapping = [ordered]@{
  execution = 'execute'
  review = 'review'
  maintenance = 'maintenance'
  recovery = 'recovery'
}
```

RegisterCandidate 从 session 取得 baseline；验证 WorkType、TaskId、Executor、ExpectedPaths；DeepSeek 退避未到期时返回 `worker_backoff`；调用 guard Check。退出 20 不改变状态，退出 21 或非法参数调用统一写前 Fail 并返回稳定 failurePolicy。通过时依次写 `queues_loaded`、`task_selected`，持久化外部 branch、executor 和 requiredSources。

- [ ] **Step 4: 运行确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: mapping、candidate conflict、invalid preflight 和合法候选登记通过。

### Task 3: mutation 失败证据与恢复

**Files:**
- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller.ps1`

- [ ] **Step 1: 写 mutation/recovery RED**

测试合法登记后 BeginMutation，修改 expected path，再调用 Fail：

```powershell
$begin = Invoke-Controller @('BeginMutation', '-RepositoryRoot', $repo, '-StatePath', $statePath, '-RunRoot', $runRoot, '-RunId', $runId)
Assert-Code $begin 0 'begin mutation'
[IO.File]::WriteAllText((Join-Path $repo 'task.txt'), "controller residue`n", [Text.UTF8Encoding]::new($false))
$failed = Invoke-Controller @('Fail', '-RepositoryRoot', $repo, '-StatePath', $statePath, '-RunRoot', $runRoot, '-RunId', $runId, '-ErrorMessage', 'simulated interruption')
$failedState = Read-State
if (-not $failedState.recoveryEvidencePath -or -not $failedState.recoveryEvidenceHash) {
  throw 'mutation failure did not capture recovery evidence'
}
$recovery = Invoke-Controller @('Start', '-RepositoryRoot', $repo, '-StatePath', $statePath, '-RunRoot', $runRoot, '-RunId', $recoveryRunId, '-ActualModel', 'gpt-test', '-Now', '2026-07-15T04:00:00Z')
if (($recovery.Output | ConvertFrom-Json).branchKind -ne 'recovery') { throw 'exact residue did not recover' }
```

再增加 expected path 被追加修改和路径外 `human.txt` 改变，分别断言 `recovery_expected_changed` 与 `baseline_changed`。

- [ ] **Step 2: 运行确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: FAIL，Fail 尚未捕获证据或 Start 尚未 CheckRecovery。

- [ ] **Step 3: 实现 BeginMutation、Fail 和 recovery Start**

BeginMutation 只允许 task_selected 阶段。Fail 在 mutation_started 且无证据时先 Verify，再 CaptureRecoveryEvidence，再用不改变 checkpoint 名称的 Checkpoint 保存 baseline/evidence 引用，最后调用状态 Fail；`WasRecovery` 由 session 推导。Start 检测 Acquire 后保留的任务字段，校验 state evidence hash 与文件 payloadHash，调用 CheckRecovery，并根据 checkpoint 返回 `resume_task` 或 `finish_task`。

- [ ] **Step 4: 运行确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: 精确残留恢复、expected path 变化拒绝、路径外/HEAD 变化拒绝全部通过。

### Task 4: pending decision 与 DeepSeek backoff

**Files:**
- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller.ps1`

- [ ] **Step 1: 写 decision/backoff RED**

测试 CreateDecision 使用当前 taskKind/taskId，并只在 expectedPaths 已含 `开发管理/自动工作流状态.txt` 时允许；ResolveDecisionReply 只接受严格正文：

```powershell
$reply = Invoke-Controller @(
  'ResolveDecisionReply', '-StatePath', $statePath, '-RunRoot', $runRoot,
  '-RunId', $runId, '-ReplyText', "$decisionId：选择 A"
)
Assert-Code $reply 0 'strict decision reply'

$invalidReply = Invoke-Controller @(
  'ResolveDecisionReply', '-StatePath', $statePath, '-RunRoot', $runRoot,
  '-RunId', $runId, '-ReplyText', "我建议选 A"
)
if (($invalidReply.Output | ConvertFrom-Json).errorCode -ne 'invalid_reply') { throw 'fuzzy reply was accepted' }
```

DeepSeek 场景先 RecordWorkerFailure，断言 Start 返回 backoff 信息且 RegisterCandidate executor=deepseek 返回 skip；ClearWorkerFailure 后同候选可以登记。

- [ ] **Step 2: 运行确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: FAIL，对应辅助动作尚未实现。

- [ ] **Step 3: 实现状态辅助动作**

CreateDecision 从当前状态读取内部 TaskKind/TaskId，不接受模型覆盖；ResolveDecisionReply 使用锚定正则解析同一 decisionId 和唯一有效 option key，再调用 ResolveDecision `ReplySource=email`。RecordWorkerFailure、ClearWorkerFailure 和 RecordQueueState 只包装现有状态动作并返回更新后的 backoff/队列字段。

- [ ] **Step 4: 运行确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: pending decision 与 backoff 全部通过。

### Task 5: Finish、单路径和多路径实际变化提交

**Files:**
- Modify: `tools/test-automation-controller.ps1`
- Modify: `tools/automation-controller.ps1`

- [ ] **Step 1: 写 Finish RED**

分别建立单路径和两路径允许范围；多路径只改其中一个时，断言提交只含实际变化文件，预先 staged/dirty/untracked 文件不变：

```powershell
$finish = Invoke-Controller @(
  'Finish', '-RepositoryRoot', $repo, '-StatePath', $statePath,
  '-RunRoot', $runRoot, '-RunId', $runId, '-CommitMessage', 'test: v3 finish'
)
Assert-Code $finish 0 'finish'
$finishJson = $finish.Output | ConvertFrom-Json
if ($finishJson.action -ne 'completed' -or $finishJson.commit -notmatch '^[0-9a-f]{40,64}$' -or (Read-State).state -ne 'IDLE') {
  throw "finish protocol mismatch: $($finish.Output)"
}
```

- [ ] **Step 2: 运行确认 RED**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: FAIL，Finish 尚未实现固定收尾。

- [ ] **Step 3: 实现 Finish 与 CompleteNoChange**

Finish 从 state 读取 expectedPaths 与原始 baseline，依次 Verify、CaptureRecoveryEvidence、verification_completed、finalizer、commit_completed、Verify、Complete；任何错误统一 Fail。CompleteNoChange 在 started 阶段用固定不存在 sentinel 调用 guard Check，或在 task_selected 阶段调用 Verify，确认无项目变化后 Complete。成功后仅删除 RunRoot 内本轮 session、baseline 和已消费 evidence。

- [ ] **Step 4: 运行确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1`

Expected: fresh、conflict、recovery、decision、backoff、单/多路径 Finish 全部输出 `test-automation-controller: OK`。

- [ ] **Step 5: 提交完整入口与测试**

Run:

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths 'tools/automation-controller.ps1|tools/test-automation-controller.ps1'
git add -- tools/automation-controller.ps1 tools/test-automation-controller.ps1
git diff --cached --check
git commit -m "feat(automation): orchestrate controller lifecycle"
```

### Task 6: 薄 prompt、规则和部署契约

**Files:**
- Create: `开发管理/自动工作流控制器提示词.txt`
- Modify: `开发管理/自动工作流规则.txt`
- Modify: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: 先修改 checker 并确认旧配置 RED**

checker 新增：入口和 prompt source 必须存在；部署 prompt 必须与 source 完全一致；字符数不超过 3,000、编号步骤不超过 10；必须包含 Start/RegisterCandidate/BeginMutation/Finish/CompleteNoChange/Fail 和 requiredSources；拒绝直接出现三个底层 helper、`TaskKind`、`CaptureRecoveryEvidence`、`CheckRecovery` 或 `git commit`。把现有详细规则匹配从 deployed prompt 移到 `自动工作流规则.txt`。

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`

Expected: FAIL，缺少 prompt source 且部署 prompt 超限。

- [ ] **Step 2: 写薄 prompt 与 v3 规则**

prompt 使用 8 个编号步骤：读规则；Node REPL 身份；Start；按 requiredSources/action 做语义路由；RegisterCandidate/标题；BeginMutation/实施；最小充分验证；Finish/CompleteNoChange/Fail 与简短结果。硬边界只保留唯一写者、禁止并行/推送/直接 helper/Git/TOML 和 DeepSeek 权限。

规则增加“确定性入口”章节，明确后续每轮顺序是入口内部契约，模型只调用门面；保留全部现有安全语义和分支事实源。

- [ ] **Step 3: 通过自动化 API 部署 PAUSED prompt**

读取版本化 prompt 和当前 TOML 的所有非 prompt 字段，调用 `codex_app__automation_update`，只替换 prompt 并保持 status=PAUSED。不得直接编辑 TOML。

- [ ] **Step 4: 运行 checker 确认 GREEN**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1`

Expected: `check-automation-workflow: OK`，活动写入者为 0。

- [ ] **Step 5: 提交规则、prompt source 和 checker**

Run:

```powershell
& tools/check-pending-whitespace.ps1 -ExpectedPaths '开发管理/自动工作流控制器提示词.txt|开发管理/自动工作流规则.txt|tools/check-automation-workflow.ps1'
git add -- 开发管理/自动工作流控制器提示词.txt 开发管理/自动工作流规则.txt tools/check-automation-workflow.ps1
git diff --cached --check
git commit -m "refactor(automation): deploy thin controller prompt"
```

### Task 7: 合并控制面回归、激活和真实金丝雀

**Files:**
- Verify: `tools/test-automation-controller.ps1`
- Verify: `tools/test-automation-controller-state.ps1`
- Verify: `tools/test-automation-workspace-guard.ps1`
- Verify: `tools/test-automation-finalize-commit.ps1`
- Verify: `tools/check-automation-workflow.ps1`

- [ ] **Step 1: 运行一次合并控制面回归**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-controller-state.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-workspace-guard.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/test-automation-finalize-commit.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1
```

Expected: 五项全部 OK；不运行 Unity、BattleSim 或数据链检查。

- [ ] **Step 2: 运行 PAUSED 写前金丝雀**

在真实项目和真实本机 state 上调用 Start，然后 CompleteNoChange；不 Register 业务候选、不修改项目文件。断言 state 回到 IDLE、Git 工作区未变化。

- [ ] **Step 3: 激活唯一控制器**

通过 automation API 保留所有字段，只把 `tzg-hourly-controller` 改为 ACTIVE；WF1/WF3/WF4 不变。

- [ ] **Step 4: 最终验证**

Run: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-automation-workflow.ps1 -ExpectControllerActive`

并读取 state 与 `git status --short`。Expected: checker OK、state=IDLE、无 recovery/pending/backoff、工作区干净、活动写入者恰好 1。

- [ ] **Step 5: 记录最终指标**

报告旧/新 prompt 的字符、行、步骤、“必须”和“不得/禁止”计数；列出下沉职责、五项测试、提交号和残留风险。不得推送远端。
