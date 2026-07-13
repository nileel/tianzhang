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

function Invoke-GitAt {
  param([string]$Repository, [string[]]$Arguments)

  $output = & git -C $Repository @Arguments 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "git $($Arguments -join ' ') failed: $($output -join "`n")"
  }
  $output
}

function Invoke-Git {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

  Invoke-GitAt $repo $Arguments
}

function Invoke-GitIndexInfo {
  param([string]$Repository, [string[]]$Records)

  $payload = [System.Text.UTF8Encoding]::new($false).GetBytes(([string]::Join([char]0, $Records) + [char]0))
  $startInfo = [System.Diagnostics.ProcessStartInfo]::new('git')
  $startInfo.WorkingDirectory = $Repository
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.ArgumentList.Add('update-index')
  $startInfo.ArgumentList.Add('-z')
  $startInfo.ArgumentList.Add('--index-info')
  $process = [System.Diagnostics.Process]::Start($startInfo)
  try {
    $process.StandardInput.BaseStream.Write($payload, 0, $payload.Length)
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "git update-index --index-info failed: $stderr$stdout" }
  } finally {
    $process.Dispose()
  }
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
  foreach ($property in @('safe', 'expectedPaths', 'conflictingPaths')) {
    if ($json.PSObject.Properties.Name -notcontains $property) { throw "$Label omitted JSON property '$property': $($Result.Output)" }
  }
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
  if (-not $Expected -and [string]::IsNullOrWhiteSpace([string]$json.reason)) {
    throw "$Label omitted a stable failure reason: $($Result.Output)"
  }
}

function Get-FileHashText {
  param([string]$Path)

  (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
}

function Get-TestBaselinePayloadHash {
  param($Baseline)

  $entries = @($Baseline.entries | ForEach-Object {
    [pscustomobject][ordered]@{
      path = [string]$_.path
      kind = [string]$_.kind
      indexStatus = [string]$_.indexStatus
      worktreeStatus = [string]$_.worktreeStatus
      indexBlob = if ($null -eq $_.indexBlob) { $null } else { [string]$_.indexBlob }
      worktreeHash = if ($null -eq $_.worktreeHash) { $null } else { [string]$_.worktreeHash }
      statusFingerprint = [string]$_.statusFingerprint
    }
  })
  $payload = [pscustomobject][ordered]@{
    repositoryRoot = [string]$Baseline.repositoryRoot
    head = [string]$Baseline.head
    entries = $entries
  } | ConvertTo-Json -Depth 8 -Compress
  $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($payload)
  [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
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
  if ($cleanSnapshot.schemaVersion -ne 2 -or $cleanSnapshot.repositoryRoot -ne $resolvedRepo -or $cleanSnapshot.head -ne $cleanHead -or @($cleanSnapshot.entries).Count -ne 0 -or [string]$cleanSnapshot.payloadHash -notmatch '^[0-9a-f]{64}$') {
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
  if ($snapshot.schemaVersion -ne 2 -or $snapshot.repositoryRoot -ne $resolvedRepo -or -not $snapshot.head -or [string]$snapshot.payloadHash -notmatch '^[0-9a-f]{64}$') {
    throw 'snapshot metadata was not persisted correctly'
  }
  $entryPaths = @($snapshot.entries.path)
  foreach ($required in @('human.txt', 'staged.txt', 'untracked.txt', '未跟踪 空格.txt', ' leading.txt', 'renamed.txt', 'renamed-by-human.txt', 'deleted.txt', 'src/Assets/clean.txt')) {
    if ($entryPaths -notcontains $required) { throw "snapshot omitted '$required'" }
  }
  foreach ($entry in @($snapshot.entries)) {
    if ([string]$entry.statusFingerprint -notmatch '^[0-9a-f]{64}$') { throw "snapshot entry omitted a raw status fingerprint: $($entry.path)" }
  }
  $snapshotBytes = [System.IO.File]::ReadAllBytes($baseline)
  if ($snapshotBytes.Length -ge 3 -and $snapshotBytes[0] -eq 0xEF -and $snapshotBytes[1] -eq 0xBB -and $snapshotBytes[2] -eq 0xBF) {
    throw 'snapshot JSON must be UTF-8 without BOM'
  }
  $baselineHash = Get-FileHashText $baseline

  $tamperedBaseline = Join-Path $sandbox 'tampered-baseline.json'
  $tampered = Get-Content -Raw -LiteralPath $baseline | ConvertFrom-Json
  $tampered.entries = @()
  [System.IO.File]::WriteAllText($tamperedBaseline, ($tampered | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $tamperedBaseline, '-ExpectedPaths', 'task.txt')
  if ($r.Code -eq 0) { throw "tampered baseline was accepted: $($r.Output)" }
  $tamperedResult = $r.Output | ConvertFrom-Json
  if ([bool]$tamperedResult.safe -ne $false -or $tamperedResult.reason -ne 'baseline_invalid' -or @($tamperedResult.expectedPaths).Count -ne 1 -or $tamperedResult.expectedPaths[0] -ne 'task.txt') {
    throw "tampered baseline rejection was not structured: $($r.Output)"
  }

  $schema1Baseline = Join-Path $sandbox 'schema1-baseline.json'
  $legacy = Get-Content -Raw -LiteralPath $baseline | ConvertFrom-Json
  $legacy.schemaVersion = 1
  [System.IO.File]::WriteAllText($schema1Baseline, ($legacy | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $schema1Baseline, '-ExpectedPaths', 'task.txt')
  if ($r.Code -eq 0) { throw 'schema1 baseline was accepted' }
  $legacyResult = $r.Output | ConvertFrom-Json
  if ($legacyResult.reason -ne 'baseline_invalid') { throw "schema1 rejection was not structured: $($r.Output)" }

  $taskBeforeCas = [System.IO.File]::ReadAllText((Join-Path $repo 'task.txt'))
  [System.IO.File]::AppendAllText((Join-Path $repo 'task.txt'), "changed after snapshot`n")
  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'check CAS detects expected path change'
  Assert-JsonSafe $r $false @('task.txt') 'check CAS detects expected path change'
  $casExpected = $r.Output | ConvertFrom-Json
  if ($casExpected.reason -ne 'baseline_changed' -or @($casExpected.expectedPaths).Count -ne 1 -or $casExpected.expectedPaths[0] -ne 'task.txt') {
    throw "expected-path CAS failure was not structured: $($r.Output)"
  }
  [System.IO.File]::WriteAllText((Join-Path $repo 'task.txt'), $taskBeforeCas, [System.Text.UTF8Encoding]::new($false))

  $humanBeforeCas = [System.IO.File]::ReadAllText((Join-Path $repo 'human.txt'))
  [System.IO.File]::AppendAllText((Join-Path $repo 'human.txt'), "changed again after snapshot`n")
  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'check CAS detects unrelated path change'
  Assert-JsonSafe $r $false @('human.txt') 'check CAS detects unrelated path change'
  [System.IO.File]::WriteAllText((Join-Path $repo 'human.txt'), $humanBeforeCas, [System.Text.UTF8Encoding]::new($false))

  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $sandbox, '-BaselinePath', $baseline)
  if ($r.Code -eq 0) { throw 'snapshot unexpectedly accepted a non-repository root' }
  if ((Get-FileHashText $baseline) -ne $baselineHash) { throw 'failed snapshot overwrote a valid baseline' }

  $r = Invoke-Guard @('Check', '-RepositoryRoot', $repo, '-BaselinePath', $baseline, '-ExpectedPaths', '.\task.txt|TASK.txt')
  Assert-Code $r 0 'safe task check'
  Assert-JsonSafe $r $true @() 'safe task check'
  $safeTaskJson = $r.Output | ConvertFrom-Json
  if (@($safeTaskJson.expectedPaths).Count -ne 1 -or $safeTaskJson.expectedPaths[0] -ne 'task.txt') {
    throw "safe output did not return normalized IgnoreCase-deduplicated expected paths: $($r.Output)"
  }

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

  $conflictRepo = Join-Path $sandbox 'unmerged-repo'
  New-Item -ItemType Directory -Path $conflictRepo | Out-Null
  Invoke-GitAt $conflictRepo @('init') | Out-Null
  Invoke-GitAt $conflictRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $conflictRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $conflictRepo 'conflict.txt'), "base`n", [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::WriteAllText((Join-Path $conflictRepo 'task.txt'), "task`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $conflictRepo @('add', '--', 'conflict.txt', 'task.txt') | Out-Null
  Invoke-GitAt $conflictRepo @('commit', '-m', 'test: conflict base') | Out-Null
  $mainBranch = (Invoke-GitAt $conflictRepo @('branch', '--show-current')) -join ''
  Invoke-GitAt $conflictRepo @('branch', 'side') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $conflictRepo 'conflict.txt'), "main`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $conflictRepo @('commit', '-am', 'test: main conflict') | Out-Null
  Invoke-GitAt $conflictRepo @('switch', 'side') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $conflictRepo 'conflict.txt'), "side`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $conflictRepo @('commit', '-am', 'test: side conflict') | Out-Null
  Invoke-GitAt $conflictRepo @('switch', $mainBranch) | Out-Null
  $previousPreference = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  try {
    & git -C $conflictRepo merge side 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { throw 'unmerged fixture did not conflict' }
  } finally {
    $ErrorActionPreference = $previousPreference
  }
  [System.IO.File]::WriteAllText((Join-Path $conflictRepo 'alternate-stage3.txt'), "alternate stage three`n", [System.Text.UTF8Encoding]::new($false))
  $alternateStage3 = @(Invoke-GitAt $conflictRepo @('hash-object', '-w', '--', 'alternate-stage3.txt') | Where-Object { $_ -match '^[0-9a-f]{40,64}$' })[-1]
  $unmergedLines = @(Invoke-GitAt $conflictRepo @('ls-files', '-u', '--', 'conflict.txt'))
  $stage1 = (($unmergedLines | Where-Object { $_ -match ' 1\tconflict\.txt$' }) -split ' ')[1]
  $stage2 = (($unmergedLines | Where-Object { $_ -match ' 2\tconflict\.txt$' }) -split ' ')[1]
  $stage3Before = (($unmergedLines | Where-Object { $_ -match ' 3\tconflict\.txt$' }) -split ' ')[1]
  if (-not $stage1 -or -not $stage2 -or -not $stage3Before -or $stage3Before -eq $alternateStage3) { throw 'unmerged fixture stages were invalid' }
  $unmergedBaseline = Join-Path $sandbox 'unmerged-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $conflictRepo, '-BaselinePath', $unmergedBaseline)
  Assert-Code $r 0 'unmerged snapshot'
  Invoke-GitIndexInfo $conflictRepo @(
    "0 0000000000000000000000000000000000000000`tconflict.txt",
    "100644 $stage1 1`tconflict.txt",
    "100644 $stage2 2`tconflict.txt",
    "100644 $alternateStage3 3`tconflict.txt"
  )
  $unmergedAfter = @(Invoke-GitAt $conflictRepo @('ls-files', '-u', '--', 'conflict.txt'))
  if (-not ($unmergedAfter -match "100644 $alternateStage3 3`tconflict.txt")) { throw 'stage-3 replacement did not take effect' }
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $conflictRepo, '-BaselinePath', $unmergedBaseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify detects stage-3-only unmerged change'
  Assert-JsonSafe $r $false @('conflict.txt') 'verify detects stage-3-only unmerged change'

  $caseRepo = Join-Path $sandbox 'case-repo'
  New-Item -ItemType Directory -Path $caseRepo | Out-Null
  Invoke-GitAt $caseRepo @('init') | Out-Null
  Invoke-GitAt $caseRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $caseRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $caseRepo 'task.txt'), "task`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $caseRepo @('add', '--', 'task.txt') | Out-Null
  Invoke-GitAt $caseRepo @('commit', '-m', 'test: case base') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $caseRepo 'lower-blob.tmp'), "lower`n", [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::WriteAllText((Join-Path $caseRepo 'upper-blob.tmp'), "upper`n", [System.Text.UTF8Encoding]::new($false))
  $lowerBlob = @(Invoke-GitAt $caseRepo @('hash-object', '-w', '--', 'lower-blob.tmp') | Where-Object { $_ -match '^[0-9a-f]{40,64}$' })[-1]
  $upperBlob = @(Invoke-GitAt $caseRepo @('hash-object', '-w', '--', 'upper-blob.tmp') | Where-Object { $_ -match '^[0-9a-f]{40,64}$' })[-1]
  Remove-Item -LiteralPath (Join-Path $caseRepo 'lower-blob.tmp'), (Join-Path $caseRepo 'upper-blob.tmp')
  Invoke-GitIndexInfo $caseRepo @("100644 $lowerBlob`tfoo.txt", "100644 $upperBlob`tFOO.txt")
  $caseBaseline = Join-Path $sandbox 'case-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $caseRepo, '-BaselinePath', $caseBaseline)
  Assert-Code $r 0 'case-sensitive index snapshot'
  $caseSnapshot = Get-Content -Raw -LiteralPath $caseBaseline | ConvertFrom-Json
  if (@($caseSnapshot.entries.path | Where-Object { $_ -ceq 'foo.txt' }).Count -ne 1 -or @($caseSnapshot.entries.path | Where-Object { $_ -ceq 'FOO.txt' }).Count -ne 1) {
    throw 'case-sensitive index paths were folded in the baseline'
  }
  $r = Invoke-Guard @('Check', '-RepositoryRoot', $caseRepo, '-BaselinePath', $caseBaseline, '-ExpectedPaths', 'foo.txt|FOO.TXT')
  Assert-Code $r 20 'case-conservative candidate conflict'
  Assert-JsonSafe $r $false @('foo.txt', 'FOO.txt') 'case-conservative candidate conflict'
  if (@(($r.Output | ConvertFrom-Json).conflictingPaths).Count -ne 2) { throw "case conflicts were folded: $($r.Output)" }

  $securityRepo = Join-Path $sandbox 'security-repo'
  $outsideRepo = Join-Path $sandbox 'junction-target'
  New-Item -ItemType Directory -Path $securityRepo, $outsideRepo | Out-Null
  Invoke-GitAt $securityRepo @('init') | Out-Null
  Invoke-GitAt $securityRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $securityRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $securityRepo '.gitignore'), "escape/`n", [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::WriteAllText((Join-Path $securityRepo 'task.txt'), "task`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $securityRepo @('add', '--', '.gitignore', 'task.txt') | Out-Null
  Invoke-GitAt $securityRepo @('commit', '-m', 'test: security base') | Out-Null
  New-Item -ItemType Junction -Path (Join-Path $securityRepo 'escape') -Target $outsideRepo | Out-Null
  $securityBaseline = Join-Path $sandbox 'security-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $securityRepo, '-BaselinePath', $securityBaseline)
  Assert-Code $r 0 'security snapshot'
  foreach ($unsafePath in @('.git', '.git/config', 'escape/child.txt')) {
    $r = Invoke-Guard @('Check', '-RepositoryRoot', $securityRepo, '-BaselinePath', $securityBaseline, '-ExpectedPaths', $unsafePath)
    Assert-Code $r 15 "unsafe expected path $unsafePath"
    Assert-JsonSafe $r $false @() "unsafe expected path $unsafePath"
    if (($r.Output | ConvertFrom-Json).reason -ne 'invalid_arguments') { throw "unsafe path reason was unstable: $($r.Output)" }
  }

  $headRepo = Join-Path $sandbox 'head-repo'
  New-Item -ItemType Directory -Path $headRepo | Out-Null
  Invoke-GitAt $headRepo @('init') | Out-Null
  Invoke-GitAt $headRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $headRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $headRepo 'task.txt'), "task`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $headRepo @('add', '--', 'task.txt') | Out-Null
  Invoke-GitAt $headRepo @('commit', '-m', 'test: head base') | Out-Null
  $headBaseline = Join-Path $sandbox 'head-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $headRepo, '-BaselinePath', $headBaseline)
  Assert-Code $r 0 'head baseline snapshot'
  $tree = (Invoke-GitAt $headRepo @('rev-parse', 'HEAD^{tree}')) -join ''
  $unrelatedHead = (Invoke-GitAt $headRepo @('commit-tree', $tree, '-m', 'test: unrelated head')) -join ''
  Invoke-GitAt $headRepo @('update-ref', 'refs/heads/unrelated', $unrelatedHead) | Out-Null
  Invoke-GitAt $headRepo @('symbolic-ref', 'HEAD', 'refs/heads/unrelated') | Out-Null
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $headRepo, '-BaselinePath', $headBaseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify rejects non-descendant HEAD'
  Assert-JsonSafe $r $false @('<HEAD>') 'verify rejects non-descendant HEAD'
  if (($r.Output | ConvertFrom-Json).reason -ne 'head_not_descendant') { throw "non-descendant reason was unstable: $($r.Output)" }

  $missingHeadBaseline = Join-Path $sandbox 'missing-head-baseline.json'
  $missingHead = Get-Content -Raw -LiteralPath $headBaseline | ConvertFrom-Json
  $missingHead.head = '0000000000000000000000000000000000000000'
  $missingHead.payloadHash = Get-TestBaselinePayloadHash $missingHead
  [System.IO.File]::WriteAllText($missingHeadBaseline, ($missingHead | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $headRepo, '-BaselinePath', $missingHeadBaseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify rejects missing baseline HEAD'
  Assert-JsonSafe $r $false @('<HEAD>') 'verify rejects missing baseline HEAD'
  if (($r.Output | ConvertFrom-Json).reason -ne 'baseline_head_missing') { throw "missing HEAD reason was unstable: $($r.Output)" }

  $mergeRepo = Join-Path $sandbox 'merge-repo'
  New-Item -ItemType Directory -Path $mergeRepo | Out-Null
  Invoke-GitAt $mergeRepo @('init') | Out-Null
  Invoke-GitAt $mergeRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $mergeRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $mergeRepo 'task.txt'), "task base`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $mergeRepo @('add', '--', 'task.txt') | Out-Null
  Invoke-GitAt $mergeRepo @('commit', '-m', 'test: merge base') | Out-Null
  $mergeMain = (Invoke-GitAt $mergeRepo @('branch', '--show-current')) -join ''
  $mergeBaseline = Join-Path $sandbox 'merge-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $mergeRepo, '-BaselinePath', $mergeBaseline)
  Assert-Code $r 0 'merge baseline snapshot'
  Invoke-GitAt $mergeRepo @('switch', '-c', 'side') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $mergeRepo '侧支 空格.txt'), "side intruder`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $mergeRepo @('add', '--', '侧支 空格.txt') | Out-Null
  Invoke-GitAt $mergeRepo @('commit', '-m', 'test: side intruder') | Out-Null
  Invoke-GitAt $mergeRepo @('switch', $mergeMain) | Out-Null
  [System.IO.File]::AppendAllText((Join-Path $mergeRepo 'task.txt'), "main expected`n")
  Invoke-GitAt $mergeRepo @('commit', '-am', 'test: main expected') | Out-Null
  Invoke-GitAt $mergeRepo @('merge', '--no-ff', 'side', '-m', 'test: merge side') | Out-Null
  Remove-Item -LiteralPath (Join-Path $mergeRepo '侧支 空格.txt')
  Invoke-GitAt $mergeRepo @('add', '-u', '--', '侧支 空格.txt') | Out-Null
  Invoke-GitAt $mergeRepo @('commit', '-m', 'test: remove merged intruder') | Out-Null
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $mergeRepo, '-BaselinePath', $mergeBaseline, '-ExpectedPaths', 'task.txt')
  Assert-Code $r 21 'verify scans side branch paths removed after merge'
  Assert-JsonSafe $r $false @('侧支 空格.txt') 'verify scans side branch paths removed after merge'

  $expectedMergeRepo = Join-Path $sandbox 'expected-merge-repo'
  New-Item -ItemType Directory -Path $expectedMergeRepo | Out-Null
  Invoke-GitAt $expectedMergeRepo @('init') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('config', 'user.name', 'Workspace Guard Test') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('config', 'user.email', 'workspace-guard@example.invalid') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $expectedMergeRepo 'base.txt'), "base`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $expectedMergeRepo @('add', '--', 'base.txt') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('commit', '-m', 'test: expected merge base') | Out-Null
  $expectedMergeMain = (Invoke-GitAt $expectedMergeRepo @('branch', '--show-current')) -join ''
  $expectedMergeBaseline = Join-Path $sandbox 'expected-merge-baseline.json'
  $r = Invoke-Guard @('Snapshot', '-RepositoryRoot', $expectedMergeRepo, '-BaselinePath', $expectedMergeBaseline)
  Assert-Code $r 0 'expected merge baseline snapshot'
  Invoke-GitAt $expectedMergeRepo @('switch', '-c', 'side') | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $expectedMergeRepo 'expected-dir') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $expectedMergeRepo 'expected-dir\side.txt'), "side`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $expectedMergeRepo @('add', '--', 'expected-dir/side.txt') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('commit', '-m', 'test: expected side') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('switch', $expectedMergeMain) | Out-Null
  New-Item -ItemType Directory -Path (Join-Path $expectedMergeRepo 'expected-dir') | Out-Null
  [System.IO.File]::WriteAllText((Join-Path $expectedMergeRepo 'expected-dir\main.txt'), "main`n", [System.Text.UTF8Encoding]::new($false))
  Invoke-GitAt $expectedMergeRepo @('add', '--', 'expected-dir/main.txt') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('commit', '-m', 'test: expected main') | Out-Null
  Invoke-GitAt $expectedMergeRepo @('merge', '--no-ff', 'side', '-m', 'test: expected merge') | Out-Null
  $r = Invoke-Guard @('Verify', '-RepositoryRoot', $expectedMergeRepo, '-BaselinePath', $expectedMergeBaseline, '-ExpectedPaths', 'expected-dir')
  Assert-Code $r 0 'verify allows expected-only merge DAG'
  Assert-JsonSafe $r $true @() 'verify allows expected-only merge DAG'

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
