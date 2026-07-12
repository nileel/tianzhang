namespace TianZhang.Entity
{
    /// <summary>
    /// 战斗单位可展示状态。它只传递领域快照，不持有 UI 组件或命令入口。
    /// </summary>
    public sealed class CombatantPanelState
    {
        public string Name { get; }
        public int CurrentHP { get; }
        public int MaxHP { get; }
        public int CurrentMP { get; }
        public int MaxMP { get; }
        public float CTRatio { get; }
        public string Element { get; }
        public string Status { get; }

        public CombatantPanelState(
            string name,
            int currentHP,
            int maxHP,
            int currentMP,
            int maxMP,
            float ctRatio,
            string element,
            string status)
        {
            Name = name ?? string.Empty;
            CurrentHP = currentHP;
            MaxHP = maxHP;
            CurrentMP = currentMP;
            MaxMP = maxMP;
            CTRatio = ctRatio;
            Element = element ?? string.Empty;
            Status = status ?? string.Empty;
        }
    }
}
