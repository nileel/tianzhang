# TQ-053 Crit Multiplier Semantics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the documented `critDamage` field mean an additive percentage-point bonus on the fixed 1.50 critical multiplier in Unity and BattleSim, with matching 1.50 and 1.65 regressions.

**Architecture:** Each runtime exposes a narrowly named critical-multiplier helper that adds `critDamage` and elemental critical-damage bonuses to a `1.5` base multiplier. Existing damage-resolution paths call that helper, so the value tested directly is also the value used in combat. Documentation distinguishes the field from a total multiplier and preserves the numeric-design document as the source for secondary-stat origins.

**Tech Stack:** Unity 6.0.3 / NUnit / C# / .NET 10 BattleSim.

## Global Constraints

- `docs/基础设定/战斗系统.txt` is the base critical-multiplier fact source: a critical begins at 150% (×1.50).
- `critDamage` is an additive percentage-point bonus, not a total multiplier: `0 → 1.50`, `15 → 1.65`.
- Elemental critical-damage effects add percentage points through the same calculation.
- Keep all existing damage, hit, block, shield, and element-resolution behavior unchanged apart from replacing the duplicated expression with the named helper.

---

### Task 1: Lock the public field and design terminology

**Files:**
- Modify: `docs/基础设定/战斗系统.txt:126-131`
- Modify: `docs/基础设定/角色数值设计.txt:161-164`
- Modify: `src/Assets/Scripts/Entity/CharacterData.cs:33-41`

**Interfaces:**
- Produces: an explicit cross-runtime contract: `critDamage` and elemental bonuses are percentage points above `DamageCalculator.BaseCritMultiplier` / `Combat.BaseCritMultiplier`.

- [x] **Step 1: State the unambiguous combat rule**

Add text directly after the critical multiplier definition:

```text
运行时字段 `critDamage`/“暴击伤害”表示相对基础 150% 的附加百分比点，不是总倍率：`critDamage = 0` 时为 ×1.50，`critDamage = 15` 时为 ×1.65；五行内圈暴击伤害修正也按百分比点相加。
```

- [x] **Step 2: Align secondary-stat source notes and serialized field comment**

Update the numeric-design note and `CharacterData.critDamage` comment so both say “基础 150% 之上的附加百分比点”. Do not alter stat weights or CSV values.

### Task 2: Add Unity’s tested multiplier boundary

**Files:**
- Modify: `src/Assets/Scripts/Combat/DamageCalculator.cs:11-16,221-228`
- Modify: `src/Assets/Tests/EditMode/TacticalGridModelTests.cs` (new `CombatMechanismTests` test)

**Interfaces:**
- Produces: `DamageCalculator.BaseCritMultiplier` (`1.5f`) and `DamageCalculator.GetCritMultiplier(float critDamage, float elementCritDamageBonus = 0f)`.
- Consumes: `Character.CritDamage` and `ElementMatch.CritDamageBonus` as additive percentage points.

- [x] **Step 1: Write the failing Unity regression**

Add an NUnit test that executes:

```csharp
Assert.AreEqual(1.50f, DamageCalculator.GetCritMultiplier(0f), 0.0001f);
Assert.AreEqual(1.65f, DamageCalculator.GetCritMultiplier(15f), 0.0001f);
Assert.AreEqual(1.75f, DamageCalculator.GetCritMultiplier(15f, 10f), 0.0001f);
```

- [x] **Step 2: Implement the minimal reusable expression**

Add this API near the existing constants and change `RollCrit` to call it:

```csharp
public const float BaseCritMultiplier = 1.5f;

public static float GetCritMultiplier(float critDamage, float elementCritDamageBonus = 0f)
{
    return BaseCritMultiplier + (critDamage + elementCritDamageBonus) / 100f;
}
```

```csharp
return GetCritMultiplier(attacker.CritDamage, elementMatch.CritDamageBonus);
```

- [x] **Step 3: Run Unity compilation and regression**

Run `dotnet build src/Assembly-CSharp.csproj`, `dotnet build src/TianZhang.EditModeTests.csproj`, and `powershell -ExecutionPolicy Bypass -File tools/run-unity-editmode-tests.ps1`. Require the new test and all existing EditMode tests to pass.

### Task 3: Add BattleSim’s matching multiplier boundary

**Files:**
- Modify: `simulations/BattleSim/Combat.cs:8-32`
- Modify: `simulations/BattleSim/BattleSimSelfTests.cs:10-44` and a new suite method

**Interfaces:**
- Produces: `Combat.BaseCritMultiplier` (`1.5`) and `Combat.GetCritMultiplier(double critDamage, double elementCritDamageBonus = 0)`.
- Consumes: the same percentage-point inputs as Unity.

- [x] **Step 1: Write the failing BattleSim self-test suite**

Route `--self-test crit-multiplier-tq053` to assertions:

```csharp
AssertClose(1.50, Combat.GetCritMultiplier(0), 0.0001, "zero critDamage keeps base multiplier");
AssertClose(1.65, Combat.GetCritMultiplier(15), 0.0001, "15 critDamage adds percentage points");
AssertClose(1.75, Combat.GetCritMultiplier(15, 10), 0.0001, "element bonus adds percentage points");
```

- [x] **Step 2: Implement and use the equivalent helper**

Add the public constant and helper to `Combat`, then replace the critical expression in `ApplyDefenses` with:

```csharp
rawDmg = (int)Math.Round(rawDmg * GetCritMultiplier(
    attacker.Secondary.GetValueOrDefault("暴击伤害", 0), critDamageBonus));
```

- [x] **Step 3: Verify BattleSim**

Run `dotnet build -c Release --no-restore simulations/BattleSim`, `dotnet run --no-build -c Release --project simulations/BattleSim -- --self-test crit-multiplier-tq053`, and the default `dotnet run --no-build -c Release --project simulations/BattleSim`. Require exit code 0 for all commands.

### Task 4: Close the trust-gate slice

**Files:**
- Modify: `开发管理/当前任务队列.txt`
- Create: `开发管理/任务归档/2026-07-11-TQ-053-统一暴击倍率语义归档.txt`
- Modify: `开发管理/设计-当前状态.txt` only if it has a G2 task-status section suited to the completed fact

**Interfaces:**
- Consumes: passing Unity and BattleSim verification evidence.
- Produces: a completed/archived TQ-053 record and unblocked TQ-054 task state, without claiming G2 has passed.

- [x] **Step 1: Run final hygiene checks**

Run `git diff --check` and require no output; re-run the Unity, BattleSim self-test, and default BattleSim commands after all documentation/state edits.

- [x] **Step 2: Record only verified facts**

Archive the original task card with the files changed and exact passing commands. Mark TQ-053 completed, set TQ-054 from blocked to pending, and state that G2 remains incomplete until its subsequent tasks finish.

- [x] **Step 3: Commit the self-contained slice**

Stage only the files above and commit with `TQ-053: unify crit multiplier semantics`.
