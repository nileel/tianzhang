# BattleSim 一对一距离模型 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 BattleSim 1v1 接入主战法宝普攻档案、独立技能射程和移动后施法，并移除风格驱动的远程伤害近似。

**Architecture:** `GameData` 声明基础攻击、术法和神通的距离档案；`Combat` 持有每场的双方距离并在每次行动前移动到可施放位置。无装备角色使用 `BasicAttackProfile` 兜底，后续主战法宝数据可替换该档案而不改战斗循环。

**Tech Stack:** .NET 10、C#、BattleSim self-test。

---

### Task 1: 建立攻击距离档案与普攻兜底

**Files:**

- Modify: `simulations/BattleSim/GameData.cs:255-545`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
var unarmed = GameData.UnarmedBasicAttack;
AssertEqual(1, unarmed.MinRange, "unarmed basic attack minimum range");
AssertEqual(1, unarmed.MaxRange, "unarmed basic attack maximum range");
AssertEqual(2, GameData.TaiyiFuxiuArt.MinRange, "symbol art keeps an independent minimum range");
AssertEqual(4, GameData.TaiyiFuxiuArt.MaxRange, "symbol art keeps an independent maximum range");
```

- [ ] **Step 2: 运行红灯测试**

Run: `dotnet build -c Release --no-restore simulations/BattleSim`

Expected: 因 `MinRange`、`MaxRange` 或 `UnarmedBasicAttack` 缺失失败。

- [ ] **Step 3: 最小实现距离档案**

```csharp
public record AttackProfile(string Name, string Type, double Mult, string Element, int MinRange, int MaxRange);
public record ArtConfig(string Name, string Type, double Mult, int MPCost, int Cooldown, string Element, int MinRange, int MaxRange);
public record DivineConfig(string Name, string Type, double Mult, double DefPen, int Cooldown, string Element, int MinRange, int MaxRange);
public static readonly AttackProfile UnarmedBasicAttack = new("徒手", "物理", 1.0, "", 1, 1);
```

为每个既有术法/神通显式登记距离：拳法 1–1、常规神魂术法 2–4、符修 2–4、太虚 2–4、玉清剑诀 1–3、水系 1–3；保持现有倍率、MP 和冷却不变。

- [ ] **Step 4: 验证绿灯**

Run: `dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test distance-model-tq055`

Expected: `SELFTEST distance-model-tq055 PASS`。

- [ ] **Step 5: 提交**

```powershell
git add simulations/BattleSim/GameData.cs simulations/BattleSim/BattleSimSelfTests.cs
git commit -m "feat(battlesim): add attack distance profiles"
```

### Task 2: 以距离和移力替代 rangePenalty

**Files:**

- Modify: `simulations/BattleSim/Combat.cs:38-281`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs`

- [ ] **Step 1: 扩展失败测试**

```csharp
AssertEqual(6, Combat.InitialDistance, "distance-model opening separation");
AssertEqual(1, Combat.MoveIntoRange(6, 5, 1, 1), "melee moves before attacking");
AssertEqual(2, Combat.MoveIntoRange(1, 3, 2, 4), "minimum range forces retreat");
AssertEqual(6, Combat.MoveIntoRange(6, 0, 2, 4), "zero movement keeps distance");
```

- [ ] **Step 2: 运行红灯测试**

Run: `dotnet build -c Release --no-restore simulations/BattleSim`

Expected: 因 `InitialDistance` 或 `MoveIntoRange` 缺失失败。

- [ ] **Step 3: 实现纯距离辅助函数和行动选择**

```csharp
internal const int InitialDistance = 6;
internal static int MoveIntoRange(int distance, int movePoints, int minRange, int maxRange)
{
    if (distance > maxRange) return Math.Max(maxRange, distance - Math.Max(0, movePoints));
    if (distance < minRange) return Math.Min(minRange, distance + Math.Max(0, movePoints));
    return distance;
}
```

在 `Simulate` 为双方维护一个共享 `distance`。每次行动依次选择合法神通、合法术法、合法普攻；没有合法攻击时先调用 `MoveIntoRange`，再重新选择。删除 `rangePenaltyA/B` 及所有 0.35 乘法。普攻读取 `UnarmedBasicAttack`，不再从 `Style` 推断攻击距离。

- [ ] **Step 4: 验证距离回归和旧战斗回归**

Run:

```powershell
dotnet build -c Release --no-restore simulations/BattleSim
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test distance-model-tq055
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test ct-reaction-tq052
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test crit-multiplier-tq053
```

Expected: 四个命令均成功；近战会移动后攻击，射程技能可首回合攻击，最小距离会迫使后退。

- [ ] **Step 5: 提交**

```powershell
git add simulations/BattleSim/Combat.cs simulations/BattleSim/BattleSimSelfTests.cs
git commit -m "feat(battlesim): simulate one-vs-one distance"
```

### Task 3: 更新 G2 审计口径并做全量验证

**Files:**

- Modify: `simulations/BattleSim/G2AttributionAudit.cs`
- Modify: `simulations/BattleSim/Program.cs`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
AssertEqual(false, G2AttributionAudit.Layers.Contains("远程资格统一"),
    "distance model removes the retired range-eligibility counterfactual");
AssertSequence(new[] { "先天交换", "功法包交换" }, G2AttributionAudit.Layers,
    "remaining attribution layers are ordered");
```

- [ ] **Step 2: 运行红灯测试**

Run: `dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055`

Expected: suite 失败，说明旧远程资格层仍存在。

- [ ] **Step 3: 最小实现审计更新**

移除远程资格统一层及 `Combat.Options` 覆盖；保留先天交换、功法包交换，令两者在新距离模型下重跑。输出注明“距离模型基线”，不宣称平衡已通过。

- [ ] **Step 4: 全量验证**

Run:

```powershell
dotnet build -c Release --no-restore simulations/BattleSim
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test distance-model-tq055
dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test g2-attribution-tq055
dotnet run --no-build -c Release --project simulations/BattleSim -- --g2-audit --cycles 400
dotnet run --no-build -c Release --project simulations/BattleSim -- --g2-attribution --cycles 400
dotnet run --no-build -c Release --project simulations/BattleSim
git diff --check
```

Expected: 审计输出使用距离模型；G2 结论只反映覆盖，极端结果仍等待产品分类。

- [ ] **Step 5: 暂存并提交**

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/check-pending-whitespace.ps1 -ExpectedPaths 'simulations/BattleSim/GameData.cs|simulations/BattleSim/Combat.cs|simulations/BattleSim/G2AttributionAudit.cs|simulations/BattleSim/Program.cs|simulations/BattleSim/BattleSimSelfTests.cs' -Fix
git add simulations/BattleSim/GameData.cs simulations/BattleSim/Combat.cs simulations/BattleSim/G2AttributionAudit.cs simulations/BattleSim/Program.cs simulations/BattleSim/BattleSimSelfTests.cs
git diff --cached --check
git commit -m "feat(battlesim): align G2 with distance model"
```

## 计划自检

- 规格覆盖：Task 1 定义主战法宝可替换的普攻兜底和独立技能射程；Task 2 建立 1v1 平地移动/距离；Task 3 更新 G2 诊断口径。
- 后续路线：地形/视野/位移、2v2 战术和运行时 CSV/asset 数据链路均保留在规格路线中，不进入本实现计划。
- 类型一致性：`AttackProfile`、`ArtConfig`、`DivineConfig`、`InitialDistance`、`MoveIntoRange` 和 `G2AttributionAudit.Layers` 在各任务的测试与实现中同名。
