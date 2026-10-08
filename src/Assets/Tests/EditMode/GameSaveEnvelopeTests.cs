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
        public void SchemaThreeRoundTripIsCanonicalAndIdempotent()
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
        public void CurrentSchemaDefaultNavigationRoundTripRemainsCanonical()
        {
            var source = new GameRuntime();
            string first = source.CaptureSaveJson();
            var restored = new GameRuntime();

            restored.RestoreSaveJson(first, null);

            Assert.That(restored.CaptureSaveJson(), Is.EqualTo(first));
        }

        [Test]
        public void VisibleRootFactsRoundTripThroughCurrentSchemaJsonWithoutChangingLoadout()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            definition.visibleRootId = "root_water_middle";
            definition.visibleRootGrade = "中品";
            definition.visibleRootElement = "水";
            definition.availableSpells = new[] { "already_known_spell" };
            definition.equippedSpells = new[] { "already_known_spell" };
            definition.maxSpellSlots = 1;
            source.Player.Restore(CharacterRuntimeProfile.FromDefinition("player", definition).Capture());
            GameSaveEnvelope save = source.CaptureSave();
            Assert.That(save.player.visibleRootId, Is.EqualTo(definition.visibleRootId));
            Assert.That(save.player.visibleRootGrade, Is.EqualTo(definition.visibleRootGrade));
            Assert.That(save.player.visibleRootElement, Is.EqualTo(definition.visibleRootElement));
            string json = source.CaptureSaveJson();
            var restored = new GameRuntime();

            restored.RestoreSaveJson(json, catalog);

            Assert.That(restored.Player.VisibleRootId, Is.EqualTo(definition.visibleRootId));
            Assert.That(restored.Player.VisibleRootGrade, Is.EqualTo(definition.visibleRootGrade));
            Assert.That(restored.Player.VisibleRootElement, Is.EqualTo(definition.visibleRootElement));
            Assert.That(restored.CaptureSave().player.knownSpells, Is.EqualTo(new[] { "already_known_spell" }));
            Assert.That(restored.CaptureSave().player.equippedSpells, Is.EqualTo(new[] { "already_known_spell" }));
            Assert.That(restored.CaptureSaveJson(), Is.EqualTo(json));
        }

        [Test]
        public void CurrentSchemaJsonWithoutVisibleRootFieldsKeepsUnknownRootAndExistingSpells()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            definition.visibleRootId = "root_water_middle";
            definition.visibleRootGrade = "中品";
            definition.visibleRootElement = "水";
            definition.availableSpells = new[] { "already_known_spell" };
            definition.equippedSpells = new[] { "already_known_spell" };
            definition.maxSpellSlots = 1;
            source.Player.Restore(CharacterRuntimeProfile.FromDefinition("player", definition).Capture());
            string legacyJson = System.Text.RegularExpressions.Regex.Replace(source.CaptureSaveJson(),
                "\"visibleRoot(?:Id|Grade|Element)\"\\s*:\\s*\"[^\"]*\"\\s*,", "");
            StringAssert.DoesNotContain("visibleRootId", legacyJson);
            StringAssert.DoesNotContain("visibleRootGrade", legacyJson);
            StringAssert.DoesNotContain("visibleRootElement", legacyJson);
            Assert.That(GameSaveSerializer.Deserialize(legacyJson).schemaVersion,
                Is.EqualTo(GameSaveSerializer.SchemaVersion));
            var restored = new GameRuntime();

            restored.RestoreSaveJson(legacyJson, catalog);

            Assert.That(restored.Player.VisibleRootId, Is.Null.Or.Empty);
            Assert.That(restored.Player.VisibleRootGrade, Is.Null.Or.Empty);
            Assert.That(restored.Player.VisibleRootElement, Is.Null.Or.Empty);
            Assert.That(restored.CaptureSave().player.knownSpells, Is.EqualTo(new[] { "already_known_spell" }));
            Assert.That(restored.CaptureSave().player.equippedSpells, Is.EqualTo(new[] { "already_known_spell" }));
        }

        [TestCase("id")]
        [TestCase("grade")]
        [TestCase("element")]
        public void PlayerPayloadWithOnlyVisibleRootFactFailsClosedWithoutChangingLiveOwners(string field)
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = new GameRuntime().CaptureSave();
            invalid.hasPlayer = false;
            invalid.player = new CharacterRecord();
            if (field == "id") invalid.player.visibleRootId = "root_water_middle";
            if (field == "grade") invalid.player.visibleRootGrade = "中品";
            if (field == "element") invalid.player.visibleRootElement = "水";

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [Test]
        public void LegacyPlayerSchemaIsRejectedRatherThanInventingAFoundationStage()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            GameSaveEnvelope legacy = source.CaptureSave();
            legacy.schemaVersion = GameSaveSerializer.LegacySchemaVersion;
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                GameSaveSerializer.Deserialize(JsonUtility.ToJson(legacy)));
            StringAssert.StartsWith(FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible + ":", exception.Message);
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
                invalid.cultivation = new CultivationRecord { hasFoundationPurpleMansionState = true };

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

        [TestCase("{\"schemaVersion\":0}")]
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
            definition.actionPointMaximumUnits = 100000;
            definition.movementEfficiencyMilliUnits = 2000;
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
        public void SchemaTwoEnvelopeIsRejectedAsAnIncompatibleFoundationSave()
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => GameSaveSerializer.Deserialize(
                "{\"schemaVersion\":2,\"hasPlayer\":true,\"player\":{\"characterId\":\"player\",\"displayName\":\"旧档\"}}"));
            StringAssert.StartsWith(FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible + ":", exception.Message);
        }

        [Test]
        public void SchemaOneEnvelopeIsRejectedAsAnIncompatibleFoundationSave()
        {
            InvalidDataException exception = Assert.Throws<InvalidDataException>(() => GameSaveSerializer.Deserialize(
                "{\"schemaVersion\":1,\"hasPlayer\":true,\"player\":{\"characterId\":\"player\",\"displayName\":\"旧档\"}}"));
            StringAssert.StartsWith(FoundationPurpleMansionRuntimeState.LegacyStageSchemaIncompatible + ":", exception.Message);
        }

        [Test]
        public void PlayerAndNpcFoundationStateRoundTripWithTheSameSchemaTwoPayload()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            source.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", definition),
                CultivationState.FromFoundationPurpleMansionSaveData(CreateCompleteFoundationSave("player_foundation")),
                "guanzhong_hub");
            GameSaveEnvelope save = source.CaptureSave();
            save.npcs = new[]
            {
                new NpcRecord
                {
                    npcId = "npc_foundation",
                    worldNodeId = "guanzhong_hub",
                    cultivationActionId = "",
                    cultivationState = CreateCompleteFoundationSave("npc_foundation"),
                },
            };

            var restored = new GameRuntime();
            restored.RestoreSave(save, catalog);
            GameSaveEnvelope roundTrip = restored.CaptureSave();

            Assert.That(roundTrip.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.That(roundTrip.cultivation.hasFoundationPurpleMansionState, Is.True);
            Assert.That(roundTrip.cultivation.foundationPurpleMansionState.foundationState.stageCode,
                Is.EqualTo((int)FoundationStage.Complete));
            Assert.That(roundTrip.npcs[0].cultivationState.schemaVersion, Is.EqualTo(2));
            Assert.That(roundTrip.npcs[0].cultivationState.foundationState.completionMansionCapacity, Is.EqualTo(1));
        }

        [Test]
        public void SchemaFourRoundTripKeepsLongTermActionEconomyInputsWithoutTemporaryLedgers()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            GameSaveEnvelope save = source.CaptureSave();
            Assert.That(save.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.That(save.player.actionEconomyInputsPresent, Is.True);
            Assert.That(save.player.actionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(save.player.movementEfficiencyMilliUnits, Is.EqualTo(2000));
            Assert.That(save.player.actionEconomyModifierInputs, Is.Empty);

            string json = source.CaptureSaveJson();
            StringAssert.DoesNotContain("actionPointsRemaining", json);
            StringAssert.DoesNotContain("pendingCtPenalty", json);
            StringAssert.DoesNotContain("actionPointLimitAtTurnStart", json);
            StringAssert.DoesNotContain("refundBudgetUsed", json);
            StringAssert.DoesNotContain("turnSequence", json);

            var restored = new GameRuntime();
            restored.RestoreSaveJson(json, catalog);
            GameSaveEnvelope roundTrip = restored.CaptureSave();

            Assert.That(roundTrip.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.That(roundTrip.player.actionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(roundTrip.player.movementEfficiencyMilliUnits, Is.EqualTo(2000));
            Assert.That(roundTrip.player.actionEconomyInputsPresent, Is.True);
            Assert.That(restored.Player.Attributes.ActionPointMaximumUnits, Is.EqualTo(100000));
            Assert.That(restored.Player.Attributes.MovementEfficiencyMilliUnits, Is.EqualTo(2000));
            Assert.That(restored.CaptureSaveJson(), Is.EqualTo(json));
        }

        [Test]
        public void SchemaFourKeepsALegalZeroMovementEfficiencyApartFromAMissingInput()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            definition.movementEfficiencyMilliUnits = 0;
            runtime.Player.Restore(CharacterRuntimeProfile.FromDefinition("player", definition).Capture());
            GameSaveEnvelope save = runtime.CaptureSave();

            Assert.That(save.player.movementEfficiencyMilliUnits, Is.EqualTo(0));
            Assert.That(save.player.actionEconomyInputsPresent, Is.True);

            var restored = new GameRuntime();
            restored.RestoreSaveJson(JsonUtility.ToJson(save), catalog);

            Assert.That(restored.Player.Attributes.MovementEfficiencyMilliUnits, Is.EqualTo(0));
            Assert.That(restored.Player.Attributes.ActionEconomyInputsPresent, Is.True);
        }

        [Test]
        public void SchemaFourCharacterPayloadWithMissingOrInvalidActionEconomyInputsFailsClosed()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();

            AssertRecordRejected(runtime, player => player.actionEconomyInputsPresent = false);
            AssertRecordRejected(runtime, player => player.actionPointMaximumUnits = 0);
            AssertRecordRejected(runtime, player => player.actionPointMaximumUnits = -100000);
            AssertRecordRejected(runtime, player => player.movementEfficiencyMilliUnits = -1);
            AssertRecordRejected(runtime, player => player.actionEconomyModifierInputs = new[]
            {
                new ActionEconomyModifierRecord
                {
                    sourceInstanceId = "gear_a",
                    sourceDefinitionId = "definition_a",
                    actionKind = 99,
                    retainedCostPermille = 0,
                    minimumNetCostUnits = 1,
                    refundOnEvent = (int)ActionEconomyModifierInput.SuccessEvent.None,
                },
            });
            AssertRecordRejected(runtime, player => player.actionEconomyModifierInputs = new[]
            {
                new ActionEconomyModifierRecord
                {
                    sourceInstanceId = "gear_a",
                    sourceDefinitionId = "definition_a",
                    actionKind = (int)ActionEconomyModifierInput.CommandKind.Guard,
                    retainedCostPermille = 0,
                    minimumNetCostUnits = 1,
                    refundOnEvent = (int)ActionEconomyModifierInput.SuccessEvent.RootActionCommitted,
                    refundUnits = 5000,
                    refundLimitPerOwnTurn = 0,
                    refundBudgetPerOwnTurnUnits = 0,
                },
            });
        }

        [Test]
        public void SchemaFourRejectsDuplicateActionEconomySourceInstances()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = runtime.CaptureSave();
            invalid.player.actionEconomyModifierInputs = new[]
            {
                CreateModifierRecord("gear_a"),
                CreateModifierRecord("gear_a"),
            };

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [Test]
        public void ActionEconomyProtocolFixtureRoundTripsWithoutPublishingARuntimeCharacter()
        {
            ActionEconomyModifierRecord record = CreateModifierRecord("gear_a");
            record.actionPointMaximumBonusUnits = 20000;
            record.movementEfficiencyBonusMilliUnits = -500;
            record.retainedCostPermille = 800;
            record.minimumNetCostUnits = 5000;

            ActionEconomyModifierRecord restored = JsonUtility.FromJson<ActionEconomyModifierRecord>(
                JsonUtility.ToJson(record));
            ActionEconomyModifierRecord roundTrip = ActionEconomyModifierRecord.Capture(restored.Restore());

            Assert.That(JsonUtility.ToJson(roundTrip), Is.EqualTo(JsonUtility.ToJson(record)));
        }

        [TestCase("unknown_source", null)]
        [TestCase("unknown_source", "unknown_profile")]
        [TestCase("definition_a", "unknown_profile")]
        [TestCase("basic_unarmed", "basic_unarmed")]
        public void SchemaFourRejectsUnapprovedReferencesBeforeReplacingAnyLiveOwner(string sourceId, string profileId)
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            string baseline = runtime.CaptureSaveJson();
            var player = runtime.Player;
            var inventoryGrants = runtime.InventoryGrants;
            var cultivation = runtime.Cultivation;
            var worldClock = runtime.WorldClock;
            var bounties = runtime.Bounties;
            var charters = runtime.Charters;
            GameSaveEnvelope invalid = runtime.CaptureSave();
            ActionEconomyModifierRecord record = CreateModifierRecord("gear_a");
            record.sourceDefinitionId = sourceId;
            record.profileId = profileId;
            invalid.player.actionEconomyModifierInputs = new[] { record };

            System.ArgumentException direct = Assert.Throws<System.ArgumentException>(() => invalid.player.Restore());
            StringAssert.StartsWith(GameSaveSerializer.ActionEconomySaveInvalid + ":", direct.Message);
            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
            System.ArgumentException json = Assert.Throws<System.ArgumentException>(() =>
                runtime.RestoreSaveJson(JsonUtility.ToJson(invalid), catalog));
            StringAssert.StartsWith(GameSaveSerializer.ActionEconomySaveInvalid + ":", json.Message);

            Assert.That(runtime.Player, Is.SameAs(player));
            Assert.That(runtime.InventoryGrants, Is.SameAs(inventoryGrants));
            Assert.That(runtime.Cultivation, Is.SameAs(cultivation));
            Assert.That(runtime.WorldClock, Is.SameAs(worldClock));
            Assert.That(runtime.Bounties, Is.SameAs(bounties));
            Assert.That(runtime.Charters, Is.SameAs(charters));
            Assert.That(runtime.CaptureSaveJson(), Is.EqualTo(baseline));
        }

        [Test]
        public void PlayerPayloadWithOnlyActionEconomyInputsFailsClosed()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            GameSaveEnvelope invalid = new GameRuntime().CaptureSave();
            invalid.hasPlayer = false;
            invalid.player = new CharacterRecord
            {
                actionEconomyInputsPresent = true,
                actionPointMaximumUnits = 100000,
            };

            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
        }

        [Test]
        public void SchemaThreePlayerPayloadIsRejectedForMissingActionEconomyInputs()
        {
            GameRuntime source = CreateRuntimeWithInventory();
            GameSaveEnvelope legacy = source.CaptureSave();
            legacy.schemaVersion = GameSaveSerializer.LegacyActionEconomySchemaVersion;

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                GameSaveSerializer.Deserialize(JsonUtility.ToJson(legacy)));
            StringAssert.StartsWith(GameSaveSerializer.LegacyActionEconomyInputMissing + ":", exception.Message);
        }

        [Test]
        public void SchemaThreeEmptySessionMigratesMechanicallyToTheCurrentSchema()
        {
            GameSaveEnvelope empty = new GameRuntime().CaptureSave();
            Assert.That(empty.hasPlayer, Is.False);
            empty.schemaVersion = GameSaveSerializer.LegacyActionEconomySchemaVersion;

            GameSaveEnvelope migrated = GameSaveSerializer.Deserialize(JsonUtility.ToJson(empty));
            Assert.That(migrated.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));

            var restored = new GameRuntime();
            restored.RestoreSave(migrated, null);

            Assert.That(restored.Player, Is.Null);
            Assert.That(restored.Cultivation, Is.Null);
            Assert.That(restored.CaptureSave().player, Is.Null);
        }

        [Test]
        public void SchemaThreePayloadHidingBehindAMissingPlayerFlagIsStillRejected()
        {
            GameRuntime runtime = CreateRuntimeWithInventory();
            string baseline = runtime.CaptureSaveJson();
            GameSaveEnvelope hidden = new GameRuntime().CaptureSave();
            hidden.hasPlayer = false;
            hidden.player = new CharacterRecord
            {
                characterId = "legacy",
                displayName = "旧档",
                actionEconomyInputsPresent = true,
                actionPointMaximumUnits = 100000,
                movementEfficiencyMilliUnits = 2000,
            };
            hidden.schemaVersion = GameSaveSerializer.LegacyActionEconomySchemaVersion;

            GameSaveEnvelope migrated = GameSaveSerializer.Deserialize(JsonUtility.ToJson(hidden));
            Assert.That(migrated.schemaVersion, Is.EqualTo(GameSaveSerializer.SchemaVersion));
            Assert.Throws<System.ArgumentException>(() => runtime.RestoreSave(migrated, catalog));

            Assert.That(runtime.CaptureSaveJson(), Is.EqualTo(baseline));
        }

        private static ActionEconomyModifierRecord CreateModifierRecord(string sourceInstanceId)
        {
            return new ActionEconomyModifierRecord
            {
                sourceInstanceId = sourceInstanceId,
                sourceDefinitionId = "definition_a",
                actionKind = (int)ActionEconomyModifierInput.CommandKind.Guard,
                retainedCostPermille = 0,
                minimumNetCostUnits = 1,
                refundOnEvent = (int)ActionEconomyModifierInput.SuccessEvent.None,
            };
        }

        private void AssertRecordRejected(GameRuntime runtime, System.Action<CharacterRecord> mutate)
        {
            GameSaveEnvelope invalid = runtime.CaptureSave();
            mutate(invalid.player);
            AssertRestoreFailsWithoutChangingRuntime(runtime, invalid);
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
            definition.actionPointMaximumUnits = 100000;
            definition.movementEfficiencyMilliUnits = 2000;
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

        private static FoundationPurpleMansionSaveData CreateCompleteFoundationSave(string foundationId)
        {
            return new FoundationPurpleMansionSaveData
            {
                schemaId = "foundationPurpleMansionState",
                schemaVersion = 2,
                characterId = foundationId,
                foundationState = new FoundationStateRecord
                {
                    foundationInstanceId = foundationId,
                    foundationDefinitionId = "foundation_definition",
                    sourceGongFaId = "gongfa_definition",
                    stageId = FoundationStage.Complete,
                    stageCode = (int)FoundationStage.Complete,
                    continuousProgress = 100f,
                    phaseBoundarySetId = "phase_boundaries",
                    naturalMansionCapacity = 1,
                    expansionGrants = new FoundationExpansionGrant[0],
                    expandedMansionCapacity = 0,
                    carryingCapacityProfileId = "carrying_profile",
                    currentMansionCarryingCapacity = 1,
                    completionMansionCapacity = 1,
                },
                mansionStates = new[]
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
                },
                effectBindings = new FoundationEffectBinding[0],
                guardianAbilities = new GuardianAbilityRecord[0],
                enhancementNodes = new EnhancementNodeRecord[0],
                jindanLock = new JindanLockRecord { status = JindanLockStatus.PreJindan },
            };
        }

        private static PurpleMansionStateRecord NotBuilt(PurpleMansionKind kind)
        {
            return new PurpleMansionStateRecord { mansionKind = kind, state = PurpleMansionBuildState.NotBuilt };
        }
    }
}
