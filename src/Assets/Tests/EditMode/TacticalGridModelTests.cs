using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Entity;
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
}
