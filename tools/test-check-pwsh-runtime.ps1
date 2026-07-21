[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$checkerPath = Join-Path $PSScriptRoot 'check-pwsh-runtime.ps1'
if (-not (Test-Path -LiteralPath $checkerPath -PathType Leaf)) {
    throw 'tools/check-pwsh-runtime.ps1 is missing'
}

$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempRoot ('tzg-pwsh-runtime-' + [guid]::NewGuid().ToString('N'))

function Assert-SafeFixturePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $tempPrefix = $tempRoot
    if (-not $tempPrefix.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $tempPrefix += [System.IO.Path]::DirectorySeparatorChar
    }
    $leaf = Split-Path -Leaf $fullPath
    if (-not $fullPath.StartsWith($tempPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        $leaf -notmatch '^tzg-pwsh-runtime-[0-9a-f]{32}$') {
        throw "Unsafe fixture path: $fullPath"
    }
}

function Write-FixtureFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content,
        [string]$Root = $fixtureRoot
    )

    $path = Join-Path $Root $RelativePath
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Convert-ProcessBytesToText {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][byte[]]$Bytes)

    try {
        return [System.Text.UTF8Encoding]::new($false, $true).GetString($Bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        $oemCodePage = [System.Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage
        return [System.Text.Encoding]::GetEncoding($oemCodePage).GetString($Bytes)
    }
}

function Invoke-RuntimeCheckerProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$AdditionalArguments = @()
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = [System.Environment]::ProcessPath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checkerPath,
            '-RepositoryRoot', $Root) + $AdditionalArguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw 'Failed to start runtime checker process.' }
        $stdoutBuffer = [System.IO.MemoryStream]::new()
        $stderrBuffer = [System.IO.MemoryStream]::new()
        $stdoutTask = $process.StandardOutput.BaseStream.CopyToAsync($stdoutBuffer)
        $stderrTask = $process.StandardError.BaseStream.CopyToAsync($stderrBuffer)
        $process.WaitForExit()
        $stdoutTask.GetAwaiter().GetResult()
        $stderrTask.GetAwaiter().GetResult()
        $stdout = (Convert-ProcessBytesToText -Bytes $stdoutBuffer.ToArray()).TrimEnd([char[]]@([char]13, [char]10))
        $stderr = (Convert-ProcessBytesToText -Bytes $stderrBuffer.ToArray()).TrimEnd([char[]]@([char]13, [char]10))
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdout
            StdErr = $stderr
        }
    }
    finally {
        if ($stdoutBuffer) { $stdoutBuffer.Dispose() }
        if ($stderrBuffer) { $stderrBuffer.Dispose() }
        $process.Dispose()
    }
}

function Invoke-DefaultRuntimeChecker {
    param([Parameter(Mandatory = $true)][string]$Root)

    return Invoke-RuntimeCheckerProcess -Root $Root
}

function Invoke-RuntimeChecker {
    param(
        [Parameter(Mandatory = $true)][string[]]$DocumentPaths,
        [Parameter(Mandatory = $true)][string[]]$ScriptPaths,
        [Parameter(Mandatory = $true)][string[]]$RequiredVersionPaths
    )

    return Invoke-RuntimeCheckerProcess -Root $fixtureRoot -AdditionalArguments @(
        '-DocumentPaths', ($DocumentPaths -join '|'),
        '-ScriptPaths', ($ScriptPaths -join '|'),
        '-RequiredVersionPaths', ($RequiredVersionPaths -join '|'))
}

function Assert-FailedWithCategory {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Category,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Result.ExitCode -eq 0) {
        throw "$Label should fail."
    }
    if (-not [string]::IsNullOrEmpty($Result.StdOut)) {
        throw "$Label should not write stdout on failure. Actual:`n$($Result.StdOut)"
    }
    if ($Result.StdErr -notmatch [regex]::Escape($Category)) {
        throw "$Label should emit $Category on stderr. Actual:`n$($Result.StdErr)"
    }
}

function Assert-FailedWithDiagnostic {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Diagnostic,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Result.ExitCode -eq 0) {
        throw "$Label should fail."
    }
    if (-not [string]::IsNullOrEmpty($Result.StdOut)) {
        throw "$Label should not write stdout on failure. Actual:`n$($Result.StdOut)"
    }
    if ($Result.StdErr -notmatch [regex]::Escape($Diagnostic)) {
        throw "$Label should emit '$Diagnostic' on stderr. Actual:`n$($Result.StdErr)"
    }
}

function Assert-Passed {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Result.ExitCode -ne 0) {
        throw "$Label should pass. Stderr:`n$($Result.StdErr)"
    }
    if ($Result.StdOut -cne 'check-pwsh-runtime: OK') {
        throw "$Label should emit exactly one success line on stdout. Actual:`n$($Result.StdOut)"
    }
    if (-not [string]::IsNullOrEmpty($Result.StdErr)) {
        throw "$Label should leave stderr empty on success. Actual:`n$($Result.StdErr)"
    }
}

Assert-SafeFixturePath -Path $fixtureRoot

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

    $documentPath = 'docs/runtime.md'
    $scriptPath = 'tools/runtime.ps1'
    $requiredVersionPath = 'tools/required.ps1'
    $windowsPowerShell = 'power' + 'shell'
    $absoluteWindowsPowerShell = 'C:\Windows\System32\WindowsPowerShell\v1.0\' + $windowsPowerShell + '.exe'
    $absolutePwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'

    $badDocumentCases = @(
        "$windowsPowerShell -File tools/check.ps1",
        "$windowsPowerShell -ExecutionPolicy Bypass -File tools/check.ps1",
        "${windowsPowerShell}.exe -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1",
        "& $windowsPowerShell -File tools/check.ps1",
        "& '$absoluteWindowsPowerShell' -File tools/check.ps1",
        "$windowsPowerShell tools/check.ps1",
        "准确调用：$windowsPowerShell -File tools/check.ps1",
        'Run `powershell -File tools/check.ps1` now.',
        'Run ``powershell -File tools/check.ps1`` now.',
        'Run ```powershell -File tools/check.ps1``` now.',
        '准确调用：& ''powershell'' -File tools/check.ps1',
        '准确调用：`powershell -File tools/check.ps1`'
    )
    $allowedDocumentCases = @(
        'pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1',
        'PWSH -nOpRoFiLe -eXeCuTiOnPoLiCy bYpAsS -fIlE tools/check.ps1',
        '```powershell',
        'PowerShell 7 is required',
        'Invoke-ChildPowerShell',
        'PowerShell checks use pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1',
        '$example = ''pwsh -NoProfile -File tools/check.ps1''',
        '$example = ''powershell -File tools/check.ps1''',
        '$example = ''准确调用：powershell -File tools/check.ps1''',
        '$example = ''前文，准确调用：powershell -File tools/check.ps1''',
        '$example = "前文，准确调用：powershell -File tools/check.ps1"',
        '$example = ''前文''''引用，准确调用：powershell -File tools/check.ps1''',
        '$example = "前文`"引用，准确调用：powershell -File tools/check.ps1"',
        'Do not run powershell -File tools/check.ps1',
        'Do not run `powershell -File tools/check.ps1`.',
        '说明：Do not run powershell -File tools/check.ps1',
        '禁止调用 `powershell -File tools/check.ps1`。',
        '禁止运行： `powershell -File tools/check.ps1`。',
        '不得调用: `powershell -File tools/check.ps1`。',
        '不得运行：`powershell -File tools/check.ps1`。',
        '不要调用: `powershell -File tools/check.ps1`。',
        '不要运行 `powershell -File tools/check.ps1`。',
        '普通说明：pwsh -NoProfile -File tools/check.ps1 只是错误示例，不得执行。',
        '准确调用：pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1',
        "`$example = @'`npwsh -NoProfile -File tools/check.ps1`n'@",
        "PowerShell 7 is required`n`nInvoke-ChildPowerShell"
    )
    $nonCanonicalPwshDocumentCases = @(
        'pwsh -NoProfile -File tools/check.ps1',
        'pwsh -ExecutionPolicy Bypass -File tools/check.ps1',
        'pwsh -NoProfile -ExecutionPolicy RemoteSigned -File tools/check.ps1',
        'pwsh -f tools/check.ps1',
        'pwsh -fi tools/check.ps1',
        'pwsh -fil tools/check.ps1',
        'pwsh tools/check.ps1',
        'pwsh -NoProfile -ExecutionPolicy Bypass tools/check.ps1',
        '准确调用：pwsh -NoProfile -File tools/check.ps1',
        '> pwsh -NoProfile -File tools/check.ps1',
        '- pwsh -ExecutionPolicy Bypass -File tools/check.ps1',
        '1. pwsh -NoProfile -ExecutionPolicy RemoteSigned -File tools/check.ps1',
        '- [ ] pwsh -NoProfile -File tools/check.ps1',
        ('```powershell' + "`n" + 'pwsh -f tools/check.ps1' + "`n" + '```'),
        'Run `pwsh -fi tools/check.ps1` now.'
    )
    $badScript = @"
#requires -Version 7.0
& $windowsPowerShell -NoProfile -File tools/check.ps1
"@
    $forbiddenScriptCases = @(
        $badScript,
        "#requires -Version 7.0`n& '$absoluteWindowsPowerShell' -File tools/check.ps1`n",
        @'
#requires -Version 7.0
& "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -File tools/check.ps1
'@,
        "#requires -Version 7.0`n& $windowsPowerShell tools/check.ps1`n",
        @'
#requires -Version 7.0
$exe = 'powershell'
& $exe -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'power' + 'shell'
& $exe -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'pwsh'
$exe = 'powershell'
& $exe -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'powershell'
& $exe -File tools/check.ps1
$exe = 'pwsh'
'@,
        @'
#requires -Version 7.0
$condition = Get-Random -Minimum 0 -Maximum 2
if ($condition) {
    $exe = 'powershell'
}
else {
    $exe = 'pwsh'
}
& $exe -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'powershell'
$condition = Get-Random -Minimum 0 -Maximum 2
if ($condition) {
    $exe = 'custom-runner'
}
& $exe -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
& $exe -File tools/check.ps1
'@
    )
    $nonCanonicalPwshScriptCases = @(
        ('#requires -Version 7.0' + "`n& pwsh -NoProfile -File tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -ExecutionPolicy Bypass -File tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -NoProfile -ExecutionPolicy RemoteSigned -File tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -f tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -fi tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -fil tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh tools/check.ps1`n"),
        ('#requires -Version 7.0' + "`n& pwsh -NoProfile -ExecutionPolicy Bypass tools/check.ps1`n"),
        @'
#requires -Version 7.0
$script = 'tools/check.ps1'
& pwsh $script
'@,
        @'
#requires -Version 7.0
& pwsh ('tools/' + 'check.ps1')
'@,
        "#requires -Version 7.0`n& '$absolutePwsh' -NoProfile -File tools/check.ps1`n",
        @'
#requires -Version 7.0
$exe = 'pwsh'
& $exe -NoProfile -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'pw' + 'sh'
& $exe -NoProfile -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$exe = 'powershell'
$exe = 'pwsh'
& $exe -NoProfile -File tools/check.ps1
'@
    )
    $allowedDynamicScriptCases = @(
        @'
#requires -Version 7.0
$engine = (Get-Process -Id $PID).Path
& $engine -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$engine = (Get-Process -Id $PID).Path
function Invoke-ChildScript {
    & $engine -File tools/check.ps1
}
'@,
        @'
#requires -Version 7.0
$checker = Join-Path $PSScriptRoot 'check.ps1'
& $checker -File tools/check.ps1
'@,
        @'
#requires -Version 7.0
$script = Join-Path $PSScriptRoot 'check.ps1'
& pwsh $script
'@,
        @'
#requires -Version 7.0
$script = 'tools/check.ps1'
& pwsh -NoProfile -ExecutionPolicy Bypass -File $script
'@,
        @'
#requires -Version 7.0
$condition = Get-Random -Minimum 0 -Maximum 2
if ($condition) {
    $exe = 'powershell'
}
else {
    $exe = 'custom-runner'
}
$exe = 'custom-runner'
& $exe -File tools/check.ps1
'@
    )
    $goodScript = @'
#requires -Version 7.0
& pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1
& PWSH -nOpRoFiLe -eXeCuTiOnPoLiCy bYpAsS -fIlE tools/check.ps1
& 'C:\Program Files\PowerShell\7\pwsh.exe' -NoProfile -ExecutionPolicy Bypass -File tools/check.ps1
$name = 'power' + 'shell -File is forbidden text, not a command'
$example = 'pwsh -NoProfile -File tools/check.ps1'
'@
    $missingRequires = @'
param()
'runtime gate missing'
'@
    $invalidRequiresCases = @(
        "`$text = @'`n#requires -Version 7.0`n'@`n",
        '$text = ''#requires -Version 7.0''',
        "<#`n#requires -Version 7.0`n#>`n",
        "#requires -Version 6.0`n",
        "#requires -Version 7.1`n",
        "#requires -Version 7.4`n",
        "#requires -Version 7.99`n"
    )
    $validRequiresCases = @(
        "#requires -Version 7.0`nparam()`n"
    )

    Write-FixtureFile -RelativePath $scriptPath -Content $goodScript
    Write-FixtureFile -RelativePath $requiredVersionPath -Content $goodScript

    for ($index = 0; $index -lt $badDocumentCases.Count; $index++) {
        Write-FixtureFile -RelativePath $documentPath -Content $badDocumentCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-FailedWithCategory -Result $result -Category 'PW7_FORBIDDEN_DOCUMENT_COMMAND' -Label "bad document case $index"
    }

    for ($index = 0; $index -lt $allowedDocumentCases.Count; $index++) {
        Write-FixtureFile -RelativePath $documentPath -Content $allowedDocumentCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-Passed -Result $result -Label "allowed document case $index"
    }

    for ($index = 0; $index -lt $nonCanonicalPwshDocumentCases.Count; $index++) {
        Write-FixtureFile -RelativePath $documentPath -Content $nonCanonicalPwshDocumentCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-FailedWithCategory -Result $result -Category 'PW7_NONCANONICAL_PWSH_COMMAND' -Label "noncanonical pwsh document case $index"
    }

    Write-FixtureFile -RelativePath $documentPath -Content $allowedDocumentCases[0]
    for ($index = 0; $index -lt $forbiddenScriptCases.Count; $index++) {
        Write-FixtureFile -RelativePath $scriptPath -Content $forbiddenScriptCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-FailedWithCategory -Result $result -Category 'PW7_FORBIDDEN_SCRIPT_COMMAND' -Label "forbidden script command case $index"
    }

    for ($index = 0; $index -lt $nonCanonicalPwshScriptCases.Count; $index++) {
        Write-FixtureFile -RelativePath $scriptPath -Content $nonCanonicalPwshScriptCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-FailedWithCategory -Result $result -Category 'PW7_NONCANONICAL_PWSH_COMMAND' -Label "noncanonical pwsh script case $index"
    }

    for ($index = 0; $index -lt $allowedDynamicScriptCases.Count; $index++) {
        Write-FixtureFile -RelativePath $scriptPath -Content $allowedDynamicScriptCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-Passed -Result $result -Label "allowed dynamic script case $index"
    }

    Write-FixtureFile -RelativePath $scriptPath -Content $goodScript
    $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
    Assert-Passed -Result $result -Label 'good script command'

    Write-FixtureFile -RelativePath $requiredVersionPath -Content $missingRequires
    $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
    Assert-FailedWithCategory -Result $result -Category 'PW7_MISSING_REQUIRES' -Label 'missing requires declaration'

    for ($index = 0; $index -lt $invalidRequiresCases.Count; $index++) {
        Write-FixtureFile -RelativePath $requiredVersionPath -Content $invalidRequiresCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-FailedWithCategory -Result $result -Category 'PW7_MISSING_REQUIRES' -Label "invalid requires case $index"
    }

    for ($index = 0; $index -lt $validRequiresCases.Count; $index++) {
        Write-FixtureFile -RelativePath $requiredVersionPath -Content $validRequiresCases[$index]
        $result = Invoke-RuntimeChecker -DocumentPaths @($documentPath) -ScriptPaths @($scriptPath) -RequiredVersionPaths @($requiredVersionPath)
        Assert-Passed -Result $result -Label "valid requires case $index"
    }

    $defaultRoot = Join-Path $fixtureRoot 'default-mode'
    $defaultDocumentPaths = @(
        'AGENTS.md',
        'CLAUDE.md',
        '开发管理/开发-技术经验.txt',
        '开发管理/状态与建议维护规则.txt',
        '开发管理/自动工作流规则.txt',
        '开发管理/自动工作流控制器提示词.txt',
        '开发管理/当前任务队列.txt',
        '开发管理/任务列表/内容设计任务.txt',
        '开发管理/任务列表/场景与Unity任务.txt',
        '开发管理/任务列表/数值与战斗任务.txt',
        '开发管理/任务列表/数据链路任务.txt',
        '开发管理/任务列表/审核与交接任务.txt',
        'docs/superpowers/plans/2026-07-15-feishu-decision-channel-implementation.md'
    )
    $defaultRequiredVersionPaths = @(
        'tools/hourly-automation-lease.ps1',
        'tools/check-automation-workflow.ps1',
        'tools/check-review-text.ps1',
        'tools/check-data-chain.ps1',
        'tools/check-pending-whitespace.ps1',
        'tools/run-unity-editmode-tests.ps1'
    )
    foreach ($path in $defaultDocumentPaths) {
        Write-FixtureFile -Root $defaultRoot -RelativePath $path -Content "runtime policy`n"
    }
    foreach ($path in $defaultRequiredVersionPaths) {
        Write-FixtureFile -Root $defaultRoot -RelativePath $path -Content "#requires -Version 7.0`nparam()`n"
    }
    Write-FixtureFile -Root $defaultRoot -RelativePath '开发管理/任务列表/历史归档/old.txt' -Content "$windowsPowerShell -File tools/old.ps1`n"

    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-Passed -Result $result -Label 'clean minimal default fixture with archived document'

    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/hourly-automation-lease.ps1' -Content ''
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_MISSING_REQUIRES tools/hourly-automation-lease.ps1:1' -Label 'hourly lease requires PowerShell 7'
    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/hourly-automation-lease.ps1' -Content "#requires -Version 7.0`nparam()`n"

    Write-FixtureFile -Root $defaultRoot -RelativePath '开发管理/任务列表/审核与交接任务.txt' -Content "- [ ] pwsh -fi tools/check.ps1`n"
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_NONCANONICAL_PWSH_COMMAND 开发管理/任务列表/审核与交接任务.txt:1' -Label 'direct review task document discovery'
    Write-FixtureFile -Root $defaultRoot -RelativePath '开发管理/任务列表/审核与交接任务.txt' -Content "runtime policy`n"

    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/nested/forbidden.ps1' -Content "#requires -Version 7.0`n& $windowsPowerShell -File tools/check.ps1`n"
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_FORBIDDEN_SCRIPT_COMMAND tools/nested/forbidden.ps1:2' -Label 'nested tools script discovery'
    Remove-Item -LiteralPath (Join-Path $defaultRoot 'tools/nested/forbidden.ps1') -Force

    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/nested/parse-error.ps1' -Content "param(`n"
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_SCRIPT_PARSE_ERROR tools/nested/parse-error.ps1:1' -Label 'default parser error diagnostic'
    Remove-Item -LiteralPath (Join-Path $defaultRoot 'tools/nested/parse-error.ps1') -Force

    Remove-Item -LiteralPath (Join-Path $defaultRoot 'CLAUDE.md') -Force
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_PATH_NOT_FOUND CLAUDE.md:1' -Label 'default missing path diagnostic'
    Write-FixtureFile -Root $defaultRoot -RelativePath 'CLAUDE.md' -Content "runtime policy`n"

    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/check-review-text.ps1' -Content ''
    $result = Invoke-DefaultRuntimeChecker -Root $defaultRoot
    Assert-FailedWithDiagnostic -Result $result -Diagnostic 'PW7_MISSING_REQUIRES tools/check-review-text.ps1:1' -Label 'default empty required script diagnostic'
    Write-FixtureFile -Root $defaultRoot -RelativePath 'tools/check-review-text.ps1' -Content "#requires -Version 7.0`nparam()`n"

    Write-Output 'test-check-pwsh-runtime: OK'
}
finally {
    Assert-SafeFixturePath -Path $fixtureRoot
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
