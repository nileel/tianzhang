#requires -Version 7.0

[CmdletBinding()]
param(
  [ValidateSet('Start', 'Resume')]
  [string]$Action,
  [string]$RepositoryRoot,
  [string]$TaskId,
  [string]$RunId,
  [string]$SessionId,
  [string]$Model
)

$ErrorActionPreference = 'Stop'
$runnerExitCode = 1
$childExitCode = $null
$resultStatus = 'failed'
$resultSessionId = $null
$script:threadStartedCount = 0
$script:threadStartedId = $null

try {
  if ([string]::IsNullOrWhiteSpace($Action) -or
      [string]::IsNullOrWhiteSpace($RepositoryRoot) -or
      [string]::IsNullOrWhiteSpace($TaskId) -or
      [string]::IsNullOrWhiteSpace($RunId)) {
    throw 'Required argument is missing.'
  }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) {
    throw 'RepositoryRoot must be an absolute path.'
  }

  $resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
  if (-not (Test-Path -LiteralPath $resolvedRepositoryRoot -PathType Container)) {
    throw 'RepositoryRoot does not exist.'
  }
  if ($Action -ceq 'Resume' -and [string]::IsNullOrWhiteSpace($SessionId)) {
    throw 'SessionId is required for Resume.'
  }

  $inputText = [Console]::In.ReadToEnd()
  $codexCommand = Get-Command codex -CommandType Application, ExternalScript -ErrorAction Stop | Select-Object -First 1
  if ($null -eq $codexCommand) {
    throw 'Codex command was not found.'
  }

  $childArguments = if ($Action -ceq 'Start') {
    $arguments = @('exec', '--json')
    if (-not [string]::IsNullOrWhiteSpace($Model)) {
      $arguments += @('-m', $Model)
    }
    $arguments += @('-s', 'danger-full-access', '-')
    $arguments
  } else {
    @('exec', 'resume', '--json', $SessionId, '-')
  }

  Push-Location -LiteralPath $resolvedRepositoryRoot
  try {
    $inputText | & $codexCommand @childArguments 2>$null | ForEach-Object {
      $line = [string]$_
      if ([string]::IsNullOrWhiteSpace($line)) {
        return
      }

      try {
        $event = $line | ConvertFrom-Json -ErrorAction Stop
      } catch {
        return
      }

      if ([string]$event.type -cne 'thread.started') {
        return
      }

      $script:threadStartedCount++
      $threadIdProperty = $event.PSObject.Properties['thread_id']
      $threadId = if ($null -ne $threadIdProperty) { [string]$threadIdProperty.Value } else { $null }
      if ($script:threadStartedCount -eq 1) {
        $script:threadStartedId = $threadId
        [Console]::Error.WriteLine('session_started')
        [Console]::Error.WriteLine('running')
      }
    }
    $childExitCode = if ($null -eq $LASTEXITCODE) { 1 } else { [int]$LASTEXITCODE }
  } finally {
    Pop-Location
  }

  $hasUniqueSession = $script:threadStartedCount -eq 1 -and
    -not [string]::IsNullOrWhiteSpace($script:threadStartedId)
  $sessionMatchesAction = $hasUniqueSession -and
    ($Action -ceq 'Start' -or $script:threadStartedId -ceq $SessionId)
  if ($sessionMatchesAction) {
    $resultSessionId = $script:threadStartedId
  }

  if ($childExitCode -eq 0 -and $sessionMatchesAction) {
    $resultStatus = 'ok'
    $runnerExitCode = 0
  }
} catch {
  $resultStatus = 'failed'
  $runnerExitCode = 1
} finally {
  $summary = [ordered]@{
    status = $resultStatus
    action = $Action
    taskId = $TaskId
    runId = $RunId
    sessionId = $resultSessionId
    exitCode = $childExitCode
  }
  [Console]::Out.WriteLine(($summary | ConvertTo-Json -Compress))
}

exit $runnerExitCode
