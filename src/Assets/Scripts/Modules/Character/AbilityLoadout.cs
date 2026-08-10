using System;
using System.Collections.Generic;

namespace TianZhang.Character
{
    /// <summary>Learned and equipped abilities without combat-only cooldown or swap state.</summary>
    public sealed class AbilityLoadout
    {
        private readonly List<string> knownSpells;
        private readonly List<string> knownSkills;
        private readonly List<string> equippedSpells;
        private readonly List<string> equippedSkills;

        public AbilityLoadout(IEnumerable<string> knownSpells, IEnumerable<string> knownSkills, int spellSlots, int skillSlots)
        {
            this.knownSpells = CopyDistinct(knownSpells); this.knownSkills = CopyDistinct(knownSkills);
            equippedSpells = new List<string>(); equippedSkills = new List<string>();
            SpellSlots = Math.Max(0, spellSlots); SkillSlots = Math.Max(0, skillSlots);
        }
        public int SpellSlots { get; private set; } public int SkillSlots { get; private set; }
        public IReadOnlyList<string> KnownSpells { get { return knownSpells.AsReadOnly(); } }
        public IReadOnlyList<string> KnownSkills { get { return knownSkills.AsReadOnly(); } }
        public IReadOnlyList<string> EquippedSpells { get { return equippedSpells.AsReadOnly(); } }
        public IReadOnlyList<string> EquippedSkills { get { return equippedSkills.AsReadOnly(); } }
        public bool TryEquipSpell(string id) { return TryEquip(id, knownSpells, equippedSpells, SpellSlots); }
        public bool TryEquipSkill(string id) { return TryEquip(id, knownSkills, equippedSkills, SkillSlots); }
        public bool UnequipSpell(string id) { return equippedSpells.Remove(id); }
        public bool UnequipSkill(string id) { return equippedSkills.Remove(id); }
        public void SetSlots(int spellSlots, int skillSlots)
        {
            SpellSlots = Math.Max(0, spellSlots); SkillSlots = Math.Max(0, skillSlots);
            Trim(equippedSpells, SpellSlots); Trim(equippedSkills, SkillSlots);
        }
        public AbilityLoadoutSnapshot Capture() { return new AbilityLoadoutSnapshot(knownSpells, knownSkills, equippedSpells, equippedSkills, SpellSlots, SkillSlots); }
        public void Restore(AbilityLoadoutSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            knownSpells.Clear(); knownSpells.AddRange(CopyDistinct(snapshot.KnownSpells));
            knownSkills.Clear(); knownSkills.AddRange(CopyDistinct(snapshot.KnownSkills));
            equippedSpells.Clear(); equippedSpells.AddRange(CopyDistinct(snapshot.EquippedSpells));
            equippedSkills.Clear(); equippedSkills.AddRange(CopyDistinct(snapshot.EquippedSkills));
            SetSlots(snapshot.SpellSlots, snapshot.SkillSlots);
        }
        private static bool TryEquip(string id, List<string> known, List<string> equipped, int slots)
        {
            if (string.IsNullOrWhiteSpace(id) || !known.Contains(id) || equipped.Contains(id) || equipped.Count >= slots) return false;
            equipped.Add(id); return true;
        }
        private static List<string> CopyDistinct(IEnumerable<string> values)
        {
            var result = new List<string>(); if (values == null) return result;
            foreach (string value in values) if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value)) result.Add(value);
            return result;
        }
        private static void Trim(List<string> values, int count) { if (values.Count > count) values.RemoveRange(count, values.Count - count); }
    }

    public sealed class AbilityLoadoutSnapshot
    {
        public AbilityLoadoutSnapshot(IEnumerable<string> knownSpells, IEnumerable<string> knownSkills, IEnumerable<string> equippedSpells, IEnumerable<string> equippedSkills, int spellSlots, int skillSlots)
        {
            KnownSpells = Copy(knownSpells); KnownSkills = Copy(knownSkills); EquippedSpells = Copy(equippedSpells); EquippedSkills = Copy(equippedSkills);
            SpellSlots = spellSlots; SkillSlots = skillSlots;
        }
        public string[] KnownSpells { get; } public string[] KnownSkills { get; }
        public string[] EquippedSpells { get; } public string[] EquippedSkills { get; }
        public int SpellSlots { get; } public int SkillSlots { get; }
        private static string[] Copy(IEnumerable<string> values) { return values == null ? new string[0] : new List<string>(values).ToArray(); }
    }
}
