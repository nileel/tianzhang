#requires -Version 7.0

Set-StrictMode -Version Latest

$script:RegisteredChecks = [ordered]@{
  'data-chain' = 'tools/check-data-chain.ps1'
  'unity-editmode-related' = 'tools/run-unity-editmode-tests.ps1'
  'pending-whitespace' = $null
  'cached-diff-check' = $null
}
$script:DelegatedChecks = @('pending-whitespace', 'cached-diff-check')
$script:ForbiddenDiagnosticFields = @(
  'appSecret', 'tenantKey', 'openId', 'chatId', 'messageId', 'eventId',
  'providerMessageId', 'providerEventId', 'evidenceHash', 'rawEvent'
)

function Throw-VerificationError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message,
    [string[]]$ChangedPaths = @(),
    [AllowNull()][string]$Diagnostic = $null
  )

  $exception = [InvalidOperationException]::new("$Code`: $Message")
  $exception.Data['errorCode'] = $Code
  $exception.Data['changedPaths'] = [string[]]@($ChangedPaths)
  if ($null -ne $Diagnostic) {
    $exception.Data['diagnostic'] = $Diagnostic
  }
  throw $exception
}

function ConvertTo-SanitizedVerificationDiagnostic {
  param([AllowNull()][string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    return ''
  }
  $normalized = (($Value -replace "`r`n?", "`n") -replace '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]', '').Trim()
  foreach ($field in $script:ForbiddenDiagnosticFields) {
    if ($normalized.IndexOf($field, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
      return '[REDACTED]'
    }
  }
  if ($normalized.Length -gt 512) {
    return $normalized.Substring(0, 512)
  }
  $normalized
}

function Resolve-VerificationRepository {
  param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

  if (-not [IO.Path]::IsPathFullyQualified($RepositoryRoot) -or
      -not (Test-Path -LiteralPath $RepositoryRoot -PathType Container)) {
    Throw-VerificationError -Code 'invalid_request' -Message 'repository root must be an existing absolute directory'
  }
  $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
  $gitRoot = @(& git -C $root rev-parse --show-toplevel 2>&1)
  if ($LASTEXITCODE -ne 0 -or $gitRoot.Count -ne 1 -or
      -not [IO.Path]::GetFullPath(([string]$gitRoot[0]).Trim()).TrimEnd('\', '/').Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
    Throw-VerificationError -Code 'invalid_request' -Message 'repository root must be the Git root'
  }
  $root
}

function Invoke-FixedProcess {
  param(
    [Parameter(Mandatory = $true)][string]$FileName,
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$WorkingDirectory
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = $FileName
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  try {
    if (-not $process.Start()) {
      Throw-VerificationError -Code 'internal_error' -Message 'fixed verification process did not start'
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    [pscustomobject][ordered]@{
      exitCode = $process.ExitCode
      stdout = $stdoutTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
      stderr = $stderrTask.GetAwaiter().GetResult().TrimEnd("`r", "`n")
    }
  } finally {
    $process.Dispose()
  }
}

function Invoke-RegisteredChecks {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string[]]$RequiredChecks
  )

  $root = Resolve-VerificationRepository -RepositoryRoot $RepositoryRoot
  if ($RequiredChecks.Count -eq 0 -or ($RequiredChecks | Select-Object -Unique).Count -ne $RequiredChecks.Count) {
    Throw-VerificationError -Code 'manifest_invalid' -Message 'required checks must be a non-empty unique list'
  }
  foreach ($check in $RequiredChecks) {
    if ($check -cnotin @($script:RegisteredChecks.Keys)) {
      Throw-VerificationError -Code 'manifest_invalid' -Message "unregistered check: $check"
    }
  }
  $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
  if ($null -eq $pwsh) {
    Throw-VerificationError -Code 'internal_error' -Message 'PowerShell 7 is unavailable'
  }
  $evidence = @()
  foreach ($check in $RequiredChecks) {
    if ($check -cin $script:DelegatedChecks) {
      continue
    }
    $relative = [string]$script:RegisteredChecks[$check]
    $path = Join-Path $root ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
      Throw-VerificationError -Code 'internal_error' -Message "registered check script is missing: $check"
    }
    $result = Invoke-FixedProcess -FileName $pwsh.Source -Arguments @(
      '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $path
    ) -WorkingDirectory $root
    $combined = @($result.stdout, $result.stderr | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n"
    $summary = ConvertTo-SanitizedVerificationDiagnostic -Value $combined
    if ($result.exitCode -ne 0) {
      Throw-VerificationError -Code 'check_failed' -Message "registered check failed: $check" -Diagnostic $summary
    }
    $evidence += [pscustomobject][ordered]@{
      checkId = $check
      status = 'PASSED'
      summary = $summary
    }
  }
  @($evidence)
}

function Read-GuardJson {
  param([Parameter(Mandatory = $true)]$ProcessResult)

  $lines = @(([string]$ProcessResult.stdout) -split '\r?\n' | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) })
  if ($lines.Count -ne 1) {
    Throw-VerificationError -Code 'internal_error' -Message 'workspace guard returned invalid stdout'
  }
  try {
    $lines[0] | ConvertFrom-Json
  } catch {
    Throw-VerificationError -Code 'internal_error' -Message 'workspace guard returned invalid JSON'
  }
}

function Invoke-WorkspaceGuard {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][ValidateSet('Snapshot', 'Check', 'Verify', 'CaptureInterruptionEvidence')][string]$Action,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$BaselinePath,
    [string[]]$ExpectedPaths = @(),
    [AllowNull()][string]$EvidencePath = $null
  )

  $root = Resolve-VerificationRepository -RepositoryRoot $RepositoryRoot
  $guardPath = Join-Path $root 'tools\automation-workspace-guard.ps1'
  if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
    Throw-VerificationError -Code 'internal_error' -Message 'workspace guard is missing'
  }
  $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
  if ($null -eq $pwsh) {
    Throw-VerificationError -Code 'internal_error' -Message 'PowerShell 7 is unavailable'
  }
  $arguments = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $guardPath, $Action,
    '-RepositoryRoot', $root, '-BaselinePath', ([IO.Path]::GetFullPath($BaselinePath))
  )
  if ($Action -cne 'Snapshot') {
    if ($ExpectedPaths.Count -eq 0) {
      Throw-VerificationError -Code 'invalid_request' -Message 'workspace guard expected paths are required'
    }
    $arguments += @('-ExpectedPaths', ($ExpectedPaths -join '|'))
  }
  if ($Action -ceq 'CaptureInterruptionEvidence') {
    if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
      Throw-VerificationError -Code 'invalid_request' -Message 'interruption evidence path is required'
    }
    $arguments += @('-EvidencePath', ([IO.Path]::GetFullPath($EvidencePath)))
  }
  $processResult = Invoke-FixedProcess -FileName $pwsh.Source -Arguments $arguments -WorkingDirectory $root
  if ($Action -ceq 'Snapshot') {
    if ($processResult.exitCode -ne 0) {
      Throw-VerificationError -Code 'baseline_changed' -Message 'workspace snapshot failed'
    }
    return [pscustomobject][ordered]@{ safe = $true; conflictingPaths = @(); reason = $null }
  }
  $payload = Read-GuardJson -ProcessResult $processResult
  [pscustomobject][ordered]@{
    exitCode = [int]$processResult.exitCode
    payload = $payload
  }
}

function Invoke-GuardedFinalizer {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string[]]$ExpectedPaths,
    [Parameter(Mandatory = $true)][string]$CommitMessage
  )

  $root = Resolve-VerificationRepository -RepositoryRoot $RepositoryRoot
  $finalizerPath = Join-Path $root 'tools\automation-finalize-commit.ps1'
  if (-not (Test-Path -LiteralPath $finalizerPath -PathType Leaf)) {
    Throw-VerificationError -Code 'internal_error' -Message 'automation finalizer is missing'
  }
  if ([string]::IsNullOrWhiteSpace($CommitMessage) -or $CommitMessage -match '[\x00-\x1f\x7f]') {
    Throw-VerificationError -Code 'invalid_request' -Message 'commit message is invalid'
  }
  $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
  if ($null -eq $pwsh) {
    Throw-VerificationError -Code 'internal_error' -Message 'PowerShell 7 is unavailable'
  }
  $result = Invoke-FixedProcess -FileName $pwsh.Source -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $finalizerPath,
    '-RepositoryRoot', $root,
    '-ExpectedPaths', ($ExpectedPaths -join '|'),
    '-CommitMessage', $CommitMessage
  ) -WorkingDirectory $root
  $summary = ConvertTo-SanitizedVerificationDiagnostic -Value (@($result.stdout, $result.stderr) -join "`n")
  if ($result.exitCode -ne 0) {
    Throw-VerificationError -Code 'check_failed' -Message 'guarded finalizer failed' -Diagnostic $summary
  }
  $lines = @($result.stdout -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  $commitSha = if ($lines.Count -gt 0) { [string]$lines[-1] } else { '' }
  if ($commitSha -notmatch '^[0-9a-f]{40,64}$') {
    Throw-VerificationError -Code 'internal_error' -Message 'guarded finalizer returned no commit SHA'
  }
  [pscustomobject][ordered]@{
    commitSha = $commitSha
    delegatedChecks = @(
      [pscustomobject][ordered]@{ checkId = 'pending-whitespace'; status = 'PASSED' },
      [pscustomobject][ordered]@{ checkId = 'cached-diff-check'; status = 'PASSED' }
    )
  }
}

Export-ModuleMember -Function @(
  'Invoke-RegisteredChecks',
  'Invoke-WorkspaceGuard',
  'Invoke-GuardedFinalizer',
  'ConvertTo-SanitizedVerificationDiagnostic'
)
