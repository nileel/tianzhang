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
    }
}
