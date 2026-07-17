#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$engine = Join-Path $PSHOME 'pwsh.exe'
$testFiles = @([IO.Directory]::GetFiles($PSScriptRoot, '*.tests.ps1') | Sort-Object { [IO.Path]::GetFileName($_) })
foreach ($testFile in $testFiles) {
  & $engine -NoProfile -ExecutionPolicy Bypass -File $testFile
  if ($LASTEXITCODE -ne 0) {
    throw "v2 test failed: $([IO.Path]::GetFileName($testFile))"
  }
}

Write-Output 'hourly-controller-v2-tests: OK'
