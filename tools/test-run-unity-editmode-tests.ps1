[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'run-unity-editmode-tests.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("tzg-unity-editmode-runner-" + [guid]::NewGuid().ToString('N'))

function Invoke-ResultCase {
    param(
        [string]$Name,
        [string]$Xml,
        [int]$ExpectedExitCode
    )

    $resultPath = Join-Path $temporaryRoot "$Name.xml"
    [IO.File]::WriteAllText($resultPath, $Xml, [Text.UTF8Encoding]::new($false))
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $scriptPath -ResultXmlPath $resultPath -ValidateResultOnly -ErrorAction Continue
    $ErrorActionPreference = $previousErrorActionPreference
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "$Name expected exit code $ExpectedExitCode but got $LASTEXITCODE."
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    Invoke-ResultCase -Name 'passed' -ExpectedExitCode 0 -Xml '<test-run total="1" passed="1" failed="0" inconclusive="0" skipped="0" result="Passed" />'
    Invoke-ResultCase -Name 'failed' -ExpectedExitCode 1 -Xml '<test-run total="1" passed="0" failed="1" inconclusive="0" skipped="0" result="Failed" />'

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $scriptPath -ResultXmlPath (Join-Path $temporaryRoot 'missing.xml') -ValidateResultOnly -ErrorAction Continue
    $ErrorActionPreference = $previousErrorActionPreference
    if ($LASTEXITCODE -ne 1) {
        throw "missing expected exit code 1 but got $LASTEXITCODE."
    }

    Invoke-ResultCase -Name 'malformed' -ExpectedExitCode 1 -Xml '<test-run'
    Invoke-ResultCase -Name 'zero-tests' -ExpectedExitCode 1 -Xml '<test-run total="0" passed="0" failed="0" inconclusive="0" skipped="0" result="Passed" />'

    Write-Host 'All Unity EditMode result validation cases passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
