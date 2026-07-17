#requires -Version 7.0

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'protocol.psm1') -Force -DisableNameChecking

$script:RegistryRepositoryRoot = $null
$script:RootFields = @('schemaVersion', 'tasks')
$script:TaskFields = @(
  'taskId',
  'title',
  'priority',
  'owner',
  'executor',
  'status',
  'dependencies',
  'dependencyEvidence',
  'executionEnabled',
  'requiredSources',
  'allowedRoots',
  'discoveryChecks',
  'requiredChecks',
  'completionEvidence',
  'decisionIds',
  'coverageRules'
)
$script:EvidenceFields = @('taskId', 'status', 'source', 'match')
$script:CoverageFields = @('decisionId', 'requiredPaths', 'requiredRoots', 'requiredAnyGlobs', 'requiredInventories')
$script:InventoryFields = @('root', 'glob')
$script:ExpectedTaskIds = @('TQ-057', 'TQ-059', 'TQ-069', 'N-SLOT-01', 'N-DIST-01')
$script:Tq057DecisionIds = @(
  'DEC-20260715-35ACB87E6C10',
  'DEC-20260715-75D7BA2AF210',
  'DEC-20260714-29A5D1356CC8',
  'DEC-20260714-320075D033A5',
  'DEC-20260713-A07FA708DB22'
)
$script:Tq057RequiredChecks = @('data-chain', 'unity-editmode-related', 'pending-whitespace', 'cached-diff-check')
$script:Tq057ExactAllowedPaths = @('tools/check-data-chain.ps1')
$script:MultiplierDecisionId = 'DEC-20260715-75D7BA2AF210'
$script:MultiplierCorePaths = @(
  'src/Assets/DataConfig/Spells.csv',
  'src/Assets/Scripts/Editor/DataConfigImporter.cs',
  'src/Assets/Scripts/Combat/SpellData.cs',
  'src/Assets/Scripts/Combat/CombatResolver.cs',
  'tools/check-data-chain.ps1'
)

function Throw-RegistryInvalid {
  param([Parameter(Mandatory = $true)][string]$Message)

  throw "registry_invalid: $Message"
}

function Assert-ExactFields {
  param(
    [Parameter(Mandatory = $true)]
    [Collections.IDictionary]$Value,
    [Parameter(Mandatory = $true)]
    [string[]]$Expected,
    [Parameter(Mandatory = $true)]
    [string]$Label
  )

  $actual = @($Value.Keys)
  $unknown = @($actual | Where-Object { $_ -cnotin $Expected })
  $missing = @($Expected | Where-Object { $_ -cnotin $actual })
  if ($unknown.Count -gt 0 -or $missing.Count -gt 0) {
    Throw-RegistryInvalid "$Label fields do not match schema"
  }
}

function Assert-StringArray {
  param(
    [Parameter(Mandatory = $true)]$Value,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ($Value -is [string] -or $Value -isnot [Collections.IEnumerable]) {
    Throw-RegistryInvalid "$Label must be an array"
  }
  foreach ($item in @($Value)) {
    if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace($item)) {
      Throw-RegistryInvalid "$Label must contain non-empty strings"
    }
  }
}

function Assert-RegistryPathSyntax {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ([string]::IsNullOrWhiteSpace($Path) -or
      [IO.Path]::IsPathFullyQualified($Path) -or
      $Path.Contains('\') -or
      $Path -match '(^|/)\.\.?(/|$)' -or
      $Path -match '//') {
    Throw-RegistryInvalid "$Label contains an invalid project path"
  }
}

function Assert-CoverageRule {
  param([Parameter(Mandatory = $true)][Collections.IDictionary]$Rule)

  Assert-ExactFields -Value $Rule -Expected $script:CoverageFields -Label 'coverage rule'
  if ([string]::IsNullOrWhiteSpace([string]$Rule.decisionId)) {
    Throw-RegistryInvalid 'coverage rule decisionId is empty'
  }
  foreach ($field in @('requiredPaths', 'requiredRoots', 'requiredAnyGlobs')) {
    Assert-StringArray -Value $Rule[$field] -Label "coverage rule $field"
  }
  foreach ($path in @($Rule.requiredPaths) + @($Rule.requiredRoots)) {
    Assert-RegistryPathSyntax -Path $path -Label 'coverage rule'
  }
  foreach ($glob in @($Rule.requiredAnyGlobs)) {
    if ($glob.Contains('\') -or [IO.Path]::IsPathFullyQualified($glob) -or $glob -match '(^|/)\.\.?(/|$)') {
      Throw-RegistryInvalid 'coverage rule contains an invalid glob'
    }
  }
  if ($Rule.requiredInventories -is [string] -or $Rule.requiredInventories -isnot [Collections.IEnumerable]) {
    Throw-RegistryInvalid 'requiredInventories must be an array'
  }
  foreach ($inventory in @($Rule.requiredInventories)) {
    if ($inventory -isnot [Collections.IDictionary]) {
      Throw-RegistryInvalid 'inventory must be an object'
    }
    Assert-ExactFields -Value $inventory -Expected $script:InventoryFields -Label 'inventory'
    Assert-RegistryPathSyntax -Path ([string]$inventory.root) -Label 'inventory root'
    if ([string]::IsNullOrWhiteSpace([string]$inventory.glob) -or [string]$inventory.glob -match '[\\/]') {
      Throw-RegistryInvalid 'inventory glob is invalid'
    }
  }
}

function Assert-Tq057Contract {
  param([Parameter(Mandatory = $true)][Collections.IDictionary]$Task)

  if (($Task.decisionIds -join '|') -cne ($script:Tq057DecisionIds -join '|')) {
    Throw-RegistryInvalid 'TQ-057 decisionIds are incomplete'
  }
  foreach ($check in $script:Tq057RequiredChecks) {
    if ($check -cnotin @($Task.requiredChecks)) {
      Throw-RegistryInvalid "TQ-057 required check is missing: $check"
    }
  }
  foreach ($path in $script:Tq057ExactAllowedPaths) {
    if ($path -cnotin @($Task.allowedRoots)) {
      Throw-RegistryInvalid "TQ-057 exact allowed path is missing: $path"
    }
  }
  $rules = @($Task.coverageRules)
  foreach ($decisionId in $script:Tq057DecisionIds) {
    if (@($rules | Where-Object { $_.decisionId -ceq $decisionId }).Count -ne 1) {
      Throw-RegistryInvalid "TQ-057 coverage rule is missing: $decisionId"
    }
  }
  $multiplierRule = @($rules | Where-Object { $_.decisionId -ceq $script:MultiplierDecisionId })[0]
  foreach ($path in $script:MultiplierCorePaths) {
    if ($path -cnotin @($multiplierRule.requiredPaths)) {
      Throw-RegistryInvalid "TQ-057 multiplier path is missing: $path"
    }
  }
  if ('src/Assets/Tests/EditMode/*.cs' -cnotin @($multiplierRule.requiredAnyGlobs)) {
    Throw-RegistryInvalid 'TQ-057 multiplier EditMode coverage is missing'
  }
  $spellInventory = @($multiplierRule.requiredInventories | Where-Object {
      $_.root -ceq 'src/Assets/Data/Spells' -and $_.glob -ceq '*.asset'
    })
  if ($spellInventory.Count -ne 1) {
    Throw-RegistryInvalid 'TQ-057 spell asset inventory is missing'
  }
}

function Read-TaskRegistry {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path
  )

  if (-not [IO.Path]::IsPathFullyQualified($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    Throw-RegistryInvalid 'registry path must be an existing absolute file'
  }
  try {
    $decoder = [Text.UTF8Encoding]::new($false, $true)
    $text = $decoder.GetString([IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))).TrimStart([char]0xFEFF)
    $registry = $text | ConvertFrom-Json -AsHashtable
  } catch {
    Throw-RegistryInvalid 'registry must be valid UTF-8 JSON'
  }
  if ($registry -isnot [Collections.IDictionary]) {
    Throw-RegistryInvalid 'registry root must be an object'
  }
  Assert-ExactFields -Value $registry -Expected $script:RootFields -Label 'registry root'
  if ($registry.schemaVersion -ne 1 -or $registry.tasks -is [string] -or $registry.tasks -isnot [Collections.IEnumerable]) {
    Throw-RegistryInvalid 'registry schemaVersion or tasks is invalid'
  }

  $ids = @()
  foreach ($task in @($registry.tasks)) {
    if ($task -isnot [Collections.IDictionary]) {
      Throw-RegistryInvalid 'task must be an object'
    }
    Assert-ExactFields -Value $task -Expected $script:TaskFields -Label 'task'
    foreach ($field in @('taskId', 'title', 'priority', 'owner', 'executor', 'status')) {
      if ([string]::IsNullOrWhiteSpace([string]$task[$field])) {
        Throw-RegistryInvalid "task $field is empty"
      }
    }
    if ([string]$task.priority -notmatch '^P\d+$' -or $task.executionEnabled -isnot [bool]) {
      Throw-RegistryInvalid 'task priority or executionEnabled is invalid'
    }
    foreach ($field in @('dependencies', 'requiredSources', 'allowedRoots', 'discoveryChecks', 'requiredChecks', 'completionEvidence', 'decisionIds')) {
      Assert-StringArray -Value $task[$field] -Label "task $($task.taskId) $field"
    }
    foreach ($path in @($task.requiredSources) + @($task.allowedRoots)) {
      Assert-RegistryPathSyntax -Path $path -Label "task $($task.taskId)"
    }
    if ($task.dependencyEvidence -is [string] -or $task.dependencyEvidence -isnot [Collections.IEnumerable]) {
      Throw-RegistryInvalid 'dependencyEvidence must be an array'
    }
    foreach ($evidence in @($task.dependencyEvidence)) {
      if ($evidence -isnot [Collections.IDictionary]) {
        Throw-RegistryInvalid 'dependency evidence must be an object'
      }
      Assert-ExactFields -Value $evidence -Expected $script:EvidenceFields -Label 'dependency evidence'
      foreach ($field in $script:EvidenceFields) {
        if ([string]::IsNullOrWhiteSpace([string]$evidence[$field])) {
          Throw-RegistryInvalid "dependency evidence $field is empty"
        }
      }
      Assert-RegistryPathSyntax -Path ([string]$evidence.source) -Label 'dependency evidence source'
    }
    if ($task.coverageRules -is [string] -or $task.coverageRules -isnot [Collections.IEnumerable]) {
      Throw-RegistryInvalid 'coverageRules must be an array'
    }
    foreach ($rule in @($task.coverageRules)) {
      if ($rule -isnot [Collections.IDictionary]) {
        Throw-RegistryInvalid 'coverage rule must be an object'
      }
      Assert-CoverageRule -Rule $rule
    }
    $ids += [string]$task.taskId
  }

  if (($ids | Select-Object -Unique).Count -ne $ids.Count) {
    Throw-RegistryInvalid 'duplicate task id'
  }
  if (($ids -join '|') -cne ($script:ExpectedTaskIds -join '|')) {
    Throw-RegistryInvalid 'registry task ids or order do not match the current queue'
  }
  $enabled = @($registry.tasks | Where-Object { $_.executionEnabled })
  if ($enabled.Count -ne 1 -or $enabled[0].taskId -cne 'TQ-057') {
    Throw-RegistryInvalid 'only TQ-057 may be execution enabled'
  }
  Assert-Tq057Contract -Task $enabled[0]

  $managementRoot = Split-Path -Parent ([IO.Path]::GetFullPath($Path))
  $script:RegistryRepositoryRoot = Split-Path -Parent $managementRoot
  $registry
}

function Get-QueueRows {
  param([Parameter(Mandatory = $true)][string]$Text)

  $rows = [ordered]@{}
  $pattern = '(?m)^\|\s*(?<id>[^|]+?)\s*\|\s*(?<priority>[^|]+?)\s*\|\s*(?<owner>[^|]+?)\s*\|\s*(?<type>[^|]+?)\s*\|\s*(?<status>[^|]+?)\s*\|\s*(?<title>[^|]+?)\s*\|\s*$'
  foreach ($match in [regex]::Matches($Text, $pattern)) {
    $id = $match.Groups['id'].Value.Trim()
    if ($id -match '^(TQ-\d+|N-[A-Z]+-\d+)$') {
      $rows[$id] = [ordered]@{
        priority = $match.Groups['priority'].Value.Trim()
        owner = $match.Groups['owner'].Value.Trim()
        status = $match.Groups['status'].Value.Trim()
        title = $match.Groups['title'].Value.Trim()
      }
    }
  }
  $rows
}

function Assert-RegistryMatchesQueue {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [Collections.IDictionary]$Registry,
    [Parameter(Mandatory = $true)]
    [string]$QueuePath
  )

  if (-not [IO.Path]::IsPathFullyQualified($QueuePath) -or -not (Test-Path -LiteralPath $QueuePath -PathType Leaf)) {
    Throw-RegistryInvalid 'queue path must be an existing absolute file'
  }
  $queueText = [IO.File]::ReadAllText([IO.Path]::GetFullPath($QueuePath))
  $rows = Get-QueueRows -Text $queueText
  if ((@($rows.Keys) -join '|') -cne ((@($Registry.tasks.taskId)) -join '|')) {
    Throw-RegistryInvalid 'registry and queue task ids differ'
  }
  foreach ($task in @($Registry.tasks)) {
    $row = $rows[[string]$task.taskId]
    foreach ($field in @('priority', 'owner', 'status', 'title')) {
      if ([string]$task[$field] -cne [string]$row[$field]) {
        Throw-RegistryInvalid "queue $field mismatch for $($task.taskId)"
      }
    }
    foreach ($dependency in @($task.dependencies)) {
      $evidence = @($task.dependencyEvidence | Where-Object { $_.taskId -ceq $dependency })
      if ($evidence.Count -ne 1 -or $queueText.IndexOf([string]$evidence[0].match, [StringComparison]::Ordinal) -lt 0) {
        Throw-RegistryInvalid "queue dependency mismatch for $($task.taskId)"
      }
    }
  }
  $script:RegistryRepositoryRoot = Split-Path -Parent (Split-Path -Parent ([IO.Path]::GetFullPath($QueuePath)))
}

function Test-DependencyEvidence {
  param([Parameter(Mandatory = $true)][Collections.IDictionary]$Task)

  if (@($Task.dependencies).Count -eq 0) {
    return $true
  }
  if ([string]::IsNullOrWhiteSpace([string]$script:RegistryRepositoryRoot)) {
    return $false
  }
  foreach ($dependency in @($Task.dependencies)) {
    $matches = @($Task.dependencyEvidence | Where-Object {
        $_.taskId -ceq $dependency -and $_.status -ceq 'completed'
      })
    if ($matches.Count -ne 1) {
      return $false
    }
    try {
      $source = Normalize-ProjectPath -Path ([string]$matches[0].source) -RepositoryRoot $script:RegistryRepositoryRoot
      $sourcePath = Join-Path $script:RegistryRepositoryRoot ($source.Replace('/', [IO.Path]::DirectorySeparatorChar))
      if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        return $false
      }
      $text = [IO.File]::ReadAllText($sourcePath)
      if ($text.IndexOf([string]$matches[0].match, [StringComparison]::Ordinal) -lt 0) {
        return $false
      }
    } catch {
      return $false
    }
  }
  $true
}

function Select-ExecutableTask {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [Collections.IDictionary]$Registry
  )

  $candidates = @()
  for ($index = 0; $index -lt @($Registry.tasks).Count; $index++) {
    $task = $Registry.tasks[$index]
    if (-not $task.executionEnabled -or
        $task.status -cne '待处理' -or
        $task.executor -cne 'codex' -or
        -not (Test-DependencyEvidence -Task $task)) {
      continue
    }
    $candidates += [pscustomobject]@{
      priority = [int]([string]$task.priority).Substring(1)
      index = $index
      task = $task
    }
  }
  $selected = @($candidates | Sort-Object priority, index | Select-Object -First 1)
  if ($selected.Count -eq 0) {
    return $null
  }
  $selected[0].task
}

function Get-TaskContract {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory = $true)]
    [Collections.IDictionary]$Registry,
    [Parameter(Mandatory = $true)]
    [string]$TaskId
  )

  $matches = @($Registry.tasks | Where-Object { $_.taskId -ceq $TaskId })
  if ($matches.Count -ne 1) {
    throw "task_not_found: $TaskId"
  }
  $matches[0]
}

Export-ModuleMember -Function @(
  'Read-TaskRegistry',
  'Assert-RegistryMatchesQueue',
  'Select-ExecutableTask',
  'Get-TaskContract'
)
