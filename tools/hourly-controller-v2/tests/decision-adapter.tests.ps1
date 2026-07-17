#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'decision-adapter.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'decision-adapter.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking

function New-TestDecision {
  New-DecisionRequest `
    -TaskId 'TQ-TEST' `
    -Question '请选择本次实现范围。' `
    -Options @(
      [ordered]@{
        key = 'A'
        text = '采用方案甲，并且只修改冻结路径。'
        recommended = $true
        scopeContract = [ordered]@{
          expectedPaths = @('src/a.txt', 'src/b.txt')
          requiredChecks = @('data-chain')
        }
      },
      [ordered]@{
        key = 'B'
        text = '采用方案乙，并返回重新发现阶段。'
        recommended = $false
        scopeContract = [ordered]@{
          expectedPaths = @('src/c.txt')
          requiredChecks = @('pending-whitespace')
        }
      },
      [ordered]@{
        key = 'C'
        text = '停止本次任务，不授权任何修改。'
        recommended = $false
        scopeContract = [ordered]@{
          expectedPaths = @()
          requiredChecks = @()
        }
      }
    )
}

function Read-TestJson {
  param([Parameter(Mandatory = $true)][string]$Path)

  $text = [IO.File]::ReadAllText($Path)
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $text | ConvertFrom-Json -DateKind String
  } else {
    $text | ConvertFrom-Json
  }
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-decision-' + [guid]::NewGuid().ToString('N'))
$bridgeRoot = Join-Path $sandbox 'fake-bridge'
$captureRoot = Join-Path $sandbox 'capture'
$runRoot = Join-Path $sandbox 'private-run'
$oldMode = $env:FAKE_BRIDGE_MODE
$oldCapture = $env:FAKE_BRIDGE_CAPTURE

try {
  [IO.Directory]::CreateDirectory((Join-Path $bridgeRoot 'src')) | Out-Null
  [IO.Directory]::CreateDirectory($captureRoot) | Out-Null
  [IO.Directory]::CreateDirectory($runRoot) | Out-Null

  Write-TestUtf8 -Path (Join-Path $bridgeRoot 'src\send-decision.mjs') -Value @'
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const requestPath = process.argv[3];
const root = process.env.FAKE_BRIDGE_CAPTURE;
const request = JSON.parse(readFileSync(requestPath, 'utf8'));
writeFileSync(join(root, 'send-request.json'), JSON.stringify(request));
const countPath = join(root, 'send-count.txt');
let count = 0;
try { count = Number(readFileSync(countPath, 'utf8')); } catch {}
writeFileSync(countPath, String(count + 1));
if (process.env.FAKE_BRIDGE_MODE === 'unavailable') {
  process.stdout.write('{"result":"CHANNEL_UNAVAILABLE"}\n');
  process.exitCode = 20;
} else {
  process.stdout.write(JSON.stringify({
    result: 'PROVIDER_ACCEPTED',
    targetHash: 'a'.repeat(64),
    providerMessageIdHash: 'b'.repeat(64),
    providerChatIdHash: 'c'.repeat(64),
    cardNonceHash: 'd'.repeat(64),
    intentKeyHash: 'e'.repeat(64),
  }) + '\n');
}
'@

  Write-TestUtf8 -Path (Join-Path $bridgeRoot 'src\consume-reply.mjs') -Value @'
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const requestPath = process.argv[3];
const root = process.env.FAKE_BRIDGE_CAPTURE;
const request = JSON.parse(readFileSync(requestPath, 'utf8'));
writeFileSync(join(root, 'consume-request.json'), JSON.stringify(request));
const countPath = join(root, 'consume-count.txt');
let count = 0;
try { count = Number(readFileSync(countPath, 'utf8')); } catch {}
writeFileSync(countPath, String(count + 1));
const hashes = {
  providerMessageIdHash: 'b'.repeat(64),
  providerEventIdHash: 'f'.repeat(64),
  operatorOpenIdHash: '1'.repeat(64),
  tenantKeyHash: '2'.repeat(64),
  cardNonceHash: 'd'.repeat(64),
  evidenceHash: '3'.repeat(64),
};
switch (process.env.FAKE_BRIDGE_MODE) {
  case 'option':
    process.stdout.write(JSON.stringify({ result: 'OPTION_ACCEPTED', optionKey: 'A', source: 'feishu_card', ...hashes }) + '\n');
    break;
  case 'custom':
    process.stdout.write(JSON.stringify({ result: 'CUSTOM_ACCEPTED', decisionId: request.pendingDecision.decisionId, customText: '改用更窄的实现范围', source: 'feishu_card_input', ...hashes }) + '\n');
    break;
  case 'unavailable':
    process.stdout.write('{"result":"CHANNEL_UNAVAILABLE"}\n');
    process.exitCode = 20;
    break;
  default:
    process.stdout.write('{"result":"NO_REPLY"}\n');
}
'@

  $env:FAKE_BRIDGE_CAPTURE = $captureRoot
  $env:FAKE_BRIDGE_MODE = 'accepted'

  $decision = New-TestDecision
  Assert-True ([bool]($decision.decisionId -match '^DEC-[0-9]{8}-[A-Z0-9]+$')) 'decision id format'
  Assert-Equal $decision.recommendedOption 'A' 'recommended option'
  Assert-Equal $decision.options[0].scopeContract.expectedPaths[1] 'src/b.txt' 'frozen option scope'

  $sent = Send-DecisionRequest -Decision $decision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  Assert-True ([bool]$sent.ok) 'send accepted'
  Assert-Equal $sent.phase 'WAITING_DECISION' 'send phase'
  Assert-Equal $sent.nextAction 'ConsumeDecisionReply' 'send next action'
  Assert-False ([bool]$sent.authorized) 'send does not authorize'
  Assert-Equal $sent.errorCode $null 'send error code'

  $capturedSend = Read-TestJson -Path (Join-Path $captureRoot 'send-request.json')
  Assert-Equal (($capturedSend.PSObject.Properties.Name) -join '|') 'attemptNumber|decision' 'bridge send request fields'
  Assert-Equal $capturedSend.decision.options[0].label '采用方案甲，并且只修改冻结路径。' 'complete option text sent to card body'
  Assert-Equal $capturedSend.decision.options[2].label '停止本次任务，不授权任何修改。' 'third option text sent to card body'

  $recordPath = Join-Path $runRoot "decisions\$($decision.decisionId).json"
  $record = Read-TestJson -Path $recordPath
  Assert-Equal $record.decision.options[0].scopeContract.expectedPaths[0] 'src/a.txt' 'persisted scope contract'
  Assert-Equal $record.phase 'WAITING_DECISION' 'persisted waiting phase'
  Assert-Equal $record.resolution $null 'unresolved decision record'

  $env:FAKE_BRIDGE_MODE = 'none'
  $pending = Consume-DecisionReply -Decision $decision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  Assert-True ([bool]$pending.ok) 'no reply is stable'
  Assert-Equal $pending.phase 'WAITING_DECISION' 'no reply phase'
  Assert-Equal $pending.nextAction 'ConsumeDecisionReply' 'no reply next action'
  Assert-Equal $pending.errorCode 'decision_pending' 'no reply code'
  Assert-False ([bool]$pending.authorized) 'no reply does not authorize'

  $env:FAKE_BRIDGE_MODE = 'option'
  $accepted = Consume-DecisionReply -Decision $decision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  Assert-True ([bool]$accepted.ok) 'option accepted'
  Assert-Equal $accepted.phase 'IMPLEMENTATION_PENDING' 'option phase'
  Assert-Equal $accepted.nextAction 'SubmitManifest' 'option next action'
  Assert-Equal $accepted.resolutionKind 'OPTION' 'option resolution kind'
  Assert-Equal $accepted.selectedOptionId 'A' 'selected option'
  Assert-Equal $accepted.resolutionText '采用方案甲，并且只修改冻结路径。' 'selected full text'
  Assert-Equal $accepted.scopeContract.expectedPaths[1] 'src/b.txt' 'selected scope result'
  Assert-False ([bool]$accepted.authorized) 'option still requires manifest approval'

  $resolvedRecord = Read-TestJson -Path $recordPath
  Assert-Equal $resolvedRecord.resolution.scopeContract.requiredChecks[0] 'data-chain' 'resolved scope persisted'
  $consumeCountBeforeConflict = [int][IO.File]::ReadAllText((Join-Path $captureRoot 'consume-count.txt'))
  $env:FAKE_BRIDGE_MODE = 'custom'
  $conflict = Consume-DecisionReply -Decision $decision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  $consumeCountAfterConflict = [int][IO.File]::ReadAllText((Join-Path $captureRoot 'consume-count.txt'))
  Assert-Equal $conflict.resolutionKind 'OPTION' 'first valid reply wins'
  Assert-Equal $conflict.selectedOptionId 'A' 'conflict does not overwrite'
  Assert-Equal $consumeCountAfterConflict $consumeCountBeforeConflict 'resolved reply is idempotent'

  $customDecision = New-TestDecision
  $env:FAKE_BRIDGE_MODE = 'accepted'
  Send-DecisionRequest -Decision $customDecision -RunRoot $runRoot -BridgeRoot $bridgeRoot | Out-Null
  $env:FAKE_BRIDGE_MODE = 'custom'
  $custom = Consume-DecisionReply -Decision $customDecision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  Assert-True ([bool]$custom.ok) 'custom accepted'
  Assert-Equal $custom.phase 'IMPLEMENTATION_PENDING' 'custom phase'
  Assert-Equal $custom.resolutionKind 'CUSTOM' 'custom resolution kind'
  Assert-Equal $custom.resolutionText '改用更窄的实现范围' 'custom text'
  Assert-True ([bool]$custom.requiresManifestApproval) 'custom requires new manifest approval'
  Assert-False ([bool]$custom.authorized) 'custom does not authorize mutation'

  $unavailableDecision = New-TestDecision
  $env:FAKE_BRIDGE_MODE = 'unavailable'
  $unavailable = Send-DecisionRequest -Decision $unavailableDecision -RunRoot $runRoot -BridgeRoot $bridgeRoot
  Assert-False ([bool]$unavailable.ok) 'unavailable send'
  Assert-Equal $unavailable.errorCode 'feishu_unavailable' 'unavailable code'
  Assert-Equal $unavailable.phase 'WAITING_DECISION' 'unavailable preserves decision phase'
  Assert-False ([bool]$unavailable.authorized) 'unavailable does not authorize'
  $unavailableRecord = Read-TestJson -Path (Join-Path $runRoot "decisions\$($unavailableDecision.decisionId).json")
  Assert-Equal $unavailableRecord.resolution $null 'unavailable preserves unresolved decision'

  $fixturePath = Join-Path $PSScriptRoot 'fixtures\tq057-valid-manifest.json'
  $manifest = Read-TestJson -Path $fixturePath
  $approval = New-ManifestApprovalDecision -Manifest $manifest
  Assert-Equal $approval.taskId 'TQ-057' 'approval task id'
  Assert-True ([bool]$approval.question.Contains('任务 TQ-057')) 'approval task body'
  foreach ($path in @($manifest.expectedPaths)) {
    Assert-True ([bool]$approval.question.Contains($path)) "approval path $path"
  }
  foreach ($coverage in @($manifest.decisionCoverage)) {
    Assert-True ([bool]$approval.question.Contains($coverage.decisionId)) "approval decision $($coverage.decisionId)"
  }
  foreach ($check in @($manifest.requiredChecks)) {
    Assert-True ([bool]$approval.question.Contains($check)) "approval check $check"
  }
  Assert-True ([bool]$approval.question.Contains("$($approval.decisionId)：自定义 <你的方案>")) 'approval copy reply format'
  Assert-Equal @($approval.options[0].scopeContract.expectedPaths).Count 13 'approval frozen paths'
  Assert-Equal @($approval.options[0].scopeContract.decisionIds).Count 5 'approval frozen decisions'
  Assert-Equal @($approval.options[0].scopeContract.requiredChecks).Count 4 'approval frozen checks'

  $visibleJson = @($sent, $pending, $accepted, $conflict, $custom, $unavailable, $approval) | ConvertTo-Json -Depth 100
  foreach ($forbidden in @('appSecret', 'tenantKey', 'openId', 'chatId', 'messageId', 'eventId', 'providerMessageId', 'providerEventId', 'evidenceHash', 'rawEvent')) {
    Assert-False ([bool]($visibleJson -match [regex]::Escape($forbidden))) "forbidden visible field $forbidden"
  }

  Write-Output 'decision-adapter.tests: OK'
} finally {
  $env:FAKE_BRIDGE_MODE = $oldMode
  $env:FAKE_BRIDGE_CAPTURE = $oldCapture
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    Remove-Item -LiteralPath $resolvedSandbox -Recurse -Force
  }
}
