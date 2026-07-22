#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Assert-Equal {
  param(
    [AllowNull()]
    [object]$Actual,
    [AllowNull()]
    [object]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ($Actual -ne $Expected) {
    throw "$Message (expected=$Expected actual=$Actual)"
  }
}

function Assert-Match {
  param(
    [AllowNull()]
    [object]$Actual,
    [Parameter(Mandatory = $true)]
    [string]$Pattern,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ([string]$Actual -notmatch $Pattern) {
    throw "$Message (actual=$Actual pattern=$Pattern)"
  }
}

function Assert-NotMatch {
  param(
    [AllowNull()]
    [object]$Actual,
    [Parameter(Mandatory = $true)]
    [string]$Pattern,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ([string]$Actual -match $Pattern) {
    throw "$Message (actual=$Actual pattern=$Pattern)"
  }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runnerPath = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
  throw "Expected implementation is missing: $runnerPath"
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("tzg-codex-cli-session-test-" + [Guid]::NewGuid().ToString('N'))
$fakeBin = Join-Path $testRoot 'bin'
$argsTracePath = Join-Path $testRoot 'args.txt'
$stdinTracePath = Join-Path $testRoot 'stdin.txt'
$fakeCodexPath = Join-Path $fakeBin 'codex.ps1'
$expectedSessionId = '11111111-2222-4333-8444-555555555555'

function Invoke-Runner {
  param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Resume')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$Prompt,
    [Parameter(Mandatory = $true)]
    [string]$Case,
    [string]$SessionId,
    [string]$Model,
    [int]$ChildExitCode = 0
  )

  Remove-Item -LiteralPath $argsTracePath, $stdinTracePath -Force -ErrorAction SilentlyContinue

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @(
      '-NoProfile',
      '-ExecutionPolicy',
      'Bypass',
      '-File',
      $runnerPath,
      '-Action',
      $Action,
      '-RepositoryRoot',
      $repositoryRoot,
      '-TaskId',
      'task-session-test',
      '-RunId',
      'run-session-test'
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
    $startInfo.ArgumentList.Add('-SessionId')
    $startInfo.ArgumentList.Add($SessionId)
  }
  if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $startInfo.ArgumentList.Add('-Model')
    $startInfo.ArgumentList.Add($Model)
  }

  $startInfo.Environment['Path'] = $fakeBin + [IO.Path]::PathSeparator + [Environment]::GetEnvironmentVariable('Path')
  $startInfo.Environment['CODEX_SESSION_TEST_ARGS_PATH'] = $argsTracePath
  $startInfo.Environment['CODEX_SESSION_TEST_STDIN_PATH'] = $stdinTracePath
  $startInfo.Environment['CODEX_SESSION_TEST_CASE'] = $Case
  $startInfo.Environment['CODEX_SESSION_TEST_EXIT_CODE'] = [string]$ChildExitCode
  $startInfo.Environment['CODEX_SESSION_TEST_EXPECTED_ID'] = $expectedSessionId
  $startInfo.Environment['CODEX_SESSION_TEST_SECRET'] = $Prompt

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Failed to start runner process.'
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($Prompt)
  $process.StandardInput.Close()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()

  $stdoutLines = @($stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-Equal -Actual $stdoutLines.Count -Expected 1 -Message "$Case must emit exactly one stdout line"
  try {
    $json = $stdoutLines[0] | ConvertFrom-Json
  } catch {
    throw "$Case stdout is not JSON: $($stdoutLines[0])"
  }

  $stderrLines = @($stderr -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  foreach ($line in $stderrLines) {
    Assert-True -Condition ($line -cin @('session_started', 'running')) -Message "$Case emitted unsafe stderr: $line"
  }
  Assert-NotMatch -Actual ($stdout + $stderr) -Pattern ([regex]::Escape($Prompt)) -Message "$Case leaked prompt or reply"

  [pscustomobject]@{
    ExitCode = $exitCode
    Json = $json
    Stdout = $stdout
    Stderr = $stderr
    StderrLines = $stderrLines
    RecordedArgs = if (Test-Path -LiteralPath $argsTracePath) { [IO.File]::ReadAllText($argsTracePath) } else { $null }
    RecordedStdin = if (Test-Path -LiteralPath $stdinTracePath) { [IO.File]::ReadAllText($stdinTracePath) } else { $null }
  }
}

try {
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  $fakeCodex = @'
$ErrorActionPreference = 'Stop'
$recordedArgs = @($args) -join '|'
$recordedStdin = @($input) -join "`n"
[IO.File]::WriteAllText($env:CODEX_SESSION_TEST_ARGS_PATH, $recordedArgs, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($env:CODEX_SESSION_TEST_STDIN_PATH, $recordedStdin, [Text.UTF8Encoding]::new($false))
Write-Error -Message "child stderr $($env:CODEX_SESSION_TEST_SECRET)" -ErrorAction Continue

$threadId = $env:CODEX_SESSION_TEST_EXPECTED_ID
switch ($env:CODEX_SESSION_TEST_CASE) {
  'start-success' {
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
    [pscustomobject]@{
      type = 'item.completed'
      item = @{ type = 'agent_message'; text = $env:CODEX_SESSION_TEST_SECRET }
    } | ConvertTo-Json -Compress -Depth 5
  }
  'resume-success' {
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
  }
  'resume-non-json' {
    Write-Output 'codex-cli informational diagnostic'
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
  }
  'missing-thread' {
    [pscustomobject]@{
      type = 'item.completed'
      item = @{ type = 'agent_message'; thread_id = $threadId }
    } | ConvertTo-Json -Compress -Depth 5
  }
  'duplicate-thread' {
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
  }
  'mismatched-thread' {
    [pscustomobject]@{ type = 'thread.started'; thread_id = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee' } | ConvertTo-Json -Compress
  }
  'child-failed' {
    [pscustomobject]@{ type = 'thread.started'; thread_id = $threadId } | ConvertTo-Json -Compress
  }
  default {
    throw "Unknown fake case: $($env:CODEX_SESSION_TEST_CASE)"
  }
}
$global:LASTEXITCODE = [int]$env:CODEX_SESSION_TEST_EXIT_CODE
'@
  [IO.File]::WriteAllText($fakeCodexPath, $fakeCodex, [Text.UTF8Encoding]::new($false))

  $startPrompt = 'secret-start-marker-7ee5f0'
  $start = Invoke-Runner -Action Start -Prompt $startPrompt -Case 'start-success'
  Assert-Equal -Actual $start.ExitCode -Expected 0 -Message 'Start process failed'
  Assert-Equal -Actual $start.Json.status -Expected 'ok' -Message 'Start status mismatch'
  Assert-Equal -Actual $start.Json.action -Expected 'Start' -Message 'Start action mismatch'
  Assert-Equal -Actual $start.Json.taskId -Expected 'task-session-test' -Message 'Start task mismatch'
  Assert-Equal -Actual $start.Json.runId -Expected 'run-session-test' -Message 'Start run mismatch'
  Assert-Equal -Actual $start.Json.sessionId -Expected $expectedSessionId -Message 'Start session mismatch'
  Assert-Equal -Actual $start.Json.exitCode -Expected 0 -Message 'Start child exit mismatch'
  Assert-Match -Actual $start.RecordedArgs -Pattern '^exec\|--json\|-s\|danger-full-access\|-$' -Message 'Start argv mismatch'
  Assert-Equal -Actual $start.RecordedStdin -Expected $startPrompt -Message 'Start stdin mismatch'
  Assert-Equal -Actual $start.StderrLines.Count -Expected 2 -Message 'Start progress count mismatch'
  Assert-Equal -Actual $start.StderrLines[0] -Expected 'session_started' -Message 'Start first progress mismatch'
  Assert-Equal -Actual $start.StderrLines[1] -Expected 'running' -Message 'Start second progress mismatch'

  $explicitModel = 'gpt-5.6-terra'
  $startWithModel = Invoke-Runner `
    -Action Start `
    -Prompt 'secret-model-marker-6b4f8d' `
    -Case 'start-success' `
    -Model $explicitModel
  Assert-Equal -Actual $startWithModel.ExitCode -Expected 0 -Message 'Start with model process failed'
  Assert-Equal -Actual $startWithModel.Json.status -Expected 'ok' -Message 'Start with model status mismatch'
  Assert-Match `
    -Actual $startWithModel.RecordedArgs `
    -Pattern ('^exec\|--json\|-m\|' + [regex]::Escape($explicitModel) + '\|-s\|danger-full-access\|-$') `
    -Message 'Start model argv mismatch'

  $resumePrompt = 'secret-resume-marker-e72ac1'
  $resume = Invoke-Runner -Action Resume -Prompt $resumePrompt -Case 'resume-success' -SessionId $expectedSessionId
  Assert-Equal -Actual $resume.ExitCode -Expected 0 -Message 'Resume process failed'
  Assert-Equal -Actual $resume.Json.status -Expected 'ok' -Message 'Resume status mismatch'
  Assert-Equal -Actual $resume.Json.sessionId -Expected $expectedSessionId -Message 'Resume session mismatch'
  Assert-Match -Actual $resume.RecordedArgs -Pattern ('^exec\|resume\|--json\|' + [regex]::Escape($expectedSessionId) + '\|-$') -Message 'Resume argv mismatch'
  Assert-Equal -Actual $resume.RecordedStdin -Expected $resumePrompt -Message 'Resume stdin mismatch'

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

  foreach ($failure in @(
      @{ Case = 'missing-thread'; Action = 'Start'; SessionId = $null; ChildExitCode = 0 },
      @{ Case = 'duplicate-thread'; Action = 'Start'; SessionId = $null; ChildExitCode = 0 },
      @{ Case = 'mismatched-thread'; Action = 'Resume'; SessionId = $expectedSessionId; ChildExitCode = 0 },
      @{ Case = 'child-failed'; Action = 'Start'; SessionId = $null; ChildExitCode = 9 }
    )) {
    $failurePrompt = "secret-$($failure.Case)-marker"
    $result = Invoke-Runner `
      -Action $failure.Action `
      -Prompt $failurePrompt `
      -Case $failure.Case `
      -SessionId $failure.SessionId `
      -ChildExitCode $failure.ChildExitCode
    Assert-True -Condition ($result.ExitCode -ne 0) -Message "$($failure.Case) process unexpectedly succeeded"
    Assert-Equal -Actual $result.Json.status -Expected 'failed' -Message "$($failure.Case) status mismatch"
    Assert-Equal -Actual $result.Json.exitCode -Expected $failure.ChildExitCode -Message "$($failure.Case) child exit mismatch"
  }

  Write-Output 'test-codex-cli-session: OK'
} finally {
  $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
  $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
  if ($resolvedTestRoot.StartsWith($resolvedTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
      (Split-Path -Leaf $resolvedTestRoot).StartsWith('tzg-codex-cli-session-test-', [StringComparison]::Ordinal)) {
    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
}
