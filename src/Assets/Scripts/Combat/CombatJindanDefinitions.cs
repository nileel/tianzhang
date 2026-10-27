using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Explicit content projections; no Unity assets or private resource pools.</summary>
    public sealed class CombatJindanAbilityDefinition
    {
        public CombatJindanAbilityDefinition(string effectId, bool enabled, int actionPoints, int mana,
            int cooldown, int closeActionPoints, int upkeepMana, int range, int verticalReach,
            int capacity, int supportDurability, int remoteMana, int visionRange,
            IEnumerable<int> metricModes, string unavailableReason, int durationOwnTurnEnds = 0, CombatTideDefinition tide = null,
            CombatSealDefinition seal = null, CombatFireDomainDefinition fireDomain = null)
        {
            if (string.IsNullOrWhiteSpace(effectId) || actionPoints <= 0 || mana < 0 || cooldown < 0 ||
                closeActionPoints <= 0 || upkeepMana < 0 || range < 0 || verticalReach < 0 || capacity < 1 ||
                visionRange < 1) throw new ArgumentException("jindan_ability_definition_invalid");
            EffectId = effectId; Enabled = enabled; ActionPoints = actionPoints; Mana = mana;
            Cooldown = cooldown; CloseActionPoints = closeActionPoints; UpkeepMana = upkeepMana;
            Range = range; VerticalReach = verticalReach; Capacity = capacity;
            SupportDurability = supportDurability; RemoteMana = remoteMana; VisionRange = visionRange;
            MetricModes = Array.AsReadOnly((metricModes ?? Array.Empty<int>()).ToArray());
            UnavailableReason = unavailableReason ?? "jindan_effect_not_implemented";
            DurationOwnTurnEnds = durationOwnTurnEnds;
            Tide = tide;
            Seal = seal;
            FireDomain = fireDomain;
            if (enabled && effectId == CombatFireDomainRules.Effect && (fireDomain == null || durationOwnTurnEnds < 1 || supportDurability <= 0))
                throw new ArgumentException("fire_domain_definition_missing");
            if (enabled && effectId == "SPACE_SEAL" && (seal == null || supportDurability != checked(seal.LayerDurability * 2) || capacity != 1))
                throw new ArgumentException("jindan_seal_definition_invalid");
            if (enabled && effectId == "WATER_TIDE" && tide == null) throw new ArgumentException("tide_definition_missing");
            if (enabled && effectId == "SPACE_SCALE" && (supportDurability <= 0 || remoteMana < 0 || MetricModes.Count != 2 ||
                MetricModes.Any(value => value <= 0))) throw new ArgumentException("jindan_scale_definition_invalid");
            if (enabled && effectId is "EARTH_MANIFEST" or "EARTH_GRAVITY" && durationOwnTurnEnds < 1)
                throw new ArgumentException("jindan_earth_duration_invalid");
        }
        public string EffectId { get; }
        public bool Enabled { get; }
        public int ActionPoints { get; }
        public int Mana { get; }
        public int Cooldown { get; }
        public int CloseActionPoints { get; }
        public int UpkeepMana { get; }
        public int Range { get; }
        public int VerticalReach { get; }
        public int Capacity { get; }
        public int SupportDurability { get; }
        public int RemoteMana { get; }
        public int VisionRange { get; }
        public IReadOnlyList<int> MetricModes { get; }
        public string UnavailableReason { get; }
        public int DurationOwnTurnEnds { get; }
        public CombatTideDefinition Tide { get; }
        public CombatSealDefinition Seal { get; }
        public CombatFireDomainDefinition FireDomain { get; }
    }

    public sealed class CombatJindanSeatDefinition
    {
        public CombatJindanSeatDefinition(string actorId, string seatId, string positionId,
            string positionType, string abilityInstanceId, IEnumerable<string> candidates, string initialEffect)
        {
            ActorId = actorId; SeatId = seatId; PositionId = positionId; PositionType = positionType;
            AbilityInstanceId = abilityInstanceId; Candidates = Array.AsReadOnly(candidates.ToArray());
            InitialEffect = initialEffect;
            if (new[] { actorId, seatId, positionId, positionType, abilityInstanceId }.Any(string.IsNullOrWhiteSpace) ||
                Candidates.Count == 0 || Candidates.Distinct(StringComparer.Ordinal).Count() != Candidates.Count ||
                !Candidates.Contains(initialEffect)) throw new ArgumentException("jindan_seat_definition_invalid");
        }
        public string ActorId { get; }
        public string SeatId { get; }
        public string PositionId { get; }
        public string PositionType { get; }
        public string AbilityInstanceId { get; }
        public IReadOnlyList<string> Candidates { get; }
        public string InitialEffect { get; }
    }

    public sealed class CombatJindanRequest
    {
        public CombatJindanRequest(string seatId, IEnumerable<HexCoord> edgePath = null,
            int metricDistanceUnits = 0, HexCoord? remoteOrigin = null, CombatEarthMode? earthMode = null,
            string earthTargetId = null, HexCoord? selectedTargetCell = null, CombatTideAction? tideAction = null,
            HexCoord? tideSource = null, bool charged = false, bool createTideEye = false)
        {
            SeatId = seatId ?? string.Empty;
            EdgePath = Array.AsReadOnly((edgePath ?? Array.Empty<HexCoord>()).ToArray());
            MetricDistanceUnits = metricDistanceUnits; RemoteOrigin = remoteOrigin;
            EarthMode = earthMode; EarthTargetId = earthTargetId; SelectedTargetCell = selectedTargetCell;
            TideAction=tideAction; TideSource=tideSource; Charged=charged; CreateTideEye=createTideEye;
        }
        public string SeatId { get; }
        public IReadOnlyList<HexCoord> EdgePath { get; }
        public int MetricDistanceUnits { get; }
        public HexCoord? RemoteOrigin { get; }
        public CombatEarthMode? EarthMode { get; }
        public string EarthTargetId { get; }
        public HexCoord? SelectedTargetCell { get; }
        public CombatTideAction? TideAction { get; }
        public HexCoord? TideSource { get; }
        public bool Charged { get; }
        public bool CreateTideEye { get; }
        internal string Facts => SeatId + ":" + MetricDistanceUnits + ":" + RemoteOrigin + ":" +
            string.Join("/", EdgePath) + ":" + EarthMode + ":" + EarthTargetId + ":" + SelectedTargetCell +
            ":" + TideAction + ":" + TideSource + ":" + Charged + ":" + CreateTideEye;
    }

    public sealed class CombatJindanSeatState
    {
        internal CombatJindanSeatState(CombatJindanSeatDefinition definition)
        { Definition = definition; EffectId = definition.InitialEffect; }
        public CombatJindanSeatDefinition Definition { get; }
        public string EffectId { get; internal set; }
        public long LastSwitchTurn { get; internal set; } = -1;
        public long LockedOwnTurn { get; internal set; } = -1;
        public int TotalManaSpent { get; internal set; }
        public long TotalActionPointSpent { get; internal set; }
        public int SuccessfulDeployments { get; internal set; }
        public int MaintenancePayments { get; internal set; }
        public CombatScaleInstance ActiveScale { get; internal set; }
        public CombatSealInstance ActiveSeal { get; internal set; }
        public CombatEarthInstance ActiveEarth { get; internal set; }
        public CombatFireDomainState FireDomain { get; internal set; }
        public CombatPendingTide PendingTide { get; internal set; }
        public int OwnedSurfaceCount { get; internal set; }
        public string LastEndReason { get; internal set; } = string.Empty;
    }

    public sealed class CombatScaleInstance
    {
        internal CombatScaleInstance(string supportId, int createdTick, HexCoord origin,
            IEnumerable<HexCoord> path, int metric, CombatSceneObject support)
        {
            SupportId = supportId; CreatedTick = createdTick; LastPaidTick = createdTick;
            Origin = origin; Path = Array.AsReadOnly(path.ToArray()); MetricDistanceUnits = metric; Support = support;
        }
        public string SupportId { get; }
        public int CreatedTick { get; }
        public int LastPaidTick { get; internal set; }
        public HexCoord Origin { get; }
        public IReadOnlyList<HexCoord> Path { get; }
        public int MetricDistanceUnits { get; }
        public CombatSceneObject Support { get; }
    }
}
