$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-finalize-commit.ps1'
$engine = (Get-Process -Id $PID).Path
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ('tzg-finalize-commit-test-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $sandbox '中文仓库'
$legacyConsoleWrapper = Join-Path $sandbox 'legacy-console.ps1'

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $output = & git -C $repo @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  $output
}

function Invoke-Helper {
  param(
    [string]$ExpectedPaths,
    [string]$CommitMessage,
    [switch]$RequireAutomationMetadata,
    [string]$AutomationTask,
    [string]$AutomationState,
    [string]$AutomationResult,
    [string]$AutomationImpact,
    [string]$AutomationVerify,
    [string]$AutomationPlain
  )

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $arguments = @(
      '-NoProfile',
      '-ExecutionPolicy', 'Bypass',
      '-File', $tool,
      '-RepositoryRoot', $repo,
      '-ExpectedPaths', $ExpectedPaths,
      '-CommitMessage', $CommitMessage
    )
    if ($RequireAutomationMetadata) {
      $arguments += @(
        '-RequireAutomationMetadata',
        '-AutomationTask', $AutomationTask,
        '-AutomationState', $AutomationState,
        '-AutomationResult', $AutomationResult,
        '-AutomationImpact', $AutomationImpact,
        '-AutomationVerify', $AutomationVerify,
        '-AutomationPlain', $AutomationPlain
      )
    }
    $output = & $engine @arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Invoke-HelperWithLegacyConsole {
  param([string]$ExpectedPaths, [string]$CommitMessage)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $legacyConsoleWrapper `
      -Tool $tool -RepositoryRoot $repo -ExpectedPaths $ExpectedPaths -CommitMessage $CommitMessage 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Write-Utf8 {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
  [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

Write-Utf8 $legacyConsoleWrapper @'
param(
  [string]$Tool,
  [string]$RepositoryRoot,
  [string]$ExpectedPaths,
  [string]$CommitMessage
)

[Console]::OutputEncoding = [Text.Encoding]::GetEncoding(1252)
& $Tool -RepositoryRoot $RepositoryRoot -ExpectedPaths $ExpectedPaths -CommitMessage $CommitMessage
'@
New-Item -ItemType Directory -Path $repo -Force | Out-Null
try {
  Invoke-Git init | Out-Null
  Invoke-Git config user.name 'Finalize Commit Test' | Out-Null
  Invoke-Git config user.email 'finalize-commit@example.invalid' | Out-Null
  Invoke-Git config core.quotepath false | Out-Null

  $expected = '目录/决策 状态.txt'
  $secondExpected = '目录/第二 状态.txt'
  $unrelatedStaged = 'manual-staged.txt'
  $unrelatedDirty = 'manual-dirty.txt'
  $unrelatedUntracked = 'manual-untracked.txt'
  Write-Utf8 (Join-Path $repo $expected) "expected base`n"
  Write-Utf8 (Join-Path $repo $secondExpected) "second expected base  `n"
  Write-Utf8 (Join-Path $repo $unrelatedStaged) "staged base`n"
  Write-Utf8 (Join-Path $repo $unrelatedDirty) "dirty base`n"
  Invoke-Git add -- . | Out-Null
  Invoke-Git commit -m 'test: base' | Out-Null

  Write-Utf8 (Join-Path $repo $unrelatedStaged) "staged change`n"
  Invoke-Git add -- $unrelatedStaged | Out-Null
  $stagedBlobBefore = (Invoke-Git rev-parse ":$unrelatedStaged") -join ''
  Write-Utf8 (Join-Path $repo $unrelatedDirty) "dirty change`n"
  Write-Utf8 (Join-Path $repo $unrelatedUntracked) "untracked change`n"
  $dirtyHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedDirty)).Hash
  $untrackedHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedUntracked)).Hash
  Write-Utf8 (Join-Path $repo $expected) "expected change`n"

  $result = Invoke-Helper $expected 'test: expected only'
  if ($result.Code -ne 0) { throw "commit helper failed: $($result.Output)" }
  $committed = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($committed.Count -ne 1 -or $committed[0] -ne $expected) { throw "unexpected commit paths: $($committed -join ', ')" }
  if (((Invoke-Git rev-parse ":$unrelatedStaged") -join '') -ne $stagedBlobBefore) { throw 'unrelated staged blob changed' }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedDirty)).Hash -ne $dirtyHashBefore) { throw 'unrelated dirty file changed' }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedUntracked)).Hash -ne $untrackedHashBefore) { throw 'unrelated untracked file changed' }

  $cleanAllowedHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $secondExpected)).Hash
  Write-Utf8 (Join-Path $repo $expected) "expected allowed subset change`n"
  $subsetResult = Invoke-Helper "$expected|$secondExpected" 'test: changed subset only'
  if ($subsetResult.Code -ne 0) { throw "commit helper failed for changed subset: $($subsetResult.Output)" }
  $subsetCommitted = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($subsetCommitted.Count -ne 1 -or $subsetCommitted[0] -ne $expected) {
    throw "clean allowed path was added to the commit: $($subsetCommitted -join ', ')"
  }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $secondExpected)).Hash -ne $cleanAllowedHashBefore) {
    throw 'clean allowed path was rewritten by finalization'
  }

  $missingPotential = '目录/尚未创建.asset'
  Write-Utf8 (Join-Path $repo $expected) "expected decision-only change`n"
  $potentialResult = Invoke-Helper "$expected|$missingPotential" 'test: changed path with missing potential path'
  if ($potentialResult.Code -ne 0) { throw "commit helper rejected an unchanged missing potential path: $($potentialResult.Output)" }
  $potentialCommitted = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($potentialCommitted.Count -ne 1 -or $potentialCommitted[0] -ne $expected) {
    throw "missing potential path affected the commit: $($potentialCommitted -join ', ')"
  }

  Write-Utf8 (Join-Path $repo $expected) "expected second change`n"
  Write-Utf8 (Join-Path $repo $secondExpected) "second expected change`n"
  $multiResult = Invoke-Helper "$expected|$secondExpected" 'test: multiple expected paths'
  if ($multiResult.Code -ne 0) { throw "commit helper failed for multiple paths: $($multiResult.Output)" }
  $multiCommitted = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($multiCommitted.Count -ne 2 -or $multiCommitted -notcontains $expected -or $multiCommitted -notcontains $secondExpected) {
    throw "unexpected multi-path commit: $($multiCommitted -join ', ')"
  }
  if (((Invoke-Git rev-parse ":$unrelatedStaged") -join '') -ne $stagedBlobBefore) { throw 'multi-path commit changed unrelated staged blob' }

  $activeCard = '开发管理/任务卡/归档测试.txt'
  $archiveCard = '开发管理/任务归档/归档测试.txt'
  Write-Utf8 (Join-Path $repo $activeCard) "archive fixture base`n"
  Invoke-Git add -- $activeCard | Out-Null
  Invoke-Git commit --only -m 'test: add active archive fixture' -- $activeCard | Out-Null
  Write-Utf8 (Join-Path $repo $archiveCard) "archive fixture base`ncompleted`n"
  Remove-Item -LiteralPath (Join-Path $repo $activeCard) -Force
  Invoke-Git add -- $activeCard $archiveCard | Out-Null

  $archiveResult = Invoke-Helper "$activeCard|$archiveCard" 'test: archive active card atomically'
  if ($archiveResult.Code -ne 0) { throw "commit helper failed for staged archive: $($archiveResult.Output)" }
  $archiveCommitted = @(Invoke-Git show --format= --name-status --no-renames HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if (
    $archiveCommitted.Count -ne 2 -or
    $archiveCommitted -notcontains "D`t$activeCard" -or
    $archiveCommitted -notcontains "A`t$archiveCard"
  ) {
    throw "archive commit did not include both source deletion and destination addition: $($archiveCommitted -join ', ')"
  }
  & git -C $repo diff --cached --quiet -- $activeCard $archiveCard
  if ($LASTEXITCODE -ne 0) { throw 'archive commit left expected paths staged' }
  if (((Invoke-Git rev-parse ":$unrelatedStaged") -join '') -ne $stagedBlobBefore) { throw 'archive commit changed unrelated staged blob' }

  Write-Utf8 (Join-Path $repo $expected) "expected legacy console change`n"
  $legacyResult = Invoke-HelperWithLegacyConsole $expected 'test: unicode path with legacy console'
  if ($legacyResult.Code -ne 0) { throw "commit helper failed with legacy console encoding: $($legacyResult.Output)" }
  $legacyCommitted = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($legacyCommitted.Count -ne 1 -or $legacyCommitted[0] -ne $expected) {
    throw "unexpected legacy-console commit paths: $($legacyCommitted -join ', ')"
  }

  Write-Utf8 (Join-Path $repo $expected) "expected automation metadata change`n"
  $automationFields = @{
    AutomationTask = 'TASK-AUTO-001'
    AutomationState = 'completed'
    AutomationResult = '问题=自动化提交缺少统一元数据门禁；完成=finalizer 在提交前使用统一契约'
    AutomationImpact = '影响=无效摘要不会进入提交历史；边界=不修改路径隔离与任务状态'
    AutomationVerify = '验证=test-automation-finalize-commit 通过；后续=等待固定调用器复用同一契约'
    AutomationPlain = '发生=自动化提交能够保存通俗说明；影响=负责人收到任务通知时能看懂实际结果；需要=无需处理'
  }
  $invalidAutomationFields = @(
    ($automationFields.Clone() | ForEach-Object { $_.AutomationTask = ''; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationState = 'failed'; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationResult = "完成自动化提交元数据测试`n额外一行"; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationResult = '问题=缺少完成字段'; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationImpact = ''; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationImpact = '影响=缺少边界字段'; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationVerify = ''; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationVerify = '验证=缺少后续字段'; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationPlain = ''; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationPlain = '发生=缺少影响和动作'; $_ }),
    ($automationFields.Clone() | ForEach-Object { $_.AutomationPlain = "发生=$('长' * 201)；影响=不会进入提交；需要=无需处理"; $_ })
  )
  foreach ($invalidAutomationFieldSet in $invalidAutomationFields) {
    $headBeforeInvalid = (Invoke-Git rev-parse HEAD) -join ''
    $cachedBeforeInvalid = (Invoke-Git diff --cached --binary) -join "`n"
    $invalidResult = Invoke-Helper `
      -ExpectedPaths $expected `
      -CommitMessage 'feat(test): 写入自动化成果摘要' `
      -RequireAutomationMetadata `
      @invalidAutomationFieldSet
    if ($invalidResult.Code -eq 0) { throw 'invalid automation metadata was accepted' }
    if (((Invoke-Git rev-parse HEAD) -join '') -ne $headBeforeInvalid) { throw 'invalid automation metadata created a commit' }
    if (((Invoke-Git diff --cached --binary) -join "`n") -cne $cachedBeforeInvalid) { throw 'invalid automation metadata changed the index' }
  }

  $automationResult = Invoke-Helper `
    -ExpectedPaths $expected `
    -CommitMessage 'feat(test): 写入自动化成果摘要' `
    -RequireAutomationMetadata `
    @automationFields
  if ($automationResult.Code -ne 0) { throw "valid automation metadata was rejected: $($automationResult.Output)" }
  $automationBody = ((Invoke-Git log -1 --format=%B) -join "`n").Replace("`r`n", "`n").TrimEnd()
  $expectedAutomationBody = @'
feat(test): 写入自动化成果摘要

Automation: tzg-hourly-controller
Task: TASK-AUTO-001
State: completed
Result: 问题=自动化提交缺少统一元数据门禁；完成=finalizer 在提交前使用统一契约
Impact: 影响=无效摘要不会进入提交历史；边界=不修改路径隔离与任务状态
Verify: 验证=test-automation-finalize-commit 通过；后续=等待固定调用器复用同一契约
Plain: 发生=自动化提交能够保存通俗说明；影响=负责人收到任务通知时能看懂实际结果；需要=无需处理
'@.Replace("`r`n", "`n").TrimEnd()
  if ($automationBody -cne $expectedAutomationBody) { throw "automation metadata changed in Git: $automationBody" }

  $replayBase = (Invoke-Git rev-parse HEAD) -join ''
  Write-Utf8 (Join-Path $repo $expected) "candidate tree replay change`n"
  Invoke-Git add -- $expected | Out-Null
  Invoke-Git commit --only -m 'candidate(TEST): Codex implementation' -- $expected | Out-Null
  $candidateSha = (Invoke-Git rev-parse HEAD) -join ''
  $candidateTree = (Invoke-Git rev-parse "$candidateSha^{tree}") -join ''
  Invoke-Git switch --detach $replayBase | Out-Null
  Invoke-Git cherry-pick --no-commit $candidateSha | Out-Null
  $formalReplay = Invoke-Helper `
    -ExpectedPaths $expected `
    -CommitMessage 'feat(TEST): complete Codex task' `
    -RequireAutomationMetadata `
    @automationFields
  if ($formalReplay.Code -ne 0) { throw "candidate tree replay finalization failed: $($formalReplay.Output)" }
  $formalSha = (Invoke-Git rev-parse HEAD) -join ''
  $formalTree = (Invoke-Git rev-parse 'HEAD^{tree}') -join ''
  $formalSubject = (Invoke-Git log -1 --format=%s) -join ''
  $formalBody = ((Invoke-Git log -1 --format=%B) -join "`n").Replace("`r`n", "`n")
  if ($formalSha -ceq $candidateSha) { throw 'formal replay reused the candidate commit identity' }
  if ($formalTree -cne $candidateTree) { throw 'formal replay changed the candidate tree' }
  if ($formalSubject -cne 'feat(TEST): complete Codex task' -or $formalSubject.StartsWith('candidate(', [StringComparison]::Ordinal)) {
    throw "formal replay kept an invalid subject: $formalSubject"
  }
  foreach ($line in @(
      'State: completed',
      "Result: $($automationFields.AutomationResult)",
      "Impact: $($automationFields.AutomationImpact)",
      "Verify: $($automationFields.AutomationVerify)",
      "Plain: $($automationFields.AutomationPlain)"
    )) {
    if (-not $formalBody.Contains($line, [StringComparison]::Ordinal)) { throw "formal replay metadata is missing: $line" }
  }

  $headBeforeMissing = (Invoke-Git rev-parse HEAD) -join ''
  $missing = Invoke-Helper 'missing.txt' 'test: must not commit'
  if ($missing.Code -eq 0) { throw 'missing expected path was accepted' }
  if (((Invoke-Git rev-parse HEAD) -join '') -ne $headBeforeMissing) { throw 'missing expected path created a commit' }

  'test-automation-finalize-commit: OK'
} finally {
  if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
