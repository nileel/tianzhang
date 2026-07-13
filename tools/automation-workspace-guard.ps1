param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Snapshot', 'Check', 'Verify')]
  [string]$Action,

  [string]$RepositoryRoot = (Get-Location).Path,

  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,

  [string]$ExpectedPaths
)

$ErrorActionPreference = 'Stop'
$ExitInvalidArguments = 15
$ExitCandidateConflict = 20
$ExitBaselineChanged = 21

function Invoke-GitRaw {
  param(
    [string]$WorkingDirectory,
    [string[]]$Arguments
  )

  $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.CreateNoWindow = $true
  foreach ($argument in $Arguments) {
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
    $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
      throw "git $($Arguments -join ' ') failed with exit $($process.ExitCode): $($stderr.Trim())"
    }
    [byte[]]$stdoutBytes = $stdout.ToArray()
    return ,([pscustomobject]@{
      Bytes = $stdoutBytes
      Text = [System.Text.UTF8Encoding]::new($false, $true).GetString($stdoutBytes).TrimEnd("`r", "`n")
    })
  } finally {
    $stdout.Dispose()
    $process.Dispose()
  }
}

function Resolve-RepositoryRoot {
  param([string]$Candidate)

  if ([string]::IsNullOrWhiteSpace($Candidate)) {
    throw 'RepositoryRoot must not be empty.'
  }
  $candidatePath = [System.IO.Path]::GetFullPath($Candidate)
  if (-not [System.IO.Directory]::Exists($candidatePath)) {
    throw "RepositoryRoot does not exist: $candidatePath"
  }
  $gitRoot = (Invoke-GitRaw $candidatePath @('rev-parse', '--show-toplevel')).Text
  [System.IO.Path]::GetFullPath($gitRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
}

function ConvertTo-NormalizedExpectedPaths {
  param([string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw 'ExpectedPaths must contain at least one repository-relative path.'
  }

  $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  $result = [System.Collections.Generic.List[string]]::new()
  foreach ($rawPath in $Value.Split('|')) {
    if ([string]::IsNullOrWhiteSpace($rawPath)) {
      throw 'ExpectedPaths contains an empty path.'
    }
    $path = $rawPath.Replace('\', '/')
    if ([System.IO.Path]::IsPathRooted($path) -or $path -match '^[A-Za-z]:') {
      throw "Expected path must be repository-relative: $rawPath"
    }
    while ($path.StartsWith('./', [System.StringComparison]::Ordinal)) {
      $path = $path.Substring(2)
    }
    if ([string]::IsNullOrWhiteSpace($path)) {
      throw 'ExpectedPaths contains an empty path.'
    }
    $segments = $path.Split('/')
    foreach ($segment in $segments) {
      if ([string]::IsNullOrEmpty($segment) -or $segment -eq '.' -or $segment -eq '..') {
        throw "Expected path contains an unsafe segment: $rawPath"
      }
    }
    if ($seen.Add($path)) {
      $result.Add($path)
    }
  }
  $result.ToArray()
}

function Test-PathOverlap {
  param([string]$Left, [string]$Right)

  if ($Left.Equals($Right, [System.StringComparison]::OrdinalIgnoreCase)) {
    return $true
  }
  $leftPrefix = $Left.TrimEnd('/') + '/'
  $rightPrefix = $Right.TrimEnd('/') + '/'
  $Right.StartsWith($leftPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    $Left.StartsWith($rightPrefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-OverlapsAnyExpectedPath {
  param([string]$Path, [string[]]$Expected)

  foreach ($expectedPath in $Expected) {
    if (Test-PathOverlap $Path $expectedPath) {
      return $true
    }
  }
  $false
}

function Get-WorktreeHash {
  param([string]$Repository, [string]$Path)

  $absolutePath = Join-Path $Repository ($Path.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
  if (-not [System.IO.File]::Exists($absolutePath)) {
    return $null
  }
  (Invoke-GitRaw $Repository @('hash-object', '--no-filters', '--', $Path)).Text
}

function New-WorkspaceEntry {
  param(
    [string]$Repository,
    [string]$Path,
    [string]$Kind,
    [string]$IndexStatus,
    [string]$WorktreeStatus,
    [AllowNull()][string]$IndexBlob
  )

  [pscustomobject][ordered]@{
    path = $Path.Replace('\', '/')
    kind = $Kind
    indexStatus = $IndexStatus
    worktreeStatus = $WorktreeStatus
    indexBlob = $IndexBlob
    worktreeHash = Get-WorktreeHash $Repository $Path
  }
}

function Sort-WorkspaceEntries {
  param([object[]]$Entries)

  $list = [System.Collections.Generic.List[object]]::new()
  foreach ($entry in $Entries) { $list.Add($entry) }
  $comparison = [System.Comparison[object]]{
    param($left, $right)
    $result = [System.StringComparer]::OrdinalIgnoreCase.Compare([string]$left.path, [string]$right.path)
    if ($result -eq 0) {
      $result = [System.StringComparer]::Ordinal.Compare([string]$left.path, [string]$right.path)
    }
    $result
  }
  $list.Sort($comparison)
  $list.ToArray()
}

function Get-WorkspaceSnapshot {
  param([string]$Repository)

  $status = Invoke-GitRaw $Repository @('status', '--porcelain=v2', '-z', '--untracked-files=all')
  $text = [string]$status.Text
  $records = $text.Split([char]0)
  $entries = [System.Collections.Generic.List[object]]::new()
  for ($index = 0; $index -lt $records.Length; $index++) {
    $record = $records[$index]
    if ([string]::IsNullOrEmpty($record)) { continue }

    switch ($record[0]) {
      '1' {
        $fields = $record.Split(' ', 9)
        if ($fields.Length -ne 9) { throw "Malformed porcelain v2 ordinary record: $record" }
        $xy = $fields[1]
        $indexBlob = if ($xy[0] -ne '.') { $fields[7] } else { $null }
        $entries.Add((New-WorkspaceEntry $Repository $fields[8] 'ordinary' ([string]$xy[0]) ([string]$xy[1]) $indexBlob))
      }
      '2' {
        $fields = $record.Split(' ', 10)
        if ($fields.Length -ne 10 -or $index + 1 -ge $records.Length) { throw "Malformed porcelain v2 rename/copy record: $record" }
        $xy = $fields[1]
        $newPath = $fields[9]
        $oldPath = $records[++$index]
        if ([string]::IsNullOrEmpty($oldPath)) { throw 'Malformed porcelain v2 rename/copy old path.' }
        $kind = if ($fields[8].StartsWith('C', [System.StringComparison]::Ordinal)) { 'copy' } else { 'rename' }
        $indexBlob = if ($xy[0] -ne '.') { $fields[7] } else { $null }
        $entries.Add((New-WorkspaceEntry $Repository $newPath "$kind-new" ([string]$xy[0]) ([string]$xy[1]) $indexBlob))
        $entries.Add((New-WorkspaceEntry $Repository $oldPath "$kind-old" ([string]$xy[0]) ([string]$xy[1]) $null))
      }
      'u' {
        $fields = $record.Split(' ', 11)
        if ($fields.Length -ne 11) { throw "Malformed porcelain v2 unmerged record: $record" }
        $xy = $fields[1]
        $entries.Add((New-WorkspaceEntry $Repository $fields[10] 'unmerged' ([string]$xy[0]) ([string]$xy[1]) $fields[8]))
      }
      '?' {
        if ($record.Length -lt 3 -or $record[1] -ne ' ') { throw "Malformed porcelain v2 untracked record: $record" }
        $entries.Add((New-WorkspaceEntry $Repository $record.Substring(2) 'untracked' '?' '?' $null))
      }
      '!' { }
      default { throw "Unsupported porcelain v2 record: $record" }
    }
  }

  $head = (Invoke-GitRaw $Repository @('rev-parse', 'HEAD')).Text
  [pscustomobject][ordered]@{
    schemaVersion = 1
    repositoryRoot = $Repository
    head = $head
    entries = @(Sort-WorkspaceEntries $entries.ToArray())
  }
}

function Write-JsonAtomically {
  param([object]$Value, [string]$Path)

  $fullPath = [System.IO.Path]::GetFullPath($Path)
  $directory = Split-Path -Parent $fullPath
  if (-not [System.IO.Directory]::Exists($directory)) {
    throw "Baseline directory does not exist: $directory"
  }
  $temporaryPath = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  try {
    $json = $Value | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($temporaryPath, $json + "`n", [System.Text.UTF8Encoding]::new($false))
    if ([System.IO.File]::Exists($fullPath)) {
      [System.IO.File]::Replace($temporaryPath, $fullPath, $null)
    } else {
      [System.IO.File]::Move($temporaryPath, $fullPath)
    }
  } finally {
    if ([System.IO.File]::Exists($temporaryPath)) {
      [System.IO.File]::Delete($temporaryPath)
    }
  }
}

function Read-Baseline {
  param([string]$Path, [string]$Repository)

  $fullPath = [System.IO.Path]::GetFullPath($Path)
  if (-not [System.IO.File]::Exists($fullPath)) {
    throw "Baseline does not exist: $fullPath"
  }
  $baseline = [System.IO.File]::ReadAllText($fullPath, [System.Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($baseline.schemaVersion -ne 1 -or [string]::IsNullOrWhiteSpace([string]$baseline.repositoryRoot) -or
      [string]::IsNullOrWhiteSpace([string]$baseline.head) -or $null -eq $baseline.entries) {
    throw 'Baseline has an invalid or unsupported schema.'
  }
  $baselineRoot = [System.IO.Path]::GetFullPath([string]$baseline.repositoryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  if (-not $baselineRoot.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Baseline repository root does not match: $baselineRoot"
  }
  $baseline
}

function Get-EntryFingerprint {
  param($Entry)

  $indexBlob = if ($null -eq $Entry.indexBlob) { '<null>' } else { [string]$Entry.indexBlob }
  $worktreeHash = if ($null -eq $Entry.worktreeHash) { '<null>' } else { [string]$Entry.worktreeHash }
  @(
    [string]$Entry.path,
    [string]$Entry.kind,
    [string]$Entry.indexStatus,
    [string]$Entry.worktreeStatus,
    $indexBlob,
    $worktreeHash
  ) -join "`u{001F}"
}

function Get-ChangedWorkspacePaths {
  param($Baseline, $Fresh, [string[]]$Expected)

  $old = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  $new = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @($Baseline.entries)) {
    if (-not (Test-OverlapsAnyExpectedPath ([string]$entry.path) $Expected)) {
      $old[[string]$entry.path] = Get-EntryFingerprint $entry
    }
  }
  foreach ($entry in @($Fresh.entries)) {
    if (-not (Test-OverlapsAnyExpectedPath ([string]$entry.path) $Expected)) {
      $new[[string]$entry.path] = Get-EntryFingerprint $entry
    }
  }

  $changed = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($path in $old.Keys) {
    if (-not $new.ContainsKey($path) -or $old[$path] -ne $new[$path]) { [void]$changed.Add($path) }
  }
  foreach ($path in $new.Keys) {
    if (-not $old.ContainsKey($path) -or $old[$path] -ne $new[$path]) { [void]$changed.Add($path) }
  }
  return ,$changed
}

function Get-HeadRangePaths {
  param([string]$Repository, [string]$BaselineHead, [string]$CurrentHead)

  if ($BaselineHead.Equals($CurrentHead, [System.StringComparison]::OrdinalIgnoreCase)) {
    return @()
  }

  $mergeBase = (Invoke-GitRaw $Repository @('merge-base', $BaselineHead, $CurrentHead)).Text
  if (-not $BaselineHead.Equals($mergeBase, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Current HEAD is not a descendant of the baseline HEAD.'
  }

  $revisionList = (Invoke-GitRaw $Repository @('rev-list', '--reverse', "$BaselineHead..$CurrentHead")).Text
  $paths = [System.Collections.Generic.List[string]]::new()
  foreach ($commit in @($revisionList -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
    $diff = Invoke-GitRaw $Repository @(
      'diff-tree', '--root', '--no-commit-id', '--name-only', '-z', '-r', '-m', '--no-renames', $commit
    )
    foreach ($path in ([string]$diff.Text).Split([char]0)) {
      if (-not [string]::IsNullOrEmpty($path)) {
        $paths.Add($path.Replace('\', '/'))
      }
    }
  }
  $paths.ToArray()
}

function Write-SafetyResult {
  param([bool]$Safe, [string[]]$ConflictingPaths)

  $unique = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($path in $ConflictingPaths) { [void]$unique.Add($path) }
  $sortedList = [System.Collections.Generic.List[string]]::new()
  foreach ($path in $unique) { $sortedList.Add($path) }
  $sortedList.Sort([System.StringComparer]::OrdinalIgnoreCase)
  $sorted = $sortedList.ToArray()
  [pscustomobject][ordered]@{
    safe = $Safe
    conflictingPaths = $sorted
  } | ConvertTo-Json -Compress
}

try {
  if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    throw 'BaselinePath must not be empty.'
  }
  $repository = Resolve-RepositoryRoot $RepositoryRoot

  switch ($Action) {
    'Snapshot' {
      $snapshot = Get-WorkspaceSnapshot $repository
      Write-JsonAtomically $snapshot $BaselinePath
      exit 0
    }
    'Check' {
      try { $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths } catch { [Console]::Error.WriteLine($_.Exception.Message); exit $ExitInvalidArguments }
      $baseline = Read-Baseline $BaselinePath $repository
      $conflicts = @($baseline.entries | Where-Object { Test-OverlapsAnyExpectedPath ([string]$_.path) $expected } | ForEach-Object { [string]$_.path })
      if ($conflicts.Count -gt 0) {
        Write-SafetyResult $false $conflicts
        exit $ExitCandidateConflict
      }
      Write-SafetyResult $true @()
      exit 0
    }
    'Verify' {
      try { $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths } catch { [Console]::Error.WriteLine($_.Exception.Message); exit $ExitInvalidArguments }
      $baseline = Read-Baseline $BaselinePath $repository
      $fresh = Get-WorkspaceSnapshot $repository
      $changed = Get-ChangedWorkspacePaths $baseline $fresh $expected
      foreach ($path in (Get-HeadRangePaths $repository ([string]$baseline.head) ([string]$fresh.head))) {
        if (-not (Test-OverlapsAnyExpectedPath $path $expected)) { [void]$changed.Add($path) }
      }
      if ($changed.Count -gt 0) {
        Write-SafetyResult $false @($changed)
        exit $ExitBaselineChanged
      }
      Write-SafetyResult $true @()
      exit 0
    }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
