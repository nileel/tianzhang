#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'private-path-acl.ps1')

$script:ReadLimitBytes = 1MB
$script:SearchLimit = 500
$script:ListLimit = 5000
$script:CheckOutputLimitBytes = 64KB

function Throw-DiscoveryError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message
  )

  throw "$Code`: $Message"
}

function Assert-DiscoveryContext {
  param([Parameter(Mandatory = $true)]$Context)

  foreach ($field in @('repositoryRoot', 'runRoot', 'requiredSources', 'allowedRoots', 'discoveryChecks')) {
    if ($field -cnotin @($Context.PSObject.Properties.Name)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message "context field is missing: $field"
    }
  }
  if (-not [IO.Path]::IsPathFullyQualified([string]$Context.repositoryRoot) -or
      -not (Test-Path -LiteralPath ([string]$Context.repositoryRoot) -PathType Container) -or
      -not [IO.Path]::IsPathFullyQualified([string]$Context.runRoot) -or
      -not (Test-Path -LiteralPath ([string]$Context.runRoot) -PathType Container)) {
    Throw-DiscoveryError -Code 'discovery_denied' -Message 'context roots must be existing absolute directories'
  }
  foreach ($field in @('requiredSources', 'allowedRoots', 'discoveryChecks')) {
    $value = $Context.$field
    if ($value -is [string] -or $value -isnot [Collections.IEnumerable]) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message "context $field must be an array"
    }
  }
}

function Get-DiscoveryErrorCode {
  param([Parameter(Mandatory = $true)][Management.Automation.ErrorRecord]$ErrorRecord)

  $message = [string]$ErrorRecord.Exception.Message
  foreach ($code in @('discovery_denied', 'check_failed', 'internal_error')) {
    if ($message.StartsWith($code + ':', [StringComparison]::Ordinal)) {
      return $code
    }
  }
  'internal_error'
}

function ConvertTo-DiscoveryJsonLine {
  param([Parameter(Mandatory = $true)]$Value)

  ($Value | ConvertTo-Json -Depth 30 -Compress) + "`n"
}

function Write-DiscoveryLog {
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Action,
    [Parameter(Mandatory = $true)]$InputValue,
    [Parameter(Mandatory = $true)][bool]$Ok,
    [AllowNull()][string]$SourceSha256,
    [AllowNull()][string]$ErrorCode,
    [AllowNull()][string]$SatisfiedSource,
    [AllowNull()][string]$SatisfiedCheck
  )

  Assert-DiscoveryContext -Context $Context
  $logPath = Join-Path ([IO.Path]::GetFullPath([string]$Context.runRoot)) 'discovery-log.jsonl'
  $sequence = 1
  if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    Assert-PrivatePathAcl -Path $logPath
    $lines = [IO.File]::ReadAllLines($logPath)
    if ($lines.Count -gt 0) {
      try {
        $last = $lines[-1] | ConvertFrom-Json
        $sequence = [int]$last.sequence + 1
      } catch {
        Throw-DiscoveryError -Code 'internal_error' -Message 'discovery log is invalid'
      }
    }
  }
  $entry = [ordered]@{
    sequence = $sequence
    action = $Action
    input = $InputValue
    ok = $Ok
    sourceSha256 = $SourceSha256
    errorCode = $ErrorCode
    satisfiedSource = $SatisfiedSource
    satisfiedCheck = $SatisfiedCheck
    recordedAt = [DateTimeOffset]::UtcNow.ToString('o')
  }
  $line = ConvertTo-DiscoveryJsonLine -Value $entry
  if (Test-Path -LiteralPath $logPath -PathType Leaf) {
    [IO.File]::AppendAllText($logPath, $line, [Text.UTF8Encoding]::new($false))
  } else {
    $tempPath = Join-Path ([IO.Path]::GetFullPath([string]$Context.runRoot)) ('.discovery-log.' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
      [IO.File]::WriteAllText($tempPath, $line, [Text.UTF8Encoding]::new($false))
      Set-PrivatePathAcl -Path $tempPath
      Assert-PrivatePathAcl -Path $tempPath
      [IO.File]::Move($tempPath, $logPath)
    } finally {
      if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
        [IO.File]::Delete($tempPath)
      }
    }
  }
  Assert-PrivatePathAcl -Path $logPath
}

function Normalize-DiscoveryPath {
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Path
  )

  try {
    Normalize-ProjectPath -Path $Path -RepositoryRoot ([IO.Path]::GetFullPath([string]$Context.repositoryRoot))
  } catch {
    Throw-DiscoveryError -Code 'discovery_denied' -Message $_.Exception.Message
  }
}

function Test-PathUnderRoot {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Root
  )

  $Path -ceq $Root -or $Path.StartsWith($Root.TrimEnd('/') + '/', [StringComparison]::Ordinal)
}

function Assert-ReadablePath {
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Path
  )

  if ($Path -cin @($Context.requiredSources)) {
    return
  }
  foreach ($root in @($Context.allowedRoots)) {
    if (Test-PathUnderRoot -Path $Path -Root ([string]$root)) {
      return
    }
  }
  Throw-DiscoveryError -Code 'discovery_denied' -Message 'path is outside requiredSources and allowedRoots'
}

function Assert-AllowedDiscoveryRoot {
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Root
  )

  foreach ($allowedRoot in @($Context.allowedRoots)) {
    if (Test-PathUnderRoot -Path $Root -Root ([string]$allowedRoot)) {
      return
    }
  }
  Throw-DiscoveryError -Code 'discovery_denied' -Message 'root is outside allowedRoots'
}

function Resolve-DiscoveryProjectPath {
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Path
  )

  Join-Path ([IO.Path]::GetFullPath([string]$Context.repositoryRoot)) ($Path.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Get-FileSha256 {
  param([Parameter(Mandatory = $true)][string]$Path)

  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path))).ToLowerInvariant()
}

function ConvertFrom-BoundedUtf8 {
  param(
    [Parameter(Mandatory = $true)][byte[]]$Bytes,
    [Parameter(Mandatory = $true)][int]$Limit
  )

  $length = [Math]::Min($Bytes.Length, $Limit)
  $decoder = [Text.UTF8Encoding]::new($false, $true)
  while ($length -ge 0) {
    try {
      return [pscustomobject]@{
        content = $decoder.GetString($Bytes, 0, $length)
        truncated = $Bytes.Length -gt $length
      }
    } catch [Text.DecoderFallbackException] {
      if ($Bytes.Length -le $Limit -or $Limit - $length -ge 3) {
        Throw-DiscoveryError -Code 'discovery_denied' -Message 'file is not valid UTF-8'
      }
      $length--
    }
  }
  Throw-DiscoveryError -Code 'discovery_denied' -Message 'file is not valid UTF-8'
}

function Assert-FixedGlob {
  param([Parameter(Mandatory = $true)][string]$Glob)

  if ([string]::IsNullOrWhiteSpace($Glob) -or
      $Glob.Contains('\') -or
      $Glob.Contains('/') -or
      $Glob.Contains('..') -or
      $Glob.IndexOfAny([char[]]@(0, 10, 13)) -ge 0) {
    Throw-DiscoveryError -Code 'discovery_denied' -Message 'glob is invalid'
  }
}

function Invoke-DiscoverRead {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Path
  )

  $inputValue = [ordered]@{ path = $Path }
  try {
    Assert-DiscoveryContext -Context $Context
    $normalized = Normalize-DiscoveryPath -Context $Context -Path $Path
    $inputValue.path = $normalized
    Assert-ReadablePath -Context $Context -Path $normalized
    $fullPath = Resolve-DiscoveryProjectPath -Context $Context -Path $normalized
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'read path is not a file'
    }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $bounded = ConvertFrom-BoundedUtf8 -Bytes $bytes -Limit $script:ReadLimitBytes
    $sourceSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $result = [pscustomobject][ordered]@{
      path = $normalized
      sha256 = $sourceSha256
      content = $bounded.content
      truncated = [bool]$bounded.truncated
    }
    $satisfiedSource = if ($normalized -cin @($Context.requiredSources)) { $normalized } else { $null }
    Write-DiscoveryLog -Context $Context -Action 'DiscoverRead' -InputValue $inputValue -Ok $true -SourceSha256 $sourceSha256 -ErrorCode $null -SatisfiedSource $satisfiedSource -SatisfiedCheck $null
    $result
  } catch {
    $code = Get-DiscoveryErrorCode -ErrorRecord $_
    Write-DiscoveryLog -Context $Context -Action 'DiscoverRead' -InputValue $inputValue -Ok $false -SourceSha256 $null -ErrorCode $code -SatisfiedSource $null -SatisfiedCheck $null
    throw
  }
}

function Read-RgJsonLine {
  param([Parameter(Mandatory = $true)][string]$Line)

  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
    $Line | ConvertFrom-Json -DateKind String
  } else {
    $Line | ConvertFrom-Json
  }
}

function Invoke-DiscoverSearch {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Pattern,
    [string]$Glob = '*'
  )

  $inputValue = [ordered]@{ root = $Root; pattern = $Pattern; glob = $Glob }
  try {
    Assert-DiscoveryContext -Context $Context
    if ([string]::IsNullOrEmpty($Pattern) -or $Pattern.Length -gt 4096 -or $Pattern.IndexOfAny([char[]]@(0, 10, 13)) -ge 0) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'search pattern is invalid'
    }
    Assert-FixedGlob -Glob $Glob
    $normalizedRoot = Normalize-DiscoveryPath -Context $Context -Path $Root
    $inputValue.root = $normalizedRoot
    Assert-AllowedDiscoveryRoot -Context $Context -Root $normalizedRoot
    $fullRoot = Resolve-DiscoveryProjectPath -Context $Context -Path $normalizedRoot
    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'search root is not a directory'
    }
    $rg = Get-Command rg -ErrorAction SilentlyContinue
    if ($null -eq $rg) {
      Throw-DiscoveryError -Code 'internal_error' -Message 'rg is unavailable'
    }
    $arguments = @('--json', '--color', 'never', '--glob', $Glob, '--', $Pattern, $fullRoot)
    $raw = @(& $rg.Source @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -notin @(0, 1)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'rg rejected the search request'
    }
    $items = @()
    foreach ($rawLine in $raw) {
      $line = [string]$rawLine
      if ([string]::IsNullOrWhiteSpace($line)) { continue }
      try {
        $record = Read-RgJsonLine -Line $line
      } catch {
        Throw-DiscoveryError -Code 'internal_error' -Message 'rg returned invalid JSON'
      }
      if ($record.type -cne 'match' -or $null -eq $record.data.path.text) { continue }
      $relative = [IO.Path]::GetRelativePath([string]$Context.repositoryRoot, [IO.Path]::GetFullPath([string]$record.data.path.text)).Replace('\', '/')
      $relative = Normalize-DiscoveryPath -Context $Context -Path $relative
      Assert-ReadablePath -Context $Context -Path $relative
      $items += [pscustomobject][ordered]@{
        path = $relative
        line = [int]$record.data.line_number
        text = ([string]$record.data.lines.text).TrimEnd("`r", "`n")
      }
    }
    $orderedItems = @($items | Sort-Object path, line, text)
    $truncated = $orderedItems.Count -gt $script:SearchLimit
    $limited = @($orderedItems | Select-Object -First $script:SearchLimit)
    $result = [pscustomobject][ordered]@{ items = $limited; truncated = $truncated }
    $sourceSha256 = Get-Sha256Text -Text ($limited | ConvertTo-Json -Depth 10 -Compress)
    Write-DiscoveryLog -Context $Context -Action 'DiscoverSearch' -InputValue $inputValue -Ok $true -SourceSha256 $sourceSha256 -ErrorCode $null -SatisfiedSource $null -SatisfiedCheck $null
    $result
  } catch {
    $code = Get-DiscoveryErrorCode -ErrorRecord $_
    Write-DiscoveryLog -Context $Context -Action 'DiscoverSearch' -InputValue $inputValue -Ok $false -SourceSha256 $null -ErrorCode $code -SatisfiedSource $null -SatisfiedCheck $null
    throw
  }
}

function Get-BoundedFileList {
  param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Glob,
    [Parameter(Mandatory = $true)]$Context
  )

  $directories = [Collections.Generic.Stack[string]]::new()
  $directories.Push($Root)
  $paths = [Collections.Generic.List[string]]::new()
  while ($directories.Count -gt 0) {
    $current = $directories.Pop()
    $entries = @([IO.Directory]::EnumerateFileSystemEntries($current) | Sort-Object)
    foreach ($entry in $entries) {
      $attributes = [IO.File]::GetAttributes($entry)
      if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        Throw-DiscoveryError -Code 'discovery_denied' -Message 'list encountered a reparse point'
      }
      if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
        $directories.Push($entry)
      } elseif ([IO.Enumeration.FileSystemName]::MatchesSimpleExpression($Glob, [IO.Path]::GetFileName($entry), $true)) {
        $relative = [IO.Path]::GetRelativePath([string]$Context.repositoryRoot, $entry).Replace('\', '/')
        $paths.Add((Normalize-DiscoveryPath -Context $Context -Path $relative))
      }
    }
  }
  @($paths | Sort-Object)
}

function Invoke-DiscoverList {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Glob
  )

  $inputValue = [ordered]@{ root = $Root; glob = $Glob }
  try {
    Assert-DiscoveryContext -Context $Context
    Assert-FixedGlob -Glob $Glob
    $normalizedRoot = Normalize-DiscoveryPath -Context $Context -Path $Root
    $inputValue.root = $normalizedRoot
    Assert-AllowedDiscoveryRoot -Context $Context -Root $normalizedRoot
    $fullRoot = Resolve-DiscoveryProjectPath -Context $Context -Path $normalizedRoot
    if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'list root is not a directory'
    }
    $paths = @(Get-BoundedFileList -Root $fullRoot -Glob $Glob -Context $Context)
    $truncated = $paths.Count -gt $script:ListLimit
    $limited = @($paths | Select-Object -First $script:ListLimit)
    $result = [pscustomobject][ordered]@{ items = $limited; truncated = $truncated }
    $sourceSha256 = Get-Sha256Text -Text ($limited -join "`n")
    Write-DiscoveryLog -Context $Context -Action 'DiscoverList' -InputValue $inputValue -Ok $true -SourceSha256 $sourceSha256 -ErrorCode $null -SatisfiedSource $null -SatisfiedCheck $null
    $result
  } catch {
    $code = Get-DiscoveryErrorCode -ErrorRecord $_
    Write-DiscoveryLog -Context $Context -Action 'DiscoverList' -InputValue $inputValue -Ok $false -SourceSha256 $null -ErrorCode $code -SatisfiedSource $null -SatisfiedCheck $null
    throw
  }
}

function Invoke-DiscoverCheck {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Context,
    [Parameter(Mandatory = $true)][string]$CheckId
  )

  $inputValue = [ordered]@{ checkId = $CheckId }
  try {
    Assert-DiscoveryContext -Context $Context
    if ($CheckId -cne 'data-chain-readonly' -or $CheckId -cnotin @($Context.discoveryChecks)) {
      Throw-DiscoveryError -Code 'discovery_denied' -Message 'check id is not registered'
    }
    $scriptRelative = Normalize-DiscoveryPath -Context $Context -Path 'tools/check-data-chain.ps1'
    $scriptPath = Resolve-DiscoveryProjectPath -Context $Context -Path $scriptRelative
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
      Throw-DiscoveryError -Code 'internal_error' -Message 'registered check script is missing'
    }
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwsh) {
      Throw-DiscoveryError -Code 'internal_error' -Message 'PowerShell 7 is unavailable'
    }
    Push-Location ([string]$Context.repositoryRoot)
    try {
      $rawOutput = @(& $pwsh.Source -NoProfile -ExecutionPolicy Bypass -File $scriptPath 2>&1)
      $exitCode = $LASTEXITCODE
    } finally {
      Pop-Location
    }
    $outputText = ($rawOutput | ForEach-Object { [string]$_ }) -join "`n"
    $outputBytes = [Text.UTF8Encoding]::new($false).GetBytes($outputText)
    $bounded = ConvertFrom-BoundedUtf8 -Bytes $outputBytes -Limit $script:CheckOutputLimitBytes
    $result = [pscustomobject][ordered]@{
      checkId = $CheckId
      exitCode = $exitCode
      output = $bounded.content
      truncated = [bool]$bounded.truncated
    }
    $sourceSha256 = Get-Sha256Text -Text $outputText
    Write-DiscoveryLog -Context $Context -Action 'DiscoverCheck' -InputValue $inputValue -Ok $true -SourceSha256 $sourceSha256 -ErrorCode $null -SatisfiedSource $null -SatisfiedCheck $CheckId
    $result
  } catch {
    $code = Get-DiscoveryErrorCode -ErrorRecord $_
    Write-DiscoveryLog -Context $Context -Action 'DiscoverCheck' -InputValue $inputValue -Ok $false -SourceSha256 $null -ErrorCode $code -SatisfiedSource $null -SatisfiedCheck $null
    throw
  }
}

Export-ModuleMember -Function @(
  'Invoke-DiscoverRead',
  'Invoke-DiscoverSearch',
  'Invoke-DiscoverList',
  'Invoke-DiscoverCheck'
)
