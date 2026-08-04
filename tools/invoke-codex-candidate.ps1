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
  $path = Join-Path $script:root "开发管理/任务卡/$TaskId.txt"
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
      status = @{ type = 'string'; enum = @('completed', 'no_candidate', 'needs_decision', 'blocked', 'failed') }
      identity = @{ type = 'string' }; model = @{ type = 'string' }
      candidateCommit = @{ type = 'string' }
      expectedTransition = @{ type = 'string' }
      changedPaths = @{ type = 'array'; items = @{ type = 'string' } }
      verified = @{ type = 'array'; items = @{ type = 'string' } }
      unverified = @{ type = 'array'; items = @{ type = 'string' } }
      residualRisk = @{ type = 'string' }; result = @{ type = 'string' }; impact = @{ type = 'string' }; verify = @{ type = 'string' }; plain = @{ type = 'string' }
      decisionId = @{ type = 'string'; pattern = '^DEC-[0-9]{8}-[A-Z0-9]+$' }
      question = @{ type = 'string' }
      options = @{ type = 'array'; maxItems = 3; items = @{ type = 'object'; properties = @{ key = @{ type = 'string' }; label = @{ type = 'string' } }; required = @('key', 'label'); additionalProperties = $false } }
      recommendedOption = @{ type = 'string' }
      impactSummary = @{ type = 'string' }
      plainSummary = @{ type = 'object'; properties = @{ situation = @{ type = 'string' }; impact = @{ type = 'string' }; action = @{ type = 'string' } }; required = @('situation', 'impact', 'action'); additionalProperties = $false }
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
  if ([string]$context.taskId -cne $TaskId -or [string]::IsNullOrWhiteSpace([string]$context.decisionId) -or [string]::IsNullOrWhiteSpace([string]$context.replyValue)) { Stop-Candidate 'codex_resume_context_invalid' }
  $context
}

function New-Prompt {
  param([object]$Run, [AllowNull()][object]$ResumeContext, [string[]]$CandidatePaths)
  $routeInstruction = switch ($Route) {
    'Execution' { '按指定 codex_execute 任务实施。' }
    'Review' { "按审核入口复审指定 codex_review 任务。结论为不通过或部分通过且需返工时，必须在开发管理/未通过审核清单.txt 使用三级标题 '### $TaskId · <标题>'，并以 '- 审核对象：正式提交 ``<完整 SHA>``；结论：不通过。' 或 '- 审核对象：正式提交 ``<完整 SHA>``；结论：部分通过，仍需返工。' 记录本轮被复审提交；完整 SHA 必须在修改任务卡前通过 git log -1 --format=%H -- 开发管理/任务卡/$TaskId.txt 取得。不得使用短 SHA、二级任务标题或「复审对象」等替代表述。" }
    'QueueMaintenance' { '只做空队列维护；本轮不执行新增业务任务。' }
  }
  $resumeInstruction = if ($null -eq $ResumeContext) {
    '本轮没有 checkpoint 回复上下文。'
  } else {
    "已机械核验并重放 checkpoint。负责人回复上下文：$($ResumeContext | ConvertTo-Json -Compress -Depth 10)。只把它用于对应 decisionId，不恢复旧模型会话。"
  }
  $pathText = if ($CandidatePaths.Count -gt 0) { $CandidatePaths -join '|' } else { '<ACTUAL_CHANGED_PATHS_FROM_ALLOWED_QUEUE_MAINTENANCE_SET>' }
  $finalizerCommand = "pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -RepositoryRoot $(Quote-Single $script:root) -ExpectedPaths $(Quote-Single $pathText) -CommitMessage $(Quote-Single "candidate($TaskId): Codex implementation") -RequireAutomationMetadata -AutomationTask $(Quote-Single $TaskId) -AutomationState 'completed' -AutomationResult '<RESULT>' -AutomationImpact '<IMPACT>' -AutomationVerify '<VERIFY>' -AutomationPlain '<PLAIN>'"
  @(
    '[TZG_CODEX_CANDIDATE]'
    "模型核验证明：外层已核验并传入 $Model；返回 model 必须精确等于该值。"
    "TaskId: $TaskId"; "RunId: $RunId"; "Route: $Route"; "RepositoryRoot: $script:root"; "CandidateBranch: $($Run.candidateBranch)"
    $routeInstruction; $resumeInstruction
    '固定入口已经选择并 claim 本任务。不得重扫队列、领取其他任务、调用 runtime、集成、管理 automation 或修改其他 worktree。'
    '只在当前 worktree 实施、验证并形成一个 candidate 提交；正式结果由共享入口在最新 master 重放。'
    "CandidatePaths: $pathText"
    '正常完成时，先确定 result/impact/verify/plain 四个单行值；值中不得含单引号或控制字符。然后把下面命令中的四个占位符替换为这些值并原样执行一次：'
    $finalizerCommand
    '不得用普通 git commit 代替，也不得省略 -RequireAutomationMetadata。最终 JSON 的 result/impact/verify/plain 必须与该提交的四个元数据值逐字一致。QueueMaintenance 只可把占位路径替换为本轮实际改动且符合既有允许集合的精确仓库相对路径。'
    '正常完成返回 status=completed、identity=Codex、完整 candidate SHA、精确 paths、验证数组、风险和九字段值。QueueMaintenance 无变化返回 no_candidate。'
    '开发中确需负责人决定时立即停止猜测，将当前合法修改整理为一个干净、唯一、直接后继 checkpoint 提交；返回 needs_decision、提交 SHA、精确 paths、验证/风险，以及完整三选一决策卡字段。checkpoint 不得改变任务生命周期。'
    '业务 blocker 且没有合法 checkpoint 时恢复工作树到本轮初始状态并返回 blocked/detailCode。技术失败返回 failed/detailCode；普通失败不得伪装为 decision checkpoint。'
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

function Assert-Decision {
  param([object]$Terminal, [string[]]$AllowedPaths, [string]$BaseCommit)
  foreach ($field in @('candidateCommit', 'changedPaths', 'verified', 'unverified', 'residualRisk', 'decisionId', 'question', 'options', 'recommendedOption', 'impactSummary', 'plainSummary')) { if ($Terminal.PSObject.Properties.Name -cnotcontains $field) { Stop-Candidate 'codex_terminal_invalid' } }
  $commit = [string]$Terminal.candidateCommit
  if ((Invoke-GitText @('rev-parse', 'HEAD')) -cne $commit -or (Invoke-GitText @('rev-list', '--count', "$BaseCommit..$commit")) -cne '1' -or (Invoke-GitText @('rev-parse', "$commit^")) -cne $BaseCommit) { Stop-Candidate 'codex_checkpoint_invalid' }
  if (-not [string]::IsNullOrWhiteSpace((Invoke-GitText @('status', '--porcelain=v1', '--untracked-files=all')))) { Stop-Candidate 'codex_checkpoint_dirty' }
  $actual = @(Get-ChangedPaths -Range "$BaseCommit..$commit")
  $reported = @($Terminal.changedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  if ($actual.Count -eq 0 -or ($actual -join "`0") -cne ($reported -join "`0")) { Stop-Candidate 'codex_checkpoint_paths_invalid' }
  foreach ($path in $actual) { if ($AllowedPaths -cnotcontains $path) { Stop-Candidate 'codex_checkpoint_paths_invalid' } }
  $options = @($Terminal.options)
  if ($options.Count -ne 3 -or (@($options | ForEach-Object { [string]$_.key }) -join '') -cne 'ABC') { Stop-Candidate 'codex_decision_invalid' }
  foreach ($value in @([string]$Terminal.decisionId, [string]$Terminal.question, [string]$Terminal.recommendedOption, [string]$Terminal.impactSummary, [string]$Terminal.plainSummary.situation, [string]$Terminal.plainSummary.impact, [string]$Terminal.plainSummary.action)) { if ([string]::IsNullOrWhiteSpace($value)) { Stop-Candidate 'codex_decision_invalid' } }
  if ([string]$Terminal.decisionId -cnotmatch '^DEC-[0-9]{8}-[A-Z0-9]+$') { Stop-Candidate 'codex_decision_invalid' }
  [ordered]@{
    category = 'decision_checkpoint'; decisionId = [string]$Terminal.decisionId; question = [string]$Terminal.question
    options = @($options | ForEach-Object { [ordered]@{ key = [string]$_.key; label = [string]$_.label } }); recommendedOption = [string]$Terminal.recommendedOption
    impactSummary = [string]$Terminal.impactSummary; plainSummary = $Terminal.plainSummary
    checkpointCommit = $commit; baseCommit = $BaseCommit; branch = (Invoke-GitText @('branch', '--show-current')); changedPaths = $actual
    verified = @($Terminal.verified | ForEach-Object { [string]$_ }); unverified = @($Terminal.unverified | ForEach-Object { [string]$_ }); residualRisk = [string]$Terminal.residualRisk
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
    if ($null -ne $resume) {
      $expectedInitial = @($resume.checkpointChangedPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
      if (($initialPaths -join "`0") -cne ($expectedInitial -join "`0")) { Stop-Candidate 'codex_resume_context_invalid' }
    }
    $expectedPaths = @()
    if ($Route -cne 'QueueMaintenance') {
      $task = Read-TaskMetadata
      if ([string]$task.Digest -cne [string]$run.taskCardDigest) { Stop-Candidate 'codex_task_changed' }
      $metadata = $task.Metadata
      if ([string]$metadata.route -cne $expectedRoute -or [string]$metadata.owner -cne 'codex' -or [string]$metadata.dispatchState -cne 'ready') { Stop-Candidate 'codex_task_not_ready' }
      $expectedPaths = @($metadata.expectedPaths | ForEach-Object { [string]$_ })
    }
    $runOutput = Invoke-Runner -Prompt (New-Prompt -Run $run -ResumeContext $resume -CandidatePaths $expectedPaths)
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
        $transition = if ($Route -ceq 'QueueMaintenance') { $e = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'; "queue_ready_count=$([int]$e.readyCount)" } else { $e = Invoke-JsonTool -Path $checkerPath -Arguments @('-RepositoryRoot', $script:root, '-TaskId', $TaskId, '-Postcondition', 'CodexClosedOrNonReady', '-OutputJson') -DetailCode 'codex_candidate_postcondition_failed'; [string]$e.taskState }
        $candidateResult = [ordered]@{ category = 'completed'; expectedTransition = $transition; changedPaths = $changed; verified = @([string]$metadataContract.Verification); unverified = @([string]$metadataContract.Next); residualRisk = [string]$metadataContract.Next; result = [string]$metadataContract.ResultText; impact = [string]$metadataContract.ImpactText; verify = [string]$metadataContract.VerifyText; plain = [string]$metadataContract.PlainText }
        $result = [ordered]@{ status = 'completed'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = $head; candidateResult = $candidateResult }
      }
      'no_candidate' {
        if ($Route -cne 'QueueMaintenance' -or $head -cne [string]$run.baseCommit -or -not [string]::IsNullOrWhiteSpace($status)) { Stop-Candidate 'codex_no_candidate_invalid' }
        $result = [ordered]@{ status = 'no_candidate'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; detailCode = 'no_runnable_candidate' }
      }
      'needs_decision' {
        $decision = Assert-Decision -Terminal $terminal -AllowedPaths $expectedPaths -BaseCommit ([string]$run.baseCommit)
        $result = [ordered]@{ status = 'needs_decision'; taskId = $TaskId; runId = $RunId; sessionId = [string]$runOutput.Runner.sessionId; candidateCommit = [string]$decision.checkpointCommit; candidateResult = $decision }
      }
      { $_ -cin @('blocked', 'failed') } {
        if ($head -cne [string]$run.baseCommit -or -not [string]::IsNullOrWhiteSpace($status) -or [string]::IsNullOrWhiteSpace([string]$terminal.detailCode)) { Stop-Candidate 'codex_terminal_invalid' }
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
