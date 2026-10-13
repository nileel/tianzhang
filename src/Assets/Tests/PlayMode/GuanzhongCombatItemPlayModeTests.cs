using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.CombatPresentation;
using TianZhang.Features.Settlement;
using TianZhang.Features.WorldMap;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatItemPlayModeTests
    {
        private const string Item = "item_huoxue_dan", Slot = "formal-item";
        private string saveDirectory;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private CombatActionBarView bar;
        private CombatCommandInput input;
        private CombatActionPreviewView preview;
        private CombatRandom random;

        [UnitySetUp] public IEnumerator EnterFormalAdventure()
        {
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1920, 1080);
            yield return null;
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu); yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Item-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            var creation = Find<CharacterCreationController>(); creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit(Slot, "战斗用药验证"); yield return SceneReady(GameplaySceneNames.World);
            var qualifiedRuntime = GameBootstrap.RequireRuntime();
            Assert.AreEqual("练气", qualifiedRuntime.Player.Progression.RealmStage);
            Assert.AreEqual(3, qualifiedRuntime.GetItemQuantity(Item));
            var read = new GameSaveSlotStore(saveDirectory).Read(Slot);
            Assert.IsTrue(read.Succeeded); Assert.AreEqual(3, read.Envelope.inventory.Single(entry => entry.itemId == Item).quantity);
            Assert.AreEqual(3, GameBootstrap.RequireRuntime().GetItemQuantity(Item));
            var world = Find<WorldMapController>(); Assert.IsTrue(world.SelectNode("guanzhong_hub")); world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            var settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>(); random = new CombatRandom(); encounter.SetCombatResolutionRandomSource(random);
            bar = Find<CombatActionBarView>(); input = Find<CombatCommandInput>(); preview = Find<CombatActionPreviewView>();
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session"); spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            yield return Ready();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu); yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }
        private string State() => GameBootstrap.RequireRuntime().CaptureSaveJson() + ":" + GameBootstrap.RequireRuntime().InventoryVersion + ":" +
            session.StateVersion + ":" + session.TurnSequence + ":" + session.TurnScheduler.CurrentTick + ":" +
            spawned.Player.CurrentHealth + ":" + spawned.Player.CurrentSpirit + ":" + spawned.Player.ActionPointsRemainingUnits + ":" +
            spawned.Player.IsGuarding + ":" + session.PendingCtPenalty + ":" + random.Count;

        [UnityTest] public IEnumerator CancelAndInventoryChangeRejectWithoutAnyPartialCommit()
        {
            Assert.IsNotNull(bar.UseItemButton); Assert.IsTrue(bar.UseItemButton.interactable);
            int randomBefore = random.Count;
            string before = State(); bar.UseItemButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.UseItem, input.Candidate.Kind); Assert.AreEqual("player", input.Candidate.TargetId);
            Assert.AreEqual(30000, input.Preview.ActionPointCostUnits); Assert.AreEqual(3, input.Preview.ItemAvailableQuantity);
            Assert.AreEqual(Math.Min(80, spawned.Player.MaximumHealth - spawned.Player.CurrentHealth), input.Preview.ExpectedHealthRecovery);
            preview.CancelButton.onClick.Invoke(); Assert.IsNull(input.Candidate); Assert.AreEqual(before, State());
            bar.UseItemButton.onClick.Invoke();
            var runtime = GameBootstrap.RequireRuntime(); var catalog = Get<ContentCatalogData>(encounter, "equipmentCatalog");
            Assert.IsTrue(runtime.InventoryGrants.Grant(catalog, new[] { new InventoryGrantRequest("item_lingshi_low", 1) }).Applied);
            before = State(); preview.ConfirmButton.onClick.Invoke(); Assert.AreEqual(before, State());
            Assert.AreEqual(3, runtime.GetItemQuantity(Item)); Assert.AreEqual(randomBefore, random.Count);
            yield return null;
        }
        [UnityTest] public IEnumerator FormalUseSavesAfterVictory() { yield return RoundTrip(false); }
        [UnityTest] public IEnumerator FormalUseSavesAfterDefeat() { yield return RoundTrip(true); }
        [UnityTest] public IEnumerator InitialWriteFailureClearsTheGrantAndDoesNotActivateTheNewSlot()
        {
            SceneManager.LoadScene(GameplaySceneNames.StartMenu); yield return SceneReady(GameplaySceneNames.StartMenu);
            var bootstrap = GameBootstrap.RequireInstance(); string active = Get<string>(bootstrap, "activeSlotId");
            string blockedDirectory = Path.Combine(saveDirectory, "blocked-directory"); File.WriteAllText(blockedDirectory, "write failure fixture");
            Set(bootstrap, "slotStore", new GameSaveSlotStore(blockedDirectory));
            var creation = Find<CharacterCreationController>(); creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit("failed-new-slot", "初始存档失败验证"); yield return null;
            Assert.AreEqual(GameplaySceneNames.StartMenu, SceneManager.GetActiveScene().name);
            Assert.IsNull(GameBootstrap.RequireRuntime().Player); Assert.AreEqual(0, GameBootstrap.RequireRuntime().GetItemQuantity(Item));
            Assert.AreEqual(active, Get<string>(bootstrap, "activeSlotId"));
        }
        [UnityTest] public IEnumerator ExistingSchemaFourSaveWithoutMedicineLoadsWithoutAnotherGrant()
        {
            var store = new GameSaveSlotStore(saveDirectory); var read = store.Read(Slot); Assert.IsTrue(read.Succeeded);
            read.Envelope.inventory = read.Envelope.inventory.Where(entry => entry.itemId != Item).ToArray();
            Assert.IsTrue(store.Write(Slot, read.Envelope).Succeeded);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu); yield return SceneReady(GameplaySceneNames.StartMenu);
            Find<StartMenuController>().LoadPlayer(Slot); yield return SceneReady(GameplaySceneNames.World);
            Assert.AreEqual(0, GameBootstrap.RequireRuntime().GetItemQuantity(Item));
            Assert.AreEqual("练气", GameBootstrap.RequireRuntime().Player.Progression.RealmStage);
        }
        private IEnumerator RoundTrip(bool defeat)
        {
            var runtime = GameBootstrap.RequireRuntime();
            // Explicit injury fixture on the real session; production acquisition and input remain unchanged.
            spawned.Player.ReceiveDamage(Math.Min(100, spawned.Player.CurrentHealth - 1));
            spawned.Player.IsGuarding = true;
            int health = spawned.Player.CurrentHealth, expected = Math.Min(80, spawned.Player.MaximumHealth - health);
            int spirit = spawned.Player.CurrentSpirit, tick = session.TurnScheduler.CurrentTick;
            long version = runtime.InventoryVersion, turn = session.TurnSequence;
            int randomBefore = random.Count;
            bar.UseItemButton.onClick.Invoke(); Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            Assert.AreEqual(expected, input.Preview.ExpectedHealthRecovery);
            var old = input.Candidate; preview.ConfirmButton.onClick.Invoke(); yield return Ready();
            Assert.AreEqual(health + expected, spawned.Player.CurrentHealth); Assert.AreEqual(70000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(2, runtime.GetItemQuantity(Item)); Assert.AreEqual(version + 1, runtime.InventoryVersion);
            Assert.AreEqual(spirit, spawned.Player.CurrentSpirit); Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(turn, session.TurnSequence); Assert.AreEqual(randomBefore, random.Count); Assert.IsFalse(spawned.Player.IsGuarding);
            string before = State(); encounter.ConfirmAction(old); Assert.AreEqual(before, State());
            foreach (int width in new[] { 1920, 1366 })
            {
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                AssertButtonBounds();
                string evidence = Path.Combine(Application.dataPath, "../Logs/weapon-item-ui");
                Directory.CreateDirectory(evidence);
                ScreenCapture.CaptureScreenshot(Path.Combine(evidence, "item-" + width + (defeat ? "-defeat" : "-victory") + ".png"));
                for (int frame = 0; frame < 3; frame++) yield return null;
            }
            GuanzhongBasicAttackPlayModeTests.ArmEnemyOutcomeFixture(spawned.Enemy);
            if (defeat) spawned.Player.ReceiveDamage(spawned.Player.CurrentHealth - 1);
            for (float deadline = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;)
            {
                if (!defeat) encounter.RequestBasicAttack("player", "enemy");
                encounter.RequestEndTurn("player"); yield return null;
            }
            Assert.AreEqual(GameplaySceneNames.Settlement, SceneManager.GetActiveScene().name);
            Assert.AreEqual(2, runtime.GetItemQuantity(Item));
            // Scene return itself has not saved: disk still carries the original grant.
            Assert.AreEqual(3, new GameSaveSlotStore(saveDirectory).Read(Slot).Envelope.inventory.Single(entry => entry.itemId == Item).quantity);
            string json = runtime.CaptureSaveJson();
            Find<SettlementController>().SaveAndReturnToMenu(); yield return SceneReady(GameplaySceneNames.StartMenu);
            Find<StartMenuController>().LoadPlayer(Slot); yield return SceneReady(GameplaySceneNames.Settlement);
            runtime = GameBootstrap.RequireRuntime(); Assert.AreEqual(json, runtime.CaptureSaveJson()); Assert.AreEqual(2, runtime.GetItemQuantity(Item));
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            Assert.AreEqual(2, runtime.GetItemQuantity(Item));
        }
        private void AssertButtonBounds()
        {
            Canvas.ForceUpdateCanvases();
            var item = bar.UseItemButton.GetComponent<RectTransform>();
            var equipment = bar.SwapEquipmentButton.GetComponent<RectTransform>();
            var status = Get<Text>(bar, "actionStatusLabel").rectTransform;
            Assert.AreSame(equipment.parent, item.parent); Assert.AreSame(item.parent, status.parent);
            Assert.That(item.anchoredPosition.x, Is.GreaterThan(equipment.anchoredPosition.x + equipment.rect.width));
            Assert.That(status.anchoredPosition.x, Is.GreaterThan(item.anchoredPosition.x + item.rect.width));
            var label = bar.UseItemButton.GetComponentInChildren<Text>();
            var canvas = item.GetComponentInParent<Canvas>();
            var corners = new Vector3[4]; item.GetWorldCorners(corners);
            Debug.Log("[ItemUI] source=" + item.name + "; parent=" + item.parent.name + "; canvas=" + canvas.name +
                "; mode=" + canvas.renderMode + "; scale=" + canvas.scaleFactor + "; camera=" + canvas.worldCamera +
                "; local=" + item.anchoredPosition + "/" + item.rect.size + "; drawnMin=" + corners[0] + "; drawnMax=" + corners[2] +
                "; label=" + label.rectTransform.rect.size + "; font=" + label.font.name + "/" + label.fontSize +
                "; preferred=" + label.preferredWidth + "/" + label.preferredHeight + "; writer=CombatActionBarView.PresentAction");
            Assert.That(label.preferredWidth, Is.LessThanOrEqualTo(label.rectTransform.rect.width));
            Assert.That(label.preferredHeight, Is.LessThanOrEqualTo(label.rectTransform.rect.height));
            Debug.Log("[ItemUI] parent=" + item.parent.name + "; item=" + item.anchoredPosition + "/" + item.rect.size +
                "; equipment=" + equipment.anchoredPosition + "/" + equipment.rect.size + "; status=" + status.anchoredPosition + "/" + status.rect.size);
        }
        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);) yield return null;
            yield return null; Assert.AreEqual("player", session.CurrentActorId); Assert.IsFalse(encounter.IsPresenting);
        }
        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name); yield return null;
        }
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        private sealed class CombatRandom : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() => Count++ % 4 == 0 ? 0f : 99f;
        }
    }
}
