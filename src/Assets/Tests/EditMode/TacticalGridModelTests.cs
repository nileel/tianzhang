using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Tactical;

namespace TianZhang.Tests
{
    public class TacticalGridModelTests
    {
        [Test]
        public void FromHexGridCopiesBlockersOccupantsAndDefaultHeight()
        {
            var source = new HexGrid();
            var center = new HexCoord(0, 0);
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);

            source.SetBlocked(blocked, true);
            source.SetOccupied(occupied, 42);

            var model = TacticalGridModel.FromHexGrid(new[] { center, blocked, occupied }, source);

            Assert.AreEqual(3, model.Count);
            Assert.IsFalse(model.GetTile(center).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksGroundMove);
            Assert.IsTrue(model.GetTile(blocked).BlocksLanding);
            Assert.AreEqual(0, model.GetTile(blocked).HeightLevel);
            Assert.AreEqual(42, model.GetTile(occupied).OccupiedUnitId);
            Assert.IsTrue(model.IsOccupied(occupied));
        }

        [Test]
        public void ToHexGridPreservesGroundBlockersAndOccupants()
        {
            var blocked = new HexCoord(1, 0);
            var occupied = new HexCoord(0, 1);
            var model = new TacticalGridModel();

            model.SetTile(new TacticalTileData(new HexCoord(0, 0)));
            model.SetTile(new TacticalTileData(blocked)
            {
                BlocksGroundMove = true,
            });
            model.SetTile(new TacticalTileData(occupied)
            {
                OccupiedUnitId = 7,
            });

            var grid = model.ToHexGrid();

            Assert.IsFalse(grid.IsBlocked(new HexCoord(0, 0)));
            Assert.IsTrue(grid.IsBlocked(blocked));
            Assert.AreEqual(7, grid.GetOccupant(occupied));
            Assert.IsTrue(grid.IsOccupied(occupied));
        }
    }

    public class CombatMechanismTests
    {
        [Test]
        public void FullFudanBoostsAndConsumesMagicDivineSkill()
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver { Engine = engine };
            var skill = ScriptableObject.CreateInstance<DivineSkillData>();
            try
            {
                skill.skillName = "符胆神通回归";
                skill.type = SpellType.Magic;
                skill.minRange = 1;
                skill.maxRange = 3;
                skill.mpCost = 0;
                skill.cooldownTicks = 0;
                skill.damageMultiplier = 1f;

                var baseCaster = CreateFudanCaster(engine, "无符胆", 0);
                var baseTarget = CreateTarget(engine, "基准目标");
                LogAssert.Expect(LogType.Log, new Regex("无符胆 神通·符胆神通回归"));
                var baseResult = resolver.UseSkill(baseCaster, baseTarget, 0, skill);

                var fullCaster = CreateFudanCaster(engine, "满符胆", 5);
                var fullTarget = CreateTarget(engine, "满层目标");
                LogAssert.Expect(LogType.Log, new Regex("满符胆 神通·符胆神通回归"));
                var fullResult = resolver.UseSkill(fullCaster, fullTarget, 0, skill);

                Assert.IsTrue(baseResult.Success);
                Assert.IsTrue(fullResult.Success);
                Assert.Greater(fullResult.Damage.FinalDamage, baseResult.Damage.FinalDamage);
                Assert.AreEqual(1, fullCaster.FudanStacks);
            }
            finally
            {
                Object.DestroyImmediate(skill);
            }
        }

        private static Character CreateFudanCaster(CTBEngine engine, string name, int fudanStacks)
        {
            var character = new Character
            {
                Name = name,
                GongFaName = "云篆度人经",
                MaxHP = 1000,
                CurrentHP = 1000,
                MaxMP = 100,
                CurrentMP = 100,
                PhysAtk = 50,
                MagAtk = 200,
                PhysDef = 50,
                MagDef = 50,
                Reaction = 100,
                HitRateBonus = 0f,
                CritRate = 0f,
                CritDamage = 0f,
                Position = new HexCoord(0, 0),
                FudanStacks = fudanStacks,
            };
            character.SetRealm("金丹");
            character.SkillCooldowns = new int[1];
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }

        private static Character CreateTarget(CTBEngine engine, string name)
        {
            var character = new Character
            {
                Name = name,
                GongFaName = "含弘光大典",
                MaxHP = 1000,
                CurrentHP = 1000,
                MaxMP = 100,
                CurrentMP = 100,
                PhysAtk = 50,
                MagAtk = 50,
                PhysDef = 100,
                MagDef = 100,
                Reaction = 100,
                SoulShieldRate = 0f,
                DodgeRate = 0f,
                CritRate = 0f,
                Position = new HexCoord(1, 0),
            };
            character.SetRealm("金丹");
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }
    }

    public class TacticalCombatControllerTests
    {
        [Test]
        public void BeginCombatResetsCtbLinksGridFacesTargetsAndInitializesGongFaStacks()
        {
            var grid = new HexGrid();
            var engine = new CTBEngine();
            var resolver = new CombatResolver();
            var controller = new TacticalCombatController(engine, resolver);

            var player = CreateCombatant("太一修士", "云篆度人经", new HexCoord(0, 0), engine);
            var enemy = CreateCombatant("守一敌", "抱元守一经", new HexCoord(1, 0), engine);
            player.CTBUnit.CT = 250f;
            player.CTBUnit.PendingCooldownPenalty = 30;
            player.CTBUnit.NextActionThreshold = 130f;
            enemy.CTBUnit.CT = 150f;

            var session = controller.BeginCombat(player, enemy, grid);

            Assert.AreSame(player, session.Player);
            Assert.AreSame(enemy, session.Enemy);
            Assert.AreSame(grid, controller.Resolver.Grid);
            Assert.AreEqual(0f, player.CTBUnit.CT);
            Assert.AreEqual(0, player.CTBUnit.PendingCooldownPenalty);
            Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.NextActionThreshold);
            Assert.AreEqual(0f, enemy.CTBUnit.CT);
            Assert.AreEqual(0, player.Facing);
            Assert.AreEqual(3, enemy.Facing);
            Assert.AreEqual(2, player.FudanStacks);
            Assert.AreEqual(2, enemy.ShouyiStacks);
        }

        [Test]
        public void AdvanceUntilActionReturnsNextActorAndAdvancesCooldowns()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var fast = CreateCombatant("快修", "抱元守一经", new HexCoord(0, 0), controller.Engine);
            var slow = CreateCombatant("慢修", "含弘光大典", new HexCoord(1, 0), controller.Engine);
            fast.Reaction = 100;
            fast.CTBUnit.Speed = 100;
            fast.CTBUnit.CtPerTick = 100f;
            slow.Reaction = 10;
            slow.CTBUnit.Speed = 10;
            slow.CTBUnit.CtPerTick = 10f;
            fast.SpellCooldowns[0] = 5;
            slow.SpellCooldowns[0] = 5;

            controller.BeginCombat(fast, slow, grid);
            var next = controller.AdvanceUntilAction();
            controller.AdvanceCooldowns(next.TicksElapsed);

            Assert.AreSame(fast, next.Actor);
            Assert.AreEqual(1, next.TicksElapsed);
            Assert.AreEqual(4, fast.SpellCooldowns[0]);
            Assert.AreEqual(4, slow.SpellCooldowns[0]);
        }

        [Test]
        public void PlayerBasicAttackConsumesActionOnlyWhenAttackSucceeds()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(1, 0), controller.Engine);

            controller.BeginCombat(player, enemy, grid);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var hit = controller.ExecutePlayerBasicAttack();

            Assert.IsTrue(hit.Success);
            Assert.AreEqual(0f, player.CTBUnit.CT);
            Assert.Less(enemy.CurrentHP, enemy.MaxHP);

            enemy.Position = new HexCoord(3, 0);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var outOfRange = controller.ExecutePlayerBasicAttack();

            Assert.IsFalse(outOfRange.Success);
            Assert.AreEqual("目标不在近战范围", outOfRange.Message);
            Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.CT);
        }

        [Test]
        public void PlayerSpellPreservesPrecheckFailuresAndConsumesActionOnSuccess()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(3, 0), controller.Engine);
            var spell = ScriptableObject.CreateInstance<SpellData>();
            try
            {
                spell.spellName = "测试术法";
                spell.type = SpellType.Magic;
                spell.minRange = 1;
                spell.maxRange = 1;
                spell.mpCost = 10;
                spell.cooldownTicks = 20;
                spell.damageMultiplier = 1f;

                controller.BeginCombat(player, enemy, grid);
                player.CTBUnit.CT = CTBEngine.ActionThreshold;
                int mpBefore = player.CurrentMP;

                var outOfRange = controller.ExecutePlayerSpell(0, new[] { spell });

                Assert.IsFalse(outOfRange.Success);
                Assert.AreEqual("超出射程", outOfRange.Message);
                Assert.AreEqual(mpBefore, player.CurrentMP);
                Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.CT);

                enemy.Position = new HexCoord(1, 0);

                var cast = controller.ExecutePlayerSpell(0, new[] { spell });

                Assert.IsTrue(cast.Success);
                Assert.AreEqual(0f, player.CTBUnit.CT);
                Assert.AreEqual(20, player.SpellCooldowns[0]);
                Assert.Less(player.CurrentMP, mpBefore);
            }
            finally
            {
                Object.DestroyImmediate(spell);
            }
        }

        [Test]
        public void PlayerGuardConsumesFullActionAndWaitRetainsHalfCt()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(1, 0), controller.Engine);

            controller.BeginCombat(player, enemy, grid);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var guard = controller.ExecutePlayerGuard();

            Assert.IsTrue(guard.Success);
            Assert.IsTrue(player.IsGuarding);
            Assert.AreEqual(0f, player.CTBUnit.CT);

            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var wait = controller.ExecutePlayerWait();

            Assert.IsTrue(wait.Success);
            Assert.IsFalse(player.IsGuarding);
            Assert.AreEqual(CTBEngine.ActionThreshold * CTBEngine.CtRetentionOnWait, player.CTBUnit.CT);
        }

        [Test]
        public void CreateDropItemsUsesDefeatedEnemyRealmThresholds()
        {
            var weak = ScriptableObject.CreateInstance<CharacterData>();
            var middle = ScriptableObject.CreateInstance<CharacterData>();
            var strong = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                weak.realmMultiplier = 1.2f;
                middle.realmMultiplier = 1.3f;
                strong.realmMultiplier = 2.0f;

                CollectionAssert.AreEqual(new[] { "灵石×5" }, TacticalCombatController.CreateDropItems(weak));
                CollectionAssert.AreEqual(new[] { "灵石×5", "下品丹药×1" }, TacticalCombatController.CreateDropItems(middle));
                CollectionAssert.AreEqual(new[] { "灵石×5", "中品丹药×1" }, TacticalCombatController.CreateDropItems(strong));
            }
            finally
            {
                Object.DestroyImmediate(weak);
                Object.DestroyImmediate(middle);
                Object.DestroyImmediate(strong);
            }
        }

        private static Character CreateCombatant(string name, string gongFa, HexCoord position, CTBEngine engine)
        {
            var character = new Character
            {
                Name = name,
                GongFaName = gongFa,
                Position = position,
                Reaction = 30,
                MaxHP = 100,
                CurrentHP = 100,
                MaxMP = 100,
                CurrentMP = 100,
                PhysAtk = 20,
                MagAtk = 20,
                PhysDef = 5,
                MagDef = 5,
                HitRateBonus = 100f,
                SpellCooldowns = new int[1],
                SkillCooldowns = new int[1],
            };
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }
    }

    public class AdventureSceneControllerTests
    {
        [Test]
        public void EncounterStateMovesBetweenExplorationCombatAndReturning()
        {
            var go = new GameObject("AdventureSceneControllerTests");
            try
            {
                var controller = go.AddComponent<AdventureSceneController>();

                Assert.AreEqual(AdventureSceneState.Loading, controller.CurrentState);
                controller.MarkExplorationReady();
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                controller.BeginEncounter();
                Assert.AreEqual(AdventureSceneState.Combat, controller.CurrentState);
                controller.CompleteEncounter();
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                controller.MarkReturning();
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }

    public class BattleUIManagerTests
    {
        [Test]
        public void ActionBarButtonsRouteThroughCombatCommandHandlerWhenBound()
        {
            var host = new GameObject("BattleUIManagerCommandTest");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);
                var handler = new RecordingCombatCommandHandler();
                ui.SetCombatCommandHandler(handler);

                FindButton("BtnAttack").onClick.Invoke();
                FindButton("BtnGuard").onClick.Invoke();
                FindButton("BtnWait").onClick.Invoke();
                FindButton("BtnSpell2").onClick.Invoke();
                FindButton("BtnSkill1").onClick.Invoke();

                Assert.AreEqual(1, handler.BasicAttackRequests);
                Assert.AreEqual(1, handler.GuardRequests);
                Assert.AreEqual(1, handler.WaitRequests);
                Assert.AreEqual(2, handler.LastSpellIndex);
                Assert.AreEqual(1, handler.LastSkillIndex);
            }
            finally
            {
                var canvas = GameObject.Find("UICanvas");
                if (canvas != null)
                    Object.DestroyImmediate(canvas);
                Object.DestroyImmediate(host);
            }
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
            {
                if (button.name == name)
                    return button;
            }

            Assert.Fail(name);
            return null;
        }

        private sealed class RecordingCombatCommandHandler : ICombatCommandHandler
        {
            public int BasicAttackRequests { get; private set; }
            public int GuardRequests { get; private set; }
            public int WaitRequests { get; private set; }
            public int LastSpellIndex { get; private set; } = -1;
            public int LastSkillIndex { get; private set; } = -1;

            public void RequestBasicAttack() => BasicAttackRequests++;
            public void RequestGuard() => GuardRequests++;
            public void RequestWait() => WaitRequests++;
            public void RequestSpell(int index) => LastSpellIndex = index;
            public void RequestSkill(int index) => LastSkillIndex = index;
        }
    }
}
