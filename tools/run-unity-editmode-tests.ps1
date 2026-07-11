[CmdletBinding()]
param(
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe',
    [string]$ProjectPath,
    [string]$ResultXmlPath,
    [ValidateRange(0, 600)]
    [int]$ResultExitGraceSeconds = 30,
    [switch]$ValidateResultOnly
)

$ErrorActionPreference = 'Stop'

function Get-UnityWorkspaceSnapshot {
    param([string]$ProjectPath)

    $relativePaths = @(
        & git -C $ProjectPath ls-files -- 'Assets/**' 'ProjectSettings/**'
    )
    if ($relativePaths.Count -eq 0) {
        throw 'No tracked Unity workspace files were found to protect.'
    }

    return @($relativePaths | Sort-Object | ForEach-Object {
        $fullPath = Join-Path $ProjectPath $_
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Protected file is missing: $_"
        }
        [pscustomobject]@{
            RelativePath = $_
            FullPath = $fullPath
            Content = [IO.File]::ReadAllBytes($fullPath)
        }
    })
}

function Restore-UnityWorkspaceSnapshot {
    param([object[]]$Snapshot)

    foreach ($file in $Snapshot) {
        [IO.File]::WriteAllBytes($file.FullPath, $file.Content)
    }
}

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

$exitCode = 0
$workspaceSnapshot = $null
try {
    if ($ValidateResultOnly) {
        if ([string]::IsNullOrWhiteSpace($ResultXmlPath)) {
            throw 'ResultXmlPath is required with ValidateResultOnly.'
        }
        Test-UnityEditModeResult -Path $ResultXmlPath
    }

    if (-not $ValidateResultOnly) {
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
        $workspaceSnapshot = Get-UnityWorkspaceSnapshot -ProjectPath $resolvedProjectPath

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

        $process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -PassThru
        $validResultObserved = $false
        $stoppedAfterValidResult = $false
        while (-not $process.HasExited) {
            if (Test-Path -LiteralPath $ResultXmlPath -PathType Leaf) {
                try {
                    Test-UnityEditModeResult -Path $ResultXmlPath
                    $validResultObserved = $true
                    break
                }
                catch {
                    # Unity may still be writing the result XML; its final validation runs below.
                }
            }
            Start-Sleep -Milliseconds 250
        }

        if ($validResultObserved -and -not $process.HasExited) {
            if (-not $process.WaitForExit($ResultExitGraceSeconds * 1000)) {
                Stop-Process -Id $process.Id -Force -ErrorAction Stop
                $process.WaitForExit()
                $stoppedAfterValidResult = $true
            }
        }

        if ($process.ExitCode -ne 0 -and -not $stoppedAfterValidResult) {
            throw "Unity EditMode test process failed with exit code $($process.ExitCode). See $logPath"
        }

        Test-UnityEditModeResult -Path $ResultXmlPath
    }
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    $exitCode = 1
}
finally {
    if ($null -ne $workspaceSnapshot) {
        try {
            Restore-UnityWorkspaceSnapshot -Snapshot $workspaceSnapshot
        }
        catch {
            [Console]::Error.WriteLine("Could not restore Unity workspace files: $($_.Exception.Message)")
            $exitCode = 1
        }
    }
}

exit $exitCode
