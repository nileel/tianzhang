param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Snapshot', 'Check', 'Verify', 'CaptureRecoveryEvidence', 'CaptureInterruptionEvidence', 'CheckRecovery')]
  [string]$Action,

  [string]$RepositoryRoot = (Get-Location).Path,

  [Parameter(Mandatory = $true)]
  [string]$BaselinePath,

  [string]$ExpectedPaths,

  [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$ExitInvalidArguments = 15
$ExitCandidateConflict = 20
$ExitBaselineChanged = 21
$ExitRecoveryExpectedChanged = 22

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

function Assert-ExpectedPathsSafe {
  param([string]$Repository, [string[]]$Expected)

  $repositoryPrefix = $Repository.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
  $gitlinks = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
  $indexRecords = ([string](Invoke-GitRaw $Repository @('ls-files', '--stage', '-z')).Text).Split([char]0)
  foreach ($record in $indexRecords) {
    if ([string]::IsNullOrEmpty($record)) { continue }
    $tabIndex = $record.IndexOf([char]9)
    if ($tabIndex -lt 0) { throw 'Malformed git ls-files --stage record.' }
    $metadata = $record.Substring(0, $tabIndex)
    if ($metadata.StartsWith('160000 ', [System.StringComparison]::Ordinal)) {
      [void]$gitlinks.Add($record.Substring($tabIndex + 1).Replace('\', '/'))
    }
  }

  foreach ($path in $Expected) {
    if ($path.Equals('.git', [System.StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith('.git/', [System.StringComparison]::OrdinalIgnoreCase)) {
      throw 'ExpectedPaths must not target Git administrative data.'
    }
    foreach ($gitlink in $gitlinks) {
      if ($path.Equals($gitlink, [System.StringComparison]::OrdinalIgnoreCase) -or
          $path.StartsWith($gitlink + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'expected_path_enters_submodule'
      }
    }

    $current = $Repository
    $segments = $path.Split('/')
    for ($index = 0; $index -lt $segments.Length; $index++) {
      $current = Join-Path $current $segments[$index]
      if (-not (Test-Path -LiteralPath $current)) { break }
      $item = Get-Item -LiteralPath $current -Force
      if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Expected path crosses a reparse point: $path"
      }
      $resolved = (Resolve-Path -LiteralPath $current).Path
      if (-not $resolved.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase) -and
          -not $resolved.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected path resolves outside the repository: $path"
      }
    }
  }
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
    [AllowNull()]$IndexBlob,
    [string]$StatusFingerprint
  )

  [pscustomobject][ordered]@{
    path = $Path.Replace('\', '/')
    kind = $Kind
    indexStatus = $IndexStatus
    worktreeStatus = $WorktreeStatus
    indexBlob = if ($null -eq $IndexBlob) { $null } else { [string]$IndexBlob }
    worktreeHash = Get-WorktreeHash $Repository $Path
    statusFingerprint = $StatusFingerprint
  }
}

function Sort-WorkspaceEntries {
  param([object[]]$Entries)

  $list = [System.Collections.Generic.List[object]]::new()
  foreach ($entry in $Entries) { $list.Add($entry) }
  $comparison = [System.Comparison[object]]{
    param($left, $right)
    [System.StringComparer]::Ordinal.Compare([string]$left.path, [string]$right.path)
  }
  $list.Sort($comparison)
  $list.ToArray()
}

function Sort-OrdinalPaths {
  param([string[]]$Paths)

  $unique = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($path in $Paths) { [void]$unique.Add($path) }
  $list = [System.Collections.Generic.List[string]]::new()
  foreach ($path in $unique) { $list.Add($path) }
  $list.Sort([System.StringComparer]::Ordinal)
  $list.ToArray()
}

function Get-Sha256Text {
  param([string]$Value)

  $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Value)
  $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
  [System.Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-BaselinePayloadHash {
  param($Snapshot)

  $canonicalEntries = @($Snapshot.entries | ForEach-Object {
    [pscustomobject][ordered]@{
      path = [string]$_.path
      kind = [string]$_.kind
      indexStatus = [string]$_.indexStatus
      worktreeStatus = [string]$_.worktreeStatus
      indexBlob = if ($null -eq $_.indexBlob) { $null } else { [string]$_.indexBlob }
      worktreeHash = if ($null -eq $_.worktreeHash) { $null } else { [string]$_.worktreeHash }
      statusFingerprint = [string]$_.statusFingerprint
    }
  })
  $canonicalEntries = @(Sort-WorkspaceEntries $canonicalEntries)
  $payload = [pscustomobject][ordered]@{
    repositoryRoot = [string]$Snapshot.repositoryRoot
    head = [string]$Snapshot.head
    entries = $canonicalEntries
  }
  Get-Sha256Text ($payload | ConvertTo-Json -Depth 8 -Compress)
}

function Get-RecoveryEvidencePayloadHash {
  param($Evidence)

  $canonicalEntries = @($Evidence.expectedEntries | ForEach-Object {
    [pscustomobject][ordered]@{
      path = [string]$_.path
      kind = [string]$_.kind
      indexStatus = [string]$_.indexStatus
      worktreeStatus = [string]$_.worktreeStatus
      indexBlob = if ($null -eq $_.indexBlob) { $null } else { [string]$_.indexBlob }
      worktreeHash = if ($null -eq $_.worktreeHash) { $null } else { [string]$_.worktreeHash }
      statusFingerprint = [string]$_.statusFingerprint
    }
  })
  if ([long]$Evidence.schemaVersion -eq 2) {
    $payload = [pscustomobject][ordered]@{
      purpose = [string]$Evidence.purpose
      repositoryRoot = [string]$Evidence.repositoryRoot
      baselinePayloadHash = [string]$Evidence.baselinePayloadHash
      head = [string]$Evidence.head
      expectedPaths = @($Evidence.expectedPaths | ForEach-Object { [string]$_ })
      expectedChangedPaths = @(Sort-OrdinalPaths @($Evidence.expectedChangedPaths | ForEach-Object { [string]$_ }))
      expectedEntries = @(Sort-WorkspaceEntries $canonicalEntries)
    }
  } else {
    $payload = [pscustomobject][ordered]@{
      repositoryRoot = [string]$Evidence.repositoryRoot
      baselinePayloadHash = [string]$Evidence.baselinePayloadHash
      head = [string]$Evidence.head
      expectedPaths = @($Evidence.expectedPaths | ForEach-Object { [string]$_ })
      expectedEntries = @(Sort-WorkspaceEntries $canonicalEntries)
    }
  }
  Get-Sha256Text ($payload | ConvertTo-Json -Depth 8 -Compress)
}

function Get-WorkspaceSnapshot {
  param([string]$Repository)

  $status = Invoke-GitRaw $Repository @('status', '--porcelain=v2', '-z', '--untracked-files=all', '--ignore-submodules=none')
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
        if ($fields[2] -ne 'N...') { throw 'dirty_submodule_unsupported' }
        $xy = $fields[1]
        $indexBlob = if ($xy[0] -ne '.') { $fields[7] } else { $null }
        $fingerprint = Get-Sha256Text ($record + [char]0)
        $entries.Add((New-WorkspaceEntry $Repository $fields[8] 'ordinary' ([string]$xy[0]) ([string]$xy[1]) $indexBlob $fingerprint))
      }
      '2' {
        $fields = $record.Split(' ', 10)
        if ($fields.Length -ne 10 -or $index + 1 -ge $records.Length) { throw "Malformed porcelain v2 rename/copy record: $record" }
        if ($fields[2] -ne 'N...') { throw 'dirty_submodule_unsupported' }
        $xy = $fields[1]
        $newPath = $fields[9]
        $oldPath = $records[++$index]
        if ([string]::IsNullOrEmpty($oldPath)) { throw 'Malformed porcelain v2 rename/copy old path.' }
        $kind = if ($fields[8].StartsWith('C', [System.StringComparison]::Ordinal)) { 'copy' } else { 'rename' }
        $indexBlob = if ($xy[0] -ne '.') { $fields[7] } else { $null }
        $fingerprint = Get-Sha256Text ($record + [char]0 + $oldPath + [char]0)
        $entries.Add((New-WorkspaceEntry $Repository $newPath "$kind-new" ([string]$xy[0]) ([string]$xy[1]) $indexBlob $fingerprint))
        $entries.Add((New-WorkspaceEntry $Repository $oldPath "$kind-old" ([string]$xy[0]) ([string]$xy[1]) $null $fingerprint))
      }
      'u' {
        $fields = $record.Split(' ', 11)
        if ($fields.Length -ne 11) { throw "Malformed porcelain v2 unmerged record: $record" }
        if ($fields[2] -ne 'N...') { throw 'dirty_submodule_unsupported' }
        $xy = $fields[1]
        $fingerprint = Get-Sha256Text ($record + [char]0)
        $entries.Add((New-WorkspaceEntry $Repository $fields[10] 'unmerged' ([string]$xy[0]) ([string]$xy[1]) $fields[8] $fingerprint))
      }
      '?' {
        if ($record.Length -lt 3 -or $record[1] -ne ' ') { throw "Malformed porcelain v2 untracked record: $record" }
        $fingerprint = Get-Sha256Text ($record + [char]0)
        $entries.Add((New-WorkspaceEntry $Repository $record.Substring(2) 'untracked' '?' '?' $null $fingerprint))
      }
      '!' { }
      default { throw "Unsupported porcelain v2 record: $record" }
    }
  }

  $head = (Invoke-GitRaw $Repository @('rev-parse', 'HEAD')).Text
  $snapshot = [pscustomobject][ordered]@{
    schemaVersion = 2
    repositoryRoot = $Repository
    head = $head
    entries = @(Sort-WorkspaceEntries $entries.ToArray())
    payloadHash = $null
  }
  $snapshot.payloadHash = Get-BaselinePayloadHash $snapshot
  $snapshot
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
      [System.IO.File]::Move($temporaryPath, $fullPath, $true)
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
  if ($baseline.schemaVersion -isnot [long] -or $baseline.schemaVersion -ne 2 -or
      $baseline.repositoryRoot -isnot [string] -or [string]::IsNullOrWhiteSpace($baseline.repositoryRoot) -or
      $baseline.head -isnot [string] -or $baseline.head -notmatch '^[0-9a-f]{40,64}$' -or
      $baseline.entries -isnot [System.Array] -or
      $baseline.payloadHash -isnot [string] -or $baseline.payloadHash -notmatch '^[0-9a-f]{64}$') {
    throw 'Baseline has an invalid or unsupported schema; create a new Snapshot.'
  }
  $requiredEntryProperties = @('path', 'kind', 'indexStatus', 'worktreeStatus', 'indexBlob', 'worktreeHash', 'statusFingerprint')
  $paths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($entry in @($baseline.entries)) {
    foreach ($property in $requiredEntryProperties) {
      if ($entry.PSObject.Properties.Name -notcontains $property) { throw "Baseline entry is missing '$property'." }
    }
    if ($entry.path -isnot [string] -or [string]::IsNullOrEmpty($entry.path) -or $entry.path.Contains('\') -or
        [System.IO.Path]::IsPathRooted($entry.path) -or @($entry.path.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0 -or
        $entry.kind -isnot [string] -or [string]::IsNullOrEmpty($entry.kind) -or
        $entry.indexStatus -isnot [string] -or $entry.indexStatus.Length -ne 1 -or
        $entry.worktreeStatus -isnot [string] -or $entry.worktreeStatus.Length -ne 1 -or
        ($null -ne $entry.indexBlob -and ($entry.indexBlob -isnot [string] -or $entry.indexBlob -notmatch '^[0-9a-f]{40,64}$')) -or
        ($null -ne $entry.worktreeHash -and ($entry.worktreeHash -isnot [string] -or $entry.worktreeHash -notmatch '^[0-9a-f]{40,64}$')) -or
        $entry.statusFingerprint -isnot [string] -or $entry.statusFingerprint -notmatch '^[0-9a-f]{64}$') {
      throw 'Baseline entry has an invalid type or value.'
    }
    if (-not $paths.Add([string]$entry.path)) { throw "Baseline contains a duplicate path: $($entry.path)" }
  }
  $actualHash = Get-BaselinePayloadHash $baseline
  if (-not $actualHash.Equals([string]$baseline.payloadHash, [System.StringComparison]::Ordinal)) {
    throw 'Baseline payload hash does not match its contents.'
  }
  $baselineRoot = [System.IO.Path]::GetFullPath([string]$baseline.repositoryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  if (-not $baselineRoot.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Baseline repository root does not match: $baselineRoot"
  }
  $baseline
}

function Read-RecoveryEvidence {
  param([string]$Path, [string]$Repository)

  if ([string]::IsNullOrWhiteSpace($Path)) { throw 'EvidencePath must not be empty.' }
  $fullPath = [System.IO.Path]::GetFullPath($Path)
  if (-not [System.IO.File]::Exists($fullPath)) { throw "Recovery evidence does not exist: $fullPath" }
  $evidence = [System.IO.File]::ReadAllText($fullPath, [System.Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($evidence.schemaVersion -isnot [long] -or ($evidence.schemaVersion -ne 1 -and $evidence.schemaVersion -ne 2) -or
      $evidence.repositoryRoot -isnot [string] -or [string]::IsNullOrWhiteSpace($evidence.repositoryRoot) -or
      $evidence.baselinePayloadHash -isnot [string] -or $evidence.baselinePayloadHash -notmatch '^[0-9a-f]{64}$' -or
      $evidence.head -isnot [string] -or $evidence.head -notmatch '^[0-9a-f]{40,64}$' -or
      $evidence.expectedPaths -isnot [System.Array] -or @($evidence.expectedPaths).Count -eq 0 -or
      $evidence.expectedEntries -isnot [System.Array] -or
      $evidence.payloadHash -isnot [string] -or $evidence.payloadHash -notmatch '^[0-9a-f]{64}$') {
    throw 'Recovery evidence has an invalid or unsupported schema.'
  }
  if ($evidence.schemaVersion -eq 1 -and @($evidence.expectedEntries).Count -eq 0) {
    throw 'Recovery evidence has an invalid or unsupported schema.'
  }
  if ($evidence.schemaVersion -eq 2 -and
      ($evidence.purpose -isnot [string] -or $evidence.purpose -cne 'interruption' -or
       $evidence.expectedChangedPaths -isnot [System.Array] -or @($evidence.expectedChangedPaths).Count -eq 0)) {
    throw 'Recovery evidence has an invalid or unsupported schema.'
  }
  foreach ($pathValue in @($evidence.expectedPaths)) {
    if ($pathValue -isnot [string] -or [string]::IsNullOrWhiteSpace($pathValue) -or $pathValue.Contains('\') -or [System.IO.Path]::IsPathRooted($pathValue)) {
      throw 'Recovery evidence contains an invalid expected path.'
    }
  }
  if ($evidence.schemaVersion -eq 2) {
    $changedPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($pathValue in @($evidence.expectedChangedPaths)) {
      if ($pathValue -isnot [string] -or [string]::IsNullOrWhiteSpace($pathValue) -or $pathValue.Contains('\') -or
          [System.IO.Path]::IsPathRooted($pathValue) -or -not (Test-OverlapsAnyExpectedPath $pathValue @($evidence.expectedPaths)) -or
          -not $changedPathSet.Add($pathValue)) {
        throw 'Recovery evidence contains an invalid changed expected path.'
      }
    }
    $recordedChangedPaths = @($evidence.expectedChangedPaths | ForEach-Object { [string]$_ })
    $sortedChangedPaths = @(Sort-OrdinalPaths $recordedChangedPaths)
    if (($recordedChangedPaths | ConvertTo-Json -Compress) -cne ($sortedChangedPaths | ConvertTo-Json -Compress)) {
      throw 'Recovery evidence changed expected paths are not sorted.'
    }
  }
  $requiredEntryProperties = @('path', 'kind', 'indexStatus', 'worktreeStatus', 'indexBlob', 'worktreeHash', 'statusFingerprint')
  foreach ($entry in @($evidence.expectedEntries)) {
    foreach ($property in $requiredEntryProperties) {
      if ($entry.PSObject.Properties.Name -notcontains $property) { throw "Recovery evidence entry is missing '$property'." }
    }
    if ($entry.path -isnot [string] -or [string]::IsNullOrEmpty($entry.path) -or $entry.path.Contains('\') -or
        $entry.kind -isnot [string] -or [string]::IsNullOrEmpty($entry.kind) -or
        $entry.indexStatus -isnot [string] -or $entry.indexStatus.Length -ne 1 -or
        $entry.worktreeStatus -isnot [string] -or $entry.worktreeStatus.Length -ne 1 -or
        ($null -ne $entry.indexBlob -and ($entry.indexBlob -isnot [string] -or $entry.indexBlob -notmatch '^[0-9a-f]{40,64}$')) -or
        ($null -ne $entry.worktreeHash -and ($entry.worktreeHash -isnot [string] -or $entry.worktreeHash -notmatch '^[0-9a-f]{40,64}$')) -or
        $entry.statusFingerprint -isnot [string] -or $entry.statusFingerprint -notmatch '^[0-9a-f]{64}$') {
      throw 'Recovery evidence contains an invalid entry.'
    }
  }
  $actualHash = Get-RecoveryEvidencePayloadHash $evidence
  if (-not $actualHash.Equals([string]$evidence.payloadHash, [System.StringComparison]::Ordinal)) { throw 'Recovery evidence payload hash does not match.' }
  $evidenceRoot = [System.IO.Path]::GetFullPath([string]$evidence.repositoryRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
  if (-not $evidenceRoot.Equals($Repository, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Recovery evidence repository root does not match.' }
  $evidence
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
    $worktreeHash,
    [string]$Entry.statusFingerprint
  ) -join "`u{001F}"
}

function Get-WorkspaceChangePartition {
  param($Baseline, $Fresh, [string[]]$Expected)

  $old = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
  $new = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
  foreach ($entry in @($Baseline.entries)) {
    $old[[string]$entry.path] = Get-EntryFingerprint $entry
  }
  foreach ($entry in @($Fresh.entries)) {
    $new[[string]$entry.path] = Get-EntryFingerprint $entry
  }

  $allChanged = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($path in $old.Keys) {
    if (-not $new.ContainsKey($path) -or $old[$path] -ne $new[$path]) { [void]$allChanged.Add($path) }
  }
  foreach ($path in $new.Keys) {
    if (-not $old.ContainsKey($path) -or $old[$path] -ne $new[$path]) { [void]$allChanged.Add($path) }
  }

  $expectedChanged = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  $outsideChanged = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($path in $allChanged) {
    if (Test-OverlapsAnyExpectedPath $path $Expected) {
      [void]$expectedChanged.Add($path)
    } else {
      [void]$outsideChanged.Add($path)
    }
  }

  [pscustomobject]@{
    expectedChangedPaths = $expectedChanged
    outsideChangedPaths = $outsideChanged
  }
}

function Get-ChangedWorkspacePaths {
  param($Baseline, $Fresh, [string[]]$Expected)

  $partition = Get-WorkspaceChangePartition $Baseline $Fresh $Expected
  return ,$partition.outsideChangedPaths
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
  param(
    [bool]$Safe,
    [string[]]$Expected,
    [string[]]$ConflictingPaths,
    [AllowNull()][string]$Reason
  )

  $unique = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
  foreach ($path in $ConflictingPaths) { [void]$unique.Add($path) }
  $sortedList = [System.Collections.Generic.List[string]]::new()
  foreach ($path in $unique) { $sortedList.Add($path) }
  $sortedList.Sort([System.StringComparer]::Ordinal)
  $sorted = $sortedList.ToArray()
  [pscustomobject][ordered]@{
    safe = $Safe
    expectedPaths = @($Expected)
    conflictingPaths = $sorted
    reason = if ([string]::IsNullOrEmpty($Reason)) { $null } else { $Reason }
  } | ConvertTo-Json -Compress
}

function Write-InterruptionResult {
  param(
    [string]$Classification,
    [string[]]$Expected,
    [string[]]$ChangedExpectedPaths,
    [string[]]$ConflictingPaths,
    [AllowNull()][string]$Reason,
    [AllowNull()][string]$EvidenceHash
  )

  [pscustomobject][ordered]@{
    safe = $Classification -ne 'unsafe'
    classification = $Classification
    expectedPaths = @($Expected)
    changedExpectedPaths = @(Sort-OrdinalPaths $ChangedExpectedPaths)
    conflictingPaths = @(Sort-OrdinalPaths $ConflictingPaths)
    reason = if ([string]::IsNullOrEmpty($Reason)) { $null } else { $Reason }
    evidenceHash = if ([string]::IsNullOrEmpty($EvidenceHash)) { $null } else { $EvidenceHash }
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
    'CaptureRecoveryEvidence' {
      $expected = @()
      try {
        $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths
        Assert-ExpectedPathsSafe $repository $expected
        if ([string]::IsNullOrWhiteSpace($EvidencePath)) { throw 'EvidencePath must not be empty.' }
      } catch {
        $reason = if ($_.Exception.Message -eq 'expected_path_enters_submodule') { 'expected_path_enters_submodule' } else { 'invalid_arguments' }
        Write-SafetyResult $false $expected @() $reason
        exit $ExitInvalidArguments
      }
      try { $baseline = Read-Baseline $BaselinePath $repository } catch { Write-SafetyResult $false $expected @() 'baseline_invalid'; exit 1 }
      try { $fresh = Get-WorkspaceSnapshot $repository } catch {
        if ($_.Exception.Message -ne 'dirty_submodule_unsupported') { throw }
        Write-SafetyResult $false $expected @('<SUBMODULE>') 'dirty_submodule_unsupported'
        exit $ExitBaselineChanged
      }
      $changed = Get-ChangedWorkspacePaths $baseline $fresh $expected
      if (-not ([string]$baseline.head).Equals([string]$fresh.head, [System.StringComparison]::Ordinal)) { [void]$changed.Add('<HEAD>') }
      if ($changed.Count -gt 0) {
        Write-SafetyResult $false $expected @($changed) 'baseline_changed'
        exit $ExitBaselineChanged
      }
      $expectedEntries = @($fresh.entries | Where-Object { Test-OverlapsAnyExpectedPath ([string]$_.path) $expected })
      if ($expectedEntries.Count -eq 0) {
        Write-SafetyResult $false $expected @() 'recovery_expected_missing'
        exit $ExitRecoveryExpectedChanged
      }
      $evidence = [pscustomobject][ordered]@{
        schemaVersion = 1
        repositoryRoot = $repository
        baselinePayloadHash = [string]$baseline.payloadHash
        head = [string]$fresh.head
        expectedPaths = @($expected)
        expectedEntries = @(Sort-WorkspaceEntries $expectedEntries)
        payloadHash = $null
      }
      $evidence.payloadHash = Get-RecoveryEvidencePayloadHash $evidence
      Write-JsonAtomically $evidence $EvidencePath
      [pscustomobject][ordered]@{
        safe = $true
        expectedPaths = @($expected)
        conflictingPaths = @()
        reason = $null
        evidenceHash = [string]$evidence.payloadHash
      } | ConvertTo-Json -Compress
      exit 0
    }
    'CaptureInterruptionEvidence' {
      $expected = @()
      try {
        $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths
        Assert-ExpectedPathsSafe $repository $expected
        if ([string]::IsNullOrWhiteSpace($EvidencePath)) { throw 'EvidencePath must not be empty.' }
      } catch {
        $reason = if ($_.Exception.Message -eq 'expected_path_enters_submodule') { 'expected_path_enters_submodule' } else { 'invalid_arguments' }
        Write-InterruptionResult 'unsafe' $expected @() @() $reason $null
        exit $ExitInvalidArguments
      }
      try { $baseline = Read-Baseline $BaselinePath $repository } catch {
        Write-InterruptionResult 'unsafe' $expected @() @() 'baseline_invalid' $null
        exit 1
      }
      try { $fresh = Get-WorkspaceSnapshot $repository } catch {
        if ($_.Exception.Message -ne 'dirty_submodule_unsupported') { throw }
        Write-InterruptionResult 'unsafe' $expected @() @('<SUBMODULE>') 'dirty_submodule_unsupported' $null
        exit $ExitBaselineChanged
      }

      $partition = Get-WorkspaceChangePartition $baseline $fresh $expected
      $changedExpectedPaths = @(Sort-OrdinalPaths @($partition.expectedChangedPaths))
      $conflictingPaths = [System.Collections.Generic.List[string]]::new()
      foreach ($path in $partition.outsideChangedPaths) { $conflictingPaths.Add($path) }
      $headChanged = -not ([string]$baseline.head).Equals([string]$fresh.head, [System.StringComparison]::Ordinal)
      if ($headChanged) { $conflictingPaths.Add('<HEAD>') }

      if ($conflictingPaths.Count -gt 0) {
        $reason = if ($headChanged) { 'head_changed' } else { 'baseline_changed' }
        Write-InterruptionResult 'unsafe' $expected $changedExpectedPaths $conflictingPaths.ToArray() $reason $null
        exit $ExitBaselineChanged
      }
      if ($changedExpectedPaths.Count -eq 0) {
        Write-InterruptionResult 'clean' $expected @() @() $null $null
        exit 0
      }

      $expectedEntries = @($fresh.entries | Where-Object {
        $null -ne $_.worktreeHash -and (Test-OverlapsAnyExpectedPath ([string]$_.path) $expected)
      })
      $evidence = [pscustomobject][ordered]@{
        schemaVersion = 2
        purpose = 'interruption'
        repositoryRoot = $repository
        baselinePayloadHash = [string]$baseline.payloadHash
        head = [string]$fresh.head
        expectedPaths = @($expected)
        expectedChangedPaths = @($changedExpectedPaths)
        expectedEntries = @(Sort-WorkspaceEntries $expectedEntries)
        payloadHash = $null
      }
      $evidence.payloadHash = Get-RecoveryEvidencePayloadHash $evidence
      Write-JsonAtomically $evidence $EvidencePath
      Write-InterruptionResult 'recoverable' $expected $changedExpectedPaths @() $null ([string]$evidence.payloadHash)
      exit 0
    }
    'CheckRecovery' {
      $expected = @()
      try {
        $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths
        Assert-ExpectedPathsSafe $repository $expected
      } catch {
        $reason = if ($_.Exception.Message -eq 'expected_path_enters_submodule') { 'expected_path_enters_submodule' } else { 'invalid_arguments' }
        Write-SafetyResult $false $expected @() $reason
        exit $ExitInvalidArguments
      }
      try { $baseline = Read-Baseline $BaselinePath $repository } catch { Write-SafetyResult $false $expected @() 'baseline_invalid'; exit 1 }
      try { $evidence = Read-RecoveryEvidence $EvidencePath $repository } catch { Write-SafetyResult $false $expected @() 'recovery_evidence_invalid'; exit 1 }
      $requestedPaths = ($expected | ConvertTo-Json -Compress)
      $recordedPaths = (@($evidence.expectedPaths) | ConvertTo-Json -Compress)
      if ($evidence.baselinePayloadHash -cne $baseline.payloadHash -or $requestedPaths -cne $recordedPaths) {
        Write-SafetyResult $false $expected @() 'recovery_evidence_invalid'
        exit 1
      }
      try { $fresh = Get-WorkspaceSnapshot $repository } catch {
        if ($_.Exception.Message -ne 'dirty_submodule_unsupported') { throw }
        Write-SafetyResult $false $expected @('<SUBMODULE>') 'dirty_submodule_unsupported'
        exit $ExitBaselineChanged
      }
      $changed = Get-ChangedWorkspacePaths $baseline $fresh $expected
      if (-not ([string]$fresh.head).Equals([string]$evidence.head, [System.StringComparison]::Ordinal)) { [void]$changed.Add('<HEAD>') }
      if ($changed.Count -gt 0) {
        Write-SafetyResult $false $expected @($changed) 'baseline_changed'
        exit $ExitBaselineChanged
      }
      $currentExpectedEntries = @($fresh.entries | Where-Object { Test-OverlapsAnyExpectedPath ([string]$_.path) $expected })
      if ($evidence.schemaVersion -eq 2) {
        $currentExpectedEntries = @($currentExpectedEntries | Where-Object { $null -ne $_.worktreeHash })
      }
      $currentExpected = @($currentExpectedEntries | ForEach-Object { Get-EntryFingerprint $_ })
      $recordedExpected = @($evidence.expectedEntries | ForEach-Object { Get-EntryFingerprint $_ })
      $expectedChanged = $false
      $expectedConflicts = @($fresh.entries | Where-Object { Test-OverlapsAnyExpectedPath ([string]$_.path) $expected } | ForEach-Object { [string]$_.path })
      if (($currentExpected | ConvertTo-Json -Compress) -cne ($recordedExpected | ConvertTo-Json -Compress)) {
        $expectedChanged = $true
      }
      if ($evidence.schemaVersion -eq 2) {
        $partition = Get-WorkspaceChangePartition $baseline $fresh $expected
        $currentChangedPaths = @(Sort-OrdinalPaths @($partition.expectedChangedPaths))
        $recordedChangedPaths = @($evidence.expectedChangedPaths | ForEach-Object { [string]$_ })
        if (($currentChangedPaths | ConvertTo-Json -Compress) -cne ($recordedChangedPaths | ConvertTo-Json -Compress)) {
          $expectedChanged = $true
        }
        $expectedConflicts = @($expectedConflicts) + @($currentChangedPaths)
      }
      if ($expectedChanged) {
        $conflicts = @(Sort-OrdinalPaths $expectedConflicts)
        Write-SafetyResult $false $expected $conflicts 'recovery_expected_changed'
        exit $ExitRecoveryExpectedChanged
      }
      Write-SafetyResult $true $expected @() $null
      exit 0
    }
    'Check' {
      $expected = @()
      try {
        $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths
        Assert-ExpectedPathsSafe $repository $expected
      } catch {
        $reason = if ($_.Exception.Message -eq 'expected_path_enters_submodule') { 'expected_path_enters_submodule' } else { 'invalid_arguments' }
        Write-SafetyResult $false $expected @() $reason
        exit $ExitInvalidArguments
      }
      try { $baseline = Read-Baseline $BaselinePath $repository } catch { Write-SafetyResult $false $expected @() 'baseline_invalid'; exit 1 }
      try {
        $fresh = Get-WorkspaceSnapshot $repository
      } catch {
        if ($_.Exception.Message -ne 'dirty_submodule_unsupported') { throw }
        Write-SafetyResult $false $expected @('<SUBMODULE>') 'dirty_submodule_unsupported'
        exit $ExitBaselineChanged
      }
      $casChanged = Get-ChangedWorkspacePaths $baseline $fresh @()
      if (-not ([string]$baseline.head).Equals([string]$fresh.head, [System.StringComparison]::Ordinal)) {
        try {
          foreach ($path in (Get-HeadRangePaths $repository ([string]$baseline.head) ([string]$fresh.head))) { [void]$casChanged.Add($path) }
        } catch {
          [void]$casChanged.Add('<HEAD>')
        }
        if ($casChanged.Count -eq 0) { [void]$casChanged.Add('<HEAD>') }
      }
      if ($casChanged.Count -gt 0) {
        Write-SafetyResult $false $expected @($casChanged) 'baseline_changed'
        exit $ExitBaselineChanged
      }
      $conflicts = @($baseline.entries | Where-Object { Test-OverlapsAnyExpectedPath ([string]$_.path) $expected } | ForEach-Object { [string]$_.path })
      if ($conflicts.Count -gt 0) {
        Write-SafetyResult $false $expected $conflicts 'candidate_conflict'
        exit $ExitCandidateConflict
      }
      Write-SafetyResult $true $expected @() $null
      exit 0
    }
    'Verify' {
      $expected = @()
      try {
        $expected = ConvertTo-NormalizedExpectedPaths $ExpectedPaths
        Assert-ExpectedPathsSafe $repository $expected
      } catch {
        $reason = if ($_.Exception.Message -eq 'expected_path_enters_submodule') { 'expected_path_enters_submodule' } else { 'invalid_arguments' }
        Write-SafetyResult $false $expected @() $reason
        exit $ExitInvalidArguments
      }
      try { $baseline = Read-Baseline $BaselinePath $repository } catch { Write-SafetyResult $false $expected @() 'baseline_invalid'; exit 1 }
      try {
        $fresh = Get-WorkspaceSnapshot $repository
      } catch {
        if ($_.Exception.Message -ne 'dirty_submodule_unsupported') { throw }
        Write-SafetyResult $false $expected @('<SUBMODULE>') 'dirty_submodule_unsupported'
        exit $ExitBaselineChanged
      }
      $changed = Get-ChangedWorkspacePaths $baseline $fresh $expected
      try {
        [void](Invoke-GitRaw $repository @('cat-file', '-e', "$($baseline.head)^{commit}"))
      } catch {
        Write-SafetyResult $false $expected @('<HEAD>') 'baseline_head_missing'
        exit $ExitBaselineChanged
      }
      try {
        $headRangePaths = Get-HeadRangePaths $repository ([string]$baseline.head) ([string]$fresh.head)
      } catch {
        Write-SafetyResult $false $expected @('<HEAD>') 'head_not_descendant'
        exit $ExitBaselineChanged
      }
      foreach ($path in $headRangePaths) {
        if (-not (Test-OverlapsAnyExpectedPath $path $expected)) { [void]$changed.Add($path) }
      }
      if ($changed.Count -gt 0) {
        Write-SafetyResult $false $expected @($changed) 'baseline_changed'
        exit $ExitBaselineChanged
      }
      Write-SafetyResult $true $expected @() $null
      exit 0
    }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
