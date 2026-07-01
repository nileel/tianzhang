using System;
using System.Collections.Generic;
using TianZhang.Core;
using TianZhang.Entity;

namespace TianZhang.Combat
{
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

        private void EnsureSession()
        {
            if (!hasSession)
                throw new InvalidOperationException("BeginCombat must be called before advancing tactical combat.");
        }
    }
}
