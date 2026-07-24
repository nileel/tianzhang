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
  $null
}

function Get-CommitRecord {
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

  $commitText = Invoke-GitRaw -Arguments @('rev-list', '--reverse', "--since=$sinceIso", "--until=$untilIso", 'HEAD')
  $commitShas = @($commitText -split '\r?\n' | Where-Object { $_ -match '^[0-9a-f]{40}$' })
  $groupMap = [Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal)
  $errors = [Collections.Generic.List[object]]::new()

  foreach ($commitSha in $commitShas) {
    $record = Get-CommitRecord -CommitSha $commitSha
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

  $groups = [Collections.Generic.List[object]]::new()
  foreach ($task in $groupMap.Keys) {
    $group = $groupMap[$task]
    $groups.Add([pscustomobject][ordered]@{
      task = $group.task
      category = $group.category
      commits = @($group.commits)
    })
  }
  $result = [ordered]@{
    status = 'ok'
    since = $sinceIso
    until = $untilIso
    groups = @($groups)
    errors = @($errors)
  }
  $resultExitCode = 0
} catch {
  $result = [ordered]@{
    status = 'failed'
    error = 'source_error'
    groups = @()
    errors = @()
  }
  $resultExitCode = 1
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 20))
exit $resultExitCode
