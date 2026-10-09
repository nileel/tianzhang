using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using TianZhang.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class CombatActionPointItemTests
    {
        private const string Item = "item_huoxue_dan";
        private ContentCatalogData catalog;
        private ItemData medicine;
        private CharacterData definition;
        private GameRuntime runtime;
        private InventoryStore inventory;
        private EncounterCoordinator bridge;
        private CombatSession session;
        private CombatantSnapshot actor;
        private readonly CombatCommandService service = new CombatCommandService();

        [SetUp] public void SetUp()
        {
            catalog = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset"));
            Assert.IsTrue(catalog.TryGetCombatItem(Item, out var source));
            medicine = Object.Instantiate(source);
            Set(catalog, "items", Get<ItemData[]>(catalog, "items").Select(item => item.itemId == Item ? medicine : item).ToArray());
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "道具验证"; definition.realmStage = "练气"; definition.realmMultiplier = 1;
            definition.actionPointMaximumUnits = 100000; definition.movementEfficiencyMilliUnits = 2000;
            definition.unarmedBasicAttackProfileId = "basic_unarmed";
            runtime = new GameRuntime();
            runtime.BeginEquippedNewGame(CharacterRuntimeProfile.FromDefinition("player", definition), CultivationState.CreateEmpty(), "guanzhong_hub", catalog);
            inventory = Get<InventoryStore>(runtime, "inventory");
            bridge = new GameObject("ItemTestOwner").AddComponent<EncounterCoordinator>();
            bridge.ConfigureEquipment(runtime.Player, catalog, runtime.OwnsEquipmentInstance, () => runtime.InventoryVersion);
            bridge.ConfigureItems(runtime.GetItemQuantity, (string id, int count, long version, out Action apply, out string reason) =>
                runtime.TryPrepareCombatConsumption(catalog, id, count, version, out apply, out reason));
            bridge.SetCombatResolutionRandomSource(new NoRandom());
            actor = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0, 0), 100, 200, 200, 60, 10, 10, 10, 100000, 2000,
                basicAttackProfileId: "basic_iron_sword");
            var enemy = new CombatantSnapshot("enemy", CombatTeam.Enemy, new HexCoord(1, 0), 1, 10000, 10000, 60, 0, 10, 10, 100000, 2000,
                basicAttackProfileId: "basic_unarmed");
            var spawn = new AdventureSpawnSet(actor, enemy, null, "basic_iron_sword", "basic_unarmed", Array.Empty<string>());
            var profiles = new[] { "basic_unarmed", "basic_iron_sword", "basic_iron_blade", "spell_guanshiji", "spell_julingshu" }
                .Select(id => AssetDatabase.LoadAssetAtPath<AttackProfileData>("Assets/Data/AttackProfiles/AttackProfile_" + id + ".asset")).ToArray();
            var ordinary = CombatActionPointKernelTests.Ordinary().Concat(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.UseItem, 30000, 30000, 0, 0) });
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            Assert.IsTrue(new CombatEntryAdapter(ordinary).TryCreateSession(spawn, profiles, environment, out session, out var reason), reason);
            session.ExternalResources = bridge; Set(bridge, "session", session); Set(bridge, "unitPresentation", new NoPresentation());
            Assert.AreEqual("player", service.AdvanceUntilAction(session).ActorId);
        }
        [TearDown] public void TearDown()
        { Object.DestroyImmediate(bridge.gameObject); Object.DestroyImmediate(definition); Object.DestroyImmediate(medicine); Object.DestroyImmediate(catalog); }
        private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private CombatCommand Use(string id = Item, string target = "player", int count = 1, long context = 0) =>
            new CombatCommand(CombatCommandKind.UseItem, "player", target, contextVersion: context, itemId: id, itemQuantityCost: count);
        private CombatActionResult Execute(CombatCommand command, CombatCommandPreview quote = null) =>
            (CombatActionResult)typeof(EncounterCoordinator).GetMethod("ExecuteCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(bridge, new object[] { command, quote });
        private void Spend(int units) => typeof(CombatantSnapshot).GetMethod("SpendActionPoints", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(actor, new object[] { units });
        private string State() => runtime.CaptureSaveJson() + ":" + inventory.Version + ":" + session.StateVersion + ":" +
            session.TurnSequence + ":" + session.TurnScheduler.CurrentTick + ":" + session.PendingCtPenalty + ":" +
            session.CurrentActorId + ":" + session.TurnPhase + ":" + session.HasCommittedBenefit + ":" + actor.CurrentHealth + ":" +
            actor.MaximumHealth + ":" + actor.CurrentSpirit + ":" + actor.ActionPointsRemainingUnits + ":" + actor.IsGuarding + ":" +
            actor.Position + ":" + actor.CombatSwapsUsed + ":" + string.Join(";", actor.Cooldowns);

        [TestCase(50, 130)] [TestCase(180, 200)] [TestCase(200, 200)]
        public void SuccessfulUseIsAtomicIncludingFullHealth(int health, int expected)
        {
            actor.ReceiveDamage(200 - health); actor.IsGuarding = true; actor.SetCooldown("spell_guanshiji", 41);
            long version = inventory.Version, state = session.StateVersion, turn = session.TurnSequence;
            int tick = session.TurnScheduler.CurrentTick;
            int spirit = actor.CurrentSpirit;
            string before = State(); var quote = service.Preview(session, Use()); Assert.AreEqual(before, State());
            Assert.IsTrue(quote.IsLegal); Assert.AreEqual(30000, quote.ActionPointCostUnits);
            Assert.AreEqual(80, quote.ItemHealthRecovery); Assert.AreEqual(expected - health, quote.ExpectedHealthRecovery);
            Assert.AreEqual(1, quote.ItemQuantityCost); Assert.AreEqual(3, quote.ItemAvailableQuantity);
            Assert.IsTrue(Execute(Use(), quote).Succeeded);
            Assert.AreEqual(expected, actor.CurrentHealth); Assert.AreEqual(200, actor.MaximumHealth);
            Assert.AreEqual(70000, actor.ActionPointsRemainingUnits); Assert.AreEqual(2, runtime.GetItemQuantity(Item));
            Assert.AreEqual(version + 1, inventory.Version); Assert.AreEqual(state + 1, session.StateVersion);
            Assert.AreEqual(turn, session.TurnSequence); Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(spirit, actor.CurrentSpirit); Assert.AreEqual(41, actor.GetCooldown("spell_guanshiji"));
            Assert.AreEqual(0, session.PendingCtPenalty); Assert.IsFalse(actor.IsGuarding);
            Assert.IsFalse(service.Preview(session, new CombatCommand(CombatCommandKind.Wait, "player")).IsLegal);
            before = State(); Assert.IsFalse(Execute(Use(), quote).Succeeded); Assert.AreEqual(before, State());
        }
        [TestCase("unknown", "player", 1)] [TestCase("item_lingshi_low", "player", 1)]
        [TestCase("item_iron_sword", "player", 1)] [TestCase(Item, "enemy", 1)] [TestCase(Item, "", 1)]
        [TestCase(Item, "player", 0)] [TestCase(Item, "player", 2)]
        public void InvalidRequestsArePure(string id, string target, int count)
        { actor.IsGuarding = true; string before = State(); Assert.IsFalse(Execute(Use(id, target, count)).Succeeded); Assert.AreEqual(before, State()); }
        [TestCase("凡人")] [TestCase("筑基")] [TestCase("unknown")]
        public void RealmMustBeExactlyLianqi(string realm)
        { runtime.Player.Progression.Restore(new CharacterProgressionSnapshot("", realm, 1)); string before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State()); }
        [Test] public void DeathStunOtherTurnAndEndedSessionArePure()
        {
            actor.ApplyStatus(CombatStatusKind.Stun, 1); string before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
            actor.ReceiveDamage(actor.CurrentHealth); before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void WrongTurnAndMissingDefinitionArePure()
        {
            Assert.IsTrue(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, "player")).Succeeded);
            string before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
        }
        [TestCase(29999, false)] [TestCase(30000, true)]
        public void ExactApBoundary(int remaining, bool success)
        {
            Spend(100000 - remaining); actor.IsGuarding = true; string before = State();
            Assert.AreEqual(success, Execute(Use()).Succeeded);
            if (!success) Assert.AreEqual(before, State());
            else { Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase); Assert.AreEqual(0, actor.ActionPointsRemainingUnits); Assert.AreEqual(2, runtime.GetItemQuantity(Item)); }
        }
        [Test] public void ThreeFreshQuotesConsumeLastEntryAndEmptyInventoryRoundTrips()
        {
            for (int i = 0; i < 3; i++) Assert.IsTrue(Execute(Use(), service.Preview(session, Use())).Succeeded);
            Assert.AreEqual(10000, actor.ActionPointsRemainingUnits); Assert.AreEqual(0, runtime.GetItemQuantity(Item));
            Assert.IsFalse(inventory.Capture().Entries.Any(entry => entry.ItemId == Item));
            Assert.AreEqual(30000, service.Preview(session, Use()).ActionPointCostUnits);
            string before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
            var loaded = new GameRuntime(); loaded.RestoreSaveJson(runtime.CaptureSaveJson(), catalog);
            Assert.AreEqual(0, loaded.GetItemQuantity(Item));
        }
        [TestCase("inventory")] [TestCase("health")] [TestCase("realm")] [TestCase("definition")] [TestCase("ap")] [TestCase("context")]
        public void ChangedFactsRejectTheOldQuote(string change)
        {
            var quote = service.Preview(session, Use());
            switch (change)
            {
                case "inventory": Assert.IsTrue(runtime.InventoryGrants.Grant(catalog, new[] { new InventoryGrantRequest("item_lingshi_low", 1) }).Applied); break;
                case "health": actor.ReceiveDamage(1); break;
                case "realm": runtime.Player.Progression.Restore(new CharacterProgressionSnapshot("", "筑基", 1)); break;
                case "definition": medicine.combatUseHealthRecovery++; break;
                case "ap": Spend(1); break;
            }
            string before = State(); Assert.IsFalse(Execute(Use(context: change == "context" ? 1 : 0), quote).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void InventoryOverflowOrInvalidWholeInventoryDoesNotCommitCombat()
        {
            Set(inventory, "<Version>k__BackingField", long.MaxValue);
            string before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
            Set(inventory, "<Version>k__BackingField", 0L);
            inventory.Restore(new InventoryStoreSnapshot(new[] { new InventoryEntry(Item, 3), new InventoryEntry("item_lingshi_low", 100) }));
            before = State(); Assert.IsFalse(Execute(Use()).Succeeded); Assert.AreEqual(before, State());
        }
        [Test] public void SaveValidationAndInitialGrantKeepExistingOwnersAtomic()
        {
            Assert.AreEqual(3, runtime.GetItemQuantity(Item));
            var loaded = new GameRuntime(); loaded.RestoreSaveJson(runtime.CaptureSaveJson(), catalog); Assert.AreEqual(3, loaded.GetItemQuantity(Item));
            var legacy = runtime.CaptureSave(); legacy.inventory = legacy.inventory.Where(entry => entry.itemId != Item).ToArray();
            loaded.RestoreSave(legacy, catalog); Assert.AreEqual(0, loaded.GetItemQuantity(Item));
            foreach (int quantity in new[] { 0, -1, 100 })
            {
                var bad = runtime.CaptureSave(); bad.inventory.Single(entry => entry.itemId == Item).quantity = quantity;
                string before = State(); Assert.Throws<ArgumentException>(() => runtime.RestoreSave(bad, catalog)); Assert.AreEqual(before, State());
            }
            medicine.combatUseEffectId = "unknown"; string state = State();
            Assert.Throws<InvalidOperationException>(() => runtime.BeginEquippedNewGame(CharacterRuntimeProfile.FromDefinition("player", definition), CultivationState.CreateEmpty(), "guanzhong_hub", catalog));
            Assert.AreEqual(state, State());
        }
        [TestCase("combatUseHealthRecovery", "0")] [TestCase("combatUseHealthRecovery", "2147483648")]
        [TestCase("initialGrantQuantity", "100")] [TestCase("combatUseEffectId", "unknown")]
        [TestCase("combatUseRealmId", "realm_zhuji")] [TestCase("combatUseTargetMode", "other")]
        public void InvalidMedicineCsvFailsBeforeWritingAssets(string field, string value)
        {
            string[] Read(string name) => File.ReadAllLines(Path.Combine(Application.dataPath, "DataConfig", name + ".csv"));
            string[] rows = Read("Items"); int header = Array.FindIndex(rows, row => row.StartsWith("itemId,"));
            int column = Array.IndexOf(rows[header].Split(','), field), index = Array.FindIndex(rows, row => row.StartsWith(Item + ","));
            string[] cells = rows[index].Split(','); cells[column] = value; rows[index] = string.Join(",", cells);
            Assert.Throws<InvalidDataException>(() => SettlementContentImporter.ParseContentCatalog(Read("Language"), Read("Settlements"), rows, Read("Bounties"), Read("Enemies")));
        }
        private sealed class NoRandom : ICombatResolutionRandomSource { public float NextPercent() => throw new InvalidOperationException("Item must not sample rolls."); }
        private sealed class NoPresentation : ICombatUnitPresentationPort
        {
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) { }
            public void Spawn(CombatUnitPresentationDescriptor value) { }
            public void Present(CombatUnitPresentationEventProjection value) { }
            public void Remove(string id) { }
            public void Clear() { }
        }
    }
}
