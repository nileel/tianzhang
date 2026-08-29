#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Assert-Equal {
  param($Actual, $Expected, [string]$Message)
  if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" }
}

function Stop-Hourly {
  param([string]$Code)
  $error = [InvalidOperationException]::new($Code)
  $error.Data['DetailCode'] = $Code
  throw $error
}

function Get-ParsedAst {
  param([string]$Path)
  $tokens = $null
  $errors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
  if (@($errors).Count -ne 0) { throw "PowerShell parse failed for $Path" }
  $ast
}

function Write-Utf8 {
  param([string]$Path, [string]$Text)
  [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-ExpectedFailure {
  param([string]$FixturePath, [string]$ExpectedPaths, [string]$Context)
  $script:finalizerPath = $FixturePath
  $detailCode = $null
  try {
    $null = Invoke-Finalizer -Worktree $script:sandbox -Parameters @{ ExpectedPaths = $ExpectedPaths; CommitMessage = 'test: reject fixture' }
  } catch {
    if ($_.Exception.Data.Contains('DetailCode')) { $detailCode = [string]$_.Exception.Data['DetailCode'] }
  }
  Assert-Equal $detailCode 'hourly_formal_commit_failed' "$Context did not preserve the formal failure detailCode"
}

$ownerPath = Join-Path $PSScriptRoot 'invoke-hourly-owner.ps1'
$productionFinalizerPath = Join-Path $PSScriptRoot 'automation-finalize-commit.ps1'
$ownerAst = Get-ParsedAst $ownerPath
$finalizerDefinitions = @($ownerAst.FindAll({
  param($node)
  $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-Finalizer'
}, $true))
Assert-Equal $finalizerDefinitions.Count 1 'Invoke-Finalizer definition count changed'
$finalizerDefinition = $finalizerDefinitions[0]
$commands = @($finalizerDefinition.Body.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true))
Assert-True (-not @($commands | Where-Object { $_.GetCommandName() -ceq 'pwsh' }).Count) 'Invoke-Finalizer still launches nested pwsh'
$directCalls = @($commands | Where-Object {
  $_.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Ampersand -and
  $_.CommandElements.Count -gt 0 -and
  $_.CommandElements[0] -is [Management.Automation.Language.VariableExpressionAst] -and
  $_.CommandElements[0].VariablePath.UserPath -ceq 'finalizerPath'
})
Assert-Equal $directCalls.Count 1 'Invoke-Finalizer does not directly invoke finalizerPath exactly once'
Assert-True $finalizerDefinition.Extent.Text.Contains('*>&1') 'Invoke-Finalizer does not contain all finalizer output streams'

$rangeDiffCalls = @($ownerAst.FindAll({
  param($node)
  $node -is [Management.Automation.Language.CommandAst] -and
  $node.GetCommandName() -ceq 'Invoke-GitText' -and
  $node.Extent.Text.Contains("'diff'") -and
  $node.Extent.Text.Contains("'--check'") -and
  $node.Extent.Text.Contains('..')
}, $true))
Assert-Equal $rangeDiffCalls.Count 3 'Formal range diff --check invocation count changed'
foreach ($call in $rangeDiffCalls) {
  Assert-True $call.Extent.Text.Contains("@('-c', 'core.whitespace=-blank-at-eol', 'diff', '--check'") `
    'A formal range diff --check invocation does not use the Unity semantic whitespace configuration'
}

$combinedValidationDefinition = @($ownerAst.FindAll({
  param($node)
  $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Invoke-CombinedValidation'
}, $true))
$maintenanceDecisionDefinition = @($ownerAst.FindAll({
  param($node)
  $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Apply-TerminatedMaintenanceDecision'
}, $true))
$reviewReworkDefinition = @($ownerAst.FindAll({
  param($node)
  $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Apply-AnsweredReviewRework'
}, $true))
Assert-Equal $combinedValidationDefinition.Count 1 'Invoke-CombinedValidation definition count changed'
Assert-Equal $maintenanceDecisionDefinition.Count 1 'Apply-TerminatedMaintenanceDecision definition count changed'
Assert-Equal $reviewReworkDefinition.Count 1 'Apply-AnsweredReviewRework definition count changed'

$combinedText = $combinedValidationDefinition[0].Extent.Text
$maintenanceText = $maintenanceDecisionDefinition[0].Extent.Text
$reviewText = $reviewReworkDefinition[0].Extent.Text
Assert-True ($combinedText.IndexOf('$whitespacePath', [StringComparison]::Ordinal) -ge 0 -and
  $combinedText.IndexOf('$whitespacePath', [StringComparison]::Ordinal) -lt $combinedText.IndexOf('core.whitespace=-blank-at-eol', [StringComparison]::Ordinal)) `
  'Ordinary formal validation no longer runs the precise whitespace checker before configured git diff --check'
Assert-True ($maintenanceText.IndexOf('Invoke-Finalizer', [StringComparison]::Ordinal) -ge 0 -and
  $maintenanceText.IndexOf('Invoke-Finalizer', [StringComparison]::Ordinal) -lt $maintenanceText.IndexOf('core.whitespace=-blank-at-eol', [StringComparison]::Ordinal)) `
  'Maintenance decision validation no longer runs the finalizer before configured git diff --check'
Assert-True ($reviewText.IndexOf('$whitespacePath', [StringComparison]::Ordinal) -ge 0 -and
  $reviewText.IndexOf('$whitespacePath', [StringComparison]::Ordinal) -lt $reviewText.IndexOf('core.whitespace=-blank-at-eol', [StringComparison]::Ordinal)) `
  'Review rework validation no longer runs the precise whitespace checker before configured git diff --check'

$productionFinalizerAst = Get-ParsedAst $productionFinalizerPath
$exitStatements = @($productionFinalizerAst.FindAll({ param($node) $node -is [Management.Automation.Language.ExitStatementAst] }, $true))
Assert-Equal $exitStatements.Count 0 'Production finalizer must throw on failure and must not call exit'

. ([scriptblock]::Create($finalizerDefinition.Extent.Text))

$testId = [Guid]::NewGuid().ToString('N')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$script:sandbox = Join-Path $temporaryRoot "tzg-hourly-finalizer-invocation-test-$testId"
$successFixture = Join-Path $script:sandbox 'success.ps1'
$exitFixture = Join-Path $script:sandbox 'exit-one.ps1'
$throwFixture = Join-Path $script:sandbox 'throw.ps1'
$invalidFixture = Join-Path $script:sandbox 'invalid.ps1'

try {
  [IO.Directory]::CreateDirectory($script:sandbox) | Out-Null
  Write-Utf8 $successFixture @'
[CmdletBinding()]
param([string]$RepositoryRoot, [string]$ExpectedPaths, [string]$CommitMessage)
$PSNativeCommandUseErrorActionPreference = $false
$paths = @($ExpectedPaths.Split('|'))
if ($paths.Count -ne 648 -or $ExpectedPaths.Length -le 32767) { throw 'large expected paths were truncated' }
Write-Host 'check-pending-whitespace: OK (648 files checked; fix=False)'
& cmd /c exit 1
(('a' * 40) -join '')
'@
  Write-Utf8 $exitFixture @'
[CmdletBinding()]
param([string]$RepositoryRoot, [string]$ExpectedPaths, [string]$CommitMessage)
(('b' * 40) -join '')
exit 1
'@
  Write-Utf8 $throwFixture @'
[CmdletBinding()]
param([string]$RepositoryRoot, [string]$ExpectedPaths, [string]$CommitMessage)
throw 'fixture finalizer failed'
'@
  Write-Utf8 $invalidFixture @'
[CmdletBinding()]
param([string]$RepositoryRoot, [string]$ExpectedPaths, [string]$CommitMessage)
'not-a-commit'
'@

  $largePaths = @(0..647 | ForEach-Object { "src/Assets/Generated/$($_.ToString('D4'))-$('x' * 48).asset" })
  $largeExpectedPaths = $largePaths -join '|'
  Assert-Equal $largePaths.Count 648 'Large expected-path fixture count changed'
  Assert-True ($largeExpectedPaths.Length -gt 32767) 'Large expected-path fixture no longer exceeds the Windows command-line limit'

  $script:finalizerPath = $successFixture
  $LASTEXITCODE = 0
  $visibleOutput = @(Invoke-Finalizer -Worktree $script:sandbox -Parameters @{ ExpectedPaths = $largeExpectedPaths; CommitMessage = 'test: large in-process finalizer' } *>&1)
  Assert-Equal $visibleOutput.Count 1 'In-process finalizer leaked non-result output to the owner caller'
  $commit = [string]$visibleOutput[0]
  Assert-Equal $commit ('a' * 40) 'In-process finalizer rejected a valid SHA after an internal native exit code 1'
  Assert-Equal $LASTEXITCODE 1 'Fixture did not reproduce LASTEXITCODE contamination from inside the finalizer'

  Invoke-ExpectedFailure -FixturePath $exitFixture -ExpectedPaths $largeExpectedPaths -Context 'Explicit exit 1'
  Invoke-ExpectedFailure -FixturePath $throwFixture -ExpectedPaths $largeExpectedPaths -Context 'Thrown finalizer failure'
  Invoke-ExpectedFailure -FixturePath $invalidFixture -ExpectedPaths $largeExpectedPaths -Context 'Invalid finalizer output'

  'test-hourly-finalizer-invocation: OK'
} finally {
  if (Test-Path -LiteralPath $script:sandbox) {
    $resolvedSandbox = [IO.Path]::GetFullPath($script:sandbox)
    if (-not $resolvedSandbox.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolvedSandbox) -cne "tzg-hourly-finalizer-invocation-test-$testId") {
      throw "Unsafe finalizer test cleanup: $resolvedSandbox"
    }
    Remove-Item -LiteralPath $resolvedSandbox -Recurse -Force
  }
}
