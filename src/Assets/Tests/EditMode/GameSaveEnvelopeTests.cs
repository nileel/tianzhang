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

        [Test]
        public void ExplicitCombatModifiersRoundTripLosslesslyThroughSaveAndRestore()
        {
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "显式加成角色";
            definition.realmMultiplier = 1f;
            definition.unarmedBasicAttackProfileId = "basic_unarmed";
            definition.hpBonus = 12.4f;
            definition.mpBonus = 7.6f;
            definition.physAtkBonus = 3.2f;
            definition.magAtkBonus = 4.8f;
            definition.physDefBonus = 1.1f;
            definition.magDefBonus = 2.9f;
            definition.blockRate = 15f;
            definition.blockReduction = 25f;
            definition.soulShieldRate = 10f;
            definition.soulShieldReduction = 20f;
            definition.dodgeRate = 5f;
            definition.critRate = 8f;
            definition.critDamage = 15f;
            definition.hitRateBonus = 3f;
            appearance = ScriptableObject.CreateInstance<AppearanceProfileData>();
            appearance.appearanceProfileId = AppearanceProfileData.NoneId;
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.SetAppearanceProfiles(new[] { appearance });

            var runtime = new GameRuntime();
            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", definition),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            int expectedMaximumHealth = runtime.Player.Resources.MaximumHealth;
            int expectedMaximumSpirit = runtime.Player.Resources.MaximumSpirit;

            GameSaveEnvelope saved = runtime.CaptureSave();
            Assert.That(saved.player.hpBonus, Is.EqualTo(12.4f));
            Assert.That(saved.player.mpBonus, Is.EqualTo(7.6f));
            Assert.That(saved.player.physAtkBonus, Is.EqualTo(3.2f));
            Assert.That(saved.player.magAtkBonus, Is.EqualTo(4.8f));
            Assert.That(saved.player.physDefBonus, Is.EqualTo(1.1f));
            Assert.That(saved.player.magDefBonus, Is.EqualTo(2.9f));
            Assert.That(saved.player.blockRate, Is.EqualTo(15f));
            Assert.That(saved.player.blockReduction, Is.EqualTo(25f));
            Assert.That(saved.player.soulShieldRate, Is.EqualTo(10f));
            Assert.That(saved.player.soulShieldReduction, Is.EqualTo(20f));
            Assert.That(saved.player.dodgeRate, Is.EqualTo(5f));
            Assert.That(saved.player.critRate, Is.EqualTo(8f));
            Assert.That(saved.player.critDamage, Is.EqualTo(15f));
            Assert.That(saved.player.hitRateBonus, Is.EqualTo(3f));

            var restored = new GameRuntime();
            restored.RestoreSave(saved, catalog);

            Assert.That(restored.Player.Resources.MaximumHealth, Is.EqualTo(expectedMaximumHealth));
            Assert.That(restored.Player.Resources.MaximumSpirit, Is.EqualTo(expectedMaximumSpirit));
            Assert.That(restored.Player.CombatModifiers.HpBonus, Is.EqualTo(12.4f));
            Assert.That(restored.Player.CombatModifiers.MpBonus, Is.EqualTo(7.6f));
            Assert.That(restored.Player.CombatModifiers.PhysAtkBonus, Is.EqualTo(3.2f));
            Assert.That(restored.Player.CombatModifiers.MagAtkBonus, Is.EqualTo(4.8f));
            Assert.That(restored.Player.CombatModifiers.PhysDefBonus, Is.EqualTo(1.1f));
            Assert.That(restored.Player.CombatModifiers.MagDefBonus, Is.EqualTo(2.9f));
            Assert.That(restored.Player.CombatModifiers.BlockRate, Is.EqualTo(15f));
            Assert.That(restored.Player.CombatModifiers.BlockReduction, Is.EqualTo(25f));
            Assert.That(restored.Player.CombatModifiers.SoulShieldRate, Is.EqualTo(10f));
            Assert.That(restored.Player.CombatModifiers.SoulShieldReduction, Is.EqualTo(20f));
            Assert.That(restored.Player.CombatModifiers.DodgeRate, Is.EqualTo(5f));
            Assert.That(restored.Player.CombatModifiers.CritRate, Is.EqualTo(8f));
            Assert.That(restored.Player.CombatModifiers.CritDamage, Is.EqualTo(15f));
            Assert.That(restored.Player.CombatModifiers.HitRateBonus, Is.EqualTo(3f));
            Assert.That(restored.CaptureSaveJson(), Is.EqualTo(runtime.CaptureSaveJson()));
        }

        [Test]
        public void ZeroBonusCharacterKeepsCombatModifierFieldsAtZero()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope saved = runtime.CaptureSave();
            Assert.That(saved.player.hpBonus, Is.EqualTo(0f));
            Assert.That(saved.player.mpBonus, Is.EqualTo(0f));
            Assert.That(saved.player.physAtkBonus, Is.EqualTo(0f));
            Assert.That(saved.player.magAtkBonus, Is.EqualTo(0f));
            Assert.That(saved.player.physDefBonus, Is.EqualTo(0f));
            Assert.That(saved.player.magDefBonus, Is.EqualTo(0f));
            Assert.That(saved.player.blockRate, Is.EqualTo(0f));
            Assert.That(saved.player.blockReduction, Is.EqualTo(0f));
            Assert.That(saved.player.soulShieldRate, Is.EqualTo(0f));
            Assert.That(saved.player.soulShieldReduction, Is.EqualTo(0f));
            Assert.That(saved.player.dodgeRate, Is.EqualTo(0f));
            Assert.That(saved.player.critRate, Is.EqualTo(0f));
            Assert.That(saved.player.critDamage, Is.EqualTo(0f));
            Assert.That(saved.player.hitRateBonus, Is.EqualTo(0f));
        }

        [Test]
        public void PlayerPayloadWithOnlyExplicitCombatModifiersFailsClosed()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = new GameRuntime().CaptureSave();
            invalid.hasPlayer = false;
            invalid.player = new CharacterRecord { hpBonus = 5f };

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [Test]
        public void SchemaTwoEnvelopeMissingCombatModifierFieldsDefaultsToZero()
        {
            GameSaveEnvelope envelope = GameSaveSerializer.Deserialize(
                "{\"schemaVersion\":2,\"hasPlayer\":true,\"player\":{\"characterId\":\"player\",\"displayName\":\"旧档\"}}");
            Assert.That(envelope.player.hpBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.mpBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.physAtkBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.magAtkBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.physDefBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.magDefBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.blockRate, Is.EqualTo(0f));
            Assert.That(envelope.player.blockReduction, Is.EqualTo(0f));
            Assert.That(envelope.player.soulShieldRate, Is.EqualTo(0f));
            Assert.That(envelope.player.soulShieldReduction, Is.EqualTo(0f));
            Assert.That(envelope.player.dodgeRate, Is.EqualTo(0f));
            Assert.That(envelope.player.critRate, Is.EqualTo(0f));
            Assert.That(envelope.player.critDamage, Is.EqualTo(0f));
            Assert.That(envelope.player.hitRateBonus, Is.EqualTo(0f));
        }

        [Test]
        public void SchemaOneMigrationKeepsCombatModifierFieldsAtZero()
        {
            GameSaveEnvelope envelope = GameSaveSerializer.Deserialize(
                "{\"schemaVersion\":1,\"hasPlayer\":true,\"player\":{\"characterId\":\"player\",\"displayName\":\"旧档\"}}");
            Assert.That(envelope.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.That(envelope.player.hpBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.mpBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.physAtkBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.magAtkBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.physDefBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.magDefBonus, Is.EqualTo(0f));
            Assert.That(envelope.player.blockRate, Is.EqualTo(0f));
            Assert.That(envelope.player.blockReduction, Is.EqualTo(0f));
            Assert.That(envelope.player.soulShieldRate, Is.EqualTo(0f));
            Assert.That(envelope.player.soulShieldReduction, Is.EqualTo(0f));
            Assert.That(envelope.player.dodgeRate, Is.EqualTo(0f));
            Assert.That(envelope.player.critRate, Is.EqualTo(0f));
            Assert.That(envelope.player.critDamage, Is.EqualTo(0f));
            Assert.That(envelope.player.hitRateBonus, Is.EqualTo(0f));
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
