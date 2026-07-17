#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'registry.psm1'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $v2Root)
$registryPath = Join-Path $repositoryRoot '开发管理\自动工作流任务注册表.json'
$queuePath = Join-Path $repositoryRoot '开发管理\当前任务队列.txt'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'registry.psm1 is missing'
}
if (-not (Test-Path -LiteralPath $registryPath -PathType Leaf)) {
  throw 'task registry is missing'
}
Import-Module $modulePath -Force -DisableNameChecking

function Copy-TestObject {
  param([Parameter(Mandatory = $true)]$Value)

  ($Value | ConvertTo-Json -Depth 100) | ConvertFrom-Json -AsHashtable
}

function Write-RegistryVariant {
  param(
    [Parameter(Mandatory = $true)]$Registry,
    [Parameter(Mandatory = $true)][string]$Path
  )

  Write-TestUtf8 -Path $Path -Value (($Registry | ConvertTo-Json -Depth 100) + "`n")
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-registry-' + [guid]::NewGuid().ToString('N'))
$fixtureManagementRoot = Join-Path $sandbox '开发管理'
$queueFixture = Join-Path $fixtureManagementRoot '当前任务队列.txt'

try {
  [IO.Directory]::CreateDirectory($fixtureManagementRoot) | Out-Null
  [IO.File]::Copy($queuePath, $queueFixture, $false)

  $registry = Read-TaskRegistry -Path $registryPath
  Assert-Equal ((@($registry.Keys) | Sort-Object) -join '|') 'schemaVersion|tasks' 'registry root fields'
  Assert-Equal $registry.schemaVersion 1 'registry schema version'
  Assert-Equal $registry.tasks.Count 5 'registry task count'
  Assert-Equal (($registry.tasks.taskId) -join '|') 'TQ-057|TQ-059|TQ-069|N-SLOT-01|N-DIST-01' 'registry task order'

  $enabledTasks = @($registry.tasks | Where-Object { $_.executionEnabled })
  Assert-Equal $enabledTasks.Count 1 'enabled task count'
  Assert-Equal $enabledTasks[0].taskId 'TQ-057' 'enabled task id'

  Assert-RegistryMatchesQueue -Registry $registry -QueuePath $queueFixture
  $selected = Select-ExecutableTask -Registry $registry
  Assert-Equal $selected.taskId 'TQ-057' 'deterministic selected task'
  Assert-Equal (Get-TaskContract -Registry $registry -TaskId 'TQ-057').title 'D-TRUST-02：清理现存数据矛盾' 'task contract lookup'

  $tq057 = Get-TaskContract -Registry $registry -TaskId 'TQ-057'
  $expectedDecisionIds = @(
    'DEC-20260715-35ACB87E6C10',
    'DEC-20260715-75D7BA2AF210',
    'DEC-20260714-29A5D1356CC8',
    'DEC-20260714-320075D033A5',
    'DEC-20260713-A07FA708DB22'
  )
  Assert-Equal (($tq057.decisionIds) -join '|') ($expectedDecisionIds -join '|') 'TQ-057 decision ids'
  Assert-Equal (($tq057.requiredChecks) -join '|') 'data-chain|unity-editmode-related|pending-whitespace|cached-diff-check' 'TQ-057 required checks'
  $dataChainCheckerPath = 'tools/check-data-chain.ps1'
  Assert-True ($dataChainCheckerPath -cin @($tq057.allowedRoots)) 'TQ-057 exact data-chain checker scope'

  $multiplierRule = @($tq057.coverageRules | Where-Object { $_.decisionId -ceq 'DEC-20260715-75D7BA2AF210' })
  Assert-Equal $multiplierRule.Count 1 'double multiplier coverage rule count'
  $corePaths = @(
    'src/Assets/DataConfig/Spells.csv',
    'src/Assets/Scripts/Editor/DataConfigImporter.cs',
    'src/Assets/Scripts/Combat/SpellData.cs',
    'src/Assets/Scripts/Combat/CombatResolver.cs',
    $dataChainCheckerPath
  )
  foreach ($corePath in $corePaths) {
    Assert-True ($corePath -cin @($multiplierRule[0].requiredPaths)) "double multiplier path $corePath"
  }
  Assert-True ('src/Assets/Tests/EditMode/*.cs' -cin @($multiplierRule[0].requiredAnyGlobs)) 'double multiplier EditMode glob'
  Assert-Equal $multiplierRule[0].requiredInventories[0].root 'src/Assets/Data/Spells' 'double multiplier inventory root'
  Assert-Equal $multiplierRule[0].requiredInventories[0].glob '*.asset' 'double multiplier inventory glob'

  $statusMismatch = Copy-TestObject $registry
  $statusMismatch.tasks[0].status = '阻塞'
  Assert-Throws `
    -Script { Assert-RegistryMatchesQueue -Registry $statusMismatch -QueuePath $queueFixture } `
    -MessageLike 'registry_invalid' `
    -Label 'queue status mismatch'

  $ownerMismatch = Copy-TestObject $registry
  $ownerMismatch.tasks[0].owner = 'DeepSeek V4 Pro'
  Assert-Throws `
    -Script { Assert-RegistryMatchesQueue -Registry $ownerMismatch -QueuePath $queueFixture } `
    -MessageLike 'registry_invalid' `
    -Label 'queue owner mismatch'

  $dependencyMismatch = Copy-TestObject $registry
  $dependencyMismatch.tasks[0].dependencies = @('TQ-999')
  $dependencyMismatch.tasks[0].dependencyEvidence = @([ordered]@{
    taskId = 'TQ-999'
    status = 'completed'
    source = '开发管理/当前任务队列.txt'
    match = '依赖：TQ-999 已完成'
  })
  Assert-Throws `
    -Script { Assert-RegistryMatchesQueue -Registry $dependencyMismatch -QueuePath $queueFixture } `
    -MessageLike 'registry_invalid' `
    -Label 'queue dependency mismatch'

  $missingEvidence = Copy-TestObject $registry
  $missingEvidence.tasks[0].dependencyEvidence = @()
  Assert-True ($null -eq (Select-ExecutableTask -Registry $missingEvidence)) 'missing dependency evidence is not selectable'

  $invalidEvidence = Copy-TestObject $registry
  $invalidEvidence.tasks[0].dependencyEvidence[0].match = 'not present in queue'
  Assert-True ($null -eq (Select-ExecutableTask -Registry $invalidEvidence)) 'invalid dependency evidence is not selectable'

  $missingDecision = Copy-TestObject $registry
  $missingDecision.tasks[0].decisionIds = @($missingDecision.tasks[0].decisionIds | Where-Object { $_ -cne $expectedDecisionIds[0] })
  $missingDecisionPath = Join-Path $sandbox 'missing-decision.json'
  Write-RegistryVariant -Registry $missingDecision -Path $missingDecisionPath
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $missingDecisionPath } `
    -MessageLike 'registry_invalid' `
    -Label 'missing TQ-057 decision'

  $missingCorePath = Copy-TestObject $registry
  $missingCorePath.tasks[0].coverageRules[1].requiredPaths = @($missingCorePath.tasks[0].coverageRules[1].requiredPaths | Where-Object { $_ -cne $corePaths[0] })
  $missingCorePathFile = Join-Path $sandbox 'missing-core-path.json'
  Write-RegistryVariant -Registry $missingCorePath -Path $missingCorePathFile
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $missingCorePathFile } `
    -MessageLike 'registry_invalid' `
    -Label 'missing double multiplier path'

  $missingCheckerScope = Copy-TestObject $registry
  $missingCheckerScope.tasks[0].allowedRoots = @($missingCheckerScope.tasks[0].allowedRoots | Where-Object { $_ -cne $dataChainCheckerPath })
  $missingCheckerScopePath = Join-Path $sandbox 'missing-checker-scope.json'
  Write-RegistryVariant -Registry $missingCheckerScope -Path $missingCheckerScopePath
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $missingCheckerScopePath } `
    -MessageLike 'registry_invalid' `
    -Label 'missing exact data-chain checker scope'

  $missingCheck = Copy-TestObject $registry
  $missingCheck.tasks[0].requiredChecks = @($missingCheck.tasks[0].requiredChecks | Where-Object { $_ -cne 'data-chain' })
  $missingCheckPath = Join-Path $sandbox 'missing-check.json'
  Write-RegistryVariant -Registry $missingCheck -Path $missingCheckPath
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $missingCheckPath } `
    -MessageLike 'registry_invalid' `
    -Label 'missing required check'

  $duplicateId = Copy-TestObject $registry
  $duplicateId.tasks[1].taskId = $duplicateId.tasks[0].taskId
  $duplicateIdPath = Join-Path $sandbox 'duplicate-id.json'
  Write-RegistryVariant -Registry $duplicateId -Path $duplicateIdPath
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $duplicateIdPath } `
    -MessageLike 'registry_invalid' `
    -Label 'duplicate task id'

  $unknownField = Copy-TestObject $registry
  $unknownField.tasks[0].unexpected = 'no'
  $unknownFieldPath = Join-Path $sandbox 'unknown-field.json'
  Write-RegistryVariant -Registry $unknownField -Path $unknownFieldPath
  Assert-Throws `
    -Script { Read-TaskRegistry -Path $unknownFieldPath } `
    -MessageLike 'registry_invalid' `
    -Label 'unknown task field'

  Write-Output 'registry.tests: OK'
} finally {
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    [IO.Directory]::Delete($resolvedSandbox, $true)
  }
}
