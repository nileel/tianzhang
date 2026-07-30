using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public class AttackProfileDataTests
    {
        private readonly List<AttackProfileData> profiles = new List<AttackProfileData>();

        [TearDown]
        public void DestroyProfiles()
        {
            foreach (var profile in profiles)
                Object.DestroyImmediate(profile);
            profiles.Clear();
        }

        [Test]
        public void StrictCsvValidationRejectsNonExactRowsWithoutWritingAssets()
        {
            string header = string.Join(",", new[]
            {
                "attackProfileId", "displayNameKey", "profileKind", "basicBindingKind",
                "contentScope", "sourceAffiliation", "realmRequirementId", "elementRequirementId",
                "effectType", "damageElementId", "physicalDamageMultiplier", "soulDamageMultiplier",
                "healAmount", "buffMultiplier", "defensePenetration", "resourceKind", "resourceCost",
                "cooldownTicks", "minCastRange", "maxCastRange", "targetingMode", "areaCenterKind",
                "areaShapeKind", "areaRadius", "areaLength", "areaFanHalfAngleSteps", "areaFacing",
                "areaInnerRadius", "areaEffectBlockers", "areaAllowedFactions", "areaAllowedStates",
                "isDomain", "isBloodline", "specialEffectTextKey",
            });
            var valid = new string[34];
            valid[0] = "basic_test";
            valid[1] = "name_test";
            valid[2] = "basic";
            valid[3] = "main_equipment";
            valid[8] = "physical";
            valid[9] = "element_none";
            valid[10] = "1";
            valid[15] = "none";
            valid[16] = "0";
            valid[17] = "0";
            valid[18] = "1";
            valid[19] = "1";
            valid[20] = "single";

            Assert.IsTrue(
                DataConfigImporter.TryValidateAttackProfileCsv(
                    new[] { header, string.Join(",", valid) },
                    new HashSet<string> { "name_test" },
                    out var validReason),
                validReason);
            Assert.IsFalse(
                DataConfigImporter.TryValidateAttackProfileCsv(
                    new[] { header, "basic_test,name_test,basic" },
                    new HashSet<string> { "name_test" },
                    out var invalidReason));
            StringAssert.StartsWith("attack_profile_short_or_extra_row", invalidReason);
        }

        [Test]
        public void TwoVsTwoSessionKeepsStableMembersAndRequiresExplicitEnemyTarget()
        {
            var engine = new CTBEngine();
            var controller = new TacticalCombatController(engine);
            var playerOne = CreateCombatant("玩家一", new HexCoord(0, 0), engine);
            var playerTwo = CreateCombatant("玩家二", new HexCoord(0, 1), engine);
            var enemyOne = CreateCombatant("敌人一", new HexCoord(1, 0), engine);
            var enemyTwo = CreateCombatant("敌人二", new HexCoord(1, 1), engine);
            var grid = new HexGrid();
            var anchors = new Dictionary<int, SpatialHexCoord>();
            foreach (var character in new[] { playerOne, playerTwo, enemyOne, enemyTwo })
            {
                grid.SetOccupied(character.Position, character.CTBUnit.Id);
                anchors.Add(character.CTBUnit.Id, new SpatialHexCoord(character.Position.q, character.Position.r));
            }

            var basic = CreateBasicProfile();
            var setup = new TacticalCombatSetup(
                new[] { playerOne, playerTwo },
                new[] { enemyOne, enemyTwo },
                SpatialQueryTestFixture.CreateOpenBoard(),
                anchors,
                new[] { basic });

            Assert.IsTrue(controller.TryBeginCombat(setup, grid, out var session, out var reason), reason);
            Assert.AreEqual(4, session.CreateActiveUnitList().Count);
            Assert.AreSame(playerTwo, session.Members[1].Character);
            Assert.AreSame(enemyOne, session.Members[2].Character);

            var selfTarget = controller.ExecuteBasicAttack(playerOne.CTBUnit.Id, playerTwo.CTBUnit.Id);
            Assert.IsFalse(selfTarget.Success);
            Assert.AreEqual("combat_session_target_invalid", selfTarget.Message);

            playerTwo.CTBUnit.CT = CTBEngine.ActionThreshold;
            var enemyTarget = controller.ExecuteBasicAttack(playerTwo.CTBUnit.Id, enemyTwo.CTBUnit.Id);
            Assert.IsTrue(enemyTarget.Success);
        }

        [Test]
        public void UnknownBasicAttackProfileIdRejectsSessionBeforeCombatStateChanges()
        {
            var engine = new CTBEngine();
            var controller = new TacticalCombatController(engine, new CombatResolver());
            var grid = new HexGrid();
            var player = CreateCombatant("玩家", new HexCoord(0, 0), engine);
            var enemy = CreateCombatant("敌人", new HexCoord(1, 0), engine);
            player.MainEquipmentBasicAttackProfileId = "basic_missing";
            player.BasicAttackProfileId = "basic_missing";
            enemy.MainEquipmentBasicAttackProfileId = "basic_missing";
            enemy.BasicAttackProfileId = "basic_missing";
            grid.SetOccupied(player.Position, player.CTBUnit.Id);
            grid.SetOccupied(enemy.Position, enemy.CTBUnit.Id);
            var anchors = new Dictionary<int, SpatialHexCoord>
            {
                [player.CTBUnit.Id] = new SpatialHexCoord(0, 0),
                [enemy.CTBUnit.Id] = new SpatialHexCoord(1, 0),
            };

            var setup = new TacticalCombatSetup(
                new[] { player },
                new[] { enemy },
                SpatialQueryTestFixture.CreateOpenBoard(),
                anchors,
                new[] { CreateBasicProfile() });

            Assert.IsFalse(controller.TryBeginCombat(setup, grid, out _, out var reason));
            Assert.AreEqual("basic_attack_profile_not_found", reason);
            Assert.AreEqual(0f, player.CTBUnit.CT);
        }

        [Test]
        public void ArtCooldownSlotMismatchFailsClosedWithoutUnitConversion()
        {
            var engine = new CTBEngine();
            var controller = new TacticalCombatController(engine, new CombatResolver());
            var grid = new HexGrid();
            var player = CreateCombatant("玩家", new HexCoord(0, 0), engine);
            var enemy = CreateCombatant("敌人", new HexCoord(1, 0), engine);
            var basic = CreateBasicProfile();
            var art = CreateSingleArt();
            player.EquippedSpellIds = new[] { art.attackProfileId };
            player.SpellCooldowns = System.Array.Empty<int>();
            grid.SetOccupied(player.Position, player.CTBUnit.Id);
            grid.SetOccupied(enemy.Position, enemy.CTBUnit.Id);
            var anchors = new Dictionary<int, SpatialHexCoord>
            {
                [player.CTBUnit.Id] = new SpatialHexCoord(0, 0),
                [enemy.CTBUnit.Id] = new SpatialHexCoord(1, 0),
            };
            var setup = new TacticalCombatSetup(
                new[] { player },
                new[] { enemy },
                SpatialQueryTestFixture.CreateOpenBoard(),
                anchors,
                new[] { basic, art });
            Assert.IsTrue(controller.TryBeginCombat(setup, grid, out _, out var reason), reason);

            var result = controller.ExecuteArt(player.CTBUnit.Id, enemy.CTBUnit.Id, 0, new[] { art });

            Assert.IsFalse(result.Success);
            Assert.AreEqual("attack_profile_cooldown_slot_invalid", result.Message);
        }

        [Test]
        public void AreaTargetingUsesSessionFactionAndAreaBoardPathWithoutLineOfSight()
        {
            var engine = new CTBEngine();
            var controller = new TacticalCombatController(engine);
            var player = CreateCombatant("玩家", new HexCoord(0, 0), engine);
            var enemyOne = CreateCombatant("敌人一", new HexCoord(1, 0), engine);
            var enemyTwo = CreateCombatant("敌人二", new HexCoord(1, 1), engine);
            var ally = CreateCombatant("同伴", new HexCoord(0, 1), engine);
            var grid = new HexGrid();
            var anchors = new Dictionary<int, SpatialHexCoord>();
            foreach (var character in new[] { player, ally, enemyOne, enemyTwo })
            {
                grid.SetOccupied(character.Position, character.CTBUnit.Id);
                anchors.Add(character.CTBUnit.Id, new SpatialHexCoord(character.Position.q, character.Position.r));
            }

            var basic = CreateBasicProfile();
            var area = CreateAreaArt();
            var setup = new TacticalCombatSetup(
                new[] { player, ally },
                new[] { enemyOne, enemyTwo },
                SpatialQueryTestFixture.CreateOpenBoard(),
                anchors,
                new[] { basic, area });
            Assert.IsTrue(controller.TryBeginCombat(setup, grid, out _, out var reason), reason);

            var result = controller.ResolveAreaTargets(player.CTBUnit.Id, area, enemyOne.Position);

            CollectionAssert.AreEquivalent(
                new[] { enemyOne.CTBUnit.Id, enemyTwo.CTBUnit.Id },
                result.HitUnitIds);
            Assert.AreEqual(string.Empty, result.RejectionReason);
        }

        private Character CreateCombatant(string name, HexCoord position, CTBEngine engine)
        {
            var character = new Character
            {
                Name = name,
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
                MainEquipmentBasicAttackProfileId = "basic_main",
                BasicAttackProfileId = "basic_main",
                BasicAttackBindingKind = "main_equipment",
            };
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }

        private AttackProfileData CreateBasicProfile()
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            profiles.Add(profile);
            profile.attackProfileId = "basic_main";
            profile.displayNameKey = "basic_main_name";
            profile.profileKind = AttackProfileKind.Basic;
            profile.basicBindingKind = BasicAttackBindingKind.MainEquipment;
            profile.effectType = AttackEffectType.Physical;
            profile.damageElementId = "element_none";
            profile.physicalDamageMultiplier = 1f;
            profile.resourceKind = AttackResourceKind.None;
            profile.minCastRange = 1;
            profile.maxCastRange = 1;
            profile.targetingMode = AttackTargetingMode.Single;
            profile.areaFacing = -1;
            return profile;
        }

        private AttackProfileData CreateAreaArt()
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            profiles.Add(profile);
            profile.attackProfileId = "area_art";
            profile.displayNameKey = "area_art_name";
            profile.profileKind = AttackProfileKind.Art;
            profile.contentScope = "player";
            profile.sourceAffiliation = "test";
            profile.realmRequirementId = "realm_fanren";
            profile.elementRequirementId = "element_none";
            profile.effectType = AttackEffectType.Magic;
            profile.damageElementId = "element_none";
            profile.soulDamageMultiplier = 1f;
            profile.resourceKind = AttackResourceKind.Mp;
            profile.resourceCost = 1;
            profile.cooldownTicks = 1;
            profile.minCastRange = 1;
            profile.maxCastRange = 1;
            profile.targetingMode = AttackTargetingMode.Area;
            profile.areaCenterKind = AttackAreaCenterKind.TargetCell;
            profile.areaShapeKind = AttackAreaShapeKind.Circle;
            profile.areaRadius = 1;
            profile.areaFacing = -1;
            profile.areaEffectBlockers = AttackAreaEffectBlocker.None;
            profile.areaAllowedFactions = AttackAreaTargetFaction.Enemy;
            profile.areaAllowedStates = AttackAreaTargetState.Alive;
            return profile;
        }

        private AttackProfileData CreateSingleArt()
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            profiles.Add(profile);
            profile.attackProfileId = "single_art";
            profile.displayNameKey = "single_art_name";
            profile.profileKind = AttackProfileKind.Art;
            profile.contentScope = "player";
            profile.sourceAffiliation = "test";
            profile.realmRequirementId = "realm_fanren";
            profile.elementRequirementId = "element_none";
            profile.effectType = AttackEffectType.Magic;
            profile.damageElementId = "element_none";
            profile.soulDamageMultiplier = 1f;
            profile.resourceKind = AttackResourceKind.None;
            profile.cooldownTicks = 1;
            profile.minCastRange = 1;
            profile.maxCastRange = 1;
            profile.targetingMode = AttackTargetingMode.Single;
            profile.areaFacing = -1;
            return profile;
        }
    }
}
