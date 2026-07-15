#requires -Version 7.0

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

function Test-SemanticMetaEmptyValue {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Line
    )

    return [IO.Path]::GetExtension($Path) -ieq '.meta' -and
        $Line -cmatch '^  (?:userData|assetBundleName|assetBundleVariant): $'
}

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
        $rawBytes = [IO.File]::ReadAllBytes($resolvedPath.Path)
        $hadUtf8Bom = $rawBytes.Length -ge 3 -and
            $rawBytes[0] -eq 0xEF -and $rawBytes[1] -eq 0xBB -and $rawBytes[2] -eq 0xBF
        $rawText = [IO.File]::ReadAllText($resolvedPath.Path)
        $rewrittenText = [regex]::Replace($rawText, '[ \t]+(?=\r\n|\n|\r|$)', {
            param($match)

            $lineStart = 0
            if ($match.Index -gt 0) {
                $lastLf = $rawText.LastIndexOf([char]10, $match.Index - 1)
                $lastCr = $rawText.LastIndexOf([char]13, $match.Index - 1)
                $lineStart = [Math]::Max($lastLf, $lastCr) + 1
            }
            $line = $rawText.Substring($lineStart, $match.Index + $match.Length - $lineStart)
            if (Test-SemanticMetaEmptyValue -Path $resolvedPath.Path -Line $line) {
                return $match.Value
            }
            return ''
        })
        if ($rewrittenText -cne $rawText) {
            [IO.File]::WriteAllText($resolvedPath.Path, $rewrittenText, [Text.UTF8Encoding]::new($hadUtf8Bom))
            $lines = [IO.File]::ReadAllLines($resolvedPath.Path)
            Write-Output "Fixed trailing whitespace: $($resolvedPath.Path)"
        }
    }

    $lineNumber = 0
    foreach ($line in $lines) {
        $lineNumber++
        if ($line -match '[ \t]+$' -and -not (Test-SemanticMetaEmptyValue -Path $resolvedPath.Path -Line $line)) {
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
