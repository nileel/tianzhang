namespace TianZhang.Character
{
    /// <summary>Stable identity and display information for one runtime character.</summary>
    public sealed class CharacterIdentity
    {
        public CharacterIdentity(string characterId, string displayName)
        {
            CharacterId = characterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string CharacterId { get; private set; }
        public string DisplayName { get; private set; }

        public CharacterIdentitySnapshot Capture()
        {
            return new CharacterIdentitySnapshot(CharacterId, DisplayName);
        }

        public void Restore(CharacterIdentitySnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            CharacterId = snapshot.CharacterId;
            DisplayName = snapshot.DisplayName;
        }
    }

    public sealed class CharacterIdentitySnapshot
    {
        public CharacterIdentitySnapshot(string characterId, string displayName)
        {
            CharacterId = characterId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string CharacterId { get; }
        public string DisplayName { get; }
    }
}
