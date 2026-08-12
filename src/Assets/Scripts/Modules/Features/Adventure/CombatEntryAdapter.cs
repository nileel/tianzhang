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
        public bool TryCreateSession(
            AdventureSpawnSet spawned,
            AttackProfileData[] attackProfiles,
            EnvironmentProfileAsset environmentProfile,
            out CombatSession session,
            out string reason)
        {
            session = null;
            if (spawned == null)
            {
                reason = "adventure_spawn_set_missing";
                return false;
            }
            if (!TryProjectProfiles(attackProfiles, out var profiles, out reason)) return false;

            var grid = new TacticalGridModel();
            const int radius = 12;
            for (int q = -radius; q <= radius; q++)
            for (int r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
                grid.SetTile(new TacticalTileData(new HexCoord(q, r)));
            if (!SpatialQueryBoardFactory.TryCreate(grid, environmentProfile, out SpatialQuerySnapshot spatial, out reason))
                return false;

            try
            {
                session = new CombatSession(
                    new[] { spawned.Player, spawned.Enemy },
                    profiles,
                    new SpatialCombatQuery(spatial.Board));
                reason = null;
                return true;
            }
            catch (ArgumentException exception)
            {
                reason = "combat_session_setup_invalid:" + exception.Message;
                return false;
            }
        }

        private static bool TryProjectProfiles(
            AttackProfileData[] inputs,
            out IReadOnlyList<CombatAttackProfile> profiles,
            out string reason)
        {
            var result = new List<CombatAttackProfile>();
            foreach (AttackProfileData profile in inputs ?? Array.Empty<AttackProfileData>())
            {
                if (profile == null || !profile.TryValidate(out _) || profile.targetingMode != AttackTargetingMode.Single)
                    continue;
                CombatAttackEffect effect = profile.effectType switch
                {
                    AttackEffectType.Physical => CombatAttackEffect.Physical,
                    AttackEffectType.Magic => CombatAttackEffect.Soul,
                    AttackEffectType.Hybrid => CombatAttackEffect.Hybrid,
                    AttackEffectType.Heal => CombatAttackEffect.Heal,
                    _ => (CombatAttackEffect)(-1),
                };
                if ((int)effect < 0) continue;
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
                    profile.physicalDamageMultiplier,
                    profile.soulDamageMultiplier,
                    profile.healAmount,
                    profile.resourceCost,
                    profile.cooldownTicks,
                    profile.damageElementId,
                    profile.defensePenetration));
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
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied)
            {
                IReadOnlyDictionary<HexCoord, int> reachable = board.FindReachable(source, movementPoints, occupied);
                if (!reachable.TryGetValue(destination, out int cost))
                    return new CombatMovementQueryResult(false, SpatialQueryReasons.NoLegalPath, null, 0);
                IReadOnlyList<HexCoord> path = board.FindPath(source, destination, movementPoints, occupied);
                int movementCost = (cost + board.UnitsPerRange - 1) / board.UnitsPerRange;
                return new CombatMovementQueryResult(path.Count > 0, SpatialQueryReasons.Ok, path, movementCost);
            }

            public IReadOnlyDictionary<HexCoord, int> FindReachable(
                HexCoord source,
                int movementPoints,
                IReadOnlyCollection<HexCoord> occupied) => board.FindReachable(source, movementPoints, occupied);
        }
    }
}
