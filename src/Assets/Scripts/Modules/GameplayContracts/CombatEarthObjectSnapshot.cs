namespace TianZhang.Gameplay.Contracts
{
    public sealed class CombatEarthObjectSnapshot
    {
        public CombatEarthObjectSnapshot(string id,CombatUnitPresentationHex a,CombatUnitPresentationHex b,bool ramp,int durability)
        { Id=id; A=a; B=b; IsRamp=ramp; Durability=durability; }
        public string Id { get; }
        public CombatUnitPresentationHex A { get; }
        public CombatUnitPresentationHex B { get; }
        public bool IsRamp { get; }
        public int Durability { get; }
    }
}
