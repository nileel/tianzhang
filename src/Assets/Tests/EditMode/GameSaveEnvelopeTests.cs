using System.IO;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class GameSaveEnvelopeTests
    {
        private CharacterData definition;
        private ContentCatalogData catalog;
        private ItemData item;

        [TearDown]
        public void TearDown()
        {
            if (definition != null) Object.DestroyImmediate(definition);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (item != null) Object.DestroyImmediate(item);
        }

        [Test]
        public void SchemaOneRoundTripIsCanonicalAndIdempotent()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            string first = source.CaptureSaveJson();
            var restored = new GameRuntime();

            restored.RestoreSaveJson(first, catalog);
            string second = restored.CaptureSaveJson();

            Assert.That(second, Is.EqualTo(first));
            Assert.That(restored.Player.Identity.CharacterId, Is.EqualTo("player"));
            Assert.That(restored.Navigation.AdventureId, Is.EqualTo("adventure_test"));
            Assert.That(restored.CaptureSave().inventory[0].quantity, Is.EqualTo(2));
            Assert.That(restored.Player.UnarmedBasicAttackProfileId, Is.EqualTo("basic_unarmed"));
        }

        [Test]
        public void FailedRestoreDoesNotReplaceAnyLiveOwner()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            string baseline = runtime.CaptureSaveJson();
            GameSaveEnvelope invalid = runtime.CaptureSave();
            invalid.inventory[0].quantity = item.maxStack + 1;

            Assert.Throws<System.ArgumentException>(() => runtime.RestoreSave(invalid, catalog));

            Assert.That(runtime.CaptureSaveJson(), Is.EqualTo(baseline));
        }

        [TestCase("{\"schemaVersion\":4}")]
        [TestCase("{\"schemaVersion\":99}")]
        [TestCase("not-json")]
        public void LegacyUnknownAndInvalidJsonFailClosed(string json)
        {
            Assert.Throws<InvalidDataException>(() => GameSaveSerializer.Deserialize(json));
        }

        private GameRuntime CreateRuntimeWithInventory()
        {
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "存档角色";
            definition.realmMultiplier = 1f;
            definition.unarmedBasicAttackProfileId = "basic_unarmed";
            item = ScriptableObject.CreateInstance<ItemData>();
            item.itemId = "item_test";
            item.contentScope = InventoryGrantUseCase.ProductionContentScope;
            item.maxStack = 10;
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.ReplaceEntries(null, null, new[] { item }, null);

            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", definition),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            Assert.That(runtime.InventoryGrants.Grant(
                catalog,
                new[] { new InventoryGrantRequest(item.itemId, 2) }).Applied,
                Is.True);
            runtime.EnterSettlement("settlement_test");
            runtime.EnterAdventure("adventure_test", SceneReturnTarget.Settlement("settlement_test"));
            return runtime;
        }
    }
}
