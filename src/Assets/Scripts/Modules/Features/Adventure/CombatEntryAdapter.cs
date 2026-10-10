using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    public sealed class CombatEntryAdapter
    {
        private readonly CombatOrdinaryActionDefinition[] ordinaryActions;
        private readonly CombatRuleSet rules;
        private readonly string ruleFailure;
        public CombatEntryAdapter(IEnumerable<CombatOrdinaryActionDefinition> ordinaryActions, CombatRuleSetData ruleData)
        {
            if (ruleData == null) ruleFailure = "combat_rule_set_missing";
            else if (!ruleData.TryValidateIdentity(out ruleFailure)) { }
            else
            {
                try
                {
                    rules = CombatRuleSet.Parse(new[] { CombatRuleSet.RealmColumns }.Concat(ruleData.ExportRealms()).ToArray(),
                        new[] { CombatRuleSet.TierColumns }.Concat(ruleData.ExportTiers()).ToArray(),
                        new[] { CombatRuleSet.EconomyColumns }.Concat(ruleData.ExportEconomy()).ToArray(), ruleData.sourceDigest);
                }
                catch (Exception exception) when (exception is System.IO.InvalidDataException || exception is OverflowException || exception is ArgumentException)
                { ruleFailure = "combat_rule_set_invalid:" + exception.Message; }
            }
            this.ordinaryActions = ordinaryActions?.Select(value => value == null ? null :
                new CombatOrdinaryActionDefinition(value.Kind, value.ActionPointCostUnits,
                    value.MinimumNetCostUnits, value.CtPenalty, value.ReuseCooldownTicks)).ToArray();
        }

        public bool TryCreateSession(
            AdventureSpawnSet spawned,
            AttackProfileData[] attackProfiles,
            EnvironmentProfileAsset environmentProfile,
            out CombatSession session,
            out string reason)
        {
            session = null;
            if (rules == null) { reason = ruleFailure ?? "combat_rule_set_missing"; return false; }
            if (spawned == null)
            {
                reason = "adventure_spawn_set_missing";
                return false;
            }
            if (!TryProjectProfiles(attackProfiles, out var profiles, out reason)) return false;
            foreach (CombatantSnapshot actor in new[] { spawned.Player, spawned.Enemy })
                if (!string.IsNullOrEmpty(actor.BasicAttackProfileId) &&
                    !profiles.Any(profile => profile.Id == actor.BasicAttackProfileId && profile.Kind == CombatAttackKind.Basic))
                { reason = "combat_session_basic_attack_binding_invalid"; return false; }

            if (environmentProfile == null ||
                environmentProfile.battlefieldCells == null ||
                environmentProfile.battlefieldCells.Length == 0 ||
                !environmentProfile.TryCreateDefinition(out _, out reason))
            {
                reason ??= "environment_profile_not_configured";
                return false;
            }

            var grid = new TacticalGridModel();
            foreach (EnvironmentBattlefieldCell cell in environmentProfile.battlefieldCells)
            {
                grid.SetTile(new TacticalTileData(new HexCoord(cell.q, cell.r))
                {
                    TerrainType = cell.blocksGroundMove ? TacticalTerrainType.Obstacle : TacticalTerrainType.Plain,
                    BlocksGroundMove = cell.blocksGroundMove,
                    BlocksFlyingMove = false,
                    BlocksLineOfSight = cell.blocksLineOfSight,
                    BlocksLanding = cell.blocksGroundMove,
                    IsEntityObstacle = false,
                });
            }
            if (!SpatialQueryBoardFactory.TryCreate(grid, environmentProfile, out SpatialQuerySnapshot spatial, out reason))
                return false;

            try
            {
                session = new CombatSession(
                    new[] { spawned.Player, spawned.Enemy },
                    profiles,
                    new SpatialCombatQuery(spatial.Board),
                    ordinaryActions,
                    spatial.Environment.SpiritRecoveryEnvironment switch
                    {
                        SpiritRecoveryEnvironment.Ordinary => 1f,
                        SpiritRecoveryEnvironment.SpiritVeinNode or SpiritRecoveryEnvironment.BlessedLand => 2f,
                        _ => throw new ArgumentException("spirit_recovery_environment_not_configured"),
                    }, rules);
                reason = null;
                return true;
            }
            catch (ArgumentException exception)
            {
                reason = "combat_session_setup_invalid:" + exception.Message;
                return false;
            }
        }

        private bool TryProjectProfiles(
            AttackProfileData[] inputs,
            out IReadOnlyList<CombatAttackProfile> profiles,
            out string reason)
        {
            var result = new List<CombatAttackProfile>();
            foreach (AttackProfileData profile in inputs ?? Array.Empty<AttackProfileData>())
            {
                if (profile == null) { profiles = null; reason = "attack_profile_missing"; return false; }
                if (!profile.TryValidate(out reason)) { profiles = null; return false; }
                if (profile.targetingMode != AttackTargetingMode.Single) continue;
                if (profile.ruleSetVersion != rules.RuleSetVersion || profile.ruleSetSourceDigest != rules.SourceDigest ||
                    !rules.Tiers.Any(tier => tier.AbilityTierId == profile.abilityTierId) ||
                    (profile.profileKind == AttackProfileKind.Basic && profile.basicBindingKind != BasicAttackBindingKind.MainEquipment))
                { profiles = null; reason = "attack_profile_rule_reference_invalid"; return false; }
                CombatAttackEffect effect = profile.effectType switch
                {
                    AttackEffectType.Physical => CombatAttackEffect.Physical,
                    AttackEffectType.Magic => CombatAttackEffect.Soul,
                    AttackEffectType.Hybrid => CombatAttackEffect.Hybrid,
                    AttackEffectType.Heal => CombatAttackEffect.Heal,
                    AttackEffectType.RestoreSpirit => CombatAttackEffect.RestoreSpirit,
                    AttackEffectType.Buff => CombatAttackEffect.Buff,
                    AttackEffectType.Debuff => CombatAttackEffect.Debuff,
                    _ => (CombatAttackEffect)(-1),
                };
                if ((int)effect < 0) continue;
                string realmRequirement = profile.realmRequirementId;
                string elementRequirement = profile.elementRequirementId;
                Func<string, string, string, bool> qualification = profile.profileKind == AttackProfileKind.Basic
                    ? null
                    : (realmStage, rootElement, rootGrade) => AbilityRequirementPolicy.IsSatisfied(
                        realmStage, rootElement, rootGrade, realmRequirement, elementRequirement);
                result.Add(new CombatAttackProfile(
                    profile.attackProfileId,
                    profile.profileKind switch
                    {
                        AttackProfileKind.Basic => CombatAttackKind.Basic,
                        AttackProfileKind.Art => CombatAttackKind.Art,
                        AttackProfileKind.Divine => CombatAttackKind.Divine,
                        _ => throw new ArgumentOutOfRangeException(),
                    },
                    effect,
                    profile.minCastRange,
                    profile.maxCastRange,
                    profile.actionPointCostUnits,
                    profile.minimumNetCostUnits,
                    profile.ctPenalty,
                    profile.reuseCooldownTicks,
                    profile.physicalDamageMultiplier,
                    profile.soulDamageMultiplier,
                    profile.healAmount,
                    profile.resourceCost,
                    profile.damageElementId,
                    profile.defensePenetration,
                    profile.spiritRecoveryMultiplier,
                    profile.minimumSpiritRecovery,
                    profile.otherTargetResourceCost,
                    profile.otherTargetRecoveryMultiplier,
                    qualification, ruleMetadata: new CombatAttackRuleMetadata(profile.ruleSetVersion, profile.ruleSetSourceDigest,
                        rules.GetTier(profile.abilityTierId), profile.cooldownClass, profile.effectBudgetMode,
                        profile.effectBudgetMode == "damage_budget" ? new CombatRuleRatio(profile.efficiencyNumerator, profile.efficiencyDenominator) : (CombatRuleRatio?)null,
                        profile.effectBudgetMode == "damage_budget" ? new CombatRuleRatio(profile.damageMigrationNumerator, profile.damageMigrationDenominator) : (CombatRuleRatio?)null,
                        profile.effectBudgetMode == "damage_budget" ? new CombatRuleRatio(profile.damageWeightNumerator, profile.damageWeightDenominator) : (CombatRuleRatio?)null,
                        profile.statusId, profile.statusAttributeId, profile.statusRelativePermille, profile.statusDurationOwnTurnEnds,
                        profile.statusStackLimit, profile.statusRefreshPolicy, profile.controlChancePermille, profile.controlUsesMindContest)));
            }
            if (result.Count == 0 || result.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            {
                profiles = null;
                reason = "combat_session_attack_profile_invalid";
                return false;
            }
            profiles = result;
            reason = null;
            return true;
        }

        private sealed class SpatialCombatQuery : ICombatSpatialQuery
        {
            private readonly SpatialQueryBoard board;
            public SpatialCombatQuery(SpatialQueryBoard spatialBoard) => board = spatialBoard;

            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange)
            {
                SpatialRangeEntry result = board.QueryRangeEntry(
                    source, target, minimumRange, maximumRange, SpatialQueryKind.Attack, true);
                return new CombatRangeQueryResult(result.IsInRange, result.Reason);
            }

            public CombatMovementQueryResult QueryMovement(
                HexCoord source,
                HexCoord destination,
                int actionPointBudgetUnits,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied)
            {
                SpatialMovementPathResult result = board.QueryMovementPath(source, destination,
                    actionPointBudgetUnits, movementEfficiencyMilliUnits, occupied);
                return new CombatMovementQueryResult(result.Path.Count > 0, result.Reason, result.Path,
                    result.Edges, result.UnitsPerRange, result.ActionPointCostUnits);
            }

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int actionPointBudgetUnits,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied) => board.FindReachableWithinActionPoints(
                    source, actionPointBudgetUnits, movementEfficiencyMilliUnits, occupied);
        }
    }
}
