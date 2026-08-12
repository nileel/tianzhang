using System;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Spatial;
using UnityEngine;
using EntityCharacter = TianZhang.Entity.Character;

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
            EntityCharacter enemyCharacter = EntityCharacter.FromData(enemyData.combatTemplate, enemyPosition);
            CombatantSnapshot enemySnapshot = CreateEnemy(enemyCharacter);
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
                source.AbilityLoadout.KnownSpells,
                EntityCharacter.MaxCombatSwaps,
                0)
            {
                GongFaId = source.Progression.GongFaId,
            };
            snapshot.SetSpirit(source.Resources.MaximumSpirit, source.Resources.CurrentSpirit);
            return snapshot;
        }

        private static CombatantSnapshot CreateEnemy(EntityCharacter source)
        {
            var snapshot = new CombatantSnapshot(
                "enemy",
                CombatTeam.Enemy,
                source.Position,
                source.Reaction,
                source.MaxHP,
                source.CurrentHP,
                source.PhysAtk,
                source.MagAtk,
                source.PhysDef,
                source.MagDef,
                source.RealmMultiplier,
                source.MovePoints,
                source.EquippedSpellIds,
                source.AvailableSpells,
                EntityCharacter.MaxCombatSwaps,
                0)
            {
                BlockRate = source.BlockRate,
                BlockReduction = source.BlockReduction,
                SoulShieldRate = source.SoulShieldRate,
                SoulShieldReduction = source.SoulShieldReduction,
                DodgeRate = source.DodgeRate,
                CriticalRate = source.CritRate,
                CriticalDamage = source.CritDamage,
                HitRateBonus = source.HitRateBonus,
                GongFaId = source.GongFaName,
            };
            snapshot.SetSpirit(source.MaxMP, source.CurrentMP);
            return snapshot;
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
