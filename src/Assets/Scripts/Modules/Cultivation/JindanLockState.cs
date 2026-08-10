namespace TianZhang.Cultivation
{
    /// <summary>Permanent Jindan lock; only an explicit formation command can set it.</summary>
    public sealed class JindanLockState
    {
        public JindanLockState(bool formed, string formedBy) { IsFormed = formed; FormedBy = formedBy ?? string.Empty; }
        public bool IsFormed { get; private set; } public string FormedBy { get; private set; }
        public bool TryForm(string source)
        {
            if (IsFormed || string.IsNullOrWhiteSpace(source)) return false;
            IsFormed = true; FormedBy = source; return true;
        }
        public JindanLockStateSnapshot Capture() { return new JindanLockStateSnapshot(IsFormed, FormedBy); }
        public void Restore(JindanLockStateSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));
            IsFormed = snapshot.IsFormed; FormedBy = snapshot.FormedBy;
        }
    }
    public sealed class JindanLockStateSnapshot
    {
        public JindanLockStateSnapshot(bool isFormed, string formedBy) { IsFormed = isFormed; FormedBy = formedBy ?? string.Empty; }
        public bool IsFormed { get; } public string FormedBy { get; }
    }
}
