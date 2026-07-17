#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$installTool = Join-Path $PSScriptRoot 'install-feishu-decision-bridge.ps1'
$setupTool = Join-Path $PSScriptRoot 'setup-feishu-decision-channel.ps1'
$startTool = Join-Path $PSScriptRoot 'start-feishu-decision-bridge.ps1'
$hiddenLauncher = Join-Path $PSScriptRoot 'start-feishu-decision-bridge-hidden.vbs'
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
    Processes = [Collections.Generic.List[object]]::new()
    StoppedProcessIds = [Collections.Generic.List[int]]::new()
    ProcessSnapshots = 0
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
    StopTask = {
      param([string]$TaskName)
      $state.Calls.Add("stop-task:$TaskName")
      if ($state.Tasks.ContainsKey($TaskName)) { $state.Tasks[$TaskName].State = 'Ready' }
    }
    GetProcesses = {
      $state.Calls.Add('get-processes')
      $snapshot = @($state.Processes)
      if ($state.ProcessSnapshots -eq 0) {
        foreach ($process in @($state.Processes | Where-Object { $_.ProcessId -eq 901 })) {
          $state.Processes.Remove($process) | Out-Null
        }
      }
      $state.ProcessSnapshots++
      $snapshot
    }
    StopProcess = {
      param([int]$ProcessId)
      $state.Calls.Add("stop-process:$ProcessId")
      $state.StoppedProcessIds.Add($ProcessId)
      foreach ($process in @($state.Processes | Where-Object { $_.ProcessId -eq $ProcessId })) {
        $state.Processes.Remove($process) | Out-Null
      }
    }
    UpsertTask = {
      param($Plan)
      $state.Calls.Add("upsert:$($Plan.taskName)")
      $state.Tasks[$Plan.taskName] = [pscustomobject]@{
        TaskName = $Plan.taskName
        State = 'Ready'
        Enabled = $true
        Plan = $Plan
      }
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
    IsTaskEnabled = {
      param([string]$TaskName)
      $state.Calls.Add("enabled:$TaskName")
      if (-not $state.Tasks.ContainsKey($TaskName)) { throw 'task is not installed' }
      [bool]$state.Tasks[$TaskName].Enabled
    }
    EnableTask = {
      param([string]$TaskName)
      $state.Calls.Add("enable:$TaskName")
      if (-not $state.Tasks.ContainsKey($TaskName)) { throw 'task is not installed' }
      $state.Tasks[$TaskName].Enabled = $true
    }
    DisableTask = {
      param([string]$TaskName)
      $state.Calls.Add("disable:$TaskName")
      if (-not $state.Tasks.ContainsKey($TaskName)) { throw 'task is not installed' }
      $state.Tasks[$TaskName].Enabled = $false
      $state.Tasks[$TaskName].State = 'Ready'
    }
    StartTask = {
      param([string]$TaskName)
      $state.Calls.Add("start-task:$TaskName")
      if (-not $state.Tasks.ContainsKey($TaskName) -or -not $state.Tasks[$TaskName].Enabled) {
        throw 'task is not installed and enabled'
      }
      $state.Tasks[$TaskName].State = 'Running'
    }
  }
  foreach ($key in @($adapter.Keys)) { $adapter[$key] = $adapter[$key].GetNewClosure() }
  [pscustomobject]@{ State = $state; Adapter = $adapter }
}

foreach ($path in @($installTool, $setupTool, $startTool, $hiddenLauncher)) {
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
  $bridgeEntry = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'feishu-decision-bridge\src\bridge.mjs'))
  $taskName = 'TianZhang-Feishu-Decision-Bridge'
  $fake.State.Tasks[$taskName] = [pscustomobject]@{
    TaskName = $taskName
    State = 'Running'
    Enabled = $true
    Plan = [pscustomobject]@{
      execute = [IO.Path]::GetFullPath((Get-Command pwsh -ErrorAction Stop).Source)
      arguments = '-NoProfile -WindowStyle Hidden -File start-feishu-decision-bridge.ps1'
      launchMode = 'LEGACY_DIRECT_PWSH'
    }
  }
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 100
    ParentProcessId = 999
    Name = 'node.exe'
    CommandLine = "node.exe `"$bridgeEntry`""
  })
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 101
    ParentProcessId = 901
    Name = 'node.exe'
    CommandLine = "node.exe `"$bridgeEntry`""
  })
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 901
    ParentProcessId = 1
    Name = 'pwsh.exe'
    CommandLine = 'pwsh.exe -File start-feishu-decision-bridge.ps1'
  })
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 102
    ParentProcessId = 999
    Name = 'node.exe'
    CommandLine = "node.exe `"${bridgeEntry}-near`""
  })
  $planResult = Invoke-Script -Path $installTool -Arguments @('Plan', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $planResult 0 'Plan'
  $plan = $planResult.Output | ConvertFrom-Json
  if ($plan.taskName -ne 'TianZhang-Feishu-Decision-Bridge' -or $plan.trigger -ne 'AtLogOn') {
    throw 'Plan did not use the fixed task identity and login trigger'
  }
  if ($plan.multipleInstances -ne 'IgnoreNew' -or -not $plan.hidden -or $plan.launchMode -cne 'WINDOWLESS_WSCRIPT') {
    throw 'Plan did not enforce hidden single-instance startup'
  }
  if (-not [IO.Path]::IsPathFullyQualified([string]$plan.execute) -or
      [IO.Path]::GetFileName([string]$plan.execute) -ine 'wscript.exe' -or
      [IO.Path]::GetFileName([string]$plan.execute) -ieq 'pwsh.exe') {
    throw 'Plan did not use an absolute system wscript.exe action'
  }
  if ([IO.Path]::GetFullPath([string]$plan.startScript) -ne [IO.Path]::GetFullPath($startTool)) {
    throw 'Plan did not target the fixed bridge start script'
  }
  $pwshPath = [IO.Path]::GetFullPath((Get-Command pwsh -ErrorAction Stop).Source)
  $expectedArguments = "//B //NoLogo `"$([IO.Path]::GetFullPath($hiddenLauncher))`" `"$pwshPath`" `"$([IO.Path]::GetFullPath($startTool))`""
  if ([string]$plan.arguments -cne $expectedArguments) {
    throw "Plan startup arguments were not the fixed windowless command: $($plan.arguments)"
  }
  if ($plan.arguments.Contains($configPath, [StringComparison]::OrdinalIgnoreCase) -or
      $plan.arguments.Contains($secret, [StringComparison]::Ordinal) -or
      $plan.arguments.Contains($recipient, [StringComparison]::Ordinal) -or
      $plan.arguments -match '(?i)ConfigPath|AppSecret') {
    throw 'Plan startup command exposed private configuration'
  }
  if ($fake.State.Calls.Count -ne 0) { throw 'Plan unexpectedly touched the scheduler or package manager' }

  $firstInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $firstInstall 0 'first Install'
  $firstOutput = $firstInstall.Output | ConvertFrom-Json
  if ($firstOutput.result -ne 'INSTALLED' -or -not $firstOutput.updated) { throw 'legacy task upgrade summary was invalid' }
  if ($fake.State.Tasks.Count -ne 1 -or -not $fake.State.Tasks.ContainsKey('TianZhang-Feishu-Decision-Bridge')) {
    throw 'Install did not preserve exactly one fixed task during in-place upgrade'
  }
  $installedTask = $fake.State.Tasks[$taskName]
  if (-not $installedTask.Enabled -or $installedTask.State -cne 'Running' -or
      $installedTask.Plan.launchMode -cne 'WINDOWLESS_WSCRIPT' -or
      [IO.Path]::GetFileName([string]$installedTask.Plan.execute) -ine 'wscript.exe') {
    throw 'Install did not replace the legacy direct-pwsh task with one enabled running windowless task'
  }
  if ($fake.State.StoppedProcessIds.Count -ne 2 -or
      $fake.State.StoppedProcessIds[0] -ne 100 -or
      $fake.State.StoppedProcessIds[1] -ne 101) {
    throw "Install did not stop verified bridge processes across the parent-exit race: $($fake.State.StoppedProcessIds -join ',')"
  }
  $calls = @($fake.State.Calls)
  $nodeIndex = [Array]::IndexOf($calls, 'node-version')
  $npmIndex = [Array]::FindIndex($calls, [Predicate[string]]{ param($value) $value.StartsWith('npm-ci:', [StringComparison]::Ordinal) })
  $upsertIndex = [Array]::IndexOf($calls, 'upsert:TianZhang-Feishu-Decision-Bridge')
  $enableIndex = [Array]::LastIndexOf($calls, 'enable:TianZhang-Feishu-Decision-Bridge')
  $startIndex = [Array]::LastIndexOf($calls, 'start-task:TianZhang-Feishu-Decision-Bridge')
  $lastProcessIndex = [Array]::LastIndexOf($calls, 'get-processes')
  if ($nodeIndex -lt 0 -or $npmIndex -le $nodeIndex -or
      $lastProcessIndex -le $npmIndex -or $upsertIndex -le $lastProcessIndex -or
      $enableIndex -le $upsertIndex -or $startIndex -le $enableIndex) {
    throw "Install did not validate Node/install packages before scheduling: $($calls -join ',')"
  }

  $secondInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $secondInstall 0 'second Install'
  $secondOutput = $secondInstall.Output | ConvertFrom-Json
  if ($secondOutput.result -ne 'INSTALLED' -or -not $secondOutput.updated) {
    throw 'second Install did not report an in-place update'
  }
  if ($fake.State.Tasks.Count -ne 1) { throw 'second Install created a duplicate task' }
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 200
    ParentProcessId = 902
    Name = 'node.exe'
    CommandLine = "node.exe `"$bridgeEntry`""
  })
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 201
    ParentProcessId = 1
    Name = 'node.exe'
    CommandLine = 'node.exe unrelated-service.mjs'
  })
  $fake.State.Processes.Add([pscustomobject]@{
    ProcessId = 202
    ParentProcessId = 1
    Name = 'pwsh.exe'
    CommandLine = 'pwsh.exe -NoProfile -File unrelated-job.ps1'
  })

  $healthTimestamp = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
  [IO.File]::WriteAllText(
    (Join-Path $stateRoot 'health.json'),
    ([ordered]@{
      schemaVersion = 1
      status = 'CONNECTED'
      pid = $PID
      updatedAt = $healthTimestamp
      appIdHash = ('a' * 64)
    } | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false)
  )
  [IO.File]::WriteAllText(
    (Join-Path $stateRoot 'text-reply-health.json'),
    ([ordered]@{
      schemaVersion = 1
      status = 'TEXT_REPLY_UNAVAILABLE'
      updatedAt = $healthTimestamp
    } | ConvertTo-Json -Compress),
    [Text.UTF8Encoding]::new($false)
  )
  $stop = Invoke-Script -Path $installTool -Arguments @('Stop', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $stop 0 'Stop'
  $stopAgain = Invoke-Script -Path $installTool -Arguments @('Stop', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $stopAgain 0 'second Stop'
  if ($fake.State.Tasks[$taskName].State -cne 'Ready' -or -not $fake.State.Tasks[$taskName].Enabled) {
    throw 'Stop did not preserve enabled=true while idempotently stopping the task'
  }
  if (@($fake.State.StoppedProcessIds) -notcontains 200 -or
      @($fake.State.StoppedProcessIds) -contains 201 -or
      @($fake.State.StoppedProcessIds) -contains 202) {
    throw "Stop did not isolate cleanup to the fixed bridge process tree: $($fake.State.StoppedProcessIds -join ',')"
  }
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf) -or -not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
    throw 'Stop removed private configuration or bridge state'
  }
  $status = Invoke-Script -Path $installTool -Arguments @('Status', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $status 0 'Status'
  $statusOutput = $status.Output | ConvertFrom-Json
  if (-not $statusOutput.installed -or $statusOutput.taskState -ne 'Ready' -or
      -not $statusOutput.enabled -or $statusOutput.launchMode -cne 'WINDOWLESS_WSCRIPT') {
    throw 'Status did not report the fake installed task'
  }
  if ($statusOutput.bridgeStatus -ne 'CONNECTED' -or $statusOutput.cardStatus -ne 'CONNECTED' -or
      $statusOutput.textReplyStatus -ne 'TEXT_REPLY_UNAVAILABLE' -or
      $statusOutput.textReplyAgeSeconds -lt 0 -or $statusOutput.textReplyAgeSeconds -gt 10 -or
      $statusOutput.healthAgeSeconds -lt 0 -or $statusOutput.healthAgeSeconds -gt 10) {
    throw "Status did not report a fresh UTC heartbeat: $($status.Output)"
  }

  $start = Invoke-Script -Path $installTool -Arguments @('Start', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $start 0 'Start'
  $startAgain = Invoke-Script -Path $installTool -Arguments @('Start', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $startAgain 0 'second Start'
  if ($fake.State.Tasks[$taskName].State -cne 'Running') { throw 'Start was not idempotent' }

  $disable = Invoke-Script -Path $installTool -Arguments @('Disable', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $disable 0 'Disable'
  $disableAgain = Invoke-Script -Path $installTool -Arguments @('Disable', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $disableAgain 0 'second Disable'
  if ($fake.State.Tasks[$taskName].Enabled -or $fake.State.Tasks[$taskName].State -cne 'Ready') {
    throw 'Disable did not stop and disable the task idempotently'
  }
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf) -or -not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
    throw 'Disable removed private configuration or bridge state'
  }
  $disabledStart = Invoke-Script -Path $installTool -Arguments @('Start', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  if ($disabledStart.Code -eq 0) { throw 'Start accepted a disabled task' }
  $disabledStatus = Invoke-Script -Path $installTool -Arguments @('Status', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $disabledStatus 0 'disabled Status'
  $disabledStatusOutput = $disabledStatus.Output | ConvertFrom-Json
  if (-not $disabledStatusOutput.installed -or $disabledStatusOutput.enabled -or
      $disabledStatusOutput.launchMode -cne 'WINDOWLESS_WSCRIPT') {
    throw 'Status did not report enabled=false for the disabled task'
  }

  $enable = Invoke-Script -Path $installTool -Arguments @('Enable', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $enable 0 'Enable'
  $enableAgain = Invoke-Script -Path $installTool -Arguments @('Enable', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $enableAgain 0 'second Enable'
  if (-not $fake.State.Tasks[$taskName].Enabled -or $fake.State.Tasks[$taskName].State -cne 'Running') {
    throw 'Enable did not enable and idempotently start the task'
  }

  $uninstall = Invoke-Script -Path $installTool -Arguments @('Uninstall', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $uninstall 0 'Uninstall'
  if ($fake.State.Tasks.Count -ne 0) { throw 'Uninstall did not remove the fixed task' }
  if (-not (Test-Path -LiteralPath $configPath -PathType Leaf) -or -not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
    throw 'Uninstall removed private configuration or bridge state'
  }
  $uninstalledStatus = Invoke-Script -Path $installTool -Arguments @('Status', '-ConfigPath', $configPath, '-SchedulerAdapter', $fake.Adapter)
  Assert-Code $uninstalledStatus 0 'uninstalled Status'
  $uninstalledStatusOutput = $uninstalledStatus.Output | ConvertFrom-Json
  if ($uninstalledStatusOutput.installed -or $null -ne $uninstalledStatusOutput.enabled -or
      $uninstalledStatusOutput.launchMode -cne 'WINDOWLESS_WSCRIPT') {
    throw 'Status did not report enabled=null for an uninstalled task'
  }

  $oldNode = New-FakeScheduler -NodeVersion 'v19.9.0'
  $oldNodeInstall = Invoke-Script -Path $installTool -Arguments @('Install', '-ConfigPath', $configPath, '-SchedulerAdapter', $oldNode.Adapter)
  if ($oldNodeInstall.Code -eq 0) { throw 'Install accepted Node 19'
  }
  if (@($oldNode.State.Calls | Where-Object { $_ -like 'npm-ci:*' -or $_ -like 'upsert:*' }).Count -ne 0) {
    throw 'Install touched packages or scheduler after rejecting Node 19'
  }

  $allOutput = @(
    $planResult.Output, $firstInstall.Output, $secondInstall.Output,
    $stop.Output, $stopAgain.Output, $status.Output, $start.Output, $startAgain.Output,
    $disable.Output, $disableAgain.Output, $disabledStart.Output, $disabledStatus.Output,
    $enable.Output, $enableAgain.Output, $uninstall.Output, $uninstalledStatus.Output,
    $oldNodeInstall.Output
  ) -join "`n"
  foreach ($literal in @($appId, $secret, $recipient)) {
    if ($allOutput.Contains($literal, [StringComparison]::Ordinal)) { throw 'install workflow output exposed a protected literal' }
  }

  $startText = Get-Content -LiteralPath $startTool -Raw
  if ($startText -notmatch '(?i)Mutex' -or $startText -notmatch 'bridge\.mjs') {
    throw 'start script does not provide a process-level single-instance guard and fixed bridge entrypoint'
  }
  if ($startText -match '(?m)\bSet-Acl\b' -or $startText -notmatch 'private-path-acl\.ps1') {
    throw 'start script does not use the DACL-only private path helper'
  }

  $launcherText = Get-Content -LiteralPath $hiddenLauncher -Raw
  if ($launcherText -notmatch [regex]::Escape('Run(command, 0, True)') -or
      $launcherText -notmatch [regex]::Escape('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File') -or
      $launcherText -notmatch [regex]::Escape('start-feishu-decision-bridge.ps1')) {
    throw 'windowless launcher does not use the fixed hidden PowerShell command'
  }
  foreach ($forbidden in @($appId, $secret, $recipient, $configPath, 'ConfigPath', 'recipientValue')) {
    if ($launcherText.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) {
      throw 'windowless launcher contains private configuration or a dynamic target'
    }
  }

  Write-Output 'test-install-feishu-decision-bridge: OK'
} finally {
  if ($safeToRemove) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
