using System;
using System.Collections.Generic;
using UnityEngine;

namespace TianZhang.Cultivation
{
    public static class ContentScopePolicy
    {
        public const string Player = "player";
        public const string Reserved = "reserved";

        public static bool IsKnown(string contentScope)
        {
            return string.Equals(contentScope, Player, StringComparison.Ordinal)
                || string.Equals(contentScope, Reserved, StringComparison.Ordinal);
        }

        public static bool IsPlayerAvailable(string contentScope)
        {
            return string.Equals(contentScope, Player, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 功法成长数据（ScriptableObject）
    /// 每境界每小级的属性成长 + 篇章加成
    /// 数据与 BattleSim GongFaTables 字典对齐
    /// </summary>
    [CreateAssetMenu(fileName = "GongFa_", menuName = "天章/功法数据")]
    public class GongFaGrowthData : ScriptableObject
    {
        [Header("基础信息")]
        public string gongFaName = "无名功法";
        public string affiliation = "太一道庭";   // 归属门派
        public string grade = "中品";              // 品级
        public string elementMain = "水";          // 主属性
        public string elementSub = "风";           // 辅属性
        public string contentScope = "player";     // player/reserved

        [Header("属性倾向（★1-5）")]
        public int starRootBone = 2;
        public int starPhysique = 1;
        public int starSpirit = 4;
        public int starMind = 4;
        public int starReaction = 1;
        public int starTalent = 3;
        public int starFortune = 3;

        [Header("境界小级成长（每级）")]
        public SubGrowthPerRealm[] subGrowth;

        [Header("篇章加成（每篇章解锁后永久获得）")]
        public ChapterBonus[] chapters;

        [System.Serializable]
        public struct SubGrowthPerRealm
        {
            public string realm;     // 练气/筑基/金丹/元婴/化神/炼虚
            public float hp;
            public float mp;
            public float physAtk;
            public float magAtk;
            public float physDef;
            public float magDef;
            public float reaction;
            public float movePoints;
            public float mindGrowth;
        }

        [System.Serializable]
        public struct ChapterBonus
        {
            public string chapterName;
            public string realm;          // 解锁境界
            public float soulShieldRate;  // 魂盾率加成
            public float hitRate;         // 命中率加成
            public float blockRate;
            public float critRate;
            public float critDamage;
            public float dodgeRate;
            public float magAtkBonus;
            public float magDefBonus;
            public string specialEffect;  // 特殊效果描述（守一印记等）
        }

        /// <summary>根据境界获取每级成长值</summary>
        public SubGrowthPerRealm GetGrowth(string realm)
        {
            if (subGrowth == null) return default;
            foreach (var g in subGrowth)
                if (g.realm == realm) return g;
            return default;
        }

        /// <summary>获取篇章加成总和（截至指定境界）</summary>
        public ChapterBonus GetCumulativeBonus(string realm)
        {
            var bonus = new ChapterBonus();
            if (chapters == null) return bonus;
            // 境界顺序
            var order = new List<string> { "练气", "筑基", "金丹", "元婴", "化神", "炼虚" };
            int maxIdx = order.IndexOf(realm);
            if (maxIdx < 0) return bonus;

            foreach (var ch in chapters)
            {
                int chIdx = order.IndexOf(ch.realm);
                if (chIdx >= 0 && chIdx <= maxIdx)
                {
                    bonus.soulShieldRate += ch.soulShieldRate;
                    bonus.hitRate += ch.hitRate;
                    bonus.blockRate += ch.blockRate;
                    bonus.critRate += ch.critRate;
                    bonus.critDamage += ch.critDamage;
                    bonus.dodgeRate += ch.dodgeRate;
                    bonus.magAtkBonus += ch.magAtkBonus;
                    bonus.magDefBonus += ch.magDefBonus;
                }
            }
            return bonus;
        }
    }
}
