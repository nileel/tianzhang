#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('Start')][string]$Action,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$RunId,
  [Parameter(Mandatory = $true)][string]$Model,
  [Parameter(Mandatory = $true)][string]$OutputSchemaPath,
  [Parameter(Mandatory = $true)][string]$OutputLastMessagePath
)

$ErrorActionPreference = 'Stop'
$runnerExitCode = 1
$childExitCode = $null
$resultStatus = 'failed'
$resultSessionId = $null
$detailCode = 'runner_initialization_failed'
$script:threadStartedCount = 0
$script:threadStartedId = $null

try {
  $detailCode = 'runner_argument_invalid'
  foreach ($value in @($RepositoryRoot, $TaskId, $RunId, $Model, $OutputSchemaPath, $OutputLastMessagePath)) {
    if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[\x00-\x1F\x7F]') { throw 'Required argument is invalid' }
  }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot) -or -not [IO.Path]::IsPathFullyQualified($OutputSchemaPath) -or -not [IO.Path]::IsPathFullyQualified($OutputLastMessagePath)) {
    throw 'Paths must be absolute'
  }
  $root = [IO.Path]::GetFullPath($RepositoryRoot)
  $detailCode = 'runner_path_unavailable'
  if (-not (Test-Path -LiteralPath $root -PathType Container) -or -not (Test-Path -LiteralPath $OutputSchemaPath -PathType Leaf)) { throw 'Required path is unavailable' }
  if (Test-Path -LiteralPath $OutputLastMessagePath) { throw 'OutputLastMessagePath must not already exist' }

  $detailCode = 'runner_input_invalid'
  $stdinReader = [IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false, $true), $false)
  try { $inputText = $stdinReader.ReadToEnd() } finally { $stdinReader.Dispose() }
  $detailCode = 'runner_codex_unavailable'
  $codexCommand = Get-Command codex -CommandType Application -ErrorAction Stop | Select-Object -First 1
  if ($null -eq $codexCommand) { throw 'Codex command was not found' }
  $arguments = @(
    'exec', '--json', '-m', $Model, '-s', 'danger-full-access',
    '--output-schema', $OutputSchemaPath, '--output-last-message', $OutputLastMessagePath, '-'
  )

  Push-Location -LiteralPath $root
  try {
    $detailCode = 'runner_codex_failed'
    $inputText | & $codexCommand @arguments 2>$null | ForEach-Object {
      $line = [string]$_
      if ([string]::IsNullOrWhiteSpace($line)) { return }
      try { $event = $line | ConvertFrom-Json -ErrorAction Stop } catch { return }
      if ([string]$event.type -cne 'thread.started') { return }
      $script:threadStartedCount++
      $property = $event.PSObject.Properties['thread_id']
      $threadId = if ($null -ne $property) { [string]$property.Value } else { $null }
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

  $detailCode = if ($script:threadStartedCount -eq 1) { 'runner_terminal_missing' } else { 'runner_thread_contract_invalid' }
  if ($script:threadStartedCount -eq 1 -and -not [string]::IsNullOrWhiteSpace($script:threadStartedId)) { $resultSessionId = $script:threadStartedId }
  if ($childExitCode -eq 0 -and $null -ne $resultSessionId -and (Test-Path -LiteralPath $OutputLastMessagePath -PathType Leaf)) {
    $resultStatus = 'ok'
    $detailCode = $null
    $runnerExitCode = 0
  }
} catch {
  $resultStatus = 'failed'
} finally {
  [Console]::Out.WriteLine(([ordered]@{
    status = $resultStatus; action = 'Start'; taskId = $TaskId; runId = $RunId
    sessionId = $resultSessionId; exitCode = $childExitCode; detailCode = $detailCode
  } | ConvertTo-Json -Compress))
}

exit $runnerExitCode
