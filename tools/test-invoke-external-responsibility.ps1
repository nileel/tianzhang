#requires -Version 7.0

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param($Actual, $Expected, [string]$Message)
  if ($Actual -cne $Expected) {
    throw "$Message (actual=$Actual expected=$Expected)"
  }
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-Git {
  param([string]$Root, [string[]]$Arguments)
  $output = & git -C $Root @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "git failed: $($Arguments -join ' '): $(@($output) -join "`n")"
  }
  (@($output) -join "`n").TrimEnd()
}

function Invoke-Wrapper {
  param(
    [string]$WrapperPath,
    [string]$Root,
    [string]$StateRoot,
    [string]$RunId,
    [string]$Owner = 'deepseek',
    [ValidateSet('Start', 'Resume')]
    [string]$Action = 'Start',
    [string]$SessionId,
    [string]$DecisionOption
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = 'pwsh'
  $startInfo.WorkingDirectory = $Root
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $WrapperPath,
      '-Action', $Action,
      '-RepositoryRoot', $Root,
      '-TaskId', 'TASK-EXT-001',
      '-RunId', $RunId,
      '-Owner', $Owner,
      '-StateRoot', $StateRoot,
      '-ResponsibilityTimeoutSeconds', '30'
    )) {
    $startInfo.ArgumentList.Add($argument)
  }
  if (-not [string]::IsNullOrWhiteSpace($SessionId)) {
    $startInfo.ArgumentList.Add('-SessionId')
    $startInfo.ArgumentList.Add($SessionId)
  }
  if (-not [string]::IsNullOrWhiteSpace($DecisionOption)) {
    $startInfo.ArgumentList.Add('-DecisionOption')
    $startInfo.ArgumentList.Add($DecisionOption)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  Assert-True ($process.Start()) 'wrapper process did not start'
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $process.WaitForExit()
  $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
  $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
  $exitCode = $process.ExitCode
  $process.Dispose()
  [pscustomobject]@{
    ExitCode = $exitCode
    Stdout = $stdout
    Stderr = $stderr
    Json = if ([string]::IsNullOrWhiteSpace($stdout)) { $null } else { $stdout | ConvertFrom-Json -Depth 20 }
  }
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $temporaryBase "tzg-external-wrapper-test-$testId"
$repositoryRoot = Join-Path $testRoot 'repository'
$approvedStateBase = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
$stateRoot = Join-Path $approvedStateBase "tzg-external-wrapper-test-$testId"
$fakeBin = Join-Path $testRoot 'bin'
$recordPath = Join-Path $testRoot 'fake-claude-record.json'
$wrapperPath = Join-Path $PSScriptRoot 'invoke-external-responsibility.ps1'
$leasePath = Join-Path $PSScriptRoot 'hourly-automation-lease.ps1'
$originalPath = $env:PATH
$originalBaseUrl = $env:ANTHROPIC_BASE_URL
$originalMode = $env:TZG_FAKE_CLAUDE_MODE
$originalRecord = $env:TZG_FAKE_CLAUDE_RECORD

try {
  Assert-True (Test-Path -LiteralPath $wrapperPath -PathType Leaf) "missing wrapper: $wrapperPath"
  [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null
  [IO.Directory]::CreateDirectory($stateRoot) | Out-Null
  [IO.Directory]::CreateDirectory($fakeBin) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'tools')) | Out-Null

  foreach ($toolName in @(
      'automation-commit-metadata.ps1',
      'automation-workspace-guard.ps1',
      'automation-finalize-commit.ps1',
      'check-pending-whitespace.ps1',
      'check-task-cards.ps1'
    )) {
    Copy-Item `
      -LiteralPath (Join-Path $PSScriptRoot $toolName) `
      -Destination (Join-Path $repositoryRoot "tools\$toolName")
  }

  Write-Utf8 -Path (Join-Path $repositoryRoot 'AGENTS.md') -Text '# External wrapper test'
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/自动工作流规则.txt') -Text '# 自动工作流规则'
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/AI协作规则.txt') -Text '# AI协作规则'
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/DeepSeek工作提示词.txt') -Text '# DeepSeek工作提示词'
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/AI合作沟通.txt') -Text '# AI合作沟通'

  $metadata = [ordered]@{
    schemaVersion = 1
    id = 'TASK-EXT-001'
    title = '固定外部入口测试'
    priority = 'P0'
    route = 'external_execute'
    owner = 'deepseek'
    domain = 'automation'
    stage = 'verification'
    dispatchState = 'ready'
    blockedBy = @()
    stateReason = '验证固定外部入口'
    expectedPaths = @(
      'fixtures/generated-business.txt'
      '开发管理/任务列表/自动化任务.txt'
      '开发管理/当前任务队列.txt'
      '开发管理/AI合作沟通.txt'
      '开发管理/任务卡/TASK-EXT-001.txt'
      '开发管理/任务归档/TASK-EXT-001.txt'
    )
    sourceBacklog = '开发管理/任务列表/自动化任务.txt'
  }
  $cardText = @(
    '---TASK-META---'
    ($metadata | ConvertTo-Json -Depth 10)
    '---TASK-BODY---'
    '# TASK-EXT-001 · 固定外部入口测试'
    '## 来源与当前边界'
    '- 已由测试控制器选中。'
    '## 必查范围'
    '- 固定入口。'
    '## 实施范围'
    '- 新建测试文件。'
    '## 禁止项'
    '- 不访问其他仓库。'
    '## 验证'
    '- 运行直接检查。'
    '## 完成条件'
    '- 返回严格终态。'
    '## 停止条件'
    '- 门禁失败。'
  ) -join "`n"
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/任务卡/TASK-EXT-001.txt') -Text $cardText
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/当前任务队列.txt') -Text (@(
      '| ID | 路由 | 主责 | 优先级 | 领域 | 阶段 | 标题 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- | --- |'
      '| TASK-EXT-001 | external_execute | deepseek | P0 | automation | verification | 固定外部入口测试 | 开发管理/任务卡/TASK-EXT-001.txt |'
    ) -join "`n")
  Write-Utf8 -Path (Join-Path $repositoryRoot '开发管理/任务列表/自动化任务.txt') -Text (@(
      '| ID | 优先级 | 主责 | 状态投影 | 阻塞于 | 摘要 | 任务卡 |'
      '| --- | --- | --- | --- | --- | --- | --- |'
      '| TASK-EXT-001 | P0 | deepseek | 已排队 | — | 固定外部入口测试 | 开发管理/任务卡/TASK-EXT-001.txt |'
    ) -join "`n")

  Invoke-Git -Root $repositoryRoot -Arguments @('init') | Out-Null
  Invoke-Git -Root $repositoryRoot -Arguments @('config', 'user.name', 'External Wrapper Test') | Out-Null
  Invoke-Git -Root $repositoryRoot -Arguments @('config', 'user.email', 'external-wrapper@example.invalid') | Out-Null
  Invoke-Git -Root $repositoryRoot -Arguments @('add', '--', '.') | Out-Null
  Invoke-Git -Root $repositoryRoot -Arguments @('commit', '-m', 'test: initialize wrapper fixture') | Out-Null

  Write-Utf8 -Path (Join-Path $fakeBin 'fake-claude.ps1') -Text @'
param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$CliArguments
)
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
$inputText = [Console]::In.ReadToEnd()
$sessionFlag = if ($CliArguments -ccontains '--session-id') { '--session-id' } else { '--resume' }
$sessionIndex = [Array]::IndexOf($CliArguments, $sessionFlag)
$sessionId = if ($sessionIndex -ge 0 -and $sessionIndex + 1 -lt $CliArguments.Count) {
  $CliArguments[$sessionIndex + 1]
} else {
  $null
}
[IO.File]::WriteAllText(
  $env:TZG_FAKE_CLAUDE_RECORD,
  ([ordered]@{
    arguments = $CliArguments
    input = $inputText
    workingDirectory = [Environment]::CurrentDirectory
    baseUrl = $env:ANTHROPIC_BASE_URL
  } | ConvertTo-Json -Depth 10),
  [Text.UTF8Encoding]::new($false)
)
function New-TestCommits {
  param([switch]$InvalidMetadata)

  $verify = if ($InvalidMetadata) {
    '验证=外部 wrapper 测试通过'
  } else {
    '验证=外部 wrapper 测试通过；后续=等待 Codex 复审'
  }
  $message = "test: external business result`n`nAutomation: tzg-hourly-controller`nTask: TASK-EXT-001`nState: pending_review`nResult: 问题=缺少固定外部提交；完成=创建测试业务提交`nImpact: 影响=外部 wrapper 可核验实际元数据；边界=不修改生产任务`nVerify: $verify`nPlain: 发生=外部测试创建了可核验的业务提交；影响=只验证自动化收尾合同；需要=无需处理"
  & git commit --allow-empty -q -m $message
  if ($LASTEXITCODE -ne 0) { throw 'unable to create fake business commit' }
  $businessCommit = [string](& git rev-parse HEAD)
  & git commit --allow-empty -q -m 'test: external handoff result'
  if ($LASTEXITCODE -ne 0) { throw 'unable to create fake handoff commit' }
  [pscustomobject]@{
    BusinessCommit = $businessCommit
    HandoffCommit = [string](& git rev-parse HEAD)
  }
}
$structuredOutput = $null
$omitStructuredOutput = $false
switch ($env:TZG_FAKE_CLAUDE_MODE) {
  'invalid' {
    [Console]::Out.WriteLine('not-json')
  }
  'missing-structured-output' {
    $omitStructuredOutput = $true
  }
  'empty-structured-output' {
    $structuredOutput = [ordered]@{}
  }
  'completed-missing-field' {
    $structuredOutput = [ordered]@{
      status = 'completed'
      identity = 'DeepSeek V4 Flash'
    }
  }
  'decision-missing-field' {
    $structuredOutput = [ordered]@{
      status = 'needs_decision'
      decisionId = 'DEC-TEST'
      question = 'Choose one option.'
    }
  }
  'failed-missing-field' {
    $structuredOutput = [ordered]@{
      status = 'failed'
    }
  }
  'short-commit' {
    $structuredOutput = [ordered]@{
      status = 'completed'
      identity = 'DeepSeek V4 Flash'
      sessionId = $sessionId
      businessCommit = '0a9e847'
      handoffCommit = 'd9e95fc'
    }
  }
  'identity-mismatch' {
    $structuredOutput = [ordered]@{
      status = 'completed'
      identity = 'Claude Code'
      sessionId = $sessionId
      businessCommit = ('a' * 40)
      handoffCommit = ('b' * 40)
    }
  }
  'invalid-metadata' {
    $commits = New-TestCommits -InvalidMetadata
    $structuredOutput = [ordered]@{
      status = 'completed'
      identity = 'DeepSeek V4 Flash'
      sessionId = $sessionId
      businessCommit = $commits.BusinessCommit
      handoffCommit = $commits.HandoffCommit
    }
  }
  default {
    $commits = New-TestCommits
    $structuredOutput = [ordered]@{
      status = 'completed'
      identity = 'DeepSeek V4 Flash'
      sessionId = $sessionId
      businessCommit = $commits.BusinessCommit
      handoffCommit = $commits.HandoffCommit
    }
  }
}
if ($env:TZG_FAKE_CLAUDE_MODE -cne 'invalid') {
  $envelope = [ordered]@{
    type = 'result'
    subtype = 'success'
    is_error = $false
    session_id = $sessionId
  }
  if (-not $omitStructuredOutput) {
    $envelope.structured_output = $structuredOutput
  }
  [Console]::Out.WriteLine(($envelope | ConvertTo-Json -Compress -Depth 10))
}
'@
  Write-Utf8 -Path (Join-Path $fakeBin 'claude.cmd') -Text @'
@echo off
pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-claude.ps1" %*
'@

  $env:PATH = "$fakeBin;$originalPath"
  $env:ANTHROPIC_BASE_URL = 'http://127.0.0.1:15721/claude-desktop'
  $env:TZG_FAKE_CLAUDE_MODE = 'completed'
  $env:TZG_FAKE_CLAUDE_RECORD = $recordPath

  $acquireOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Acquire `
      -StateRoot $stateRoot `
      -TaskId 'TASK-EXT-001' `
      -Owner 'deepseek' `
      -RepositoryRoot $repositoryRoot)
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'fixture lease acquire failed'
  Assert-Equal -Actual $acquireOutput.Count -Expected 1 -Message 'fixture lease acquire output is invalid'
  $acquire = $acquireOutput[0] | ConvertFrom-Json
  Assert-Equal -Actual ([string]$acquire.status) -Expected 'ACQUIRED' -Message 'fixture lease was not acquired'
  $runId = [string]$acquire.runId

  $start = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual $start.ExitCode -Expected 0 -Message "valid wrapper start failed: $($start.Stderr)"
  Assert-Equal -Actual ([string]$start.Json.status) -Expected 'completed' -Message "wrapper start status mismatch: $($start.Stdout) stderr=$($start.Stderr)"
  Assert-Equal -Actual ([string]$start.Json.identity) -Expected 'DeepSeek V4 Flash' -Message 'wrapper identity mismatch'
  Assert-Equal -Actual ([string]$start.Json.runId) -Expected $runId -Message 'wrapper runId mismatch'
  Assert-Equal -Actual ([string]$start.Json.taskId) -Expected 'TASK-EXT-001' -Message 'wrapper taskId mismatch'
  Assert-True ([Guid]::TryParse([string]$start.Json.sessionId, [ref]([Guid]::Empty))) 'wrapper did not return a real sessionId'

  $record = Get-Content -Raw -LiteralPath $recordPath | ConvertFrom-Json -Depth 20
  $arguments = @($record.arguments | ForEach-Object { [string]$_ })
  Assert-True ($arguments -ccontains '--session-id') 'wrapper did not start a fixed session'
  Assert-True ($arguments -ccontains '--permission-mode') 'wrapper omitted permission mode'
  Assert-True ($arguments -ccontains 'dontAsk') 'wrapper permission mode is not dontAsk'
  Assert-True ($arguments -ccontains '--allowedTools') 'wrapper omitted allowed tools'
  Assert-True ($arguments -ccontains '--output-format') 'wrapper omitted official JSON output format'
  Assert-Equal `
    -Actual ([string]$arguments[[Array]::IndexOf($arguments, '--output-format') + 1]) `
    -Expected 'json' `
    -Message 'wrapper output format is not json'
  Assert-True ($arguments -ccontains '--json-schema') 'wrapper omitted the terminal JSON schema'
  $terminalSchema = [string]$arguments[[Array]::IndexOf($arguments, '--json-schema') + 1] | ConvertFrom-Json -Depth 20
  Assert-True (
    @($terminalSchema.properties.status.enum) -ccontains 'completed'
  ) 'wrapper terminal schema omitted completed'
  Assert-Equal `
    -Actual ([string]$terminalSchema.properties.businessCommit.pattern) `
    -Expected '[0-9a-f]{40}$' `
    -Message 'wrapper terminal schema did not require a full businessCommit SHA'
  Assert-Equal `
    -Actual ([int]$terminalSchema.properties.businessCommit.minLength) `
    -Expected 40 `
    -Message 'wrapper terminal schema businessCommit minimum length mismatch'
  Assert-Equal `
    -Actual ([int]$terminalSchema.properties.businessCommit.maxLength) `
    -Expected 40 `
    -Message 'wrapper terminal schema businessCommit maximum length mismatch'
  Assert-Equal `
    -Actual ([string]$terminalSchema.properties.handoffCommit.pattern) `
    -Expected '[0-9a-f]{40}$' `
    -Message 'wrapper terminal schema did not require a full handoffCommit SHA'
  Assert-Equal `
    -Actual ([int]$terminalSchema.properties.handoffCommit.minLength) `
    -Expected 40 `
    -Message 'wrapper terminal schema handoffCommit minimum length mismatch'
  Assert-Equal `
    -Actual ([int]$terminalSchema.properties.handoffCommit.maxLength) `
    -Expected 40 `
    -Message 'wrapper terminal schema handoffCommit maximum length mismatch'
  $allowedTools = [string]$arguments[[Array]::IndexOf($arguments, '--allowedTools') + 1]
  foreach ($requiredTool in @('Read', 'Edit', 'Write', 'Bash(git diff --check)')) {
    Assert-True ($allowedTools.Split(',') -ccontains $requiredTool) "allowed tools omitted $requiredTool"
  }
  Assert-True (-not $allowedTools.Contains('Bash(*)', [StringComparison]::Ordinal)) 'wrapper allowed wildcard Bash'
  Assert-True (
    [string]$record.input -match [regex]::Escape('TaskId: TASK-EXT-001')
  ) 'wrapper prompt omitted TaskId'
  Assert-True (
    [string]$record.input -match [regex]::Escape('fixtures/generated-business.txt')
  ) 'wrapper prompt omitted expected paths'
  Assert-True (
    [string]$record.input -match [regex]::Escape('the exact matching entry in 开发管理/未通过审核清单.txt')
  ) 'wrapper prompt omitted review-return evidence routing'
  Assert-True (
    [string]$record.input -match [regex]::Escape('-Postcondition ExternalPendingReview -OutputJson')
  ) 'wrapper prompt omitted the pre-commit pending-review check'
  Assert-True (
    [string]$record.input -match [regex]::Escape('full 40-character lowercase hexadecimal SHA')
  ) 'wrapper prompt omitted the full commit SHA requirement'
  Assert-True (
    [string]$record.input -match [regex]::Escape('exactly one -Paths <single path>')
  ) 'wrapper prompt omitted the single-path whitespace-check contract'
  Assert-True (
    [string]$record.input -match [regex]::Escape('Never pass comma-separated paths')
  ) 'wrapper prompt did not forbid Bash-to-PowerShell path arrays'
  Assert-True (
    [string]$record.input -match [regex]::Escape('Do not end the turn with progress narration')
  ) 'wrapper prompt did not forbid narration-only termination'
  foreach ($metadataTemplate in @(
      'AutomationResult 问题=<原问题>；完成=<具体交付>',
      'AutomationImpact 影响=<实际行为变化>；边界=<明确未涉及范围>',
      'AutomationVerify 验证=<关键检查与结果>；后续=<解锁项、剩余依赖或下一状态>',
      'AutomationPlain 发生=<负责人短句>；影响=<负责人短句>；需要=<负责人短句>'
    )) {
    Assert-True (
      [string]$record.input -match [regex]::Escape($metadataTemplate)
    ) "wrapper prompt omitted metadata template: $metadataTemplate"
  }
  $baselinePath = Join-Path $stateRoot "external-baselines\$runId.json"
  Assert-True (
    [string]$record.input -match [regex]::Escape($baselinePath)
  ) 'wrapper prompt omitted the private baseline path'
  Assert-True (Test-Path -LiteralPath (Split-Path -Parent $baselinePath) -PathType Container) 'wrapper did not create the baseline parent'
  Assert-True (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.claude'))) 'wrapper created a repository-local .claude path'

  $resumeSessionId = [Guid]::NewGuid().ToString()
  $resume = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId `
    -Action Resume `
    -SessionId $resumeSessionId `
    -DecisionOption A
  Assert-Equal -Actual $resume.ExitCode -Expected 0 -Message "valid wrapper resume failed: $($resume.Stderr)"
  Assert-Equal -Actual ([string]$resume.Json.sessionId) -Expected $resumeSessionId -Message 'wrapper resumed the wrong session'
  $resumeRecord = Get-Content -Raw -LiteralPath $recordPath | ConvertFrom-Json -Depth 20
  Assert-True (@($resumeRecord.arguments) -ccontains '--resume') 'wrapper resume omitted --resume'
  Assert-Equal -Actual ([string]$resumeRecord.input).Trim() -Expected 'A' -Message 'wrapper did not pass the decision option'

  $env:TZG_FAKE_CLAUDE_MODE = 'invalid'
  $invalid = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual ([string]$invalid.Json.status) -Expected 'failed' -Message 'invalid CLI output did not fail'
  Assert-Equal -Actual ([string]$invalid.Json.detailCode) -Expected 'external_invalid_terminal' -Message 'invalid CLI detailCode mismatch'

  foreach ($mode in @(
      'missing-structured-output',
      'empty-structured-output',
      'completed-missing-field',
      'decision-missing-field',
      'failed-missing-field'
    )) {
    $env:TZG_FAKE_CLAUDE_MODE = $mode
    $missingField = Invoke-Wrapper `
      -WrapperPath $wrapperPath `
      -Root $repositoryRoot `
      -StateRoot $stateRoot `
      -RunId $runId
    Assert-Equal `
      -Actual ([string]$missingField.Json.status) `
      -Expected 'failed' `
      -Message "$mode did not fail"
    Assert-Equal `
      -Actual ([string]$missingField.Json.detailCode) `
      -Expected 'external_invalid_terminal' `
      -Message "$mode detailCode mismatch"
  }

  $env:TZG_FAKE_CLAUDE_MODE = 'short-commit'
  $shortCommit = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual ([string]$shortCommit.Json.status) -Expected 'failed' -Message 'short commit SHAs did not fail'
  Assert-Equal -Actual ([string]$shortCommit.Json.detailCode) -Expected 'external_invalid_terminal' -Message 'short commit SHA detailCode mismatch'
  Assert-True (
    $shortCommit.Json.PSObject.Properties.Name -cnotcontains 'businessCommit'
  ) 'wrapper normalized a short businessCommit SHA'

  $env:TZG_FAKE_CLAUDE_MODE = 'invalid-metadata'
  $invalidMetadata = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual ([string]$invalidMetadata.Json.status) -Expected 'failed' -Message 'invalid business metadata did not fail'
  Assert-Equal -Actual ([string]$invalidMetadata.Json.detailCode) -Expected 'external_commit_metadata_invalid' -Message 'invalid business metadata detailCode mismatch'

  $env:TZG_FAKE_CLAUDE_MODE = 'identity-mismatch'
  $identityMismatch = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual ([string]$identityMismatch.Json.status) -Expected 'failed' -Message 'identity mismatch did not fail'
  Assert-Equal -Actual ([string]$identityMismatch.Json.detailCode) -Expected 'external_identity_mismatch' -Message 'identity mismatch detailCode mismatch'

  $releaseOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $leasePath `
      -Action Release `
      -StateRoot $stateRoot `
      -RunId $runId)
  Assert-Equal -Actual $LASTEXITCODE -Expected 0 -Message 'fixture lease release failed'
  Assert-Equal -Actual ([string](($releaseOutput[0] | ConvertFrom-Json).status)) -Expected 'RELEASED' -Message 'fixture lease was not released'
  if (Test-Path -LiteralPath $recordPath) {
    Remove-Item -LiteralPath $recordPath -Force
  }
  $leaseMismatch = Invoke-Wrapper `
    -WrapperPath $wrapperPath `
    -Root $repositoryRoot `
    -StateRoot $stateRoot `
    -RunId $runId
  Assert-Equal -Actual ([string]$leaseMismatch.Json.status) -Expected 'failed' -Message 'missing lease did not fail'
  Assert-Equal -Actual ([string]$leaseMismatch.Json.detailCode) -Expected 'external_lease_mismatch' -Message 'missing lease detailCode mismatch'
  Assert-True ($null -eq $leaseMismatch.Json.sessionId) 'wrapper invented a sessionId before CLI start'
  Assert-True (-not (Test-Path -LiteralPath $recordPath)) 'wrapper launched Claude CLI without the selected lease'

  Write-Output 'test-invoke-external-responsibility: OK'
} finally {
  $env:PATH = $originalPath
  $env:ANTHROPIC_BASE_URL = $originalBaseUrl
  $env:TZG_FAKE_CLAUDE_MODE = $originalMode
  $env:TZG_FAKE_CLAUDE_RECORD = $originalRecord
  if (Test-Path -LiteralPath $testRoot) {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $expectedPrefix = $temporaryBase + [IO.Path]::DirectorySeparatorChar
    if (
      -not $resolvedRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
      (Split-Path -Leaf $resolvedRoot) -cne "tzg-external-wrapper-test-$testId"
    ) {
      throw "refusing unsafe wrapper-test cleanup: $resolvedRoot"
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
  }
  if (Test-Path -LiteralPath $stateRoot) {
    $resolvedStateRoot = [IO.Path]::GetFullPath($stateRoot)
    $expectedStatePrefix = $approvedStateBase + [IO.Path]::DirectorySeparatorChar
    if (
      -not $resolvedStateRoot.StartsWith($expectedStatePrefix, [StringComparison]::OrdinalIgnoreCase) -or
      (Split-Path -Leaf $resolvedStateRoot) -cne "tzg-external-wrapper-test-$testId"
    ) {
      throw "refusing unsafe wrapper-state cleanup: $resolvedStateRoot"
    }
    Remove-Item -LiteralPath $resolvedStateRoot -Recurse -Force
  }
}
