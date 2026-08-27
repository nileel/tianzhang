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
        private AppearanceProfileData appearance;

        [TearDown]
        public void TearDown()
        {
            if (definition != null) Object.DestroyImmediate(definition);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (item != null) Object.DestroyImmediate(item);
            if (appearance != null) Object.DestroyImmediate(appearance);
        }

        [Test]
        public void SchemaTwoRoundTripIsCanonicalAndIdempotent()
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
            Assert.That(restored.Player.AppearanceProfileId, Is.EqualTo(AppearanceProfileData.NoneId));
        }

        [Test]
        public void SchemaTwoDefaultNavigationRoundTripRemainsCanonical()
        {
            var source = new GameRuntime();
            string first = source.CaptureSaveJson();
            var restored = new GameRuntime();

            restored.RestoreSaveJson(first, null);

            Assert.That(restored.CaptureSaveJson(), Is.EqualTo(first));
        }

        [Test]
        public void SchemaOneDeserializeMigratesAppearanceToNoneAndResavesSchemaTwo()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            GameSaveEnvelope legacy = source.CaptureSave();
            legacy.schemaVersion = GameSaveSerializer.LegacySchemaVersion;
            legacy.player.appearanceProfileId = null;

            GameSaveEnvelope migrated = GameSaveSerializer.Deserialize(JsonUtility.ToJson(legacy));

            Assert.That(migrated.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.That(migrated.player.appearanceProfileId, Is.EqualTo(AppearanceProfileData.NoneId));

            var restored = new GameRuntime();
            restored.RestoreSave(migrated, catalog);
            Assert.That(restored.Player.AppearanceProfileId, Is.EqualTo(AppearanceProfileData.NoneId));
            Assert.That(restored.CaptureSave().schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
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

        [TestCase(true)]
        [TestCase(false)]
        public void SinglePlayerOrCultivationPayloadFailsClosedWithoutReplacingAnyLiveOwner(bool includePlayerPayload)
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope donor = runtime.CaptureSave();
            GameSaveEnvelope invalid = new GameRuntime().CaptureSave();
            invalid.hasPlayer = false;
            invalid.player = includePlayerPayload ? donor.player : null;
            invalid.cultivation = includePlayerPayload ? null : donor.cultivation;
            if (!includePlayerPayload)
                invalid.cultivation.foundationPhase = 1;

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [Test]
        public void UnknownAppearanceProfileFailsClosedWithoutReplacingAnyLiveOwner()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = runtime.CaptureSave();
            invalid.player.appearanceProfileId = "appearance_unknown";

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [TestCase(false, true, 0)]
        [TestCase(true, false, 1)]
        [TestCase(false, false, 1)]
        [TestCase(true, true, 0)]
        public void InvalidCharterPresenceFailsClosedWithoutReplacingAnyLiveOwner(
            bool hasRuntimeState,
            bool includeRuntimeState,
            int definitionCatalogVersion)
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = runtime.CaptureSave();
            invalid.charter.hasRuntimeState = hasRuntimeState;
            invalid.charter.runtimeState = includeRuntimeState
                ? new CharterRuntimeStateData { stateId = "unexpected_state" }
                : null;
            invalid.charter.definitionCatalogVersion = definitionCatalogVersion;

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [TestCase(GameplaySceneNames.World, null, null)]
        [TestCase(GameplaySceneNames.World, "guanzhong_hub", "guanzhong_city")]
        [TestCase(GameplaySceneNames.Settlement, null, null)]
        [TestCase(GameplaySceneNames.Settlement, "guanzhong_hub", "guanzhong_city")]
        [TestCase("UnknownScene", "guanzhong_hub", null)]
        [TestCase("", "guanzhong_hub", null)]
        public void InvalidNavigationReturnTargetFailsClosedWithoutReplacingAnyLiveOwner(
            string returnSceneName,
            string returnWorldNodeId,
            string returnSettlementId)
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = runtime.CaptureSave();
            invalid.navigation.returnSceneName = returnSceneName;
            invalid.navigation.returnWorldNodeId = returnWorldNodeId;
            invalid.navigation.returnSettlementId = returnSettlementId;

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [TestCase("{\"schemaVersion\":4}")]
        [TestCase("{\"schemaVersion\":99}")]
        [TestCase("not-json")]
        public void LegacyUnknownAndInvalidJsonFailClosed(string json)
        {
            Assert.Throws<InvalidDataException>(() => GameSaveSerializer.Deserialize(json));
        }

        private void AssertRestoreFailsWithoutChangingRuntime(
            GameRuntime runtime,
            GameSaveEnvelope invalid)
        {
            string baseline = runtime.CaptureSaveJson();

            Assert.Throws<System.ArgumentException>(() => runtime.RestoreSave(invalid, catalog));

            Assert.That(runtime.CaptureSaveJson(), Is.EqualTo(baseline));
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
            appearance = ScriptableObject.CreateInstance<AppearanceProfileData>();
            appearance.appearanceProfileId = AppearanceProfileData.NoneId;
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.ReplaceEntries(null, null, new[] { item }, null);
            catalog.SetAppearanceProfiles(new[] { appearance });

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
