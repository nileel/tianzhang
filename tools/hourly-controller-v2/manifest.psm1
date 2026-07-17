#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking

$script:ManifestFields = @(
  'schemaVersion',
  'runId',
  'taskId',
  'model',
  'threadId',
  'planOnly',
  'sourceEvidence',
  'decisionCoverage',
  'expectedPaths',
  'intendedChanges',
  'requiredChecks',
  'completionEvidence'
)
$script:SourceEvidenceFields = @('path', 'sha256')
$script:DecisionCoverageFields = @('decisionId', 'resolutionText', 'paths', 'implementation')
$script:IntendedChangeFields = @('path', 'operation', 'summary')
$script:AllowedOperations = @('create', 'modify', 'delete')
$script:WorkspaceGuardPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'automation-workspace-guard.ps1'

function Throw-ManifestError {
  param(
    [Parameter(Mandatory = $true)][string]$Code,
    [Parameter(Mandatory = $true)][string]$Message,
    [string[]]$ChangedPaths = @()
  )

  $exception = [InvalidOperationException]::new("$Code`: $Message")
  $exception.Data['errorCode'] = $Code
  $exception.Data['changedPaths'] = [string[]]@($ChangedPaths)
  throw $exception
}

function Get-ManifestFieldNames {
  param([Parameter(Mandatory = $true)]$Value)

  if ($Value -is [Collections.IDictionary]) {
    return @($Value.Keys)
  }
  @($Value.PSObject.Properties.Name)
}

function Assert-ManifestFields {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string[]]$Expected,
    [Parameter(Mandatory = $true)][string]$Label,
    [string]$Code = 'manifest_invalid'
  )

  $actual = @(Get-ManifestFieldNames -Value $Value)
  if (@($actual | Where-Object { $_ -cnotin $Expected }).Count -gt 0 -or
      @($Expected | Where-Object { $_ -cnotin $actual }).Count -gt 0) {
    Throw-ManifestError -Code $Code -Message "$Label fields do not match schema"
  }
}

function Assert-ManifestArray {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ($Value -is [string] -or $Value -isnot [Collections.IEnumerable]) {
    Throw-ManifestError -Code 'manifest_invalid' -Message "$Label must be an array"
  }
}

function Read-JsonWithStringDates {
  param(
    [Parameter(Mandatory = $true)][string]$Text,
    [switch]$AsHashtable
  )

  $parameters = @{}
  if ($AsHashtable) { $parameters['AsHashtable'] = $true }
  if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $parameters['DateKind'] = 'String' }
  $Text | ConvertFrom-Json @parameters
}

function Read-WorkManifest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Throw-ManifestError -Code 'invalid_request' -Message 'manifest path must be an existing absolute file'
  }
  try {
    $decoder = [Text.UTF8Encoding]::new($false, $true)
    $text = $decoder.GetString([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))).TrimStart([char]0xFEFF)
    if ($text -notmatch '^\s*\{') {
      Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest must be a JSON object'
    }
    $manifest = Read-JsonWithStringDates -Text $text -AsHashtable
  } catch [InvalidOperationException] {
    throw
  } catch {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest must be valid UTF-8 JSON'
  }
  if ($manifest -isnot [Collections.IDictionary]) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest root must be an object'
  }
  Assert-ManifestFields -Value $manifest -Expected $script:ManifestFields -Label 'manifest'
  if ($manifest.schemaVersion -ne 1 -or
      [string]::IsNullOrWhiteSpace([string]$manifest.taskId) -or
      [string]::IsNullOrWhiteSpace([string]$manifest.model) -or
      $manifest.planOnly -isnot [bool]) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest scalar fields are invalid'
  }
  $parsedGuid = [guid]::Empty
  if (-not [guid]::TryParse([string]$manifest.runId, [ref]$parsedGuid) -or
      -not [guid]::TryParse([string]$manifest.threadId, [ref]$parsedGuid)) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest runId and threadId must be UUIDs'
  }
  foreach ($field in @('sourceEvidence', 'decisionCoverage', 'expectedPaths', 'intendedChanges', 'requiredChecks', 'completionEvidence')) {
    Assert-ManifestArray -Value $manifest[$field] -Label $field
  }
  foreach ($evidence in @($manifest.sourceEvidence)) {
    if ($evidence -isnot [Collections.IDictionary]) {
      Throw-ManifestError -Code 'manifest_invalid' -Message 'sourceEvidence item must be an object'
    }
    Assert-ManifestFields -Value $evidence -Expected $script:SourceEvidenceFields -Label 'sourceEvidence item'
  }
  foreach ($coverage in @($manifest.decisionCoverage)) {
    if ($coverage -isnot [Collections.IDictionary]) {
      Throw-ManifestError -Code 'manifest_invalid' -Message 'decisionCoverage item must be an object'
    }
    Assert-ManifestFields -Value $coverage -Expected $script:DecisionCoverageFields -Label 'decisionCoverage item'
    Assert-ManifestArray -Value $coverage.paths -Label 'decisionCoverage paths'
  }
  foreach ($change in @($manifest.intendedChanges)) {
    if ($change -isnot [Collections.IDictionary]) {
      Throw-ManifestError -Code 'manifest_invalid' -Message 'intendedChanges item must be an object'
    }
    Assert-ManifestFields -Value $change -Expected $script:IntendedChangeFields -Label 'intendedChanges item'
  }
  $manifest
}

function Normalize-ManifestPath {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [string]$Code = 'manifest_invalid'
  )

  try {
    Normalize-ProjectPath -Path $Path -RepositoryRoot $RepositoryRoot
  } catch {
    Throw-ManifestError -Code $Code -Message $_.Exception.Message
  }
}

function Test-ManifestPathUnderRoot {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Root
  )

  $Path -ceq $Root -or $Path.StartsWith($Root.TrimEnd('/') + '/', [StringComparison]::Ordinal)
}

function Assert-ExpectedPaths {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot
  )

  $expectedPaths = @()
  foreach ($rawPath in @($Manifest.expectedPaths)) {
    if ($rawPath -isnot [string]) {
      Throw-ManifestError -Code 'manifest_invalid' -Message 'expectedPaths must contain strings'
    }
    $path = Normalize-ManifestPath -Path $rawPath -RepositoryRoot $RepositoryRoot -Code 'path_outside_scope'
    $allowed = $false
    foreach ($root in @($TaskContract.allowedRoots)) {
      if (Test-ManifestPathUnderRoot -Path $path -Root ([string]$root)) {
        $allowed = $true
        break
      }
    }
    if (-not $allowed) {
      Throw-ManifestError -Code 'path_outside_scope' -Message "expected path is outside allowedRoots: $path"
    }
    $expectedPaths += $path
  }
  if ($expectedPaths.Count -eq 0 -or @($expectedPaths | Select-Object -Unique).Count -ne $expectedPaths.Count) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'expectedPaths must be non-empty and unique'
  }

  $intendedPaths = @()
  foreach ($change in @($Manifest.intendedChanges)) {
    $path = Normalize-ManifestPath -Path ([string]$change.path) -RepositoryRoot $RepositoryRoot
    if ([string]$change.operation -cnotin $script:AllowedOperations -or [string]::IsNullOrWhiteSpace([string]$change.summary)) {
      Throw-ManifestError -Code 'manifest_invalid' -Message "intended change is invalid: $path"
    }
    $intendedPaths += $path
  }
  if (@($intendedPaths | Select-Object -Unique).Count -ne $intendedPaths.Count -or
      (($intendedPaths | Sort-Object) -join '|') -cne (($expectedPaths | Sort-Object) -join '|')) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'each expectedPath must have exactly one intendedChange'
  }
  @($expectedPaths)
}

function Read-DiscoveryLogEntries {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Throw-ManifestError -Code 'discovery_incomplete' -Message 'discovery log is missing'
  }
  $entries = @()
  foreach ($line in [IO.File]::ReadAllLines([IO.Path]::GetFullPath($Path))) {
    try {
      $entries += Read-JsonWithStringDates -Text $line
    } catch {
      Throw-ManifestError -Code 'discovery_incomplete' -Message 'discovery log is invalid'
    }
  }
  @($entries)
}

function Get-ManifestFileSha256 {
  param([Parameter(Mandatory = $true)][string]$Path)

  [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($Path))).ToLowerInvariant()
}

function Assert-SourceEvidence {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)][object[]]$DiscoveryEntries,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot
  )

  if (@($Manifest.sourceEvidence).Count -ne @($TaskContract.requiredSources).Count) {
    Throw-ManifestError -Code 'discovery_incomplete' -Message 'required source evidence count is incomplete'
  }
  foreach ($source in @($TaskContract.requiredSources)) {
    $manifestEvidence = @($Manifest.sourceEvidence | Where-Object { $_.path -ceq $source })
    if ($manifestEvidence.Count -ne 1) {
      Throw-ManifestError -Code 'discovery_incomplete' -Message "required source evidence is missing: $source"
    }
    if ([string]$manifestEvidence[0].sha256 -notmatch '^[0-9a-f]{64}$') {
      Throw-ManifestError -Code 'manifest_invalid' -Message "source hash is invalid: $source"
    }
    $logEvidence = @($DiscoveryEntries | Where-Object {
        $_.ok -and $_.action -ceq 'DiscoverRead' -and $_.satisfiedSource -ceq $source
      })
    if ($logEvidence.Count -lt 1) {
      Throw-ManifestError -Code 'discovery_incomplete' -Message "source was not read through the gateway: $source"
    }
    $latestLog = $logEvidence[-1]
    $fullPath = Join-Path $RepositoryRoot ($source.Replace('/', [IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
      Throw-ManifestError -Code 'source_changed' -Message "required source no longer exists: $source"
    }
    $currentHash = Get-ManifestFileSha256 -Path $fullPath
    if ([string]$manifestEvidence[0].sha256 -cne [string]$latestLog.sourceSha256 -or
        $currentHash -cne [string]$manifestEvidence[0].sha256) {
      Throw-ManifestError -Code 'source_changed' -Message "required source changed: $source" -ChangedPaths @($source)
    }
  }
  foreach ($evidence in @($Manifest.sourceEvidence)) {
    if ([string]$evidence.path -cnotin @($TaskContract.requiredSources)) {
      Throw-ManifestError -Code 'manifest_invalid' -Message "unregistered source evidence: $($evidence.path)"
    }
  }
}

function Assert-DiscoveryCheckEvidence {
  param(
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)][object[]]$DiscoveryEntries
  )

  foreach ($checkId in @($TaskContract.discoveryChecks)) {
    $evidence = @($DiscoveryEntries | Where-Object {
        $_.ok -and
        $_.action -ceq 'DiscoverCheck' -and
        $_.input.checkId -ceq [string]$checkId -and
        $_.satisfiedCheck -ceq [string]$checkId
      })
    if ($evidence.Count -lt 1) {
      Throw-ManifestError -Code 'discovery_incomplete' -Message "registered discovery check evidence is missing: $checkId"
    }
  }
}

function Assert-DecisionCoverageShape {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)]$DecisionLedger,
    [Parameter(Mandatory = $true)][string[]]$ExpectedPaths,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot
  )

  if (@($Manifest.decisionCoverage).Count -ne @($TaskContract.decisionIds).Count) {
    Throw-ManifestError -Code 'decision_coverage_incomplete' -Message 'decision coverage count is incomplete'
  }
  foreach ($decisionId in @($TaskContract.decisionIds)) {
    $coverage = @($Manifest.decisionCoverage | Where-Object { $_.decisionId -ceq $decisionId })
    $ledger = @($DecisionLedger | Where-Object { $_.decisionId -ceq $decisionId })
    if ($coverage.Count -ne 1 -or $ledger.Count -ne 1) {
      Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "decision coverage is missing: $decisionId"
    }
    if ([string]$coverage[0].resolutionText -cne [string]$ledger[0].resolutionText -or
        [string]::IsNullOrWhiteSpace([string]$coverage[0].implementation) -or
        @($coverage[0].paths).Count -eq 0) {
      Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "decision resolution or implementation is incomplete: $decisionId"
    }
    $coveragePaths = @()
    foreach ($rawPath in @($coverage[0].paths)) {
      $path = Normalize-ManifestPath -Path ([string]$rawPath) -RepositoryRoot $RepositoryRoot
      if ($path -cnotin $ExpectedPaths) {
        Throw-ManifestError -Code 'manifest_invalid' -Message "decision path is outside expectedPaths: $path"
      }
      $coveragePaths += $path
    }
    if (@($coveragePaths | Select-Object -Unique).Count -ne $coveragePaths.Count) {
      Throw-ManifestError -Code 'manifest_invalid' -Message "decision paths are duplicated: $decisionId"
    }
  }
  foreach ($coverage in @($Manifest.decisionCoverage)) {
    if ([string]$coverage.decisionId -cnotin @($TaskContract.decisionIds)) {
      Throw-ManifestError -Code 'manifest_invalid' -Message "unregistered decision coverage: $($coverage.decisionId)"
    }
  }
}

function Test-PathMatchesManifestGlob {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Pattern
  )

  $separator = $Pattern.LastIndexOf('/')
  if ($separator -lt 0) { return $false }
  $root = $Pattern.Substring(0, $separator)
  $glob = $Pattern.Substring($separator + 1)
  (Test-ManifestPathUnderRoot -Path $Path -Root $root) -and
    [IO.Enumeration.FileSystemName]::MatchesSimpleExpression($glob, [IO.Path]::GetFileName($Path), $true)
}

function Get-ManifestInventory {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$Glob
  )

  $fullRoot = Join-Path $RepositoryRoot ($Root.Replace('/', [IO.Path]::DirectorySeparatorChar))
  if (-not (Test-Path -LiteralPath $fullRoot -PathType Container)) {
    Throw-ManifestError -Code 'discovery_incomplete' -Message "inventory root is missing: $Root"
  }
  $paths = @()
  foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($fullRoot, '*', [IO.SearchOption]::AllDirectories)) {
    $attributes = [IO.File]::GetAttributes($entry)
    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      Throw-ManifestError -Code 'discovery_incomplete' -Message 'inventory contains a reparse point'
    }
    if (($attributes -band [IO.FileAttributes]::Directory) -eq 0 -and
        [IO.Enumeration.FileSystemName]::MatchesSimpleExpression($Glob, [IO.Path]::GetFileName($entry), $true)) {
      $paths += [IO.Path]::GetRelativePath($RepositoryRoot, $entry).Replace('\', '/')
    }
  }
  @($paths | Sort-Object)
}

function Assert-CoverageRules {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)][object[]]$DiscoveryEntries,
    [Parameter(Mandatory = $true)][string[]]$ExpectedPaths,
    [Parameter(Mandatory = $true)][string]$RepositoryRoot
  )

  foreach ($rule in @($TaskContract.coverageRules)) {
    $coverage = @($Manifest.decisionCoverage | Where-Object { $_.decisionId -ceq $rule.decisionId })[0]
    $paths = @($coverage.paths)
    foreach ($requiredPath in @($rule.requiredPaths)) {
      if ([string]$requiredPath -cnotin $paths) {
        Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "required decision path is missing: $requiredPath"
      }
    }
    foreach ($requiredRoot in @($rule.requiredRoots)) {
      if (@($paths | Where-Object { Test-ManifestPathUnderRoot -Path ([string]$_) -Root ([string]$requiredRoot) }).Count -eq 0) {
        Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "required decision root is missing: $requiredRoot"
      }
    }
    foreach ($requiredGlob in @($rule.requiredAnyGlobs)) {
      if (@($paths | Where-Object { Test-PathMatchesManifestGlob -Path ([string]$_) -Pattern ([string]$requiredGlob) }).Count -eq 0) {
        Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "required decision glob is missing: $requiredGlob"
      }
    }
    foreach ($inventoryRule in @($rule.requiredInventories)) {
      $inventory = @(Get-ManifestInventory -RepositoryRoot $RepositoryRoot -Root ([string]$inventoryRule.root) -Glob ([string]$inventoryRule.glob))
      $inventoryHash = Get-Sha256Text -Text ($inventory -join "`n")
      $listEvidence = @($DiscoveryEntries | Where-Object {
          $_.ok -and $_.action -ceq 'DiscoverList' -and
          $_.input.root -ceq [string]$inventoryRule.root -and
          $_.input.glob -ceq [string]$inventoryRule.glob -and
          $_.sourceSha256 -ceq $inventoryHash
        })
      if ($listEvidence.Count -lt 1) {
        Throw-ManifestError -Code 'discovery_incomplete' -Message "inventory discovery evidence is missing: $($inventoryRule.root)"
      }
      foreach ($inventoryPath in $inventory) {
        if ($inventoryPath -cnotin $ExpectedPaths -or $inventoryPath -cnotin $paths) {
          Throw-ManifestError -Code 'decision_coverage_incomplete' -Message "discovered inventory path is missing: $inventoryPath"
        }
      }
    }
  }
}

function Assert-RegisteredLists {
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract
  )

  if (((@($Manifest.requiredChecks) | Sort-Object) -join '|') -cne ((@($TaskContract.requiredChecks) | Sort-Object) -join '|') -or
      @($Manifest.requiredChecks).Count -ne @($TaskContract.requiredChecks).Count) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'requiredChecks must exactly match the task registry'
  }
  if (((@($Manifest.completionEvidence) | Sort-Object) -join '|') -cne ((@($TaskContract.completionEvidence) | Sort-Object) -join '|') -or
      @($Manifest.completionEvidence).Count -ne @($TaskContract.completionEvidence).Count) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'completionEvidence must exactly match the task registry'
  }
}

function Invoke-ManifestWorkspaceCheck {
  param(
    [Parameter(Mandatory = $true)][string]$RepositoryRoot,
    [Parameter(Mandatory = $true)][string]$BaselinePath,
    [Parameter(Mandatory = $true)][string[]]$ExpectedPaths
  )

  if (-not (Test-Path -LiteralPath $script:WorkspaceGuardPath -PathType Leaf)) {
    Throw-ManifestError -Code 'internal_error' -Message 'workspace guard is missing'
  }
  $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
  if ($null -eq $pwsh) {
    Throw-ManifestError -Code 'internal_error' -Message 'PowerShell 7 is unavailable'
  }
  $raw = @(& $pwsh.Source -NoProfile -ExecutionPolicy Bypass -File $script:WorkspaceGuardPath Check -RepositoryRoot $RepositoryRoot -BaselinePath $BaselinePath -ExpectedPaths ($ExpectedPaths -join '|') 2>&1)
  $exitCode = $LASTEXITCODE
  $jsonLines = @($raw | ForEach-Object { [string]$_ } | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) })
  if ($jsonLines.Count -lt 1) {
    Throw-ManifestError -Code 'internal_error' -Message 'workspace guard returned no JSON'
  }
  try {
    $result = Read-JsonWithStringDates -Text $jsonLines[-1]
  } catch {
    Throw-ManifestError -Code 'internal_error' -Message 'workspace guard returned invalid JSON'
  }
  if ($exitCode -eq 0 -and $result.safe) {
    return
  }
  $changedPaths = @($result.conflictingPaths | ForEach-Object { [string]$_ } | Sort-Object -Unique)
  $baselineText = [IO.File]::ReadAllText([IO.Path]::GetFullPath($BaselinePath))
  $baseline = Read-JsonWithStringDates -Text $baselineText
  $currentHeadOutput = @(& git -C $RepositoryRoot rev-parse HEAD 2>&1)
  $headChanged = $LASTEXITCODE -ne 0 -or [string]$baseline.head -cne ([string]$currentHeadOutput[0]).Trim()
  if ($headChanged) {
    if ('<HEAD>' -cnotin $changedPaths) { $changedPaths += '<HEAD>' }
    Throw-ManifestError -Code 'head_changed' -Message 'workspace HEAD changed after baseline' -ChangedPaths $changedPaths
  }
  Throw-ManifestError -Code 'baseline_changed' -Message 'workspace changed after baseline' -ChangedPaths $changedPaths
}

function Test-WorkManifest {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]$Manifest,
    [Parameter(Mandatory = $true)]$TaskContract,
    [Parameter(Mandatory = $true)]$DecisionLedger,
    [Parameter(Mandatory = $true)][string]$DiscoveryLogPath,
    [Parameter(Mandatory = $true)][string]$BaselinePath
  )

  if ([string]$Manifest.taskId -cne [string]$TaskContract.taskId) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'manifest taskId does not match task contract'
  }
  if (-not [IO.Path]::IsPathFullyQualified($BaselinePath) -or -not (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'baseline path is invalid'
  }
  $baselineText = [IO.File]::ReadAllText([IO.Path]::GetFullPath($BaselinePath))
  $baseline = Read-JsonWithStringDates -Text $baselineText
  $repositoryRoot = [IO.Path]::GetFullPath([string]$baseline.repositoryRoot)
  if (-not (Test-Path -LiteralPath $repositoryRoot -PathType Container)) {
    Throw-ManifestError -Code 'manifest_invalid' -Message 'baseline repository root is invalid'
  }

  $expectedPaths = @(Assert-ExpectedPaths -Manifest $Manifest -TaskContract $TaskContract -RepositoryRoot $repositoryRoot)
  $discoveryEntries = @(Read-DiscoveryLogEntries -Path $DiscoveryLogPath)
  Assert-SourceEvidence -Manifest $Manifest -TaskContract $TaskContract -DiscoveryEntries $discoveryEntries -RepositoryRoot $repositoryRoot
  Assert-DiscoveryCheckEvidence -TaskContract $TaskContract -DiscoveryEntries $discoveryEntries
  Assert-DecisionCoverageShape -Manifest $Manifest -TaskContract $TaskContract -DecisionLedger $DecisionLedger -ExpectedPaths $expectedPaths -RepositoryRoot $repositoryRoot
  Assert-CoverageRules -Manifest $Manifest -TaskContract $TaskContract -DiscoveryEntries $discoveryEntries -ExpectedPaths $expectedPaths -RepositoryRoot $repositoryRoot
  Assert-RegisteredLists -Manifest $Manifest -TaskContract $TaskContract
  Invoke-ManifestWorkspaceCheck -RepositoryRoot $repositoryRoot -BaselinePath $BaselinePath -ExpectedPaths $expectedPaths

  [pscustomobject][ordered]@{
    ok = $true
    taskId = [string]$Manifest.taskId
    planOnly = [bool]$Manifest.planOnly
    expectedPaths = @($expectedPaths)
    requiredChecks = @($Manifest.requiredChecks)
  }
}

Export-ModuleMember -Function @(
  'Read-WorkManifest',
  'Test-WorkManifest'
)
