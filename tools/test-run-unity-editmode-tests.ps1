[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'run-unity-editmode-tests.ps1'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("tzg-unity-editmode-runner-" + [guid]::NewGuid().ToString('N'))

function Get-ProtectedFileHashes {
    param([string]$ProjectPath)

    $paths = @(
        & git -C $ProjectPath ls-files -- 'Assets/Scenes/*.unity' 'ProjectSettings/EditorBuildSettings.asset'
    )
    if ($paths.Count -eq 0) {
        throw 'Expected tracked Unity scenes and EditorBuildSettings.asset.'
    }

    return @($paths | ForEach-Object {
        $fullPath = Join-Path $ProjectPath $_
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Protected file is missing: $_"
        }
        "$_=$((Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash)"
    })
}

function Assert-ProtectedFilesUnchanged {
    param(
        [string[]]$Before,
        [string]$ProjectPath,
        [string]$Name
    )

    $after = Get-ProtectedFileHashes -ProjectPath $ProjectPath
    if (@(Compare-Object -ReferenceObject $Before -DifferenceObject $after).Count -ne 0) {
        throw "$Name changed a protected Unity scene or EditorBuildSettings.asset."
    }
}

function New-FakeUnityExecutable {
    param(
        [string]$Path,
        [int]$ExitCode,
        [int]$DelaySeconds = 0
    )

    $script = @'
@echo off
setlocal
set "project="
set "results="
:parse
if "%~1"=="" goto parsed
if /I "%~1"=="-projectPath" (
    set "project=%~2"
    shift
)
if /I "%~1"=="-testResults" (
    set "results=%~2"
    shift
)
shift
goto parse
:parsed
> "%project%\Assets\Scenes\StartMenuScene.unity" echo mutated-by-fake-unity
> "%project%\ProjectSettings\EditorBuildSettings.asset" echo mutated-by-fake-unity
if "__EXIT_CODE__"=="0" (
    > "%results%" echo ^<test-run total="1" passed="1" failed="0" inconclusive="0" skipped="0" result="Passed" /^>
)
for /l %%i in (1,1,__DELAY_SECONDS__) do ping 127.0.0.1 -n 2 > nul
exit /b __EXIT_CODE__
'@
    $script = $script.Replace('__EXIT_CODE__', [string]$ExitCode).Replace('__DELAY_SECONDS__', [string]$DelaySeconds)
    [IO.File]::WriteAllText($Path, $script, [Text.UTF8Encoding]::new($false))
}

function Invoke-RestoreCase {
    param(
        [string]$Name,
        [int]$FakeExitCode,
        [int]$ExpectedExitCode,
        [string]$ProjectPath
    )

    $before = Get-ProtectedFileHashes -ProjectPath $ProjectPath
    $fakeUnityPath = Join-Path $temporaryRoot "$Name-fake-unity.cmd"
    $resultPath = Join-Path $temporaryRoot "$Name-results.xml"
    New-FakeUnityExecutable -Path $fakeUnityPath -ExitCode $FakeExitCode

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $scriptPath -UnityExecutable $fakeUnityPath -ProjectPath $ProjectPath -ResultXmlPath $resultPath -ErrorAction Continue
    $ErrorActionPreference = $previousErrorActionPreference
    if ($LASTEXITCODE -ne $ExpectedExitCode) {
        throw "$Name expected exit code $ExpectedExitCode but got $LASTEXITCODE."
    }

    Assert-ProtectedFilesUnchanged -Before $before -ProjectPath $ProjectPath -Name $Name
}

function Invoke-CompletedResultExitGraceCase {
    param([string]$ProjectPath)

    $before = Get-ProtectedFileHashes -ProjectPath $ProjectPath
    $fakeUnityPath = Join-Path $temporaryRoot 'completed-result-delayed-exit-fake-unity.cmd'
    $resultPath = Join-Path $temporaryRoot 'completed-result-delayed-exit.xml'
    New-FakeUnityExecutable -Path $fakeUnityPath -ExitCode 0 -DelaySeconds 5

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $scriptPath -UnityExecutable $fakeUnityPath -ProjectPath $ProjectPath -ResultXmlPath $resultPath -ResultExitGraceSeconds 1 -ErrorAction Continue
    $ErrorActionPreference = $previousErrorActionPreference
    if ($LASTEXITCODE -ne 0) {
        throw "completed-result-delayed-exit expected exit code 0 but got $LASTEXITCODE."
    }

    Assert-ProtectedFilesUnchanged -Before $before -ProjectPath $ProjectPath -Name 'completed-result-delayed-exit'
}

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

    $projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..\src')).Path
    Invoke-RestoreCase -Name 'success' -FakeExitCode 0 -ExpectedExitCode 0 -ProjectPath $projectPath
    Invoke-RestoreCase -Name 'exception' -FakeExitCode 23 -ExpectedExitCode 1 -ProjectPath $projectPath
    Invoke-CompletedResultExitGraceCase -ProjectPath $projectPath

    Write-Host 'All Unity EditMode result validation cases passed.'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
