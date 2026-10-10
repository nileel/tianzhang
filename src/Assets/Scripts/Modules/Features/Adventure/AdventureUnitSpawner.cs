using System;
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
            string[] playerDivineProfileIds)
        {
            Player = player;
            Enemy = enemy;
            EnemyData = enemyData;
            PlayerBasicProfileId = playerBasicProfileId;
            EnemyBasicProfileId = enemyBasicProfileId;
            PlayerDivineProfileIds = playerDivineProfileIds ?? Array.Empty<string>();
        }

        public CombatantSnapshot Player { get; }
        public CombatantSnapshot Enemy { get; }
        public EnemyData EnemyData { get; }
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
            string playerBasic = ResolveBasicAttackBinding(
                player.MainEquipmentBasicAttackProfileId,
                player.UnarmedBasicAttackProfileId);
            if (playerBasic == null)
            {
                reason = "adventure_player_basic_attack_binding_invalid";
                return false;
            }
            string enemyBasic = ResolveBasicAttackBinding(
                enemyData.combatTemplate.mainEquipmentBasicAttackProfileId,
                enemyData.combatTemplate.unarmedBasicAttackProfileId);
            if (enemyBasic == null)
            {
                reason = "adventure_enemy_basic_attack_binding_invalid";
                return false;
            }

            CombatantSnapshot playerSnapshot;
            CombatantSnapshot enemySnapshot;
            try
            {
                // Rebuild from authoritative inputs at entry; a stale derived snapshot cannot override the table.
                CharacterRuntimeProfile.RequirePlayerRealm(player.Progression.RealmStage);
                player = CharacterRuntimeProfile.FromSnapshot(player, catalog.CombatRuleSet).Capture();
                playerSnapshot = CreatePlayer(player, playerPosition, playerBasic);
                enemySnapshot = CreateEnemy(enemyData.combatTemplate, enemyPosition, enemyBasic, catalog.CombatRuleSet);
            }
            catch (ArgumentException) { reason = "adventure_growth_inputs_invalid"; return false; }
            catch (OverflowException) { reason = "adventure_growth_inputs_invalid"; return false; }
            spawned = new AdventureSpawnSet(
                playerSnapshot,
                enemySnapshot,
                enemyData,
                playerBasic,
                enemyBasic,
                (string[])player.AbilityLoadout.EquippedSkills.Clone());
            reason = null;
            return true;
        }

        private static string ResolveBasicAttackBinding(string mainEquipment, string unarmed)
        {
            bool hasMain = !string.IsNullOrWhiteSpace(mainEquipment);
            bool hasUnarmed = !string.IsNullOrWhiteSpace(unarmed);
            if (hasUnarmed)
                return null;
            return hasMain ? mainEquipment : string.Empty;
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

        private static CombatantSnapshot CreateEnemy(CharacterData source, HexCoord position, string basicAttackProfileId, CombatRuleSetData growthRules)
        {
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(source, growthRules);
            CharacterCombatModifiers combatModifiers = CharacterCombatModifiers.FromDefinition(source);
            float realmMultiplier = source.realmMultiplier > 0f ? source.realmMultiplier : 1f;
            CharacterDerivedAttributes derived = attributes.Derive(realmMultiplier, combatModifiers.ToAttributeBonuses());
            var snapshot = new CombatantSnapshot(
                "enemy",
                CombatTeam.Enemy,
                position,
                attributes.Reaction,
                derived.MaxHealth,
                derived.MaxHealth,
                derived.PhysicalAttack,
                derived.MagicAttack,
                derived.PhysicalDefense,
                derived.MagicDefense,
                attributes.ActionPointMaximumUnits,
                attributes.MovementEfficiencyMilliUnits,
                realmMultiplier,
                basicAttackProfileId: basicAttackProfileId,
                rawSpirit: attributes.Spirit,
                realmStage: source.realmStage,
                visibleRootElement: source.visibleRootElement,
                visibleRootGrade: source.visibleRootGrade,
                actionEconomyModifiers: Array.Empty<CombatActionEconomyModifier>())
            {
                BlockRate = combatModifiers.BlockRate,
                BlockReduction = combatModifiers.BlockReduction,
                SoulShieldRate = combatModifiers.SoulShieldRate,
                SoulShieldReduction = combatModifiers.SoulShieldReduction,
                DodgeRate = combatModifiers.DodgeRate,
                CriticalRate = combatModifiers.CritRate,
                CriticalDamage = combatModifiers.CritDamage,
                HitRateBonus = combatModifiers.HitRateBonus,
                GongFaId = source.gongFaName,
                GongFaElement = CombatElementFacts.ResolveGongFaElement(source.gongFaName),
            };
            snapshot.SetSpirit(derived.MaxSpirit, derived.MaxSpirit);
            return snapshot;
        }

    }
}
