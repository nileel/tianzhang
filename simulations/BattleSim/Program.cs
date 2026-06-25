using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

class Program
{
    record BuildDef(string Name, string Desc, Dictionary<string, int> Innate, string Style, string GongFaName = "", Dictionary<string, double> Weights = null);

    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test")
            return BattleSimSelfTests.Run(args[1]);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        const string TECH = "上品", SPIRIT = "中品";
        const int SEEDS = 20, SIM = 2000;

        var buildDefs = new BuildDef[]
        {
            new("物·纯战", "资质3根骨25", new() { ["根骨"]=25,["魂魄"]=8,["神识"]=5,["资质"]=3,["气运"]=5 }, "physical", "疾雷破山经"),
            new("物·均衡", "资质11根骨20", new() { ["根骨"]=20,["魂魄"]=8,["神识"]=8,["资质"]=11,["气运"]=8 }, "physical", "含弘光大典"),
            new("物·修炼", "资质19根骨15", new() { ["根骨"]=15,["魂魄"]=8,["神识"]=8,["资质"]=19,["气运"]=8 }, "physical", "白屋青云录"),
            new("肉盾型",  "根骨43极限", new() { ["根骨"]=43,["魂魄"]=5,["神识"]=5,["资质"]=5,["气运"]=5 }, "physical", "混元同尘典"),
            new("法·纯战", "资质3魂魄25", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=8,["资质"]=3,["气运"]=5 }, "magic", "抱元守一经"),
            new("法·均衡", "资质11魂魄20", new() { ["根骨"]=8,["魂魄"]=20,["神识"]=8,["资质"]=11,["气运"]=8 }, "magic", "万物不迁法"),
            new("法·修炼", "资质19魂魄15", new() { ["根骨"]=8,["魂魄"]=15,["神识"]=8,["资质"]=19,["气运"]=8 }, "magic", "万物不迁法"),
            new("灵修型",  "魂魄43极限", new() { ["根骨"]=5,["魂魄"]=43,["神识"]=5,["资质"]=5,["气运"]=5 }, "magic", "万物不迁法"),
            new("水·散修", "资质18气运14", new() { ["根骨"]=10,["魂魄"]=9,["神识"]=9,["资质"]=18,["气运"]=14 }, "water_physical", "秋水游心经"),
            new("太一·法修", "资质14魂魄18", new() { ["根骨"]=6,["魂魄"]=18,["神识"]=10,["资质"]=14,["气运"]=8 }, "taiyi", "抱元守一经"),
            new("太一·符修", "神识18魂魄14", new() { ["根骨"]=5,["魂魄"]=14,["神识"]=18,["资质"]=12,["气运"]=10 }, "taiyi_fuxiu", "云篆度人经"),
            // v5.3: 太虚观（暗系神魂）
            new("太虚·魂修", "魂魄25神识14", new() { ["根骨"]=5,["魂魄"]=25,["神识"]=14,["资质"]=14,["气运"]=5 }, "taixu", "不真自虚法"),
            new("太虚·均衡", "魂魄18神识14", new() { ["根骨"]=8,["魂魄"]=18,["神识"]=14,["资质"]=12,["气运"]=5 }, "taixu", "万物不迁法"),
            new("太虚·宿慧", "资质16神识18", new() { ["根骨"]=5,["魂魄"]=12,["神识"]=18,["资质"]=16,["气运"]=8 }, "taixu", "心无性有法"),
            // v5.3: 玉清崖（雷剑双修）
            new("玉清·剑修", "根骨22神识18", new() { ["根骨"]=22,["魂魄"]=8,["神识"]=18,["资质"]=12,["气运"]=3 }, "yuqing", "疾雷破山经"),
            new("玉清·雷修", "根骨18魂魄12", new() { ["根骨"]=18,["魂魄"]=12,["神识"]=14,["资质"]=8,["气运"]=3 }, "yuqing", "疾雷破山经"),
            // v5.4: 玉清崖 BuildDef 补全
            new("玉清·雷劫", "根骨18神识18", new() { ["根骨"]=18,["魂魄"]=10,["神识"]=18,["资质"]=10,["气运"]=3 }, "yuqing_leijie", "九霄雷劫录"),
            new("玉清·苦行", "神识20资质14", new() { ["根骨"]=16,["魂魄"]=6,["神识"]=20,["资质"]=14,["气运"]=5 }, "yuqing_kuxing", "苦行剑典"),
            new("玉清·雷体", "根骨30神识12", new() { ["根骨"]=30,["魂魄"]=5,["神识"]=12,["资质"]=8,["气运"]=5 }, "yuqing", "雷池淬体功"),
            // 太虚观 / 混元山 BuildDef 补全
            new("太虚·玄感", "魂魄18神识14", new() { ["根骨"]=5,["魂魄"]=18,["神识"]=14,["资质"]=12,["气运"]=8 }, "taixu_xuangan", "南华玄感录"),
            new("混元·正法", "神识16根骨14", new() { ["根骨"]=14,["魂魄"]=12,["神识"]=16,["资质"]=10,["气运"]=8 }, "physical", "绳墨正法录"),
        };
        int N = buildDefs.Length;

        Console.WriteLine($"修炼模拟 ({SEEDS}种子 x {GameData.CultivationCycles}轮, 灵根={SPIRIT}, 功法={TECH})...");
        var pool = new List<Character>[N];
        var realmDist = new Dictionary<string, int>[N];
        // v3.5: 道基分布统计
                var dfDist = new Dictionary<string, int>[N];
        var gcDist = new Dictionary<string, int>[N];
        for (int i = 0; i < N; i++)
        {
            pool[i] = new List<Character>();
            realmDist[i] = new Dictionary<string, int> { ["练气"] = 0, ["筑基"] = 0, ["金丹"] = 0 };
            dfDist[i] = new Dictionary<string, int>();
                        foreach (var q in GameData.DFQualities) dfDist[i][q] = 0;
            gcDist[i] = new Dictionary<string, int>();
            foreach (var q in GameData.GCQualities.Skip(1)) gcDist[i][q] = 0;
            gcDist[i][""] = 0;
        }

        for (int seed = 0; seed < SEEDS; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i, SPIRIT, TECH);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                // v3.5: 记录道基
                c.DFQuality = result.DFQuality;
                c.DFMult = GameData.DFMultiplier[result.DFQuality];
                c.DFScore = result.DFScore;
                c.GCQuality = result.GCQuality; c.GCMult = GameData.GCMultiplier.GetValueOrDefault(result.GCQuality, 1.0); c.GCScore = result.GCScore;
                c.GCType = result.GCType; c.AssignArts(); c.GCType = result.GCType;
                c.GCTypeMult = GameData.GCTypeScaling.GetValueOrDefault(result.GCQuality, 1.0);
                pool[i].Add(c);
                realmDist[i][c.Realm]++;
                dfDist[i][c.DFQuality]++;
                    if (c.GCQuality != "") gcDist[i][c.GCQuality]++;
                    else gcDist[i][""]++;
            }
        }
        Console.WriteLine("完成");

        // v3.5: 道基品质分布
        Console.WriteLine();
        Console.WriteLine("【道基品质分布】");
        Console.WriteLine($"{"Build",-10} {"无道基",-6} {"黄品",-6} {"玄品",-6} {"地品",-6} {"天品",-6} {"平均凝聚值",-8}");
        Console.WriteLine(new string('-', 52));
        for (int i = 0; i < N; i++)
        {
            double avgScore = pool[i].Average(c => (double)c.DFScore);
            Console.Write($"  {buildDefs[i].Name,-8}");
            foreach (var q in GameData.DFQualities)
                Console.Write($" {dfDist[i][q],4} ");
            Console.WriteLine($" {avgScore,7:F0}");
        }

        // 金丹品级分布
        Console.WriteLine();
        Console.WriteLine("【金丹品级分布（已结丹角色）】");
        Console.Write("{0,-10}", "Build");
        foreach (var q in GameData.GCQualities.Skip(1)) Console.Write("{0,5}", q);
        Console.WriteLine(" {0,-6} {1,-8}", "无金丹", "平均凝聚值");
        Console.WriteLine(new string('-', 10 + 6 * GameData.GCQualities.Length + 12));
        for (int i = 0; i < N; i++)
        {
            Console.Write("{0,-10}", buildDefs[i].Name);
            foreach (var q in GameData.GCQualities.Skip(1)) Console.Write("{0,5}", gcDist[i].GetValueOrDefault(q, 0));
            Console.Write(" {0,5}", gcDist[i].GetValueOrDefault("", 0));
            double avgGc = pool[i].Where(c => c.GCScore > 0).Select(c => (double)c.GCScore).DefaultIfEmpty(0).Average();
            Console.WriteLine(" {0,8:F0}", avgGc);
        }
        // 详细子境界分布
        Console.WriteLine();
        Console.WriteLine("【详细境界分布】");
        for (int i = 0; i < N; i++)
        {
            var groups = pool[i].GroupBy(c => GameData.StageName(c.Realm, c.SubIndex)).OrderBy(g => g.Key);
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
        string[] tags = buildDefs.Select(b => b.Name).ToArray();
        var goldPools = pool.Select(p => p.Where(c => c.Realm == "金丹").ToList()).ToArray();
        Console.WriteLine("【金丹样本数】");
        for (int i = 0; i < N; i++) Console.WriteLine($"  {tags[i],-8}: {goldPools[i].Count}/{SEEDS}");
        Console.WriteLine($"正在计算 {N}x{N} 金丹同境矩阵...");
        double[,] mat = new double[N, N];
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                int wI = 0, tot = 0;
                var left = goldPools[i];
                var right = goldPools[j];
                int pairs = Math.Min(left.Count, right.Count);
                if (pairs == 0)
                {
                    mat[i, j] = double.NaN;
                    mat[j, i] = double.NaN;
                    continue;
                }
                int h = Math.Max(1, SIM / pairs / 2);
                for (int s = 0; s < pairs; s++)
                {
                    var ci = left[s]; var cj = right[s];
                    var (wi, wj, _) = Combat.Simulate(ci, cj, h);
                    var (wi2, wj2, _) = Combat.Simulate(cj, ci, h);
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
        Console.WriteLine($"  修炼后金丹同境战斗胜率矩阵 (仅统计双方均为金丹的样本, {SIM}轮, {sw.ElapsedMilliseconds}ms)");
        Console.WriteLine($"================================================================================");
        Console.WriteLine();

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
                    if (double.IsNaN(p)) Console.Write($"{"NA",-8}");
                    else
                    {
                        string tag = p switch { >= 80 => "CR", >= 60 => "FV", >= 40 => "EV", _ => "WK" };
                        Console.Write($"{tag}{p,4:F0}% ");
                    }
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine("  CR=碾压 FV=优势 EV=均势 WK=劣势 NA=金丹样本不足");

        var zifuPools = pool.Select(p => p.Where(c => c.Realm == "筑基" && c.SubIndex == 4).ToList()).ToArray();
        Console.WriteLine();
        Console.WriteLine("【紫府圆满样本数】");
        for (int i = 0; i < N; i++) Console.WriteLine($"  {tags[i],-8}: {zifuPools[i].Count}/{SEEDS}");
        Console.WriteLine($"正在计算 {N}x{N} 紫府圆满同境矩阵...");
        var zifuMat = ComputeSymmetricMatrix(zifuPools, Math.Max(400, SIM / 2));
        PrintWinRateMatrix("紫府圆满同境战斗胜率矩阵", tags, zifuMat);

        Console.WriteLine();
        Console.WriteLine("【紫府圆满 vs 金丹初期压制战】");
        Console.WriteLine($"{"Build",-10} {"紫府样本",-8} {"金丹样本",-8} {"紫府胜率",-8} {"平均回合",-8}");
        Console.WriteLine(new string('-', 50));
        for (int i = 0; i < N; i++)
        {
            var zifu = zifuPools[i];
            var goldEarly = goldPools[i].Where(c => c.SubIndex == 0).ToList();
            if (zifu.Count == 0 || goldEarly.Count == 0)
            {
                Console.WriteLine($"  {tags[i],-8} {zifu.Count,6} {goldEarly.Count,8} {"NA",8} {"NA",8}");
                continue;
            }

            int pairs = Math.Min(zifu.Count, goldEarly.Count);
            int roundsPerPair = Math.Max(1, SIM / pairs / 4);
            double totalWin = 0;
            double totalTurns = 0;
            int combats = 0;
            for (int s = 0; s < pairs; s++)
            {
                var z = zifu[s];
                var g = goldEarly[s];
                var (zw, _, t1) = Combat.Simulate(z, g, roundsPerPair);
                var (_, zwSecond, t2) = Combat.Simulate(g, z, roundsPerPair);
                totalWin += zw + zwSecond;
                totalTurns += t1 + t2;
                combats += 2;
            }

            Console.WriteLine($"  {tags[i],-8} {zifu.Count,6} {goldEarly.Count,8} {totalWin / combats,7:F1}% {totalTurns / combats,7:F1}");
        }
        Console.WriteLine("  说明：同 Build 内紫府圆满挑战金丹初期，验证金丹压制是否明显但仍保留战术出口。");

        // ═══════════════════════════════════════
        // 2v2 群战矩阵 (v6.0)
        // ═══════════════════════════════════════
        Console.WriteLine();
        Console.WriteLine(new string('=', 80));
        Console.WriteLine("  2v2 群战胜率矩阵 (同Build两人组队，仅金丹样本)");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine();

        double[,] mat2v2 = new double[N, N];
        int sim2v2 = SIM / 4;
        for (int i = 0; i < N; i++)
            for (int j = i + 1; j < N; j++)
            {
                int wI = 0, tot = 0;
                var left = goldPools[i];
                var right = goldPools[j];
                int pairs = Math.Min(left.Count, right.Count);
                if (left.Count < 2 || right.Count < 2 || pairs == 0)
                {
                    mat2v2[i, j] = double.NaN;
                    mat2v2[j, i] = double.NaN;
                    continue;
                }
                int roundsPerPair = Math.Max(1, sim2v2 / pairs);
                for (int s = 0; s < pairs; s++)
                {
                    int a2 = (s + left.Count / 2) % left.Count;
                    int b2 = (s + right.Count / 2) % right.Count;
                    var (wi, wj, t) = Combat.Simulate2v2(left[s], left[a2], right[s], right[b2], roundsPerPair);
                    wI += (int)Math.Round(wi * roundsPerPair / 100.0);
                    tot += roundsPerPair;
                }
                mat2v2[i, j] = wI * 100.0 / Math.Max(1, tot);
                mat2v2[j, i] = 100.0 - mat2v2[i, j];
            }

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
                    double p = mat2v2[i, j];
                    if (double.IsNaN(p)) Console.Write($"{"NA",-8}");
                    else
                    {
                        string tag = p switch { >= 80 => "CR", >= 60 => "FV", >= 40 => "EV", _ => "WK" };
                        Console.Write($"{tag}{p,4:F0}% ");
                    }
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine("  CR=碾压 FV=优势 EV=均势 WK=劣势 NA=金丹样本不足");
        Console.WriteLine();

        // 群战增益分析
        Console.WriteLine("【1v1 vs 2v2 增益对比】");
        Console.WriteLine($"{"Build",-10} {"1v1%",-7} {"2v2%",-7} {"增益",-7}");
        Console.WriteLine(new string('-', 34));
        for (int i = 0; i < N; i++)
        {
            double a1 = 0, a2 = 0; int c = 0;
            for (int j = 0; j < N; j++)
                if (i != j && !double.IsNaN(mat[i, j]) && !double.IsNaN(mat2v2[i, j]))
                {
                    a1 += mat[i, j];
                    a2 += mat2v2[i, j];
                    c++;
                }
            if (c == 0) Console.WriteLine($"  {tags[i],-8} {"NA",5} {"NA",6} {"NA",7}");
            else
            {
                a1 /= c; a2 /= c;
                Console.WriteLine($"  {tags[i],-8} {a1,5:F1}% {a2,5:F1}% {a2-a1,6:+0.0;-0.0;0.0}%");
            }
        }
        Console.WriteLine();

        Console.WriteLine("================================================================================");

        // DEBUG

        // 练气快照
        const int EARLY_CYCLES = 40;
        Console.WriteLine();
        Console.WriteLine("[练气快照 同境对战]");
        var earlyPool = new List<Character>[N];
        for (int i = 0; i < N; i++) earlyPool[i] = new List<Character>();
        for (int seed = 0; seed < SEEDS; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i + 10000, SPIRIT, TECH, maxCycles: EARLY_CYCLES);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.DFQuality = result.DFQuality; c.DFMult = GameData.DFMultiplier[result.DFQuality];
                c.GCQuality = result.GCQuality; c.GCMult = GameData.GCMultiplier.GetValueOrDefault(result.GCQuality, 1.0);
                c.GCType = result.GCType; c.AssignArts(); c.GCType = result.GCType;
                c.GCTypeMult = GameData.GCTypeScaling.GetValueOrDefault(result.GCQuality, 1.0);
                earlyPool[i].Add(c);
            }
        }
        Console.WriteLine("  练气角色境界分布:");
        for (int i = 0; i < N; i++)
        {
            var groups = earlyPool[i].GroupBy(c => GameData.StageName(c.Realm, c.SubIndex)).OrderBy(g => g.Key);
            Console.WriteLine($"    {buildDefs[i].Name,-8}: {string.Join(", ", groups.Select(g => $"{g.Key}({g.Count()})"))}");
        }
        double earlyTotalTurns = 0; int earlyTurnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                var ciList = earlyPool[i].Where(c => c.Realm == "练气").Take(5).ToList();
                var cjList = earlyPool[j].Where(c => c.Realm == "练气").Take(5).ToList();
                foreach (var ci in ciList)
                    foreach (var cj in cjList)
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        earlyTotalTurns += t + t2; earlyTurnCombats += 2;
                    }
            }
        }
        if (earlyTurnCombats > 0)
            Console.WriteLine("  练气同境平均回合数: {0:F1} (样本={1}场)", earlyTotalTurns / earlyTurnCombats, earlyTurnCombats);
        else
            Console.WriteLine("  练气同境: 无练气角色可对战 (当前40轮均已突破至筑基+)");
        // 筑基同境（使用主池200轮数据）
        double baseTotalTurns = 0; int baseTurnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                var ciList = pool[i].Where(c => c.Realm == "筑基").Take(5).ToList();
                var cjList = pool[j].Where(c => c.Realm == "筑基").Take(5).ToList();
                foreach (var ci in ciList)
                    foreach (var cj in cjList)
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        baseTotalTurns += t + t2; baseTurnCombats += 2;
                    }
            }
        }
        if (baseTurnCombats > 0)
            Console.WriteLine("  筑基同境平均回合数: {0:F1} (样本={1}场)", baseTotalTurns / baseTurnCombats, baseTurnCombats);
        else
            Console.WriteLine("  筑基同境: 无筑基角色可对战");
        Console.WriteLine();
        // 平均回合数
        Console.WriteLine();
        Console.WriteLine("【金丹同境平均战斗回合数（含术法/神通）】");
        Console.WriteLine("（每对Build取前5个角色互搏，每场5轮）");
        Console.WriteLine();
        double totalTurnsAll = 0; int turnCombats = 0;
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                var ciList = goldPools[i].Take(5).ToList();
                var cjList = goldPools[j].Take(5).ToList();
                foreach (var ci in ciList)
                    foreach (var cj in cjList)
                    {
                        var (_, _, t) = Combat.Simulate(ci, cj, 5);
                        var (_, _, t2) = Combat.Simulate(cj, ci, 5);
                        totalTurnsAll += t + t2; turnCombats += 2;
                    }
            }
        }
        if (turnCombats > 0)
            Console.WriteLine("  总平均回合数: {0:F1} (样本={1}场)", totalTurnsAll / turnCombats, turnCombats);
        else
            Console.WriteLine("  金丹同境: 无金丹角色可对战");
        Console.WriteLine();

        // DEBUG
        Console.WriteLine("【调试：物·均衡 vs 物·纯战 属性对比（seed=0）】");
        var c_wl_jh = pool[1][0];
        var c_wl_cz = pool[0][0];
        void PrintStats(Character c) {
            Console.Write($"  {c.Name} ({GameData.StageName(c.Realm, c.SubIndex)}) 道基={c.DFQuality}({c.DFScore}) 金丹={c.GCQuality}({c.GCScore}):");
            foreach (var k in new[]{"HP","MP","肉攻","神攻","肉防","神防","反应"})
                Console.Write($" {k}={c.Primary[k]}");
            Console.Write("  二级:");
            foreach (var k in new[]{"格挡率","格挡减伤率","魂盾率","魂盾减伤率","闪避率","命中率","暴击率","暴击伤害"})
                Console.Write($" {k}={c.Secondary.GetValueOrDefault(k, 0):F0}");
            Console.WriteLine();
        }
        PrintStats(c_wl_jh);
        PrintStats(c_wl_cz);
        var (wa, wb, _) = Combat.Simulate(c_wl_jh, c_wl_cz, 100);
        var (wa2, wb2, _) = Combat.Simulate(c_wl_cz, c_wl_jh, 100);
        Console.WriteLine($"  均衡先手: {wa:F0}%  纯战先手: {wb2:F0}%  均: {(wa+wb2)/2:F0}%");
        Console.WriteLine("  v3.5: 属性双轨成长 + 道基品级（凝聚值判定+功法上下限钳制）");
        Console.WriteLine("================================================================================");
        return 0;
    }

    static double[,] ComputeSymmetricMatrix(IReadOnlyList<Character>[] pools, int sim)
    {
        int n = pools.Length;
        double[,] mat = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                int wI = 0, tot = 0;
                var left = pools[i];
                var right = pools[j];
                int pairs = Math.Min(left.Count, right.Count);
                if (pairs == 0)
                {
                    mat[i, j] = double.NaN;
                    mat[j, i] = double.NaN;
                    continue;
                }

                int h = Math.Max(1, sim / pairs / 2);
                for (int s = 0; s < pairs; s++)
                {
                    var ci = left[s];
                    var cj = right[s];
                    var (wi, _, _) = Combat.Simulate(ci, cj, h);
                    var (_, wj2, _) = Combat.Simulate(cj, ci, h);
                    wI += (int)Math.Round((wi + wj2) / 2.0 * h * 2 / 100.0);
                    tot += h * 2;
                }

                mat[i, j] = wI * 100.0 / tot;
                mat[j, i] = 100.0 - mat[i, j];
            }
        }

        return mat;
    }

    static void PrintWinRateMatrix(string title, string[] tags, double[,] mat)
    {
        Console.WriteLine();
        Console.WriteLine($"================================================================================");
        Console.WriteLine($"  {title}");
        Console.WriteLine($"================================================================================");
        Console.WriteLine();

        int n = tags.Length;
        Console.Write($"{"",-10}");
        for (int j = 0; j < n; j++) Console.Write($"{tags[j],-8}");
        Console.WriteLine();
        Console.WriteLine(new string('-', 10 + 8 * n));

        for (int i = 0; i < n; i++)
        {
            Console.Write($"{tags[i],-10}");
            for (int j = 0; j < n; j++)
            {
                if (i == j) Console.Write($"{"---",-8}");
                else
                {
                    double p = mat[i, j];
                    if (double.IsNaN(p)) Console.Write($"{"NA",-8}");
                    else
                    {
                        string tag = p switch { >= 80 => "CR", >= 60 => "FV", >= 40 => "EV", _ => "WK" };
                        Console.Write($"{tag}{p,4:F0}% ");
                    }
                }
            }
            Console.WriteLine();
        }
        Console.WriteLine("  CR=碾压 FV=优势 EV=均势 WK=劣势 NA=样本不足");
    }
}
