using System;
using System.Collections.Generic;
using TianZhang.Core;
using TianZhang.Entity;

namespace TianZhang.Combat
{
    public interface ICombatCommandHandler
    {
        void RequestBasicAttack();
        void RequestGuard();
        void RequestWait();
        void RequestSwapSpell();
        void RequestSpell(int index);
        void RequestSkill(int index);
    }

    public struct TacticalCombatSession
    {
        public Character Player { get; }
        public Character Enemy { get; }

        public TacticalCombatSession(Character player, Character enemy)
        {
            Player = player;
            Enemy = enemy;
        }

        public List<CTBEngine.CTBUnit> CreateActiveUnitList()
        {
            return new List<CTBEngine.CTBUnit> { Player.CTBUnit, Enemy.CTBUnit };
        }
    }

    public struct TacticalActionAdvance
    {
        public Character Actor { get; }
        public CTBEngine.CTBUnit Unit { get; }
        public int TicksElapsed { get; }

        public TacticalActionAdvance(Character actor, CTBEngine.CTBUnit unit, int ticksElapsed)
        {
            Actor = actor;
            Unit = unit;
            TicksElapsed = ticksElapsed;
        }
    }

    public enum TacticalCombatEndOutcome
    {
        Ongoing,
        Victory,
        Defeat,
    }

    public struct TacticalCombatEndResult
    {
        public TacticalCombatEndOutcome Outcome { get; }
        public string Message { get; }
        public IReadOnlyList<string> DropItems { get; }

        public bool IsEnded => Outcome != TacticalCombatEndOutcome.Ongoing;

        public TacticalCombatEndResult(
            TacticalCombatEndOutcome outcome,
            string message,
            IReadOnlyList<string> dropItems = null)
        {
            Outcome = outcome;
            Message = message ?? string.Empty;
            DropItems = dropItems ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// CTB 战斗调度边界：持有战斗引擎、解析器与 AI，并封装单场遭遇的准备和推进。
    /// </summary>
    public sealed class TacticalCombatController
    {
        private TacticalCombatSession currentSession;
        private bool hasSession;

        public CTBEngine Engine { get; }
        public CombatResolver Resolver { get; }
        public IAIController AIController { get; }

        public TacticalCombatController()
            : this(new CTBEngine(), null, null)
        {
        }

        public TacticalCombatController(CTBEngine engine, CombatResolver resolver = null, IAIController aiController = null)
        {
            Engine = engine ?? new CTBEngine();
            Resolver = resolver ?? new CombatResolver();
            Resolver.Engine = Engine;
            AIController = aiController ?? new SimpleAI();
        }

        public TacticalCombatSession CurrentSession => currentSession;

        public TacticalCombatSession BeginCombat(Character player, Character enemy, HexGrid grid)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            EnsureRegistered(player);
            EnsureRegistered(enemy);

            player.FaceTarget(enemy.Position);
            enemy.FaceTarget(player.Position);

            Engine.ResetUnitCT(player.CTBUnit);
            Engine.ResetUnitCT(enemy.CTBUnit);
            Engine.ClearActionQueue();
            Resolver.Grid = grid;

            InitializeGongFaStacks(player);
            InitializeGongFaStacks(enemy);

            currentSession = new TacticalCombatSession(player, enemy);
            hasSession = true;
            return currentSession;
        }

        public TacticalActionAdvance AdvanceUntilAction()
        {
            EnsureSession();
            var (unit, ticksElapsed) = Engine.AdvanceUntilAction(currentSession.CreateActiveUnitList());
            return new TacticalActionAdvance(unit?.UserData as Character, unit, ticksElapsed);
        }

        public void AdvanceCooldowns(int ticks)
        {
            EnsureSession();
            Resolver.AdvanceCooldowns(currentSession.Player, ticks);
            Resolver.AdvanceCooldowns(currentSession.Enemy, ticks);
        }

        public CombatResolver.ActionResult ExecutePlayerBasicAttack()
        {
            EnsureSession();
            var player = currentSession.Player;
            var enemy = currentSession.Enemy;
            if (!CanPlayerAct(player) || enemy == null)
                return NoAction();

            bool useMagic = player.MagAtk > player.PhysAtk;
            var result = Resolver.BasicAttack(player, enemy, useMagic);
            ConsumeActionIfSuccessful(player, result);
            return result;
        }

        public CombatResolver.ActionResult ExecutePlayerGuard()
        {
            EnsureSession();
            var player = currentSession.Player;
            if (!CanPlayerAct(player))
                return NoAction();

            var result = Resolver.Guard(player);
            ConsumeActionIfSuccessful(player, result);
            return result;
        }

        public CombatResolver.ActionResult ExecutePlayerWait()
        {
            EnsureSession();
            var player = currentSession.Player;
            if (!CanPlayerAct(player))
                return NoAction();

            return Resolver.Wait(player);
        }

        public CombatResolver.ActionResult ExecutePlayerSpell(int index, SpellData[] spells)
        {
            EnsureSession();
            var player = currentSession.Player;
            if (!CanPlayerAct(player) || spells == null || index < 0 || index >= spells.Length || spells[index] == null)
                return NoAction();
            if (player.SpellCooldowns == null || index >= player.SpellCooldowns.Length)
                return NoAction();

            var spell = spells[index];
            if (player.SpellCooldowns[index] > 0)
                return Failure("术法冷却中");
            if (player.CurrentMP < spell.mpCost)
                return Failure("灵力不足");

            bool isSelfTarget = spell.minRange == 0 && spell.maxRange == 0;
            var target = isSelfTarget ? player : currentSession.Enemy;
            if (target == null)
                return NoAction();
            if (!isSelfTarget)
            {
                int dist = player.Position.Distance(target.Position);
                if (dist < spell.minRange || dist > spell.maxRange)
                    return Failure("超出射程");
            }

            var result = Resolver.CastSpell(player, target, index, spell);
            ConsumeActionIfSuccessful(player, result);
            return result;
        }

        public CombatResolver.ActionResult ExecutePlayerSkill(int index, DivineSkillData[] skills)
        {
            EnsureSession();
            var player = currentSession.Player;
            var enemy = currentSession.Enemy;
            if (!CanPlayerAct(player) || enemy == null || skills == null || index < 0 || index >= skills.Length || skills[index] == null)
                return NoAction();
            if (player.SkillCooldowns == null || index >= player.SkillCooldowns.Length)
                return NoAction();

            var skill = skills[index];
            if (player.SkillCooldowns[index] > 0)
                return Failure("神通冷却中");
            if (player.CurrentMP < skill.mpCost)
                return Failure("灵力不足");

            int dist = player.Position.Distance(enemy.Position);
            if (dist < skill.minRange || dist > skill.maxRange)
                return Failure("超出射程");

            var result = Resolver.UseSkill(player, enemy, index, skill);
            ConsumeActionIfSuccessful(player, result);
            return result;
        }

        public CombatResolver.ActionResult ExecutePlayerSwapSpell(int slotIndex, string newSpellId)
        {
            EnsureSession();
            var player = currentSession.Player;
            if (!CanPlayerAct(player))
                return NoAction();
            if (player.CombatSwapsUsed >= Character.MaxCombatSwaps)
                return Failure("本场战斗换法次数已用完");

            string oldSpell = player.SwapSpellInCombat(slotIndex, newSpellId);
            if (oldSpell == null)
                return Failure("换法失败");

            ConsumeAction(player);
            return new CombatResolver.ActionResult
            {
                Success = true,
                Message = $"临阵换法: {oldSpell} → {newSpellId} (CD×2, 剩余{Character.MaxCombatSwaps - player.CombatSwapsUsed}次)"
            };
        }

        public static IReadOnlyList<string> CreateDropItems(CharacterData enemyData)
        {
            var dropItems = new List<string> { "灵石×5" };
            float realmMultiplier = enemyData != null ? enemyData.realmMultiplier : 0f;
            if (realmMultiplier >= 2.0f)
                dropItems.Add("中品丹药×1");
            else if (realmMultiplier >= 1.3f)
                dropItems.Add("下品丹药×1");

            return dropItems;
        }

        public TacticalCombatEndResult ResolveBattleEnd(CharacterData enemyData, HexGrid grid)
        {
            EnsureSession();
            var player = currentSession.Player;
            var enemy = currentSession.Enemy;

            if (player == null || !player.IsAlive)
            {
                if (player?.CTBUnit != null)
                    player.CTBUnit.IsAlive = false;
                return new TacticalCombatEndResult(
                    TacticalCombatEndOutcome.Defeat,
                    "玩家被击败！游戏结束");
            }

            if (enemy == null || enemy.IsAlive)
                return new TacticalCombatEndResult(TacticalCombatEndOutcome.Ongoing, string.Empty);

            if (enemy.CTBUnit != null)
                enemy.CTBUnit.IsAlive = false;
            grid?.ClearOccupied(enemy.Position);

            return new TacticalCombatEndResult(
                TacticalCombatEndOutcome.Victory,
                $"击败了 {enemy.Name}！",
                CreateDropItems(enemyData));
        }

        public string ExecuteEnemyTurn(Character enemy, Character target, SpellData[] spells, DivineSkillData[] skills, HexGrid grid)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            Resolver.Grid = grid;
            return AIController.ExecuteTurn(enemy, target, spells, skills, Resolver, grid);
        }

        public void ConsumeAction(Character character)
        {
            if (character?.CTBUnit == null) return;
            Engine.ConsumeAction(character.CTBUnit);
        }

        private void EnsureRegistered(Character character)
        {
            if (character.CTBUnit == null)
                character.CTBUnit = Engine.RegisterUnit(character.Reaction, character);

            character.CTBUnit.UserData = character;
            character.CTBUnit.IsAlive = character.IsAlive;
        }

        private static void InitializeGongFaStacks(Character character)
        {
            if (character.GongFaName == "抱元守一经")
                character.ShouyiStacks = 2;
            if (character.GongFaName == "云篆度人经")
                character.FudanStacks = 2;
        }

        private static bool CanPlayerAct(Character player)
        {
            return player != null && player.IsAlive;
        }

        private void ConsumeActionIfSuccessful(Character character, CombatResolver.ActionResult result)
        {
            if (result.Success)
                ConsumeAction(character);
        }

        private static CombatResolver.ActionResult NoAction()
        {
            return new CombatResolver.ActionResult { Success = false };
        }

        private static CombatResolver.ActionResult Failure(string message)
        {
            return new CombatResolver.ActionResult { Success = false, Message = message };
        }

        private void EnsureSession()
        {
            if (!hasSession)
                throw new InvalidOperationException("BeginCombat must be called before advancing tactical combat.");
        }
    }
}
