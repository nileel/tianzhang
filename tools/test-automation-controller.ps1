$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-controller.ps1'
$stateTool = Join-Path $root 'tools\automation-controller-state.ps1'
$engine = (Get-Process -Id $PID).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-controller-v3-test-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $sandbox 'repo'
$statePath = Join-Path $sandbox 'state.json'
$runRoot = Join-Path $sandbox 'runs'
$safeToRemove = $false

function Invoke-Controller {
  param([string[]]$Arguments)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Invoke-State {
  param([string[]]$Arguments)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $stateTool @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "state helper failed: $($output -join "`n")" }
    ($output -join "`n") | ConvertFrom-Json
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $output = & git -C $repo @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  @($output)
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)

  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Read-State {
  Invoke-State @('Show', '-StatePath', $statePath)
}

function Write-Utf8 {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
  [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Path $repo, $runRoot -Force | Out-Null
$resolvedRepo = (Resolve-Path -LiteralPath $repo).Path
$tempPrefix = (Resolve-Path -LiteralPath $tempRoot).Path.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedRepo.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing fixture outside temp root: $resolvedRepo"
}
$safeToRemove = $true

try {
  Invoke-Git init | Out-Null
  Invoke-Git config user.name 'Controller V3 Test' | Out-Null
  Invoke-Git config user.email 'controller-v3@example.invalid' | Out-Null
  Write-Utf8 (Join-Path $repo 'base.txt') "base`n"
  Invoke-Git add -- base.txt | Out-Null
  Invoke-Git commit -m 'test: base' | Out-Null

  $runId = '11111111-1111-4111-8111-111111111111'
  $start = Invoke-Controller @(
    'Start', '-RepositoryRoot', $repo, '-StatePath', $statePath,
    '-RunRoot', $runRoot, '-RunId', $runId,
    '-ActualModel', 'gpt-test', '-Now', '2026-07-15T00:00:00Z'
  )
  Assert-Code $start 0 'fresh start'
  $startJson = $start.Output | ConvertFrom-Json
  if (-not $startJson.ok -or $startJson.action -ne 'select_candidate' -or
      $startJson.branchKind -ne 'selection' -or $startJson.nextCommand -ne 'RegisterCandidate' -or
      -not (Test-Path -LiteralPath $startJson.baselinePath)) {
    throw "fresh start protocol mismatch: $($start.Output)"
  }
  $state = Read-State
  if ($state.state -ne 'RUNNING' -or $state.runId -ne $runId -or $state.checkpoint -ne 'identity_checked') {
    throw 'fresh start did not persist the identity checkpoint'
  }

  'test-automation-controller: OK'
} finally {
  if ($safeToRemove) {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
  }
}
