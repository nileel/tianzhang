using System;
using TianZhang.Cultivation;
using TianZhang.Core;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Editor
{
    public static class TQ013CRealmAndSlotVerification
    {
        public static void Run()
        {
            AssertEqual(4, CultivationEngine.RealmOrder.Length, "玩家境界链长度");
            AssertEqual("凡人", CultivationEngine.RealmOrder[0], "玩家境界链[0]");
            AssertEqual("练气", CultivationEngine.RealmOrder[1], "玩家境界链[1]");
            AssertEqual("筑基", CultivationEngine.RealmOrder[2], "玩家境界链[2]");
            AssertEqual("金丹", CultivationEngine.RealmOrder[3], "玩家境界链[3]");
            AssertEqual(5, CultivationEngine.Sublevels["筑基"], "筑基小阶段");
            AssertEqual(3, CultivationEngine.Sublevels["金丹"], "金丹小阶段");
            AssertFalse(CultivationEngine.Sublevels.ContainsKey("炼虚"), "炼虚不应在玩家链");
            AssertEqual("紫府初开", CultivationEngine.StageName("筑基", 3), "紫府阶段显示");
            AssertEqual("金丹圆满", CultivationEngine.StageName("金丹", 2), "金丹阶段显示");

            AssertEqual(4, Character.GetDefaultSpellSlots(1.5f), "练气术法槽");
            AssertEqual(1, Character.GetDefaultSkillSlots(1.5f), "练气神通槽");
            AssertEqual(5, Character.GetDefaultSpellSlots(3f), "筑基术法槽");
            AssertEqual(2, Character.GetDefaultSkillSlots(3f), "筑基神通槽");
            AssertEqual(5, Character.GetDefaultSpellSlots(12f), "高阶兼容术法槽");
            AssertEqual(2, Character.GetDefaultSkillSlots(12f), "高阶兼容神通槽");

            var data = ScriptableObject.CreateInstance<CharacterData>();
            data.charName = "紫府测试";
            data.realmMultiplier = 3f;
            data.rootBone = 10;
            data.physique = 10;
            data.spirit = 10;
            data.mind = 10;
            data.reaction = 10;
            data.talent = 10;
            data.equippedSpells = Array.Empty<string>();
            data.equippedSkills = Array.Empty<string>();
            data.developedMansions = new[] { "命府", "气府", "识府" };

            var character = Character.FromData(data, new HexCoord(0, 0));
            AssertEqual(7, character.MaxSpellSlots, "紫府术法槽");
            AssertEqual(3, character.MaxSkillSlots, "紫府神通槽");
            AssertEqual(character.MaxSpellSlots, character.SpellCooldowns.Length, "术法冷却数组长度");
            AssertEqual(character.MaxSkillSlots, character.SkillCooldowns.Length, "神通冷却数组长度");

            var cultivator = new Character
            {
                Name = "结丹测试",
                RootBone = 20,
                Physique = 10,
                Spirit = 30,
                Mind = 18,
                Reaction = 12,
                Talent = 100
            };
            var result = CultivationEngine.Simulate(
                cultivator,
                null,
                spiritGrade: "极品",
                techGrade: "极品",
                treasureGrade: "极品",
                maxCycles: 1000,
                seed: 7);
            AssertEqual("金丹", result.FinalRealm, "默认玩家链终点");
            AssertEqual("成丹", result.FormedState, "成丹状态");
            AssertFalse(string.IsNullOrEmpty(result.DanJiType), "丹籍类型");
            AssertFalse(string.IsNullOrEmpty(result.OccupancyState), "占据状态");
            AssertFalse(string.IsNullOrEmpty(result.DanName), "丹名");
            AssertFalse(string.IsNullOrEmpty(result.DanNature), "丹性");
            AssertEqual(result.LegacyGCGrade, result.GCQuality, "旧品级兼容字段");

            Debug.Log("[TQ013C] Realm and slot verification passed.");
        }

        private static void AssertEqual<T>(T expected, T actual, string label)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }

        private static void AssertFalse(bool value, string label)
        {
            if (value)
                throw new InvalidOperationException($"{label}: expected false");
        }
    }
}
