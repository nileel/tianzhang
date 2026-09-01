#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][ValidateSet('Canary', 'Candidate')][string]$Action,
  [ValidateSet('Execution', 'Review', 'QueueMaintenance')][string]$Route,
  [Parameter(Mandatory = $true)][string]$RepositoryRoot,
  [Parameter(Mandatory = $true)][string]$TaskId,
  [Parameter(Mandatory = $true)][string]$RunId,
  [Parameter(Mandatory = $true)][string]$Model,
  [string]$StateRoot = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller-runtime'),
  [string]$ResumeContextPath,
  [string]$PreflightResultPath,
  [ValidateRange(1, 86400)][int]$ResponsibilityTimeoutSeconds = 3000
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runtimePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$runnerPath = Join-Path $PSScriptRoot 'codex-cli-session.ps1'
$checkerPath = Join-Path $PSScriptRoot 'check-task-cards.ps1'
. (Join-Path $PSScriptRoot 'automation-commit-metadata.ps1')
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')

function Stop-Candidate { param([string]$Code) $e = [InvalidOperationException]::new($Code); $e.Data['DetailCode'] = $Code; throw $e }
function Normalize-FullPath { param([string]$Path) [IO.Path]::GetFullPath($Path).TrimEnd('\', '/') }
function Quote-Single { param([string]$Value) "'" + $Value.Replace("'", "''") + "'" }

$canaryProbePath = '.tzg-codex-canary-probe.txt'
$canaryResultText = '问题=候选提交合同需要真实核验；完成=canary 已通过正式 finalizer 创建提交'
$canaryImpactText = '影响=验证 Codex 候选提交元数据链路；边界=仅修改隔离 canary worktree'
$canaryVerifyText = '验证=提交元数据与终态字段一致；后续=由外层清理 canary worktree'
$canaryPlainText = '发生=自动化完成了一次隔离提交探针；影响=不会进入主分支；需要=无需处理'

function Invoke-GitText {
  param([string[]]$Arguments, [string]$DetailCode = 'codex_git_failed')
  $start = [Diagnostics.ProcessStartInfo]::new()
  $start.FileName = 'git'; $start.WorkingDirectory = $script:root; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
  $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
  $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false); $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  foreach ($argument in @('-C', $script:root) + $Arguments) { $start.ArgumentList.Add($argument) }
  $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
  if (-not $process.Start()) { Stop-Candidate $DetailCode }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync(); $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $code = $process.ExitCode; $process.Dispose()
  if ($code -ne 0) { Stop-Candidate $DetailCode }
  $stdout.TrimEnd()
}

function Invoke-JsonTool {
  param([string]$Path, [string[]]$Arguments, [string]$DetailCode)
  $output = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>$null)
  if ($LASTEXITCODE -ne 0 -or $output.Count -ne 1) { Stop-Candidate $DetailCode }
  try { $output[0] | ConvertFrom-Json -Depth 100 } catch { Stop-Candidate $DetailCode }
}

function Get-NormalizedTextDigest {
  param([string]$Path)
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path)).TrimStart([char]0xFEFF)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n").Replace("`r", "`n")))).ToLowerInvariant()
}

function Read-TaskMetadata {
  param([string]$RequestedTaskId = $TaskId)
  $path = Join-Path $script:root "开发管理/任务卡/$RequestedTaskId.txt"
  $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($path)).TrimStart([char]0xFEFF)
  $match = [regex]::Match($text, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
  if (-not $match.Success) { Stop-Candidate 'codex_task_invalid' }
  try { $metadata = $match.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100 } catch { Stop-Candidate 'codex_task_invalid' }
  [pscustomobject]@{ Metadata = $metadata; Digest = Get-NormalizedTextDigest -Path $path }
}

function Get-ChangedPaths {
  param([string]$Range, [switch]$Worktree)
  $arguments = if ($Worktree) { @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', 'HEAD') } else { @('-c', 'core.quotepath=false', 'diff', '--name-only', '--no-renames', $Range) }
  @((Invoke-GitText $arguments) -split '\r?\n' | Where-Object { $_ } | ForEach-Object { $_.Replace('\', '/') } | Sort-Object -Unique)
}

function Test-QueueMaintenancePath {
  param([string]$Path)
  $Path -match '^开发管理/(?:当前任务队列\.txt|任务卡/[^/]+\.txt|任务列表/[^/]+\.txt|设计-当前状态\.txt|设计-下一步建议\.txt|开发-下一步建议\.txt|自动工作流状态\.txt)$'
}

function New-TerminalSchema {
  if ($Action -ceq 'Canary') {
    return ([ordered]@{
      type = 'object'
      properties = [ordered]@{
        status = @{ type = 'string'; enum = @('verified') }; identity = @{ type = 'string' }; model = @{ type = 'string' }
        candidateCommit = @{ type = 'string'; pattern = '^[0-9a-f]{40,64}$' }
        result = @{ type = 'string' }; impact = @{ type = 'string' }; verify = @{ type = 'string' }; plain = @{ type = 'string' }
      }
      required = @('status', 'identity', 'model', 'candidateCommit', 'result', 'impact', 'verify', 'plain')
      additionalProperties = $false
    } | ConvertTo-Json -Compress -Depth 10)
  }
  $schema = [ordered]@{
    type = 'object'
    properties = [ordered]@{
      status = @{ type = 'string'; enum = @('completed', 'no_candidate', 'needs_decision', 'maintenance_decision', 'blocked', 'failed') }
      identity = @{ type = 'string' }; model = @{ type = 'string' }
      candidateCommit = @{ type = 'string' }
      expectedTransition = @{ type = 'string' }
      changedPaths = @{ type = 'array'; items = @{ type = 'string' } }
      verified = @{ type = 'array'; items = @{ type = 'string' } }
      unverified = @{ type = 'array'; items = @{ type = 'string' } }
      residualRisk = @{ type = 'string' }; result = @{ type = 'string' }; impact = @{ type = 'string' }; verify = @{ type = 'string' }; plain = @{ type = 'string' }
      decisionId = @{ type = 'string'; pattern = '^(?:|DEC-[0-9]{8}-[A-Z0-9]+)$'; description = 'Schema filler. Direct and maintenance decisions return an empty string; the deterministic wrapper owns the final decision ID.' }
      decisionTaskId = @{ type = 'string' }
      question = @{ type = 'string' }
      options = @{ type = 'array'; maxItems = 3; items = @{ type = 'object'; properties = @{ key = @{ type = 'string' }; label = @{ type = 'string' }; targetState = @{ type = 'string'; enum = @('ready', 'blocked') } }; required = @('key', 'label', 'targetState'); additionalProperties = $false } }
      recommendedOption = @{ type = 'string' }
      impactSummary = @{ type = 'string' }
      plainSummary = @{ type = 'object'; description = 'Schema filler for model output. The deterministic wrapper derives the direct-decision summary from validated semantic fields.'; properties = @{ situation = @{ type = 'string' }; impact = @{ type = 'string' }; action = @{ type = 'string' } }; required = @('situation', 'impact', 'action'); additionalProperties = $false }
      detailCode = @{ type = 'string' }
    }
    required = @()
    additionalProperties = $false
  }
  $schema.required = @($schema.properties.Keys)
  $schema | ConvertTo-Json -Compress -Depth 30
}

function Read-ResumeContext {
  if ([string]::IsNullOrWhiteSpace($ResumeContextPath)) { return $null }
  $state = Normalize-FullPath $StateRoot
  $path = Normalize-FullPath $ResumeContextPath
  if (-not $path.StartsWith($state + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Candidate 'codex_resume_context_invalid' }
  Assert-PrivatePathAcl -Path $path
  try { $context = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30 } catch { Stop-Candidate 'codex_resume_context_invalid' }
  $properties = @($context.PSObject.Properties.Name)
  foreach ($field in @('schemaVersion', 'kind', 'taskId', 'decisionId', 'replyKind', 'replyValue', 'source', 'evidenceHash')) {
    if ($properties -cnotcontains $field) { Stop-Candidate 'codex_resume_context_invalid' }
  }
  $kind = [string]$context.kind
  if (
    [string]$context.schemaVersion -cne '1' -or
    $kind -cnotin @('decision_checkpoint', 'queue_maintenance') -or
    [string]$context.taskId -cne $TaskId -or
    [string]::IsNullOrWhiteSpace([string]$context.decisionId) -or
    [string]::IsNullOrWhiteSpace([string]$context.replyKind) -or
    [string]::IsNullOrWhiteSpace([string]$context.replyValue) -or
    [string]::IsNullOrWhiteSpace([string]$context.source) -or
    [string]$context.evidenceHash -cnotmatch '^[0-9a-f]{64}$'
  ) { Stop-Candidate 'codex_resume_context_invalid' }
  if ($kind -ceq 'decision_checkpoint') {
    if ($Route -ceq 'QueueMaintenance' -or $properties -cnotcontains 'checkpointCommit' -or $properties -cnotcontains 'checkpointChangedPaths') { Stop-Candidate 'codex_resume_context_invalid' }
    $checkpointPaths = $context.checkpointChangedPaths
    if (
      [string]$context.checkpointCommit -cnotmatch '^[0-9a-f]{40,64}$' -or
      $checkpointPaths -is [string] -or
      $checkpointPaths -isnot [Collections.IEnumerable] -or
      @($checkpointPaths).Count -eq 0 -or
      @($checkpointPaths | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0
    ) { Stop-Candidate 'codex_resume_context_invalid' }
  } else {
    if ($Route -cne 'QueueMaintenance' -or $properties -cnotcontains 'decisionTaskId' -or [string]::IsNullOrWhiteSpace([string]$context.decisionTaskId) -or [string]$context.replyValue -cnotin @('A', 'B')) { Stop-Candidate 'codex_resume_context_invalid' }
  }
  $context
}

function Read-PreflightResult {
  param([string]$ExpectedTaskCardDigest)
  if ([string]::IsNullOrWhiteSpace($PreflightResultPath)) { Stop-Candidate 'codex_preflight_required' }
  $state = Normalize-FullPath $StateRoot
  $path = Normalize-FullPath $PreflightResultPath
  if (-not $path.StartsWith($state + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Candidate 'codex_preflight_invalid' }
  if ([IO.Path]::GetFileName($path) -cne "$RunId.json") { Stop-Candidate 'codex_preflight_invalid' }
  Assert-PrivatePathAcl -Path $path
  try { $result = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 50 } catch { Stop-Candidate 'codex_preflight_invalid' }
  foreach ($field in @('runId', 'taskId', 'taskCardDigest', 'indexDigest', 'matched', 'notice', 'mustRead', 'gates', 'gatePointers')) {
    if ($result.PSObject.Properties.Name -cnotcontains $field) { Stop-Candidate 'codex_preflight_invalid' }
  }
  if ([string]$result.runId -cne $RunId -or [string]$result.taskId -cne $TaskId -or [string]$result.taskCardDigest -cne $ExpectedTaskCardDigest) { Stop-Candidate 'codex_preflight_invalid' }
  $indexPath = Join-Path $script:root '开发管理/经验库/风险索引.json'
  if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf) -or [string]$result.indexDigest -cne (Get-NormalizedTextDigest $indexPath)) { Stop-Candidate 'codex_preflight_invalid' }
  $result
}

function New-Prompt {
  param([object]$Run, [AllowNull()][object]$ResumeContext, [string[]]$CandidatePaths, [AllowNull()][object]$PreflightResult)
  $routeInstruction = switch ($Route) {
    'Execution' { '按指定 codex_execute 任务实施。' }
    'Review' { "按审核入口复审指定 codex_review 任务。先以目标提交、任务卡完成条件和直接事实源形成通过／部分通过／不通过结论；不得修改被审业务语义来消除缺口或制造通过。结论通过时，只可更新任务生命周期、索引中的内容状态以及被审文件中明确存在的审核标记／审核记录，不得顺手补写规则或内容。结论为部分通过或不通过时，保留被审业务文件，把当前任务设为 blocked 并移出 ready 队列；必须在开发管理/未通过审核清单.txt 使用三级标题 '### $TaskId · <标题>'，并以 '- 审核对象：正式提交 ``<完整 SHA>``；结论：不通过。' 或 '- 审核对象：正式提交 ``<完整 SHA>``；结论：部分通过，仍需返工。' 记录本轮被复审提交；完整 SHA 必须在修改任务卡前通过 git log -1 --format=%H -- 开发管理/任务卡/$TaskId.txt 取得。不得使用短 SHA、二级任务标题或「复审对象」等替代表述。" }
    'QueueMaintenance' { '只做空队列维护，本轮不执行新增业务任务。先扫描各分线 backlog 中所有明确标为阻塞的任务；对阻塞描述中明确出现的稳定任务 ID，依次核对 开发管理/任务卡/<ID>.txt 与 开发管理/任务归档/<ID>.txt。backlog 中“阻塞”字样本身不能证明前置仍未完成：命名 blocker 在活跃任务卡和完成归档中都不存在时保持阻塞，不猜测完成状态；同一 ID 同时存在活跃任务卡与完成归档时保持阻塞，不提升下游任务。当本轮确认某个具名前置已完成并从直接下游卡移除该 ID，且本轮移除使该卡的 blockedBy 从非空变为空时，必须继续读取同一卡完整正文并收口这次状态事件：剩余动作若只是当前仓库与已批准事实即可完成的任务卡准备，例如实时路径扫描或字面量路径冻结，就在本轮完成准备并重新判断 runnable；唯一剩余条件若是负责人在两条可确定性形成完整 ready 卡的路线间选择，则按维护型决策终态处理；其他内容冻结、外部工作面、项目闸门、事实冲突或停止条件保持阻塞。不得顺带扫描其他原本就是 blockedBy=[] 的活跃卡，不得因准确的 stateReason 未变化而机械重写或制造维护提交。命名前置已完成但仍有其他真实条件时只在原说明失真时改写为实际剩余 blocker；全部前置已完成且现有事实足以形成完整任务卡时，同步 backlog、建立完整任务卡并按既有排序规则入队。新建 ready 卡或把非 ready 卡重新置为 ready 前，必须调用现有 tools/get-experience-risk-preflight.ps1；只有结果 status=ok，且结果中的每个 gates ID 都有同 ID、非空 instructionRef 的 gatePointers 时，才可在同一提交写入 schemaVersion=2，并让 riskPreflight.explicitRefs 精确等于调用时的 -ExplicitRefs、matched/gates 分别与结果逐项一致。预检脚本失败、status=preflight_overbroad、或任何 gate 缺少上述门禁指针时不得置为 ready，不得伪造零命中继续。完成全部阻塞项核对及上述直接受影响卡的收口后仍没有合法候选，才允许返回 no_candidate。' }
  }
  $resumeInstruction = if ($null -eq $ResumeContext) {
    '本轮没有 checkpoint 回复上下文。'
  } elseif ([string]$ResumeContext.kind -ceq 'queue_maintenance') {
    "已机械核验维护型决策回复：$($ResumeContext | ConvertTo-Json -Compress -Depth 10)。本轮只准备 decisionTaskId 对应任务卡，使所选 A/B 路线具备完整 expectedPaths、验证、完成条件和停止条件；保留 automationDecision 与 pending_decision，不入队、不执行业务内容。最终返回 completed，共享入口会在最新 master 上调用 ResolveMaintenanceDecision。"
  } else {
    "已机械核验并重放 checkpoint。负责人回复上下文：$($ResumeContext | ConvertTo-Json -Compress -Depth 10)。只把它用于对应 decisionId，不恢复旧模型会话。"
  }
  $pathText = if ($CandidatePaths.Count -gt 0) { $CandidatePaths -join '|' } else { '<ACTUAL_CHANGED_PATHS_FROM_ALLOWED_QUEUE_MAINTENANCE_SET>' }
  $preflightInstruction = if ($null -eq $PreflightResult) {
    '本轮无共享入口预检结果（queue maintenance）。'
  } else {
    $short = [ordered]@{
      matched = @($PreflightResult.matched | ForEach-Object { [string]$_ })
      notice = @($PreflightResult.notice | ForEach-Object { [string]$_ })
      mustRead = @($PreflightResult.mustRead | ForEach-Object { [ordered]@{ id = [string]$_.id; title = [string]$_.title; detailRef = [string]$_.detailRef; body = [string]$_.body } })
      gatePointers = @($PreflightResult.gatePointers | ForEach-Object { [ordered]@{ id = [string]$_.id; instructionRef = [string]$_.instructionRef } })
    }
    '共享入口已验证预检结果（绑定 runId/taskId/任务卡 digest/索引 digest）：' + ($short | ConvertTo-Json -Compress -Depth 10)
  }
  $finalizerCommand = "pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot $(Quote-Single $script:root) -ExpectedPaths $(Quote-Single $pathText) -CommitMessage $(Quote-Single "candidate($TaskId): Codex implementation") -RequireAutomationMetadata -AutomationTask $(Quote-Single $TaskId) -AutomationState 'completed' -AutomationResult '<RESULT>' -AutomationImpact '<IMPACT>' -AutomationVerify '<VERIFY>' -AutomationPlain '<PLAIN>'"
  @(
    '[TZG_CODEX_CANDIDATE]'
    "模型核验证明：外层已核验并传入 $Model；返回 model 必须精确等于该值。"
    "TaskId: $TaskId"; "RunId: $RunId"; "Route: $Route"; "RepositoryRoot: $script:root"; "CandidateBranch: $($Run.candidateBranch)"; "BaseCommit: $($Run.baseCommit)"
    $routeInstruction; $resumeInstruction
    $preflightInstruction
    '固定入口已经选择并 claim 本任务。不得重扫队列、领取其他任务、调用 runtime、集成、管理 automation 或修改其他 worktree。'
    '除 QueueMaintenance 外，不得从其他任务卡的 blockedBy 或 backlog 行的「阻塞于」投影移除当前 taskId，也不得顺带提升、重排或关闭下游任务。当前任务完成后，其他任务对它的具名前置引用继续保留；只有正式结果进入 master 后，后续 QueueMaintenance 才按既有事实源同时更新下游任务卡和 backlog 投影。'
    '只在当前 worktree 实施、验证并形成一个 candidate 提交；正式结果由共享入口在最新 master 重放。'
    '当前 worktree 的验证若要求 dotnet build --no-restore 且对应项目缺少 obj/project.assets.json，先在同一 worktree 对该项目执行一次 dotnet restore 作为环境准备，再执行任务卡规定的 --no-restore build；不得改用主工作区、其他 worktree 或其 obj/bin 缓存。'
    "CandidatePaths: $pathText"
    '提交完成后，必须原样执行以下 PowerShell 命令生成最终 changedPaths：'
    "`$changedPaths = @(git -c core.quotepath=false diff --name-only --no-renames '$($Run.baseCommit)..HEAD' | Where-Object { `$_ } | Sort-Object -Unique)"
    '该命令按删除/新增表示移动，因此移动源路径、移动目标路径、普通删除路径和普通新增路径都必须出现。最终 JSON 的 changedPaths 必须直接使用该数组。不得使用 git show --name-only、--find-renames 或任何会把移动压缩为单条目标路径的输出替代。'
    '正常完成时，先确定四个 PowerShell 参数的原始单行值；值中不得含单引号或控制字符。-AutomationResult 的值精确为 问题=<问题>；完成=<完成>，-AutomationImpact 的值精确为 影响=<影响>；边界=<边界>，-AutomationVerify 的值精确为 验证=<验证>；后续=<后续>，-AutomationPlain 的值精确为 发生=<发生>；影响=<影响>；需要=<需要>。result、impact、verify、plain 仅是最终 JSON 字段名，不是参数值的一部分；不得把 result=、impact=、verify=、plain= 写入对应参数值。然后把下面命令中的四个占位符替换为上述原始值并原样执行一次：'
    $finalizerCommand
    '不得用普通 git commit 代替，也不得省略 -RequireAutomationMetadata。最终 JSON 的 result/impact/verify/plain 必须与该提交的四个元数据值逐字一致。QueueMaintenance 只可把占位路径替换为本轮实际改动且符合既有允许集合的精确仓库相对路径。'
    '正常完成返回 status=completed、identity=Codex、完整 candidate SHA、精确 paths、验证数组、风险和九字段值。QueueMaintenance 无变化返回 no_candidate。'
    'QueueMaintenance 仅在本轮移除直接下游卡的最后一个具名前置、完整读卡后确认唯一剩余条件是负责人在两条可确定性形成 ready 卡的路线间选择时，返回 maintenance_decision。该候选提交只完成前置移除与准确阻塞事实，不写 automationDecision、不改 pending_decision、不入队；decisionTaskId 指向该下游卡，options 必须恰为 A/B/C 且 targetState 依次为 ready/ready/blocked，allowCustomReply 由共享入口固定为 false。decisionId 返回空字符串，由共享入口按事实摘要生成。'
    '开发中确需负责人决定时立即停止猜测，将当前合法修改整理为一个干净、唯一、直接后继 checkpoint 提交；返回 needs_decision、提交 SHA、精确 paths、验证/风险，以及 question、按 A/B/C 排序的三个非空 option label、recommendedOption 和 impactSummary。direct needs_decision 的 decisionId 返回空字符串，plainSummary 三字段返回空字符串；固定 wrapper 会从 run/checkpoint 身份与上述已校验语义字段确定性生成它们。checkpoint 不得改变任务生命周期。'
    '业务 blocker 且没有合法 checkpoint 时恢复工作树到本轮初始状态并返回 blocked/detailCode。技术失败同样先恢复工作树到本轮初始状态，再返回 failed/detailCode；普通失败不得伪装为 decision checkpoint。'
    '严格终态 schema 要求每个字段都出现。当前 status 不使用的字符串和数组填空字符串或空数组，plainSummary 填三个空字符串；固定 wrapper 只按实际 status 核验必需字段。'
    '除 QueueMaintenance 的 no_candidate 外，最终只输出符合 schema 的 JSON 对象。'
  ) -join "`n"
}

function New-CanaryPrompt {
  $finalizerCommand = "pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot $(Quote-Single $script:root) -ExpectedPaths $(Quote-Single $canaryProbePath) -CommitMessage 'canary: verify Codex candidate metadata contract' -RequireAutomationMetadata -AutomationTask $(Quote-Single $TaskId) -AutomationState 'completed' -AutomationResult $(Quote-Single $canaryResultText) -AutomationImpact $(Quote-Single $canaryImpactText) -AutomationVerify $(Quote-Single $canaryVerifyText) -AutomationPlain $(Quote-Single $canaryPlainText)"
  @(
    '[TZG_CODEX_CANARY]'
    "模型核验证明：外层已核验并传入 $Model；返回 model 必须精确等于该值。"
    '这是隔离 canary worktree。不得读取任务队列、领取业务任务、修改其他 worktree、调用 runtime 或管理 automation。'
    "只创建仓库根目录文件 $canaryProbePath，内容为 TZG_CODEX_CANDIDATE_METADATA_CANARY，然后原样执行以下唯一提交命令："
    $finalizerCommand
    "返回 status=verified、identity=Codex、model=$Model、完整 candidateCommit，并逐字返回以下四值："
    "result=$canaryResultText"
    "impact=$canaryImpactText"
    "verify=$canaryVerifyText"
    "plain=$canaryPlainText"
    '只输出符合 schema 的 JSON 对象。'
  ) -join "`n"
}

function Invoke-Runner {
  param([string]$Prompt)
  $directory = Join-Path (Normalize-FullPath $StateRoot) 'codex-structured-output'
  [IO.Directory]::CreateDirectory($directory) | Out-Null
  Set-PrivatePathAcl -Path $directory -Directory
  $schemaPath = Join-Path $directory "$RunId.schema.json"
  $outputPath = Join-Path $directory "$RunId.output.json"
  Remove-Item -LiteralPath $schemaPath, $outputPath -Force -ErrorAction SilentlyContinue
  [IO.File]::WriteAllText($schemaPath, (New-TerminalSchema), [Text.UTF8Encoding]::new($false))
  Set-PrivatePathAcl -Path $schemaPath
  try {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'pwsh'; $start.WorkingDirectory = $script:root; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true; $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $runnerPath, '-Action', 'Start', '-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-RunId', $RunId, '-Model', $Model, '-OutputSchemaPath', $schemaPath, '-OutputLastMessagePath', $outputPath)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    if (-not $process.Start()) { Stop-Candidate 'codex_runner_unavailable' }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync(); $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.Write($Prompt); $process.StandardInput.Close()
    $timedOut = -not $process.WaitForExit([int]($ResponsibilityTimeoutSeconds * 1000))
    if ($timedOut) { try { $process.Kill($true) } catch {}; $process.WaitForExit() }
    $stdout = $stdoutTask.GetAwaiter().GetResult(); $null = $stderrTask.GetAwaiter().GetResult(); $exitCode = if ($timedOut) { 124 } else { $process.ExitCode }; $process.Dispose()
    if ($timedOut) { Stop-Candidate 'codex_responsibility_timeout' }
    $lines = @($stdout -split '\r?\n' | Where-Object { $_ })
    if ($lines.Count -ne 1) { Stop-Candidate 'codex_runner_failed' }
    try { $runner = $lines[0] | ConvertFrom-Json -Depth 20 } catch { Stop-Candidate 'codex_runner_failed' }
    if ($exitCode -ne 0 -or [string]$runner.status -cne 'ok') {
      $runnerDetail = if ([string]$runner.detailCode -cmatch '^runner_[a-z_]+$') { "codex_$([string]$runner.detailCode)" } else { 'codex_runner_failed' }
      Stop-Candidate $runnerDetail
    }
    Set-PrivatePathAcl -Path $outputPath
    try { $terminal = [IO.File]::ReadAllText($outputPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 30 } catch { Stop-Candidate 'codex_terminal_invalid' }
    [pscustomobject]@{ Runner = $runner; Terminal = $terminal }
  } finally {
    Remove-Item -LiteralPath $schemaPath, $outputPath -Force -ErrorAction SilentlyContinue
  }
}

function Assert-StringArray { param([object]$Value) if ($Value -is [string] -or $Value -isnot [Collections.IEnumerable]) { Stop-Candidate 'codex_terminal_invalid' }; foreach ($item in @($Value)) { if ([string]::IsNullOrWhiteSpace([string]$item)) { Stop-Candidate 'codex_terminal_invalid' } } }

function Get-DirectDecisionId {
  param([object]$Run, [string]$CheckpointCommit)
  $startedAt = [DateTimeOffset]::MinValue
  if (
    [string]::IsNullOrWhiteSpace([string]$Run.runId) -or
    [string]::IsNullOrWhiteSpace($CheckpointCommit) -or
    -not [DateTimeOffset]::TryParse([string]$Run.startedAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$startedAt)
  ) { Stop-Candidate 'codex_decision_invalid' }
  $material = "$TaskId$([char]0)$([string]$Run.runId)$([char]0)$CheckpointCommit"
  $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.UTF8Encoding]::new($false).GetBytes($material)))
  "DEC-$($startedAt.ToString('yyyyMMdd', [Globalization.CultureInfo]::InvariantCulture))-CD$($hash.Substring(0, 12))"
}

function Assert-Decision {
  param([object]$Terminal, [string[]]$AllowedPaths, [object]$Run)
  foreach ($field in @('candidateCommit', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'decisionId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary')) { if ($Terminal.PSObject.Properties.Name -cnotcontains $field) { Stop-Candidate 'codex_terminal_invalid' } }
  $commit = [string]$Terminal.candidateCommit
  $baseCommit = [string]$Run.baseCommit
  if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $commit -or (Invoke-GitText @('rev-list', '--count', "$baseCommit..$commit")) -cne '1' -or (Invoke-GitText @('rev-parse', "$commit^")) -cne $baseCommit) { Stop-Candidate 'codex_checkpoint_invalid' }
  if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Candidate 'codex_checkpoint_dirty' }
  $actual = @(Get-ChangedPaths -Range "$baseCommit..$commit")
  $reported = @($Terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if ($actual.Count -eq 0 -or ($actual -join "`0") -cne ($reported -join "`0")) { Stop-Candidate 'codex_checkpoint_paths_invalid' }
  foreach ($path in $actual) { if ($AllowedPaths -cnotcontains $path) { Stop-Candidate 'codex_checkpoint_paths_invalid' } }
  $options = @($Terminal.options)
  if ($options.Count -ne 3 -or (@($options | ForEach-Object { [string]$_.key }) -join '') -cne 'ABC') { Stop-Candidate 'codex_decision_invalid' }
  foreach ($value in @($options | ForEach-Object { [string]$_.label }) + @([string]$Terminal.question, [string]$Terminal.impactSummary)) { if ([string]::IsNullOrWhiteSpace($value)) { Stop-Candidate 'codex_decision_invalid' } }
  $recommendedOption = [string]$Terminal.recommendedOption
  if ($recommendedOption -cnotin @('A', 'B', 'C')) { Stop-Candidate 'codex_decision_invalid' }
  $recommendedLabel = [string](@($options | Where-Object { [string]$_.key -ceq $recommendedOption })[0].label)
  $decisionId = Get-DirectDecisionId -Run $Run -CheckpointCommit $commit
  $plainSummary = [ordered]@{
    situation = [string]$Terminal.question
    impact = [string]$Terminal.impactSummary
    action = "建议选择 $recommendedOption：$recommendedLabel"
  }
  [ordered]@{
    category = 'decision_checkpoint'; decisionId = $decisionId; question = [string]$Terminal.question
    options = @($options | ForEach-Object { [ordered]@{ key = [string]$_.key; label = [string]$_.label } }); recommendedOption = $recommendedOption
    impactSummary = [string]$Terminal.impactSummary; plainSummary = $plainSummary
    checkpointCommit = $commit; baseCommit = $baseCommit; branch = (Invoke-GitText @('branch', '--show-current')); changedPaths = $actual
    verified = @($Terminal.verified | ForEach-Object { [string]$_ }); unverified = @($Terminal.unverified | ForEach-Object { [string]$_ }); residualRisk = [string]$Terminal.residualRisk
  }
}

function Assert-MaintenanceDecision {
  param([object]$Terminal, [object]$Run)
  if ($Route -cne 'QueueMaintenance' -or [string]::IsNullOrWhiteSpace([string]$Terminal.decisionTaskId)) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  $commit = [string]$Terminal.candidateCommit
  if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $commit -or (Invoke-GitText @('rev-list', '--count', "$($Run.baseCommit)..$commit")) -cne '1' -or (Invoke-GitText @('rev-parse', "$commit^")) -cne [string]$Run.baseCommit) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  $changed = @(Get-ChangedPaths -Range "$($Run.baseCommit)..$commit")
  $reported = @($Terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if ($changed.Count -eq 0 -or ($changed -join "`0") -cne ($reported -join "`0")) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  foreach ($path in $changed) { if (-not (Test-QueueMaintenancePath $path)) { Stop-Candidate 'codex_candidate_path_violation' } }
  $options = @($Terminal.options)
  if ($options.Count -ne 3 -or (@($options | ForEach-Object { [string]$_.key }) -join '') -cne 'ABC' -or (@($options | ForEach-Object { [string]$_.targetState }) -join ',') -cne 'ready,ready,blocked') { Stop-Candidate 'codex_maintenance_decision_invalid' }
  foreach ($value in @([string]$Terminal.question, [string]$Terminal.recommendedOption, [string]$Terminal.impactSummary, [string]$Terminal.plainSummary.situation, [string]$Terminal.plainSummary.impact, [string]$Terminal.plainSummary.action)) { if ([string]::IsNullOrWhiteSpace($value)) { Stop-Candidate 'codex_maintenance_decision_invalid' } }
  if ([string]$Terminal.recommendedOption -cnotin @('A', 'B')) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  $taskPath = Join-Path $script:root "开发管理\任务卡\$([string]$Terminal.decisionTaskId).txt"
  if (-not (Test-Path -LiteralPath $taskPath -PathType Leaf)) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  $task = Read-TaskMetadata -RequestedTaskId ([string]$Terminal.decisionTaskId)
  if ([string]$task.Metadata.dispatchState -cne 'blocked' -or @($task.Metadata.blockedBy).Count -ne 0 -or $task.Metadata.PSObject.Properties.Name -contains 'automationDecision' -or $task.Metadata.PSObject.Properties.Name -contains 'automationCheckpoint') { Stop-Candidate 'codex_maintenance_decision_invalid' }
  try {
    $beforeText = Invoke-GitText @('show', "$($Run.baseCommit):开发管理/任务卡/$([string]$Terminal.decisionTaskId).txt")
    $beforeMatch = [regex]::Match($beforeText, '(?ms)^---TASK-META---\r?\n(?<json>.*?)\r?\n---TASK-BODY---')
    $beforeMeta = $beforeMatch.Groups['json'].Value.Trim() | ConvertFrom-Json -Depth 100
  } catch { Stop-Candidate 'codex_maintenance_decision_invalid' }
  if (-not $beforeMatch.Success -or [string]$beforeMeta.dispatchState -cne 'blocked' -or @($beforeMeta.blockedBy).Count -eq 0) { Stop-Candidate 'codex_maintenance_decision_invalid' }
  try {
    $commitMessage = Invoke-GitText @('show', '-s', '--format=%B', $commit)
    $metadataContract = ConvertFrom-TzgAutomationCommitMessage -Message $commitMessage -ExpectedTask $TaskId -ExpectedState 'completed'
  } catch { Stop-Candidate 'codex_candidate_metadata_invalid' }
  foreach ($pair in @(@([string]$metadataContract.ResultText, [string]$Terminal.result), @([string]$metadataContract.ImpactText, [string]$Terminal.impact), @([string]$metadataContract.VerifyText, [string]$Terminal.verify), @([string]$metadataContract.PlainText, [string]$Terminal.plain))) {
    if ($pair[0] -cne $pair[1]) { Stop-Candidate 'codex_candidate_metadata_fields_invalid' }
  }
  [ordered]@{
    category = 'maintenance_decision'; expectedTransition = 'maintenance_pending_decision'; changedPaths = $changed
    verified = @([string]$metadataContract.Verification); unverified = @([string]$metadataContract.Next); residualRisk = [string]$metadataContract.Next
    result = [string]$metadataContract.ResultText; impact = [string]$metadataContract.ImpactText; verify = [string]$metadataContract.VerifyText; plain = [string]$metadataContract.PlainText
    decisionTaskId = [string]$Terminal.decisionTaskId; question = [string]$Terminal.question
    options = @($options | ForEach-Object { [ordered]@{ key = [string]$_.key; label = [string]$_.label; targetState = [string]$_.targetState } })
    recommendedOption = [string]$Terminal.recommendedOption; impactSummary = [string]$Terminal.impactSummary; plainSummary = $Terminal.plainSummary
  }
}

$result = $null
try {
  $script:root = Normalize-FullPath (Resolve-Path -LiteralPath $RepositoryRoot).Path
  if (-not (Test-Path -LiteralPath (Join-Path $script:root '.git'))) { Stop-Candidate 'codex_repository_invalid' }
  if ($Action -ceq 'Canary') {
    $beforeHead = Invoke-GitText @('rev-parse', 'HEAD'); $beforeStatus = Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')
    if (-not [string]::IsNullOrWhiteSpace($beforeStatus)) { Stop-Candidate 'codex_canary_modified_repository' }
    $runOutput = Invoke-Runner -Prompt (New-CanaryPrompt)
    $terminal = $runOutput.Terminal
    if ([string]$runOutput.Runner.status -cne 'ok' -or [string]$terminal.status -cne 'verified' -or [string]$terminal.identity -cne 'Codex' -or [string]$terminal.model -cne $Model) { Stop-Candidate 'codex_canary_identity_mismatch' }
    $head = Invoke-GitText @('rev-parse', 'HEAD')
    if (
      $head -cne [string]$terminal.candidateCommit -or
      (Invoke-GitText @('rev-list', '--count', "$beforeHead..$head")) -cne '1' -or
      (Invoke-GitText @('rev-parse', "$head^")) -cne $beforeHead -or
      -not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))
    ) { Stop-Candidate 'codex_canary_modified_repository' }
    $changed = @(Get-ChangedPaths -Range "$beforeHead..$head")
    if ($changed.Count -ne 1 -or $changed[0] -cne $canaryProbePath) { Stop-Candidate 'codex_canary_modified_repository' }
    try {
      $metadataContract = ConvertFrom-TzgAutomationCommitMessage -Message (Invoke-GitText @('show', '-s', '--format=%B', $head)) -ExpectedTask $TaskId -ExpectedState 'completed'
    } catch { Stop-Candidate 'codex_canary_metadata_invalid' }
    foreach ($pair in @(
        @([string]$metadataContract.ResultText, $canaryResultText, [string]$terminal.result),
        @([string]$metadataContract.ImpactText, $canaryImpactText, [string]$terminal.impact),
        @([string]$metadataContract.VerifyText, $canaryVerifyText, [string]$terminal.verify),
        @([string]$metadataContract.PlainText, $canaryPlainText, [string]$terminal.plain)
      )) { if ($pair[0] -cne $pair[1] -or $pair[0] -cne $pair[2]) { Stop-Candidate 'codex_canary_metadata_invalid' } }
    $result = [ordered]@{ status = 'verified'; identity = 'Codex'; model = $Model; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = $head; pwshMajor = $PSVersionTable.PSVersion.Major; git = 'available' }
  } else {
    if ([string]::IsNullOrWhiteSpace($Route)) { Stop-Candidate 'codex_route_invalid' }
    $shown = Invoke-JsonTool -Path $runtimePath -Arguments @('-Action', 'Show', '-StateRoot', $StateRoot) -DetailCode 'codex_runtime_mismatch'
    $run = $shown.state.runs.codex
    $expectedRoute = switch ($Route) { 'Execution' { 'codex_execute' }; 'Review' { 'codex_review' }; 'QueueMaintenance' { 'queue_maintenance' } }
    if ([string]$shown.status -cne 'OK' -or $null -eq $run -or [string]$run.runId -cne $RunId -or [string]$run.taskId -cne $TaskId -or [string]$run.route -cne $expectedRoute -or [string]$run.state -cne 'developing' -or (Normalize-FullPath ([string]$run.worktree)) -cne $script:root) { Stop-Candidate 'codex_runtime_mismatch' }
    if ((Invoke-GitText @('branch', '--show-current')) -cne [string]$run.candidateBranch -or (Invoke-GitText @('rev-parse', 'HEAD')) -cne [string]$run.baseCommit) { Stop-Candidate 'codex_worktree_mismatch' }
    $resume = Read-ResumeContext
    $initialPaths = @(Get-ChangedPaths -Worktree)
    if ($null -eq $resume -and $initialPaths.Count -ne 0) { Stop-Candidate 'codex_worktree_dirty' }
    if ($null -ne $resume -and [string]$resume.kind -ceq 'decision_checkpoint') {
      $expectedInitial = @($resume.checkpointChangedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
      if (($initialPaths -join "`0") -cne ($expectedInitial -join "`0")) { Stop-Candidate 'codex_resume_context_invalid' }
    } elseif ($null -ne $resume -and $initialPaths.Count -ne 0) {
      Stop-Candidate 'codex_resume_context_invalid'
    }
    $expectedPaths = @()
    if ($Route -cne 'QueueMaintenance') {
      $task = Read-TaskMetadata
      if ([string]$task.Digest -cne [string]$run.taskCardDigest) { Stop-Candidate 'codex_task_changed' }
      $metadata = $task.Metadata
      if ([string]$metadata.route -cne $expectedRoute -or [string]$metadata.owner -cne 'codex' -or [string]$metadata.dispatchState -cne 'ready') { Stop-Candidate 'codex_task_not_ready' }
      $expectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
    }
    $preflightResult = $null
    if ($Route -cne 'QueueMaintenance') {
      $preflightResult = Read-PreflightResult -ExpectedTaskCardDigest ([string]$run.taskCardDigest)
    }
    $runOutput = Invoke-Runner -Prompt (New-Prompt -Run $run -ResumeContext $resume -CandidatePaths $expectedPaths -PreflightResult $preflightResult)
    $terminal = $runOutput.Terminal
    if ([string]$runOutput.Runner.status -cne 'ok' -or [string]$terminal.identity -cne 'Codex' -or [string]$terminal.model -cne $Model) { Stop-Candidate 'codex_runner_failed' }
    $head = Invoke-GitText @('rev-parse', 'HEAD'); $status = Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')
    switch ([string]$terminal.status) {
      'completed' {
        if (-not [string]::IsNullOrWhiteSpace($status) -or $head -cne [string]$terminal.candidateCommit -or (Invoke-GitText @('rev-list', '--count', "$($run.baseCommit)..$head")) -cne '1' -or (Invoke-GitText @('rev-parse', "$head^")) -cne [string]$run.baseCommit) { Stop-Candidate 'codex_candidate_invalid' }
        try {
          $commitMessage = Invoke-GitText -Arguments @('show', '-s', '--format=%B', $head)
          $metadataContract = ConvertFrom-TzgAutomationCommitMessage -Message $commitMessage -ExpectedTask $TaskId -ExpectedState 'completed'
        } catch {
          $metadataDetail = switch -Exact ($_.Exception.Message) {
            'Automation commit metadata format is invalid.' { 'codex_candidate_metadata_format_invalid' }
            'Automation commit metadata identity is invalid.' { 'codex_candidate_metadata_identity_invalid' }
            'Automation commit metadata fields are invalid.' { 'codex_candidate_metadata_fields_invalid' }
            default { 'codex_candidate_metadata_invalid' }
          }
          Stop-Candidate $metadataDetail
        }
        $changed = @(Get-ChangedPaths -Range "$($run.baseCommit)..$head")
        $reported = @($terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
        if (($changed -join "`0") -cne ($reported -join "`0")) { Stop-Candidate 'codex_candidate_path_mismatch' }
        foreach ($pair in @(
            @([string]$metadataContract.ResultText, [string]$terminal.result),
            @([string]$metadataContract.ImpactText, [string]$terminal.impact),
            @([string]$metadataContract.VerifyText, [string]$terminal.verify),
            @([string]$metadataContract.PlainText, [string]$terminal.plain)
          )) { if ($pair[0] -cne $pair[1]) { Stop-Candidate 'codex_candidate_metadata_fields_invalid' } }
        if ($Route -ceq 'QueueMaintenance') { foreach ($path in $changed) { if (-not (Test-QueueMaintenancePath $path)) { Stop-Candidate 'codex_candidate_path_violation' } } } else { foreach ($path in $changed) { if ($expectedPaths -cnotcontains $path) { Stop-Candidate 'codex_candidate_path_violation' } } }
        $transition = if ($null -ne $resume -and [string]$resume.kind -ceq 'queue_maintenance') { 'maintenance_resolution' } elseif ($Route -ceq 'QueueMaintenance') { $e = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-Postcondition', 'QueueMaintenanceReadySchema2Guard', '-BaseCommit', [string]$run.baseCommit, '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'; "queue_ready_count=$([int]$e.readyCount)" } else { $e = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'; [string]$e.taskState }
        $candidateResult = [ordered]@{ category = 'completed'; expectedTransition = $transition; changedPaths = $changed; verified = @([string]$metadataContract.Verification); unverified = @([string]$metadataContract.Next); residualRisk = [string]$metadataContract.Next; result = [string]$metadataContract.ResultText; impact = [string]$metadataContract.ImpactText; verify = [string]$metadataContract.VerifyText; plain = [string]$metadataContract.PlainText }
        if ($null -ne $resume -and [string]$resume.kind -ceq 'queue_maintenance') { $candidateResult['maintenanceResolution'] = $resume }
        $result = [ordered]@{ status = 'completed'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = $head; candidateResult = $candidateResult }
      }
      'no_candidate' {
        if ($Route -cne 'QueueMaintenance' -or $head -cne [string]$run.baseCommit -or -not [string]::IsNullOrWhiteSpace($status)) { Stop-Candidate 'codex_no_candidate_invalid' }
        $result = [ordered]@{ status = 'no_candidate'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; detailCode = 'no_runnable_candidate' }
      }
      'needs_decision' {
        $reportedPaths = @($terminal.changedPaths | ForEach-Object { [string]$_ })
        if (
          $head -ceq [string]$run.baseCommit -and
          [string]::IsNullOrWhiteSpace($status) -and
          [string]::IsNullOrWhiteSpace([string]$terminal.candidateCommit) -and
          $reportedPaths.Count -eq 0 -and
          -not [string]::IsNullOrWhiteSpace([string]$terminal.detailCode)
        ) {
          $result = [ordered]@{ status = 'blocked'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; detailCode = [string]$terminal.detailCode }
        } else {
          $decision = Assert-Decision -Terminal $terminal -AllowedPaths $expectedPaths -Run $run
          $result = [ordered]@{ status = 'needs_decision'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = [string]$decision.checkpointCommit; candidateResult = $decision }
        }
      }
      'maintenance_decision' {
        $decision = Assert-MaintenanceDecision -Terminal $terminal -Run $run
        $result = [ordered]@{ status = 'maintenance_decision'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = $head; candidateResult = $decision }
      }
      { $_ -cin @('blocked', 'failed') } {
        if ($head -cne [string]$run.baseCommit) { Stop-Candidate 'codex_failed_head_changed' }
        if (-not [string]::IsNullOrWhiteSpace($status)) { Stop-Candidate 'codex_failed_dirty_worktree' }
        if ([string]::IsNullOrWhiteSpace([string]$terminal.detailCode)) { Stop-Candidate 'codex_terminal_invalid' }
        $result = [ordered]@{ status = [string]$terminal.status; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; detailCode = [string]$terminal.detailCode }
      }
      default { Stop-Candidate 'codex_terminal_invalid' }
    }
  }
} catch {
  $detail = if ($_.Exception.Data.Contains('DetailCode')) { [string]$_.Exception.Data['DetailCode'] } else { 'codex_candidate_wrapper_error' }
  $result = [ordered]@{ status = 'failed'; taskId = $TaskId; runId = $RunId; detailCode = $detail }
}

[Console]::Out.WriteLine(($result | ConvertTo-Json -Compress -Depth 40))
