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
Assert-Equal $deepseek.model 'deepseek-v4-flash' 'DeepSeek model is invalid'
Assert-Equal ($deepseek.allowedRoutes -join ',') 'external_execute' 'DeepSeek route is invalid'
Assert-True ($deepseek.formalMode -eq 'external_pending_review') 'DeepSeek formal mode is invalid'

$fakeRun = [pscustomobject]@{ route='codex_review'; worktree='C:\fixture'; taskId='T-1'; runId='R-1' }
$codexArgs = Get-HourlyCandidateArguments -Adapter $codex -Run $fakeRun -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath 'C:\state\resume.json'
Assert-True ($codexArgs -contains 'Review' -and $codexArgs -contains 'gpt-test' -and $codexArgs -contains 'C:\state\resume.json') 'Codex candidate arguments are incomplete'
$deepArgs = Get-HourlyCandidateArguments -Adapter $deepseek -Run ([pscustomobject]@{ route='external_execute'; worktree='C:\fixture'; taskId='T-2'; runId='R-2' }) -StateRoot 'C:\state' -TimeoutSeconds 10 -ResumeContextPath $null
Assert-True ($deepArgs -contains 'Candidate' -and $deepArgs -notcontains 'gpt-test') 'DeepSeek candidate arguments are invalid'

$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'hourly-owner-adapter.ps1'))
Assert-True ($source -match 'Test-HourlyOwnerModelVerified') 'Owner adapter is missing the model verification guard'
Assert-True ($source -notmatch '(?i)Invoke-Git|git\s+-C|hourly-automation-lease|CompleteRun|AcquireIntegration|merge\s+--') 'Owner adapter contains shared Git or runtime orchestration'
Write-Output 'test-hourly-owner-adapter: OK'
