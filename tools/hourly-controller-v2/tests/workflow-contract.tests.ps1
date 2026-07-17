#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$projectRoot = Split-Path -Parent (Split-Path -Parent $v2Root)
$promptPath = Join-Path $projectRoot '开发管理\自动工作流控制器v2提示词.txt'
$rulesPath = Join-Path $projectRoot '开发管理\自动工作流v2规则.txt'
$checkerPath = Join-Path $projectRoot 'tools\check-hourly-controller-v2.ps1'

foreach ($path in @($promptPath, $rulesPath, $checkerPath)) {
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
    throw "v2 workflow contract file is missing: $path"
  }
}

$engine = Join-Path $PSHOME 'pwsh.exe'
$output = @(& $engine -NoProfile -ExecutionPolicy Bypass -File $checkerPath 2>&1)
if ($LASTEXITCODE -ne 0) {
  throw "v2 workflow checker failed: $($output -join "`n")"
}
Assert-Equal ([string]$output[-1]) 'check-hourly-controller-v2: OK' 'checker success marker'

$checkerText = [IO.File]::ReadAllText($checkerPath)
foreach ($forbiddenInvocation in @(
    'tests/run-tests.ps1',
    'test-automation-workspace-guard.ps1',
    'test-automation-finalize-commit.ps1',
    'npm test'
  )) {
  Assert-False ([bool]$checkerText.Contains($forbiddenInvocation, [StringComparison]::OrdinalIgnoreCase)) "static checker invocation $forbiddenInvocation"
}

Write-Output 'workflow-contract.tests: OK'
