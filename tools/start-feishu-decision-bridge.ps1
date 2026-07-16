#requires -Version 7.0

param(
  [string]$ConfigPath = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller.feishu.private.json')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'private-path-acl.ps1')

function Resolve-AbsolutePath {
  param([string]$Path, [string]$Label)

  if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label is invalid" }
  $resolved = [IO.Path]::GetFullPath($Path)
  if (-not [IO.Path]::IsPathFullyQualified($resolved)) { throw "$Label is invalid" }
  return $resolved
}

function Test-PrivateAcl {
  param([string]$Path, [switch]$AllowInherited)

  $allowed = @((Get-PrivateAclSids).Value)
  $acl = Get-Acl -LiteralPath $Path
  $rules = @($acl.Access)
  if (-not $AllowInherited -and -not $acl.AreAccessRulesProtected) { return $false }
  if ($rules.Count -eq 0) { return $false }
  foreach ($rule in $rules) {
    $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
    if (
      $sid -notin $allowed -or
      (-not $AllowInherited -and $rule.IsInherited) -or
      $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
      ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl
    ) {
      return $false
    }
  }
  return $true
}

function Initialize-PrivateDirectory {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
    New-Item -ItemType Directory -Path $Path -Force | Out-Null
    Set-PrivatePathAcl -Path $Path -Directory
    return
  }
  if (-not (Test-PrivateAcl $Path)) { throw 'Private bridge directory ACL is unsafe' }
}

function Read-StrictConfig {
  param([string]$Path)

  $fullPath = Resolve-AbsolutePath $Path 'ConfigPath'
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf) -or -not (Test-PrivateAcl $fullPath)) {
    throw 'Private configuration is missing or unsafe'
  }
  if ((Get-Item -LiteralPath $fullPath).Length -gt 64KB) { throw 'Private configuration is invalid' }
  try {
    $json = [IO.File]::ReadAllText($fullPath, [Text.UTF8Encoding]::new($false, $true))
    $config = $json | ConvertFrom-Json -AsHashtable
  } catch {
    throw 'Private configuration is invalid'
  }
  $expected = @(
    'schemaVersion', 'appId', 'appSecret', 'recipient', 'expectedTenantKey',
    'pairedOperatorOpenIdHash', 'hmacKey', 'stateRoot'
  )
  if ($config.Count -ne $expected.Count -or @($expected | Where-Object { -not $config.Contains($_) }).Count -ne 0) {
    throw 'Private configuration is invalid'
  }
  if (
    $config.schemaVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace([string]$config.appId) -or
    [string]::IsNullOrWhiteSpace([string]$config.appSecret) -or
    $config.recipient.type -notin @('email', 'open_id') -or
    [string]::IsNullOrWhiteSpace([string]$config.recipient.value) -or
    [string]::IsNullOrWhiteSpace([string]$config.hmacKey)
  ) {
    throw 'Private configuration is invalid'
  }
  $config.stateRoot = Resolve-AbsolutePath ([string]$config.stateRoot) 'stateRoot'
  return $config
}

function Protect-LogLine {
  param([string]$Line, [string[]]$SensitiveValues)

  $sanitized = $Line
  foreach ($value in @($SensitiveValues | Where-Object { -not [string]::IsNullOrEmpty($_) } | Sort-Object Length -Descending -Unique)) {
    $sanitized = $sanitized.Replace($value, '[REDACTED]', [StringComparison]::Ordinal)
  }
  $sanitized = [regex]::Replace($sanitized, '[\p{Cc}\p{Cf}\p{Cs}\p{Zl}\p{Zp}]', ' ')
  $sanitized = [regex]::Replace($sanitized, '[\w.%+-]+@[\w.-]+\.[A-Za-z]{2,}', '[REDACTED]')
  $sanitized = [regex]::Replace($sanitized, '\bou_[A-Za-z0-9_-]+\b', '[REDACTED]')
  if ($sanitized.Length -gt 2048) { $sanitized = $sanitized.Substring(0, 2048) }
  return $sanitized.Trim()
}

if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required' }
$resolvedConfigPath = Resolve-AbsolutePath $ConfigPath 'ConfigPath'
$config = Read-StrictConfig $resolvedConfigPath
Initialize-PrivateDirectory $config.stateRoot
$logRoot = Join-Path $config.stateRoot 'logs'
if (-not (Test-Path -LiteralPath $logRoot -PathType Container)) {
  New-Item -ItemType Directory -Path $logRoot | Out-Null
}
if (-not (Test-PrivateAcl $logRoot -AllowInherited)) { throw 'Bridge log directory ACL is unsafe' }

$mutex = [Threading.Mutex]::new($false, 'Local\TianZhang-Feishu-Decision-Bridge')
$ownsMutex = $false
try {
  try { $ownsMutex = $mutex.WaitOne(0, $false) }
  catch [Threading.AbandonedMutexException] { $ownsMutex = $true }
  if (-not $ownsMutex) { return }

  $node = Get-Command node -ErrorAction SilentlyContinue
  if ($null -eq $node) { throw 'Node runtime is unavailable' }
  $bridgePath = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\bridge.mjs'
  if (-not (Test-Path -LiteralPath $bridgePath -PathType Leaf)) { throw 'Bridge entrypoint is missing' }
  $logPath = Join-Path $logRoot ('bridge-' + [datetime]::UtcNow.ToString('yyyyMMdd') + '.log')
  $sensitive = @(
    [string]$config.appId,
    [string]$config.appSecret,
    [string]$config.recipient.value,
    [string]$config.expectedTenantKey,
    [string]$config.hmacKey
  )
  $oldConfig = [Environment]::GetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', 'Process')
  try {
    [Environment]::SetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', $resolvedConfigPath, 'Process')
    $startLine = "$(Get-Date -AsUTC -Format o) bridge_start"
    [IO.File]::AppendAllText($logPath, $startLine + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    & $node.Source $bridgePath 2>&1 | ForEach-Object {
      $line = Protect-LogLine ([string]$_) $sensitive
      if (-not [string]::IsNullOrWhiteSpace($line)) {
        [IO.File]::AppendAllText(
          $logPath,
          "$(Get-Date -AsUTC -Format o) $line" + [Environment]::NewLine,
          [Text.UTF8Encoding]::new($false)
        )
      }
    }
    if ($LASTEXITCODE -ne 0) { throw 'Bridge process exited unexpectedly' }
  } finally {
    [Environment]::SetEnvironmentVariable('FEISHU_DECISION_CONFIG_PATH', $oldConfig, 'Process')
  }
} finally {
  if ($ownsMutex) { $mutex.ReleaseMutex() }
  $mutex.Dispose()
}
