$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\automation-workspace-guard.ps1'
$engine = (Get-Process -Id $PID).Path
$tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
$sandbox = Join-Path $tempRoot ("tzg-workspace-guard-test-" + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $sandbox 'repo'
$baseline = Join-Path $sandbox 'baseline.json'
$cleanBaseline = Join-Path $sandbox 'clean-baseline.json'
$safeToRemove = $false

function Invoke-Guard {
  param([string[]]$Arguments)

  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    $output = & $engine -NoProfile -ExecutionPolicy Bypass -File $tool @Arguments 2>&1
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output -join "`n") }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
}

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  $output = & git -C $repo @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "git $($Arguments -join ' ') failed: $($output -join "`n")"
  }
  $output
}

function Assert-Code {
  param($Result, [int]$Expected, [string]$Label)

  if ($Result.Code -ne $Expected) {
    throw "$Label expected exit $Expected but got $($Result.Code): $($Result.Output)"
  }
}

function Assert-JsonSafe {
  param($Result, [bool]$Expected, [string[]]$Conflicts, [string]$Label)

  $json = $Result.Output | ConvertFrom-Json
  if ([bool]$json.safe -ne $Expected) {
    throw "$Label returned unexpected safe value: $($Result.Output)"
  }
  if ($null -ne $Conflicts) {
    $actual = @($json.conflictingPaths)
    foreach ($path in $Conflicts) {
      if ($actual -notcontains $path) {
        throw "$Label did not report conflict '$path': $($Result.Output)"
      }
    }
  }
}

function Get-FileHashText {
  param([string]$Path)

  (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

New-Item -ItemType Directory -Path $repo -Force | Out-Null
$resolvedTemp = (Resolve-Path -LiteralPath $tempRoot).Path.TrimEnd('\', '/')
$resolvedRepo = (Resolve-Path -LiteralPath $repo).Path
$prefix = $resolvedTemp + [System.IO.Path]::DirectorySeparatorChar
if (-not $resolvedRepo.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
  throw "Refusing unsafe fixture path outside temp root: $resolvedRepo"
}
$safeToRemove = $true

try {
  Invoke-Git init | Out-Null
  Invoke-Git config user.name 'Workspace Guard Test' | Out-Null
  Invoke-Git config user.email 'workspace-guard@example.invalid' | Out-Null

  $initial = @{
    'human.txt' = "human base`n"
    'staged.txt' = "staged base`n"
    'task.txt' = "task base`n"
    'renamed.txt' = "rename base`n"
    'deleted.txt' = "delete base`n"
    'src/Assets/clean.txt' = "directory base`n"
    '中文 space.txt' = "unicode base`n"
    ' leading.txt' = "leading-space base`n"
  }
  foreach ($item in $initial.GetEnumerator()) {
    $path = Join-Path $repo $item.Key
    $parent = Split-Path -Parent $path
    if (-not (Test-Path -LiteralPath $parent)) {
      New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($path, $item.Value, [System.Text.UTF8Encoding]::new($false))
  }
  Invoke-Git add -- . | Out-Null
  Invoke-Git commit -m 'test: initial fixture' | Out-Null

  $cleanHead = (Invoke-Git rev-parse HEAD) -join ''
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $repo, '-BaselinePath', $cleanBaseline)
  Assert-Code $r 0 'clean repository snapshot'
  if (-not (Test-Path -LiteralPath $cleanBaseline)) { throw 'clean repository snapshot did not create a baseline' }
  $cleanSnapshot = Get-Content -Raw -LiteralPath $cleanBaseline | ConvertFrom-Json
  if ($cleanSnapshot.schemaVersion -ne 1 -or $cleanSnapshot.repositoryRoot -ne $resolvedRepo -or $cleanSnapshot.head -ne $cleanHead -or @($cleanSnapshot.entries).Count -ne 0) {
    throw 'clean repository snapshot metadata or empty entries were incorrect'
  }

  [System.IO.File]::AppendAllText((Join-Path $repo 'human.txt'), "human unstaged`n")
  [System.IO.File]::AppendAllText((Join-Path $repo 'staged.txt'), "human staged`n")
  Invoke-Git add -- staged.txt | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $repo 'untracked.txt'), "human untracked`n", [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::WriteAllText((Join-Path $repo '未跟踪 空格.txt'), "unicode untracked`n", [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::AppendAllText((Join-Path $repo ' leading.txt'), "leading-space dirty`n")
  Invoke-Git mv -- renamed.txt renamed-by-human.txt | Out-Null
  Remove-Item -LiteralPath (Join-Path $repo 'deleted.txt')
  [System.IO.File]::AppendAllText((Join-Path $repo 'src\Assets\clean.txt'), "directory dirty`n")

  $humanHash = Get-FileHashText (Join-Path $repo 'human.txt')
  $stagedHash = Get-FileHashText (Join-Path $repo 'staged.txt')
  $untrackedHash = Get-FileHashText (Join-Path $repo 'untracked.txt')
  $unicodeHash = Get-FileHashText (Join-Path $repo '未跟踪 空格.txt')
  $renamedHash = Get-FileHashText (Join-Path $repo 'renamed-by-human.txt')

  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $repo, '-BaselinePath', $baseline)
  Assert-Code $r 0 'snapshot'
  $snapshot = Get-Content -Raw -LiteralPath $baseline | ConvertFrom-Json
  if ($snapshot.schemaVersion -ne 1 -or $snapshot.repositoryRoot -ne $resolvedRepo -or -not $snapshot.head) {
    throw 'snapshot metadata was not persisted correctly'
  }
  $entryPaths = @($snapshot.entries.path)
  foreach ($required in @('human.txt', 'staged.txt', 'untracked.txt', '未跟踪 空格.txt', ' leading.txt', 'renamed.txt', 'renamed-by-human.txt', 'deleted.txt', 'src/Assets/clean.txt')) {
    if ($entryPaths -notcontains $required) { throw "snapshot omitted '$required'" }
  }
  $snapshotBytes = [System.IO.File]::ReadAllBytes($baseline)
  if ($snapshotBytes.Length -ge 3 -and $snapshotBytes[0] -eq 0xEF -and $snapshotBytes[1] -eq 0xBB -and $snapshotBytes[2] -eq 0xBF) {
    throw 'snapshot JSON must be UTF-8 without BOM'
  }
  $baselineHash = Get-FileHashText $baseline

  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $sandbox, '-BaselinePath', $baseline)
  if ($r.Code -eq 0) { throw 'snapshot unexpectedly accepted a non-repository root' }
  if ((Get-FileHashText $baseline) -ne $baselineHash) { throw 'failed snapshot overwrote a valid baseline' }

  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', '.\task.txt|TASK.txt')
  Assert-Code $r 0 'safe task check'
  Assert-JsonSafe $r $true @() 'safe task check'

  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', ' leading.txt| leading.txt')
  Assert-Code $r 20 'leading-space path conflict and dedupe'
  Assert-JsonSafe $r $false @(' leading.txt') 'leading-space path conflict and dedupe'
  $leadingJson = $r.Output | ConvertFrom-Json
  if (@($leadingJson.conflictingPaths).Count -ne 1 -or @($leadingJson.conflictingPaths) -contains 'leading.txt') {
    throw "leading-space path was trimmed or not deduplicated exactly: $($r.Output)"
  }

  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', '.\ leading.txt')
  Assert-Code $r 20 'leading-dot normalization preserves path whitespace'
  Assert-JsonSafe $r $false @(' leading.txt') 'leading-dot normalization preserves path whitespace'

  foreach ($case in @(
    @{ Paths = 'human.txt'; Conflicts = @('human.txt'); Label = 'unstaged conflict' },
    @{ Paths = 'staged.txt'; Conflicts = @('staged.txt'); Label = 'staged conflict' },
    @{ Paths = 'untracked.txt'; Conflicts = @('untracked.txt'); Label = 'untracked conflict' },
    @{ Paths = '未跟踪 空格.txt'; Conflicts = @('未跟踪 空格.txt'); Label = 'unicode and space conflict' },
    @{ Paths = 'renamed.txt'; Conflicts = @('renamed.txt'); Label = 'rename old-side conflict' },
    @{ Paths = 'renamed-by-human.txt'; Conflicts = @('renamed-by-human.txt'); Label = 'rename new-side conflict' },
    @{ Paths = 'deleted.txt'; Conflicts = @('deleted.txt'); Label = 'delete conflict' },
    @{ Paths = 'src/Assets'; Conflicts = @('src/Assets/clean.txt'); Label = 'directory descendant conflict' },
    @{ Paths = 'src'; Conflicts = @('src/Assets/clean.txt'); Label = 'unsafe parent conflict' },
    @{ Paths = 'src/Assets/clean.txt/child'; Conflicts = @('src/Assets/clean.txt'); Label = 'unsafe child conflict' }
  )) {
    $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', $case.Paths)
    Assert-Code $r 20 $case.Label
    Assert-JsonSafe $r $false $case.Conflicts $case.Label
  }

  foreach ($invalid in @(
    (Join-Path $repo 'task.txt'),
    '../task.txt',
    'src/../task.txt',
    'src//task.txt',
    'src/./task.txt',
    'task.txt||other.txt',
    ' '
  )) {
    $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', $invalid)
    Assert-Code $r 15 "invalid expected path '$invalid'"
  }

  [System.IO.File]::AppendAllText((Join-Path $repo 'task.txt'), "task implementation`n")
  Invoke-Git add -- task.txt | Out-Null
  Invoke-Git commit --only -m 'test: task-only commit' -- task.txt | Out-Null

  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 0 'verify isolated task commit'
  Assert-JsonSafe $r $true @() 'verify isolated task commit'

  $commitPaths = @(Invoke-Git diff-tree --no-commit-id --name-only -r HEAD)
  if ($commitPaths.Count -ne 1 -or $commitPaths[0] -ne 'task.txt') { throw "task commit included unrelated paths: $($commitPaths -join ', ')" }
  $status = (Invoke-Git status --porcelain=v1 -z) -join ''
  foreach ($fragment in @('M  staged.txt', ' M human.txt', 'R  renamed-by-human.txt', 'renamed.txt', ' D deleted.txt', '?? untracked.txt')) {
    if (-not $status.Contains($fragment)) { throw "human state missing after verify: $fragment" }
  }
  if ((Get-FileHashText (Join-Path $repo 'human.txt')) -ne $humanHash -or
      (Get-FileHashText (Join-Path $repo 'staged.txt')) -ne $stagedHash -or
      (Get-FileHashText (Join-Path $repo 'untracked.txt')) -ne $untrackedHash -or
      (Get-FileHashText (Join-Path $repo '未跟踪 空格.txt')) -ne $unicodeHash -or
      (Get-FileHashText (Join-Path $repo 'renamed-by-human.txt')) -ne $renamedHash -or
      (Test-Path -LiteralPath (Join-Path $repo 'deleted.txt'))) {
    throw 'verify changed pre-existing human file state or content'
  }

  [System.IO.File]::AppendAllText((Join-Path $repo 'human.txt'), "human changed again`n")
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify detects baseline change'
  Assert-JsonSafe $r $false @('human.txt') 'verify detects baseline change'

  [System.IO.File]::WriteAllText((Join-Path $repo 'intruder.txt'), "unexpected commit`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-Git add -- intruder.txt | Out-Null
  Invoke-Git commit --only -m 'test: unexpected middle commit' -- intruder.txt | Out-Null
  Remove-Item -LiteralPath (Join-Path $repo 'intruder.txt')
  [System.IO.File]::AppendAllText((Join-Path $repo 'task.txt'), "latest expected commit`n")
  Invoke-Git add -- intruder.txt task.txt | Out-Null
  Invoke-Git commit --only -m 'test: latest task commit deletes intruder' -- intruder.txt task.txt | Out-Null
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify scans the entire HEAD range'
  Assert-JsonSafe $r $false @('human.txt', 'intruder.txt') 'verify scans the entire HEAD range'

  'test-automation-workspace-guard: OK'
  exit 0
} finally {
  if ($safeToRemove) {
    $resolvedRepoNow = (Resolve-Path -LiteralPath $repo -ErrorAction SilentlyContinue).Path
    if ($resolvedRepoNow -and $resolvedRepoNow.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    } else {
      throw "Refusing unsafe fixture cleanup: $sandbox"
    }
  }
}
