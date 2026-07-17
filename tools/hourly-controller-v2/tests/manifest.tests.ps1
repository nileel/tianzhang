#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'manifest.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'manifest.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking
Import-Module (Join-Path $v2Root 'registry.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $v2Root 'discovery.psm1') -Force -DisableNameChecking

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

function Copy-ManifestObject {
  param([Parameter(Mandatory = $true)]$Value)

  $json = $Value | ConvertTo-Json -Depth 100
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $json | ConvertFrom-Json -AsHashtable -DateKind String
  } else {
    $json | ConvertFrom-Json -AsHashtable
  }
}

function Write-ManifestObject {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string]$Path
  )

  Write-TestUtf8 -Path $Path -Value (($Value | ConvertTo-Json -Depth 100) + "`n")
  Read-WorkManifest -Path $Path
}

function Assert-ManifestRejected {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)]$Ledger,
    [Parameter(Mandatory = $true)][string]$DiscoveryLogPath,
    [Parameter(Mandatory = $true)][string]$BaselinePath
  )

  Assert-Throws `
    -Script { Test-WorkManifest -Manifest $Manifest -TaskContract $TaskContract -DecisionLedger $Ledger -DiscoveryLogPath $DiscoveryLogPath -BaselinePath $BaselinePath } `
    -MessageLike $Code `
    -Label $Label
}

$fixtureRoot = Join-Path $PSScriptRoot 'fixtures'
$validFixturePath = Join-Path $fixtureRoot 'tq057-valid-manifest.json'
$incompleteFixturePath = Join-Path $fixtureRoot 'tq057-incomplete-manifest.json'
$projectRoot = Split-Path -Parent (Split-Path -Parent $v2Root)
$registryPath = Join-Path $projectRoot '开发管理\自动工作流任务注册表.json'
$stateFixturePath = Join-Path $fixtureRoot 'migrated-v1-tq057.expected.json'
$guardPath = Join-Path $projectRoot 'tools\automation-workspace-guard.ps1'
$engine = Join-Path $PSHOME 'pwsh.exe'

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-manifest-' + [guid]::NewGuid().ToString('N'))
$repositoryRoot = Join-Path $sandbox 'repo'
$runRoot = Join-Path $sandbox 'private-run'
$baselinePath = Join-Path $sandbox 'baseline.json'
$discoveryLogPath = Join-Path $runRoot 'discovery-log.jsonl'
$junctionPath = $null

try {
  [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
  [IO.Directory]::CreateDirectory($runRoot) | Out-Null
  $fixtureFiles = [ordered]@{
    '开发管理/当前任务队列.txt' = "fixture queue`n"
    '开发管理/自动工作流状态.txt' = "fixture status`n"
    '开发管理/开发-技术经验.txt' = "fixture tech`n"
    'docs/superpowers/specs/2026-07-17-hourly-controller-orchestration-rebuild-design.md' = "fixture design`n"
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
    'tools/check-data-chain.ps1' = "#requires -Version 7.0`nWrite-Output 'fixture data-chain: ISSUES_FOUND'`nexit 7`n"
  }
  foreach ($entry in $fixtureFiles.GetEnumerator()) {
    Write-TestUtf8 -Path (Join-Path $repositoryRoot ($entry.Key.Replace('/', '\'))) -Value $entry.Value
  }

  Invoke-TestGit -Repository $repositoryRoot init | Out-Null
  Invoke-TestGit -Repository $repositoryRoot config user.email 'fixture@example.invalid' | Out-Null
  Invoke-TestGit -Repository $repositoryRoot config user.name 'Fixture' | Out-Null
  Invoke-TestGit -Repository $repositoryRoot add -- . | Out-Null
  Invoke-TestGit -Repository $repositoryRoot commit -m 'fixture baseline' | Out-Null

  $snapshotOutput = @(& $engine -NoProfile -ExecutionPolicy Bypass -File $guardPath Snapshot -RepositoryRoot $repositoryRoot -BaselinePath $baselinePath 2>&1)
  if ($LASTEXITCODE -ne 0) {
    throw "workspace guard fixture snapshot failed: $($snapshotOutput -join "`n")"
  }

  $registry = Read-TaskRegistry -Path $registryPath
  $taskContract = Get-TaskContract -Registry $registry -TaskId 'TQ-057'
  $stateText = [IO.File]::ReadAllText($stateFixturePath)
  $stateFixture = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $stateText | ConvertFrom-Json -DateKind String
  } else {
    $stateText | ConvertFrom-Json
  }
  $ledger = @($stateFixture.decisionLedger)
  $context = [pscustomobject]@{
    repositoryRoot = $repositoryRoot
    runRoot = $runRoot
    requiredSources = @($taskContract.requiredSources)
    allowedRoots = @($taskContract.allowedRoots)
    discoveryChecks = @($taskContract.discoveryChecks)
  }
  foreach ($source in @($taskContract.requiredSources)) {
    Invoke-DiscoverRead -Context $context -Path $source | Out-Null
  }
  Invoke-DiscoverList -Context $context -Root 'src/Assets/Data/Spells' -Glob '*.asset' | Out-Null
  $diagnosticCheck = Invoke-DiscoverCheck -Context $context -CheckId 'data-chain-readonly'
  Assert-Equal $diagnosticCheck.exitCode 7 'diagnostic discovery check exit code'

  $validManifestPath = Join-Path $runRoot 'valid-manifest.json'
  [IO.File]::Copy($validFixturePath, $validManifestPath, $false)
  $validManifest = Read-WorkManifest -Path $validManifestPath
  $validResult = Test-WorkManifest -Manifest $validManifest -TaskContract $taskContract -DecisionLedger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath
  Assert-True ([bool]$validResult.ok) 'valid manifest result'
  Assert-Equal @($validResult.expectedPaths).Count 14 'valid manifest expected path count'

  $missingDiscoveryCheckLogPath = Join-Path $runRoot 'discovery-missing-check.jsonl'
  $withoutDiscoveryCheck = @([IO.File]::ReadAllLines($discoveryLogPath) | Where-Object {
      [string](($_ | ConvertFrom-Json).action) -cne 'DiscoverCheck'
    })
  Write-TestUtf8 -Path $missingDiscoveryCheckLogPath -Value (($withoutDiscoveryCheck -join "`n") + "`n")
  Assert-ManifestRejected -Manifest $validManifest -Code 'discovery_incomplete' -Label 'missing registered discovery check evidence' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $missingDiscoveryCheckLogPath -BaselinePath $baselinePath

  $incompleteManifest = Read-WorkManifest -Path $incompleteFixturePath
  Assert-ManifestRejected -Manifest $incompleteManifest -Code 'discovery_incomplete' -Label 'incomplete fixture' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $missingSource = Copy-ManifestObject $validManifest
  $missingSource.sourceEvidence = @($missingSource.sourceEvidence | Where-Object { $_.path -cne '开发管理/开发-技术经验.txt' })
  Assert-ManifestRejected -Manifest $missingSource -Code 'discovery_incomplete' -Label 'missing required source' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $wrongSourceHash = Copy-ManifestObject $validManifest
  $wrongSourceHash.sourceEvidence[0].sha256 = '0' * 64
  Assert-ManifestRejected -Manifest $wrongSourceHash -Code 'source_changed' -Label 'source hash mismatch' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $missingDecision = Copy-ManifestObject $validManifest
  $missingDecision.decisionCoverage = @($missingDecision.decisionCoverage | Where-Object { $_.decisionId -cne 'DEC-20260714-29A5D1356CC8' })
  Assert-ManifestRejected -Manifest $missingDecision -Code 'decision_coverage_incomplete' -Label 'missing decision' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $letterOnly = Copy-ManifestObject $validManifest
  $letterOnly.decisionCoverage[0].resolutionText = 'B'
  Assert-ManifestRejected -Manifest $letterOnly -Code 'decision_coverage_incomplete' -Label 'letter-only resolution' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  foreach ($requiredPath in @(
      'src/Assets/DataConfig/Spells.csv',
      'src/Assets/Scripts/Editor/DataConfigImporter.cs',
      'src/Assets/Scripts/Combat/SpellData.cs',
      'src/Assets/Scripts/Combat/CombatResolver.cs',
      'src/Assets/Tests/EditMode/SpellDamageMultiplierTests.cs',
      'tools/check-data-chain.ps1',
      'src/Assets/Data/Spells/spell-c.asset'
    )) {
    $missingPath = Copy-ManifestObject $validManifest
    $missingPath.expectedPaths = @($missingPath.expectedPaths | Where-Object { $_ -cne $requiredPath })
    $missingPath.intendedChanges = @($missingPath.intendedChanges | Where-Object { $_.path -cne $requiredPath })
    foreach ($coverage in @($missingPath.decisionCoverage)) {
      $coverage.paths = @($coverage.paths | Where-Object { $_ -cne $requiredPath })
    }
    Assert-ManifestRejected -Manifest $missingPath -Code 'decision_coverage_incomplete' -Label "missing coverage path $requiredPath" -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath
  }

  $outsideScope = Copy-ManifestObject $validManifest
  $outsideScope.expectedPaths += 'simulations/outside.txt'
  $outsideScope.intendedChanges += [ordered]@{ path = 'simulations/outside.txt'; operation = 'create'; summary = 'outside' }
  Assert-ManifestRejected -Manifest $outsideScope -Code 'path_outside_scope' -Label 'outside allowed roots' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $missingIntent = Copy-ManifestObject $validManifest
  $missingIntent.intendedChanges = @($missingIntent.intendedChanges | Where-Object { $_.path -cne 'src/Assets/Scripts/Combat/SpellData.cs' })
  Assert-ManifestRejected -Manifest $missingIntent -Code 'manifest_invalid' -Label 'missing intended change' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $coverageOutsideExpected = Copy-ManifestObject $validManifest
  $coverageOutsideExpected.expectedPaths = @($coverageOutsideExpected.expectedPaths | Where-Object { $_ -cne 'src/Assets/DataConfig/Language.csv' })
  $coverageOutsideExpected.intendedChanges = @($coverageOutsideExpected.intendedChanges | Where-Object { $_.path -cne 'src/Assets/DataConfig/Language.csv' })
  Assert-ManifestRejected -Manifest $coverageOutsideExpected -Code 'manifest_invalid' -Label 'decision path outside expected paths' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $missingCheck = Copy-ManifestObject $validManifest
  $missingCheck.requiredChecks = @($missingCheck.requiredChecks | Where-Object { $_ -cne 'data-chain' })
  Assert-ManifestRejected -Manifest $missingCheck -Code 'manifest_invalid' -Label 'missing registered required check' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  $unknownCheck = Copy-ManifestObject $validManifest
  $unknownCheck.requiredChecks += 'arbitrary-command'
  Assert-ManifestRejected -Manifest $unknownCheck -Code 'manifest_invalid' -Label 'unregistered check' -TaskContract $taskContract -Ledger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath

  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'src\Assets\DataConfig\Spells.csv') -Value "changed outside baseline`n"
  $baselineError = $null
  try {
    Test-WorkManifest -Manifest $validManifest -TaskContract $taskContract -DecisionLedger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath | Out-Null
  } catch {
    $baselineError = $_.Exception
  }
  Assert-True ($null -ne $baselineError) 'baseline change rejection'
  Assert-True ([bool]$baselineError.Message.Contains('baseline_changed')) 'baseline change error code'
  Assert-True ([bool](@($baselineError.Data['changedPaths']) -contains 'src/Assets/DataConfig/Spells.csv')) 'baseline changed path detail'
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'src\Assets\DataConfig\Spells.csv') -Value "spell data`n"

  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'src\Assets\DataConfig\head-change.txt') -Value "head change`n"
  Invoke-TestGit -Repository $repositoryRoot add -- 'src/Assets/DataConfig/head-change.txt' | Out-Null
  Invoke-TestGit -Repository $repositoryRoot commit -m 'head change' | Out-Null
  $headError = $null
  try {
    Test-WorkManifest -Manifest $validManifest -TaskContract $taskContract -DecisionLedger $ledger -DiscoveryLogPath $discoveryLogPath -BaselinePath $baselinePath | Out-Null
  } catch {
    $headError = $_.Exception
  }
  Assert-True ($null -ne $headError) 'HEAD change rejection'
  Assert-True ([bool]$headError.Message.Contains('head_changed')) 'HEAD change error code'
  Assert-True ([bool](@($headError.Data['changedPaths']) -contains '<HEAD>')) 'HEAD changed path sentinel'

  Write-Output 'manifest.tests: OK'
} finally {
  if ($null -ne $junctionPath -and (Test-Path -LiteralPath $junctionPath)) {
    Remove-Item -LiteralPath $junctionPath -Force
  }
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    Remove-Item -LiteralPath $resolvedSandbox -Recurse -Force
  }
}
