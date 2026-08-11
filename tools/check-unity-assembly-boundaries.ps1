#requires -Version 7.0

[CmdletBinding()]
param(
  [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$AssemblyRoot = 'src/Assets/Scripts',
  [switch]$SkipRequiredAssemblies
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Boundary {
  param([bool]$Condition, [string]$Message)
  if (-not $Condition) { throw $Message }
}

function Get-AbsolutePath {
  param([string]$Root, [string]$Path)
  if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
  return [IO.Path]::GetFullPath((Join-Path $Root $Path))
}

function Read-AssemblyDefinitions {
  param([string]$Root, [string]$RepoRoot)

  $definitions = @()
  foreach ($file in @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.asmdef' | Sort-Object FullName)) {
    try { $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -Depth 20 }
    catch { throw "invalid asmdef JSON: $($file.FullName): $($_.Exception.Message)" }

    Assert-Boundary ($json.PSObject.Properties.Name -contains 'name') "asmdef has no name: $($file.FullName)"
    Assert-Boundary (-not [string]::IsNullOrWhiteSpace([string]$json.name)) "asmdef has empty name: $($file.FullName)"
    $references = if ($json.PSObject.Properties.Name -contains 'references') {
      @($json.references | ForEach-Object { [string]$_ })
    } else { @() }
    $includePlatforms = if ($json.PSObject.Properties.Name -contains 'includePlatforms') {
      @($json.includePlatforms | ForEach-Object { [string]$_ })
    } else { @() }
    $definitions += [pscustomobject]@{
      Name = [string]$json.name
      References = $references
      IncludePlatforms = $includePlatforms
      FullPath = $file.FullName
      RelativePath = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
    }
  }
  $definitions
}

function Assert-ReferenceList {
  param([string]$Name, [string[]]$Actual, [string[]]$Expected)
  $actualText = $Actual -join ','
  $expectedText = $Expected -join ','
  Assert-Boundary ($actualText -ceq $expectedText) "assembly references differ: $Name expected=[$expectedText] actual=[$actualText]"
}

$repoRoot = Get-AbsolutePath -Root (Get-Location).Path -Path $RepositoryRoot
$assemblyRootPath = Get-AbsolutePath -Root $repoRoot -Path $AssemblyRoot
Assert-Boundary (Test-Path -LiteralPath $assemblyRootPath -PathType Container) "assembly root not found: $assemblyRootPath"

$definitions = @(Read-AssemblyDefinitions -Root $assemblyRootPath -RepoRoot $repoRoot)
Assert-Boundary ($definitions.Count -gt 0) "no asmdef files found: $assemblyRootPath"

$byName = @{}
foreach ($definition in $definitions) {
  Assert-Boundary (-not $byName.ContainsKey($definition.Name)) "duplicate assembly name: $($definition.Name)"
  $byName[$definition.Name] = $definition
}

foreach ($definition in $definitions) {
  foreach ($reference in $definition.References) {
    if ($reference.StartsWith('TianZhang.', [StringComparison]::Ordinal)) {
      Assert-Boundary ($byName.ContainsKey($reference)) "unresolved TianZhang assembly reference: $($definition.Name) -> $reference"
    }
  }
}

$featurePrefix = 'TianZhang.Features.'
$domainAssemblies = @(
  'TianZhang.Foundation',
  'TianZhang.Domain',
  'TianZhang.Spatial',
  'TianZhang.Content',
  'TianZhang.Character',
  'TianZhang.Cultivation',
    'TianZhang.Combat',
    'TianZhang.Combat.Turns',
  'TianZhang.World',
  'TianZhang.Gameplay.Contracts'
)

foreach ($definition in $definitions) {
  $featureReferences = @($definition.References | Where-Object { $_.StartsWith($featurePrefix, [StringComparison]::Ordinal) })
  if ($definition.Name.StartsWith($featurePrefix, [StringComparison]::Ordinal)) {
    $siblingReferences = @($featureReferences | Where-Object { $_ -cne $definition.Name })
    Assert-Boundary ($siblingReferences.Count -eq 0) "sibling Feature reference is forbidden: $($definition.Name) -> $($siblingReferences -join ',')"
  }
  if ($domainAssemblies -ccontains $definition.Name) {
    Assert-Boundary ($featureReferences.Count -eq 0) "Domain-to-Feature reference is forbidden: $($definition.Name) -> $($featureReferences -join ',')"
  }
  if ($definition.Name -cne 'TianZhang.Bootstrap') {
    Assert-Boundary ($featureReferences.Count -le 1) "only Bootstrap may reference multiple Feature implementations: $($definition.Name)"
  }

  $isEditorAssembly = $definition.IncludePlatforms -ccontains 'Editor'
  if (-not $isEditorAssembly) {
    foreach ($reference in $definition.References) {
      if ($byName.ContainsKey($reference)) {
        $referencedAssembly = $byName[$reference]
        $referencedIsEditor = $referencedAssembly.IncludePlatforms -ccontains 'Editor'
        Assert-Boundary (-not $referencedIsEditor) "Editor assembly may not enter Player: $($definition.Name) -> $reference"
      }
    }
  }
}

$visitState = @{}
$visitStack = [Collections.Generic.List[string]]::new()
function Visit-Assembly {
  param([string]$AssemblyName)

  $state = if ($visitState.ContainsKey($AssemblyName)) { [int]$visitState[$AssemblyName] } else { 0 }
  if ($state -eq 2) { return }
  if ($state -eq 1) {
    $start = $visitStack.IndexOf($AssemblyName)
    $cycle = @($visitStack.GetRange($start, $visitStack.Count - $start)) + $AssemblyName
    throw "assembly dependency cycle: $($cycle -join ' -> ')"
  }

  $visitState[$AssemblyName] = 1
  [void]$visitStack.Add($AssemblyName)
  foreach ($reference in $byName[$AssemblyName].References) {
    if ($byName.ContainsKey($reference)) { Visit-Assembly -AssemblyName $reference }
  }
  $visitStack.RemoveAt($visitStack.Count - 1)
  $visitState[$AssemblyName] = 2
}

foreach ($name in @($byName.Keys | Sort-Object)) { Visit-Assembly -AssemblyName $name }

if (-not $SkipRequiredAssemblies) {
  $required = [ordered]@{
    'TianZhang.Spatial' = @('src/Assets/Scripts/Modules/Spatial/TianZhang.Spatial.asmdef', @('TianZhang.Foundation'))
    'TianZhang.Content' = @('src/Assets/Scripts/Modules/Content/TianZhang.Content.asmdef', @('TianZhang.Foundation'))
    'TianZhang.Character' = @('src/Assets/Scripts/Modules/Character/TianZhang.Character.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Spatial'))
    'TianZhang.Cultivation' = @('src/Assets/Scripts/Modules/Cultivation/TianZhang.Cultivation.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Character'))
    'TianZhang.World' = @('src/Assets/Scripts/Modules/World/TianZhang.World.asmdef', @('TianZhang.Foundation', 'TianZhang.Content'))
    'TianZhang.Combat' = @('src/Assets/Scripts/Combat/TianZhang.Combat.asmdef', @('TianZhang.Foundation', 'TianZhang.Domain', 'TianZhang.Content', 'TianZhang.Spatial', 'TianZhang.Combat.Turns'))
    'TianZhang.Combat.Turns' = @('src/Assets/Scripts/Combat/Turns/TianZhang.Combat.Turns.asmdef', @())
    'TianZhang.Gameplay.Contracts' = @('src/Assets/Scripts/Modules/GameplayContracts/TianZhang.Gameplay.Contracts.asmdef', @('TianZhang.Foundation'))
    'TianZhang.Features.CharacterCreation' = @('src/Assets/Scripts/Modules/Features/CharacterCreation/TianZhang.Features.CharacterCreation.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Character', 'TianZhang.Cultivation', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Features.WorldMap' = @('src/Assets/Scripts/Modules/Features/WorldMap/TianZhang.Features.WorldMap.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.World', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Features.Settlement' = @('src/Assets/Scripts/Modules/Features/Settlement/TianZhang.Features.Settlement.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Character', 'TianZhang.World', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Features.Adventure' = @('src/Assets/Scripts/Modules/Features/Adventure/TianZhang.Features.Adventure.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Character', 'TianZhang.World', 'TianZhang.Combat', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Features.CombatPresentation' = @('src/Assets/Scripts/Modules/Features/CombatPresentation/TianZhang.Features.CombatPresentation.asmdef', @('TianZhang.Foundation', 'TianZhang.Combat', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Infrastructure.Persistence' = @('src/Assets/Scripts/Modules/Infrastructure/Persistence/TianZhang.Infrastructure.Persistence.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Character', 'TianZhang.Cultivation', 'TianZhang.World', 'TianZhang.Gameplay.Contracts'))
    'TianZhang.Infrastructure.UnityContent' = @('src/Assets/Scripts/Modules/Infrastructure/UnityContent/TianZhang.Infrastructure.UnityContent.asmdef', @('TianZhang.Foundation', 'TianZhang.Content', 'TianZhang.Spatial'))
    'TianZhang.Bootstrap' = @('src/Assets/Scripts/Modules/Bootstrap/TianZhang.Bootstrap.asmdef', @('TianZhang.Gameplay.Contracts', 'TianZhang.Features.CharacterCreation', 'TianZhang.Features.WorldMap', 'TianZhang.Features.Settlement', 'TianZhang.Features.Adventure', 'TianZhang.Features.CombatPresentation', 'TianZhang.Infrastructure.Persistence', 'TianZhang.Infrastructure.UnityContent'))
  }

  foreach ($entry in $required.GetEnumerator()) {
    Assert-Boundary ($byName.ContainsKey($entry.Key)) "required target assembly missing: $($entry.Key)"
    $definition = $byName[$entry.Key]
    Assert-Boundary ($definition.RelativePath -ceq [string]$entry.Value[0]) "target assembly is in the wrong path: $($entry.Key) -> $($definition.RelativePath)"
    Assert-ReferenceList -Name $entry.Key -Actual $definition.References -Expected @($entry.Value[1])
  }

  $bootstrapAssemblies = @($definitions | Where-Object { $_.Name -ceq 'TianZhang.Bootstrap' })
  Assert-Boundary ($bootstrapAssemblies.Count -eq 1) "exactly one TianZhang.Bootstrap assembly is required"
}

$asmrefs = @(Get-ChildItem -LiteralPath $assemblyRootPath -Recurse -File -Filter '*.asmref' | Sort-Object FullName)
foreach ($asmref in $asmrefs) {
  try { $referenceJson = Get-Content -LiteralPath $asmref.FullName -Raw | ConvertFrom-Json -Depth 10 }
  catch { throw "invalid asmref JSON: $($asmref.FullName): $($_.Exception.Message)" }
  $referenceName = [string]$referenceJson.reference
  Assert-Boundary ($byName.ContainsKey($referenceName)) "asmref targets an unknown assembly: $($asmref.FullName) -> $referenceName"
}

Write-Output "Unity assembly boundaries OK: assemblies=$($definitions.Count), asmrefs=$($asmrefs.Count), root=$assemblyRootPath"
