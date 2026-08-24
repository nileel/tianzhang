#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Get-Location).Path,
  [Parameter(Mandatory = $true)]
  [string]$Since,
  [Parameter(Mandatory = $true)]
  [string]$Until
)

$ErrorActionPreference = 'Stop'
$result = $null
$resultExitCode = 1
$script:failureCode = 'source_error'

function Invoke-GitRaw {
  param([string[]]$Arguments)

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @('-C', $script:resolvedRepositoryRoot) + $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) { throw 'Unable to start git' }
  $stdout = [IO.MemoryStream]::new()
  try {
    $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdout)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [void]$stdoutTask.GetAwaiter().GetResult()
    [void]$stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw 'Git query failed' }
    [Text.UTF8Encoding]::new($false, $true).GetString($stdout.ToArray()).TrimEnd("`r", "`n")
  } finally {
    $stdout.Dispose()
    $process.Dispose()
  }
}

function Get-CommitPaths {
  param([string]$CommitSha)

  $pathText = Invoke-GitRaw -Arguments @(
    '-c', 'core.quotepath=false', 'diff-tree', '--root', '--no-commit-id',
    '--name-only', '-r', '--no-renames', '-z', $CommitSha
  )
  @([regex]::Split($pathText, [string][char]0) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-CommitSummary {
  param([string]$CommitSha)

  $body = Invoke-GitRaw -Arguments @('show', '-s', '--format=%B', $CommitSha)
  $automationMatches = @([regex]::Matches($body, '(?m)^Automation:\s*(?<value>[^\r\n]*\S)\s*$'))
  $automationMetadata = $null
  if ($automationMatches.Count -gt 0) {
    $metadata = [ordered]@{}
    foreach ($field in @('Automation', 'Task', 'State', 'Result', 'Impact', 'Verify')) {
      $metadata[$field] = Get-SingleMetadataValue -Body $body -Name $field
    }
    $automationMetadata = [pscustomobject]$metadata
  }

  [pscustomobject][ordered]@{
    sha = $CommitSha
    shortSha = $CommitSha.Substring(0, 8)
    subject = Invoke-GitRaw -Arguments @('show', '-s', '--format=%s', $CommitSha)
    committedAt = Invoke-GitRaw -Arguments @('show', '-s', '--format=%cI', $CommitSha)
    changedPaths = @(Get-CommitPaths -CommitSha $CommitSha)
    automationMetadata = $automationMetadata
  }
}

function Test-ExcludedArtFileName {
  param([string]$Name)

  foreach ($pattern in @(
      '.DS_Store', '.DS_Store?', '._*', 'ehthumbs.db', 'Thumbs.db', 'Desktop.ini',
      '*.log', '*.tmp', '*.temp', '*.bak', '*.swp', '~$*'
    )) {
    if ($Name -ilike $pattern) { return $true }
  }
  $false
}

function Get-SourceArtActivity {
  param(
    [Collections.Generic.HashSet[string]]$TrackedPaths,
    [DateTimeOffset]$SinceValue,
    [DateTimeOffset]$UntilValue
  )

  $artRoot = Join-Path $script:resolvedRepositoryRoot 'assets/source'
  if (-not (Test-Path -LiteralPath $artRoot -PathType Container)) {
    throw 'Art source root does not exist'
  }
  $resolvedArtRoot = [IO.Path]::GetFullPath($artRoot).TrimEnd('\', '/')
  $artPrefix = $resolvedArtRoot + [IO.Path]::DirectorySeparatorChar
  $excludedDirectories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  [void]$excludedDirectories.Add('.Spotlight-V100')
  [void]$excludedDirectories.Add('.Trashes')
  $pending = [Collections.Generic.Stack[IO.DirectoryInfo]]::new()
  $pending.Push([IO.DirectoryInfo]::new($resolvedArtRoot))
  $records = [Collections.Generic.List[object]]::new()

  while ($pending.Count -gt 0) {
    $directory = $pending.Pop()
    try {
      $entries = @($directory.EnumerateFileSystemInfos())
    } catch {
      throw 'Unable to enumerate art source directory'
    }
    foreach ($entry in @($entries | Sort-Object Name)) {
      if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'Art source contains a reparse point'
      }
      if (($entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        if (-not $excludedDirectories.Contains($entry.Name)) {
          $pending.Push([IO.DirectoryInfo]$entry)
        }
        continue
      }
      if (Test-ExcludedArtFileName -Name $entry.Name) { continue }

      $fullPath = [IO.Path]::GetFullPath($entry.FullName)
      if (-not $fullPath.StartsWith($artPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Art source path escaped the approved root'
      }
      $relativePath = [IO.Path]::GetRelativePath($script:resolvedRepositoryRoot, $fullPath).Replace('\', '/')
      if ($TrackedPaths.Contains($relativePath)) { continue }

      try {
        $file = [IO.FileInfo]$entry
        $creationAt = [DateTimeOffset]$file.CreationTimeUtc
        $modifiedAt = [DateTimeOffset]$file.LastWriteTimeUtc
        $activityAt = if ($creationAt -ge $modifiedAt) { $creationAt } else { $modifiedAt }
        $bytes = $file.Length
      } catch {
        throw 'Unable to read art source file metadata'
      }
      if ($activityAt -le $SinceValue -or $activityAt -gt $UntilValue) { continue }
      $records.Add([pscustomobject][ordered]@{
        path = $relativePath
        bytes = $bytes
        creationAt = $creationAt.ToString('o')
        modifiedAt = $modifiedAt.ToString('o')
        activityAt = $activityAt.ToString('o')
      })
    }
  }

  @($records | Sort-Object path)
}

function Get-SingleMetadataValue {
  param([string]$Body, [string]$Name)

  $matches = @([regex]::Matches($Body, "(?m)^$([regex]::Escape($Name)):\s*(?<value>[^\r\n]*\S)\s*$"))
  if ($matches.Count -ne 1) { return $null }
  $matches[0].Groups['value'].Value
}

function Read-CommitFile {
  param([string]$CommitSha, [string]$Path)

  try {
    Invoke-GitRaw -Arguments @('show', "$CommitSha`:$Path")
  } catch {
    $null
  }
}

function Get-TaskStateFromCard {
  param([string]$Text, [string]$Task)

  if ($null -eq $Text) { return $null }
  $metaMarkers = [regex]::Matches($Text, '(?m)^---TASK-META---\r?$')
  $bodyMarkers = [regex]::Matches($Text, '(?m)^---TASK-BODY---\r?$')
  if (
    $metaMarkers.Count -ne 1 -or
    $bodyMarkers.Count -ne 1 -or
    $metaMarkers[0].Index -ge $bodyMarkers[0].Index
  ) {
    return $null
  }
  $jsonText = $Text.Substring(
    $metaMarkers[0].Index + $metaMarkers[0].Length,
    $bodyMarkers[0].Index - ($metaMarkers[0].Index + $metaMarkers[0].Length)
  ).Trim()
  try {
    $metadata = $jsonText | ConvertFrom-Json -Depth 20
  } catch {
    return $null
  }
  if (
    $null -eq $metadata -or
    [string]$metadata.id -cne $Task
  ) {
    return $null
  }
  [string]$metadata.dispatchState
}

function Test-CommittedReadyQueueEntry {
  param([string]$CommitSha, [string]$Task)

  $queueText = Read-CommitFile -CommitSha $CommitSha -Path '开发管理/当前任务队列.txt'
  if ($null -eq $queueText) { return $false }
  $taskPattern = [regex]::Escape($Task)
  $matches = [regex]::Matches($queueText, "(?m)^\|\s*$taskPattern\s*\|")
  $matches.Count -eq 1
}

function Get-CommittedTaskState {
  param([string]$CommitSha, [string]$Task)

  $activeText = Read-CommitFile -CommitSha $CommitSha -Path "开发管理/任务卡/$Task.txt"
  $archiveText = Read-CommitFile -CommitSha $CommitSha -Path "开发管理/任务归档/$Task.txt"
  if (($null -ne $activeText) -eq ($null -ne $archiveText)) {
    return $null
  }
  if ($null -ne $archiveText) {
    $archiveState = Get-TaskStateFromCard -Text $archiveText -Task $Task
    if ($archiveState -ceq 'completed') { return $archiveState }
    return $null
  }
  $activeState = Get-TaskStateFromCard -Text $activeText -Task $Task
  if ($activeState -cin @('blocked', 'frozen', 'pending_decision', 'waiting_reply')) {
    return $activeState
  }
  if (
    $activeState -ceq 'ready' -and
    (Test-CommittedReadyQueueEntry -CommitSha $CommitSha -Task $Task)
  ) {
    return $activeState
  }
  $null
}

function Get-AutomationCommitRecord {
  param([string]$CommitSha)

  $body = Invoke-GitRaw -Arguments @('show', '-s', '--format=%B', $CommitSha)
  $automationMatches = @([regex]::Matches($body, '(?m)^Automation:\s*(?<value>[^\r\n]*\S)\s*$'))
  if ($automationMatches.Count -eq 0) {
    return [pscustomobject]@{ Kind = 'ignored'; Value = $null }
  }

  $values = [ordered]@{}
  foreach ($field in @('Automation', 'Task', 'State', 'Result', 'Impact', 'Verify')) {
    $value = Get-SingleMetadataValue -Body $body -Name $field
    if ($null -eq $value) {
      return [pscustomobject]@{ Kind = 'error'; Value = [ordered]@{ sha = $CommitSha; shortSha = $CommitSha.Substring(0, 8); reason = 'invalid_metadata' } }
    }
    $values[$field] = $value
  }
  if (
    [string]$values.Automation -cne 'tzg-hourly-controller' -or
    [string]$values.State -cnotin @('completed', 'pending_review')
  ) {
    return [pscustomobject]@{ Kind = 'error'; Value = [ordered]@{ sha = $CommitSha; shortSha = $CommitSha.Substring(0, 8); reason = 'invalid_metadata' } }
  }

  $subject = Invoke-GitRaw -Arguments @('show', '-s', '--format=%s', $CommitSha)
  $committedAt = Invoke-GitRaw -Arguments @('show', '-s', '--format=%cI', $CommitSha)
  $category = if ([string]$values.Task -ceq 'QUEUE-MAINTENANCE') {
    'queue_maintenance'
  } elseif ([string]$values.State -ceq 'pending_review') {
    'pending_review'
  } else {
    Get-CommittedTaskState -CommitSha $CommitSha -Task ([string]$values.Task)
  }
  if ([string]::IsNullOrWhiteSpace([string]$category)) {
    return [pscustomobject]@{
      Kind = 'error'
      Value = [ordered]@{
        sha = $CommitSha
        shortSha = $CommitSha.Substring(0, 8)
        reason = 'outcome_unverifiable'
      }
    }
  }
  [pscustomobject]@{
    Kind = 'candidate'
    Value = [ordered]@{
      sha = $CommitSha
      shortSha = $CommitSha.Substring(0, 8)
      subject = $subject
      committedAt = $committedAt
      task = [string]$values.Task
      state = [string]$values.State
      result = [string]$values.Result
      impact = [string]$values.Impact
      verify = [string]$values.Verify
      category = $category
    }
  }
}

try {
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot)) {
    throw 'RepositoryRoot must be absolute'
  }
  $script:resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath $script:resolvedRepositoryRoot -PathType Container)) {
    throw 'RepositoryRoot does not exist'
  }
  $gitRoot = Invoke-GitRaw -Arguments @('rev-parse', '--show-toplevel')
  if (-not [IO.Path]::GetFullPath($gitRoot).TrimEnd('\', '/').Equals($script:resolvedRepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RepositoryRoot must be the Git root'
  }

  $sinceValue = [DateTimeOffset]::MinValue
  $untilValue = [DateTimeOffset]::MinValue
  if (-not [DateTimeOffset]::TryParse($Since, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$sinceValue)) {
    throw 'Since must be an ISO timestamp'
  }
  if (-not [DateTimeOffset]::TryParse($Until, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$untilValue)) {
    throw 'Until must be an ISO timestamp'
  }
  if ($sinceValue -ge $untilValue) { throw 'Since must be earlier than Until' }
  $sinceIso = $sinceValue.ToString('o')
  $untilIso = $untilValue.ToString('o')

  $commitText = Invoke-GitRaw -Arguments @('rev-list', '--reverse', "--since=$sinceIso", "--until=$untilIso", 'refs/heads/master')
  $commitShas = @($commitText -split '\r?\n' | Where-Object { $_ -match '^[0-9a-f]{40}$' })
  $groupMap = [Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal)
  $commits = [Collections.Generic.List[object]]::new()
  $errors = [Collections.Generic.List[object]]::new()

  foreach ($commitSha in $commitShas) {
    $summary = Get-CommitSummary -CommitSha $commitSha
    $committedValue = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse(
        [string]$summary.committedAt,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$committedValue
      )) {
      throw 'Commit timestamp is invalid'
    }
    if ($committedValue -le $sinceValue -or $committedValue -gt $untilValue) { continue }
    $commits.Add($summary)

    $record = Get-AutomationCommitRecord -CommitSha $commitSha
    if ($record.Kind -ceq 'ignored') { continue }
    if ($record.Kind -ceq 'error') {
      $errors.Add([pscustomobject]$record.Value)
      continue
    }
    $candidate = $record.Value
    $task = [string]$candidate.task
    if (-not $groupMap.Contains($task)) {
      $groupMap.Add($task, [ordered]@{
        task = $task
        category = [string]$candidate.category
        commits = [Collections.Generic.List[object]]::new()
      })
    }
    $group = $groupMap[$task]
    $group.commits.Add([pscustomobject]$candidate)
    $group.category = [string]$candidate.category
  }

  $automationGroups = [Collections.Generic.List[object]]::new()
  foreach ($task in $groupMap.Keys) {
    $group = $groupMap[$task]
    $automationGroups.Add([pscustomobject][ordered]@{
      task = $group.task
      category = $group.category
      commits = @($group.commits)
    })
  }

  $trackedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $trackedText = Invoke-GitRaw -Arguments @('-c', 'core.quotepath=false', 'ls-files', '--cached', '-z', '--', 'assets/source')
  foreach ($trackedPath in @([regex]::Split($trackedText, [string][char]0))) {
    if ([string]::IsNullOrWhiteSpace($trackedPath)) { continue }
    [void]$trackedPaths.Add($trackedPath.Replace('\', '/'))
  }
  $script:failureCode = 'art_source_error'
  $sourceArtActivity = @(Get-SourceArtActivity -TrackedPaths $trackedPaths -SinceValue $sinceValue -UntilValue $untilValue)
  $script:failureCode = 'source_error'

  $result = [ordered]@{
    status = 'ok'
    since = $sinceIso
    until = $untilIso
    commits = @($commits)
    automationGroups = @($automationGroups)
    sourceArtActivity = @($sourceArtActivity)
    errors = @($errors)
  }
  $resultExitCode = 0
} catch {
  $result = [ordered]@{
    status = 'failed'
    error = $script:failureCode
    commits = @()
    automationGroups = @()
    sourceArtActivity = @()
    errors = @()
  }
  $resultExitCode = 1
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 20))
exit $resultExitCode
