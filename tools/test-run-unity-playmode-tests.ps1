[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$runner = Join-Path $PSScriptRoot 'run-unity-playmode-tests.ps1'
if (-not (Test-Path -LiteralPath $runner)) {
    throw "Missing runner: $runner"
}

# The runner reports failures through [Console]::Error, which bypasses in-process stream
# redirection; launch it as a child pwsh process so both its exit code and its stderr
# diagnostics are observable, exactly like the production invocation pattern.
function Invoke-RunnerCase {
    param([string[]]$Arguments)

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $runner @Arguments 2>&1
    [pscustomobject]@{
        Output = @($output -join "`n")
        ExitCode = $LASTEXITCODE
    }
}

function New-TestResultXml {
    param(
        [string]$Path,
        [string]$RootName,
        [string]$RootAttributes
    )

    $content = '<?xml version="1.0" encoding="utf-8"?>' + "`n<" + $RootName + $RootAttributes + ' />'
    [IO.File]::WriteAllText($Path, $content, [Text.UTF8Encoding]::new($false))
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('tzg-playmode-test-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null

    $missingXml = Join-Path $fixtureRoot 'missing.xml'
    $missingCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $missingXml)
    if ($missingCase.ExitCode -eq 0) {
        throw 'Expected a missing result XML to fail.'
    }
    if ($missingCase.Output -notmatch 'was not created') {
        throw "Expected a missing-result diagnostic. Actual: $($missingCase.Output)"
    }
    Write-Host 'PASS rejects a missing result XML'

    $noResultPathCase = Invoke-RunnerCase @('-ValidateResultOnly')
    if ($noResultPathCase.ExitCode -eq 0) {
        throw 'Expected ValidateResultOnly without ResultXmlPath to fail.'
    }
    if ($noResultPathCase.Output -notmatch 'ResultXmlPath is required') {
        throw "Expected a missing-parameter diagnostic. Actual: $($noResultPathCase.Output)"
    }
    Write-Host 'PASS rejects ValidateResultOnly without a result path'

    $notXml = Join-Path $fixtureRoot 'not-xml.txt'
    [IO.File]::WriteAllText($notXml, 'this is not xml', [Text.UTF8Encoding]::new($false))
    $notXmlCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $notXml)
    if ($notXmlCase.ExitCode -eq 0) {
        throw 'Expected an unparseable result XML to fail.'
    }
    if ($notXmlCase.Output -notmatch 'could not be parsed') {
        throw "Expected an unparseable-XML diagnostic. Actual: $($notXmlCase.Output)"
    }
    Write-Host 'PASS rejects an unparseable result XML'

    $wrongRoot = Join-Path $fixtureRoot 'wrong-root.xml'
    New-TestResultXml -Path $wrongRoot -RootName 'test-suite' -RootAttributes ' result="Passed"'
    $wrongRootCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $wrongRoot)
    if ($wrongRootCase.ExitCode -eq 0) {
        throw 'Expected a non-test-run root element to fail.'
    }
    if ($wrongRootCase.Output -notmatch 'test-run root element') {
        throw "Expected a root-element diagnostic. Actual: $($wrongRootCase.Output)"
    }
    Write-Host 'PASS rejects a non-test-run root element'

    $missingTotal = Join-Path $fixtureRoot 'missing-total.xml'
    New-TestResultXml -Path $missingTotal -RootName 'test-run' -RootAttributes ' result="Passed" passed="1" failed="0" inconclusive="0" skipped="0"'
    $missingTotalCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $missingTotal)
    if ($missingTotalCase.ExitCode -eq 0) {
        throw 'Expected a result XML without total to fail.'
    }
    if ($missingTotalCase.Output -notmatch "missing the 'total' attribute") {
        throw "Expected a missing-total diagnostic. Actual: $($missingTotalCase.Output)"
    }
    Write-Host 'PASS rejects a result XML without the total attribute'

    $zeroTotal = Join-Path $fixtureRoot 'zero-total.xml'
    New-TestResultXml -Path $zeroTotal -RootName 'test-run' -RootAttributes ' result="Passed" total="0" passed="0" failed="0" inconclusive="0" skipped="0"'
    $zeroTotalCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $zeroTotal)
    if ($zeroTotalCase.ExitCode -eq 0) {
        throw 'Expected a zero-total result XML to fail.'
    }
    if ($zeroTotalCase.Output -notmatch 'zero executed tests') {
        throw "Expected a zero-total diagnostic. Actual: $($zeroTotalCase.Output)"
    }
    Write-Host 'PASS rejects a zero-total result XML'

    $incomplete = Join-Path $fixtureRoot 'incomplete.xml'
    New-TestResultXml -Path $incomplete -RootName 'test-run' -RootAttributes ' result="Failed" total="2" passed="1" failed="0" inconclusive="0" skipped="0"'
    $incompleteCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $incomplete)
    if ($incompleteCase.ExitCode -eq 0) {
        throw 'Expected an incomplete result XML to fail.'
    }
    if ($incompleteCase.Output -notmatch 'is incomplete') {
        throw "Expected an incompleteness diagnostic. Actual: $($incompleteCase.Output)"
    }
    Write-Host 'PASS rejects an incomplete result XML'

    $failedXml = Join-Path $fixtureRoot 'failed.xml'
    New-TestResultXml -Path $failedXml -RootName 'test-run' -RootAttributes ' result="Failed" total="1" passed="0" failed="1" inconclusive="0" skipped="0"'
    $failedCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $failedXml)
    if ($failedCase.ExitCode -eq 0) {
        throw 'Expected a failed result XML to fail.'
    }
    if ($failedCase.Output -notmatch 'did not pass completely') {
        throw "Expected a failure diagnostic. Actual: $($failedCase.Output)"
    }
    Write-Host 'PASS propagates a failed PlayMode run'

    $passedXml = Join-Path $fixtureRoot 'passed.xml'
    New-TestResultXml -Path $passedXml -RootName 'test-run' -RootAttributes ' result="Passed" total="2" passed="2" failed="0" inconclusive="0" skipped="0"'
    $passedCase = Invoke-RunnerCase @('-ValidateResultOnly', '-ResultXmlPath', $passedXml)
    if ($passedCase.ExitCode -ne 0) {
        throw "Expected a fully passing result XML to succeed. Actual: $($passedCase.Output)"
    }
    Write-Host 'PASS accepts a fully passing result XML'

    $missingUnity = Join-Path $fixtureRoot 'missing-unity.exe'
    $missingUnityCase = Invoke-RunnerCase @('-UnityExecutable', $missingUnity, '-ProjectPath', $fixtureRoot)
    if ($missingUnityCase.ExitCode -eq 0) {
        throw 'Expected a missing Unity executable to fail before launching.'
    }
    if ($missingUnityCase.Output -notmatch 'Unity executable was not found') {
        throw "Expected a missing-executable diagnostic. Actual: $($missingUnityCase.Output)"
    }
    Write-Host 'PASS rejects a missing Unity executable without launching'

    $missingProject = Join-Path $fixtureRoot 'does-not-exist'
    $missingProjectCase = Invoke-RunnerCase @('-ProjectPath', $missingProject)
    if ($missingProjectCase.ExitCode -eq 0) {
        throw 'Expected a missing project path to fail.'
    }
    if ($missingProjectCase.Output -notmatch 'does-not-exist') {
        throw "Expected a missing-project diagnostic. Actual: $($missingProjectCase.Output)"
    }
    Write-Host 'PASS rejects a missing project path'
}
finally {
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
}
