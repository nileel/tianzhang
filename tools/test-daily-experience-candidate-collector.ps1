#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:toolRoot = $PSScriptRoot
$script:temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$script:fixtures = [Collections.Generic.List[string]]::new()

function Assert-True { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ([string]$Actual -cne [string]$Expected) { throw "$Message (actual=$Actual expected=$Expected)" } }

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-Native {
  param([string]$FileName, [string[]]$Arguments, [string]$WorkingDirectory)
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = $FileName; $start.WorkingDirectory = $WorkingDirectory; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { throw 'unable to start fixture process' }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $result = [pscustomobject]@{ ExitCode=$process.ExitCode; Stdout=$stdoutTask.GetAwaiter().GetResult().TrimEnd("`r","`n"); Stderr=$stderrTask.GetAwaiter().GetResult().TrimEnd("`r","`n") }
  $process.Dispose(); $result
}

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $result = Invoke-Native -FileName 'git' -Arguments (@('-C', $Root) + $Arguments) -WorkingDirectory $Root
  if ($result.ExitCode -ne 0) { throw "fixture git failed: $($Arguments -join ' ') $($result.Stderr)" }
  [string]$result.Stdout
}

function New-TaskCardText {
  param([string]$Id, [string]$State = 'ready', [string]$Domain = 'automation', [string[]]$BlockedBy = @())
  $metadata = [ordered]@{
    schemaVersion=1; id=$Id; title="Fixture $Id"; priority='P1'; route='codex_execute'; owner='codex'; domain=$Domain
    stage='implementation'; dispatchState=$State; blockedBy=[object[]]$BlockedBy; stateReason='fixture state'
    expectedPaths=[object[]]@("开发管理/任务卡/$Id.txt", "开发管理/任务归档/$Id.txt")
    sourceBacklog='开发管理/任务列表/管理与自动化任务.txt'
  }
  @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $Id · Fixture $Id", '',
    '## 来源与当前边界', '', '- fixture', '', '## 必查范围', '', '- fixture', '', '## 实施范围', '', '- fixture', '',
    '## 禁止项', '', '- fixture', '', '## 验证', '', '- fixture', '', '## 完成条件', '', '- fixture', '', '## 停止条件', '', '- fixture', ''
  ) -join "`n"
}

function New-CandidateBlock {
  param(
    [string]$Id, [string]$State='pending', [string]$PromotionTaskId='', [string]$Resolution='unresolved',
    [string]$ExperienceId='', [string]$Domain='automation', [switch]$InvalidMissingEvidence
  )
  $lines = @(
    "### $Id", "- collectionState: $State",
    $(if ([string]::IsNullOrEmpty($PromotionTaskId)) { '- promotionTaskId:' } else { "- promotionTaskId: $PromotionTaskId" }),
    "- resolution: $Resolution",
    $(if ([string]::IsNullOrEmpty($ExperienceId)) { '- experienceId:' } else { "- experienceId: $ExperienceId" }),
    "- 经验领域：$Domain", "- 现象：fixture symptom $Id",
    "- 已证实根因：fixture root cause $Id", "- 可能复用范围：fixture scope $Id"
  )
  if (-not $InvalidMissingEvidence) { $lines += "- 证据：fixture evidence $Id" }
  $lines += '- 门禁可能：no'
  $lines -join "`n"
}

function New-ArchiveText {
  param([string]$Id, [string[]]$CandidateBlocks, [string]$State='completed')
  $metadata = [ordered]@{
    schemaVersion=1; id=$Id; title="Archive $Id"; priority='P2'; route='codex_execute'; owner='codex'; domain='automation'
    stage='implementation'; dispatchState=$State; blockedBy=[object[]]@(); stateReason='fixture archive'
    expectedPaths=[object[]]@("开发管理/任务卡/$Id.txt", "开发管理/任务归档/$Id.txt")
    sourceBacklog='开发管理/任务列表/管理与自动化任务.txt'
  }
  $parts = @(
    '---TASK-META---', ($metadata | ConvertTo-Json -Depth 10), '---TASK-BODY---', "# $Id · Archive $Id", '',
    '## 来源与当前边界', '', '- fixture', '', '## 必查范围', '', '- fixture', '', '## 实施范围', '', '- fixture', '',
    '## 禁止项', '', '- fixture', '', '## 验证', '', '- fixture', '', '## 完成条件', '', '- fixture', '', '## 停止条件', '', '- fixture', '',
    '## 经验候选', ''
  )
  $parts += $CandidateBlocks
  ($parts -join "`n") + "`n"
}

function Commit-All {
  param([string]$Root, [string]$Message, [string]$Date)
  $oldAuthor=$env:GIT_AUTHOR_DATE; $oldCommitter=$env:GIT_COMMITTER_DATE
  try {
    $env:GIT_AUTHOR_DATE=$Date; $env:GIT_COMMITTER_DATE=$Date
    $null=Invoke-Git $Root @('add','--','.')
    $null=Invoke-Git $Root @('commit','-m',$Message)
  } finally { $env:GIT_AUTHOR_DATE=$oldAuthor; $env:GIT_COMMITTER_DATE=$oldCommitter }
  Invoke-Git $Root @('rev-parse','HEAD')
}

function New-Fixture {
  $id=[guid]::NewGuid().ToString('N'); $root=[IO.Path]::GetFullPath((Join-Path $script:temporaryRoot "tzg-daily-collector-test-$id"))
  if (-not $root.StartsWith($script:temporaryRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'fixture escaped temp root' }
  [IO.Directory]::CreateDirectory($root)|Out-Null; $script:fixtures.Add($root)
  foreach ($dir in @('tools','开发管理/任务卡','开发管理/任务归档','开发管理/任务列表','开发管理/经验库/经验卡')) { [IO.Directory]::CreateDirectory((Join-Path $root $dir))|Out-Null }
  foreach ($tool in @(
    'invoke-daily-experience-candidate-collector.ps1','get-experience-risk-preflight.ps1','check-task-cards.ps1',
    'check-review-text.ps1','check-pending-whitespace.ps1','automation-finalize-commit.ps1','automation-commit-metadata.ps1',
    'invoke-project-integration.ps1','hourly-integration-lock.ps1'
  )) { Copy-Item -LiteralPath (Join-Path $script:toolRoot $tool) -Destination (Join-Path $root "tools/$tool") }
  Write-Utf8 (Join-Path $root '.gitignore') ".worktrees/`n"
  Write-Utf8 (Join-Path $root '开发管理/经验库/风险索引.json') "{`n  `"schemaVersion`": 1,`n  `"experiences`": [],`n  `"gates`": []`n}`n"
  $existing='TASK-EXISTING-READY'
  Write-Utf8 (Join-Path $root "开发管理/任务卡/$existing.txt") (New-TaskCardText $existing)
  Write-Utf8 (Join-Path $root '开发管理/当前任务队列.txt') (@(
    '# 当前任务队列（fixture）','', '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |',
    '|----|------|------|--------|------|------|------|--------|',
    "| $existing | codex_execute | codex | P1 | automation | implementation | Fixture $existing | 开发管理/任务卡/$existing.txt |",''
  ) -join "`n")
  Write-Utf8 (Join-Path $root '开发管理/任务列表/管理与自动化任务.txt') (@(
    '# 管理与自动化任务列表（fixture）','', '## 任务','', '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |',
    '|----|--------|------|----------|--------|------|--------|',
    "| $existing | P1 | codex | 已排队 | — | Fixture $existing | 开发管理/任务卡/$existing.txt |",'', '## 执行边界','', '- fixture',''
  ) -join "`n")
  $null=Invoke-Git $root @('init','-b','master'); $null=Invoke-Git $root @('config','user.name','Collector Test'); $null=Invoke-Git $root @('config','user.email','collector@example.invalid')
  $null=Commit-All $root 'test: initialize collector fixture' '2026-08-01T00:00:00+08:00'
  $root
}

function Invoke-Collector {
  param([string]$Root)
  $result=Invoke-Native -FileName 'pwsh' -Arguments @(
    '-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $Root 'tools/invoke-daily-experience-candidate-collector.ps1'),
    '-RepositoryRoot',$Root,'-Now','2026-08-31T12:00:00+08:00','-OutputJson'
  ) -WorkingDirectory $Root
  $lines=@($result.Stdout -split "`r?`n" | Where-Object { $_ })
  if ($lines.Count -ne 1) { throw "collector did not return one JSON line: $($result.Stdout) stderr=$($result.Stderr)" }
  try { $json=$lines[0]|ConvertFrom-Json -Depth 100 } catch { throw "collector output invalid: $($result.Stdout)" }
  [pscustomobject]@{ ExitCode=$result.ExitCode; Json=$json; Stdout=$result.Stdout; Stderr=$result.Stderr }
}

function Get-PromotionTaskId {
  param([string]$SourceTaskId,[string]$CandidateId)
  $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes("$SourceTaskId`n$CandidateId")))
  'M-EXP-PROMOTE-'+$hash.Substring(0,12)
}

function Assert-SourceState {
  param([string]$Root,[string]$SourceId,[string]$CandidateId,[string]$State,[string]$PromotionTaskId)
  $text=Get-Content -LiteralPath (Join-Path $Root "开发管理/任务归档/$SourceId.txt") -Raw
  $candidate=[regex]::Match($text,"(?ms)^### $([regex]::Escape($CandidateId))\r?\n(?<block>.*?)(?=^### |\z)")
  Assert-True $candidate.Success "source candidate missing: $SourceId/$CandidateId"
  $block=$candidate.Groups['block'].Value
  Assert-True ($block -match "(?m)^- collectionState: $([regex]::Escape($State))\r?$") "source state mismatch: $SourceId/$CandidateId"
  $promotionPattern=if ([string]::IsNullOrEmpty($PromotionTaskId)) { '(?m)^- promotionTaskId:\s*\r?$' } else { "(?m)^- promotionTaskId: $([regex]::Escape($PromotionTaskId))\r?$" }
  Assert-True ($block -match $promotionPattern) "source promotion task mismatch: $SourceId/$CandidateId"
}

try {
  # Zero candidates: no worktree commit and no writes.
  $zero=New-Fixture; $zeroHead=Invoke-Git $zero @('rev-parse','HEAD'); $zeroResult=Invoke-Collector $zero
  Assert-Equal $zeroResult.ExitCode 0 'zero candidate run failed'; Assert-Equal $zeroResult.Json.status 'no_candidate' 'zero candidate status mismatch'
  Assert-Equal (Invoke-Git $zero @('rev-parse','HEAD')) $zeroHead 'zero candidate changed HEAD'
  Assert-Equal (Invoke-Git $zero @('status','--porcelain=v1','--untracked-files=all')) '' 'zero candidate dirtied repository'

  # Previous-day, older pending, same-source multiple candidates, skips, non-completed source, blocked content, and idempotence.
  $success=New-Fixture
  Write-Utf8 (Join-Path $success '开发管理/任务归档/SRC-OLD.txt') (New-ArchiveText 'SRC-OLD' @((New-CandidateBlock 'CAND-SRC-OLD-01' -Domain automation)))
  $null=Commit-All $success 'test: add old pending candidate' '2026-08-20T09:00:00+08:00'
  $previousBlocks=@(
    (New-CandidateBlock 'CAND-SRC-PREV-01' -Domain management),
    (New-CandidateBlock 'CAND-SRC-PREV-02' -Domain content),
    (New-CandidateBlock 'CAND-SRC-PREV-03' -Domain data -State provisional),
    (New-CandidateBlock 'CAND-SRC-PREV-04' -Domain unity -State collected -PromotionTaskId 'M-EXISTING')
  )
  Write-Utf8 (Join-Path $success '开发管理/任务归档/SRC-PREV.txt') (New-ArchiveText 'SRC-PREV' $previousBlocks)
  Write-Utf8 (Join-Path $success '开发管理/任务归档/SRC-NONCOMPLETE.txt') (New-ArchiveText 'SRC-NONCOMPLETE' @((New-CandidateBlock 'CAND-SRC-NONCOMPLETE-01')) -State blocked)
  $null=Commit-All $success 'test: add previous-day candidate set' '2026-08-30T22:00:00+08:00'
  Write-Utf8 (Join-Path $success '开发管理/任务归档/SRC-TODAY.txt') (New-ArchiveText 'SRC-TODAY' @((New-CandidateBlock 'CAND-SRC-TODAY-01')))
  $successBase=Commit-All $success 'test: add current-day excluded candidate' '2026-08-31T08:00:00+08:00'
  $indexBefore=Get-Content -LiteralPath (Join-Path $success '开发管理/经验库/风险索引.json') -Raw
  $successResult=Invoke-Collector $success
  Assert-Equal $successResult.ExitCode 0 "success run failed: $($successResult.Stdout) $($successResult.Stderr)"
  Assert-Equal $successResult.Json.status 'completed' 'success status mismatch'; Assert-Equal $successResult.Json.candidateCount 3 'success candidate count mismatch'
  $oldTask=Get-PromotionTaskId 'SRC-OLD' 'CAND-SRC-OLD-01'; $managementTask=Get-PromotionTaskId 'SRC-PREV' 'CAND-SRC-PREV-01'; $contentTask=Get-PromotionTaskId 'SRC-PREV' 'CAND-SRC-PREV-02'
  foreach ($task in @($oldTask,$managementTask,$contentTask)) { Assert-True (Test-Path -LiteralPath (Join-Path $success "开发管理/任务卡/$task.txt")) "missing generated card $task" }
  Assert-SourceState $success 'SRC-OLD' 'CAND-SRC-OLD-01' 'collected' $oldTask
  Assert-SourceState $success 'SRC-PREV' 'CAND-SRC-PREV-01' 'collected' $managementTask
  Assert-SourceState $success 'SRC-PREV' 'CAND-SRC-PREV-02' 'collected' $contentTask
  Assert-SourceState $success 'SRC-TODAY' 'CAND-SRC-TODAY-01' 'pending' ''
  $contentText=Get-Content -LiteralPath (Join-Path $success "开发管理/任务卡/$contentTask.txt") -Raw
  Assert-True ($contentText -match '"dispatchState":\s+"blocked"') 'content candidate was not blocked'
  Assert-True ($contentText -match 'M-EXP-EXT-CONTENT-01') 'content blocker missing'
  $queue=Get-Content -LiteralPath (Join-Path $success '开发管理/当前任务队列.txt') -Raw
  Assert-True ($queue.IndexOf('TASK-EXISTING-READY',[StringComparison]::Ordinal) -lt $queue.IndexOf($oldTask,[StringComparison]::Ordinal)) 'existing ready order was not preserved'
  Assert-True (-not $queue.Contains($contentTask,[StringComparison]::Ordinal)) 'blocked content task entered queue'
  Assert-Equal (Get-Content -LiteralPath (Join-Path $success '开发管理/经验库/风险索引.json') -Raw) $indexBefore 'collector modified risk index'
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $success '开发管理/经验库/经验卡/EXP-AUTO-001.txt'))) 'collector created a formal experience card'
  Assert-Equal (Invoke-Git $success @('rev-list','--count',"$successBase..HEAD")) 1 'success batch was not one formal commit'
  Assert-Equal (Invoke-Git $success @('status','--porcelain=v1','--untracked-files=all')) '' 'success repository not clean'
  $repeatHead=Invoke-Git $success @('rev-parse','HEAD'); $repeat=Invoke-Collector $success
  Assert-Equal $repeat.Json.status 'no_candidate' 'repeat run duplicated cards'; Assert-Equal (Invoke-Git $success @('rev-parse','HEAD')) $repeatHead 'repeat run changed HEAD'

  # Invalid field set fails before any formal commit.
  $invalid=New-Fixture
  Write-Utf8 (Join-Path $invalid '开发管理/任务归档/SRC-INVALID.txt') (New-ArchiveText 'SRC-INVALID' @((New-CandidateBlock 'CAND-SRC-INVALID-01' -InvalidMissingEvidence)))
  $invalidHead=Commit-All $invalid 'test: add invalid candidate' '2026-08-30T10:00:00+08:00'; $invalidRun=Invoke-Collector $invalid
  Assert-True ($invalidRun.ExitCode -ne 0) 'invalid candidate unexpectedly succeeded'; Assert-Equal $invalidRun.Json.detailCode 'daily_collector_candidate_invalid' 'invalid candidate code mismatch'
  Assert-Equal (Invoke-Git $invalid @('rev-parse','HEAD')) $invalidHead 'invalid candidate formed a formal commit'

  # Stable task id collision fails the whole batch.
  $taskCollision=New-Fixture; $collisionSource='SRC-TASK-COLLISION'; $collisionCandidate='CAND-SRC-TASK-COLLISION-01'; $collisionTask=Get-PromotionTaskId $collisionSource $collisionCandidate
  Write-Utf8 (Join-Path $taskCollision "开发管理/任务卡/$collisionTask.txt") (New-TaskCardText $collisionTask -State blocked -BlockedBy @('UNRELATED'))
  Add-Content -LiteralPath (Join-Path $taskCollision '开发管理/任务列表/管理与自动化任务.txt') -Value "| $collisionTask | P1 | codex | 阻塞 | UNRELATED | Fixture $collisionTask | 开发管理/任务卡/$collisionTask.txt |" -Encoding utf8
  Write-Utf8 (Join-Path $taskCollision "开发管理/任务归档/$collisionSource.txt") (New-ArchiveText $collisionSource @((New-CandidateBlock $collisionCandidate)))
  $taskCollisionHead=Commit-All $taskCollision 'test: add task id collision' '2026-08-30T10:00:00+08:00'; $taskCollisionRun=Invoke-Collector $taskCollision
  Assert-Equal $taskCollisionRun.Json.detailCode 'daily_collector_task_id_collision' 'task collision code mismatch'; Assert-Equal (Invoke-Git $taskCollision @('rev-parse','HEAD')) $taskCollisionHead 'task collision changed HEAD'

  # Reserved experience path collision fails instead of silently taking another id.
  $experienceCollision=New-Fixture
  Write-Utf8 (Join-Path $experienceCollision '开发管理/经验库/经验卡/EXP-MGMT-001.txt') '# stray collision'
  Write-Utf8 (Join-Path $experienceCollision '开发管理/任务归档/SRC-EXP-COLLISION.txt') (New-ArchiveText 'SRC-EXP-COLLISION' @((New-CandidateBlock 'CAND-SRC-EXP-COLLISION-01' -Domain management)))
  $experienceCollisionHead=Commit-All $experienceCollision 'test: add experience path collision' '2026-08-30T10:00:00+08:00'; $experienceCollisionRun=Invoke-Collector $experienceCollision
  Assert-Equal $experienceCollisionRun.Json.detailCode 'daily_collector_experience_id_collision' 'experience collision code mismatch'; Assert-Equal (Invoke-Git $experienceCollision @('rev-parse','HEAD')) $experienceCollisionHead 'experience collision changed HEAD'

  # Source digest changes after worktree checkout are rejected.
  $digest=New-Fixture
  Write-Utf8 (Join-Path $digest '开发管理/任务归档/SRC-DIGEST.txt') (New-ArchiveText 'SRC-DIGEST' @((New-CandidateBlock 'CAND-SRC-DIGEST-01')))
  $digestHead=Commit-All $digest 'test: add digest candidate' '2026-08-30T10:00:00+08:00'
  $hook=@'
#!/bin/sh
case "$PWD" in
  *daily-experience-candidate-collector*) printf '\nchanged-after-checkout\n' >> "开发管理/任务归档/SRC-DIGEST.txt" ;;
esac
exit 0
'@
  Write-Utf8 (Join-Path $digest '.git/hooks/post-checkout') $hook
  $digestRun=Invoke-Collector $digest
  Assert-Equal $digestRun.Json.detailCode 'daily_collector_source_changed' 'source digest change code mismatch'; Assert-Equal (Invoke-Git $digest @('rev-parse','HEAD')) $digestHead 'source digest change altered HEAD'

  # Commit failure leaves the source pending in master.
  $commitFailure=New-Fixture
  Write-Utf8 (Join-Path $commitFailure '开发管理/任务归档/SRC-COMMIT-FAIL.txt') (New-ArchiveText 'SRC-COMMIT-FAIL' @((New-CandidateBlock 'CAND-SRC-COMMIT-FAIL-01')))
  $commitFailureHead=Commit-All $commitFailure 'test: add commit failure candidate' '2026-08-30T10:00:00+08:00'
  Write-Utf8 (Join-Path $commitFailure '.git/hooks/pre-commit') "#!/bin/sh`nexit 1`n"
  $commitFailureRun=Invoke-Collector $commitFailure
  Assert-Equal $commitFailureRun.Json.detailCode 'daily_collector_commit_failed' 'commit failure code mismatch'; Assert-Equal (Invoke-Git $commitFailure @('rev-parse','HEAD')) $commitFailureHead 'commit failure altered master'
  Assert-SourceState $commitFailure 'SRC-COMMIT-FAIL' 'CAND-SRC-COMMIT-FAIL-01' 'pending' ''

  # Dirty main target path is rejected by the existing integration helper.
  $dirty=New-Fixture
  Write-Utf8 (Join-Path $dirty '开发管理/任务归档/SRC-DIRTY.txt') (New-ArchiveText 'SRC-DIRTY' @((New-CandidateBlock 'CAND-SRC-DIRTY-01')))
  $dirtyHead=Commit-All $dirty 'test: add dirty-path candidate' '2026-08-30T10:00:00+08:00'
  Add-Content -LiteralPath (Join-Path $dirty '开发管理/任务列表/管理与自动化任务.txt') -Value '# dirty fixture' -Encoding utf8
  $dirtyRun=Invoke-Collector $dirty
  Assert-Equal $dirtyRun.Json.detailCode 'daily_collector_integration_failed' 'dirty path failure code mismatch'; Assert-Equal (Invoke-Git $dirty @('rev-parse','HEAD')) $dirtyHead 'dirty path integration changed master'
  Assert-SourceState $dirty 'SRC-DIRTY' 'CAND-SRC-DIRTY-01' 'pending' ''

  # Occupied process-held integration lock rejects the batch without a formal commit.
  $locked=New-Fixture
  Write-Utf8 (Join-Path $locked '开发管理/任务归档/SRC-LOCKED.txt') (New-ArchiveText 'SRC-LOCKED' @((New-CandidateBlock 'CAND-SRC-LOCKED-01')))
  $lockedHead=Commit-All $locked 'test: add locked candidate' '2026-08-30T10:00:00+08:00'
  . (Join-Path $locked 'tools/hourly-integration-lock.ps1')
  $handle=Enter-TzgIntegrationLock -RepositoryRoot $locked -TimeoutSeconds 0
  Assert-True ($null -ne $handle) 'fixture could not hold integration lock'
  try { $lockedRun=Invoke-Collector $locked } finally { Exit-TzgIntegrationLock -Handle $handle }
  Assert-Equal $lockedRun.Json.detailCode 'daily_collector_integration_failed' 'lock failure code mismatch'; Assert-Equal (Invoke-Git $locked @('rev-parse','HEAD')) $lockedHead 'lock failure changed master'

  $collectorText=Get-Content -LiteralPath (Join-Path $script:toolRoot 'invoke-daily-experience-candidate-collector.ps1') -Raw
  Assert-True ($collectorText -notmatch 'invoke-hourly-owner|invoke-codex-candidate|invoke-deepseek-responsibility') 'collector references an owner/model wrapper'
  Write-Output 'test-daily-experience-candidate-collector: OK'
} finally {
  foreach ($fixture in $script:fixtures) {
    $resolved=[IO.Path]::GetFullPath($fixture).TrimEnd('\','/')
    if (-not $resolved.StartsWith($script:temporaryRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -cnotmatch '^tzg-daily-collector-test-[0-9a-f]{32}$') {
      throw "unsafe fixture cleanup target: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
  }
}
