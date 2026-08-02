using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using TianZhang.Editor;
using TianZhang.Entity;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class AttackProfileDataTests
    {
        private const string AttackProfileFixtureRelativePath = "Assets/Tests/EditMode/Fixtures/AttackProfiles.fixture.csv";

        private readonly List<AttackProfileData> profiles = new List<AttackProfileData>();

        [TearDown]
        public void DestroyProfiles()
        {
            foreach (var profile in profiles)
                UnityEngine.Object.DestroyImmediate(profile);
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
        public void FixtureRowsProjectExplicitlyWithoutWritingAssets()
        {
            var lines = File.ReadAllLines(FixtureAbsolutePath());
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            Assert.IsTrue(
                DataConfigImporter.TryBuildAttackProfileProjection(lines, languageKeys, out var projected, out var reason),
                reason);
            try
            {
                Assert.AreEqual(6, projected.Length);
                var byId = projected.ToDictionary(profile => profile.attackProfileId, StringComparer.Ordinal);
                var main = byId["basic_fixture_main"];
                Assert.AreEqual(AttackProfileKind.Basic, main.profileKind);
                Assert.AreEqual(BasicAttackBindingKind.MainEquipment, main.basicBindingKind);
                Assert.AreEqual(AttackEffectType.Physical, main.effectType);
                Assert.AreEqual("element_none", main.damageElementId);
                Assert.AreEqual(1f, main.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(AttackResourceKind.None, main.resourceKind);
                Assert.AreEqual(0, main.resourceCost);
                Assert.AreEqual(0, main.cooldownTicks);
                Assert.AreEqual(1, main.minCastRange);
                Assert.AreEqual(1, main.maxCastRange);
                Assert.AreEqual(AttackTargetingMode.Single, main.targetingMode);
                Assert.AreEqual(AttackAreaShapeKind.Unknown, main.areaShapeKind);
                Assert.AreEqual(-1, main.areaFacing);

                var unarmed = byId["basic_fixture_unarmed"];
                Assert.AreEqual(BasicAttackBindingKind.UnarmedFallback, unarmed.basicBindingKind);
                Assert.AreEqual(0.9f, unarmed.physicalDamageMultiplier, 0.0001f);

                var artSingle = byId["art_fixture_single"];
                Assert.AreEqual(AttackProfileKind.Art, artSingle.profileKind);
                Assert.AreEqual(BasicAttackBindingKind.Unknown, artSingle.basicBindingKind);
                Assert.AreEqual("player", artSingle.contentScope);
                Assert.AreEqual("fixture_sect", artSingle.sourceAffiliation);
                Assert.AreEqual("realm_fanren", artSingle.realmRequirementId);
                Assert.AreEqual("element_none", artSingle.elementRequirementId);
                Assert.AreEqual(AttackEffectType.Magic, artSingle.effectType);
                Assert.AreEqual("element_water", artSingle.damageElementId);
                Assert.AreEqual(0f, artSingle.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(1.5f, artSingle.soulDamageMultiplier, 0.0001f);
                Assert.AreEqual(AttackResourceKind.Mp, artSingle.resourceKind);
                Assert.AreEqual(8, artSingle.resourceCost);
                Assert.AreEqual(0, artSingle.cooldownTicks);
                Assert.AreEqual(2, artSingle.minCastRange);
                Assert.AreEqual(3, artSingle.maxCastRange);
                Assert.AreEqual(AttackTargetingMode.Single, artSingle.targetingMode);
                Assert.AreEqual(AttackAreaShapeKind.Unknown, artSingle.areaShapeKind);
                Assert.AreEqual(-1, artSingle.areaFacing);

                var circle = byId["art_fixture_circle"];
                Assert.AreEqual(AttackTargetingMode.Area, circle.targetingMode);
                Assert.AreEqual(AttackAreaCenterKind.Caster, circle.areaCenterKind);
                Assert.AreEqual(AttackAreaShapeKind.Circle, circle.areaShapeKind);
                Assert.AreEqual(2, circle.areaRadius);
                Assert.AreEqual(0, circle.areaLength);
                Assert.AreEqual(0, circle.areaFanHalfAngleSteps);
                Assert.AreEqual(-1, circle.areaFacing);
                Assert.AreEqual(1, circle.areaInnerRadius);
                Assert.AreEqual(AttackAreaEffectBlocker.None, circle.areaEffectBlockers);
                Assert.AreEqual(AttackAreaTargetFaction.Enemy, circle.areaAllowedFactions);
                Assert.AreEqual(AttackAreaTargetState.Alive, circle.areaAllowedStates);

                var fan = byId["art_fixture_fan"];
                Assert.AreEqual(AttackAreaCenterKind.TargetCell, fan.areaCenterKind);
                Assert.AreEqual(AttackAreaShapeKind.Fan, fan.areaShapeKind);
                Assert.AreEqual(0, fan.areaRadius);
                Assert.AreEqual(2, fan.areaLength);
                Assert.AreEqual(1, fan.areaFanHalfAngleSteps);
                Assert.AreEqual(1, fan.areaFacing);
                Assert.AreEqual(0, fan.areaInnerRadius);
                // fixture 的 fan 行使用单值阵营（ally）：BattleSim 侧按原 CSV 行逐字
                // Split(',') 读取同一物理行，双值阵营字段（"ally,enemy"）无法在该
                // 读取方式下保持 34 列对齐，故按契约允许的子集以 ally 单值承载。
                Assert.AreEqual(AttackAreaTargetFaction.Ally, fan.areaAllowedFactions);

                var line = byId["divine_fixture_line"];
                Assert.AreEqual(AttackProfileKind.Divine, line.profileKind);
                Assert.AreEqual("realm_zhuji", line.realmRequirementId);
                Assert.AreEqual("element_fire", line.elementRequirementId);
                Assert.AreEqual(AttackEffectType.Physical, line.effectType);
                Assert.AreEqual("element_fire", line.damageElementId);
                Assert.AreEqual(0.8f, line.physicalDamageMultiplier, 0.0001f);
                Assert.AreEqual(0f, line.soulDamageMultiplier, 0.0001f);
                Assert.AreEqual(2.5f, line.defensePenetration, 0.0001f);
                Assert.AreEqual(12, line.resourceCost);
                Assert.AreEqual(0, line.cooldownTicks);
                Assert.AreEqual(2, line.minCastRange);
                Assert.AreEqual(4, line.maxCastRange);
                Assert.AreEqual(AttackAreaShapeKind.Line, line.areaShapeKind);
                Assert.AreEqual(3, line.areaLength);
                Assert.AreEqual(0, line.areaFacing);
                Assert.AreEqual(1, line.areaInnerRadius);
                Assert.AreEqual(AttackAreaEffectBlocker.DirectedEdge, line.areaEffectBlockers);
                Assert.IsTrue(line.isDomain);
                Assert.IsFalse(line.isBloodline);
                Assert.AreEqual("special_fixture_divine", line.specialEffectTextKey);
            }
            finally
            {
                foreach (var profile in projected)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FixtureRowVariantsShareOnePerColumnMutationFailureWithNoPartialProjection()
        {
            var lines = File.ReadAllLines(FixtureAbsolutePath());
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            var fixtureLine = lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal));
            var fixtureColumns = fixtureLine.Split(',');
            var originalBasicLine = lines.First(line => line.StartsWith("basic_fixture_main,", System.StringComparison.Ordinal));
            var basicColumns = originalBasicLine.Split(',');
            var originalLineLine = lines.First(line => line.StartsWith("divine_fixture_line,", System.StringComparison.Ordinal));
            var lineColumns = originalLineLine.Split(',');
            var originalCircleLine = lines.First(line => line.StartsWith("art_fixture_circle,", System.StringComparison.Ordinal));
            var circleColumns = originalCircleLine.Split(',');

            void AssertRejects(string[] mutated, string expectedPrefix)
            {
                var header = lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'));
                var mutatedLines = lines.Where(line =>
                    !line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal) &&
                    !line.Equals(header, System.StringComparison.Ordinal) &&
                    !line.TrimStart().StartsWith('#')).ToArray();
                var combined = new[] { header }.Concat(mutatedLines).Concat(new[] { string.Join(",", mutated) }).ToArray();
                Assert.IsFalse(
                    DataConfigImporter.TryBuildAttackProfileProjection(combined, languageKeys, out var rejected, out var reason),
                    $"mutated row must fail: {expectedPrefix}");
                StringAssert.StartsWith(expectedPrefix, reason);
                Assert.IsEmpty(rejected);
            }

            var duplicateId = (string[])fixtureColumns.Clone();
            duplicateId[0] = "basic_fixture_main";
            AssertRejects(duplicateId, "attack_profile_id_duplicate");

            var unknownRealm = (string[])fixtureColumns.Clone();
            unknownRealm[6] = "realm_unknown";
            AssertRejects(unknownRealm, "attack_profile_requirement_reference_unknown");

            var unknownElement = (string[])fixtureColumns.Clone();
            unknownElement[7] = "element_unknown";
            AssertRejects(unknownElement, "attack_profile_requirement_reference_unknown");

            var unknownDisplay = (string[])fixtureColumns.Clone();
            unknownDisplay[1] = "name_unknown";
            AssertRejects(unknownDisplay, "attack_profile_display_key_unknown");

            var negativeCost = (string[])fixtureColumns.Clone();
            negativeCost[16] = "-1";
            AssertRejects(negativeCost, "attack_profile_resourceCost_invalid");

            var reversedRange = (string[])fixtureColumns.Clone();
            reversedRange[18] = "4";
            reversedRange[19] = "2";
            AssertRejects(reversedRange, "attack_profile_cast_range_invalid");

            var nonNumericMult = (string[])fixtureColumns.Clone();
            nonNumericMult[11] = "abc";
            AssertRejects(nonNumericMult, "attack_profile_soulDamageMultiplier_invalid");

            var wrongKindFields = (string[])basicColumns.Clone();
            wrongKindFields[4] = "player";
            AssertRejects(wrongKindFields, "attack_profile_contentScope_must_be_empty");

            var badCircleShape = (string[])circleColumns.Clone();
            badCircleShape[26] = "east";
            AssertRejects(badCircleShape, "attack_profile_areaFacing_must_be_empty");

            var badLineShape = (string[])lineColumns.Clone();
            badLineShape[23] = "1";
            AssertRejects(badLineShape, "area_shape_contract_invalid");

            var badFanHalfAngle = (string[])circleColumns.Clone();
            badFanHalfAngle[23] = "0";
            badFanHalfAngle[25] = "2";
            badFanHalfAngle[26] = "east";
            badFanHalfAngle[22] = "fan";
            AssertRejects(badFanHalfAngle, "area_shape_contract_invalid");

            var unknownFacing = (string[])lineColumns.Clone();
            unknownFacing[26] = "north";
            AssertRejects(unknownFacing, "attack_profile_area_facing_invalid");

            var unknownShape = (string[])circleColumns.Clone();
            unknownShape[22] = "hex";
            AssertRejects(unknownShape, "attack_profile_area_shape_invalid");

            var singleWithAreaColumn = (string[])fixtureColumns.Clone();
            singleWithAreaColumn[21] = "caster";
            AssertRejects(singleWithAreaColumn, "attack_profile_areaCenterKind_must_be_empty");

            var unknownBlocker = (string[])circleColumns.Clone();
            unknownBlocker[28] = "sight_blocked";
            AssertRejects(unknownBlocker, "attack_profile_area_blocker_invalid");
        }

        [Test]
        public void ProjectionFailureLeavesNoPartialAssetAndNoFallbackDefaults()
        {
            var lines = File.ReadAllLines(FixtureAbsolutePath());
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            var duplicateLines = lines.Concat(new[] { lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal)) }).ToArray();
            Assert.IsFalse(
                DataConfigImporter.TryBuildAttackProfileProjection(duplicateLines, languageKeys, out var rejected, out _));
            Assert.IsEmpty(rejected);

            var singleRow = new[]
            {
                lines.First(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#')),
                lines.First(line => line.StartsWith("art_fixture_single,", System.StringComparison.Ordinal)),
            };
            Assert.IsTrue(
                DataConfigImporter.TryBuildAttackProfileProjection(singleRow, languageKeys, out var single, out var reason),
                reason);
            try
            {
                Assert.AreEqual(1, single.Length);
                Assert.AreEqual("art_fixture_single", single[0].attackProfileId);
            }
            finally
            {
                foreach (var profile in single)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static string FixtureAbsolutePath()
        {
            return Path.Combine(Application.dataPath, "..", AttackProfileFixtureRelativePath);
        }

        private static string[] LanguageFixtureKeys()
        {
            return new[]
            {
                "name_fixture_basic_main",
                "name_fixture_basic_unarmed",
                "name_fixture_art_single",
                "name_fixture_art_circle",
                "name_fixture_art_fan",
                "name_fixture_divine_line",
                "special_fixture_divine",
            };
        }

        [Test]
        public void AssetIdConflictOnCanonicalPathFailsClosedWithoutMutation()
        {
            // 非规范路径上存在同 ID asset：同一 attackProfileId 映射到多个 asset 路径必须失败。
            // 规范目录 Assets/Data/AttackProfiles/ 尚无生产 asset，测试在既有 Fixtures 旁创建并清理临时 asset。
            const string strayAssetPath = "Assets/Tests/EditMode/AttackProfile_stray_fixture.asset";
            AssetDatabase.DeleteAsset(strayAssetPath);
            AssetDatabase.Refresh();

            var lines = File.ReadAllLines(FixtureAbsolutePath());
            var languageKeys = new HashSet<string>(LanguageFixtureKeys(), StringComparer.Ordinal);
            Assert.IsTrue(
                DataConfigImporter.TryBuildAttackProfileProjection(lines, languageKeys, out var projected, out var reason),
                reason);
            foreach (var profile in projected)
                UnityEngine.Object.DestroyImmediate(profile);
            Assert.IsTrue(
                DataConfigImporter.TryValidateAttackProfileAssetIdProjection(
                    new[] { "basic_fixture_main" }, out var cleanReason),
                cleanReason);

            var stray = ScriptableObject.CreateInstance<AttackProfileData>();
            stray.attackProfileId = "basic_fixture_main";
            stray.displayNameKey = "name_fixture_basic_main";
            stray.profileKind = AttackProfileKind.Basic;
            stray.basicBindingKind = BasicAttackBindingKind.MainEquipment;
            stray.effectType = AttackEffectType.Physical;
            stray.damageElementId = "element_none";
            stray.physicalDamageMultiplier = 1f;
            stray.resourceKind = AttackResourceKind.None;
            stray.minCastRange = 1;
            stray.maxCastRange = 1;
            stray.targetingMode = AttackTargetingMode.Single;
            stray.areaFacing = -1;
            try
            {
                AssetDatabase.CreateAsset(stray, strayAssetPath);
                Assert.IsFalse(
                    DataConfigImporter.TryValidateAttackProfileAssetIdProjection(
                        new[] { "basic_fixture_main" }, out var conflictReason),
                    "same attackProfileId on a non-canonical path must fail the projection check");
                StringAssert.AreEqualIgnoringCase("attack_profile_asset_id_duplicate", conflictReason);
            }
            finally
            {
                AssetDatabase.DeleteAsset(strayAssetPath);
                AssetDatabase.Refresh();
            }
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
