#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Invoke-Case {
  param([string]$Name, [int]$ChildExitCode, [bool]$DuplicateThread = $false)
  $outputPath = Join-Path $testRoot "$Name-last.json"
  $env:TZG_CODEX_TEST_EXIT = [string]$ChildExitCode
  $env:TZG_CODEX_TEST_DUPLICATE = if ($DuplicateThread) { '1' } else { '0' }
  $env:TZG_CODEX_TEST_TRACE = Join-Path $testRoot "$Name-trace.json"
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = 'pwsh'; $start.WorkingDirectory = $repositoryRoot; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
  $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
  foreach ($argument in @('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,'-Action','Start','-RepositoryRoot',$repositoryRoot,'-TaskId','TASK-CLI','-RunId','RUN-CLI','-Model','gpt-5.6-terra','-OutputSchemaPath',$schemaPath,'-OutputLastMessagePath',$outputPath)) { $start.ArgumentList.Add($argument) }
  $start.Environment['PATH'] = "$fakeBin$([IO.Path]::PathSeparator)$originalPath"
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  Assert-True $process.Start() 'runner did not start'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.StandardInput.Write($prompt); $process.StandardInput.Close(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult().Trim(); $stderr = $stderrTask.GetAwaiter().GetResult().Trim(); $exitCode = $process.ExitCode; $process.Dispose()
  $lines = @($stdout -split '\r?\n' | Where-Object { $_ })
  Assert-True ($lines.Count -eq 1) "$Name emitted multiple stdout lines"
  Assert-True (Test-Path -LiteralPath $env:TZG_CODEX_TEST_TRACE) "$Name did not invoke fake codex (stdout=$stdout stderr=$stderr)"
  [pscustomobject]@{ ExitCode=$exitCode; Json=($lines[0] | ConvertFrom-Json); Stderr=$stderr; Trace=([IO.File]::ReadAllText($env:TZG_CODEX_TEST_TRACE) | ConvertFrom-Json); OutputPath=$outputPath }
}

$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$runner = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-codex-cli-structured-$([Guid]::NewGuid().ToString('N'))"
$fakeBin = Join-Path $testRoot 'bin'; $schemaPath = Join-Path $testRoot 'schema.json'
$prompt = 'secret-structured-terminal-marker'
$originalPath = $env:PATH
try {
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  [IO.File]::WriteAllText($schemaPath, '{"type":"object"}', [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $fakeBin 'codex.ps1'), @'
$CliArguments = @($args)
$promptText = @($input) -join "`n"
$lastIndex = [Array]::IndexOf($CliArguments, '--output-last-message')
$lastPath = $CliArguments[$lastIndex + 1]
[IO.File]::WriteAllText($lastPath, '{"status":"verified"}', [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($env:TZG_CODEX_TEST_TRACE, ([ordered]@{ arguments=$CliArguments; prompt=$promptText } | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
$event = [ordered]@{ type='thread.started'; thread_id='11111111-2222-4333-8444-555555555555' } | ConvertTo-Json -Compress
Write-Output $event
if ($env:TZG_CODEX_TEST_DUPLICATE -ceq '1') { Write-Output $event }
exit ([int]$env:TZG_CODEX_TEST_EXIT)
'@, [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $fakeBin 'codex.cmd'), "@echo off`r`npwsh -NoProfile -ExecutionPolicy Bypass -File `"%~dp0codex.ps1`" %*`r`n", [Text.UTF8Encoding]::new($false))

  $success = Invoke-Case -Name success -ChildExitCode 0
  Assert-True ($success.ExitCode -eq 0 -and [string]$success.Json.status -ceq 'ok') 'structured Start failed'
  Assert-True ([string]$success.Json.action -ceq 'Start') 'runner exposed a non-Start action'
  Assert-True ([string]$success.Trace.prompt -ceq $prompt) 'stdin prompt changed'
  $joined = @($success.Trace.arguments) -join '|'
  Assert-True ($joined -match '^exec\|--json\|-m\|gpt-5\.6-terra\|-s\|danger-full-access\|--output-schema\|.+\|--output-last-message\|.+\|-$') 'structured argv contract mismatch'
  Assert-True ($joined -notmatch 'resume') 'retired resume path remained callable'
  Assert-True (($success.Stderr -split '\r?\n') -join '|' -ceq 'session_started|running') 'progress output mismatch'
  Assert-True (($success.Json | ConvertTo-Json -Compress) -notmatch [regex]::Escape($prompt)) 'prompt leaked to terminal JSON'

  $failed = Invoke-Case -Name failed -ChildExitCode 7
  Assert-True ($failed.ExitCode -ne 0 -and [string]$failed.Json.status -ceq 'failed') 'child failure was accepted'
  $duplicate = Invoke-Case -Name duplicate -ChildExitCode 0 -DuplicateThread $true
  Assert-True ($duplicate.ExitCode -ne 0 -and [string]$duplicate.Json.status -ceq 'failed') 'duplicate thread event was accepted'
  Write-Output 'test-codex-cli-session: OK'
} finally {
  $env:PATH = $originalPath
  if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
