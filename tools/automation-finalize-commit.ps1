[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Get-Location).Path,
  [Parameter(Mandatory = $true)]
  [string]$ExpectedPaths,
  [Parameter(Mandatory = $true)]
  [string]$CommitMessage
)

$ErrorActionPreference = 'Stop'

function Invoke-GitRaw {
  param([string[]]$Arguments)

  $output = & git -C $script:Repository @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "git $($Arguments -join ' ') failed: $($output -join "`n")"
  }
  @($output)
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

$script:Repository = [System.IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
$gitRoot = ((& git -C $script:Repository rev-parse --show-toplevel 2>&1) -join '').Trim()
if ($LASTEXITCODE -ne 0) { throw "RepositoryRoot is not a Git repository: $RepositoryRoot" }
$resolvedGitRoot = [System.IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/')
if (-not $resolvedGitRoot.Equals($script:Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "RepositoryRoot must be the Git root: $RepositoryRoot"
}
if ([string]::IsNullOrWhiteSpace($CommitMessage)) { throw 'CommitMessage must not be empty.' }

$paths = ConvertTo-NormalizedPaths $ExpectedPaths
$existingFiles = [System.Collections.Generic.List[string]]::new()
foreach ($path in $paths) {
  $fullPath = [System.IO.Path]::GetFullPath((Join-Path $script:Repository $path))
  $prefix = $script:Repository + [System.IO.Path]::DirectorySeparatorChar
  if (-not $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw "Expected path escapes the repository: $path" }
  if (Test-Path -LiteralPath $fullPath -PathType Container) { throw "Expected path must be a file: $path" }
  if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
    [void]$existingFiles.Add($path)
  } else {
    & git -C $script:Repository ls-files --error-unmatch -- $path *> $null
    if ($LASTEXITCODE -ne 0) { throw "Expected path is neither an existing file nor a tracked deletion: $path" }
  }
}

$beforeIndex = Get-IndexEntries
Push-Location $script:Repository
try {
  if ($existingFiles.Count -gt 0) {
    & (Join-Path $PSScriptRoot 'check-pending-whitespace.ps1') -ExpectedPaths ($existingFiles.ToArray() -join '|') -Fix
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace fix failed.' }
    & (Join-Path $PSScriptRoot 'check-pending-whitespace.ps1') -ExpectedPaths ($existingFiles.ToArray() -join '|')
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace verification failed.' }
  }
} finally {
  Pop-Location
}

[void](Invoke-GitRaw (@('add', '--') + $paths))
$afterAddIndex = Get-IndexEntries
Assert-ExternalIndexUnchanged $beforeIndex $afterAddIndex $paths

$stagedPaths = @(Invoke-GitRaw (@('-c', 'core.quotepath=false', 'diff', '--cached', '--name-only', '--') + $paths) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Replace('\', '/') })
foreach ($path in $paths) {
  if (-not @($stagedPaths | Where-Object { Test-OverlapsExpected $_ @($path) }).Count) { throw "Expected path has no staged change: $path" }
}
[void](Invoke-GitRaw @('diff', '--cached', '--check'))
[void](Invoke-GitRaw (@('commit', '--only', '-m', $CommitMessage, '--') + $paths))

$afterCommitIndex = Get-IndexEntries
Assert-ExternalIndexUnchanged $beforeIndex $afterCommitIndex $paths
((Invoke-GitRaw @('rev-parse', 'HEAD')) -join '').Trim()
