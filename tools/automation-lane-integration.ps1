#requires -Version 7.0

Set-StrictMode -Version Latest

function Get-TzgFileSha256 {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    return $null
  }
  [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path))
  ).ToLowerInvariant()
}

function Get-TzgHeadRangeChangedPaths {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$BaseCommit,
    [string[]]$IgnoredCommits = @()
  )

  $currentHead = (& git -C $RepositoryRoot rev-parse HEAD 2>$null).Trim()
  $mergeBase = (& git -C $RepositoryRoot merge-base $BaseCommit $currentHead 2>$null).Trim()
  if ($LASTEXITCODE -ne 0 -or $mergeBase -cne $BaseCommit) {
    throw 'Current HEAD is not a descendant of the batch baseCommit'
  }
  if ($currentHead -ceq $BaseCommit) {
    return @()
  }
  $ignored = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($commit in $IgnoredCommits) { [void]$ignored.Add($commit) }
  $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($commit in @(& git -C $RepositoryRoot rev-list --reverse "$BaseCommit..$currentHead")) {
    if ($ignored.Contains([string]$commit)) {
      continue
    }
    foreach ($path in @(
        & git -C $RepositoryRoot -c core.quotepath=false diff-tree `
          --no-commit-id --name-only -r --no-renames $commit
      )) {
      if (-not [string]::IsNullOrWhiteSpace([string]$path)) {
        [void]$paths.Add(([string]$path).Replace('\', '/'))
      }
    }
  }
  @($paths | Sort-Object)
}

function Get-TzgLaneIntegrationPreflight {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object]$Batch,
    [Parameter(Mandatory = $true)][object]$Lane,
    [string[]]$KnownBatchCommits = @()
  )

  $cardPath = Join-Path $RepositoryRoot "开发管理/任务卡/$($Lane.taskClaim.taskId).txt"
  if ((Get-TzgFileSha256 -Path $cardPath) -cne [string]$Lane.taskClaim.cardHash) {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = @("开发管理/任务卡/$($Lane.taskClaim.taskId).txt") }
  }
  try {
    $queueRows = @(Read-TzgLaneQueue -Path (Join-Path $RepositoryRoot '开发管理/当前任务队列.txt'))
  } catch {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = @('开发管理/当前任务队列.txt') }
  }
  $row = @($queueRows | Where-Object { [string]$_.taskId -ceq [string]$Lane.taskClaim.taskId })
  if ($row.Count -ne 1 -or [string]$row[0].rowHash -cne [string]$Lane.taskClaim.queueRowHash) {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = @('开发管理/当前任务队列.txt') }
  }

  $manualPaths = @(Get-TzgManualWorkspacePaths -RepositoryRoot $RepositoryRoot)
  $workerConflicts = @($manualPaths | Where-Object {
    Test-TzgLanePathSetOverlap -Left @($_) -Right @($Lane.workerPaths)
  })
  if ($workerConflicts.Count -gt 0) {
    return [pscustomobject]@{ classification = 'held_conflict'; paths = $workerConflicts }
  }
  $factAndCoordinator = @($Lane.coordinatorPaths) + @($Lane.factPaths)
  $staleManual = @($manualPaths | Where-Object {
    Test-TzgLanePathSetOverlap -Left @($_) -Right $factAndCoordinator
  })
  if ($staleManual.Count -gt 0) {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = $staleManual }
  }

  try {
    $headPaths = @(Get-TzgHeadRangeChangedPaths `
      -RepositoryRoot $RepositoryRoot `
      -BaseCommit ([string]$Batch.baseCommit) `
      -IgnoredCommits $KnownBatchCommits)
  } catch {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = @('<HEAD>') }
  }
  $headWorker = @($headPaths | Where-Object {
    Test-TzgLanePathSetOverlap -Left @($_) -Right @($Lane.workerPaths)
  })
  if ($headWorker.Count -gt 0) {
    return [pscustomobject]@{ classification = 'held_conflict'; paths = $headWorker }
  }
  $headStale = @($headPaths | Where-Object {
    Test-TzgLanePathSetOverlap -Left @($_) -Right $factAndCoordinator
  })
  if ($headStale.Count -gt 0) {
    return [pscustomobject]@{ classification = 'stale_selection'; paths = $headStale }
  }
  [pscustomobject]@{ classification = 'ready'; paths = @() }
}

function Write-TzgCoordinatorChanges {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object[]]$Changes
  )

  foreach ($change in $Changes) {
    $relativePath = @(ConvertTo-TzgLanePaths -Paths @([string]$change.path) -Label 'coordinatorChanges')[0]
    $fullPath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relativePath))
    $rootPrefix = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Coordinator change escapes repository: $relativePath"
    }
    if ([string]$change.operation -ceq 'delete') {
      if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        [IO.File]::Delete($fullPath)
      }
      continue
    }
    [IO.Directory]::CreateDirectory((Split-Path -Parent $fullPath)) | Out-Null
    [IO.File]::WriteAllText($fullPath, [string]$change.content, [Text.UTF8Encoding]::new($true))
  }
}

function ConvertTo-TzgCoordinatorText {
  param([AllowNull()][string]$Text)

  if ($null -eq $Text) {
    return $null
  }
  $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Merge-TzgTaskProjectionTable {
  param(
    [Parameter(Mandatory = $true)][string]$TaskId,
    [Parameter(Mandatory = $true)][string]$CurrentContent,
    [Parameter(Mandatory = $true)][string]$BaseContent,
    [Parameter(Mandatory = $true)][string]$DesiredContent
  )

  $pattern = '^\|\s*' + [Regex]::Escape($TaskId) + '\s*\|'
  $currentLines = [Collections.Generic.List[string]]::new()
  foreach ($line in [Regex]::Split($CurrentContent, "`n")) { $currentLines.Add($line) }
  $baseLines = @([Regex]::Split($BaseContent, "`n"))
  $desiredLines = @([Regex]::Split($DesiredContent, "`n"))
  $currentIndexes = @(for ($index = 0; $index -lt $currentLines.Count; $index++) {
    if ($currentLines[$index] -match $pattern) { $index }
  })
  $baseIndexes = @(for ($index = 0; $index -lt $baseLines.Count; $index++) {
    if ($baseLines[$index] -match $pattern) { $index }
  })
  $desiredIndexes = @(for ($index = 0; $index -lt $desiredLines.Count; $index++) {
    if ($desiredLines[$index] -match $pattern) { $index }
  })
  if ($baseIndexes.Count -ne 1 -or $desiredIndexes.Count -gt 1) {
    return [pscustomobject]@{ disposition = 'not_applicable'; content = $null }
  }

  $baseOutside = @(for ($index = 0; $index -lt $baseLines.Count; $index++) {
    if ($index -ne $baseIndexes[0]) { $baseLines[$index] }
  }) -join "`n"
  $desiredOutside = @(for ($index = 0; $index -lt $desiredLines.Count; $index++) {
    if ($desiredIndexes -notcontains $index) { $desiredLines[$index] }
  }) -join "`n"
  if ($baseOutside -cne $desiredOutside) {
    return [pscustomobject]@{ disposition = 'not_applicable'; content = $null }
  }
  if (
    $currentIndexes.Count -ne 1 -or
    $currentLines[$currentIndexes[0]] -cne $baseLines[$baseIndexes[0]]
  ) {
    return [pscustomobject]@{ disposition = 'conflict'; content = $null }
  }

  if ($desiredIndexes.Count -eq 0) {
    $currentLines.RemoveAt($currentIndexes[0])
  } else {
    $currentLines[$currentIndexes[0]] = $desiredLines[$desiredIndexes[0]]
  }
  [pscustomobject]@{
    disposition = 'merged'
    content = @($currentLines) -join "`n"
  }
}

function Merge-TzgCoordinatorChanges {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$BaseRepositoryRoot,
    [Parameter(Mandatory = $true)][string]$TaskId,
    [Parameter(Mandatory = $true)][object[]]$Changes,
    [Parameter(Mandatory = $true)][string]$PrivateDirectory
  )

  [IO.Directory]::CreateDirectory($PrivateDirectory) | Out-Null
  $resolved = [Collections.Generic.List[object]]::new()
  $conflicts = [Collections.Generic.List[string]]::new()
  foreach ($change in $Changes) {
    $relativePath = @(ConvertTo-TzgLanePaths -Paths @([string]$change.path) -Label 'coordinatorChanges')[0]
    $currentPath = Join-Path $RepositoryRoot $relativePath
    $basePath = Join-Path $BaseRepositoryRoot $relativePath
    $currentExists = Test-Path -LiteralPath $currentPath -PathType Leaf
    $baseExists = Test-Path -LiteralPath $basePath -PathType Leaf
    $currentContent = if ($currentExists) {
      ConvertTo-TzgCoordinatorText -Text ([IO.File]::ReadAllText($currentPath))
    } else {
      $null
    }
    $baseContent = if ($baseExists) {
      ConvertTo-TzgCoordinatorText -Text ([IO.File]::ReadAllText($basePath))
    } else {
      $null
    }

    if ([string]$change.operation -ceq 'delete') {
      if ($currentExists -and (-not $baseExists -or $currentContent -cne $baseContent)) {
        $conflicts.Add($relativePath)
      } else {
        $resolved.Add([pscustomobject][ordered]@{ path = $relativePath; operation = 'delete' })
      }
      continue
    }

    $desiredContent = ConvertTo-TzgCoordinatorText -Text ([string]$change.content)
    if (
      (-not $currentExists -and -not $baseExists) -or
      ($currentExists -and $baseExists -and $currentContent -ceq $baseContent)
    ) {
      $resolved.Add([pscustomobject][ordered]@{
        path = $relativePath
        operation = 'write'
        content = $desiredContent
      })
      continue
    }
    if ($baseExists -and $desiredContent -ceq $baseContent) {
      $resolved.Add([pscustomobject][ordered]@{
        path = $relativePath
        operation = 'write'
        content = $currentContent
      })
      continue
    }
    if (-not $currentExists -or -not $baseExists) {
      $conflicts.Add($relativePath)
      continue
    }

    $projectionMerge = Merge-TzgTaskProjectionTable `
      -TaskId $TaskId `
      -CurrentContent $currentContent `
      -BaseContent $baseContent `
      -DesiredContent $desiredContent
    if ([string]$projectionMerge.disposition -ceq 'merged') {
      $resolved.Add([pscustomobject][ordered]@{
        path = $relativePath
        operation = 'write'
        content = [string]$projectionMerge.content
      })
      continue
    }
    if ([string]$projectionMerge.disposition -ceq 'conflict') {
      $conflicts.Add($relativePath)
      continue
    }

    $mergeId = [Guid]::NewGuid().ToString('N')
    $currentTemp = Join-Path $PrivateDirectory "$mergeId-current.txt"
    $baseTemp = Join-Path $PrivateDirectory "$mergeId-base.txt"
    $desiredTemp = Join-Path $PrivateDirectory "$mergeId-desired.txt"
    try {
      [IO.File]::WriteAllText($currentTemp, $currentContent, [Text.UTF8Encoding]::new($false))
      [IO.File]::WriteAllText($baseTemp, $baseContent, [Text.UTF8Encoding]::new($false))
      [IO.File]::WriteAllText($desiredTemp, $desiredContent, [Text.UTF8Encoding]::new($false))
      $mergeOutput = @(& git merge-file -- $currentTemp $baseTemp $desiredTemp 2>&1)
      if ($LASTEXITCODE -eq 1) {
        $conflicts.Add($relativePath)
        continue
      }
      if ($LASTEXITCODE -ne 0) {
        throw "Coordinator merge failed for $relativePath`: $(@($mergeOutput) -join ' ')"
      }
      $resolved.Add([pscustomobject][ordered]@{
        path = $relativePath
        operation = 'write'
        content = [IO.File]::ReadAllText($currentTemp)
      })
    } finally {
      foreach ($tempPath in @($currentTemp, $baseTemp, $desiredTemp)) {
        if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
          [IO.File]::Delete($tempPath)
        }
      }
    }
  }
  [pscustomobject][ordered]@{
    state = if ($conflicts.Count -eq 0) { 'ready' } else { 'held_conflict' }
    conflicts = @($conflicts)
    changes = @($resolved)
  }
}

function Write-TzgCandidatePatch {
  param(
    [Parameter(Mandatory = $true)][string]$CandidateRepositoryRoot,
    [Parameter(Mandatory = $true)][string]$CandidateCommit,
    [Parameter(Mandatory = $true)][string[]]$WorkerPaths,
    [Parameter(Mandatory = $true)][string]$PatchPath
  )

  $parent = (& git -C $CandidateRepositoryRoot rev-parse "$CandidateCommit^").Trim()
  if ($LASTEXITCODE -ne 0) {
    throw 'Candidate parent is unavailable'
  }
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'git'
  $startInfo.WorkingDirectory = $CandidateRepositoryRoot
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @('-C', $CandidateRepositoryRoot, 'diff', '--binary', '--full-index', $parent, $CandidateCommit, '--') + $WorkerPaths) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Unable to start candidate diff'
  }
  $stream = [IO.File]::Create($PatchPath)
  try {
    $copyTask = $process.StandardOutput.BaseStream.CopyToAsync($stream)
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [void]$copyTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) {
      throw "Candidate diff failed: $stderr"
    }
  } finally {
    $stream.Dispose()
    $process.Dispose()
  }
}

function Invoke-TzgGitApply {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$PatchPath,
    [switch]$Check
  )

  $arguments = @('-C', $RepositoryRoot, 'apply')
  if ($Check) { $arguments += '--check' }
  $arguments += @('--whitespace=nowarn', '--', $PatchPath)
  $output = @(& git @arguments 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "git apply failed: $(@($output) -join ' ')"
  }
}

function Invoke-TzgTaskCardPostcondition {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object]$Lane,
    [Parameter(Mandatory = $true)][object]$Terminal
  )

  $checker = Join-Path $RepositoryRoot 'tools/check-task-cards.ps1'
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checker,
    '-RepositoryRoot', $RepositoryRoot,
    '-TaskId', [string]$Lane.taskClaim.taskId,
    '-OutputJson'
  )
  if ([string]$Lane.taskClaim.route -ceq 'external_execute') {
    $arguments += @('-Postcondition', 'ExternalPendingReview')
  } else {
    $arguments += @('-Postcondition', 'CodexClosedOrNonReady')
  }
  $output = @(& pwsh @arguments 2>&1)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) {
    throw "Task-card postcondition failed: $(@($output) -join ' ')"
  }
  $evidence = $output[0] | ConvertFrom-Json -Depth 20
  if ([string]$evidence.status -cne 'ok') {
    throw 'Task-card postcondition did not return ok'
  }
  $evidence
}

function Invoke-TzgFinalizer {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string[]]$ExpectedPaths,
    [Parameter(Mandatory = $true)][string]$Subject,
    [switch]$AutomationMetadata,
    [string]$TaskId,
    [string]$State,
    [object]$Terminal
  )

  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    (Join-Path $RepositoryRoot 'tools/automation-finalize-commit.ps1'),
    '-RepositoryRoot', $RepositoryRoot,
    '-ExpectedPaths', ($ExpectedPaths -join '|'),
    '-CommitMessage', $Subject
  )
  if ($AutomationMetadata) {
    $arguments += @(
      '-RequireAutomationMetadata',
      '-AutomationTask', $TaskId,
      '-AutomationState', $State,
      '-AutomationResult', "问题=$($Terminal.goal)；完成=$($Terminal.completed)",
      '-AutomationImpact', "影响=$($Terminal.impact)；边界=$($Terminal.boundary)",
      '-AutomationVerify', "验证=$($Terminal.verification)；后续=$($Terminal.next)",
      '-AutomationPlain', "发生=$($Terminal.plainHappened)；影响=$($Terminal.plainImpact)；需要=$($Terminal.plainAction)"
    )
  }
  $output = @(& pwsh @arguments 2>&1)
  $commit = if ($output.Count -gt 0) { [string]$output[-1] } else { '' }
  if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '\A[0-9a-f]{40,64}\z') {
    throw "Canonical finalizer failed: $(@($output) -join ' ')"
  }
  $commit
}

function New-TzgIntegrationSnapshot {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string[]]$Paths
  )

  @($Paths | Sort-Object -Unique | ForEach-Object {
    $relativePath = [string]$_
    $fullPath = Join-Path $RepositoryRoot $relativePath
    [pscustomobject][ordered]@{
      path = $relativePath
      existed = Test-Path -LiteralPath $fullPath -PathType Leaf
      bytes = if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        [IO.File]::ReadAllBytes($fullPath)
      } else {
        $null
      }
    }
  })
}

function Restore-TzgIntegrationSnapshot {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object[]]$Snapshot
  )

  $paths = @($Snapshot | ForEach-Object { [string]$_.path })
  foreach ($entry in $Snapshot) {
    $fullPath = Join-Path $RepositoryRoot ([string]$entry.path)
    if ([bool]$entry.existed) {
      [IO.Directory]::CreateDirectory((Split-Path -Parent $fullPath)) | Out-Null
      [IO.File]::WriteAllBytes($fullPath, [byte[]]$entry.bytes)
    } elseif (Test-Path -LiteralPath $fullPath -PathType Leaf) {
      [IO.File]::Delete($fullPath)
    }
  }
  $output = @(& git -C $RepositoryRoot add -A -- $paths 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "Unable to restore integration index: $(@($output) -join ' ')"
  }
  & git -C $RepositoryRoot diff --quiet -- $paths
  $worktreeExit = $LASTEXITCODE
  & git -C $RepositoryRoot diff --cached --quiet -- $paths
  $indexExit = $LASTEXITCODE
  if ($worktreeExit -ne 0 -or $indexExit -ne 0) {
    throw 'Integration snapshot restoration was incomplete'
  }
}

function Invoke-TzgLaneCanonicalIntegration {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object]$Batch,
    [Parameter(Mandatory = $true)][object]$Lane,
    [Parameter(Mandatory = $true)][object]$Terminal,
    [Parameter(Mandatory = $true)][string]$PrivateDirectory,
    [string[]]$KnownBatchCommits = @()
  )

  Assert-TzgLaneWorkerTerminal -Terminal $Terminal -Lane $Lane -BatchId ([string]$Batch.batchId)
  $null = Test-TzgCandidateCommit -RepositoryRoot ([string]$Lane.worktree) -Lane $Lane -Terminal $Terminal
  $preflight = Get-TzgLaneIntegrationPreflight `
    -RepositoryRoot $RepositoryRoot `
    -Batch $Batch `
    -Lane $Lane `
    -KnownBatchCommits $KnownBatchCommits
  if ([string]$preflight.classification -cne 'ready') {
    return [pscustomobject][ordered]@{
      state = [string]$preflight.classification
      conflictingPaths = @($preflight.paths)
      businessCommit = $null
      handoffCommit = $null
    }
  }

  [IO.Directory]::CreateDirectory($PrivateDirectory) | Out-Null
  $patchPath = Join-Path $PrivateDirectory "$($Lane.laneId)-candidate.patch"
  Write-TzgCandidatePatch `
    -CandidateRepositoryRoot ([string]$Lane.worktree) `
    -CandidateCommit ([string]$Terminal.candidateCommit) `
    -WorkerPaths @($Lane.workerPaths) `
    -PatchPath $patchPath
  Invoke-TzgGitApply -RepositoryRoot $RepositoryRoot -PatchPath $patchPath -Check

  $handoffPath = '开发管理/AI合作沟通.txt'
  $coordinatorMerge = Merge-TzgCoordinatorChanges `
    -RepositoryRoot $RepositoryRoot `
    -BaseRepositoryRoot ([string]$Lane.worktree) `
    -TaskId ([string]$Lane.taskClaim.taskId) `
    -Changes @($Terminal.coordinatorChanges) `
    -PrivateDirectory $PrivateDirectory
  if ([string]$coordinatorMerge.state -cne 'ready') {
    return [pscustomobject][ordered]@{
      state = 'held_conflict'
      conflictingPaths = @($coordinatorMerge.conflicts)
      businessCommit = $null
      handoffCommit = $null
    }
  }
  $businessChanges = @($coordinatorMerge.changes)
  $handoffChanges = @()
  if ([string]$Lane.taskClaim.route -ceq 'external_execute') {
    $handoffChanges = @($businessChanges | Where-Object { [string]$_.path -ceq $handoffPath })
    $businessChanges = @($businessChanges | Where-Object { [string]$_.path -cne $handoffPath })
    if ($handoffChanges.Count -ne 1) {
      throw 'DeepSeek integration requires one handoff coordinator change'
    }
  }

  $businessPaths = @(
    @($Terminal.changedPaths | ForEach-Object { [string]$_ }) +
    @($businessChanges | ForEach-Object { [string]$_.path })
  ) | Sort-Object -Unique
  $allIntegrationPaths = @(
    $businessPaths +
    @($handoffChanges | ForEach-Object { [string]$_.path })
  ) | Sort-Object -Unique
  $integrationHead = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
  $snapshot = @(New-TzgIntegrationSnapshot -RepositoryRoot $RepositoryRoot -Paths $allIntegrationPaths)
  try {
    Invoke-TzgGitApply -RepositoryRoot $RepositoryRoot -PatchPath $patchPath
    Write-TzgCoordinatorChanges -RepositoryRoot $RepositoryRoot -Changes $businessChanges
    $evidence = Invoke-TzgTaskCardPostcondition -RepositoryRoot $RepositoryRoot -Lane $Lane -Terminal $Terminal
    $businessState = if ([string]$Lane.taskClaim.route -ceq 'external_execute') { 'pending_review' } else { 'completed' }
    $businessCommit = Invoke-TzgFinalizer `
      -RepositoryRoot $RepositoryRoot `
      -ExpectedPaths $businessPaths `
      -Subject "自动化：$($Lane.taskClaim.taskId)" `
      -AutomationMetadata `
      -TaskId ([string]$Lane.taskClaim.taskId) `
      -State $businessState `
      -Terminal $Terminal
    $handoffCommit = $null
    if ($handoffChanges.Count -eq 1) {
      Write-TzgCoordinatorChanges -RepositoryRoot $RepositoryRoot -Changes $handoffChanges
      $handoffCommit = Invoke-TzgFinalizer `
        -RepositoryRoot $RepositoryRoot `
        -ExpectedPaths @($handoffPath) `
        -Subject "交接：$($Lane.taskClaim.taskId)"
    }
    [pscustomobject][ordered]@{
      state = 'integrated'
      conflictingPaths = @()
      businessCommit = $businessCommit
      handoffCommit = $handoffCommit
      taskState = [string]$evidence.taskState
    }
  } catch {
    $integrationError = $_
    $currentHead = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -eq 0 -and $currentHead -ceq $integrationHead) {
      Restore-TzgIntegrationSnapshot -RepositoryRoot $RepositoryRoot -Snapshot $snapshot
    }
    throw $integrationError
  }
}
