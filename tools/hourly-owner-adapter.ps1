#requires -Version 7.0

Set-StrictMode -Version Latest

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
    model = 'deepseek-v4-flash'
    identity = 'DeepSeek V4 Flash'
    formalMode = 'external_pending_review'
    successPostcondition = 'ExternalPendingReview'
    completedStatus = 'pending_review'
  }
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
