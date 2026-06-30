using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.TestTools;
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
}
