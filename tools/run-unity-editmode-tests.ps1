[CmdletBinding()]
param(
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe',
    [string]$ProjectPath,
    [string]$ResultXmlPath,
    [switch]$ValidateResultOnly
)

$ErrorActionPreference = 'Stop'

function Get-RequiredResultCount {
    param(
        [System.Xml.XmlElement]$Result,
        [string]$Name
    )

    if (-not $Result.HasAttribute($Name)) {
        throw "Test result XML is missing the '$Name' attribute."
    }

    $value = 0
    if (-not [int]::TryParse($Result.GetAttribute($Name), [ref]$value) -or $value -lt 0) {
        throw "Test result XML has an invalid '$Name' attribute."
    }

    return $value
}

function Test-UnityEditModeResult {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Unity EditMode result XML was not created: $Path"
    }

    try {
        [xml]$document = [IO.File]::ReadAllText($Path)
    }
    catch {
        throw "Unity EditMode result XML could not be parsed: $Path. $($_.Exception.Message)"
    }

    $result = $document.DocumentElement
    if ($null -eq $result -or $result.Name -ne 'test-run') {
        throw "Unity EditMode result XML must have a test-run root element."
    }

    $total = Get-RequiredResultCount -Result $result -Name 'total'
    $passed = Get-RequiredResultCount -Result $result -Name 'passed'
    $failed = Get-RequiredResultCount -Result $result -Name 'failed'
    $inconclusive = Get-RequiredResultCount -Result $result -Name 'inconclusive'
    $skipped = Get-RequiredResultCount -Result $result -Name 'skipped'

    if ($total -eq 0) {
        throw 'Unity EditMode result XML reported zero executed tests.'
    }

    if ($total -ne ($passed + $failed + $inconclusive + $skipped)) {
        throw "Unity EditMode result XML is incomplete: total=$total, passed=$passed, failed=$failed, inconclusive=$inconclusive, skipped=$skipped."
    }

    if ($failed -ne 0 -or $inconclusive -ne 0 -or $skipped -ne 0 -or $result.GetAttribute('result') -ne 'Passed') {
        throw "Unity EditMode tests did not pass completely: passed=$passed, failed=$failed, inconclusive=$inconclusive, skipped=$skipped, result=$($result.GetAttribute('result'))."
    }
}

try {
    if ($ValidateResultOnly) {
        if ([string]::IsNullOrWhiteSpace($ResultXmlPath)) {
            throw 'ResultXmlPath is required with ValidateResultOnly.'
        }
        Test-UnityEditModeResult -Path $ResultXmlPath
        exit 0
    }

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        $ProjectPath = Join-Path $PSScriptRoot '..\src'
    }

    $resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $UnityExecutable -PathType Leaf)) {
        throw "Unity executable was not found: $UnityExecutable"
    }

    if ([string]::IsNullOrWhiteSpace($ResultXmlPath)) {
        $ResultXmlPath = Join-Path ([IO.Path]::GetTempPath()) 'tzg-editmode-test-results.xml'
    }

    $resultDirectory = Split-Path -Parent $ResultXmlPath
    if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
        New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    }
    Remove-Item -LiteralPath $ResultXmlPath -Force -ErrorAction SilentlyContinue

    $logPath = Join-Path ([IO.Path]::GetTempPath()) 'tzg-editmode-test-runner.log'
    $arguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath', $resolvedProjectPath,
        '-runTests',
        '-testPlatform', 'EditMode',
        '-testResults', $ResultXmlPath,
        '-logFile', $logPath
    )

    $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Unity EditMode test process failed with exit code $($process.ExitCode). See $logPath"
    }

    Test-UnityEditModeResult -Path $ResultXmlPath
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
