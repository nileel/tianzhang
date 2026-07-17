#requires -Version 7.0

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$v2Root = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $v2Root 'discovery.psm1'
if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
  throw 'discovery.psm1 is missing'
}
Import-Module $modulePath -Force -DisableNameChecking

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ('tzg-hourly-controller-v2-discovery-' + [guid]::NewGuid().ToString('N'))
$repositoryRoot = Join-Path $sandbox 'repo'
$runRoot = Join-Path $sandbox 'private-run'
$allowedRoot = Join-Path $repositoryRoot 'src\Data'
$largeListRoot = Join-Path $repositoryRoot 'src\LargeList'
$outsideRoot = Join-Path $sandbox 'outside'
$junctionPath = Join-Path $allowedRoot 'linked'
$logPath = Join-Path $runRoot 'discovery-log.jsonl'

try {
  [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot '开发管理')) | Out-Null
  [IO.Directory]::CreateDirectory($allowedRoot) | Out-Null
  [IO.Directory]::CreateDirectory($largeListRoot) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'docs')) | Out-Null
  [IO.Directory]::CreateDirectory((Join-Path $repositoryRoot 'tools')) | Out-Null
  [IO.Directory]::CreateDirectory($runRoot) | Out-Null
  [IO.Directory]::CreateDirectory($outsideRoot) | Out-Null

  Write-TestUtf8 -Path (Join-Path $repositoryRoot '开发管理\事实源.txt') -Value "required source`n"
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'docs\denied.txt') -Value "denied`n"
  Write-TestUtf8 -Path (Join-Path $allowedRoot 'alpha.txt') -Value "alpha`nneedle one`n"
  Write-TestUtf8 -Path (Join-Path $allowedRoot 'beta.txt') -Value "beta`nneedle two`n"
  Write-TestUtf8 -Path (Join-Path $outsideRoot 'secret.txt') -Value "secret`n"
  Write-TestUtf8 -Path (Join-Path $allowedRoot 'many.txt') -Value (((1..600 | ForEach-Object { "needle $_" }) -join "`n") + "`n")
  Write-TestUtf8 -Path (Join-Path $allowedRoot 'large.txt') -Value ('x' * (1MB + 17))
  for ($index = 0; $index -lt 5001; $index++) {
    [IO.File]::WriteAllText((Join-Path $largeListRoot ("item-{0:D4}.txt" -f $index)), '', [Text.UTF8Encoding]::new($false))
  }
  Write-TestUtf8 -Path (Join-Path $repositoryRoot 'tools\check-data-chain.ps1') -Value @'
#requires -Version 7.0
Write-Output 'data-chain fixture: OK'
'@

  $context = [pscustomobject]@{
    repositoryRoot = $repositoryRoot
    runRoot = $runRoot
    requiredSources = @('开发管理/事实源.txt')
    allowedRoots = @('src/Data', 'src/LargeList')
    discoveryChecks = @('data-chain-readonly')
  }

  $requiredRead = Invoke-DiscoverRead -Context $context -Path '开发管理/事实源.txt'
  Assert-Equal $requiredRead.path '开发管理/事实源.txt' 'required source read path'
  Assert-Equal $requiredRead.content "required source`n" 'required source read content'
  Assert-False ([bool]$requiredRead.truncated) 'required source read truncation'

  $allowedRead = Invoke-DiscoverRead -Context $context -Path 'src/Data/alpha.txt'
  Assert-Equal $allowedRead.path 'src/Data/alpha.txt' 'allowed root read path'
  Assert-True ([bool]($allowedRead.sha256 -match '^[0-9a-f]{64}$')) 'read source SHA-256'

  $largeRead = Invoke-DiscoverRead -Context $context -Path 'src/Data/large.txt'
  Assert-True ([bool]$largeRead.truncated) 'large read truncation flag'
  Assert-Equal ([Text.UTF8Encoding]::new($false).GetByteCount([string]$largeRead.content)) 1MB 'large read byte limit'

  foreach ($deniedPath in @('docs/denied.txt', '../outside/secret.txt', (Join-Path $repositoryRoot 'docs\denied.txt'))) {
    Assert-Throws `
      -Script { Invoke-DiscoverRead -Context $context -Path $deniedPath } `
      -MessageLike 'discovery_denied' `
      -Label "denied discovery read $deniedPath"
  }

  $search = Invoke-DiscoverSearch -Context $context -Root 'src/Data' -Pattern 'needle' -Glob '*.txt'
  Assert-True ([bool]$search.truncated) 'search truncation flag'
  Assert-Equal @($search.items).Count 500 'search result limit'
  Assert-True ([bool](@($search.items | Where-Object { $_.path -ceq 'src/Data/alpha.txt' }).Count -ge 1)) 'search allowed path result'

  $list = Invoke-DiscoverList -Context $context -Root 'src/LargeList' -Glob '*.txt'
  Assert-True ([bool]$list.truncated) 'list truncation flag'
  Assert-Equal @($list.items).Count 5000 'list result limit'
  Assert-Equal $list.items[0] 'src/LargeList/item-0000.txt' 'list deterministic order'

  $check = Invoke-DiscoverCheck -Context $context -CheckId 'data-chain-readonly'
  Assert-Equal $check.checkId 'data-chain-readonly' 'registered discovery check id'
  Assert-Equal $check.exitCode 0 'registered discovery check exit code'
  Assert-True ([bool]$check.output.Contains('data-chain fixture: OK')) 'registered discovery check output'

  Assert-Throws `
    -Script { Invoke-DiscoverCheck -Context $context -CheckId 'unknown-check' } `
    -MessageLike 'discovery_denied' `
    -Label 'unknown discovery check'

  $beforeCommandAttempt = @([IO.File]::ReadAllLines($logPath)).Count
  Assert-Throws `
    -Script { Invoke-DiscoverCheck -Context $context -CheckId 'data-chain-readonly' -Command 'Get-ChildItem' } `
    -MessageLike 'parameter' `
    -Label 'arbitrary command parameter'
  Assert-Equal @([IO.File]::ReadAllLines($logPath)).Count $beforeCommandAttempt 'arbitrary command generated no evidence'

  New-Item -ItemType Junction -Path $junctionPath -Target $outsideRoot -ErrorAction Stop | Out-Null
  Assert-Throws `
    -Script { Invoke-DiscoverRead -Context $context -Path 'src/Data/linked/secret.txt' } `
    -MessageLike 'discovery_denied' `
    -Label 'junction discovery escape'

  $beforeDirectShell = @([IO.File]::ReadAllLines($logPath)).Count
  Get-ChildItem -LiteralPath $allowedRoot | Out-Null
  Assert-Equal @([IO.File]::ReadAllLines($logPath)).Count $beforeDirectShell 'direct Get-ChildItem generated no discovery evidence'

  $entries = @([IO.File]::ReadAllLines($logPath) | ForEach-Object { $_ | ConvertFrom-Json })
  for ($index = 0; $index -lt $entries.Count; $index++) {
    Assert-Equal $entries[$index].sequence ($index + 1) "discovery log sequence $index"
  }
  $successfulRequiredSource = @($entries | Where-Object {
      $_.ok -and $_.action -ceq 'DiscoverRead' -and $_.input.path -ceq '开发管理/事实源.txt'
    })
  Assert-Equal $successfulRequiredSource.Count 1 'required source discovery evidence'
  Assert-True ([bool]($successfulRequiredSource[0].sourceSha256 -match '^[0-9a-f]{64}$')) 'required source log hash'
  $failedEntries = @($entries | Where-Object { -not $_.ok })
  Assert-True ([bool]($failedEntries.Count -ge 5)) 'failed discovery log entries'
  Assert-True ([bool](@($failedEntries | Where-Object { $_.errorCode -ceq 'discovery_denied' }).Count -ge 5)) 'failed discovery error code'

  Write-Output 'discovery.tests: OK'
} finally {
  if (Test-Path -LiteralPath $junctionPath) {
    Remove-Item -LiteralPath $junctionPath -Force
  }
  $resolvedSandbox = [IO.Path]::GetFullPath($sandbox)
  if ($resolvedSandbox.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
      (Test-Path -LiteralPath $resolvedSandbox)) {
    [IO.Directory]::Delete($resolvedSandbox, $true)
  }
}
