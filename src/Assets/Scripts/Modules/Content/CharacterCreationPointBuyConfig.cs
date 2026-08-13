using System;
using UnityEngine;

namespace TianZhang.Game.CharacterCreation
{
    [CreateAssetMenu(fileName = "CharacterCreationPointBuyConfig", menuName = "天章/角色创建/点购配置")]
    public class CharacterCreationPointBuyConfig : ScriptableObject
    {
        public int purchasePointLimit;
        public int minValue;
        public int baseValue;
        public int maxValue;
        public CostRange[] costRanges = new CostRange[0];

        [Serializable]
        public struct CostRange
        {
            public int fromValue;
            public int toValue;
            public int costPerLevel;
        }

        public int CalculateCost(int rootBone, int soul, int divineSense, int aptitude, int fortune)
        {
            return CalculateCost(rootBone)
                + CalculateCost(soul)
                + CalculateCost(divineSense)
                + CalculateCost(aptitude)
                + CalculateCost(fortune);
        }

        public int CalculateCost(int value)
        {
            if (value <= baseValue)
                return 0;

            int cost = 0;
            for (int current = baseValue + 1; current <= value; current++)
                cost += CostForLevel(current);
            return cost;
        }

        public int CostForLevel(int value)
        {
            for (int i = 0; costRanges != null && i < costRanges.Length; i++)
            {
                var range = costRanges[i];
                if (value >= range.fromValue && value <= range.toValue)
                    return Math.Max(0, range.costPerLevel);
            }

            return 0;
        }
    }
}
