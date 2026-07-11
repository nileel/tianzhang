[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$checker = Join-Path $PSScriptRoot 'check-pending-whitespace.ps1'
if (-not (Test-Path -LiteralPath $checker)) {
    throw "Missing checker: $checker"
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('tzg-whitespace-check-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

    $badFile = Join-Path $fixtureRoot 'untracked-trailing-space.txt'
    [IO.File]::WriteAllText($badFile, "line with trailing space `n", [Text.UTF8Encoding]::new($false))

    $badOutput = & $checker -Paths @($badFile) 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Expected an untracked file with trailing whitespace to fail.'
    }
    if (($badOutput -join "`n") -notmatch 'untracked-trailing-space\.txt:1') {
        throw "Expected file and line diagnostic. Actual: $($badOutput -join "`n")"
    }
    Write-Host 'PASS rejects untracked trailing whitespace'

    $cleanFile = Join-Path $fixtureRoot 'clean.txt'
    [IO.File]::WriteAllText($cleanFile, "clean line`n", [Text.UTF8Encoding]::new($false))

    & $checker -Paths @($cleanFile)
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected a clean untracked file to pass.'
    }
    Write-Host 'PASS accepts clean untracked file'

    $cleanPaths = @($cleanFile, $cleanFile)
    & $checker -Paths $cleanPaths
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected multiple clean paths to pass.'
    }
    Write-Host 'PASS accepts multiple clean paths'

    $pipeDelimitedPaths = "$cleanFile|$cleanFile"
    & $checker -ExpectedPaths $pipeDelimitedPaths
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected pipe-delimited expected paths to pass.'
    }
    Write-Host 'PASS accepts pipe-delimited expected paths'

    & powershell -NoProfile -ExecutionPolicy Bypass -File $checker -ExpectedPaths $pipeDelimitedPaths
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected a fresh PowerShell process with pipe-delimited expected paths to pass.'
    }
    Write-Host 'PASS accepts pipe-delimited expected paths in a fresh PowerShell process'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Force -Recurse -ErrorAction SilentlyContinue
}
