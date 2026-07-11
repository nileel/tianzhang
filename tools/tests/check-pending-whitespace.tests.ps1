$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$checker = Join-Path $repositoryRoot 'tools\check-pending-whitespace.ps1'
$fixture = Join-Path $env:TEMP ('tzg-whitespace-fixture-' + [guid]::NewGuid().ToString('N') + '.txt')

try {
    [IO.File]::WriteAllText($fixture, "alpha `r`nbeta`t`r`n", [Text.UTF8Encoding]::new($false))

    & $checker -ExpectedPaths $fixture -Fix
    if ($LASTEXITCODE -ne 0) {
        throw "Expected -Fix to succeed, got exit code $LASTEXITCODE."
    }

    $lines = [IO.File]::ReadAllLines($fixture)
    if ($lines | Where-Object { $_ -match '[ \t]+$' }) {
        throw 'The fixture still contains trailing whitespace after -Fix.'
    }

    Write-Host 'check-pending-whitespace.tests: PASS'
}
finally {
    Remove-Item -LiteralPath $fixture -Force -ErrorAction SilentlyContinue
}
