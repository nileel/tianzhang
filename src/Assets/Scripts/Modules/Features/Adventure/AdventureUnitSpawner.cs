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
            string[] playerDivineProfileIds,
            GameObject playerMarker,
            GameObject enemyMarker)
        {
            Player = player;
            Enemy = enemy;
            EnemyData = enemyData;
            PlayerBasicProfileId = playerBasicProfileId;
            EnemyBasicProfileId = enemyBasicProfileId;
            PlayerDivineProfileIds = playerDivineProfileIds ?? Array.Empty<string>();
            PlayerMarker = playerMarker;
            EnemyMarker = enemyMarker;
        }

        public CombatantSnapshot Player { get; }
        public CombatantSnapshot Enemy { get; }
        public EnemyData EnemyData { get; }
        public string PlayerBasicProfileId { get; }
        public string EnemyBasicProfileId { get; }
        public string[] PlayerDivineProfileIds { get; }
        public GameObject PlayerMarker { get; }
        public GameObject EnemyMarker { get; }
    }

    public sealed class AdventureUnitSpawner : MonoBehaviour
    {
        public bool TrySpawn(
            CharacterStateSnapshot player,
            ContentCatalogData catalog,
            AdventureNodeData startNode,
            AdventureNodeData encounterNode,
            GameObject unitMarkerPrefab,
            out AdventureSpawnSet spawned,
            out string reason)
        {
            spawned = null;
            if (player == null)
            {
                reason = "adventure_player_missing";
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
            if (unitMarkerPrefab == null)
            {
                reason = "adventure_unit_marker_missing";
                return false;
            }

            var playerPosition = new HexCoord(startNode.q, startNode.r);
            var enemyPosition = new HexCoord(encounterNode.q, encounterNode.r);
            CombatantSnapshot playerSnapshot = CreatePlayer(player, playerPosition);
            CombatantSnapshot enemySnapshot = CreateEnemy(enemyData.combatTemplate, enemyPosition);
            GameObject playerMarker = InstantiateMarker(unitMarkerPrefab, playerPosition, "PlayerMarker", Color.cyan);
            GameObject enemyMarker = InstantiateMarker(unitMarkerPrefab, enemyPosition, "EnemyMarker", Color.red);
            string playerBasic = !string.IsNullOrWhiteSpace(player.MainEquipmentBasicAttackProfileId)
                ? player.MainEquipmentBasicAttackProfileId
                : player.UnarmedBasicAttackProfileId;
            string enemyBasic = !string.IsNullOrWhiteSpace(enemyData.combatTemplate.mainEquipmentBasicAttackProfileId)
                ? enemyData.combatTemplate.mainEquipmentBasicAttackProfileId
                : enemyData.combatTemplate.unarmedBasicAttackProfileId;
            spawned = new AdventureSpawnSet(
                playerSnapshot,
                enemySnapshot,
                enemyData,
                playerBasic,
                enemyBasic,
                (string[])player.AbilityLoadout.EquippedSkills.Clone(),
                playerMarker,
                enemyMarker);
            reason = null;
            return true;
        }

        private static CombatantSnapshot CreatePlayer(CharacterStateSnapshot source, HexCoord position)
        {
            var attributes = new CharacterAttributes(
                source.Attributes.RootBone,
                source.Attributes.Physique,
                source.Attributes.Spirit,
                source.Attributes.Mind,
                source.Attributes.Reaction,
                source.Attributes.Talent,
                source.Attributes.Fortune);
            CharacterDerivedAttributes derived = attributes.Derive(
                source.Progression.RealmMultiplier,
                CharacterAttributeBonuses.Empty);
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
                source.Progression.RealmMultiplier,
                Mathf.Clamp(Mathf.RoundToInt(source.Attributes.Reaction / 20f), 2, 8),
                source.AbilityLoadout.EquippedSpells,
                source.AbilityLoadout.KnownSpells)
            {
                GongFaId = source.Progression.GongFaId,
            };
            snapshot.SetSpirit(source.Resources.MaximumSpirit, source.Resources.CurrentSpirit);
            return snapshot;
        }

        private static CombatantSnapshot CreateEnemy(CharacterData source, HexCoord position)
        {
            CharacterAttributes attributes = CharacterAttributes.FromDefinition(source);
            float realmMultiplier = source.realmMultiplier > 0f ? source.realmMultiplier : 1f;
            CharacterDerivedAttributes derived = attributes.Derive(
                realmMultiplier,
                new CharacterAttributeBonuses
                {
                    Health = Mathf.RoundToInt(source.hpBonus),
                    SpiritResource = Mathf.RoundToInt(source.mpBonus),
                    PhysicalAttack = Mathf.RoundToInt(source.physAtkBonus),
                    MagicAttack = Mathf.RoundToInt(source.magAtkBonus),
                    PhysicalDefense = Mathf.RoundToInt(source.physDefBonus),
                    MagicDefense = Mathf.RoundToInt(source.magDefBonus),
                });
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
                realmMultiplier,
                Mathf.Clamp(Mathf.RoundToInt(attributes.Reaction / 20f), 2, 8),
                ProjectEquippedSpells(source, realmMultiplier),
                source.availableSpells)
            {
                BlockRate = source.blockRate,
                BlockReduction = source.blockReduction,
                SoulShieldRate = source.soulShieldRate,
                SoulShieldReduction = source.soulShieldReduction,
                DodgeRate = source.dodgeRate,
                CriticalRate = source.critRate,
                CriticalDamage = source.critDamage,
                HitRateBonus = source.hitRateBonus,
                GongFaId = source.gongFaName,
            };
            snapshot.SetSpirit(derived.MaxSpirit, derived.MaxSpirit);
            return snapshot;
        }

        private static string[] ProjectEquippedSpells(CharacterData source, float realmMultiplier)
        {
            string[] equipped = source.equippedSpells ?? Array.Empty<string>();
            int slotLimit = source.maxSpellSlots > 0
                ? source.maxSpellSlots
                : DefaultSpellSlots(realmMultiplier) + MansionSpellSlotBonus(source);
            if (slotLimit <= 0)
                return Array.Empty<string>();
            if (equipped.Length <= slotLimit)
                return (string[])equipped.Clone();

            var result = new string[slotLimit];
            Array.Copy(equipped, result, slotLimit);
            return result;
        }

        private static int DefaultSpellSlots(float realmMultiplier)
        {
            if (realmMultiplier >= 3f) return 5;
            if (realmMultiplier >= 1.5f) return 4;
            return 0;
        }

        private static int MansionSpellSlotBonus(CharacterData source)
        {
            if (source.foundationPurpleMansionState != null || source.developedMansions == null)
                return 0;

            var seen = new System.Collections.Generic.HashSet<string>();
            int bonus = 0;
            foreach (string mansion in source.developedMansions)
            {
                if (string.IsNullOrWhiteSpace(mansion) || !seen.Add(mansion))
                    continue;
                if ((mansion == "命府" || mansion == "魂府" || mansion == "气府") && bonus < 3)
                    bonus++;
            }
            return bonus;
        }

        private static GameObject InstantiateMarker(
            GameObject prefab,
            HexCoord coord,
            string objectName,
            Color color)
        {
            GameObject marker = Instantiate(prefab, ToWorld(coord), Quaternion.identity);
            marker.name = objectName;
            SpriteRenderer renderer = marker.GetComponentInChildren<SpriteRenderer>();
            if (renderer != null) renderer.color = color;
            return marker;
        }

        private static Vector3 ToWorld(HexCoord coord)
        {
            return new Vector3(coord.Q + coord.R * 0.5f, coord.R * 0.8660254f, 0f);
        }
    }
}
