#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Write-Utf8File {
  param([string]$Path, [string]$Content)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Write-Automation {
  param([string]$Root, [string]$Id, [ValidateSet('ACTIVE', 'PAUSED')][string]$Status, [string]$Prompt)
  $encodedPrompt = $Prompt | ConvertTo-Json -Compress
  Write-Utf8File -Path (Join-Path $Root "$Id\automation.toml") -Content @"
version = 1
id = "$Id"
name = "$Id"
prompt = $encodedPrompt
status = "$Status"
"@
}

function Invoke-Checker {
  param([string]$RepositoryRoot, [string]$AutomationRoot, [switch]$RequireActive, [switch]$RequireLegacyRetired)
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'check-automation-workflow.ps1'),
    '-RepositoryRoot', $RepositoryRoot, '-AutomationRoot', $AutomationRoot
  )
  if ($RequireActive) { $arguments += '-RequireActive' }
  if ($RequireLegacyRetired) { $arguments += '-RequireLegacyRetired' }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  Assert-True -Condition $process.Start() -Message 'Unable to start workflow checker'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $result = [pscustomobject]@{
    ExitCode = $process.ExitCode
    Stdout = $stdoutTask.GetAwaiter().GetResult()
    Stderr = $stderrTask.GetAwaiter().GetResult()
  }
  $process.Dispose()
  $result
}

function Assert-Passes {
  param([object]$Result, [string]$Context)
  Assert-True -Condition ($Result.ExitCode -eq 0) -Message "$Context failed: $($Result.Stderr)$($Result.Stdout)"
  Assert-True -Condition $Result.Stdout.Contains('check-automation-workflow: OK', [StringComparison]::Ordinal) -Message "$Context did not emit OK"
}

function Assert-Fails {
  param([object]$Result, [string]$Context, [string]$Contains)
  Assert-True -Condition ($Result.ExitCode -ne 0) -Message "$Context unexpectedly passed"
  if (-not [string]::IsNullOrWhiteSpace($Contains)) {
    Assert-True -Condition $Result.Stderr.Contains($Contains, [StringComparison]::OrdinalIgnoreCase) -Message "$Context did not report '$Contains': $($Result.Stderr)"
  }
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryRoot "tzg-workflow-checker-test-$testId"
$repositoryRoot = Join-Path $testRoot 'repo'
$automationRoot = Join-Path $testRoot 'automations'
$promptPath = Join-Path $repositoryRoot '开发管理/自动工作流控制器提示词.txt'
$rulesPath = Join-Path $repositoryRoot '开发管理/自动工作流规则.txt'
$recoveryRulesPath = Join-Path $repositoryRoot '开发管理/自动工作流恢复规则.txt'
$maintenanceRulesPath = Join-Path $repositoryRoot '开发管理/状态与建议维护规则.txt'
$statusPath = Join-Path $repositoryRoot '开发管理/自动工作流状态.txt'
$dailyPromptPath = Join-Path $repositoryRoot '开发管理/自动化简报提示词.txt'
$claudePath = Join-Path $repositoryRoot 'CLAUDE.md'
$collaborationPath = Join-Path $repositoryRoot '开发管理/AI协作规则.txt'

$canonicalPrompt = @'
# 每小时自动工作流薄路由

1. 每轮第一项通过 `tools/hourly-automation-lease.ps1` 调用 `Show`；逻辑暂停时立即结束，不读取任何项目路由源。
2. `Show` 返回 recovery 时才读取 `开发管理/自动工作流恢复规则.txt` 并映射 `Recovery`；没有 recovery 时不得读取恢复规则。
3. 无 recovery 时读取 `开发管理/自动工作流规则.txt` 与 `开发管理/当前任务队列.txt`，按行顺序检查，选择第一项当前可安全执行的任务，每轮只为一个 TaskId 调用 `Acquire`。临时运行冲突本轮跳过且不修改任务卡或队列顺序。
4. `codex_execute -> Execution`，`codex_review -> Review`，外部 route 调用既有 wrapper；仅队列为空时调用 `QueueMaintenance` 一次，本轮不执行新任务。Codex 路由只调用 `tools/invoke-codex-responsibility.ps1`。
5. 外部身份先读进程 `ANTHROPIC_BASE_URL`，为空时只补读 `~/.claude/settings.json`；`http://127.0.0.1:15721` 同源地址统一命名为 `DeepSeek V4 Pro`。
6. 固定调用器的 `tools.shell_command` 不得使用 180000 毫秒（三分钟）硬超时；`timeout_ms` 必须设为 3300000 毫秒作为单轮上限，与现有 3600 秒租约对齐并保留 5 分钟边界。
7. 调用返回 `Script running with cell ID ...` 时，保留同一 cell ID 并继续调用 `functions.wait`；空输出、yield 或尚未返回都不是终态，不得据此结束本轮、记录结果、释放租约或启动第二责任方。
8. 外部 AI 返回 completed 后，只核验 `identity=DeepSeek V4 Pro`、`sessionId`、`businessCommit`、`handoffCommit`、提交父子关系、Automation 元数据和相对基线新增未提交路径；全部成立后依次调用 `RecordResult -Category success` 与 `Release`。终态无效且无残留时记录 failed 后释放；存在新增未提交路径时保留现场和租约并转人工阻塞。
9. 最终只报告 route、TaskId、category、sessionId、commitSha 或 recovery 状态。
'@

$canonicalClaude = @'
# Claude / DeepSeek

- 进程 `ANTHROPIC_BASE_URL` 为空时补读 `~/.claude/settings.json`。
- `http://127.0.0.1:15721` 同源地址（含 `/claude-desktop`）实际身份与修改方为 `DeepSeek V4 Pro`。
'@

$canonicalCollaboration = @'
# AI协作规则

- 进程 `ANTHROPIC_BASE_URL` 为空时补读 `~/.claude/settings.json`。
- `http://127.0.0.1:15721` 同源地址（含 `/claude-desktop`）实际身份与修改方为 `DeepSeek V4 Pro`。
'@

$canonicalRules = @'
# 自动工作流规则

- 单写入租约；`Show` 在队列读取前，pauseRequested=true 时结束，recovery 优先并只路由 `开发管理/自动工作流恢复规则.txt`；每轮一个责任方。
- 按 `开发管理/当前任务队列.txt` 的固定行序查找 `dispatchState=ready`，依次识别 `codex_execute`、`external_execute`、`codex_review`，选择第一项当前可安全执行。
- 每行核对当前执行器可用性、`临时运行条件` 与当前路径冲突；临时冲突只跳过本轮，不修改任务卡或队列顺序。
- 同一稳定 fingerprint 连续两轮才逻辑暂停；`明确任务阻塞` 或投影不一致时停止业务执行并完成状态纠正事件。
- `事件发生时` 才更新状态；`队列为空` 时只做一次 QueueMaintenance，`本轮不执行新任务`。
- Codex 只经 `tools/invoke-codex-responsibility.ps1` 启动；固定 `RepositoryRoot` 的 current branch 和 HEAD，不得调用 `using-git-worktrees` 或 `git worktree add`，不得创建 linked worktree 或任务分支。
- runner timeout、deferred wait、workspace guard、automation-finalize-commit.ps1、Automation 元数据、RecordResult 与 Release 边界不变。
- 外部 AI 保留 `businessCommit` 与 `handoffCommit` 的连续双提交 closeout；handoff 不重复统计。
- 失败保留现场、runtime 与日志；不自动 stash、reset、revert、checkout 或 clean。
'@

$canonicalRecoveryRules = @'
# 自动工作流恢复规则

## 读取条件与共同边界

- 本文件只有两个读取条件。
- `Show.recovery != null` 时，恢复 route 读取对应恢复规则；普通 Acquire 收到 RECOVERY_ONLY 后停止。
- 普通责任方实际到达新的用户决定事件时，只读取 `创建决定恢复`；未到达决定事件时不得读取本文件。

## 创建决定恢复

- 仅实际到达新的用户决定事件后进入本节。
- 仅 PROVIDER_ACCEPTED 后调用 SaveRecovery。

## 决定恢复

- decision recovery 后续由 consume-reply.mjs 读取回复，再 Acquire -ResumeRecovery。
- 新责任方必须使用 -Action Start -Route Recovery，不得 `Resume` 原 session。

## 中断恢复

- interruption recovery 才允许 Resume 原 session。

## UTF-8 决定回复

- 自定义回复使用 UTF-8 stdin。

## 失败关闭

- 恢复失败时保留 recovery 和现场。
'@

$canonicalMaintenanceRules = @'
# 状态与建议维护规则

- 队列为空时维护 backlog；本轮不执行新任务。
'@

$canonicalStatus = @'
# 自动工作流状态

- 实时 status 以 automation 配置为准；lease、recovery 和 lastResult 以本机 runtime 为准；业务结果以 Git 为准。
- 当前修复期保持人工暂停，恢复时间由负责人决定。
'@

$canonicalDailyPrompt = @'
# 每日自动化简报

调用 `tools/get-automation-briefing-source.ps1` 取得时间窗内候选；只检查候选 diff 是否支持 Result、Impact、Verify，再按 Task 汇总。不得读取 automation memory，不重复统计 handoff commit。
'@

$canonicalInvoker = 'RepositoryRoot using-git-worktrees git worktree add IO.StreamReader Console]::OpenStandardInput Text.UTF8Encoding'

try {
  foreach ($entry in @{
      '开发管理/自动工作流控制器提示词.txt' = $canonicalPrompt
      '开发管理/自动工作流规则.txt' = $canonicalRules
      '开发管理/自动工作流恢复规则.txt' = $canonicalRecoveryRules
      '开发管理/状态与建议维护规则.txt' = $canonicalMaintenanceRules
      '开发管理/自动工作流状态.txt' = $canonicalStatus
      '开发管理/自动化简报提示词.txt' = $canonicalDailyPrompt
      'CLAUDE.md' = $canonicalClaude
      '开发管理/AI协作规则.txt' = $canonicalCollaboration
      'tools/hourly-automation-lease.ps1' = "schemaVersion = 3`nValidateSet('Show','Acquire','SaveRecovery','SaveInterruption','ClearRecovery','RecordResult','ClearBlocking','Release')"
      'tools/check-task-cards.ps1' = 'task card checker fixture'
      'tools/codex-cli-session.ps1' = 'runner fixture'
      'tools/invoke-codex-responsibility.ps1' = $canonicalInvoker
      'tools/automation-workspace-guard.ps1' = 'guard fixture'
      'tools/automation-finalize-commit.ps1' = 'finalizer fixture'
      'tools/get-automation-briefing-source.ps1' = 'briefing source fixture'
      'tools/feishu-decision-bridge/src/consume-reply.mjs' = 'OPTION_ACCEPTED CUSTOM_ACCEPTED NO_REPLY'
    }.GetEnumerator()) {
    Write-Utf8File -Path (Join-Path $repositoryRoot $entry.Key) -Content $entry.Value
  }
  [IO.Directory]::CreateDirectory($automationRoot) | Out-Null
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-daily-automation-briefing' -Status 'PAUSED' -Prompt $canonicalDailyPrompt

  Assert-Passes -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Paused canonical fixture'

  $existingRecoveryReadLine = '- `Show.recovery != null` 时，恢复 route 读取对应恢复规则；普通 Acquire 收到 RECOVERY_ONLY 后停止。'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules.Replace($existingRecoveryReadLine, '- recovery route 按既有规则处理；普通 Acquire 收到 RECOVERY_ONLY 后停止。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing existing-recovery read condition' -Contains 'recovery read contract'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules

  $newDecisionReadLine = '- 普通责任方实际到达新的用户决定事件时，只读取 `创建决定恢复`；未到达决定事件时不得读取本文件。'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules.Replace($newDecisionReadLine, '- 普通责任方启动时读取恢复规则。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing just-in-time decision read condition' -Contains 'recovery read contract'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules

  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules.Replace('## 创建决定恢复', '## 恢复建立')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing decision-creation section' -Contains 'recovery read contract'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules

  $externalCloseoutLine = '8. 外部 AI 返回 completed 后，只核验 `identity=DeepSeek V4 Pro`、`sessionId`、`businessCommit`、`handoffCommit`、提交父子关系、Automation 元数据和相对基线新增未提交路径；全部成立后依次调用 `RecordResult -Category success` 与 `Release`。终态无效且无残留时记录 failed 后释放；存在新增未提交路径时保留现场和租约并转人工阻塞。'
  $missingExternalCloseout = $canonicalPrompt.Replace($externalCloseoutLine, '8. 外部 AI 返回 completed 后只报告两个提交 SHA。')
  Write-Utf8File -Path $promptPath -Content $missingExternalCloseout
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $missingExternalCloseout
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing external closeout contract' -Contains 'external closeout contract'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $missingCompletedSessionId = $canonicalPrompt.Replace(
    '只核验 `identity=DeepSeek V4 Pro`、`sessionId`、`businessCommit`、`handoffCommit`',
    '只核验 `identity=DeepSeek V4 Pro`、`businessCommit`、`handoffCommit`'
  )
  Write-Utf8File -Path $promptPath -Content $missingCompletedSessionId
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $missingCompletedSessionId
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing completed-gate session ID' -Contains 'external completed gate'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $reversedIdentitySources = $canonicalPrompt.Replace(
    '外部身份先读进程 `ANTHROPIC_BASE_URL`，为空时只补读 `~/.claude/settings.json`',
    '外部身份先读 `~/.claude/settings.json`，为空时只补读进程 `ANTHROPIC_BASE_URL`'
  )
  Write-Utf8File -Path $promptPath -Content $reversedIdentitySources
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $reversedIdentitySources
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Reversed identity source precedence' -Contains 'identity source precedence'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $reversedSuccessCloseout = $canonicalPrompt.Replace(
    '依次调用 `RecordResult -Category success` 与 `Release`',
    '依次调用 `Release` 与 `RecordResult -Category success`'
  )
  Write-Utf8File -Path $promptPath -Content $reversedSuccessCloseout
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $reversedSuccessCloseout
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Reversed success closeout order' -Contains 'success closeout order'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  Write-Utf8File -Path $claudePath -Content $canonicalClaude.Replace('同源地址', '仅该路径')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing DeepSeek identity contract' -Contains 'DeepSeek identity contract'
  Write-Utf8File -Path $claudePath -Content $canonicalClaude

  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt ($canonicalPrompt + "`ndrift")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Paused controller drift' -Contains 'controller prompt'
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $unsafeWaitPrompt = $canonicalPrompt.Replace(
    '7. 调用返回 `Script running with cell ID ...` 时，保留同一 cell ID 并继续调用 `functions.wait`；空输出、yield 或尚未返回都不是终态，不得据此结束本轮、记录结果、释放租约或启动第二责任方。',
    '7. 等待同一次调用返回；不得实施、验证、stage、commit 或启动第二责任方。'
  )
  Write-Utf8File -Path $promptPath -Content $unsafeWaitPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $unsafeWaitPrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing deferred wait contract' -Contains 'deferred wait contract'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $missingRecoveryRoutePrompt = $canonicalPrompt.Replace(
    '2. `Show` 返回 recovery 时才读取 `开发管理/自动工作流恢复规则.txt` 并映射 `Recovery`；没有 recovery 时不得读取恢复规则。',
    '2. `Show` 返回 recovery 时停止本轮。'
  )
  Write-Utf8File -Path $promptPath -Content $missingRecoveryRoutePrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $missingRecoveryRoutePrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing recovery-rule route' -Contains 'recovery rule route'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $unsafeTimeoutPrompt = $canonicalPrompt.Replace(
    '6. 固定调用器的 `tools.shell_command` 不得使用 180000 毫秒（三分钟）硬超时；`timeout_ms` 必须设为 3300000 毫秒作为单轮上限，与现有 3600 秒租约对齐并保留 5 分钟边界。',
    '6. 固定调用器的 `tools.shell_command` 使用 `timeout_ms=180000` 的三分钟硬超时。'
  )
  Write-Utf8File -Path $promptPath -Content $unsafeTimeoutPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $unsafeTimeoutPrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Three-minute invoker timeout' -Contains 'invocation timeout contract'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  Write-Automation -Root $automationRoot -Id 'tzg-daily-automation-briefing' -Status 'PAUSED' -Prompt ($canonicalDailyPrompt + "`ndrift")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Paused daily drift' -Contains 'daily briefing prompt'
  Write-Automation -Root $automationRoot -Id 'tzg-daily-automation-briefing' -Status 'PAUSED' -Prompt $canonicalDailyPrompt

  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireActive) -Context 'Require active while paused' -Contains 'unique ACTIVE'
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'ACTIVE' -Prompt $canonicalPrompt
  Assert-Passes -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireActive) -Context 'Single active controller'
  Write-Automation -Root $automationRoot -Id 'tzg-other-writer' -Status 'ACTIVE' -Prompt $canonicalPrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Multiple active writers' -Contains 'more than one writer'
  Write-Automation -Root $automationRoot -Id 'tzg-other-writer' -Status 'PAUSED' -Prompt $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  Write-Utf8File -Path $promptPath -Content ($canonicalPrompt + "`nBuffer.from('prompt')")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Ad-hoc encoding in controller' -Contains 'forbidden implementation token'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  Write-Utf8File -Path $rulesPath -Content ($canonicalRules + "`nRecordQueueState")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Retired action in rules' -Contains 'retired workflow token'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $leaseFixturePath = Join-Path $repositoryRoot 'tools/hourly-automation-lease.ps1'
  Write-Utf8File -Path $leaseFixturePath -Content "schemaVersion = 2`nValidateSet('Show','Acquire','SaveRecovery','SaveInterruption','ClearRecovery','QueueResume','TakeResume','RecordResult','ClearBlocking','Release')"
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Legacy decision resume runtime' -Contains 'runtime schema 3 contract'
  Write-Utf8File -Path $leaseFixturePath -Content "schemaVersion = 3`nValidateSet('Show','Acquire','SaveRecovery','SaveInterruption','ClearRecovery','RecordResult','ClearBlocking','Release')"

  Write-Utf8File -Path $rulesPath -Content ($canonicalRules + "`n- 三类候选每轮汇总后统一排序。")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Reintroduced unified sorting' -Contains 'unified sorting'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $fixedQueueLine = '- 按 `开发管理/当前任务队列.txt` 的固定行序查找 `dispatchState=ready`，依次识别 `codex_execute`、`external_execute`、`codex_review`，选择第一项当前可安全执行。'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules.Replace($fixedQueueLine, '- 汇总所有候选后选择可执行任务。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing fixed queue order' -Contains 'fixed queue order'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $temporarySkipLine = '- 每行核对当前执行器可用性、`临时运行条件` 与当前路径冲突；临时冲突只跳过本轮，不修改任务卡或队列顺序。'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules.Replace($temporarySkipLine, '- 临时冲突时重新排列任务。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing temporary skip without reorder' -Contains 'temporary skip contract'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $blockingLine = '- 同一稳定 fingerprint 连续两轮才逻辑暂停；`明确任务阻塞` 或投影不一致时停止业务执行并完成状态纠正事件。'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules.Replace($blockingLine, '- 发现阻塞时立即暂停。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing same-fingerprint two-round pause' -Contains 'two-round pause'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $emptyQueueLine = '- `事件发生时` 才更新状态；`队列为空` 时只做一次 QueueMaintenance，`本轮不执行新任务`。'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules.Replace($emptyQueueLine, '- QueueMaintenance 后立即执行新任务。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing empty-queue maintenance-only behavior' -Contains 'empty queue maintenance-only'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  Write-Utf8File -Path $promptPath -Content ($canonicalPrompt + "`n调用 consume-reply.mjs。")
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt ($canonicalPrompt + "`n调用 consume-reply.mjs。")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Recovery detail leaked into prompt' -Contains 'recovery detail leak'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  Write-Utf8File -Path $rulesPath -Content ($canonicalRules + "`nPROVIDER_ACCEPTED 后 SaveRecovery。")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Recovery detail leaked into core rules' -Contains 'recovery detail leak'
  Write-Utf8File -Path $rulesPath -Content $canonicalRules

  $freshDecisionLine = '- 新责任方必须使用 -Action Start -Route Recovery，不得 `Resume` 原 session。'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules.Replace($freshDecisionLine, '- 决定恢复沿用旧 session。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing fresh decision Start' -Contains 'decision recovery Start'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules

  $interruptionLine = '- interruption recovery 才允许 Resume 原 session。'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules.Replace($interruptionLine, '- 所有 recovery 都可以 Resume 原 session。')
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing interruption-only Resume' -Contains 'interruption recovery Resume'
  Write-Utf8File -Path $recoveryRulesPath -Content $canonicalRecoveryRules

  $showLine = '1. 每轮第一项通过 `tools/hourly-automation-lease.ps1` 调用 `Show`；逻辑暂停时立即结束，不读取任何项目路由源。'
  $sourceLine = '3. 无 recovery 时读取 `开发管理/自动工作流规则.txt` 与 `开发管理/当前任务队列.txt`，按行顺序检查，选择第一项当前可安全执行的任务，每轮只为一个 TaskId 调用 `Acquire`。临时运行冲突本轮跳过且不修改任务卡或队列顺序。'
  $lateShowPrompt = $canonicalPrompt.Replace($showLine, '<SHOW-LINE>').Replace($sourceLine, $showLine).Replace('<SHOW-LINE>', $sourceLine)
  Write-Utf8File -Path $promptPath -Content $lateShowPrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Queue read before Show' -Contains 'before queue source'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $invokerFixturePath = Join-Path $repositoryRoot 'tools/invoke-codex-responsibility.ps1'
  Write-Utf8File -Path $invokerFixturePath -Content 'IO.StreamReader Console]::OpenStandardInput Text.UTF8Encoding'
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing fixed root and worktree prohibition' -Contains 'fixed root/worktree contract'
  Write-Utf8File -Path $invokerFixturePath -Content $canonicalInvoker

  Write-Utf8File -Path $invokerFixturePath -Content 'RepositoryRoot using-git-worktrees git worktree add'
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing explicit UTF-8 stdin tokens' -Contains 'UTF-8 stdin contract'
  Write-Utf8File -Path $invokerFixturePath -Content $canonicalInvoker

  Write-Utf8File -Path $statusPath -Content ($canonicalStatus + "`n- 生产入口已恢复为 ACTIVE。")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Static live status claim' -Contains 'live status'
  Write-Utf8File -Path $statusPath -Content $canonicalStatus

  Remove-Item -LiteralPath $invokerFixturePath -Force
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing fixed invoker' -Contains 'missing workflow component'
  Write-Utf8File -Path $invokerFixturePath -Content $canonicalInvoker

  $retiredDecisionTriggerPath = Join-Path $repositoryRoot 'tools/feishu-decision-bridge/src/decision-trigger.mjs'
  Write-Utf8File -Path $retiredDecisionTriggerPath -Content 'retired'
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Retired decision trigger' -Contains 'retired decision trigger'
  [IO.File]::Delete($retiredDecisionTriggerPath)

  $legacyPath = Join-Path $repositoryRoot 'tools/automation-controller.ps1'
  Write-Utf8File -Path $legacyPath -Content 'legacy'
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireLegacyRetired) -Context 'Legacy path' -Contains 'legacy workflow path'
  [IO.File]::Delete($legacyPath)
  Assert-Passes -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireLegacyRetired) -Context 'Retired legacy paths'

  Write-Output 'test-check-automation-workflow: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $prefix = $temporaryRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -ne "tzg-workflow-checker-test-$testId") {
      throw "Refusing unsafe checker-test cleanup: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
