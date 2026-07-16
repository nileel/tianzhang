#requires -Version 7.0

param(
  [Parameter(Mandatory = $true, Position = 0)]
  [ValidateSet('Plan', 'Install', 'Uninstall', 'Status')]
  [string]$Action,

  [string]$ConfigPath = (Join-Path $env:USERPROFILE '.codex\automation-state\tzg-hourly-controller.feishu.private.json'),

  [hashtable]$SchedulerAdapter
)

$ErrorActionPreference = 'Stop'
$script:TaskName = 'TianZhang-Feishu-Decision-Bridge'
$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:PackageRoot = Join-Path $PSScriptRoot 'feishu-decision-bridge'
$script:StartScript = Join-Path $PSScriptRoot 'start-feishu-decision-bridge.ps1'
$script:BridgeEntry = Join-Path $script:PackageRoot 'src\bridge.mjs'

function Resolve-AbsolutePath {
  param([string]$Path, [string]$Label)

  if ([string]::IsNullOrWhiteSpace($Path)) { throw "$Label is invalid" }
  $resolved = [IO.Path]::GetFullPath($Path)
  if (-not [IO.Path]::IsPathFullyQualified($resolved)) { throw "$Label is invalid" }
  return $resolved
}

function Write-SanitizedJson {
  param($Value)

  Write-Output ($Value | ConvertTo-Json -Depth 8 -Compress)
}

function Get-TaskPlan {
  $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
  if ($null -eq $pwsh) { throw 'PowerShell 7 is unavailable' }
  $startScript = Resolve-AbsolutePath $script:StartScript 'start script'
  if ($startScript.Contains('"', [StringComparison]::Ordinal)) { throw 'Start script path is invalid' }
  [ordered]@{
    schemaVersion = 1
    taskName = $script:TaskName
    trigger = 'AtLogOn'
    execute = [IO.Path]::GetFullPath($pwsh.Source)
    arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$startScript`""
    workingDirectory = [IO.Path]::GetFullPath($script:RepositoryRoot)
    startScript = $startScript
    hidden = $true
    multipleInstances = 'IgnoreNew'
  }
}

function New-RealSchedulerAdapter {
  @{
    GetNodeVersion = {
      $output = @(& node --version 2>&1)
      if ($LASTEXITCODE -ne 0) { throw 'Node runtime is unavailable' }
      [string]($output | Select-Object -Last 1)
    }
    InstallPackages = {
      param([string]$PackageDirectory)
      Push-Location $PackageDirectory
      try {
        $null = @(& npm ci --ignore-scripts 2>&1)
        if ($LASTEXITCODE -ne 0) { throw 'Bridge package installation failed' }
      } finally {
        Pop-Location
      }
    }
    GetTask = {
      param([string]$TaskName)
      Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    StopTask = {
      param([string]$TaskName)
      Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    }
    GetProcesses = {
      @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine)
    }
    StopProcess = {
      param([int]$ProcessId)
      Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
    UpsertTask = {
      param($Plan)
      $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
      $taskAction = New-ScheduledTaskAction -Execute $Plan.execute -Argument $Plan.arguments -WorkingDirectory $Plan.workingDirectory
      $trigger = New-ScheduledTaskTrigger -AtLogOn -User $identity
      $settings = New-ScheduledTaskSettingsSet -Hidden -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
      $principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
      Register-ScheduledTask -TaskName $Plan.taskName -Action $taskAction -Trigger $trigger -Settings $settings -Principal $principal -Force | Out-Null
      Start-ScheduledTask -TaskName $Plan.taskName
    }
    RemoveTask = {
      param([string]$TaskName)
      Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
      Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    }
    GetTaskStatus = {
      param([string]$TaskName)
      $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
      if ($null -eq $task) { 'NotInstalled' } else { [string]$task.State }
    }
  }
}

function Assert-TestAdapterSafe {
  if ($null -eq $SchedulerAdapter) { return }
  $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  $fullConfig = Resolve-AbsolutePath $ConfigPath 'ConfigPath'
  if (-not $fullConfig.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SchedulerAdapter is restricted to temporary test configuration'
  }
  $expected = @(
    'GetNodeVersion', 'InstallPackages', 'GetTask', 'StopTask',
    'GetProcesses', 'StopProcess', 'UpsertTask', 'RemoveTask', 'GetTaskStatus'
  )
  if ($SchedulerAdapter.Count -ne $expected.Count) { throw 'SchedulerAdapter is invalid' }
  foreach ($key in $expected) {
    if (-not $SchedulerAdapter.ContainsKey($key) -or $SchedulerAdapter[$key] -isnot [scriptblock]) {
      throw 'SchedulerAdapter is invalid'
    }
  }
}

function Invoke-Adapter {
  param([string]$Operation, [object[]]$Arguments = @())

  & $script:Adapter[$Operation] @Arguments
}

function Assert-NodeVersion {
  $version = [string](Invoke-Adapter 'GetNodeVersion')
  if ($version -notmatch '^v?(\d+)\.\d+\.\d+$' -or [int]$Matches[1] -lt 20) {
    throw 'Node 20 or newer is required'
  }
}

function Assert-PackageLock {
  $packagePath = Join-Path $script:PackageRoot 'package.json'
  $lockPath = Join-Path $script:PackageRoot 'package-lock.json'
  if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf) -or -not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw 'Bridge package lock is missing'
  }
  try {
    $lock = [IO.File]::ReadAllText($lockPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -AsHashtable
    if (
      $lock.lockfileVersion -ne 3 -or
      [string]$lock.packages[''].dependencies['@larksuiteoapi/node-sdk'] -cne '1.71.1'
    ) {
      throw 'invalid'
    }
  } catch {
    throw 'Bridge package lock is invalid'
  }
}

function Stop-VerifiedLegacyBridgeProcesses {
  $processes = @(Invoke-Adapter 'GetProcesses')
  $liveIds = [Collections.Generic.HashSet[int]]::new()
  foreach ($process in $processes) {
    $processId = 0
    if ([int]::TryParse([string]$process.ProcessId, [ref]$processId) -and $processId -gt 0) {
      $liveIds.Add($processId) | Out-Null
    }
  }
  $entry = [IO.Path]::GetFullPath($script:BridgeEntry)
  $entryPattern = '(?i)(?:^|\s)"?' + [regex]::Escape($entry) + '"?(?:\s|$)'
  foreach ($process in $processes) {
    $processId = 0
    $parentProcessId = 0
    if (
      [string]$process.Name -cne 'node.exe' -or
      -not [int]::TryParse([string]$process.ProcessId, [ref]$processId) -or
      $processId -le 0 -or
      -not [int]::TryParse([string]$process.ParentProcessId, [ref]$parentProcessId) -or
      $liveIds.Contains($parentProcessId) -or
      [string]$process.CommandLine -notmatch $entryPattern
    ) {
      continue
    }
    Invoke-Adapter 'StopProcess' @($processId)
  }
}

function Assert-PrivateConfiguration {
  $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
  $setup = Join-Path $PSScriptRoot 'setup-feishu-decision-channel.ps1'
  $output = @(& $pwsh -NoProfile -ExecutionPolicy Bypass -File $setup -Action ShowSanitized -ConfigPath (Resolve-AbsolutePath $ConfigPath 'ConfigPath') 2>&1)
  if ($LASTEXITCODE -ne 0) { throw 'Private Feishu configuration is invalid or unsafe' }
  $line = @($output | ForEach-Object { [string]$_ } | Where-Object { $_.TrimStart().StartsWith('{') }) | Select-Object -Last 1
  try {
    $summary = $line | ConvertFrom-Json
    if ($summary.result -cne 'CONFIGURATION_SUMMARY' -or $summary.schemaVersion -ne 1) { throw 'invalid' }
  } catch {
    throw 'Private Feishu configuration is invalid or unsafe'
  }
}

function Get-HealthSummary {
  try {
    $config = [IO.File]::ReadAllText((Resolve-AbsolutePath $ConfigPath 'ConfigPath')) | ConvertFrom-Json -AsHashtable
    $stateRoot = Resolve-AbsolutePath ([string]$config.stateRoot) 'stateRoot'
    $healthPath = Join-Path $stateRoot 'health.json'
    if (-not (Test-Path -LiteralPath $healthPath -PathType Leaf) -or (Get-Item $healthPath).Length -gt 16KB) {
      return [ordered]@{
        bridgeStatus = 'UNAVAILABLE'
        cardStatus = 'UNAVAILABLE'
        healthAgeSeconds = $null
        textReplyStatus = 'TEXT_REPLY_UNVERIFIED'
        textReplyAgeSeconds = $null
      }
    }
    $healthJson = [IO.File]::ReadAllText($healthPath)
    $health = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
      $healthJson | ConvertFrom-Json -AsHashtable -DateKind String
    } else {
      $healthJson | ConvertFrom-Json -AsHashtable
    }
    $updated = [DateTimeOffset]::Parse([string]$health.updatedAt).ToUniversalTime()
    $age = [math]::Max(0, [math]::Floor(([DateTimeOffset]::UtcNow - $updated).TotalSeconds))
    $textReplyStatus = 'TEXT_REPLY_UNVERIFIED'
    $textReplyAgeSeconds = $null
    $textHealthPath = Join-Path $stateRoot 'text-reply-health.json'
    if (Test-Path -LiteralPath $textHealthPath -PathType Leaf) {
      try {
        if ((Get-Item $textHealthPath).Length -gt 4KB) { throw 'invalid' }
        $textJson = [IO.File]::ReadAllText($textHealthPath)
        $textHealth = if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) {
          $textJson | ConvertFrom-Json -AsHashtable -DateKind String
        } else {
          $textJson | ConvertFrom-Json -AsHashtable
        }
        if ($textHealth.Count -ne 3 -or $textHealth.schemaVersion -ne 1 -or
            [string]$textHealth.status -notin @('TEXT_REPLY_READY', 'TEXT_REPLY_UNAVAILABLE')) {
          throw 'invalid'
        }
        $textUpdated = [DateTimeOffset]::Parse([string]$textHealth.updatedAt).ToUniversalTime()
        $textReplyStatus = [string]$textHealth.status
        $textReplyAgeSeconds = [int64][math]::Max(0, [math]::Floor(([DateTimeOffset]::UtcNow - $textUpdated).TotalSeconds))
      } catch {
        $textReplyStatus = 'TEXT_REPLY_UNVERIFIED'
        $textReplyAgeSeconds = $null
      }
    }
    return [ordered]@{
      bridgeStatus = [string]$health.status
      cardStatus = [string]$health.status
      healthAgeSeconds = [int64]$age
      textReplyStatus = $textReplyStatus
      textReplyAgeSeconds = $textReplyAgeSeconds
    }
  } catch {
    return [ordered]@{
      bridgeStatus = 'UNAVAILABLE'
      cardStatus = 'UNAVAILABLE'
      healthAgeSeconds = $null
      textReplyStatus = 'TEXT_REPLY_UNVERIFIED'
      textReplyAgeSeconds = $null
    }
  }
}

if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or newer is required' }
Assert-TestAdapterSafe
$script:Adapter = if ($null -ne $SchedulerAdapter) { $SchedulerAdapter } else { New-RealSchedulerAdapter }
$plan = Get-TaskPlan

switch ($Action) {
  'Plan' {
    Write-SanitizedJson $plan
  }
  'Install' {
    Assert-NodeVersion
    Assert-PackageLock
    Assert-PrivateConfiguration
    $existing = Invoke-Adapter 'GetTask' @($script:TaskName)
    if ($null -ne $existing) {
      Invoke-Adapter 'StopTask' @($script:TaskName)
    }
    Stop-VerifiedLegacyBridgeProcesses
    Invoke-Adapter 'InstallPackages' @($script:PackageRoot)
    Stop-VerifiedLegacyBridgeProcesses
    Invoke-Adapter 'UpsertTask' @([pscustomobject]$plan)
    Write-SanitizedJson ([ordered]@{
      result = 'INSTALLED'
      taskName = $script:TaskName
      updated = $null -ne $existing
      multipleInstances = 'IgnoreNew'
    })
  }
  'Uninstall' {
    $existing = Invoke-Adapter 'GetTask' @($script:TaskName)
    if ($null -ne $existing) { Invoke-Adapter 'RemoveTask' @($script:TaskName) }
    Write-SanitizedJson ([ordered]@{
      result = 'UNINSTALLED'
      taskName = $script:TaskName
      removed = $null -ne $existing
      privateStatePreserved = $true
    })
  }
  'Status' {
    $state = [string](Invoke-Adapter 'GetTaskStatus' @($script:TaskName))
    $health = Get-HealthSummary
    Write-SanitizedJson ([ordered]@{
      result = 'STATUS'
      taskName = $script:TaskName
      installed = $state -cne 'NotInstalled'
      taskState = $state
      bridgeStatus = $health.bridgeStatus
      cardStatus = $health.cardStatus
      healthAgeSeconds = $health.healthAgeSeconds
      textReplyStatus = $health.textReplyStatus
      textReplyAgeSeconds = $health.textReplyAgeSeconds
    })
  }
}
