#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$script:canaryStage = 'initialize'
$script:canaryDiagnostic = [ordered]@{}

function Assert-Canary {
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

function Invoke-CanaryProcess {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FileName,
    [string[]]$Arguments = @(),
    [Parameter(Mandatory = $true)]
    [string]$WorkingDirectory,
    [AllowNull()]
    [string]$InputText,
    [ValidateRange(1, 900)]
    [int]$TimeoutSeconds = 120
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $FileName
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  if ($null -ne $InputText) {
    $startInfo.RedirectStandardInput = $true
    $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  }
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "Unable to start $FileName"
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if ($null -ne $InputText) {
    $process.StandardInput.Write($InputText)
    $process.StandardInput.Close()
  }
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try {
      $process.Kill($true)
    } catch {
      # Best effort process-tree cleanup after the single allowed invocation times out.
    }
    $process.Dispose()
    throw "$FileName timed out"
  }
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()

  [pscustomobject]@{
    ExitCode = $exitCode
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Invoke-CanaryGit {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string[]]$Arguments
  )

  $result = Invoke-CanaryProcess `
    -FileName 'git' `
    -Arguments $Arguments `
    -WorkingDirectory $RepositoryRoot `
    -InputText $null
  if ($result.ExitCode -ne 0) {
    throw "Git command failed: $($Arguments[0])"
  }
  $result.Stdout.Trim()
}

function Invoke-SessionRunner {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RunnerPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Resume')]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$CanaryRepository,
    [Parameter(Mandatory = $true)]
    [string]$Prompt,
    [string]$SessionId,
    [ValidateRange(1, 900)]
    [int]$TimeoutSeconds
  )

  $arguments = @(
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    $RunnerPath,
    '-Action',
    $Action,
    '-RepositoryRoot',
    $CanaryRepository,
    '-TaskId',
    'codex-cli-session-canary',
    '-RunId',
    'codex-cli-session-canary-run'
  )
  if ($Action -ceq 'Resume') {
    $arguments += @('-SessionId', $SessionId)
  }

  $script:canaryDiagnostic = [ordered]@{
    action = $Action
    processExit = $null
    stdoutLineCount = $null
    stderrLineCount = $null
    stderrTokensValid = $null
    jsonParsed = $false
    runnerStatus = $null
    runnerAction = $null
    childExit = $null
    sessionIdPresent = $false
  }
  $result = Invoke-CanaryProcess `
    -FileName 'pwsh' `
    -Arguments $arguments `
    -WorkingDirectory $CanaryRepository `
    -InputText $Prompt `
    -TimeoutSeconds $TimeoutSeconds
  $script:canaryDiagnostic.processExit = $result.ExitCode
  $stdoutLines = @($result.Stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $script:canaryDiagnostic.stdoutLineCount = $stdoutLines.Count
  Assert-Canary -Condition ($stdoutLines.Count -eq 1) -Message 'Runner stdout contract failed.'
  try {
    $summary = $stdoutLines[0] | ConvertFrom-Json
    $script:canaryDiagnostic.jsonParsed = $true
    $script:canaryDiagnostic.runnerStatus = [string]$summary.status
    $script:canaryDiagnostic.runnerAction = [string]$summary.action
    $script:canaryDiagnostic.childExit = $summary.exitCode
    $script:canaryDiagnostic.sessionIdPresent = -not [string]::IsNullOrWhiteSpace([string]$summary.sessionId)
  } catch {
    throw 'Runner stdout was not valid JSON.'
  }
  $stderrLines = @($result.Stderr -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $script:canaryDiagnostic.stderrLineCount = $stderrLines.Count
  $script:canaryDiagnostic.stderrTokensValid = $true
  foreach ($line in $stderrLines) {
    if ($line -cnotin @('session_started', 'running')) {
      $script:canaryDiagnostic.stderrTokensValid = $false
    }
    Assert-Canary `
      -Condition ($line -cin @('session_started', 'running')) `
      -Message 'Runner stderr contract failed.'
  }
  Assert-Canary -Condition ($result.ExitCode -eq 0) -Message 'Runner process failed.'
  Assert-Canary -Condition ($summary.status -ceq 'ok') -Message 'Runner child status failed.'
  Assert-Canary -Condition ($summary.action -ceq $Action) -Message 'Runner action mismatch.'
  Assert-Canary -Condition ($summary.exitCode -eq 0) -Message 'Codex child process failed.'
  Assert-Canary `
    -Condition (-not [string]::IsNullOrWhiteSpace([string]$summary.sessionId)) `
    -Message 'Runner did not return a session ID.'
  $summary
}

$sourceRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$runnerPath = Join-Path $sourceRoot 'tools\codex-cli-session.ps1'
Assert-Canary -Condition (Test-Path -LiteralPath $runnerPath -PathType Leaf) -Message 'Runner is missing.'

$canaryId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$canaryRoot = Join-Path $temporaryBase "tzg-codex-cli-session-canary-$canaryId"
$canaryRepository = Join-Path $canaryRoot 'repo'
$marker = 'tzg-continuity-' + [Convert]::ToHexString(
  [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
).ToLowerInvariant()

try {
  [IO.Directory]::CreateDirectory($canaryRepository) | Out-Null
  [IO.File]::WriteAllText(
    (Join-Path $canaryRepository 'AGENTS.md'),
    @'
# Codex CLI Session Canary

- This is an isolated system-temporary repository. Never access another repository.
- Follow the current prompt exactly; do not create extra files or commits.
- All file edits must use apply_patch. Do not push, stash, reset, checkout, clean, retry, or dispatch another agent.
'@,
    [Text.UTF8Encoding]::new($false)
  )
  [IO.File]::WriteAllText(
    (Join-Path $canaryRepository 'README.md'),
    "# Same-session canary`n",
    [Text.UTF8Encoding]::new($false)
  )
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('init', '-q') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.name', 'Codex Session Canary') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.email', 'codex-session-canary@example.invalid') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'core.autocrlf', 'false') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'commit.gpgsign', 'false') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('add', '--', 'AGENTS.md', 'README.md') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('commit', '-q', '-m', 'test: initialize same-session canary') | Out-Null
  $initialHead = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $initialCommitCount = [int](Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-list', '--count', 'HEAD'))

  $script:canaryStage = 'start_runner'
  $startPrompt = @"
You are running a same-session continuity canary in this isolated temporary Git repository.
Remember this exact marker for the next turn: $marker
Do not create, modify, delete, stage, or commit any file. Do not reveal or repeat the marker in your final response. End with a brief acknowledgement that you are waiting for continuation.
"@
  $started = Invoke-SessionRunner `
    -RunnerPath $runnerPath `
    -Action Start `
    -CanaryRepository $canaryRepository `
    -Prompt $startPrompt `
    -TimeoutSeconds 300
  $sessionId = [string]$started.sessionId
  $script:canaryStage = 'start_repository_clean'
  Assert-Canary `
    -Condition ((Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) -ceq '') `
    -Message 'Repository changed during Start.'
  $script:canaryStage = 'start_head_unchanged'
  Assert-Canary `
    -Condition ((Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')) -ceq $initialHead) `
    -Message 'Start created a commit.'

  $script:canaryStage = 'resume_prompt_isolation'
  $resumePrompt = @'
Continue the same canary. Retrieve the exact marker from the preceding turn. Use apply_patch to create continuity.txt containing exactly that marker followed by one newline. Modify no other file. Then run `git add -- continuity.txt` and `git commit -m "test: prove Codex session continuity"`. Do not print the marker in your final response. Do not retry any failed operation.
'@
  Assert-Canary -Condition (-not $resumePrompt.Contains($marker)) -Message 'Resume prompt contains the marker.'
  $script:canaryStage = 'resume_runner'
  $resumed = Invoke-SessionRunner `
    -RunnerPath $runnerPath `
    -Action Resume `
    -CanaryRepository $canaryRepository `
    -Prompt $resumePrompt `
    -SessionId $sessionId `
    -TimeoutSeconds 600
  $script:canaryStage = 'resume_session_identity'
  Assert-Canary -Condition ([string]$resumed.sessionId -ceq $sessionId) -Message 'Resume returned a different session ID.'

  $script:canaryStage = 'verify_continuity_file'
  $continuityPath = Join-Path $canaryRepository 'continuity.txt'
  Assert-Canary -Condition (Test-Path -LiteralPath $continuityPath -PathType Leaf) -Message 'continuity.txt is missing.'
  $continuityText = [IO.File]::ReadAllText($continuityPath, [Text.UTF8Encoding]::new($false, $true))
  Assert-Canary -Condition ($continuityText -ceq "$marker`n") -Message 'Continuity marker mismatch.'
  $script:canaryStage = 'verify_commit_count'
  $finalCommitCount = [int](Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-list', '--count', 'HEAD'))
  Assert-Canary -Condition ($finalCommitCount -eq ($initialCommitCount + 1)) -Message 'Canary did not create exactly one commit.'
  $commitPaths = Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('-c', 'core.quotepath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD')
  $script:canaryStage = 'verify_commit_paths'
  Assert-Canary -Condition ($commitPaths -ceq 'continuity.txt') -Message 'Canary commit changed an unauthorized path.'
  $script:canaryStage = 'verify_repository_clean'
  Assert-Canary `
    -Condition ((Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) -ceq '') `
    -Message 'Temporary repository is not clean.'

  Write-Output "test-codex-cli-session-canary: OK sessionId=$sessionId commits=1"
} catch {
  $failureType = $_.Exception.GetType().Name
  $safeDiagnostic = $script:canaryDiagnostic | ConvertTo-Json -Compress
  [Console]::Error.WriteLine(
    "test-codex-cli-session-canary: FAILED stage=$script:canaryStage type=$failureType diagnostic=$safeDiagnostic"
  )
  throw 'Codex same-session canary failed.'
} finally {
  if (Test-Path -LiteralPath $canaryRoot) {
    $resolvedRoot = [IO.Path]::GetFullPath($canaryRoot)
    $temporaryPrefix = $temporaryBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $leaf = Split-Path -Leaf $resolvedRoot
    if (
      -not $resolvedRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) `
      -or $leaf -ne "tzg-codex-cli-session-canary-$canaryId"
    ) {
      throw "Refusing unsafe canary cleanup: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  }
}
