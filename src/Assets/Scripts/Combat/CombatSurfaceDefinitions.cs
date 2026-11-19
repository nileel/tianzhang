using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatSurfaceKind { None, Scorched, Burning, Wet, ShallowWater, DeepWater }
    public enum CombatTideAction { Rise, Ebb }

    public sealed class CombatSurfaceDefinition
    {
        public CombatSurfaceDefinition(HexCoord cell, CombatSurfaceKind kind, int fuel = 0,
            string sourceRef = null, string fuelObjectId = null)
        {
            if (!Enum.IsDefined(typeof(CombatSurfaceKind), kind) || fuel < 0)
                throw new ArgumentException("surface_definition_invalid");
            Cell = cell; Kind = kind; Fuel = fuel;
            SourceRef = string.IsNullOrWhiteSpace(sourceRef)?null:sourceRef;
            FuelObjectId = string.IsNullOrWhiteSpace(fuelObjectId)?null:fuelObjectId;
        }
        public HexCoord Cell { get; }
        public CombatSurfaceKind Kind { get; }
        public int Fuel { get; }
        public string SourceRef { get; }
        public string FuelObjectId { get; }
    }

    public sealed class CombatSurfaceCell
    {
        internal CombatSurfaceCell(CombatSurfaceDefinition value)
        { Cell=value.Cell; Kind=value.Kind; Fuel=value.Fuel; SourceRef=value.SourceRef; FuelObjectId=value.FuelObjectId; }
        public HexCoord Cell { get; }
        public CombatSurfaceKind Kind { get; internal set; }
        public int Fuel { get; internal set; }
        public string SourceRef { get; internal set; }
        public string FuelObjectId { get; }
    }

    public sealed class CombatFireDefinition
    {
        public CombatFireDefinition(string sourceRef, string actorId, int periodTicks, int durationTicks,
            int capacity, int fuelBudget, int attackCapacity, float damageMultiplier)
        {
            if (string.IsNullOrWhiteSpace(sourceRef) || string.IsNullOrWhiteSpace(actorId) || periodTicks < 1 ||
                durationTicks < periodTicks || capacity < 1 || fuelBudget < 1 || attackCapacity < 1 ||
                float.IsNaN(damageMultiplier) || float.IsInfinity(damageMultiplier) || damageMultiplier <= 0)
                throw new ArgumentException("fire_definition_invalid");
            SourceRef=sourceRef; ActorId=actorId; PeriodTicks=periodTicks; DurationTicks=durationTicks;
            Capacity=capacity; FuelBudget=fuelBudget; AttackCapacity=attackCapacity; DamageMultiplier=damageMultiplier;
        }
        public string SourceRef { get; }
        public string ActorId { get; }
        public int PeriodTicks { get; }
        public int DurationTicks { get; }
        public int Capacity { get; }
        public int FuelBudget { get; }
        public int AttackCapacity { get; }
        public float DamageMultiplier { get; }
    }

    public sealed class CombatFireState
    {
        internal CombatFireState(CombatFireDefinition definition) { Definition=definition; RemainingFuel=definition.FuelBudget; }
        public CombatFireDefinition Definition { get; }
        public int RemainingFuel { get; internal set; }
        public int ElapsedTicks { get; internal set; }
        public int RemainingTicks => Math.Max(0,Definition.DurationTicks-ElapsedTicks);
    }

    public sealed class CombatTideDefinition
    {
        public CombatTideDefinition(int radius, int waterlineOffset, int forceRank, int distanceBudget,
            int chargeExtraActionPoints, int chargeExtraMana, int chargeTicks, int eyeExtraActionPoints,
            int eyeExtraMana, int attackCapacity, float waterMultiplier, float chargeMultiplier, int erosionDamage)
        {
            if (radius != 1 || waterlineOffset != 1 || forceRank < 1 || distanceBudget < 1 ||
                chargeExtraActionPoints < 1 || chargeExtraMana < 0 || chargeTicks < 1 || eyeExtraActionPoints < 1 ||
                eyeExtraMana < 0 || attackCapacity < 1 || waterMultiplier <= 0 || chargeMultiplier < 1 ||
                float.IsNaN(waterMultiplier) || float.IsInfinity(waterMultiplier) ||
                float.IsNaN(chargeMultiplier) || float.IsInfinity(chargeMultiplier) || erosionDamage < 1)
                throw new ArgumentException("tide_definition_invalid");
            Radius=radius; WaterlineOffset=waterlineOffset; ForceRank=forceRank; DistanceBudget=distanceBudget;
            ChargeExtraActionPoints=chargeExtraActionPoints; ChargeExtraMana=chargeExtraMana; ChargeTicks=chargeTicks;
            EyeExtraActionPoints=eyeExtraActionPoints; EyeExtraMana=eyeExtraMana; AttackCapacity=attackCapacity;
            WaterMultiplier=waterMultiplier; ChargeMultiplier=chargeMultiplier; ErosionDamage=erosionDamage;
        }
        public int Radius { get; }
        public int WaterlineOffset { get; }
        public int ForceRank { get; }
        public int DistanceBudget { get; }
        public int ChargeExtraActionPoints { get; }
        public int ChargeExtraMana { get; }
        public int ChargeTicks { get; }
        public int EyeExtraActionPoints { get; }
        public int EyeExtraMana { get; }
        public int AttackCapacity { get; }
        public float WaterMultiplier { get; }
        public float ChargeMultiplier { get; }
        public int ErosionDamage { get; }
    }

    public readonly struct CombatSurfaceChange
    {
        public CombatSurfaceChange(HexCoord cell, CombatSurfaceKind before, CombatSurfaceKind after)
        { Cell=cell; Before=before; After=after; }
        public HexCoord Cell { get; }
        public CombatSurfaceKind Before { get; }
        public CombatSurfaceKind After { get; }
    }

    public sealed class CombatTidePush
    {
        internal CombatTidePush(string actorId, int direction, CombatForcedMovementResult movement)
        { ActorId=actorId; Direction=direction; Movement=movement; }
        public string ActorId { get; }
        public int Direction { get; }
        public CombatForcedMovementResult Movement { get; }
        public int FallDamage { get; internal set; }
    }

    public sealed class CombatSurfaceSettlement
    {
        internal CombatSurfaceSettlement(long sequence, string sourceRef, int tick,
            IEnumerable<CombatSurfaceChange> changes, IEnumerable<CombatTidePush> pushes,
            IEnumerable<CombatDamageResult> damage, IEnumerable<CombatSceneObjectChange> objectChanges,
            IEnumerable<CombatDamageResult> fallDamage = null, IEnumerable<CombatSurfaceTargetSnapshot> targets = null,
            HexCoord? tideSource = null, CombatTideAction? tideAction = null)
        {
            Sequence=sequence; SourceRef=sourceRef; Tick=tick;
            TideSource = tideSource; TideAction = tideAction;
            Changes=Array.AsReadOnly(changes.ToArray()); Pushes=Array.AsReadOnly(pushes.ToArray());
            Damage=Array.AsReadOnly(damage.ToArray()); SceneObjectChanges=Array.AsReadOnly(objectChanges.ToArray());
            FallDamage=Array.AsReadOnly((fallDamage??Array.Empty<CombatDamageResult>()).ToArray());
            Targets=Array.AsReadOnly((targets??Array.Empty<CombatSurfaceTargetSnapshot>()).ToArray());
        }
        public long Sequence { get; }
        public HexCoord? TideSource { get; }
        public CombatTideAction? TideAction { get; }
        public string SourceRef { get; }
        public int Tick { get; }
        public IReadOnlyList<CombatSurfaceChange> Changes { get; }
        public IReadOnlyList<CombatTidePush> Pushes { get; }
        public IReadOnlyList<CombatDamageResult> Damage { get; }
        public IReadOnlyList<CombatDamageResult> FallDamage { get; }
        public IReadOnlyList<CombatSurfaceTargetSnapshot> Targets { get; }
        public IReadOnlyList<CombatSceneObjectChange> SceneObjectChanges { get; }
    }

    public sealed class CombatSurfaceTargetSnapshot
    {
        internal CombatSurfaceTargetSnapshot(CombatantSnapshot actor)
        { ActorId=actor.Id;Pose=actor.Pose;Health=actor.CurrentHealth;IsAlive=actor.IsAlive; }
        public string ActorId { get; }
        public SpatialPose Pose { get; }
        public int Health { get; }
        public bool IsAlive { get; }
    }
}
