[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Get-Location).Path,
  [Parameter(Mandatory = $true)]
  [string]$ExpectedPaths,
  [Parameter(Mandatory = $true)]
  [string]$CommitMessage,
  [switch]$RequireAutomationMetadata,
  [string]$AutomationTask,
  [string]$AutomationState,
  [string]$AutomationResult,
  [string]$AutomationImpact,
  [string]$AutomationVerify
)

$ErrorActionPreference = 'Stop'

function Assert-AutomationMetadata {
  param([Parameter(Mandatory = $true)][string]$Message)

  $singleLine = '[^\r\n]*\S[^\r\n]*'
  $pattern = "\A$singleLine\r?\n\r?\n" +
    'Automation: tzg-hourly-controller\r?\n' +
    "Task: $singleLine\r?\n" +
    'State: (?:completed|pending_review)\r?\n' +
    "Result: $singleLine\r?\n" +
    "Impact: $singleLine\r?\n" +
    "Verify: $singleLine\r?\n?\z"

  if (-not [regex]::IsMatch($Message, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
    throw 'CommitMessage does not match the required tzg-hourly-controller metadata format.'
  }
}

function New-AutomationCommitMessage {
  param(
    [Parameter(Mandatory = $true)][string]$Subject,
    [string]$Task,
    [string]$State,
    [string]$Result,
    [string]$Impact,
    [string]$Verify
  )

  foreach ($field in @{
      Subject = $Subject
      Task = $Task
      Result = $Result
      Impact = $Impact
      Verify = $Verify
    }.GetEnumerator()) {
    if ([string]::IsNullOrWhiteSpace([string]$field.Value) -or [string]$field.Value -match '[\r\n]') {
      throw "$($field.Key) must be non-empty single-line text."
    }
  }
  if ($State -notin @('completed', 'pending_review')) {
    throw 'AutomationState must be completed or pending_review.'
  }

  @(
    $Subject,
    '',
    'Automation: tzg-hourly-controller',
    "Task: $Task",
    "State: $State",
    "Result: $Result",
    "Impact: $Impact",
    "Verify: $Verify"
  ) -join "`n"
}

function Invoke-GitRaw {
  param([string[]]$Arguments)

  $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.WorkingDirectory = $script:Repository
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.CreateNoWindow = $true
  foreach ($argument in @('-C', $script:Repository) + $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }

  $process = [System.Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Unable to start git.'
  }

  $stdout = [System.IO.MemoryStream]::new()
  try {
    $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [void]$stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
      throw "git $($Arguments -join ' ') failed: $($stderr.Trim())"
    }
    $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($stdout.ToArray()).TrimEnd("`r", "`n")
    if ($text.Length -gt 0) {
      $text -split "`r?`n"
    }
  } finally {
    $stdout.Dispose()
    $process.Dispose()
  }
}

function ConvertTo-NormalizedPaths {
  param([string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) { throw 'ExpectedPaths must contain at least one path.' }
  $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  $result = [System.Collections.Generic.List[string]]::new()
  foreach ($rawPath in $Value.Split('|')) {
    if ([string]::IsNullOrWhiteSpace($rawPath)) { throw 'ExpectedPaths contains an empty path.' }
    $path = $rawPath.Replace('\', '/')
    while ($path.StartsWith('./', [System.StringComparison]::Ordinal)) { $path = $path.Substring(2) }
    if ([System.IO.Path]::IsPathRooted($path) -or $path -match '^[A-Za-z]:') { throw "Expected path must be repository-relative: $rawPath" }
    foreach ($segment in $path.Split('/')) {
      if ([string]::IsNullOrEmpty($segment) -or $segment -eq '.' -or $segment -eq '..') { throw "Expected path contains an unsafe segment: $rawPath" }
    }
    if ($path.Equals('.git', [System.StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith('.git/', [System.StringComparison]::OrdinalIgnoreCase)) {
      throw 'ExpectedPaths must not target Git administrative data.'
    }
    if ($seen.Add($path)) { [void]$result.Add($path) }
  }
  return ,$result.ToArray()
}

function Test-OverlapsExpected {
  param([string]$Path, [string[]]$Expected)

  foreach ($expectedPath in $Expected) {
    if ($Path.Equals($expectedPath, [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($expectedPath + '/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $expectedPath.StartsWith($Path + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
      return $true
    }
  }
  $false
}

function Get-IndexEntries {
  $entries = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
  foreach ($line in @(Invoke-GitRaw @('-c', 'core.quotepath=false', 'ls-files', '--stage'))) {
    if ([string]::IsNullOrEmpty($line)) { continue }
    $separator = $line.IndexOf("`t")
    if ($separator -lt 0) { throw "Unexpected git ls-files --stage output: $line" }
    $path = $line.Substring($separator + 1).Replace('\', '/')
    $entries[$path] = $line.Substring(0, $separator)
  }
  $entries
}

function Assert-ExternalIndexUnchanged {
  param($Before, $After, [string[]]$Expected)

  $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($path in $Before.Keys) { if (-not (Test-OverlapsExpected $path $Expected)) { [void]$paths.Add($path) } }
  foreach ($path in $After.Keys) { if (-not (Test-OverlapsExpected $path $Expected)) { [void]$paths.Add($path) } }
  foreach ($path in $paths) {
    $beforeValue = if ($Before.ContainsKey($path)) { $Before[$path] } else { $null }
    $afterValue = if ($After.ContainsKey($path)) { $After[$path] } else { $null }
    if ($beforeValue -cne $afterValue) { throw "Unrelated staged entry changed: $path" }
  }
}

function Test-DiffChanged {
  param([string[]]$Arguments)

  & git -C $script:Repository @Arguments *> $null
  if ($LASTEXITCODE -eq 0) { return $false }
  if ($LASTEXITCODE -eq 1) { return $true }
  throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE"
}

function Test-PathChanged {
  param([string]$Path)

  if (Test-DiffChanged @('diff', '--quiet', '--cached', '--', $Path)) { return $true }
  if (Test-DiffChanged @('diff', '--quiet', '--', $Path)) { return $true }
  & git -C $script:Repository ls-files --error-unmatch -- $Path *> $null
  $isTracked = $LASTEXITCODE -eq 0
  if (-not $isTracked) {
    return Test-Path -LiteralPath (Join-Path $script:Repository $Path) -PathType Leaf
  }
  $false
}

function Test-PathNeedsStaging {
  param([string]$Path)

  if (Test-DiffChanged @('diff', '--quiet', '--', $Path)) { return $true }
  & git -C $script:Repository ls-files --error-unmatch -- $Path *> $null
  if ($LASTEXITCODE -eq 0) { return $false }
  Test-Path -LiteralPath (Join-Path $script:Repository $Path) -PathType Leaf
}

$script:Repository = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$gitRoot = ((Invoke-GitRaw @('rev-parse', '--show-toplevel')) -join '').Trim()
$resolvedGitRoot = [System.IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/')
if (-not $resolvedGitRoot.Equals($script:Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "RepositoryRoot must be the Git root: $RepositoryRoot"
}
if ([string]::IsNullOrWhiteSpace($CommitMessage)) { throw 'CommitMessage must not be empty.' }
if ($RequireAutomationMetadata) {
  $CommitMessage = New-AutomationCommitMessage `
    -Subject $CommitMessage `
    -Task $AutomationTask `
    -State $AutomationState `
    -Result $AutomationResult `
    -Impact $AutomationImpact `
    -Verify $AutomationVerify
  Assert-AutomationMetadata -Message $CommitMessage
}

$paths = ConvertTo-NormalizedPaths $ExpectedPaths
foreach ($path in $paths) {
  $fullPath = [System.IO.Path]::GetFullPath((Join-Path $script:Repository $path))
  $prefix = $script:Repository + [System.IO.Path]::DirectorySeparatorChar
  if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Expected path escapes the repository: $path" }
  if (Test-Path -LiteralPath $fullPath -PathType Container) { throw "Expected path must be a file: $path" }
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    & git -C $script:Repository ls-files --error-unmatch -- $path *> $null
    if ($LASTEXITCODE -ne 0) { continue }
  }
}

$changedPaths = @($paths | Where-Object { Test-PathChanged $_ })
if ($changedPaths.Count -eq 0) { throw 'No expected path has a staged, unstaged, untracked, or deleted change.' }
$changedExistingFiles = @($changedPaths | Where-Object { Test-Path -LiteralPath (Join-Path $script:Repository $_) -PathType Leaf })

$beforeIndex = Get-IndexEntries
Push-Location $script:Repository
try {
  if ($changedExistingFiles.Count -gt 0) {
    & (Join-Path $PSScriptRoot 'check-pending-whitespace.ps1') -ExpectedPaths ($changedExistingFiles -join '|')
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace verification failed.' }
  }
} finally {
  Pop-Location
}

$pathsToStage = @($changedPaths | Where-Object { Test-PathNeedsStaging $_ })
if ($pathsToStage.Count -gt 0) {
  [void](Invoke-GitRaw (@('add', '--') + $pathsToStage))
}
$afterAddIndex = Get-IndexEntries
Assert-ExternalIndexUnchanged $beforeIndex $afterAddIndex $changedPaths

$stagedPaths = @(Invoke-GitRaw (@('-c', 'core.quotepath=false', 'diff', '--cached', '--no-renames', '--name-only', '--') + $changedPaths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/') })
foreach ($path in $changedPaths) {
  if (-not @($stagedPaths | Where-Object { Test-OverlapsExpected $_ @($path) }).Count) { throw "Expected path has no staged change: $path" }
}
[void](Invoke-GitRaw @('diff', '--cached', '--check'))
[void](Invoke-GitRaw (@('commit', '--only', '-m', $CommitMessage, '--') + $changedPaths))

$afterCommitIndex = Get-IndexEntries
Assert-ExternalIndexUnchanged $beforeIndex $afterCommitIndex $changedPaths
((Invoke-GitRaw @('rev-parse', 'HEAD')) -join '').Trim()
