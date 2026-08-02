#requires -Version 7.0

$ErrorActionPreference = 'Stop'
function Assert-True { param([bool]$Value, [string]$Message) if (-not $Value) { throw $Message } }
function Assert-Equal { param($Actual, $Expected, [string]$Message) if ($Actual -ne $Expected) { throw "$Message (expected=$Expected actual=$Actual)" } }
function Write-Utf8 { param([string]$Path,[string]$Text) [IO.Directory]::CreateDirectory((Split-Path -Parent $Path))|Out-Null; [IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false)) }

$root = Join-Path ([IO.Path]::GetTempPath()) "tzg-integration-lock-test-$([Guid]::NewGuid().ToString('N'))"
$holdScript = Join-Path $root 'hold.ps1'
$readyPath = Join-Path $root 'ready.txt'
$modulePath = Join-Path $PSScriptRoot 'hourly-integration-lock.ps1'
$entryPath = Join-Path $PSScriptRoot 'invoke-project-integration.ps1'
try {
  [IO.Directory]::CreateDirectory($root) | Out-Null
  & git -C $root init -q; & git -C $root config user.name 'Integration Test'; & git -C $root config user.email 'integration@example.invalid'
  Write-Utf8 (Join-Path $root 'seed.txt') 'seed'; & git -C $root add seed.txt; & git -C $root commit -q -m 'test: seed'
  $base = [string](& git -C $root rev-parse HEAD)
  & git -C $root switch -q -c formal; Write-Utf8 (Join-Path $root 'result.txt') 'formal'; & git -C $root add result.txt; & git -C $root commit -q -m 'test: formal'; $target=[string](& git -C $root rev-parse HEAD); & git -C $root switch -q master

  $hold = @"
. '$modulePath'
`$handle = Enter-TzgIntegrationLock -RepositoryRoot '$root' -TimeoutSeconds 0
[IO.File]::WriteAllText('$readyPath','ready')
Start-Sleep -Seconds 4
Exit-TzgIntegrationLock -Handle `$handle
"@
  Write-Utf8 $holdScript $hold
  $process = Start-Process pwsh -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',$holdScript) -WindowStyle Hidden -PassThru
  $deadline = [DateTime]::UtcNow.AddSeconds(3)
  while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
  Assert-True (Test-Path -LiteralPath $readyPath) 'Lock holder did not start'
  $busy = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $entryPath -RepositoryRoot $root -ExpectedMainHead $base -TargetCommit $target -ExpectedPaths 'result.txt' 2>$null)
  Assert-Equal $LASTEXITCODE 2 'Concurrent integration was not rejected'
  Assert-Equal (($busy[0] | ConvertFrom-Json).status) 'occupied' 'Concurrent integration returned the wrong status'
  $process.WaitForExit()

  $integrated = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $entryPath -RepositoryRoot $root -ExpectedMainHead $base -TargetCommit $target -ExpectedPaths 'result.txt' 2>$null)
  Assert-Equal $LASTEXITCODE 0 'Fast-forward integration failed'
  Assert-Equal ([string](& git -C $root rev-parse HEAD)) $target 'Integration did not reach target'

  & git -C $root switch -q -c formal-two; Write-Utf8 (Join-Path $root 'result.txt') 'formal-two'; & git -C $root add result.txt; & git -C $root commit -q -m 'test: formal two'; $targetTwo=[string](& git -C $root rev-parse HEAD); & git -C $root switch -q master
  Write-Utf8 (Join-Path $root 'result.txt') 'manual-conflict'
  $before = [string](& git -C $root rev-parse HEAD)
  $conflict = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $entryPath -RepositoryRoot $root -ExpectedMainHead $before -TargetCommit $targetTwo -ExpectedPaths 'result.txt' 2>$null)
  Assert-Equal $LASTEXITCODE 1 'Conflicting integration was accepted'
  Assert-Equal ([string](& git -C $root rev-parse HEAD)) $before 'Conflict changed main HEAD'
  Assert-Equal ([IO.File]::ReadAllText((Join-Path $root 'result.txt'))) 'manual-conflict' 'Conflict overwrote manual work'
  Write-Output 'test-hourly-integration-lock: OK'
} finally {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
