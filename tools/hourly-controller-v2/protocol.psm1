#requires -Version 7.0

Set-StrictMode -Version Latest

$script:AllowedActions = @(
  'Start',
  'RecordTitleResult',
  'DiscoverRead',
  'DiscoverSearch',
  'DiscoverList',
  'DiscoverCheck',
  'SubmitManifest',
  'BeginMutation',
  'Finish',
  'Abort',
  'CreateDecision',
  'SendDecision',
  'ConsumeDecision',
  'MigrateLegacy',
  'Show'
)

$script:AllowedErrorCodes = @(
  'invalid_request',
  'invalid_state',
  'metadata_missing',
  'thread_id_mismatch',
  'registry_invalid',
  'task_not_found',
  'task_not_executable',
  'discovery_denied',
  'discovery_incomplete',
  'source_changed',
  'manifest_invalid',
  'decision_coverage_incomplete',
  'baseline_changed',
  'head_changed',
  'path_outside_scope',
  'check_failed',
  'decision_invalid',
  'feishu_unavailable',
  'migration_invalid',
  'internal_error'
)

$script:ForbiddenFields = @(
  'appSecret',
  'tenantKey',
  'openId',
  'chatId',
  'messageId',
  'eventId',
  'providerMessageId',
  'providerEventId',
  'evidenceHash',
  'rawEvent'
)

function Assert-NoReparsePoint {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Root,
    [Parameter(Mandatory = $true)]
    [string[]]$Segments
  )

  $current = $Root
  foreach ($segment in $Segments) {
    $current = Join-Path $current $segment
    if (-not (Test-Path -LiteralPath $current)) {
      break
    }
    $attributes = [IO.File]::GetAttributes($current)
    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "Path traverses a reparse point: $segment"
    }
  }
}

function Read-ControllerRequest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path)) {
    throw 'Controller request path must be absolute'
  }

  $fullPath = [IO.Path]::GetFullPath($Path)
  $privateRoot = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE '.codex\automation-state')).TrimEnd('\', '/')
  $privatePrefix = $privateRoot + [IO.Path]::DirectorySeparatorChar
  if (-not $fullPath.StartsWith($privatePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Controller request path must be inside the private state root'
  }
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    throw 'Controller request file does not exist'
  }

  $relative = $fullPath.Substring($privatePrefix.Length)
  $segments = $relative.Split([char]'\', [StringSplitOptions]::RemoveEmptyEntries)
  Assert-NoReparsePoint -Root $privateRoot -Segments $segments

  try {
    $decoder = [Text.UTF8Encoding]::new($false, $true)
    $text = $decoder.GetString([IO.File]::ReadAllBytes($fullPath)).TrimStart([char]0xFEFF)
  } catch [Text.DecoderFallbackException] {
    throw 'Controller request must be valid UTF-8 JSON'
  }

  if ($text -notmatch '^\s*\{') {
    throw 'Controller request must be a JSON object'
  }
  try {
    $request = $text | ConvertFrom-Json -AsHashtable
  } catch {
    throw 'Controller request must be a valid JSON object'
  }
  if ($request -isnot [Collections.IDictionary]) {
    throw 'Controller request must be a JSON object'
  }
  if (-not $request.Contains('schemaVersion') -or $request.schemaVersion -ne 1) {
    throw 'Controller request schemaVersion must be 1'
  }

  $request
}

function New-ControllerResponse {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Action,
    [string]$RunId = '00000000-0000-0000-0000-000000000000',
    [string]$TaskId = '',
    [string]$Phase = 'IDLE',
    [string]$NextAction = '',
    [AllowNull()]
    [string]$ErrorCode = $null,
    [object[]]$ChangedPaths = @(),
    [object[]]$RequiredSources = @(),
    [object[]]$RequiredChecks = @(),
    [object[]]$DecisionConstraints = @(),
    [AllowNull()]
    [object]$Result = $null
  )

  if ($Action -cnotin $script:AllowedActions) {
    throw "Unknown controller action: $Action"
  }
  $hasError = -not [string]::IsNullOrEmpty($ErrorCode)
  if ($hasError -and $ErrorCode -cnotin $script:AllowedErrorCodes) {
    throw "Unknown controller error code: $ErrorCode"
  }
  if ($null -eq $Result) {
    $Result = [ordered]@{}
  }

  [pscustomobject][ordered]@{
    schemaVersion = 1
    ok = -not $hasError
    action = $Action
    runId = $RunId
    taskId = $TaskId
    phase = $Phase
    nextAction = $NextAction
    errorCode = if ($hasError) { $ErrorCode } else { $null }
    changedPaths = @($ChangedPaths)
    requiredSources = @($RequiredSources)
    requiredChecks = @($RequiredChecks)
    decisionConstraints = @($DecisionConstraints)
    result = $Result
  }
}

function ConvertTo-RedactedControllerValue {
  param([AllowNull()]$Value)

  if ($null -eq $Value) {
    return $null
  }
  if ($Value -is [string] -or $Value.GetType().IsPrimitive -or $Value -is [decimal] -or $Value -is [datetime]) {
    return $Value
  }
  if ($Value -is [Collections.IDictionary]) {
    $copy = [ordered]@{}
    foreach ($key in $Value.Keys) {
      $name = [string]$key
      $copy[$name] = if ($name -iin $script:ForbiddenFields) {
        '[REDACTED]'
      } else {
        ConvertTo-RedactedControllerValue -Value $Value[$key]
      }
    }
    return $copy
  }
  if ($Value -is [Collections.IEnumerable]) {
    return @($Value | ForEach-Object { ConvertTo-RedactedControllerValue -Value $_ })
  }

  $copy = [ordered]@{}
  foreach ($property in $Value.PSObject.Properties) {
    $copy[$property.Name] = if ($property.Name -iin $script:ForbiddenFields) {
      '[REDACTED]'
    } else {
      ConvertTo-RedactedControllerValue -Value $property.Value
    }
  }
  $copy
}

function Write-ControllerResponse {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [object]$Response,
    [string]$LogMessage = ''
  )

  if ($LogMessage.Length -gt 0) {
    [Console]::Error.WriteLine($LogMessage)
  }
  $redacted = ConvertTo-RedactedControllerValue -Value $Response
  Write-Output ($redacted | ConvertTo-Json -Depth 100 -Compress)
}

function Normalize-ProjectPath {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or
      [IO.Path]::IsPathFullyQualified($Path) -or
      $Path.Contains('\')) {
    throw 'Invalid project path'
  }
  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot) -or
      -not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
    throw 'Repository root must be an existing absolute directory'
  }

  $segments = $Path.Split([char]'/', [StringSplitOptions]::None)
  $invalidNameCharacters = [IO.Path]::GetInvalidFileNameChars()
  foreach ($segment in $segments) {
    if ([string]::IsNullOrEmpty($segment) -or
        $segment -ceq '.' -or
        $segment -ceq '..' -or
        $segment.IndexOfAny($invalidNameCharacters) -ge 0) {
      throw 'Invalid project path'
    }
  }

  $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  $nativeRelativePath = $segments -join [IO.Path]::DirectorySeparatorChar
  $candidate = [IO.Path]::GetFullPath((Join-Path $root $nativeRelativePath))
  $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
  if (-not $candidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Invalid project path: repository escape'
  }
  Assert-NoReparsePoint -Root $root -Segments $segments

  $segments -join '/'
}

function Get-Sha256Text {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$Text
  )

  $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

Export-ModuleMember -Function @(
  'Read-ControllerRequest',
  'New-ControllerResponse',
  'Write-ControllerResponse',
  'Normalize-ProjectPath',
  'Get-Sha256Text'
)
