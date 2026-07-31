#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$checker = Join-Path $PSScriptRoot 'check-automation-workflow.ps1'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "tzg-workflow-check-$([Guid]::NewGuid().ToString('N'))"

function Invoke-Checker {
  param([string]$AutomationRoot, [switch]$RepositoryOnly)
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checker,
    '-RepositoryRoot', $repositoryRoot
  )
  if ($RepositoryOnly) {
    $arguments += '-RepositoryOnly'
  } else {
    $arguments += @('-AutomationRoot', $AutomationRoot)
  }
  $output = @(& pwsh @arguments 2>&1)
  [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = @($output) -join "`n" }
}

try {
  $repositoryCheck = Invoke-Checker -RepositoryOnly
  if ($repositoryCheck.ExitCode -ne 0) {
    throw "repository workflow contract failed: $($repositoryCheck.Output)"
  }

  $controllerDirectory = Join-Path $testRoot 'tzg-hourly-controller'
  [IO.Directory]::CreateDirectory($controllerDirectory) | Out-Null
  $automationPath = Join-Path $controllerDirectory 'automation.toml'
  foreach ($readOnlyAutomation in @(
      [pscustomobject]@{ Id = 'tzg-daily-automation-briefing'; PromptPath = '开发管理/自动化简报提示词.txt' },
      [pscustomobject]@{ Id = 'tzg-weekly-project-summary'; PromptPath = '开发管理/每周项目总结提示词.txt' }
    )) {
    $directory = Join-Path $testRoot $readOnlyAutomation.Id
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $readOnlyPrompt = [Text.UTF8Encoding]::new($false, $true).GetString(
      [IO.File]::ReadAllBytes((Join-Path $repositoryRoot $readOnlyAutomation.PromptPath))
    ).TrimStart([char]0xFEFF)
    $encodedReadOnlyPrompt = $readOnlyPrompt | ConvertTo-Json -Compress
    [IO.File]::WriteAllText(
      (Join-Path $directory 'automation.toml'),
      "status = `"ACTIVE`"`nprompt = $encodedReadOnlyPrompt`n",
      [Text.UTF8Encoding]::new($false)
    )
  }
  [IO.File]::WriteAllText(
    $automationPath,
    "status = `"PAUSED`"`nprompt = `"old prompt`"`n",
    [Text.UTF8Encoding]::new($false)
  )
  $mismatch = Invoke-Checker -AutomationRoot $testRoot
  if ($mismatch.ExitCode -eq 0 -or -not $mismatch.Output.Contains('controller prompt does not match', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'live prompt mismatch was not detected'
  }

  $prompt = [Text.UTF8Encoding]::new($false, $true).GetString(
    [IO.File]::ReadAllBytes((Join-Path $repositoryRoot '开发管理/自动工作流控制器提示词.txt'))
  ).TrimStart([char]0xFEFF)
  $encodedPrompt = $prompt | ConvertTo-Json -Compress
  [IO.File]::WriteAllText(
    $automationPath,
    "status = `"PAUSED`"`nprompt = $encodedPrompt`n",
    [Text.UTF8Encoding]::new($false)
  )
  $match = Invoke-Checker -AutomationRoot $testRoot
  if ($match.ExitCode -ne 0) {
    throw "matching live prompt failed: $($match.Output)"
  }

  Write-Output 'test-check-automation-workflow: OK'
} finally {
  if (Test-Path -LiteralPath $testRoot) {
    $fullPath = [IO.Path]::GetFullPath($testRoot)
    $tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
      Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
  }
}
