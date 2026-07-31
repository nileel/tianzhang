using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using TianZhang.Adventure;
using TianZhang.Core;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.HexTile;
using TianZhang.Tactical;

namespace TianZhang.Map
{
    /// <summary>
    /// 地图探索控制器 — 阶段二：最小可玩循环
    /// 职责：生成可探索地图 + 敌人生成 + 探索→战斗循环
    /// 操作：鼠标点击移动，接近敌人触发战斗
    /// </summary>
    public class ExplorationController : MonoBehaviour, ICombatCommandHandler
    {
        [Header("引用")]
        public HexTilemapManager tilemapManager;
        public Game.BattleUIManager uiManager;

        [Header("地图参数")]
        public int mapRadius = 12;
        public int obstaclePercent = 15;       // 障碍覆盖率(%)

        [Header("敌人配置")]
        public int enemyCount = 4;
        public CharacterData[] enemyTemplates; // 从 Enemies.csv 导入的敌人模板池

        [Header("术法/神通")]
        public AttackProfileData[] playerSpells;
        public AttackProfileData[] playerSkills;
        [Tooltip("当前遭遇可解析的统一攻击档案；由导入器生成的 asset 在此作为只读输入。")]
        public AttackProfileData[] attackProfiles;

        [Header("视野")]
        public int playerSightRange = 8;       // 玩家视野范围(格)

        // ---- 核心系统 ----
        private CTBEngine ctbEngine;
        private CombatResolver resolver;
        private TacticalCombatController tacticalCombatController;
        private CombatLogAdapter combatLogAdapter;
        private AdventureSceneController adventureSceneController;
        private EnvironmentProfileData environmentProfile;
        private EnemyData formalEncounterEnemy;
        private IAIController formalEncounterAiController;
        private SpatialQuerySnapshot spatialQuerySnapshot;
        private Character player;
        private List<EnemyUnit> enemies = new List<EnemyUnit>();

        // ---- 视觉标记 ----
        private GameObject playerMarker;
        private Dictionary<int, GameObject> enemyMarkers = new Dictionary<int, GameObject>();

        // ---- 状态机 ----
        public enum GameState { Loading, Exploration, BattlePrep, Combat, Ended }
        private GameState state = GameState.Loading;
        private Character currentCombatTarget;

        // ---- 玩家移动 ----
        private bool waitingForMoveInput;
        private bool hasMovedThisTurn;
        private bool waitingForPlayerCombatAction;
        private int hexesMovedThisTurn;

        // ---- CT消耗常量 ----
        public const float CtPerMoveHex = 25f;
        public const float CtPerAction = 100f;
        public const float CtPerGuard = 60f;

        // ---- 阵营标记 ----
        private int nextUnitId = 0;

        // ---- 地形标记（HexGrid blocked）----
        private HashSet<HexCoord> blockedTiles = new HashSet<HexCoord>();
        private UnityEngine.Tilemaps.Tile blockedTile; // 障碍格素材

        private class EnemyUnit
        {
            public Character character;
            public CharacterData data;
            public EnemyData enemyData;
            public IAIController aiController;
            public AttackProfileData[] spells;
            public AttackProfileData[] skills;
            public GameObject marker;
            public bool defeated;
        }

        private void Start()
        {
            StartCoroutine(InitExploration());
        }

        public void ConfigureEnvironmentProfile(EnvironmentProfileData profile)
        {
            environmentProfile = profile;
        }

        public void ConfigureFormalEncounter(
            EnemyData enemy,
            IAIController aiController)
        {
            formalEncounterEnemy = enemy;
            formalEncounterAiController = aiController;
        }

        public void ClearFormalEncounter()
        {
            formalEncounterEnemy = null;
            formalEncounterAiController = null;
        }

        private void Update()
        {
            if (state == GameState.Exploration && waitingForMoveInput)
            {
                HandleKeyboardInput();
                HandleMouseClick();
            }
            else if (state == GameState.Combat)
            {
                if (waitingForPlayerCombatAction)
                    HandleCombatKeyboardInput();
            }
        }

        // ==================== 初始化 ====================

        private IEnumerator InitExploration()
        {
            state = GameState.Loading;
            adventureSceneController = FindFirstObjectByType<AdventureSceneController>();
            if (adventureSceneController != null &&
                adventureSceneController.RequiresFormalEncounter &&
                (formalEncounterEnemy == null || formalEncounterAiController == null))
            {
                adventureSceneController.ReportEncounterConfigurationFailure(
                    "formal_encounter_enemy_not_configured");
                enabled = false;
                yield break;
            }

            if (environmentProfile == null)
            {
                adventureSceneController?.ReportEncounterConfigurationFailure(
                    "正式探索控制器未收到环境档案，已阻止遭遇启动。");
                enabled = false;
                yield break;
            }

            ctbEngine = new CTBEngine();
            resolver = new CombatResolver { Engine = ctbEngine };
            tacticalCombatController = new TacticalCombatController(ctbEngine, resolver);
            combatLogAdapter = new CombatLogAdapter(AddLog, SetStatus);

            // 创建障碍格素材（深灰色）
            blockedTile = CreateColoredTile("BlockedTile", new Color(0.25f, 0.22f, 0.2f, 1f));
            // 生成地图（地形 + 六角格）
            GenerateTerrain();
            tilemapManager.GenerateHexGrid();
            ApplyBlockedTiles();

            // 创建玩家
            var playerStart = new HexCoord(0, 0);
            player = CreatePlayer(playerStart);
            player.CTBUnit = ctbEngine.RegisterUnit(player.Reaction, player);
            player.CTBUnit.Id = nextUnitId++;
            tilemapManager.Grid.SetOccupied(playerStart, player.CTBUnit.Id);

            playerMarker = tilemapManager.PlaceUnitMarker(playerStart, Color.cyan, "玩家");

            // 生成敌人
            if (!SpawnEnemies(out string spawnReason))
            {
                adventureSceneController?.ReportEncounterConfigurationFailure(spawnReason);
                enabled = false;
                yield break;
            }

            var tacticalGrid = TacticalGridModel.FromHexGrid(
                tilemapManager.allHexCoords,
                tilemapManager.Grid);
            if (!SpatialQueryBoardFactory.TryCreate(
                tacticalGrid,
                environmentProfile,
                out spatialQuerySnapshot,
                out string spatialReason))
            {
                adventureSceneController?.ReportEncounterConfigurationFailure(
                    "正式空间查询快照创建失败: " + spatialReason);
                enabled = false;
                yield break;
            }
            resolver.SpatialBoard = spatialQuerySnapshot.Board;

            var environmentPresentation = tilemapManager.PresentEnvironment(tacticalGrid);
            if (!environmentPresentation.IsConfigured)
            {
                adventureSceneController?.ReportEncounterConfigurationFailure(
                    "正式环境表现创建失败: " + environmentPresentation.FailureReason);
                enabled = false;
                yield break;
            }
            adventureSceneController?.SetEnvironmentPresentation(environmentPresentation);

            // 刷新UI
            if (uiManager != null)
            {
                uiManager.SetCombatCommandHandler(this);
                uiManager.SetTurnBanner("探索地图");
                uiManager.ClearLog();
            }
            RefreshUI();

            state = GameState.Exploration;
            waitingForMoveInput = true;
            adventureSceneController?.MarkExplorationReady();

            Debug.Log($"探索地图已生成: {tilemapManager.allHexCoords?.Count ?? 0} 格, {enemies.Count} 个敌人");
            yield break;
        }

        // ==================== 地形生成 ====================


        private void ApplyBlockedTiles()
        {
            if (blockedTile == null || tilemapManager?.groundTilemap == null) return;
            foreach (var coord in blockedTiles)
            {
                var cell = new Vector3Int(coord.q, coord.r, 0);
                tilemapManager.groundTilemap.SetTile(cell, blockedTile);
            }
        }

        private UnityEngine.Tilemaps.Tile CreateColoredTile(string name, Color color)
        {
            int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            float r = s / 2f - 1f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    px[y * s + x] = ((x - s / 2f) * (x - s / 2f) + (y - s / 2f) * (y - s / 2f) <= r * r)
                        ? Color.white : Color.clear;
            tex.SetPixels(px);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();

            var tile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            tile.name = name;
            tile.sprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            tile.color = color;
            return tile;
        }
        private void GenerateTerrain()
        {
            var rng = new System.Random(42); // 固定种子，可重复
            blockedTiles.Clear();

            // 扩大 tilemapManager 的半径
            tilemapManager.gridRadius = mapRadius;

            // 预计算所有坐标，随机放置障碍
            for (int q = -mapRadius; q <= mapRadius; q++)
            {
                for (int r = Mathf.Max(-mapRadius, -q - mapRadius);
                         r <= Mathf.Min(mapRadius, -q + mapRadius); r++)
                {
                    var coord = new HexCoord(q, r);

                    // 边缘6格强制留空（地图边界）
                    int distFromCenter = coord.Distance(new HexCoord(0, 0));
                    if (distFromCenter >= mapRadius - 1)
                    {
                        blockedTiles.Add(coord);
                        tilemapManager.Grid.SetBlocked(coord, true);
                        continue;
                    }

                    // 玩家起点周围清空
                    if (coord.Distance(new HexCoord(0, 0)) <= 1) continue;

                    // 按概率放置障碍
                    if (rng.Next(100) < obstaclePercent)
                    {
                        blockedTiles.Add(coord);
                        tilemapManager.Grid.SetBlocked(coord, true);
                    }
                }
            }

            Debug.Log($"地形: {blockedTiles.Count} 个障碍格");
        }

        // ==================== 玩家创建 ====================

        private Character CreatePlayer(HexCoord startPos)
        {
            var cd = Game.GameSession.Instance != null && Game.GameSession.Instance.PlayerProfile != null
                ? Game.GameSession.Instance.PlayerProfile
                : CreateDefaultPlayerData();

            var c = Character.FromData(cd, startPos);
            c.CombatSwapsUsed = 0;
            c.EnsureCooldownArraySize();
            return c;
        }

        private CharacterData CreateDefaultPlayerData()
        {
            var cd = ScriptableObject.CreateInstance<CharacterData>();
            cd.charName = "太一修士";
            cd.rootBone = 14; cd.physique = 14; cd.spirit = 14; cd.mind = 14; cd.reaction = 14; cd.talent = 14;
            cd.blockRate = 10; cd.soulShieldRate = 13; cd.critRate = 5; cd.critDamage = 15;
            cd.realmMultiplier = 1.5f;
            cd.gongFaName = "抱元守一经"; // 默认太一道庭功法（对应五行=水）
            cd.equippedSpells = playerSpells != null
                ? System.Array.ConvertAll(playerSpells, s => s?.attackProfileId ?? "")
                : new string[0];
            cd.equippedSkills = playerSkills != null
                ? System.Array.ConvertAll(playerSkills, s => s?.attackProfileId ?? "")
                : new string[0];
            cd.availableSpells = cd.equippedSpells;
            return cd;
        }

        // ==================== 敌人生成 ====================

        private bool SpawnEnemies(out string reason)
        {
            bool isFormalEncounter = formalEncounterEnemy != null;
            if (isFormalEncounter &&
                (formalEncounterEnemy.combatTemplate == null ||
                 formalEncounterAiController == null ||
                 enemyCount != 1))
            {
                reason = "formal_encounter_spawn_configuration_invalid";
                return false;
            }

            var rng = new System.Random(123);
            int spawned = 0;
            int maxAttempts = 500;
            int minimumSpawnDistance = Mathf.Min(5, mapRadius - 2);

            for (int attempt = 0; attempt < maxAttempts && spawned < enemyCount; attempt++)
            {
                int q = rng.Next(-mapRadius + 2, mapRadius - 1);
                int r = rng.Next(-mapRadius + 2, mapRadius - 1);
                var coord = new HexCoord(q, r);
                int s = -q - r;
                if (s < -mapRadius + 2 || s > mapRadius - 2) continue;

                // 不与玩家重叠，不在障碍上，不与其他敌人重叠
                if (coord.Distance(player.Position) < minimumSpawnDistance) continue;
                if (blockedTiles.Contains(coord)) continue;
                if (tilemapManager.Grid.IsOccupied(coord)) continue;

                // 选择一个敌人模板
                CharacterData template = null;
                EnemyData enemyData = null;
                IAIController aiController = tacticalCombatController.AIController;
                if (isFormalEncounter)
                {
                    enemyData = formalEncounterEnemy;
                    template = formalEncounterEnemy.combatTemplate;
                    aiController = formalEncounterAiController;
                }
                else if (enemyTemplates != null && enemyTemplates.Length > 0)
                    template = enemyTemplates[spawned % enemyTemplates.Length];
                else
                    template = CreateFallbackEnemy(spawned);

                var enemy = Character.FromData(template, coord);
                enemy.EnsureCooldownArraySize();
                enemy.CTBUnit = ctbEngine.RegisterUnit(enemy.Reaction, enemy);
                enemy.CTBUnit.Id = nextUnitId++;
                tilemapManager.Grid.SetOccupied(coord, enemy.CTBUnit.Id);

                var spells = ResolveAttackProfiles(template.equippedSpells, AttackProfileKind.Art);
                var skills = ResolveAttackProfiles(template.equippedSkills, AttackProfileKind.Divine);

                var marker = tilemapManager.PlaceUnitMarker(coord, Color.red, template.charName);
                marker.transform.localScale = Vector3.one * 0.6f;

                enemies.Add(new EnemyUnit
                {
                    character = enemy,
                    data = template,
                    enemyData = enemyData,
                    aiController = aiController,
                    spells = spells,
                    skills = skills,
                    marker = marker,
                    defeated = false,
                });

                spawned++;
            }

            Debug.Log($"敌人生成: {spawned}/{enemyCount}");
            if (isFormalEncounter && spawned != 1)
            {
                reason = "formal_encounter_spawn_failed";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private CharacterData CreateFallbackEnemy(int index)
        {
            var data = ScriptableObject.CreateInstance<CharacterData>();
            string[] names = { "石甲兽", "风隼", "焰尾狐", "荒野散修" };
            int[] rootBones = { 18, 7, 8, 12 };
            int[] physiques = { 16, 5, 6, 12 };
            int[] spirits = { 6, 13, 16, 10 };
            int[] minds = { 6, 8, 12, 10 };
            int[] reactions = { 6, 20, 14, 10 };
            float[] multis = { 1.2f, 1.4f, 1.3f, 1.5f };

            int i = index % names.Length;
            data.charName = names[i];
            data.rootBone = rootBones[i]; data.physique = physiques[i];
            data.spirit = spirits[i]; data.mind = minds[i];
            data.reaction = reactions[i]; data.talent = 8;
            data.realmMultiplier = multis[i];
            data.blockRate = i == 0 ? 15 : 5;
            data.soulShieldRate = i == 2 ? 8 : 0;
            data.dodgeRate = i == 1 ? 18 : 5;
            data.critRate = 5; data.critDamage = i == 2 ? 15 : 10;
            // 五行属性：石甲兽(土)、风隼(金)、焰尾狐(火)、荒野散修(水)
            string[] gongfas = { "含弘光大典", "疾雷破山经", "南华玄感录", "秋水游心经" };
            data.gongFaName = gongfas[i];
            return data;
        }

        private AttackProfileData[] ResolveAttackProfiles(string[] profileIds, AttackProfileKind expectedKind)
        {
            if (profileIds == null || profileIds.Length == 0)
                return new AttackProfileData[0];

            var result = new List<AttackProfileData>(profileIds.Length);
            foreach (string profileId in profileIds)
            {
                var profile = attackProfiles == null
                    ? null
                    : System.Array.Find(
                        attackProfiles,
                        candidate => candidate != null && candidate.attackProfileId == profileId);
                if (profile == null || profile.profileKind != expectedKind)
                {
                    Debug.LogWarning($"[Exploration] 攻击档案未解析或种类不符: {profileId}");
                    continue;
                }
                result.Add(profile);
            }
            return result.ToArray();
        }

        private bool TryRefreshEncounterSnapshot(out string reason)
        {
            var tacticalGrid = TacticalGridModel.FromHexGrid(
                tilemapManager.allHexCoords,
                tilemapManager.Grid);
            if (!SpatialQueryBoardFactory.TryCreate(
                    tacticalGrid,
                    environmentProfile,
                    out spatialQuerySnapshot,
                    out reason))
            {
                return false;
            }

            resolver.SpatialBoard = spatialQuerySnapshot.Board;
            return true;
        }

        private AttackProfileData[] GetKnownAttackProfiles()
        {
            var result = new List<AttackProfileData>();
            void AddRange(AttackProfileData[] profiles)
            {
                if (profiles == null) return;
                foreach (var profile in profiles)
                {
                    if (profile != null && !result.Contains(profile))
                        result.Add(profile);
                }
            }

            AddRange(attackProfiles);
            AddRange(playerSpells);
            AddRange(playerSkills);
            foreach (var enemy in enemies)
            {
                AddRange(enemy.spells);
                AddRange(enemy.skills);
            }
            return result.ToArray();
        }

        // ==================== 探索输入 ====================

        private void HandleKeyboardInput()
        {
            if (Keyboard.current != null && Keyboard.current.wKey.wasPressedThisFrame)
                PlayerWait();
            else if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                PlayerEndExplorationTurn();
        }

        private void HandleMouseClick()
        {
            if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;
            if (uiManager != null && uiManager.IsPointerOverUI()) return;
            if (Camera.main == null || tilemapManager == null) return;

            var coord = tilemapManager.ScreenToHex((Vector3)Mouse.current.position.ReadValue());

            // 检查是否点击敌人（战斗触发）
            foreach (var eu in enemies)
            {
                if (eu.defeated) continue;
                if (eu.character.Position == coord)
                {
                    // 检查玩家是否在相邻格
                    if (resolver.CanTarget(player.Position, coord, 1, 1, out _))
                    {
                        StartBattle(eu);
                        return;
                    }
                    // 太远，先移动到相邻位置
                    var adjacent = FindAdjacentFreeTile(coord);
                    if (adjacent.HasValue)
                    {
                        var path = tilemapManager.Grid.FindPath(player.Position, adjacent.Value, player.MovePoints);
                        if (path != null && path.Count > 0)
                        {
                            MovePlayer(path);
                            // 移动后延迟一帧再开战（避免瞬移感）
                            StartCoroutine(DelayedStartBattle(eu));
                        }
                    }
                    return;
                }
            }

            // 普通移动
            var movePath = tilemapManager.Grid.FindPath(player.Position, coord, player.MovePoints);
            if (movePath != null && movePath.Count > 0)
            {
                MovePlayer(movePath);
            }
            else
            {
                // 点击不可达格子时给出反馈
                if (tilemapManager.Grid.IsBlocked(coord))
                    AddLog("此处是障碍，无法通行");
                else if (player.Position.Distance(coord) > player.MovePoints)
                    AddLog("距离太远，走不到");
            }
        }

        private void MovePlayer(List<HexCoord> path)
        {
            if (path == null || path.Count == 0) return;

            // 只更新起点和终点，不污染中间格子
            tilemapManager.Grid.ClearOccupied(player.Position);
            player.Position = path[path.Count - 1];
            tilemapManager.Grid.SetOccupied(player.Position, player.CTBUnit.Id);

            // 更新标记位置
            if (playerMarker != null)
                playerMarker.transform.position = tilemapManager.HexToWorld(player.Position);

            hasMovedThisTurn = true;
            hexesMovedThisTurn += path.Count;

            // 检查视野内敌人
            CheckEnemyProximity();

            RefreshUI();
        }

        private HexCoord? FindAdjacentFreeTile(HexCoord target)
        {
            foreach (var dir in HexCoord.Directions)
            {
                var adj = target + dir;
                if (!tilemapManager.Grid.IsBlocked(adj)
                    && !tilemapManager.Grid.IsOccupied(adj)
                    && adj.Distance(player.Position) <= player.MovePoints)
                {
                    return adj;
                }
            }
            return null;
        }

        private void CheckEnemyProximity()
        {
            foreach (var eu in enemies)
            {
                if (eu.defeated) continue;
                int dist = player.Position.Distance(eu.character.Position);
                if (dist <= 1 && !eu.defeated)
                {
                    AddLog($"发现敌人: {eu.character.Name}！点击敌人发起攻击");
                }
                else if (dist <= 3)
                {
                    AddLog($"前方有 {eu.character.Name} 出没（距离 {dist} 格）");
                }
            }
        }

        public void PlayerWait()
        {
            if (!waitingForMoveInput) return;
            AddLog("待机观察...");
            hasMovedThisTurn = true;
            CheckEnemyProximity();
            RefreshUI();
        }

        public void PlayerEndExplorationTurn()
        {
            if (!waitingForMoveInput) return;
            AddLog("结束探索回合");
            hasMovedThisTurn = true;
            RefreshUI();
        }

        private System.Collections.IEnumerator DelayedStartBattle(EnemyUnit eu)
        {
            // 等待一帧让移动动画可见
            yield return null;
            if (state == GameState.Exploration && !eu.defeated)
                StartBattle(eu);
        }

        // ==================== 战斗 ====================

        private void StartBattle(EnemyUnit enemy)
        {
            if (enemy.defeated) return;
            if (adventureSceneController != null &&
                adventureSceneController.RequiresFormalEncounter &&
                (!ReferenceEquals(enemy.enemyData, formalEncounterEnemy) ||
                 enemy.aiController == null))
            {
                adventureSceneController.ReportEncounterConfigurationFailure(
                    "formal_encounter_runtime_identity_invalid");
                return;
            }

            state = GameState.BattlePrep;
            waitingForMoveInput = false;
            currentCombatTarget = enemy.character;
            if (!TryRefreshEncounterSnapshot(out string snapshotReason))
            {
                AddLog("遭遇空间快照创建失败: " + snapshotReason);
                adventureSceneController?.ReportEncounterConfigurationFailure(snapshotReason);
                state = GameState.Exploration;
                return;
            }

            var setup = new TacticalCombatSetup(
                new[] { player },
                new[] { enemy.character },
                spatialQuerySnapshot.Board,
                spatialQuerySnapshot.UnitAnchors,
                GetKnownAttackProfiles());
            if (!tacticalCombatController.TryBeginCombat(
                    setup,
                    tilemapManager.Grid,
                    out _,
                    out string setupReason))
            {
                AddLog("遭遇配置被拒绝: " + setupReason);
                adventureSceneController?.ReportEncounterConfigurationFailure(setupReason);
                state = GameState.Exploration;
                return;
            }

            adventureSceneController?.BeginEncounter();

            GetCombatLogAdapter().AnnounceBattleStart(player.Name, enemy.character.Name);

            // 显示战斗UI
            if (uiManager != null)
            {
                uiManager.ShowEnemyPanel(true);
                uiManager.SetActionBarVisible(true);
            }

            // 重置冷却（防止跨战斗残留，使用槽位数确保长度一致）
            player.EnsureCooldownArraySize();
            System.Array.Clear(player.SpellCooldowns, 0, player.SpellCooldowns.Length);
            System.Array.Clear(player.SkillCooldowns, 0, player.SkillCooldowns.Length);

            if (uiManager != null)
            {
                RefreshCombatButtons(false);
            }

            state = GameState.Combat;
            StartCoroutine(CombatLoop(enemy));
        }

        private IEnumerator CombatLoop(EnemyUnit enemyUnit)
        {
            waitingForPlayerCombatAction = false;
            uiManager?.SetActionButtonsInteractable(false);

            while (state == GameState.Combat && player.IsAlive && enemyUnit.character.IsAlive)
            {
                var nextAction = tacticalCombatController.AdvanceUntilAction();
                var nextUnit = nextAction.Unit;
                int ticksElapsed = nextAction.TicksElapsed;
                tacticalCombatController.AdvanceCooldowns(ticksElapsed);
                RefreshUI();

                if (nextUnit == null)
                {
                    AddLog("CTB推进超时，战斗中止");
                    SetStatus("CTB异常");
                    state = GameState.Ended;
                    break;
                }

                var actor = nextAction.Actor;
                if (actor == player)
                {
                    hasMovedThisTurn = false;
                    hexesMovedThisTurn = 0;
                    waitingForPlayerCombatAction = true;
                    SetStatus($"你的行动（推进{ticksElapsed}刻）");
                    RefreshCombatButtons(true);

                    while (!hasMovedThisTurn && state == GameState.Combat && player.IsAlive && enemyUnit.character.IsAlive)
                        yield return null;

                    waitingForPlayerCombatAction = false;
                    RefreshCombatButtons(false);

                    if (!player.IsAlive || !enemyUnit.character.IsAlive) break;

                    RefreshUI();
                    yield return new WaitForSeconds(0.2f);
                }
                else if (actor == enemyUnit.character)
                {
                    hasMovedThisTurn = false;
                    hexesMovedThisTurn = 0;
                    waitingForPlayerCombatAction = false;
                    RefreshCombatButtons(false);
                    SetStatus($"{enemyUnit.character.Name} 行动中...（推进{ticksElapsed}刻）");
                    RefreshUI();

                    yield return new WaitForSeconds(0.5f);

                    ExecuteEnemyAI(enemyUnit, enemyUnit.character);
                    tacticalCombatController.ConsumeAction(enemyUnit.character);

                    RefreshUI();
                    yield return new WaitForSeconds(0.3f);
                }
            }

            waitingForPlayerCombatAction = false;
            uiManager?.SetActionButtonsInteractable(false);
            EndBattle(enemyUnit);
        }

        private void RefreshCombatButtons(bool interactable)
        {
            if (uiManager == null) return;

            uiManager.RefreshSpellButtons(
                playerSpells != null ? System.Array.ConvertAll(playerSpells, s => s?.displayNameKey ?? "?") : new string[0],
                player.SpellCooldowns,
                player.CurrentMP,
                playerSpells != null ? System.Array.ConvertAll(playerSpells, s => s?.resourceCost ?? 0) : new int[0],
                player.MaxSpellSlots,
                playerSpells != null ? System.Array.ConvertAll(playerSpells, s => TianZhang.Combat.DamageCalculator.ResolveElement(s?.damageElementId ?? "")) : new string[0]);
            uiManager.RefreshSkillButtons(
                playerSkills != null ? System.Array.ConvertAll(playerSkills, s => s?.displayNameKey ?? "?") : new string[0],
                player.SkillCooldowns,
                player.CurrentMP,
                playerSkills != null ? System.Array.ConvertAll(playerSkills, s => s?.resourceCost ?? 0) : new int[0],
                -1,
                playerSkills != null ? System.Array.ConvertAll(playerSkills, s => TianZhang.Combat.DamageCalculator.ResolveElement(s?.damageElementId ?? "")) : new string[0]);
            uiManager.SetActionButtonsInteractable(interactable);
        }

        private void ExecuteEnemyAI(EnemyUnit enemyUnit, Character ch)
        {
            var result = tacticalCombatController.ExecuteEnemyTurn(
                enemyUnit.character.CTBUnit.Id,
                player.CTBUnit.Id,
                enemyUnit.spells.Length > 0 ? enemyUnit.spells : null,
                enemyUnit.skills.Length > 0 ? enemyUnit.skills : null,
                enemyUnit.aiController,
                tilemapManager.Grid);

            AddLog($"{enemyUnit.character.Name}: {result}");
            hasMovedThisTurn = true;
        }

        private void HandleCombatKeyboardInput()
        {
            if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
                PlayerBasicAttack();
            else if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
                PlayerGuard();
            else if (Keyboard.current != null && Keyboard.current.wKey.wasPressedThisFrame)
                PlayerCombatWait();
            else if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame)
                PlayerSwapSpell();
            else if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame) PlayerCastSpell(0);
            else if (Keyboard.current != null && Keyboard.current.digit2Key.wasPressedThisFrame) PlayerCastSpell(1);
            else if (Keyboard.current != null && Keyboard.current.digit3Key.wasPressedThisFrame) PlayerCastSpell(2);
            else if (Keyboard.current != null && Keyboard.current.digit4Key.wasPressedThisFrame) PlayerCastSpell(3);
            else if (Keyboard.current != null && Keyboard.current.digit5Key.wasPressedThisFrame) PlayerUseSkill(0);
            else if (Keyboard.current != null && Keyboard.current.digit6Key.wasPressedThisFrame) PlayerUseSkill(1);
        }

        private void PlayerSwapSpell()
        {
            if (!waitingForPlayerCombatAction) return;
            if (currentCombatTarget == null || !player.IsAlive) return;
            if (player.CombatSwapsUsed >= Character.MaxCombatSwaps)
            {
                AddLog("本场战斗换法次数已用完");
                return;
            }

            var swappable = player.GetSwappableSpells();
            if (swappable.Length == 0)
            {
                AddLog("无可换入的术法");
                return;
            }

            // 换入第一个可用术法到槽位0
            string newSpell = swappable[0];
            var result = tacticalCombatController.ExecuteSwapSpell(player.CTBUnit.Id, 0, newSpell);
            AddActionLog(result);
            if (result.Success)
            {
                hasMovedThisTurn = true;
                RefreshUI();
            }
        }

        public void RequestBasicAttack() => PlayerBasicAttack();
        public void RequestGuard() => PlayerGuard();
        public void RequestWait() => PlayerCombatWait();
        public void RequestSwapSpell() => PlayerSwapSpell();
        public void RequestSpell(int index) => PlayerCastSpell(index);
        public void RequestSkill(int index) => PlayerUseSkill(index);

        public void PlayerBasicAttack()
        {
            if (!waitingForPlayerCombatAction) return;
            if (currentCombatTarget == null || !player.IsAlive) return;
            var result = tacticalCombatController.ExecuteBasicAttack(
                player.CTBUnit.Id,
                currentCombatTarget.CTBUnit.Id);
            AddActionLog(result);
            if (!result.Success)
            {
                RefreshUI();
                return;
            }
            hasMovedThisTurn = true;
            RefreshUI();
        }

        public void PlayerGuard()
        {
            if (!waitingForPlayerCombatAction) return;
            if (!player.IsAlive) return;
            var result = tacticalCombatController.ExecuteGuard(player.CTBUnit.Id);
            AddActionLog(result);
            if (!result.Success)
            {
                RefreshUI();
                return;
            }
            hasMovedThisTurn = true;
            RefreshUI();
        }

        public void PlayerCombatWait()
        {
            if (!waitingForPlayerCombatAction) return;
            if (!player.IsAlive) return;
            var result = tacticalCombatController.ExecuteWait(player.CTBUnit.Id);
            AddActionLog(result);
            if (!result.Success)
            {
                RefreshUI();
                return;
            }
            hasMovedThisTurn = true;
            RefreshUI();
        }

        public void PlayerCastSpell(int index)
        {
            if (!waitingForPlayerCombatAction) return;
            if (!player.IsAlive) return;
            if (currentCombatTarget == null) return;
            var result = tacticalCombatController.ExecuteArt(
                player.CTBUnit.Id,
                currentCombatTarget.CTBUnit.Id,
                index,
                playerSpells);
            AddActionLog(result);
            if (!result.Success)
            {
                RefreshUI();
                return;
            }
            hasMovedThisTurn = true;
            RefreshUI();
        }

        public void PlayerUseSkill(int index)
        {
            if (!waitingForPlayerCombatAction) return;
            if (currentCombatTarget == null || !player.IsAlive) return;
            var result = tacticalCombatController.ExecuteDivine(
                player.CTBUnit.Id,
                currentCombatTarget.CTBUnit.Id,
                index,
                playerSkills);
            AddActionLog(result);
            if (!result.Success)
            {
                RefreshUI();
                return;
            }
            hasMovedThisTurn = true;
            RefreshUI();
        }

        private void EndBattle(EnemyUnit enemyUnit)
        {
            var endResult = tacticalCombatController.ResolveBattleEnd(tilemapManager.Grid);
            if (endResult.Outcome == TacticalCombatEndOutcome.Defeat)
            {
                AddLog(endResult.Message);
                SetStatus("败北");
                state = GameState.Ended;
                adventureSceneController?.ResolveEncounterAndReturn(
                    endResult.Outcome,
                    enemyUnit.enemyData);
            }
            else if (endResult.Outcome == TacticalCombatEndOutcome.Victory)
            {
                AddLog(endResult.Message);
                SetStatus("胜利");
                enemyUnit.defeated = true;
                enemyUnit.marker?.SetActive(false);

                if (adventureSceneController != null &&
                    adventureSceneController.RequiresFormalEncounter &&
                    !endResult.DefeatedEnemyUnitIds.Contains(
                        enemyUnit.character.CTBUnit.Id))
                {
                    adventureSceneController.ReportEncounterConfigurationFailure(
                        "formal_encounter_defeated_member_mismatch");
                    enemyUnit.enemyData = null;
                }

                // 正式 AdventureScene 的单场闭环在结算后立即返回来源上下文。
                state = GameState.Ended;
                waitingForMoveInput = false;
                currentCombatTarget = null;
                tilemapManager.ClearOverlay();

                // 隐藏战斗UI
                if (uiManager != null)
                {
                    uiManager.ShowEnemyPanel(false);
                    uiManager.SetActionBarVisible(false);
                }

                RefreshUI();
                adventureSceneController?.ResolveEncounterAndReturn(
                    endResult.Outcome,
                    enemyUnit.enemyData);
            }
        }

        // ==================== UI工具 ====================

        private void RefreshUI()
        {
            if (uiManager == null) return;
            float playerThreshold = player.CTBUnit != null ? Mathf.Max(CTBEngine.ActionThreshold, player.CTBUnit.NextActionThreshold) : CTBEngine.ActionThreshold;
            float ct = player.CTBUnit != null ? player.CTBUnit.CT / playerThreshold : 0;
            string status = "";
            if (!player.IsAlive) status = "阵亡";
            else if (state == GameState.Exploration) status = "探索中";
            else if (state == GameState.Combat) status = "战斗中";
            uiManager.UpdatePlayerInfo(player.BuildCombatantPanelState(
                ct,
                TianZhang.Combat.DamageCalculator.GetGongFaElement(player.GongFaName),
                status));

            if (currentCombatTarget != null && currentCombatTarget.IsAlive)
            {
                float enemyThreshold = currentCombatTarget.CTBUnit != null ? Mathf.Max(CTBEngine.ActionThreshold, currentCombatTarget.CTBUnit.NextActionThreshold) : CTBEngine.ActionThreshold;
                float ect = currentCombatTarget.CTBUnit != null
                    ? currentCombatTarget.CTBUnit.CT / enemyThreshold : 0;
                uiManager.UpdateEnemyInfo(currentCombatTarget.BuildCombatantPanelState(
                    ect,
                    TianZhang.Combat.DamageCalculator.GetGongFaElement(currentCombatTarget.GongFaName),
                    string.Empty));
            }
            else
            {
                uiManager.ShowEnemyPanel(false);
            }
        }

        private void SetStatus(string text)
        {
            if (uiManager != null)
                uiManager.SetTurnBanner(text);
        }

        private void AddLog(string message)
        {
            if (uiManager != null)
                uiManager.AddLog(message);
        }

        private void AddActionLog(CombatResolver.ActionResult result)
        {
            GetCombatLogAdapter().AppendActionResult(result);
        }

        private CombatLogAdapter GetCombatLogAdapter()
        {
            if (combatLogAdapter == null)
                combatLogAdapter = new CombatLogAdapter(AddLog, SetStatus);
            return combatLogAdapter;
        }

        // ==================== 公共查询 ====================

        public Character GetPlayer() => player;
        public GameState GetState() => state;
        public HexGrid GetGrid() => tilemapManager?.Grid;
    }
}
