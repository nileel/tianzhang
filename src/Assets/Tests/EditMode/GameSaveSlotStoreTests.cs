using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class GameSaveSlotStoreTests
    {
        private readonly List<UnityEngine.Object> ownedObjects = new List<UnityEngine.Object>();
        private string temporaryDirectory;

        [SetUp]
        public void SetUp()
        {
            temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "TianZhang.GameSaveSlotStoreTests",
                Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < ownedObjects.Count; index++)
            {
                if (ownedObjects[index] != null)
                    UnityEngine.Object.DestroyImmediate(ownedObjects[index]);
            }
            ownedObjects.Clear();

            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, true);
        }

        [Test]
        public void NewSlotRoundTripPreservesCanonicalEnvelopeAndSummary()
        {
            var store = new GameSaveSlotStore(temporaryDirectory);
            GameSaveEnvelope envelope = CreateEnvelope("player_a", "初始角色");

            GameSaveSlotWriteResult write = store.Write("slot_a", envelope);
            GameSaveSlotReadResult read = store.Read("slot_a");
            GameSaveSlotListResult list = store.ListSlots();

            Assert.That(write.Succeeded, Is.True);
            Assert.That(read.Succeeded, Is.True);
            Assert.That(
                GameSaveSerializer.Serialize(read.Envelope),
                Is.EqualTo(GameSaveSerializer.Serialize(envelope)));
            Assert.That(list.Succeeded, Is.True);
            Assert.That(list.Slots.Count, Is.EqualTo(1));
            Assert.That(list.Slots[0].SlotId, Is.EqualTo("slot_a"));
            Assert.That(list.Slots[0].CharacterId, Is.EqualTo("player_a"));
            Assert.That(list.Slots[0].CharacterDisplayName, Is.EqualTo("初始角色"));
            Assert.That(list.Slots[0].IsReadable, Is.True);
            Assert.That(list.Slots[0].LastWriteTimeUtc, Is.Not.EqualTo(DateTime.MinValue));
        }

        [Test]
        public void ExistingSlotReplacementExposesOnlyTheNewEnvelope()
        {
            var store = new GameSaveSlotStore(temporaryDirectory);
            GameSaveEnvelope first = CreateEnvelope("player_a", "旧角色");
            GameSaveEnvelope second = CreateEnvelope("player_b", "新角色");

            Assert.That(store.Write("slot_a", first).Succeeded, Is.True);
            Assert.That(store.Write("slot_a", second).Succeeded, Is.True);

            GameSaveSlotReadResult read = store.Read("slot_a");
            Assert.That(read.Succeeded, Is.True);
            Assert.That(read.Envelope.player.characterId, Is.EqualTo("player_b"));
            Assert.That(read.Envelope.player.displayName, Is.EqualTo("新角色"));
            Assert.That(Directory.GetFiles(temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [Test]
        public void FailedReplacementKeepsTheOriginalFileByteForByte()
        {
            var store = new GameSaveSlotStore(temporaryDirectory);
            Assert.That(store.Write("slot_a", CreateEnvelope("player_a", "旧角色")).Succeeded, Is.True);
            string slotPath = Path.Combine(temporaryDirectory, "slot_a.json");
            byte[] before = File.ReadAllBytes(slotPath);

            GameSaveSlotWriteResult write;
            using (new FileStream(slotPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                write = store.Write("slot_a", CreateEnvelope("player_b", "新角色"));
            }

            Assert.That(write.Succeeded, Is.False);
            Assert.That(write.FailureReason, Is.EqualTo(GameSaveSlotFailureReason.WriteFailed));
            Assert.That(File.ReadAllBytes(slotPath), Is.EqualTo(before));
            Assert.That(Directory.GetFiles(temporaryDirectory, "*.tmp"), Is.Empty);
        }

        [TestCase("")]
        [TestCase("../slot")]
        [TestCase("..\\slot")]
        [TestCase("slot/name")]
        [TestCase("slot\\name")]
        [TestCase("含中文")]
        [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void InvalidSlotIdsAreRejectedWithoutCreatingFiles(string slotId)
        {
            var store = new GameSaveSlotStore(temporaryDirectory);

            GameSaveSlotWriteResult write = store.Write(slotId, CreateEnvelope("player", "角色"));
            GameSaveSlotReadResult read = store.Read(slotId);

            Assert.That(write.FailureReason, Is.EqualTo(GameSaveSlotFailureReason.InvalidSlotId));
            Assert.That(read.FailureReason, Is.EqualTo(GameSaveSlotFailureReason.InvalidSlotId));
            Assert.That(Directory.Exists(temporaryDirectory), Is.False);
        }

        [Test]
        public void CorruptSchemaAndMissingPlayerSlotsRemainVisibleWithoutBlockingValidSlots()
        {
            var store = new GameSaveSlotStore(temporaryDirectory);
            Assert.That(store.Write("valid", CreateEnvelope("player", "可读角色")).Succeeded, Is.True);
            File.WriteAllText(Path.Combine(temporaryDirectory, "broken.json"), "not-json");
            File.WriteAllText(Path.Combine(temporaryDirectory, "old.json"), "{\"schemaVersion\":4}");
            File.WriteAllText(Path.Combine(temporaryDirectory, "empty.json"), "{\"schemaVersion\":1,\"hasPlayer\":false}");

            GameSaveSlotListResult list = store.ListSlots();

            Assert.That(list.Succeeded, Is.True);
            Assert.That(list.Slots.Select(slot => slot.SlotId), Is.EqualTo(new[]
            {
                "broken", "empty", "old", "valid",
            }));
            Assert.That(Find(list, "broken").FailureReason, Is.EqualTo(GameSaveSlotFailureReason.InvalidSaveData));
            Assert.That(Find(list, "old").FailureReason, Is.EqualTo(GameSaveSlotFailureReason.InvalidSaveData));
            Assert.That(Find(list, "empty").FailureReason, Is.EqualTo(GameSaveSlotFailureReason.MissingPlayerPayload));
            Assert.That(Find(list, "valid").IsReadable, Is.True);
        }

        [Test]
        public void DirectoryIoFailureReturnsAStableFailureWithoutCreatingASecondLocation()
        {
            string blockedPath = Path.Combine(temporaryDirectory, "blocked");
            Directory.CreateDirectory(temporaryDirectory);
            File.WriteAllText(blockedPath, "not a directory");
            var store = new GameSaveSlotStore(blockedPath);

            GameSaveSlotListResult list = store.ListSlots();
            GameSaveSlotWriteResult write = store.Write("slot_a", CreateEnvelope("player", "角色"));

            Assert.That(list.Succeeded, Is.False);
            Assert.That(list.FailureReason, Is.EqualTo(GameSaveSlotFailureReason.DirectoryUnavailable));
            Assert.That(write.Succeeded, Is.False);
            Assert.That(write.FailureReason, Is.EqualTo(GameSaveSlotFailureReason.DirectoryUnavailable));
        }

        private static GameSaveSlotSummary Find(GameSaveSlotListResult list, string slotId)
        {
            return list.Slots.Single(slot => slot.SlotId == slotId);
        }

        private GameSaveEnvelope CreateEnvelope(string characterId, string displayName)
        {
            CharacterData definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = displayName;
            definition.realmMultiplier = 1f;
            ownedObjects.Add(definition);

            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition(characterId, definition),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            return runtime.CaptureSave();
        }
    }
}
