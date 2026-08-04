[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$checkerPath = Join-Path $repoRoot 'tools/check-data-chain.ps1'
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("tianzhang-data-chain-test-" + [Guid]::NewGuid())

function Copy-FixtureSource {
    param([string]$RelativePath)

    $sourcePath = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required checker fixture source is missing: $RelativePath"
    }

    $destinationPath = Join-Path $fixtureRoot $RelativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destinationPath) | Out-Null
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

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
    $script:charterRuleDefinitionHeader = 'ruleEntryId,displayName,ruleFamily,relationElement,compatiblePhenomena,positiveCommit,negativeCommit,requiredAuthority,requiredNodeTypes,scopeType,scopeTierCap,anchorNodeIds,propagationBoundaryProfileId,currentCoverageSet,affectedWorldVariables,conflictProfileId,failurePolicy,worldEventOutputs'
    $script:charterSiteHeader = 'siteId,displayNameKey,settlementId,passageCapabilityId,passageOperatorId,passageTargetId,passageProtocolState,passageStructureState,passagePowerState,interactionTimeProfileId,recognitionTiming,operationTiming,cancellationPolicy,facilityId,sealRelicId,sealManagerId,sealBeneficiaryId,sealAuthorizationVersionId,ruleEntryId,ruleEntryOccupancyId,nodeOccupancyId,jindanConflictEventId,jindanChallengeEventId,grantId,grantDefinitionVersion,grantTargetVariableId,grantChallengerId,grantQualificationSource,grantAllowedOperationId,grantTargetId,grantScopeId,grantBeneficiaryId,grantRealityAnchorId,grantResourceLedgerRef,grantCapacityLedgerRef,grantChallengeRuleTier,grantEffectiveAtTick,grantExpiresAtTick,grantIsRevoked,grantRevocationReason,grantDisplaySource,leftCandidateId,leftCandidateTargetVariableId,leftCandidateTargetId,leftCandidateHasVariableAuthority,leftCandidateHasLegalTarget,leftCandidatePositionRank,leftCandidateRealityAnchorRank,leftCandidateAlreadyPaidCost,leftCandidateHasActiveContinuousCarrier,leftCandidateConflictReserve,leftCandidatePulseCost,leftCandidateSettlementCooldown,rightCandidateId,rightCandidateTargetVariableId,rightCandidateTargetId,rightCandidateHasVariableAuthority,rightCandidateHasLegalTarget,rightCandidatePositionRank,rightCandidateRealityAnchorRank,rightCandidateAlreadyPaidCost,rightCandidateHasActiveContinuousCarrier,rightCandidateConflictReserve,rightCandidatePulseCost,rightCandidateSettlementCooldown,charterCandidateId,yuanyingConflictEventId,yuanyingTargetVariableId,yuanyingTargetId,yuanyingScopeId,yuanyingRealityAnchorId'
    $script:charterSiteRow = 'charter_site_old_water_station,charter_site_old_water_station,guanzhong_city,capability_kaihe_jiuzhang_v1,operator_old_water_station,gate_old_water_station_pump,compatible,intact,available,interaction_time_old_water_station_gate_v1,instant,sustained_guided,no_commit_on_cancel,facility_old_water_station,relic_taixuan_realm_seal,manager_old_water_station,beneficiary_water_basin,authorization_taixuan_seal_old_water_station_management_v1,charter_entry_suifu_diji,occupancy_suifu_diji_v1,occupancy_suifu_waterworks_v1,conflict_suifu_water_spirit_001,challenge_suifu_001,cross_tier_charter_water_basin_v1,1,water_element_spirit_flow,jindan_challenger,JindanProtection,charter_apply,node_old_water_station_waterworks,scope_suifu_water_basin,beneficiary_water_basin,anchor_suifu_waterway,ledger_suifu_resource,ledger_suifu_capacity,1,0,500,false,none,charter_site_old_water_station,jindan_left,water_element_spirit_flow,node_old_water_station_waterworks,true,true,3,1,2,true,6,2,3,jindan_right,water_element_spirit_flow,node_old_water_station_waterworks,true,true,2,1,2,true,6,2,3,jindan_right,anchor_suifu_water_001,wetland_waterline_state,node_old_water_station_river_wetland,scope_suifu_water_basin,anchor_yuanying_road'

    Write-FixtureFile (Join-Path "docs/$cultivation" "$gongFa/fixture/fixture.txt") 'fixture'
    Write-FixtureFile (Join-Path "docs/$cultivation" "$spells/fixture/fixture.txt") 'fixture'
    Write-FixtureFile (Join-Path "docs/$cultivation" "$skills/fixture/fixture.txt") 'fixture'
    Write-FixtureFile 'src/Assets/DataConfig/GongFa.csv' "# fixture`n$gongFaHeader`n$gongFaRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/Spells.csv' "# fixture`n$spellHeader`n$spellRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/Skills.csv' "# fixture`n$skillHeader`n$skillRow`n"
    Write-FixtureFile 'src/Assets/DataConfig/EnvironmentProfiles.csv' "# fixture`n$script:environmentProfileHeader`n"
    Write-FixtureFile 'src/Assets/DataConfig/CharterRuleDefinitions.csv' "# fixture`n$script:charterRuleDefinitionHeader`n"
    Write-FixtureFile 'src/Assets/DataConfig/CharterSites.csv' "# fixture`n$script:charterSiteHeader`n$script:charterSiteRow`n"
    Write-FixtureFile 'src/Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset' @'
  siteId: charter_site_old_water_station
  displayNameKey: charter_site_old_water_station
  settlementId: guanzhong_city
  passageCapabilityId: capability_kaihe_jiuzhang_v1
  interactionTimeProfileId: interaction_time_old_water_station_gate_v1
  grantId: cross_tier_charter_water_basin_v1
  charterCandidateId: jindan_right
'@
    Write-FixtureFile 'src/Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset.meta' @'
fileFormatVersion: 2
guid: d22b5344c9094d70a4755bec21554c95
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
'@
    Write-FixtureFile 'src/Assets/DataConfig/Language.csv' "realm_lianqi,练气`n"
    Write-FixtureFile 'src/Assets/Data/GongFa/GongFa_gongfa_fixture.asset' "contentScope: player`n"
    Write-FixtureFile 'src/Assets/Data/Spells/Spell_spell_fixture.asset' "contentScope: player`nrealmRequirement: realm_lianqi`nelementRequirement: element_water_root`nsourceAffiliation: faction_fixture`n"
    Write-FixtureFile 'src/Assets/Data/Skills/Skill_skill_fixture.asset' "contentScope: player`nrealmRequirement: realm_lianqi`nsourceAffiliation: faction_fixture`n"
    Write-FixtureFile 'tools/data-chain-warning-waivers.json' '[]'

    foreach ($relativePath in @(
        'src/Assets/DataConfig/Language.csv',
        'src/Assets/DataConfig/Settlements.csv',
        'src/Assets/DataConfig/Items.csv',
        'src/Assets/DataConfig/Bounties.csv',
        'src/Assets/DataConfig/Enemies.csv',
        'src/Assets/DataConfig/FoundationPurpleMansionStates.csv',
        'src/Assets/DataConfig/JindanStaticStates.csv',
        'src/Assets/DataConfig/NpcCultivationActionWeightProfiles.csv',
        'src/Assets/Data/Settlements/Settlement_guanzhong_city.asset',
        'src/Assets/Data/Items/Item_item_lingshi_low.asset',
        'src/Assets/Data/Items/Item_item_shijia_piece.asset',
        'src/Assets/Data/Bounties/Bounty_bounty_guanzhong_shijiahou.asset',
        'src/Assets/Data/Enemies/Enemy_enemy_shijiahou.asset',
        'src/Assets/Data/ContentCatalog/ContentCatalog.asset',
        'src/Assets/Data/NpcCultivationActionWeightProfiles/NpcCultivationActionWeightProfile_npc-cultivation-production-v1.asset'
    )) {
        Copy-FixtureSource $relativePath
    }
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

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterRuleDefinitions.csv')).Replace($script:charterRuleDefinitionHeader, "$script:charterRuleDefinitionHeader,unknown") | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterRuleDefinitions.csv')
    Assert-CheckerResult -Name 'charter schema unknown column' -ShouldPass $false -ExpectedRuleId 'CSV_SCHEMA_UNKNOWN_COLUMN'
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

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')).Replace($script:charterSiteHeader, "$script:charterSiteHeader,unknown") | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')
    Assert-CheckerResult -Name 'charter site schema unknown column' -ShouldPass $false -ExpectedRuleId 'CSV_SCHEMA_UNKNOWN_COLUMN'
    New-ValidFixture

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')).Replace('jindan_right,anchor_suifu_water_001', 'jindan_left,anchor_suifu_water_001') | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')
    Assert-CheckerResult -Name 'charter site charter side undeclared' -ShouldPass $false -ExpectedRuleId 'CHARTER_SITE_CHARTER_SIDE_UNDECLARED'
    New-ValidFixture

    (Get-Content -Raw (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')).Replace('true,true,3,1,2,true,6,2,3,jindan_right,water_element_spirit_flow', 'true,true,2,1,2,true,6,2,3,jindan_right,water_element_spirit_flow') | Set-Content -Encoding utf8 (Join-Path $fixtureRoot 'src/Assets/DataConfig/CharterSites.csv')
    Assert-CheckerResult -Name 'charter site charter side not stable' -ShouldPass $false -ExpectedRuleId 'CHARTER_SITE_CHARTER_SIDE_NOT_STABLE'
    New-ValidFixture
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -Recurse -Force -LiteralPath $fixtureRoot }
}
