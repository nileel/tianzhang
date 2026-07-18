#requires -Version 7.0

$ErrorActionPreference = 'Stop'

$scriptPath = Join-Path $PSScriptRoot 'test-external-ai-self-commit.ps1'
$text = Get-Content -LiteralPath $scriptPath -Raw

function Require-Match {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Pattern,
    [Parameter(Mandatory = $true)]
    [string]$Message
  )

  if ($text -notmatch $Pattern) {
    throw $Message
  }
}

Require-Match "'--permission-mode'\s*'dontAsk'" 'Canary lacks non-interactive permission mode'
Require-Match "'--allowedTools'" 'Canary lacks a least-privilege tool allowlist'
Require-Match 'automation-workspace-guard\.ps1' 'Canary does not allow the workspace guard'
Require-Match 'check-pending-whitespace\.ps1' 'Canary does not allow the direct check'
Require-Match 'automation-finalize-commit\.ps1' 'Canary does not allow the finalizer'
Require-Match '-File tools/automation-workspace-guard\.ps1 Snapshot' 'Canary prompt lacks the exact Snapshot command form'
Require-Match '-File tools/automation-workspace-guard\.ps1 Check' 'Canary prompt lacks the exact Check command form'
Require-Match '-File tools/automation-workspace-guard\.ps1 Verify' 'Canary prompt lacks the exact Verify command form'
Require-Match '-File tools/automation-finalize-commit\.ps1 -ExpectedPaths' 'Canary prompt lacks the exact finalizer command form'
Require-Match 'GIT_AUTHOR_NAME' 'Canary does not supply the external Git identity'
Require-Match '\.claude[\\/]settings\.json' 'Canary does not inspect effective Claude settings'
Require-Match 'phase1\.stdout\.txt' 'Canary does not preserve phase 1 stdout'
Require-Match 'phase2\.stdout\.txt' 'Canary does not preserve phase 2 stdout'

if ($text -match 'dangerously-skip-permissions') {
  throw 'Canary must not bypass all Claude permissions'
}

foreach ($identityPath in @(
  (Join-Path (Split-Path -Parent $PSScriptRoot) 'AGENTS.md'),
  (Join-Path (Split-Path -Parent $PSScriptRoot) 'CLAUDE.md'),
  (Join-Path (Split-Path -Parent $PSScriptRoot) '开发管理\AI协作规则.txt')
)) {
  $identityText = Get-Content -LiteralPath $identityPath -Raw
  if (
    -not $identityText.Contains('.claude/settings.json', [StringComparison]::Ordinal) `
    -or -not $identityText.Contains('实际生效', [StringComparison]::Ordinal) `
    -or -not $identityText.Contains('127.0.0.1:15721', [StringComparison]::Ordinal)
  ) {
    throw "Identity contract does not use effective Claude settings: $identityPath"
  }
}

Write-Output 'test-external-ai-self-commit-contract: OK'
