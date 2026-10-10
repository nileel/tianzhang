using System;
using System.Linq;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.Persistence;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using TianZhang.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatActionPointEquipmentTests
    {
        private static TianZhang.Content.CombatRuleSetData GrowthRules
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
#else
                throw new System.NotSupportedException("Editor fixture requires the production rule asset.");
#endif
            }
        }

        private ContentCatalogData catalog;
        private CharacterData definition;
        private GameRuntime runtime;
        private EncounterCoordinator bridge;
        private CombatSession session;
        private CombatantSnapshot actor;
        private readonly CombatCommandService service = new CombatCommandService();
        private const string Sword = "item_iron_sword", Blade = "item_iron_blade";

        [SetUp] public void SetUp()
        {
            catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.realmStage = "凡人";
            definition.charName = "装备验证"; definition.realmStage = "练气"; definition.realmMultiplier = 1;
            definition.actionPointMaximumUnits = 100000; definition.movementEfficiencyMilliUnits = 2000;
            definition.unarmedBasicAttackProfileId = "basic_unarmed";
            runtime = new GameRuntime();
            runtime.BeginEquippedNewGame(CharacterRuntimeProfile.FromDefinition("player", definition, GrowthRules), CultivationState.CreateEmpty(), "guanzhong_hub", catalog);
            bridge = new GameObject("EquipmentTestOwner").AddComponent<EncounterCoordinator>();
            bridge.ConfigureEquipment(runtime.Player, catalog, runtime.OwnsEquipmentInstance, () => runtime.InventoryVersion);
            actor = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 100, 10000, 10000, 60, 0, 10, 10, runtime.Player.Attributes.ActionPointMaximumUnits, runtime.Player.Attributes.MovementEfficiencyMilliUnits,
                basicAttackProfileId: "basic_iron_sword");
            var enemy = new CombatantSnapshot("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 10000, 10000, 60, 0, 10, 10, 100000, 2000,
                basicAttackProfileId: string.Empty);
            var spawn = new AdventureSpawnSet(actor, enemy, null, "basic_iron_sword", string.Empty, Array.Empty<string>());
            var profiles = new[] { "basic_iron_sword", "basic_iron_blade", "spell_guanshiji", "spell_julingshu" }
                .Select(id => AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_" + id + ".asset")).ToArray();
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var ordinary = CombatActionPointKernelTests.Ordinary().Concat(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.SwapEquipment, 25000, 25000, 0, 0) });
            Assert.IsTrue(new CombatEntryAdapter(ordinary, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")).TryCreateSession(spawn, profiles, environment, out session, out var reason), reason);
            session.ExternalResources = bridge;
            typeof(EncounterCoordinator).GetField("unitPresentation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridge, new NoPresentation());
            typeof(EncounterCoordinator).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bridge, session);
            Assert.AreEqual("player", service.AdvanceUntilAction(session).ActorId);
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(bridge.gameObject); Object.DestroyImmediate(definition); }
        private void Spend(int units) => typeof(CombatantSnapshot).GetMethod("SpendActionPoints", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor, new object[] { units });
        private sealed class NoPresentation : ICombatUnitPresentationPort
        {
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) { }
            public void Spawn(CombatUnitPresentationDescriptor value) { }
            public void Present(CombatUnitPresentationEventProjection value) { }
            public void Remove(string id) { }
            public void Clear() { }
        }
        private CombatCommand Swap(string id = Blade, string slot = "main_hand", string instance = null) => new CombatCommand(
            CombatCommandKind.SwapEquipment, "player", profileId: id == Sword ? "basic_iron_sword" : "basic_iron_blade",
            equipmentSlotId: slot, itemId: id, instanceId: instance ?? "player:initial:" + id);
        private CombatActionResult Execute(CombatCommand command, CombatCommandPreview quote = null) =>
            (CombatActionResult)typeof(EncounterCoordinator).GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bridge, new object[] { command, quote });
        private string State() => runtime.CaptureSaveJson() + ":" + runtime.InventoryVersion + ":" + session.StateVersion + ":" +
            session.TurnSequence + ":" + session.TurnScheduler.CurrentTick + ":" + session.PendingCtPenalty + ":" + actor.ActionPointsRemainingUnits + ":" +
            actor.IsGuarding + ":" + actor.BasicAttackProfileId + ":" + actor.CombatSwapsUsed + ":" + string.Join(";", actor.Cooldowns);

        [Test] public void SwordBladeSwordCommitsOnceAndPreservesTurnAndAbilityLedgers()
        {
            Assert.AreEqual(Sword, runtime.Player.MainEquipmentItemId);
            actor.SetCooldown("spell_guanshiji", 41); actor.IsGuarding = true;
            long turn = session.TurnSequence, inventory = runtime.InventoryVersion;
            var quote = service.Preview(session, Swap());
            Assert.IsTrue(quote.IsLegal); Assert.AreEqual(25000, quote.ActionPointCostUnits);
            Assert.IsTrue(Execute(Swap(), quote).Succeeded);
            Assert.AreEqual(75000, actor.ActionPointsRemainingUnits); Assert.IsFalse(actor.IsGuarding);
            Assert.AreEqual(Blade, runtime.Player.MainEquipmentItemId); Assert.AreEqual("basic_iron_blade", actor.BasicAttackProfileId);
            string state = State(); Assert.IsFalse(Execute(Swap(), quote).Succeeded); Assert.AreEqual(state, State());
            Assert.IsTrue(Execute(Swap(Sword)).Succeeded); Assert.AreEqual(50000, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(Sword, runtime.Player.MainEquipmentItemId); Assert.AreEqual("basic_iron_sword", actor.BasicAttackProfileId);
            Assert.AreEqual(41, actor.GetCooldown("spell_guanshiji")); Assert.AreEqual(0, actor.CombatSwapsUsed);
            Assert.AreEqual(100000, actor.ActionPointLimitAtTurnStartUnits); Assert.AreEqual(turn, session.TurnSequence);
            Assert.AreEqual(inventory, runtime.InventoryVersion); Assert.IsFalse(service.Preview(session, new CombatCommand(CombatCommandKind.Wait, "player")).IsLegal);
            Assert.IsTrue(runtime.OwnsEquipmentInstance("player", Sword, "player:initial:" + Sword));
            Assert.IsTrue(runtime.OwnsEquipmentInstance("player", Blade, "player:initial:" + Blade));
        }

        [TestCase(Sword, "main_hand", null)]
        [TestCase(Blade, "other", null)]
        [TestCase(Blade, "main_hand", "other:initial:item_iron_blade")]
        [TestCase("unknown", "main_hand", null)]
        public void InvalidRequestsArePure(string id, string slot, string instance)
        {
            actor.IsGuarding = true; string before = State();
            Assert.IsFalse(Execute(Swap(id, slot, instance)).Succeeded); Assert.AreEqual(before, State());
        }
        [TestCase("凡人")][TestCase("unknown")]
        public void InvalidRealmIsPure(string realm)
        {
            if (realm == "unknown")
            {
                string unchanged = State();
                Assert.Throws<ArgumentException>(() => runtime.Player.RestoreProgression(
                    new CharacterProgressionSnapshot("", realm, 1)));
                Assert.AreEqual(unchanged, State());
                return;
            }
            runtime.Player.RestoreProgression(new CharacterProgressionSnapshot("", realm, 1));
            string before = State(); Assert.IsFalse(Execute(Swap()).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void InsufficientApAndChangedInventoryRejectWithoutPublishingEitherCandidate()
        {
            Spend(75001); actor.IsGuarding = true;
            string before = State(); Assert.IsFalse(Execute(Swap()).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void ChangedLongTermLoadoutInvalidatesTheOriginalQuote()
        {
            var quote = service.Preview(session, Swap());
            Assert.IsTrue(runtime.Player.TryPrepareEquipmentChange(catalog, "main_hand", Blade, "player:initial:" + Blade,
                true, out Action commit, out _)); commit();
            string before = State(); Assert.IsFalse(Execute(Swap(), quote).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void InventoryVersionAndApInvalidateTheOriginalQuote()
        {
            var quote = service.Preview(session, Swap());
            Assert.IsTrue(runtime.InventoryGrants.Grant(catalog, new[] { new InventoryGrantRequest("item_lingshi_low", 1) }).Applied);
            string before = State(); Assert.IsFalse(Execute(Swap(), quote).Succeeded); Assert.AreEqual(before, State());
            quote = service.Preview(session, Swap()); Spend(1);
            before = State(); Assert.IsFalse(Execute(Swap(), quote).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void PreviewDoesNotChangeStateAndNextAttackUsesTheNewProfile()
        {
            string before = State(); service.Preview(session, Swap()); Assert.AreEqual(before, State());
            Assert.IsTrue(Execute(Swap()).Succeeded);
            var attack = new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", actor.BasicAttackProfileId,
                new CombatResolutionRolls(0, 100, 100, 100));
            Assert.AreEqual(50000, service.Preview(session, attack).ActionPointCostUnits);
            Assert.IsTrue(service.Execute(session, attack).Succeeded); Assert.AreEqual(25000, actor.ActionPointsRemainingUnits);
            Assert.IsTrue(Execute(Swap(Sword)).Succeeded); Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            Assert.AreEqual(Sword, runtime.Player.MainEquipmentItemId);
        }
        [TestCase(Sword, 100000, 2, 20000)]
        [TestCase(Blade, 100000, 2, 0)]
        [TestCase(Sword, 80000, 2, 0)]
        [TestCase(Blade, 80000, 1, 30000)]
        public void WeaponCostsControlTheNumberOfSuccesses(string weapon, int ap, int count, int remaining)
        {
            // Set the pre-turn loadout as a fixture; no in-turn swap is omitted from the charged scenario.
            if (weapon == Blade)
            {
                Assert.IsTrue(runtime.Player.TryPrepareEquipmentChange(catalog, "main_hand", weapon, "player:initial:" + weapon,
                    true, out Action equip, out _)); equip(); actor.BasicAttackProfileId = "basic_iron_blade";
            }
            if (ap < 100000) Spend(100000 - ap);
            var command = new CombatCommand(CombatCommandKind.BasicAttack, "player", "enemy", actor.BasicAttackProfileId,
                new CombatResolutionRolls(0, 100, 100, 100));
            for (int i = 0; i < count; i++) Assert.IsTrue(service.Execute(session, command).Succeeded);
            Assert.AreEqual(remaining, actor.ActionPointsRemainingUnits);
            Assert.IsFalse(service.Execute(session, command).Succeeded);
        }
        [TestCase(Sword)][TestCase(Blade)]
        public void SaveRoundTripPreservesExactEquipmentAndInventory(string item)
        {
            if (item == Blade) Assert.IsTrue(Execute(Swap()).Succeeded);
            string json = runtime.CaptureSaveJson();
            var loaded = new GameRuntime(); loaded.RestoreSaveJson(json, catalog);
            Assert.AreEqual(json, loaded.CaptureSaveJson()); Assert.AreEqual(item, loaded.Player.MainEquipmentItemId);
            Assert.IsTrue(loaded.OwnsEquipmentInstance("player", item, "player:initial:" + item));
            Assert.IsFalse(runtime.InventoryGrants.Grant(catalog, new[] { new InventoryGrantRequest(item, 1) }).Applied);
        }
        [TestCase("item")][TestCase("instance")][TestCase("profile")][TestCase("inventory")][TestCase("hidden")]
        public void BadSaveDoesNotReplaceLiveOwners(string kind)
        {
            var envelope = runtime.CaptureSave(); var owner = runtime.Player; string before = runtime.CaptureSaveJson();
            switch (kind)
            {
                case "item": envelope.player.mainEquipmentItemId = "unknown"; break;
                case "instance": envelope.player.mainEquipmentInstanceId = "other:initial:" + Sword; break;
                case "profile": envelope.player.mainEquipmentBasicAttackProfileId = "basic_iron_blade"; break;
                case "inventory": envelope.inventory = envelope.inventory.Where(value => value.itemId != Sword).ToArray(); break;
                case "hidden": envelope.hasPlayer = false; break;
            }
            StringAssert.Contains("equipment_save_invalid", Assert.Throws<ArgumentException>(() => runtime.RestoreSave(envelope, catalog)).Message);
            Assert.AreSame(owner, runtime.Player); Assert.AreEqual(before, runtime.CaptureSaveJson());
        }
        [Test] public void LegacyUnarmedPlayerIsNotGrantedWeaponsAndBadInitialCatalogDoesNotPublish()
        {
            var unarmed = new GameRuntime(); unarmed.BeginNewGame(CharacterRuntimeProfile.FromDefinition("player", definition, GrowthRules), CultivationState.CreateEmpty(), "guanzhong_hub");
            string json = unarmed.CaptureSaveJson(); unarmed.RestoreSaveJson(json, catalog); Assert.AreEqual(json, unarmed.CaptureSaveJson());
            Assert.IsFalse(unarmed.OwnsEquipmentInstance("player", Sword, "player:initial:" + Sword));
            var empty = ScriptableObject.CreateInstance<ContentCatalogData>();
            empty.SetCombatRuleSet(GrowthRules);
            try { Assert.Throws<InvalidOperationException>(() => unarmed.BeginEquippedNewGame(CharacterRuntimeProfile.FromDefinition("player", definition, GrowthRules), CultivationState.CreateEmpty(), "guanzhong_hub", empty)); Assert.AreEqual(json, unarmed.CaptureSaveJson()); }
            finally { Object.DestroyImmediate(empty); }
        }
        [Test] public void InventoryProofAndVersionRejectMissingForeignOrDuplicateInstances()
        {
            var store = new InventoryStore(); Assert.AreEqual(0, store.Version);
            Assert.IsFalse(store.OwnsEquipmentInstance("player", Sword, "player:initial:" + Sword));
            store.Restore(new InventoryStoreSnapshot(new[] { new InventoryEntry(Sword, 1) })); Assert.AreEqual(1, store.Version);
            Assert.IsTrue(store.OwnsEquipmentInstance("player", Sword, "player:initial:" + Sword));
            Assert.IsFalse(store.OwnsEquipmentInstance("other", Sword, "player:initial:" + Sword));
            store.Restore(new InventoryStoreSnapshot(new[] { new InventoryEntry(Sword, 2) })); Assert.AreEqual(2, store.Version);
            Assert.IsFalse(store.OwnsEquipmentInstance("player", Sword, "player:initial:" + Sword));
        }
    }
}
