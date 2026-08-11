using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public readonly struct CombatRangeQueryResult
    {
        public CombatRangeQueryResult(bool isInRange, string reason)
        {
            IsInRange = isInRange;
            Reason = reason ?? string.Empty;
        }

        public bool IsInRange { get; }
        public string Reason { get; }
    }

    /// <summary>Combat receives spatial facts through this narrow query boundary.</summary>
    public interface ICombatSpatialQuery
    {
        CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange);
    }

    /// <summary>Pure session aggregate; it has no scene registration or production composition role.</summary>
    public sealed class CombatSession
    {
        private readonly Dictionary<string, CombatAttackProfile> profiles;

        public CombatSession(
            IEnumerable<CombatantSnapshot> combatants,
            IEnumerable<CombatAttackProfile> attackProfiles,
            ICombatSpatialQuery spatialQuery)
        {
            if (spatialQuery == null)
                throw new ArgumentNullException(nameof(spatialQuery));
            Combatants = new CombatantRegistry(combatants);
            ValidateSideCardinality(Combatants.All);
            profiles = new Dictionary<string, CombatAttackProfile>(StringComparer.Ordinal);
            if (attackProfiles == null)
                throw new ArgumentNullException(nameof(attackProfiles));
            foreach (CombatAttackProfile profile in attackProfiles)
            {
                if (profile == null || !profiles.TryAdd(profile.Id, profile))
                    throw new ArgumentException("Attack profiles must have unique non-null IDs.", nameof(attackProfiles));
            }

            SpatialQuery = spatialQuery;
            TurnScheduler = new CombatTurnScheduler(Combatants.All);
        }

        public CombatantRegistry Combatants { get; }
        public ICombatSpatialQuery SpatialQuery { get; }
        public CombatTurnScheduler TurnScheduler { get; }

        public bool TryGetProfile(string id, out CombatAttackProfile profile)
        {
            profile = null;
            return !string.IsNullOrWhiteSpace(id) && profiles.TryGetValue(id, out profile);
        }

        private static void ValidateSideCardinality(IReadOnlyList<CombatantSnapshot> combatants)
        {
            int players = 0;
            int enemies = 0;
            foreach (CombatantSnapshot combatant in combatants)
            {
                if (combatant.Team == CombatTeam.Player)
                    players++;
                else
                    enemies++;
            }
            if (players is < 1 or > 2 || players != enemies)
                throw new ArgumentException("Combat sessions require matched 1v1 or 2v2 teams.", nameof(combatants));
        }
    }
}
