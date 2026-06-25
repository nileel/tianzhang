using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

class Character
{
    public string Name, Realm, Style;
    public int SubIndex;
    // v3.5: 道基
    public string DFQuality = "无道基";   // 天品/地品/玄品/黄品/无道基
    public double DFMult = 0.0;            // 道基效果倍率
    public int DFScore = 0;                // 凝聚值 (调试用)
    // TQ-013B: 金丹兼容层。LegacyGCGrade 只用于调试回看，不驱动战斗强度。
    public string FormedState { get; set; } = "未成丹";
    public string DanJiType { get; set; } = "";
    public string OccupancyState { get; set; } = "未成丹";
    public string DanName { get; set; } = "";
    public string DanNature { get; set; } = "";
    public string LegacyGCGrade { get; set; } = "";
    public double DanJiStabilityMult { get; set; } = 1.0;
    public int GCScore = 0;               // 成丹判定值 (调试用)
    public double DanJiArtAffinityMult { get; set; } = 1.0;
    // v5.1: 功法名称（用于读取实际小境界成长表）
    public string GongFaName = "";
    // v4.1: 术法与神通
    public string ArtName = "";          // 术法名称
    public string ArtType = "";          // "物理" or "神魂"
    public string ArtElement = "";       // 术法五行属性
    public double ArtMult = 1.0;         // 术法倍率
    public int ArtMPCost = 0;            // 术法灵力消耗
    public int ArtCooldown = 3;          // 术法冷却回合数
    public string DivineName = "";       // 神通名称
    public string DivineType = "";       // "物理" or "神魂"
    public string DivineElement = "";    // 神通五行属性
    public double DivineMult = 1.0;      // 神通倍率
    public double DivineDefPen = 0;      // 神通防御穿透%
    public int DivineCooldown = 5;       // 神通冷却回合数

    public Dictionary<string, int> Innate = new();
    public Dictionary<string, int> Primary = new();
    public Dictionary<string, double> Secondary = new();
    public static string[] InnateKeys = ["根骨", "魂魄", "神识", "资质", "气运"];

    public static Character Create(string name, Dictionary<string, int> innate, string style)
    {
        var c = new Character { Name = name, Realm = "凡人", SubIndex = 0, Style = style };
        foreach (var k in InnateKeys) c.Innate[k] = innate[k];
        return c;
    }

    public void ApplyGrowth(string realm, string techGrade, Dictionary<string, double> weights)
    {
        int realmIdx = Array.IndexOf(GameData.RealmOrder, realm);
        int breakthroughs = realmIdx;
        if (breakthroughs == 0) return;
        int totalPts = breakthroughs * GameData.InnatePerBreakthrough;
        double tw = weights.Values.Sum();
        int sumAlloc = 0;
        var add = new Dictionary<string, int>();
        foreach (var k in InnateKeys) { int a = (int)Math.Round(totalPts * weights[k] / tw); add[k] = a; sumAlloc += a; }
        int diff = totalPts - sumAlloc;
        if (diff != 0) { string mk = InnateKeys[0]; foreach (var k in InnateKeys) if (weights[k] > weights[mk]) mk = k; add[mk] += diff; }
        foreach (var k in InnateKeys) Innate[k] += add[k];
    }

    static (string[] types, double pctPerChapter) ResolveSecondary(Dictionary<string, double> w)
    {
        var map = new Dictionary<string, string[]> {
            ["根骨"] = new[] { "格挡率" }, ["魂魄"] = new[] { "魂盾率" },
            ["神识"] = new[] { "命中率" }, ["资质"] = new[] { "暴击伤害" }, ["气运"] = new[] { "闪避率" }
        };
        var ordered = w.Where(kv => kv.Value > 0.15).OrderByDescending(kv => kv.Value).Take(2).ToList();
        if (ordered.Count == 0) ordered.Add(new("根骨", 1.0));
        var types = ordered.Select(kv => map[kv.Key][0]).ToArray();
        double pct = types.Length == 1 ? 5.0 : 3.0;
        return (types, pct);
    }

    public void FinalizeStats(string realm, int subIdx, string spiritGrade, Dictionary<string, double> weights)
    {
        Realm = realm; SubIndex = subIdx;
        var rb = GameData.Base[realm];
        var rf = GameData.Factor[realm];
        double sm = GameData.SpiritMod[spiritGrade];
        var (secTypes, secPctPerChapter) = ResolveSecondary(weights);

        // 一级属性 = 境界基础 + sum(小境界成长) + 先天×系数×权重
        Primary["HP"] = (int)Math.Round((rb.HP + SubGrowthSum("HP", realm, subIdx, weights, GongFaName) + Innate["根骨"] * rf.HP * weights["根骨"] * 2.2) * sm);
        Primary["MP"] = (int)Math.Round((rb.MP + SubGrowthSum("MP", realm, subIdx, weights) + Innate["魂魄"] * rf.MP * weights["魂魄"]) * sm * DanJiStabilityMult);
        Primary["肉攻"] = (int)Math.Round((rb.肉攻 + SubGrowthSum("肉攻", realm, subIdx, weights) + Innate["根骨"] * rf.攻 * weights["根骨"]) * sm);
        Primary["神攻"] = (int)Math.Round((rb.神攻 + SubGrowthSum("神攻", realm, subIdx, weights) + Innate["魂魄"] * rf.攻 * weights["魂魄"]) * sm);
        Primary["肉防"] = (int)Math.Round((rb.肉防 + SubGrowthSum("肉防", realm, subIdx, weights) + Innate["根骨"] * rf.防 * weights["根骨"]) * sm);
        Primary["神防"] = (int)Math.Round((rb.神防 + SubGrowthSum("神防", realm, subIdx, weights) + Innate["魂魄"] * rf.防 * weights["魂魄"]) * sm);
        Primary["反应"] = (int)Math.Round((rb.反应 + SubGrowthSum("反应", realm, subIdx, weights) + (Innate["根骨"] * weights["根骨"] + Innate["魂魄"] * weights["魂魄"] + Innate["神识"] * weights["神识"]) * rf.反应 / 3.0) * sm);
        Primary["移力"] = rb.移力;
        Primary["神识"] = (int)Math.Round((rb.神识 + SubGrowthSum("神识", realm, subIdx, weights) + Innate["神识"] * rf.神识 * weights["神识"]) * sm);

        // 二级属性
        int chapters = GameData.TotalSubs(realm, subIdx) / 4 + 1;
        foreach (var t in secTypes) Secondary[t] = chapters * secPctPerChapter;
        Secondary["格挡减伤率"] = Secondary.GetValueOrDefault("格挡率", 0) * 0.8;
        Secondary["魂盾减伤率"] = Secondary.GetValueOrDefault("魂盾率", 0) * 0.8;
        Secondary["闪避率"] = Secondary.GetValueOrDefault("闪避率", 0) + Innate["气运"] * 0.3;
        Secondary["命中率"] = Secondary.GetValueOrDefault("命中率", 0);
        Secondary["暴击率"] = Secondary.GetValueOrDefault("暴击率", 0) + Innate["神识"] * 0.3;
        Secondary["暴击伤害"] = Secondary.GetValueOrDefault("暴击伤害", 0) + Innate["资质"] * 0.8;
        Secondary["物抗率"] = 0; Secondary["魂抗率"] = 0;
    }

    double SubGrowthSum(string attr, string realm, int subIdx, Dictionary<string, double> w, string gongFaName = "")
    {
        // v5.1: 优先使用功法实际成长表
        if (!string.IsNullOrEmpty(gongFaName) && GameData.GongFaTables.TryGetValue(gongFaName, out var table))
        {
            double sum = 0; int totalSubs = GameData.TotalSubs(realm, subIdx);
            int prevSubs = 0;
            foreach (var r in GameData.RealmOrder)
            {
                if (r == "凡人") continue;
                int subsHere = GameData.Sublevels[r];
                int effective = Math.Min(subsHere, Math.Max(0, totalSubs - prevSubs));
                if (effective <= 0) break;
                if (!table.TryGetValue(r, out var grow)) break;
                double val = attr switch
                {
                    "HP" => grow.HP, "MP" => grow.MP, "肉攻" => grow.肉攻, "神攻" => grow.神攻,
                    "肉防" => grow.肉防, "神防" => grow.神防, "反应" => grow.反应, "神识" => grow.神识, _ => 0
                };
                sum += val * effective;
                prevSubs += subsHere;
                if (r == realm) break;
            }
            return sum;
        }
        // 回退：原有权重近似计算
        double wsum = 0; int wtotalSubs = GameData.TotalSubs(realm, subIdx);
        int wprevSubs = 0;
        foreach (var r in GameData.RealmOrder)
        {
            if (r == "凡人") continue;
            int subsHere = GameData.Sublevels[r];
            int effective = Math.Min(subsHere, Math.Max(0, wtotalSubs - wprevSubs));
            if (effective <= 0) break;
            var sgb = GameData.SubGrowthBase[r];
            double val = attr switch
            {
                "HP" => sgb.HP, "MP" => sgb.MP, "肉攻" => sgb.肉攻, "神攻" => sgb.神攻,
                "肉防" => sgb.肉防, "神防" => sgb.神防, "反应" => sgb.反应, "神识" => sgb.神识, _ => 0
            };
            string innateKey = attr switch { "HP" or "肉攻" or "肉防" => "根骨", "MP" or "神攻" or "神防" => "魂魄", "神识" => "神识", "反应" => "根骨", _ => "根骨" };
            double scale = w[innateKey] / 0.6;
            wsum += val * scale * effective;
            wprevSubs += subsHere;
            if (r == realm) break;
        }
        return wsum;
    }

    // v4.1: 根据风格和境界分配术法与神通
    public void AssignArts()
    {
        var artCfg = Style switch { "water_physical" => GameData.WaterArt, "physical" => GameData.PhysicalArt, "taiyi_fuxiu" => GameData.TaiyiFuxiuArt, "taiyi" => GameData.TaiyiArt, "taixu" => GameData.TaixuArt, "taixu_xuangan" => GameData.TaixuArt, "yuqing" => GameData.YuqingArt, "yuqing_kuxing" => GameData.YuqingArt, "yuqing_leijie" => GameData.YuqingLeijieArt, _ => GameData.MagicArt };
        ArtName = artCfg.Name; ArtType = artCfg.Type; ArtElement = artCfg.Element; ArtMult = artCfg.Mult; ArtMPCost = artCfg.MPCost; ArtCooldown = artCfg.Cooldown;
        if (Realm == "金丹" && FormedState == "成丹" && !string.IsNullOrEmpty(DanJiType))
        {
            var divCfg = Style switch { "water_physical" => GameData.WaterDivine, "physical" => GameData.PhysicalDivine, "taiyi_fuxiu" => GameData.TaiyiFuxiuDivine, "taiyi" => GameData.TaiyiDivine, "taixu" => GameData.TaixuDivine, "taixu_xuangan" => GameData.TaixuDivine, "yuqing" => GameData.YuqingDivine, "yuqing_kuxing" => GameData.YuqingDivine, "yuqing_leijie" => GameData.YuqingLeijieDivine, _ => GameData.MagicDivine };
            DivineName = divCfg.Name; DivineType = divCfg.Type; DivineElement = divCfg.Element; DivineMult = divCfg.Mult; DivineDefPen = divCfg.DefPen; DivineCooldown = divCfg.Cooldown;
        }
    }

}
