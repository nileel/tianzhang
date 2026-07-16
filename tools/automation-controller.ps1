#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)]
  [ValidateSet('Contract','Start','InspectCandidate','RegisterCandidate','BeginMutation','Renew','Finish','CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure','PrepareDecision','CreateDecision','SendDecisionNotification','ConsumeDecisionReply','PrepareDecisionNotification','MarkDecisionSubmitted','RetryDecisionNotification','MarkDecisionDeliveryFailed','ResolveDecisionEmailReply','ResolveDecisionManual')]
  [string]$Action,
  [string]$RepositoryRoot = (Get-Location).Path,
  [string]$StatePath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.json",
  [string]$RunRoot = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller-runs",
  [string]$RunId,
  [string]$ActualModel,
  [string]$WorkType,
  [string]$TaskId,
  [string]$Executor,
  [string]$ExpectedPaths,
  [string]$CommitMessage,
  [string]$ErrorMessage,
  [string]$QueueFingerprint,
  [int]$RunnableCount = -1,
  [switch]$QueueAuditCompleted,
  [switch]$NoCandidate,
  [string]$WorkerError,
  [int]$BackoffMinutes = 180,
  [string]$TaskSummary,
  [string]$DecisionQuestion,
  [string]$DecisionOptions,
  [string]$RecommendedOption,
  [string]$ImpactSummary,
  [string]$ReplyText,
  [string]$DecisionId,
  [string]$NotificationError,
  [string]$PrivateConfigPath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.private.json",
  [string]$FeishuConfigPath = "$env:USERPROFILE\.codex\automation-state\tzg-hourly-controller.feishu.private.json",
  [string]$FeishuBridgeRoot = "$env:USERPROFILE\.codex\automation-state\tzg-feishu-decision-bridge",
  [string]$NodeExecutable = 'node',
  [string]$FeishuSenderScript = (Join-Path $PSScriptRoot 'feishu-decision-bridge\src\send-decision.mjs'),
  [string]$FeishuConsumerScript = (Join-Path $PSScriptRoot 'feishu-decision-bridge\src\consume-reply.mjs'),
  [string]$ProviderMessageId,
  [string]$PriorProviderMessageId,
  [string]$ObservedRecipient,
  [string]$ReplyMessageId,
  [string]$ReplyFrom,
  [string]$CurrentThreadId,
  [string]$CurrentTurnId,
  [switch]$ManualOverride,
  [int]$LeaseMinutes = 180,
  [string]$Now
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')
$script:ProtocolVersion = 1
$script:StateTool = Join-Path $PSScriptRoot 'automation-controller-state.ps1'
$script:GuardTool = Join-Path $PSScriptRoot 'automation-workspace-guard.ps1'
$script:FinalizerTool = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
$script:DecisionStatusTool = Join-Path $PSScriptRoot 'automation-decision-status.ps1'
$script:DecisionStatusRelativePath = '开发管理/自动工作流状态.txt'
$script:ExecutionQueueRelativePath = '开发管理/当前任务队列.txt'
$script:ExecutionCandidateResolver = 'current_task_queue_execution'
$script:FeishuSenderScriptExplicit = $PSBoundParameters.ContainsKey('FeishuSenderScript')
$script:FeishuConsumerScriptExplicit = $PSBoundParameters.ContainsKey('FeishuConsumerScript')
$script:ControllerBoundParameterNames = @($PSBoundParameters.Keys)
$script:LegacyDecisionActions = @(
  'PrepareDecisionNotification', 'MarkDecisionSubmitted', 'RetryDecisionNotification',
  'MarkDecisionDeliveryFailed', 'ResolveDecisionEmailReply'
)
$script:TaskKindMapping = [ordered]@{
  execution = 'execute'
  review = 'review'
  maintenance = 'maintenance'
  recovery = 'recovery'
}

function Invoke-ChildPowerShell {
  param([string]$ScriptPath, [string[]]$Arguments)

  $positionals = [Collections.Generic.List[string]]::new()
  $parameters = [ordered]@{}
  $switchNames = @('WasRecovery', 'QueueAuditCompleted', 'NoCandidate', 'ManualOverride')
  for ($index = 0; $index -lt $Arguments.Count; $index++) {
    $token = [string]$Arguments[$index]
    if (-not $token.StartsWith('-', [StringComparison]::Ordinal)) {
      $positionals.Add($token)
      continue
    }
    $name = $token.Substring(1)
    if ($switchNames -contains $name) {
      $parameters[$name] = $true
      continue
    }
    if ($index + 1 -ge $Arguments.Count) { throw "Missing value for child parameter: $token" }
    $parameters[$name] = [string]$Arguments[++$index]
  }
  $request = [pscustomobject]@{
    scriptPath = $ScriptPath
    positionals = @($positionals)
    parameters = [pscustomobject]$parameters
  } | ConvertTo-Json -Depth 4 -Compress
  $requestBase64 = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes($request))
  $command = @"
`$utf8 = [Text.UTF8Encoding]::new(`$false)
[Console]::OutputEncoding = `$utf8
`$OutputEncoding = `$utf8
`$requestJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$requestBase64'))
`$request = `$requestJson | ConvertFrom-Json -DateKind String
`$childPositionals = @(`$request.positionals | ForEach-Object { [string]`$_ })
`$childParameters = @{}
foreach (`$property in `$request.parameters.PSObject.Properties) { `$childParameters[[string]`$property.Name] = `$property.Value }
& ([string]`$request.scriptPath) @childPositionals @childParameters
exit `$LASTEXITCODE
"@
  $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = (Get-Process -Id $PID).Path
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
  $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
  $startInfo.CreateNoWindow = $true
  $allArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encodedCommand)
  foreach ($argument in $allArguments) { $startInfo.ArgumentList.Add([string]$argument) }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  try {
    if (-not $process.Start()) { throw "Unable to start child PowerShell: $ScriptPath" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [pscustomobject]@{
      Code = $process.ExitCode
      Output = $stdoutTask.GetAwaiter().GetResult().Trim()
      Error = $stderrTask.GetAwaiter().GetResult().Trim()
    }
  } finally {
    $process.Dispose()
  }
}

function Invoke-StateTool {
  param([string[]]$Arguments)
  Invoke-ChildPowerShell $script:StateTool (@($Arguments) + @('-StatePath', $StatePath, '-LeaseMinutes', [string]$LeaseMinutes) + $(if ($Now) { @('-Now', $Now) } else { @() }))
}

function Invoke-GuardTool {
  param([string[]]$Arguments)
  Invoke-ChildPowerShell $script:GuardTool (@($Arguments) + @('-RepositoryRoot', $RepositoryRoot))
}

function Get-FileSha256Lower {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "File does not exist: $Path" }
  (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-DecisionStatusPath {
  Join-Path $RepositoryRoot $script:DecisionStatusRelativePath
}

function Get-DecisionStatusProjection {
  param($State)

  $pending = $null
  if ($null -ne $State.pendingDecision) {
    $notificationAttempts = @($State.pendingDecision.notificationAttempts)
    $notificationProvider = if ($notificationAttempts.Count -gt 0) {
      [string]$notificationAttempts[$notificationAttempts.Count - 1].provider
    } else {
      $null
    }
    $pending = [ordered]@{
      decisionId = [string]$State.pendingDecision.decisionId
      createdAt = [string]$State.pendingDecision.createdAt
      taskId = [string]$State.pendingDecision.taskId
      taskSummary = [string]$State.pendingDecision.taskSummary
      question = [string]$State.pendingDecision.question
      options = @($State.pendingDecision.options | ForEach-Object {
        [ordered]@{ key = [string]$_.key; label = [string]$_.label }
      })
      recommendedOption = [string]$State.pendingDecision.recommendedOption
      status = [string]$State.pendingDecision.status
    }
    if (-not [string]::IsNullOrWhiteSpace($notificationProvider)) {
      $pending['notificationProvider'] = $notificationProvider
    }
  }
  $flow = $null
  if ($null -ne $State.decisionFlow) {
    $flow = [ordered]@{
      taskId = [string]$State.decisionFlow.taskId
      status = [string]$State.decisionFlow.status
      resolvedDecisions = @($State.decisionFlow.resolvedDecisions | ForEach-Object {
        [ordered]@{
          decisionId = [string]$_.decisionId
          resolution = [ordered]@{
            optionKey = [string]$_.resolution.optionKey
            source = [string]$_.resolution.source
          }
        }
      })
    }
  }
  [ordered]@{ pendingDecision = $pending; decisionFlow = $flow }
}

function Invoke-DecisionStatusPublisher {
  param(
    [ValidateSet('PublishPending', 'PublishImplementationPending', 'Clear')]
    [string]$PublisherAction,
    [AllowNull()][object]$State
  )

  $arguments = @($PublisherAction, '-StatusPath', (Get-DecisionStatusPath))
  if ($PublisherAction -ne 'Clear') {
    if ($null -eq $State) { throw 'State is required for decision status publishing.' }
    $json = (Get-DecisionStatusProjection $State) | ConvertTo-Json -Depth 8 -Compress
    $base64 = [Convert]::ToBase64String([Text.UTF8Encoding]::new($false).GetBytes($json))
    $arguments += @('-DecisionStateJsonBase64', $base64)
  }
  Invoke-ChildPowerShell $script:DecisionStatusTool $arguments
}

function Get-Sha256TextLower {
  param([string]$Value)
  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Value)
  try { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
  finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

function Test-EmailAddressShape {
  param([string]$Value)
  -not [string]::IsNullOrWhiteSpace($Value) -and
    $Value -cmatch '^[A-Za-z0-9.!#$%&''*+/=?^_`{|}~-]+@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$'
}

function Normalize-EmailTarget {
  param([string]$Value)
  $Value.Trim().ToLowerInvariant()
}

function Read-PrivateDecisionConfig {
  if (-not (Test-Path -LiteralPath $PrivateConfigPath -PathType Leaf)) { throw 'Private decision configuration is missing.' }
  try {
    $json = [IO.File]::ReadAllText($PrivateConfigPath, [Text.UTF8Encoding]::new($false, $true))
    $config = $json | ConvertFrom-Json -DateKind String
  } catch {
    throw "Private decision configuration is invalid UTF-8 JSON: $($_.Exception.Message)"
  }
  if ($null -eq $config -or [int]$config.schemaVersion -ne 1) { throw 'Private decision configuration schemaVersion must be 1.' }
  if (-not (Test-EmailAddressShape ([string]$config.recipientEmail)) -or [string]$config.recipientEmail -ceq 'me') {
    throw 'Private recipientEmail must be one explicit email address.'
  }
  if (-not (Test-EmailAddressShape ([string]$config.allowedReplyFrom))) {
    throw 'Private allowedReplyFrom must be one explicit email address.'
  }
  $aliases = @()
  if ($null -ne $config.aliases) {
    if ($config.aliases -is [string] -or $config.aliases -isnot [System.Array]) { throw 'Private aliases must be an array.' }
    foreach ($alias in @($config.aliases)) {
      if (-not (Test-EmailAddressShape ([string]$alias))) { throw 'Every private reply alias must be an email address.' }
      $aliases += Normalize-EmailTarget ([string]$alias)
    }
  }
  [pscustomobject]@{
    recipientEmail = [string]$config.recipientEmail
    recipientNormalized = Normalize-EmailTarget ([string]$config.recipientEmail)
    allowedReplyFrom = Normalize-EmailTarget ([string]$config.allowedReplyFrom)
    aliases = @($aliases)
    gmailLabel = [string]$config.gmailLabel
  }
}

function Set-SessionNotificationContext {
  param($Session, [AllowNull()][object]$Context)
  if ($null -eq $Session.PSObject.Properties['notificationContext']) {
    $Session | Add-Member -NotePropertyName notificationContext -NotePropertyValue $Context
  } else {
    $Session.notificationContext = $Context
  }
  Save-Session $Session
}

function New-PreparedNotificationResult {
  param($Session, $State, $Config)
  if ($null -eq $State.pendingDecision) { throw 'An active pending decision is required.' }
  $attemptNumber = @($State.pendingDecision.notificationAttempts).Count + 1
  if ($attemptNumber -gt 3 -or [string]$State.pendingDecision.status -ceq 'RETRY_EXHAUSTED') { return $null }
  $context = [pscustomobject]@{
    decisionId = [string]$State.pendingDecision.decisionId
    normalizedTargetHash = Get-Sha256TextLower ([string]$Config.recipientNormalized)
    preparedAt = if ($Now) { ([DateTimeOffset]::Parse($Now)).ToString('o') } else { [DateTimeOffset]::UtcNow.ToString('o') }
    attemptNumber = $attemptNumber
  }
  Set-SessionNotificationContext $Session $context
  [ordered]@{
    decisionId = [string]$State.pendingDecision.decisionId
    subject = "Decision required: $([string]$State.pendingDecision.decisionId)"
    body = "$([string]$State.pendingDecision.question)`nReply exactly: $([string]$State.pendingDecision.decisionId)：选择 $([string]$State.pendingDecision.recommendedOption)"
    recipientEmail = [string]$Config.recipientEmail
    gmailLabel = [string]$Config.gmailLabel
    attemptNumber = $attemptNumber
  }
}

function Get-NotificationErrorCategory {
  param([string]$Value)
  $category = ([regex]::Replace($Value.Trim().ToLowerInvariant(), '[^a-z0-9]+', '_')).Trim('_')
  if ([string]::IsNullOrWhiteSpace($category) -or $category[0] -notmatch '[a-z]') { $category = "connector_$category".TrimEnd('_') }
  if ($category.Length -gt 120) { $category = $category.Substring(0, 120).TrimEnd('_') }
  if ([string]::IsNullOrWhiteSpace($category)) { 'connector_failure' } else { $category }
}

function Get-DecisionCommandContract {
  [ordered]@{
    requiredParameters = @('TaskSummary', 'DecisionQuestion', 'DecisionOptions', 'RecommendedOption', 'ImpactSummary')
    optionFormat = 'A=label|B=label|C=label'
    template = 'CreateDecision -RepositoryRoot $RepositoryRoot -RunId $runId -TaskSummary $summary -DecisionQuestion $question -DecisionOptions ''A=label|B=label|C=label'' -RecommendedOption A -ImpactSummary $impact'
  }
}

function Convert-ChildJson {
  param($Result, [string]$Label)

  if ([string]::IsNullOrWhiteSpace([string]$Result.Output)) {
    $detail = if ([string]::IsNullOrWhiteSpace([string]$Result.Error)) { '<no stderr>' } else { [string]$Result.Error }
    throw "$Label returned no JSON: $detail"
  }
  try { $Result.Output | ConvertFrom-Json } catch { throw "$Label returned invalid JSON: $($Result.Output)" }
}

function New-ProtocolResult {
  param(
    [bool]$Ok,
    [string]$NextAction,
    [string]$BranchKind,
    [string]$FailurePolicy,
    [AllowNull()][string]$ErrorCode,
    [AllowNull()][string]$Message
  )

  $result = [ordered]@{
    protocolVersion = $script:ProtocolVersion
    ok = $Ok
    action = $NextAction
    runId = if ([string]::IsNullOrWhiteSpace($RunId)) { $null } else { $RunId }
    branchKind = $BranchKind
    taskId = $null
    executor = $null
    expectedPaths = @()
    requiredSources = @()
    requiredChecks = @()
    discoveryPolicy = $null
    nextCommand = $null
    failurePolicy = $FailurePolicy
    errorCode = $ErrorCode
    message = $Message
  }
  if ($script:LegacyDecisionActions -contains $Action) { $result['legacyOnly'] = $true }
  $result
}

function Write-ProtocolResult {
  param([System.Collections.IDictionary]$Result, [int]$ExitCode = 0)

  [pscustomobject]$Result | ConvertTo-Json -Depth 8 -Compress
  exit $ExitCode
}

function Get-SessionPath {
  if ([string]::IsNullOrWhiteSpace($RunId)) { throw 'RunId is required' }
  Join-Path $RunRoot "$RunId.json"
}

function Read-Session {
  $path = Get-SessionPath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Run session does not exist: $RunId" }
  $session = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
  if ($session.protocolVersion -ne $script:ProtocolVersion -or $session.runId -ne $RunId) {
    throw 'Run session is invalid or belongs to another run.'
  }
  $session
}

function Save-Session {
  param($Session)
  Write-JsonAtomically $Session (Get-SessionPath)
}

function Remove-SessionFile {
  $path = Get-SessionPath
  if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}

function Write-JsonAtomically {
  param([object]$Value, [string]$Path)

  $directory = Split-Path -Parent $Path
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
  $temporary = Join-Path $directory ('.' + [IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
  try {
    [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
    if ([IO.File]::Exists($Path)) {
      $backup = "$Path.backup"
      Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
      [IO.File]::Replace($temporary, $Path, $backup, $true)
      Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    } else {
      [IO.File]::Move($temporary, $Path)
    }
  } finally {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
  }
}

function Initialize-PrivateDirectory {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-PrivatePathAcl -Path $Path -Directory
    return
  }
  $security = Get-Acl -LiteralPath $Path
  $allowed = @((Get-PrivateAclSids).Value)
  $rules = @($security.Access)
  if (-not $security.AreAccessRulesProtected -or $rules.Count -ne 2) {
    throw 'Private directory ACL is unsafe.'
  }
  foreach ($rule in $rules) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if ($sid -notin $allowed -or $rule.IsInherited -or
        $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
        ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl) {
      throw 'Private directory ACL is unsafe.'
    }
  }
}

function Test-Hex64 {
  param([AllowNull()][object]$Value)
  $Value -is [string] -and $Value -cmatch '^[0-9a-f]{64}$'
}

function Test-ExactKeys {
  param([Collections.IDictionary]$Value, [string[]]$Expected)

  if ($null -eq $Value -or $Value.Count -ne $Expected.Count) { return $false }
  foreach ($key in $Expected) {
    if (-not $Value.Contains($key)) { return $false }
  }
  $true
}

function ConvertTo-ExactUtcIso {
  param([DateTimeOffset]$Value)
  $Value.UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-ControllerNowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  try { [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture) }
  catch { throw 'Controller time is invalid.' }
}

function Assert-FeishuCliOverrideSafe {
  param([string]$ScriptPath, [bool]$WasExplicit)

  if (-not $WasExplicit) { return }
  $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  foreach ($candidate in @($RunRoot, $FeishuConfigPath, $FeishuBridgeRoot, $ScriptPath)) {
    $full = [IO.Path]::GetFullPath($candidate)
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw 'Feishu CLI script overrides are restricted to temporary test paths.'
    }
  }
}

function Invoke-FeishuDecisionCli {
  param(
    [string]$ScriptPath,
    [bool]$ScriptWasExplicit,
    [object]$Request
  )

  $script:LastFeishuCliStage = 'path_validation'
  Assert-FeishuCliOverrideSafe $ScriptPath $ScriptWasExplicit
  $resolvedScript = [IO.Path]::GetFullPath($ScriptPath)
  $resolvedConfig = [IO.Path]::GetFullPath($FeishuConfigPath)
  $script:LastFeishuCliStage = 'script_validation'
  if (-not (Test-Path -LiteralPath $resolvedScript -PathType Leaf)) { throw 'Feishu CLI invocation failed.' }
  $script:LastFeishuCliStage = 'runtime_resolution'
  try {
    $node = Get-Command -Name $NodeExecutable -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $nodePath = [string]$node.Source
  } catch {
    throw 'Feishu CLI invocation failed.'
  }

  $requestDirectory = Join-Path ([IO.Path]::GetFullPath($RunRoot)) '.feishu-requests'
  $script:LastFeishuCliStage = 'request_acl'
  Initialize-PrivateDirectory $requestDirectory
  $requestPath = Join-Path $requestDirectory ('.request-' + [guid]::NewGuid().ToString('N') + '.json')
  try {
    Write-JsonAtomically $Request $requestPath
    Set-PrivatePathAcl $requestPath

    $script:LastFeishuCliStage = 'process_configuration'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $nodePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [Text.UTF8Encoding]::new($false, $true)
    $startInfo.StandardErrorEncoding = [Text.UTF8Encoding]::new($false, $true)
    $startInfo.CreateNoWindow = $true
    $startInfo.ArgumentList.Add($resolvedScript)
    $startInfo.ArgumentList.Add('--request-file')
    $startInfo.ArgumentList.Add($requestPath)
    $startInfo.Environment['FEISHU_DECISION_CONFIG_PATH'] = $resolvedConfig

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
      $script:LastFeishuCliStage = 'process_start'
      if (-not $process.Start()) { throw 'Feishu CLI invocation failed.' }
      $stdoutTask = $process.StandardOutput.ReadToEndAsync()
      $stderrTask = $process.StandardError.ReadToEndAsync()
      $script:LastFeishuCliStage = 'process_wait'
      $process.WaitForExit()
      $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
      [void]$stderrTask.GetAwaiter().GetResult()
      if ($stdout.Length -gt 16KB -or @($stdout -split '\r?\n').Count -ne 1) {
        throw 'Feishu CLI invocation failed.'
      }
      $script:LastFeishuCliStage = 'output_parse'
      try { $payload = $stdout | ConvertFrom-Json -AsHashtable -DateKind String }
      catch { throw 'Feishu CLI invocation failed.' }
      $script:LastFeishuCliStage = 'complete'
      [pscustomobject]@{ Code = $process.ExitCode; Payload = $payload }
    } finally {
      $process.Dispose()
    }
  } finally {
    Remove-Item -LiteralPath $requestPath -Force -ErrorAction SilentlyContinue
  }
}

function Assert-NoModelDecisionEvidence {
  param([string]$CliAction)

  $forbidden = @(
    'TaskId', 'TaskSummary', 'DecisionQuestion', 'DecisionOptions', 'RecommendedOption',
    'ImpactSummary', 'DecisionId', 'ReplyText', 'NotificationError', 'ProviderMessageId',
    'PriorProviderMessageId', 'ObservedRecipient', 'ReplyMessageId', 'ReplyFrom',
    'CurrentThreadId', 'CurrentTurnId', 'ManualOverride'
  )
  if (@($forbidden | Where-Object { $script:ControllerBoundParameterNames -contains $_ }).Count -gt 0) {
    throw "$CliAction reads decision and provider evidence only from locked state."
  }
}

function Get-FeishuDecisionRoute {
  param($PendingDecision)

  $attempts = @($PendingDecision.notificationAttempts | Where-Object { [string]$_.provider -ceq 'feishu' })
  if (@($attempts | Where-Object { [string]$_.result -ceq 'PROVIDER_OUTCOME_UNKNOWN' }).Count -gt 0) {
    return [ordered]@{
      nextCommand = 'CompleteNoChange'
      nextCommands = @('CompleteNoChange', 'ResolveDecisionManual')
      route = 'manual_reconciliation'
    }
  }
  if (@($attempts | Where-Object { [string]$_.result -ceq 'PROVIDER_ACCEPTED' }).Count -gt 0) {
    return [ordered]@{
      nextCommand = 'ConsumeDecisionReply'
      nextCommands = @('ConsumeDecisionReply', 'ResolveDecisionManual')
      route = 'consume_reply'
    }
  }
  $failureCount = @($attempts | Where-Object { [string]$_.result -in @('DELIVERY_FAILED', 'MISADDRESSED') }).Count
  if ($failureCount -ge 3) {
    return [ordered]@{
      nextCommand = 'CompleteNoChange'
      nextCommands = @('CompleteNoChange', 'ResolveDecisionManual')
      route = 'retry_exhausted'
    }
  }
  [ordered]@{
    nextCommand = 'SendDecisionNotification'
    nextCommands = @('SendDecisionNotification', 'ResolveDecisionManual')
    route = 'send_notification'
  }
}

function Get-DecisionBindingWindow {
  param($PendingDecision)

  $issuedAt = Get-ControllerNowValue
  if ($null -ne $PendingDecision.PSObject.Properties['expiresAt'] -and
      -not [string]::IsNullOrWhiteSpace([string]$PendingDecision.expiresAt)) {
    try { $expiresAt = [DateTimeOffset]::Parse([string]$PendingDecision.expiresAt, [Globalization.CultureInfo]::InvariantCulture) }
    catch { throw 'Pending decision expiry is invalid.' }
  } else {
    $expiresAt = $issuedAt.AddDays(7)
  }
  if ($expiresAt -le $issuedAt) { throw 'Pending decision has expired.' }
  [ordered]@{
    issuedAt = ConvertTo-ExactUtcIso $issuedAt
    expiresAt = ConvertTo-ExactUtcIso $expiresAt
  }
}

function Write-FeishuPendingBinding {
  param($PendingDecision, [string]$CardNonceHash, [string]$ProviderMessageIdHash, $Window)

  $root = [IO.Path]::GetFullPath($FeishuBridgeRoot)
  Initialize-PrivateDirectory $root
  $binding = [ordered]@{
    kind = 'decision_reply'
    decisionId = [string]$PendingDecision.decisionId
    allowedOptions = @($PendingDecision.options | ForEach-Object { [string]$_.key })
    issuedAt = [string]$Window.issuedAt
    expiresAt = [string]$Window.expiresAt
    cardNonceHash = $CardNonceHash
    providerMessageIdHash = $ProviderMessageIdHash
  }
  $path = Join-Path $root 'pending-bindings.json'
  Write-JsonAtomically @($binding) $path
  Set-PrivatePathAcl $path
}

function Read-FeishuPendingBinding {
  param($PendingDecision)

  $path = Join-Path ([IO.Path]::GetFullPath($FeishuBridgeRoot)) 'pending-bindings.json'
  if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -gt 64KB) {
    throw 'Feishu pending binding is unavailable.'
  }
  try {
    $value = [IO.File]::ReadAllText($path, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable -DateKind String
  } catch {
    throw 'Feishu pending binding is invalid.'
  }
  if ($value -isnot [System.Array]) { $value = @($value) }
  $matches = @($value | Where-Object { $_ -is [Collections.IDictionary] -and [string]$_['decisionId'] -ceq [string]$PendingDecision.decisionId })
  if ($matches.Count -ne 1) { throw 'Feishu pending binding is invalid.' }
  $binding = $matches[0]
  $expected = @('kind','decisionId','allowedOptions','issuedAt','expiresAt','cardNonceHash','providerMessageIdHash')
  if (-not (Test-ExactKeys $binding $expected) -or [string]$binding.kind -cne 'decision_reply' -or
      (@($binding.allowedOptions) -join '|') -cne 'A|B|C' -or
      -not (Test-Hex64 $binding.cardNonceHash) -or -not (Test-Hex64 $binding.providerMessageIdHash)) {
    throw 'Feishu pending binding is invalid.'
  }
  try {
    $issuedAt = [DateTimeOffset]::Parse([string]$binding.issuedAt, [Globalization.CultureInfo]::InvariantCulture)
    $expiresAt = [DateTimeOffset]::Parse([string]$binding.expiresAt, [Globalization.CultureInfo]::InvariantCulture)
    if ($expiresAt -le $issuedAt) { throw 'invalid' }
  } catch {
    throw 'Feishu pending binding is invalid.'
  }
  [pscustomobject]@{
    decisionId = [string]$binding.decisionId
    allowedOptions = @($binding.allowedOptions)
    issuedAt = ConvertTo-ExactUtcIso $issuedAt
    expiresAt = ConvertTo-ExactUtcIso $expiresAt
    cardNonceHash = [string]$binding.cardNonceHash
    providerMessageIdHash = [string]$binding.providerMessageIdHash
  }
}

function Test-ValidRunId {
  param([string]$Value)

  $parsed = [guid]::Empty
  [guid]::TryParse($Value, [ref]$parsed) -and $parsed -ne [guid]::Empty
}

function Close-StartFailure {
  param([string]$Code, [string]$Message, [int]$ExitCode = 1)

  if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    [void](Invoke-StateTool @('AbortClean', '-RunId', $RunId, '-ErrorMessage', $Message))
  }
  $result = New-ProtocolResult $false 'stopped' 'none' 'close_empty_run' $Code $Message
  Write-ProtocolResult $result $ExitCode
}

function Close-EmptyRun {
  param([string]$Code, [string]$Message, [int]$ExitCode, [string]$FailurePolicy = 'close_empty_run')

  $failed = Invoke-StateTool @('AbortClean', '-RunId', $RunId, '-ErrorMessage', $Message)
  if ($failed.Code -ne 0) {
    $result = New-ProtocolResult $false 'stopped' 'none' 'preserve_recovery' 'fail_close_error' $(if ($failed.Error) { $failed.Error } else { 'State Fail failed.' })
    Write-ProtocolResult $result $failed.Code
  }
  Remove-SessionFile
  $result = New-ProtocolResult $false 'stopped' 'none' $FailurePolicy $Code $Message
  Write-ProtocolResult $result $ExitCode
}

function Get-BranchSources {
  param([string]$Branch, [string]$SelectedExecutor)

  $sources = [Collections.Generic.List[string]]::new()
  switch ($Branch) {
    'execution' {
      $sources.Add('开发管理/当前任务队列.txt')
      $sources.Add('开发管理/AI协作规则.txt')
    }
    'review' { $sources.Add('开发管理/审核入口.txt') }
    'maintenance' {
      $sources.Add('开发管理/状态与建议维护规则.txt')
      $sources.Add('开发管理/当前任务队列.txt')
    }
    'recovery' {
      $sources.Add('开发管理/当前任务队列.txt')
      $sources.Add('开发管理/AI协作规则.txt')
    }
  }
  if ($SelectedExecutor -eq 'deepseek') {
    if (-not $sources.Contains('开发管理/AI协作规则.txt')) { $sources.Add('开发管理/AI协作规则.txt') }
    $sources.Add('开发管理/DeepSeek工作提示词.txt')
  }
  @($sources | Select-Object -Unique)
}

function Get-ExecutorForQueueOwner {
  param([string]$Owner)

  $normalizedOwner = ([regex]::Replace(([string]$Owner).Trim().TrimEnd('。'), '\s*/\s*', ' / ')).Trim()
  if (@(
      'Codex',
      'ChatGPT5.5',
      'gpt-5.5',
      'Codex / ChatGPT5.5',
      'Codex / gpt-5.5'
    ) -contains $normalizedOwner) {
    return 'codex'
  }
  if (@(
      'DeepSeek V4 Pro',
      'Claude Code',
      'Claude / DeepSeek'
    ) -contains $normalizedOwner) {
    return 'deepseek'
  }
  $null
}

function Resolve-ExecutionQueueCandidate {
  param([string]$SelectedTaskId)

  if ($SelectedTaskId -notmatch '^[A-Za-z0-9][A-Za-z0-9-]*$') {
    return [pscustomobject]@{
      Ok = $false; Fatal = $false; ErrorCode = 'candidate_not_found'
      Message = 'TaskId is not a valid current-queue identifier.'
    }
  }

  $queuePath = Join-Path $RepositoryRoot $script:ExecutionQueueRelativePath
  if (-not (Test-Path -LiteralPath $queuePath -PathType Leaf)) {
    return [pscustomobject]@{
      Ok = $false; Fatal = $true; ErrorCode = 'queue_source_missing'
      Message = "Current task queue is missing: $($script:ExecutionQueueRelativePath)"
    }
  }

  try {
    $lines = [IO.File]::ReadAllLines($queuePath, [Text.UTF8Encoding]::new($false, $true))
  } catch {
    return [pscustomobject]@{
      Ok = $false; Fatal = $true; ErrorCode = 'queue_source_invalid'
      Message = "Current task queue could not be read as UTF-8: $($_.Exception.Message)"
    }
  }

  $insideTable = $false
  $rows = [Collections.Generic.List[object]]::new()
  foreach ($line in $lines) {
    $trimmed = ([string]$line).Trim()
    if ($trimmed -eq '## 队列表头') {
      $insideTable = $true
      continue
    }
    if ($insideTable -and $trimmed -match '^##\s+') { break }
    if (-not $insideTable -or -not $trimmed.StartsWith('|', [StringComparison]::Ordinal)) { continue }

    $cells = @(($trimmed.Trim('|') -split '\|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -ne 6) {
      return [pscustomobject]@{
        Ok = $false; Fatal = $true; ErrorCode = 'queue_source_invalid'
        Message = 'Current task queue contains a malformed table row.'
      }
    }
    if ($cells[0] -eq 'ID' -or $cells[0] -match '^-+$') { continue }
    $rows.Add([pscustomobject]@{
      TaskId = [string]$cells[0]
      Priority = [string]$cells[1]
      Owner = [string]$cells[2]
      BusinessType = [string]$cells[3]
      Status = [string]$cells[4]
      Summary = [string]$cells[5]
    })
  }

  if (-not $insideTable) {
    return [pscustomobject]@{
      Ok = $false; Fatal = $true; ErrorCode = 'queue_source_invalid'
      Message = 'Current task queue does not contain the queue table section.'
    }
  }

  $matches = @($rows | Where-Object { [string]$_.TaskId -ceq $SelectedTaskId })
  if ($matches.Count -eq 0) {
    return [pscustomobject]@{
      Ok = $false; Fatal = $false; ErrorCode = 'candidate_not_found'
      Message = 'TaskId is not present in the current task queue.'
    }
  }
  if ($matches.Count -ne 1) {
    return [pscustomobject]@{
      Ok = $false; Fatal = $true; ErrorCode = 'queue_source_invalid'
      Message = 'Current task queue contains duplicate task identifiers.'
    }
  }

  $row = $matches[0]
  if ([string]$row.Status -cne '待处理') {
    return [pscustomobject]@{
      Ok = $false; Fatal = $false; ErrorCode = 'candidate_not_runnable'
      Message = "Queue task status is not runnable: $([string]$row.Status)"
    }
  }

  $mappedExecutor = Get-ExecutorForQueueOwner ([string]$row.Owner)
  if ([string]::IsNullOrWhiteSpace([string]$mappedExecutor)) {
    return [pscustomobject]@{
      Ok = $false; Fatal = $false; ErrorCode = 'candidate_executor_unmapped'
      Message = "Queue task owner does not map to a configured executor: $([string]$row.Owner)"
    }
  }

  [pscustomobject]@{
    Ok = $true
    Fatal = $false
    ErrorCode = $null
    Message = $null
    WorkType = 'execution'
    TaskId = [string]$row.TaskId
    Executor = $mappedExecutor
    Owner = [string]$row.Owner
    BusinessType = [string]$row.BusinessType
    Status = [string]$row.Status
    Summary = [string]$row.Summary
  }
}

function Get-ExternalWorkType {
  param([string]$InternalTaskKind)

  foreach ($entry in $script:TaskKindMapping.GetEnumerator()) {
    if ([string]$entry.Value -eq $InternalTaskKind) { return [string]$entry.Key }
  }
  throw "Unknown internal TaskKind: $InternalTaskKind"
}

function Get-StateSnapshot {
  $shown = Invoke-StateTool @('Show')
  if ($shown.Code -ne 0) { throw $(if ($shown.Error) { $shown.Error } else { 'State Show failed.' }) }
  Convert-ChildJson $shown 'state Show'
}

function Get-RecoveryFailurePolicy {
  param($State)
  if ($State.state -eq 'AUTO-BLOCKED') { 'auto_blocked' } else { 'preserve_recovery' }
}

function Get-NowValue {
  if ([string]::IsNullOrWhiteSpace($Now)) { return [DateTimeOffset]::UtcNow }
  [DateTimeOffset]::Parse($Now, [Globalization.CultureInfo]::InvariantCulture)
}

function Test-DeepSeekBackoffActive {
  param($State)

  $until = [string]$State.workerState.deepseek.backoffUntil
  if ([string]::IsNullOrWhiteSpace($until)) { return $false }
  [DateTimeOffset]::Parse($until, [Globalization.CultureInfo]::InvariantCulture) -gt (Get-NowValue)
}

function Remove-RunArtifacts {
  param($Session)

  $rootPath = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\', '/')
  $prefix = $rootPath + [IO.Path]::DirectorySeparatorChar
  $candidates = @(
    (Get-SessionPath),
    [string]$Session.currentBaselinePath,
    [string]$Session.baselinePath,
    [string]$Session.evidencePath
  ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
  foreach ($candidate in $candidates) {
    $fullPath = [IO.Path]::GetFullPath([string]$candidate)
    if ($fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
      Remove-Item -LiteralPath $fullPath -Force
    }
  }
}

function Close-InterruptedState {
  param($Session, [string]$Message)

  $before = Get-StateSnapshot
  $paths = @($before.expectedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  if ($paths.Count -eq 0) {
    $closed = Invoke-StateTool @('AbortClean', '-RunId', $RunId, '-ErrorMessage', $Message)
    if ($closed.Code -ne 0) { throw $(if ($closed.Error) { $closed.Error } else { 'AbortClean failed.' }) }
    return [pscustomobject]@{
      Classification = 'clean'
      FailurePolicy = 'close_clean'
      State = Convert-ChildJson $closed 'clean interruption close'
      OriginalState = $before
      ChangedExpectedPaths = @()
      ConflictingPaths = @()
    }
  }

  $capture = Invoke-GuardTool @(
    'CaptureInterruptionEvidence', '-BaselinePath', [string]$Session.baselinePath,
    '-EvidencePath', [string]$Session.evidencePath, '-ExpectedPaths', ($paths -join '|')
  )
  $captureJson = Convert-ChildJson $capture 'interruption classification'
  $classification = [string]$captureJson.classification
  switch ($classification) {
    'clean' {
      $closed = Invoke-StateTool @('AbortClean', '-RunId', $RunId, '-ErrorMessage', $Message)
      $policy = 'close_clean'
    }
    'recoverable' {
      $arguments = @(
        'RecordRecoverableInterruption', '-RunId', $RunId, '-ErrorMessage', $Message,
        '-RecoveryBaselinePath', [string]$Session.baselinePath,
        '-RecoveryEvidencePath', [string]$Session.evidencePath,
        '-RecoveryEvidenceHash', [string]$captureJson.evidenceHash
      )
      if ([bool]$Session.isRecovery) { $arguments += '-WasRecovery' }
      $closed = Invoke-StateTool $arguments
      $policy = if ($closed.Code -eq 0) { Get-RecoveryFailurePolicy (Convert-ChildJson $closed 'recoverable interruption close') } else { 'preserve_recovery' }
    }
    'unsafe' {
      $reason = if ([string]::IsNullOrWhiteSpace([string]$captureJson.reason)) { $Message } else { "$Message ($([string]$captureJson.reason))" }
      $closed = Invoke-StateTool @('BlockUnsafe', '-RunId', $RunId, '-ErrorMessage', $reason)
      $policy = 'auto_blocked'
    }
    default {
      throw "Unsupported interruption classification: $classification"
    }
  }
  if ($closed.Code -ne 0) { throw $(if ($closed.Error) { $closed.Error } else { "State close failed for $classification interruption." }) }
  $closedState = Convert-ChildJson $closed "$classification interruption close"
  [pscustomobject]@{
    Classification = $classification
    FailurePolicy = $policy
    State = $closedState
    OriginalState = $before
    ChangedExpectedPaths = @($captureJson.changedExpectedPaths)
    ConflictingPaths = @($captureJson.conflictingPaths)
  }
}

function Stop-RegisteredWork {
  param($Session, [string]$Code, [string]$Message, [int]$ExitCode)

  try {
    $closure = Close-InterruptedState $Session $Message
  } catch {
    $result = New-ProtocolResult $false 'stopped' ([string]$Session.branchKind) 'preserve_recovery' 'fail_close_error' $_.Exception.Message
    Write-ProtocolResult $result 1
  }
  $result = New-ProtocolResult $false 'stopped' ([string]$Session.branchKind) ([string]$closure.FailurePolicy) $Code $Message
  $result.taskId = [string]$closure.OriginalState.taskId
  $result.executor = [string]$closure.OriginalState.taskExecutor
  $result.expectedPaths = @($closure.OriginalState.expectedPaths)
  $result.conflictingPaths = @($closure.ConflictingPaths)
  Write-ProtocolResult $result $ExitCode
}

try {
  switch ($Action) {
    'Start' {
      if ([string]::IsNullOrWhiteSpace($ActualModel) -or $ActualModel -eq 'unknown') {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'identity_unknown' 'Actual model identity is unavailable.'
        Write-ProtocolResult $result 15
      }
      if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = [guid]::NewGuid().ToString() }
      if (-not (Test-ValidRunId $RunId)) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'invalid_run_id' 'RunId must be a non-empty UUID.'
        Write-ProtocolResult $result 15
      }

      New-Item -ItemType Directory -Path $RunRoot -Force | Out-Null
      $acquire = Invoke-StateTool @('Acquire', '-RunId', $RunId)
      if ($acquire.Code -eq 10) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'lease_busy' 'Another run owns the active lease.'
        Write-ProtocolResult $result 10
      }
      if ($acquire.Code -eq 11) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'auto_blocked' 'auto_blocked' 'Controller is AUTO-BLOCKED.'
        Write-ProtocolResult $result 11
      }
      if ($acquire.Code -eq 13 -and "$($acquire.Error) $($acquire.Output)" -match 'stale_running_state') {
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'stale_running_state' 'A stale RUNNING state requires operator classification.'
        Write-ProtocolResult $result 13
      }
      if ($acquire.Code -ne 0) {
        $message = if ($acquire.Error) { $acquire.Error } else { 'State Acquire failed.' }
        $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'state_error' $message
        Write-ProtocolResult $result $acquire.Code
      }
      $acquiredState = Convert-ChildJson $acquire 'state Acquire'

      $currentBaselinePath = Join-Path $RunRoot "$RunId.baseline.json"
      $snapshot = Invoke-GuardTool @('Snapshot', '-BaselinePath', $currentBaselinePath)
      if ($snapshot.Code -ne 0) {
        $message = if ($snapshot.Error) { $snapshot.Error } else { 'Workspace Snapshot failed.' }
        Close-StartFailure 'snapshot_failed' $message $snapshot.Code
      }

      $hasRecovery = [string]$acquiredState.runMode -ceq 'recovery'
      if ($hasRecovery) {
        if ([string]::IsNullOrWhiteSpace([string]$acquiredState.taskKind) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.taskId) -or
            @($acquiredState.expectedPaths).Count -eq 0 -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryBaselinePath) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidencePath) -or
            [string]::IsNullOrWhiteSpace([string]$acquiredState.recoveryEvidenceHash)) {
          [void](Invoke-StateTool @('BlockUnsafe', '-RunId', $RunId, '-ErrorMessage', 'recovery_state_incomplete'))
          $result = New-ProtocolResult $false 'stopped' 'recovery' 'auto_blocked' 'recovery_state_incomplete' 'Recovery state is incomplete.'
          Write-ProtocolResult $result 1
        }
        try {
          $evidence = [IO.File]::ReadAllText([string]$acquiredState.recoveryEvidencePath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
          if ([string]$evidence.payloadHash -cne [string]$acquiredState.recoveryEvidenceHash) { throw 'Recovery evidence hash does not match state.' }
        } catch {
          [void](Invoke-StateTool @('BlockUnsafe', '-RunId', $RunId, '-ErrorMessage', 'recovery_evidence_invalid'))
          $result = New-ProtocolResult $false 'stopped' 'recovery' 'auto_blocked' 'recovery_evidence_invalid' $_.Exception.Message
          Write-ProtocolResult $result 1
        }

        $workType = Get-ExternalWorkType ([string]$acquiredState.taskKind)
        $session = [ordered]@{
          protocolVersion = $script:ProtocolVersion
          runId = $RunId
          repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
          baselinePath = [string]$acquiredState.recoveryBaselinePath
          currentBaselinePath = $currentBaselinePath
          evidencePath = [string]$acquiredState.recoveryEvidencePath
          isRecovery = $true
          phase = [string]$acquiredState.checkpoint
          branchKind = 'recovery'
          workType = $workType
          taskId = [string]$acquiredState.taskId
          executor = [string]$acquiredState.taskExecutor
          decisionContextHash = $null
          resumeTaskId = $null
        }
        Write-JsonAtomically ([pscustomobject]$session) (Get-SessionPath)

        $recovery = Invoke-GuardTool @(
          'CheckRecovery', '-BaselinePath', [string]$acquiredState.recoveryBaselinePath,
          '-EvidencePath', [string]$acquiredState.recoveryEvidencePath,
          '-ExpectedPaths', (@($acquiredState.expectedPaths) -join '|')
        )
        if ($recovery.Code -ne 0) {
          $recoveryJson = if ($recovery.Output) { Convert-ChildJson $recovery 'workspace CheckRecovery' } else { $null }
          $reason = if ($null -ne $recoveryJson -and $recoveryJson.reason) { [string]$recoveryJson.reason } else { 'recovery_check_failed' }
          $failed = Invoke-StateTool @('RecordRecoverableInterruption', '-RunId', $RunId, '-WasRecovery', '-ErrorMessage', $reason)
          $failedState = if ($failed.Code -eq 0) { Convert-ChildJson $failed 'recovery interruption' } else { $acquiredState }
          $result = New-ProtocolResult $false 'stopped' 'recovery' (Get-RecoveryFailurePolicy $failedState) $reason "Recovery check failed: $reason"
          $result.taskId = [string]$acquiredState.taskId
          $result.executor = [string]$acquiredState.taskExecutor
          $result.expectedPaths = @($acquiredState.expectedPaths)
          $result.conflictingPaths = if ($null -ne $recoveryJson) { @($recoveryJson.conflictingPaths) } else { @() }
          Write-ProtocolResult $result $recovery.Code
        }

        $result = New-ProtocolResult $true 'resume_task' 'recovery' 'preserve_recovery' $null 'Recovery evidence exactly matches the controller residue.'
        $result.taskId = [string]$acquiredState.taskId
        $result.executor = [string]$acquiredState.taskExecutor
        $result.expectedPaths = @($acquiredState.expectedPaths)
        $result.requiredSources = @(Get-BranchSources 'recovery' ([string]$acquiredState.taskExecutor))
        $result.nextCommand = 'Finish'
        $result.baselinePath = [string]$acquiredState.recoveryBaselinePath
        Write-ProtocolResult $result
      }

      $checkpoint = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'identity_checked')
      if ($checkpoint.Code -ne 0) {
        $message = if ($checkpoint.Error) { $checkpoint.Error } else { 'Identity checkpoint failed.' }
        Close-StartFailure 'checkpoint_failed' $message $checkpoint.Code
      }
      $state = Convert-ChildJson $checkpoint 'identity checkpoint'

      $session = [ordered]@{
        protocolVersion = $script:ProtocolVersion
        runId = $RunId
        repositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
        baselinePath = $currentBaselinePath
        currentBaselinePath = $currentBaselinePath
        evidencePath = (Join-Path $RunRoot "$RunId.evidence.json")
        isRecovery = $false
        phase = 'identity_checked'
        branchKind = 'selection'
        workType = $null
        taskId = $null
        executor = $null
        candidateResolver = $script:ExecutionCandidateResolver
        decisionContextHash = $null
        resumeTaskId = $null
        notificationContext = $null
      }
      Write-JsonAtomically ([pscustomobject]$session) (Get-SessionPath)

      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'close_empty_run' $null 'Lease acquired and workspace baseline captured.'
      $result.nextCommand = 'InspectCandidate'
      $result.baselinePath = $currentBaselinePath
      $result.requiredSources = @($script:ExecutionQueueRelativePath)
      $result.selectionPolicy = [ordered]@{
        resolver = $script:ExecutionCandidateResolver
        semanticInputs = @('TaskId')
        protocolValues = 'controller_owned'
      }
      $result.workerBackoff = $state.workerState.deepseek
      if ($null -ne $state.pendingDecision) {
        $result.action = 'inspect_pending_decision'
        $result.branchKind = 'pending_decision'
        $result.requiredSources = @($script:DecisionStatusRelativePath)
        $result.pendingDecision = $state.pendingDecision
        $route = Get-FeishuDecisionRoute $state.pendingDecision
        $result.nextCommands = @($route.nextCommands)
        $result.nextCommand = [string]$route.nextCommand
        $result.decisionRoute = [string]$route.route
      } elseif ($null -ne $state.decisionFlow -and [string]$state.decisionFlow.status -ceq 'IMPLEMENTATION_PENDING') {
        $session.resumeTaskId = [string]$state.decisionFlow.taskId
        Save-Session $session
        $result.action = 'resume_decision_task'
        $result.branchKind = 'selection'
        $result.taskId = [string]$state.decisionFlow.taskId
        $result.requiredSources = @($script:ExecutionQueueRelativePath, $script:DecisionStatusRelativePath)
        $result.nextCommand = 'InspectCandidate'
      }
      Write-ProtocolResult $result
    }
    'Contract' {
      $result = New-ProtocolResult $true 'contract' 'none' 'stop_read_only' $null 'Controller protocol contract.'
      $result.runId = $null
      $result.taskKindMapping = $script:TaskKindMapping
      $result.actions = @(
        'Contract','Start','InspectCandidate','RegisterCandidate','BeginMutation','Renew','Finish',
        'CompleteNoChange','Fail','RecordQueueState','RecordWorkerFailure','ClearWorkerFailure',
        'PrepareDecision','CreateDecision','SendDecisionNotification','ConsumeDecisionReply','ResolveDecisionManual'
      )
      $result.commandTemplates = [ordered]@{
        Start = "Start -RepositoryRoot 'D:\天章游戏开发' -RunId `$runId -ActualModel `$actualModel"
        InspectCandidate = 'InspectCandidate -RepositoryRoot $RepositoryRoot -RunId $runId -TaskId $taskId'
        RegisterCandidate = 'RegisterCandidate -RepositoryRoot $RepositoryRoot -RunId $runId -ExpectedPaths $expectedPaths'
        PrepareDecision = 'PrepareDecision -RepositoryRoot $RepositoryRoot -RunId $runId'
        CreateDecision = (Get-DecisionCommandContract).template
        SendDecisionNotification = 'SendDecisionNotification -RepositoryRoot $RepositoryRoot -RunId $runId'
        ConsumeDecisionReply = 'ConsumeDecisionReply -RepositoryRoot $RepositoryRoot -RunId $runId'
        ResolveDecisionManual = 'ResolveDecisionManual -RepositoryRoot $RepositoryRoot -RunId $runId -ReplyText ''DEC-YYYYMMDD-ID：选择 A'' -CurrentThreadId $threadId -ManualOverride'
      }
      $result.decisionParameters = [ordered]@{
        required = (Get-DecisionCommandContract).requiredParameters
        optionFormat = (Get-DecisionCommandContract).optionFormat
      }
      $result.candidateResolvers = [ordered]@{
        execution = [ordered]@{
          action = 'InspectCandidate'
          source = $script:ExecutionQueueRelativePath
          semanticInputs = @('TaskId')
          protocolValues = 'controller_owned'
        }
        review = [ordered]@{
          source = '开发管理/审核入口.txt'
          mode = 'separate_resolver_required'
        }
        maintenance = [ordered]@{
          source = '开发管理/状态与建议维护规则.txt'
          mode = 'separate_resolver_required'
        }
      }
      Write-ProtocolResult $result
    }
    'InspectCandidate' {
      $session = Read-Session
      if ($session.phase -notin @('identity_checked', 'candidate_inspection')) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'InspectCandidate requires a pre-task phase.'
        Write-ProtocolResult $result 13
      }
      if (-not [string]::IsNullOrWhiteSpace($WorkType) -or -not [string]::IsNullOrWhiteSpace($Executor)) {
        Close-EmptyRun 'candidate_selector_override_forbidden' 'InspectCandidate accepts only semantic TaskId; work type and executor are controller-owned.' 15
      }
      if ([string]::IsNullOrWhiteSpace($TaskId)) { Close-EmptyRun 'invalid_arguments' 'TaskId is required.' 15 }
      if ([string]$session.candidateResolver -cne $script:ExecutionCandidateResolver) {
        Close-EmptyRun 'candidate_resolver_mismatch' 'InspectCandidate is reserved for fresh current-queue execution candidates.' 15
      }
      if ($null -ne $session.PSObject.Properties['resumeTaskId'] -and
          -not [string]::IsNullOrWhiteSpace([string]$session.resumeTaskId) -and
          [string]$TaskId -cne [string]$session.resumeTaskId) {
        $result = New-ProtocolResult $false 'inspect_candidate' 'selection' 'preserve_recovery' 'decision_task_mismatch' 'Resolved decision recovery must inspect the original task id.'
        $result.taskId = [string]$session.resumeTaskId
        $result.nextCommand = 'InspectCandidate'
        Write-ProtocolResult $result 24
      }

      $candidate = Resolve-ExecutionQueueCandidate $TaskId
      if (-not [bool]$candidate.Ok) {
        if ([bool]$candidate.Fatal) {
          Close-EmptyRun ([string]$candidate.ErrorCode) ([string]$candidate.Message) 15
        }
        $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' ([string]$candidate.ErrorCode) ([string]$candidate.Message)
        $result.taskId = $TaskId
        $result.nextCommand = 'InspectCandidate'
        Write-ProtocolResult $result 24
      }

      $selectedWorkType = [string]$candidate.WorkType
      $selectedTaskId = [string]$candidate.TaskId
      $selectedExecutor = [string]$candidate.Executor
      if ($selectedExecutor -eq 'deepseek') {
        $workerState = Get-StateSnapshot
        if (Test-DeepSeekBackoffActive $workerState) {
          $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'worker_backoff' 'DeepSeek worker is in backoff.'
          $result.nextCommand = 'InspectCandidate'
          $result.workerBackoff = $workerState.workerState.deepseek
          Write-ProtocolResult $result 23
        }
      }

      $session.phase = 'candidate_inspection'
      $session.branchKind = $selectedWorkType
      $session.workType = $selectedWorkType
      $session.taskId = $selectedTaskId
      $session.executor = $selectedExecutor
      Save-Session $session

      $result = New-ProtocolResult $true 'inspect_candidate' $selectedWorkType 'close_empty_run' $null 'Candidate facts may be inspected read-only before path registration.'
      $result.taskId = $selectedTaskId
      $result.executor = $selectedExecutor
      $result.requiredSources = @(Get-BranchSources $selectedWorkType $selectedExecutor)
      $result.discoveryPolicy = [ordered]@{
        readOnlyProjectDiscovery = $true
        allowedCommands = @('rg', 'rg --files', 'Get-Content', 'git status', 'git diff', 'task-card required checks')
        prohibitedOperations = @('project writes', 'worker dispatch', 'stage', 'commit', 'controller helper calls')
      }
      $result.nextCommand = 'RegisterCandidate'
      Write-ProtocolResult $result
    }
    'RegisterCandidate' {
      $session = Read-Session
      if ($session.phase -ne 'candidate_inspection') {
        $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'invalid_phase' 'RegisterCandidate requires InspectCandidate first.'
        $result.nextCommand = 'InspectCandidate'
        Write-ProtocolResult $result 13
      }
      $selectedWorkType = [string]$session.workType
      $selectedTaskId = [string]$session.taskId
      $selectedExecutor = [string]$session.executor
      if ((-not [string]::IsNullOrWhiteSpace($WorkType) -and $WorkType -cne $selectedWorkType) -or
          (-not [string]::IsNullOrWhiteSpace($TaskId) -and $TaskId -cne $selectedTaskId) -or
          (-not [string]::IsNullOrWhiteSpace($Executor) -and $Executor -cne $selectedExecutor)) {
        Close-EmptyRun 'candidate_identity_mismatch' 'RegisterCandidate identity does not match the inspected candidate.' 15
      }
      if ([string]::IsNullOrWhiteSpace($ExpectedPaths)) { Close-EmptyRun 'invalid_arguments' 'ExpectedPaths is required.' 15 }
      if ($selectedExecutor -eq 'deepseek') {
        $workerState = Get-StateSnapshot
        if (Test-DeepSeekBackoffActive $workerState) {
          $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'worker_backoff' 'DeepSeek worker is in backoff.'
          $result.nextCommand = 'InspectCandidate'
          $result.workerBackoff = $workerState.workerState.deepseek
          Write-ProtocolResult $result 23
        }
      }

      $check = Invoke-GuardTool @('Check', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', $ExpectedPaths)
      $checkJson = if ($check.Output) { Convert-ChildJson $check 'workspace Check' } else { $null }
      if ($check.Code -eq 20) {
        $result = New-ProtocolResult $false 'select_candidate' 'selection' 'skip_candidate' 'candidate_conflict' 'Candidate paths overlap the captured workspace baseline.'
        $result.nextCommand = 'InspectCandidate'
        $result.expectedPaths = @($checkJson.expectedPaths)
        $result.conflictingPaths = @($checkJson.conflictingPaths)
        Write-ProtocolResult $result 20
      }
      if ($check.Code -ne 0) {
        $reason = if ($null -ne $checkJson -and $checkJson.reason) { [string]$checkJson.reason } else { 'workspace_check_failed' }
        $policy = if ($check.Code -eq 21) { 'stop_read_only' } else { 'close_empty_run' }
        Close-EmptyRun $reason "Workspace Check failed: $reason" $check.Code $policy
      }
      $normalizedPaths = @($checkJson.expectedPaths)
      $decisionState = Get-StateSnapshot
      $resumesDecisionFlow = (
        $null -eq $decisionState.pendingDecision -and
        $null -ne $decisionState.decisionFlow -and
        [string]$decisionState.decisionFlow.status -ceq 'IMPLEMENTATION_PENDING' -and
        [string]$decisionState.decisionFlow.taskId -ceq $selectedTaskId
      )
      if ($resumesDecisionFlow -and $normalizedPaths -notcontains $script:DecisionStatusRelativePath) {
        Close-EmptyRun 'decision_status_path_missing' 'Resolved decision work must register the project-visible decision status path.' 15
      }

      $queues = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'queues_loaded')
      if ($queues.Code -ne 0) {
        Close-EmptyRun 'checkpoint_failed' $(if ($queues.Error) { $queues.Error } else { 'queues_loaded checkpoint failed.' }) $queues.Code
      }
      $mappedTaskKind = [string]$script:TaskKindMapping[$selectedWorkType]
      $selected = Invoke-StateTool @(
        'Checkpoint', '-RunId', $RunId,
        '-TaskKind', $mappedTaskKind, '-TaskId', $selectedTaskId, '-TaskExecutor', $selectedExecutor,
        '-Checkpoint', 'task_selected', '-ExpectedPaths', ($normalizedPaths -join '|'),
        '-RecoveryBaselinePath', [string]$session.baselinePath
      )
      if ($selected.Code -ne 0) {
        Close-EmptyRun 'checkpoint_failed' $(if ($selected.Error) { $selected.Error } else { 'task_selected checkpoint failed.' }) $selected.Code
      }

      $session.phase = 'task_selected'
      $session.branchKind = $selectedWorkType
      $session.workType = $selectedWorkType
      $session.taskId = $selectedTaskId
      $session.executor = $selectedExecutor
      Save-Session $session

      $result = New-ProtocolResult $true 'implement_task' $selectedWorkType 'preserve_recovery' $null 'Candidate registered and isolated.'
      $result.taskId = $selectedTaskId
      $result.executor = $selectedExecutor
      $result.expectedPaths = $normalizedPaths
      $requiredSources = @(Get-BranchSources $selectedWorkType $selectedExecutor)
      if ($normalizedPaths -contains $script:DecisionStatusRelativePath) {
        $requiredSources += $script:DecisionStatusRelativePath
      }
      $result.requiredSources = @($requiredSources | Select-Object -Unique)
      $result.nextCommand = 'BeginMutation'
      Write-ProtocolResult $result
    }
    'BeginMutation' {
      $session = Read-Session
      if ($session.phase -ne 'task_selected') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'BeginMutation requires the task_selected phase.'
        Write-ProtocolResult $result 13
      }
      $checkpoint = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'mutation_started')
      if ($checkpoint.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'checkpoint_failed' $(if ($checkpoint.Error) { $checkpoint.Error } else { 'mutation_started checkpoint failed.' })
        Write-ProtocolResult $result $checkpoint.Code
      }
      $session.phase = 'mutation_started'
      Save-Session $session
      $state = Convert-ChildJson $checkpoint 'mutation_started checkpoint'
      $result = New-ProtocolResult $true 'perform_semantic_work' ([string]$session.branchKind) 'preserve_recovery' $null 'Mutation checkpoint recorded.'
      $result.taskId = [string]$state.taskId
      $result.executor = [string]$state.taskExecutor
      $result.expectedPaths = @($state.expectedPaths)
      $result.nextCommand = 'Finish'
      Write-ProtocolResult $result
    }
    'Renew' {
      $session = Read-Session
      $renewed = Invoke-StateTool @('Renew', '-RunId', $RunId)
      if ($renewed.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'renew_failed' $(if ($renewed.Error) { $renewed.Error } else { 'Lease renewal failed.' })
        Write-ProtocolResult $result $renewed.Code
      }
      $state = Convert-ChildJson $renewed 'state Renew'
      $result = New-ProtocolResult $true 'lease_renewed' ([string]$session.branchKind) 'preserve_recovery' $null 'Lease renewed.'
      $result.taskId = [string]$state.taskId
      $result.executor = [string]$state.taskExecutor
      $result.expectedPaths = @($state.expectedPaths)
      Write-ProtocolResult $result
    }
    'Finish' {
      $session = Read-Session
      if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_arguments' 'CommitMessage is required.'
        Write-ProtocolResult $result 15
      }
      if ($session.phase -notin @('mutation_started', 'verification_completed', 'commit_completed')) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'Finish requires a mutated or recoverable work unit.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $paths = @($state.expectedPaths)
      if ($paths.Count -eq 0 -or [string]::IsNullOrWhiteSpace([string]$session.baselinePath)) {
        Stop-RegisteredWork $session 'recovery_state_incomplete' 'Finish is missing expected paths or the original baseline.' 1
      }

      $ownsDecisionStatus = $paths -contains $script:DecisionStatusRelativePath
      $ownsPendingDecision = (
        $ownsDecisionStatus -and
        $null -ne $state.pendingDecision -and
        [string]$state.pendingDecision.taskId -ceq [string]$state.taskId
      )
      $completesDecisionFlow = (
        $null -eq $state.pendingDecision -and
        $null -ne $state.decisionFlow -and
        [string]$state.decisionFlow.status -ceq 'IMPLEMENTATION_PENDING' -and
        [string]$state.decisionFlow.taskId -ceq [string]$state.taskId
      )
      if ($completesDecisionFlow -and -not $ownsDecisionStatus) {
        Stop-RegisteredWork $session 'decision_status_path_missing' 'Decision implementation must own the project-visible decision status path.' 15
      }
      if ($ownsPendingDecision -and [string]$session.phase -cne 'commit_completed') {
        $published = Invoke-DecisionStatusPublisher 'PublishPending' $state
        if ($published.Code -ne 0) {
          $message = if ($published.Error) { $published.Error } elseif ($published.Output) { $published.Output } else { 'Decision status publisher failed.' }
          Stop-RegisteredWork $session 'decision_status_publish_failed' $message $published.Code
        }
      } elseif ($completesDecisionFlow -and [string]$session.phase -cne 'commit_completed') {
        $published = Invoke-DecisionStatusPublisher 'PublishImplementationPending' $state
        if ($published.Code -ne 0) {
          $message = if ($published.Error) { $published.Error } elseif ($published.Output) { $published.Output } else { 'Decision status publisher failed.' }
          Stop-RegisteredWork $session 'decision_status_publish_failed' $message $published.Code
        }
        $cleared = Invoke-DecisionStatusPublisher 'Clear' $null
        if ($cleared.Code -ne 0) {
          $message = if ($cleared.Error) { $cleared.Error } elseif ($cleared.Output) { $cleared.Output } else { 'Decision status clear failed.' }
          Stop-RegisteredWork $session 'decision_status_publish_failed' $message $cleared.Code
        }
      }

      $verify = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      if ($verify.Code -ne 0) {
        $verifyJson = if ($verify.Output) { Convert-ChildJson $verify 'pre-commit Verify' } else { $null }
        $reason = if ($null -ne $verifyJson -and $verifyJson.reason) { [string]$verifyJson.reason } else { 'verify_failed' }
        Stop-RegisteredWork $session $reason "Pre-commit Verify failed: $reason" $verify.Code
      }

      if ([string]::IsNullOrWhiteSpace([string]$state.recoveryEvidencePath)) {
        $capture = Invoke-GuardTool @(
          'CaptureRecoveryEvidence', '-BaselinePath', [string]$session.baselinePath,
          '-EvidencePath', [string]$session.evidencePath, '-ExpectedPaths', ($paths -join '|')
        )
        if ($capture.Code -ne 0) {
          $captureJson = if ($capture.Output) { Convert-ChildJson $capture 'recovery evidence capture' } else { $null }
          $reason = if ($null -ne $captureJson -and $captureJson.reason) { [string]$captureJson.reason } else { 'evidence_capture_failed' }
          Stop-RegisteredWork $session $reason "Recovery evidence capture failed: $reason" $capture.Code
        }
        $captureJson = Convert-ChildJson $capture 'recovery evidence capture'
        $evidencePath = [string]$session.evidencePath
        $evidenceHash = [string]$captureJson.evidenceHash
      } else {
        $evidencePath = [string]$state.recoveryEvidencePath
        $evidenceHash = [string]$state.recoveryEvidenceHash
      }

      $verified = Invoke-StateTool @(
        'Checkpoint', '-RunId', $RunId, '-Checkpoint', 'verification_completed',
        '-RecoveryBaselinePath', [string]$session.baselinePath,
        '-RecoveryEvidencePath', $evidencePath, '-RecoveryEvidenceHash', $evidenceHash
      )
      if ($verified.Code -ne 0) {
        Stop-RegisteredWork $session 'checkpoint_failed' $(if ($verified.Error) { $verified.Error } else { 'verification_completed checkpoint failed.' }) $verified.Code
      }
      $session.phase = 'verification_completed'
      $session.evidencePath = $evidencePath
      Save-Session $session

      $finalized = Invoke-ChildPowerShell $script:FinalizerTool @(
        '-RepositoryRoot', $RepositoryRoot, '-ExpectedPaths', ($paths -join '|'), '-CommitMessage', $CommitMessage
      )
      if ($finalized.Code -ne 0) {
        $message = if ($finalized.Error) { $finalized.Error } elseif ($finalized.Output) { $finalized.Output } else { 'Finalizer failed.' }
        Stop-RegisteredWork $session 'finalizer_failed' $message $finalized.Code
      }
      $commitCandidates = @(([string]$finalized.Output) -split '\r?\n' | Where-Object { $_ -match '^[0-9a-f]{40,64}$' })
      if ($commitCandidates.Count -ne 1) {
        Stop-RegisteredWork $session 'finalizer_protocol_invalid' 'Finalizer did not return a commit hash.' 1
      }
      $commit = [string]$commitCandidates[0]

      $committed = Invoke-StateTool @('Checkpoint', '-RunId', $RunId, '-Checkpoint', 'commit_completed')
      if ($committed.Code -ne 0) {
        Stop-RegisteredWork $session 'checkpoint_failed' $(if ($committed.Error) { $committed.Error } else { 'commit_completed checkpoint failed.' }) $committed.Code
      }
      $session.phase = 'commit_completed'
      Save-Session $session

      $postVerify = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      if ($postVerify.Code -ne 0) {
        $postJson = if ($postVerify.Output) { Convert-ChildJson $postVerify 'post-commit Verify' } else { $null }
        $reason = if ($null -ne $postJson -and $postJson.reason) { [string]$postJson.reason } else { 'post_commit_verify_failed' }
        Stop-RegisteredWork $session $reason "Post-commit Verify failed: $reason" $postVerify.Code
      }

      if ($completesDecisionFlow) {
        $flowCompleted = Invoke-StateTool @('CompleteDecisionFlow', '-RunId', $RunId, '-TaskId', [string]$session.taskId)
        if ($flowCompleted.Code -ne 0) {
          Stop-RegisteredWork $session 'decision_flow_complete_failed' $(if ($flowCompleted.Error) { $flowCompleted.Error } else { 'CompleteDecisionFlow failed.' }) $flowCompleted.Code
        }
      }

      $completed = Invoke-StateTool @('Complete', '-RunId', $RunId)
      if ($completed.Code -ne 0) {
        Stop-RegisteredWork $session 'complete_failed' $(if ($completed.Error) { $completed.Error } else { 'Complete failed.' }) $completed.Code
      }
      Remove-RunArtifacts $session
      $result = New-ProtocolResult $true 'completed' ([string]$session.branchKind) 'stop_read_only' $null 'Work unit committed and completed.'
      $result.taskId = [string]$session.taskId
      $result.executor = [string]$session.executor
      $result.expectedPaths = $paths
      $result.commit = $commit
      $result.nextCommand = $null
      Write-ProtocolResult $result
    }
    'CompleteNoChange' {
      $session = Read-Session
      if ($session.phase -eq 'mutation_started' -or $session.phase -eq 'verification_completed' -or $session.phase -eq 'commit_completed') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'mutation_exists' 'CompleteNoChange cannot close a mutated work unit.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $paths = @($state.expectedPaths)
      if ($paths.Count -gt 0) {
        $noChangeCheck = Invoke-GuardTool @('Verify', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', ($paths -join '|'))
      } else {
        $noChangeCheck = Invoke-GuardTool @('Check', '-BaselinePath', [string]$session.baselinePath, '-ExpectedPaths', 'tools/.tzg-no-change-sentinel')
      }
      if ($noChangeCheck.Code -ne 0) {
        $json = if ($noChangeCheck.Output) { Convert-ChildJson $noChangeCheck 'CompleteNoChange guard' } else { $null }
        $reason = if ($null -ne $json -and $json.reason) { [string]$json.reason } else { 'baseline_changed' }
        Stop-RegisteredWork $session $reason "CompleteNoChange guard failed: $reason" $noChangeCheck.Code
      }
      $completed = Invoke-StateTool @('Complete', '-RunId', $RunId)
      if ($completed.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'complete_failed' $(if ($completed.Error) { $completed.Error } else { 'Complete failed.' })
        Write-ProtocolResult $result $completed.Code
      }
      Remove-RunArtifacts $session
      $result = New-ProtocolResult $true 'completed_no_change' ([string]$session.branchKind) 'stop_read_only' $null 'Run completed without project changes.'
      Write-ProtocolResult $result
    }
    'Fail' {
      if ([string]::IsNullOrWhiteSpace($ErrorMessage)) {
        $result = New-ProtocolResult $false 'stopped' 'none' 'preserve_recovery' 'invalid_arguments' 'ErrorMessage is required.'
        Write-ProtocolResult $result 15
      }
      $session = Read-Session
      try {
        $closure = Close-InterruptedState $session $ErrorMessage
      } catch {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'fail_close_error' $_.Exception.Message
        Write-ProtocolResult $result 1
      }
      if ($closure.Classification -eq 'clean') {
        Remove-RunArtifacts $session
      } else {
        $session.phase = 'failed'
        Save-Session $session
      }
      $result = New-ProtocolResult $false 'failed' ([string]$session.branchKind) ([string]$closure.FailurePolicy) 'task_failed' $ErrorMessage
      $result.taskId = [string]$closure.OriginalState.taskId
      $result.executor = [string]$closure.OriginalState.taskExecutor
      $result.expectedPaths = @($closure.OriginalState.expectedPaths)
      $result.changedExpectedPaths = @($closure.ChangedExpectedPaths)
      $result.conflictingPaths = @($closure.ConflictingPaths)
      $result.nextCommand = 'Start'
      Write-ProtocolResult $result
    }
    'RecordQueueState' {
      [void](Read-Session)
      if ([string]::IsNullOrWhiteSpace($QueueFingerprint) -or $RunnableCount -lt 0) {
        $result = New-ProtocolResult $false 'stopped' 'maintenance' 'preserve_recovery' 'invalid_arguments' 'QueueFingerprint and non-negative RunnableCount are required.'
        Write-ProtocolResult $result 15
      }
      $arguments = @('RecordQueueState', '-RunId', $RunId, '-QueueFingerprint', $QueueFingerprint, '-RunnableCount', [string]$RunnableCount)
      if ($QueueAuditCompleted) { $arguments += '-QueueAuditCompleted' }
      if ($NoCandidate) { $arguments += '-NoCandidate' }
      $recorded = Invoke-StateTool $arguments
      if ($recorded.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'maintenance' 'preserve_recovery' 'state_error' $(if ($recorded.Error) { $recorded.Error } else { 'RecordQueueState failed.' })
        Write-ProtocolResult $result $recorded.Code
      }
      $state = Convert-ChildJson $recorded 'RecordQueueState'
      $result = New-ProtocolResult $true 'queue_state_recorded' 'maintenance' 'preserve_recovery' $null 'Queue state recorded.'
      $result.queueFingerprint = [string]$state.lastQueueFingerprint
      $result.runnableCount = $state.lastRunnableCount
      $result.nextCommand = 'CompleteNoChange'
      Write-ProtocolResult $result
    }
    'RecordWorkerFailure' {
      [void](Read-Session)
      if ([string]::IsNullOrWhiteSpace($WorkerError)) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'invalid_arguments' 'WorkerError is required.'
        Write-ProtocolResult $result 15
      }
      $recorded = Invoke-StateTool @(
        'RecordWorkerFailure', '-RunId', $RunId, '-WorkerId', 'deepseek',
        '-WorkerError', $WorkerError, '-BackoffMinutes', [string]$BackoffMinutes
      )
      if ($recorded.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'state_error' $(if ($recorded.Error) { $recorded.Error } else { 'RecordWorkerFailure failed.' })
        Write-ProtocolResult $result $recorded.Code
      }
      $state = Convert-ChildJson $recorded 'RecordWorkerFailure'
      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'skip_candidate' $null 'DeepSeek worker backoff recorded.'
      $result.workerBackoff = $state.workerState.deepseek
      $result.nextCommand = 'RegisterCandidate'
      Write-ProtocolResult $result
    }
    'ClearWorkerFailure' {
      [void](Read-Session)
      $cleared = Invoke-StateTool @('ClearWorkerFailure', '-RunId', $RunId, '-WorkerId', 'deepseek')
      if ($cleared.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'execution' 'preserve_recovery' 'state_error' $(if ($cleared.Error) { $cleared.Error } else { 'ClearWorkerFailure failed.' })
        Write-ProtocolResult $result $cleared.Code
      }
      $state = Convert-ChildJson $cleared 'ClearWorkerFailure'
      $result = New-ProtocolResult $true 'select_candidate' 'selection' 'skip_candidate' $null 'DeepSeek worker backoff cleared.'
      $result.workerBackoff = $state.workerState.deepseek
      $result.nextCommand = 'RegisterCandidate'
      Write-ProtocolResult $result
    }
    'PrepareDecision' {
      $session = Read-Session
      if ([string]$session.phase -cne 'mutation_started') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'PrepareDecision requires the mutation_started phase.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      if (@($state.expectedPaths) -notcontains $script:DecisionStatusRelativePath) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'decision_status_path_missing' 'The project-visible decision status path is not registered.'
        Write-ProtocolResult $result 15
      }
      $contextHash = Get-FileSha256Lower (Get-DecisionStatusPath)
      if ($null -eq $session.PSObject.Properties['decisionContextHash']) {
        $session | Add-Member -NotePropertyName decisionContextHash -NotePropertyValue $contextHash
      } else {
        $session.decisionContextHash = $contextHash
      }
      Save-Session $session

      $result = New-ProtocolResult $true 'inspect_decision_context' 'pending_decision' 'preserve_recovery' $null 'Decision context locked; use the exact command contract after reading the required source.'
      $result.taskId = [string]$state.taskId
      $result.requiredSources = @($script:DecisionStatusRelativePath)
      $result.command = Get-DecisionCommandContract
      $result.nextCommand = 'CreateDecision'
      Write-ProtocolResult $result
    }
    'CreateDecision' {
      $session = Read-Session
      if ([string]$session.phase -cne 'mutation_started') {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_phase' 'CreateDecision requires the mutation_started phase.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      if (@($state.expectedPaths) -notcontains $script:DecisionStatusRelativePath) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'decision_status_path_missing' 'The project-visible decision status path is not registered.'
        Write-ProtocolResult $result 15
      }
      if ($null -eq $session.PSObject.Properties['decisionContextHash'] -or
          [string]::IsNullOrWhiteSpace([string]$session.decisionContextHash)) {
        $result = New-ProtocolResult $false 'inspect_decision_context' 'pending_decision' 'preserve_recovery' 'decision_context_not_prepared' 'PrepareDecision must expose and lock the project decision context first.'
        $result.requiredSources = @($script:DecisionStatusRelativePath)
        $result.nextCommand = 'PrepareDecision'
        Write-ProtocolResult $result 15
      }
      $currentContextHash = Get-FileSha256Lower (Get-DecisionStatusPath)
      if ($currentContextHash -cne [string]$session.decisionContextHash) {
        $session.decisionContextHash = $null
        Save-Session $session
        $result = New-ProtocolResult $false 'inspect_decision_context' 'pending_decision' 'preserve_recovery' 'decision_context_changed' 'The project decision context changed after preparation.'
        $result.requiredSources = @($script:DecisionStatusRelativePath)
        $result.nextCommand = 'PrepareDecision'
        Write-ProtocolResult $result 15
      }
      foreach ($required in @(
        @{ Name = 'TaskSummary'; Value = $TaskSummary },
        @{ Name = 'DecisionQuestion'; Value = $DecisionQuestion },
        @{ Name = 'DecisionOptions'; Value = $DecisionOptions },
        @{ Name = 'RecommendedOption'; Value = $RecommendedOption },
        @{ Name = 'ImpactSummary'; Value = $ImpactSummary }
      )) {
        if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
          $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'invalid_arguments' "$($required.Name) is required."
          Write-ProtocolResult $result 15
        }
      }
      $created = Invoke-StateTool @(
        'CreateDecision', '-RunId', $RunId, '-TaskKind', [string]$state.taskKind,
        '-TaskId', [string]$state.taskId, '-TaskSummary', $TaskSummary,
        '-DecisionQuestion', $DecisionQuestion, '-DecisionOptions', $DecisionOptions,
        '-RecommendedOption', $RecommendedOption, '-ImpactSummary', $ImpactSummary
      )
      if ($created.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' ([string]$session.branchKind) 'preserve_recovery' 'decision_create_failed' $(if ($created.Error) { $created.Error } else { 'CreateDecision failed.' })
        Write-ProtocolResult $result $created.Code
      }
      $createdState = Convert-ChildJson $created 'CreateDecision'
      $published = Invoke-DecisionStatusPublisher 'PublishPending' $createdState
      if ($published.Code -ne 0) {
        $rollback = Invoke-StateTool @(
          'RollbackDecision', '-RunId', $RunId,
          '-DecisionId', [string]$createdState.pendingDecision.decisionId,
          '-CancellationReason', 'decision_status_publish_failed'
        )
        if ($rollback.Code -ne 0) {
          Stop-RegisteredWork $session 'decision_rollback_failed' $(if ($rollback.Error) { $rollback.Error } else { 'RollbackDecision failed.' }) $rollback.Code
        }
        $afterFailureHash = Get-FileSha256Lower (Get-DecisionStatusPath)
        if ($afterFailureHash -cne $currentContextHash) {
          Stop-RegisteredWork $session 'decision_status_publish_failed' 'Decision status publishing failed after changing the project file.' $published.Code
        }
        $completed = Invoke-StateTool @('Complete', '-RunId', $RunId)
        if ($completed.Code -ne 0) {
          Stop-RegisteredWork $session 'complete_failed' $(if ($completed.Error) { $completed.Error } else { 'Complete failed after decision rollback.' }) $completed.Code
        }
        Remove-RunArtifacts $session
        $message = if ($published.Error) { $published.Error } elseif ($published.Output) { $published.Output } else { 'Decision status publisher failed.' }
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'stop_read_only' 'decision_status_publish_failed' $message
        Write-ProtocolResult $result $published.Code
      }
      $session.decisionContextHash = $null
      Set-SessionNotificationContext $session $null
      $result = New-ProtocolResult $true 'send_decision_notification' 'pending_decision' 'preserve_recovery' $null 'Pending decision created and published to the project status file.'
      $result.taskId = [string]$createdState.taskId
      $result.pendingDecision = $createdState.pendingDecision
      $result.notificationPolicy = [ordered]@{ providerEvidenceRequired = $true; sensitiveStorage = 'sha256_only' }
      $result.nextCommands = @('SendDecisionNotification')
      $result.nextCommand = 'SendDecisionNotification'
      Write-ProtocolResult $result
    }
    'SendDecisionNotification' {
      $session = Read-Session
      if ([string]$session.phase -notin @('identity_checked', 'mutation_started')) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_phase' 'SendDecisionNotification requires a current pending-decision run.'
        Write-ProtocolResult $result 13
      }
      try { Assert-NoModelDecisionEvidence 'SendDecisionNotification' } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'model_evidence_forbidden' 'SendDecisionNotification reads the unique decision and provider evidence from locked state.'
        Write-ProtocolResult $result 15
      }
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'pending_decision_missing' 'SendDecisionNotification requires an active pending decision.'
        Write-ProtocolResult $result 15
      }
      $route = Get-FeishuDecisionRoute $state.pendingDecision
      if ([string]$route.route -cne 'send_notification') {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_send_not_allowed' 'The current Feishu delivery evidence does not allow a new send.'
        $result.pendingDecision = $state.pendingDecision
        $result.nextCommands = @($route.nextCommands)
        $result.nextCommand = [string]$route.nextCommand
        Write-ProtocolResult $result 15
      }
      if ((@($state.pendingDecision.options | ForEach-Object { [string]$_.key }) -join '|') -cne 'A|B|C') {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_options_invalid' 'Feishu decision cards require the exact A/B/C option set.'
        $result.nextCommands = @('ResolveDecisionManual')
        $result.nextCommand = 'ResolveDecisionManual'
        Write-ProtocolResult $result 15
      }
      try { $window = Get-DecisionBindingWindow $state.pendingDecision } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_expired' 'The pending decision is no longer valid for a new Feishu card.'
        $result.nextCommands = @('ResolveDecisionManual')
        $result.nextCommand = 'ResolveDecisionManual'
        Write-ProtocolResult $result 15
      }

      $feishuAttempts = @($state.pendingDecision.notificationAttempts | Where-Object { [string]$_.provider -ceq 'feishu' })
      $request = [ordered]@{
        decision = [ordered]@{
          decisionId = [string]$state.pendingDecision.decisionId
          taskId = [string]$state.pendingDecision.taskId
          question = [string]$state.pendingDecision.question
          options = @($state.pendingDecision.options | ForEach-Object {
            [ordered]@{ key = [string]$_.key; label = [string]$_.label }
          })
          recommendedOption = [string]$state.pendingDecision.recommendedOption
          impactSummary = [string]$state.pendingDecision.impactSummary
        }
        attemptNumber = $feishuAttempts.Count + 1
      }
      try {
        $cli = Invoke-FeishuDecisionCli $FeishuSenderScript $script:FeishuSenderScriptExplicit $request
      } catch {
        $category = if ([string]::IsNullOrWhiteSpace([string]$script:LastFeishuCliStage)) { 'unknown' } else { [string]$script:LastFeishuCliStage }
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' "feishu_cli_$category" 'Feishu sender could not be invoked safely.'
        $result.nextCommand = if ([string]$session.phase -ceq 'mutation_started') { 'Finish' } else { 'CompleteNoChange' }
        Write-ProtocolResult $result 1
      }
      $payload = $cli.Payload
      $category = if ($payload -is [Collections.IDictionary] -and $payload.Contains('result')) { [string]$payload.result } else { '' }
      $validResult = switch ($category) {
        'CHANNEL_UNAVAILABLE' {
          $cli.Code -eq 20 -and (Test-ExactKeys $payload @('result'))
          break
        }
        'DELIVERY_FAILED' {
          $cli.Code -eq 21 -and (Test-ExactKeys $payload @('result','targetHash')) -and (Test-Hex64 $payload.targetHash)
          break
        }
        'PROVIDER_OUTCOME_UNKNOWN' {
          $cli.Code -eq 23 -and
            (Test-ExactKeys $payload @('result','targetHash','cardNonceHash','intentKeyHash')) -and
            (Test-Hex64 $payload.targetHash) -and (Test-Hex64 $payload.cardNonceHash) -and (Test-Hex64 $payload.intentKeyHash)
          break
        }
        'PROVIDER_ACCEPTED' {
          $cli.Code -eq 0 -and
            (Test-ExactKeys $payload @('result','targetHash','providerMessageIdHash','cardNonceHash')) -and
            (Test-Hex64 $payload.targetHash) -and (Test-Hex64 $payload.providerMessageIdHash) -and (Test-Hex64 $payload.cardNonceHash)
          break
        }
        default { $false }
      }
      if (-not $validResult) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_sender_invalid' 'Feishu sender returned an invalid sanitized result.'
        $result.nextCommand = if ([string]$session.phase -ceq 'mutation_started') { 'Finish' } else { 'CompleteNoChange' }
        Write-ProtocolResult $result 15
      }

      $stateArguments = @('RecordDecisionNotification', '-RunId', $RunId, '-NotificationProvider', 'feishu', '-NotificationStatus', $category)
      if ($category -ne 'CHANNEL_UNAVAILABLE') { $stateArguments += @('-TargetHash', [string]$payload.targetHash) }
      if ($category -eq 'PROVIDER_ACCEPTED') { $stateArguments += @('-ProviderMessageIdHash', [string]$payload.providerMessageIdHash) }
      if ($category -eq 'DELIVERY_FAILED') { $stateArguments += @('-NotificationError', 'provider_rejected') }
      $recorded = Invoke-StateTool $stateArguments
      if ($recorded.Code -ne 0) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' 'Feishu notification evidence could not be recorded.'
        Write-ProtocolResult $result $recorded.Code
      }
      $recordedState = Convert-ChildJson $recorded 'RecordDecisionNotification'
      if ($category -eq 'PROVIDER_ACCEPTED') {
        try {
          Write-FeishuPendingBinding $recordedState.pendingDecision ([string]$payload.cardNonceHash) ([string]$payload.providerMessageIdHash) $window
        } catch {
          $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_binding_write_failed' 'Feishu delivery was accepted but its local reply binding could not be written.'
          $result.pendingDecision = $recordedState.pendingDecision
          $result.nextCommands = @('ResolveDecisionManual')
          $result.nextCommand = 'ResolveDecisionManual'
          Write-ProtocolResult $result 1
        }
      }

      $safeEnd = if ([string]$session.phase -ceq 'mutation_started') { 'Finish' } else { 'CompleteNoChange' }
      $message = switch ($category) {
        'CHANNEL_UNAVAILABLE' { 'Feishu bridge is unavailable; no send attempt was consumed.' }
        'PROVIDER_OUTCOME_UNKNOWN' { 'Feishu send outcome requires manual reconciliation; automatic resend is disabled.' }
        'DELIVERY_FAILED' { 'Feishu explicitly rejected the send; the failure was recorded.' }
        default { 'Feishu accepted the decision card and the reply binding was written.' }
      }
      $result = New-ProtocolResult ($category -in @('CHANNEL_UNAVAILABLE','PROVIDER_ACCEPTED')) 'decision_notification_recorded' 'pending_decision' 'preserve_recovery' $(if ($category -eq 'DELIVERY_FAILED') { 'delivery_failed' } elseif ($category -eq 'PROVIDER_OUTCOME_UNKNOWN') { 'provider_outcome_unknown' } else { $null }) $message
      $result.deliveryResult = $category
      $result.pendingDecision = $recordedState.pendingDecision
      $result.nextCommands = if ($category -eq 'PROVIDER_OUTCOME_UNKNOWN') { @($safeEnd, 'ResolveDecisionManual') } else { @($safeEnd) }
      $result.nextCommand = $safeEnd
      Write-ProtocolResult $result
    }
    'ConsumeDecisionReply' {
      $session = Read-Session
      if ([string]$session.phase -cne 'identity_checked') {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'invalid_phase' 'ConsumeDecisionReply requires a fresh identity_checked run.'
        Write-ProtocolResult $result 13
      }
      try { Assert-NoModelDecisionEvidence 'ConsumeDecisionReply' } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'model_evidence_forbidden' 'ConsumeDecisionReply reads the unique decision and reply evidence from locked state.'
        Write-ProtocolResult $result 15
      }
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'pending_decision_missing' 'ConsumeDecisionReply requires an active pending decision.'
        Write-ProtocolResult $result 15
      }
      $acceptedAttempts = @($state.pendingDecision.notificationAttempts | Where-Object {
        [string]$_.provider -ceq 'feishu' -and [string]$_.result -ceq 'PROVIDER_ACCEPTED'
      })
      if ($acceptedAttempts.Count -ne 1) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_delivery_not_accepted' 'A unique accepted Feishu delivery is required before reply consumption.'
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result 15
      }
      try { $binding = Read-FeishuPendingBinding $state.pendingDecision } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_binding_invalid' 'The Feishu reply binding is unavailable or invalid.'
        $result.nextCommands = @('CompleteNoChange', 'ResolveDecisionManual')
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result 15
      }
      if ([string]$acceptedAttempts[0].providerMessageIdHash -cne [string]$binding.providerMessageIdHash) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_binding_mismatch' 'The Feishu reply binding does not match accepted delivery evidence.'
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result 15
      }
      try {
        $createdAt = ConvertTo-ExactUtcIso ([DateTimeOffset]::Parse([string]$state.pendingDecision.createdAt, [Globalization.CultureInfo]::InvariantCulture))
      } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'pending_decision_invalid' 'The pending decision timestamp is invalid.'
        Write-ProtocolResult $result 15
      }
      $request = [ordered]@{
        pendingDecision = [ordered]@{
          decisionId = [string]$state.pendingDecision.decisionId
          allowedOptions = @($binding.allowedOptions)
          createdAt = $createdAt
          expiresAt = [string]$binding.expiresAt
          cardNonceHash = [string]$binding.cardNonceHash
          providerMessageIdHash = [string]$binding.providerMessageIdHash
        }
      }
      try {
        $cli = Invoke-FeishuDecisionCli $FeishuConsumerScript $script:FeishuConsumerScriptExplicit $request
      } catch {
        $category = if ([string]::IsNullOrWhiteSpace([string]$script:LastFeishuCliStage)) { 'unknown' } else { [string]$script:LastFeishuCliStage }
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' "feishu_cli_$category" 'Feishu reply consumer could not be invoked safely.'
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result 1
      }
      $payload = $cli.Payload
      $category = if ($payload -is [Collections.IDictionary] -and $payload.Contains('result')) { [string]$payload.result } else { '' }
      if ($category -eq 'NO_REPLY' -and $cli.Code -eq 0 -and (Test-ExactKeys $payload @('result'))) {
        $result = New-ProtocolResult $true 'no_decision_reply' 'pending_decision' 'stop_read_only' $null 'No valid Feishu card reply is available.'
        $result.nextCommands = @('CompleteNoChange', 'ResolveDecisionManual')
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result
      }
      $acceptedKeys = @(
        'result','optionKey','source','providerMessageIdHash','providerEventIdHash',
        'operatorOpenIdHash','tenantKeyHash','cardNonceHash','evidenceHash'
      )
      $validAccepted = $category -ceq 'REPLY_ACCEPTED' -and $cli.Code -eq 0 -and
        (Test-ExactKeys $payload $acceptedKeys) -and [string]$payload.source -ceq 'feishu_card' -and
        @($binding.allowedOptions) -contains [string]$payload.optionKey -and
        [string]$payload.providerMessageIdHash -ceq [string]$binding.providerMessageIdHash -and
        [string]$payload.cardNonceHash -ceq [string]$binding.cardNonceHash -and
        @('providerMessageIdHash','providerEventIdHash','operatorOpenIdHash','tenantKeyHash','cardNonceHash','evidenceHash' |
          Where-Object { -not (Test-Hex64 $payload[$_]) }).Count -eq 0
      if (-not $validAccepted) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'feishu_reply_invalid' 'Feishu reply evidence was invalid or conflicting; the pending decision was preserved.'
        $result.nextCommands = @('CompleteNoChange', 'ResolveDecisionManual')
        $result.nextCommand = 'CompleteNoChange'
        Write-ProtocolResult $result 15
      }

      $originalTaskId = [string]$state.pendingDecision.taskId
      $resolved = Invoke-StateTool @(
        'ResolveDecision', '-RunId', $RunId, '-DecisionId', [string]$state.pendingDecision.decisionId,
        '-OptionKey', [string]$payload.optionKey, '-ReplySource', 'feishu_card',
        '-ProviderMessageIdHash', [string]$payload.providerMessageIdHash,
        '-ProviderEventIdHash', [string]$payload.providerEventIdHash,
        '-OperatorHash', [string]$payload.operatorOpenIdHash,
        '-TenantKeyHash', [string]$payload.tenantKeyHash,
        '-CardNonceHash', [string]$payload.cardNonceHash,
        '-EvidenceHash', [string]$payload.evidenceHash
      )
      if ($resolved.Code -ne 0) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_resolve_failed' 'The validated Feishu decision reply could not be recorded.'
        Write-ProtocolResult $result $resolved.Code
      }
      $resolvedState = Convert-ChildJson $resolved 'ResolveDecision'
      $session.branchKind = 'selection'
      $session.workType = $null
      $session.taskId = $null
      $session.executor = $null
      $session.resumeTaskId = $originalTaskId
      Save-Session $session
      $result = New-ProtocolResult $true 'inspect_candidate' 'selection' 'preserve_recovery' $null 'Validated Feishu card reply resolved; inspect and register the original task again.'
      $result.taskId = $originalTaskId
      $result.decisionFlow = $resolvedState.decisionFlow
      $result.requiredSources = @($script:ExecutionQueueRelativePath, $script:DecisionStatusRelativePath)
      $result.nextCommand = 'InspectCandidate'
      Write-ProtocolResult $result
    }
    'PrepareDecisionNotification' {
      $session = Read-Session
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'pending_decision_missing' 'PrepareDecisionNotification requires an active pending decision.'
        Write-ProtocolResult $result 15
      }
      try { $config = Read-PrivateDecisionConfig } catch {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'private_config_invalid' $_.Exception.Message
        Write-ProtocolResult $result 15
      }
      $notification = New-PreparedNotificationResult $session $state $config
      if ($null -eq $notification) {
        $result = New-ProtocolResult $false 'retry_exhausted' 'pending_decision' 'preserve_recovery' 'retry_exhausted' 'Decision notification retry limit has been reached.'
        $result.pendingDecision = $state.pendingDecision
        Write-ProtocolResult $result
      }
      $result = New-ProtocolResult $true 'submit_decision_notification' 'pending_decision' 'preserve_recovery' $null 'Transient notification payload prepared.'
      $result.notification = $notification
      $result.nextCommands = @('MarkDecisionSubmitted','MarkDecisionDeliveryFailed')
      $result.nextCommand = 'MarkDecisionSubmitted'
      Write-ProtocolResult $result
    }
    'MarkDecisionSubmitted' {
      $session = Read-Session
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision -or $null -eq $session.notificationContext -or
          [string]$session.notificationContext.decisionId -cne [string]$state.pendingDecision.decisionId) {
        $result = New-ProtocolResult $false 'prepare_decision_notification' 'pending_decision' 'preserve_recovery' 'notification_not_prepared' 'A current prepared notification is required.'
        Write-ProtocolResult $result 15
      }
      if ([string]::IsNullOrWhiteSpace($ProviderMessageId) -or -not (Test-EmailAddressShape $ObservedRecipient)) {
        $result = New-ProtocolResult $false 'submit_decision_notification' 'pending_decision' 'preserve_recovery' 'provider_evidence_invalid' 'ProviderMessageId and one explicit ObservedRecipient are required.'
        Write-ProtocolResult $result 15
      }
      $observedHash = Get-Sha256TextLower (Normalize-EmailTarget $ObservedRecipient)
      $matchesPreparedTarget = $observedHash -ceq [string]$session.notificationContext.normalizedTargetHash
      $status = if ($matchesPreparedTarget) { 'PROVIDER_ACCEPTED' } else { 'MISADDRESSED' }
      $stateArguments = @(
        'RecordDecisionNotification', '-RunId', $RunId, '-NotificationStatus', $status,
        '-RecipientHash', $observedHash, '-ProviderMessageId', $ProviderMessageId
      )
      if (-not $matchesPreparedTarget) { $stateArguments += @('-NotificationError', 'recipient_mismatch') }
      $marked = Invoke-StateTool $stateArguments
      if ($marked.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' $(if ($marked.Error) { $marked.Error } else { 'RecordDecisionNotification failed.' })
        Write-ProtocolResult $result $marked.Code
      }
      $state = Convert-ChildJson $marked 'RecordDecisionNotification'
      Set-SessionNotificationContext $session $null
      $published = Invoke-DecisionStatusPublisher 'PublishPending' $state
      if ($published.Code -ne 0) {
        $message = if ($published.Error) { $published.Error } elseif ($published.Output) { $published.Output } else { 'Decision status publisher failed.' }
        Stop-RegisteredWork $session 'decision_status_publish_failed' $message $published.Code
      }
      $result = New-ProtocolResult $matchesPreparedTarget $(if ($matchesPreparedTarget) { 'decision_delivery_accepted' } else { 'decision_delivery_misaddressed' }) 'pending_decision' 'preserve_recovery' $(if ($matchesPreparedTarget) { $null } else { 'recipient_mismatch' }) $(if ($matchesPreparedTarget) { 'Provider submission accepted for the prepared target.' } else { 'Provider submission target did not match the prepared target.' })
      $result.pendingDecision = $state.pendingDecision
      $result.nextCommand = if ($matchesPreparedTarget) { 'Finish' } else { 'RetryDecisionNotification' }
      Write-ProtocolResult $result
    }
    'RetryDecisionNotification' {
      $session = Read-Session
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision -or [string]::IsNullOrWhiteSpace($DecisionId) -or
          [string]$state.pendingDecision.decisionId -cne $DecisionId) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_id_mismatch' 'RetryDecisionNotification requires the current decision id.'
        Write-ProtocolResult $result 15
      }
      $attempts = @($state.pendingDecision.notificationAttempts)
      if ($attempts.Count -ge 3 -or [string]$state.pendingDecision.status -ceq 'RETRY_EXHAUSTED') {
        $result = New-ProtocolResult $false 'retry_exhausted' 'pending_decision' 'preserve_recovery' 'retry_exhausted' 'Decision notification retry limit has been reached.'
        $result.pendingDecision = $state.pendingDecision
        Write-ProtocolResult $result
      }
      if (-not [string]::IsNullOrWhiteSpace($PriorProviderMessageId) -or -not [string]::IsNullOrWhiteSpace($ObservedRecipient)) {
        if ([string]::IsNullOrWhiteSpace($PriorProviderMessageId) -or -not (Test-EmailAddressShape $ObservedRecipient) -or $null -eq $session.notificationContext) {
          $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'prior_provider_evidence_invalid' 'PriorProviderMessageId and ObservedRecipient must describe the current prepared submission together.'
          Write-ProtocolResult $result 15
        }
        $observedHash = Get-Sha256TextLower (Normalize-EmailTarget $ObservedRecipient)
        if ($observedHash -ceq [string]$session.notificationContext.normalizedTargetHash) {
          $result = New-ProtocolResult $false 'submit_decision_notification' 'pending_decision' 'preserve_recovery' 'prior_submission_matches' 'The prior submission matches the prepared target; record it with MarkDecisionSubmitted.'
          Write-ProtocolResult $result 15
        }
        $marked = Invoke-StateTool @(
          'RecordDecisionNotification', '-RunId', $RunId, '-NotificationStatus', 'MISADDRESSED',
          '-RecipientHash', $observedHash, '-ProviderMessageId', $PriorProviderMessageId,
          '-NotificationError', 'recipient_mismatch'
        )
        if ($marked.Code -ne 0) {
          $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' $(if ($marked.Error) { $marked.Error } else { 'RecordDecisionNotification failed.' })
          Write-ProtocolResult $result $marked.Code
        }
        $state = Convert-ChildJson $marked 'RecordDecisionNotification'
        Set-SessionNotificationContext $session $null
        $attempts = @($state.pendingDecision.notificationAttempts)
        if ($attempts.Count -ge 3 -or [string]$state.pendingDecision.status -ceq 'RETRY_EXHAUSTED') {
          $published = Invoke-DecisionStatusPublisher 'PublishPending' $state
          if ($published.Code -ne 0) { Stop-RegisteredWork $session 'decision_status_publish_failed' 'Decision status publisher failed.' $published.Code }
          $result = New-ProtocolResult $false 'retry_exhausted' 'pending_decision' 'preserve_recovery' 'retry_exhausted' 'Decision notification retry limit has been reached.'
          $result.pendingDecision = $state.pendingDecision
          Write-ProtocolResult $result
        }
      }
      try { $config = Read-PrivateDecisionConfig } catch {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'private_config_invalid' $_.Exception.Message
        Write-ProtocolResult $result 15
      }
      $notification = New-PreparedNotificationResult $session $state $config
      if ($null -eq $notification) {
        $result = New-ProtocolResult $false 'retry_exhausted' 'pending_decision' 'preserve_recovery' 'retry_exhausted' 'Decision notification retry limit has been reached.'
        Write-ProtocolResult $result
      }
      $result = New-ProtocolResult $true 'submit_decision_notification' 'pending_decision' 'preserve_recovery' $null 'Transient retry notification payload prepared.'
      $result.notification = $notification
      $result.nextCommands = @('MarkDecisionSubmitted','MarkDecisionDeliveryFailed')
      $result.nextCommand = 'MarkDecisionSubmitted'
      Write-ProtocolResult $result
    }
    'MarkDecisionDeliveryFailed' {
      $session = Read-Session
      $state = Get-StateSnapshot
      if ($null -eq $state.pendingDecision -or $null -eq $session.notificationContext -or
          [string]$session.notificationContext.decisionId -cne [string]$state.pendingDecision.decisionId) {
        $result = New-ProtocolResult $false 'prepare_decision_notification' 'pending_decision' 'preserve_recovery' 'notification_not_prepared' 'A current prepared notification is required.'
        Write-ProtocolResult $result 15
      }
      if ([string]::IsNullOrWhiteSpace($NotificationError)) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'invalid_arguments' 'NotificationError is required.'
        Write-ProtocolResult $result 15
      }
      $errorCategory = Get-NotificationErrorCategory $NotificationError
      $marked = Invoke-StateTool @(
        'RecordDecisionNotification', '-RunId', $RunId, '-NotificationStatus', 'DELIVERY_FAILED',
        '-RecipientHash', [string]$session.notificationContext.normalizedTargetHash,
        '-NotificationError', $errorCategory
      )
      if ($marked.Code -ne 0) {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'decision_notification_failed' $(if ($marked.Error) { $marked.Error } else { 'MarkDecisionDeliveryFailed failed.' })
        Write-ProtocolResult $result $marked.Code
      }
      $state = Convert-ChildJson $marked 'RecordDecisionNotification'
      Set-SessionNotificationContext $session $null
      $published = Invoke-DecisionStatusPublisher 'PublishPending' $state
      if ($published.Code -ne 0) {
        $message = if ($published.Error) { $published.Error } elseif ($published.Output) { $published.Output } else { 'Decision status publisher failed.' }
        Stop-RegisteredWork $session 'decision_status_publish_failed' $message $published.Code
      }
      $result = New-ProtocolResult $true 'decision_delivery_failed' 'pending_decision' 'preserve_recovery' $null 'Decision delivery failure recorded and published.'
      $result.pendingDecision = $state.pendingDecision
      $result.nextCommand = if ([string]$state.pendingDecision.status -ceq 'RETRY_EXHAUSTED') { $null } else { 'RetryDecisionNotification' }
      Write-ProtocolResult $result
    }
    'ResolveDecisionEmailReply' {
      $session = Read-Session
      if ([string]$session.phase -cne 'identity_checked') {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'invalid_phase' 'ResolveDecisionEmailReply requires a fresh identity_checked run after decision publication is finished.'
        $result.nextCommand = 'Finish'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $pattern = '^\s*(?<id>DEC-[0-9]{8}-[A-Z0-9]+)\s*[：:]\s*(?:选择|选)\s*(?<key>[A-Za-z0-9]+)\s*$'
      if ([string]::IsNullOrWhiteSpace($ReplyText) -or $ReplyText -cnotmatch $pattern -or
          [string]::IsNullOrWhiteSpace($ReplyMessageId) -or -not (Test-EmailAddressShape $ReplyFrom)) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply' 'Decision reply does not match the strict single-option format.'
        $result.nextCommand = 'ResolveDecisionEmailReply'
        Write-ProtocolResult $result 15
      }
      $decisionId = [string]$Matches['id']
      $optionKey = [string]$Matches['key']
      if ($null -eq $state.pendingDecision -or $decisionId -cne [string]$state.pendingDecision.decisionId -or
          @($state.pendingDecision.options | Where-Object { [string]$_.key -ceq $optionKey }).Count -ne 1) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply' 'Decision id or option key is invalid.'
        $result.nextCommand = 'ResolveDecisionEmailReply'
        Write-ProtocolResult $result 15
      }
      try { $config = Read-PrivateDecisionConfig } catch {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'private_config_invalid' $_.Exception.Message
        Write-ProtocolResult $result 15
      }
      $sender = Normalize-EmailTarget $ReplyFrom
      if ($sender -cne [string]$config.allowedReplyFrom -and @($config.aliases) -cnotcontains $sender) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply_source' 'Decision reply sender is not allowed.'
        $result.nextCommand = 'ResolveDecisionEmailReply'
        Write-ProtocolResult $result 15
      }
      $originalTaskId = [string]$state.pendingDecision.taskId
      $resolved = Invoke-StateTool @(
        'ResolveDecision', '-RunId', $RunId, '-DecisionId', $decisionId,
        '-OptionKey', $optionKey, '-ReplySource', 'email',
        '-EvidenceMessageId', $ReplyMessageId, '-EvidenceSender', $sender
      )
      if ($resolved.Code -ne 0) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_resolve_failed' $(if ($resolved.Error) { $resolved.Error } else { 'ResolveDecision failed.' })
        Write-ProtocolResult $result $resolved.Code
      }
      $resolvedState = Convert-ChildJson $resolved 'ResolveDecision'
      $session.phase = 'identity_checked'
      $session.branchKind = 'selection'
      $session.workType = $null
      $session.taskId = $null
      $session.executor = $null
      if ($null -eq $session.PSObject.Properties['resumeTaskId']) {
        $session | Add-Member -NotePropertyName resumeTaskId -NotePropertyValue $originalTaskId
      } else {
        $session.resumeTaskId = $originalTaskId
      }
      Save-Session $session
      $result = New-ProtocolResult $true 'inspect_candidate' 'selection' 'preserve_recovery' $null 'Strict email decision reply resolved; inspect and register the original task again.'
      $result.taskId = $originalTaskId
      $result.decisionFlow = $resolvedState.decisionFlow
      $result.requiredSources = @($script:ExecutionQueueRelativePath, $script:DecisionStatusRelativePath)
      $result.nextCommand = 'InspectCandidate'
      Write-ProtocolResult $result
    }
    'ResolveDecisionManual' {
      $session = Read-Session
      if ([string]$session.phase -cne 'identity_checked') {
        $result = New-ProtocolResult $false 'stopped' 'pending_decision' 'preserve_recovery' 'invalid_phase' 'ResolveDecisionManual requires a fresh identity_checked run.'
        Write-ProtocolResult $result 13
      }
      $state = Get-StateSnapshot
      $pattern = '^\s*(?<id>DEC-[0-9]{8}-[A-Z0-9]+)\s*[：:]\s*(?:选择|选)\s*(?<key>[A-Za-z0-9]+)\s*$'
      if (-not $ManualOverride -or [string]::IsNullOrWhiteSpace($ReplyText) -or $ReplyText -cnotmatch $pattern -or
          [string]::IsNullOrWhiteSpace($CurrentThreadId)) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_manual_override' 'Manual resolution requires an exact reply, CurrentThreadId, and -ManualOverride.'
        Write-ProtocolResult $result 15
      }
      $decisionId = [string]$Matches['id']
      $optionKey = [string]$Matches['key']
      if ($null -eq $state.pendingDecision -or $decisionId -cne [string]$state.pendingDecision.decisionId -or
          @($state.pendingDecision.options | Where-Object { [string]$_.key -ceq $optionKey }).Count -ne 1) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'invalid_reply' 'Decision id or option key is invalid.'
        Write-ProtocolResult $result 15
      }
      $originalTaskId = [string]$state.pendingDecision.taskId
      $arguments = @(
        'ResolveDecision', '-RunId', $RunId, '-DecisionId', $decisionId,
        '-OptionKey', $optionKey, '-ReplySource', 'manual', '-ManualOverride',
        '-EvidenceThreadId', $CurrentThreadId
      )
      if (-not [string]::IsNullOrWhiteSpace($CurrentTurnId)) { $arguments += @('-EvidenceTurnId', $CurrentTurnId) }
      $resolved = Invoke-StateTool $arguments
      if ($resolved.Code -ne 0) {
        $result = New-ProtocolResult $false 'inspect_pending_decision' 'pending_decision' 'preserve_recovery' 'decision_resolve_failed' $(if ($resolved.Error) { $resolved.Error } else { 'ResolveDecision failed.' })
        Write-ProtocolResult $result $resolved.Code
      }
      $resolvedState = Convert-ChildJson $resolved 'ResolveDecision'
      $session.resumeTaskId = $originalTaskId
      Save-Session $session
      $result = New-ProtocolResult $true 'inspect_candidate' 'selection' 'preserve_recovery' $null 'Manual decision resolution recorded; inspect and register the original task again.'
      $result.taskId = $originalTaskId
      $result.decisionFlow = $resolvedState.decisionFlow
      $result.requiredSources = @($script:ExecutionQueueRelativePath, $script:DecisionStatusRelativePath)
      $result.nextCommand = 'InspectCandidate'
      Write-ProtocolResult $result
    }
    default {
      $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'not_implemented' "$Action is not implemented."
      Write-ProtocolResult $result 1
    }
  }
} catch {
  $result = New-ProtocolResult $false 'stopped' 'none' 'stop_read_only' 'controller_error' $_.Exception.Message
  Write-ProtocolResult $result 1
}
