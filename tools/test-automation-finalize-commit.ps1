$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-finalize-commit.ps1'
$engine = (Get-Process -Id $PID).Path
$sandbox = Join-Path ([System.IO.Path]::GetTempPath()) ('tzg-finalize-commit-test-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $sandbox 'repo'

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $output = & git -C $repo @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $($output -join "`n")" }
  $output
}

function Invoke-Helper {
  param([string]$ExpectedPaths, [string]$CommitMessage)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool `
      -RepositoryRoot $repo -ExpectedPaths $ExpectedPaths -CommitMessage $CommitMessage 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Write-Utf8 {
  param([string]$Path, [string]$Value)

  $parent = Split-Path -Parent $Path
  if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
  [System.IO.File]::WriteAllText($Path, $Value, [System.Text.UTF8Encoding]::new($false))
}

New-Item -ItemType Directory -Path $repo -Force | Out-Null
try {
  Invoke-Git init | Out-Null
  Invoke-Git config user.name 'Finalize Commit Test' | Out-Null
  Invoke-Git config user.email 'finalize-commit@example.invalid' | Out-Null
  Invoke-Git config core.quotepath false | Out-Null

  $expected = '目录/决策 状态.txt'
  $secondExpected = '目录/第二 状态.txt'
  $unrelatedStaged = 'manual-staged.txt'
  $unrelatedDirty = 'manual-dirty.txt'
  $unrelatedUntracked = 'manual-untracked.txt'
  Write-Utf8 (Join-Path $repo $expected) "expected base`n"
  Write-Utf8 (Join-Path $repo $secondExpected) "second expected base`n"
  Write-Utf8 (Join-Path $repo $unrelatedStaged) "staged base`n"
  Write-Utf8 (Join-Path $repo $unrelatedDirty) "dirty base`n"
  Invoke-Git add -- . | Out-Null
  Invoke-Git commit -m 'test: base' | Out-Null

  Write-Utf8 (Join-Path $repo $unrelatedStaged) "staged change`n"
  Invoke-Git add -- $unrelatedStaged | Out-Null
  $stagedBlobBefore = (Invoke-Git rev-parse ":$unrelatedStaged") -join ''
  Write-Utf8 (Join-Path $repo $unrelatedDirty) "dirty change`n"
  Write-Utf8 (Join-Path $repo $unrelatedUntracked) "untracked change`n"
  $dirtyHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedDirty)).Hash
  $untrackedHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedUntracked)).Hash
  Write-Utf8 (Join-Path $repo $expected) "expected change`n"

  $result = Invoke-Helper $expected 'test: expected only'
  if ($result.Code -ne 0) { throw "commit helper failed: $($result.Output)" }
  $committed = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($committed.Count -ne 1 -or $committed[0] -ne $expected) { throw "unexpected commit paths: $($committed -join ', ')" }
  if (((Invoke-Git rev-parse ":$unrelatedStaged") -join '') -ne $stagedBlobBefore) { throw 'unrelated staged blob changed' }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedDirty)).Hash -ne $dirtyHashBefore) { throw 'unrelated dirty file changed' }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repo $unrelatedUntracked)).Hash -ne $untrackedHashBefore) { throw 'unrelated untracked file changed' }

  Write-Utf8 (Join-Path $repo $expected) "expected second change`n"
  Write-Utf8 (Join-Path $repo $secondExpected) "second expected change`n"
  $multiResult = Invoke-Helper "$expected|$secondExpected" 'test: multiple expected paths'
  if ($multiResult.Code -ne 0) { throw "commit helper failed for multiple paths: $($multiResult.Output)" }
  $multiCommitted = @(Invoke-Git show --format= --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($multiCommitted.Count -ne 2 -or $multiCommitted -notcontains $expected -or $multiCommitted -notcontains $secondExpected) {
    throw "unexpected multi-path commit: $($multiCommitted -join ', ')"
  }
  if (((Invoke-Git rev-parse ":$unrelatedStaged") -join '') -ne $stagedBlobBefore) { throw 'multi-path commit changed unrelated staged blob' }

  $headBeforeMissing = (Invoke-Git rev-parse HEAD) -join ''
  $missing = Invoke-Helper 'missing.txt' 'test: must not commit'
  if ($missing.Code -eq 0) { throw 'missing expected path was accepted' }
  if (((Invoke-Git rev-parse HEAD) -join '') -ne $headBeforeMissing) { throw 'missing expected path created a commit' }

  'test-automation-finalize-commit: OK'
} finally {
  if (Test-Path -LiteralPath $sandbox) { Remove-Item -LiteralPath $sandbox -Recurse -Force }
}
