#requires -Version 7.0

$ErrorActionPreference = 'Stop'

function Invoke-CanaryProcess {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FileName,
    [string[]]$Arguments = @(),
    [Parameter(Mandatory = $true)]
    [string]$WorkingDirectory,
    [AllowNull()]
    [string]$InputText,
    [ValidateRange(1, 900)]
    [int]$TimeoutSeconds = 120,
    [switch]$AllowNonZero
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $FileName
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
  if ($null -ne $InputText) {
    $startInfo.RedirectStandardInput = $true
    $startInfo.StandardInputEncoding = [Text.UTF8Encoding]::new($false)
  }
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) {
    throw "Unable to start $FileName"
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  if ($null -ne $InputText) {
    $process.StandardInput.Write($InputText)
    $process.StandardInput.Close()
  }
  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try {
      $process.Kill($true)
    } catch {
      # Best effort cleanup of the timed-out process tree.
    }
    $process.Dispose()
    throw "$FileName timed out"
  }
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult()
  $stderr = $stderrTask.GetAwaiter().GetResult()
  $exitCode = $process.ExitCode
  $process.Dispose()
  if ($exitCode -ne 0 -and -not $AllowNonZero) {
    $safeError = if ($stderr.Length -gt 2000) { $stderr.Substring(0, 2000) } else { $stderr }
    throw "$FileName exited with $exitCode`: $safeError"
  }
  [pscustomobject]@{
    ExitCode = $exitCode
    Stdout = $stdout
    Stderr = $stderr
  }
}

function Invoke-CanaryGit {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string[]]$Arguments
  )

  (Invoke-CanaryProcess `
    -FileName 'git' `
    -Arguments $Arguments `
    -WorkingDirectory $RepositoryRoot `
    -InputText $null).Stdout.Trim()
}

function Write-CanaryFile {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [Parameter(Mandatory = $true)]
    [string]$RelativePath,
    [Parameter(Mandatory = $true)]
    [string]$Content
  )

  $repository = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  $fullPath = [IO.Path]::GetFullPath((Join-Path $repository $RelativePath))
  $prefix = $repository + [IO.Path]::DirectorySeparatorChar
  if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Fixture path escapes temporary repository: $RelativePath"
  }
  [IO.Directory]::CreateDirectory((Split-Path -Parent $fullPath)) | Out-Null
  [IO.File]::WriteAllText($fullPath, $Content, [Text.UTF8Encoding]::new($false))
}

function Assert-CanaryEqual {
  param(
    [AllowNull()]
    [object]$Actual,
    [AllowNull()]
    [object]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ($Actual -ne $Expected) {
    throw "$Message (expected=$Expected actual=$Actual)"
  }
}

$canaryId = [Guid]::NewGuid().ToString('N')
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$canaryRoot = Join-Path $temporaryBase "tzg-external-ai-canary-$canaryId"
$canaryRepository = Join-Path $canaryRoot 'repo'
$canaryRuntime = Join-Path $canaryRoot 'runtime'
$baselinePath = Join-Path $canaryRuntime 'workspace-baseline.json'
$canarySucceeded = $false

try {
  [IO.Directory]::CreateDirectory($canaryRepository) | Out-Null
  [IO.Directory]::CreateDirectory($canaryRuntime) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $canaryRepository 'tools')) | Out-Null
  foreach ($toolName in @(
    'automation-workspace-guard.ps1',
    'automation-finalize-commit.ps1',
    'check-pending-whitespace.ps1',
    'check-task-cards.ps1'
  )) {
    Copy-Item `
      -LiteralPath (Join-Path $PSScriptRoot $toolName) `
      -Destination (Join-Path $canaryRepository "tools\$toolName")
  }

  $baseUrl = [string]$env:ANTHROPIC_BASE_URL
  if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    $settingsPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude\settings.json'
    if (Test-Path -LiteralPath $settingsPath) {
      $baseUrl = [string](Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json).env.ANTHROPIC_BASE_URL
    }
  }
  $expectedAuthor = if ($baseUrl -match '^http://127\.0\.0\.1:15721(?:/|$)') {
    'DeepSeek V4 Pro'
  } else {
    'Claude Code'
  }
  $externalOwner = if ($expectedAuthor -ceq 'DeepSeek V4 Pro') { 'deepseek' } else { 'claude' }
  $claudeAllowedTools = @(
    'Read'
    'Edit'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 *)'
  ) -join ','
  $claudePermissionArguments = @(
    '--permission-mode'
    'dontAsk'
    '--allowedTools'
    $claudeAllowedTools
  )

  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath 'AGENTS.md' -Content @'
# External AI Canary Rules

- This repository is an isolated system-temporary canary. Never access or modify any other repository.
- Consume only the already-selected `external_execute` card from the canonical queue. Never rescan or choose another candidate.
- The selected card owner must match the supplied external identity.
- Before modification, use the copied workspace guard. Use a file editing tool for changes; do not use shell file-write commands.
- Update the same card and its canonical queue/backlog projections to `route=codex_review`, `owner=codex`, `dispatchState=ready`, then run the copied task-card checker.
- Run only the task's direct checks. Create the business and handoff commits yourself with the copied finalizer.
- Do not self-review, widen authorized paths, dispatch another agent, push, stash, reset, checkout, or clean.
'@
  $taskId = 'TASK-EXT-001'
  $taskCardRelativePath = "开发管理/任务卡/$taskId.txt"
  $taskArchiveRelativePath = "开发管理/任务归档/$taskId.txt"
  $taskBacklogRelativePath = '开发管理/任务列表/自动化任务.txt'
  $businessExpectedPaths = @(
    'fixtures/business.txt'
    $taskCardRelativePath
    '开发管理/当前任务队列.txt'
    $taskBacklogRelativePath
  )
  $taskMetadata = [ordered]@{
    schemaVersion = 1
    id = $taskId
    title = '外部责任方同卡待复审转换'
    priority = 'P0'
    route = 'external_execute'
    owner = $externalOwner
    domain = 'automation'
    stage = 'verification'
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = $null
    expectedPaths = @(
      'fixtures/business.txt'
      $taskCardRelativePath
      $taskArchiveRelativePath
      '开发管理/当前任务队列.txt'
      $taskBacklogRelativePath
      '开发管理/AI合作沟通.txt'
    )
    sourceBacklog = $taskBacklogRelativePath
  }
  $taskCardText = @(
    '---TASK-META---'
    ($taskMetadata | ConvertTo-Json -Depth 10)
    '---TASK-BODY---'
    "# $taskId · 外部责任方同卡待复审转换"
    '## 来源与当前边界'
    '- 本卡已由 canary 调度器选中；不得重新扫描候选。'
    '## 必查范围'
    '- `fixtures/business.txt` 与本卡的 canonical 投影。'
    '## 实施范围'
    '- A：写入业务结果，并将同一卡转换为 codex_review ready。'
    '## 禁止项'
    '- 不创建第二张复审卡，不自审。'
    '## 验证'
    '- check-pending-whitespace；check-task-cards ExternalPendingReview。'
    '## 完成条件'
    '- 同一 ID 的 card、queue、backlog 已进入待复审。'
    '## 停止条件'
    '- B：不修改并返回 blocked。'
  ) -join "`n"
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath $taskCardRelativePath -Content $taskCardText
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath 'fixtures/business.txt' -Content "status=pending`nchoice=`nverified=`n"
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath '开发管理/当前任务队列.txt' -Content (@(
    '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
    '| --- | --- | --- | --- | --- | --- | --- | --- |'
    "| $taskId | external_execute | $externalOwner | P0 | automation | verification | 外部责任方同卡待复审转换 | $taskCardRelativePath |"
  ) -join "`n")
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath $taskBacklogRelativePath -Content (@(
    '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |'
    '| --- | --- | --- | --- | --- | --- | --- |'
    "| $taskId | P0 | $externalOwner | 已排队 | — | 外部责任方同卡待复审转换 | $taskCardRelativePath |"
  ) -join "`n")
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath '开发管理/AI合作沟通.txt' -Content "# AI合作沟通`n"

  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('init') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.name', 'Canary Harness') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.email', 'canary-harness@example.invalid') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'core.autocrlf', 'true') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('add', '--', '.') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('commit', '-m', 'test: initialize external AI canary') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.name', $expectedAuthor) | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.email', 'external-worker@example.invalid') | Out-Null

  $baselineCardCheck = Invoke-CanaryProcess `
    -FileName 'pwsh' `
    -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/check-task-cards.ps1',
      '-RepositoryRoot', $canaryRepository
    ) `
    -WorkingDirectory $canaryRepository `
    -InputText $null
  Assert-CanaryEqual -Actual $baselineCardCheck.ExitCode -Expected 0 -Message "Initial canonical task projection is invalid: $($baselineCardCheck.Stderr)"

  $initialHead = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $taskCardPath = Join-Path $canaryRepository $taskCardRelativePath
  $initialTaskCardText = [IO.File]::ReadAllText($taskCardPath, [Text.UTF8Encoding]::new($false, $true))
  $initialTaskBody = $initialTaskCardText.Substring(
    $initialTaskCardText.IndexOf('---TASK-BODY---', [StringComparison]::Ordinal)
  ).Replace("`r`n", "`n")
  $sessionId = [Guid]::NewGuid().ToString()
  $claudeCommand = Get-Command 'claude.cmd' -CommandType Application -ErrorAction Stop
  $claudeExecutable = $claudeCommand.Source

  $promptTemplate = @'
You are running an isolated external-worker canary in repository __REPOSITORY_ROOT__.
Actual modification identity and required Git author: __EXPECTED_AUTHOR__.
Selected task-card owner: __EXTERNAL_OWNER__.

Read AGENTS.md, 开发管理/当前任务队列.txt, and 开发管理/任务卡/TASK-EXT-001.txt. This exact `external_execute` card is already selected; do not rescan candidates.

Phase 1 is decision-only. Do not modify files, Git config, index, or commits. Do not create a baseline yet. Ask the single task-card decision and exit. Your final output for this phase must contain one JSON object with:
{"status":"needs_decision","decisionId":"DECISION-EXT-001","question":"是否按 A 执行授权修改？","options":["A","B"]}

When this exact session is resumed with the raw reply A, perform Phase 2 without asking again:
1. Work only in __REPOSITORY_ROOT__. The temporary repository Git author is already configured as __EXPECTED_AUTHOR__. Do not run git config or edit .git. Do not use cd or chain shell commands; the current working directory is already the repository.
2. Run these exact commands separately:
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Snapshot -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Check -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__' -ExpectedPaths '__BUSINESS_EXPECTED_PATHS__'
3. Use your file editing tool to make fixtures/business.txt exactly:
status=approved
choice=A
verified=check-pending-whitespace
In 开发管理/任务卡/TASK-EXT-001.txt, change only the JSON metadata values `route` from `external_execute` to `codex_review` and `owner` from `__EXTERNAL_OWNER__` to `codex`; keep `dispatchState=ready`, the same ID/title/body, and every other field unchanged.
Make 开发管理/当前任务队列.txt exactly these three lines:
| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TASK-EXT-001 | codex_review | codex | P0 | automation | verification | 外部责任方同卡待复审转换 | 开发管理/任务卡/TASK-EXT-001.txt |
Make 开发管理/任务列表/自动化任务.txt exactly these three lines:
| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |
| --- | --- | --- | --- | --- | --- | --- |
| TASK-EXT-001 | P0 | codex | 已排队 | — | 外部责任方同卡待复审转换 | 开发管理/任务卡/TASK-EXT-001.txt |
Do not modify the handoff file yet.
4. Run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths '__BUSINESS_EXPECTED_PATHS__'. Then run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-task-cards.ps1 -RepositoryRoot '__REPOSITORY_ROOT__' -TaskId 'TASK-EXT-001' -Postcondition ExternalPendingReview. Then run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Verify -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__' -ExpectedPaths '__BUSINESS_EXPECTED_PATHS__'.
5. Run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -ExpectedPaths '__BUSINESS_EXPECTED_PATHS__' -CommitMessage 'test(external): create business commit' -RequireAutomationMetadata -AutomationTask 'TASK-EXT-001' -AutomationState 'pending_review' -AutomationResult '完成外部责任方授权修改' -AutomationImpact 'TASK-EXT-001 已进入待复审状态' -AutomationVerify 'check-pending-whitespace 与 ExternalPendingReview 通过' -AutomationPlain '发生=外部责任方已完成授权修改并提交复审；影响=任务现在等待 Codex 检查后才能正式完成；需要=无需处理'. Save its stdout SHA as businessCommit.
6. Modify only 开发管理/AI合作沟通.txt to exactly these six lines, substituting the real SHA:
# AI合作沟通
HANDOFF-EXT-001
status=待复审
businessCommit=<REAL_SHA>
verified=check-pending-whitespace
unverified=none; risk=none
7. Do not rerun the direct/domain check. Run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -ExpectedPaths '开发管理/AI合作沟通.txt' -CommitMessage 'docs(external): record handoff'. Save its stdout SHA as handoffCommit.
8. Do not push. Finish with one JSON object containing status completed, identity `__EXPECTED_AUTHOR__`, and both real SHAs.

If any exact command returns nonzero, do not retry or diagnose it. Immediately output {"status":"failed","detailCode":"canary_command_failed"} and exit.

If resumed with B, output {"status":"blocked"} without modifying the repository.
'@
  $prompt = $promptTemplate.Replace('__REPOSITORY_ROOT__', $canaryRepository).Replace(
    '__BASELINE_PATH__',
    $baselinePath
  ).Replace('__EXPECTED_AUTHOR__', $expectedAuthor).Replace(
    '__EXTERNAL_OWNER__',
    $externalOwner
  ).Replace('__BUSINESS_EXPECTED_PATHS__', ($businessExpectedPaths -join '|'))

  [IO.File]::WriteAllText(
    (Join-Path $canaryRuntime 'session-id.txt'),
    $sessionId,
    [Text.UTF8Encoding]::new($false)
  )

  $first = Invoke-CanaryProcess `
    -FileName $claudeExecutable `
    -Arguments (@('--session-id', $sessionId, '--print') + $claudePermissionArguments) `
    -WorkingDirectory $canaryRepository `
    -InputText ($prompt + "`n") `
    -TimeoutSeconds 300
  if ($first.Stdout -notmatch '"status"\s*:\s*"needs_decision"') {
    throw 'First external session did not return needs_decision'
  }
  Assert-CanaryEqual `
    -Actual (Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) `
    -Expected '' `
    -Message 'Repository changed before decision'
  Assert-CanaryEqual `
    -Actual (Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')) `
    -Expected $initialHead `
    -Message 'Commit changed before decision'

  $second = Invoke-CanaryProcess `
    -FileName $claudeExecutable `
    -Arguments (@('--resume', $sessionId, '--print') + $claudePermissionArguments) `
    -WorkingDirectory $canaryRepository `
    -InputText "A`n" `
    -TimeoutSeconds 600
  if ($second.Stdout -notmatch '"status"\s*:\s*"completed"') {
    throw 'Resumed external session did not return completed'
  }
  if (-not $second.Stdout.Contains($expectedAuthor, [StringComparison]::Ordinal)) {
    throw 'Resumed external session did not return the owner-mapped identity'
  }

  $newCommitCount = [int](Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('rev-list', '--count', "$initialHead..HEAD"))
  Assert-CanaryEqual -Actual $newCommitCount -Expected 2 -Message 'External worker did not create exactly two commits'
  $handoffCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $businessCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD^')

  $businessPathText = Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('-c', 'core.quotepath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', $businessCommit)
  $businessPaths = @($businessPathText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)
  $expectedBusinessPaths = @($businessExpectedPaths | Sort-Object)
  Assert-CanaryEqual `
    -Actual ($businessPaths -join '|') `
    -Expected ($expectedBusinessPaths -join '|') `
    -Message 'Business commit paths mismatch'

  $businessBody = Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('show', '-s', '--format=%B', $businessCommit)
  foreach ($requiredMetadata in @(
      'Automation: tzg-hourly-controller',
      'Task: TASK-EXT-001',
      'State: pending_review',
      'Result: 完成外部责任方授权修改',
      'Impact: TASK-EXT-001 已进入待复审状态',
      'Verify: check-pending-whitespace 与 ExternalPendingReview 通过',
      'Plain: 发生=外部责任方已完成授权修改并提交复审；影响=任务现在等待 Codex 检查后才能正式完成；需要=无需处理'
    )) {
    if (-not $businessBody.Contains($requiredMetadata, [StringComparison]::Ordinal)) {
      throw "Business commit metadata is missing: $requiredMetadata"
    }
  }

  $handoffPathText = Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('-c', 'core.quotepath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', $handoffCommit)
  $handoffPaths = @($handoffPathText -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-CanaryEqual `
    -Actual ($handoffPaths -join '|') `
    -Expected '开发管理/AI合作沟通.txt' `
    -Message 'Handoff commit must contain only the handoff file'
  $handoffBody = Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('show', '-s', '--format=%B', $handoffCommit)
  if ($handoffBody.Contains('Automation: tzg-hourly-controller', [StringComparison]::Ordinal)) {
    throw 'Handoff commit must not contain automation result metadata'
  }

  $handoffText = [IO.File]::ReadAllText(
    (Join-Path $canaryRepository '开发管理\AI合作沟通.txt'),
    [Text.UTF8Encoding]::new($false, $true)
  )
  if (-not $handoffText.Contains("businessCommit=$businessCommit")) {
    throw 'Handoff does not contain the real business SHA'
  }
  $finalTaskCardText = [IO.File]::ReadAllText($taskCardPath, [Text.UTF8Encoding]::new($false, $true))
  $metaStart = $finalTaskCardText.IndexOf('---TASK-META---', [StringComparison]::Ordinal) + '---TASK-META---'.Length
  $bodyStart = $finalTaskCardText.IndexOf('---TASK-BODY---', [StringComparison]::Ordinal)
  $finalMetadata = $finalTaskCardText.Substring($metaStart, $bodyStart - $metaStart).Trim() | ConvertFrom-Json -Depth 10
  Assert-CanaryEqual `
    -Actual ([string]$finalMetadata.id) `
    -Expected $taskId `
    -Message 'External transition changed the task-card ID'
  Assert-CanaryEqual `
    -Actual ([string]$finalMetadata.route) `
    -Expected 'codex_review' `
    -Message 'External transition did not set codex_review'
  Assert-CanaryEqual `
    -Actual ([string]$finalMetadata.owner) `
    -Expected 'codex' `
    -Message 'External transition did not set owner=codex'
  Assert-CanaryEqual `
    -Actual ([string]$finalMetadata.dispatchState) `
    -Expected 'ready' `
    -Message 'External transition did not keep dispatchState=ready'
  Assert-CanaryEqual `
    -Actual $finalTaskCardText.Substring($bodyStart).Replace("`r`n", "`n") `
    -Expected $initialTaskBody `
    -Message 'External transition changed the task-card body'
  Assert-CanaryEqual `
    -Actual (@(Get-ChildItem -LiteralPath (Join-Path $canaryRepository '开发管理/任务卡') -Filter '*.txt' -File).Count) `
    -Expected 1 `
    -Message 'External transition created a second task card'
  $finalCardCheck = Invoke-CanaryProcess `
    -FileName 'pwsh' `
    -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tools/check-task-cards.ps1',
      '-RepositoryRoot', $canaryRepository,
      '-TaskId', $taskId,
      '-Postcondition', 'ExternalPendingReview'
    ) `
    -WorkingDirectory $canaryRepository `
    -InputText $null
  Assert-CanaryEqual -Actual $finalCardCheck.ExitCode -Expected 0 -Message "Final pending-review projection is invalid: $($finalCardCheck.Stderr)"
  Assert-CanaryEqual `
    -Actual (Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('status', '--porcelain=v1', '--untracked-files=all')) `
    -Expected '' `
    -Message 'Temporary repository is not clean'
  foreach ($commit in @($businessCommit, $handoffCommit)) {
    Assert-CanaryEqual `
      -Actual (Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('show', '-s', '--format=%an', $commit)) `
      -Expected $expectedAuthor `
      -Message "Commit $commit was not authored by the external worker"
  }

  $canarySucceeded = $true
  Write-Output 'test-external-ai-self-commit: OK'
} finally {
  if ($canarySucceeded -and (Test-Path -LiteralPath $canaryRoot)) {
    $resolvedRoot = [IO.Path]::GetFullPath($canaryRoot)
    $temporaryPrefix = $temporaryBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $leaf = Split-Path -Leaf $resolvedRoot
    if (
      -not $resolvedRoot.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) `
      -or $leaf -ne "tzg-external-ai-canary-$canaryId"
    ) {
      throw "Refusing unsafe canary cleanup: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  } elseif (-not $canarySucceeded) {
    [Console]::Error.WriteLine("External AI canary preserved for diagnosis: $canaryRoot")
  }
}
