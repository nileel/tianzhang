using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Spatial;
using UnityEngine;

namespace TianZhang.Features.Adventure
{
    public sealed class AdventureSpawnSet
    {
        public AdventureSpawnSet(
            CombatantSnapshot player,
            CombatantSnapshot enemy,
            EnemyData enemyData,
            string playerBasicProfileId,
            string enemyBasicProfileId,
            string[] playerDivineProfileIds,
            IReadOnlyList<CombatantSnapshot> additionalEnemies = null,
            EnemyCombatStats enemyStats = null)
        {
            Player = player;
            Enemy = enemy;
            Enemies = new[] { enemy }.Concat(additionalEnemies ?? Array.Empty<CombatantSnapshot>()).ToArray();
            EnemyData = enemyData;
            EnemyStats = enemyStats;
            PlayerBasicProfileId = playerBasicProfileId;
            EnemyBasicProfileId = enemyBasicProfileId;
            PlayerDivineProfileIds = playerDivineProfileIds ?? Array.Empty<string>();
        }

        public CombatantSnapshot Player { get; }
        public CombatantSnapshot Enemy { get; }
        public IReadOnlyList<CombatantSnapshot> Enemies { get; }
        public EnemyData EnemyData { get; }
        public EnemyCombatStats EnemyStats { get; }
        public string PlayerBasicProfileId { get; }
        public string EnemyBasicProfileId { get; }
        public string[] PlayerDivineProfileIds { get; }
    }

    public sealed class AdventureUnitSpawner : MonoBehaviour
    {
        public bool TrySpawn(
            CharacterStateSnapshot player,
            ContentCatalogData catalog,
            AdventureNodeData startNode,
            AdventureNodeData encounterNode,
            out AdventureSpawnSet spawned,
            out string reason)
        {
            spawned = null;
            if (player == null)
            {
                reason = "adventure_player_missing";
                return false;
            }
            if (!player.Attributes.ActionEconomyInputsPresent || player.CombatModifiers.ActionEconomyInputs.Count != 0)
            {
                reason = "adventure_action_economy_inputs_invalid";
                return false;
            }
            if (catalog == null || encounterNode == null ||
                !catalog.TryGetEnemy(encounterNode.contentId, out EnemyData enemyData) ||
                enemyData.combatTemplate == null)
            {
                reason = "adventure_enemy_unresolved";
                return false;
            }
            if (startNode == null || (startNode.q == encounterNode.q && startNode.r == encounterNode.r))
            {
                reason = "adventure_spawn_coordinate_invalid";
                return false;
            }
            var playerPosition = new HexCoord(startNode.q, startNode.r);
            var enemyPosition = new HexCoord(encounterNode.q, encounterNode.r);
            string playerBasic;
            string enemyBasic;

            CombatantSnapshot playerSnapshot;
            CombatantSnapshot enemySnapshot;
            EnemyCombatStats enemyStats;
            var additionalSnapshots = new List<CombatantSnapshot>();
            try
            {
                // Rebuild from authoritative inputs at entry; a stale derived snapshot cannot override the table.
                CharacterRuntimeProfile.RequirePlayerRealm(player.Progression.RealmStage);
                var playerProfile = CharacterRuntimeProfile.FromSnapshot(player, catalog.CombatRuleSet);
                if (!playerProfile.ValidateEquipmentState(catalog, true))
                { reason = "adventure_player_basic_attack_binding_invalid"; return false; }
                player = playerProfile.Capture();
                var enemyProfile = CharacterRuntimeProfile.FromDefinition("enemy", enemyData.combatTemplate, catalog.CombatRuleSet);
                if (!enemyProfile.ValidateEquipmentState(catalog, true))
                { reason = "adventure_enemy_basic_attack_binding_invalid"; return false; }
                playerBasic = ResolveBasicAttackBinding(player.MainEquipmentBasicAttackProfileId, player.UnarmedBasicAttackProfileId);
                enemyBasic = ResolveBasicAttackBinding(enemyProfile.MainEquipmentBasicAttackProfileId, enemyProfile.UnarmedBasicAttackProfileId);
                playerSnapshot = CreatePlayer(player, playerPosition, playerBasic);
                enemyStats = ProjectEnemyStats(enemyData.combatTemplate, catalog.CombatRuleSet, enemyData.combatStatScale);
                enemySnapshot = CreateEnemy(enemyData.combatTemplate, enemyStats, enemyPosition, enemyBasic);
                var ids = new HashSet<string>(StringComparer.Ordinal) { "player", "enemy" };
                var positions = new HashSet<HexCoord> { playerPosition, enemyPosition };
                foreach (AdventureEnemySpawnData extra in encounterNode.additionalEnemies ?? Array.Empty<AdventureEnemySpawnData>())
                {
                    if (extra == null || string.IsNullOrWhiteSpace(extra.instanceId) || !ids.Add(extra.instanceId) ||
                        !positions.Add(new HexCoord(extra.q, extra.r)))
                    { reason = "adventure_spawn_coordinate_or_identity_invalid"; return false; }
                    additionalSnapshots.Add(CreateEnemy(enemyData.combatTemplate, enemyStats, new HexCoord(extra.q, extra.r),
                        enemyBasic, extra.instanceId));
                }
            }
            catch (ArgumentException) { reason = "adventure_growth_inputs_invalid"; return false; }
            catch (OverflowException) { reason = "adventure_growth_inputs_invalid"; return false; }
            spawned = new AdventureSpawnSet(
                playerSnapshot,
                enemySnapshot,
                enemyData,
                playerBasic,
                enemyBasic,
                (string[])player.AbilityLoadout.EquippedSkills.Clone(), additionalSnapshots, enemyStats);
            reason = null;
            return true;
        }

        private static string ResolveBasicAttackBinding(string mainEquipment, string unarmed)
        {
            bool hasMain = !string.IsNullOrWhiteSpace(mainEquipment);
            bool hasUnarmed = !string.IsNullOrWhiteSpace(unarmed);
            if (!hasUnarmed) throw new ArgumentException("character_unarmed_binding_invalid");
            return hasMain ? mainEquipment : unarmed;
        }

        private static CombatantSnapshot CreatePlayer(CharacterStateSnapshot source, HexCoord position, string basicAttackProfileId)
        {
            var attributes = new CharacterAttributes(
                source.Attributes.RootBone,
                source.Attributes.Physique,
                source.Attributes.Spirit,
                source.Attributes.Mind,
                source.Attributes.Reaction,
                source.Attributes.Talent,
                source.Attributes.Fortune,
                source.Attributes.ActionPointMaximumUnits,
                source.Attributes.MovementEfficiencyMilliUnits,
                source.Attributes.ActionEconomyInputsPresent);
            CharacterDerivedAttributes derived = attributes.Derive(
                source.Progression.RealmMultiplier,
                source.CombatModifiers.ToAttributeBonuses());
            var snapshot = new CombatantSnapshot(
                "player",
                CombatTeam.Player,
                position,
                source.Attributes.Reaction,
                source.Resources.MaximumHealth,
                source.Resources.CurrentHealth,
                derived.PhysicalAttack,
                derived.MagicAttack,
                derived.PhysicalDefense,
                derived.MagicDefense,
                source.Attributes.ActionPointMaximumUnits,
                source.Attributes.MovementEfficiencyMilliUnits,
                source.Progression.RealmMultiplier,
                equippedArtProfileIds: source.AbilityLoadout.EquippedSpells,
                availableArtProfileIds: source.AbilityLoadout.KnownSpells,
                basicAttackProfileId: basicAttackProfileId,
                rawSpirit: source.Attributes.Spirit,
                rawMind: source.Attributes.Mind,
                realmStage: source.Progression.RealmStage,
                visibleRootElement: source.VisibleRootElement,
                visibleRootGrade: source.VisibleRootGrade,
                actionEconomyModifiers: Array.Empty<CombatActionEconomyModifier>())
            {
                BlockRate = source.CombatModifiers.BlockRate,
                BlockReduction = source.CombatModifiers.BlockReduction,
                SoulShieldRate = source.CombatModifiers.SoulShieldRate,
                SoulShieldReduction = source.CombatModifiers.SoulShieldReduction,
                DodgeRate = source.CombatModifiers.DodgeRate,
                CriticalRate = source.CombatModifiers.CritRate,
                CriticalDamage = source.CombatModifiers.CritDamage,
                HitRateBonus = source.CombatModifiers.HitRateBonus,
                GongFaId = source.Progression.GongFaId,
                GongFaElement = CombatElementFacts.ResolveGongFaElement(source.Progression.GongFaId),
            };
            snapshot.SetSpirit(source.Resources.MaximumSpirit, source.Resources.CurrentSpirit);
            return snapshot;
        }

        public static EnemyCombatStats ProjectEnemyStats(CharacterData source, CombatRuleSetData growthRules, float scale)
        {
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(source, growthRules);
            CharacterCombatModifiers combatModifiers = CharacterCombatModifiers.FromDefinition(source);
            float realmMultiplier = source.realmMultiplier > 0f ? source.realmMultiplier : 1f;
            CharacterDerivedAttributes derived = attributes.Derive(realmMultiplier, combatModifiers.ToAttributeBonuses());
            return new EnemyCombatStats
            {
                rootBone = attributes.RootBone, physique = attributes.Physique,
                spirit = attributes.Spirit, mind = attributes.Mind, reaction = attributes.Reaction, talent = attributes.Talent,
                maximumHealth = derived.MaxHealth, maximumSpirit = derived.MaxSpirit,
                physicalAttack = derived.PhysicalAttack, soulAttack = derived.MagicAttack,
                physicalDefense = derived.PhysicalDefense, soulDefense = derived.MagicDefense,
                actionPointMaximumUnits = attributes.ActionPointMaximumUnits,
                movementEfficiencyMilliUnits = attributes.MovementEfficiencyMilliUnits,
                hpBonus = combatModifiers.HpBonus, mpBonus = combatModifiers.MpBonus,
                physAtkBonus = combatModifiers.PhysAtkBonus, magAtkBonus = combatModifiers.MagAtkBonus,
                physDefBonus = combatModifiers.PhysDefBonus, magDefBonus = combatModifiers.MagDefBonus,
                blockRate = combatModifiers.BlockRate, blockReduction = combatModifiers.BlockReduction,
                soulShieldRate = combatModifiers.SoulShieldRate, soulShieldReduction = combatModifiers.SoulShieldReduction,
                dodgeRate = combatModifiers.DodgeRate, critRate = combatModifiers.CritRate,
                critDamage = combatModifiers.CritDamage, hitRateBonus = combatModifiers.HitRateBonus,
            }.Scale(scale);
        }

        private static CombatantSnapshot CreateEnemy(CharacterData source, EnemyCombatStats stats, HexCoord position,
            string basicAttackProfileId, string instanceId = "enemy")
        {
            var snapshot = new CombatantSnapshot(
                instanceId,
                CombatTeam.Enemy,
                position,
                stats.reaction,
                stats.maximumHealth,
                stats.maximumHealth,
                stats.physicalAttack,
                stats.soulAttack,
                stats.physicalDefense,
                stats.soulDefense,
                stats.actionPointMaximumUnits,
                stats.movementEfficiencyMilliUnits,
                source.realmMultiplier,
                basicAttackProfileId: basicAttackProfileId,
                rawSpirit: stats.spirit,
                rawMind: stats.mind,
                realmStage: source.realmStage,
                visibleRootElement: source.visibleRootElement,
                visibleRootGrade: source.visibleRootGrade,
                actionEconomyModifiers: Array.Empty<CombatActionEconomyModifier>())
            {
                BlockRate = stats.blockRate,
                BlockReduction = stats.blockReduction,
                SoulShieldRate = stats.soulShieldRate,
                SoulShieldReduction = stats.soulShieldReduction,
                DodgeRate = stats.dodgeRate,
                CriticalRate = stats.critRate,
                CriticalDamage = stats.critDamage,
                HitRateBonus = stats.hitRateBonus,
                GongFaId = source.gongFaName,
                GongFaElement = CombatElementFacts.ResolveGongFaElement(source.gongFaName),
            };
            snapshot.SetSpirit(stats.maximumSpirit, stats.maximumSpirit);
            return snapshot;
        }

    }
}
