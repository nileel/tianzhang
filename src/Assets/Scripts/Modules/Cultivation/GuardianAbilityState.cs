namespace TianZhang.Cultivation
{
    /// <summary>Persistent guardian-ability binding for a mansion.</summary>
    public sealed class GuardianAbilityState
    {
        public GuardianAbilityState(string mansionId, string abilityInstanceId, string form)
        { MansionId = mansionId ?? string.Empty; AbilityInstanceId = abilityInstanceId ?? string.Empty; Form = form ?? string.Empty; }
        public string MansionId { get; private set; } public string AbilityInstanceId { get; private set; } public string Form { get; private set; }
        public void Bind(string abilityInstanceId, string form) { AbilityInstanceId = abilityInstanceId ?? string.Empty; Form = form ?? string.Empty; }
        public GuardianAbilityStateSnapshot Capture() { return new GuardianAbilityStateSnapshot(MansionId, AbilityInstanceId, Form); }
        public void Restore(GuardianAbilityStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            MansionId = snapshot.MansionId; AbilityInstanceId = snapshot.AbilityInstanceId; Form = snapshot.Form;
        }
    }
    public sealed class GuardianAbilityStateSnapshot
    {
        public GuardianAbilityStateSnapshot(string mansionId, string abilityInstanceId, string form)
        { MansionId = mansionId ?? string.Empty; AbilityInstanceId = abilityInstanceId ?? string.Empty; Form = form ?? string.Empty; }
        public string MansionId { get; } public string AbilityInstanceId { get; } public string Form { get; }
    }
}
