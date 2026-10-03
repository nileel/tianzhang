using System;
using System.Collections.Generic;

namespace TianZhang.Gameplay.Contracts
{
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
        public CombatArtTargetSnapshot(string id, string displayName, int spiritCost, bool canExecute, string rejectionReason)
        {
            Id = id;
            DisplayName = displayName;
            SpiritCost = spiritCost;
            CanExecute = canExecute;
            RejectionReason = rejectionReason ?? string.Empty;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public int SpiritCost { get; }
        public bool CanExecute { get; }
        public string RejectionReason { get; }
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
            int speed, float currentCt, float nextActionThreshold, bool isReady, int queuePosition, bool isCurrent)
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
    }

    [Serializable]
    public sealed class CombatTimelineSnapshot
    {
        public CombatTimelineSnapshot(int currentTick, string currentActorId, IReadOnlyList<CombatTimelineEntrySnapshot> entries)
        {
            CurrentTick = currentTick;
            CurrentActorId = currentActorId;
            Entries = entries == null ? Array.Empty<CombatTimelineEntrySnapshot>()
                : new List<CombatTimelineEntrySnapshot>(entries).AsReadOnly();
        }

        public int CurrentTick { get; }
        public string CurrentActorId { get; }
        public IReadOnlyList<CombatTimelineEntrySnapshot> Entries { get; }
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
            IReadOnlyList<CombatArtActionSnapshot> artActions = null,
            bool canEndTurn = false,
            CombatTimelineSnapshot timeline = null)
        {
            Player = player;
            Enemy = enemy;
            TurnText = turnText;
            AcceptsCommands = acceptsCommands;
            ArtProfileIds = artProfileIds ?? Array.Empty<string>();
            DivineProfileIds = divineProfileIds ?? Array.Empty<string>();
            ArtActions = artActions == null ? Array.Empty<CombatArtActionSnapshot>() :
                new List<CombatArtActionSnapshot>(artActions).AsReadOnly();
            CanEndTurn = canEndTurn;
            Timeline = timeline;
        }

        public CombatantHudSnapshot Player { get; }
        public CombatantHudSnapshot Enemy { get; }
        public string TurnText { get; }
        public bool AcceptsCommands { get; }
        public IReadOnlyList<string> ArtProfileIds { get; }
        public IReadOnlyList<string> DivineProfileIds { get; }
        public IReadOnlyList<CombatArtActionSnapshot> ArtActions { get; }
        public bool CanEndTurn { get; }
        public CombatTimelineSnapshot Timeline { get; }
    }

    public interface ICombatPresentationSink
    {
        void Present(CombatHudSnapshot snapshot);
        void ClearLog();
        void AppendLog(string message);
        void Hide();
    }
}
