[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$Paths,
    [string]$ExpectedPaths
)

$ErrorActionPreference = 'Stop'
$hasErrors = $false
$checkedCount = 0
$pathsToCheck = @()
if ($null -ne $Paths) {
    $pathsToCheck += @($Paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedPaths)) {
    $pathsToCheck += @($ExpectedPaths -split '\|' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if ($pathsToCheck.Count -eq 0) {
    throw 'Specify at least one file through -Paths or -ExpectedPaths.'
}

foreach ($path in $pathsToCheck) {
    $resolvedPath = Resolve-Path -LiteralPath $path -ErrorAction SilentlyContinue
    if ($null -eq $resolvedPath) {
        Write-Output "Missing path: $path"
        $hasErrors = $true
        continue
    }

    if (Test-Path -LiteralPath $resolvedPath.Path -PathType Container) {
        Write-Output "Expected a file path, received directory: $($resolvedPath.Path)"
        $hasErrors = $true
        continue
    }

    $checkedCount++
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($resolvedPath.Path)) {
        $lineNumber++
        if ($line -match '[ \t]+$') {
            Write-Output "$($resolvedPath.Path):${lineNumber}: trailing whitespace."
            $hasErrors = $true
        }
    }
}

if ($hasErrors) {
    exit 1
}

Write-Host "check-pending-whitespace: OK ($checkedCount files checked)"
exit 0
