using System;
using NUnit.Framework;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class SessionStateSnapshotTests
    {
        [TearDown]
        public void TearDown()
        {
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
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
                session.RestoreSaveData(serialized);

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

            session.RestoreSaveData(JsonUtility.FromJson<GameSessionSaveData>(legacyJson));

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

        private static void AssertRejectedWithoutMutation<TException>(
            GameSession session,
            string baselineJson,
            Action<GameSessionSaveData> mutate)
            where TException : Exception
        {
            GameSessionSaveData invalid =
                JsonUtility.FromJson<GameSessionSaveData>(baselineJson);
            mutate(invalid);

            Assert.Throws<TException>(() => session.RestoreSaveData(invalid));
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
    }
}
