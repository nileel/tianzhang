[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checker = Join-Path $repoRoot 'tools/check-asset-versioning.ps1'
if (-not (Test-Path -LiteralPath $checker -PathType Leaf)) { throw "Missing checker: $checker" }

function Assert-ExitCode {
  param([int]$Expected, [int]$Actual, [string]$Label)
  if ($Actual -ne $Expected) { throw "$Label expected exit $Expected but received $Actual." }
}

function New-Fixture {
  $fixture = Join-Path ([System.IO.Path]::GetTempPath()) ("tzg-asset-versioning-" + [guid]::NewGuid().ToString('N'))
  New-Item -ItemType Directory -Path $fixture | Out-Null
  & git -C $fixture init --quiet
  if ($LASTEXITCODE -ne 0) { throw 'git init failed.' }
  New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'src/Assets/Art') | Out-Null
  New-Item -ItemType Directory -Force -Path (Join-Path $fixture 'src/Assets/Scenes') | Out-Null
  [System.IO.File]::WriteAllBytes((Join-Path $fixture 'src/Assets/Art/icon.png'), [byte[]](0,1,2,3))
  [System.IO.File]::WriteAllText((Join-Path $fixture 'src/Assets/Scenes/Test.unity'), "%YAML 1.1`n", [System.Text.UTF8Encoding]::new($false))
  return $fixture
}

function Set-Attributes {
  param([string]$Fixture, [string]$Content)
  [System.IO.File]::WriteAllText((Join-Path $Fixture '.gitattributes'), $Content, [System.Text.UTF8Encoding]::new($false))
}

$fixtures = [System.Collections.Generic.List[string]]::new()
try {
  $fixture = New-Fixture
  $fixtures.Add($fixture) | Out-Null
  Set-Attributes $fixture "src/Assets/Art/**/*.png filter=lfs diff=lfs merge=lfs -text`n"
  $coveredOutput = @(& pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture 2>&1)
  Assert-ExitCode 0 $LASTEXITCODE 'LFS-covered binary fixture'
  if ($coveredOutput -match 'fatal:') { throw 'LFS-covered binary fixture emitted unexpected Git diagnostics.' }

  $fixture = New-Fixture
  $fixtures.Add($fixture) | Out-Null
  Set-Attributes $fixture "src/Assets/Art/**/*.wav filter=lfs diff=lfs merge=lfs -text`n"
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture 2>$null
  Assert-ExitCode 1 $LASTEXITCODE 'untracked binary fixture'

  $fixture = New-Fixture
  $fixtures.Add($fixture) | Out-Null
  Set-Attributes $fixture @"
src/Assets/Art/**/*.png filter=lfs diff=lfs merge=lfs -text
src/Assets/Scenes/**/*.unity filter=lfs diff=lfs merge=lfs -text
"@
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -ProjectRoot $fixture 2>$null
  Assert-ExitCode 1 $LASTEXITCODE 'Unity text asset fixture'
}
finally {
  foreach ($fixture in $fixtures) { Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue }
}
