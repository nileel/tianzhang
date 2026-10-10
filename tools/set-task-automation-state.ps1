#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('PauseDecision', 'ResumeReady', 'Block', 'RequeueReview', 'PauseMaintenanceDecision', 'ResolveMaintenanceDecision', 'ExpireMaintenanceDecision')][string]$Action,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$ContextPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Read-Utf8 { param([string]$Path) [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF) }
function Write-Utf8 { param([string]$Path, [string]$Text) [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false)) }
function Normalize-Cell { param([string]$Value) $Value.Trim().Trim([char]96) }

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = Read-Utf8 $Path
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
}

function Get-TaskContextDigest {
  param([object]$Metadata)
  $automationInputs = @(
    if ($Metadata.PSObject.Properties.Name -contains 'automationInputs') {
      foreach ($input in @($Metadata.automationInputs)) {
        [ordered]@{
          path = [string]$input.path
          bytes = [Convert]::ToInt64($input.bytes)
          sha256 = ([string]$input.sha256).ToUpperInvariant()
        }
      }
    }
  )
  $context = [ordered]@{
    id = [string]$Metadata.id; title = [string]$Metadata.title; priority = [string]$Metadata.priority
    route = [string]$Metadata.route; owner = [string]$Metadata.owner; domain = [string]$Metadata.domain; stage = [string]$Metadata.stage
    blockedBy = @($Metadata.blockedBy | ForEach-Object { [string]$_ }); expectedPaths = @($Metadata.expectedPaths | ForEach-Object { [string]$_ })
    automationInputs = $automationInputs
    sourceBacklog = [string]$Metadata.sourceBacklog
  }
  if ($Metadata.PSObject.Properties.Name -ccontains 'executionModel') { $context.executionModel = [string]$Metadata.executionModel }
  $json = $context | ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}

function Read-Card {
  param([string]$Path)
  $text = Read-Utf8 $Path
  $match = [regex]::Match($text, '(?ms)\A---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---\r?\n(?<body>.*)\z')
  if (-not $match.Success) { throw 'Task card is invalid' }
  [pscustomobject]@{ Metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100; Body = $match.Groups['body'].Value }
}

function Write-Card {
  param([string]$Path, [object]$Metadata, [string]$Body)
  Write-Utf8 $Path ((@('---TASK-META---', ($Metadata | ConvertTo-Json -Depth 100), '---TASK-BODY---', $Body) -join "`n"))
}

function Get-Table {
  param([string]$Path, [string[]]$Header)
  $text = Read-Utf8 $Path
  $finalNewline = $text.EndsWith("`n", [StringComparison]::Ordinal)
  $lines = [Collections.Generic.List[string]]::new()
  foreach ($line in @($text -split "`n")) { $lines.Add($line.TrimEnd("`r")) }
  $headerIndex = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $cells = if ($lines[$i].Trim().StartsWith('|')) { @($lines[$i].Trim().Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ }) } else { @() }
    if (($cells -join "`0") -ceq ($Header -join "`0")) { $headerIndex = $i; break }
  }
  if ($headerIndex -lt 0) { throw 'Table header is missing' }
  $rows = [Collections.Generic.List[object]]::new()
  $start = $headerIndex + 2
  $end = $start
  while ($end -lt $lines.Count -and $lines[$end].Trim().StartsWith('|') -and -not [string]::IsNullOrWhiteSpace($lines[$end])) {
    $cells = @($lines[$end].Trim().Trim('|').Split('|') | ForEach-Object { Normalize-Cell $_ })
    if ($cells.Count -ne $Header.Count) { throw 'Table row is invalid' }
    $rows.Add($cells); $end++
  }
  [pscustomobject]@{ Path = $Path; Lines = $lines; HeaderIndex = $headerIndex; Start = $start; End = $end; Rows = $rows; FinalNewline = $finalNewline }
}

function Write-Table {
  param([object]$Table)
  $replacement = @($Table.Rows | ForEach-Object { '| ' + (@($_) -join ' | ') + ' |' })
  $count = $Table.End - $Table.Start
  if ($count -gt 0) { $Table.Lines.RemoveRange($Table.Start, $count) }
  if ($replacement.Count -gt 0) { $Table.Lines.InsertRange($Table.Start, [string[]]$replacement) }
  $text = @($Table.Lines) -join "`n"
  if ($Table.FinalNewline -and -not $text.EndsWith("`n", [StringComparison]::Ordinal)) { $text += "`n" }
  Write-Utf8 $Table.Path $text
}

function Find-RowIndex { param([object]$Table, [string]$Id) $matches = @(for ($i = 0; $i -lt $Table.Rows.Count; $i++) { if ([string]$Table.Rows[$i][0] -ceq $Id) { $i } }); if ($matches.Count -gt 1) { throw 'Duplicate table row' }; if ($matches.Count -eq 0) { return -1 }; [int]$matches[0] }

$resumeOriginal = @{}
$failureCode = 'task_automation_state_failed'
try {
  $root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $RepositoryRoot).Path).TrimEnd('\', '/')
  if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw 'Repository root is invalid' }
  if (-not [IO.Path]::IsPathFullyQualified($ContextPath) -or -not (Test-Path -LiteralPath $ContextPath -PathType Leaf)) { throw 'Context is invalid' }
  $context = Read-Utf8 $ContextPath | ConvertFrom-Json -Depth 100 -DateKind String
  if ([int]$context.schemaVersion -ne 1 -or [string]$context.taskId -cne $TaskId) { throw 'Context is invalid' }
  $cardPath = Join-Path $root "开发管理\任务卡\$TaskId.txt"
  $card = Read-Card $cardPath
  $meta = $card.Metadata
  if ([string]$meta.id -cne $TaskId) { throw 'Task identity is invalid' }
  $queuePath = Join-Path $root '开发管理\当前任务队列.txt'
  $queue = Get-Table -Path $queuePath -Header @('ID', '路由', '主责', '优先级', '领域', '阶段', '标题', '任务卡')
  $backlogPath = Join-Path $root ([string]$meta.sourceBacklog)
  $backlog = Get-Table -Path $backlogPath -Header @('ID', '优先级', '主责', '状态投影', '阻塞于', '摘要', '任务卡')
  $queueIndex = Find-RowIndex -Table $queue -Id $TaskId
  $backlogIndex = Find-RowIndex -Table $backlog -Id $TaskId
  if ($backlogIndex -lt 0) { throw 'Backlog row is missing' }

  switch ($Action) {
    'PauseDecision' {
      if ([string]$meta.dispatchState -cne 'ready' -or $queueIndex -lt 0 -or [string]$context.taskContextDigest -cne (Get-TaskContextDigest $meta)) { throw 'Pause precondition failed' }
      foreach ($name in @('sourceRunId', 'owner', 'route', 'decisionId', 'question', 'checkpointCommit', 'baseCommit', 'branch', 'createdAt')) { if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Checkpoint context is incomplete' } }
      if ([string]$context.decisionId -cnotmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { throw 'Checkpoint decisionId is invalid' }
      $context | Add-Member -NotePropertyName queueIndex -NotePropertyValue $queueIndex -Force
      $meta.dispatchState = 'pending_decision'
      $meta.stateReason = "等待负责人决定：$([string]$context.question)"
      $meta | Add-Member -NotePropertyName automationCheckpoint -NotePropertyValue $context -Force
      if ($meta.PSObject.Properties.Name -contains 'automationReply') { $meta.PSObject.Properties.Remove('automationReply') }
      $queue.Rows.RemoveAt($queueIndex)
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '待决定'
    }
    'ResumeReady' {
      if ([string]$meta.dispatchState -cnotin @('pending_decision', 'waiting_reply') -or $queueIndex -ge 0 -or $meta.PSObject.Properties.Name -cnotcontains 'automationCheckpoint') { throw 'Resume precondition failed' }
      $checkpoint = $meta.automationCheckpoint
      if ([string]$checkpoint.decisionId -cne [string]$context.decisionId -or [string]$checkpoint.taskContextDigest -cne (Get-TaskContextDigest $meta)) { throw 'Reply binding is invalid' }
      foreach ($name in @('result', 'replyKind', 'replyValue', 'source', 'evidenceHash')) { if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Reply evidence is incomplete' } }
      # A reply authorizes the selected frozen contract, not arbitrary model-inferred paths.
      $failureCode = 'checkpoint_scope_contract_missing'
      if ([string]$context.result -cne 'OPTION_ACCEPTED' -or [string]$context.replyKind -cne 'option') { throw 'A custom reply requires manual scope preparation' }
      $selected = @($checkpoint.options | Where-Object { [string]$_.key -ceq [string]$context.replyValue })
      if ($selected.Count -ne 1) { throw 'Selected option is missing' }
      $option = $selected[0]
      foreach ($field in @('targetState', 'expectedPaths', 'taskBody')) { if ($option.PSObject.Properties.Name -cnotcontains $field) { throw 'Selected option has no frozen scope' } }
      $failureCode = 'checkpoint_scope_contract_invalid'
      if ([string]$option.targetState -cnotin @('ready', 'blocked') -or $option.expectedPaths -isnot [array] -or $option.taskBody -isnot [string]) { throw 'Invalid selected scope' }
      if ([string]$option.targetState -ceq 'ready') {
        $paths = @($option.expectedPaths)
        if ($paths.Count -eq 0 -or [string]::IsNullOrWhiteSpace($option.taskBody) -or ($option.taskBody -match '---TASK-(META|BODY)---')) { throw 'Incomplete selected scope' }
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($path in $paths) {
          if ($path -isnot [string] -or [string]::IsNullOrWhiteSpace($path) -or $path -cne $path.Trim() -or
              [IO.Path]::IsPathRooted($path) -or $path -match '[\\:*?\[\]\x00-\x1F\x7F]' -or
              @($path.Split('/') | Where-Object { $_ -in @('', '.', '..', '.git', '.worktrees') }).Count -gt 0 -or
              [string]::IsNullOrEmpty([IO.Path]::GetExtension($path)) -or -not $seen.Add($path)) { throw 'Invalid scope path' }
        }
        foreach ($path in @($checkpoint.changedPaths) + @("开发管理/任务卡/$TaskId.txt", "开发管理/任务归档/$TaskId.txt", '开发管理/当前任务队列.txt', [string]$meta.sourceBacklog)) {
          if ($paths -cnotcontains $path) { throw 'Scope excludes checkpoint or lifecycle path' }
        }
        foreach ($path in @($cardPath, $queuePath, $backlogPath)) { $resumeOriginal[$path] = [IO.File]::ReadAllBytes($path) }
        $meta.expectedPaths = $paths
        $card.Body = $option.taskBody
        # Keep pending_decision out of the queue until the new scope passes preflight.
        $meta.schemaVersion = 2
        $refs = @(if ($meta.PSObject.Properties.Name -contains 'riskPreflight') { $meta.riskPreflight.explicitRefs })
        $meta | Add-Member -NotePropertyName riskPreflight -NotePropertyValue ([ordered]@{ explicitRefs = $refs; matched = @(); gates = @() }) -Force
        Write-Card -Path $cardPath -Metadata $meta -Body $card.Body
        $failureCode = 'checkpoint_scope_preflight_failed'
        $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools/get-experience-risk-preflight.ps1') -RepositoryRoot $root -TaskId $TaskId 2>$null)
        if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { throw 'Scope preflight failed' }
        $preflight = $output[0] | ConvertFrom-Json -Depth 100
        if ([string]$preflight.status -cne 'ok' -or [string]$preflight.taskId -cne $TaskId -or
            [string]$preflight.taskCardDigest -cne (Get-NormalizedTextDigest $cardPath) -or [int]$preflight.mustReadChars -gt 600) { throw 'Scope preflight binding failed' }
        foreach ($gate in @($preflight.gates)) {
          $pointers = @($preflight.gatePointers | Where-Object { [string]$_.id -ceq [string]$gate -and -not [string]::IsNullOrWhiteSpace([string]$_.instructionRef) })
          if ($pointers.Count -ne 1) { throw 'Scope preflight gate is missing' }
        }
        $meta.riskPreflight.matched = @($preflight.matched)
        $meta.riskPreflight.gates = @($preflight.gates)
      }
      $reply = [ordered]@{ decisionId = [string]$context.decisionId; result = [string]$context.result; replyKind = [string]$context.replyKind; replyValue = [string]$context.replyValue; source = [string]$context.source; evidenceHash = [string]$context.evidenceHash }
      $meta | Add-Member -NotePropertyName automationReply -NotePropertyValue $reply -Force
      $meta.dispatchState = [string]$option.targetState
      $meta.stateReason = "负责人回复已落实范围：$([string]$context.decisionId)/$([string]$context.replyValue)"
      if ($meta.dispatchState -ceq 'ready') {
        $row = @([string]$meta.id, [string]$meta.route, [string]$meta.owner, [string]$meta.priority, [string]$meta.domain, [string]$meta.stage, [string]$meta.title, "开发管理/任务卡/$TaskId.txt")
        $insert = [Math]::Min([Math]::Max(0, [int]$checkpoint.queueIndex), $queue.Rows.Count)
        $queue.Rows.Insert($insert, $row)
      }
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = if ($meta.dispatchState -ceq 'ready') { '已排队' } else { '阻塞' }
      $failureCode = 'checkpoint_scope_projection_failed'
    }
    'Block' {
      if ([string]$meta.dispatchState -cne 'ready' -or $queueIndex -lt 0 -or [string]::IsNullOrWhiteSpace([string]$context.detailCode)) { throw 'Block precondition failed' }
      $meta.dispatchState = 'blocked'
      $meta.stateReason = "自动责任方确认阻塞：$([string]$context.detailCode)"
      $queue.Rows.RemoveAt($queueIndex)
      $backlog.Rows[$backlogIndex][3] = '阻塞'
    }
    'RequeueReview' {
      if (
        [string]$context.kind -cne 'review_rework' -or
        [string]$context.optionKey -cnotin @('A', 'B') -or
        [string]$context.decisionId -cnotmatch '^DEC-[0-9]{8}-[A-Z0-9]+$' -or
        [string]$context.reviewCommit -cnotmatch '^[0-9a-f]{40,64}$' -or
        [string]$context.reviewEntryDigest -cnotmatch '^[0-9a-f]{64}$' -or
        [string]$context.replyEvidenceHash -cnotmatch '^[0-9a-f]{64}$' -or
        [string]$meta.dispatchState -cne 'blocked' -or
        $queueIndex -ge 0 -or
        [string]$context.taskDigest -cne (Get-NormalizedTextDigest $cardPath) -or
        [string]$context.taskContextDigest -cne (Get-TaskContextDigest $meta) -or
        @($meta.expectedPaths | ForEach-Object { [string]$_ }) -cnotcontains '开发管理/未通过审核清单.txt'
      ) { throw 'Review requeue precondition failed' }
      $target = if ([string]$context.optionKey -ceq 'A') {
        [ordered]@{ route = 'external_execute'; owner = 'deepseek'; label = 'DeepSeek' }
      } else {
        [ordered]@{ route = 'codex_execute'; owner = 'codex'; label = 'Codex' }
      }
      $meta.route = [string]$target.route
      $meta.owner = [string]$target.owner
      if ($meta.owner -cne 'codex') { $meta.PSObject.Properties.Remove('executionModel') }
      $meta.dispatchState = 'ready'
      $meta.stateReason = "负责人已通过 $([string]$context.decisionId) 选择 $([string]$target.label) 按未通过审核清单返工"
      foreach ($field in @('automationCheckpoint', 'automationReply')) {
        if ($meta.PSObject.Properties.Name -contains $field) { $meta.PSObject.Properties.Remove($field) }
      }
      $row = @([string]$meta.id, [string]$meta.route, [string]$meta.owner, [string]$meta.priority, [string]$meta.domain, [string]$meta.stage, [string]$meta.title, "开发管理/任务卡/$TaskId.txt")
      $insert = [Math]::Min([Math]::Max(0, [int]$context.queueIndex), $queue.Rows.Count)
      $queue.Rows.Insert($insert, $row)
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '已排队'
    }
    'PauseMaintenanceDecision' {
      if (
        [string]$context.kind -cne 'queue_maintenance' -or
        [string]$meta.dispatchState -cne 'blocked' -or
        $queueIndex -ge 0 -or
        @($meta.blockedBy).Count -ne 0 -or
        $meta.PSObject.Properties.Name -contains 'automationCheckpoint' -or
        $meta.PSObject.Properties.Name -contains 'automationDecision' -or
        [string]$context.sourceTaskDigest -cne (Get-NormalizedTextDigest $cardPath) -or
        [string]$context.taskContextDigest -cne (Get-TaskContextDigest $meta)
      ) { throw 'Maintenance decision pause precondition failed' }
      foreach ($name in @('sourceRunId', 'decisionId', 'question', 'recommendedOption', 'impactSummary', 'sourceCommit', 'sourceTaskDigest', 'taskContextDigest', 'createdAt')) {
        if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Maintenance decision context is incomplete' }
      }
      if ([string]$context.decisionId -cnotmatch '^DEC-[0-9]{8}-QM[0-9A-F]{12}$' -or [string]$context.sourceCommit -cnotmatch '^[0-9a-f]{40,64}$' -or [string]$context.sourceTaskDigest -cnotmatch '^[0-9a-f]{64}$' -or [string]$context.taskContextDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'Maintenance decision identity is invalid' }
      $options = @($context.options)
      if ($options.Count -ne 3 -or (@($options | ForEach-Object { [string]$_.key }) -join '') -cne 'ABC' -or (@($options | ForEach-Object { [string]$_.targetState }) -join ',') -cne 'ready,ready,blocked' -or [bool]$context.allowCustomReply) { throw 'Maintenance decision options are invalid' }
      $decision = [ordered]@{
        schemaVersion = 1; kind = 'queue_maintenance'; status = 'awaiting_reply'; decisionId = [string]$context.decisionId; taskId = $TaskId
        sourceRunId = [string]$context.sourceRunId; question = [string]$context.question; options = @($options); recommendedOption = [string]$context.recommendedOption
        impactSummary = [string]$context.impactSummary; plainSummary = $context.plainSummary; allowCustomReply = $false
        sourceCommit = [string]$context.sourceCommit; sourceTaskDigest = [string]$context.sourceTaskDigest; taskContextDigest = [string]$context.taskContextDigest
        queueIndex = 0; createdAt = [string]$context.createdAt
      }
      $meta.dispatchState = 'pending_decision'
      $meta.stateReason = "等待负责人决定：$([string]$context.question)"
      $meta | Add-Member -NotePropertyName automationDecision -NotePropertyValue $decision -Force
      if ($meta.PSObject.Properties.Name -contains 'automationReply') { $meta.PSObject.Properties.Remove('automationReply') }
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '待决定'
    }
    'ResolveMaintenanceDecision' {
      if (
        [string]$context.kind -cne 'queue_maintenance' -or
        [string]$meta.dispatchState -cne 'pending_decision' -or
        $queueIndex -ge 0 -or
        $meta.PSObject.Properties.Name -cnotcontains 'automationDecision' -or
        $meta.PSObject.Properties.Name -contains 'automationCheckpoint' -or
        [string]$context.preparedTaskDigest -cne (Get-NormalizedTextDigest $cardPath)
      ) { throw 'Maintenance decision resolve precondition failed' }
      $decision = $meta.automationDecision
      if ([string]$decision.kind -cne 'queue_maintenance' -or [string]$decision.status -cne 'awaiting_reply' -or [string]$decision.decisionId -cne [string]$context.decisionId) { throw 'Maintenance decision binding is invalid' }
      foreach ($name in @('optionKey', 'source', 'evidenceHash', 'resolvedAt', 'preparedTaskDigest')) { if ([string]::IsNullOrWhiteSpace([string]$context.$name)) { throw 'Maintenance decision reply evidence is incomplete' } }
      if ([string]$context.optionKey -cnotin @('A', 'B', 'C') -or [string]$context.source -cne 'feishu_card' -or [string]$context.evidenceHash -cnotmatch '^[0-9a-f]{64}$') { throw 'Maintenance decision reply evidence is invalid' }
      $selected = @($decision.options | Where-Object { [string]$_.key -ceq [string]$context.optionKey })
      if ($selected.Count -ne 1) { throw 'Maintenance decision option is invalid' }
      $targetState = [string]$selected[0].targetState
      $decision.status = 'resolved'
      foreach ($entry in @{
          optionKey = [string]$context.optionKey; targetState = $targetState; replySource = [string]$context.source
          replyEvidenceHash = [string]$context.evidenceHash; resolvedAt = [string]$context.resolvedAt
        }.GetEnumerator()) { $decision | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value -Force }
      $meta.dispatchState = $targetState
      $meta.stateReason = "负责人已通过 $([string]$decision.decisionId) 选择 $([string]$context.optionKey)：$([string]$selected[0].label)"
      if ($targetState -ceq 'ready') {
        $row = @([string]$meta.id, [string]$meta.route, [string]$meta.owner, [string]$meta.priority, [string]$meta.domain, [string]$meta.stage, [string]$meta.title, "开发管理/任务卡/$TaskId.txt")
        $queue.Rows.Insert(0, $row)
        $backlog.Rows[$backlogIndex][3] = '已排队'
      } else {
        $backlog.Rows[$backlogIndex][3] = '阻塞'
      }
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
    }
    'ExpireMaintenanceDecision' {
      if (
        [string]$context.kind -cne 'queue_maintenance' -or
        [string]$meta.dispatchState -cne 'pending_decision' -or
        $queueIndex -ge 0 -or
        $meta.PSObject.Properties.Name -cnotcontains 'automationDecision' -or
        [string]$meta.automationDecision.status -cne 'awaiting_reply' -or
        [string]$meta.automationDecision.decisionId -cne [string]$context.decisionId -or
        [string]$context.detailCode -notin @('maintenance_decision_expired', 'maintenance_decision_reply_invalid') -or
        [string]::IsNullOrWhiteSpace([string]$context.terminatedAt)
      ) { throw 'Maintenance decision expiry precondition failed' }
      $decision = $meta.automationDecision
      $decision.status = if ([string]$context.detailCode -ceq 'maintenance_decision_expired') { 'expired' } else { 'attention_required' }
      $decision | Add-Member -NotePropertyName detailCode -NotePropertyValue ([string]$context.detailCode) -Force
      $decision | Add-Member -NotePropertyName terminatedAt -NotePropertyValue ([string]$context.terminatedAt) -Force
      $meta.dispatchState = 'blocked'
      $meta.stateReason = "维护型决策已停止：$([string]$context.detailCode)"
      $backlog.Rows[$backlogIndex][2] = [string]$meta.owner
      $backlog.Rows[$backlogIndex][3] = '阻塞'
    }
  }

  Write-Card -Path $cardPath -Metadata $meta -Body $card.Body
  Write-Table $queue
  Write-Table $backlog
  $checkArgs = @('-RepositoryRoot', $root, '-TaskId', $TaskId)
  if ($Action -ceq 'PauseMaintenanceDecision') {
    $checkArgs += @('-Postcondition', 'MaintenancePendingDecision')
  } elseif ($Action -ceq 'ResolveMaintenanceDecision') {
    $checkArgs += @('-Postcondition', $(if ([string]$meta.dispatchState -ceq 'ready') { 'MaintenanceResolvedReady' } else { 'MaintenanceResolvedBlocked' }))
  } elseif ($Action -ceq 'ExpireMaintenanceDecision') {
    $checkArgs += $(if ([string]$context.detailCode -ceq 'maintenance_decision_expired') { @('-Postcondition', 'MaintenanceExpiredBlocked') } else { @('-Postcondition', 'CodexClosedOrNonReady') })
  } elseif ($Action -notin @('ResumeReady', 'RequeueReview') -or [string]$meta.dispatchState -cne 'ready') {
    $checkArgs += @('-Postcondition', 'CodexClosedOrNonReady')
  } elseif ([string]$meta.owner -ceq 'codex') {
    $checkArgs += @('-Postcondition', 'CodexDispatchReady', '-ExpectedRoute', [string]$meta.route)
  } else {
    $checkArgs += @('-Postcondition', 'ExternalDispatchReady', '-ExpectedOwner', [string]$meta.owner)
  }
  $checkArgs += '-OutputJson'
  $check = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\check-task-cards.ps1') @checkArgs 2>$null)
  if ($LASTEXITCODE -ne 0 -or $check.Count -ne 1) { throw 'Task projection validation failed' }
  [Console]::Out.WriteLine(([ordered]@{ status = 'updated'; taskId = $TaskId; dispatchState = [string]$meta.dispatchState; changedPaths = @("开发管理/任务卡/$TaskId.txt", '开发管理/当前任务队列.txt', [string]$meta.sourceBacklog) } | ConvertTo-Json -Compress))
} catch {
  foreach ($entry in $resumeOriginal.GetEnumerator()) { [IO.File]::WriteAllBytes($entry.Key, $entry.Value) }
  [Console]::Out.WriteLine(([ordered]@{ status = 'failed'; taskId = $TaskId; detailCode = $failureCode } | ConvertTo-Json -Compress))
  exit 1
}
