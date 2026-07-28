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
$approvedStateBase = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$canaryRuntime = Join-Path $approvedStateBase "tzg-external-ai-canary-$canaryId"
$wrapperPath = Join-Path $PSScriptRoot 'invoke-external-responsibility.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$activeRunId = $null
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
    'fixtures/generated-business.txt'
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
      'fixtures/generated-business.txt'
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
    '- 新建 `fixtures/generated-business.txt` 并同步本卡的 canonical 投影。'
    '## 实施范围'
    '- 不询问决定，直接新建业务结果；文件必须恰好包含三行：`status=approved`、`choice=A`、`verified=check-pending-whitespace`。'
    '- 只把本卡 JSON metadata 的 `route` 从 `external_execute` 改为 `codex_review`、`owner` 从当前外部 owner 改为 `codex`；保持 `dispatchState=ready`、同一 ID、标题、正文及其他字段。'
    '- 将当前队列和 source backlog 的同一行同步为 `codex_review / codex / ready` 投影，不修改交接文件。'
    '- businessCommit 标题固定为 `test(external): create business commit`；Automation 元数据固定使用本卡验证段给出的四项文本。'
    '- businessCommit 完成后只修改 `开发管理/AI合作沟通.txt`，写入 `HANDOFF-EXT-001`、待复审、真实业务 SHA、`verified=check-pending-whitespace`、`unverified=none; risk=none`，再以标题 `docs(external): record handoff` 创建无 Automation 元数据的 handoffCommit。'
    '## 禁止项'
    '- 不创建第二张复审卡，不自审。'
    '## 验证'
    '- business expected paths 为 `fixtures/generated-business.txt|开发管理/任务卡/TASK-EXT-001.txt|开发管理/当前任务队列.txt|开发管理/任务列表/自动化任务.txt`。'
    '- 依次运行 check-pending-whitespace、check-task-cards ExternalPendingReview、workspace guard Verify 与 `git diff --check`。'
    '- AutomationResult：`问题=测试需要验证固定外部入口能创建新文件；完成=已创建授权业务文件并转换同一卡待复审`。'
    '- AutomationImpact：`影响=外部任务已形成可复审双提交；边界=仅修改 canary 授权路径`。'
    '- AutomationVerify：`验证=check-pending-whitespace、ExternalPendingReview 与 git diff --check 通过；后续=等待 Codex 复审`。'
    '- AutomationPlain：`发生=外部责任方已完成授权修改并提交复审；影响=任务现在等待 Codex 检查后才能正式完成；需要=无需处理`。'
    '## 完成条件'
    '- 同一 ID 的 card、queue、backlog 已进入待复审。'
    '## 停止条件'
    '- 任一命令失败时不重试，返回 `failed/canary_command_failed`。'
  ) -join "`n"
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath $taskCardRelativePath -Content $taskCardText
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
  $generatedBusinessPath = Join-Path $canaryRepository 'fixtures\generated-business.txt'
  if (Test-Path -LiteralPath $generatedBusinessPath) {
    throw 'Generated business fixture must not exist before the external session'
  }
  $taskCardPath = Join-Path $canaryRepository $taskCardRelativePath
  $initialTaskCardText = [IO.File]::ReadAllText($taskCardPath, [Text.UTF8Encoding]::new($false, $true))
  $initialTaskBody = $initialTaskCardText.Substring(
    $initialTaskCardText.IndexOf('---TASK-BODY---', [StringComparison]::Ordinal)
  ).Replace("`r`n", "`n")
  $acquire = Invoke-CanaryProcess `
    -FileName 'pwsh' `
    -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $leasePath,
      '-Action', 'Acquire',
      '-StateRoot', $canaryRuntime,
      '-TaskId', $taskId,
      '-Owner', $externalOwner,
      '-RepositoryRoot', $canaryRepository
    ) `
    -WorkingDirectory $canaryRepository `
    -InputText $null
  $acquireJson = $acquire.Stdout.Trim() | ConvertFrom-Json
  if ([string]$acquireJson.status -cne 'ACQUIRED') {
    throw "Canary lease was not acquired: $($acquire.Stdout)"
  }
  $activeRunId = [string]$acquireJson.runId
  $wrapperResult = Invoke-CanaryProcess `
    -FileName 'pwsh' `
    -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapperPath,
      '-Action', 'Start',
      '-RepositoryRoot', $canaryRepository,
      '-TaskId', $taskId,
      '-RunId', $activeRunId,
      '-Owner', $externalOwner,
      '-StateRoot', $canaryRuntime,
      '-ResponsibilityTimeoutSeconds', '600'
    ) `
    -WorkingDirectory $canaryRepository `
    -InputText $null `
    -TimeoutSeconds 600
  $terminal = $wrapperResult.Stdout.Trim() | ConvertFrom-Json -Depth 20
  if ([string]$terminal.status -cne 'completed') {
    throw "Production wrapper did not return completed: $($wrapperResult.Stdout)"
  }
  if ([string]$terminal.identity -cne $expectedAuthor) {
    throw 'Production wrapper did not return the owner-mapped identity'
  }
  $sessionId = [string]$terminal.sessionId
  $parsedSessionId = [Guid]::Empty
  if (-not [Guid]::TryParse($sessionId, [ref]$parsedSessionId)) {
    throw 'Production wrapper did not return a real session ID'
  }
  $baselinePath = Join-Path $canaryRuntime "external-baselines\$activeRunId.json"
  if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw 'External session did not create the private runtime baseline'
  }
  if (Test-Path -LiteralPath (Join-Path $canaryRepository '.claude')) {
    throw 'External session created a repository-local .claude control path'
  }
  $generatedBusinessText = [IO.File]::ReadAllText(
    $generatedBusinessPath,
    [Text.UTF8Encoding]::new($false, $true)
  ).Replace("`r`n", "`n")
  Assert-CanaryEqual `
    -Actual $generatedBusinessText.TrimEnd("`r", "`n") `
    -Expected "status=approved`nchoice=A`nverified=check-pending-whitespace" `
    -Message 'External session did not create the expected business file'

  $newCommitCount = [int](Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('rev-list', '--count', "$initialHead..HEAD"))
  Assert-CanaryEqual -Actual $newCommitCount -Expected 2 -Message 'External worker did not create exactly two commits'
  $handoffCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $businessCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD^')
  Assert-CanaryEqual `
    -Actual ([string]$terminal.businessCommit) `
    -Expected $businessCommit `
    -Message 'Production wrapper returned the wrong businessCommit'
  Assert-CanaryEqual `
    -Actual ([string]$terminal.handoffCommit) `
    -Expected $handoffCommit `
    -Message 'Production wrapper returned the wrong handoffCommit'

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
      'Result: 问题=测试需要验证固定外部入口能创建新文件；完成=已创建授权业务文件并转换同一卡待复审',
      'Impact: 影响=外部任务已形成可复审双提交；边界=仅修改 canary 授权路径',
      'Verify: 验证=check-pending-whitespace、ExternalPendingReview 与 git diff --check 通过；后续=等待 Codex 复审',
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
  foreach ($handoffEvidence in @(
      'HANDOFF-EXT-001',
      '待复审',
      $businessCommit,
      'check-pending-whitespace',
      'none',
      'risk'
    )) {
    if (-not $handoffText.Contains($handoffEvidence, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Handoff evidence is missing: $handoffEvidence"
    }
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
  if (-not [string]::IsNullOrWhiteSpace($activeRunId) -and (Test-Path -LiteralPath $canaryRuntime)) {
    $null = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
        -Action Release `
        -StateRoot $canaryRuntime `
        -RunId $activeRunId 2>$null)
  }
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
  if ($canarySucceeded -and (Test-Path -LiteralPath $canaryRuntime)) {
    $resolvedRuntime = [IO.Path]::GetFullPath($canaryRuntime)
    $runtimePrefix = $approvedStateBase + [IO.Path]::DirectorySeparatorChar
    $runtimeLeaf = Split-Path -Leaf $resolvedRuntime
    if (
      -not $resolvedRuntime.StartsWith($runtimePrefix, [StringComparison]::OrdinalIgnoreCase) -or
      $runtimeLeaf -cne "tzg-external-ai-canary-$canaryId"
    ) {
      throw "Refusing unsafe canary runtime cleanup: $resolvedRuntime"
    }
    Remove-Item -LiteralPath $resolvedRuntime -Recurse -Force
  } elseif (-not $canarySucceeded -and (Test-Path -LiteralPath $canaryRuntime)) {
    [Console]::Error.WriteLine("External AI canary runtime preserved for diagnosis: $canaryRuntime")
  }
}
