#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }

$sourcePath = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
$source = [IO.File]::ReadAllText($sourcePath)
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($source, [ref]$tokens, [ref]$errors)
Assert-Equal @($errors).Count 0 'Shared owner entry did not parse'
foreach ($functionAst in @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false))) {
  Invoke-Expression $functionAst.Extent.Text
}

$script:notificationCalls = 0
function Invoke-AttentionNotification {
  param([object]$Run, [string]$DetailCode)
  $script:notificationCalls++
  "sent:$($Run.runId):$DetailCode"
}

$run = [pscustomobject]@{ taskId = 'TASK-ATTENTION'; runId = 'run-attention' }
$attention = [ordered]@{ status = 'attention_required'; taskId = 'TASK-ATTENTION'; runId = 'run-attention'; detailCode = 'formal_failed' }
$attentionResult = Add-AttentionNotification -Final $attention -Run $run
Assert-Equal $script:notificationCalls 1 'Attention result did not send exactly one notification'
Assert-Equal $attentionResult.notification 'sent:run-attention:formal_failed' 'Attention result did not retain the sanitized notification outcome'

$existing = [ordered]@{ status = 'existing_run'; taskId = 'TASK-ATTENTION'; runId = 'run-attention'; detailCode = 'formal_failed' }
$null = Add-AttentionNotification -Final $existing -Run $run
Assert-Equal $script:notificationCalls 1 'Existing run sent a repeated notification'

$completed = [ordered]@{ status = 'completed'; taskId = 'TASK-ATTENTION'; runId = 'run-attention'; detailCode = 'commit_abc' }
$null = Add-AttentionNotification -Final $completed -Run $run
Assert-Equal $script:notificationCalls 1 'Completed result sent an attention notification'

$attentionWithoutRun = [ordered]@{ status = 'attention_required'; taskId = 'TASK-ATTENTION'; detailCode = 'preclaim_failed' }
$null = Add-AttentionNotification -Final $attentionWithoutRun -Run $null
Assert-Equal $script:notificationCalls 1 'Attention result without an owner run sent a notification'

$notificationFailure = [ordered]@{ status = 'attention_required'; taskId = 'TASK-ATTENTION'; runId = 'run-attention'; detailCode = 'notification_failed' }
function Invoke-AttentionNotification { param([object]$Run, [string]$DetailCode) $script:notificationCalls++; 'failed' }
$failureResult = Add-AttentionNotification -Final $notificationFailure -Run $run
Assert-Equal $failureResult.status 'attention_required' 'Notification failure changed the attention terminal state'
Assert-Equal $failureResult.notification 'failed' 'Notification failure was not reported as a sanitized result'

Assert-True ($source.Contains('$final = Add-AttentionNotification -Final $final -Run $run')) 'Shared owner entry did not apply the final attention notification step'

Write-Output 'test-hourly-attention-notification: OK'
