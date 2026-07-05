# Character Creation Point Buy Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move character creation innate point-buy values from hardcoded logic into designer-editable CSV data imported as a runtime ScriptableObject asset.

**Architecture:** Designers edit `Assets/DataConfig/CharacterCreationPointBuy.csv`; `DataConfigImporter` generates a `CharacterCreationPointBuyConfig` asset under `Assets/Resources/Data/CharacterCreation/`. Runtime validation loads the config through `Resources.Load` with a code fallback matching the current 25-point, 3-15, 1/2/3 cost table.

**Tech Stack:** Unity 6, C# ScriptableObject, existing `DataConfigImporter`, NUnit EditMode tests, BattleSim regression.

---

### Task 1: RED Tests For Config-Driven Point Buy

**Files:**
- Modify: `src/Assets/Tests/EditMode/CharacterCreationTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests proving custom config changes the purchase limit and cost curve:

```csharp
[Test]
public void InnateValidationUsesProvidedPointBuyConfig()
{
    var config = CharacterCreationPointBuyConfig.CreateFallback();
    config.purchasePointLimit = 30;
    var draft = CharacterCreationCatalog.CreateDefaultDraft();
    draft.Innate = new InnateAttributeSet(15, 8, 3, 3, 3);

    var result = CharacterCreationRules.Validate(draft, config);

    Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
    Assert.AreEqual(27, result.InnatePurchasePointsUsed);
    Assert.AreEqual(3, result.InnatePurchasePointsRemaining);
}

[Test]
public void PointBuyConfigCanChangeTierCosts()
{
    var config = CharacterCreationPointBuyConfig.CreateFallback();
    config.costRanges = new[]
    {
        new CharacterCreationPointBuyConfig.CostRange { fromValue = 4, toValue = 8, costPerLevel = 1 },
        new CharacterCreationPointBuyConfig.CostRange { fromValue = 9, toValue = 12, costPerLevel = 2 },
        new CharacterCreationPointBuyConfig.CostRange { fromValue = 13, toValue = 15, costPerLevel = 4 },
    };

    Assert.AreEqual(22, CharacterCreationPointBuyConfig.CreateFallback().CalculateCost(15));
    Assert.AreEqual(25, config.CalculateCost(15));
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet build src/TianZhang.EditModeTests.csproj`

Expected: compile failure because `CharacterCreationPointBuyConfig` and `Validate(draft, config)` do not exist.

---

### Task 2: Runtime Config Asset Type

**Files:**
- Create: `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationPointBuyConfig.cs`
- Create: `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationPointBuyConfig.cs.meta`
- Modify: `src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add ScriptableObject**

Create `CharacterCreationPointBuyConfig` with:

```csharp
[CreateAssetMenu(fileName = "CharacterCreationPointBuyConfig", menuName = "天章/角色创建/点购配置")]
public class CharacterCreationPointBuyConfig : ScriptableObject
{
    public int purchasePointLimit = 25;
    public int minValue = 3;
    public int baseValue = 3;
    public int maxValue = 15;
    public CostRange[] costRanges = { ... };

    public static CharacterCreationPointBuyConfig LoadDefault();
    public static CharacterCreationPointBuyConfig CreateFallback();
    public int CalculateCost(int value);
    public int CalculateCost(InnateAttributeSet innate);
}
```

- [ ] **Step 2: Verify GREEN**

Run: `dotnet build src/TianZhang.EditModeTests.csproj`

Expected: tests compile to the next missing overload or pass after Task 3.

---

### Task 3: Rules Use Config

**Files:**
- Modify: `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationModels.cs`
- Modify: `src/Assets/Scripts/Game/CharacterCreation/CharacterCreationRules.cs`
- Modify: `src/Assets/Scripts/Game/SectSelectionManager.cs`

- [ ] **Step 1: Add overloads and replace hardcoded reads**

`CharacterCreationRules.Validate(draft)` should call `Validate(draft, CharacterCreationPointBuyConfig.LoadDefault())`. `ValidateInnate` should use `config.minValue`, `config.maxValue`, `config.purchasePointLimit`, and `config.CalculateCost(innate)`.

- [ ] **Step 2: Preserve current default behavior**

Existing tests for `8/8/8/8/8` and `15/6/3/3/3` must still pass with fallback/default config.

- [ ] **Step 3: Verify GREEN**

Run: `dotnet build src/TianZhang.EditModeTests.csproj`

Expected: all EditMode tests build cleanly.

---

### Task 4: CSV Import And Default Data

**Files:**
- Create: `src/Assets/DataConfig/CharacterCreationPointBuy.csv`
- Create: `src/Assets/DataConfig/CharacterCreationPointBuy.csv.meta`
- Create: `src/Assets/Resources.meta` if absent
- Create: `src/Assets/Resources/Data.meta` if absent
- Create: `src/Assets/Resources/Data/CharacterCreation.meta` if absent
- Create: `src/Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset`
- Create: `src/Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset.meta`
- Modify: `src/Assets/Scripts/Editor/DataConfigImporter.cs`
- Modify: `src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add CSV**

Use:

```csv
configId,purchasePointLimit,minValue,baseValue,maxValue,fromValue,toValue,costPerLevel
default,25,3,3,15,4,8,1
default,25,3,3,15,9,12,2
default,25,3,3,15,13,15,3
```

- [ ] **Step 2: Add importer menu**

Add `ImportCharacterCreationPointBuy()` to `DataConfigImporter`, include it in `ImportAll()`, parse the CSV, and create/update the Resources asset.

- [ ] **Step 3: Verify importer compiles**

Run: `dotnet build src/Assembly-CSharp.csproj` and `dotnet build src/TianZhang.EditModeTests.csproj`.

Expected: both builds succeed.

---

### Task 5: Project Rule Documentation And Regression

**Files:**
- Modify: `开发管理/开发-技术经验.txt`

- [ ] **Step 1: Record the rule**

Add a short rule: values expected to be tuned frequently by design must use `DataConfig CSV -> generated ScriptableObject asset -> runtime read`, with code fallback only for safety.

- [ ] **Step 2: Run final verification**

Run:

```powershell
dotnet build src/TianZhang.EditModeTests.csproj
dotnet build src/Assembly-CSharp.csproj
dotnet build src/TianZhang.Runtime.csproj
dotnet build -c Release --no-restore "D:\天章游戏开发\simulations\BattleSim"
dotnet run --no-build -c Release --project "D:\天章游戏开发\simulations\BattleSim"
```

Expected: all commands pass.

---

### Self-Review

- Spec coverage: covers designer CSV, import-generated asset, runtime read, fallback defaults, and future project rule.
- Placeholder scan: no TBD/TODO placeholders.
- Type consistency: all new API names are scoped under `TianZhang.Game.CharacterCreation`.
