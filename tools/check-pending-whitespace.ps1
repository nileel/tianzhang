[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$Paths,
    [string]$ExpectedPaths,
    [switch]$Fix
)

$ErrorActionPreference = 'Stop'
$hasErrors = $false
$checkedCount = 0
$pathsToCheck = @()
$textExtensions = @(
    '.asmdef', '.asmref', '.asset', '.cginc', '.cs', '.csv', '.hlsl', '.json',
    '.md', '.meta', '.prefab', '.ps1', '.shader', '.txt', '.unity', '.xml', '.yaml', '.yml'
)
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
    $isTextFile = $textExtensions -contains [IO.Path]::GetExtension($resolvedPath.Path).ToLowerInvariant()
    $lines = [IO.File]::ReadAllLines($resolvedPath.Path)

    if ($Fix -and $isTextFile) {
        $trimmedLines = @($lines | ForEach-Object { $_ -replace '[ \t]+$', '' })
        if (-not [Linq.Enumerable]::SequenceEqual([string[]]$lines, [string[]]$trimmedLines)) {
            [IO.File]::WriteAllLines($resolvedPath.Path, [string[]]$trimmedLines, [Text.UTF8Encoding]::new($false))
            $lines = $trimmedLines
            Write-Output "Fixed trailing whitespace: $($resolvedPath.Path)"
        }
    }

    $lineNumber = 0
    foreach ($line in $lines) {
        $lineNumber++
        if ($line -match '[ \t]+$') {
            if ($Fix -and -not $isTextFile) {
                Write-Output "$($resolvedPath.Path):${lineNumber}: trailing whitespace in a non-text file; not modified."
                $hasErrors = $true
                continue
            }
            Write-Output "$($resolvedPath.Path):${lineNumber}: trailing whitespace."
            $hasErrors = $true
        }
    }
}

if ($hasErrors) {
    exit 1
}

Write-Host "check-pending-whitespace: OK ($checkedCount files checked; fix=$Fix)"
exit 0
