using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class GameData
{
    public static readonly Dictionary<string, int> Sublevels = new()
    {
        ["凡人"] = 1, ["练气"] = 9, ["筑基"] = 4, ["金丹"] = 4,
        ["元婴"] = 4, ["化神"] = 4, ["炼虚"] = 4
    };
    public static readonly string[] RealmOrder = ["凡人", "练气", "筑基", "金丹", "元婴", "化神", "炼虚"];
    public static readonly Dictionary<string, int> TechInnate = new() { ["上品"] = 4 };
    public static readonly Dictionary<string, double> SpiritMod = new()
    {
        ["凡品"] = 0.70, ["下品"] = 0.85, ["中品"] = 1.00, ["上品"] = 1.20, ["极品"] = 1.50
    };

    public record RealmBase(int HP, int MP, int 肉攻, int 神攻, int 肉防, int 神防, int 反应, int 移力, int 神识);
    public static readonly Dictionary<string, RealmBase> Base = new()
    {
        ["凡人"] = new(30, 0, 5, 5, 3, 3, 5, 2, 3),
        ["练气"] = new(100, 10, 25, 25, 20, 20, 15, 3, 5),
        ["筑基"] = new(400, 100, 120, 120, 100, 100, 50, 4, 8),
        ["金丹"] = new(3000, 2000, 1000, 1000, 800, 800, 300, 5, 15),
    };
    // v3.2: 移力系数移除，移力改为纯境界固定值
    public record RealmFactor(double HP, double MP, double 攻, double 防, double 反应, double 神识);
    public static readonly Dictionary<string, RealmFactor> Factor = new()
    {
        ["凡人"] = new(4, 0.5, 1, 0.8, 0.6, 0.15),
        ["练气"] = new(8, 2, 3, 2, 0.75, 0.20),
        ["筑基"] = new(9, 7, 6.5, 4, 1.5, 0.25),
        ["金丹"] = new(22, 14, 12, 8, 3.0, 0.35),
    };
    // v3.4: 小境界一级属性成长基准表（★权重=0.6时，每级增量）
    public record SubGrowth(double HP, double MP, double 肉攻, double 神攻, double 肉防, double 神防, double 反应, double 移力, double 神识);
    public static readonly Dictionary<string, SubGrowth> SubGrowthBase = new()
    {
        ["练气"] = new(8, 3, 4, 3, 3, 2, 2, 0.2, 0.3),
        ["筑基"] = new(80, 35, 30, 25, 25, 20, 12, 0.5, 1.0),
        ["金丹"] = new(800, 700, 350, 320, 280, 250, 90, 0.8, 4.0),
    };

    // 大境界突破先天成长（上品功法=14/突破）
    public const int InnatePerBreakthrough = 14;


    public static readonly (string realm, int subIdx, int cpp)[] Milestones = new (string, int, int)[]
    {
        ("练气", 0, 10), ("练气", 1, 22), ("练气", 2, 36), ("练气", 3, 52),
        ("练气", 4, 70), ("练气", 5, 90), ("练气", 6, 112), ("练气", 7, 136), ("练气", 8, 162),
        ("筑基", 0, 200), ("筑基", 1, 250), ("筑基", 2, 310), ("筑基", 3, 390),
        ("金丹", 0, 500), ("金丹", 1, 650), ("金丹", 2, 830), ("金丹", 3, 1050),
    };

    public const double BaseGainPerCycle = 10.0;
    public const int CultivationCycles = 200;
    public const double BreakthroughBaseRate = 0.70;

    public static int TotalSubs(string realm, int subIdx)
    {
        int t = 0;
        foreach (var r in RealmOrder) { if (r == realm) { t += subIdx + 1; break; } t += Sublevels[r]; }
        return t;
    }
}

class Character
{
    public string Name, Realm, Style;
    public int SubIndex;
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

    // v3.4: 先天属性仅在大境界突破时成长
    public void ApplyGrowth(string realm, string techGrade, Dictionary<string, double> weights)
    {
        int realmIdx = Array.IndexOf(GameData.RealmOrder, realm);
        int breakthroughs = realmIdx;  // 凡人=0, 练气=1, 筑基=2, ...
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

    // v3.4: 按功法权重+风格决定二级属性（仅权重>0.15的属性参与，最多2种）
    static (string[] types, double pctPerChapter) ResolveSecondary(Dictionary<string, double> w)
    {
        var map = new Dictionary<string, string[]> {
            ["根骨"] = new[] { "格挡率" }, ["魂魄"] = new[] { "魂盾率" },
            ["神识"] = new[] { "命中率", "暴击率" }, ["资质"] = new[] { "暴击伤害" },
            ["气运"] = new[] { "闪避率" },
        };
        // Only attributes with meaningful weight
        var valid = w.Where(kv => kv.Value > 0.15).OrderByDescending(kv => kv.Value).ToList();
        var picked = new List<string>();
        int limit = Math.Min(2, valid.Count);
        for (int i = 0; i < limit; i++) {
            foreach (var c in map[valid[i].Key]) {
                if (!picked.Contains(c) && picked.Count < 2) picked.Add(c);
            }
        }
        double pct = picked.Count == 1 ? 5.0 : 3.0;
        return (picked.ToArray(), pct);
    }
    // v3.4: 一级属性 = 境界基础 + Σ小境界成长 + 先天×系数×(权重/0.6)
    public void FinalizeStats(string realm, int subIdx, string spiritGrade, Dictionary<string, double> weights)
    {
        Realm = realm; SubIndex = subIdx;
        var b = GameData.Base[realm]; var f = GameData.Factor[realm];
        double mod = GameData.SpiritMod[spiritGrade];

        double guMul = weights["根骨"] / 0.6, hunMul = weights["魂魄"] / 0.6, shenMul = weights["神识"] / 0.6;

        // Accumulate per-realm sub-growth
        double hpG = 0, mpG = 0, rgG = 0, sgG = 0, rfG = 0, sfG = 0, fyG = 0, ylG = 0, ssG = 0;
        int curIdx = Array.IndexOf(GameData.RealmOrder, realm);
        foreach (var r in GameData.RealmOrder)
        {
            if (r == "凡人") continue;
            int rIdx = Array.IndexOf(GameData.RealmOrder, r);
            int subs = rIdx < curIdx ? GameData.Sublevels[r] : rIdx == curIdx ? subIdx : 0;
            if (subs <= 0) break;
            var sg = GameData.SubGrowthBase[r];
            hpG += subs * sg.HP * guMul;   mpG += subs * sg.MP * hunMul;
            rgG += subs * sg.肉攻 * guMul;  sgG += subs * sg.神攻 * hunMul;
            rfG += subs * sg.肉防 * guMul;  sfG += subs * sg.神防 * shenMul;
            fyG += subs * sg.反应 * shenMul; ylG += subs * sg.移力;
            ssG += subs * sg.神识 * shenMul;
        }

        Primary["HP"]   = (int)Math.Round((b.HP + hpG + Innate["根骨"] * f.HP * (weights["根骨"]/0.6)) * mod);
        Primary["MP"]   = (int)Math.Round((b.MP + mpG + Innate["魂魄"] * f.MP * (weights["魂魄"]/0.6)) * mod);
        Primary["肉攻"] = (int)Math.Round((b.肉攻 + rgG + Innate["根骨"] * f.攻 * (weights["根骨"]/0.6)) * mod);
        Primary["神攻"] = (int)Math.Round((b.神攻 + sgG + Innate["魂魄"] * f.攻 * (weights["魂魄"]/0.6)) * mod);
        Primary["肉防"] = (int)Math.Round((b.肉防 + rfG + Innate["根骨"] * f.防 * (weights["根骨"]/0.6)) * mod);
        Primary["神防"] = (int)Math.Round((b.神防 + sfG + Innate["神识"] * f.防 * (weights["神识"]/0.6)) * mod);
        Primary["反应"] = (int)Math.Round((b.反应 + fyG + Innate["神识"] * f.反应 * (weights["神识"]/0.6)) * mod);
        Primary["移力"] = b.移力;
        Primary["神识"] = (int)Math.Round(b.神识 + ssG + Innate["神识"] * f.神识 * (weights["神识"]/0.6));

        // v3.4: 二级属性按功法权重聚焦1-2种（单种5%/篇章，两种各3%/篇章）
        int chapters = curIdx;  // 境界数=已解锁篇章数
        var (secTypes, secPct) = ResolveSecondary(weights);
        double chTotal = chapters * secPct;
        Secondary["生命恢复"] = Primary["HP"] * 2.0 / 100;
        Secondary["格挡率"]   = secTypes.Contains("格挡率") ? chTotal : 0;
        Secondary["魂盾率"]   = secTypes.Contains("魂盾率") ? chTotal : 0;
        Secondary["格挡减伤率"] = Secondary["格挡率"] > 0 ? 55 : 0;
        Secondary["魂盾减伤率"] = Secondary["魂盾率"] > 0 ? 55 : 0;
        Secondary["命中率"]   = secTypes.Contains("命中率") ? chTotal : 12;
        Secondary["暴击率"]   = secTypes.Contains("暴击率") ? chTotal : 10;
        Secondary["暴击伤害"] = secTypes.Contains("暴击伤害") ? (170 + chTotal) : 170;
        Secondary["闪避率"]   = secTypes.Contains("闪避率") ? chTotal : 12;
        Secondary["物抗率"]   = 12;
        Secondary["魂抗率"]   = 12;
    }
    static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
}

static class Cultivation
{
    public static (string realm, int subIdx, int totalSubs) Simulate(
        Dictionary<string, int> baseInnate, Dictionary<string, double> weights, int seed)
    {
        var rng = new Random(seed);
        double cpp = 0;
        string realm = "凡人"; int subIdx = 0;

        double 资质 = baseInnate["资质"], 气运 = baseInnate["气运"];
        double 修炼速度 = 1.0 + 资质 * 0.03;
        double 悟性 = 1.0 + 资质 * 0.015;
        double 突破率 = Clamp(GameData.BreakthroughBaseRate * 100 + (悟性 - 1.0) * 50 + 气运 * 0.05, 20, 95) / 100.0;
        int nextMs = 0;

        for (int cycle = 0; cycle < GameData.CultivationCycles; cycle++)
        {
            double gain = GameData.BaseGainPerCycle * 修炼速度 * (0.85 + rng.NextDouble() * 0.30);
            cpp += gain;
            bool progressed = true;
            while (progressed && nextMs < GameData.Milestones.Length)
            {
                var ms = GameData.Milestones[nextMs];
                if (cpp < ms.cpp) { progressed = false; break; }
                if (rng.NextDouble() < 突破率)
                {
                    realm = ms.realm; subIdx = ms.subIdx; cpp -= ms.cpp;
                    nextMs = FindNext(realm, subIdx);
                }
                else { double penalty = ms.cpp * (0.10 + rng.NextDouble() * 0.10); cpp = Math.Max(0, cpp - penalty); progressed = false; }
            }
        }
        return (realm, subIdx, GameData.TotalSubs(realm, subIdx));
    }

    static int FindNext(string realm, int subIdx)
    {
        for (int i = 0; i < GameData.Milestones.Length; i++)
        {
            var ms = GameData.Milestones[i];
            if (ms.realm == realm && ms.subIdx == subIdx + 1) return i;
            int ri = Array.IndexOf(GameData.RealmOrder, realm), mri = Array.IndexOf(GameData.RealmOrder, ms.realm);
            if (mri == ri + 1 && ms.subIdx == 0) return i;
        }
        return GameData.Milestones.Length;
    }
    static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
}

static class Combat
{
    static readonly Random Rng = new();
    static int Dmg(int atk, int def, double resist)
    {
        double df = atk / (double)(atk + def);
        return (int)Math.Max(0, Math.Round(atk * df * (1 - resist / 100.0)));
    }

    public static (double winsA, double winsB) Simulate(Character ca, Character cb, int rounds)
    {
        int winsA = 0, winsB = 0;
        for (int r = 0; r < rounds; r++)
        {
            int hpA = ca.Primary["HP"], hpB = cb.Primary["HP"];
            double ctA = 0, ctB = 0;
            double sA = 100.0 / ca.Primary["反应"], sB = 100.0 / cb.Primary["反应"];

            while (hpA > 0 && hpB > 0)
            {
                if (ctA <= ctB)
                {
                    int dmg = ca.Style == "physical" ? Dmg(ca.Primary["肉攻"], cb.Primary["肉防"], cb.Secondary["物抗率"])
                                                      : Dmg(ca.Primary["神攻"], cb.Primary["神防"], cb.Secondary["魂抗率"]);
                    // v3.2: 格挡/魂盾改为减伤率制
                    double blockRate = ca.Style == "physical" ? cb.Secondary["格挡率"] : cb.Secondary["魂盾率"];
                    if (Rng.NextDouble() * 100 < blockRate)
                    {
                        double reduction = ca.Style == "physical" ? cb.Secondary["格挡减伤率"] : cb.Secondary["魂盾减伤率"];
                        dmg = (int)Math.Round(dmg * (1 - reduction / 100));
                    }
                    if (Rng.NextDouble() * 100 < Math.Max(0, cb.Secondary["闪避率"] - ca.Secondary["命中率"])) dmg = 0;
                    if (dmg > 0 && Rng.NextDouble() * 100 < ca.Secondary["暴击率"]) dmg = (int)Math.Round(dmg * ca.Secondary["暴击伤害"] / 100);
                    hpB -= dmg; hpA += (int)ca.Secondary["生命恢复"]; ctA += sA;
                }
                else
                {
                    int dmg = cb.Style == "physical" ? Dmg(cb.Primary["肉攻"], ca.Primary["肉防"], ca.Secondary["物抗率"])
                                                      : Dmg(cb.Primary["神攻"], ca.Primary["神防"], ca.Secondary["魂抗率"]);
                    double blockRate = cb.Style == "physical" ? ca.Secondary["格挡率"] : ca.Secondary["魂盾率"];
                    if (Rng.NextDouble() * 100 < blockRate)
                    {
                        double reduction = cb.Style == "physical" ? ca.Secondary["格挡减伤率"] : ca.Secondary["魂盾减伤率"];
                        dmg = (int)Math.Round(dmg * (1 - reduction / 100));
                    }
                    if (Rng.NextDouble() * 100 < Math.Max(0, ca.Secondary["闪避率"] - cb.Secondary["命中率"])) dmg = 0;
                    if (dmg > 0 && Rng.NextDouble() * 100 < cb.Secondary["暴击率"]) dmg = (int)Math.Round(dmg * cb.Secondary["暴击伤害"] / 100);
                    hpA -= dmg; hpB += (int)cb.Secondary["生命恢复"]; ctB += sB;
                }
                if (hpA <= 0) { winsB++; break; }
                if (hpB <= 0) { winsA++; break; }
            }
        }
        return (winsA * 100.0 / rounds, winsB * 100.0 / rounds);
    }
}
class Program
{
    record BuildDef(string Name, string Desc, Dictionary<string, int> Innate, string Style, Dictionary<string, double> Weights);

    static void Main()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const string TECH = "上品", SPIRIT = "中品";
        const int SEEDS = 20, SIM = 2000;

        var buildDefs = new BuildDef[]
        {
            new("物·纯战", "资质3根骨25", new() { ["根骨"]=25,["魂魄"]=8,["神识"]=5,["资质"]=3,["气运"]=5 }, "physical",
                new() { ["根骨"]=0.8,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.0,["气运"]=0.0 }),
            new("物·均衡", "资质11根骨20", new() { ["根骨"]=20,["魂魄"]=8,["神识"]=8,["资质"]=11,["气运"]=8 }, "physical",
                new() { ["根骨"]=0.4,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.2,["气运"]=0.2 }),
            new("物·修炼", "资质19根骨15", new() { ["根骨"]=15,["魂魄"]=8,["神识"]=8,["资质"]=19,["气运"]=8 }, "physical",
                new() { ["根骨"]=0.3,["魂魄"]=0.1,["神识"]=0.1,["资质"]=0.4,["气运"]=0.1 }),
            new("肉盾型",  "根骨43极限", new() { ["根骨"]=43,["魂魄"]=5,["神识"]=5,["资质"]=5,["气运"]=5 }, "physical",
                new() { ["根骨"]=1.0,["魂魄"]=0.0,["神识"]=0.0,["资质"]=0.0,["气运"]=0.0 }),
            new("法·纯战", "资质3魂魄25", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=8,["资质"]=3,["气运"]=5 }, "magic",
                new() { ["根骨"]=0.0,["魂魄"]=0.8,["神识"]=0.1,["资质"]=0.0,["气运"]=0.1 }),
            new("法·均衡", "资质11魂魄20", new() { ["根骨"]=8,["魂魄"]=20,["神识"]=8,["资质"]=11,["气运"]=8 }, "magic",
                new() { ["根骨"]=0.1,["魂魄"]=0.4,["神识"]=0.1,["资质"]=0.2,["气运"]=0.2 }),
            new("法·修炼", "资质19魂魄15", new() { ["根骨"]=8,["魂魄"]=15,["神识"]=8,["资质"]=19,["气运"]=8 }, "magic",
                new() { ["根骨"]=0.1,["魂魄"]=0.3,["神识"]=0.1,["资质"]=0.4,["气运"]=0.1 }),
            new("灵修型",  "魂魄43极限", new() { ["根骨"]=5,["魂魄"]=43,["神识"]=5,["资质"]=5,["气运"]=5 }, "magic",
                new() { ["根骨"]=0.0,["魂魄"]=1.0,["神识"]=0.0,["资质"]=0.0,["气运"]=0.0 }),
        };
        int N = buildDefs.Length;

        Console.WriteLine($"修炼模拟 ({SEEDS}种子 × {GameData.CultivationCycles}轮)...");
        var pool = new List<Character>[N];
        var realmDist = new Dictionary<string, int>[N];
        for (int i = 0; i < N; i++) { pool[i] = new List<Character>(); realmDist[i] = new Dictionary<string, int> { ["练气"] = 0, ["筑基"] = 0, ["金丹"] = 0 }; }

        for (int seed = 0; seed < SEEDS; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var (realm, subIdx, totalSubs) = Cultivation.Simulate(bd.Innate, bd.Weights, seed * 100 + i);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(realm, TECH, bd.Weights);
                c.FinalizeStats(realm, subIdx, SPIRIT, bd.Weights);
                pool[i].Add(c);
                realmDist[i][c.Realm]++;
            }
        }
        Console.WriteLine("完成");

        // v3.4: 详细子境界分布
        Console.WriteLine();
        Console.WriteLine("【详细境界分布】");
        for (int i = 0; i < N; i++)
        {
            var groups = pool[i].GroupBy(c => $"{c.Realm}{c.SubIndex}").OrderBy(g => g.Key);
            Console.WriteLine($"  {buildDefs[i].Name,-8}: {string.Join(", ", groups.Select(g => $"{g.Key}({g.Count()})"))}");
        }
        Console.WriteLine();

        // 统计
        Console.WriteLine("【境界 & 属性分布】");
        Console.WriteLine($"{"Build",-10} {"资质",-5} {"练气%",-7} {"筑基%",-7} {"金丹%",-7} {"平均HP",-7} {"成长率",-7}");
        Console.WriteLine(new string('-', 65));
        for (int i = 0; i < N; i++)
        {
            double avgHp = pool[i].Average(c => (double)c.Primary["HP"]);
            double avg资 = pool[i].Average(c => (double)c.Innate["资质"]);
            double lq = realmDist[i]["练气"] * 100.0 / SEEDS;
            double zj = realmDist[i]["筑基"] * 100.0 / SEEDS;
            double jd = realmDist[i]["金丹"] * 100.0 / SEEDS;
            Console.WriteLine($"  {buildDefs[i].Name,-8} {avg资,4:F0}  {lq,5:F0}%  {zj,5:F0}%  {jd,5:F0}%  {avgHp,6:F0}  {buildDefs[i].Desc}");
        }
        Console.WriteLine();

        // 战斗矩阵
        Console.WriteLine($"正在计算 {N}×{N} 矩阵...");
        var rng = new Random(12345);
        double[,] mat = new double[N, N];
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                int wI = 0, tot = 0;
                for (int s = 0; s < SEEDS; s++)
                {
                    var ci = pool[i][s]; var cj = pool[j][s];
                    int h = SIM / SEEDS / 2;
                    var (wi, wj) = Combat.Simulate(ci, cj, h);
                    var (wi2, wj2) = Combat.Simulate(cj, ci, h);
                    wI += (int)Math.Round((wi + wj2) / 2.0 * h * 2 / 100.0);
                    tot += h * 2;
                }
                mat[i, j] = wI * 100.0 / tot;
                mat[j, i] = 100.0 - mat[i, j];
            }
        }
        sw.Stop();

        Console.WriteLine();
        Console.WriteLine($"================================================================================");
        Console.WriteLine($"  修炼后战斗胜率矩阵 ({SEEDS}次修炼 × {SIM}轮, {sw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"================================================================================");
        Console.WriteLine();

        string[] tags = buildDefs.Select(b => b.Name).ToArray();
        Console.Write($"{"",-10}");
        for (int j = 0; j < N; j++) Console.Write($"{tags[j],-8}");
        Console.WriteLine();
        Console.WriteLine(new string('-', 10 + 8 * N));

        for (int i = 0; i < N; i++)
        {
            Console.Write($"{tags[i],-10}");
            for (int j = 0; j < N; j++)
            {
                if (i == j) Console.Write($"{"---",-8}");
                else
                {
                    double p = mat[i, j];
                    string tag = p switch { >= 80 => "CR", >= 60 => "FV", >= 40 => "EV", _ => "WK" };
                    Console.Write($"{tag}{p,4:F0}% ");
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine("  CR=碾压 FV=优势 EV=均势 WK=劣势");
        Console.WriteLine();

        Console.WriteLine("================================================================================");
        
        // DEBUG: print key stats for one character pair
        Console.WriteLine();
        Console.WriteLine("【调试：物·均衡 vs 物·纯战 属性对比（seed=0）】");
        var c_wl_jh = pool[1][0]; // 物·均衡
        var c_wl_cz = pool[0][0]; // 物·纯战
        void PrintStats(Character c) {
            Console.Write($"  {c.Name} ({c.Realm}{c.SubIndex}):");
            foreach (var k in new[]{"HP","MP","肉攻","神攻","肉防","神防","反应"})
                Console.Write($" {k}={c.Primary[k]}");
            Console.Write("  二级:");
            foreach (var k in new[]{"格挡率","格挡减伤率","魂盾率","魂盾减伤率","闪避率","命中率","暴击率","暴击伤害"})
                Console.Write($" {k}={c.Secondary[k]:F0}");
            Console.WriteLine();
        }
        PrintStats(c_wl_jh);
        PrintStats(c_wl_cz);
        // Run 100 fights
        var (wa, wb) = Combat.Simulate(c_wl_jh, c_wl_cz, 100);
        var (wa2, wb2) = Combat.Simulate(c_wl_cz, c_wl_jh, 100);
        Console.WriteLine($"  均衡先手: {wa:F0}%  纯战先手: {wb2:F0}%  均: {(wa+wb2)/2:F0}%");
Console.WriteLine("  v3.4: 属性双轨成长（大境界突破→先天/小级别→一级）+ 二级属性聚焦");
        Console.WriteLine("================================================================================");
    }
}