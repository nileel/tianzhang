#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value,[string]$Message) if(-not $Value){throw $Message} }
function Assert-Equal { param($Actual,$Expected,[string]$Message) if($Actual -ne $Expected){throw "$Message (expected=$Expected actual=$Actual)"} }
function Write-Utf8 { param([string]$Path,[string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))|Out-Null; [IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false)) }
function New-Card {
  param([string]$Id,[string]$Route,[string]$Owner,[string]$Title)
  $meta=[ordered]@{schemaVersion=1;id=$Id;title=$Title;priority='P1';route=$Route;owner=$Owner;domain='automation';stage='implementation';dispatchState='ready';blockedBy=@();stateReason='ready';expectedPaths=@("开发管理/任务卡/$Id.txt","开发管理/任务归档/$Id.txt",'开发管理/当前任务队列.txt','开发管理/任务列表/自动化任务.txt');sourceBacklog='开发管理/任务列表/自动化任务.txt'}
  $body=@("# $Id · $Title",'','## 来源与当前边界','fixture','## 必查范围','fixture','## 实施范围','fixture','## 禁止项','fixture','## 验证','fixture','## 完成条件','fixture','## 停止条件','fixture') -join "`n"
  @('---TASK-META---',($meta|ConvertTo-Json -Depth 20),'---TASK-BODY---',$body) -join "`n"
}
function Read-Meta { param([string]$Path) $text=[IO.File]::ReadAllText($Path); [regex]::Match($text,'(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---').Groups['json'].Value|ConvertFrom-Json -Depth 30 }
function Get-ContextDigest {
  param([object]$Metadata)
  $context=[ordered]@{id=[string]$Metadata.id;title=[string]$Metadata.title;priority=[string]$Metadata.priority;route=[string]$Metadata.route;owner=[string]$Metadata.owner;domain=[string]$Metadata.domain;stage=[string]$Metadata.stage;blockedBy=@($Metadata.blockedBy|ForEach-Object{[string]$_});expectedPaths=@($Metadata.expectedPaths|ForEach-Object{[string]$_});sourceBacklog=[string]$Metadata.sourceBacklog}
  $json=$context|ConvertTo-Json -Compress -Depth 20
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($json))).ToLowerInvariant()
}
function Invoke-State { param([string]$Action,[string]$Task,[string]$Context,[int[]]$Allowed=@(0)) $output=@(& pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Action $Action -RepositoryRoot $root -TaskId $Task -ContextPath $Context 2>$null); Assert-True ($LASTEXITCODE -in $Allowed) "$Action exit code was invalid"; $output[0]|ConvertFrom-Json -Depth 30 }

$root=Join-Path ([IO.Path]::GetTempPath()) "tzg-decision-checkpoint-test-$([Guid]::NewGuid().ToString('N'))"
$scriptPath=Join-Path $PSScriptRoot 'set-task-automation-state.ps1'
try{
  [IO.Directory]::CreateDirectory((Join-Path $root 'tools'))|Out-Null
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'check-task-cards.ps1') -Destination (Join-Path $root 'tools\check-task-cards.ps1')
  & git -C $root init -q; & git -C $root config user.name 'Decision Test'; & git -C $root config user.email 'decision@example.invalid'
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-A.txt') (New-Card 'TASK-A' 'external_execute' 'deepseek' 'Checkpoint A')
  Write-Utf8 (Join-Path $root '开发管理\任务卡\TASK-B.txt') (New-Card 'TASK-B' 'codex_execute' 'codex' 'Block B')
  $queue=@('# 当前任务队列','','| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |','|---|---|---|---|---|---|---|---|','| TASK-A | external_execute | deepseek | P1 | automation | implementation | Checkpoint A | 开发管理/任务卡/TASK-A.txt |','| TASK-B | codex_execute | codex | P1 | automation | implementation | Block B | 开发管理/任务卡/TASK-B.txt |','') -join "`n"
  $backlog=@('# 自动化任务','','| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |','|---|---|---|---|---|---|---|','| TASK-A | P1 | deepseek | 已排队 | — | Checkpoint A | 开发管理/任务卡/TASK-A.txt |','| TASK-B | P1 | codex | 已排队 | — | Block B | 开发管理/任务卡/TASK-B.txt |','') -join "`n"
  Write-Utf8 (Join-Path $root '开发管理\当前任务队列.txt') $queue; Write-Utf8 (Join-Path $root '开发管理\任务列表\自动化任务.txt') $backlog
  & git -C $root add -A; & git -C $root commit -q -m 'test: seed'

  $meta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  $pause=[ordered]@{schemaVersion=1;taskId='TASK-A';sourceRunId='run-a';owner='deepseek';route='external_execute';decisionId='DEC-20260803-TESTA';question='选择哪一种？';options=@(@{key='A';label='甲'},@{key='B';label='乙'},@{key='C';label='丙'});recommendedOption='B';impactSummary='B 风险最低';plainSummary=@{situation='需要决定';impact='会影响实现';action='请选择 B'};checkpointCommit=('a'*40);baseCommit=('b'*40);branch='codex/automation/deepseek/run-a/candidate';changedPaths=@('result.txt');verified=@('test');unverified=@('none');residualRisk='none';taskContextDigest=(Get-ContextDigest $meta);createdAt='2026-08-03T00:00:00.0000000+00:00'}
  $pausePath=Join-Path $root 'pause.json'; Write-Utf8 $pausePath ($pause|ConvertTo-Json -Compress -Depth 20)
  $paused=Invoke-State 'PauseDecision' 'TASK-A' $pausePath
  Assert-Equal $paused.dispatchState 'pending_decision' 'Pause did not change state'
  $pausedMeta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  Assert-Equal $pausedMeta.automationCheckpoint.queueIndex 0 'Queue position was not captured'
  Assert-True (-not ([IO.File]::ReadAllText((Join-Path $root '开发管理\当前任务队列.txt')) -match 'TASK-A')) 'Paused task remained in queue'

  $reply=[ordered]@{schemaVersion=1;taskId='TASK-A';decisionId='DEC-20260803-TESTA';result='OPTION_ACCEPTED';replyKind='option';replyValue='B';source='feishu_card';evidenceHash=('c'*64)}
  $replyPath=Join-Path $root 'reply.json'; Write-Utf8 $replyPath ($reply|ConvertTo-Json -Compress)
  $resumed=Invoke-State 'ResumeReady' 'TASK-A' $replyPath
  Assert-Equal $resumed.dispatchState 'ready' 'Reply did not restore ready'
  $resumedMeta=Read-Meta (Join-Path $root '开发管理\任务卡\TASK-A.txt')
  Assert-Equal $resumedMeta.automationReply.replyValue 'B' 'Reply evidence was not bound'
  $queueText=[IO.File]::ReadAllText((Join-Path $root '开发管理\当前任务队列.txt'))
  Assert-True ($queueText.IndexOf('TASK-A',[StringComparison]::Ordinal) -lt $queueText.IndexOf('TASK-B',[StringComparison]::Ordinal)) 'Queue position was not restored'

  $block=[ordered]@{schemaVersion=1;taskId='TASK-B';detailCode='dependency_missing'}
  $blockPath=Join-Path $root 'block.json'; Write-Utf8 $blockPath ($block|ConvertTo-Json -Compress)
  $blocked=Invoke-State 'Block' 'TASK-B' $blockPath
  Assert-Equal $blocked.dispatchState 'blocked' 'Ordinary blocker did not transition'
  $invalidResume=Invoke-State 'ResumeReady' 'TASK-B' $replyPath @(1)
  Assert-Equal $invalidResume.status 'failed' 'Ordinary failure used checkpoint resume path'
  Write-Output 'test-hourly-decision-checkpoint: OK'
}finally{if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}}
