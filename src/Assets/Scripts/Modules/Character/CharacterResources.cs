namespace TianZhang.Character
{
    /// <summary>Persistent resource state. Position, guarding and damage resolution remain Combat concerns.</summary>
    public sealed class CharacterResources
    {
        public CharacterResources(int maximumHealth, int currentHealth, int maximumSpirit, int currentSpirit)
        {
            MaximumHealth = maximumHealth < 0 ? 0 : maximumHealth;
            MaximumSpirit = maximumSpirit < 0 ? 0 : maximumSpirit;
            CurrentHealth = Clamp(currentHealth, MaximumHealth);
            CurrentSpirit = Clamp(currentSpirit, MaximumSpirit);
        }

        public int MaximumHealth { get; private set; }
        public int CurrentHealth { get; private set; }
        public int MaximumSpirit { get; private set; }
        public int CurrentSpirit { get; private set; }
        public bool IsAlive { get { return CurrentHealth > 0; } }

        public bool TrySpendSpirit(int amount)
        {
            if (amount < 0 || CurrentSpirit < amount) return false;
            CurrentSpirit -= amount;
            return true;
        }

        public void RestoreSpirit(int amount) { if (amount > 0) CurrentSpirit = Clamp(CurrentSpirit + amount, MaximumSpirit); }
        public void RestoreHealth(int amount) { if (amount > 0) CurrentHealth = Clamp(CurrentHealth + amount, MaximumHealth); }
        public void SetMaximums(int maximumHealth, int maximumSpirit)
        {
            MaximumHealth = maximumHealth < 0 ? 0 : maximumHealth;
            MaximumSpirit = maximumSpirit < 0 ? 0 : maximumSpirit;
            CurrentHealth = Clamp(CurrentHealth, MaximumHealth);
            CurrentSpirit = Clamp(CurrentSpirit, MaximumSpirit);
        }

        public CharacterResourcesSnapshot Capture() { return new CharacterResourcesSnapshot(MaximumHealth, CurrentHealth, MaximumSpirit, CurrentSpirit); }
        public void Restore(CharacterResourcesSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            MaximumHealth = snapshot.MaximumHealth; CurrentHealth = Clamp(snapshot.CurrentHealth, MaximumHealth);
            MaximumSpirit = snapshot.MaximumSpirit; CurrentSpirit = Clamp(snapshot.CurrentSpirit, MaximumSpirit);
        }
        private static int Clamp(int value, int maximum) { return value < 0 ? 0 : (value > maximum ? maximum : value); }
    }

    public sealed class CharacterResourcesSnapshot
    {
        public CharacterResourcesSnapshot(int maximumHealth, int currentHealth, int maximumSpirit, int currentSpirit)
        {
            MaximumHealth = maximumHealth; CurrentHealth = currentHealth; MaximumSpirit = maximumSpirit; CurrentSpirit = currentSpirit;
        }
        public int MaximumHealth { get; } public int CurrentHealth { get; }
        public int MaximumSpirit { get; } public int CurrentSpirit { get; }
    }
}
