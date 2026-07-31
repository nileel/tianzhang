using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TianZhang.Adventure;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.Tactical;
using UnityEditor.SceneManagement;

namespace TianZhang.Tests
{
    internal static class SpatialQueryTestFixture
    {
        public static SpatialQueryBoard CreateOpenBoard(int radius = 6)
        {
            var cells = new Dictionary<SpatialHexCoord, SpatialCellRules>();
            for (int q = -radius; q <= radius; q++)
            {
                int minimumR = System.Math.Max(-radius, -q - radius);
                int maximumR = System.Math.Min(radius, -q + radius);
                for (int r = minimumR; r <= maximumR; r++)
                {
                    var coord = new SpatialHexCoord(q, r);
                    cells.Add(coord, new SpatialCellRules(0, false, false, false, 0));
                }
            }

            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>();
            foreach (var from in cells.Keys)
            {
                foreach (var to in from.Neighbors())
                {
                    if (cells.ContainsKey(to))
                        edges.Add(new SpatialDirectedEdge(from, to), new SpatialEdgeRules(2, true, true));
                }
            }
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }

        public static SpatialQueryBoard CreateCompressedLineBoard()
        {
            var origin = new SpatialHexCoord(0, 0);
            var east = new SpatialHexCoord(1, 0);
            var eastTwo = new SpatialHexCoord(2, 0);
            var cells = new Dictionary<SpatialHexCoord, SpatialCellRules>
            {
                [origin] = new SpatialCellRules(0, false, false, false, 0),
                [east] = new SpatialCellRules(0, false, false, false, 0),
                [eastTwo] = new SpatialCellRules(0, false, false, false, 0),
            };
            var edges = new Dictionary<SpatialDirectedEdge, SpatialEdgeRules>
            {
                [new SpatialDirectedEdge(origin, east)] = new SpatialEdgeRules(1, true, true),
                [new SpatialDirectedEdge(east, eastTwo)] = new SpatialEdgeRules(1, true, true),
            };
            return new SpatialQueryBoard(cells, edges, new SpatialQueryLimits(2, 16));
        }
    }

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

        [Test]
        public void EnvironmentProfileProjectionProvidesOnlyConfiguredRuntimeInputs()
        {
            var model = new TacticalGridModel();
            var profile = CreateEnvironmentProfile();
            try
            {
                Assert.IsTrue(model.TryConfigureEnvironmentProfile(profile, out var reason), reason);
                Assert.AreEqual("runtime_fixture", model.EnvironmentRules.ProfileId);
                Assert.IsTrue(model.EnvironmentRules.IsSurfacePrototypeConfigured("surface_grassland", out var surfaceReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, surfaceReason);
                Assert.IsFalse(model.EnvironmentRules.IsSurfacePrototypeConfigured("surface_default", out surfaceReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.SurfacePrototypeNotConfigured, surfaceReason);
                Assert.IsTrue(model.EnvironmentRules.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "wind",
                    out var pairingResult,
                    out var pairingReason));
                Assert.AreEqual("gust", pairingResult);
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, pairingReason);
                Assert.IsFalse(model.EnvironmentRules.TryResolvePhenomenonPair(
                    EnvironmentPhenomenonChannel.Airflow,
                    "gust",
                    "breeze",
                    out _,
                    out pairingReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.PhenomenonPairNotConfigured, pairingReason);
                Assert.IsTrue(model.EnvironmentRules.IsElementRelationConfigured("element_wood", out var elementReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.Ok, elementReason);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void InvalidEnvironmentProfileClearsPreviouslyBoundRuntimeRules()
        {
            var model = new TacticalGridModel();
            var valid = CreateEnvironmentProfile();
            var invalid = CreateEnvironmentProfile();
            try
            {
                Assert.IsTrue(model.TryConfigureEnvironmentProfile(valid, out var validReason), validReason);
                invalid.surfacePrototypeRefs = new[] { "surface_grassland", "surface_grassland" };

                Assert.IsFalse(model.TryConfigureEnvironmentProfile(invalid, out var invalidReason));
                Assert.AreEqual(EnvironmentRuntimeReasons.SurfacePrototypesNotConfigured, invalidReason);
                Assert.IsNull(model.EnvironmentRules);
            }
            finally
            {
                Object.DestroyImmediate(invalid);
                Object.DestroyImmediate(valid);
            }
        }

        private static EnvironmentProfileData CreateEnvironmentProfile()
        {
            var profile = ScriptableObject.CreateInstance<EnvironmentProfileData>();
            profile.profileId = "runtime_fixture";
            profile.unitsPerRange = 2;
            profile.maxQueryRange = 16;
            profile.directedEdges = new[]
            {
                new EnvironmentDirectedEdge
                {
                    fromQ = 0,
                    fromR = 0,
                    toQ = 1,
                    toR = 0,
                    metricDistanceUnits = 2,
                    allowsMovement = true,
                    allowsEffects = true,
                },
            };
            profile.surfacePrototypeRefs = new[] { "surface_grassland", "surface_loess" };
            profile.phenomenonChannels = new[]
            {
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Airflow,
                    phenomenonTypeRefs = new[] { "wind", "gust", "breeze" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Visibility,
                    phenomenonTypeRefs = new[] { "mist" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Temperature,
                    phenomenonTypeRefs = new[] { "heat" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.Precipitation,
                    phenomenonTypeRefs = new[] { "rain" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.SuspendedHazard,
                    phenomenonTypeRefs = new[] { "ash" },
                },
                new EnvironmentPhenomenonChannelData
                {
                    channel = EnvironmentPhenomenonChannel.CloudDischarge,
                    phenomenonTypeRefs = new[] { "storm" },
                },
            };
            profile.phenomenonPairs = new[]
            {
                new EnvironmentPhenomenonPairing
                {
                    channel = EnvironmentPhenomenonChannel.Airflow,
                    firstTypeRef = "wind",
                    secondTypeRef = "gust",
                    resultTypeRef = "gust",
                },
            };
            profile.elementRelationRefs = new[]
            {
                "element_wood",
                "element_fire",
                "element_earth",
                "element_metal",
                "element_water",
            };
            return profile;
        }
    }

    public class CombatMechanismTests
    {
        private static AttackProfileData CreateBasicProfile(AttackEffectType effectType)
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.attackProfileId = effectType == AttackEffectType.Magic
                ? "test_basic_magic"
                : "test_basic_physical";
            profile.displayNameKey = effectType == AttackEffectType.Magic ? "神魂攻击" : "物理攻击";
            profile.profileKind = AttackProfileKind.Basic;
            profile.basicBindingKind = BasicAttackBindingKind.MainEquipment;
            profile.effectType = effectType;
            profile.damageElementId = "element_none";
            profile.physicalDamageMultiplier = effectType is AttackEffectType.Physical or AttackEffectType.Hybrid ? 1f : 0f;
            profile.soulDamageMultiplier = effectType is AttackEffectType.Magic or AttackEffectType.Hybrid ? 1f : 0f;
            profile.resourceKind = AttackResourceKind.None;
            profile.minCastRange = 1;
            profile.maxCastRange = 3;
            profile.targetingMode = AttackTargetingMode.Single;
            profile.areaFacing = -1;
            return profile;
        }

        private static AttackProfileData CreateDivineMagicProfile(string displayNameKey)
        {
            var profile = CreateBasicProfile(AttackEffectType.Magic);
            profile.attackProfileId = "test_divine_magic";
            profile.displayNameKey = displayNameKey;
            profile.profileKind = AttackProfileKind.Divine;
            profile.basicBindingKind = BasicAttackBindingKind.Unknown;
            profile.contentScope = "player";
            profile.sourceAffiliation = "test";
            profile.realmRequirementId = "realm_fanren";
            profile.elementRequirementId = "element_none";
            return profile;
        }

        [Test]
        public void CritDamageUsesBaseOnePointFivePlusPercentagePointBonuses()
        {
            Assert.AreEqual(1.50f, DamageCalculator.GetCritMultiplier(0f), 0.0001f);
            Assert.AreEqual(1.65f, DamageCalculator.GetCritMultiplier(15f), 0.0001f);
            Assert.AreEqual(1.75f, DamageCalculator.GetCritMultiplier(15f, 10f), 0.0001f);
        }

        [Test]
        public void FullFudanBoostsAndConsumesMagicDivineSkill()
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };
            var skill = CreateDivineMagicProfile("符胆神通回归");

            var baseCaster = CreateFudanCaster(engine, "无符胆", 0);
            var baseTarget = CreateTarget(engine, "基准目标");
            LogAssert.Expect(LogType.Log, new Regex("无符胆 神通 符胆神通回归.*基准目标"));
            var baseResult = resolver.UseSkill(baseCaster, baseTarget, 0, skill);

            var fullCaster = CreateFudanCaster(engine, "满符胆", 5);
            var fullTarget = CreateTarget(engine, "满层目标");
            LogAssert.Expect(LogType.Log, new Regex("满符胆 神通 符胆神通回归.*满层目标"));
            var fullResult = resolver.UseSkill(fullCaster, fullTarget, 0, skill);

            Assert.IsTrue(baseResult.Success);
            Assert.IsTrue(fullResult.Success);
            Assert.Greater(fullResult.Damage.FinalDamage, baseResult.Damage.FinalDamage);
            Assert.AreEqual(1, fullCaster.FudanStacks);
        }

        [Test]
        public void LeijieDamageTakenBoostsNextPhysicalAttackAndThenConsumes()
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };

            var baseAttacker = CreateLeijieCombatant(engine, "未蓄雷", new HexCoord(0, 0));
            var baseTarget = CreateTarget(engine, "基准目标");
            LogAssert.Expect(LogType.Log, new Regex("未蓄雷 攻击 物理攻击.*基准目标"));
            var baseResult = resolver.BasicAttack(baseAttacker, baseTarget, CreateBasicProfile(AttackEffectType.Physical));

            var chargedAttacker = CreateLeijieCombatant(engine, "已蓄雷", new HexCoord(0, 0));
            chargedAttacker.TakeDamage(1);
            chargedAttacker.TakeDamage(1);
            chargedAttacker.TakeDamage(1);

            var chargedTarget = CreateTarget(engine, "蓄雷目标");
            LogAssert.Expect(LogType.Log, new Regex("已蓄雷 攻击 物理攻击.*蓄雷目标"));
            var chargedResult = resolver.BasicAttack(chargedAttacker, chargedTarget, CreateBasicProfile(AttackEffectType.Physical));

            var spentTarget = CreateTarget(engine, "耗尽目标");
            LogAssert.Expect(LogType.Log, new Regex("已蓄雷 攻击 物理攻击.*耗尽目标"));
            var spentResult = resolver.BasicAttack(chargedAttacker, spentTarget, CreateBasicProfile(AttackEffectType.Physical));

            Assert.IsTrue(baseResult.Success);
            Assert.IsTrue(chargedResult.Success);
            Assert.IsTrue(spentResult.Success);
            Assert.Greater(chargedResult.Damage.FinalDamage, baseResult.Damage.FinalDamage);
            Assert.AreEqual(baseResult.Damage.FinalDamage, spentResult.Damage.FinalDamage);
        }

        [Test]
        public void FullLeijieStackReducesMagicDefenseAgainstMagicAttack()
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };
            var caster = CreateMagicCaster(engine, "神魂测试者", new HexCoord(0, 0));

            var freshDefender = CreateLeijieCombatant(engine, "未满雷劫", new HexCoord(1, 0));
            LogAssert.Expect(LogType.Log, new Regex("神魂测试者 攻击 神魂攻击.*未满雷劫"));
            var freshResult = resolver.BasicAttack(caster, freshDefender, CreateBasicProfile(AttackEffectType.Magic));

            var chargedDefender = CreateLeijieCombatant(engine, "满层雷劫", new HexCoord(1, 0));
            for (int i = 0; i < 5; i++)
                chargedDefender.TakeDamage(1);

            LogAssert.Expect(LogType.Log, new Regex("神魂测试者 攻击 神魂攻击.*满层雷劫"));
            var chargedResult = resolver.BasicAttack(caster, chargedDefender, CreateBasicProfile(AttackEffectType.Magic));

            Assert.IsTrue(freshResult.Success);
            Assert.IsTrue(chargedResult.Success);
            Assert.Greater(chargedResult.Damage.FinalDamage, freshResult.Damage.FinalDamage);
        }

        [Test]
        public void XuanganAddsRealmMindStrengthToMagicDamage()
        {
            AssertXuanganAddsRealmMindStrengthToMagicDamage(expectLogs: true);
        }

        [Test]
        public void HanhongPhysicalDefenseBonusReducesPhysicalDamageAtFullHp()
        {
            AssertHanhongPhysicalDefenseBonusReducesPhysicalDamageAtFullHp(expectLogs: true);
        }

        [Test]
        public void ZaiwuMissingHpIncreasesPhysicalAndMagicDefense()
        {
            AssertZaiwuMissingHpIncreasesPhysicalAndMagicDefense(expectLogs: true);
        }

        internal static void AssertXuanganAddsRealmMindStrengthToMagicDamage(bool expectLogs)
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };

            var normalCaster = CreateMagicCaster(engine, "普通神魂", new HexCoord(0, 0), "含弘光大典");
            var normalTarget = CreateTarget(engine, "普通目标");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("普通神魂 攻击 神魂攻击.*普通目标"));
            var normalResult = resolver.BasicAttack(normalCaster, normalTarget, CreateBasicProfile(AttackEffectType.Magic));

            var xuanganCaster = CreateMagicCaster(engine, "玄感神魂", new HexCoord(0, 0), "南华玄感录");
            var xuanganTarget = CreateTarget(engine, "玄感目标");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("玄感神魂 攻击 神魂攻击.*玄感目标"));
            var xuanganResult = resolver.BasicAttack(xuanganCaster, xuanganTarget, CreateBasicProfile(AttackEffectType.Magic));

            Assert.IsTrue(normalResult.Success);
            Assert.IsTrue(xuanganResult.Success);
            Assert.Greater(xuanganResult.Damage.FinalDamage, normalResult.Damage.FinalDamage);
        }

        internal static void AssertHanhongPhysicalDefenseBonusReducesPhysicalDamageAtFullHp(bool expectLogs)
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };
            var attacker = CreateLeijieCombatant(engine, "物理测试者", new HexCoord(0, 0));

            var neutralDefender = CreateTarget(engine, "普通防御", "秋水游心经");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("物理测试者 攻击 物理攻击.*普通防御"));
            var neutralResult = resolver.BasicAttack(attacker, neutralDefender, CreateBasicProfile(AttackEffectType.Physical));

            var hanhongDefender = CreateTarget(engine, "含弘防御", "含弘光大典");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("物理测试者 攻击 物理攻击.*含弘防御"));
            var hanhongResult = resolver.BasicAttack(attacker, hanhongDefender, CreateBasicProfile(AttackEffectType.Physical));

            Assert.IsTrue(neutralResult.Success);
            Assert.IsTrue(hanhongResult.Success);
            Assert.Less(hanhongResult.Damage.FinalDamage, neutralResult.Damage.FinalDamage);
        }

        internal static void AssertZaiwuMissingHpIncreasesPhysicalAndMagicDefense(bool expectLogs)
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateOpenBoard(),
            };

            var physicalAttacker = CreateLeijieCombatant(engine, "载物物理", new HexCoord(0, 0));
            var fullPhysicalTarget = CreateTarget(engine, "满血物防", "含弘光大典");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("载物物理 攻击 物理攻击.*满血物防"));
            var fullPhysical = resolver.BasicAttack(physicalAttacker, fullPhysicalTarget, CreateBasicProfile(AttackEffectType.Physical));

            var lowPhysicalTarget = CreateTarget(engine, "残血物防", "含弘光大典");
            lowPhysicalTarget.CurrentHP = lowPhysicalTarget.MaxHP / 2;
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("载物物理 攻击 物理攻击.*残血物防"));
            var lowPhysical = resolver.BasicAttack(physicalAttacker, lowPhysicalTarget, CreateBasicProfile(AttackEffectType.Physical));

            var fullMagicCaster = CreateMagicCaster(engine, "载物神魂满", new HexCoord(0, 0), "抱元守一经");
            var fullMagicTarget = CreateTarget(engine, "满血魂防", "含弘光大典");
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("载物神魂满 攻击 神魂攻击.*满血魂防"));
            var fullMagic = resolver.BasicAttack(fullMagicCaster, fullMagicTarget, CreateBasicProfile(AttackEffectType.Magic));

            var lowMagicCaster = CreateMagicCaster(engine, "载物神魂残", new HexCoord(0, 0), "抱元守一经");
            var lowMagicTarget = CreateTarget(engine, "残血魂防", "含弘光大典");
            lowMagicTarget.CurrentHP = lowMagicTarget.MaxHP / 2;
            if (expectLogs)
                LogAssert.Expect(LogType.Log, new Regex("载物神魂残 攻击 神魂攻击.*残血魂防"));
            var lowMagic = resolver.BasicAttack(lowMagicCaster, lowMagicTarget, CreateBasicProfile(AttackEffectType.Magic));

            Assert.IsTrue(fullPhysical.Success);
            Assert.IsTrue(lowPhysical.Success);
            Assert.IsTrue(fullMagic.Success);
            Assert.IsTrue(lowMagic.Success);
            Assert.Less(lowPhysical.Damage.FinalDamage, fullPhysical.Damage.FinalDamage);
            Assert.Less(lowMagic.Damage.FinalDamage, fullMagic.Damage.FinalDamage);
        }

        [Test]
        public void CombatResolverFailsClosedWithoutSpatialQueryConfiguration()
        {
            var engine = new CTBEngine();
            var attacker = CreateLeijieCombatant(engine, "未配置攻击者", new HexCoord(0, 0));
            var target = CreateTarget(engine, "未配置目标");

            var result = new CombatResolver { Engine = engine }.BasicAttack(
                attacker,
                target,
                CreateBasicProfile(AttackEffectType.Physical));

            Assert.IsFalse(result.Success);
            Assert.AreEqual("目标不在射程范围", result.Message);
        }

        [Test]
        public void CombatResolverUsesWeightedRangeInsteadOfHexDistance()
        {
            var engine = new CTBEngine();
            var resolver = new CombatResolver
            {
                Engine = engine,
                SpatialBoard = SpatialQueryTestFixture.CreateCompressedLineBoard(),
            };
            var attacker = CreateLeijieCombatant(engine, "压缩边攻击者", new HexCoord(0, 0));
            var target = CreateTarget(engine, "压缩边目标");
            target.Position = new HexCoord(2, 0);
            LogAssert.Expect(LogType.Log, new Regex("压缩边攻击者 攻击 物理攻击.*压缩边目标"));

            var result = resolver.BasicAttack(attacker, target, CreateBasicProfile(AttackEffectType.Physical));

            Assert.IsTrue(result.Success);
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

        private static Character CreateLeijieCombatant(CTBEngine engine, string name, HexCoord position)
        {
            var character = new Character
            {
                Name = name,
                GongFaName = "九霄雷劫录",
                MaxHP = 1000,
                CurrentHP = 1000,
                MaxMP = 100,
                CurrentMP = 100,
                PhysAtk = 200,
                MagAtk = 50,
                PhysDef = 100,
                MagDef = 200,
                Reaction = 100,
                HitRateBonus = 100f,
                CritRate = 0f,
                CritDamage = 0f,
                BlockRate = 0f,
                SoulShieldRate = 0f,
                DodgeRate = 0f,
                Position = position,
            };
            character.SetRealm("金丹");
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }

        private static Character CreateMagicCaster(CTBEngine engine, string name, HexCoord position)
        {
            return CreateMagicCaster(engine, name, position, "南华玄感录");
        }

        private static Character CreateMagicCaster(CTBEngine engine, string name, HexCoord position, string gongFaName)
        {
            var character = new Character
            {
                Name = name,
                GongFaName = gongFaName,
                MaxHP = 1000,
                CurrentHP = 1000,
                MaxMP = 100,
                CurrentMP = 100,
                PhysAtk = 50,
                MagAtk = 300,
                PhysDef = 50,
                MagDef = 50,
                Reaction = 100,
                HitRateBonus = 100f,
                CritRate = 0f,
                CritDamage = 0f,
                Position = position,
            };
            character.SetRealm("金丹");
            character.CTBUnit = engine.RegisterUnit(character.Reaction, character);
            return character;
        }

        private static Character CreateTarget(CTBEngine engine, string name, string gongFaName = "含弘光大典")
        {
            var character = new Character
            {
                Name = name,
                GongFaName = gongFaName,
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

    public static class CombatMechanismBatchRunner
    {
        public static void RunXuanganMindStrength()
        {
            try
            {
                CombatMechanismTests.AssertXuanganAddsRealmMindStrengthToMagicDamage(expectLogs: false);
                Debug.Log("CombatMechanismBatchRunner.RunXuanganMindStrength passed.");
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        public static void RunHanhongZaiwuDefense()
        {
            try
            {
                CombatMechanismTests.AssertHanhongPhysicalDefenseBonusReducesPhysicalDamageAtFullHp(expectLogs: false);
                CombatMechanismTests.AssertZaiwuMissingHpIncreasesPhysicalAndMagicDefense(expectLogs: false);
                Debug.Log("CombatMechanismBatchRunner.RunHanhongZaiwuDefense passed.");
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }

    public class TacticalCombatControllerTests
    {
        private readonly List<AttackProfileData> temporaryProfiles = new List<AttackProfileData>();

        [TearDown]
        public void DestroyTemporaryProfiles()
        {
            foreach (var profile in temporaryProfiles)
                Object.DestroyImmediate(profile);
            temporaryProfiles.Clear();
        }

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

            var session = Begin(controller, grid, player, enemy);

            Assert.AreSame(player, session.Members[0].Character);
            Assert.AreSame(enemy, session.Members[1].Character);
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

            Begin(controller, grid, fast, slow);
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

            Begin(controller, grid, player, enemy);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var hit = controller.ExecuteBasicAttack(player.CTBUnit.Id, enemy.CTBUnit.Id);

            Assert.IsTrue(hit.Success);
            Assert.AreEqual(0f, player.CTBUnit.CT);
            Assert.Less(enemy.CurrentHP, enemy.MaxHP);

            enemy.Position = new HexCoord(3, 0);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var outOfRange = controller.ExecuteBasicAttack(player.CTBUnit.Id, enemy.CTBUnit.Id);

            Assert.IsFalse(outOfRange.Success);
            Assert.AreEqual("目标不在射程范围", outOfRange.Message);
            Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.CT);
        }

        [Test]
        public void PlayerSpellPreservesPrecheckFailuresAndConsumesActionOnSuccess()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(3, 0), controller.Engine);
            var art = CreateArt("art_test");
            player.EquippedSpellIds = new[] { art.attackProfileId };

            Begin(controller, grid, player, enemy, art);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;
            int mpBefore = player.CurrentMP;

            var outOfRange = controller.ExecuteArt(
                player.CTBUnit.Id, enemy.CTBUnit.Id, 0, new[] { art });

            Assert.IsFalse(outOfRange.Success);
            Assert.AreEqual("目标不在射程范围", outOfRange.Message);
            Assert.AreEqual(mpBefore, player.CurrentMP);
            Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.CT);

            enemy.Position = new HexCoord(1, 0);

            var cast = controller.ExecuteArt(
                player.CTBUnit.Id, enemy.CTBUnit.Id, 0, new[] { art });

            Assert.IsTrue(cast.Success);
            Assert.AreEqual(0f, player.CTBUnit.CT);
            Assert.AreEqual(20, player.SpellCooldowns[0]);
            Assert.Less(player.CurrentMP, mpBefore);
        }

        [Test]
        public void PlayerGuardConsumesFullActionAndWaitRetainsHalfCt()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(1, 0), controller.Engine);

            Begin(controller, grid, player, enemy);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var guard = controller.ExecuteGuard(player.CTBUnit.Id);

            Assert.IsTrue(guard.Success);
            Assert.IsTrue(player.IsGuarding);
            Assert.AreEqual(0f, player.CTBUnit.CT);

            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var wait = controller.ExecuteWait(player.CTBUnit.Id);

            Assert.IsTrue(wait.Success);
            Assert.IsFalse(player.IsGuarding);
            Assert.AreEqual(CTBEngine.ActionThreshold * CTBEngine.CtRetentionOnWait, player.CTBUnit.CT);
        }

        [Test]
        public void PlayerSwapSpellConsumesActionOnSuccessAndPreservesFailure()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(1, 0), controller.Engine);
            player.EquippedSpellIds = new[] { "old-spell" };
            player.AvailableSpells = new[] { "old-spell", "new-spell", "backup-spell" };

            Begin(controller, grid, player, enemy);
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var swapped = controller.ExecuteSwapSpell(player.CTBUnit.Id, 0, "new-spell");

            Assert.IsTrue(swapped.Success);
            Assert.AreEqual("临阵换法: old-spell → new-spell (CD×2, 剩余1次)", swapped.Message);
            Assert.AreEqual("new-spell", player.EquippedSpellIds[0]);
            Assert.AreEqual(60, player.SpellCooldowns[0]);
            Assert.AreEqual(1, player.CombatSwapsUsed);
            Assert.AreEqual(0f, player.CTBUnit.CT);

            player.CombatSwapsUsed = Character.MaxCombatSwaps;
            player.CTBUnit.CT = CTBEngine.ActionThreshold;

            var exhausted = controller.ExecuteSwapSpell(player.CTBUnit.Id, 0, "backup-spell");

            Assert.IsFalse(exhausted.Success);
            Assert.AreEqual("本场战斗换法次数已用完", exhausted.Message);
            Assert.AreEqual("new-spell", player.EquippedSpellIds[0]);
            Assert.AreEqual(CTBEngine.ActionThreshold, player.CTBUnit.CT);
        }

        [Test]
        public void MeleeAiProfileResolvesToExistingSimpleAiAndUnknownProfileFails()
        {
            Assert.IsTrue(
                EnemyAIProfileResolver.TryResolve(
                    EnemyAIProfileResolver.MeleeProfileId,
                    out var aiController,
                    out var reason),
                reason);
            Assert.IsInstanceOf<SimpleAI>(aiController);

            Assert.IsFalse(
                EnemyAIProfileResolver.TryResolve("ai_unknown", out var unknown, out reason));
            Assert.IsNull(unknown);
            Assert.AreEqual(EnemyAIProfileResolver.UnknownProfileReason, reason);

            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant(
                "玩家",
                "含弘光大典",
                new HexCoord(0, 0),
                controller.Engine);
            var enemy = CreateCombatant(
                "石甲兽",
                "含弘光大典",
                new HexCoord(1, 0),
                controller.Engine);
            Begin(controller, grid, player, enemy);
            int playerHpBefore = player.CurrentHP;

            string action = controller.ExecuteEnemyTurn(
                enemy.CTBUnit.Id,
                player.CTBUnit.Id,
                null,
                null,
                aiController,
                grid);

            StringAssert.Contains("物理伤害", action);
            Assert.Less(player.CurrentHP, playerHpBefore);
        }

        [Test]
        public void ResolveBattleEndClearsDefeatedEnemyOccupancyAndReportsIdentityOnly()
        {
            var grid = new HexGrid();
            var controller = new TacticalCombatController();
            var player = CreateCombatant("玩家", "含弘光大典", new HexCoord(0, 0), controller.Engine);
            var enemy = CreateCombatant("敌人", "含弘光大典", new HexCoord(1, 0), controller.Engine);
            grid.SetOccupied(enemy.Position, enemy.CTBUnit.Id);
            Begin(controller, grid, player, enemy);
            enemy.TakeDamage(enemy.CurrentHP);

            var result = controller.ResolveBattleEnd(grid);

            Assert.AreEqual(TacticalCombatEndOutcome.Victory, result.Outcome);
            Assert.AreEqual("击败了 敌人！", result.Message);
            CollectionAssert.AreEqual(
                new[] { enemy.CTBUnit.Id },
                result.DefeatedEnemyUnitIds);
            Assert.IsFalse(grid.IsOccupied(enemy.Position));
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
            character.MainEquipmentBasicAttackProfileId = "basic_main";
            character.BasicAttackProfileId = "basic_main";
            character.BasicAttackBindingKind = "main_equipment";
            return character;
        }

        private TacticalCombatSession Begin(
            TacticalCombatController controller,
            HexGrid grid,
            Character player,
            Character enemy,
            params AttackProfileData[] additionalProfiles)
        {
            grid.SetOccupied(player.Position, player.CTBUnit.Id);
            grid.SetOccupied(enemy.Position, enemy.CTBUnit.Id);
            var profiles = new List<AttackProfileData> { CreateBasicAttack() };
            if (additionalProfiles != null)
                profiles.AddRange(additionalProfiles);
            var anchors = new Dictionary<int, SpatialHexCoord>
            {
                [player.CTBUnit.Id] = new SpatialHexCoord(player.Position.q, player.Position.r),
                [enemy.CTBUnit.Id] = new SpatialHexCoord(enemy.Position.q, enemy.Position.r),
            };
            var setup = new TacticalCombatSetup(
                new[] { player },
                new[] { enemy },
                SpatialQueryTestFixture.CreateOpenBoard(),
                anchors,
                profiles);
            Assert.IsTrue(controller.TryBeginCombat(setup, grid, out var session, out var reason), reason);
            return session;
        }

        private AttackProfileData CreateBasicAttack()
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            temporaryProfiles.Add(profile);
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

        private AttackProfileData CreateArt(string id)
        {
            var profile = ScriptableObject.CreateInstance<AttackProfileData>();
            temporaryProfiles.Add(profile);
            profile.attackProfileId = id;
            profile.displayNameKey = id + "_name";
            profile.profileKind = AttackProfileKind.Art;
            profile.contentScope = "player";
            profile.sourceAffiliation = "test";
            profile.realmRequirementId = "realm_fanren";
            profile.elementRequirementId = "element_none";
            profile.effectType = AttackEffectType.Magic;
            profile.damageElementId = "element_none";
            profile.soulDamageMultiplier = 1f;
            profile.resourceKind = AttackResourceKind.Mp;
            profile.resourceCost = 10;
            profile.cooldownTicks = 20;
            profile.minCastRange = 1;
            profile.maxCastRange = 1;
            profile.targetingMode = AttackTargetingMode.Single;
            profile.areaFacing = -1;
            return profile;
        }
    }

    public class FormalEncounterResultTests
    {
        private readonly List<Object> temporaryObjects = new List<Object>();

        [TearDown]
        public void DestroyTemporaryObjects()
        {
            foreach (Object value in temporaryObjects)
                Object.DestroyImmediate(value);
            temporaryObjects.Clear();
        }

        [Test]
        public void ProductionCatalogResolvesStableEnemyAndExplicitMeleeAi()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                "Assets/Data/ContentCatalog/ContentCatalog.asset");

            Assert.IsTrue(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    catalog,
                    out EnemyData enemy,
                    out IAIController aiController,
                    out string reason),
                reason);
            Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, enemy.enemyId);
            Assert.IsNotNull(enemy.combatTemplate);
            Assert.IsInstanceOf<SimpleAI>(aiController);
        }

        [Test]
        public void ConfigurationRejectsMissingCatalogAndUnknownAiBeforeCombat()
        {
            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    null,
                    out _,
                    out _,
                    out string reason));
            Assert.AreEqual(FormalEncounterRules.CatalogMissingReason, reason);

            var emptyCatalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            emptyCatalog.ReplaceEntries(null, null, null, null);
            AssertRejected(emptyCatalog, FormalEncounterRules.EnemyMissingReason);

            var fixture = CreateFixture();
            fixture.Enemy.aiProfileId = "ai_unknown";

            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    fixture.Catalog,
                    out _,
                    out _,
                    out reason));
            Assert.AreEqual(EnemyAIProfileResolver.UnknownProfileReason, reason);
        }

        [Test]
        public void ConfigurationRejectsInvalidScopeTemplateDropsAndItemsBeforeCombat()
        {
            var fixture = CreateFixture();
            fixture.Enemy.contentScope = "other_scope";
            AssertRejected(fixture.Catalog, FormalEncounterRules.EnemyScopeInvalidReason);

            fixture = CreateFixture();
            fixture.Enemy.combatTemplate = null;
            AssertRejected(fixture.Catalog, FormalEncounterRules.CombatTemplateMissingReason);

            fixture = CreateFixture();
            fixture.Enemy.dropEntries = System.Array.Empty<EnemyDropEntry>();
            AssertRejected(fixture.Catalog, FormalEncounterRules.DropsMissingReason);

            fixture = CreateFixture();
            fixture.Enemy.dropEntries[0].itemId = "item_missing";
            AssertRejected(
                fixture.Catalog,
                FormalEncounterRules.DropItemMissingReason + ":item_missing");

            fixture = CreateFixture();
            Assert.IsTrue(fixture.Catalog.TryGetItem("item_shijia_piece", out ItemData item));
            item.contentScope = "reserved";
            AssertRejected(
                fixture.Catalog,
                FormalEncounterRules.DropItemNotProductionReason + ":item_shijia_piece");
        }

        [Test]
        public void VictoryRollsDropsIndependentlyWithStrictLessThanComparison()
        {
            var fixture = CreateFixture();

            Assert.IsTrue(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    TacticalCombatEndOutcome.Victory,
                    new SequenceRandomSource(99, 49),
                    out FormalEncounterResult bothDrops,
                    out string reason),
                reason);
            Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, bothDrops.EnemyId);
            Assert.AreEqual(2, bothDrops.DropGrants.Count);

            Assert.IsTrue(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    TacticalCombatEndOutcome.Victory,
                    new SequenceRandomSource(0, 50),
                    out FormalEncounterResult thresholdResult,
                    out reason),
                reason);
            Assert.AreEqual(1, thresholdResult.DropGrants.Count);
            Assert.AreEqual("item_shijia_piece", thresholdResult.DropGrants[0].ItemId);
        }

        [Test]
        public void ResultRejectsDifferentEnemyIdentityAndOutOfRangeRandomValue()
        {
            var fixture = CreateFixture();
            var differentEnemy = Track(ScriptableObject.CreateInstance<EnemyData>());

            Assert.IsFalse(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    differentEnemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    TacticalCombatEndOutcome.Victory,
                    new SequenceRandomSource(0, 0),
                    out _,
                    out string reason));
            Assert.AreEqual(FormalEncounterRules.EnemyIdentityMismatchReason, reason);

            Assert.IsFalse(
                FormalEncounterResult.TryCreate(
                    fixture.Catalog,
                    fixture.Enemy,
                    FormalEncounterRules.GuanzhongWildAdventureId,
                    TacticalCombatEndOutcome.Victory,
                    new SequenceRandomSource(100),
                    out _,
                    out reason));
            Assert.AreEqual(FormalEncounterRules.RandomValueInvalidReason, reason);
        }

        private FormalFixture CreateFixture()
        {
            var template = Track(ScriptableObject.CreateInstance<CharacterData>());
            template.charName = "石甲兽";

            var guaranteedItem = CreateItem("item_shijia_piece");
            var chanceItem = CreateItem("item_lingshi_low");
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = FormalEncounterRules.ShijiahouEnemyId;
            enemy.contentScope = FormalEncounterRules.GuanzhongContentScope;
            enemy.aiProfileId = EnemyAIProfileResolver.MeleeProfileId;
            enemy.combatTemplate = template;
            enemy.dropEntries = new[]
            {
                new EnemyDropEntry
                {
                    itemId = guaranteedItem.itemId,
                    dropChancePercent = 100,
                    quantity = 1,
                },
                new EnemyDropEntry
                {
                    itemId = chanceItem.itemId,
                    dropChancePercent = 50,
                    quantity = 1,
                },
            };

            var catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            catalog.ReplaceEntries(
                null,
                new[] { enemy },
                new[] { guaranteedItem, chanceItem },
                null);
            return new FormalFixture(catalog, enemy);
        }

        private ItemData CreateItem(string itemId)
        {
            var item = Track(ScriptableObject.CreateInstance<ItemData>());
            item.itemId = itemId;
            item.contentScope = InventoryGrantService.ProductionContentScope;
            item.maxStack = 99;
            return item;
        }

        private static void AssertRejected(ContentCatalogData catalog, string expectedReason)
        {
            Assert.IsFalse(
                FormalEncounterRules.TryResolveGuanzhongEnemy(
                    catalog,
                    out _,
                    out _,
                    out string reason));
            Assert.AreEqual(expectedReason, reason);
        }

        private T Track<T>(T value)
            where T : Object
        {
            temporaryObjects.Add(value);
            return value;
        }

        private sealed class FormalFixture
        {
            public ContentCatalogData Catalog { get; }
            public EnemyData Enemy { get; }

            public FormalFixture(ContentCatalogData catalog, EnemyData enemy)
            {
                Catalog = catalog;
                Enemy = enemy;
            }
        }

        internal sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly Queue<int> values;

            public SequenceRandomSource(params int[] values)
            {
                this.values = new Queue<int>(values);
            }

            public int NextPercent()
            {
                return values.Dequeue();
            }
        }
    }

    public class CombatLogAdapterTests
    {
        [Test]
        public void AdapterFormatsBattleStartActionResultAndDrops()
        {
            var logs = new System.Collections.Generic.List<string>();
            string status = null;
            var adapter = new CombatLogAdapter(logs.Add, value => status = value);

            adapter.AnnounceBattleStart("玩家", "石甲兽");
            adapter.AppendActionResult(new CombatResolver.ActionResult { Success = true, Message = "玩家 物理攻击 石甲兽" });
            adapter.AppendActionResult(new CombatResolver.ActionResult { Success = false, Message = "" });
            adapter.AppendDropItems(new[] { "灵石×5", "下品丹药×1" });

            CollectionAssert.AreEqual(
                new[]
                {
                    "=== 战斗开始！玩家 VS 石甲兽 ===",
                    "玩家 物理攻击 石甲兽",
                    "掉落: 灵石×5, 下品丹药×1",
                },
                logs);
            Assert.AreEqual("⚔ 石甲兽", status);
        }
    }

    public static class CombatLogAdapterBatchRunner
    {
        public static void RunCombatLogAdapter()
        {
            try
            {
                new CombatLogAdapterTests().AdapterFormatsBattleStartActionResultAndDrops();
                Debug.Log("CombatLogAdapterBatchRunner.RunCombatLogAdapter passed.");
                EditorApplication.Exit(0);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }
    }

    public class AdventureSceneControllerTests
    {
        [Test]
        public void GuanzhongWildInitializationSpawnsTheFormalShijiahouMarker()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);

                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                var initMethod = typeof(TianZhang.Map.ExplorationController).GetMethod(
                    "InitExploration",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(initMethod);

                var initialization = (System.Collections.IEnumerator)initMethod.Invoke(exploration, null);
                while (initialization.MoveNext())
                {
                }

                Assert.IsNotNull(GameObject.Find("石甲兽"), "The formal encounter must spawn its enemy marker.");
                var snapshot = GetPrivateField<SpatialQuerySnapshot>(exploration, "spatialQuerySnapshot");
                Assert.IsNotNull(snapshot);
                Assert.AreEqual(2, snapshot.Board.UnitsPerRange);
                var enemies = (System.Collections.IList)exploration.GetType()
                    .GetField("enemies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(exploration);
                var firstEnemy = enemies[0];
                var formalEnemyData = (EnemyData)firstEnemy.GetType()
                    .GetField("enemyData")
                    .GetValue(firstEnemy);
                var combatTemplate = (CharacterData)firstEnemy.GetType()
                    .GetField("data")
                    .GetValue(firstEnemy);
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var expectedEnemy));
                Assert.AreSame(expectedEnemy, formalEnemyData);
                Assert.AreSame(expectedEnemy.combatTemplate, combatTemplate);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildConfiguredAdjacentEnemyBeginsCombat()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                var initialization = (System.Collections.IEnumerator)typeof(TianZhang.Map.ExplorationController)
                    .GetMethod("InitExploration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(exploration, null);
                while (initialization.MoveNext())
                {
                }

                var player = GetPrivateField<Character>(exploration, "player");
                var enemies = (System.Collections.IList)exploration.GetType()
                    .GetField("enemies", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .GetValue(exploration);
                var firstEnemy = enemies[0];
                var enemy = (Character)firstEnemy.GetType()
                    .GetField("character", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                    .GetValue(firstEnemy);
                var resolver = GetPrivateField<CombatResolver>(exploration, "resolver");
                player.Position = new HexCoord(0, 0);
                enemy.Position = new HexCoord(1, 0);
                Assert.IsTrue(resolver.CanTarget(player.Position, enemy.Position, 1, 1, out var reason), reason);

                controller.BeginEncounter();

                Assert.AreEqual(AdventureSceneState.Combat, controller.CurrentState);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildDisplaysItsNameAndConfiguresOnlyTheFormalShijiahou()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var expectedEnemy));

                exploration.enemyCount = 3;
                exploration.enemyTemplates = System.Array.Empty<CharacterData>();
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                InvokeStart(controller);

                StringAssert.Contains("关中野外", GameObject.Find("AdventureIdText")?.GetComponent<Text>()?.text);
                Assert.AreEqual(1, exploration.enemyCount);
                CollectionAssert.IsEmpty(exploration.enemyTemplates);
                Assert.AreSame(
                    expectedEnemy,
                    GetPrivateField<EnemyData>(exploration, "formalEncounterEnemy"));
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void GuanzhongWildWithoutFormalCatalogBlocksEncounterButKeepsReturnExit()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(exploration);

                var serializedController = new SerializedObject(controller);
                var catalogProperty = serializedController.FindProperty("contentCatalog");
                Assert.IsNotNull(catalogProperty);
                catalogProperty.objectReferenceValue = null;
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                exploration.enabled = true;
                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.CatalogMissingReason));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");
                InvokeStart(controller);

                Assert.IsFalse(exploration.enabled);
                Assert.AreEqual(AdventureSceneState.Loading, controller.CurrentState);
                Assert.IsNotNull(GameObject.Find("ReturnToSourceButton"));
            }
            finally
            {
                DestroyAdventureUi();
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalVictoryGrantsStructuredDropsOnlyOnce()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalAdventureControllerTest");
            var explorationGo = new GameObject("FormalExplorationControllerTest");
            var sessionGo = new GameObject("FormalGameSessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(99, 49));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                controller.ResolveEncounterAndReturn(TacticalCombatEndOutcome.Victory, enemy);

                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(FormalEncounterRules.ShijiahouEnemyId, controller.LastFormalEncounterResult.EnemyId);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out var piece));
                Assert.AreEqual(1, piece.Quantity);
                Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out var lingshi));
                Assert.AreEqual(1, lingshi.Quantity);

                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                controller.ResolveEncounterAndReturn(TacticalCombatEndOutcome.Victory, enemy);

                Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.EncounterResolutionFailureReason);
                Assert.AreEqual(2, session.InventoryStates.Count);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out piece));
                Assert.AreEqual(1, piece.Quantity);
                Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out lingshi));
                Assert.AreEqual(1, lingshi.Quantity);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void FormalVictoryInventoryFailureIsAtomicAndObservable()
        {
            DestroyExistingSceneFlowAndSession();
            var controllerGo = new GameObject("FormalAdventureControllerTest");
            var explorationGo = new GameObject("FormalExplorationControllerTest");
            var sessionGo = new GameObject("FormalGameSessionTest");
            try
            {
                var controller = controllerGo.AddComponent<AdventureSceneController>();
                explorationGo.AddComponent<TianZhang.Map.ExplorationController>();
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId(FormalEncounterRules.GuanzhongWildAdventureId);
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>(
                    "Assets/Data/ContentCatalog/ContentCatalog.asset");
                Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out var enemy));
                session.InventoryStates.Set(
                    new InventoryStateSnapshot(
                        "item_shijia_piece",
                        99,
                        new StateStepSnapshot(false, false, false, false, false, false, false)));
                controller.SetContentCatalog(catalog);
                controller.SetGuanzhongWildEnvironmentProfile(
                    AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(
                        "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
                controller.SetEncounterRandomSource(
                    new FormalEncounterResultTests.SequenceRandomSource(0, 0));
                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                LogAssert.Expect(LogType.Error, new Regex("StackLimitExceeded"));
                controller.ResolveEncounterAndReturn(TacticalCombatEndOutcome.Victory, enemy);

                StringAssert.Contains("StackLimitExceeded", controller.EncounterResolutionFailureReason);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);
                Assert.IsTrue(session.InventoryStates.TryGet("item_shijia_piece", out var piece));
                Assert.AreEqual(99, piece.Quantity);
                Assert.IsFalse(session.InventoryStates.TryGet("item_lingshi_low", out _));
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                Object.DestroyImmediate(explorationGo);
                Object.DestroyImmediate(controllerGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void NonGuanzhongAdventureDoesNotConsumeGuanzhongBinding()
        {
            DestroyExistingSceneFlowAndSession();
            SceneBuilder.BuildAdventureScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AdventureScene.unity", OpenSceneMode.Single);

            var sessionGo = new GameObject("GameSessionTest");
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                session.SetAdventureId("taiyi_trial");

                var controller = Object.FindFirstObjectByType<AdventureSceneController>();
                var exploration = Object.FindFirstObjectByType<TianZhang.Map.ExplorationController>();
                var originalEnemyCount = exploration.enemyCount;
                var originalEnemyTemplates = exploration.enemyTemplates;

                InvokePrivate(controller, "ConfigureCurrentAdventureEncounter");

                Assert.AreEqual(originalEnemyCount, exploration.enemyCount);
                Assert.AreSame(originalEnemyTemplates, exploration.enemyTemplates);
            }
            finally
            {
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

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

        [TestCase(TacticalCombatEndOutcome.Victory)]
        [TestCase(TacticalCombatEndOutcome.Defeat)]
        public void CompletedEncounterRecordsOutcomeAndReturnsToSource(TacticalCombatEndOutcome outcome)
        {
            var go = new GameObject("AdventureSceneControllerTests");
            try
            {
                var controller = go.AddComponent<AdventureSceneController>();
                controller.MarkExplorationReady();
                controller.BeginEncounter();

                controller.ResolveEncounterAndReturn(outcome);

                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(outcome, controller.LastEncounterOutcome);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void NewGameSessionClearsPreviousSceneContext()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var oldProfile = ScriptableObject.CreateInstance<CharacterData>();
            var newProfile = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                oldProfile.charName = "旧档角色";
                newProfile.charName = "新档角色";

                session.SetPlayerProfile(oldProfile);
                session.SetWorldNode("old_node");
                session.SetSettlementId("old_settlement");
                session.SetAdventureId("old_adventure");
                session.SetReturnTarget(SceneReturnTarget.Settlement("old_settlement"));

                session.BeginNewGame(newProfile, "jiangzuo_hub");

                Assert.AreSame(newProfile, session.PlayerProfile);
                Assert.AreEqual("jiangzuo_hub", session.CurrentWorldNodeId);
                Assert.IsNull(session.CurrentSettlementId);
                Assert.IsNull(session.CurrentAdventureId);
                Assert.IsTrue(string.IsNullOrEmpty(session.LastReturnTarget.SceneName));
            }
            finally
            {
                Object.DestroyImmediate(newProfile);
                Object.DestroyImmediate(oldProfile);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        [Test]
        public void ExplorationPlayerUsesGameSessionProfileWhenAvailable()
        {
            DestroyExistingSceneFlowAndSession();
            var sessionGo = new GameObject("GameSessionTest");
            var controllerGo = new GameObject("ExplorationControllerTest");
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                var session = sessionGo.AddComponent<GameSession>();
                profile.charName = "玉清崖";
                profile.gongFaName = "苦行剑典";
                profile.rootBone = 16;
                profile.physique = 14;
                profile.spirit = 8;
                profile.mind = 14;
                profile.reaction = 20;
                profile.talent = 10;
                profile.realmMultiplier = 1.5f;
                profile.equippedSpells = new[] { "引雷诀", "苦行剑式" };
                profile.availableSpells = new[] { "引雷诀", "苦行剑式", "剑罡护体" };
                session.BeginNewGame(profile, "jiangzuo_hub");

                var controller = controllerGo.AddComponent<TianZhang.Map.ExplorationController>();
                var method = typeof(TianZhang.Map.ExplorationController).GetMethod(
                    "CreatePlayer",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                var player = (Character)method.Invoke(controller, new object[] { new HexCoord(0, 0) });

                Assert.AreEqual("玉清崖", player.Name);
                Assert.AreEqual("苦行剑典", player.GongFaName);
                Assert.AreEqual(16, player.RootBone);
                Assert.AreEqual(20, player.Reaction);
                CollectionAssert.AreEqual(new[] { "引雷诀", "苦行剑式" }, player.EquippedSpellIds);
                CollectionAssert.AreEqual(new[] { "引雷诀", "苦行剑式", "剑罡护体" }, player.AvailableSpells);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(controllerGo);
                Object.DestroyImmediate(sessionGo);
                DestroyExistingSceneFlowAndSession();
            }
        }

        private static void DestroyExistingSceneFlowAndSession()
        {
            if (SceneFlowManager.Instance != null)
                Object.DestroyImmediate(SceneFlowManager.Instance.gameObject);
            if (GameSession.Instance != null)
                Object.DestroyImmediate(GameSession.Instance.gameObject);
        }

        private static void InvokeStart(MonoBehaviour controller)
        {
            controller.GetType()
                .GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(controller, null);
        }

        private static void InvokePrivate(MonoBehaviour controller, string methodName)
        {
            var method = controller.GetType()
                .GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName);
            method.Invoke(controller, null);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            return (T)target.GetType()
                .GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void DestroyAdventureUi()
        {
            var canvas = GameObject.Find("UICanvas");
            if (canvas != null)
                Object.DestroyImmediate(canvas);
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
                FindButton("BtnSwap").onClick.Invoke();
                FindButton("BtnSpell2").onClick.Invoke();
                FindButton("BtnSkill1").onClick.Invoke();

                Assert.AreEqual(1, handler.BasicAttackRequests);
                Assert.AreEqual(1, handler.GuardRequests);
                Assert.AreEqual(1, handler.WaitRequests);
                Assert.AreEqual(1, handler.SwapSpellRequests);
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

        [Test]
        public void ActionBarButtonsIgnoreClicksWhenNoCombatCommandHandlerIsBound()
        {
            var host = new GameObject("BattleUIManagerNoCommandHandlerTest");
            try
            {
                var ui = host.AddComponent<BattleUIManager>();
                typeof(BattleUIManager)
                    .GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(ui, null);

                Assert.DoesNotThrow(() =>
                {
                    FindButton("BtnAttack").onClick.Invoke();
                    FindButton("BtnGuard").onClick.Invoke();
                    FindButton("BtnWait").onClick.Invoke();
                    FindButton("BtnSwap").onClick.Invoke();
                    FindButton("BtnSpell0").onClick.Invoke();
                    FindButton("BtnSkill0").onClick.Invoke();
                });
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
            public int SwapSpellRequests { get; private set; }
            public int LastSpellIndex { get; private set; } = -1;
            public int LastSkillIndex { get; private set; } = -1;

            public void RequestBasicAttack() => BasicAttackRequests++;
            public void RequestGuard() => GuardRequests++;
            public void RequestWait() => WaitRequests++;
            public void RequestSwapSpell() => SwapSpellRequests++;
            public void RequestSpell(int index) => LastSpellIndex = index;
            public void RequestSkill(int index) => LastSkillIndex = index;
        }
    }
}
