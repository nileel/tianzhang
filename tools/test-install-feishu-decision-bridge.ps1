#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$installTool = Join-Path $PSScriptRoot 'install-feishu-decision-bridge.ps1'
$setupTool = Join-Path $PSScriptRoot 'setup-feishu-decision-channel.ps1'
$startTool = Join-Path $PSScriptRoot 'start-feishu-decision-bridge.ps1'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-feishu-install-test-' + [guid]::NewGuid().ToString('N'))
$safeToRemove = $false

function Invoke-Script {
  param([string]$Path, [object[]]$Arguments)

  if ($Arguments.Count -lt 1 -or (($Arguments.Count - 1) % 2) -ne 0) {
    throw 'Invoke-Script received invalid test arguments'
  }
  $parameters = @{ Action = [string]$Arguments[0] }
  for ($index = 1; $index -lt $Arguments.Count; $index += 2) {
    $name = [string]$Arguments[$index]
    if (-not $name.StartsWith('-', [StringComparison]::Ordinal)) { throw 'Invoke-Script received an invalid parameter name' }
    $parameters[$name.Substring(1)] = $Arguments[$index + 1]
  }
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    try {
      $output = @(& $Path @parameters 2>&1)
      [pscustomobject]@{ Code = 0; Output = ($output -join "`n") }
    } catch {
      [pscustomobject]@{ Code = 1; Output = ([string]$_.Exception.Message + "`n" + [string]$_.ScriptStackTrace) }
    }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)

  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function New-FakeScheduler {
  param([string]$NodeVersion = 'v22.17.0')

  $state = [pscustomobject]@{
    Calls = [Collections.Generic.List[string]]::new()
    Tasks = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    NodeVersion = $NodeVersion
  }
  $adapter = @{
    GetNodeVersion = {
      $state.Calls.Add('node-version')
      $state.NodeVersion
    }
    InstallPackages = {
      param([string]$PackageDirectory)
      $state.Calls.Add("npm-ci:$PackageDirectory")
    }
    GetTask = {
      param([string]$TaskName)
      $state.Calls.Add("get:$TaskName")
      if ($state.Tasks.ContainsKey($TaskName)) { $state.Tasks[$TaskName] } else { $null }
    }
    UpsertTask = {
      param($Plan)
      $state.Calls.Add("upsert:$($Plan.taskName)")
      $state.Tasks[$Plan.taskName] = [pscustomobject]@{ TaskName = $Plan.taskName; State = 'Running'; Plan = $Plan }
    }
    RemoveTask = {
      param([string]$TaskName)
      $state.Calls.Add("remove:$TaskName")
      $state.Tasks.Remove($TaskName) | Out-Null
    }
    GetTaskStatus = {
      param([string]$TaskName)
      $state.Calls.Add("status:$TaskName")
      if ($state.Tasks.ContainsKey($TaskName)) { $state.Tasks[$TaskName].State } else { 'NotInstalled' }
    }
  }
  foreach ($key in @($adapter.Keys)) { $adapter[$key] = $adapter[$key].GetNewClosure() }
  [pscustomobject]@{ State = $state; Adapter = $adapter }
}

foreach ($path in @($installTool, $setupTool, $startTool)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "production script is missing: $path" }
}

New-Item -ItemType Directory -Path $sandbox | Out-Null
$resolvedSandbox = (Resolve-Path -LiteralPath $sandbox).Path
if (-not $resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing fixture outside temp root: $resolvedSandbox"
}
$safeToRemove = $true

try {
  $configPath = Join-Path $sandbox 'private.json'
  $stateRoot = Join-Path $sandbox 'state'
  $appId = 'install_fixture_app'
  $secret = 'install_fixture_secret_never_log'
  $recipient = 'install@example.invalid'
  Assert-Code (Invoke-Script -Path $setupTool -Arguments @(
    'Configure', '-ConfigPath', $configPath, '-StateRoot', $stateRoot,
    '-ConfigValues', @{ appId = $appId; appSecret = $secret; recipientType = 'email'; recipientValue = $recipient }
  )) 0 'install fixture Configure'

  $fake = New-FakeScheduler
  $planResult = Invoke-Script -Path $installTool -Arguments @('Plan', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $planResult 0 'Plan'
  $plan = $planResult.Output | ConvertFrom-Json
  if ($plan.taskName -ne 'TianZhang-Feishu-Decision-Bridge' -or $plan.trigger -ne 'AtLogOn') {
    throw 'Plan did not use the fixed task identity and login trigger'
  }
  if ($plan.multipleInstances -ne 'IgnoreNew' -or -not $plan.hidden) {
    throw 'Plan did not enforce hidden single-instance startup'
  }
  if ([IO.Path]::GetFullPath([string]$plan.startScript) -ne [IO.Path]::GetFullPath($startTool)) {
    throw 'Plan did not target the fixed bridge start script'
  }
  if ($plan.arguments -notmatch '(?i)-NoProfile' -or $plan.arguments -notmatch '(?i)-WindowStyle\s+Hidden' -or $plan.arguments -notmatch '(?i)-ExecutionPolicy\s+Bypass' -or $plan.arguments -notmatch '(?i)-File') {
    throw 'Plan startup arguments were not the canonical hidden PowerShell command'
  }
  if ($plan.arguments.Contains($secret, [StringComparison]::Ordinal) -or $plan.arguments.Contains($recipient, [StringComparison]::Ordinal) -or $plan.arguments -match '(?i)ConfigPath|AppSecret') {
    throw 'Plan startup command exposed private configuration'
  }
  if ($fake.State.Calls.Count -ne 0) { throw 'Plan unexpectedly touched the scheduler or package manager' }

  $firstInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $firstInstall 0 'first Install'
  $firstOutput = $firstInstall.Output | ConvertFrom-Json
  if ($firstOutput.result -ne 'INSTALLED' -or $firstOutput.updated) { throw 'first Install summary was invalid' }
  if ($fake.State.Tasks.Count -ne 1 -or -not $fake.State.Tasks.ContainsKey('TianZhang-Feishu-Decision-Bridge')) {
    throw 'first Install did not create exactly one fixed task'
  }
  $calls = @($fake.State.Calls)
  $nodeIndex = [Array]::IndexOf($calls, 'node-version')
  $npmIndex = [Array]::FindIndex($calls, [Predicate[string]]{ param($value) $value.StartsWith('npm-ci:', [StringComparison]::Ordinal) })
  $upsertIndex = [Array]::IndexOf($calls, 'upsert:TianZhang-Feishu-Decision-Bridge')
  if ($nodeIndex -lt 0 -or $npmIndex -le $nodeIndex -or $upsertIndex -le $npmIndex) {
    throw "Install did not validate Node/install packages before scheduling: $($calls -join ',')"
  }

  $secondInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $secondInstall 0 'second Install'
  $secondOutput = $secondInstall.Output | ConvertFrom-Json
  if ($secondOutput.result -ne 'INSTALLED' -or -not $secondOutput.updated) {
    throw 'second Install did not report an in-place update'
  }
  if ($fake.State.Tasks.Count -ne 1) { throw 'second Install created a duplicate task' }

  $status = Invoke-Script -Path $installTool -Arguments @('Status', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $status 0 'Status'
  $statusOutput = $status.Output | ConvertFrom-Json
  if (-not $statusOutput.installed -or $statusOutput.taskState -ne 'Running') {
    throw 'Status did not report the fake installed task'
  }

  $uninstall = Invoke-Script -Path $installTool -Arguments @('Uninstall', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $uninstall 0 'Uninstall'
  if ($fake.State.Tasks.Count -ne 0) { throw 'Uninstall did not remove the fixed task' }
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf) -or -not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
    throw 'Uninstall removed private configuration or bridge state'
  }

  $oldNode = New-FakeScheduler -NodeVersion 'v19.9.0'
  $oldNodeInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $oldNode.Adapter)
  if ($oldNodeInstall.Code -eq 0) { throw 'Install accepted Node 19'
  }
  if (@($oldNode.State.Calls | Where-Object { $_ -like 'npm-ci:*' -or $_ -like 'upsert:*' }).Count -ne 0) {
    throw 'Install touched packages or scheduler after rejecting Node 19'
  }

  $allOutput = @($planResult.Output, $firstInstall.Output, $secondInstall.Output, $status.Output, $uninstall.Output, $oldNodeInstall.Output) -join "`n"
  foreach ($literal in @($appId, $secret, $recipient)) {
    if ($allOutput.Contains($literal, [StringComparison]::Ordinal)) { throw 'install workflow output exposed a protected literal' }
  }

  $startText = Get-Content -LiteralPath $startTool -Raw
  if ($startText -notmatch '(?i)Mutex' -or $startText -notmatch 'bridge\.mjs') {
    throw 'start script does not provide a process-level single-instance guard and fixed bridge entrypoint'
  }

  Write-Output 'test-install-feishu-decision-bridge: OK'
} finally {
  if ($safeToRemove) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
