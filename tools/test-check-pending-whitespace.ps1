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

    $missingFile = Join-Path $fixtureRoot 'missing.txt'
    $missingOutput = & $checker -Paths @($missingFile) 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Expected an explicitly requested missing path to fail.'
    }
    if (($missingOutput -join "`n") -notmatch 'Missing path:') {
        throw "Expected a missing-path diagnostic. Actual: $($missingOutput -join "`n")"
    }
    Write-Host 'PASS rejects an explicitly requested missing path'

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

    & $checker -Paths @($badFile) -Fix
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected -Fix to repair ordinary trailing whitespace.'
    }
    if ([IO.File]::ReadAllLines($badFile) | Where-Object { $_ -match '[ \t]+$' }) {
        throw 'Expected -Fix to remove ordinary trailing whitespace.'
    }
    Write-Host 'PASS fixes ordinary trailing whitespace'

    $mjsFile = Join-Path $fixtureRoot 'tracked-text-extension.mjs'
    $mjsContent = "export const value = 1; `n"
    [IO.File]::WriteAllText($mjsFile, $mjsContent, [Text.UTF8Encoding]::new($false))

    $mjsOutput = & $checker -Paths @($mjsFile) 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Expected an .mjs file with trailing whitespace to fail.'
    }
    if (($mjsOutput -join "`n") -notmatch 'tracked-text-extension\.mjs:1') {
        throw "Expected an .mjs file and line diagnostic. Actual: $($mjsOutput -join "`n")"
    }
    if ([IO.File]::ReadAllText($mjsFile) -cne $mjsContent) {
        throw 'Expected a read-only .mjs check to preserve file content.'
    }

    & $checker -Paths @($mjsFile) -Fix
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected -Fix to repair trailing whitespace in an .mjs file.'
    }
    if ([IO.File]::ReadAllLines($mjsFile) | Where-Object { $_ -match '[ \t]+$' }) {
        throw 'Expected -Fix to remove trailing whitespace from an .mjs file.'
    }
    Write-Host 'PASS checks and fixes a tracked .mjs text extension'

    $binaryCases = @(
        @{
            Name = 'binary-whitespace.blend'
            Bytes = [byte[]](0x42, 0x4C, 0x45, 0x4E, 0x44, 0x45, 0x52, 0x20, 0x09, 0x0D, 0x0A, 0x00, 0x44, 0x41, 0x54, 0x41, 0x20, 0x0A, 0xFF)
        },
        @{
            Name = 'binary-whitespace.fbx'
            Bytes = [byte[]](0x4B, 0x61, 0x79, 0x64, 0x61, 0x72, 0x61, 0x20, 0x46, 0x42, 0x58, 0x09, 0x0A, 0x00, 0x20, 0x0D, 0x0A, 0xFE)
        }
    )
    $binaryPaths = @()
    $binaryHashes = @{}
    foreach ($case in $binaryCases) {
        $casePath = Join-Path $fixtureRoot $case.Name
        [IO.File]::WriteAllBytes($casePath, $case.Bytes)
        $binaryPaths += $casePath
        $binaryHashes[$casePath] = (Get-FileHash -LiteralPath $casePath -Algorithm SHA256).Hash
    }

    $binaryOutput = & $checker -Paths $binaryPaths *>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Expected binary .blend and .fbx files to be skipped. Actual: $($binaryOutput -join "`n")"
    }
    if (($binaryOutput -join "`n") -notmatch 'check-pending-whitespace: OK \(2 files checked; fix=False\)') {
        throw "Expected skipped binaries to retain the input-file count. Actual: $($binaryOutput -join "`n")"
    }
    foreach ($casePath in $binaryPaths) {
        if ((Get-FileHash -LiteralPath $casePath -Algorithm SHA256).Hash -cne $binaryHashes[$casePath]) {
            throw "Expected a read-only binary check to preserve bytes: $casePath"
        }
    }

    $binaryFixOutput = & $checker -Paths $binaryPaths -Fix *>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Expected binary .blend and .fbx files to be skipped with -Fix. Actual: $($binaryFixOutput -join "`n")"
    }
    if (($binaryFixOutput -join "`n") -notmatch 'check-pending-whitespace: OK \(2 files checked; fix=True\)') {
        throw "Expected skipped binaries to retain the input-file count with -Fix. Actual: $($binaryFixOutput -join "`n")"
    }
    foreach ($casePath in $binaryPaths) {
        if ((Get-FileHash -LiteralPath $casePath -Algorithm SHA256).Hash -cne $binaryHashes[$casePath]) {
            throw "Expected -Fix to preserve binary bytes: $casePath"
        }
    }
    Write-Host 'PASS skips binary .blend and .fbx bytes without changing count or content'

    $semanticMetaFile = Join-Path $fixtureRoot 'semantic-meta-empty-values.meta'
    $semanticMetaContent = "  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    [IO.File]::WriteAllText($semanticMetaFile, $semanticMetaContent, [Text.UTF8Encoding]::new($false))

    & $checker -Paths @($semanticMetaFile)
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected semantic Unity .meta empty-value spaces to pass.'
    }

    & $checker -Paths @($semanticMetaFile) -Fix
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected semantic Unity .meta empty-value spaces to pass with -Fix.'
    }
    if ([IO.File]::ReadAllText($semanticMetaFile) -cne $semanticMetaContent) {
        throw 'Expected -Fix to preserve semantic Unity .meta empty-value spaces.'
    }
    Write-Host 'PASS preserves semantic Unity .meta empty-value spaces'

    $byteStableMetaFile = Join-Path $fixtureRoot 'byte-stable-fix.meta'
    $byteStableMetaContent = "  userData: `nordinary: bad `nlast: clean"
    $byteStableMetaExpected = "  userData: `nordinary: bad`nlast: clean"
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllBytes($byteStableMetaFile, $utf8NoBom.GetBytes($byteStableMetaContent))

    & $checker -Paths @($byteStableMetaFile) -Fix
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected -Fix to repair the byte-stability .meta fixture.'
    }
    $fixedBytes = [IO.File]::ReadAllBytes($byteStableMetaFile)
    $expectedBytes = $utf8NoBom.GetBytes($byteStableMetaExpected)
    if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$fixedBytes, [byte[]]$expectedBytes)) {
        throw 'Expected -Fix to preserve LF/no-BOM/no-EOF-newline bytes while removing only the ordinary trailing space.'
    }
    if ($fixedBytes.Length -ge 3 -and $fixedBytes[0] -eq 0xEF -and $fixedBytes[1] -eq 0xBB -and $fixedBytes[2] -eq 0xBF) {
        throw 'Expected -Fix to preserve UTF-8 without BOM.'
    }
    if ($fixedBytes -contains 0x0D) {
        throw 'Expected -Fix to preserve LF newlines for .meta files.'
    }
    if ($fixedBytes[-1] -in @(0x0A, 0x0D)) {
        throw 'Expected -Fix to preserve the missing EOF newline.'
    }
    Write-Host 'PASS preserves .meta bytes while fixing ordinary trailing whitespace'

    $utf8Bom = [Text.UTF8Encoding]::new($true)
    $bomByteStableCases = @(
        @{ Name = 'bom-lf-with-eof.meta'; Newline = "`n"; HasEofNewline = $true },
        @{ Name = 'bom-lf-without-eof.meta'; Newline = "`n"; HasEofNewline = $false },
        @{ Name = 'bom-crlf-with-eof.meta'; Newline = "`r`n"; HasEofNewline = $true },
        @{ Name = 'bom-crlf-without-eof.meta'; Newline = "`r`n"; HasEofNewline = $false }
    )
    foreach ($case in $bomByteStableCases) {
        $casePath = Join-Path $fixtureRoot $case.Name
        $sourceText = "  userData: $($case.Newline)ordinary: bad $($case.Newline)last: clean"
        $expectedText = "  userData: $($case.Newline)ordinary: bad$($case.Newline)last: clean"
        if ($case.HasEofNewline) {
            $sourceText += $case.Newline
            $expectedText += $case.Newline
        }
        $sourceBytes = [byte[]]($utf8Bom.GetPreamble() + $utf8Bom.GetBytes($sourceText))
        $expectedBytes = [byte[]]($utf8Bom.GetPreamble() + $utf8Bom.GetBytes($expectedText))
        [IO.File]::WriteAllBytes($casePath, $sourceBytes)

        & $checker -Paths @($casePath) -Fix
        if ($LASTEXITCODE -ne 0) {
            throw "Expected -Fix to repair BOM byte-stability fixture: $($case.Name)"
        }
        $actualBytes = [IO.File]::ReadAllBytes($casePath)
        if (-not [Linq.Enumerable]::SequenceEqual([byte[]]$actualBytes, [byte[]]$expectedBytes)) {
            throw "Expected -Fix to preserve BOM, newline convention, and EOF newline state: $($case.Name)"
        }
    }
    Write-Host 'PASS preserves BOM, LF/CRLF, and EOF newline state while fixing whitespace'

    $nonSemanticMetaCases = @(
        @{ Name = 'semantic-shape-wrong-extension.txt'; Content = "  userData: `n  assetBundleName: `n  assetBundleVariant: `n" },
        @{ Name = 'semantic-tab-indent.meta'; Content = "`tuserData: `n" },
        @{ Name = 'semantic-zero-indent.meta'; Content = "userData: `n" },
        @{ Name = 'semantic-one-space-indent.meta'; Content = " userData: `n" },
        @{ Name = 'semantic-three-space-indent.meta'; Content = "   userData: `n" },
        @{ Name = 'semantic-two-trailing-spaces.meta'; Content = "  userData:  `n" }
    )
    foreach ($case in $nonSemanticMetaCases) {
        $casePath = Join-Path $fixtureRoot $case.Name
        [IO.File]::WriteAllText($casePath, $case.Content, [Text.UTF8Encoding]::new($false))

        $caseOutput = & $checker -Paths @($casePath) 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw "Expected non-semantic trailing whitespace to fail: $($case.Name)"
        }
        if (($caseOutput -join "`n") -notmatch [regex]::Escape("$($case.Name):1")) {
            throw "Expected file and line diagnostic for $($case.Name). Actual: $($caseOutput -join "`n")"
        }

        & $checker -Paths @($casePath) -Fix
        if ($LASTEXITCODE -ne 0) {
            throw "Expected -Fix to repair non-semantic trailing whitespace: $($case.Name)"
        }
        if ([IO.File]::ReadAllLines($casePath) | Where-Object { $_ -match '[ \t]+$' }) {
            throw "Expected -Fix to remove non-semantic trailing whitespace: $($case.Name)"
        }
    }
    Write-Host 'PASS rejects and fixes non-semantic Unity .meta lookalikes'

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

    & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -ExpectedPaths $pipeDelimitedPaths
    if ($LASTEXITCODE -ne 0) {
        throw 'Expected a fresh PowerShell process with pipe-delimited expected paths to pass.'
    }
    Write-Host 'PASS accepts pipe-delimited expected paths in a fresh PowerShell process'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Force -Recurse -ErrorAction SilentlyContinue
}
