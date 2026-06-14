using System;
using System.Collections.Generic;
using UnityEngine;
using TianZhang.Entity;

namespace TianZhang.Cultivation
{
    /// <summary>
    /// 修炼引擎 — 与 BattleSim Cultivation.Simulate 逻辑对齐
    /// 修炼循环 + 突破率 + 丹药加成 + 道基/金丹生成
    /// </summary>
    public static class CultivationEngine
    {
        // ═══ 常量（与 BattleSim GameData 一致）═══
        public const int CultivationCycles = 200;
        public const float BaseGainPerCycle = 10f;
        public const float BreakthroughBaseRate = 0.70f;
        public const int InnatePerBreakthrough = 14;

        // 小境界数量
        public static readonly Dictionary<string, int> Sublevels = new()
        {
            ["凡人"] = 1, ["练气"] = 9, ["筑基"] = 4, ["金丹"] = 4,
            ["元婴"] = 4, ["化神"] = 4, ["炼虚"] = 4
        };

        // 境界顺序
        public static readonly string[] RealmOrder = { "凡人", "练气", "筑基", "金丹", "元婴", "化神", "炼虚" };

        // 灵根品级灵力修正
        public static readonly Dictionary<string, float> SpiritMod = new()
        {
            ["凡品"] = 0.70f, ["下品"] = 0.85f, ["中品"] = 1.00f, ["上品"] = 1.20f, ["极品"] = 1.50f
        };

        // 突破里程碑：(境界, 小级序号, 所需CPP)
        private static readonly (string realm, int subIdx, float cpp)[] Milestones = new[]
        {
            ("练气", 0, 10f), ("练气", 1, 22f), ("练气", 2, 36f), ("练气", 3, 52f),
            ("练气", 4, 70f), ("练气", 5, 90f), ("练气", 6, 112f), ("练气", 7, 136f), ("练气", 8, 162f),
            ("筑基", 0, 200f), ("筑基", 1, 250f), ("筑基", 2, 310f), ("筑基", 3, 390f),
            ("金丹", 0, 500f), ("金丹", 1, 650f), ("金丹", 2, 830f), ("金丹", 3, 1050f),
            ("元婴", 0, 1350f), ("元婴", 1, 1620f), ("元婴", 2, 1940f), ("元婴", 3, 2320f),
            ("化神", 0, 2780f), ("化神", 1, 3340f), ("化神", 2, 4010f), ("化神", 3, 4810f),
        };

        /// <summary>修炼结果</summary>
        public struct CultivationResult
        {
            public string FinalRealm;
            public int FinalSubIdx;
            public int TotalSubsAchieved;
            public float CultivationProgress; // 当前CPP
            public float NextMilestoneCPP;    // 下一突破所需CPP
            public string DFQuality;          // 道基品级
            public int DFScore;
            public string GCQuality;          // 金丹品级
            public int GCScore;
            public int Breakthroughs;         // 成功突破次数
            public int Failures;              // 突破失败次数
            public List<string> Log;
        }

        /// <summary>
        /// 模拟修炼过程
        /// </summary>
        public static CultivationResult Simulate(
            Character character,
            GongFaGrowthData gongFa,
            string spiritGrade = "中品",
            string techGrade = "上品",
            string treasureGrade = "",
            int maxCycles = -1,
            int seed = -1)
        {
            var rng = seed < 0 ? new System.Random() : new System.Random(seed);
            var result = new CultivationResult { Log = new List<string>() };
            int cycles = maxCycles < 0 ? CultivationCycles : maxCycles;

            float cpp = 0;
            string realm = "凡人";
            int subIdx = 0;
            string dfQuality = "无道基";
            int dfScore = 0;
            string gcQuality = "";
            int gcScore = 0;
            bool dfGenerated = false;
            bool gcGenerated = false;

            // 修炼速度 = 1.0 + 资质 * 0.03
            float cultivateSpeed = 1f + character.Talent * 0.03f;
            // 悟性 = 1.0 + 资质 * 0.015
            float insight = 1f + character.Talent * 0.015f;
            // 天材地宝加成
            float treasureBonus = treasureGrade switch
            {
                "下品" => 10f, "中品" => 15f, "上品" => 20f, "极品" => 25f, _ => 0f
            };
            // 突破率
            float breakthroughRate = Mathf.Clamp(
                BreakthroughBaseRate * 100f + (insight - 1f) * 50f + character.Talent * 0.05f + treasureBonus,
                20f, 95f) / 100f;

            int nextMs = 0;
            int breakthroughs = 0;
            int failures = 0;

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                float gain = BaseGainPerCycle * cultivateSpeed * (0.85f + (float)rng.NextDouble() * 0.30f);
                cpp += gain;

                bool progressed = true;
                while (progressed && nextMs < Milestones.Length)
                {
                    var (msRealm, msSubIdx, msCpp) = Milestones[nextMs];
                    if (cpp < msCpp) { progressed = false; break; }

                    if (rng.NextDouble() < breakthroughRate)
                    {
                        breakthroughs++;
                        string prevRealm = realm;
                        realm = msRealm;
                        subIdx = msSubIdx;
                        cpp -= msCpp;
                        result.Log.Add($"突破! {prevRealm} → {realm} Lv.{subIdx}");

                        // 道基生成（筑基初阶）
                        if (!dfGenerated && realm == "筑基" && subIdx == 0)
                        {
                            dfGenerated = true;
                            float overflowCpp = Mathf.Max(0, cpp);
                            (dfQuality, dfScore) = GenerateDaoFoundation(
                                character.Talent, spiritGrade, techGrade, overflowCpp, treasureGrade, rng);
                            result.Log.Add($"道基凝结: {dfQuality} (评分{dfScore})");
                        }

                        // 金丹生成（金丹初阶）
                        if (!gcGenerated && realm == "金丹" && subIdx == 0)
                        {
                            gcGenerated = true;
                            float totalCpp = msCpp + Mathf.Max(0, cpp);
                            (gcQuality, gcScore) = GenerateGoldenCore(
                                character.Talent, spiritGrade, techGrade, dfQuality, totalCpp, treasureGrade, rng);
                            result.Log.Add($"金丹凝结: {gcQuality} (评分{gcScore})");
                        }

                        // 应用突破属性成长
                        if (gongFa != null)
                            ApplyBreakthroughGrowth(character, gongFa, realm);

                        nextMs = FindNextMilestone(realm, subIdx);
                    }
                    else
                    {
                        failures++;
                        float penalty = msCpp * (0.10f + (float)rng.NextDouble() * 0.10f);
                        cpp = Mathf.Max(0, cpp - penalty);
                        result.Log.Add($"突破失败，CPP-{penalty:F0}");
                        progressed = false;
                    }
                }
            }

            result.FinalRealm = realm;
            result.FinalSubIdx = subIdx;
            result.TotalSubsAchieved = TotalSubs(realm, subIdx);
            result.CultivationProgress = cpp;
            result.NextMilestoneCPP = nextMs < Milestones.Length ? Milestones[nextMs].cpp : float.MaxValue;
            result.DFQuality = dfQuality;
            result.DFScore = dfScore;
            result.GCQuality = gcQuality;
            result.GCScore = gcScore;
            result.Breakthroughs = breakthroughs;
            result.Failures = failures;

            return result;
        }

        /// <summary>应用突破后的属性成长</summary>
        private static void ApplyBreakthroughGrowth(Character c, GongFaGrowthData gongFa, string newRealm)
        {
            var growth = gongFa.GetGrowth(newRealm);
            float realmMult = GetRealmMultiplier(newRealm);
            int subCount = Sublevels.GetValueOrDefault(newRealm, 1);

            // 应用整个境界的成长 = 每级成长 × 小境界数 × 境界倍率
            c.MaxHP += Mathf.RoundToInt(growth.hp * subCount * realmMult);
            c.MaxMP += Mathf.RoundToInt(growth.mp * subCount * realmMult);
            c.PhysAtk += Mathf.RoundToInt(growth.physAtk * subCount * realmMult);
            c.MagAtk += Mathf.RoundToInt(growth.magAtk * subCount * realmMult);
            c.PhysDef += Mathf.RoundToInt(growth.physDef * subCount * realmMult);
            c.MagDef += Mathf.RoundToInt(growth.magDef * subCount * realmMult);
            c.Reaction += Mathf.RoundToInt(growth.reaction * realmMult);
            c.MovePoints = Mathf.Clamp(Mathf.RoundToInt(c.Reaction / 20f), 2, 8);

            c.CurrentHP = c.MaxHP;
            c.CurrentMP = c.MaxMP;
        }

        /// <summary>境界倍率</summary>
        public static float GetRealmMultiplier(string realm) => realm switch
        {
            "凡人" => 1.0f,
            "练气" => 1.5f,
            "筑基" => 3.0f,
            "金丹" => 6.0f,
            "元婴" => 12.0f,
            "化神" => 24.0f,
            "炼虚" => 48.0f,
            _ => 1.0f
        };

        /// <summary>道基生成（与 BattleSim 一致）</summary>
        private static (string quality, int score) GenerateDaoFoundation(
            int talent, string spiritGrade, string techGrade,
            float overflowCpp, string treasureGrade, System.Random rng)
        {
            int spiritBase = spiritGrade switch
            {
                "凡品" => 15, "下品" => 22, "中品" => 30, "上品" => 40, "极品" => 55, _ => 30
            };
            int techMod = techGrade switch
            {
                "下品" => 5, "中品" => 10, "上品" => 15, "极品" => 22, _ => 10
            };
            int overflowMod = Mathf.FloorToInt(overflowCpp / 50f);
            int dice = rng.Next(1, 31);
            int treasureBonus = treasureGrade switch
            {
                "下品" => 10, "中品" => 15, "上品" => 20, "极品" => 25, _ => 0
            };

            int score = spiritBase + techMod + overflowMod + dice + treasureBonus;
            string quality = score switch
            {
                >= 120 => "极品道基",
                >= 90 => "上品道基",
                >= 60 => "中品道基",
                >= 30 => "下品道基",
                _ => "无品道基"
            };
            return (quality, score);
        }

        /// <summary>金丹生成</summary>
        private static (string quality, int score) GenerateGoldenCore(
            int talent, string spiritGrade, string techGrade,
            string dfQuality, float totalCpp, string treasureGrade, System.Random rng)
        {
            int spiritBase = spiritGrade switch
            {
                "凡品" => 20, "下品" => 30, "中品" => 40, "上品" => 55, "极品" => 75, _ => 40
            };
            int dfBonus = dfQuality switch
            {
                "极品道基" => 40, "上品道基" => 25, "中品道基" => 15, "下品道基" => 5, _ => 0
            };
            int cppBonus = Mathf.FloorToInt(totalCpp / 100f);
            int dice = rng.Next(1, 41);
            int treasureBonus = treasureGrade switch
            {
                "下品" => 15, "中品" => 22, "上品" => 30, "极品" => 40, _ => 0
            };

            int score = spiritBase + dfBonus + cppBonus + dice + treasureBonus;
            string quality = score switch
            {
                >= 180 => "九转金丹",
                >= 130 => "上品金丹",
                >= 80 => "中品金丹",
                >= 40 => "下品金丹",
                _ => "虚丹"
            };
            return (quality, score);
        }

        private static int FindNextMilestone(string realm, int subIdx)
        {
            for (int i = 0; i < Milestones.Length; i++)
            {
                if (Milestones[i].realm == realm && Milestones[i].subIdx == subIdx)
                    return i + 1;
            }
            return Milestones.Length; // 已达顶点
        }

        private static int TotalSubs(string realm, int subIdx)
        {
            int total = 0;
            for (int i = 0; i < RealmOrder.Length; i++)
            {
                string r = RealmOrder[i];
                int subs = Sublevels.GetValueOrDefault(r, 0);
                if (r != realm) { total += subs; continue; }
                total += subIdx + 1;
                break;
            }
            return total;
        }
    }
}
