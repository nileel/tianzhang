using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Core;
using TianZhang.Cultivation.JindanProof;
using TianZhang.Entity;
using TianZhang.Game;
using TianZhang.World;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class SessionStateSnapshotTests
    {
        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void GameSessionOwnsSeparateSnapshotsAndKeepsEveryStateStepObservable()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            var steps = new StateStepSnapshot(
                shown: true,
                clicked: false,
                opened: true,
                selected: false,
                applied: true,
                completed: false,
                persisted: true);

            session.QuestStates.Set(new QuestStateSnapshot("quest_first_steps", steps));
            session.InventoryStates.Set(new InventoryStateSnapshot("item_spirit_stone", 3, steps));
            session.NpcStates.Set(new NpcStateSnapshot("npc_guide", "jiangzuo_hub", steps));

            Assert.AreEqual(1, session.QuestStates.Count);
            Assert.AreEqual(1, session.InventoryStates.Count);
            Assert.AreEqual(1, session.NpcStates.Count);

            Assert.IsTrue(session.QuestStates.TryGet("quest_first_steps", out var quest));
            Assert.IsTrue(quest.Steps.Shown);
            Assert.IsFalse(quest.Steps.Clicked);
            Assert.IsTrue(quest.Steps.Opened);
            Assert.IsFalse(quest.Steps.Selected);
            Assert.IsTrue(quest.Steps.Applied);
            Assert.IsFalse(quest.Steps.Completed);
            Assert.IsTrue(quest.Steps.Persisted);

            session.SetAdventureId("state_snapshot_test");
            session.SetWorldNode("guanzhong_hub");

            Assert.IsTrue(session.InventoryStates.TryGet("item_spirit_stone", out var inventory));
            Assert.AreEqual(3, inventory.Quantity);
            Assert.IsTrue(session.NpcStates.TryGet("npc_guide", out var npc));
            Assert.AreEqual("jiangzuo_hub", npc.WorldNodeId);

            session.BeginNewGame(null, "jiangzuo_hub");

            Assert.AreEqual(0, session.QuestStates.Count);
            Assert.AreEqual(0, session.InventoryStates.Count);
            Assert.AreEqual(0, session.NpcStates.Count);
        }

        [Test]
        public void SnapshotOwnersRejectMissingIdentityAndNegativeInventoryQuantity()
        {
            var steps = new StateStepSnapshot(false, false, false, false, false, false, false);

            Assert.Throws<ArgumentException>(() => new QuestStateSnapshot("", steps));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryStateSnapshot("item_test", -1, steps));
            Assert.Throws<ArgumentException>(() => new NpcStateSnapshot("npc_test", "", steps));
        }

        [Test]
        public void NewGameAndClearSessionProduceTheSameInitialSaveState()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();

            session.BeginNewGame(null, "jiangzuo_hub");
            string newGameJson = JsonUtility.ToJson(session.CaptureSaveData());

            session.SetWorldNode("guanzhong_hub");
            session.AdvanceWorldDay();
            session.SetSettlementId("guanzhong_city");
            session.SetAdventureId("guanzhong_wild");
            session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
            session.QuestStates.Set(new QuestStateSnapshot("quest_changed", Steps(true)));

            session.ClearSession();

            Assert.AreEqual(newGameJson, JsonUtility.ToJson(session.CaptureSaveData()));
        }

        [Test]
        public void CurrentSchemaJsonRoundTripRestoresWorldContextAndSeparateStateSteps()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            var profile = ScriptableObject.CreateInstance<TianZhang.Entity.CharacterData>();
            try
            {
                session.BeginNewGame(profile, "guanzhong_hub");
                session.AdvanceWorldDay();
                session.SetSettlementId("guanzhong_city");
                session.SetAdventureId("guanzhong_wild");
                session.SetReturnTarget(SceneReturnTarget.Settlement("guanzhong_city"));
                var steps = new StateStepSnapshot(
                    shown: true,
                    clicked: false,
                    opened: true,
                    selected: false,
                    applied: true,
                    completed: false,
                    persisted: true);
                session.QuestStates.Set(new QuestStateSnapshot("quest_round_trip", steps));
                session.InventoryStates.Set(new InventoryStateSnapshot("item_round_trip", 7, steps));
                session.NpcStates.Set(new NpcStateSnapshot("npc_round_trip", "longxi_hub", steps));

                string json = JsonUtility.ToJson(session.CaptureSaveData());
                var serialized = JsonUtility.FromJson<GameSessionSaveData>(json);

                session.BeginNewGame(profile, "jiangzuo_hub");
                session.RestoreSaveData(serialized, CreateCatalog());

                Assert.AreSame(profile, session.PlayerProfile);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, serialized.schemaVersion);
                Assert.AreEqual("guanzhong_hub", session.CurrentWorldNodeId);
                Assert.AreEqual(GameSession.InitialWorldYear, session.WorldYear);
                Assert.AreEqual(GameSession.InitialWorldSeasonId, session.WorldSeasonId);
                Assert.AreEqual(2, session.WorldDay);
                Assert.AreEqual(GameSession.InitialWorldTimeOfDayId, session.WorldTimeOfDayId);
                Assert.AreEqual("guanzhong_city", session.CurrentSettlementId);
                Assert.AreEqual("guanzhong_wild", session.CurrentAdventureId);
                Assert.AreEqual("SettlementScene", session.LastReturnTarget.SceneName);
                Assert.AreEqual("guanzhong_city", session.LastReturnTarget.SettlementId);

                Assert.IsTrue(session.QuestStates.TryGet("quest_round_trip", out var quest));
                AssertEveryStepIsSeparate(quest.Steps);
                Assert.IsTrue(session.InventoryStates.TryGet("item_round_trip", out var inventory));
                Assert.AreEqual(7, inventory.Quantity);
                AssertEveryStepIsSeparate(inventory.Steps);
                Assert.IsTrue(session.NpcStates.TryGet("npc_round_trip", out var npc));
                Assert.AreEqual("longxi_hub", npc.WorldNodeId);
                AssertEveryStepIsSeparate(npc.Steps);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LegacySchemaMigratesMissingStateCollectionsToCurrentEmptyCollections()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            const string legacyJson =
                "{\"schemaVersion\":0,\"currentWorldNodeId\":\"legacy_hub\"," +
                "\"worldYear\":386,\"worldSeasonId\":\"summer\",\"worldDay\":9," +
                "\"worldTimeOfDayId\":\"night\",\"currentSettlementId\":\"legacy_city\"," +
                "\"currentAdventureId\":\"legacy_trial\",\"lastReturnTarget\":{" +
                "\"sceneName\":\"WorldScene\",\"worldNodeId\":\"legacy_hub\"}}";

            session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(legacyJson), CreateCatalog());

            Assert.AreEqual("legacy_hub", session.CurrentWorldNodeId);
            Assert.AreEqual(386, session.WorldYear);
            Assert.AreEqual("summer", session.WorldSeasonId);
            Assert.AreEqual(9, session.WorldDay);
            Assert.AreEqual("night", session.WorldTimeOfDayId);
            Assert.AreEqual("legacy_city", session.CurrentSettlementId);
            Assert.AreEqual("legacy_trial", session.CurrentAdventureId);
            Assert.AreEqual("WorldScene", session.LastReturnTarget.SceneName);
            Assert.AreEqual("legacy_hub", session.LastReturnTarget.WorldNodeId);
            Assert.AreEqual(0, session.QuestStates.Count);
            Assert.AreEqual(0, session.InventoryStates.Count);
            Assert.AreEqual(0, session.NpcStates.Count);
            Assert.AreEqual(
                GameSessionSnapshot.CurrentSchemaVersion,
                session.CaptureSaveData().schemaVersion);
        }

        [Test]
        public void FoundationPurpleMansionSaveRoundTripMigratesVersionOneAndRejectsTamperedJindanState()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            FoundationPurpleMansionStateData state = CreateCompleteFoundationPurpleMansionState();
            CharacterData profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.charName = "save_fixture";
            profile.realmMultiplier = 3f;
            profile.foundationPurpleMansionState = state;
            try
            {
                session.BeginNewGame(profile, "jiangzuo_hub");
                Character player = Character.FromData(profile, new HexCoord(0, 0));
                Assert.IsTrue(new JindanProofCoordinator()
                    .TryFormFoundationPurpleMansionLock(player).Succeeded);
                session.CapturePlayerFoundationPurpleMansionState(player);

                string savedJson = JsonUtility.ToJson(session.CaptureSaveData());
                GameSessionSaveData saved = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, saved.schemaVersion);
                Assert.IsNotNull(saved.playerFoundationPurpleMansionState);

                session.BeginNewGame(profile, "guanzhong_hub");
                session.RestoreSaveData(saved, CreateCatalog());
                Character restoredPlayer = Character.FromData(profile, new HexCoord(0, 0));
                Assert.IsTrue(session.ApplyPlayerFoundationPurpleMansionState(restoredPlayer));
                Assert.IsTrue(restoredPlayer.FoundationPurpleMansionState.IsJindanFormed);
                Assert.AreEqual("guardian_ming", restoredPlayer.FoundationPurpleMansionState
                    .GetGuardianAbilities()[0].abilityInstanceId);
                Assert.AreEqual("node_ming_1", restoredPlayer.FoundationPurpleMansionState
                    .GetEnhancementNodes()[0].nodeId);
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                GameSessionSaveData tampered = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                tampered.playerFoundationPurpleMansionState.foundationState.naturalMansionCapacity = 2;
                tampered.playerFoundationPurpleMansionState.foundationState.releasedNaturalCapacity = 2;
                tampered.playerFoundationPurpleMansionState.foundationState.totalMansionCapacity = 2;
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(tampered, CreateCatalog()));
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                saved.schemaVersion = GameSessionSnapshot.StateCollectionsSchemaVersion;
                saved.playerFoundationPurpleMansionState = null;
                session.RestoreSaveData(saved, CreateCatalog());
                Assert.IsNull(session.PlayerFoundationPurpleMansionSaveData);
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion,
                    session.CaptureSaveData().schemaVersion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(state);
            }
        }

        [Test]
        public void NpcCultivationStateRoundTripsOnlyInCurrentSchemaAndInvalidStateFailsAtomically()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            FoundationPurpleMansionStateData state = CreateCompleteFoundationPurpleMansionState();
            try
            {
                Assert.That(FoundationPurpleMansionRuntimeState.TryCreate(
                    state,
                    out FoundationPurpleMansionRuntimeState runtime,
                    out string failureReason), Is.True, failureReason);
                Assert.That(runtime.TryStartCultivationAction(
                    CultivationActionKind.FoundationNurture,
                    "npc_action_nurture",
                    "foundation_save_fixture",
                    "cycle_nurture",
                    "boundary_started",
                    "progress_nurture",
                    new[] { "numeric_nurture" }).Succeeded, Is.True);
                Assert.That(runtime.TryCommitCultivationActionCycle("world_day_18").Succeeded, Is.True);
                Assert.That(runtime.TryPauseCultivationAction("RESOURCE_INSUFFICIENT").Succeeded, Is.True);
                session.NpcStates.Set(new NpcStateSnapshot(
                    "npc_save_fixture",
                    "jiangzuo_hub",
                    Steps(true),
                    runtime.CaptureSaveData()));

                GameSessionSaveData current = session.CaptureSaveData();
                Assert.That(current.schemaVersion, Is.EqualTo(GameSessionSnapshot.CurrentSchemaVersion));
                Assert.That(current.npcs[0].foundationPurpleMansionState.cultivationActionState.targetRef,
                    Is.EqualTo("foundation_save_fixture"));

                string currentJson = JsonUtility.ToJson(current);
                session.ClearSession();
                session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(currentJson), CreateCatalog());
                Assert.That(session.NpcStates.TryGet("npc_save_fixture", out NpcStateSnapshot restored), Is.True);
                Assert.That(restored.FoundationPurpleMansionState.cultivationActionState.actionStateId,
                    Is.EqualTo("npc_action_nurture"));
                Assert.That(restored.FoundationPurpleMansionState.cultivationActionState.committedCycleIds,
                    Is.EquivalentTo(new[] { "world_day_18" }));
                Assert.That(restored.FoundationPurpleMansionState.lastClosedRetreatStopReason,
                    Is.EqualTo("RESOURCE_INSUFFICIENT"));

                foreach (int legacyVersion in new[]
                {
                    GameSessionSnapshot.LegacySchemaVersion,
                    GameSessionSnapshot.StateCollectionsSchemaVersion,
                    GameSessionSnapshot.FoundationPurpleMansionSchemaVersion,
                })
                {
                    GameSessionSaveData legacy = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                    legacy.schemaVersion = legacyVersion;
                    session.RestoreSaveData(legacy, CreateCatalog());
                    if (legacyVersion == GameSessionSnapshot.LegacySchemaVersion)
                    {
                        Assert.That(session.NpcStates.Count, Is.EqualTo(0));
                    }
                    else
                    {
                        Assert.That(session.NpcStates.TryGet("npc_save_fixture", out NpcStateSnapshot migrated), Is.True);
                        Assert.That(migrated.FoundationPurpleMansionState, Is.Null);
                    }
                }

                session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(currentJson), CreateCatalog());
                GameSessionSaveData invalid = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                invalid.npcs[0].foundationPurpleMansionState.foundationState.totalMansionCapacity = 0;
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(invalid, CreateCatalog()));
                Assert.That(JsonUtility.ToJson(session.CaptureSaveData()), Is.EqualTo(currentJson));

                invalid = JsonUtility.FromJson<GameSessionSaveData>(currentJson);
                invalid.npcs[0].foundationPurpleMansionState =
                    new FoundationPurpleMansionSaveData();
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(invalid, CreateCatalog()));
                Assert.That(JsonUtility.ToJson(session.CaptureSaveData()), Is.EqualTo(currentJson));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(state);
            }
        }

        [Test]
        public void InvalidOrUnsupportedSaveDataFailsBeforeChangingTheSession()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            session.BeginNewGame(null, "jiangzuo_hub");
            session.AdvanceWorldDay();
            session.QuestStates.Set(new QuestStateSnapshot("quest_valid", Steps(true)));
            session.InventoryStates.Set(new InventoryStateSnapshot("item_valid", 2, Steps(false)));
            session.NpcStates.Set(new NpcStateSnapshot("npc_valid", "jiangzuo_hub", Steps(true)));
            string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());

            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.currentWorldNodeId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.quests[0].questId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.inventory[0].itemId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs[0].npcId = "");
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs[0].worldNodeId = "");
            AssertRejectedWithoutMutation<ArgumentOutOfRangeException>(session, baselineJson, data =>
                data.inventory[0].quantity = -1);
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.quests.Add(Clone(data.quests[0])));
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.inventory.Add(Clone(data.inventory[0])));
            AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                data.npcs.Add(Clone(data.npcs[0])));
            AssertRejectedWithoutMutation<NotSupportedException>(session, baselineJson, data =>
                data.schemaVersion = -1);
            AssertRejectedWithoutMutation<NotSupportedException>(session, baselineJson, data =>
                data.schemaVersion = GameSessionSnapshot.CurrentSchemaVersion + 1);
        }

        [Test]
        public void BountyInstancesRoundTripInCurrentSchemaAndLegacyVersionsRestoreEmpty()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalog(
                "bounty_round_trip",
                "bounty_round_trip_claimed",
                "bounty_round_trip_second");
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                session.SetSettlementId("guanzhong_city");
                session.SetAdventureId("guanzhong_wild");
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip", BountyStatus.Accepted, 0));
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip_second", BountyStatus.ObjectiveCompleted, 1));
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip_claimed", BountyStatus.Claimed, 1));

                GameSessionSaveData saved = session.CaptureSaveData();
                Assert.AreEqual(GameSessionSnapshot.CurrentSchemaVersion, saved.schemaVersion);
                Assert.AreEqual(3, saved.bounties.Count);
                Assert.AreEqual("bounty_round_trip", saved.bounties[0].bountyId);
                Assert.AreEqual(BountyStatus.Accepted, saved.bounties[0].status);
                Assert.AreEqual("bounty_round_trip_claimed", saved.bounties[1].bountyId);
                Assert.AreEqual(BountyStatus.Claimed, saved.bounties[1].status);
                Assert.AreEqual("bounty_round_trip_second", saved.bounties[2].bountyId);
                string savedJson = JsonUtility.ToJson(saved);

                session.ClearSession();
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(savedJson),
                    catalog);

                Assert.IsTrue(session.BountyStates.TryGet(
                    "bounty_round_trip", out BountyStateSnapshot accepted));
                Assert.AreEqual(BountyStatus.Accepted, accepted.Status);
                Assert.AreEqual(0, accepted.Progress);
                Assert.IsTrue(session.BountyStates.TryGet(
                    "bounty_round_trip_second", out BountyStateSnapshot completed));
                Assert.AreEqual(BountyStatus.ObjectiveCompleted, completed.Status);
                Assert.AreEqual(1, completed.Progress);
                Assert.IsTrue(session.BountyStates.TryGet(
                    "bounty_round_trip_claimed", out BountyStateSnapshot claimed));
                Assert.AreEqual(BountyStatus.Claimed, claimed.Status);
                Assert.AreEqual(1, claimed.Progress);
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                foreach (int legacyVersion in new[]
                {
                    GameSessionSnapshot.LegacySchemaVersion,
                    GameSessionSnapshot.StateCollectionsSchemaVersion,
                    GameSessionSnapshot.FoundationPurpleMansionSchemaVersion,
                })
                {
                    GameSessionSaveData legacy =
                        JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                    legacy.schemaVersion = legacyVersion;
                    session.RestoreSaveData(legacy, catalog);
                    Assert.AreEqual(0, session.BountyStates.Count);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void RestoreRejectsUnresolvableDuplicateOrIllegalBountyStateAtomically()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalog("bounty_round_trip");
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip", BountyStatus.Accepted, 0));
                string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());

                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[0].bountyId = "bounty_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[0].bountyId = "", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties.Add(Clone(data.bounties[0])), catalog);
                AssertRejectedWithoutMutation<ArgumentOutOfRangeException>(session, baselineJson, data =>
                    data.bounties[0].status = (BountyStatus)99, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[0].status = BountyStatus.Available, catalog);
                AssertRejectedWithoutMutation<ArgumentOutOfRangeException>(session, baselineJson, data =>
                    data.bounties[0].progress = -1, catalog);
                AssertRejectedWithoutMutation<ArgumentOutOfRangeException>(session, baselineJson, data =>
                    data.bounties[0].progress = 2, catalog);
                Assert.Throws<ArgumentNullException>(() => session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(baselineJson),
                    null));
                Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void RestoreRejectsBountyStateProgressCombinationsTheRuntimeCannotProduce()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalog(
                "bounty_round_trip",
                "bounty_round_trip_second",
                "bounty_round_trip_claimed");
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip", BountyStatus.Accepted, 0));
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip_second", BountyStatus.ObjectiveCompleted, 1));
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip_claimed", BountyStatus.Claimed, 1));
                string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());

                // 捕获按 bountyId 排序：Accepted 在 [0]，Claimed 在 [1]，ObjectiveCompleted 在 [2]。
                // 非法组合：Accepted 已达到目标、Claimed／ObjectiveCompleted 低于目标；每次拒绝后
                // 整个会话 JSON 与基线一致。
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[0].progress = 1, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[1].progress = 0, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[2].progress = 0, catalog);
                Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void RestoreAcceptsAcceptedProgressBeforeTargetAndRejectsItAtTarget()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithRequiredCount("bounty_round_trip", 2);
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                session.BountyStates.Set(new BountyStateSnapshot(
                    "bounty_round_trip", BountyStatus.Accepted, 1));
                string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());

                // 正例：目标 2 的 Accepted 进度 1 可以无损往返，校验使用已解析的 requiredCount。
                session.ClearSession();
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(baselineJson),
                    catalog);
                Assert.IsTrue(session.BountyStates.TryGet(
                    "bounty_round_trip", out BountyStateSnapshot accepted));
                Assert.AreEqual(BountyStatus.Accepted, accepted.Status);
                Assert.AreEqual(1, accepted.Progress);
                Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 负例：Accepted 进度达到目标（2）不能由状态机产生，恢复失败且会话不变。
                AssertRejectedWithoutMutation<ArgumentException>(session, baselineJson, data =>
                    data.bounties[0].progress = 2, catalog);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterRuntimeStateRoundTripsInSchemaFourAndLegacyVersionsRestoreUnaccessed()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                string savedJson = JsonUtility.ToJson(BuildCharterSaveData(BuildValidCharterState(), version));
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(savedJson),
                    catalog);

                Assert.IsNotNull(session.CharterRuntimeState);
                Assert.AreEqual(version, session.CharterDefinitionCatalogVersion);
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 保存链只捕获深复制：篡改已捕获 payload 或已恢复存档 DTO 都不影响会话状态。
                GameSessionSaveData captured = session.CaptureSaveData();
                captured.charterRuntimeState.registeredRuleEntryIds = new[] { "tampered_entry" };
                captured.charterRuntimeState.realitySupplyStates = Array.Empty<CharterRealitySupplyStateData>();
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                GameSessionSaveData loaded = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                session.RestoreSaveData(loaded, catalog);
                loaded.charterRuntimeState.registeredRuleEntryIds[0] = "tampered_entry";
                loaded.charterRuntimeState.positiveCommitResults =
                    Array.Empty<CharterCommitResultStateData>();
                Assert.AreEqual(savedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // schema 0～3 只恢复明确未接入：无状态、版本 0、presence false。
                foreach (int legacyVersion in new[]
                {
                    GameSessionSnapshot.LegacySchemaVersion,
                    GameSessionSnapshot.StateCollectionsSchemaVersion,
                    GameSessionSnapshot.FoundationPurpleMansionSchemaVersion,
                    GameSessionSnapshot.BountySchemaVersion,
                })
                {
                    GameSessionSaveData legacy = JsonUtility.FromJson<GameSessionSaveData>(savedJson);
                    legacy.schemaVersion = legacyVersion;
                    session.RestoreSaveData(legacy, catalog);

                    Assert.IsNull(session.CharterRuntimeState);
                    Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                    GameSessionSaveData recaptured = session.CaptureSaveData();
                    Assert.IsFalse(recaptured.hasCharterRuntimeState);
                    Assert.AreEqual(0, recaptured.charterDefinitionCatalogVersion);
                    Assert.IsNull(recaptured.charterRuntimeState);
                }

                // schema 4 presence=false 只接受空 payload 与版本 0 → 明确未接入。
                session.RestoreSaveData(BuildCharterSaveData(null, 0), catalog);
                Assert.IsNull(session.CharterRuntimeState);
                Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                GameSessionSaveData absent = session.CaptureSaveData();
                Assert.IsFalse(absent.hasCharterRuntimeState);
                Assert.AreEqual(0, absent.charterDefinitionCatalogVersion);
                Assert.IsNull(absent.charterRuntimeState);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterSaveRestoreRejectsInvalidPayloadsAtomically()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                string baselineJson = JsonUtility.ToJson(BuildCharterSaveData(BuildValidCharterState(), version));
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(baselineJson),
                    catalog);
                string seededJson = JsonUtility.ToJson(session.CaptureSaveData());
                Assert.AreEqual(baselineJson, seededJson);

                // 版本与 presence 组合失败关闭。
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterDefinitionCatalogVersion = version + 1, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState = null, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterDefinitionCatalogVersion = 0, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.hasCharterRuntimeState = false, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                {
                    data.hasCharterRuntimeState = false;
                    data.charterDefinitionCatalogVersion = 1;
                }, catalog);

                // 条目、节点、授权、覆盖、占用、供给、正负提交与当前地区篡改失败关闭。
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.registeredRuleEntryIds[0] = "entry_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.registeredRuleEntryIds =
                        new[] { "charter_entry_suifu_diji", "charter_entry_suifu_diji" }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.currentRegionRuleEntryIds[0] = "entry_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.nodeStates[0].nodeId = "node_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.nodeStates =
                        new[]
                        {
                            data.charterRuntimeState.nodeStates[0],
                            data.charterRuntimeState.nodeStates[0],
                        }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.organizationAuthorizationVersions[0].authorizationVersionId =
                        "authorization_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.organizationAuthorizationVersions =
                        new[]
                        {
                            data.charterRuntimeState.organizationAuthorizationVersions[0],
                            data.charterRuntimeState.organizationAuthorizationVersions[0],
                        }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.organizationAuthorizationVersions =
                        Array.Empty<CharterAuthorizationVersionStateData>(), catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.currentCoverageSet[0] = "coverage_other", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.currentCoverageSet =
                        new[]
                        {
                            data.charterRuntimeState.currentCoverageSet[0],
                            data.charterRuntimeState.currentCoverageSet[0],
                            data.charterRuntimeState.currentCoverageSet[1],
                        }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.ruleEntryOccupancies =
                        new[]
                        {
                            data.charterRuntimeState.ruleEntryOccupancies[0],
                            data.charterRuntimeState.ruleEntryOccupancies[0],
                        }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.realitySupplyStates =
                        new[]
                        {
                            data.charterRuntimeState.realitySupplyStates[0],
                            data.charterRuntimeState.realitySupplyStates[0],
                        }, catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.negativeCommitResults =
                        Array.Empty<CharterCommitResultStateData>(), catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.positiveCommitResults[0].commitId = "commit_unknown", catalog);
                AssertRejectedWithoutMutation<ArgumentException>(session, seededJson, data =>
                    data.charterRuntimeState.nodeStates[0].state = "", catalog);

                // 非法档对完整旧会话的原子拒绝：每次失败后会话 JSON 与种子完全一致。
                Assert.AreEqual(seededJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterRestoreDoesNotReSettleAllocatedSuppliesOccupanciesOrCommits()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(
                        JsonUtility.ToJson(BuildCharterSaveData(BuildValidCharterState(), version))),
                    catalog);
                string firstJson = JsonUtility.ToJson(session.CaptureSaveData());

                // 重复读取只恢复已保存的 allocated／结果事实，不重放供给、占用或提交结算。
                session.RestoreSaveData(
                    JsonUtility.FromJson<GameSessionSaveData>(firstJson),
                    catalog);
                Assert.AreEqual(firstJson, JsonUtility.ToJson(session.CaptureSaveData()));

                Assert.AreEqual(3, session.CharterRuntimeState.realitySupplyStates.Length);
                Assert.AreEqual(
                    3,
                    session.CharterRuntimeState.realitySupplyStates.Count(
                        supply => supply.state == "allocated"));
                Assert.AreEqual(1, session.CharterRuntimeState.ruleEntryOccupancies.Length);
                Assert.AreEqual(3, session.CharterRuntimeState.nodeOccupancies.Length);
                Assert.AreEqual(1, session.CharterRuntimeState.positiveCommitResults.Length);
                Assert.AreEqual(1, session.CharterRuntimeState.negativeCommitResults.Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterPresenceRestoreFailsClosedWithoutTheStaticCatalog()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                string baselineJson = JsonUtility.ToJson(session.CaptureSaveData());
                GameSessionSaveData charterSave = BuildCharterSaveData(BuildValidCharterState(), version);

                // 没有唯一静态目录的 ContentCatalogData 不能为 schema 4 presence 提供校验来源。
                Assert.Throws<ArgumentException>(() => session.RestoreSaveData(charterSave, CreateCatalog()));
                Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterFirstFormalCommitWritesStateAndVersionAtomicallyAndRoundTrips()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                string unaccessedJson = JsonUtility.ToJson(session.CaptureSaveData());
                CharterSiteInteractionRuntime runtime = CreateCharterInteractionRuntime();
                CompleteCharterInteractionSteps(runtime);
                Assert.That(runtime.TryCreatePreparation(
                    out CharterInvocationPreparation preparation, out string prepReason), Is.True, prepReason);

                CharterRuleInvocationResult result = runtime.EvaluateFormal(
                    preparation, null, 100, "applied", "applied");
                Assert.IsTrue(result.Succeeded, result.Reason);

                // 首次成功：长期状态与目录版本从 null／0 一次原子替换为当前生产目录版本。
                CharterInvocationCommitResult commit =
                    session.CommitCharterFormalResult(catalog, result, preparation.CatalogVersion);
                Assert.IsTrue(commit.Succeeded, commit.Reason);
                Assert.IsNotNull(session.CharterRuntimeState);
                Assert.AreEqual(version, session.CharterDefinitionCatalogVersion);
                Assert.AreEqual(1, session.CharterRuntimeState.registeredRuleEntryIds.Length);
                Assert.AreEqual(1, session.CharterRuntimeState.currentRegionRuleEntryIds.Length);
                Assert.AreEqual(3, session.CharterRuntimeState.nodeOccupancies.Length);
                Assert.AreEqual(1, session.CharterRuntimeState.positiveCommitResults.Length);
                Assert.AreEqual(1, session.CharterRuntimeState.negativeCommitResults.Length);
                Assert.AreEqual(
                    3,
                    session.CharterRuntimeState.realitySupplyStates.Count(
                        supply => supply.state == CharterRuleRuntime.AllocatedSupplyState));

                // schema 4 保存／读取保持长期结果；读档不重复结算。
                string committedJson = JsonUtility.ToJson(session.CaptureSaveData());
                Assert.AreNotEqual(unaccessedJson, committedJson);
                session.ClearSession();
                session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(committedJson), catalog);
                Assert.AreEqual(committedJson, JsonUtility.ToJson(session.CaptureSaveData()));
                Assert.IsNotNull(session.CharterRuntimeState);
                Assert.AreEqual(version, session.CharterDefinitionCatalogVersion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterFirstFormalFailureKeepsUnaccessedStateAndZeroVersion()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            int version = LoadProductionStaticCatalog().DefinitionCatalogVersion;
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                string unaccessedJson = JsonUtility.ToJson(session.CaptureSaveData());
                CharterSiteInteractionRuntime runtime = CreateCharterInteractionRuntime();
                CompleteCharterInteractionSteps(runtime);
                Assert.That(runtime.TryCreatePreparation(
                    out CharterInvocationPreparation preparation, out string prepReason), Is.True, prepReason);
                CharterRuleInvocationResult result = runtime.EvaluateFormal(
                    preparation, null, 100, "applied", "applied");
                Assert.IsTrue(result.Succeeded, result.Reason);

                // 目录版本不一致 → 首次失败，长期状态与版本保持 null／0。
                CharterInvocationCommitResult versionMismatch =
                    session.CommitCharterFormalResult(catalog, result, version + 1);
                Assert.IsFalse(versionMismatch.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.VersionMismatch, versionMismatch.Reason);
                Assert.IsNull(session.CharterRuntimeState);
                Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                Assert.AreEqual(unaccessedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 无效结果（金丹未获胜）→ 失败关闭，不写候选。
                CharterRuleInvocationResult jindan = runtime.EvaluateJindan(preparation, 100, "applied", "applied");
                Assert.IsFalse(jindan.Succeeded);
                CharterInvocationCommitResult invalidResult =
                    session.CommitCharterFormalResult(catalog, jindan, preparation.CatalogVersion);
                Assert.IsFalse(invalidResult.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.InvalidResult, invalidResult.Reason);
                Assert.IsNull(session.CharterRuntimeState);
                Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                Assert.AreEqual(unaccessedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 规则级首次失败 → 提交拒绝，目录版本保持 0。
                CharterRuleInvocationResult failed = runtime.EvaluateFormal(
                    preparation, null, 100, "", "");
                Assert.IsFalse(failed.Succeeded);
                CharterInvocationCommitResult failedCommit =
                    session.CommitCharterFormalResult(catalog, failed, preparation.CatalogVersion);
                Assert.IsFalse(failedCommit.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.InvalidResult, failedCommit.Reason);
                Assert.IsNull(session.CharterRuntimeState);
                Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                Assert.AreEqual(unaccessedJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterRepeatedFormalConsumptionKeepsCommittedStateUnchanged()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                CharterSiteInteractionRuntime runtime = CreateCharterInteractionRuntime();
                CompleteCharterInteractionSteps(runtime);
                Assert.That(runtime.TryCreatePreparation(
                    out CharterInvocationPreparation preparation, out string prepReason), Is.True, prepReason);
                CharterRuleInvocationResult first = runtime.EvaluateFormal(
                    preparation, null, 100, "applied", "applied");
                Assert.IsTrue(first.Succeeded, first.Reason);
                Assert.IsTrue(session.CommitCharterFormalResult(catalog, first, preparation.CatalogVersion).Succeeded);

                CharterRuntimeStateData committed = session.CharterRuntimeState;
                string committedJson = JsonUtility.ToJson(session.CaptureSaveData());

                // 重复调用必须继续消费现有长期状态；全新 registered 候选不自举，allocated 供给拒绝重复消费。
                CharterRuleInvocationResult second = runtime.EvaluateFormal(
                    preparation, session.CharterRuntimeState, 100, "applied", "applied");
                Assert.IsFalse(second.Succeeded);
                Assert.AreEqual(CharterRuleRuntimeReasons.RealitySupplyUnavailable, second.Reason);
                CharterInvocationCommitResult commit =
                    session.CommitCharterFormalResult(catalog, second, preparation.CatalogVersion);
                Assert.IsFalse(commit.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.InvalidResult, commit.Reason);

                // 已有长期状态的重复失败保持原实例内容不变。
                Assert.AreSame(committed, session.CharterRuntimeState);
                Assert.AreEqual(committedJson, JsonUtility.ToJson(session.CaptureSaveData()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        [Test]
        public void CharterSecondCandidateRebootstrapCannotCommitAfterFirstSuccess()
        {
            var sessionObject = new GameObject("GameSessionTest");
            var session = sessionObject.AddComponent<GameSession>();
            ContentCatalogData catalog = CreateCatalogWithCharterStaticCatalog();
            try
            {
                session.BeginNewGame(null, "jiangzuo_hub");
                CharterSiteInteractionRuntime runtime = CreateCharterInteractionRuntime();
                CompleteCharterInteractionSteps(runtime);
                Assert.That(runtime.TryCreatePreparation(
                    out CharterInvocationPreparation preparation, out string prepReason), Is.True, prepReason);
                CharterRuleInvocationResult first = runtime.EvaluateFormal(
                    preparation, null, 100, "applied", "applied");
                Assert.IsTrue(first.Succeeded, first.Reason);
                Assert.IsTrue(session.CommitCharterFormalResult(catalog, first, preparation.CatalogVersion).Succeeded);
                Assert.IsNotNull(session.CharterRuntimeState);

                CharterRuntimeStateData committed = session.CharterRuntimeState;
                string committedJson = JsonUtility.ToJson(session.CaptureSaveData());

                // 直接反例：首次提交后即使再以同一 preparation 和 null 求值（candidate 重自举）
                // 仍返回成功，唯一提交入口也必须拒绝第二次成功提交，长期状态保持原实例内容不变。
                CharterRuleInvocationResult rebootstrap = runtime.EvaluateFormal(
                    preparation, null, 100, "applied", "applied");
                Assert.IsTrue(rebootstrap.Succeeded, rebootstrap.Reason);
                CharterInvocationCommitResult secondCommit =
                    session.CommitCharterFormalResult(catalog, rebootstrap, preparation.CatalogVersion);
                Assert.IsFalse(secondCommit.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.AlreadyCommitted, secondCommit.Reason);
                Assert.AreSame(committed, session.CharterRuntimeState);
                Assert.AreEqual(committedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 会话唯一正式调用入口与当前状态绑定：已有长期状态时不再以 candidate 重自举，
                // 继续消费现有状态并失败关闭，不尝试第二次提交。
                CharterInvocationCommitResult bound =
                    session.InvokeCharterFormal(runtime, preparation, catalog, 100, "applied", "applied");
                Assert.IsFalse(bound.Succeeded);
                Assert.AreEqual(CharterInvocationCommitReasons.InvalidResult, bound.Reason);
                Assert.AreSame(committed, session.CharterRuntimeState);
                Assert.AreEqual(committedJson, JsonUtility.ToJson(session.CaptureSaveData()));

                // 未接入状态下的首次正式调用仍经同一入口自举并一次原子提交。
                session.ClearSession();
                session.BeginNewGame(null, "jiangzuo_hub");
                Assert.IsNull(session.CharterRuntimeState);
                Assert.AreEqual(0, session.CharterDefinitionCatalogVersion);
                CharterInvocationCommitResult firstBound =
                    session.InvokeCharterFormal(runtime, preparation, catalog, 100, "applied", "applied");
                Assert.IsTrue(firstBound.Succeeded, firstBound.Reason);
                Assert.IsNotNull(session.CharterRuntimeState);
                Assert.AreEqual(preparation.CatalogVersion, session.CharterDefinitionCatalogVersion);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sessionObject);
            }
        }

        private static CharterSiteInteractionRuntime CreateCharterInteractionRuntime()
        {
            var site = AssetDatabase.LoadAssetAtPath<CharterSiteData>(
                "Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset");
            Assert.IsNotNull(site, "The single approved charter site asset is missing.");
            Assert.That(CharterSiteInteractionRuntime.TryCreate(
                site, LoadProductionStaticCatalog(), "guanzhong_city",
                out CharterSiteInteractionRuntime runtime, out string reason), Is.True, reason);
            return runtime;
        }

        private static void CompleteCharterInteractionSteps(CharterSiteInteractionRuntime runtime)
        {
            Assert.That(runtime.VerifyPassage(
                "capability_kaihe_jiuzhang_v1", "operator_old_water_station", "gate_old_water_station_pump").Succeeded,
                Is.True);
            Assert.That(runtime.VerifyManagement(
                "manager_old_water_station", "beneficiary_water_basin").Succeeded, Is.True);
            Assert.That(runtime.ConnectNodes(new[]
            {
                "node_old_water_station_charter",
                "node_old_water_station_waterworks",
                "node_old_water_station_river_wetland",
            }).Succeeded, Is.True);
            Assert.That(runtime.VerifyRuleEntryRegistration(
                "charter_entry_suifu_diji",
                "relic_world_charter",
                new[]
                {
                    "authorization_suifu_water_basin_v1",
                    "authorization_taixuan_seal_old_water_station_management_v1",
                }).Succeeded, Is.True);
            Assert.That(runtime.PrepareRealitySupplies(new[]
            {
                "supply_suifu_registered_seasonal_rain",
                "supply_suifu_connected_water_balance",
                "supply_suifu_wetland_land_capacity",
            }).Succeeded, Is.True);
        }

        private static FoundationPurpleMansionStateData CreateCompleteFoundationPurpleMansionState()
        {
            var state = ScriptableObject.CreateInstance<FoundationPurpleMansionStateData>();
            state.schemaId = "foundationPurpleMansionState";
            state.schemaVersion = 1;
            state.characterId = "save_fixture";
            state.foundationState = new FoundationStateRecord
            {
                foundationInstanceId = "foundation_save_fixture",
                foundationDefinitionId = "foundation_definition",
                sourceGongFaId = "gongfa_save_fixture",
                phase = FoundationPhase.Phase4,
                continuousProgress = 400f,
                phaseBoundarySetId = "phase_boundaries",
                naturalMansionCapacity = 1,
                releasedNaturalCapacity = 1,
                expansionGrants = Array.Empty<FoundationExpansionGrant>(),
                expandedMansionCapacity = 0,
                totalMansionCapacity = 1,
            };
            state.mansionStates = new[]
            {
                new PurpleMansionStateRecord
                {
                    mansionKind = PurpleMansionKind.Ming,
                    state = PurpleMansionBuildState.Complete,
                    mansionInstanceId = "mansion_ming",
                    mansionBodyEffectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                    guardianAbilityInstanceId = "guardian_ming",
                    sourceSpellId = "spell_ming",
                    upgradePlanId = "upgrade_ming",
                    sourceSpellDisposition = "RETAIN",
                },
                NotBuilt(PurpleMansionKind.Hun),
                NotBuilt(PurpleMansionKind.Shi),
                NotBuilt(PurpleMansionKind.Wu),
                NotBuilt(PurpleMansionKind.Yun),
            };
            state.effectBindings = new[]
            {
                new FoundationEffectBinding
                {
                    effectBindingId = "MANSION_BODY_MING_YUAN_HUIHU",
                    carrierKind = FoundationEffectCarrierKind.MansionBody,
                    carrierId = "mansion_ming",
                    order = 1,
                    trigger = "fixture_trigger",
                    conditions = Array.Empty<string>(),
                    target = "fixture_target",
                    atomicEffectType = "fixture_effect",
                    parameters = Array.Empty<string>(),
                },
            };
            state.guardianAbilities = new[]
            {
                new GuardianAbilityRecord
                {
                    abilityInstanceId = "guardian_ming",
                    abilityDefinitionId = "ability_ming",
                    mansionInstanceId = "mansion_ming",
                    sourceSpellId = "spell_ming",
                    upgradePlanId = "upgrade_ming",
                    sourceSpellDisposition = "RETAIN",
                    form = GuardianAbilityForm.Passive,
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            state.enhancementNodes = new[]
            {
                new EnhancementNodeRecord
                {
                    nodeId = "node_ming_1",
                    abilityInstanceId = "guardian_ming",
                    nodeKind = EnhancementNodeKind.Cultivation,
                    requirements = Array.Empty<string>(),
                    effectBindingIds = Array.Empty<string>(),
                },
            };
            state.jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan };
            return state;
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind)
        {
            return new PurpleMansionStateRecord
            {
                mansionKind = kind,
                state = PurpleMansionBuildState.NotBuilt,
            };
        }

        private static StateStepSnapshot Steps(bool firstValue)
        {
            return new StateStepSnapshot(
                firstValue,
                !firstValue,
                firstValue,
                !firstValue,
                firstValue,
                !firstValue,
                firstValue);
        }

        private static void AssertEveryStepIsSeparate(StateStepSnapshot steps)
        {
            Assert.IsTrue(steps.Shown);
            Assert.IsFalse(steps.Clicked);
            Assert.IsTrue(steps.Opened);
            Assert.IsFalse(steps.Selected);
            Assert.IsTrue(steps.Applied);
            Assert.IsFalse(steps.Completed);
            Assert.IsTrue(steps.Persisted);
        }

        private void AssertRejectedWithoutMutation<TException>(
            GameSession session,
            string baselineJson,
            Action<GameSessionSaveData> mutate,
            ContentCatalogData catalog = null)
            where TException : Exception
        {
            GameSessionSaveData invalid =
                JsonUtility.FromJson<GameSessionSaveData>(baselineJson);
            mutate(invalid);

            Assert.Throws<TException>(() => session.RestoreSaveData(
                invalid,
                catalog ?? CreateCatalog()));
            Assert.AreEqual(baselineJson, JsonUtility.ToJson(session.CaptureSaveData()));
        }

        private static QuestStateSaveData Clone(QuestStateSaveData source)
        {
            return JsonUtility.FromJson<QuestStateSaveData>(JsonUtility.ToJson(source));
        }

        private static InventoryStateSaveData Clone(InventoryStateSaveData source)
        {
            return JsonUtility.FromJson<InventoryStateSaveData>(JsonUtility.ToJson(source));
        }

        private static NpcStateSaveData Clone(NpcStateSaveData source)
        {
            return JsonUtility.FromJson<NpcStateSaveData>(JsonUtility.ToJson(source));
        }

        private static BountyStateSaveData Clone(BountyStateSaveData source)
        {
            return JsonUtility.FromJson<BountyStateSaveData>(JsonUtility.ToJson(source));
        }

        private ContentCatalogData CreateCatalog(params string[] bountyIds)
        {
            var bounties = new List<BountyData>();
            foreach (string bountyId in bountyIds)
            {
                var bounty = ScriptableObject.CreateInstance<BountyData>();
                bounty.bountyId = bountyId;
                bounty.requiredCount = 1;
                temporaryAssets.Add(bounty);
                bounties.Add(bounty);
            }

            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            temporaryAssets.Add(catalog);
            catalog.ReplaceEntries(null, null, null, bounties.ToArray());
            return catalog;
        }

        private ContentCatalogData CreateCatalogWithRequiredCount(
            string bountyId,
            int requiredCount)
        {
            var bounty = ScriptableObject.CreateInstance<BountyData>();
            bounty.bountyId = bountyId;
            bounty.requiredCount = requiredCount;
            temporaryAssets.Add(bounty);

            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            temporaryAssets.Add(catalog);
            catalog.ReplaceEntries(null, null, null, new[] { bounty });
            return catalog;
        }

        private ContentCatalogData CreateCatalogWithCharterStaticCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            temporaryAssets.Add(catalog);
            catalog.SetCharterRuleStaticCatalog(LoadProductionStaticCatalog());
            return catalog;
        }

        private static CharterRuleStaticCatalogData LoadProductionStaticCatalog()
        {
            var staticCatalog = AssetDatabase.LoadAssetAtPath<CharterRuleStaticCatalogData>(
                "Assets/Data/CharterRuleStaticCatalog/CharterRuleStaticCatalog.asset");
            Assert.IsNotNull(staticCatalog, "The single approved charter static catalog asset is missing.");
            return staticCatalog;
        }

        private static GameSessionSaveData BuildCharterSaveData(
            CharterRuntimeStateData state,
            int definitionCatalogVersion)
        {
            return new GameSessionSaveData
            {
                schemaVersion = GameSessionSnapshot.CharterSchemaVersion,
                currentWorldNodeId = "guanzhong_hub",
                worldYear = GameSession.InitialWorldYear,
                worldSeasonId = GameSession.InitialWorldSeasonId,
                worldDay = 5,
                worldTimeOfDayId = GameSession.InitialWorldTimeOfDayId,
                hasCharterRuntimeState = state != null,
                charterDefinitionCatalogVersion = state == null ? 0 : definitionCatalogVersion,
                charterRuntimeState = state,
            };
        }

        private static CharterRuntimeStateData BuildValidCharterState()
        {
            return new CharterRuntimeStateData
            {
                stateId = "charter_runtime_save_fixture",
                charterRelicState = "recognized",
                worldSealState = "recognized",
                registeredRuleEntryIds = new[] { "charter_entry_suifu_diji" },
                currentRegionRuleEntryIds = new[] { "charter_entry_suifu_diji" },
                nodeStates = new[]
                {
                    new CharterNodeRuntimeStateData
                    {
                        nodeId = "node_old_water_station_charter",
                        state = "connected",
                    },
                    new CharterNodeRuntimeStateData
                    {
                        nodeId = "node_old_water_station_waterworks",
                        state = "connected",
                    },
                    new CharterNodeRuntimeStateData
                    {
                        nodeId = "node_old_water_station_river_wetland",
                        state = "connected",
                    },
                },
                organizationAuthorizationVersions = new[]
                {
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = "authorization_suifu_water_basin_v1",
                        state = "recognized",
                    },
                    new CharterAuthorizationVersionStateData
                    {
                        authorizationVersionId = "authorization_taixuan_seal_old_water_station_management_v1",
                        state = "recognized",
                    },
                },
                currentCoverageSet = new[]
                {
                    "coverage_old_water_station_charter",
                    "coverage_old_water_station_waterworks",
                    "coverage_old_water_station_river_wetland",
                },
                ruleEntryOccupancies = new[]
                {
                    new CharterOccupancyStateData
                    {
                        resourceId = "charter_entry_suifu_diji",
                        occupancyId = "occupancy_save_fixture_v1",
                    },
                },
                nodeOccupancies = new[]
                {
                    new CharterOccupancyStateData
                    {
                        resourceId = "node_old_water_station_charter",
                        occupancyId = "occupancy_save_waterworks_v1",
                    },
                    new CharterOccupancyStateData
                    {
                        resourceId = "node_old_water_station_waterworks",
                        occupancyId = "occupancy_save_waterworks_v1",
                    },
                    new CharterOccupancyStateData
                    {
                        resourceId = "node_old_water_station_river_wetland",
                        occupancyId = "occupancy_save_waterworks_v1",
                    },
                },
                realitySupplyStates = new[]
                {
                    new CharterRealitySupplyStateData
                    {
                        realitySupplyId = "supply_suifu_registered_seasonal_rain",
                        state = "allocated",
                    },
                    new CharterRealitySupplyStateData
                    {
                        realitySupplyId = "supply_suifu_connected_water_balance",
                        state = "allocated",
                    },
                    new CharterRealitySupplyStateData
                    {
                        realitySupplyId = "supply_suifu_wetland_land_capacity",
                        state = "allocated",
                    },
                },
                positiveCommitResults = new[]
                {
                    new CharterCommitResultStateData
                    {
                        commitId = "commit_suifu_diji_positive_ecology",
                        resultState = "applied",
                    },
                },
                negativeCommitResults = new[]
                {
                    new CharterCommitResultStateData
                    {
                        commitId = "commit_suifu_diji_negative_reallocation",
                        resultState = "applied",
                    },
                },
            };
        }
    }
}
