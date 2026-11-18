using System;
using System.Collections.Generic;

namespace TianZhang.Gameplay.Contracts
{
    /// <summary>Read-only equipment and persistent battlefield facts for the shared combat HUD.</summary>
    public sealed class JindanHudSnapshot
    {
        public string ActorId { get; }
        public string Objective { get; }
        public string PersistentStatus { get; }
        public IReadOnlyList<JindanHudAbility> Abilities { get; }
        public JindanHudSnapshot(string actorId, string objective, string status, IReadOnlyList<JindanHudAbility> abilities)
        { ActorId = actorId; Objective = objective; PersistentStatus = status; Abilities = abilities ?? Array.Empty<JindanHudAbility>(); }
    }

    public sealed class JindanHudAbility
    {
        public string Key { get; }
        public string EffectId { get; }
        public string Name { get; }
        public string Status { get; }
        public int Cooldown { get; }
        public bool Enabled { get; }
        public bool CanSwap { get; }
        public bool HasActiveInstance { get; }
        public JindanHudAbility(string key, string effectId, string name, string status, int cooldown, bool enabled, bool canSwap = false, bool hasActiveInstance = false)
        { Key = key; EffectId = effectId; Name = name; Status = status; Cooldown = cooldown; Enabled = enabled; CanSwap = canSwap; HasActiveInstance = hasActiveInstance; }
    }
}
