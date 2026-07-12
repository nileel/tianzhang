# G2 极端对局归因审计 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为所有覆盖充分的金丹同境 0%/100% 对局提供不改平衡数值的可重复反事实归因审计。

**Architecture:** `Program` 继续负责 CLI 和既有 G2 池构建；新增 `G2AttributionAudit` 集中筛选极端对局、构建反事实角色池、运行并格式化结果。`Combat` 只增加默认关闭的远程资格覆盖，原有 `Simulate` 调用保持不变。

**Tech Stack:** .NET 10、C#、现有零依赖 BattleSim 自测入口。

---

## 文件结构

- 修改：`simulations/BattleSim/Program.cs` — 解析 `--g2-attribution`，把已生成的金丹池和 Build 定义传给审计器。
- 新建：`simulations/BattleSim/G2AttributionAudit.cs` — 反事实模型、池重建、覆盖筛选、输出与聚合。
- 修改：`simulations/BattleSim/Combat.cs` — 默认关闭的远程资格覆盖及其重载。
- 修改：`simulations/BattleSim/BattleSimSelfTests.cs` — 专属 `g2-attribution-tq055` 自测。

### Task 1: 锁定 CLI、覆盖筛选与默认路径

**Files:**

- Modify: `simulations/BattleSim/BattleSimSelfTests.cs:57-63,697-744`
- Modify: `simulations/BattleSim/Program.cs:44-74,662-674`

- [ ] **Step 1: 写入会失败的归因审计自测**

在 `Run` 中加入 suite 路由，并加入如下测试；此时 `ParseG2AttributionCycles`、`IsCoveredExtreme` 和 `G2AttributionAudit` 均不存在，测试必须因缺失 API 失败。

```csharp
if (suite == "g2-attribution-tq055")
    return RunChecked(suite, RunG2AttributionTq055);

static void RunG2AttributionTq055()
{
    var parser = typeof(Program).GetMethod("ParseG2AttributionCycles",
        BindingFlags.Static | BindingFlags.NonPublic);
    if (parser == null)
        throw new InvalidOperationException("Program.ParseG2AttributionCycles is missing.");

    AssertEqual(200, parser.Invoke(null, new object[] { new[] { "--g2-attribution" } }),
        "attribution keeps the 200-cycle default");
    AssertEqual(400, parser.Invoke(null, new object[] { new[] { "--g2-attribution", "--cycles", "400" } }),
        "attribution accepts an explicit positive horizon");

    AssertEqual(true, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 0.0),
        "covered zero result is selected");
    AssertEqual(true, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 100.0),
        "covered full-win result is selected");
    AssertEqual(false, G2AttributionAudit.IsCoveredExtreme(199, 200, 2000, 0.0),
        "under-seeded result is rejected");
    AssertEqual(false, G2AttributionAudit.IsCoveredExtreme(200, 200, 2000, 50.0),
        "non-extreme result is rejected");
}
```

- [ ] **Step 2: 运行自测并确认红灯**

Run:

```powershell
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
```

Expected: `SELFTEST g2-attribution-tq055 FAIL`，原因是 `ParseG2AttributionCycles` 或 `G2AttributionAudit` 缺失，而不是编译环境错误。

- [ ] **Step 3: 以最小实现加入解析与筛选 API**

在 `Program` 增加同 `ParseG2AuditCycles` 一致的严格参数解析；在新文件声明最小的公开审计入口与筛选函数。

```csharp
static int ParseG2AttributionCycles(string[] args)
{
    if (args.Length == 1 && args[0] == "--g2-attribution")
        return GameData.CultivationCycles;
    if (args.Length == 3 && args[0] == "--g2-attribution" && args[1] == "--cycles"
        && int.TryParse(args[2], out int cycles) && cycles > 0)
        return cycles;
    throw new ArgumentException("Usage: BattleSim --g2-attribution [--cycles <positive-integer>]");
}
```

```csharp
namespace BattleSim;

static class G2AttributionAudit
{
    internal static bool IsCoveredExtreme(int leftSamples, int rightSamples, int battles, double winRate) =>
        leftSamples >= 200 && rightSamples >= 200 && battles >= 2000 &&
        (Math.Abs(winRate) < 0.000001 || Math.Abs(winRate - 100.0) < 0.000001);
}
```

在 `Main` 中把 `g2Attribution` 视为合法顶级命令；未传该参数的既有 `g2Audit` 与默认分支不得改变。

- [ ] **Step 4: 运行自测确认绿灯，并回归默认 CLI**

Run:

```powershell
dotnet build -c Release --no-restore simulations/BattleSim
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
dotnet run --no-build -c Release --project simulations/BattleSim
```

Expected: 新 suite 输出 `PASS`；默认命令仍接受零参数且不打印归因区块。

- [ ] **Step 5: 提交解析与筛选基线**

```powershell
git add simulations/BattleSim/Program.cs simulations/BattleSim/BattleSimSelfTests.cs simulations/BattleSim/G2AttributionAudit.cs
git commit -m "feat(battlesim): add G2 attribution selectors"
```

### Task 2: 为远程资格建立默认关闭的战斗覆盖

**Files:**

- Modify: `simulations/BattleSim/Combat.cs:38-281`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs:697-744`

- [ ] **Step 1: 扩展失败测试，声明覆盖 API**

在 `RunG2AttributionTq055` 追加以下断言；它要求默认资格只认 `magic`，而覆盖会给太一、符修、太虚和玄感同样资格。

```csharp
AssertEqual(false, Combat.HasRangedEligibility("taiyi_fuxiu", false),
    "symbol cultivator is not ranged in the unchanged default");
AssertEqual(true, Combat.HasRangedEligibility("taiyi_fuxiu", true),
    "symbol cultivator gains ranged eligibility only in attribution override");
AssertEqual(true, Combat.HasRangedEligibility("magic", false),
    "existing magic eligibility is retained");
AssertEqual(false, Combat.HasRangedEligibility("yuqing", true),
    "physical sword style is not promoted by caster override");
```

- [ ] **Step 2: 运行自测并确认红灯**

Run:

```powershell
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
```

Expected: 编译失败或 suite 失败，明确指出 `Combat.HasRangedEligibility` 缺失。

- [ ] **Step 3: 实现最小覆盖并保留原重载**

在 `Combat` 加入不可变选项和资格函数；三参数 `Simulate` 只委托给四参数版本，确保全部旧调用的选项为 `false`。

```csharp
internal readonly record struct CombatOptions(bool ExtendCasterRangedEligibility = false);

internal static bool HasRangedEligibility(string style, bool extendCasterEligibility) =>
    style == "magic" || (extendCasterEligibility && style is "taiyi" or "taiyi_fuxiu" or "taixu" or "taixu_xuangan");

public static (double winsA, double winsB, double avgTurns) Simulate(Character ca, Character cb, int rounds) =>
    Simulate(ca, cb, rounds, new CombatOptions());

public static (double winsA, double winsB, double avgTurns) Simulate(
    Character ca, Character cb, int rounds, CombatOptions options)
{
    // 保留原方法体；两处 `Style == "magic"` 改为 HasRangedEligibility(style, options.ExtendCasterRangedEligibility)。
}
```

- [ ] **Step 4: 验证覆盖和既有战斗回归**

Run:

```powershell
dotnet build -c Release --no-restore simulations/BattleSim
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test ct-reaction-tq052
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test crit-multiplier-tq053
```

Expected: 新 suite 与两个既有战斗 suite 全部 `PASS`；默认调用不改变远程资格。

- [ ] **Step 5: 提交远程资格覆盖**

```powershell
git add simulations/BattleSim/Combat.cs simulations/BattleSim/BattleSimSelfTests.cs
git commit -m "feat(battlesim): add attribution range override"
```

### Task 3: 构建反事实池、逐极端输出与聚合

**Files:**

- Modify: `simulations/BattleSim/G2AttributionAudit.cs`
- Modify: `simulations/BattleSim/Program.cs:44-120,286-332`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs:697-744`

- [ ] **Step 1: 增加会失败的审计模型测试**

在 `RunG2AttributionTq055` 追加以下测试，先声明可观察的数据模型；以同一对局构建三种条件，确认条件名和交战次数不被实现细节隐藏。

```csharp
var layers = G2AttributionAudit.Layers;
AssertSequence(new[] { "先天交换", "功法包交换", "远程资格统一" }, layers,
    "attribution layers remain ordered and explicit");

var aggregate = G2AttributionAudit.Summarize(new[]
{
    new G2AttributionAudit.Result("甲", "乙", "先天交换", 100.0, 40.0, 2000),
    new G2AttributionAudit.Result("甲", "乙", "功法包交换", 100.0, 75.0, 2000),
});
AssertEqual(2, aggregate.Count, "aggregate counts emitted counterfactuals");
AssertEqual(30.0, aggregate.MaxAbsoluteDelta, "aggregate keeps largest percentage-point delta");
```

- [ ] **Step 2: 运行自测并确认红灯**

Run:

```powershell
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
```

Expected: suite 失败，原因是 `Layers`、`Result` 或 `Summarize` 缺失。

- [ ] **Step 3: 实现池重建、运行与格式化**

在 `G2AttributionAudit` 实现以下稳定边界：

```csharp
internal sealed record BuildProfile(
    string Name,
    IReadOnlyDictionary<string, int> Innate,
    string Style,
    string GongFaName);

internal sealed record Result(
    string LeftName, string RightName, string Layer,
    double BaselineWinRate, double CounterfactualWinRate, int Battles)
{
    internal double DeltaPercentagePoints => CounterfactualWinRate - BaselineWinRate;
}

internal sealed record Aggregate(int Count, double MeanAbsoluteDelta, double MaxAbsoluteDelta, string MaxDeltaMatchup);

internal static IReadOnlyList<string> Layers => ["先天交换", "功法包交换", "远程资格统一"];

internal static void Print(
    IReadOnlyList<BuildProfile> builds,
    IReadOnlyList<Character>[] goldPools,
    double[,] baseline,
    int battlesPerCell)
{
    // 仅选择 IsCoveredExtreme(...) 的 i<j 项；为每个方向建立反事实角色、运行双向 Combat.Simulate，
    // 输出基线、每层反事实和 DeltaPercentagePoints，最后输出 Aggregate。
}
```

角色重建必须从 BuildProfile 和原有确定性 seed 重新执行 `Cultivation.Simulate`、`ApplyGrowth`、`ApplyGoldenCoreResult`、`FinalizeStats`、`AssignArts`。先天交换只替换初始 `Innate`；功法包交换只替换 `GongFaName`、其权重/成长表和元素；远程资格统一只传 `new CombatOptions(true)`。不得修改 `GameData`、`BuildDefs`、默认 CombatOptions 或默认矩阵。

在 `Program` 中把 `BuildDef` 改为可由审计器消费的 `internal` record，加入 `g2Attribution` 分支，调用 `G2AttributionAudit.Print(...)`。使用现有的金丹池、`mat` 与常量 `SIM`，保证基线与 G2 审计相同。

- [ ] **Step 4: 全量验证归因和默认路径**

Run:

```powershell
dotnet build -c Release --no-restore simulations/BattleSim
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
dotnet run --no-build -c Release --project simulations/BattleSim -- --g2-attribution --cycles 400
dotnet run --no-build -c Release --project simulations/BattleSim
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths simulations/BattleSim,docs/superpowers
git diff --check
```

Expected: 归因命令只为覆盖充分的 0%/100% 金丹对局打印三个反事实层和汇总；默认命令没有归因区块；所有命令退出码为 0。

- [ ] **Step 5: 清理空白、暂存并提交实现**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'simulations/BattleSim/Program.cs|simulations/BattleSim/Combat.cs|simulations/BattleSim/G2AttributionAudit.cs|simulations/BattleSim/BattleSimSelfTests.cs' -Fix
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'simulations/BattleSim/Program.cs|simulations/BattleSim/Combat.cs|simulations/BattleSim/G2AttributionAudit.cs|simulations/BattleSim/BattleSimSelfTests.cs'
git add simulations/BattleSim/Program.cs simulations/BattleSim/Combat.cs simulations/BattleSim/G2AttributionAudit.cs simulations/BattleSim/BattleSimSelfTests.cs
git diff --cached --check
git commit -m "feat(battlesim): add G2 attribution audit"
```

### Task 4: 记录 TQ-055 的诊断证据，不提前关闭 G2

**Files:**

- Modify: `开发管理/任务归档/2026-07-12-TQ-055-G2覆盖与极端结果重验归档.txt`
- Modify: `开发管理/当前任务队列.txt`
- Modify: `开发管理/自动工作流状态.txt`

- [ ] **Step 1: 写入归档与队列的失败关闭断言**

在归档中记录 400 轮覆盖证据、归因命令、极端结果仍未获产品分类这一事实；队列任务保持“待处理”，不得写 G2 通过。状态文件“最近执行结果”记录审计工具已完成、G2 继续阻塞。

- [ ] **Step 2: 验证管理文本和状态边界**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理
git diff --check
```

Expected: 文本检查通过；变更未把 TQ-055 或 G2 标为完成/通过。

- [ ] **Step 3: 提交诊断记录**

```powershell
git add 开发管理/任务归档/2026-07-12-TQ-055-G2覆盖与极端结果重验归档.txt 开发管理/当前任务队列.txt 开发管理/自动工作流状态.txt
git diff --cached --check
git commit -m "docs: record G2 attribution evidence"
```

## 计划自检

- 规格覆盖：Task 1 处理 CLI 与覆盖筛选；Task 2 实现默认关闭的远程资格覆盖；Task 3 实现三类反事实、逐对局输出和聚合；Task 4 记录证据且不改变 G2 结论。
- 无占位检查：已提供每步的文件、函数签名、测试断言、命令和预期输出。
- 类型一致性：`BuildProfile`、`Result`、`Aggregate`、`CombatOptions`、`ParseG2AttributionCycles` 和 `IsCoveredExtreme` 在创建、测试和调用处使用同名签名。
