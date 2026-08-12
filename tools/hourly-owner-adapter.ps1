#requires -Version 7.0

Set-StrictMode -Version Latest

function Test-HourlyOwnerModelVerified {
  param(
    [Parameter(Mandatory = $true)][ValidateSet('codex', 'deepseek')][string]$Owner,
    [AllowNull()][string]$Model
  )

  if ($Owner -cne 'codex') { return $true }
  if ([string]::IsNullOrWhiteSpace($Model) -or $Model -match '[\x00-\x1F\x7F]') { return $false }
  $Model -cmatch '^gpt-[A-Za-z0-9][A-Za-z0-9._-]*$'
}

function Get-HourlyOwnerAdapter {
  param(
    [Parameter(Mandatory = $true)][ValidateSet('codex', 'deepseek')][string]$Owner,
    [AllowNull()][string]$Model,
    [Parameter(Mandatory = $true)][string]$ToolsRoot
  )

  if ($Owner -ceq 'codex') {
    if ([string]::IsNullOrWhiteSpace($Model)) { throw 'Codex model is required' }
    return [pscustomobject][ordered]@{
      owner = 'codex'
      allowedRoutes = @('codex_execute', 'codex_review', 'queue_maintenance')
      sessionKind = 'codex_cli'
      candidateScript = Join-Path $ToolsRoot 'invoke-codex-candidate.ps1'
      model = $Model
      identity = 'Codex'
      formalMode = 'candidate_commit'
      successPostcondition = 'CodexClosedOrNonReady'
      completedStatus = 'completed'
    }
  }

  [pscustomobject][ordered]@{
    owner = 'deepseek'
    allowedRoutes = @('external_execute')
    sessionKind = 'claude_cli'
    candidateScript = Join-Path $ToolsRoot 'invoke-deepseek-responsibility.ps1'
    model = 'deepseek-v4-pro'
    identity = 'DeepSeek V4 Pro 0813'
    formalMode = 'external_pending_review'
    successPostcondition = 'ExternalPendingReview'
    completedStatus = 'pending_review'
  }
}

function Get-HourlyFormalCommitContract {
  param(
    [Parameter(Mandatory = $true)][object]$Adapter,
    [Parameter(Mandatory = $true)][object]$Run
  )

  $owner = [string]$Adapter.owner
  $route = [string]$Run.route
  $taskId = [string]$Run.taskId
  if ([string]::IsNullOrWhiteSpace($taskId) -or $taskId -match '[\x00-\x1F\x7F]') {
    throw 'Formal commit taskId is invalid'
  }

  if ($owner -ceq 'codex') {
    $subject = switch ($route) {
      'queue_maintenance' {
        if ($taskId -cne 'QUEUE-MAINTENANCE') { throw 'QueueMaintenance taskId is invalid' }
        'chore(QUEUE-MAINTENANCE): maintain task queue'
      }
      'codex_execute' { "feat($taskId): complete Codex task" }
      'codex_review' { "review($taskId): complete Codex review" }
      default { throw 'Codex formal route is invalid' }
    }
    return [pscustomobject][ordered]@{ subject = $subject; state = 'completed' }
  }

  if ($owner -ceq 'deepseek' -and $route -ceq 'external_execute') {
    return [pscustomobject][ordered]@{
      subject = "feat($taskId): complete DeepSeek task"
      state = 'pending_review'
    }
  }

  throw 'Formal owner route is invalid'
}

function Get-HourlyCandidateArguments {
  param(
    [Parameter(Mandatory = $true)][object]$Adapter,
    [Parameter(Mandatory = $true)][object]$Run,
    [Parameter(Mandatory = $true)][string]$StateRoot,
    [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
    [AllowNull()][string]$ResumeContextPath
  )

  if ([string]$Adapter.owner -ceq 'codex') {
    $route = switch ([string]$Run.route) {
      'codex_execute' { 'Execution' }
      'codex_review' { 'Review' }
      'queue_maintenance' { 'QueueMaintenance' }
      default { throw 'Codex route is invalid' }
    }
    $arguments = @(
      '-Action', 'Candidate', '-Route', $route, '-RepositoryRoot', [string]$Run.worktree,
      '-TaskId', [string]$Run.taskId, '-RunId', [string]$Run.runId, '-Model', [string]$Adapter.model,
      '-StateRoot', $StateRoot, '-ResponsibilityTimeoutSeconds', [string]$TimeoutSeconds
    )
  } else {
    $arguments = @(
      '-Action', 'Candidate', '-RepositoryRoot', [string]$Run.worktree, '-TaskId', [string]$Run.taskId,
      '-RunId', [string]$Run.runId, '-StateRoot', $StateRoot,
      '-ResponsibilityTimeoutSeconds', [string]$TimeoutSeconds
    )
  }
  if (-not [string]::IsNullOrWhiteSpace($ResumeContextPath)) { $arguments += @('-ResumeContextPath', $ResumeContextPath) }
  $arguments
}

function Get-HourlyCanaryArguments {
  param(
    [Parameter(Mandatory = $true)][object]$Adapter,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$StateRoot,
    [Parameter(Mandatory = $true)][int]$TimeoutSeconds
  )

  if ([string]$Adapter.owner -ceq 'codex') {
    @(
      '-Action', 'Canary', '-RepositoryRoot', $RepositoryRoot, '-TaskId', 'CANARY', '-RunId', 'CANARY',
      '-Model', [string]$Adapter.model, '-StateRoot', $StateRoot, '-ResponsibilityTimeoutSeconds', [string]$TimeoutSeconds
    )
  } else {
    @('-Action', 'Canary', '-RepositoryRoot', $RepositoryRoot, '-StateRoot', $StateRoot, '-ResponsibilityTimeoutSeconds', [string]$TimeoutSeconds)
  }
}
