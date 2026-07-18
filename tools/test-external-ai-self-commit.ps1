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
    [Collections.IDictionary]$Environment = @{},
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
  foreach ($name in $Environment.Keys) {
    $startInfo.Environment[[string]$name] = [string]$Environment[$name]
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

function Get-CanaryHash {
  param([Parameter(Mandatory = $true)][string]$Path)

  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path)))
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

function Get-ClaudeWorkerIdentity {
  $baseUrl = [string]$env:ANTHROPIC_BASE_URL
  if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    $settingsPath = Join-Path `
      ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) `
      '.claude\settings.json'
    if (Test-Path -LiteralPath $settingsPath) {
      $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json -Depth 100
      $baseUrl = [string]$settings.env.ANTHROPIC_BASE_URL
    }
  }
  $uri = $null
  if (
    [Uri]::TryCreate($baseUrl, [UriKind]::Absolute, [ref]$uri) `
    -and $uri.Host -eq '127.0.0.1' `
    -and $uri.Port -eq 15721
  ) {
    return 'DeepSeek V4 Pro'
  }
  'Claude Code'
}

function Write-CanaryRuntimeText {
  param(
    [Parameter(Mandatory = $true)]
    [string]$RuntimeRoot,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9.-]+$')]
    [string]$Name,
    [AllowEmptyString()]
    [string]$Content
  )

  [IO.File]::WriteAllText(
    (Join-Path $RuntimeRoot $Name),
    $Content,
    [Text.UTF8Encoding]::new($false)
  )
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
    'check-pending-whitespace.ps1'
  )) {
    Copy-Item `
      -LiteralPath (Join-Path $PSScriptRoot $toolName) `
      -Destination (Join-Path $canaryRepository "tools\$toolName")
  }

  $expectedAuthor = Get-ClaudeWorkerIdentity
  $externalProcessEnvironment = @{
    GIT_AUTHOR_NAME = $expectedAuthor
    GIT_AUTHOR_EMAIL = 'external-worker@example.invalid'
    GIT_COMMITTER_NAME = $expectedAuthor
    GIT_COMMITTER_EMAIL = 'external-worker@example.invalid'
  }
  $claudeAllowedTools = @(
    'Read'
    'Edit'
    'TaskCreate'
    'TaskUpdate'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 *)'
    'Bash(pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 *)'
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
- Only a pending, non-review task explicitly owned by Claude / DeepSeek may be executed.
- Before modification, use the copied workspace guard. Use a file editing tool for changes; do not use shell file-write commands.
- Run only the task's direct check. Create the business and handoff commits yourself with the copied finalizer.
- Do not self-review, widen authorized paths, dispatch another agent, push, stash, reset, checkout, or clean.
'@
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath '开发管理/任务卡.txt' -Content @'
# TASK-EXT-001

- 优先级：P0
- 主责：Claude / DeepSeek
- 状态：待处理；非复审
- 决策门禁：修改前必须询问负责人选择 A 或 B，并以 needs_decision 退出。A 批准下述精确修改；B 表示阻塞退出。
- 业务授权路径：fixtures/business.txt、开发管理/当前任务队列.txt
- 交接授权路径：开发管理/AI合作沟通.txt
- A 的业务结果：business.txt 写入 approved/A/verified 三行；队列状态改为待复审。
- 直接验证：check-pending-whitespace；不得运行其他领域检查。
- 提交：先 businessCommit，再只改交接文件创建 handoffCommit。
'@
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath 'fixtures/business.txt' -Content "status=pending`nchoice=`nverified=`n"
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath '开发管理/当前任务队列.txt' -Content "TASK-EXT-001|P0|Claude / DeepSeek|待处理`n"
  Write-CanaryFile -RepositoryRoot $canaryRepository -RelativePath '开发管理/AI合作沟通.txt' -Content "# AI合作沟通`n"

  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('init') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.name', 'Canary Harness') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'user.email', 'canary-harness@example.invalid') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('config', 'core.autocrlf', 'false') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('add', '--', '.') | Out-Null
  Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('commit', '-m', 'test: initialize external AI canary') | Out-Null

  $initialHead = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $taskCardPath = Join-Path $canaryRepository '开发管理\任务卡.txt'
  $initialTaskCardHash = Get-CanaryHash -Path $taskCardPath
  $sessionId = [Guid]::NewGuid().ToString()
  $claudeCommand = Get-Command 'claude.cmd' -CommandType Application -ErrorAction Stop
  $claudeExecutable = $claudeCommand.Source

  $promptTemplate = @'
You are running an isolated external-worker canary in repository __REPOSITORY_ROOT__.
Actual modification identity and required Git author: __EXPECTED_AUTHOR__.

Read AGENTS.md and 开发管理/任务卡.txt. The task is explicitly owned by Claude / DeepSeek, pending, and non-review.

Phase 1 is decision-only. Do not modify files, Git config, index, or commits. Do not create a baseline yet. Ask the single task-card decision and exit. Your final output for this phase must contain one JSON object with:
{"status":"needs_decision","decisionId":"DECISION-EXT-001","question":"是否按 A 执行授权修改？","options":["A","B"]}

When this exact session is resumed with the raw reply A, perform Phase 2 without asking again:
1. Work only in __REPOSITORY_ROOT__. Git author and committer identity are already supplied by the process environment as __EXPECTED_AUTHOR__. Do not run git config or edit .git. Do not use cd or chain shell commands; the current working directory is already the repository.
2. Run these exact commands separately:
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Snapshot -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__'
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Check -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__' -ExpectedPaths 'fixtures/business.txt|开发管理/当前任务队列.txt'
3. Use your file editing tool to make fixtures/business.txt exactly:
status=approved
choice=A
verified=check-pending-whitespace
and make 开发管理/当前任务队列.txt exactly:
TASK-EXT-001|P0|Claude / DeepSeek|待复审
Do not modify the task card or handoff file yet.
4. Run only: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'fixtures/business.txt|开发管理/当前任务队列.txt'. Then run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-workspace-guard.ps1 Verify -RepositoryRoot '__REPOSITORY_ROOT__' -BaselinePath '__BASELINE_PATH__' -ExpectedPaths 'fixtures/business.txt|开发管理/当前任务队列.txt'.
5. Run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -ExpectedPaths 'fixtures/business.txt|开发管理/当前任务队列.txt' -CommitMessage 'test(external): create business commit'. Save its stdout SHA as businessCommit.
6. Modify only 开发管理/AI合作沟通.txt to exactly these six lines, substituting the real SHA:
# AI合作沟通
HANDOFF-EXT-001
status=待复审
businessCommit=<REAL_SHA>
verified=check-pending-whitespace
unverified=none; risk=none
7. Do not rerun the direct/domain check. Run exactly: pwsh -NoProfile -ExecutionPolicy Bypass -File tools/automation-finalize-commit.ps1 -ExpectedPaths '开发管理/AI合作沟通.txt' -CommitMessage 'docs(external): record handoff'. Save its stdout SHA as handoffCommit.
8. Do not push. Finish with one JSON object containing status completed plus both real SHAs.

If resumed with B, output {"status":"blocked"} without modifying the repository.
'@
  $prompt = $promptTemplate.Replace('__REPOSITORY_ROOT__', $canaryRepository).Replace(
    '__BASELINE_PATH__',
    $baselinePath
  ).Replace('__EXPECTED_AUTHOR__', $expectedAuthor)

  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'session-id.txt' -Content $sessionId
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'expected-author.txt' -Content $expectedAuthor

  $first = Invoke-CanaryProcess `
    -FileName $claudeExecutable `
    -Arguments (@('--session-id', $sessionId, '--print') + $claudePermissionArguments) `
    -WorkingDirectory $canaryRepository `
    -InputText ($prompt + "`n") `
    -Environment $externalProcessEnvironment `
    -TimeoutSeconds 300 `
    -AllowNonZero
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase1.stdout.txt' -Content $first.Stdout
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase1.stderr.txt' -Content $first.Stderr
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase1.exit-code.txt' -Content ([string]$first.ExitCode)
  if ($first.ExitCode -ne 0) {
    throw "First external session exited with $($first.ExitCode)"
  }
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
    -Environment $externalProcessEnvironment `
    -TimeoutSeconds 600 `
    -AllowNonZero
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase2.stdout.txt' -Content $second.Stdout
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase2.stderr.txt' -Content $second.Stderr
  Write-CanaryRuntimeText -RuntimeRoot $canaryRuntime -Name 'phase2.exit-code.txt' -Content ([string]$second.ExitCode)
  if ($second.ExitCode -ne 0) {
    throw "Resumed external session exited with $($second.ExitCode)"
  }
  if ($second.Stdout -notmatch '"status"\s*:\s*"completed"') {
    throw 'Resumed external session did not return completed'
  }

  $newCommitCount = [int](Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('rev-list', '--count', "$initialHead..HEAD"))
  Assert-CanaryEqual -Actual $newCommitCount -Expected 2 -Message 'External worker did not create exactly two commits'
  $handoffCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD')
  $businessCommit = Invoke-CanaryGit -RepositoryRoot $canaryRepository -Arguments @('rev-parse', 'HEAD^')

  $businessPaths = @(Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('-c', 'core.quotepath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', $businessCommit) `
    -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)
  $expectedBusinessPaths = @('fixtures/business.txt', '开发管理/当前任务队列.txt') | Sort-Object
  Assert-CanaryEqual `
    -Actual ($businessPaths -join '|') `
    -Expected ($expectedBusinessPaths -join '|') `
    -Message 'Business commit paths mismatch'

  $handoffPaths = @(Invoke-CanaryGit `
    -RepositoryRoot $canaryRepository `
    -Arguments @('-c', 'core.quotepath=false', 'diff-tree', '--no-commit-id', '--name-only', '-r', $handoffCommit) `
    -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  Assert-CanaryEqual `
    -Actual ($handoffPaths -join '|') `
    -Expected '开发管理/AI合作沟通.txt' `
    -Message 'Handoff commit must contain only the handoff file'

  $handoffText = [IO.File]::ReadAllText(
    (Join-Path $canaryRepository '开发管理\AI合作沟通.txt'),
    [Text.UTF8Encoding]::new($false, $true)
  )
  if (-not $handoffText.Contains("businessCommit=$businessCommit")) {
    throw 'Handoff does not contain the real business SHA'
  }
  Assert-CanaryEqual `
    -Actual (Get-CanaryHash -Path $taskCardPath) `
    -Expected $initialTaskCardHash `
    -Message 'Unauthorized task card changed'
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
