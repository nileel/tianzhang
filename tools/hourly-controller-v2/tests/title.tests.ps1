#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'title.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'title.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking
Import-Module (Join-Path $v2Root 'state.psm1') -Force -DisableNameChecking

$threadId = '11111111-1111-1111-1111-111111111111'
$request = New-TitleRequest `
  -Model 'actual-model' `
  -ThreadId $threadId `
  -MetadataThreadId $threadId `
  -TaskTitle 'D-TRUST-02：清理现存数据矛盾'
Assert-Equal (($request.PSObject.Properties.Name) -join '|') 'model|threadId|metadataThreadId|title' 'title request fields'
Assert-Equal $request.model 'actual-model' 'title request model'
Assert-Equal $request.title 'TZG｜D-TRUST-02：清理现存数据矛盾' 'fixed task title'

$payload = Get-TitleToolPayload -TitleRequest $request
Assert-Equal (($payload.PSObject.Properties.Name) -join '|') 'threadId|title' 'title tool payload fields'
Assert-Equal $payload.threadId $threadId 'title tool thread id'
Assert-Equal $payload.title 'TZG｜D-TRUST-02：清理现存数据矛盾' 'title tool fixed title'

foreach ($case in @(
    @{ Model = ''; ThreadId = $threadId; MetadataThreadId = $threadId; Code = 'metadata_missing'; Label = 'missing model' },
    @{ Model = 'actual-model'; ThreadId = ''; MetadataThreadId = $threadId; Code = 'metadata_missing'; Label = 'missing top-level thread id' },
    @{ Model = 'actual-model'; ThreadId = $threadId; MetadataThreadId = ''; Code = 'metadata_missing'; Label = 'missing metadata thread id' },
    @{ Model = 'actual-model'; ThreadId = 'not-a-uuid'; MetadataThreadId = 'not-a-uuid'; Code = 'metadata_missing'; Label = 'malformed thread ids' },
    @{ Model = 'actual-model'; ThreadId = $threadId; MetadataThreadId = '22222222-2222-2222-2222-222222222222'; Code = 'thread_id_mismatch'; Label = 'thread id mismatch' }
  )) {
  Assert-Throws `
    -Script {
      New-TitleRequest -Model $case.Model -ThreadId $case.ThreadId -MetadataThreadId $case.MetadataThreadId -TaskTitle '任务标题'
    } `
    -MessageLike $case.Code `
    -Label $case.Label
}

$state = New-ControllerState
$state = Move-ControllerPhase -State $state -From @('IDLE') -To 'DISCOVERING'
$state.activeRun = [pscustomobject][ordered]@{
  runId = '33333333-3333-3333-3333-333333333333'
  nextAction = 'RecordTitleResult'
}
$failedState = Record-TitleResult -State $state -Succeeded $false -Diagnostic 'openId=private-user messageId=private-message tool timeout'
Assert-Equal $failedState.phase 'DISCOVERING' 'title failure phase'
Assert-Equal $failedState.activeRun.titleStatus 'FAILED' 'title failure status'
Assert-Equal $failedState.activeRun.nextAction 'DiscoverRead' 'title failure next action'
Assert-False ([bool]($failedState.activeRun.titleDiagnostic -match 'private-user|private-message')) 'title diagnostic private values'
Assert-True ([bool]$failedState.activeRun.titleDiagnostic.Contains('[REDACTED]')) 'title diagnostic redaction marker'

$successState = Record-TitleResult -State $failedState -Succeeded $true -Diagnostic 'title updated'
Assert-Equal $successState.activeRun.titleStatus 'SUCCEEDED' 'title success status'
Assert-Equal $successState.activeRun.titleDiagnostic 'title updated' 'title success diagnostic'
Assert-Equal $successState.activeRun.nextAction 'DiscoverRead' 'title success next action'

Write-Output 'title.tests: OK'
