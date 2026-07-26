using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

class Program
{
    internal record BuildDef(string Name, string Desc, Dictionary<string, int> Innate, string Style, string GongFaName = "", Dictionary<string, double> Weights = null);
    record G2CoverageResult(string Status, bool MeetsThreshold);
    record MansionBodyBudget(string Mansion, string ParameterRange, string TargetMetric, double BudgetUnits);
    record MansionBudgetFixture(string Name, Dictionary<string, int> Innate, string[] Mansions, double SoftAffinityCostMultiplier);
    record MansionBudgetAuditResult(int PalaceCount, double BudgetUnits, int AddedArtSlots, int AddedDivineArtSlots, int AddedStableSeats, int AddedDanXiang);
    record FoundationGrowth(double HP, double MP, double 肉攻, double 神攻, double 肉防, double 神防, double 反应, double 神识);
    record DaoFoundationCoreCurve(string ParameterId, double StartingNormalizedMagnitude, double MaximumNormalizedMagnitude, double Exponent);

    static readonly MansionBodyBudget[] MansionBodyBudgets =
    {
        new("命府", "activationThreshold=30%～40% 最大生命类资源；recoveryAmount=8%～12% 最大生命类资源", "一次允许伤害结算后的恢复不超过 12% 最大生命类资源", 1.0),
        new("魂府", "interceptableStatusTags=2～3 个既有状态标签；excludedStatusTags 至少 1 个", "每次仅阻止一项符合标签的待写入状态", 1.0),
        new("识府", "revealRangeModifier=0～+1 格；revealFieldSet=1～2 个既有字段", "只揭示当前合法侦知查询内的信息", 1.0),
        new("悟府", "eligibleActionTags=1 个主动修炼标签；progressModifier=8%～12%", "单次合法主动修炼进度修正不超过 12%", 1.0),
        new("运府", "revealFieldSet=1～2 个既有线索字段；selectionScope=当前候选集", "只揭示已合法生成候选的线索，不改变候选或概率", 1.0),
    };

    static readonly string[] MansionBodyOrder = { "命府", "魂府", "识府", "悟府", "运府" };
    static IReadOnlyList<string> G2AuditTargetStages => ["金丹"];
    static readonly double[] FoundationStageShares = { 0.18, 0.22, 0.27, 0.33 };
    static readonly DaoFoundationCoreCurve RepresentativeFoundationCoreCurve = new("normalizedMagnitude", 0.35, 1.00, 1.25);
    static readonly string[] FoundationGrowthAuditBuilds = { "物·纯战", "法·纯战", "太一·符修" };

    static readonly BuildDef[] BuildDefs =
    {
        new("物·纯战", "根骨15极限", new() { ["根骨"]=15,["魂魄"]=6,["神识"]=3,["资质"]=3,["气运"]=3 }, "physical", "疾雷破山经"),
        new("物·均衡", "五维均衡8", new() { ["根骨"]=8,["魂魄"]=8,["神识"]=8,["资质"]=8,["气运"]=8 }, "physical", "含弘光大典"),
        new("物·修炼", "资质15倾斜", new() { ["根骨"]=6,["魂魄"]=3,["神识"]=3,["资质"]=15,["气运"]=3 }, "physical", "白屋青云录"),
        new("肉盾型",  "根骨15极限", new() { ["根骨"]=15,["魂魄"]=6,["神识"]=3,["资质"]=3,["气运"]=3 }, "physical", "混元同尘典"),
        new("法·纯战", "魂魄15极限", new() { ["根骨"]=3,["魂魄"]=15,["神识"]=6,["资质"]=3,["气运"]=3 }, "magic", "抱元守一经"),
        new("法·均衡", "五维均衡8", new() { ["根骨"]=8,["魂魄"]=8,["神识"]=8,["资质"]=8,["气运"]=8 }, "magic", "万物不迁法"),
        new("法·修炼", "资质15倾斜", new() { ["根骨"]=3,["魂魄"]=6,["神识"]=3,["资质"]=15,["气运"]=3 }, "magic", "万物不迁法"),
        new("灵修型",  "魂魄15极限", new() { ["根骨"]=3,["魂魄"]=15,["神识"]=6,["资质"]=3,["气运"]=3 }, "magic", "万物不迁法"),
        new("水·散修", "资质9气运8", new() { ["根骨"]=8,["魂魄"]=7,["神识"]=7,["资质"]=9,["气运"]=8 }, "water_physical", "秋水游心经"),
        new("太一·法修", "魂魄12资质9", new() { ["根骨"]=3,["魂魄"]=12,["神识"]=7,["资质"]=9,["气运"]=4 }, "taiyi", "抱元守一经"),
        new("太一·符修", "神识15极限", new() { ["根骨"]=3,["魂魄"]=6,["神识"]=15,["资质"]=3,["气运"]=3 }, "taiyi_fuxiu", "云篆度人经"),
        // v5.3: 太虚观（暗系神魂）
        new("太虚·魂修", "魂魄15极限", new() { ["根骨"]=3,["魂魄"]=15,["神识"]=6,["资质"]=3,["气运"]=3 }, "taixu", "不真自虚法"),
        new("太虚·均衡", "魂魄9气运8", new() { ["根骨"]=7,["魂魄"]=9,["神识"]=8,["资质"]=7,["气运"]=8 }, "taixu", "万物不迁法"),
        new("太虚·宿慧", "神识12资质9", new() { ["根骨"]=3,["魂魄"]=6,["神识"]=12,["资质"]=9,["气运"]=5 }, "taixu", "心无性有法"),
        // v5.3: 玉清崖（雷剑双修）
        new("玉清·剑修", "根骨15极限", new() { ["根骨"]=15,["魂魄"]=3,["神识"]=6,["资质"]=3,["气运"]=3 }, "yuqing", "疾雷破山经"),
        new("玉清·雷修", "根骨12魂魄9", new() { ["根骨"]=12,["魂魄"]=9,["神识"]=8,["资质"]=3,["气运"]=3 }, "yuqing", "疾雷破山经"),
        // v5.4: 玉清崖 BuildDef 补全
        new("玉清·雷劫", "根骨11神识10", new() { ["根骨"]=11,["魂魄"]=8,["神识"]=10,["资质"]=3,["气运"]=3 }, "yuqing_leijie", "九霄雷劫录"),
        new("玉清·苦行", "神识12资质9", new() { ["根骨"]=7,["魂魄"]=3,["神识"]=12,["资质"]=9,["气运"]=4 }, "yuqing_kuxing", "苦行剑典"),
        new("玉清·雷体", "根骨15极限", new() { ["根骨"]=15,["魂魄"]=3,["神识"]=6,["资质"]=3,["气运"]=3 }, "yuqing", "雷池淬体功"),
        // 太虚观 / 混元山 BuildDef 补全
        new("太虚·玄感", "魂魄12神识8", new() { ["根骨"]=6,["魂魄"]=12,["神识"]=8,["资质"]=7,["气运"]=3 }, "taixu_xuangan", "南华玄感录"),
        new("混元·正法", "根骨10神识10", new() { ["根骨"]=10,["魂魄"]=8,["神识"]=10,["资质"]=3,["气运"]=5 }, "physical", "绳墨正法录"),
    };

    internal static IReadOnlyList<IReadOnlyDictionary<string, int>> MatrixBuildInputs => BuildDefs.Select(build => (IReadOnlyDictionary<string, int>)build.Innate).ToArray();

    static int Main(string[] args)
    {
        Combat.ResetDeterministicRandom();

        if (args.Length == 2 && args[0] == "--self-test")
            return BattleSimSelfTests.Run(args[1]);

        bool g2Audit = args.Length >= 1 && args[0] == "--g2-audit";
        bool g2Attribution = args.Length >= 1 && args[0] == "--g2-attribution";
        if (args.Length != 0 && !g2Audit && !g2Attribution)
        {
            Console.Error.WriteLine("Usage: BattleSim [--g2-audit [--cycles <positive-integer>] | --g2-attribution [--cycles <positive-integer>] | --self-test <suite>]");
            return 2;
        }

        int cultivationCycles;
        try
        {
            cultivationCycles = g2Audit
                ? ParseG2AuditCycles(args)
                : g2Attribution
                    ? ParseG2AttributionCycles(args)
                    : GameData.CultivationCycles;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        RunFoundationGrowthAudit();
        Console.WriteLine();
        RunMansionBodyBudgetAudit();
        Console.WriteLine();
        const string TECH = "上品", SPIRIT = "中品";
        int seeds = g2Audit || g2Attribution ? 200 : 20;
        int SIM = g2Audit || g2Attribution ? 2000 : 100;

        var buildDefs = BuildDefs;
        foreach (var build in buildDefs)
        {
            var validation = BuildInputRules.Validate(build.Innate);
            if (!validation.IsValid)
                throw new InvalidOperationException($"矩阵 Build 输入无效：{build.Name}：{validation.Error}");
        }
        int N = buildDefs.Length;

        Console.WriteLine($"修炼模拟 ({seeds}种子 x {cultivationCycles}轮, 灵根={SPIRIT}, 功法={TECH})...");
        var pool = new List<Character>[N];
        var realmDist = new Dictionary<string, int>[N];
        // v3.5: 道基分布统计
                var dfDist = new Dictionary<string, int>[N];
        var danJiDist = new Dictionary<string, int>[N];
        for (int i = 0; i < N; i++)
        {
            pool[i] = new List<Character>();
            realmDist[i] = new Dictionary<string, int> { ["练气"] = 0, ["筑基"] = 0, ["金丹"] = 0 };
            dfDist[i] = new Dictionary<string, int>();
                        foreach (var q in GameData.DFQualities) dfDist[i][q] = 0;
            danJiDist[i] = new Dictionary<string, int>
            {
                ["未成丹"] = 0,
                ["暂寄丹籍"] = 0,
                ["敕封丹籍"] = 0,
                ["自然丹籍"] = 0
            };
        }

        for (int seed = 0; seed < seeds; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i, SPIRIT, TECH, maxCycles: cultivationCycles);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                // v3.5: 记录道基
                c.DFQuality = result.DFQuality;
                c.DFMult = GameData.DFMultiplier[result.DFQuality];
                c.DFScore = result.DFScore;
                ApplyGoldenCoreResult(c, result);
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.AssignArts();
                pool[i].Add(c);
                realmDist[i][c.Realm]++;
                dfDist[i][c.DFQuality]++;
                var danJiKey = string.IsNullOrEmpty(c.DanJiType) ? "未成丹" : c.DanJiType;
                danJiDist[i][danJiKey]++;
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

        // 金丹成丹状态分布
        Console.WriteLine();
        Console.WriteLine("【金丹成丹状态分布（TQ-013B/TQ-015C兼容层）】");
        Console.WriteLine($"{"Build",-10} {"未成丹",-6} {"暂寄",-6} {"敕封",-6} {"自然",-6} {"平均判定值",-8} {"主要丹性",-8} {"主要位格",-14}");
        Console.WriteLine(new string('-', 78));
        for (int i = 0; i < N; i++)
        {
            double avgGc = pool[i].Where(c => c.GCScore > 0).Select(c => (double)c.GCScore).DefaultIfEmpty(0).Average();
            var majorNature = pool[i]
                .Where(c => !string.IsNullOrEmpty(c.DanNature))
                .GroupBy(c => c.DanNature)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "-";
            var majorSeat = pool[i]
                .Where(c => !string.IsNullOrEmpty(c.SeatName))
                .GroupBy(c => c.SeatName)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "-";
            Console.WriteLine($"  {buildDefs[i].Name,-8} {danJiDist[i]["未成丹"],4} {danJiDist[i]["暂寄丹籍"],7} {danJiDist[i]["敕封丹籍"],6} {danJiDist[i]["自然丹籍"],6} {avgGc,10:F0} {majorNature,-8} {majorSeat,-14}");
        }

        Console.WriteLine();
        Console.WriteLine("【席位竞争状态分布（TQ-015C-3字段拆分）】");
        Console.WriteLine($"{"Build",-10} {"自然候选",-8} {"待争席",-8} {"受敕承位",-8} {"暂寄",-6} {"已占据",-8} {"主要最终状态",-12} {"最高争席分",-8}");
        Console.WriteLine(new string('-', 84));
        for (int i = 0; i < N; i++)
        {
            int naturalCandidates = pool[i].Count(c => c.NaturalDanJiCandidateState == "自然候选");
            int pendingCompetition = pool[i].Count(c => c.SeatCompetitionState == "待争席");
            int grantedSeats = pool[i].Count(c => c.FinalOccupancyState == "受敕承位");
            int temporarySeats = pool[i].Count(c => c.FinalOccupancyState == "暂寄");
            int occupiedSeats = pool[i].Count(c => c.FinalOccupancyState == "已占据");
            int bestCompetitionScore = pool[i].Select(c => c.SeatCompetitionScore).DefaultIfEmpty(0).Max();
            var majorFinalState = pool[i]
                .GroupBy(c => c.FinalOccupancyState)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "-";
            Console.WriteLine($"  {buildDefs[i].Name,-8} {naturalCandidates,6} {pendingCompetition,8} {grantedSeats,8} {temporarySeats,6} {occupiedSeats,8} {majorFinalState,-12} {bestCompetitionScore,8}");
        }
        Console.WriteLine("  说明：自然丹籍列仍表示成丹类型；自然候选/待争席表示尚未结算席位竞争，当前不把候选直接视为已占据。");
        Console.WriteLine("  席位竞争分只用于同一目标席位候选排序，不改变成丹阈值、战斗倍率或紫府神通门槛。");

        Console.WriteLine();
        Console.WriteLine("【席位竞争样本统计（TQ-015C-6按位格汇总）】");
        Console.WriteLine($"{"SeatName",-18} {"样本",-6} {"自然候选",-8} {"敕封",-6} {"暂寄",-6} {"未成丹",-6} {"NA原因",-14} {"紫府未接入",-10}");
        Console.WriteLine(new string('-', 92));
        foreach (var row in SeatCompetitionSampleStats.Summarize(pool.SelectMany(p => p), minimumSamples: 2))
        {
            Console.WriteLine($"  {row.SeatName,-16} {row.SampleCount,4} {row.NaturalCandidateCount,8} {row.GrantedCount,6} {row.TemporaryCount,6} {row.UnformedCount,6} {row.NaReason,-14} {row.ZifuPendingCount,6}/{row.SampleCount,-3} {row.ZifuInputState}");
        }
        Console.WriteLine("  说明：本表只按目标 SeatName 汇总样本分布与 NA 原因；不结算席位胜者，不改变成丹阈值、战斗倍率或金丹样本筛选规则。");

        Console.WriteLine();
        Console.WriteLine("【紫府神通/府位闭环输入状态（TQ-015C-4占位字段）】");
        Console.WriteLine($"{"Build",-10} {"神通数",-8} {"府位覆盖",-8} {"闭环状态",-8} {"资格说明",-24}");
        Console.WriteLine(new string('-', 66));
        for (int i = 0; i < N; i++)
        {
            double avgDivineArtCount = pool[i].Average(c => (double)c.ZifuDivineArtCount);
            double avgPalaceCoverage = pool[i].Average(c => (double)c.ZifuPalaceCoverageCount);
            string majorLoopState = pool[i]
                .GroupBy(c => c.ZifuCoreLoopState)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "-";
            string majorEligibilityNote = pool[i]
                .GroupBy(c => c.ZifuEligibilityNote)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "-";
            Console.WriteLine($"  {buildDefs[i].Name,-8} {avgDivineArtCount,6:F1} {avgPalaceCoverage,8:F1} {majorLoopState,-8} {majorEligibilityNote,-24}");
        }
        Console.WriteLine("  说明：本表只记录紫府神通、府位覆盖和闭环输入状态；当前阈值待验证，不参与成丹、争席或战斗倍率。");
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
            double lq = realmDist[i]["练气"] * 100.0 / seeds;
            double zj = realmDist[i]["筑基"] * 100.0 / seeds;
            double jd = realmDist[i]["金丹"] * 100.0 / seeds;
            Console.WriteLine($"  {buildDefs[i].Name,-8} {avg资,4:F0}  {lq,5:F0}%  {zj,5:F0}%  {jd,5:F0}%  {avgHp,6:F0}  {buildDefs[i].Desc}");
        }
        Console.WriteLine();

        // 战斗矩阵
        string[] tags = buildDefs.Select(b => b.Name).ToArray();
        var stagePoolSource = pool.Cast<IReadOnlyList<Character>>().ToArray();
        var goldPools = StageCombatReport.SelectPools(stagePoolSource, "金丹");
        StageCombatReport.PrintSampleCounts("金丹样本数", tags, goldPools, seeds);
        Console.WriteLine($"正在计算 {N}x{N} 金丹同境矩阵...");
        double[,] mat = new double[N, N];
        for (int i = 0; i < N; i++)
        {
            for (int j = i + 1; j < N; j++)
            {
                double wI = 0;
                int tot = 0;
                var left = goldPools[i];
                var right = goldPools[j];
                int pairs = Math.Min(left.Count, right.Count);
                if (pairs == 0)
                {
                    mat[i, j] = double.NaN;
                    mat[j, i] = double.NaN;
                    continue;
                }
                var directionalRounds = AllocateDirectionalBattleRounds(SIM, pairs);
                for (int s = 0; s < pairs; s++)
                {
                    var ci = left[s]; var cj = right[s];
                    int forwardRounds = directionalRounds[s * 2];
                    if (forwardRounds > 0)
                    {
                        var (wi, _, _) = Combat.Simulate(ci, cj, forwardRounds);
                        wI += wi * forwardRounds / 100.0;
                        tot += forwardRounds;
                    }
                    int reverseRounds = directionalRounds[s * 2 + 1];
                    if (reverseRounds > 0)
                    {
                        var (_, wj2, _) = Combat.Simulate(cj, ci, reverseRounds);
                        wI += wj2 * reverseRounds / 100.0;
                        tot += reverseRounds;
                    }
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

        var zhujiPools = StageCombatReport.SelectPools(stagePoolSource, "筑基");
        Console.WriteLine();
        StageCombatReport.PrintSampleCounts("筑基样本数", tags, zhujiPools, seeds);
        Console.WriteLine($"正在计算 {N}x{N} 筑基同境矩阵...");
        var zhujiMat = ComputeSymmetricMatrix(zhujiPools, SIM);
        PrintWinRateMatrix("筑基同境战斗胜率矩阵（筑基五段混合样本）", tags, zhujiMat);
        Console.WriteLine("  说明：本表只验证筑基主游戏期 Build 差异，不引入紫府/金丹新倍率或改变金丹样本筛选。");

        var zifuPools = StageCombatReport.SelectPools(stagePoolSource, "筑基", 4);
        Console.WriteLine();
        StageCombatReport.PrintSampleCounts("紫府圆满样本数", tags, zifuPools, seeds);
        Console.WriteLine($"正在计算 {N}x{N} 紫府圆满同境矩阵...");
        var zifuMat = ComputeSymmetricMatrix(zifuPools, SIM);
        PrintWinRateMatrix("紫府圆满同境战斗胜率矩阵", tags, zifuMat);

        bool g2CoveragePassed = true;
        if (g2Audit)
        {
            g2CoveragePassed = PrintG2CoverageAudit("金丹", tags, goldPools, mat, SIM);
            PrintG2CoverageAudit("筑基（诊断，不计入 G2 门槛）", tags, zhujiPools, zhujiMat, SIM);
            PrintG2CoverageAudit("紫府圆满（诊断，不计入 G2 门槛）", tags, zifuPools, zifuMat, SIM);
            Console.WriteLine($"G2 覆盖结论（金丹目标矩阵）：{(g2CoveragePassed ? "SUFFICIENT" : "INSUFFICIENT")}");
        }

        if (g2Attribution)
            G2AttributionAudit.Print(buildDefs, goldPools, mat, SIM, cultivationCycles);

        Console.WriteLine();
        Console.WriteLine("【紫府圆满 vs 金丹初期压制战】");
        Console.WriteLine($"{"Build",-10} {"紫府样本",-8} {"金丹样本",-8} {"紫府胜率",-8} {"削位15",-8} {"强扫最佳",-12} {"平均回合",-8} {"出口%",-7} {"主要出口",-12}");
        Console.WriteLine(new string('-', 94));
        var suppressionProfiles = GoldenCoreSuppressionExitStats.DefaultSwitchProfiles();
        for (int i = 0; i < N; i++)
        {
            var zifu = zifuPools[i];
            var goldEarly = goldPools[i].Where(c => c.SubIndex == 0).ToList();
            if (zifu.Count == 0 || goldEarly.Count == 0)
            {
                Console.WriteLine($"  {tags[i],-8} {zifu.Count,6} {goldEarly.Count,8} {"NA",8} {"NA",8} {"NA",-12} {"NA",8} {"NA",7} {"-",-12}");
                continue;
            }

            int pairs = Math.Min(zifu.Count, goldEarly.Count);
            int roundsPerPair = Math.Max(1, SIM / pairs / 4);
            double totalWin = 0;
            double totalTurns = 0;
            int combats = 0;
            double[] profileWins = new double[suppressionProfiles.Count];
            int[] profileCombats = new int[suppressionProfiles.Count];
            int tacticalExitCount = 0;
            var routeCounts = new Dictionary<string, int>();
            for (int s = 0; s < pairs; s++)
            {
                var z = zifu[s];
                var g = goldEarly[s];
                var route = GoldenCoreSuppressionExitStats.ClassifyExit(z, g);
                if (route.IsTacticalExit)
                    tacticalExitCount++;
                routeCounts[route.ExitRoute] = routeCounts.GetValueOrDefault(route.ExitRoute, 0) + 1;

                var (zw, _, t1) = Combat.Simulate(z, g, roundsPerPair);
                var (_, zwSecond, t2) = Combat.Simulate(g, z, roundsPerPair);
                totalWin += zw + zwSecond;
                totalTurns += t1 + t2;
                combats += 2;

                for (int profileIndex = 0; profileIndex < suppressionProfiles.Count; profileIndex++)
                {
                    var scenario = GoldenCoreSuppressionExitStats.CreateTacticalScenario(z, g, suppressionProfiles[profileIndex]);
                    if (scenario.HasActiveSwitch)
                    {
                        var (switchZw, _, _) = Combat.Simulate(scenario.Zifu, scenario.Gold, roundsPerPair);
                        var (_, switchZwSecond, _) = Combat.Simulate(scenario.Gold, scenario.Zifu, roundsPerPair);
                        profileWins[profileIndex] += switchZw + switchZwSecond;
                        profileCombats[profileIndex] += 2;
                    }
                }
            }

            var majorExitSource = tacticalExitCount > 0
                ? routeCounts.Where(kv => kv.Key is not "剧情条件" and not "NA")
                : routeCounts;
            string majorExit = majorExitSource
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Select(kv => kv.Key)
                .FirstOrDefault() ?? "-";
            double exitRate = tacticalExitCount * 100.0 / pairs;
            string defaultSwitchWinRate = profileCombats[0] > 0 ? $"{profileWins[0] / profileCombats[0]:F1}%" : "NA";
            var bestProfileIndex = Enumerable.Range(0, suppressionProfiles.Count)
                .Where(profileIndex => profileCombats[profileIndex] > 0)
                .OrderByDescending(profileIndex => profileWins[profileIndex] / profileCombats[profileIndex])
                .ThenBy(profileIndex => profileIndex)
                .Cast<int?>()
                .FirstOrDefault();
            double bestProfileWin = bestProfileIndex.HasValue
                ? profileWins[bestProfileIndex.Value] / profileCombats[bestProfileIndex.Value]
                : double.NaN;
            string bestSwitchWinRate = bestProfileIndex.HasValue
                ? bestProfileWin <= 0
                    ? "全档0.0%"
                    : $"{suppressionProfiles[bestProfileIndex.Value].Label}:{bestProfileWin:F1}%"
                : "NA";

            Console.WriteLine($"  {tags[i],-8} {zifu.Count,6} {goldEarly.Count,8} {totalWin / combats,7:F1}% {defaultSwitchWinRate,8} {bestSwitchWinRate,-12} {totalTurns / combats,7:F1} {exitRate,6:F0}% {majorExit,-12}");
        }
        Console.WriteLine("  说明：同 Build 内紫府圆满挑战金丹初期，验证金丹压制是否明显但仍保留战术出口。");
        Console.WriteLine("  出口统计只分类削位/破府/封丹/阵法/剧情条件，不修改默认战斗结算、成丹阈值或金丹倍率。");
        Console.WriteLine("  削位15沿用旧开关口径；强扫最佳扫描削位15/30/45+封丹，只压低金丹MP、禁用金丹神通，不提升紫府HP/攻击。");

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
        var matrixBattlefield = HexBattlefield.CreateTechnicalFixture();
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
                    var (wi, wj, t) = Combat.Simulate2v2(
                        matrixBattlefield, left[s], left[a2], right[s], right[b2], roundsPerPair);
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
        for (int seed = 0; seed < seeds; seed++)
        {
            for (int i = 0; i < N; i++)
            {
                var bd = buildDefs[i];
                var result = Cultivation.Simulate(bd.Innate, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName), seed * 100 + i + 10000, SPIRIT, TECH, maxCycles: EARLY_CYCLES);
                var c = Character.Create(bd.Name, bd.Innate, bd.Style);
                c.ApplyGrowth(result.Realm, TECH, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.GongFaName = bd.GongFaName;
                c.DFQuality = result.DFQuality; c.DFMult = GameData.DFMultiplier[result.DFQuality];
                ApplyGoldenCoreResult(c, result);
                c.FinalizeStats(result.Realm, result.SubIdx, SPIRIT, bd.Weights ?? GameData.WeightsFromGongFa(bd.GongFaName));
                c.AssignArts();
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
            string goldenCoreLabel = string.IsNullOrEmpty(c.DanJiType)
                ? "未成丹"
                : $"{c.DanJiType}/{c.NaturalDanJiCandidateState}/{c.SeatCompetitionState}/{c.FinalOccupancyState}/{c.DanName}/{c.DanNature}/{c.SeatName}";
            Console.Write($"  {c.Name} ({GameData.StageName(c.Realm, c.SubIndex)}) 道基={c.DFQuality}({c.DFScore}) 金丹={goldenCoreLabel}({c.GCScore}):");
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
        return g2Audit && !g2CoveragePassed ? 3 : 0;
    }

    static void ApplyGoldenCoreResult(Character c, Cultivation.Result result)
    {
        c.LegacyGCGrade = result.LegacyGCGrade;
        c.GCScore = result.GCScore;
        c.FormedState = result.FormedState;
        c.DanJiType = result.DanJiType;
        c.OccupancyState = result.OccupancyState;
        c.DanName = result.DanName;
        c.DanNature = result.DanNature;
        c.TargetBranch = result.TargetBranch;
        c.TargetSeat = result.TargetSeat;
        c.SeatName = result.SeatName;
        c.DanPivot = result.DanPivot;
        c.NaturalDanJiCandidateState = result.NaturalDanJiCandidateState;
        c.SeatAccessState = result.SeatAccessState;
        c.SeatCompetitionState = result.SeatCompetitionState;
        c.FinalOccupancyState = result.FinalOccupancyState;
        c.SeatCompetitionScore = result.SeatCompetitionScore;
        c.ZifuDivineArtCount = result.ZifuDivineArtCount;
        c.ZifuPalaceCoverageCount = result.ZifuPalaceCoverageCount;
        c.ZifuCoreLoopState = result.ZifuCoreLoopState;
        c.ZifuEligibilityNote = result.ZifuEligibilityNote;
        c.DanJiStabilityMult = result.DanJiStabilityMultiplier;
        c.DanJiArtAffinityMult = result.DanJiArtAffinityMultiplier;
    }

    static void RunFoundationGrowthAudit()
    {
        const double tolerance = 0.000000001;
        const int legacyFoundationStages = 5;
        AssertFoundationGrowth(FoundationStageShares.Length == 4, "道基成长必须正好包含四个阶段");
        AssertFoundationGrowth(Math.Abs(FoundationStageShares.Sum() - 1.0) <= tolerance, "四阶段预算份额之和必须为 1");
        AssertFoundationGrowth(FoundationStageShares.Zip(FoundationStageShares.Skip(1), (left, right) => left < right).All(value => value), "四阶段预算份额必须逐阶段递增，不能平均拆分");

        Console.WriteLine("【四阶段成长与道基核心曲线审计（N-FPD-GROWTH-01）】");
        Console.WriteLine("  旧输入：每部功法现行筑基五段成长；四阶段份额=18%/22%/27%/33%，整数总额采用保底一格后的最大余数分配。");
        Console.WriteLine($"  守恒容差：{tolerance:E0}；移力按现行固定境界值处理，不迁移旧五段的非运行时增量。");

        int auditedBuilds = 0;
        foreach (var build in BuildDefs)
        {
            var legacyTotal = Multiply(ResolveLegacyFoundationGrowth(build), legacyFoundationStages);
            var fourStageGrowth = RedistributeFoundationGrowth(legacyTotal);
            var migratedTotal = SumFoundationGrowth(fourStageGrowth);
            double difference = MaximumGrowthDifference(legacyTotal, migratedTotal);
            AssertFoundationGrowth(difference <= tolerance, $"{build.Name} 的四阶段总预算偏差为 {difference:E3}");
            auditedBuilds++;

            if (FoundationGrowthAuditBuilds.Contains(build.Name))
            {
                Console.WriteLine($"  {build.Name,-8} 旧总={FormatFoundationGrowth(legacyTotal)}");
                for (int stage = 0; stage < fourStageGrowth.Length; stage++)
                    Console.WriteLine($"    第{stage + 1}阶段={FormatFoundationGrowth(fourStageGrowth[stage])}");
            }
        }

        Console.WriteLine($"  练气出口：道基成长贡献=0，迁移前后差值=0（{auditedBuilds} Build）。");
        Console.WriteLine($"  金丹入口：每个 Build 的筑基累计成长总额守恒，最大原始差值≤{tolerance:E0}。");

        var progressAnchors = new[] { 0.0, 1.0 / 3.0, 2.0 / 3.0, 1.0 };
        double previousMagnitude = double.NegativeInfinity;
        Console.WriteLine($"  核心曲线：{RepresentativeFoundationCoreCurve.ParameterId}=start + (max-start) × progress^{RepresentativeFoundationCoreCurve.Exponent:F2}");
        for (int stage = 0; stage < progressAnchors.Length; stage++)
        {
            double magnitude = EvaluateFoundationCoreCurve(progressAnchors[stage], RepresentativeFoundationCoreCurve);
            AssertFoundationGrowth(magnitude >= previousMagnitude, "道基核心数值曲线不得倒退");
            previousMagnitude = magnitude;
            Console.WriteLine($"    第{stage + 1}阶段：progress={progressAnchors[stage]:F3}，normalizedMagnitude={magnitude:F3}");
        }
        AssertFoundationGrowth(Math.Abs(EvaluateFoundationCoreCurve(0, RepresentativeFoundationCoreCurve) - RepresentativeFoundationCoreCurve.StartingNormalizedMagnitude) <= tolerance, "道基核心曲线起点错误");
        AssertFoundationGrowth(Math.Abs(EvaluateFoundationCoreCurve(1, RepresentativeFoundationCoreCurve) - RepresentativeFoundationCoreCurve.MaximumNormalizedMagnitude) <= tolerance, "道基核心曲线终点错误");
        Console.WriteLine("  结论：PASS；曲线只提供连续数值参数，非数值效果仍须显式阶段条件，不产生功法专属机制、槽位、位格或丹相。 ");
    }

    static FoundationGrowth ResolveLegacyFoundationGrowth(BuildDef build)
    {
        if (GameData.GongFaTables.TryGetValue(build.GongFaName, out var table)
            && table.TryGetValue("筑基", out var growth))
        {
            return new(growth.HP, growth.MP, growth.肉攻, growth.神攻, growth.肉防, growth.神防, growth.反应, growth.神识);
        }

        AssertFoundationGrowth(GameData.HasApprovedGrowthFallback(build.GongFaName), $"{build.GongFaName} 缺少已登记的成长回退");
        var weights = build.Weights ?? GameData.WeightsFromGongFa(build.GongFaName);
        var baseGrowth = GameData.SubGrowthBase["筑基"];
        double Scale(string innateAttribute) => weights[innateAttribute] / 0.6;

        // 与 Character.SubGrowthSum 的回退映射保持一致，审计的是现行运行时输入而不是模板旧值。
        return new(
            baseGrowth.HP * Scale("根骨"),
            baseGrowth.MP * Scale("魂魄"),
            baseGrowth.肉攻 * Scale("根骨"),
            baseGrowth.神攻 * Scale("魂魄"),
            baseGrowth.肉防 * Scale("根骨"),
            baseGrowth.神防 * Scale("魂魄"),
            baseGrowth.反应 * Scale("根骨"),
            baseGrowth.神识 * Scale("神识"));
    }

    static FoundationGrowth[] RedistributeFoundationGrowth(FoundationGrowth total)
    {
        var hp = AllocateFoundationBudget(total.HP);
        var mp = AllocateFoundationBudget(total.MP);
        var physicalAttack = AllocateFoundationBudget(total.肉攻);
        var spiritualAttack = AllocateFoundationBudget(total.神攻);
        var physicalDefense = AllocateFoundationBudget(total.肉防);
        var spiritualDefense = AllocateFoundationBudget(total.神防);
        var reaction = AllocateFoundationBudget(total.反应);
        var perception = AllocateFoundationBudget(total.神识);

        return Enumerable.Range(0, FoundationStageShares.Length)
            .Select(index => new FoundationGrowth(
                hp[index], mp[index], physicalAttack[index], spiritualAttack[index],
                physicalDefense[index], spiritualDefense[index], reaction[index], perception[index]))
            .ToArray();
    }

    static double[] AllocateFoundationBudget(double total)
    {
        AssertFoundationGrowth(total >= 0 && !double.IsNaN(total) && !double.IsInfinity(total), "成长预算必须为有限非负数");
        bool isWholeNumber = Math.Abs(total - Math.Round(total)) < 0.000000001;
        if (!isWholeNumber || total < FoundationStageShares.Length)
        {
            var fractional = FoundationStageShares.Select(share => total * share).ToArray();
            fractional[^1] = total - fractional.Take(fractional.Length - 1).Sum();
            return fractional;
        }

        int wholeTotal = (int)Math.Round(total);
        var allocation = Enumerable.Repeat(1.0, FoundationStageShares.Length).ToArray();
        int remaining = wholeTotal - allocation.Length;
        var fractionalParts = FoundationStageShares
            .Select((share, index) => new { Index = index, Target = remaining * share })
            .ToArray();
        foreach (var part in fractionalParts)
            allocation[part.Index] += Math.Floor(part.Target);

        int undistributed = remaining - fractionalParts.Sum(part => (int)Math.Floor(part.Target));
        foreach (var part in fractionalParts
                     .OrderByDescending(item => item.Target - Math.Floor(item.Target))
                     .ThenBy(item => item.Index)
                     .Take(undistributed))
            allocation[part.Index] += 1;
        return allocation;
    }

    static FoundationGrowth Multiply(FoundationGrowth value, double multiplier) => new(
        value.HP * multiplier, value.MP * multiplier, value.肉攻 * multiplier, value.神攻 * multiplier,
        value.肉防 * multiplier, value.神防 * multiplier, value.反应 * multiplier, value.神识 * multiplier);

    static FoundationGrowth SumFoundationGrowth(IEnumerable<FoundationGrowth> values) => values.Aggregate(
        new FoundationGrowth(0, 0, 0, 0, 0, 0, 0, 0),
        (total, value) => new FoundationGrowth(
            total.HP + value.HP, total.MP + value.MP, total.肉攻 + value.肉攻, total.神攻 + value.神攻,
            total.肉防 + value.肉防, total.神防 + value.神防, total.反应 + value.反应, total.神识 + value.神识));

    static double MaximumGrowthDifference(FoundationGrowth left, FoundationGrowth right) => new[]
    {
        Math.Abs(left.HP - right.HP), Math.Abs(left.MP - right.MP), Math.Abs(left.肉攻 - right.肉攻), Math.Abs(left.神攻 - right.神攻),
        Math.Abs(left.肉防 - right.肉防), Math.Abs(left.神防 - right.神防), Math.Abs(left.反应 - right.反应), Math.Abs(left.神识 - right.神识)
    }.Max();

    static string FormatFoundationGrowth(FoundationGrowth growth) =>
        $"HP={growth.HP:F2} MP={growth.MP:F2} 肉攻={growth.肉攻:F2} 神攻={growth.神攻:F2} 肉防={growth.肉防:F2} 神防={growth.神防:F2} 反应={growth.反应:F2} 神识={growth.神识:F2}";

    static double EvaluateFoundationCoreCurve(double progress, DaoFoundationCoreCurve curve)
    {
        AssertFoundationGrowth(progress is >= 0 and <= 1, "道基连续进度必须位于 [0, 1]");
        AssertFoundationGrowth(curve.StartingNormalizedMagnitude is >= 0 and <= 1, "道基核心曲线起点必须归一化");
        AssertFoundationGrowth(curve.MaximumNormalizedMagnitude >= curve.StartingNormalizedMagnitude && curve.MaximumNormalizedMagnitude <= 1, "道基核心曲线终点必须位于起点与 1 之间");
        AssertFoundationGrowth(curve.Exponent >= 1, "道基核心曲线指数不得早期超额放大");
        return curve.StartingNormalizedMagnitude
             + (curve.MaximumNormalizedMagnitude - curve.StartingNormalizedMagnitude) * Math.Pow(progress, curve.Exponent);
    }

    static void AssertFoundationGrowth(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"四阶段成长审计失败：{message}");
    }

    static void RunMansionBodyBudgetAudit()
    {
        const double expectedUnitPerMansion = 1.0;
        const double totalBudgetCap = 5.0;

        Console.WriteLine("【五府府体预算审计（N-FPD-MANSION-01）】");
        Console.WriteLine($"  共同预算：每座完整紫府={expectedUnitPerMansion:F2} 预算分；五府总上限={totalBudgetCap:F2}。预算分只用于本审计，不是运行时资源、槽位或倍率。");
        Console.WriteLine("  差异参数／目标指标：");
        foreach (var budget in MansionBodyBudgets)
            Console.WriteLine($"    {budget.Mansion}：{budget.ParameterRange}；指标：{budget.TargetMetric}。");

        var representativeFixtures = Enumerable.Range(1, MansionBodyOrder.Length)
            .Select(count => new MansionBudgetFixture(
                $"{count}府·均衡",
                CreateMansionAuditInnate(),
                MansionBodyOrder.Take(count).ToArray(),
                1.0))
            .ToArray();

        Console.WriteLine("  代表 Build（1～5 府边际）：");
        double previousBudget = 0.0;
        foreach (var fixture in representativeFixtures)
        {
            var result = EvaluateMansionBudgetFixture(fixture);
            double marginalBudget = result.BudgetUnits - previousBudget;
            AssertMansionBudget(result.PalaceCount == fixture.Mansions.Length, $"{fixture.Name} 府数不一致");
            AssertMansionBudget(Math.Abs(result.BudgetUnits - fixture.Mansions.Length * expectedUnitPerMansion) < 0.0001, $"{fixture.Name} 共同预算错误");
            AssertMansionBudget(Math.Abs(marginalBudget - expectedUnitPerMansion) < 0.0001, $"{fixture.Name} 边际收益错误");
            AssertMansionBudget(result.AddedArtSlots == 0 && result.AddedDivineArtSlots == 0 && result.AddedStableSeats == 0 && result.AddedDanXiang == 0, $"{fixture.Name} 发生数量越权");
            Console.WriteLine($"    {fixture.Name,-8} 府体={string.Join('+', fixture.Mansions),-14} 累计={result.BudgetUnits:F2} 边际={marginalBudget:F2} 术/神通槽/位/丹相=0/0/0/0");
            previousBudget = result.BudgetUnits;
        }
        AssertMansionBudget(Math.Abs(previousBudget - totalBudgetCap) < 0.0001, "五府总预算超过或未达到共同上限");

        Console.WriteLine("  五类极端样例：");
        foreach (var mansion in MansionBodyOrder)
        {
            var fixture = new MansionBudgetFixture(
                $"{mansion}极端",
                CreateMansionAuditInnate(MansionAttribute(mansion)),
                [mansion],
                1.0);
            var result = EvaluateMansionBudgetFixture(fixture);
            AssertMansionBudget(Math.Abs(result.BudgetUnits - expectedUnitPerMansion) < 0.0001, $"{fixture.Name} 超出单府预算");
            AssertMansionBudget(result.AddedArtSlots == 0 && result.AddedDivineArtSlots == 0 && result.AddedStableSeats == 0 && result.AddedDanXiang == 0, $"{fixture.Name} 发生数量越权");
            Console.WriteLine($"    {fixture.Name,-8} {MansionAttribute(mansion)}=15 预算={result.BudgetUnits:F2} 术/神通槽/位/丹相=0/0/0/0");
        }

        var lowAffinity = EvaluateMansionBudgetFixture(new MansionBudgetFixture("五府·低亲和", CreateMansionAuditInnate(), MansionBodyOrder, 0.8));
        var highAffinity = EvaluateMansionBudgetFixture(new MansionBudgetFixture("五府·高亲和", CreateMansionAuditInnate(), MansionBodyOrder, 1.2));
        AssertMansionBudget(Math.Abs(lowAffinity.BudgetUnits - highAffinity.BudgetUnits) < 0.0001, "软亲和改变了建成后的最终强度");
        Console.WriteLine($"  软亲和回归：成本系数 0.80 与 1.20 均为 {lowAffinity.BudgetUnits:F2} 预算分；建成后最终强度不变。");
        Console.WriteLine("  结论：PASS；第四、第五府各保留 1.00 预算分收益，且不增加术法槽、神通槽、稳定位格或丹相。");
    }

    static MansionBudgetAuditResult EvaluateMansionBudgetFixture(MansionBudgetFixture fixture)
    {
        var inputValidation = BuildInputRules.Validate(fixture.Innate);
        AssertMansionBudget(inputValidation.IsValid, $"{fixture.Name} Build 输入无效：{inputValidation.Error}");
        AssertMansionBudget(fixture.SoftAffinityCostMultiplier > 0, $"{fixture.Name} 软亲和成本系数必须为正数");
        AssertMansionBudget(fixture.Mansions.Length is >= 1 and <= 5, $"{fixture.Name} 府数必须在 1～5 之间");
        AssertMansionBudget(fixture.Mansions.Distinct().Count() == fixture.Mansions.Length, $"{fixture.Name} 含有重复府属");

        double budgetUnits = 0.0;
        foreach (var mansion in fixture.Mansions)
        {
            var budget = MansionBodyBudgets.SingleOrDefault(item => item.Mansion == mansion);
            AssertMansionBudget(budget != null, $"{fixture.Name} 含有未知府属：{mansion}");
            budgetUnits += budget.BudgetUnits;
        }

        return new(fixture.Mansions.Length, budgetUnits, AddedArtSlots: 0, AddedDivineArtSlots: 0, AddedStableSeats: 0, AddedDanXiang: 0);
    }

    static Dictionary<string, int> CreateMansionAuditInnate(string focusedAttribute = null)
    {
        var innate = new Dictionary<string, int>
        {
            ["根骨"] = 8,
            ["魂魄"] = 8,
            ["神识"] = 8,
            ["资质"] = 8,
            ["气运"] = 8,
        };

        if (focusedAttribute == null)
            return innate;

        foreach (var attribute in innate.Keys.ToArray())
            innate[attribute] = attribute == focusedAttribute ? 15 : 3;
        return innate;
    }

    static string MansionAttribute(string mansion) => mansion switch
    {
        "命府" => "根骨",
        "魂府" => "魂魄",
        "识府" => "神识",
        "悟府" => "资质",
        "运府" => "气运",
        _ => throw new ArgumentOutOfRangeException(nameof(mansion), mansion, "未知府属"),
    };

    static void AssertMansionBudget(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"府体预算审计失败：{message}");
    }

    static G2CoverageResult EvaluateG2Coverage(int seedsPerBuild, int distinctPairs, int battlesPerCell)
    {
        bool meetsThreshold = seedsPerBuild >= 200 && distinctPairs >= 20 && battlesPerCell >= 2000;
        return new G2CoverageResult(meetsThreshold ? "SUFFICIENT" : "INSUFFICIENT", meetsThreshold);
    }

    static int ParseG2AuditCycles(string[] args)
    {
        if (args.Length == 1 && args[0] == "--g2-audit")
            return GameData.CultivationCycles;

        if (args.Length == 3
            && args[0] == "--g2-audit"
            && args[1] == "--cycles"
            && int.TryParse(args[2], out int cycles)
            && cycles > 0)
            return cycles;

        throw new ArgumentException("Usage: BattleSim --g2-audit [--cycles <positive-integer>]");
    }

    static int ParseG2AttributionCycles(string[] args)
    {
        if (args.Length == 1 && args[0] == "--g2-attribution")
            return GameData.CultivationCycles;

        if (args.Length == 3
            && args[0] == "--g2-attribution"
            && args[1] == "--cycles"
            && int.TryParse(args[2], out int cycles)
            && cycles > 0)
            return cycles;

        throw new ArgumentException("Usage: BattleSim --g2-attribution [--cycles <positive-integer>]");
    }

    static (double Lower, double Upper) Wilson95Percent(int wins, int total)
    {
        if (total <= 0 || wins < 0 || wins > total)
            throw new ArgumentOutOfRangeException(nameof(total), "Wilson interval requires 0 <= wins <= total and total > 0.");

        const double z = 1.959963984540054;
        double n = total;
        double p = wins / n;
        double denominator = 1.0 + z * z / n;
        double centre = (p + z * z / (2.0 * n)) / denominator;
        double margin = z * Math.Sqrt((p * (1.0 - p) + z * z / (4.0 * n)) / n) / denominator;
        return (Math.Max(0.0, centre - margin), Math.Min(1.0, centre + margin));
    }

    static bool PrintG2CoverageAudit(string stage, string[] tags, IReadOnlyList<Character>[] pools, double[,] matrix, int requestedBattles)
    {
        Console.WriteLine();
        Console.WriteLine($"【G2 覆盖审计：{stage}】");
        bool passed = true;
        for (int i = 0; i < pools.Length; i++)
        {
            var buildCoverage = EvaluateG2Coverage(pools[i].Count, 20, requestedBattles);
            Console.WriteLine($"  样本 {tags[i]}：{pools[i].Count}，{buildCoverage.Status}");
            passed &= buildCoverage.MeetsThreshold;
        }

        for (int i = 0; i < pools.Length; i++)
        {
            for (int j = i + 1; j < pools.Length; j++)
            {
                int pairs = Math.Min(pools[i].Count, pools[j].Count);
                int battles = pairs == 0 ? 0 : Math.Max(1, requestedBattles / pairs / 2) * pairs * 2;
                var coverage = EvaluateG2Coverage(Math.Min(pools[i].Count, pools[j].Count), pairs, battles);
                if (double.IsNaN(matrix[i, j]) || battles == 0)
                {
                    Console.WriteLine($"  对局 {tags[i]} vs {tags[j]}：无有效样本，INSUFFICIENT");
                    passed = false;
                    continue;
                }

                int wins = (int)Math.Round(matrix[i, j] / 100.0 * battles);
                var (lower, upper) = Wilson95Percent(wins, battles);
                string extreme = wins == 0 || wins == battles ? "，极端结果" : "";
                Console.WriteLine($"  对局 {tags[i]} vs {tags[j]}：胜率={matrix[i, j]:F2}% 95% Wilson=[{lower * 100:F2}%, {upper * 100:F2}%] 配对={pairs} 场次={battles} {coverage.Status}{extreme}");
                passed &= coverage.MeetsThreshold;
            }
        }

        return passed;
    }

    static double[,] ComputeSymmetricMatrix(IReadOnlyList<Character>[] pools, int sim)
    {
        int n = pools.Length;
        double[,] mat = new double[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double wI = 0;
                int tot = 0;
                var left = pools[i];
                var right = pools[j];
                int pairs = Math.Min(left.Count, right.Count);
                if (pairs == 0)
                {
                    mat[i, j] = double.NaN;
                    mat[j, i] = double.NaN;
                    continue;
                }

                var directionalRounds = AllocateDirectionalBattleRounds(sim, pairs);
                for (int s = 0; s < pairs; s++)
                {
                    var ci = left[s];
                    var cj = right[s];
                    int forwardRounds = directionalRounds[s * 2];
                    if (forwardRounds > 0)
                    {
                        var (wi, _, _) = Combat.Simulate(ci, cj, forwardRounds);
                        wI += wi * forwardRounds / 100.0;
                        tot += forwardRounds;
                    }
                    int reverseRounds = directionalRounds[s * 2 + 1];
                    if (reverseRounds > 0)
                    {
                        var (_, wj2, _) = Combat.Simulate(cj, ci, reverseRounds);
                        wI += wj2 * reverseRounds / 100.0;
                        tot += reverseRounds;
                    }
                }

                mat[i, j] = wI * 100.0 / tot;
                mat[j, i] = 100.0 - mat[i, j];
            }
        }

        return mat;
    }

    internal static int[] AllocateDirectionalBattleRounds(int totalBattles, int pairs)
    {
        if (totalBattles < 0)
            throw new ArgumentOutOfRangeException(nameof(totalBattles));
        if (pairs <= 0)
            throw new ArgumentOutOfRangeException(nameof(pairs));

        var rounds = new int[checked(pairs * 2)];
        int roundsPerSlot = totalBattles / rounds.Length;
        int remainder = totalBattles % rounds.Length;
        for (int slot = 0; slot < rounds.Length; slot++)
            rounds[slot] = roundsPerSlot + (slot < remainder ? 1 : 0);
        return rounds;
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

readonly record struct BuildInputValidationResult(bool IsValid, int PurchaseCost, string Error);

static class BuildInputRules
{
    const int PurchasePointLimit = 25;
    const int MinimumValue = 3;
    const int MaximumValue = 15;

    static readonly string[] RequiredAttributes = { "根骨", "魂魄", "神识", "资质", "气运" };

    public static BuildInputValidationResult Validate(IReadOnlyDictionary<string, int> innate)
    {
        if (innate == null)
            return new(false, 0, "先天属性不能为空。");

        foreach (var attribute in RequiredAttributes)
        {
            if (!innate.ContainsKey(attribute))
                return new(false, 0, $"缺少必填先天属性：{attribute}。");
        }

        foreach (var attribute in innate.Keys)
        {
            if (!RequiredAttributes.Contains(attribute))
                return new(false, 0, $"未知先天属性：{attribute}。");
        }

        int purchaseCost = 0;
        foreach (var attribute in RequiredAttributes)
        {
            int value = innate[attribute];
            if (value < MinimumValue || value > MaximumValue)
                return new(false, 0, $"{attribute}必须在{MinimumValue}到{MaximumValue}之间。");
            purchaseCost += CalculateAttributeCost(value);
        }

        if (purchaseCost > PurchasePointLimit)
            return new(false, purchaseCost, $"先天属性购买点数不能超过{PurchasePointLimit}。");

        return new(true, purchaseCost, "");
    }

    static int CalculateAttributeCost(int value)
    {
        int cost = 0;
        for (int current = MinimumValue + 1; current <= value; current++)
        {
            cost += current <= 8 ? 1 : current <= 12 ? 2 : 3;
        }
        return cost;
    }
}
