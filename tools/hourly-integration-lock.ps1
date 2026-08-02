#requires -Version 7.0

Set-StrictMode -Version Latest

function Get-TzgIntegrationMutexName {
  param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

  $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/').ToUpperInvariant()
  $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.UTF8Encoding]::new($false).GetBytes("tzg-project-integration-v1`n$root")
  )).ToLowerInvariant()
  "Local\TZG-Project-Integration-$digest"
}

function Enter-TzgIntegrationLock {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [ValidateRange(0, 86400)][int]$TimeoutSeconds = 0
  )

  $mutex = [Threading.Mutex]::new($false, (Get-TzgIntegrationMutexName -RepositoryRoot $RepositoryRoot))
  $held = $false
  try {
    try {
      $held = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
    } catch [Threading.AbandonedMutexException] {
      $held = $true
    }
    if (-not $held) {
      $mutex.Dispose()
      return $null
    }
    [pscustomobject]@{ Mutex = $mutex; RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot) }
  } catch {
    if (-not $held) { $mutex.Dispose() }
    throw
  }
}

function Exit-TzgIntegrationLock {
  param([AllowNull()][object]$Handle)

  if ($null -eq $Handle) { return }
  try {
    $Handle.Mutex.ReleaseMutex()
  } finally {
    $Handle.Mutex.Dispose()
  }
}

function Get-TzgIntegrationLockStatus {
  param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

  $handle = Enter-TzgIntegrationLock -RepositoryRoot $RepositoryRoot -TimeoutSeconds 0
  if ($null -eq $handle) { return 'held' }
  Exit-TzgIntegrationLock -Handle $handle
  'none'
}
