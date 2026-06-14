using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TianZhang.Core;
using TianZhang.Entity;
using TianZhang.Combat;
using TianZhang.HexTile;

namespace TianZhang.Game
{
    /// <summary>
    /// 战斗场景控制器 — CTB六角格战棋原型 v0.3
    /// 操作：鼠标点击移动/攻击 | UI按钮施法/神通 | 键盘快捷键兼容
    /// 新增：AI分离(SimpleAI) / 移动拆分 / CT消耗细化
    /// </summary>
    public class BattleSceneController : MonoBehaviour
    {
        [Header("引用")]
        public HexTilemapManager tilemapManager;
        public BattleUIManager uiManager;
        public CharacterData playerData;
        public CharacterData enemyData;

        [Header("术法/神通")]
        public SpellData[] playerSpells;
        public DivineSkillData[] playerSkills;
        public SpellData[] enemySpells;
        public DivineSkillData[] enemySkills;

        // ---- 核心系统 ----
        private CTBEngine ctbEngine;
        private CombatResolver resolver;
        private SimpleAI aiController;
        private Character player;
        private Character enemy;

        // ---- 视觉 ----
        private GameObject playerMarker;
        private GameObject enemyMarker;

        // ---- 状态机 ----
        public enum BattleState { Idle, PlayerTurn, EnemyTurn, Animating, Ended }
        private BattleState state = BattleState.Idle;

        // ---- 玩家输入 ----
        private bool waitingForInput;
        private bool hasMovedThisTurn;      // 本回合是否已移动过
        private int hexesMovedThisTurn;      // 本回合已移动格数

        // ---- CT消耗常量 ----
        public const float CtPerMoveHex = 25f;   // 每移动1格消耗CT
        public const float CtPerAction = 100f;   // 完整行动消耗CT
        public const float CtPerGuard = 60f;     // 防御消耗CT
        public const float CtRemainThreshold = 10f; // CT低于此值视为回合结束

        private void Start()
        {
            StartCoroutine(InitBattle());
        }

        private void Update()
        {
            HandleKeyboardInput();
            HandleMouseClick();
        }

        private void HandleKeyboardInput()
        {
            if (state != BattleState.PlayerTurn || !waitingForInput) return;

            if (Input.GetKeyDown(KeyCode.A))
                PlayerBasicAttack();
            else if (Input.GetKeyDown(KeyCode.G))
                PlayerGuard();
            else if (Input.GetKeyDown(KeyCode.W))
                PlayerWait();
            else if (Input.GetKeyDown(KeyCode.E))
                PlayerEndTurn();
            else if (Input.GetKeyDown(KeyCode.Alpha1)) PlayerCastSpell(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) PlayerCastSpell(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) PlayerCastSpell(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) PlayerCastSpell(3);
            else if (Input.GetKeyDown(KeyCode.Alpha5)) PlayerUseSkill(0);
            else if (Input.GetKeyDown(KeyCode.Alpha6)) PlayerUseSkill(1);
        }

        private void HandleMouseClick()
        {
            if (state != BattleState.PlayerTurn || !waitingForInput) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (uiManager != null && uiManager.IsPointerOverUI()) return;
            if (Camera.main == null || tilemapManager == null) return;

            var coord = tilemapManager.ScreenToHex(Input.mousePosition);
            HandleTileClick(coord);
        }

        private IEnumerator InitBattle()
        {
            state = BattleState.Animating;
            ctbEngine = new CTBEngine();
            resolver = new CombatResolver { Grid = tilemapManager.Grid, Engine = ctbEngine };
            aiController = new SimpleAI();

            tilemapManager.GenerateHexGrid();

            var playerStart = new HexCoord(-2, 1);
            var enemyStart = new HexCoord(2, -1);

            player = Character.FromData(playerData, playerStart);
            enemy = Character.FromData(enemyData, enemyStart);

            player.SpellCooldowns = new int[playerSpells?.Length ?? 0];
            player.SkillCooldowns = new int[playerSkills?.Length ?? 0];
            enemy.SpellCooldowns = new int[enemySpells?.Length ?? 0];
            enemy.SkillCooldowns = new int[enemySkills?.Length ?? 0];

            player.CTBUnit = ctbEngine.RegisterUnit(player.Reaction, player);
            enemy.CTBUnit = ctbEngine.RegisterUnit(enemy.Reaction, enemy);

            tilemapManager.Grid.SetOccupied(playerStart, player.CTBUnit.Id);
            tilemapManager.Grid.SetOccupied(enemyStart, enemy.CTBUnit.Id);

            playerMarker = tilemapManager.PlaceUnitMarker(playerStart, Color.cyan, "玩家");
            enemyMarker = tilemapManager.PlaceUnitMarker(enemyStart, Color.red, "敌方");

            player.FaceTarget(enemyStart);
            enemy.FaceTarget(playerStart);

            if (uiManager != null)
            {
                uiManager.SetController(this);
                uiManager.ClearLog();
                uiManager.SetTurnBanner("准备战斗");
                RefreshAllUI();
            }

            Debug.Log("战斗初始化完成。CTB引擎就绪。");
            Debug.Log($"玩家: {player}");
            Debug.Log($"敌方: {enemy}");

            yield return new WaitForSeconds(0.3f);
            StartCoroutine(BattleLoop());
        }

        private IEnumerator BattleLoop()
        {
            while (player.IsAlive && enemy.IsAlive)
            {
                var (unit, ticks) = ctbEngine.AdvanceUntilAction();
                if (unit == null) break;

                resolver.AdvanceCooldowns(player, ticks);
                resolver.AdvanceCooldowns(enemy, ticks);

                var character = unit.UserData as Character;
                if (character == null || !character.IsAlive) continue;

                character.IsGuarding = false;
                hasMovedThisTurn = false;
                hexesMovedThisTurn = 0;

                if (character == player)
                {
                    SetStatus("你的回合", Color.cyan);
                    state = BattleState.PlayerTurn;
                    if (uiManager != null)
                    {
                        uiManager.SetActionButtonsInteractable(true);
                        RefreshSpellSkillButtons();
                    }
                    waitingForInput = true;
                    while (waitingForInput && player.IsAlive && enemy.IsAlive)
                        yield return null;
                }
                else
                {
                    SetStatus("敌方回合", Color.red);
                    state = BattleState.EnemyTurn;
                    yield return new WaitForSeconds(0.3f);

                    var aiMsg = aiController.ExecuteTurn(enemy, player,
                        enemySpells, enemySkills, resolver, tilemapManager.Grid);
                    AddLog(aiMsg);
                    UpdateMarkers();
                    ctbEngine.ConsumePartialCT(enemy.CTBUnit, CtPerAction);
                }

                RefreshAllUI();
                yield return new WaitForSeconds(0.15f);

                if (!enemy.IsAlive)
                {
                    if (enemyMarker != null)
                        enemyMarker.GetComponent<SpriteRenderer>().color = Color.gray;
                    SetStatus("胜利!", Color.green);
                    uiManager?.AddLog("*** 战斗结束：玩家胜利 ***");
                }
                if (!player.IsAlive)
                {
                    if (playerMarker != null)
                        playerMarker.GetComponent<SpriteRenderer>().color = Color.gray;
                    SetStatus("败北...", Color.gray);
                    uiManager?.AddLog("*** 战斗结束：玩家败北 ***");
                }
            }

            state = BattleState.Ended;
            uiManager?.SetActionButtonsInteractable(false);
        }

        // ==================== 玩家行动 ====================

        public void HandleTileClick(HexCoord coord)
        {
            if (!waitingForInput) return;

            int dist = player.Position.Distance(coord);

            // 点击敌方 → 近战攻击
            if (coord.Equals(enemy.Position))
            {
                if (dist <= 1)
                    PlayerBasicAttack();
                else
                    SetStatus("距离 " + dist + " 格，近战无法到达。请用术法/神通");
                return;
            }

            // 移动（支持拆分移动）
            int remainingMove = player.MovePoints - hexesMovedThisTurn;
            if (remainingMove <= 0)
            {
                SetStatus("本回合移动力已耗尽");
                return;
            }

            var path = tilemapManager.Grid.FindPath(player.Position, coord, remainingMove);
            if (path != null && path.Count > 0)
            {
                var reachable = path.GetRange(0, Mathf.Min(path.Count, remainingMove));
                HexCoord final = reachable[reachable.Count - 1];
                if (!tilemapManager.Grid.IsOccupied(final))
                {
                    var result = resolver.Move(player, reachable);
                    AddLog(result.Message);
                    UpdateMarkers();
                    hasMovedThisTurn = true;
                    hexesMovedThisTurn += reachable.Count;

                    // 移动消耗CT（每格25）
                    ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerMoveHex * reachable.Count);

                    // 检查是否还能继续行动
                    float remainingCT = player.CTBUnit.CT;
                    if (remainingCT < CtRemainThreshold || hexesMovedThisTurn >= player.MovePoints)
                    {
                        // CT不足或移动力耗尽 → 结束回合
                        SetStatus("移动完毕，回合结束");
                        FinishPlayerAction();
                    }
                    else
                    {
                        // 还可以继续行动
                        float remPct = remainingCT / CTBEngine.ActionThreshold;
                        SetStatus($"移动 {reachable.Count} 格，剩余CT {remPct:P0}（可继续行动）");
                        RefreshSpellSkillButtons();
                    }
                    return;
                }
            }

            SetStatus("无法到达 (" + coord + ")");
        }

        public void PlayerBasicAttack()
        {
            if (!waitingForInput) return;
            var result = resolver.BasicAttack(player, enemy);
            SetStatus(result.Message);
            AddLog(result.Message);
            if (result.Success)
            {
                StartCoroutine(Flash(enemyMarker, Color.white));
                ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerAction);
                FinishPlayerAction();
            }
        }

        public void PlayerCastSpell(int index)
        {
            if (!waitingForInput) return;
            if (index < 0 || playerSpells == null || index >= playerSpells.Length) return;
            var result = resolver.CastSpell(player, enemy, index, playerSpells[index]);
            SetStatus(result.Message);
            AddLog(result.Message);
            if (result.Success)
            {
                StartCoroutine(Flash(enemyMarker, Color.yellow));
                ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerAction);
                FinishPlayerAction();
            }
        }

        public void PlayerUseSkill(int index)
        {
            if (!waitingForInput) return;
            if (index < 0 || playerSkills == null || index >= playerSkills.Length) return;
            var result = resolver.UseSkill(player, enemy, index, playerSkills[index]);
            SetStatus(result.Message);
            AddLog(result.Message);
            if (result.Success)
            {
                StartCoroutine(Flash(enemyMarker, Color.magenta));
                ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerAction);
                FinishPlayerAction();
            }
        }

        public void PlayerGuard()
        {
            if (!waitingForInput) return;
            var result = resolver.Guard(player);
            SetStatus(result.Message);
            AddLog(result.Message);
            StartCoroutine(Flash(playerMarker, Color.blue));
            ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerGuard);
            FinishPlayerAction();
        }

        public void PlayerWait()
        {
            if (!waitingForInput) return;
            var result = resolver.Wait(player);
            SetStatus(result.Message);
            AddLog(result.Message);
            FinishPlayerAction();
        }

        public void PlayerEndTurn()
        {
            if (!waitingForInput) return;
            ctbEngine.ConsumePartialCT(player.CTBUnit, CtPerAction);
            SetStatus("回合结束");
            AddLog($"{player.Name} 提前结束回合");
            FinishPlayerAction();
        }

        private void FinishPlayerAction()
        {
            waitingForInput = false;
            state = BattleState.Animating;
            tilemapManager.ClearOverlay();
            if (uiManager != null)
                uiManager.SetActionButtonsInteractable(false);
        }

        // ==================== UI 更新 ====================

        private void SetStatus(string text, Color? color = null)
        {
            if (uiManager != null)
                uiManager.SetTurnBanner(text, color);
        }

        private void AddLog(string message)
        {
            if (uiManager != null)
                uiManager.AddLog(message);
        }

        private void RefreshAllUI()
        {
            if (uiManager == null) return;

            float playerCT = player.CTBUnit != null ? player.CTBUnit.CT / CTBEngine.ActionThreshold : 0;
            string ps = "";
            if (!player.IsAlive) ps = "阵亡";
            else if (player.IsGuarding) ps = "防御中";
            uiManager.UpdatePlayerInfo(player.Name, player.CurrentHP, player.MaxHP,
                player.CurrentMP, player.MaxMP, playerCT, ps);

            float enemyCT = enemy.CTBUnit != null ? enemy.CTBUnit.CT / CTBEngine.ActionThreshold : 0;
            string es = "";
            if (!enemy.IsAlive) es = "阵亡";
            else if (enemy.IsGuarding) es = "防御中";
            uiManager.UpdateEnemyInfo(enemy.Name, enemy.CurrentHP, enemy.MaxHP,
                enemy.CurrentMP, enemy.MaxMP, enemyCT, es);
        }

        private void RefreshSpellSkillButtons()
        {
            if (uiManager == null) return;

            int sl = playerSpells?.Length ?? 0;
            var spellNames = new string[sl];
            var spellMpCosts = new int[sl];
            for (int i = 0; i < sl; i++)
            {
                spellNames[i] = playerSpells[i].spellName;
                spellMpCosts[i] = playerSpells[i].mpCost;
            }
            uiManager.RefreshSpellButtons(spellNames, player.SpellCooldowns, player.CurrentMP, spellMpCosts, player.MaxSpellSlots);

            int kl = playerSkills?.Length ?? 0;
            var skillNames = new string[kl];
            var skillMpCosts = new int[kl];
            for (int i = 0; i < kl; i++)
            {
                skillNames[i] = playerSkills[i].skillName;
                skillMpCosts[i] = playerSkills[i].mpCost;
            }
            uiManager.RefreshSkillButtons(skillNames, player.SkillCooldowns, player.CurrentMP, skillMpCosts, player.MaxSpellSlots);
        }

        // ==================== 视觉 ====================

        private void UpdateMarkers()
        {
            if (playerMarker != null)
                playerMarker.transform.position = tilemapManager.HexToWorld(player.Position);
            if (enemyMarker != null)
                enemyMarker.transform.position = tilemapManager.HexToWorld(enemy.Position);
        }

        private IEnumerator Flash(GameObject marker, Color flashColor)
        {
            var sr = marker?.GetComponent<SpriteRenderer>();
            if (sr == null) yield break;
            var original = sr.color;
            sr.color = flashColor;
            yield return new WaitForSeconds(0.12f);
            sr.color = original;
        }

        // ==================== 公共查询 ====================

        public Character GetPlayer() => player;
        public Character GetEnemy() => enemy;
        public BattleState GetState() => state;
        public HexGrid GetGrid() => tilemapManager?.Grid;
        public HexTilemapManager GetTilemap() => tilemapManager;
        public CombatResolver GetResolver() => resolver;
        public CTBEngine GetCTBEngine() => ctbEngine;
    }
}
