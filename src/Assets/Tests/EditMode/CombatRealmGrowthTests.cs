using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Spatial;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatRealmGrowthTests
    {
        private CombatRuleSetData rules;
        private CharacterData definition;
        private ContentCatalogData catalog;
        [SetUp] public void SetUp()
        {
            rules = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset"));
            catalog = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            catalog.SetCombatRuleSet(rules);
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "成长验证"; definition.realmStage = "凡人"; definition.realmMultiplier = 1.5f;
            definition.actionPointMaximumUnits = 100000; definition.movementEfficiencyMilliUnits = 2000;
            definition.unarmedBasicAttackProfileId = "basic_unarmed";
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(definition); Object.DestroyImmediate(catalog); Object.DestroyImmediate(rules); }
        private CharacterRuntimeProfile Profile() => CharacterRuntimeProfile.FromDefinition("player", definition, rules);
        private static CharacterProgressionSnapshot Progress(string stage, string gongfa = "", params MovementGrowthSource[] sources) =>
            new CharacterProgressionSnapshot(gongfa, stage, 1.5f, true, sources);
        private static MovementGrowthSource Nodes(string realm, string chapter, int count, string instance = "training:1",
            string gongfa = "gongfa_kuxingjiandian") => new MovementGrowthSource(instance, gongfa, chapter, realm, Enumerable.Range(1, count).ToArray());
        private static MovementGrowthSource[] Kuxing(int qi, int zhuji = 0, int jindan = 0) => new[]
        { Nodes("realm_lianqi", "磨剑", qi), zhuji > 0 ? Nodes("realm_zhuji", "断陇", zhuji) : null,
            jindan > 0 ? Nodes("realm_jindan", "无倦", jindan) : null }.Where(x => x != null).ToArray();
        [TestCase("凡人", 100000, 2000, 2)]
        [TestCase("练气", 100000, 4000, 4)]
        [TestCase("筑基", 300000, 4000, 12)]
        [TestCase("金丹圆满", 1000000, 4800, 48)]
        [TestCase("练气一品", 100000, 4000, 4)]
        [TestCase("练气九品", 100000, 4000, 4)]
        [TestCase("开府", 300000, 4000, 12)]
        [TestCase("筑基圆满", 300000, 4000, 12)]
        [TestCase("金丹初结", 1000000, 4800, 48)]
        [TestCase("金丹温养", 1000000, 4800, 48)]
        public void DefinitionProgressionAndSnapshotUseTheSameRealmTable(string stage, int ap, int movement, int distance)
        {
            definition.realmStage = stage;
            var profile = Profile();
            Assert.AreEqual(ap, profile.Attributes.ActionPointMaximumUnits);
            Assert.AreEqual(movement, profile.Attributes.MovementEfficiencyMilliUnits);
            Assert.AreEqual(distance, (long)ap * movement / 100000000);
            profile.RestoreProgression(Progress(stage));
            var rebuilt = CharacterRuntimeProfile.FromSnapshot(profile.Capture(), rules);
            Assert.AreEqual(ap, rebuilt.Attributes.ActionPointMaximumUnits);
            Assert.AreEqual(movement, rebuilt.Attributes.MovementEfficiencyMilliUnits);
        }
        [TestCase("练气九品", 9, 0, 0, 5000)]
        [TestCase("筑基开府", 9, 2, 0, 4953)]
        [TestCase("筑基圆满", 9, 3, 0, 5333)]
        [TestCase("金丹圆满", 9, 3, 3, 6400)]
        public void MovementUsesAllExplicitNodesAndRoundsOnlyAfterRelativeConversion(string stage, int qi, int zhuji, int gold, int movement)
        {
            var profile = Profile(); var inputs = Kuxing(qi, zhuji, gold);
            profile.RestoreProgression(Progress(stage, "苦行剑典", inputs));
            Assert.AreEqual(movement, profile.Attributes.MovementEfficiencyMilliUnits);
            var duplicate = inputs.Concat(inputs).ToArray();
            profile.RestoreProgression(Progress(stage, "gongfa_kuxingjiandian", duplicate));
            Assert.AreEqual(movement, profile.Attributes.MovementEfficiencyMilliUnits);
            profile.RestoreProgression(Progress(stage, "含弘光大典"));
            Assert.AreEqual(stage.StartsWith("金丹") ? 4800 : 4000, profile.Attributes.MovementEfficiencyMilliUnits);
            profile.RestoreProgression(Progress(stage, "苦行剑典", inputs));
            Assert.AreEqual(movement, profile.Attributes.MovementEfficiencyMilliUnits);
        }
        [Test] public void AllTwentyZIdentitiesAndNamesGiveZeroPermanentMovementAndAp()
        {
            string[] names = { "疾雷破山经", "含弘光大典", "白屋青云录", "混元同尘典", "抱元守一经", "万物不迁法", "秋水游心经",
                "云篆度人经", "不真自虚法", "心无性有法", "九霄雷劫录", "雷池淬体功", "南华玄感录", "绳墨正法录", "南华阐衍典",
                "太易山藏经", "玄牝道藏", "空无般若经", "万法都箓经", "正一盟威箓" };
            var profile = Profile();
            foreach (string name in names)
            { profile.RestoreProgression(Progress("金丹圆满", name)); Assert.AreEqual(4800, profile.Attributes.MovementEfficiencyMilliUnits, name); Assert.AreEqual(1000000, profile.Attributes.ActionPointMaximumUnits); }
            profile.RestoreProgression(Progress("金丹圆满", "抱元守一经", Nodes("realm_lianqi", "守一篇", 9, gongfa: "gongfa_baoyuanshouyi")));
            Assert.AreEqual(4800, profile.Attributes.MovementEfficiencyMilliUnits);
        }
        [Test] public void InvalidGrowthCannotPartiallyPublishTheProgressionOrAttributes()
        {
            var profile = Profile(); string before = JsonUtility.ToJson(CharacterRecord.Capture(profile.Capture()));
            var invalid = new[]
            {
                Progress(""), Progress("筑基初期"), Progress("练气", "unknown"),
                new CharacterProgressionSnapshot("", "练气", 1f, false, Array.Empty<MovementGrowthSource>()),
                Progress("练气", "苦行剑典", new MovementGrowthSource("a", "苦行剑典", "磨剑", "realm_lianqi", new[] { 1, 1 })),
                Progress("练气一品", "苦行剑典", Nodes("realm_lianqi", "磨剑", 2)),
                Progress("练气", "苦行剑典", Nodes("realm_zhuji", "断陇", 1)),
                Progress("金丹", "苦行剑典", Nodes("realm_lianqi", "磨剑", 9), Nodes("realm_zhuji", "断陇", 3, "other")),
                Progress("金丹", "苦行剑典", Nodes("realm_lianqi", "unknown", 1)),
                Progress("金丹", "苦行剑典", new MovementGrowthSource("a", "苦行剑典", "磨剑", "realm_lianqi", new[] { 10 })),
                Progress("金丹", "抱元守一经", Nodes("realm_lianqi", "磨剑", 1)),
                Progress("金丹", "不真自虚法", Nodes("realm_zhuji", "不真", 1, gongfa: "不真自虚法")),
                Progress("realm_yuanying"),
            };
            foreach (var input in invalid)
            { Assert.Throws<ArgumentException>(() => profile.RestoreProgression(input)); Assert.AreEqual(before, JsonUtility.ToJson(CharacterRecord.Capture(profile.Capture()))); }
        }
        [TestCase("realm_yuanying")]
        [TestCase("realm_huashen")]
        public void ReservedRealmCannotEnterPlayerSlotsUnderAnotherIdentity(string realm)
        {
            var runtime = Runtime(); string before = runtime.CaptureSaveJson();
            definition.realmStage = realm;
            var npc = CharacterRuntimeProfile.FromDefinition("npc_fixture", definition, rules);
            Assert.Throws<ArgumentException>(() => runtime.BeginNewGame(npc, CultivationState.CreateEmpty(), "guanzhong_hub"));
            Assert.AreEqual(before, runtime.CaptureSaveJson());
            var save = runtime.CaptureSave(); save.player.characterId = "npc_fixture"; save.player.realmStage = realm;
            Assert.Throws<ArgumentException>(() => runtime.RestoreSave(save, catalog));
            Assert.AreEqual(before, runtime.CaptureSaveJson());
            var go = new GameObject("ReservedPlayerProof");
            try
            {
                var spawner = go.AddComponent<AdventureUnitSpawner>();
                Assert.IsFalse(spawner.TrySpawn(npc.Capture(), catalog,
                    new AdventureNodeData { q = 0, r = 0 },
                    new AdventureNodeData { q = 1, r = 0, contentId = "enemy_shijiahou" }, out var spawn, out var reason));
                Assert.IsNull(spawn); Assert.AreEqual("adventure_growth_inputs_invalid", reason);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [TestCase("摄法", true)]
        [TestCase("符命", false)]
        [TestCase("都箓", false)]
        public void WanfaYuanyingChapterMustMatchItsExplicitL2Realm(string chapter, bool valid)
        {
            definition.realmStage = "元婴";
            var npc = CharacterRuntimeProfile.FromDefinition("npc_fixture", definition, rules);
            var input = Progress("元婴", "万法都箓经", Nodes("realm_yuanying", chapter, 1, gongfa: "gongfa_wanfadulujing"));
            string before = JsonUtility.ToJson(CharacterRecord.Capture(npc.Capture()));
            if (valid)
            {
                npc.RestoreProgression(input);
                Assert.AreEqual(3000000, npc.Attributes.ActionPointMaximumUnits);
                Assert.AreEqual(8000, npc.Attributes.MovementEfficiencyMilliUnits);
            }
            else
            {
                Assert.Throws<ArgumentException>(() => npc.RestoreProgression(input));
                Assert.AreEqual(before, JsonUtility.ToJson(CharacterRecord.Capture(npc.Capture())));
            }
        }
        [Test] public void ConflictingDuplicateChapterCannotPublishAnyState()
        {
            definition.realmStage = "化神";
            var npc = CharacterRuntimeProfile.FromDefinition("npc_fixture", definition, rules);
            string before = JsonUtility.ToJson(CharacterRecord.Capture(npc.Capture()));
            Assert.Throws<ArgumentException>(() => npc.RestoreProgression(Progress("化神", "含弘光大典",
                Nodes("realm_huashen", "光大", 1, gongfa: "含弘光大典"),
                Nodes("realm_huashen", "品物", 1, gongfa: "含弘光大典"))));
            Assert.AreEqual(before, JsonUtility.ToJson(CharacterRecord.Capture(npc.Capture())));
        }
        [Test] public void MissingConfigurationAndOverflowAreRejected()
        {
            Assert.Throws<ArgumentException>(() => CharacterRuntimeProfile.FromDefinition("player", definition));
            var profile = Profile(); string before = JsonUtility.ToJson(CharacterRecord.Capture(profile.Capture()));
            rules.realms.Single(r => r.realmId == "realm_lianqi").legacyBaseMovementEfficiencyMilliUnits = int.MaxValue;
            rules.realms.Single(r => r.realmId == "realm_jindan").baseMovementEfficiencyMilliUnits = int.MaxValue;
            rules.economy.Single(r => r.recordKind == "global").movementBudgetNumerator.value = int.MaxValue;
            Assert.Throws<OverflowException>(() => profile.RestoreProgression(Progress("金丹", "苦行剑典", Kuxing(9, 3, 3))));
            Assert.AreEqual(before, JsonUtility.ToJson(CharacterRecord.Capture(profile.Capture())));
        }
        private GameRuntime Runtime()
        { var runtime = new GameRuntime(); runtime.ConfigureGrowth(rules); runtime.BeginNewGame(Profile(), CultivationState.CreateEmpty(), "guanzhong_hub"); return runtime; }
        [Test] public void SchemaFiveRoundTripKeepsNodesButRebuildsDerivedValues()
        {
            var runtime = Runtime(); runtime.Player.RestoreProgression(Progress("金丹圆满", "苦行剑典", Kuxing(9, 3, 3)));
            var save = GameSaveSerializer.Deserialize(runtime.CaptureSaveJson());
            Assert.AreEqual(5, save.schemaVersion); Assert.IsTrue(save.player.movementGrowthInputsPresent); Assert.AreEqual(3, save.player.movementGrowthSources.Length);
            save.player.actionPointMaximumUnits = 1; save.player.movementEfficiencyMilliUnits = 0;
            var restored = new GameRuntime(); restored.RestoreSave(save, catalog);
            Assert.AreEqual(1000000, restored.Player.Attributes.ActionPointMaximumUnits); Assert.AreEqual(6400, restored.Player.Attributes.MovementEfficiencyMilliUnits);
            Assert.AreEqual(3, restored.Player.Progression.MovementGrowthSources.Count);
            string canonical = restored.CaptureSaveJson(); restored.RestoreSaveJson(canonical, catalog); Assert.AreEqual(canonical, restored.CaptureSaveJson());
        }
        [Test] public void SchemaFourMigratesOnlyExplicitLegalRealmAndNoInventedNodes()
        {
            var runtime = Runtime(); var save = runtime.CaptureSave(); save.schemaVersion = 4;
            save.player.realmStage = "金丹"; save.player.gongFaId = "苦行剑典";
            save.player.movementGrowthInputsPresent = false; save.player.movementGrowthSources = null;
            var migrated = GameSaveSerializer.Deserialize(JsonUtility.ToJson(save));
            Assert.AreEqual(5, migrated.schemaVersion); Assert.IsTrue(migrated.player.movementGrowthInputsPresent); Assert.IsEmpty(migrated.player.movementGrowthSources);
            runtime.RestoreSave(migrated, catalog); Assert.AreEqual(1000000, runtime.Player.Attributes.ActionPointMaximumUnits); Assert.AreEqual(4800, runtime.Player.Attributes.MovementEfficiencyMilliUnits);
            save.player.realmStage = ""; var invalid = GameSaveSerializer.Deserialize(JsonUtility.ToJson(save));
            string before = runtime.CaptureSaveJson(); Assert.Throws<ArgumentException>(() => runtime.RestoreSave(invalid, catalog)); Assert.AreEqual(before, runtime.CaptureSaveJson());
        }
        [TestCase("presence")] [TestCase("null-array")] [TestCase("null-source")] [TestCase("hidden-marker")] [TestCase("hidden-source")]
        public void MalformedSaveLeavesEveryLiveOwnerUnchanged(string kind)
        {
            var runtime = Runtime(); string before = runtime.CaptureSaveJson(); var save = runtime.CaptureSave();
            switch (kind)
            {
                case "presence": save.player.movementGrowthInputsPresent = false; break;
                case "null-array": save.player.movementGrowthSources = null; break;
                case "null-source": save.player.movementGrowthSources = new MovementGrowthRecord[] { null }; break;
                case "hidden-marker": save.hasPlayer = false; save.player = new CharacterRecord { movementGrowthInputsPresent = true }; save.cultivation = null; break;
                case "hidden-source": save.hasPlayer = false; save.player = new CharacterRecord { movementGrowthSources = new[] { new MovementGrowthRecord() } }; save.cultivation = null; break;
            }
            Assert.Throws<ArgumentException>(() => runtime.RestoreSave(save, catalog)); Assert.AreEqual(before, runtime.CaptureSaveJson());
        }
        [Test] public void TableVariationChangesCreationRestoreAndProgressionWithoutHardcodedDefaults()
        {
            rules.sourceDigest = new string('a', 64); // Deliberate parameter fixture identity, not the production digest.
            var row = rules.realms.Single(r => r.realmId == "realm_fanren"); row.actionPointMaximumUnits = 123000; row.baseMovementEfficiencyMilliUnits = 3500;
            var config = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>("Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset"));
            config.actionPointMaximumUnits = 123000; config.movementEfficiencyMilliUnits = 3500;
            CharacterData created = null;
            try
            {
                created = CharacterCreationRules.BuildCharacterData(CharacterCreationCatalog.CreateDefaultDraft(), config, rules);
                var profile = CharacterRuntimeProfile.FromDefinition("player", created, rules);
                Assert.AreEqual(123000, profile.Attributes.ActionPointMaximumUnits); Assert.AreEqual(3500, profile.Attributes.MovementEfficiencyMilliUnits);
                profile.RestoreProgression(Progress("凡人")); Assert.AreEqual(3500, CharacterRuntimeProfile.FromSnapshot(profile.Capture(), rules).Attributes.MovementEfficiencyMilliUnits);
                config.movementEfficiencyMilliUnits = 2000;
                Assert.Throws<InvalidOperationException>(() => CharacterCreationRules.BuildCharacterData(CharacterCreationCatalog.CreateDefaultDraft(), config, rules));
            }
            finally { Object.DestroyImmediate(created); Object.DestroyImmediate(config); }
        }
        [Test] public void LiveBattleRefreshRejectsOldMovementQuoteAndIssuesNewApOnlyAtNextOwnStart()
        {
            var profile = Profile(); var service = new CombatCommandService();
            var player = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 100, 1000, 1000, 10, 10, 10, 10, 100000, 2000);
            var enemy = new CombatantSnapshot("enemy", CombatTeam.Enemy, new HexCoord(4, 0), 1, 1000, 1000, 10, 10, 10, 10, 100000, 4000);
            var space = (ICombatSpatialQuery)typeof(CombatActionPointKernelTests).GetMethod("ProductionSpace", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            var session = new CombatSession(new[] { player, enemy }, Array.Empty<CombatAttackProfile>(), space, CombatActionPointKernelTests.Ordinary());
            Assert.AreEqual("player", service.AdvanceUntilAction(session).ActorId);
            Assert.IsTrue(service.Execute(session, new CombatCommand(CombatCommandKind.Guard, "player")).Succeeded);
            var move = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(1, 0));
            var quote = service.Preview(session, move); Assert.AreEqual(50000, quote.ActionPointCostUnits);
            int tick = session.TurnScheduler.CurrentTick, remaining = player.ActionPointsRemainingUnits; long turn = session.TurnSequence;
            var go = new GameObject("GrowthBridgeProof"); var bridge = go.AddComponent<EncounterCoordinator>();
            try
            {
                bridge.ConfigureEquipment(profile, catalog, (_, __, ___) => false, () => 0);
                typeof(EncounterCoordinator).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(bridge, session);
                profile.RestoreProgression(Progress("金丹圆满"));
                var refresh = typeof(EncounterCoordinator).GetMethod("RefreshGrowth", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsTrue((bool)refresh.Invoke(bridge, null));
                Assert.AreEqual(1000000, player.ActionPointMaximumUnits); Assert.AreEqual(4800, player.MovementEfficiencyMilliUnits);
                Assert.AreEqual(100000, player.ActionPointLimitAtTurnStartUnits); Assert.AreEqual(remaining, player.ActionPointsRemainingUnits);
                Assert.AreEqual(tick, session.TurnScheduler.CurrentTick); Assert.AreEqual(turn, session.TurnSequence); Assert.IsTrue(session.HasCommittedBenefit);
                long version = session.StateVersion; Assert.IsFalse((bool)refresh.Invoke(bridge, null)); Assert.AreEqual(version, session.StateVersion);
                Assert.IsFalse(service.Execute(session, move, quote).Succeeded); Assert.AreEqual(remaining, player.ActionPointsRemainingUnits);
                var fresh = service.Preview(session, move); Assert.AreEqual(20833, fresh.ActionPointCostUnits);
                Assert.IsTrue(service.Execute(session, move, fresh).Succeeded);
                Assert.IsTrue(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, "player")).Succeeded);
                Assert.AreEqual("player", service.AdvanceUntilAction(session).ActorId);
                Assert.AreEqual(1000000, player.ActionPointLimitAtTurnStartUnits); Assert.AreEqual(1000000, player.ActionPointsRemainingUnits);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
