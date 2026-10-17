using System;
using System.Collections.Generic;

namespace TianZhang.Gameplay.Contracts
{
    public sealed class CombatBountyTrackerSnapshot
    {
        public CombatBountyTrackerSnapshot(string id, string title, int progress, int required, bool completed)
        { Id = id; Title = title; Progress = progress; Required = required; Completed = completed; }
        public string Id { get; }
        public string Title { get; }
        public int Progress { get; }
        public int Required { get; }
        public bool Completed { get; }
    }

    public enum CombatActionKind { BasicAttack, Art, Divine, Guard, Wait, EndTurn, Move, SwapSpell, SwapEquipment, UseItem }

    public sealed class CombatActionCandidate
    {
        public CombatActionCandidate(CombatActionKind kind, string actorId, string targetId, string profileId, long contextVersion,
            CombatUnitPresentationHex? destination = null, int movementCostUnits = 0,
            IReadOnlyList<CombatUnitPresentationHex> movementPath = null, int slotIndex = -1,
            string equipmentSlotId = null, string itemId = null, string instanceId = null, int? attackDirection = null)
        {
            Kind = kind;
            AttackDirection = attackDirection;
            ActorId = actorId;
            TargetId = targetId;
            ProfileId = profileId;
            ContextVersion = contextVersion;
            SlotIndex = slotIndex;
            EquipmentSlotId = equipmentSlotId; ItemId = itemId; InstanceId = instanceId;
            Destination = destination;
            MovementCostUnits = movementCostUnits;
            MovementPath = movementPath == null ? Array.Empty<CombatUnitPresentationHex>() :
                new List<CombatUnitPresentationHex>(movementPath).AsReadOnly();
        }
        public string EquipmentSlotId { get; }
        public int? AttackDirection { get; }
        public string ItemId { get; }
        public string InstanceId { get; }
        public CombatActionKind Kind { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public string ProfileId { get; }
        public long ContextVersion { get; }
        public int SlotIndex { get; }
        public CombatUnitPresentationHex? Destination { get; }
        public int MovementCostUnits { get; }
        public IReadOnlyList<CombatUnitPresentationHex> MovementPath { get; }
    }

    public sealed class CombatAttackAreaCellSnapshot
    {
        public CombatAttackAreaCellSnapshot(CombatUnitPresentationHex position, bool isLegal, string reason)
        { Position = position; IsLegal = isLegal; Reason = reason ?? string.Empty; }
        public CombatUnitPresentationHex Position { get; }
        public bool IsLegal { get; }
        public string Reason { get; }
    }

    public sealed class CombatAttackAreaSnapshot
    {
        public CombatAttackAreaSnapshot(CombatUnitPresentationHex origin, int direction,
            IReadOnlyList<CombatAttackAreaCellSnapshot> cells, IReadOnlyList<CombatantHudSnapshot> targets)
        {
            Origin = origin; Direction = direction;
            Cells = new List<CombatAttackAreaCellSnapshot>(cells).AsReadOnly();
            Targets = new List<CombatantHudSnapshot>(targets).AsReadOnly();
        }
        public CombatUnitPresentationHex Origin { get; }
        public int Direction { get; }
        public IReadOnlyList<CombatAttackAreaCellSnapshot> Cells { get; }
        public IReadOnlyList<CombatantHudSnapshot> Targets { get; }
    }

    public sealed class CombatActionPreviewSnapshot
    {
        public CombatActionPreviewSnapshot(CombatActionCandidate candidate, string actionName, string actorName,
            string targetName, bool canConfirm, string reason, int spiritCost, bool remainsInTurn,
            float settledCt, float threshold, long ticksUntilReady, int actionPointCostUnits = 0,
            int ctPenalty = 0, int reuseCooldownTicks = 0, int remainingCooldownTicks = 0,
            string replacedSpellName = "", int combatSwapsRemaining = 0, int itemQuantityCost = 0,
            int itemAvailableQuantity = 0, int itemHealthRecovery = 0, int expectedHealthRecovery = 0, long inventoryVersion = 0,
            int committedUseCount = 0, int repeatMultiplierPermille = 1000, string ruleSetVersion = "",
            string ruleSetSourceDigest = "", string abilityTierId = "", string basicAttackSourceKind = "",
            string unarmedRealmId = "", int physicalAttackCapacity = 0, string attackDetail = "",
            string currentEquipmentName = "", bool? equipmentOwned = null, CombatAttackAreaSnapshot attackArea = null)
        {
            Candidate = candidate;
            ActionName = actionName;
            ActorName = actorName;
            TargetName = targetName;
            CanConfirm = canConfirm;
            Reason = reason;
            SpiritCost = spiritCost;
            RemainsInTurn = remainsInTurn;
            SettledCt = settledCt;
            Threshold = threshold;
            TicksUntilReady = ticksUntilReady;
            ActionPointCostUnits = actionPointCostUnits;
            CtPenalty = ctPenalty;
            ReuseCooldownTicks = reuseCooldownTicks;
            RemainingCooldownTicks = remainingCooldownTicks;
            ReplacedSpellName = replacedSpellName;
            CombatSwapsRemaining = combatSwapsRemaining;
            ItemQuantityCost = itemQuantityCost; ItemAvailableQuantity = itemAvailableQuantity;
            ItemHealthRecovery = itemHealthRecovery; ExpectedHealthRecovery = expectedHealthRecovery; InventoryVersion = inventoryVersion;
            CommittedUseCount = committedUseCount; RepeatMultiplierPermille = repeatMultiplierPermille;
            RuleSetVersion = ruleSetVersion; RuleSetSourceDigest = ruleSetSourceDigest; AbilityTierId = abilityTierId;
            BasicAttackSourceKind = basicAttackSourceKind; UnarmedRealmId = unarmedRealmId;
            PhysicalAttackCapacity = physicalAttackCapacity; AttackDetail = attackDetail;
            CurrentEquipmentName = currentEquipmentName; EquipmentOwned = equipmentOwned;
            AttackArea = attackArea;
        }
        public CombatActionCandidate Candidate { get; }
        public string ActionName { get; }
        public string ActorName { get; }
        public string TargetName { get; }
        public bool CanConfirm { get; }
        public string Reason { get; }
        public int SpiritCost { get; }
        public bool RemainsInTurn { get; }
        public float SettledCt { get; }
        public float Threshold { get; }
        public long TicksUntilReady { get; }
        public int ActionPointCostUnits { get; }
        public int CtPenalty { get; }
        public int ReuseCooldownTicks { get; }
        public int RemainingCooldownTicks { get; }
        public string ReplacedSpellName { get; }
        public int CombatSwapsRemaining { get; }
        public int ItemQuantityCost { get; }
        public int ItemAvailableQuantity { get; }
        public int ItemHealthRecovery { get; }
        public int ExpectedHealthRecovery { get; }
        public long InventoryVersion { get; }
        public int CommittedUseCount { get; }
        public int RepeatMultiplierPermille { get; }
        public string RuleSetVersion { get; }
        public string RuleSetSourceDigest { get; }
        public string AbilityTierId { get; }
        public string BasicAttackSourceKind { get; }
        public string UnarmedRealmId { get; }
        public int PhysicalAttackCapacity { get; }
        public string AttackDetail { get; }
        public string CurrentEquipmentName { get; }
        public bool? EquipmentOwned { get; }
        public CombatAttackAreaSnapshot AttackArea { get; }
    }

    public enum CombatUnitPresentationEvent
    {
        Idle,
        Move,
        Attack,
        Hit,
        Cast,
        Death,
    }

    public enum CombatUnitDisplayFaction
    {
        Player,
        Enemy,
    }

    [Serializable]
    public readonly struct CombatUnitPresentationHex
    {
        public CombatUnitPresentationHex(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int Q { get; }
        public int R { get; }
    }

    [Serializable]
    public sealed class CombatUnitPresentationDescriptor
    {
        public CombatUnitPresentationDescriptor(
            string combatantId,
            string presentationProfileId,
            CombatUnitDisplayFaction displayFaction,
            CombatUnitPresentationHex position,
            int facing)
        {
            if (string.IsNullOrWhiteSpace(combatantId))
                throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (string.IsNullOrWhiteSpace(presentationProfileId))
                throw new ArgumentException("Presentation profile ID is required.", nameof(presentationProfileId));
            if (displayFaction < CombatUnitDisplayFaction.Player || displayFaction > CombatUnitDisplayFaction.Enemy)
                throw new ArgumentOutOfRangeException(nameof(displayFaction));
            if (facing < 0 || facing >= 6)
                throw new ArgumentOutOfRangeException(nameof(facing));

            CombatantId = combatantId;
            PresentationProfileId = presentationProfileId;
            DisplayFaction = displayFaction;
            Position = position;
            Facing = facing;
        }

        public string CombatantId { get; }
        public string PresentationProfileId { get; }
        public CombatUnitDisplayFaction DisplayFaction { get; }
        public CombatUnitPresentationHex Position { get; }
        public int Facing { get; }
    }

    [Serializable]
    public sealed class CombatUnitPresentationTargetResult
    {
        public CombatUnitPresentationTargetResult(string combatantId, int finalDamage, bool isDead, int restoredSpirit = 0)
        {
            if (string.IsNullOrWhiteSpace(combatantId))
                throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (finalDamage < 0 || restoredSpirit < 0)
                throw new ArgumentOutOfRangeException(nameof(finalDamage));

            CombatantId = combatantId;
            FinalDamage = finalDamage;
            IsDead = isDead;
            RestoredSpirit = restoredSpirit;
        }

        public string CombatantId { get; }
        public int FinalDamage { get; }
        public bool IsDead { get; }
        public int RestoredSpirit { get; }
    }

    [Serializable]
    public sealed class CombatUnitPresentationEventProjection
    {
        public CombatUnitPresentationEventProjection(
            string actorCombatantId,
            CombatUnitPresentationEvent presentationEvent,
            CombatUnitPresentationHex startPosition,
            CombatUnitPresentationHex endPosition,
            int facing,
            IReadOnlyList<CombatUnitPresentationTargetResult> targetResults,
            IReadOnlyList<CombatUnitPresentationHex> movementPath = null)
        {
            if (string.IsNullOrWhiteSpace(actorCombatantId))
                throw new ArgumentException("Actor combatant ID is required.", nameof(actorCombatantId));
            if (presentationEvent < CombatUnitPresentationEvent.Idle ||
                presentationEvent > CombatUnitPresentationEvent.Death)
                throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            if (facing < 0 || facing >= 6)
                throw new ArgumentOutOfRangeException(nameof(facing));

            ActorCombatantId = actorCombatantId;
            PresentationEvent = presentationEvent;
            StartPosition = startPosition;
            EndPosition = endPosition;
            Facing = facing;
            TargetResults = targetResults == null
                ? Array.Empty<CombatUnitPresentationTargetResult>()
                : new List<CombatUnitPresentationTargetResult>(targetResults).AsReadOnly();
            MovementPath = movementPath == null
                ? Array.Empty<CombatUnitPresentationHex>()
                : new List<CombatUnitPresentationHex>(movementPath).AsReadOnly();
        }

        public string ActorCombatantId { get; }
        public CombatUnitPresentationEvent PresentationEvent { get; }
        public CombatUnitPresentationHex StartPosition { get; }
        public CombatUnitPresentationHex EndPosition { get; }
        public int Facing { get; }
        public IReadOnlyList<CombatUnitPresentationTargetResult> TargetResults { get; }
        public IReadOnlyList<CombatUnitPresentationHex> MovementPath { get; }
    }

    public interface ICombatPresentationActivity
    {
        bool IsPresenting { get; }
    }

    [Serializable]
    public sealed class CombatMovementOption
    {
        public CombatMovementOption(CombatUnitPresentationHex destination, int cost, IReadOnlyList<CombatUnitPresentationHex> path,
            CombatActionCandidate candidate = null)
        {
            Destination = destination;
            Cost = cost;
            Path = new List<CombatUnitPresentationHex>(path).AsReadOnly();
            Candidate = candidate;
        }
        public CombatUnitPresentationHex Destination { get; }
        public int Cost { get; }
        public IReadOnlyList<CombatUnitPresentationHex> Path { get; }
        public CombatActionCandidate Candidate { get; }
    }

    [Serializable]
    public sealed class CombatMovementSnapshot
    {
        public CombatMovementSnapshot(CombatUnitPresentationHex origin, int remainingPoints, string phase,
            IReadOnlyList<CombatMovementOption> options)
        {
            Origin = origin;
            RemainingPoints = remainingPoints;
            Phase = phase;
            Options = new List<CombatMovementOption>(options).AsReadOnly();
        }
        public CombatUnitPresentationHex Origin { get; }
        public int RemainingPoints { get; }
        public string Phase { get; }
        public IReadOnlyList<CombatMovementOption> Options { get; }
    }

    public interface ICombatUnitPresentationPort
    {
        void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> combatants);
        void Spawn(CombatUnitPresentationDescriptor combatant);
        void Present(CombatUnitPresentationEventProjection presentationEvent);
        void Remove(string combatantId);
        void Clear();
    }

    [Serializable]
    public sealed class CombatantHudSnapshot
    {
        public CombatantHudSnapshot(
            string id,
            string displayName,
            int currentHealth,
            int maximumHealth,
            int currentSpirit,
            int maximumSpirit)
        {
            Id = id;
            DisplayName = displayName;
            CurrentHealth = currentHealth;
            MaximumHealth = maximumHealth;
            CurrentSpirit = currentSpirit;
            MaximumSpirit = maximumSpirit;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int CurrentHealth { get; }
        public int MaximumHealth { get; }
        public int CurrentSpirit { get; }
        public int MaximumSpirit { get; }
    }

    [Serializable]
    public sealed class CombatArtTargetSnapshot
    {
        public CombatArtTargetSnapshot(string id, string displayName, int spiritCost, bool canExecute, string rejectionReason,
            CombatActionPreviewSnapshot quote = null)
        {
            Id = id;
            DisplayName = displayName;
            SpiritCost = spiritCost;
            CanExecute = canExecute;
            RejectionReason = rejectionReason ?? string.Empty;
            Quote = quote;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int SpiritCost { get; }
        public bool CanExecute { get; }
        public string RejectionReason { get; }
        public CombatActionPreviewSnapshot Quote { get; }
    }

    [Serializable]
    public sealed class CombatArtActionSnapshot
    {
        public CombatArtActionSnapshot(string profileId, string displayName, int cooldownPenalty,
            IReadOnlyList<CombatArtTargetSnapshot> targets)
        {
            ProfileId = profileId;
            DisplayName = displayName;
            CooldownPenalty = cooldownPenalty;
            Targets = targets == null ? Array.Empty<CombatArtTargetSnapshot>() :
                new List<CombatArtTargetSnapshot>(targets).AsReadOnly();
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public int CooldownPenalty { get; }
        public IReadOnlyList<CombatArtTargetSnapshot> Targets { get; }
    }

    [Serializable]
    public sealed class CombatTimelineEntrySnapshot
    {
        public CombatTimelineEntrySnapshot(string id, string displayName, CombatUnitDisplayFaction faction,
            int speed, float currentCt, float nextActionThreshold, bool isReady, int queuePosition, bool isCurrent,
            long ticksUntilReady)
        {
            Id = id;
            DisplayName = displayName;
            Faction = faction;
            Speed = speed;
            CurrentCt = currentCt;
            NextActionThreshold = nextActionThreshold;
            IsReady = isReady;
            QueuePosition = queuePosition;
            IsCurrent = isCurrent;
            TicksUntilReady = ticksUntilReady;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public CombatUnitDisplayFaction Faction { get; }
        public int Speed { get; }
        public float CurrentCt { get; }
        public float NextActionThreshold { get; }
        public bool IsReady { get; }
        public int QueuePosition { get; }
        public bool IsCurrent { get; }
        public long TicksUntilReady { get; }
    }

    [Serializable]
    public sealed class CombatTimelineSnapshot
    {
        public CombatTimelineSnapshot(int currentTick, string currentActorId, IReadOnlyList<CombatTimelineEntrySnapshot> entries,
            IReadOnlyList<CombatTimelineSnapshot> advanceSteps = null)
        {
            CurrentTick = currentTick;
            CurrentActorId = currentActorId;
            Entries = entries == null ? Array.Empty<CombatTimelineEntrySnapshot>()
                : new List<CombatTimelineEntrySnapshot>(entries).AsReadOnly();
            AdvanceSteps = advanceSteps == null ? Array.Empty<CombatTimelineSnapshot>()
                : new List<CombatTimelineSnapshot>(advanceSteps).AsReadOnly();
        }

        public int CurrentTick { get; }
        public string CurrentActorId { get; }
        public IReadOnlyList<CombatTimelineEntrySnapshot> Entries { get; }
        public IReadOnlyList<CombatTimelineSnapshot> AdvanceSteps { get; }
    }

    [Serializable]
    public sealed class CombatHudSnapshot
    {
        public CombatHudSnapshot(
            CombatantHudSnapshot player,
            CombatantHudSnapshot enemy,
            string turnText,
            bool acceptsCommands,
            IReadOnlyList<string> artProfileIds,
            IReadOnlyList<string> divineProfileIds,
            CombatTimelineSnapshot timeline = null,
            bool canEndTurn = false,
            CombatMovementSnapshot movement = null,
            bool isPresenting = false,
            IReadOnlyList<CombatArtActionSnapshot> artActions = null,
            long commandContextVersion = 0, IReadOnlyList<CombatActionPreviewSnapshot> actions = null,
            int actionPointLimitUnits = 0, int pendingCtPenalty = 0,
            IReadOnlyList<CombatantHudSnapshot> enemies = null)
        {
            CommandContextVersion = commandContextVersion;
            Player = player;
            Enemy = enemy;
            Enemies = new List<CombatantHudSnapshot>(enemies ??
                (enemy == null ? Array.Empty<CombatantHudSnapshot>() : new[] { enemy })).AsReadOnly();
            TurnText = turnText;
            AcceptsCommands = acceptsCommands;
            ArtProfileIds = artProfileIds == null ? Array.Empty<string>() : new List<string>(artProfileIds).AsReadOnly();
            DivineProfileIds = divineProfileIds == null ? Array.Empty<string>() : new List<string>(divineProfileIds).AsReadOnly();
            ArtActions = artActions == null ? Array.Empty<CombatArtActionSnapshot>() :
                new List<CombatArtActionSnapshot>(artActions).AsReadOnly();
            CanEndTurn = canEndTurn;
            Timeline = timeline;
            Movement = movement;
            IsPresenting = isPresenting;
            Actions = actions == null ? Array.Empty<CombatActionPreviewSnapshot>() :
                new List<CombatActionPreviewSnapshot>(actions).AsReadOnly();
            ActionPointLimitUnits = actionPointLimitUnits;
            PendingCtPenalty = pendingCtPenalty;
        }

        public long CommandContextVersion { get; }
        public CombatantHudSnapshot Player { get; }
        public CombatantHudSnapshot Enemy { get; }
        public IReadOnlyList<CombatantHudSnapshot> Enemies { get; }
        public string TurnText { get; }
        public bool AcceptsCommands { get; }
        public IReadOnlyList<string> ArtProfileIds { get; }
        public IReadOnlyList<string> DivineProfileIds { get; }
        public IReadOnlyList<CombatArtActionSnapshot> ArtActions { get; }
        public bool CanEndTurn { get; }
        public CombatTimelineSnapshot Timeline { get; }
        public CombatMovementSnapshot Movement { get; }
        public bool IsPresenting { get; }
        public IReadOnlyList<CombatActionPreviewSnapshot> Actions { get; }
        public int ActionPointLimitUnits { get; }
        public int PendingCtPenalty { get; }

        public CombatActionPreviewSnapshot FindAction(CombatActionKind kind, string profileId = null)
        {
            foreach (CombatActionPreviewSnapshot action in Actions)
                if (action.Candidate.Kind == kind && (profileId == null || action.Candidate.ProfileId == profileId))
                    return action;
            return null;
        }

        public CombatActionPreviewSnapshot FindPreferredAction(CombatActionKind kind, string profileId = null)
        {
            CombatActionPreviewSnapshot first = null;
            foreach (CombatActionPreviewSnapshot action in Actions)
            {
                if (action.Candidate.Kind != kind || (profileId != null && action.Candidate.ProfileId != profileId)) continue;
                first ??= action;
                if (action.CanConfirm) return action;
            }
            return first;
        }
    }

    public interface ICombatPresentationSink
    {
        void Present(CombatHudSnapshot snapshot);
        void ClearLog();
        void AppendLog(string message);
        void Hide();
    }
}
