#requires -Version 7.0

Set-StrictMode -Version Latest

function ConvertTo-TzgLanePaths {
  param(
    [Parameter(Mandatory = $true)]
    [object[]]$Paths,
    [string]$Label = 'paths'
  )

  $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $result = [Collections.Generic.List[string]]::new()
  foreach ($value in $Paths) {
    $path = ([string]$value).Trim().Replace('\', '/')
    if (
      [string]::IsNullOrWhiteSpace($path) -or
      [IO.Path]::IsPathFullyQualified($path) -or
      $path -match '[\x00-\x1F\x7F|*?\[\]]' -or
      @($path.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0
    ) {
      throw "$Label contains an invalid repository-relative path: $value"
    }
    if (-not $seen.Add($path)) {
      throw "$Label contains a duplicate path: $path"
    }
    $result.Add($path)
  }
  @($result)
}

function Test-TzgLanePathOverlap {
  param(
    [Parameter(Mandatory = $true)][string]$Left,
    [Parameter(Mandatory = $true)][string]$Right
  )

  $Left.Equals($Right, [StringComparison]::OrdinalIgnoreCase) -or
    $Left.StartsWith($Right.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase) -or
    $Right.StartsWith($Left.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Test-TzgLanePathSetOverlap {
  param(
    [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Left,
    [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Right
  )

  foreach ($leftPath in $Left) {
    foreach ($rightPath in $Right) {
      if (Test-TzgLanePathOverlap -Left ([string]$leftPath) -Right ([string]$rightPath)) {
        return $true
      }
    }
  }
  $false
}

function Get-TzgSha256Text {
  param([Parameter(Mandatory = $true)][string]$Text)

  [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))
  ).ToLowerInvariant()
}

function Get-TzgAutomationLaneConfiguration {
  [pscustomobject][ordered]@{
    schemaVersion = 1
    maxConcurrent = 2
    lanes = @(
      [pscustomobject][ordered]@{
        laneId = 'codex'
        owner = 'codex'
        identity = 'Codex'
        acceptedRoutes = @('codex_execute', 'codex_review')
        invoker = 'tools/invoke-codex-lane-worker.ps1'
      },
      [pscustomobject][ordered]@{
        laneId = 'deepseek'
        owner = 'deepseek'
        identity = 'DeepSeek V4 Flash'
        acceptedRoutes = @('external_execute')
        invoker = 'tools/invoke-external-lane-worker.ps1'
      }
    )
  }
}

function New-TzgLaneWorkerTerminalSchema {
  $string = [ordered]@{ type = 'string'; minLength = 1; maxLength = 2000 }
  ([ordered]@{
    type = 'object'
    properties = [ordered]@{
      status = [ordered]@{
        type = 'string'
        enum = @('completed', 'needs_decision', 'blocked', 'failed')
      }
      batchId = $string
      laneId = $string
      taskId = $string
      identity = $string
      sessionId = [ordered]@{ type = @('string', 'null') }
      candidateCommit = [ordered]@{
        type = 'string'
        pattern = '^[0-9a-f]{40,64}$'
      }
      changedPaths = [ordered]@{
        type = 'array'
        minItems = 1
        uniqueItems = $true
        items = $string
      }
      validationResults = [ordered]@{
        type = 'array'
        minItems = 1
        items = [ordered]@{
          type = 'object'
          properties = [ordered]@{
            name = $string
            outcome = [ordered]@{ type = 'string'; enum = @('passed', 'failed', 'not_run') }
            detail = $string
          }
          required = @('name', 'outcome', 'detail')
          additionalProperties = $false
        }
      }
      goal = $string
      completed = $string
      impact = $string
      boundary = $string
      verification = $string
      next = $string
      plainHappened = [ordered]@{ type = 'string'; minLength = 1; maxLength = 200 }
      plainImpact = [ordered]@{ type = 'string'; minLength = 1; maxLength = 200 }
      plainAction = [ordered]@{ type = 'string'; minLength = 1; maxLength = 200 }
      transition = [ordered]@{
        type = 'object'
        properties = [ordered]@{
          route = $string
          owner = $string
          dispatchState = [ordered]@{ type = 'string'; enum = @('ready', 'blocked', 'frozen', 'pending_decision', 'waiting_reply', 'completed') }
        }
        required = @('route', 'owner', 'dispatchState')
        additionalProperties = $false
      }
      coordinatorChanges = [ordered]@{
        type = 'array'
        minItems = 1
        items = [ordered]@{
          type = 'object'
          properties = [ordered]@{
            path = $string
            operation = [ordered]@{ type = 'string'; enum = @('write', 'delete') }
            content = [ordered]@{ type = 'string' }
          }
          required = @('path', 'operation')
          additionalProperties = $false
        }
      }
      decisionId = $string
      question = $string
      options = [ordered]@{
        type = 'array'
        minItems = 2
        maxItems = 3
        items = $string
      }
      detailCode = $string
    }
    required = @('status', 'batchId', 'laneId', 'taskId', 'identity', 'sessionId')
    additionalProperties = $false
  } | ConvertTo-Json -Compress -Depth 30)
}

function Read-TzgLaneTaskCard {
  param([Parameter(Mandatory = $true)][string]$Path)

  $bytes = [IO.File]::ReadAllBytes($Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF)
  $metaMarker = '---TASK-META---'
  $bodyMarker = '---TASK-BODY---'
  $metaIndex = $text.IndexOf($metaMarker, [StringComparison]::Ordinal)
  $bodyIndex = $text.IndexOf($bodyMarker, [StringComparison]::Ordinal)
  if ($metaIndex -ne 0 -or $bodyIndex -le $metaMarker.Length) {
    throw "Invalid task card: $Path"
  }
  $json = $text.Substring($metaMarker.Length, $bodyIndex - $metaMarker.Length).Trim()
  $metadata = $json | ConvertFrom-Json -Depth 100
  if ($metadata.schemaVersion -ne 2) {
    throw "Unsupported task card schema: $Path"
  }
  $workerPaths = @(ConvertTo-TzgLanePaths -Paths @($metadata.workerPaths) -Label 'workerPaths')
  $coordinatorPaths = @(ConvertTo-TzgLanePaths -Paths @($metadata.coordinatorPaths) -Label 'coordinatorPaths')
  if (
    $workerPaths.Count -eq 0 -or
    $coordinatorPaths.Count -eq 0 -or
    (Test-TzgLanePathSetOverlap -Left $workerPaths -Right $coordinatorPaths)
  ) {
    throw "Invalid task-card path classification: $Path"
  }
  $body = $text.Substring($bodyIndex + $bodyMarker.Length)
  $repositoryRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $Path))
  $rootPrefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  $factPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($match in [regex]::Matches($body, '`(?<value>[^`\r\n]+)`')) {
    $candidate = $match.Groups['value'].Value.Trim().Replace('\', '/').TrimEnd('/')
    if (
      [string]::IsNullOrWhiteSpace($candidate) -or
      $candidate.Contains(' ', [StringComparison]::Ordinal) -or
      [IO.Path]::IsPathFullyQualified($candidate) -or
      $candidate -notmatch '/' -or
      $candidate -match '[\x00-\x1F\x7F|*?\[\]]' -or
      @($candidate.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0
    ) {
      continue
    }
    $factPath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $candidate))
    if (
      $factPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $factPath)
    ) {
      [void]$factPaths.Add($candidate)
    }
  }
  [pscustomobject][ordered]@{
    metadata = $metadata
    cardHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    workerPaths = $workerPaths
    coordinatorPaths = $coordinatorPaths
    factPaths = @($factPaths | Sort-Object)
  }
}

function Read-TzgLaneQueue {
  param([Parameter(Mandatory = $true)][string]$Path)

  $text = [Text.UTF8Encoding]::new($false, $true).GetString(
    [IO.File]::ReadAllBytes($Path)
  ).TrimStart([char]0xFEFF)
  $lines = @($text -split '\r?\n')
  $headerIndex = [Array]::FindIndex(
    $lines,
    [Predicate[string]]{ param($line) $line.Trim() -ceq '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |' }
  )
  if ($headerIndex -lt 0 -or $headerIndex + 1 -ge $lines.Count) {
    throw "Invalid queue table: $Path"
  }
  $rows = [Collections.Generic.List[object]]::new()
  for ($index = $headerIndex + 2; $index -lt $lines.Count; $index++) {
    $line = $lines[$index].Trim()
    if ([string]::IsNullOrWhiteSpace($line) -or -not $line.StartsWith('|')) {
      break
    }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { $_.Trim().Trim([char]96) })
    if ($cells.Count -ne 8) {
      throw "Invalid queue row: $line"
    }
    $rows.Add([pscustomobject][ordered]@{
      queueIndex = $rows.Count
      taskId = $cells[0]
      route = $cells[1]
      owner = $cells[2]
      priority = $cells[3]
      domain = $cells[4]
      stage = $cells[5]
      title = $cells[6]
      cardPath = $cells[7]
      rowHash = Get-TzgSha256Text -Text $line
    })
  }
  @($rows)
}

function Get-TzgManualWorkspacePaths {
  param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

  $output = & git -C $RepositoryRoot -c core.quotepath=false status --porcelain=v1 -z --untracked-files=all
  if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read workspace paths'
  }
  if ($null -eq $output) {
    return @()
  }
  $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($record in ([string]$output).Split([char]0)) {
    if ([string]::IsNullOrEmpty($record) -or $record.Length -lt 4) {
      continue
    }
    $path = $record.Substring(3)
    $arrow = $path.LastIndexOf(' -> ', [StringComparison]::Ordinal)
    if ($arrow -ge 0) {
      $path = $path.Substring($arrow + 4)
    }
    [void]$paths.Add($path.Replace('\', '/'))
  }
  @($paths | Sort-Object)
}

function Test-TzgTaskDependsOn {
  param(
    [Parameter(Mandatory = $true)][string]$TaskId,
    [Parameter(Mandatory = $true)][string]$DependencyId,
    [Parameter(Mandatory = $true)][Collections.IDictionary]$Cards,
    [Collections.Generic.HashSet[string]]$Visited = $null
  )

  if ($null -eq $Visited) {
    $Visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  }
  if (-not $Visited.Add($TaskId) -or -not $Cards.Contains($TaskId)) {
    return $false
  }
  foreach ($blockedBy in @($Cards[$TaskId].metadata.blockedBy)) {
    $blockedId = [string]$blockedBy
    if ($blockedId -ceq $DependencyId) {
      return $true
    }
    if (Test-TzgTaskDependsOn -TaskId $blockedId -DependencyId $DependencyId -Cards $Cards -Visited $Visited) {
      return $true
    }
  }
  $false
}

function Select-TzgAutomationLaneBatch {
  param(
    [Parameter(Mandatory = $true)][object[]]$QueueRows,
    [Parameter(Mandatory = $true)][Collections.IDictionary]$Cards,
    [Parameter(Mandatory = $true)][object[]]$Lanes,
    [Parameter(Mandatory = $true)][int]$MaxConcurrent,
    [object[]]$ManualPaths = @()
  )

  $selected = [Collections.Generic.List[object]]::new()
  $usedLanes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($row in @($QueueRows | Sort-Object queueIndex)) {
    if ($selected.Count -ge $MaxConcurrent -or -not $Cards.Contains([string]$row.taskId)) {
      continue
    }
    $card = $Cards[[string]$row.taskId]
    $metadata = $card.metadata
    if (
      [string]$metadata.dispatchState -cne 'ready' -or
      [string]$metadata.route -cne [string]$row.route -or
      [string]$metadata.owner -cne [string]$row.owner -or
      (Test-TzgLanePathSetOverlap -Left @($card.workerPaths) -Right $ManualPaths)
    ) {
      continue
    }
    $lane = @($Lanes | Where-Object {
      -not $usedLanes.Contains([string]$_.laneId) -and
      [string]$_.owner -ceq [string]$metadata.owner -and
      @($_.acceptedRoutes) -ccontains [string]$metadata.route
    } | Select-Object -First 1)
    if ($lane.Count -ne 1) {
      continue
    }

    $conflict = $false
    foreach ($existing in $selected) {
      if (
        (Test-TzgLanePathSetOverlap -Left @($card.workerPaths) -Right @($existing.workerPaths)) -or
        (Test-TzgLanePathSetOverlap -Left @($card.workerPaths) -Right @($existing.factPaths)) -or
        (Test-TzgLanePathSetOverlap -Left @($existing.workerPaths) -Right @($card.factPaths)) -or
        (Test-TzgTaskDependsOn -TaskId ([string]$row.taskId) -DependencyId ([string]$existing.taskId) -Cards $Cards) -or
        (Test-TzgTaskDependsOn -TaskId ([string]$existing.taskId) -DependencyId ([string]$row.taskId) -Cards $Cards)
      ) {
        $conflict = $true
        break
      }
    }
    if ($conflict) {
      continue
    }
    $usedLanes.Add([string]$lane[0].laneId) | Out-Null
    $selected.Add([pscustomobject][ordered]@{
      queueIndex = [int]$row.queueIndex
      queueRowHash = [string]$row.rowHash
      taskId = [string]$row.taskId
      route = [string]$metadata.route
      owner = [string]$metadata.owner
      lane = $lane[0]
      cardHash = [string]$card.cardHash
      workerPaths = @($card.workerPaths)
      coordinatorPaths = @($card.coordinatorPaths)
      factPaths = @($card.factPaths)
    })
  }
  @($selected)
}

function Assert-TzgLaneWorkerTerminal {
  param(
    [Parameter(Mandatory = $true)][object]$Terminal,
    [Parameter(Mandatory = $true)][object]$Lane,
    [Parameter(Mandatory = $true)][string]$BatchId
  )

  foreach ($property in @('status', 'batchId', 'laneId', 'taskId', 'identity', 'sessionId')) {
    if ($Terminal.PSObject.Properties.Name -cnotcontains $property) {
      throw "Worker terminal is missing $property"
    }
  }
  if (
    [string]$Terminal.status -cnotin @('completed', 'needs_decision', 'blocked', 'failed') -or
    [string]$Terminal.batchId -cne $BatchId -or
    [string]$Terminal.laneId -cne [string]$Lane.laneId -or
    [string]$Terminal.taskId -cne [string]$Lane.taskClaim.taskId -or
    [string]$Terminal.identity -cne [string]$Lane.identity
  ) {
    throw 'Worker terminal identity is invalid'
  }
  if ([string]$Terminal.status -ceq 'completed') {
    foreach ($property in @(
        'candidateCommit',
        'changedPaths',
        'validationResults',
        'goal',
        'completed',
        'impact',
        'boundary',
        'verification',
        'next',
        'plainHappened',
        'plainImpact',
        'plainAction',
        'transition',
        'coordinatorChanges'
      )) {
      if ($Terminal.PSObject.Properties.Name -cnotcontains $property) {
        throw "Completed worker terminal is missing $property"
      }
    }
    if ([string]$Terminal.candidateCommit -cnotmatch '\A[0-9a-f]{40,64}\z') {
      throw 'Worker candidate commit is invalid'
    }
    $changedPaths = @(ConvertTo-TzgLanePaths -Paths @($Terminal.changedPaths) -Label 'changedPaths')
    if ($changedPaths.Count -lt 1) {
      throw 'Worker changedPaths must not be empty'
    }
    foreach ($path in $changedPaths) {
      if (-not (Test-TzgLanePathSetOverlap -Left @($path) -Right @($Lane.workerPaths))) {
        throw "Worker changed path is outside workerPaths: $path"
      }
    }
    foreach ($field in @('goal', 'completed', 'impact', 'boundary', 'verification', 'next', 'plainHappened', 'plainImpact', 'plainAction')) {
      $value = [string]$Terminal.$field
      if ([string]::IsNullOrWhiteSpace($value) -or $value -match '[\x00-\x1F\x7F]') {
        throw "Worker terminal field is invalid: $field"
      }
    }
    $changePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($change in @($Terminal.coordinatorChanges)) {
      if (
        $change.PSObject.Properties.Name -cnotcontains 'path' -or
        $change.PSObject.Properties.Name -cnotcontains 'operation' -or
        [string]$change.operation -cnotin @('write', 'delete')
      ) {
        throw 'Coordinator change is invalid'
      }
      $path = @(ConvertTo-TzgLanePaths -Paths @([string]$change.path) -Label 'coordinatorChanges')[0]
      if (
        -not $changePaths.Add($path) -or
        -not (Test-TzgLanePathSetOverlap -Left @($path) -Right @($Lane.coordinatorPaths))
      ) {
        throw "Coordinator change is outside coordinatorPaths: $path"
      }
      if ([string]$change.operation -ceq 'write' -and $change.PSObject.Properties.Name -cnotcontains 'content') {
        throw "Coordinator write is missing content: $path"
      }
    }
    if ([string]$Lane.taskClaim.route -ceq 'external_execute') {
      if (
        [string]$Terminal.transition.route -cne 'codex_review' -or
        [string]$Terminal.transition.owner -cne 'codex' -or
        [string]$Terminal.transition.dispatchState -cne 'ready'
      ) {
        throw 'External worker transition is invalid'
      }
      $handoffChanges = @($Terminal.coordinatorChanges | Where-Object {
        [string]$_.path -ceq '开发管理/AI合作沟通.txt' -and
        [string]$_.operation -ceq 'write'
      })
      if ($handoffChanges.Count -ne 1) {
        throw 'External worker handoff change is missing'
      }
      $handoffContent = [string]$handoffChanges[0].content
      foreach ($token in @('DeepSeek V4 Flash', '待 Codex', '已验证', '未验证', '残留风险')) {
        if (-not $handoffContent.Contains($token, [StringComparison]::Ordinal)) {
          throw "External worker handoff is missing $token"
        }
      }
    }
  } else {
    if (
      $Terminal.PSObject.Properties.Name -cnotcontains 'detailCode' -or
      [string]::IsNullOrWhiteSpace([string]$Terminal.detailCode) -or
      [string]$Terminal.detailCode -cnotmatch '\A[a-z0-9_]{3,100}\z'
    ) {
      throw 'Non-completed worker terminal detailCode is invalid'
    }
    if ([string]$Terminal.status -ceq 'needs_decision') {
      foreach ($property in @('decisionId', 'question', 'options')) {
        if ($Terminal.PSObject.Properties.Name -cnotcontains $property) {
          throw "Decision terminal is missing $property"
        }
      }
      $options = @($Terminal.options)
      if (
        [string]::IsNullOrWhiteSpace([string]$Terminal.decisionId) -or
        [string]::IsNullOrWhiteSpace([string]$Terminal.question) -or
        $options.Count -lt 2 -or
        $options.Count -gt 3 -or
        @($options | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0
      ) {
        throw 'Decision terminal fields are invalid'
      }
    }
  }
}

function Get-TzgCommitChangedPaths {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$Commit
  )

  $parent = (& git -C $RepositoryRoot rev-parse "$Commit^").Trim()
  if ($LASTEXITCODE -ne 0) {
    throw 'Candidate commit parent is unavailable'
  }
  $output = & git -C $RepositoryRoot -c core.quotepath=false diff-tree --no-commit-id --name-only -r --no-renames $parent $Commit
  if ($LASTEXITCODE -ne 0) {
    throw 'Candidate commit paths are unavailable'
  }
  @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { ([string]$_).Replace('\', '/') })
}

function Test-TzgCandidateCommit {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][object]$Lane,
    [Parameter(Mandatory = $true)][object]$Terminal
  )

  $candidate = [string]$Terminal.candidateCommit
  & git -C $RepositoryRoot cat-file -e "$candidate^{commit}" 2>$null
  if ($LASTEXITCODE -ne 0) {
    throw 'Candidate commit is not reachable'
  }
  $parent = (& git -C $RepositoryRoot rev-parse "$candidate^").Trim()
  if ($LASTEXITCODE -ne 0 -or $parent -cne [string]$Lane.baseCommit) {
    throw 'Candidate commit parent does not match lane baseCommit'
  }
  $actualPaths = @(Get-TzgCommitChangedPaths -RepositoryRoot $RepositoryRoot -Commit $candidate)
  $reportedPaths = @($Terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object)
  if (
    ((@($actualPaths | Sort-Object) -join "`n") -cne (@($reportedPaths | Sort-Object) -join "`n"))
  ) {
    throw 'Candidate commit paths do not match worker terminal'
  }
  foreach ($path in $actualPaths) {
    if (-not (Test-TzgLanePathSetOverlap -Left @($path) -Right @($Lane.workerPaths))) {
      throw "Candidate commit path is outside workerPaths: $path"
    }
  }
  $actualPaths
}

function Write-TzgPrivateJson {
  param(
    [Parameter(Mandatory = $true)][object]$Value,
    [Parameter(Mandatory = $true)][string]$Path
  )

  $directory = Split-Path -Parent $Path
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  $temporaryPath = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
  try {
    [IO.File]::WriteAllText(
      $temporaryPath,
      ($Value | ConvertTo-Json -Depth 100) + "`n",
      [Text.UTF8Encoding]::new($false)
    )
    [IO.File]::Move($temporaryPath, $Path, $true)
  } finally {
    if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
      Remove-Item -LiteralPath $temporaryPath -Force
    }
  }
}

function Read-TzgPrivateJson {
  param([Parameter(Mandatory = $true)][string]$Path)

  [Text.UTF8Encoding]::new($false, $true).GetString(
    [IO.File]::ReadAllBytes($Path)
  ) | ConvertFrom-Json -Depth 100
}

function Test-TzgLaneCleanupAllowed {
  param([Parameter(Mandatory = $true)][object]$Lane)

  if ([string]$Lane.integrationState -ceq 'integrated') {
    return $true
  }
  $hasCandidateCommit =
    $null -ne $Lane.workerTerminal -and
    $Lane.workerTerminal.PSObject.Properties.Name -contains 'candidateCommit' -and
    -not [string]::IsNullOrWhiteSpace([string]$Lane.workerTerminal.candidateCommit)
  if (
    [string]$Lane.integrationState -ceq 'failed' -and
    $null -ne $Lane.workerTerminal -and
    [string]$Lane.workerTerminal.status -cin @('blocked', 'failed') -and
    -not $hasCandidateCommit
  ) {
    return $true
  }
  $false
}

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
