#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$controllerPath = Join-Path $v2Root 'controller.ps1'
$verificationPath = Join-Path $v2Root 'verification.psm1'
if (-not (Test-Path -LiteralPath $controllerPath -PathType Leaf)) {
  throw 'controller.ps1 is missing'
}
if (-not (Test-Path -LiteralPath $verificationPath -PathType Leaf)) {
  throw 'verification.psm1 is missing'
}

$projectRoot = Split-Path -Parent (Split-Path -Parent $v2Root)
$engine = Join-Path $PSHOME 'pwsh.exe'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-controller-' + [guid]::NewGuid().ToString('N'))
$originalUserProfile = $env:USERPROFILE
$originalBridgeMode = $env:FAKE_BRIDGE_MODE
$originalCheckLog = $env:TZG_FAKE_CHECK_LOG
$originalFailCheck = $env:TZG_FAKE_FAIL_CHECK

function Invoke-TestGit {
  param(
    [Parameter(Mandatory = $true)][string]$Repository,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
  )

  $output = @(& git -C $Repository @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "git $($Arguments -join ' ') failed: $($output -join "`n")"
  }
  @($output)
}

function Copy-TestFile {
  param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Destination
  )

  [IO.Directory]::CreateDirectory((Split-Path -Parent $Destination)) | Out-Null
  [IO.File]::Copy($Source, $Destination, $true)
}

function Get-TestSha256 {
  param([Parameter(Mandatory = $true)][string]$Path)

  (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
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

function Assert-StableResponse {
  param(
    [Parameter(Mandatory = $true)]$Response,
    [Parameter(Mandatory = $true)][string]$Action,
    [Parameter(Mandatory = $true)][string]$Label
  )

  Assert-Equal (($Response.PSObject.Properties.Name) -join '|') 'schemaVersion|ok|action|runId|taskId|phase|nextAction|errorCode|changedPaths|requiredSources|requiredChecks|decisionConstraints|result' "$Label fields"
  Assert-Equal $Response.schemaVersion 1 "$Label schema"
  Assert-Equal $Response.action $Action "$Label action"
  $json = $Response | ConvertTo-Json -Depth 100 -Compress
  foreach ($forbidden in @('appSecret', 'tenantKey', 'openId', 'chatId', 'messageId', 'eventId', 'providerMessageId', 'providerEventId', 'evidenceHash', 'rawEvent')) {
    Assert-False ([bool]($json -match [regex]::Escape($forbidden))) "$Label forbidden $forbidden"
  }
}

function Invoke-Controller {
  param(
    [Parameter(Mandatory = $true)]$Fixture,
    [Parameter(Mandatory = $true)][string]$Action,
    [Parameter(Mandatory = $true)]$Request
  )

  $requestPath = Join-Path $Fixture.PrivateRoot ("request-$([guid]::NewGuid().ToString('N')).json")
  Write-TestUtf8 -Path $requestPath -Value (($Request | ConvertTo-Json -Depth 100) + "`n")
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $engine
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $controllerPath,
      '-Action', $Action,
      '-RepositoryRoot', $Fixture.RepositoryRoot,
      '-StatePath', $Fixture.StatePath,
      '-RequestPath', $requestPath
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  $startInfo.Environment['USERPROFILE'] = $Fixture.UserProfile
  $startInfo.Environment['FAKE_BRIDGE_MODE'] = [string]$env:FAKE_BRIDGE_MODE
  $startInfo.Environment['TZG_FAKE_CHECK_LOG'] = [string]$env:TZG_FAKE_CHECK_LOG
  $startInfo.Environment['TZG_FAKE_FAIL_CHECK'] = [string]$env:TZG_FAKE_FAIL_CHECK
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  try {
    if (-not $process.Start()) { throw 'controller test process did not start' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
    $stderr = $stderrTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
    $lines = @($stdout -split '\r?\n' | Where-Object { $_.Length -gt 0 })
    if ($lines.Count -ne 1) {
      throw "controller stdout was not exactly one line (exit $($process.ExitCode)): <$stdout>; stderr: <$stderr>"
    }
    try {
      $response = $lines[0] | ConvertFrom-Json
    } catch {
      throw "controller stdout was not JSON (exit $($process.ExitCode)): <$stdout>; stderr: <$stderr>"
    }
    Assert-StableResponse -Response $response -Action $Action -Label $Action
    [pscustomobject]@{
      Code = $process.ExitCode
      Response = $response
      Stdout = $stdout
      Stderr = $stderr
    }
  } finally {
    $process.Dispose()
  }
}

function New-ControllerFixture {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [switch]$PreexistingManualChanges
  )

  $root = Join-Path $sandbox $Name
  $repositoryRoot = Join-Path $root 'repo'
  $userProfile = Join-Path $root 'user'
  $privateRoot = Join-Path $userProfile '.codex\automation-state'
  $statePath = Join-Path $privateRoot 'controller-v2.json'
  [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
  [IO.Directory]::CreateDirectory($privateRoot) | Out-Null

  foreach ($relative in @(
      '开发管理/自动工作流任务注册表.json',
      '开发管理/当前任务队列.txt',
      '开发管理/自动工作流状态.txt',
      '开发管理/开发-技术经验.txt',
      'docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md',
      'tools/automation-workspace-guard.ps1',
      'tools/automation-finalize-commit.ps1',
      'tools/check-pending-whitespace.ps1'
    )) {
    Copy-TestFile `
      -Source (Join-Path $projectRoot ($relative.Replace('/', '\'))) `
      -Destination (Join-Path $repositoryRoot ($relative.Replace('/', '\')))
  }

  $fixtureFiles = [ordered]@{
    'src/Assets/DataConfig/Spells.csv' = "spell data`n"
    'src/Assets/DataConfig/Language.csv' = "language data`n"
    'src/Assets/DataConfig/GongFa.csv' = "gongfa data`n"
    'src/Assets/Scripts/Editor/DataConfigImporter.cs' = "importer`n"
    'src/Assets/Scripts/Combat/SpellData.cs' = "spell runtime`n"
    'src/Assets/Scripts/Combat/CombatResolver.cs' = "resolver`n"
    'src/Assets/Tests/EditMode/SpellDamageMultiplierTests.cs' = "tests`n"
    'src/Assets/Data/Spells/spell-a.asset' = "asset a`n"
    'src/Assets/Data/Spells/spell-b.asset' = "asset b`n"
    'src/Assets/Data/Spells/spell-c.asset' = "asset c`n"
    'docs/角色养成/术法/古修术法一.txt' = "spell doc`n"
    'docs/角色养成/功法/示例功法.txt' = "gongfa doc`n"
    'src/Assets/Data/GongFa/示例功法.asset' = "gongfa asset`n"
    'human.txt' = "human base`n"
    'pre-staged.txt' = "staged base`n"
  }
  foreach ($entry in $fixtureFiles.GetEnumerator()) {
    Write-TestUtf8 -Path (Join-Path $repositoryRoot ($entry.Key.Replace('/', '\'))) -Value $entry.Value
  }

  $fakeCheck = @'
#requires -Version 7.0
$ErrorActionPreference = 'Stop'
$checkId = if ($MyInvocation.MyCommand.Name -eq 'check-data-chain.ps1') { 'data-chain' } else { 'unity-editmode-related' }
[IO.File]::AppendAllText($env:TZG_FAKE_CHECK_LOG, $checkId + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
if ($env:TZG_FAKE_FAIL_CHECK -ceq $checkId) {
  [Console]::Error.WriteLine('appSecret=private-value messageId=private-message check failed')
  exit 9
}
Write-Output "$checkId`: OK"
'@
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'tools\check-data-chain.ps1') -Value $fakeCheck
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'tools\run-unity-editmode-tests.ps1') -Value $fakeCheck

  $sendBridge = @'
import { readFileSync } from 'node:fs';
const request = JSON.parse(readFileSync(process.argv[3], 'utf8'));
if (process.env.FAKE_BRIDGE_MODE === 'unavailable') {
  process.stdout.write('{"result":"CHANNEL_UNAVAILABLE"}\n');
  process.exitCode = 20;
} else {
  process.stdout.write(JSON.stringify({
    result: 'PROVIDER_ACCEPTED', targetHash: 'a'.repeat(64),
    providerMessageIdHash: 'b'.repeat(64), providerChatIdHash: 'c'.repeat(64),
    cardNonceHash: 'd'.repeat(64), intentKeyHash: 'e'.repeat(64),
  }) + '\n');
}
'@
  $consumeBridge = @'
import { readFileSync } from 'node:fs';
const request = JSON.parse(readFileSync(process.argv[3], 'utf8'));
const hashes = {
  providerMessageIdHash: 'b'.repeat(64), providerEventIdHash: 'f'.repeat(64),
  operatorOpenIdHash: '1'.repeat(64), tenantKeyHash: '2'.repeat(64),
  cardNonceHash: 'd'.repeat(64), evidenceHash: '3'.repeat(64),
};
if (process.env.FAKE_BRIDGE_MODE === 'option') {
  process.stdout.write(JSON.stringify({ result: 'OPTION_ACCEPTED', optionKey: 'A', source: 'feishu_card', ...hashes }) + '\n');
} else {
  process.stdout.write('{"result":"NO_REPLY"}\n');
}
'@
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'tools\feishu-decision-bridge\src\send-decision.mjs') -Value $sendBridge
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'tools\feishu-decision-bridge\src\consume-reply.mjs') -Value $consumeBridge

  Invoke-TestGit -Repository $repositoryRoot init | Out-Null
  Invoke-TestGit -Repository $repositoryRoot config user.email 'fixture@example.invalid' | Out-Null
  Invoke-TestGit -Repository $repositoryRoot config user.name 'Fixture' | Out-Null
  Invoke-TestGit -Repository $repositoryRoot config core.autocrlf false | Out-Null
  Invoke-TestGit -Repository $repositoryRoot add -- . | Out-Null
  Invoke-TestGit -Repository $repositoryRoot commit -m 'fixture baseline' | Out-Null
  if ($PreexistingManualChanges) {
    Write-TestUtf8 -Path (Join-Path $repositoryRoot 'human.txt') -Value "human working change`n"
    Write-TestUtf8 -Path (Join-Path $repositoryRoot 'pre-staged.txt') -Value "human staged change`n"
    Invoke-TestGit -Repository $repositoryRoot add -- 'pre-staged.txt' | Out-Null
  }

  $stateFixture = Join-Path $PSScriptRoot 'fixtures\migrated-v1-tq057.expected.json'
  Copy-TestFile -Source $stateFixture -Destination $statePath
  . (Join-Path $projectRoot 'tools\private-path-acl.ps1')
  Set-PrivatePathAcl -Path $privateRoot -Directory
  Set-PrivatePathAcl -Path $statePath

  [pscustomobject]@{
    Root = $root
    RepositoryRoot = $repositoryRoot
    UserProfile = $userProfile
    PrivateRoot = $privateRoot
    StatePath = $statePath
    CheckLog = Join-Path $privateRoot 'check-log.txt'
  }
}

function Invoke-ReadOnlyPrelude {
  param([Parameter(Mandatory = $true)]$Fixture)

  $threadId = [guid]::NewGuid().ToString()
  $start = Invoke-Controller -Fixture $Fixture -Action 'Start' -Request ([ordered]@{
      schemaVersion = 1
      action = 'Start'
      model = 'fixture-model'
      threadId = $threadId
      metadataThreadId = $threadId
    })
  if ($start.Code -ne 0) {
    throw "Start failed: $($start.Stdout); stderr: $($start.Stderr)"
  }
  Assert-Equal $start.Code 0 'Start exit'
  Assert-True ([bool]$start.Response.ok) 'Start ok'
  Assert-Equal $start.Response.phase 'DISCOVERING' 'Start phase'
  Assert-Equal $start.Response.nextAction 'RecordTitleResult' 'Start next action'
  Assert-Equal $start.Response.result.titleRequest.threadId $threadId 'Start title thread'
  $runId = [string]$start.Response.runId

  $title = Invoke-Controller -Fixture $Fixture -Action 'RecordTitleResult' -Request ([ordered]@{
      schemaVersion = 1
      action = 'RecordTitleResult'
      runId = $runId
      succeeded = $false
      diagnostic = 'openId=private-user messageId=private-message title timeout'
    })
  Assert-Equal $title.Code 0 'RecordTitleResult exit'
  Assert-Equal $title.Response.result.titleStatus 'FAILED' 'title failure is nonblocking'
  Assert-Equal $title.Response.result.titleDiagnostic '[REDACTED]' 'title diagnostic redacted'
  Assert-Equal $title.Response.nextAction 'DiscoverRead' 'title next action'

  $requiredSources = @($start.Response.requiredSources)
  foreach ($source in $requiredSources) {
    $read = Invoke-Controller -Fixture $Fixture -Action 'DiscoverRead' -Request ([ordered]@{
        schemaVersion = 1
        action = 'DiscoverRead'
        runId = $runId
        path = $source
      })
    Assert-Equal $read.Code 0 "DiscoverRead $source exit"
    Assert-True ([bool]$read.Response.ok) "DiscoverRead $source ok"
  }
  $list = Invoke-Controller -Fixture $Fixture -Action 'DiscoverList' -Request ([ordered]@{
      schemaVersion = 1
      action = 'DiscoverList'
      runId = $runId
      root = 'src/Assets/Data/Spells'
      glob = '*.asset'
    })
  Assert-Equal $list.Code 0 'DiscoverList exit'
  Assert-Equal @($list.Response.result.items).Count 3 'DiscoverList spell inventory'
  $check = Invoke-Controller -Fixture $Fixture -Action 'DiscoverCheck' -Request ([ordered]@{
      schemaVersion = 1
      action = 'DiscoverCheck'
      runId = $runId
      checkId = 'data-chain-readonly'
    })
  Assert-Equal $check.Code 0 'DiscoverCheck exit'

  [pscustomobject]@{
    RunId = $runId
    ThreadId = $threadId
    RequiredSources = $requiredSources
  }
}

function Write-TestManifest {
  param(
    [Parameter(Mandatory = $true)]$Fixture,
    [Parameter(Mandatory = $true)]$Prelude
  )

  $manifest = Read-TestJson -Path (Join-Path $PSScriptRoot 'fixtures\tq057-valid-manifest.json')
  $manifest.runId = $Prelude.RunId
  $manifest.model = 'fixture-model'
  $manifest.threadId = $Prelude.ThreadId
  foreach ($source in @($manifest.sourceEvidence)) {
    $source.sha256 = Get-TestSha256 -Path (Join-Path $Fixture.RepositoryRoot ([string]$source.path).Replace('/', '\'))
  }
  $manifestPath = Join-Path $Fixture.PrivateRoot ("manifest-$($Prelude.RunId).json")
  Write-TestUtf8 -Path $manifestPath -Value (($manifest | ConvertTo-Json -Depth 100) + "`n")
  $manifestPath
}

function Submit-TestManifest {
  param(
    [Parameter(Mandatory = $true)]$Fixture,
    [Parameter(Mandatory = $true)]$Prelude,
    [Parameter(Mandatory = $true)][string]$ManifestPath
  )

  Invoke-Controller -Fixture $Fixture -Action 'SubmitManifest' -Request ([ordered]@{
      schemaVersion = 1
      action = 'SubmitManifest'
      runId = $Prelude.RunId
      manifestPath = $ManifestPath
    })
}

function Approve-TestManifest {
  param(
    [Parameter(Mandatory = $true)]$Fixture,
    [Parameter(Mandatory = $true)]$Prelude
  )

  $env:FAKE_BRIDGE_MODE = 'accepted'
  $send = Invoke-Controller -Fixture $Fixture -Action 'SendDecision' -Request ([ordered]@{
      schemaVersion = 1
      action = 'SendDecision'
      runId = $Prelude.RunId
    })
  Assert-Equal $send.Code 0 'SendDecision exit'
  Assert-Equal $send.Response.nextAction 'ConsumeDecision' 'SendDecision next action'
  $env:FAKE_BRIDGE_MODE = 'option'
  $consume = Invoke-Controller -Fixture $Fixture -Action 'ConsumeDecision' -Request ([ordered]@{
      schemaVersion = 1
      action = 'ConsumeDecision'
      runId = $Prelude.RunId
    })
  Assert-Equal $consume.Code 0 'ConsumeDecision exit'
  Assert-Equal $consume.Response.phase 'AUTHORIZED' 'manifest approval authorizes'
  Assert-Equal $consume.Response.nextAction 'BeginMutation' 'approval next action'
}

try {
  [IO.Directory]::CreateDirectory($sandbox) | Out-Null

  $successFixture = New-ControllerFixture -Name 'success' -PreexistingManualChanges
  $env:USERPROFILE = $successFixture.UserProfile
  $env:TZG_FAKE_CHECK_LOG = $successFixture.CheckLog
  $env:TZG_FAKE_FAIL_CHECK = ''
  $env:FAKE_BRIDGE_MODE = 'accepted'
  $successPrelude = Invoke-ReadOnlyPrelude -Fixture $successFixture
  $successManifestPath = Write-TestManifest -Fixture $successFixture -Prelude $successPrelude
  $submitted = Submit-TestManifest -Fixture $successFixture -Prelude $successPrelude -ManifestPath $successManifestPath
  Assert-Equal $submitted.Code 0 'SubmitManifest exit'
  Assert-Equal $submitted.Response.phase 'IMPLEMENTATION_PENDING' 'plan-only manifest phase'
  Assert-Equal $submitted.Response.nextAction 'SendDecision' 'plan-only manifest next action'

  $premature = Invoke-Controller -Fixture $successFixture -Action 'BeginMutation' -Request ([ordered]@{
      schemaVersion = 1
      action = 'BeginMutation'
      runId = $successPrelude.RunId
    })
  Assert-True ($premature.Code -ne 0) 'unapproved BeginMutation exit'
  Assert-Equal $premature.Response.errorCode 'invalid_state' 'unapproved BeginMutation code'
  Assert-Equal $premature.Response.phase 'IMPLEMENTATION_PENDING' 'unapproved manifest remains pending'

  Approve-TestManifest -Fixture $successFixture -Prelude $successPrelude
  $begin = Invoke-Controller -Fixture $successFixture -Action 'BeginMutation' -Request ([ordered]@{
      schemaVersion = 1
      action = 'BeginMutation'
      runId = $successPrelude.RunId
    })
  Assert-Equal $begin.Code 0 'BeginMutation exit'
  Assert-Equal $begin.Response.phase 'MUTATING' 'BeginMutation phase'
  Write-TestUtf8 -Path (Join-Path $successFixture.RepositoryRoot 'src\Assets\DataConfig\Spells.csv') -Value "spell data changed by controller`n"
  $finish = Invoke-Controller -Fixture $successFixture -Action 'Finish' -Request ([ordered]@{
      schemaVersion = 1
      action = 'Finish'
      runId = $successPrelude.RunId
      commitMessage = 'test: guarded controller commit'
    })
  if ($finish.Code -ne 0) {
    throw "Finish failed: $($finish.Stdout); stderr: $($finish.Stderr)"
  }
  Assert-Equal $finish.Code 0 'Finish exit'
  Assert-Equal $finish.Response.phase 'IDLE' 'Finish returns idle'
  Assert-True ([bool]([string]$finish.Response.result.commitSha -match '^[0-9a-f]{40,64}$')) 'Finish commit sha'
  $committedPaths = @(Invoke-TestGit -Repository $successFixture.RepositoryRoot -Arguments @('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD'))
  Assert-Equal ($committedPaths -join '|') 'src/Assets/DataConfig/Spells.csv' 'path-limited commit'
  Assert-Equal ((Invoke-TestGit -Repository $successFixture.RepositoryRoot -Arguments @('diff', '--cached', '--name-only')) -join '|') 'pre-staged.txt' 'pre-staged file preserved'
  Assert-Equal ((Invoke-TestGit -Repository $successFixture.RepositoryRoot diff --name-only) -join '|') 'human.txt' 'outside manual dirty file preserved'
  $successChecks = @([IO.File]::ReadAllLines($successFixture.CheckLog))
  Assert-Equal (@($successChecks | Where-Object { $_ -ceq 'data-chain' }).Count) 2 'discovery and final data-chain each run once'
  Assert-Equal (@($successChecks | Where-Object { $_ -ceq 'unity-editmode-related' }).Count) 1 'final unity check once'

  $baselineFixture = New-ControllerFixture -Name 'baseline'
  $env:USERPROFILE = $baselineFixture.UserProfile
  $env:TZG_FAKE_CHECK_LOG = $baselineFixture.CheckLog
  $env:TZG_FAKE_FAIL_CHECK = ''
  $baselinePrelude = Invoke-ReadOnlyPrelude -Fixture $baselineFixture
  $baselineManifestPath = Write-TestManifest -Fixture $baselineFixture -Prelude $baselinePrelude
  Write-TestUtf8 -Path (Join-Path $baselineFixture.RepositoryRoot 'intruder.txt') -Value "manual intrusion`n"
  $baselineRejected = Submit-TestManifest -Fixture $baselineFixture -Prelude $baselinePrelude -ManifestPath $baselineManifestPath
  Assert-True ($baselineRejected.Code -ne 0) 'baseline conflict exit'
  Assert-Equal $baselineRejected.Response.errorCode 'baseline_changed' 'baseline conflict code'
  Assert-True ([bool](@($baselineRejected.Response.changedPaths) -contains 'intruder.txt')) 'baseline exact changed path'
  Assert-Equal $baselineRejected.Response.phase 'IDLE' 'baseline conflict returns idle'
  Assert-Equal $baselineRejected.Response.result.interruptionClassification 'unsafe' 'baseline conflict interruption classification'
  Assert-True (Test-Path -LiteralPath (Join-Path $baselineFixture.RepositoryRoot 'intruder.txt')) 'baseline conflict preserved manual file'

  $headFixture = New-ControllerFixture -Name 'head'
  $env:USERPROFILE = $headFixture.UserProfile
  $env:TZG_FAKE_CHECK_LOG = $headFixture.CheckLog
  $headPrelude = Invoke-ReadOnlyPrelude -Fixture $headFixture
  $headManifestPath = Write-TestManifest -Fixture $headFixture -Prelude $headPrelude
  Invoke-TestGit -Repository $headFixture.RepositoryRoot -Arguments @('commit', '--allow-empty', '-m', 'manual head advance') | Out-Null
  $headRejected = Submit-TestManifest -Fixture $headFixture -Prelude $headPrelude -ManifestPath $headManifestPath
  Assert-True ($headRejected.Code -ne 0) 'head conflict exit'
  Assert-Equal $headRejected.Response.errorCode 'head_changed' 'head conflict code'
  Assert-True ([bool](@($headRejected.Response.changedPaths) -contains '<HEAD>')) 'head exact sentinel'
  Assert-Equal $headRejected.Response.phase 'IDLE' 'head conflict returns idle'

  $failureFixture = New-ControllerFixture -Name 'check-failure' -PreexistingManualChanges
  $env:USERPROFILE = $failureFixture.UserProfile
  $env:TZG_FAKE_CHECK_LOG = $failureFixture.CheckLog
  $env:TZG_FAKE_FAIL_CHECK = ''
  $failurePrelude = Invoke-ReadOnlyPrelude -Fixture $failureFixture
  $failureManifestPath = Write-TestManifest -Fixture $failureFixture -Prelude $failurePrelude
  $failureSubmitted = Submit-TestManifest -Fixture $failureFixture -Prelude $failurePrelude -ManifestPath $failureManifestPath
  Assert-Equal $failureSubmitted.Code 0 'failure SubmitManifest exit'
  Approve-TestManifest -Fixture $failureFixture -Prelude $failurePrelude
  $failureBegin = Invoke-Controller -Fixture $failureFixture -Action 'BeginMutation' -Request ([ordered]@{
      schemaVersion = 1; action = 'BeginMutation'; runId = $failurePrelude.RunId
    })
  Assert-Equal $failureBegin.Code 0 'failure BeginMutation exit'
  Write-TestUtf8 -Path (Join-Path $failureFixture.RepositoryRoot 'src\Assets\DataConfig\Spells.csv') -Value "uncommitted failed check change`n"
  $headBeforeFailure = (Invoke-TestGit -Repository $failureFixture.RepositoryRoot rev-parse HEAD)[0]
  $env:TZG_FAKE_FAIL_CHECK = 'data-chain'
  $failedFinish = Invoke-Controller -Fixture $failureFixture -Action 'Finish' -Request ([ordered]@{
      schemaVersion = 1; action = 'Finish'; runId = $failurePrelude.RunId; commitMessage = 'test: must not commit'
    })
  Assert-True ($failedFinish.Code -ne 0) 'failed check exit'
  Assert-Equal $failedFinish.Response.errorCode 'check_failed' 'failed check code'
  Assert-Equal $failedFinish.Response.phase 'IDLE' 'failed check returns idle'
  Assert-Equal $failedFinish.Response.result.interruptionClassification 'recoverable' 'failed check recoverable classification'
  Assert-Equal (Invoke-TestGit -Repository $failureFixture.RepositoryRoot rev-parse HEAD)[0] $headBeforeFailure 'failed check did not commit'
  Assert-True (Test-Path -LiteralPath (Join-Path $failureFixture.RepositoryRoot 'human.txt')) 'failed check preserved manual file'
  Assert-True ([bool]([string]$failedFinish.Response.result.diagnostic -eq '[REDACTED]')) 'failed check diagnostic redacted'

  $feishuFixture = New-ControllerFixture -Name 'feishu-unavailable'
  $env:USERPROFILE = $feishuFixture.UserProfile
  $env:TZG_FAKE_CHECK_LOG = $feishuFixture.CheckLog
  $env:TZG_FAKE_FAIL_CHECK = ''
  $feishuStart = Invoke-Controller -Fixture $feishuFixture -Action 'Start' -Request ([ordered]@{
      schemaVersion = 1; action = 'Start'; model = 'fixture-model'
      threadId = '11111111-1111-1111-1111-111111111111'
      metadataThreadId = '11111111-1111-1111-1111-111111111111'
    })
  $feishuTitle = Invoke-Controller -Fixture $feishuFixture -Action 'RecordTitleResult' -Request ([ordered]@{
      schemaVersion = 1; action = 'RecordTitleResult'; runId = $feishuStart.Response.runId
      succeeded = $true; diagnostic = 'title updated'
    })
  Assert-Equal $feishuTitle.Code 0 'feishu title exit'
  $createDecision = Invoke-Controller -Fixture $feishuFixture -Action 'CreateDecision' -Request ([ordered]@{
      schemaVersion = 1
      action = 'CreateDecision'
      runId = $feishuStart.Response.runId
      question = '请选择安全范围。'
      options = @(
        [ordered]@{ key = 'A'; text = '采用窄范围。'; recommended = $true; scopeContract = [ordered]@{ expectedPaths = @('src/a.txt'); requiredChecks = @('data-chain') } },
        [ordered]@{ key = 'B'; text = '返回发现。'; recommended = $false; scopeContract = [ordered]@{ expectedPaths = @(); requiredChecks = @() } },
        [ordered]@{ key = 'C'; text = '停止任务。'; recommended = $false; scopeContract = [ordered]@{ expectedPaths = @(); requiredChecks = @() } }
      )
    })
  Assert-Equal $createDecision.Code 0 'CreateDecision exit'
  Assert-Equal $createDecision.Response.phase 'WAITING_DECISION' 'CreateDecision phase'
  $env:FAKE_BRIDGE_MODE = 'unavailable'
  $sendUnavailable = Invoke-Controller -Fixture $feishuFixture -Action 'SendDecision' -Request ([ordered]@{
      schemaVersion = 1; action = 'SendDecision'; runId = $feishuStart.Response.runId
    })
  Assert-True ($sendUnavailable.Code -ne 0) 'unavailable SendDecision exit'
  Assert-Equal $sendUnavailable.Response.errorCode 'feishu_unavailable' 'unavailable SendDecision code'
  Assert-Equal $sendUnavailable.Response.phase 'WAITING_DECISION' 'unavailable decision preserved'
  $blockedMutation = Invoke-Controller -Fixture $feishuFixture -Action 'BeginMutation' -Request ([ordered]@{
      schemaVersion = 1; action = 'BeginMutation'; runId = $feishuStart.Response.runId
    })
  Assert-True ($blockedMutation.Code -ne 0) 'unavailable BeginMutation exit'
  Assert-Equal $blockedMutation.Response.errorCode 'invalid_state' 'unavailable mutation blocked'
  Assert-Equal $blockedMutation.Response.phase 'WAITING_DECISION' 'unavailable decision remains waiting'

  Write-Output 'controller.tests: OK'
} finally {
  $env:USERPROFILE = $originalUserProfile
  $env:FAKE_BRIDGE_MODE = $originalBridgeMode
  $env:TZG_FAKE_CHECK_LOG = $originalCheckLog
  $env:TZG_FAKE_FAIL_CHECK = $originalFailCheck
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    Remove-Item -LiteralPath $resolvedSandbox -Recurse -Force
  }
}
