#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Assert-True {
  param(
    [Parameter(Mandatory = $true)]
    [bool]$Condition,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Write-Utf8File {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$Content
  )

  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function ConvertTo-TomlString {
  param([Parameter(Mandatory = $true)][string]$Value)

  $Value | ConvertTo-Json -Compress
}

function Write-Automation {
  param(
    [Parameter(Mandatory = $true)]
    [string]$AutomationRoot,
    [Parameter(Mandatory = $true)]
    [string]$Id,
    [Parameter(Mandatory = $true)]
    [ValidateSet('ACTIVE', 'PAUSED')]
    [string]$Status,
    [Parameter(Mandatory = $true)]
    [string]$Prompt
  )

  $path = Join-Path $AutomationRoot "$Id\automation.toml"
  Write-Utf8File -Path $path -Content @"
version = 1
id = "$Id"
name = "$Id"
prompt = $(ConvertTo-TomlString -Value $Prompt)
status = "$Status"
"@
}

function Invoke-Checker {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string]$AutomationRoot,
    [switch]$RequireActive,
    [switch]$RequireLegacyRetired
  )

  $arguments = @(
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    (Join-Path $PSScriptRoot 'check-automation-workflow.ps1'),
    '-RepositoryRoot',
    $RepositoryRoot,
    '-AutomationRoot',
    $AutomationRoot
  )
  if ($RequireActive) {
    $arguments += '-RequireActive'
  }
  if ($RequireLegacyRetired) {
    $arguments += '-RequireLegacyRetired'
  }

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in $arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw 'Unable to start workflow checker'
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $result = [pscustomobject]@{
    ExitCode = $process.ExitCode
    Stdout = $stdoutTask.GetAwaiter().GetResult()
    Stderr = $stderrTask.GetAwaiter().GetResult()
  }
  $process.Dispose()
  $result
}

function Assert-CheckerPasses {
  param(
    [Parameter(Mandatory = $true)]
    [object]$Result,
    [Parameter(Mandatory = $true)]
    [string]$Context
  )

  Assert-True -Condition ($Result.ExitCode -eq 0) -Message "$Context failed: $($Result.Stderr)$($Result.Stdout)"
  Assert-True -Condition ($Result.Stdout.TrimEnd().EndsWith('check-automation-workflow: OK', [StringComparison]::Ordinal)) -Message "$Context did not emit OK"
}

function Assert-CheckerFails {
  param(
    [Parameter(Mandatory = $true)]
    [object]$Result,
    [Parameter(Mandatory = $true)]
    [string]$Context,
    [string]$ErrorContains
  )

  Assert-True -Condition ($Result.ExitCode -ne 0) -Message "$Context unexpectedly passed"
  if (-not [string]::IsNullOrWhiteSpace($ErrorContains)) {
    Assert-True `
      -Condition ($Result.Stderr.Contains($ErrorContains, [StringComparison]::OrdinalIgnoreCase)) `
      -Message "$Context did not report '$ErrorContains': $($Result.Stderr)"
  }
}

$testId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$testRoot = Join-Path $temporaryBase "tzg-workflow-checker-test-$testId"
$repositoryRoot = Join-Path $testRoot 'repo'
$automationRoot = Join-Path $testRoot 'automations'

$canonicalPrompt = @'
# 每小时自动工作流薄路由

读取 `开发管理/自动工作流规则.txt`、`开发管理/当前任务队列.txt`、`开发管理/审核入口.txt`、`开发管理/AI合作沟通.txt` 与必要状态。
每轮先通过 `tools/hourly-automation-lease.ps1` 调用 `Show`。pauseRequested=true 表示工具级逻辑暂停；立即输出 `suspended` 并结束，不扫描候选、不取得租约、不启动责任方。
未逻辑暂停时检查原任务恢复和已回复的 pending resume。
汇总 Codex 执行、Codex 复审、外部 AI 三类合法候选并统一排序。
三类均无合法候选时才按 `开发管理/状态与建议维护规则.txt` 补充队列，本轮不执行新任务。
队列维护正式入口必须保留两个顺序分支；没有可提升的完整 backlog 任务卡不等于阻塞，应继续从权威来源新增完整任务卡。
取得租约后每轮只启动一个责任方，路由到纯 `1`、纯 `2`、`开发管理/DeepSeek工作提示词.txt` 或队列维护。
普通 Codex 执行、复审和队列维护不得使用 Desktop/VS Code rollout；取得租约后只通过 `tools/codex-cli-session.ps1` 的 `Start`，把完整正式入口提示经 stdin 传入。
工具等待超时、yield 或尚未返回不等于 runner 失败；不得释放租约或启动第二写入者。
调度器输出 `selected`；runner 只输出 `session_started`、`running`；调度器根据既有 runtime 和退出状态输出 `waiting_decision`、`completed` 或 `failed`。
等待决定时保存 CLI session ID 并退出，回复后通过 runner `Resume` 同一 ID；旧终端结束后不回填后台结果。
现有 task-owned Desktop recovery 只允许原任务人工完成，普通自动化收到 `RECOVERY_ONLY` 后停止。
第一期不新增飞书 Tasks、task GUID 映射、进度数据库或阶段状态机。
外部 AI 自验证并创建 businessCommit 与 handoffCommit，调度器不代提交。
决策等待保存原 thread/session 后退出；占锁回复只排队，不 sleep、不轮询、不保持模型进程，不消耗等待 token。
记录结果并释放租约；相同全阻塞指纹连续两次使 pauseRequested=true 后，只报告“runtime 已逻辑暂停，界面尚未同步”并结束。
逻辑暂停期间普通 `Acquire` 返回 `SUSPENDED`；只有自动化任务之外的外部普通管理上下文可确认安全后先调用 `ClearBlocking`，再把入口设为 `ACTIVE`。
自动化任务不得调用自动化管理能力管理自身，也不得读取或更新自身配置、等待管理服务。
界面 `PAUSED` 只由外部普通管理上下文同步；未确认时只报告“runtime 已逻辑暂停，界面尚未同步”。
'@

$canonicalRules = @'
# 自动工作流规则

- 单写入租约：任何项目写入前必须取得租约。
- 候选资格：状态、依赖、决策、主责、范围和执行器均合法。
- 统一排序：项目优先级、已回复续跑、下游解锁、等待时间、稳定 ID。
- 四种路由责任：纯 1、纯 2、外部 AI、队列维护各自端到端完成。
- CLI-native Codex：普通执行、复审和队列维护只经 `tools/codex-cli-session.ps1` `Start`，完整入口只走 stdin；决定回复只 `Resume` 同一 ID。
- 有限进度：只投影 selected、session_started、running、waiting_decision、completed、failed，不新增状态字段。
- Desktop 恢复隔离：现有 task-owned Desktop recovery 只允许原任务人工完成，普通调度收到 RECOVERY_ONLY 后停止。
- 第一期不新增飞书 Tasks、task GUID 映射、进度数据库或阶段状态机。
- 人工脏改避让：冲突候选跳过，不 stash、reset、checkout 或 clean。
- 外部两提交：businessCommit 后只改交接文件创建 handoffCommit，外层不代验代提交。
- 决策恢复：保存原 thread/session；占锁排队且不等待 token。
- 两轮阻塞暂停：相同全阻塞指纹连续两次后形成工具级逻辑暂停，普通 Acquire 返回 SUSPENDED。
- 自管理边界：自动化任务不得调用自动化管理能力管理自身；只报告 runtime 已逻辑暂停，界面尚未同步。
- 外部同步与恢复：界面 PAUSED 只由外部普通管理上下文同步；确认无 lease、recovery 和 pending resume 后先 ClearBlocking，再设为 ACTIVE。
- 队列补充顺序：三类无候选时先提升 backlog，否则新增最小任务，本轮不执行。
- 私有状态边界：只保存租约、恢复、待续跑、阻塞计数和最后结果，不保存 secret。
- 回滚方式：失败时保持 PAUSED、保留提交和证据，不自动 reset、revert 或 clean。
'@

$canonicalStatus = @'
# 自动工作流状态

- 生产入口仍为 PAUSED。
- N-GROUP-01 已完成并归档。
- runtime 的 lease、recovery 与 pending resume 均为空，pauseRequested=true。
- 普通 Acquire 返回 SUSPENDED；未来恢复须先 ClearBlocking，再设为 ACTIVE。
'@

$validRelaySource = @'
const command = dispatch.resumeKind === 'codex' ? 'pwsh' : 'claude';
const args = ['-File', 'codex-cli-session.ps1', '-Action', 'Resume'];
spawnChild(command, args, { windowsHide: true });
'@

try {
  foreach ($path in @(
    '开发管理/自动工作流控制器提示词.txt',
    '开发管理/自动工作流规则.txt',
    '开发管理/自动工作流状态.txt',
    '开发管理/AI协作规则.txt',
    '开发管理/审核入口.txt',
    '开发管理/DeepSeek工作提示词.txt',
    '开发管理/状态与建议维护规则.txt',
    'tools/hourly-automation-lease.ps1',
    'tools/automation-workspace-guard.ps1',
    'tools/automation-finalize-commit.ps1',
    'tools/check-pending-whitespace.ps1',
    'tools/codex-cli-session.ps1',
    'tools/feishu-decision-bridge/src/bridge.mjs',
    'tools/feishu-decision-bridge/src/resume-trigger.mjs'
  )) {
    Write-Utf8File -Path (Join-Path $repositoryRoot $path) -Content "fixture`n"
  }
  Write-Utf8File -Path (Join-Path $repositoryRoot '开发管理/自动工作流控制器提示词.txt') -Content $canonicalPrompt
  Write-Utf8File -Path (Join-Path $repositoryRoot '开发管理/自动工作流规则.txt') -Content $canonicalRules
  Write-Utf8File -Path (Join-Path $repositoryRoot '开发管理/自动工作流状态.txt') -Content $canonicalStatus
  $relayPath = Join-Path $repositoryRoot 'tools/feishu-decision-bridge/src/resume-trigger.mjs'
  Write-Utf8File -Path $relayPath -Content $validRelaySource

  foreach ($id in @(
    'tzg-hourly-controller',
    'tzg-wf1-queue-and-review-maintenance',
    'tzg-wf3-claude-execute-1',
    'tzg-wf4-codex-execute-2'
  )) {
    Write-Automation -AutomationRoot $automationRoot -Id $id -Status 'PAUSED' -Prompt $canonicalPrompt
  }

  Assert-CheckerPasses `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
    -Context 'All-paused default contract'

  Write-Automation -AutomationRoot $automationRoot -Id 'tzg-hourly-controller' -Status 'ACTIVE' -Prompt $canonicalPrompt
  Assert-CheckerPasses `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireActive) `
    -Context 'Single active controller contract'

  Write-Automation -AutomationRoot $automationRoot -Id 'tzg-wf1-queue-and-review-maintenance' -Status 'ACTIVE' -Prompt $canonicalPrompt
  Assert-CheckerFails `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireActive) `
    -Context 'Second active writer'
  Write-Automation -AutomationRoot $automationRoot -Id 'tzg-wf1-queue-and-review-maintenance' -Status 'PAUSED' -Prompt $canonicalPrompt
  Write-Automation -AutomationRoot $automationRoot -Id 'tzg-hourly-controller' -Status 'PAUSED' -Prompt $canonicalPrompt

  $promptPath = Join-Path $repositoryRoot '开发管理/自动工作流控制器提示词.txt'
  foreach ($forbidden in @(
    'TQ-999',
    'manifest',
    'planOnly',
    'SubmitManifest',
    'DiscoverRead',
    '自动工作流任务注册表',
    'hourly-controller-v2'
  )) {
    Write-Utf8File -Path $promptPath -Content ($canonicalPrompt + "`n$forbidden`n")
    Assert-CheckerFails `
      -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
      -Context "Forbidden prompt token $forbidden"
  }
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  foreach ($forbiddenBoundary in @(
      '普通 Codex 执行、复审和队列维护直接使用 Desktop/VS Code rollout。',
      '第一期接入飞书 Tasks。',
      '第一期创建 task GUID 映射。',
      '第一期创建进度数据库。',
      '第一期创建阶段状态机。'
    )) {
    Write-Utf8File -Path $promptPath -Content ($canonicalPrompt + "`n$forbiddenBoundary`n")
    Assert-CheckerFails `
      -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
      -Context "Forbidden CLI-native boundary $forbiddenBoundary"
  }
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  Write-Utf8File -Path $promptPath -Content ($canonicalPrompt + "`n控制器直接更新自身为 PAUSED。`n")
  Assert-CheckerFails `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
    -Context 'Controller self-management boundary' `
    -ErrorContains 'manages itself'
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  foreach ($directNodeLaunch in @(
      "const command = dispatch.resumeKind === 'codex' ? 'codex' : 'claude';",
      "spawnChild('codex.cmd', [], {});",
      "spawnChild('node', ['codex.js'], {});"
    )) {
    Write-Utf8File -Path $relayPath -Content ($validRelaySource + "`n$directNodeLaunch`n")
    Assert-CheckerFails `
      -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
      -Context "Direct Node Codex launch $directNodeLaunch"
  }
  Write-Utf8File -Path $relayPath -Content $validRelaySource

  foreach ($requiredPhrase in @(
    '统一排序',
    '每轮只启动一个责任方',
    '三类均无合法候选时才',
    '队列维护正式入口必须保留两个顺序分支',
    '没有可提升的完整 backlog 任务卡不等于阻塞',
    '连续两次',
    'pauseRequested=true 表示工具级逻辑暂停',
    'SUSPENDED',
    'ClearBlocking',
    '自动化任务不得调用自动化管理能力管理自身',
    '外部普通管理上下文',
    'runtime 已逻辑暂停，界面尚未同步',
    '工具等待超时、yield 或尚未返回不等于 runner 失败',
    '不得释放租约或启动第二写入者',
    'businessCommit',
    '不消耗等待 token',
    'tools/codex-cli-session.ps1',
    '`Start`',
    'stdin',
    '`selected`',
    '`session_started`',
    '`running`',
    '`waiting_decision`',
    '`completed`',
    '`failed`'
  )) {
    $promptWithoutRequiredPhrase = $canonicalPrompt -replace [regex]::Escape($requiredPhrase), '已移除'
    Assert-True `
      -Condition (-not $promptWithoutRequiredPhrase.Contains($requiredPhrase, [StringComparison]::OrdinalIgnoreCase)) `
      -Message "Prompt fixture still contains required phrase $requiredPhrase"
    Write-Utf8File -Path $promptPath -Content $promptWithoutRequiredPhrase
    Assert-CheckerFails `
      -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot) `
      -Context "Missing prompt phrase $requiredPhrase"
  }
  Write-Utf8File -Path $promptPath -Content $canonicalPrompt

  $legacyPath = Join-Path $repositoryRoot 'tools/automation-controller.ps1'
  Write-Utf8File -Path $legacyPath -Content "legacy`n"
  Assert-CheckerFails `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireLegacyRetired) `
    -Context 'Legacy retirement guard'
  [IO.File]::Delete($legacyPath)
  Assert-CheckerPasses `
    -Result (Invoke-Checker -RepositoryRoot $repositoryRoot -AutomationRoot $automationRoot -RequireLegacyRetired) `
    -Context 'Legacy retirement success'

  Write-Output 'test-check-automation-workflow: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $temporaryPrefix = $temporaryBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (
      -not $resolvedRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) `
      -or (Split-Path -Leaf $resolvedRoot) -ne "tzg-workflow-checker-test-$testId"
    ) {
      throw "Refusing unsafe checker-test cleanup: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  }
}
