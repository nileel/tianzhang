#requires -Version 7.0

[CmdletBinding()]
param(
  [ValidateSet('Start', 'InstallLoginTask', 'Rollback')]
  [string]$Action = 'Start'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TaskName = 'TianZhang-BlenderLab-Persistent'
$BlenderPath = 'D:\Tools\Blender\5.2.0\blender.exe'
$ServiceDirectory = 'D:\Temp\TianZhang-Blender\blender-lab-persistent-session'
$BlendPath = Join-Path $ServiceDirectory 'blender-lab-persistent.blend'
$StatePath = Join-Path $ServiceDirectory 'service-state.json'
$ExpectedScriptLeaf = 'start-blender-lab-persistent-session.ps1'

function Write-Result {
  param(
    [Parameter(Mandatory = $true)][string]$Status,
    [int]$ProcessId = 0
  )

  [Console]::Out.WriteLine(([ordered]@{
    status = $Status
    processId = $ProcessId
    executablePath = $BlenderPath
    blendPath = $BlendPath
    statePath = $StatePath
  } | ConvertTo-Json -Compress))
}

function Get-LoginScriptPath {
  $repositoryRoot = Split-Path -Parent $PSScriptRoot
  $commonGitDirectory = (& git -C $repositoryRoot rev-parse --path-format=absolute --git-common-dir).Trim()
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commonGitDirectory)) {
    throw 'Unable to resolve the repository common Git directory.'
  }
  $stableRoot = Split-Path -Parent $commonGitDirectory
  $scriptPath = Join-Path $stableRoot (Join-Path 'tools' $ExpectedScriptLeaf)
  [IO.Path]::GetFullPath($scriptPath)
}

function Get-PowerShellPath {
  [string](Get-Command pwsh -CommandType Application -ErrorAction Stop | Select-Object -First 1 -ExpandProperty Source)
}

function Get-DedicatedProcesses {
  @(Get-CimInstance Win32_Process -Filter "Name = 'blender.exe'" | Where-Object {
    ([string]$_.CommandLine).IndexOf($BlendPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
  })
}

function Get-PortListeners {
  @(Get-NetTCPConnection -State Listen -LocalPort 9876 -ErrorAction SilentlyContinue)
}

function Read-ServiceState {
  if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) { return $null }
  try {
    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json -DateKind String -ErrorAction Stop
  } catch {
    throw 'The dedicated Blender service state is invalid.'
  }
  foreach ($property in @('schemaVersion', 'processId', 'processStartTimeUtc', 'executablePath', 'blendPath')) {
    if ($state.PSObject.Properties.Name -notcontains $property) {
      throw "The dedicated Blender service state is missing '$property'."
    }
  }
  $state
}

function Get-OwnedProcess {
  $state = Read-ServiceState
  if ($null -eq $state) { return $null }
  if ([int]$state.schemaVersion -ne 1 -or
      [int]$state.processId -le 0 -or
      [string]$state.executablePath -cne $BlenderPath -or
      [string]$state.blendPath -cne $BlendPath) {
    throw 'The dedicated Blender service state does not describe this service.'
  }

  try {
    $process = Get-Process -Id ([int]$state.processId) -ErrorAction Stop
    $processPath = [IO.Path]::GetFullPath([string]$process.Path)
    $recordedStartTime = ([datetimeoffset]::Parse([string]$state.processStartTimeUtc)).UtcDateTime
    $actualStartTime = $process.StartTime.ToUniversalTime()
  } catch {
    return $null
  }
  if ($processPath -cne $BlenderPath -or [math]::Abs(($actualStartTime - $recordedStartTime).TotalSeconds) -gt 1) {
    throw 'The Blender process does not match the dedicated service state.'
  }

  $processRecord = Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)" -ErrorAction SilentlyContinue
  if ($null -eq $processRecord -or ([string]$processRecord.CommandLine).IndexOf($BlendPath, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'The Blender process command line does not match the dedicated service state.'
  }
  $process
}

function Assert-NoForeignPortListener {
  param([System.Diagnostics.Process]$OwnedProcess)

  $listeners = @(Get-PortListeners)
  if ($listeners.Count -eq 0) { return }
  if ($null -eq $OwnedProcess -or $listeners.Count -ne 1 -or
      [string]$listeners[0].LocalAddress -notin @('127.0.0.1', '::1') -or
      [int]$listeners[0].OwningProcess -ne $OwnedProcess.Id) {
    throw 'localhost:9876 is occupied by an unknown or additional listener.'
  }
}

function Install-LoginTask {
  $loginScriptPath = Get-LoginScriptPath
  $currentUser = "$env:USERDOMAIN\$env:USERNAME"
  $powerShellPath = Get-PowerShellPath
  $expectedArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$loginScriptPath`""
  $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
  if ($null -ne $existing) {
    $actions = @($existing.Actions)
    $isExpected = $actions.Count -eq 1 -and
      [string]$actions[0].Execute -ieq $powerShellPath -and
      [string]$actions[0].Arguments -ceq $expectedArguments
    if (-not $isExpected) { throw "The existing '$TaskName' task is not this service's login action." }
    Write-Result -Status 'login_task_already_installed'
    return
  }

  $action = New-ScheduledTaskAction -Execute $powerShellPath -Argument $expectedArguments
  $trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
  $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -RestartCount 0
  Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Starts the dedicated TianZhang Blender Lab session at this users logon.' | Out-Null
  Write-Result -Status 'login_task_installed'
}

function Invoke-Rollback {
  $loginScriptPath = Get-LoginScriptPath
  $powerShellPath = Get-PowerShellPath
  $expectedArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$loginScriptPath`""
  $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
  if ($null -ne $existing) {
    $actions = @($existing.Actions)
    $isExpected = $actions.Count -eq 1 -and
      [string]$actions[0].Execute -ieq $powerShellPath -and
      [string]$actions[0].Arguments -ceq $expectedArguments
    if (-not $isExpected) { throw "The existing '$TaskName' task is not this service's login action." }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
  }

  $ownedProcess = Get-OwnedProcess
  if ($null -ne $ownedProcess) {
    Assert-NoForeignPortListener -OwnedProcess $ownedProcess
    Stop-Process -Id $ownedProcess.Id -ErrorAction Stop
  } elseif (@(Get-DedicatedProcesses).Count -gt 0) {
    throw 'A dedicated Blender process exists without a valid service-state identity.'
  }
  Write-Result -Status 'rolled_back'
}

function Start-DedicatedSession {
  if (-not (Test-Path -LiteralPath $BlenderPath -PathType Leaf)) { throw 'The required Blender executable was not found.' }
  $hasServiceState = Test-Path -LiteralPath $StatePath -PathType Leaf
  $ownedProcess = Get-OwnedProcess
  $dedicatedProcesses = @(Get-DedicatedProcesses)
  if ($null -ne $ownedProcess) {
    if ($dedicatedProcesses.Count -ne 1 -or [int]$dedicatedProcesses[0].ProcessId -ne $ownedProcess.Id) {
      throw 'Dedicated Blender process ownership is not unique.'
    }
    Assert-NoForeignPortListener -OwnedProcess $ownedProcess
    Write-Result -Status 'already_running' -ProcessId $ownedProcess.Id
    return
  }
  if ($dedicatedProcesses.Count -gt 0) { throw 'A dedicated Blender process exists without a valid service-state identity.' }
  if ($hasServiceState) {
    throw 'The recorded dedicated Blender process is not running; refusing to relaunch it automatically.'
  }
  Assert-NoForeignPortListener -OwnedProcess $null

  $null = New-Item -ItemType Directory -Path $ServiceDirectory -Force
  $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $BlenderPath
  $startInfo.UseShellExecute = $true
  $startInfo.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Minimized
  if (Test-Path -LiteralPath $BlendPath -PathType Leaf) {
    $startInfo.ArgumentList.Add($BlendPath)
  } else {
    $saveDedicatedBlend = "import bpy; bpy.ops.wm.save_as_mainfile(filepath=r'$BlendPath')"
    $startInfo.ArgumentList.Add('--python-expr')
    $startInfo.ArgumentList.Add($saveDedicatedBlend)
  }
  $process = [System.Diagnostics.Process]::Start($startInfo)
  if ($null -eq $process) { throw 'Blender did not return a process handle.' }

  try {
    $state = [ordered]@{
      schemaVersion = 1
      processId = $process.Id
      processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
      executablePath = $BlenderPath
      blendPath = $BlendPath
    }
    $state | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding utf8NoBOM
  } catch {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    throw
  }
  Write-Result -Status 'started' -ProcessId $process.Id
}

try {
  switch ($Action) {
    'Start' { Start-DedicatedSession }
    'InstallLoginTask' { Install-LoginTask }
    'Rollback' { Invoke-Rollback }
  }
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
