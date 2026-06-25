# 场景架构与 2.5D 战棋表现 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把当前单一 `ExplorationScene` 原型拆成 StartMenu/World/Settlement/Adventure 四类通用场景，并为 2.5D 战棋表现建立只替换渲染层的可验证路径。

**Architecture:** 第一轮不强切 2.5D，也不重写 CTB 或伤害公式。先建立 `GameSession`/`SceneFlowManager`、`TacticalGridModel`/`ITacticalRenderer` 两条边界，再逐步把 `ExplorationController` 的探索、战斗、UI 职责拆开。DeepSeek V4 Pro 优先承担场景脚手架、数据定义、机械检查、低风险包装和回归清单；Codex / ChatGPT5.5 保留核心架构边界、战斗循环拆分和复审。

**Tech Stack:** Unity 6 `6000.3.18f1`，C#，Unity Tilemap，ScriptableObject，Editor `MenuItem` 场景生成，现有 `HexCoord`/`HexGrid`/`CTBEngine`/`CombatResolver`。

---

## Current Code Map

**Existing files that anchor the migration:**

- `src/Assets/Scripts/Editor/SceneBuilder.cs`: currently generates only `Assets/Scenes/ExplorationScene.unity`; this becomes the scripted scene-generation entry for all new scenes.
- `src/Assets/Scripts/Map/ExplorationController.cs`: currently owns terrain generation, player/enemy spawning, exploration input, CTB combat loop, drops, UI refresh and public battle actions.
- `src/Assets/Scripts/Tilemap/HexTilemapManager.cs`: currently owns Tilemap rendering, screen-to-hex picking, highlight overlays and unit marker placement.
- `src/Assets/Scripts/Game/GameManager.cs`: currently owns singleton lifetime and `PlayerCharData`; it should become a thin compatibility entry after `GameSession` exists.
- `src/Assets/Scripts/Game/SectSelectionManager.cs`: currently creates a `CharacterData`, stores it in `GameManager`, then searches `ExplorationController` after a delay to reconfigure the player.
- `src/Assets/Scripts/Game/BattleUIManager.cs`: currently creates `UICanvas` unconditionally and binds action buttons directly to `ExplorationController`.
- `src/Assets/Scenes/ExplorationScene.unity`: only existing Unity scene; keep it as a migration fallback until `AdventureScene` can replace it.

**New code families planned:**

- `src/Assets/Scripts/Game/GameSession.cs`
- `src/Assets/Scripts/Game/SceneFlowManager.cs`
- `src/Assets/Scripts/Game/SceneReturnTarget.cs`
- `src/Assets/Scripts/Grid/TacticalGridModel.cs`
- `src/Assets/Scripts/Grid/ITacticalRenderer.cs`
- `src/Assets/Scripts/Grid/TilemapTacticalRenderer.cs`
- `src/Assets/Scripts/World/WorldNodeDefinition.cs`
- `src/Assets/Scripts/World/WorldSceneController.cs`
- `src/Assets/Scripts/Settlement/SettlementDefinition.cs`
- `src/Assets/Scripts/Settlement/SettlementSceneController.cs`
- `src/Assets/Scripts/Adventure/AdventureDefinition.cs`
- `src/Assets/Scripts/Adventure/AdventureSceneController.cs`
- `src/Assets/Scripts/Combat/TacticalCombatController.cs`

## Ownership Split

**Codex / ChatGPT5.5 must own or review:**

- `TacticalGridModel` and renderer interface shape.
- `TacticalCombatController` extraction and CTB action consumption.
- Any change to `CombatResolver`, `CTBEngine`, damage formula, cooldown semantics or action threshold.
- The final decision to make `AdventureScene` replace `ExplorationScene`.
- All review conclusions and status changes from `⚠️ 已修改/未审核` to `✅ 已审核`.

**DeepSeek V4 Pro can execute under this plan:**

- SceneBuilder additions that generate empty or lightly wired StartMenu/World/Settlement/Adventure scenes.
- ScriptableObject-style definition classes and static sample data.
- `GameSession`/`SceneFlowManager` first pass when matching the exact public surface below.
- Wrapping `HexTilemapManager` behind `TilemapTacticalRenderer` without deleting existing methods.
- World/Settlement UI prototypes that use the existing `UICanvas` root.
- Documentation, checklists, `rg` evidence tables and mechanical build/data-chain checks.

**DeepSeek V4 Pro must not do without Codex review first:**

- Delete `ExplorationScene.unity`, rename GUID-bearing assets, or remove old public APIs.
- Rewrite `ExplorationController.CombatLoop`.
- Change CTB, damage, five-elements, cooldown, slot or cultivation semantics.
- Mark any new work as reviewed or audited.

## Implementation Sequence

### Task 1: DeepSeek Scene Inventory And Build Baseline

**Owner:** DeepSeek V4 Pro

**Files:**
- Create: `开发管理/场景架构Unity现状清单.txt`
- Read: `src/Assets/Scripts/Editor/SceneBuilder.cs`
- Read: `src/Assets/Scripts/Map/ExplorationController.cs`
- Read: `src/Assets/Scripts/Tilemap/HexTilemapManager.cs`
- Read: `src/Assets/Scripts/Game/BattleUIManager.cs`
- Read: `src/Assets/Scripts/Game/SectSelectionManager.cs`

- [ ] **Step 1: Record current scene and script facts**

Write `开发管理/场景架构Unity现状清单.txt` with this structure:

```text
# 场景架构 Unity 现状清单（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）

## 场景资产

- `src/Assets/Scenes/ExplorationScene.unity`: 当前唯一运行场景。

## 现有脚本职责

| 文件 | 当前职责 | 后续归属 | 风险 |
|------|----------|----------|------|
| `src/Assets/Scripts/Editor/SceneBuilder.cs` | 生成 ExplorationScene、Demo 数据、Tilemap、UIManager、GameManager | 扩展为 StartMenu/World/Settlement/Adventure 场景生成入口 | 不能手写 .unity YAML |
| `src/Assets/Scripts/Map/ExplorationController.cs` | 地图生成、敌人生成、探索输入、战斗触发、CTB 循环、掉落、UI 刷新 | 拆给 AdventureSceneController + TacticalCombatController | 不可同轮改 CTB 语义 |
| `src/Assets/Scripts/Tilemap/HexTilemapManager.cs` | Tilemap 渲染、点击拾取、高亮、单位标记 | 包装到 TilemapTacticalRenderer | 规则层不能读取 Transform |
| `src/Assets/Scripts/Game/BattleUIManager.cs` | 动态创建 UICanvas、战斗面板、动作栏、日志 | 短期保留，后续只作为 BattleUI | 当前无条件创建 UICanvas，需防重复 |
| `src/Assets/Scripts/Game/SectSelectionManager.cs` | 门派选择后延迟查找 ExplorationController 重配玩家 | 迁到 StartMenu + GameSession.PlayerProfile | 当前强耦合 Adventure 原型 |

## 首轮不改项

- 不改 `CTBEngine`。
- 不改 `CombatResolver`。
- 不改 `DamageCalculator`。
- 不改 `Character.FromData` 槽位规则。
- 不删除 `ExplorationScene.unity`。
```

- [ ] **Step 2: Run build baseline**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

If it fails because Unity-generated csproj references are stale, record the exact error in the清单 and stop this task without editing C#.

- [ ] **Step 3: Commit only the inventory**

Run:

```powershell
git add 开发管理/场景架构Unity现状清单.txt
git commit -m "docs: DeepSeek梳理场景架构现状（待Codex复审）"
```

### Task 2: Codex Tactical Grid Contract

**Owner:** Codex / ChatGPT5.5

**Files:**
- Create: `src/Assets/Scripts/Grid/TacticalGridModel.cs`
- Create: `src/Assets/Scripts/Grid/ITacticalRenderer.cs`
- Modify: `src/Assets/Scripts/Tilemap/HexTilemapManager.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add `TacticalTile` and `TacticalGridModel`**

Create `src/Assets/Scripts/Grid/TacticalGridModel.cs`:

```csharp
using System.Collections.Generic;
using TianZhang.Core;

namespace TianZhang.Grid
{
    public enum TacticalTerrainType
    {
        Plain,
        Blocked,
        Water,
        HighGround
    }

    public struct TacticalTile
    {
        public HexCoord Coord;
        public TacticalTerrainType TerrainType;
        public int HeightLevel;
        public bool BlocksGroundMove;
        public bool BlocksFlyingMove;
        public bool BlocksLineOfSight;
        public bool BlocksLanding;
        public int OccupiedUnitId;
    }

    public class TacticalGridModel
    {
        private readonly Dictionary<HexCoord, TacticalTile> tiles = new Dictionary<HexCoord, TacticalTile>();

        public IEnumerable<TacticalTile> Tiles => tiles.Values;

        public void SetTile(TacticalTile tile)
        {
            tiles[tile.Coord] = tile;
        }

        public bool TryGetTile(HexCoord coord, out TacticalTile tile)
        {
            return tiles.TryGetValue(coord, out tile);
        }

        public bool IsGroundBlocked(HexCoord coord)
        {
            return tiles.TryGetValue(coord, out var tile) && tile.BlocksGroundMove;
        }

        public bool IsOccupied(HexCoord coord)
        {
            return tiles.TryGetValue(coord, out var tile) && tile.OccupiedUnitId >= 0;
        }
    }
}
```

- [ ] **Step 2: Add renderer contract**

Create `src/Assets/Scripts/Grid/ITacticalRenderer.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using TianZhang.Core;

namespace TianZhang.Grid
{
    public interface ITacticalRenderer
    {
        void RenderGrid(TacticalGridModel model);
        HexCoord ScreenToHex(Vector3 screenPosition);
        Vector3 HexToWorld(HexCoord coord);
        void HighlightMoveRange(IReadOnlyList<HexCoord> tiles);
        void HighlightAttackRange(IReadOnlyList<HexCoord> tiles);
        void ClearOverlay();
        GameObject PlaceUnitMarker(HexCoord coord, Color color, string label);
    }
}
```

- [ ] **Step 3: Keep `HexTilemapManager` behavior stable**

Do not delete any public method in `HexTilemapManager`. Add only compatibility methods if needed by later tasks.

- [ ] **Step 4: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 5: Commit**

```powershell
git add src/Assets/Scripts/Grid/TacticalGridModel.cs src/Assets/Scripts/Grid/ITacticalRenderer.cs src/Assets/Scripts/Tilemap/HexTilemapManager.cs
git commit -m "feat: add tactical grid renderer contract"
```

### Task 3: DeepSeek Tilemap Renderer Wrapper

**Owner:** DeepSeek V4 Pro

**Files:**
- Create: `src/Assets/Scripts/Grid/TilemapTacticalRenderer.cs`
- Modify: `src/Assets/Scripts/Editor/SceneBuilder.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add wrapper without changing behavior**

Create `src/Assets/Scripts/Grid/TilemapTacticalRenderer.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;
using TianZhang.Core;
using TianZhang.HexTile;

namespace TianZhang.Grid
{
    public class TilemapTacticalRenderer : MonoBehaviour, ITacticalRenderer
    {
        public HexTilemapManager tilemapManager;

        public void RenderGrid(TacticalGridModel model)
        {
            if (tilemapManager != null)
                tilemapManager.GenerateHexGrid();
        }

        public HexCoord ScreenToHex(Vector3 screenPosition)
        {
            return tilemapManager != null ? tilemapManager.ScreenToHex(screenPosition) : new HexCoord(0, 0);
        }

        public Vector3 HexToWorld(HexCoord coord)
        {
            return tilemapManager != null ? tilemapManager.HexToWorld(coord) : Vector3.zero;
        }

        public void HighlightMoveRange(IReadOnlyList<HexCoord> tiles)
        {
            if (tilemapManager != null)
                tilemapManager.HighlightMoveRange(new List<HexCoord>(tiles));
        }

        public void HighlightAttackRange(IReadOnlyList<HexCoord> tiles)
        {
            if (tilemapManager != null)
                tilemapManager.HighlightAttackRange(new List<HexCoord>(tiles));
        }

        public void ClearOverlay()
        {
            if (tilemapManager != null)
                tilemapManager.ClearOverlay();
        }

        public GameObject PlaceUnitMarker(HexCoord coord, Color color, string label)
        {
            return tilemapManager != null ? tilemapManager.PlaceUnitMarker(coord, color, label) : null;
        }
    }
}
```

- [ ] **Step 2: Wire wrapper in generated ExplorationScene**

In `SceneBuilder.BuildExplorationScene`, after creating `HexTilemapManager`, add:

```csharp
var renderer = mgrGo.AddComponent<TianZhang.Grid.TilemapTacticalRenderer>();
renderer.tilemapManager = mgr;
```

- [ ] **Step 3: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 4: Commit**

```powershell
git add src/Assets/Scripts/Grid/TilemapTacticalRenderer.cs src/Assets/Scripts/Editor/SceneBuilder.cs
git commit -m "feat: DeepSeek包装Tilemap战棋渲染器（待Codex复审）"
```

### Task 4: DeepSeek Session And Scene Flow First Pass

**Owner:** DeepSeek V4 Pro

**Files:**
- Create: `src/Assets/Scripts/Game/SceneReturnTarget.cs`
- Create: `src/Assets/Scripts/Game/GameSession.cs`
- Create: `src/Assets/Scripts/Game/SceneFlowManager.cs`
- Modify: `src/Assets/Scripts/Game/GameManager.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add return target**

Create `src/Assets/Scripts/Game/SceneReturnTarget.cs`:

```csharp
namespace TianZhang.Game
{
    [System.Serializable]
    public struct SceneReturnTarget
    {
        public string SceneName;
        public string WorldNodeId;
        public string SettlementId;
        public string AdventureId;

        public static SceneReturnTarget World(string nodeId)
        {
            return new SceneReturnTarget { SceneName = "WorldScene", WorldNodeId = nodeId };
        }

        public static SceneReturnTarget Settlement(string settlementId)
        {
            return new SceneReturnTarget { SceneName = "SettlementScene", SettlementId = settlementId };
        }
    }
}
```

- [ ] **Step 2: Add session singleton**

Create `src/Assets/Scripts/Game/GameSession.cs`:

```csharp
using UnityEngine;
using TianZhang.Entity;

namespace TianZhang.Game
{
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        public CharacterData PlayerProfile { get; private set; }
        public string CurrentWorldNodeId { get; private set; } = "jiangzuo_hub";
        public SceneReturnTarget LastReturnTarget { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void SetPlayerProfile(CharacterData profile)
        {
            PlayerProfile = profile;
        }

        public void SetWorldNode(string nodeId)
        {
            CurrentWorldNodeId = string.IsNullOrEmpty(nodeId) ? "jiangzuo_hub" : nodeId;
        }

        public void SetReturnTarget(SceneReturnTarget target)
        {
            LastReturnTarget = target;
        }
    }
}
```

- [ ] **Step 3: Add scene flow manager**

Create `src/Assets/Scripts/Game/SceneFlowManager.cs`:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using TianZhang.Entity;

namespace TianZhang.Game
{
    public class SceneFlowManager : MonoBehaviour
    {
        public static SceneFlowManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            EnsureSession();
        }

        public void StartNewGame(CharacterData profile)
        {
            EnsureSession().SetPlayerProfile(profile);
            EnterWorld("jiangzuo_hub");
        }

        public void EnterWorld(string nodeId)
        {
            EnsureSession().SetWorldNode(nodeId);
            SceneManager.LoadScene("WorldScene");
        }

        public void EnterSettlement(string settlementId)
        {
            EnsureSession().SetReturnTarget(SceneReturnTarget.World(EnsureSession().CurrentWorldNodeId));
            SceneManager.LoadScene("SettlementScene");
        }

        public void EnterAdventure(string adventureId, SceneReturnTarget returnTarget)
        {
            EnsureSession().SetReturnTarget(returnTarget);
            SceneManager.LoadScene("AdventureScene");
        }

        public void ReturnToPreviousScene()
        {
            var target = EnsureSession().LastReturnTarget;
            if (target.SceneName == "SettlementScene")
                SceneManager.LoadScene("SettlementScene");
            else
                SceneManager.LoadScene("WorldScene");
        }

        private static GameSession EnsureSession()
        {
            if (GameSession.Instance != null)
                return GameSession.Instance;

            var go = new GameObject("GameSession");
            return go.AddComponent<GameSession>();
        }
    }
}
```

- [ ] **Step 4: Keep GameManager compatibility**

In `GameManager.StartGameWithSect`, after `PlayerCharData = charData;`, add:

```csharp
var flow = SceneFlowManager.Instance;
if (flow != null)
    flow.StartNewGame(charData);
else if (GameSession.Instance != null)
    GameSession.Instance.SetPlayerProfile(charData);
```

- [ ] **Step 5: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 6: Commit**

```powershell
git add src/Assets/Scripts/Game/SceneReturnTarget.cs src/Assets/Scripts/Game/GameSession.cs src/Assets/Scripts/Game/SceneFlowManager.cs src/Assets/Scripts/Game/GameManager.cs
git commit -m "feat: DeepSeek新增场景会话流转骨架（待Codex复审）"
```

### Task 5: DeepSeek Generate Four Scene Shells

**Owner:** DeepSeek V4 Pro

**Files:**
- Modify: `src/Assets/Scripts/Editor/SceneBuilder.cs`
- Create through Unity editor menu: `src/Assets/Scenes/StartMenuScene.unity`
- Create through Unity editor menu: `src/Assets/Scenes/WorldScene.unity`
- Create through Unity editor menu: `src/Assets/Scenes/SettlementScene.unity`
- Create through Unity editor menu: `src/Assets/Scenes/AdventureScene.unity`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add scene constants**

Add near the top of `SceneBuilder`:

```csharp
private const string StartMenuScenePath = "Assets/Scenes/StartMenuScene.unity";
private const string WorldScenePath = "Assets/Scenes/WorldScene.unity";
private const string SettlementScenePath = "Assets/Scenes/SettlementScene.unity";
private const string AdventureScenePath = "Assets/Scenes/AdventureScene.unity";
```

- [ ] **Step 2: Add shared camera helper**

Add to `SceneBuilder`:

```csharp
private static Camera CreateMainCamera(float orthographicSize, Color background)
{
    var camGo = new GameObject("Main Camera");
    camGo.tag = "MainCamera";
    var cam = camGo.AddComponent<Camera>();
    cam.orthographic = true;
    cam.orthographicSize = orthographicSize;
    cam.backgroundColor = background;
    cam.transform.position = new Vector3(0, 0, -10);
    camGo.AddComponent<AudioListener>();
    return cam;
}
```

- [ ] **Step 3: Add shell scene menu item**

Add to `SceneBuilder`:

```csharp
[MenuItem("Tools/天章/生成场景架构空场景")]
public static void BuildSceneArchitectureShells()
{
    BuildEmptyScene(StartMenuScenePath, "StartMenuRoot", new Color(0.05f, 0.05f, 0.08f));
    BuildEmptyScene(WorldScenePath, "WorldRoot", new Color(0.04f, 0.08f, 0.1f));
    BuildEmptyScene(SettlementScenePath, "SettlementRoot", new Color(0.08f, 0.07f, 0.05f));
    BuildEmptyScene(AdventureScenePath, "AdventureRoot", new Color(0.08f, 0.1f, 0.14f));
    AssetDatabase.Refresh();
}

private static void BuildEmptyScene(string scenePath, string rootName, Color background)
{
    var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
        UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
        UnityEditor.SceneManagement.NewSceneMode.Single);

    CreateMainCamera(12f, background);
    new GameObject(rootName);

    var eventSystem = new GameObject("EventSystem");
    eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
    eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

    var gameManager = new GameObject("GameManager");
    gameManager.AddComponent<TianZhang.Game.GameManager>();
    gameManager.AddComponent<TianZhang.Game.SceneFlowManager>();

    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath);
}
```

- [ ] **Step 4: Run Unity menu item**

Open Unity and run:

```text
Tools/天章/生成场景架构空场景
```

Expected files after Unity saves:

```text
src/Assets/Scenes/StartMenuScene.unity
src/Assets/Scenes/StartMenuScene.unity.meta
src/Assets/Scenes/WorldScene.unity
src/Assets/Scenes/WorldScene.unity.meta
src/Assets/Scenes/SettlementScene.unity
src/Assets/Scenes/SettlementScene.unity.meta
src/Assets/Scenes/AdventureScene.unity
src/Assets/Scenes/AdventureScene.unity.meta
```

- [ ] **Step 5: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 6: Commit**

```powershell
git add src/Assets/Scripts/Editor/SceneBuilder.cs src/Assets/Scenes/StartMenuScene.unity src/Assets/Scenes/StartMenuScene.unity.meta src/Assets/Scenes/WorldScene.unity src/Assets/Scenes/WorldScene.unity.meta src/Assets/Scenes/SettlementScene.unity src/Assets/Scenes/SettlementScene.unity.meta src/Assets/Scenes/AdventureScene.unity src/Assets/Scenes/AdventureScene.unity.meta
git commit -m "feat: DeepSeek生成场景架构空场景（待Codex复审）"
```

### Task 6: DeepSeek World And Settlement Data Shell

**Owner:** DeepSeek V4 Pro

**Files:**
- Create: `src/Assets/Scripts/World/WorldNodeDefinition.cs`
- Create: `src/Assets/Scripts/World/WorldSceneController.cs`
- Create: `src/Assets/Scripts/Settlement/SettlementDefinition.cs`
- Create: `src/Assets/Scripts/Settlement/SettlementSceneController.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add world node definition**

Create `src/Assets/Scripts/World/WorldNodeDefinition.cs`:

```csharp
namespace TianZhang.World
{
    public enum WorldNodeType
    {
        RegionHub,
        City,
        Sect,
        Market,
        DungeonEntrance,
        WildEncounter,
        SpecialLocation
    }

    [System.Serializable]
    public class WorldNodeDefinition
    {
        public string id;
        public string regionId;
        public string displayName;
        public WorldNodeType nodeType;
        public string[] connectedNodeIds;
        public string settlementId;
        public string[] adventureIds;
    }
}
```

- [ ] **Step 2: Add world controller with static sample nodes**

Create `src/Assets/Scripts/World/WorldSceneController.cs`:

```csharp
using UnityEngine;
using TianZhang.Game;

namespace TianZhang.World
{
    public class WorldSceneController : MonoBehaviour
    {
        private readonly WorldNodeDefinition[] nodes =
        {
            new WorldNodeDefinition { id = "jiangzuo_hub", regionId = "jiangzuo", displayName = "江左天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub" }, settlementId = "taiyi_sect" },
            new WorldNodeDefinition { id = "guanzhong_hub", regionId = "guanzhong", displayName = "关陇玄域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "jiangzuo_hub", "longxi_hub" }, settlementId = "guanzhong_city" },
            new WorldNodeDefinition { id = "longxi_hub", regionId = "longxi", displayName = "陇西雷域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "guanzhong_hub", "zhongzhou_hub" }, adventureIds = new[] { "longxi_trial" } },
            new WorldNodeDefinition { id = "zhongzhou_hub", regionId = "zhongzhou", displayName = "中州天域", nodeType = WorldNodeType.RegionHub, connectedNodeIds = new[] { "longxi_hub" }, settlementId = "zhongzhou_city" }
        };

        private void Start()
        {
            Debug.Log("[WorldScene] nodes=" + nodes.Length);
        }

        public void EnterSettlement(string settlementId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterSettlement(settlementId);
        }

        public void EnterAdventure(string adventureId)
        {
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterAdventure(adventureId, SceneReturnTarget.World(GameSession.Instance?.CurrentWorldNodeId));
        }
    }
}
```

- [ ] **Step 3: Add settlement definition**

Create `src/Assets/Scripts/Settlement/SettlementDefinition.cs`:

```csharp
namespace TianZhang.Settlement
{
    public enum SettlementType
    {
        City,
        Sect,
        Cave,
        Market,
        Special
    }

    [System.Serializable]
    public class SettlementDefinition
    {
        public string id;
        public string displayName;
        public SettlementType settlementType;
        public string regionId;
        public string ownerFactionId;
        public string[] availableServices;
        public string[] adventureEntrances;
        public string visualTheme;
    }
}
```

- [ ] **Step 4: Add settlement controller**

Create `src/Assets/Scripts/Settlement/SettlementSceneController.cs`:

```csharp
using UnityEngine;
using TianZhang.Game;

namespace TianZhang.Settlement
{
    public class SettlementSceneController : MonoBehaviour
    {
        private readonly SettlementDefinition[] definitions =
        {
            new SettlementDefinition { id = "taiyi_sect", displayName = "太一道庭", settlementType = SettlementType.Sect, regionId = "jiangzuo", ownerFactionId = "taiyi", availableServices = new[] { "修炼", "功法", "任务", "法坛" }, adventureEntrances = new[] { "taiyi_trial" }, visualTheme = "water_talisman" },
            new SettlementDefinition { id = "guanzhong_city", displayName = "关中城", settlementType = SettlementType.City, regionId = "guanzhong", ownerFactionId = "neutral", availableServices = new[] { "坊市", "悬赏", "客栈", "情报" }, adventureEntrances = new[] { "guanzhong_wild" }, visualTheme = "city_earth" },
            new SettlementDefinition { id = "zhongzhou_city", displayName = "中州城", settlementType = SettlementType.City, regionId = "zhongzhou", ownerFactionId = "neutral", availableServices = new[] { "坊市", "传送", "悬赏", "情报" }, adventureEntrances = new[] { "zhongzhou_wild" }, visualTheme = "capital" }
        };

        private void Start()
        {
            Debug.Log("[SettlementScene] definitions=" + definitions.Length);
        }

        public void ReturnToWorld()
        {
            var nodeId = GameSession.Instance != null ? GameSession.Instance.CurrentWorldNodeId : "jiangzuo_hub";
            if (SceneFlowManager.Instance != null)
                SceneFlowManager.Instance.EnterWorld(nodeId);
        }
    }
}
```

- [ ] **Step 5: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 6: Commit**

```powershell
git add src/Assets/Scripts/World src/Assets/Scripts/Settlement
git commit -m "feat: DeepSeek新增世界和地点数据骨架（待Codex复审）"
```

### Task 7: Codex Adventure State Boundary

**Owner:** Codex / ChatGPT5.5

**Files:**
- Create: `src/Assets/Scripts/Adventure/AdventureDefinition.cs`
- Create: `src/Assets/Scripts/Adventure/AdventureSceneController.cs`
- Modify: `src/Assets/Scripts/Map/ExplorationController.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Add adventure definition**

Create `src/Assets/Scripts/Adventure/AdventureDefinition.cs`:

```csharp
namespace TianZhang.Adventure
{
    public enum TacticalRendererMode
    {
        Tilemap2D,
        Hybrid2_5D
    }

    [System.Serializable]
    public class AdventureDefinition
    {
        public string id;
        public string displayName;
        public string sourceNodeId;
        public int mapRadius = 12;
        public int obstaclePercent = 15;
        public int enemyCount = 4;
        public string enemyPoolId;
        public string eventPoolId;
        public string rewardPoolId;
        public TacticalRendererMode rendererMode = TacticalRendererMode.Tilemap2D;
        public string cameraProfile = "orthographic_topdown";
    }
}
```

- [ ] **Step 2: Add adventure controller facade**

Create `src/Assets/Scripts/Adventure/AdventureSceneController.cs`:

```csharp
using UnityEngine;
using TianZhang.Map;

namespace TianZhang.Adventure
{
    public class AdventureSceneController : MonoBehaviour
    {
        public ExplorationController explorationController;
        public AdventureDefinition definition = new AdventureDefinition
        {
            id = "prototype_adventure",
            displayName = "原型副本",
            sourceNodeId = "jiangzuo_hub"
        };

        private void Awake()
        {
            if (explorationController == null)
                explorationController = FindObjectOfType<ExplorationController>();
        }

        private void Start()
        {
            if (explorationController == null)
            {
                Debug.LogWarning("[AdventureScene] ExplorationController missing");
                return;
            }

            explorationController.mapRadius = definition.mapRadius;
            explorationController.obstaclePercent = definition.obstaclePercent;
            explorationController.enemyCount = definition.enemyCount;
        }
    }
}
```

- [ ] **Step 3: Verify no behavior change**

Do not move `ExplorationController.StartBattle`, `CombatLoop`, `EndBattle`, or public action methods in this task.

- [ ] **Step 4: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 5: Commit**

```powershell
git add src/Assets/Scripts/Adventure
git commit -m "feat: add adventure scene state facade"
```

### Task 8: Codex Tactical Combat Extraction

**Owner:** Codex / ChatGPT5.5

**Files:**
- Create: `src/Assets/Scripts/Combat/TacticalCombatController.cs`
- Modify: `src/Assets/Scripts/Map/ExplorationController.cs`
- Modify: `src/Assets/Scripts/Game/BattleUIManager.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Extract command surface only**

Create `TacticalCombatController` first with a minimal public command surface. Keep CTB loop in `ExplorationController` until this compiles:

```csharp
using UnityEngine;

namespace TianZhang.Combat
{
    public class TacticalCombatController : MonoBehaviour
    {
        public System.Action BasicAttackRequested;
        public System.Action GuardRequested;
        public System.Action WaitRequested;
        public System.Action<int> SpellRequested;
        public System.Action<int> SkillRequested;

        public void RequestBasicAttack() => BasicAttackRequested?.Invoke();
        public void RequestGuard() => GuardRequested?.Invoke();
        public void RequestWait() => WaitRequested?.Invoke();
        public void RequestSpell(int index) => SpellRequested?.Invoke(index);
        public void RequestSkill(int index) => SkillRequested?.Invoke(index);
    }
}
```

- [ ] **Step 2: Add UI controller binding without removing old binding**

In `BattleUIManager`, add:

```csharp
private TianZhang.Combat.TacticalCombatController combatController;

public void SetTacticalCombatController(TianZhang.Combat.TacticalCombatController ctrl)
{
    combatController = ctrl;
}
```

Then update button listeners to prefer `combatController` and fall back to `exploreController`:

```csharp
attackButton.onClick.AddListener(() =>
{
    if (combatController != null) combatController.RequestBasicAttack();
    else if (exploreController != null) exploreController.PlayerBasicAttack();
});
```

Apply the same pattern for guard, wait, spell and skill buttons.

- [ ] **Step 3: Bind extracted command surface**

In `ExplorationController.InitExploration`, if a `TacticalCombatController` exists, bind its actions to existing public methods:

```csharp
var tacticalCombat = FindObjectOfType<TianZhang.Combat.TacticalCombatController>();
if (tacticalCombat != null)
{
    tacticalCombat.BasicAttackRequested = PlayerBasicAttack;
    tacticalCombat.GuardRequested = PlayerGuard;
    tacticalCombat.WaitRequested = PlayerCombatWait;
    tacticalCombat.SpellRequested = PlayerCastSpell;
    tacticalCombat.SkillRequested = PlayerUseSkill;
    uiManager?.SetTacticalCombatController(tacticalCombat);
}
```

- [ ] **Step 4: Compile before moving CTB loop**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 5: Move CTB loop only after command surface passes**

Move `StartBattle`, `CombatLoop`, `RefreshCombatButtons`, `ExecuteEnemyAI`, `HandleCombatKeyboardInput`, `PlayerBasicAttack`, `PlayerGuard`, `PlayerCombatWait`, `PlayerCastSpell`, `PlayerUseSkill`, `HandleDrop`, and `EndBattle` into `TacticalCombatController` only after Step 4 passes. Preserve these semantics:

- `ctbEngine.AdvanceUntilAction(activeUnits)` remains the action scheduler.
- `resolver.AdvanceCooldowns` receives the exact `ticksElapsed`.
- Player actions call `ctbEngine.ConsumeAction(player.CTBUnit)` only after successful action result.
- Enemy action calls `ctbEngine.ConsumeAction(enemy.character.CTBUnit)` after `ExecuteEnemyAI`.
- Combat end hides enemy panel and action bar, clears overlay, marks defeated enemy inactive and returns to exploration.

- [ ] **Step 6: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 7: Commit**

```powershell
git add src/Assets/Scripts/Combat/TacticalCombatController.cs src/Assets/Scripts/Map/ExplorationController.cs src/Assets/Scripts/Game/BattleUIManager.cs
git commit -m "refactor: extract tactical combat controller"
```

### Task 9: DeepSeek Start Menu Migration

**Owner:** DeepSeek V4 Pro after Task 4 passes

**Files:**
- Modify: `src/Assets/Scripts/Game/SectSelectionManager.cs`
- Modify: `src/Assets/Scripts/Editor/SceneBuilder.cs`
- Test: `dotnet build src/Assembly-CSharp.csproj`

- [ ] **Step 1: Route selected sect through SceneFlowManager**

In `SectSelectionManager.OnStartGame`, replace the delayed `ExplorationController` reconfiguration path with session startup when `SceneFlowManager` exists:

```csharp
if (SceneFlowManager.Instance != null)
{
    SceneFlowManager.Instance.StartNewGame(charData);
    return;
}
```

Keep the existing coroutine fallback below this block so `ExplorationScene` still works during migration.

- [ ] **Step 2: Add StartMenu shell generation**

In `SceneBuilder.BuildEmptyScene`, when `rootName == "StartMenuRoot"`, add a `SectSelectionManager` GameObject and a `BattleUIManager` or shared `UICanvas` only if the scene has no UI root.

- [ ] **Step 3: Verify compile**

Run:

```powershell
dotnet build src/Assembly-CSharp.csproj
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 4: Commit**

```powershell
git add src/Assets/Scripts/Game/SectSelectionManager.cs src/Assets/Scripts/Editor/SceneBuilder.cs
git commit -m "feat: DeepSeek迁移门派选择到会话流（待Codex复审）"
```

### Task 10: DeepSeek 2.5D Prototype Evidence Pack

**Owner:** DeepSeek V4 Pro

**Files:**
- Create: `开发管理/2.5D战棋原型验证清单.txt`
- Optional create after Codex approval: `src/Assets/Scripts/Grid/HybridTacticalRenderer.cs`

- [ ] **Step 1: Write evidence checklist**

Create `开发管理/2.5D战棋原型验证清单.txt`:

```text
# 2.5D 战棋原型验证清单（⚠️ 已修改/未审核；修改方：DeepSeek V4 Pro）

## 验证目标

- 同一份 `TacticalGridModel` 在 `Tilemap2D` 与 `Hybrid2_5D` 下得到相同 `HexCoord`。
- CTB、寻路、射程、技能范围不读取 Unity Transform。
- 鼠标点击命中地形块后只换算回 `HexCoord`。

## 原型范围

- 半径：5 到 7。
- 地形：Plain、Blocked、HighGround。
- 角色：2D sprite 或 billboard。
- 相机：正交斜俯视。
- 不接入正式剧情、不改正式副本、不替换 ExplorationScene。

## 必测项

1. 点击 10 个格子，输出的 `HexCoord` 与 Tilemap2D 同坐标一致。
2. 障碍格不可地面通行。
3. 飞行单位可越过 `BlocksGroundMove` 但不能落在 `BlocksLanding`。
4. 攻击范围显示与 `HexCoord.Distance` 一致。
5. 角色高度变化只影响表现，不影响 CTB 行动顺序。
6. UI 选中反馈不被地形块遮挡。
7. 切回 Tilemap2D 后无需改战斗规则代码。
```

- [ ] **Step 2: Run text check**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths 开发管理/2.5D战棋原型验证清单.txt
```

Expected:

```text
OK
```

- [ ] **Step 3: Commit**

```powershell
git add 开发管理/2.5D战棋原型验证清单.txt
git commit -m "docs: DeepSeek补充2.5D战棋验证清单（待Codex复审）"
```

## Verification Commands

Run these after each code task:

```powershell
dotnet build src/Assembly-CSharp.csproj
git diff --check
```

Run these after each docs or management-file task:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-review-text.ps1 -Paths <changed-doc-path>
git diff --check
```

Run this after CSV, importer or asset-chain changes; TQ-014 should not normally require it:

```powershell
powershell -ExecutionPolicy Bypass -File tools/check-data-chain.ps1
```

## Self-Review

**Spec coverage:** This plan covers the required `GameSession/SceneFlowManager`, `TacticalGridModel/ITacticalRenderer`, StartMenu/World/Settlement/Adventure scene split, `TacticalCombatController` extraction and the 2.5D prototype order. `CombatScene` remains outside first-round execution and is intentionally not a task.

**Placeholder scan:** The plan does not use open-ended implementation markers. Every code task has concrete file paths, starter code and verification commands.

**Type consistency:** Names introduced here use `TianZhang.Grid`, `TianZhang.World`, `TianZhang.Settlement`, `TianZhang.Adventure`, and existing `TianZhang.Game` / `TianZhang.Combat` namespaces. `SceneFlowManager`, `GameSession`, `SceneReturnTarget`, `TacticalGridModel` and `ITacticalRenderer` are defined before later tasks reference them.
