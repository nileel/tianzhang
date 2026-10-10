#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value,[string]$Message) if(-not $Value){throw $Message} }
function Assert-Equal { param($Actual,$Expected,[string]$Message) if($Actual -ne $Expected){throw "$Message (expected=$Expected actual=$Actual)"} }
function Write-Utf8 { param([string]$Path,[string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))|Out-Null; [IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false)) }
function New-Card {
  param([string]$Id,[string]$Route,[string]$Owner,[string]$Title)
  $meta=[ordered]@{schemaVersion=1;id=$Id;title=$Title;priority='P1';route=$Route;owner=$Owner;domain='automation';stage='implementation';dispatchState='ready';blockedBy=@();stateReason='ready';expectedPaths=@("开发管理/任务卡/$Id.txt","开发管理/任务归档/$Id.txt",'开发管理/当前任务队列.txt','开发管理/任务列表/自动化任务.txt');sourceBacklog='开发管理/任务列表/自动化任务.txt'}
  $meta.schemaVersion=2; $meta.riskPreflight=@{explicitRefs=@();matched=@();gates=@()}
  $body=@("# $Id · $Title",'','## 来源与当前边界','fixture','## 必查范围','fixture','## 实施范围','fixture','## 禁止项','fixture','## 验证','fixture','## 完成条件','fixture','## 停止条件','fixture') -join "`n"
  @('---TASK-META---',($meta|ConvertTo-Json -Depth 20),'---TASK-BODY---',$body) -join "`n"
}
function Read-Meta { param([string]$Path) $text=[IO.File]::ReadAllText($Path); [regex]::Match($text,'(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value|ConvertFrom-Json -Depth 30 }
function Get-ContextDigest {
  param([object]$Metadata)
  $context=[ordered]@{id=[string]$Metadata.id;title=[string]$Metadata.title;priority=[string]$Metadata.priority;route=[string]$Metadata.route;owner=[string]$Metadata.owner;domain=[string]$Metadata.domain;stage=[string]$Metadata.stage;blockedBy=@($Metadata.blockedBy|ForEach-Object{[string]$_});expectedPaths=@($Metadata.expectedPaths|ForEach-Object{[string]$_});automationInputs=@();sourceBacklog=[string]$Metadata.sourceBacklog}
  $json=$context|ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}
function Invoke-State { param([string]$Action,[string]$Task,[string]$Context,[int[]]$Allowed=@(0)) $output=@(& pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Action $Action -RepositoryRoot $root -TaskId $Task -ContextPath $Context 2>$null); Assert-True ($LASTEXITCODE -in $Allowed) "$Action exit code was invalid"; $output[0]|ConvertFrom-Json -Depth 30 }
function Invoke-ScopeCheck {
  param([string]$ContextPath, [int]$ExpectedExit = 0)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'check-task-cards.ps1') -RepositoryRoot $root -TaskId TASK-A -Postcondition CheckpointScope -DecisionContextPath $ContextPath -OutputJson 2>$null)
  Assert-Equal $LASTEXITCODE $ExpectedExit 'Checkpoint scope validation exit mismatch'
  if ($ExpectedExit -eq 0) { Assert-Equal ($output[0] | ConvertFrom-Json).status 'ok' 'Checkpoint scope was not accepted' }
}
function Assert-TransitionScopeRejected {
  param([string]$Mode, [object]$Context)
  & {
    $ownerAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'), [ref]$null, [ref]$null)
    foreach ($name in @('Stop-Hourly', 'Normalize-FullPath', 'Invoke-GitText', 'Invoke-JsonTool', 'Integrate-StateTransition')) {
      $definition = $ownerAst.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name }, $false)
      . ([scriptblock]::Create($definition.Extent.Text))
    }
    . (Join-Path $PSScriptRoot 'hourly-integration-lock.ps1')
    # Only the private evidence sink/runtime are fixtures; use the real transition,
    # Git, integration lock and task-card checker before any state branch is created.
    function Write-PrivateJson { param($Container, $Name, $Value) $path = Join-Path $root '.git/scope-context.json'; Write-Utf8 $path ($Value | ConvertTo-Json -Depth 100); $path }
    function Set-Attention { param($Run, $Reason) }
    $Owner = 'deepseek'; $IntegrationLockTimeoutSeconds = 0
    $checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
    $run = [pscustomobject]@{runId='run-a';taskId='TASK-A'}
    $beforeHead = Invoke-GitText $root @('rev-parse', 'HEAD')
    $beforeBranches = Invoke-GitText $root @('for-each-ref', '--format=%(refname):%(objectname)', 'refs/heads')
    $detail = ''
    try { $null = Integrate-StateTransition -Run $run -Mode $Mode -Context $Context -ExistingWorktree $root }
    catch { $detail = [string]$_.Exception.Data['DetailCode'] }
    Assert-Equal $detail 'checkpoint_scope_projection_failed' 'Transition did not preserve the malformed-scope reason'
    Assert-Equal (Invoke-GitText $root @('rev-parse', 'HEAD')) $beforeHead 'Rejected scope changed HEAD'
    Assert-Equal (Invoke-GitText $root @('for-each-ref', '--format=%(refname):%(objectname)', 'refs/heads')) $beforeBranches 'Rejected scope created a state branch'
  }
}

$root=Join-Path ([IO.Path]::GetTempPath()) "tzg-decision-checkpoint-test-$([Guid]::NewGuid().ToString('N'))"
$scriptPath=Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
try{
  [IO.Directory]::CreateDirectory((Join-Path $root 'tools'))|Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $root 'tools\check-task-cards.ps1')
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'get-experience-risk-preflight.ps1') -Destination (Join-Path $root 'tools/get-experience-risk-preflight.ps1')
  Write-Utf8 (Join-Path $root '开发管理/经验库/风险索引.json') '{"schemaVersion":1,"experiences":[{"id":"EXP-AUTO-900","title":"New scope risk","preflightSummary":"Check approved source","status":"active","level":"notice","triggerMode":"path","domains":[],"stages":[],"pathPatterns":["src/approved.cs"],"textPatterns":[],"detailRef":"","gateRefs":[],"lastVerified":"2026-10-10"}],"gates":[]}'
  & git -C $root init -q; & git -C $root config user.name 'Decision Test'; & git -C $root config user.email 'decision@example.invalid'
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-A.txt') (New-Card 'TASK-A' 'external_execute' 'deepseek' 'Checkpoint A')
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-B.txt') (New-Card 'TASK-B' 'codex_execute' 'codex' 'Block B')
  $queue=@('# 当前任务队列','','| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |','|---|---|---|---|---|---|---|---|','| TASK-A | external_execute | deepseek | P1 | automation | implementation | Checkpoint A | 开发管理/任务卡/TASK-A.txt |','| TASK-B | codex_execute | codex | P1 | automation | implementation | Block B | 开发管理/任务卡/TASK-B.txt |','') -join "`n"
  $backlog=@('# 自动化任务','','| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |','|---|---|---|---|---|---|---|','| TASK-A | P1 | deepseek | 已排队 | — | Checkpoint A | 开发管理/任务卡/TASK-A.txt |','| TASK-B | P1 | codex | 已排队 | — | Block B | 开发管理/任务卡/TASK-B.txt |','') -join "`n"
  Write-Utf8 (Join-Path $root '开发管理\当前任务队列.txt') $queue; Write-Utf8 (Join-Path $root '开发管理\任务列表\自动化任务.txt') $backlog
  & git -C $root add -A; & git -C $root commit -q -m 'test: seed'

  $meta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  $pause=[ordered]@{schemaVersion=1;taskId='TASK-A';sourceRunId='run-a';owner='deepseek';route='external_execute';decisionId='DEC-20260803-TESTA';question='选择哪一种？';options=@(@{key='A';label='甲'},@{key='B';label='乙'},@{key='C';label='丙'});recommendedOption='B';impactSummary='B 风险最低';plainSummary=@{situation='需要决定';impact='会影响实现';action='请选择 B'};checkpointCommit=('a'*40);baseCommit=('b'*40);branch='codex/automation/deepseek/run-a/candidate';changedPaths=@('result.txt');verified=@('test');unverified=@('none');residualRisk='none';taskContextDigest=(Get-ContextDigest $meta);createdAt='2026-08-03T00:00:00.0000000+00:00'}
  $projectionPaths=@('开发管理\任务卡\TASK-A.txt','开发管理\当前任务队列.txt','开发管理\任务列表\自动化任务.txt')
  $beforeInvalid=@($projectionPaths|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes((Join-Path $root $_))))})
  $invalidPause=($pause|ConvertTo-Json -Compress -Depth 20)|ConvertFrom-Json -Depth 20
  $invalidPause.decisionId='TASK-A-PATHS-01'
  $invalidPausePath=Join-Path $root 'invalid-pause.json'; Write-Utf8 $invalidPausePath ($invalidPause|ConvertTo-Json -Compress -Depth 20)
  $invalidPaused=Invoke-State 'PauseDecision' 'TASK-A' $invalidPausePath @(1)
  Assert-Equal $invalidPaused.status 'failed' 'Invalid decisionId was accepted'
  $afterInvalid=@($projectionPaths|ForEach-Object{[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes((Join-Path $root $_))))})
  Assert-Equal ($afterInvalid -join '|') ($beforeInvalid -join '|') 'Invalid decisionId changed task projections'
  $pausePath=Join-Path $root 'pause.json'; Write-Utf8 $pausePath ($pause|ConvertTo-Json -Compress -Depth 20)
  $body = ([IO.File]::ReadAllText((Join-Path $root '开发管理/任务卡/TASK-A.txt')) -split '---TASK-BODY---\r?\n',2)[1]
  foreach ($option in $pause.options) {
    $option.targetState = if ($option.key -ceq 'C') { 'blocked' } else { 'ready' }
    $option.expectedPaths = @($meta.expectedPaths) + @('result.txt', 'src/approved.cs')
    $option.taskBody = $body + "`n`n## 已批准范围`n仅在 src/approved.cs 完成新增合同并验证。`n"
  }
  Write-Utf8 $pausePath ($pause|ConvertTo-Json -Compress -Depth 20)
  Invoke-ScopeCheck $pausePath
  $validBody = $pause.options[0].taskBody
  foreach ($badBody in @($validBody.Replace('## 来源与当前边界','## 来源与原子结果'), $validBody.Replace('# TASK-A · Checkpoint A','# WRONG · Wrong'))) {
    $pause.options[0].taskBody = $badBody
    Write-Utf8 $pausePath ($pause|ConvertTo-Json -Compress -Depth 20)
    Invoke-ScopeCheck $pausePath 1
    Assert-TransitionScopeRejected 'PauseDecision' $pause
  }
  $pause.options[0].taskBody = $validBody
  Write-Utf8 $pausePath ($pause|ConvertTo-Json -Compress -Depth 20)
  $paused=Invoke-State 'PauseDecision' 'TASK-A' $pausePath
  Assert-Equal $paused.dispatchState 'pending_decision' 'Pause did not change state'
  $pausedMeta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  Assert-Equal $pausedMeta.automationCheckpoint.queueIndex 0 'Queue position was not captured'
  Assert-True (-not ([IO.File]::ReadAllText((Join-Path $root '开发管理\当前任务队列.txt')) -match 'TASK-A')) 'Paused task remained in queue'

  $reply=[ordered]@{schemaVersion=1;taskId='TASK-A';decisionId='DEC-20260803-TESTA';result='OPTION_ACCEPTED';replyKind='option';replyValue='B';source='feishu_card';evidenceHash=('c'*64)}
  $replyPath=Join-Path $root 'reply.json'; Write-Utf8 $replyPath ($reply|ConvertTo-Json -Compress)
  $pendingOriginal = @{}
  foreach ($path in $projectionPaths) { $pendingOriginal[$path] = [IO.File]::ReadAllBytes((Join-Path $root $path)) }
  $cardPath = Join-Path $root '开发管理/任务卡/TASK-A.txt'
  $pendingText = [IO.File]::ReadAllText($cardPath)
  $badMeta = Read-Meta $cardPath
  $badMeta.automationCheckpoint.options[1].taskBody = $badMeta.automationCheckpoint.options[1].taskBody.Replace('## 来源与当前边界','## 来源与原子结果')
  $pendingBody = ($pendingText -split '---TASK-BODY---\r?\n',2)[1]
  Write-Utf8 $cardPath (@('---TASK-META---',($badMeta|ConvertTo-Json -Depth 50),'---TASK-BODY---',$pendingBody) -join "`n")
  $badPending = [IO.File]::ReadAllBytes($cardPath)
  Invoke-ScopeCheck $replyPath 1
  # Two hourly attempts must report the same cause, without creating a branch or ready projection.
  1..2 | ForEach-Object { Assert-TransitionScopeRejected 'ResumeReady' $reply }
  $staleBase = [string](& git -C $root rev-parse master)
  & git -C $root branch "codex/automation/deepseek/run-a/state-resumeready-$($staleBase.Substring(0,12))" $staleBase
  Assert-Equal $LASTEXITCODE 0 'Could not seed a historical failed state branch'
  Assert-TransitionScopeRejected 'ResumeReady' $reply
  Assert-Equal ([Convert]::ToHexString([IO.File]::ReadAllBytes($cardPath))) ([Convert]::ToHexString($badPending)) 'Rejected legacy scope changed the pending card'
  [IO.File]::WriteAllBytes($cardPath, $pendingOriginal['开发管理\任务卡\TASK-A.txt'])
  Invoke-ScopeCheck $replyPath
  foreach ($case in @('missing', 'invalid-path')) {
    $cardPath = Join-Path $root '开发管理/任务卡/TASK-A.txt'
    $cardText = [IO.File]::ReadAllText($cardPath)
    $cardMeta = Read-Meta $cardPath
    if ($case -eq 'missing') { $cardMeta.automationCheckpoint.options[1].PSObject.Properties.Remove('expectedPaths') }
    else { $cardMeta.automationCheckpoint.options[1].expectedPaths = @('../outside.cs') }
    $cardBody = ($cardText -split '---TASK-BODY---\r?\n',2)[1]
    Write-Utf8 $cardPath (@('---TASK-META---',($cardMeta|ConvertTo-Json -Depth 50),'---TASK-BODY---',$cardBody) -join "`n")
    $before = [IO.File]::ReadAllText($cardPath)
    $rejected = Invoke-State 'ResumeReady' 'TASK-A' $replyPath @(1)
    Assert-Equal $rejected.detailCode $(if($case -eq 'missing'){'checkpoint_scope_contract_missing'}else{'checkpoint_scope_contract_invalid'}) 'Invalid scope was not rejected precisely'
    Assert-Equal ([IO.File]::ReadAllText($cardPath)) $before 'Invalid scope changed the card'
    [IO.File]::WriteAllBytes($cardPath, $pendingOriginal['开发管理\任务卡\TASK-A.txt'])
  }
  $matcher = Join-Path $root 'tools/get-experience-risk-preflight.ps1'
  $matcherOriginal = [IO.File]::ReadAllBytes($matcher)
  Write-Utf8 $matcher 'Write-Output ''{"status":"preflight_overbroad"}'''
  $failedPreflight = Invoke-State 'ResumeReady' 'TASK-A' $replyPath @(1)
  Assert-Equal $failedPreflight.detailCode 'checkpoint_scope_preflight_failed' 'Preflight failure was accepted'
  foreach ($path in $projectionPaths) { Assert-Equal ([Convert]::ToHexString([IO.File]::ReadAllBytes((Join-Path $root $path)))) ([Convert]::ToHexString($pendingOriginal[$path])) 'Failed preflight published a partial ready projection' }
  [IO.File]::WriteAllBytes($matcher, $matcherOriginal)
  $resumed=Invoke-State 'ResumeReady' 'TASK-A' $replyPath
  Assert-Equal $resumed.dispatchState 'ready' 'Reply did not restore ready'
  $resumedMeta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  Assert-Equal $resumedMeta.automationReply.replyValue 'B' 'Reply evidence was not bound'
  Assert-True (@($resumedMeta.expectedPaths) -ccontains 'src/approved.cs') 'Approved path was not refrozen'
  Assert-Equal $resumedMeta.schemaVersion 2 'Scope resume did not produce schema 2'
  Assert-True (@($resumedMeta.riskPreflight.matched) -ccontains 'EXP-AUTO-900') 'Preflight did not recompute risk for the newly approved path'
  Assert-True ([IO.File]::ReadAllText((Join-Path $root '开发管理/任务卡/TASK-A.txt')).Contains('仅在 src/approved.cs 完成新增合同并验证。')) 'Selected task body was not applied'
  $queueText=[IO.File]::ReadAllText((Join-Path $root '开发管理\当前任务队列.txt'))
  Assert-True ($queueText.IndexOf('TASK-A',[StringComparison]::Ordinal) -lt $queueText.IndexOf('TASK-B',[StringComparison]::Ordinal)) 'Queue position was not restored'
  foreach ($path in $projectionPaths) { [IO.File]::WriteAllBytes((Join-Path $root $path), $pendingOriginal[$path]) }
  $reply.replyValue = 'C'; Write-Utf8 $replyPath ($reply|ConvertTo-Json -Compress)
  Assert-Equal (Invoke-State 'ResumeReady' 'TASK-A' $replyPath).dispatchState 'blocked' 'Pause option was incorrectly readied'
  Assert-True (-not ([IO.File]::ReadAllText((Join-Path $root '开发管理/当前任务队列.txt')) -match 'TASK-A')) 'Pause option entered the queue'

  $block=[ordered]@{schemaVersion=1;taskId='TASK-B';detailCode='dependency_missing'}
  $blockPath=Join-Path $root 'block.json'; Write-Utf8 $blockPath ($block|ConvertTo-Json -Compress)
  $blocked=Invoke-State 'Block' 'TASK-B' $blockPath
  Assert-Equal $blocked.dispatchState 'blocked' 'Ordinary blocker did not transition'
  $invalidResume=Invoke-State 'ResumeReady' 'TASK-B' $replyPath @(1)
  Assert-Equal $invalidResume.status 'failed' 'Ordinary failure used checkpoint resume path'
  Write-Output 'test-hourly-decision-checkpoint: OK'
}finally{if(Test-Path -LiteralPath $root){$resolved=[IO.Path]::GetFullPath($root); if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $resolved) -notlike 'tzg-decision-checkpoint-test-*'){throw 'Unsafe test cleanup'}; Remove-Item -LiteralPath $resolved -Recurse -Force}}
