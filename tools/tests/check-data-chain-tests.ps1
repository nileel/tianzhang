[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checkerPath = Join-Path $repoRoot 'tools/check-data-chain.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("tianzhang-data-chain-test-" + [Guid]::NewGuid())

function Write-FixtureFile {
    param([string]$RelativePath, [string]$Content)

    $path = Join-Path $fixtureRoot $RelativePath
    $directory = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    [System.IO.File]::WriteAllText($path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function New-ValidFixture {
    $script:cultivation = -join @([char]0x89D2, [char]0x8272, [char]0x517B, [char]0x6210)
    $script:gongFa = -join @([char]0x529F, [char]0x6CD5)
    $script:spells = -join @([char]0x672F, [char]0x6CD5)
    $script:skills = -join @([char]0x795E, [char]0x901A)
    $gongFaHeader = 'name,affiliation,grade,elementMain,elementSub,starRootBone,starPhysique,starSpirit,starMind,starReaction,starTalent,starFortune,growth,chapters,contentScope'
    $gongFaRow = 'gongfa_fixture,faction_fixture,grade_mid,element_water,element_wind,1,1,1,1,1,1,1,realm_lianqi:1/1/1/1/1/1/1/1/1,chapter_fixture:realm_lianqi:0:0:0:0:0:0:0:0:desc_fixture,player'
    $spellHeader = 'name,type,minRange,maxRange,mpCost,cooldownTicks,physicalDamageMultiplier,soulDamageMultiplier,healAmount,cannotBlock,cannotDodge,penetratingShield,stunChance,realmReq,elementReq,element,sourceAffiliation,contentScope'
    $spellRow = 'spell_fixture,1,1,4,1,1,0,1,0,0,0,0,0,realm_lianqi,element_water_root,element_water,faction_fixture,player'
    $skillHeader = 'name,type,minRange,maxRange,mpCost,cooldownTicks,damageMultiplier,healAmount,cannotBlock,cannotDodge,penetratingShield,stunChance,isDomain,isBloodline,specialEffectDesc,element,realmReq,sourceAffiliation,contentScope'
    $skillRow = 'skill_fixture,1,1,4,1,1,1,0,0,0,0,0,0,0,desc_fixture,element_water,realm_lianqi,faction_fixture,player'
    $script:environmentProfileHeader = 'profileId,directedEdges,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs'

    Write-FixtureFile (Join-Path "docs/$cultivation" "$gongFa/fixture/fixture.txt") 'fixture'
    Write-FixtureFile (Join-Path "docs/$cultivation" "$spells/fixture/fixture.txt") 'fixture'
    Write-FixtureFile (Join-Path "docs/$cultivation" "$skills/fixture/fixture.txt") 'fixture'
    Write-FixtureFile 'src/Assets/DataConfig/GongFa.csv' "# fixture`n$gongFaHeader`n$gongFaRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/Spells.csv' "# fixture`n$spellHeader`n$spellRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/Skills.csv' "# fixture`n$skillHeader`n$skillRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/EnvironmentProfiles.csv' "# fixture`n$script:environmentProfileHeader`n"
    Write-FixtureFile 'src/Assets/DataConfig/Language.csv' "realm_lianqi,练气`n"
    Write-FixtureFile 'src/Assets/Data/GongFa/GongFa_gongfa_fixture.asset' "contentScope: player`n"
    Write-FixtureFile 'src/Assets/Data/Spells/Spell_spell_fixture.asset' "contentScope: player`nrealmRequirement: realm_lianqi`nelementRequirement: element_water_root`nsourceAffiliation: faction_fixture`n"
    Write-FixtureFile 'src/Assets/Data/Skills/Skill_skill_fixture.asset' "contentScope: player`nrealmRequirement: realm_lianqi`nsourceAffiliation: faction_fixture`n"
    Write-FixtureFile 'tools/data-chain-warning-waivers.json' '[]'
}

function Invoke-Checker {
    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checkerPath -ProjectRoot $fixtureRoot 2>&1 | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Assert-CheckerResult {
    param([string]$Name, [bool]$ShouldPass, [string]$ExpectedRuleId)

    $result = Invoke-Checker
    if ($ShouldPass -and $result.ExitCode -ne 0) { throw "$Name should pass but exited $($result.ExitCode):`n$($result.Output)" }
    if (-not $ShouldPass -and $result.ExitCode -eq 0) { throw "$Name should fail but exited zero." }
    if ($ExpectedRuleId -and $result.Output -notmatch [regex]::Escape($ExpectedRuleId)) { throw "$Name did not emit ${ExpectedRuleId}:`n$($result.Output)" }
    Write-Output "PASS $Name"
}

try {
    New-ValidFixture
    Assert-CheckerResult -Name 'valid fixture' -ShouldPass $true -ExpectedRuleId ''

    Write-FixtureFile (Join-Path "docs/$cultivation" "$spells/fixture/functional.txt") "# Functional fixture`n`n- 内容类型：功能术法。`n"
    Assert-CheckerResult -Name 'functional spell excluded from combat data count' -ShouldPass $true -ExpectedRuleId ''
    Remove-Item -LiteralPath (Join-Path $fixtureRoot (Join-Path "docs/$cultivation" "$spells/fixture/functional.txt"))

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/EnvironmentProfiles.csv')).Replace($script:environmentProfileHeader, "$script:environmentProfileHeader,unknown") | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/EnvironmentProfiles.csv')
    Assert-CheckerResult -Name 'environment schema unknown column' -ShouldPass $false -ExpectedRuleId 'CSV_SCHEMA_UNKNOWN_COLUMN'
    New-ValidFixture

    Add-Content -LiteralPath (Join-Path $fixtureRoot (Join-Path "docs/$cultivation" "$spells/fixture/extra.txt")) -Value 'extra'
    Assert-CheckerResult -Name 'docs CSV count mismatch' -ShouldPass $false -ExpectedRuleId 'DOC_CSV_COUNT_MISMATCH'
    Remove-Item -LiteralPath (Join-Path $fixtureRoot (Join-Path "docs/$cultivation" "$spells/fixture/extra.txt"))

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/Spells.csv')).Replace('faction_fixture,player', ',player') | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/Spells.csv')
    Assert-CheckerResult -Name 'required field missing' -ShouldPass $false -ExpectedRuleId 'REQUIRED_FIELD_EMPTY'
    New-ValidFixture

    Remove-Item -LiteralPath (Join-Path $fixtureRoot 'src/Assets/Data/Skills/Skill_skill_fixture.asset')
    Write-FixtureFile 'src/Assets/Data/Skills/Skill_skill_fixture.asset' 'm_Name: Skill_skill_fixture'
    Assert-CheckerResult -Name 'asset scope missing' -ShouldPass $false -ExpectedRuleId 'ASSET_CONTENT_SCOPE_MISSING'
    New-ValidFixture

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/Data/Spells/Spell_spell_fixture.asset')).Replace('element_water_root', 'element_fire_root') | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/Data/Spells/Spell_spell_fixture.asset')
    Assert-CheckerResult -Name 'asset requirement mismatch' -ShouldPass $false -ExpectedRuleId 'ASSET_REQUIREMENT_MISMATCH'
    New-ValidFixture

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/Spells.csv')).Replace('realm_lianqi', 'realm_lianxu') | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/Spells.csv')
    Assert-CheckerResult -Name 'deleted realm active' -ShouldPass $false -ExpectedRuleId 'DELETED_REALM_ACTIVE'
    New-ValidFixture

    Add-Content -LiteralPath (Join-Path $fixtureRoot (Join-Path "docs/$cultivation" "$spells/fixture/waived.txt")) -Value 'waived'
    Write-FixtureFile 'tools/data-chain-warning-waivers.json' '[{"ruleId":"DOC_CSV_COUNT_MISMATCH","subject":"Spells","reason":"Fixture proves exact waiver matching.","owner":"TQ-056 test","removalCondition":"Remove after fixture."}]'
    Assert-CheckerResult -Name 'exact approved warning' -ShouldPass $true -ExpectedRuleId 'DOC_CSV_COUNT_MISMATCH'

    Add-Content -LiteralPath (Join-Path $fixtureRoot (Join-Path "docs/$cultivation" "$skills/fixture/unwaived.txt")) -Value 'unwaived'
    Assert-CheckerResult -Name 'unwaived new warning' -ShouldPass $false -ExpectedRuleId 'DOC_CSV_COUNT_MISMATCH'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -Recurse -Force -LiteralPath $fixtureRoot }
}
