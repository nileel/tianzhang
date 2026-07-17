#requires -Version 7.0

Set-StrictMode -Version Latest

$script:ForbiddenDiagnosticFields = @(
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

function Throw-TitleError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message
  )

  throw "$Code`: $Message"
}

function New-TitleRequest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Model,
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$ThreadId,
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$MetadataThreadId,
    [Parameter(Mandatory = $true)][string]$TaskTitle
  )

  if ([string]::IsNullOrWhiteSpace($Model) -or
      [string]::IsNullOrWhiteSpace($ThreadId) -or
      [string]::IsNullOrWhiteSpace($MetadataThreadId)) {
    Throw-TitleError -Code 'metadata_missing' -Message 'model and both thread id fields are required'
  }
  $topLevelGuid = [guid]::Empty
  $metadataGuid = [guid]::Empty
  if (-not [guid]::TryParse($ThreadId, [ref]$topLevelGuid) -or
      -not [guid]::TryParse($MetadataThreadId, [ref]$metadataGuid) -or
      $topLevelGuid -eq [guid]::Empty -or
      $metadataGuid -eq [guid]::Empty) {
    Throw-TitleError -Code 'metadata_missing' -Message 'both thread id fields must be non-empty UUIDs'
  }
  if (-not $ThreadId.Equals($MetadataThreadId, [StringComparison]::Ordinal)) {
    Throw-TitleError -Code 'thread_id_mismatch' -Message 'top-level and metadata thread ids differ'
  }
  if ([string]::IsNullOrWhiteSpace($TaskTitle)) {
    Throw-TitleError -Code 'invalid_request' -Message 'task title is required'
  }

  [pscustomobject][ordered]@{
    model = $Model
    threadId = $ThreadId
    metadataThreadId = $MetadataThreadId
    title = 'TZG｜' + $TaskTitle
  }
}

function Get-TitleToolPayload {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    $TitleRequest
  )

  foreach ($field in @('threadId', 'title')) {
    if ($field -cnotin @($TitleRequest.PSObject.Properties.Name) -or
        [string]::IsNullOrWhiteSpace([string]$TitleRequest.$field)) {
      Throw-TitleError -Code 'invalid_request' -Message "title request is missing $field"
    }
  }
  [pscustomobject][ordered]@{
    threadId = [string]$TitleRequest.threadId
    title = [string]$TitleRequest.title
  }
}

function ConvertTo-SanitizedTitleDiagnostic {
  param([AllowNull()][string]$Diagnostic)

  if ([string]::IsNullOrEmpty($Diagnostic)) {
    return ''
  }
  $normalized = $Diagnostic -replace "`r`n?", "`n"
  $normalized = $normalized -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', ''
  foreach ($field in $script:ForbiddenDiagnosticFields) {
    if ($normalized.IndexOf($field, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
      return '[REDACTED]'
    }
  }
  if ($normalized.Length -gt 512) {
    return $normalized.Substring(0, 512)
  }
  $normalized
}

function Set-ActiveRunField {
  param(
    [Parameter(Mandatory = $true)]$ActiveRun,
    [Parameter(Mandatory = $true)][string]$Name,
    [AllowNull()]$Value
  )

  if ($ActiveRun -is [Collections.IDictionary]) {
    $ActiveRun[$Name] = $Value
  } else {
    $ActiveRun | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
  }
}

function Record-TitleResult {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$State,
    [Parameter(Mandatory = $true)][bool]$Succeeded,
    [AllowNull()][string]$Diagnostic = ''
  )

  if ([string]$State.phase -cne 'DISCOVERING' -or $null -eq $State.activeRun) {
    Throw-TitleError -Code 'invalid_state' -Message 'title result requires an active DISCOVERING run'
  }
  Set-ActiveRunField -ActiveRun $State.activeRun -Name 'titleStatus' -Value $(if ($Succeeded) { 'SUCCEEDED' } else { 'FAILED' })
  Set-ActiveRunField -ActiveRun $State.activeRun -Name 'titleDiagnostic' -Value (ConvertTo-SanitizedTitleDiagnostic -Diagnostic $Diagnostic)
  Set-ActiveRunField -ActiveRun $State.activeRun -Name 'nextAction' -Value 'DiscoverRead'
  $State
}

Export-ModuleMember -Function @(
  'New-TitleRequest',
  'Get-TitleToolPayload',
  'Record-TitleResult'
)
