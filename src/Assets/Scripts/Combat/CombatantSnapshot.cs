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
        private readonly List<string> equippedArtProfileIds;
        private readonly List<string> availableArtProfileIds;

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
            float realmMultiplier = 1f,
            int movePoints = 0,
            IEnumerable<string> equippedArtProfileIds = null,
            IEnumerable<string> availableArtProfileIds = null,
            int maxCombatSwaps = 2,
            int combatSwapsUsed = 0)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Combatant ID is required.", nameof(id));
            if (maximumHealth <= 0 || currentHealth < 0 || currentHealth > maximumHealth)
                throw new ArgumentOutOfRangeException(nameof(currentHealth));
            if (movePoints < 0 || maxCombatSwaps < 0 || combatSwapsUsed < 0 || combatSwapsUsed > maxCombatSwaps)
                throw new ArgumentOutOfRangeException(nameof(movePoints));

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
            MovePoints = movePoints;
            this.equippedArtProfileIds = CreateProfileIdList(equippedArtProfileIds, allowEmpty: true);
            this.availableArtProfileIds = CreateProfileIdList(availableArtProfileIds, allowEmpty: false);
            MaxCombatSwaps = maxCombatSwaps;
            CombatSwapsUsed = combatSwapsUsed;
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
        public int MovePoints { get; }
        public IReadOnlyList<string> EquippedArtProfileIds => equippedArtProfileIds.AsReadOnly();
        public IReadOnlyList<string> AvailableArtProfileIds => availableArtProfileIds.AsReadOnly();
        public int CombatSwapsUsed { get; private set; }
        public int MaxCombatSwaps { get; }
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

        public int MaximumShouyiStacks => CombatGongFaRules.MaximumShouyiStacks(RealmMultiplier);
        public int MaximumFudanStacks => CombatGongFaRules.MaximumFudanStacks(RealmMultiplier);
        public int MaximumLeijieStacks => CombatGongFaRules.MaximumLeijieStacks(RealmMultiplier);
        public float LeijieDamageBonusPerStack => CombatGongFaRules.LeijieDamageBonusPerStack(RealmMultiplier);
        public int MindStrengthBonus => CombatGongFaRules.MindStrengthBonus(GongFaId, RealmMultiplier);

        public bool IsAlive => CurrentHealth > 0;
        public IReadOnlyDictionary<string, int> Cooldowns => cooldowns;

        public void SetPosition(HexCoord position)
        {
            Position = position;
        }

        public void SwapEquippedArt(int slotIndex, string profileId)
        {
            if (slotIndex < 0 || slotIndex >= EquippedArtProfileIds.Count)
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));

            equippedArtProfileIds[slotIndex] = profileId;
            CombatSwapsUsed++;
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
            if (damage > 0 && GongFaId == CombatGongFaRules.LeijieGongFaId)
                LeijieStacks = Math.Min(LeijieStacks + 1, MaximumLeijieStacks);
            CurrentHealth = Math.Max(0, CurrentHealth - damage);
        }

        public float DefenseMultiplier(bool physical)
        {
            return CombatGongFaRules.DefenseMultiplier(this, physical);
        }

        public void RestoreHealth(int amount)
        {
            if (amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentHealth = Math.Min(MaximumHealth, CurrentHealth + amount);
        }

        private static List<string> CreateProfileIdList(IEnumerable<string> profileIds, bool allowEmpty)
        {
            var result = new List<string>();
            if (profileIds == null)
                return result;

            foreach (string profileId in profileIds)
            {
                if (string.IsNullOrWhiteSpace(profileId))
                {
                    if (allowEmpty)
                        result.Add(string.Empty);
                    continue;
                }
                result.Add(profileId);
            }
            return result;
        }
    }

    internal static class CombatGongFaRules
    {
        internal const string ShouyiGongFaId = "抱元守一经";
        internal const string FudanGongFaId = "云篆度人经";
        internal const string LeijieGongFaId = "九霄雷劫录";
        internal const string XuanganGongFaId = "南华玄感录";
        internal const string HanhongGongFaId = "含弘光大典";

        internal static int MaximumShouyiStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : realm >= 3 ? 4 : 3;
        }

        internal static int MaximumFudanStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : realm >= 3 ? 3 : 5;
        }

        internal static int MaximumLeijieStacks(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 6 ? 5 : 3;
        }

        internal static float LeijieDamageBonusPerStack(float realmMultiplier)
        {
            int realm = RoundedRealm(realmMultiplier);
            return realm >= 24 ? 0.30f :
                realm >= 12 ? 0.22f :
                realm >= 6 ? 0.18f :
                0.15f;
        }

        internal static int MindStrengthBonus(string gongFaId, float realmMultiplier)
        {
            if (gongFaId != XuanganGongFaId)
                return 0;

            int realm = RoundedRealm(realmMultiplier);
            return realm >= 12 ? 12 :
                realm >= 6 ? 8 :
                realm >= 3 ? 5 :
                3;
        }

        internal static float DefenseMultiplier(CombatantSnapshot combatant, bool physical)
        {
            if (combatant.GongFaId != HanhongGongFaId)
                return 1f;

            float multiplier = 1f + ZaiwuAllDefenseBonus(combatant);
            if (physical)
                multiplier *= 1f + HanhongPhysicalDefenseBonus(combatant.RealmMultiplier);
            return multiplier;
        }

        private static float HanhongPhysicalDefenseBonus(float realmMultiplier)
        {
            return realmMultiplier >= 24f ? 0.30f :
                realmMultiplier >= 12f ? 0.25f :
                realmMultiplier >= 6f ? 0.20f :
                realmMultiplier >= 3f ? 0.15f :
                realmMultiplier >= 1.5f ? 0.10f :
                0f;
        }

        private static float ZaiwuAllDefenseBonus(CombatantSnapshot combatant)
        {
            if (combatant.MaximumHealth <= 0 || combatant.CurrentHealth >= combatant.MaximumHealth)
                return 0f;

            float cap = combatant.RealmMultiplier >= 24f ? 0.40f :
                combatant.RealmMultiplier >= 6f ? 0.30f :
                combatant.RealmMultiplier >= 3f ? 0.20f :
                0f;
            if (cap <= 0f)
                return 0f;

            float missingHealthRate = (combatant.MaximumHealth - combatant.CurrentHealth) /
                (float)combatant.MaximumHealth;
            float bonus = (float)Math.Floor(Math.Min(1f, Math.Max(0f, missingHealthRate)) * 10f) * 0.02f;
            return Math.Min(bonus, cap);
        }

        private static int RoundedRealm(float realmMultiplier)
        {
            return (int)Math.Round(Math.Max(1f, realmMultiplier), MidpointRounding.ToEven);
        }
    }
}
