using System;
using System.Collections.Generic;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public enum CombatTeam
    {
        Player,
        Enemy,
    }

    /// <summary>
    /// Pure battle input and mutable battle state for one stable combatant identity.
    /// This type deliberately projects values instead of retaining a Character, scene, or presentation object.
    /// </summary>
    public sealed class CombatantSnapshot
    {
        private readonly Dictionary<string, int> cooldowns = new Dictionary<string, int>(StringComparer.Ordinal);

        public CombatantSnapshot(
            string id,
            CombatTeam team,
            HexCoord position,
            int speed,
            int maximumHealth,
            int currentHealth,
            int physicalAttack,
            int soulAttack,
            int physicalDefense,
            int soulDefense,
            float realmMultiplier = 1f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Combatant ID is required.", nameof(id));
            if (maximumHealth <= 0 || currentHealth < 0 || currentHealth > maximumHealth)
                throw new ArgumentOutOfRangeException(nameof(currentHealth));

            Id = id;
            Team = team;
            Position = position;
            Speed = Math.Max(1, speed);
            MaximumHealth = maximumHealth;
            CurrentHealth = currentHealth;
            PhysicalAttack = physicalAttack;
            SoulAttack = soulAttack;
            PhysicalDefense = physicalDefense;
            SoulDefense = soulDefense;
            RealmMultiplier = Math.Max(1f, realmMultiplier);
        }

        public string Id { get; }
        public CombatTeam Team { get; }
        public HexCoord Position { get; private set; }
        public int Speed { get; }
        public int MaximumHealth { get; }
        public int CurrentHealth { get; private set; }
        public int MaximumSpirit { get; set; }
        public int CurrentSpirit { get; private set; }
        public int PhysicalAttack { get; }
        public int SoulAttack { get; }
        public int PhysicalDefense { get; }
        public int SoulDefense { get; }
        public float RealmMultiplier { get; }
        public float BlockRate { get; set; }
        public float BlockReduction { get; set; }
        public float SoulShieldRate { get; set; }
        public float SoulShieldReduction { get; set; }
        public float DodgeRate { get; set; }
        public float CriticalRate { get; set; }
        public float CriticalDamage { get; set; }
        public float HitRateBonus { get; set; }
        public int Facing { get; set; }
        public bool IsGuarding { get; set; }
        public string GongFaId { get; set; } = string.Empty;
        public string GongFaElement { get; set; } = string.Empty;
        public int ShouyiStacks { get; set; }
        public int FudanStacks { get; set; }
        public int LeijieStacks { get; set; }

        public bool IsAlive => CurrentHealth > 0;
        public IReadOnlyDictionary<string, int> Cooldowns => cooldowns;

        public void SetPosition(HexCoord position)
        {
            Position = position;
        }

        public void SetSpirit(int maximumSpirit, int currentSpirit)
        {
            if (maximumSpirit < 0 || currentSpirit < 0 || currentSpirit > maximumSpirit)
                throw new ArgumentOutOfRangeException(nameof(currentSpirit));
            MaximumSpirit = maximumSpirit;
            CurrentSpirit = currentSpirit;
        }

        public bool TryConsumeSpirit(int amount)
        {
            if (amount < 0 || CurrentSpirit < amount)
                return false;
            CurrentSpirit -= amount;
            return true;
        }

        public void SetCooldown(string profileId, int ticks)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));
            cooldowns[profileId] = Math.Max(0, ticks);
        }

        public int GetCooldown(string profileId)
        {
            return !string.IsNullOrWhiteSpace(profileId) && cooldowns.TryGetValue(profileId, out int ticks)
                ? ticks
                : 0;
        }

        public void AdvanceCooldowns(int ticks)
        {
            if (ticks < 0)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            if (ticks == 0)
                return;

            var keys = new List<string>(cooldowns.Keys);
            foreach (string key in keys)
                cooldowns[key] = Math.Max(0, cooldowns[key] - ticks);
        }

        public void ReceiveDamage(int damage)
        {
            if (damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));
            CurrentHealth = Math.Max(0, CurrentHealth - damage);
        }

        public void RestoreHealth(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentHealth = Math.Min(MaximumHealth, CurrentHealth + amount);
        }
    }
}
