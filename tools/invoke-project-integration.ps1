#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$ExpectedMainHead,
  [Parameter(Mandatory = $true)][string]$TargetCommit,
  [Parameter(Mandatory = $true)][string]$ExpectedPaths,
  [ValidateRange(0, 3600)][int]$LockTimeoutSeconds = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'hourly-integration-lock.ps1')

function Invoke-GitText {
  param([string[]]$Arguments)
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = 'git'; $start.WorkingDirectory = $script:root; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $script:root) + $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { throw 'git command failed' }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $code = $process.ExitCode; $process.Dispose()
  if ($code -ne 0) { throw 'git command failed' }
  $stdout.TrimEnd()
}

function Test-PathOverlap {
  param([string]$Left, [string]$Right)
  $a = $Left.Replace('\', '/').TrimEnd('/')
  $b = $Right.Replace('\', '/').TrimEnd('/')
  $a.Equals($b, [StringComparison]::OrdinalIgnoreCase) -or
    $a.StartsWith($b + '/', [StringComparison]::OrdinalIgnoreCase) -or
    $b.StartsWith($a + '/', [StringComparison]::OrdinalIgnoreCase)
}

try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) { throw 'repository root is invalid' }
  $script:root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RepositoryRoot).Path).TrimEnd('\', '/')
  foreach ($sha in @($ExpectedMainHead, $TargetCommit)) {
    if ($sha -cnotmatch '^[0-9a-f]{40,64}$') { throw 'commit identity is invalid' }
  }
  $allowed = @($ExpectedPaths.Split('|') | ForEach-Object { $_.Replace('\', '/').Trim() } | Where-Object { $_ })
  if ($allowed.Count -eq 0) { throw 'expected paths are invalid' }

  $lock = Enter-TzgIntegrationLock -RepositoryRoot $script:root -TimeoutSeconds $LockTimeoutSeconds
  if ($null -eq $lock) {
    [Console]::Out.WriteLine('{"status":"occupied","detailCode":"integration_lock_held"}')
    exit 2
  }
  try {
    if ((Invoke-GitText @('branch', '--show-current')) -cne 'master') { throw 'main branch is not master' }
    if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $ExpectedMainHead) { throw 'main head changed' }
    & git -C $script:root merge-base --is-ancestor $ExpectedMainHead $TargetCommit 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'target is not a fast-forward descendant' }
    $formalPaths = @((Invoke-GitText @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', "$ExpectedMainHead..$TargetCommit")) -split '\r?\n' | Where-Object { $_ })
    foreach ($path in $formalPaths) {
      if (-not @($allowed | Where-Object { Test-PathOverlap -Left $path -Right $_ }).Count) { throw 'target path is outside the authorized set' }
    }
    $dirtyPaths = @((Invoke-GitText @('-c', 'core.quotepath=false', 'status', '--porcelain=v1', '--untracked-files=all')) -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_.Substring(3).Replace('\', '/') })
    foreach ($dirty in $dirtyPaths) {
      foreach ($formal in $formalPaths) {
        if (Test-PathOverlap -Left $dirty -Right $formal) { throw 'main worktree path conflicts with integration' }
      }
    }
    $null = Invoke-GitText @('merge', '--ff-only', $TargetCommit)
    if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $TargetCommit) { throw 'fast-forward verification failed' }
    [Console]::Out.WriteLine(([ordered]@{ status = 'integrated'; previousHead = $ExpectedMainHead; head = $TargetCommit } | ConvertTo-Json -Compress))
  } finally {
    Exit-TzgIntegrationLock -Handle $lock
  }
} catch {
  [Console]::Out.WriteLine('{"status":"failed","detailCode":"project_integration_failed"}')
  exit 1
}
