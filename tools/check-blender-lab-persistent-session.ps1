#requires -Version 7.0

[CmdletBinding()]
param(
  [ValidateRange(0, 60)]
  [int]$WaitSeconds = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$BlenderPath = 'D:\Tools\Blender\5.2.0\blender.exe'
$ServiceDirectory = 'D:\Temp\TianZhang-Blender\blender-lab-persistent-session'
$BlendPath = Join-Path $ServiceDirectory 'blender-lab-persistent.blend'
$StatePath = Join-Path $ServiceDirectory 'service-state.json'

function Get-ServiceState {
  if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) { throw 'The dedicated Blender service state was not found.' }
  try {
    $state = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json -DateKind String -ErrorAction Stop
  } catch {
    throw 'The dedicated Blender service state is invalid.'
  }
  foreach ($property in @('schemaVersion', 'processId', 'processStartTimeUtc', 'executablePath', 'blendPath')) {
    if ($state.PSObject.Properties.Name -notcontains $property) { throw "The dedicated Blender service state is missing '$property'." }
  }
  if ([int]$state.schemaVersion -ne 1 -or [int]$state.processId -le 0 -or
      [string]$state.executablePath -cne $BlenderPath -or [string]$state.blendPath -cne $BlendPath) {
    throw 'The dedicated Blender service state does not describe this service.'
  }
  $state
}

function Get-OwnedProcess {
  param([object]$State)

  try {
    $process = Get-Process -Id ([int]$State.processId) -ErrorAction Stop
    $processPath = [IO.Path]::GetFullPath([string]$process.Path)
    $recordedStartTime = ([datetimeoffset]::Parse([string]$State.processStartTimeUtc)).UtcDateTime
    $actualStartTime = $process.StartTime.ToUniversalTime()
  } catch {
    throw 'The dedicated Blender process is not running.'
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

function Get-VerifiedListener {
  param([int]$ProcessId)

  $listeners = @(Get-NetTCPConnection -State Listen -LocalPort 9876 -ErrorAction SilentlyContinue)
  if ($listeners.Count -ne 1 -or [string]$listeners[0].LocalAddress -notin @('127.0.0.1', '::1') -or
      [int]$listeners[0].OwningProcess -ne $ProcessId) {
    throw 'localhost:9876 is not uniquely owned by the dedicated Blender process.'
  }
  $listeners[0]
}

function Test-BridgeConnection {
  param([string]$LocalAddress)

  $address = [Net.IPAddress]::Parse($LocalAddress)
  $client = [Net.Sockets.TcpClient]::new($address.AddressFamily)
  try {
    if (-not $client.ConnectAsync($address, 9876).Wait(5000) -or -not $client.Connected) {
      throw 'The dedicated Blender MCP bridge did not accept a local connection.'
    }
  } finally {
    $client.Dispose()
  }
}

$deadline = [datetime]::UtcNow.AddSeconds($WaitSeconds)
try {
  do {
    try {
      $state = Get-ServiceState
      $process = Get-OwnedProcess -State $state
      if (-not (Test-Path -LiteralPath $BlendPath -PathType Leaf)) {
        throw 'The dedicated Blender blend file was not created.'
      }
      $listener = Get-VerifiedListener -ProcessId $process.Id
      Test-BridgeConnection -LocalAddress ([string]$listener.LocalAddress)
      [Console]::Out.WriteLine(([ordered]@{
        status = 'healthy'
        processId = $process.Id
        processStartTimeUtc = $process.StartTime.ToUniversalTime().ToString('o')
        executablePath = $BlenderPath
        blendPath = $BlendPath
        listenerAddress = [string]$listener.LocalAddress
        listenerPort = 9876
      } | ConvertTo-Json -Compress))
      exit 0
    } catch {
      $lastError = $_
      if ([datetime]::UtcNow -ge $deadline) { throw $lastError }
      Start-Sleep -Milliseconds 500
    }
  } while ($true)
} catch {
  [Console]::Error.WriteLine($_.Exception.Message)
  exit 1
}
