using System;
using UnityEngine;

namespace TianZhang.Game.CharacterCreation
{
    [CreateAssetMenu(fileName = "CharacterCreationPointBuyConfig", menuName = "天章/角色创建/点购配置")]
    public class CharacterCreationPointBuyConfig : ScriptableObject
    {
        public const string ResourcesPath = "Data/CharacterCreation/CharacterCreationPointBuyConfig";

        public int purchasePointLimit = 25;
        public int minValue = 3;
        public int baseValue = 3;
        public int maxValue = 15;
        public CostRange[] costRanges = CreateDefaultRanges();

        [Serializable]
        public struct CostRange
        {
            public int fromValue;
            public int toValue;
            public int costPerLevel;
        }

        public static CharacterCreationPointBuyConfig LoadDefault()
        {
            var config = Resources.Load<CharacterCreationPointBuyConfig>(ResourcesPath);
            return config != null ? config : CreateFallback();
        }

        public static CharacterCreationPointBuyConfig CreateFallback()
        {
            var config = CreateInstance<CharacterCreationPointBuyConfig>();
            config.purchasePointLimit = 25;
            config.minValue = 3;
            config.baseValue = 3;
            config.maxValue = 15;
            config.costRanges = CreateDefaultRanges();
            return config;
        }

        public int CalculateCost(InnateAttributeSet innate)
        {
            if (innate == null)
                return 0;

            return CalculateCost(innate.RootBone)
                + CalculateCost(innate.Soul)
                + CalculateCost(innate.DivineSense)
                + CalculateCost(innate.Aptitude)
                + CalculateCost(innate.Fortune);
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
            var ranges = costRanges != null && costRanges.Length > 0
                ? costRanges
                : CreateDefaultRanges();

            for (int i = 0; i < ranges.Length; i++)
            {
                var range = ranges[i];
                if (value >= range.fromValue && value <= range.toValue)
                    return Math.Max(0, range.costPerLevel);
            }

            return 0;
        }

        public static CostRange[] CreateDefaultRanges()
        {
            return new[]
            {
                new CostRange { fromValue = 4, toValue = 8, costPerLevel = 1 },
                new CostRange { fromValue = 9, toValue = 12, costPerLevel = 2 },
                new CostRange { fromValue = 13, toValue = 15, costPerLevel = 3 },
            };
        }
    }
}
