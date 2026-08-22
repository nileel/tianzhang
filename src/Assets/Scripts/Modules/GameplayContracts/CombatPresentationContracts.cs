using System;
using System.Collections.Generic;

namespace TianZhang.Gameplay.Contracts
{
    public enum StaticChessPresentationEvent
    {
        Idle,
        Move,
        Attack,
        Hit,
        Cast,
        Death,
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
    public sealed class CombatHudSnapshot
    {
        public CombatHudSnapshot(
            CombatantHudSnapshot player,
            CombatantHudSnapshot enemy,
            string turnText,
            bool acceptsCommands,
            IReadOnlyList<string> artProfileIds,
            IReadOnlyList<string> divineProfileIds)
        {
            Player = player;
            Enemy = enemy;
            TurnText = turnText;
            AcceptsCommands = acceptsCommands;
            ArtProfileIds = artProfileIds ?? Array.Empty<string>();
            DivineProfileIds = divineProfileIds ?? Array.Empty<string>();
        }

        public CombatantHudSnapshot Player { get; }
        public CombatantHudSnapshot Enemy { get; }
        public string TurnText { get; }
        public bool AcceptsCommands { get; }
        public IReadOnlyList<string> ArtProfileIds { get; }
        public IReadOnlyList<string> DivineProfileIds { get; }
    }

    public interface ICombatPresentationSink
    {
        void Present(CombatHudSnapshot snapshot);
        void ClearLog();
        void AppendLog(string message);
        void Hide();
    }
}
