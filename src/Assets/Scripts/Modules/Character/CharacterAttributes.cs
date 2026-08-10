using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Character
{
    /// <summary>Immutable-base and derived character attributes; combat state is deliberately excluded.</summary>
    public sealed class CharacterAttributes
    {
        public CharacterAttributes(int rootBone, int physique, int spirit, int mind, int reaction, int talent, int fortune)
        {
            RootBone = rootBone;
            Physique = physique;
            Spirit = spirit;
            Mind = mind;
            Reaction = reaction;
            Talent = talent;
            Fortune = fortune;
        }

        public int RootBone { get; private set; }
        public int Physique { get; private set; }
        public int Spirit { get; private set; }
        public int Mind { get; private set; }
        public int Reaction { get; private set; }
        public int Talent { get; private set; }
        public int Fortune { get; private set; }

        public static CharacterAttributes FromDefinition(CharacterData definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            return new CharacterAttributes(definition.rootBone, definition.physique, definition.spirit,
                definition.mind, definition.reaction, definition.talent, definition.fortune);
        }

        public CharacterAttributesSnapshot Capture()
        {
            return new CharacterAttributesSnapshot(RootBone, Physique, Spirit, Mind, Reaction, Talent, Fortune);
        }

        public void Restore(CharacterAttributesSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            RootBone = snapshot.RootBone; Physique = snapshot.Physique; Spirit = snapshot.Spirit;
            Mind = snapshot.Mind; Reaction = snapshot.Reaction; Talent = snapshot.Talent; Fortune = snapshot.Fortune;
        }

        public CharacterDerivedAttributes Derive(float realmMultiplier, CharacterAttributeBonuses bonuses)
        {
            float realm = realmMultiplier > 0f ? realmMultiplier : 1f;
            CharacterAttributeBonuses applied = bonuses ?? CharacterAttributeBonuses.Empty;
            int maxHealth = Mathf.RoundToInt(Mathf.Pow(RootBone, 0.75f) * realm * 80f + applied.Health);
            int maxSpirit = Mathf.RoundToInt(Spirit * realm * 15f + applied.SpiritResource);
            return new CharacterDerivedAttributes(maxHealth, maxSpirit,
                Mathf.RoundToInt(RootBone * realm * 5f + applied.PhysicalAttack),
                Mathf.RoundToInt(Spirit * realm * 5f + applied.MagicAttack),
                Mathf.RoundToInt(Physique * realm * 3.5f + applied.PhysicalDefense),
                Mathf.RoundToInt(Mind * realm * 3.5f + applied.MagicDefense));
        }
    }

    public sealed class CharacterAttributesSnapshot
    {
        public CharacterAttributesSnapshot(int rootBone, int physique, int spirit, int mind, int reaction, int talent, int fortune)
        {
            RootBone = rootBone; Physique = physique; Spirit = spirit; Mind = mind;
            Reaction = reaction; Talent = talent; Fortune = fortune;
        }
        public int RootBone { get; } public int Physique { get; } public int Spirit { get; }
        public int Mind { get; } public int Reaction { get; } public int Talent { get; } public int Fortune { get; }
    }

    public sealed class CharacterAttributeBonuses
    {
        public static readonly CharacterAttributeBonuses Empty = new CharacterAttributeBonuses();
        public int Health { get; set; } public int SpiritResource { get; set; }
        public int PhysicalAttack { get; set; } public int MagicAttack { get; set; }
        public int PhysicalDefense { get; set; } public int MagicDefense { get; set; }
    }

    public sealed class CharacterDerivedAttributes
    {
        public CharacterDerivedAttributes(int maxHealth, int maxSpirit, int physicalAttack, int magicAttack, int physicalDefense, int magicDefense)
        {
            MaxHealth = maxHealth; MaxSpirit = maxSpirit; PhysicalAttack = physicalAttack;
            MagicAttack = magicAttack; PhysicalDefense = physicalDefense; MagicDefense = magicDefense;
        }
        public int MaxHealth { get; } public int MaxSpirit { get; } public int PhysicalAttack { get; }
        public int MagicAttack { get; } public int PhysicalDefense { get; } public int MagicDefense { get; }
    }
}
