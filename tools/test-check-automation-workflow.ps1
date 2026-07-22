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
$statusPath = Join-Path $repositoryRoot '开发管理/自动工作流状态.txt'
$dailyPromptPath = Join-Path $repositoryRoot '开发管理/自动化简报提示词.txt'

$canonicalPrompt = @'
# 每小时自动工作流薄路由

1. 每轮第一项通过 `tools/hourly-automation-lease.ps1` 调用 `Show`；逻辑暂停时立即结束。
2. 未暂停时读取 `开发管理/自动工作流规则.txt` 和最小候选事实源；恢复原责任优先，否则统一排序。
3. 每轮只选一个执行、复审、外部 AI 或队列维护任务，并调用 `Acquire`。
4. Codex 路由只调用 `tools/invoke-codex-responsibility.ps1`；外部 AI 只调用既有 wrapper。
5. 等待同一次调用返回；不得实施、验证、stage、commit 或启动第二责任方。
6. 最终只报告 route、TaskId、category、sessionId、commitSha 或 recovery 状态。
'@

$canonicalRules = @'
# 自动工作流规则

- 单写入租约；Show 在候选读取前，恢复优先，每轮一个责任方。
- 普通 Acquire 遇到未提交 recovery 返回 RECOVERY_ONLY；原责任方以 Acquire -ResumeRecovery 恢复。
- Codex 只经 tools/invoke-codex-responsibility.ps1 启动；责任方不调用 RecordResult 或 Release。
- 固定调用器只用 Git 元数据和 runtime 核验 completed、waiting_decision、interrupted、failed。
- 外部 AI 保留 businessCommit 与 handoffCommit；handoff 不重复统计。
- 决策只有 PROVIDER_ACCEPTED 后才能 SaveRecovery；旧 pending binding 不是互斥锁。
- 不新增中央 manifest、阶段状态机、checkpoint、重试层或第二套队列。
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

try {
  foreach ($entry in @{
      '开发管理/自动工作流控制器提示词.txt' = $canonicalPrompt
      '开发管理/自动工作流规则.txt' = $canonicalRules
      '开发管理/自动工作流状态.txt' = $canonicalStatus
      '开发管理/自动化简报提示词.txt' = $canonicalDailyPrompt
      'tools/hourly-automation-lease.ps1' = "ValidateSet('Show','Acquire','SaveRecovery','SaveInterruption','ClearRecovery','QueueResume','TakeResume','RecordResult','ClearBlocking','Release')"
      'tools/codex-cli-session.ps1' = 'runner fixture'
      'tools/invoke-codex-responsibility.ps1' = 'invoker fixture'
      'tools/automation-workspace-guard.ps1' = 'guard fixture'
      'tools/automation-finalize-commit.ps1' = 'finalizer fixture'
      'tools/get-automation-briefing-source.ps1' = 'briefing source fixture'
      'tools/feishu-decision-bridge/src/resume-trigger.mjs' = 'pwsh codex-cli-session.ps1 -Action Resume'
    }.GetEnumerator()) {
    Write-Utf8File -Path (Join-Path $repositoryRoot $entry.Key) -Content $entry.Value
  }
  [IO.Directory]::CreateDirectory($automationRoot) | Out-Null
  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt
  Write-Automation -Root $automationRoot -Id 'tzg-daily-automation-briefing' -Status 'PAUSED' -Prompt $canonicalDailyPrompt

  Assert-Passes -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Paused canonical fixture'

  Write-Automation -Root $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt ($canonicalPrompt + "`ndrift")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Paused controller drift' -Contains 'controller prompt'
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

  $showLine = '1. 每轮第一项通过 `tools/hourly-automation-lease.ps1` 调用 `Show`；逻辑暂停时立即结束。'
  $sourceLine = '2. 未暂停时读取 `开发管理/自动工作流规则.txt` 和最小候选事实源；恢复原责任优先，否则统一排序。'
  $lateShowPrompt = $canonicalPrompt.Replace($showLine, '<SHOW-LINE>').Replace($sourceLine, $showLine).Replace('<SHOW-LINE>', $sourceLine)
  Write-Utf8File -Path $promptPath -Content $lateShowPrompt
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Late Show' -Contains 'before routing sources'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  Write-Utf8File -Path $statusPath -Content ($canonicalStatus + "`n- 生产入口已恢复为 ACTIVE。")
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Static live status claim' -Contains 'live status'
  Write-Utf8File -Path $statusPath -Content $canonicalStatus

  Remove-Item -LiteralPath (Join-Path $repositoryRoot 'tools/invoke-codex-responsibility.ps1') -Force
  Assert-Fails -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) -Context 'Missing fixed invoker' -Contains 'missing workflow component'
  Write-Utf8File -Path (Join-Path $repositoryRoot 'tools/invoke-codex-responsibility.ps1') -Content 'invoker fixture'

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
