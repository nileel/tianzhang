using System;

namespace TianZhang.Entity
{
    [Serializable]
    public sealed class JindanDemoSurfaceRules
    {
        public string rulesId;
        public string environmentProfileId;
        public int wetMovementBurden;
        public int deepMovementBurden;
        public int tideRadius;
        public int waterlineOffset;
        public int forceRank;
        public int distanceBudget;
        public int chargeExtraActionPoints;
        public int chargeExtraMana;
        public int chargeTicks;
        public int eyeExtraActionPoints;
        public int eyeExtraMana;
        public int attackCapacity;
        public float waterMultiplier;
        public float chargeMultiplier;
        public int erosionDamage;
        public string sourceDocument;
    }

    [Serializable]
    public sealed class JindanDemoSurfaceCell
    {
        public string segmentId;
        public int q;
        public int r;
        public string kind;
        public int fuel;
        public string sourceRef;
        public string fuelObjectId;
    }

    [Serializable]
    public sealed class JindanDemoSurfaceFire
    {
        public string segmentId;
        public string sourceRef;
        public string actorId;
        public int periodTicks;
        public int durationTicks;
        public int capacity;
        public int fuelBudget;
        public int attackCapacity;
        public float damageMultiplier;
        public string sourceDocument;
    }
}
