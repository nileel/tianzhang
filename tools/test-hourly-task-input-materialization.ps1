#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $output = @(& git -C $Root @Arguments 2>&1)
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  @($output | ForEach-Object { [string]$_ })
}

function Import-InputMaterializationFunctions {
  $target = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
  $tokens = $null; $errors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$errors)
  Assert-True (@($errors).Count -eq 0) 'cannot parse invoke-hourly-owner.ps1'
  $names = @(
    'Stop-Hourly', 'Normalize-FullPath', 'Invoke-GitText', 'Get-NormalizedTextDigestFromText', 'Get-NormalizedTextDigest',
    'Assert-AutomationInputPath', 'Get-TaskAutomationInputs', 'Get-AutomationInputFileEvidence',
    'New-AutomationInputDestination', 'Assert-MaterializedAutomationInputs', 'Materialize-TaskAutomationInputs',
    'Read-TaskMetadata', 'Read-RunTaskMetadata', 'Read-TaskMetadataAtCommit', 'Assert-WorktreePath', 'New-CandidateWorktree',
    'Get-ChangedPaths', 'Assert-CandidateEvidence', 'Remove-ExactSuccessfulWorktree'
  )
  foreach ($name in $names) {
    $matches = @($ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $true))
    Assert-True ($matches.Count -eq 1) "missing target function: $name"
    $definition = $matches[0].Extent.Text -replace ('(?m)^function\s+' + [regex]::Escape($name) + '\b'), "function global:$name"
    Invoke-Expression $definition
  }
}

function New-InputContract {
  param([string]$Root)
  $relativePaths = @(
    'assets/source/characters/fuyuan/raw/fuyuan_static_chess_raw.fbx',
    'assets/source/characters/fuyuan/raw/textures/basecolor.JPEG',
    'assets/source/characters/fuyuan/raw/textures/metallic.JPEG',
    'assets/source/characters/fuyuan/raw/textures/normal.JPEG',
    'assets/source/characters/fuyuan/raw/textures/rm.JPEG',
    'assets/source/characters/fuyuan/raw/textures/roughness.JPEG'
  )
  $inputs = @()
  for ($index = 0; $index -lt $relativePaths.Count; $index++) {
    $path = Join-Path $Root $relativePaths[$index].Replace('/', [IO.Path]::DirectorySeparatorChar)
    $text = "fuyuan-input-$index-$('x' * ($index + 1))"
    Write-Utf8 $path $text
    $file = Get-Item -LiteralPath $path
    $inputs += [ordered]@{
      path = $relativePaths[$index]
      bytes = [int64]$file.Length
      sha256 = [string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
    }
  }
  @($inputs)
}

function Write-TestTaskCard {
  param([string]$Root, [object[]]$Inputs)
  $metadata = [ordered]@{
    schemaVersion = 1
    id = 'T-INPUT-01'
    title = '输入物化测试'
    priority = 'P1'
    route = 'codex_execute'
    owner = 'codex'
    domain = 'automation'
    stage = 'implementation'
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = 'fixture'
    expectedPaths = @('changed.txt', '开发管理/任务卡/T-INPUT-01.txt', '开发管理/任务归档/T-INPUT-01.txt')
    sourceBacklog = '开发管理/任务列表/审核与交接任务.txt'
    automationInputs = $Inputs
  }
  $body = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', '# T-INPUT-01 · 输入物化测试',
    '## 来源与当前边界', '## 必查范围', '## 实施范围', '## 禁止项', '## 验证', '## 完成条件', '## 停止条件'
  ) -join "`n"
  Write-Utf8 (Join-Path $Root '开发管理/任务卡/T-INPUT-01.txt') $body
}

function New-TestRepository {
  param([string]$Parent, [string]$Name)
  $root = Join-Path $Parent $Name
  [IO.Directory]::CreateDirectory($root) | Out-Null
  Invoke-Git $root @('init', '--initial-branch=master') | Out-Null
  Invoke-Git $root @('config', 'user.name', 'materialization-test') | Out-Null
  Invoke-Git $root @('config', 'user.email', 'materialization@test.invalid') | Out-Null
  Write-Utf8 (Join-Path $root '.gitignore') ".worktrees/`nassets/source/`n"
  $inputs = New-InputContract $root
  Write-TestTaskCard -Root $root -Inputs $inputs
  Write-Utf8 (Join-Path $root 'README.md') 'fixture'
  Invoke-Git $root @('add', '.gitignore', 'README.md', '开发管理/任务卡/T-INPUT-01.txt') | Out-Null
  Invoke-Git $root @('commit', '-m', 'fixture') | Out-Null
  [pscustomobject]@{ Root = $root; Inputs = $inputs; BaseCommit = (Invoke-Git $root @('rev-parse', 'HEAD') | Select-Object -First 1) }
}

function New-TestRun {
  param([object]$Repository, [string]$Name)
  [pscustomobject]@{
    runId = $Name
    taskId = 'T-INPUT-01'
    route = 'codex_execute'
    worktree = (Join-Path $Repository.Root ".worktrees/automation/$Name/codex")
    candidateBranch = "test/input/$Name/candidate"
    baseCommit = $Repository.BaseCommit
    taskCardDigest = Get-NormalizedTextDigest (Join-Path $Repository.Root '开发管理/任务卡/T-INPUT-01.txt')
  }
}

function Get-InputPath {
  param([object]$Repository, [int]$Index = 0, [string]$Base = 'Root')
  $root = [string]$Repository.$Base
  Join-Path $root ([string]$Repository.Inputs[$Index].path).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function Assert-HourlyFailure {
  param([string]$Name, [string]$ExpectedCode, [scriptblock]$Action)
  try { & $Action } catch {
    Assert-True ($_.Exception.Message -ceq $ExpectedCode) "$Name returned $($_.Exception.Message)"
    return
  }
  throw "$Name should fail closed"
}

function Assert-InputFailure {
  param([string]$Name, [scriptblock]$Action)
  Assert-HourlyFailure -Name $Name -ExpectedCode 'hourly_task_input_validation_failed' -Action $Action
}

function Get-RepositorySnapshot {
  param([object]$Repository)
  $sourceEvidence = foreach ($input in $Repository.Inputs) {
    $path = Join-Path $Repository.Root ([string]$input.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $item = Get-Item -LiteralPath $path -Force
    "$($input.path)|$($item.Length)|$([string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
  }
  [pscustomobject]@{
    SourceEvidence = @($sourceEvidence) -join "`n"
    Status = @(Invoke-Git $Repository.Root @('status', '--porcelain=v1', '--untracked-files=all')) -join "`n"
    Tracked = @(Invoke-Git $Repository.Root @('ls-files')) -join "`n"
  }
}

function Assert-Materialized {
  param([object]$Repository, [object]$Run)
  foreach ($input in $Repository.Inputs) {
    $path = Join-Path $Run.worktree ([string]$input.path).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $item = Get-Item -LiteralPath $path -Force
    Assert-True ($item.Length -eq [int64]$input.bytes) "copy size mismatch: $($input.path)"
    Assert-True (([string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash).ToUpperInvariant() -ceq [string]$input.sha256) "copy hash mismatch: $($input.path)"
    Assert-True (($item.Attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) "copy is not read-only: $($input.path)"
  }
}

function Set-TargetRoot {
  param([string]$Root)
  $script:root = $Root
  $global:root = $Root
}

Import-InputMaterializationFunctions
$script:Owner = 'codex'
$global:Owner = 'codex'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('hourly-task-input-materialization-' + [guid]::NewGuid().ToString('N'))

try {
  [IO.Directory]::CreateDirectory($tempRoot) | Out-Null

  $success = New-TestRepository -Parent $tempRoot -Name 'success'
  Set-TargetRoot $success.Root
  $successBefore = Get-RepositorySnapshot $success
  $successRun = New-TestRun -Repository $success -Name 'run-success'
  $null = New-CandidateWorktree $successRun
  Assert-Materialized -Repository $success -Run $successRun
  Assert-MaterializedAutomationInputs -Worktree $successRun.worktree -Metadata (Read-TaskMetadata $success.Root 'T-INPUT-01').Metadata
  $successAfter = Get-RepositorySnapshot $success
  Assert-True ($successAfter.SourceEvidence -ceq $successBefore.SourceEvidence) 'successful materialization changed source evidence'
  Assert-True ($successAfter.Status -ceq $successBefore.Status) 'successful materialization changed main repository status'
  Assert-True ($successAfter.Tracked -ceq $successBefore.Tracked) 'successful materialization changed tracked files'

  $contractMutation = New-TestRepository -Parent $tempRoot -Name 'contract-mutation'
  Set-TargetRoot $contractMutation.Root
  $contractMutationRun = New-TestRun -Repository $contractMutation -Name 'run-contract-mutation'
  $extraRelative = 'assets/source/characters/fuyuan/raw/textures/extra.JPEG'
  $extraPath = Join-Path $contractMutation.Root $extraRelative.Replace('/', [IO.Path]::DirectorySeparatorChar)
  Write-Utf8 $extraPath 'extra-input'
  $extraItem = Get-Item -LiteralPath $extraPath
  $mutatedInputs = @($contractMutation.Inputs) + @([ordered]@{
    path = $extraRelative
    bytes = [int64]$extraItem.Length
    sha256 = [string](Get-FileHash -LiteralPath $extraPath -Algorithm SHA256).Hash.ToUpperInvariant()
  })
  Write-TestTaskCard -Root $contractMutation.Root -Inputs $mutatedInputs
  Assert-HourlyFailure -Name 'task contract mutation after claim' -ExpectedCode 'hourly_task_changed_after_claim' -Action { New-CandidateWorktree $contractMutationRun }
  $unexpectedCopy = Join-Path $contractMutationRun.worktree ([string]$contractMutation.Inputs[0].path).Replace('/', [IO.Path]::DirectorySeparatorChar)
  Assert-True (-not (Test-Path -LiteralPath $unexpectedCopy)) 'task contract mutation copied an input before digest rejection'

  $missing = New-TestRepository -Parent $tempRoot -Name 'missing'
  Set-TargetRoot $missing.Root
  Remove-Item -LiteralPath (Get-InputPath $missing) -Force
  Assert-InputFailure 'missing source before candidate start' { New-CandidateWorktree (New-TestRun -Repository $missing -Name 'run-missing') }

  $size = New-TestRepository -Parent $tempRoot -Name 'size'
  Set-TargetRoot $size.Root
  Add-Content -LiteralPath (Get-InputPath $size) -Value 'size mismatch' -NoNewline
  Assert-InputFailure 'source size mismatch before candidate start' { New-CandidateWorktree (New-TestRun -Repository $size -Name 'run-size') }

  $hash = New-TestRepository -Parent $tempRoot -Name 'hash'
  Set-TargetRoot $hash.Root
  $hashPath = Get-InputPath $hash
  $hashLength = (Get-Item -LiteralPath $hashPath).Length
  [IO.File]::WriteAllBytes($hashPath, [byte[]](0..($hashLength - 1) | ForEach-Object { 65 }))
  Assert-InputFailure 'source hash mismatch before candidate start' { New-CandidateWorktree (New-TestRun -Repository $hash -Name 'run-hash') }

  $reparse = New-TestRepository -Parent $tempRoot -Name 'reparse'
  Set-TargetRoot $reparse.Root
  $reparsePath = Split-Path -Parent (Get-InputPath $reparse -Index 1)
  $actualPath = "$reparsePath-real"
  Move-Item -LiteralPath $reparsePath -Destination $actualPath
  New-Item -ItemType Junction -Path $reparsePath -Target $actualPath | Out-Null
  Assert-InputFailure 'reparse source before candidate start' { New-CandidateWorktree (New-TestRun -Repository $reparse -Name 'run-reparse') }

  $preexisting = New-TestRepository -Parent $tempRoot -Name 'preexisting'
  Set-TargetRoot $preexisting.Root
  $preexistingRun = New-TestRun -Repository $preexisting -Name 'run-preexisting'
  [IO.Directory]::CreateDirectory((Split-Path -Parent $preexistingRun.worktree)) | Out-Null
  Invoke-Git $preexisting.Root @('worktree', 'add', '-b', $preexistingRun.candidateBranch, $preexistingRun.worktree, $preexistingRun.baseCommit) | Out-Null
  Write-Utf8 (Join-Path $preexistingRun.worktree ([string]$preexisting.Inputs[0].path).Replace('/', [IO.Path]::DirectorySeparatorChar)) 'already exists'
  Assert-InputFailure 'preexisting destination before candidate start' { New-CandidateWorktree $preexistingRun }

  $tamper = New-TestRepository -Parent $tempRoot -Name 'tamper'
  Set-TargetRoot $tamper.Root
  $tamperRun = New-TestRun -Repository $tamper -Name 'run-tamper'
  $null = New-CandidateWorktree $tamperRun
  $tamperCopy = Join-Path $tamperRun.worktree ([string]$tamper.Inputs[0].path).Replace('/', [IO.Path]::DirectorySeparatorChar)
  $tamperItem = Get-Item -LiteralPath $tamperCopy -Force
  $tamperItem.Attributes = $tamperItem.Attributes -band (-bnot [IO.FileAttributes]::ReadOnly)
  Add-Content -LiteralPath $tamperCopy -Value 'tampered' -NoNewline
  Write-Utf8 (Join-Path $tamperRun.worktree 'changed.txt') 'candidate change'
  Invoke-Git $tamperRun.worktree @('add', 'changed.txt') | Out-Null
  Invoke-Git $tamperRun.worktree @('commit', '-m', 'candidate') | Out-Null
  $tamperCandidate = [pscustomobject]@{
    status = 'completed'
    candidateCommit = (Invoke-Git $tamperRun.worktree @('rev-parse', 'HEAD') | Select-Object -First 1)
    candidateResult = [pscustomobject]@{ changedPaths = @('changed.txt') }
  }
  Assert-InputFailure 'candidate copy tamper after candidate' { Assert-CandidateEvidence -Run $tamperRun -Candidate $tamperCandidate }

  $sourceTamper = New-TestRepository -Parent $tempRoot -Name 'source-tamper'
  Set-TargetRoot $sourceTamper.Root
  $sourceTamperRun = New-TestRun -Repository $sourceTamper -Name 'run-source-tamper'
  $null = New-CandidateWorktree $sourceTamperRun
  Write-Utf8 (Join-Path $sourceTamperRun.worktree 'changed.txt') 'candidate change'
  Invoke-Git $sourceTamperRun.worktree @('add', 'changed.txt') | Out-Null
  Invoke-Git $sourceTamperRun.worktree @('commit', '-m', 'candidate') | Out-Null
  $sourceTamperPath = Get-InputPath $sourceTamper
  $sourceTamperLength = (Get-Item -LiteralPath $sourceTamperPath).Length
  [IO.File]::WriteAllBytes($sourceTamperPath, [byte[]](0..($sourceTamperLength - 1) | ForEach-Object { 65 }))
  $sourceTamperCandidate = [pscustomobject]@{
    status = 'completed'
    candidateCommit = (Invoke-Git $sourceTamperRun.worktree @('rev-parse', 'HEAD') | Select-Object -First 1)
    candidateResult = [pscustomobject]@{ changedPaths = @('changed.txt') }
  }
  Assert-InputFailure 'main source tamper after candidate' { Assert-CandidateEvidence -Run $sourceTamperRun -Candidate $sourceTamperCandidate }

  $cleanup = New-TestRepository -Parent $tempRoot -Name 'cleanup'
  Set-TargetRoot $cleanup.Root
  $cleanupRun = New-TestRun -Repository $cleanup -Name 'run-cleanup'
  $null = New-CandidateWorktree $cleanupRun
  Write-Utf8 (Join-Path $cleanupRun.worktree 'changed.txt') 'candidate change'
  Invoke-Git $cleanupRun.worktree @('add', 'changed.txt') | Out-Null
  Invoke-Git $cleanupRun.worktree @('commit', '-m', 'candidate') | Out-Null
  $cleanupHead = (Invoke-Git $cleanupRun.worktree @('rev-parse', 'HEAD') | Select-Object -First 1)
  Invoke-Git $cleanup.Root @('merge', '--ff-only', $cleanupHead) | Out-Null
  $cleanupRun | Add-Member -NotePropertyName canonicalBranch -NotePropertyValue 'test/input/run-cleanup/canonical'
  Invoke-Git $cleanupRun.worktree @('switch', '-c', $cleanupRun.canonicalBranch) | Out-Null
  function Invoke-Runtime {
    param([string]$RuntimeAction, [hashtable]$Parameters = @{}, [int[]]$AllowedExitCodes = @(0))
    [pscustomobject]@{ state = [pscustomobject]@{ runs = [pscustomobject]@{ codex = $null; deepseek = $null } } }
  }
  $cleanupResult = Remove-ExactSuccessfulWorktree -Run $cleanupRun -FormalHead $cleanupHead
  Assert-True ($cleanupResult -ceq 'cleaned') "successful cleanup returned $cleanupResult"
  Assert-True (-not (Test-Path -LiteralPath $cleanupRun.worktree)) 'successful cleanup retained the worktree with ignored inputs'

  Write-Output 'test-hourly-task-input-materialization: PASS'
} finally {
  if (Test-Path -LiteralPath $tempRoot) {
    $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
    Assert-True ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase)) 'refusing to remove a non-temp test directory'
    Remove-Item -LiteralPath $resolved -Recurse -Force
  }
}
