#requires -Version 7.0

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'hourly-owner-adapter.ps1')

function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }

$codex = Get-HourlyOwnerAdapter -Owner codex -Model 'gpt-test' -ToolsRoot $PSScriptRoot
$deepseek = Get-HourlyOwnerAdapter -Owner deepseek -Model $null -ToolsRoot $PSScriptRoot
Assert-True (Test-HourlyOwnerModelVerified -Owner codex -Model 'gpt-test') 'Valid Codex model was rejected'
Assert-True (-not (Test-HourlyOwnerModelVerified -Owner codex -Model 'unknown')) 'Unknown Codex model was accepted'
Assert-True (-not (Test-HourlyOwnerModelVerified -Owner codex -Model "gpt-test`ninvalid")) 'Codex model with control characters was accepted'
Assert-True (-not (Test-HourlyOwnerModelVerified -Owner codex -Model '')) 'Empty Codex model was accepted'
Assert-True (Test-HourlyOwnerModelVerified -Owner deepseek -Model 'unknown') 'DeepSeek was incorrectly routed through the Codex model guard'
Assert-Equal $codex.model 'gpt-test' 'Codex model was not preserved'
Assert-Equal ($codex.allowedRoutes -join ',') 'codex_execute,codex_review,queue_maintenance' 'Codex routes are invalid'
Assert-Equal $deepseek.model 'deepseek-v4-pro' 'DeepSeek model is invalid'
Assert-Equal ($deepseek.allowedRoutes -join ',') 'external_execute' 'DeepSeek route is invalid'
Assert-True ($deepseek.formalMode -eq 'external_pending_review') 'DeepSeek formal mode is invalid'

$queueFormal = Get-HourlyFormalCommitContract -Adapter $codex -Run ([pscustomobject]@{ route='queue_maintenance'; taskId='QUEUE-MAINTENANCE' })
$executeFormal = Get-HourlyFormalCommitContract -Adapter $codex -Run ([pscustomobject]@{ route='codex_execute'; taskId='T-EXEC' })
$reviewFormal = Get-HourlyFormalCommitContract -Adapter $codex -Run ([pscustomobject]@{ route='codex_review'; taskId='T-REVIEW' })
$deepseekFormal = Get-HourlyFormalCommitContract -Adapter $deepseek -Run ([pscustomobject]@{ route='external_execute'; taskId='T-EXT' })
Assert-Equal $queueFormal.subject 'chore(QUEUE-MAINTENANCE): maintain task queue' 'QueueMaintenance formal subject is invalid'
Assert-Equal $executeFormal.subject 'feat(T-EXEC): complete Codex task' 'Codex execute formal subject is invalid'
Assert-Equal $reviewFormal.subject 'review(T-REVIEW): complete Codex review' 'Codex review formal subject is invalid'
Assert-Equal $queueFormal.state 'completed' 'QueueMaintenance formal state is invalid'
Assert-Equal $executeFormal.state 'completed' 'Codex execute formal state is invalid'
Assert-Equal $reviewFormal.state 'completed' 'Codex review formal state is invalid'
Assert-Equal $deepseekFormal.subject 'feat(T-EXT): complete DeepSeek task' 'DeepSeek formal subject changed'
Assert-Equal $deepseekFormal.state 'pending_review' 'DeepSeek formal state changed'

$fakeRun = [pscustomobject]@{ route='codex_review'; worktree='C:\fixture'; taskId='T-1'; runId='R-1' }
$codexArgs = Get-HourlyCandidateArguments -Adapter $codex -Run $fakeRun -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath 'C:\state\resume.json' -PreflightResultPath 'C:\state\preflight.json'
Assert-True ($codexArgs -contains 'Review' -and $codexArgs -contains 'gpt-test' -and $codexArgs -contains 'C:\state\resume.json') 'Codex candidate arguments are incomplete'
Assert-True ($codexArgs -contains '-PreflightResultPath' -and $codexArgs -contains 'C:\state\preflight.json') 'Codex candidate arguments are missing the preflight result path'
$deepArgs = Get-HourlyCandidateArguments -Adapter $deepseek -Run ([pscustomobject]@{ route='external_execute'; worktree='C:\fixture'; taskId='T-2'; runId='R-2' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath 'C:\state\preflight.json'
Assert-True ($deepArgs -contains 'Candidate' -and $deepArgs -notcontains 'gpt-test') 'DeepSeek candidate arguments are invalid'
Assert-True ($deepArgs -contains '-PreflightResultPath') 'DeepSeek candidate arguments are missing the preflight result path'
$qmArgs = Get-HourlyCandidateArguments -Adapter $codex -Run ([pscustomobject]@{ route='queue_maintenance'; worktree='C:\fixture'; taskId='QUEUE-MAINTENANCE'; runId='R-QM' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath $null
Assert-True ($qmArgs -notcontains '-PreflightResultPath') 'QueueMaintenance candidate arguments must not include the preflight result path'
$threwWithoutPath = $false
try { $null = Get-HourlyCandidateArguments -Adapter $deepseek -Run ([pscustomobject]@{ route='external_execute'; worktree='C:\fixture'; taskId='T-3'; runId='R-3' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null -PreflightResultPath $null } catch { $threwWithoutPath = $true }
Assert-True $threwWithoutPath 'Non-queue-maintenance candidate without a preflight result path must fail'

$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'hourly-owner-adapter.ps1'))
Assert-True ($source -match 'Test-HourlyOwnerModelVerified') 'Owner adapter is missing the model verification guard'
Assert-True ($source -notmatch '(?i)Invoke-Git|git\s+-C|hourly-automation-lease|CompleteRun|AcquireIntegration|merge\s+--') 'Owner adapter contains shared Git or runtime orchestration'
Write-Output 'test-hourly-owner-adapter: OK'
