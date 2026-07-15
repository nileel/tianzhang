$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-controller-repair.ps1'
$guardTool = Join-Path $root 'tools\automation-workspace-guard.ps1'
$fixturePath = Join-Path $root 'tools\fixtures\automation-controller-v5-chained-decision-stuck.json'
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ("tzg-repair-test-" + [guid]::NewGuid().ToString('N'))
$repository = Join-Path $sandbox 'repository'
$stateDirectory = Join-Path $sandbox 'state'
$statePath = Join-Path $stateDirectory 'controller.json'
$runRoot = Join-Path $stateDirectory 'runs'
$memoryPath = Join-Path $sandbox 'automation\memory.md'
$sessionId = 'session-redacted-001'
$sessionPath = Join-Path $runRoot "$sessionId.json"
$baselinePath = Join-Path $runRoot "$sessionId.baseline.json"
$engine = (Get-Process -Id $PID).Path
$threadId = '019f63c5-f73c-70a0-9773-5592a3e03194'
$decisionId = 'DEC-20260715-35ACB87E6C10'
$expectedRelativePath = '开发管理/自动工作流状态.txt'
$expectedFilePath = Join-Path $repository ($expectedRelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))

function Invoke-RepairTool {
  param([string[]]$Arguments)
  $oldPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = (($output | ForEach-Object { [string]$_ }) -join "`n") }
  } finally {
    $ErrorActionPreference = $oldPreference
  }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)
  if ($Result.Code -ne $Expected) { throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)" }
}

function Get-BytesHash {
  param([string]$Path)
  $bytes = [IO.File]::ReadAllBytes($Path)
  ([Security.Cryptography.SHA256]::HashData($bytes) | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Write-IncidentState {
  $fixture = Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json
  $fixture.recoveryBaselinePath = $baselinePath
  [IO.File]::WriteAllText($statePath, ($fixture | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}

function Get-RepairArguments {
  param([string]$Action)
  @($Action, '-RepositoryRoot', $repository, '-StatePath', $statePath, '-RunRoot', $runRoot, '-MemoryPath', $memoryPath,
    '-IncidentDecisionId', $decisionId, '-SelectedOption', 'B', '-EvidenceThreadId', $threadId)
}

New-Item -ItemType Directory -Path $repository, $stateDirectory, $runRoot, (Split-Path -Parent $memoryPath) -Force | Out-Null
try {
  & git -C $repository init --quiet
  if ($LASTEXITCODE -ne 0) { throw 'git init failed' }
  & git -C $repository config user.email 'automation-test@localhost'
  & git -C $repository config user.name 'Automation Test'
  New-Item -ItemType Directory -Path (Split-Path -Parent $expectedFilePath) -Force | Out-Null
  [IO.File]::WriteAllText($expectedFilePath, 'stable', [Text.UTF8Encoding]::new($false))
  & git -C $repository add -- $expectedRelativePath
  & git -C $repository commit --quiet -m baseline
  if ($LASTEXITCODE -ne 0) { throw 'baseline commit failed' }
  & $engine -NoProfile -ExecutionPolicy Bypass -File $guardTool Snapshot -RepositoryRoot $repository -BaselinePath $baselinePath
  if ($LASTEXITCODE -ne 0) { throw 'baseline snapshot failed' }

  $session = [ordered]@{
    protocolVersion = 2
    runId = $sessionId
    repositoryRoot = $repository
    baselinePath = $baselinePath
    currentBaselinePath = $baselinePath
    evidencePath = $null
    phase = 'mutation_started'
    taskId = 'TQ-057'
  }
  [IO.File]::WriteAllText($sessionPath, ($session | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText($memoryPath, "# Automation memory`nredacted incident`n", [Text.UTF8Encoding]::new($false))
  Write-IncidentState

  $stateBefore = Get-BytesHash $statePath
  $sessionBefore = Get-BytesHash $sessionPath
  $memoryBefore = Get-BytesHash $memoryPath
  $dryRun = Invoke-RepairTool (Get-RepairArguments 'DryRun')
  Assert-Code $dryRun 0 'repair dry-run'
  $drySummary = $dryRun.Output | ConvertFrom-Json
  if ($drySummary.action -ne 'dry_run' -or $drySummary.result -ne 'projected' -or $drySummary.before.schemaVersion -ne 5 -or
      $drySummary.after.schemaVersion -ne 6 -or $drySummary.after.state -ne 'IDLE' -or $drySummary.after.source -ne 'manual') {
    throw 'dry-run did not return the expected redacted projection'
  }
  if ($dryRun.Output.Contains($threadId) -or $dryRun.Output.Contains($baselinePath) -or $dryRun.Output.Contains($repository)) {
    throw 'dry-run output disclosed raw evidence or private paths'
  }
  if ((Get-BytesHash $statePath) -ne $stateBefore -or (Get-BytesHash $sessionPath) -ne $sessionBefore -or (Get-BytesHash $memoryPath) -ne $memoryBefore) {
    throw 'dry-run mutated state, session, or memory'
  }

  $withoutOverride = Invoke-RepairTool (Get-RepairArguments 'Apply')
  Assert-Code $withoutOverride 15 'apply without manual override'
  if ((Get-BytesHash $statePath) -ne $stateBefore) { throw 'rejected apply mutated state' }

  $applyArguments = @(Get-RepairArguments 'Apply') + '-ManualOverride'
  $apply = Invoke-RepairTool $applyArguments
  Assert-Code $apply 0 'repair apply'
  $applySummary = $apply.Output | ConvertFrom-Json
  if ($applySummary.action -ne 'apply' -or $applySummary.result -ne 'repaired' -or $applySummary.decisionId -ne $decisionId -or
      $applySummary.optionKey -ne 'B' -or $applySummary.source -ne 'manual' -or $applySummary.state -ne 'IDLE') {
    throw 'apply summary is incomplete'
  }
  if ($apply.Output.Contains($threadId) -or $apply.Output.Contains($baselinePath) -or $apply.Output.Contains($repository)) {
    throw 'apply output disclosed raw evidence or private paths'
  }
  $backupDirectory = Join-Path $stateDirectory $applySummary.backupDirectoryName
  foreach ($backupName in @('state.before.json', 'session.before.json', 'memory.before.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $backupDirectory $backupName))) { throw "missing repair backup: $backupName" }
  }
  if ((Get-BytesHash (Join-Path $backupDirectory 'state.before.json')) -ne $stateBefore -or
      (Get-BytesHash (Join-Path $backupDirectory 'session.before.json')) -ne $sessionBefore -or
      (Get-BytesHash (Join-Path $backupDirectory 'memory.before.md')) -ne $memoryBefore) {
    throw 'repair backups are not byte-identical to their sources'
  }
  $repaired = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
  $resolved = $repaired.decisionFlow.resolvedDecisions[0]
  $correction = $repaired.auditCorrections[0]
  $threadBytes = [Text.UTF8Encoding]::new($false).GetBytes($threadId)
  $threadHash = ([Security.Cryptography.SHA256]::HashData($threadBytes) | ForEach-Object { $_.ToString('x2') }) -join ''
  if ($repaired.schemaVersion -ne 6 -or $repaired.state -ne 'IDLE' -or $null -ne $repaired.runId -or $null -ne $repaired.runMode -or
      $null -ne $repaired.leaseExpiresAt -or $null -ne $repaired.taskId -or $null -ne $repaired.recoveryBaselinePath -or
      $null -ne $repaired.recoveryEvidencePath -or $null -ne $repaired.recoveryEvidenceHash -or $repaired.recoveryCount -ne 0 -or
      $null -ne $repaired.pendingDecision -or $repaired.decisionFlow.status -ne 'IMPLEMENTATION_PENDING') {
    throw 'applied repair did not establish the schema v6 idle invariants'
  }
  if ($resolved.decisionId -ne $decisionId -or $resolved.resolution.optionKey -ne 'B' -or $resolved.resolution.source -ne 'manual' -or
      $correction.oldValue -ne 'email' -or $correction.newValue -ne 'manual' -or $correction.evidenceHash -ne $threadHash -or
      [string]::IsNullOrWhiteSpace([string]$correction.correctedAt) -or $correction.reason.Length -gt 240) {
    throw 'applied repair did not preserve the corrected decision audit'
  }
  $serialized = Get-Content -Raw -LiteralPath $statePath
  if ($serialized -match '(?i)example\.invalid|"providerMessageId"\s*:|"messageId"\s*:|"sender"\s*:|"recipient"\s*:|"body"\s*:') {
    throw 'repaired state introduced raw mail provenance'
  }

  $repairedHash = Get-BytesHash $statePath
  $secondApply = Invoke-RepairTool $applyArguments
  Assert-Code $secondApply 0 'idempotent apply'
  $secondSummary = $secondApply.Output | ConvertFrom-Json
  if ($secondSummary.result -ne 'already_repaired' -or (Get-BytesHash $statePath) -ne $repairedHash) {
    throw 'second apply was not byte-idempotent'
  }

  Write-IncidentState
  $blockedStateHash = Get-BytesHash $statePath
  [IO.File]::WriteAllText($expectedFilePath, 'changed', [Text.UTF8Encoding]::new($false))
  $expectedChanged = Invoke-RepairTool $applyArguments
  if ($expectedChanged.Code -eq 0 -or (Get-BytesHash $statePath) -ne $blockedStateHash) {
    throw 'expected-path change did not block apply without mutating state'
  }

  [IO.File]::WriteAllText($expectedFilePath, 'stable', [Text.UTF8Encoding]::new($false))
  [IO.File]::WriteAllText((Join-Path $repository 'outside.txt'), 'intruder', [Text.UTF8Encoding]::new($false))
  $outsideChanged = Invoke-RepairTool $applyArguments
  if ($outsideChanged.Code -eq 0 -or (Get-BytesHash $statePath) -ne $blockedStateHash) {
    throw 'outside-path change did not block apply without mutating state'
  }

  'test-automation-controller-repair: OK'
} finally {
  Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
