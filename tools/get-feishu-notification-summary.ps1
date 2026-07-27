#requires -Version 7.0

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Since,
  [Parameter(Mandatory = $true)]
  [string]$Until
)

$ErrorActionPreference = 'Stop'
$entry = Join-Path $PSScriptRoot 'feishu-decision-bridge\src\notification-summary.mjs'

try {
  if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) {
    throw 'Notification summary is unavailable'
  }
  $output = @(& node $entry --since $Since --until $Until 2>$null)
  $exitCode = $LASTEXITCODE
  $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
  if ($lines.Count -ne 1) {
    throw 'Notification summary returned an invalid response'
  }
  [Console]::Out.WriteLine([string]$lines[0])
  exit $exitCode
} catch {
  [Console]::Out.WriteLine('{"result":"SOURCE_UNAVAILABLE"}')
  exit 1
}
