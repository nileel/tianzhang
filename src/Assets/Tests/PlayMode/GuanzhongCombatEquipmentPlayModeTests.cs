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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatEquipmentPlayModeTests
    {
        private const string Attack = "spell_guanshiji", Recovery = "spell_julingshu";
        private string saveDirectory;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;
        private CombatHudPresenter hud;
        private CombatActionBarView bar;
        private CombatCommandInput input;
        private CombatActionPreviewView preview;
        private CombatRandom random;

        [UnitySetUp]
        public IEnumerator EnterFormalAdventure()
        {
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Equipment-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit("formal-swap", "战斗换装验证");
            yield return SceneReady(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            SettlementController settlement = Find<SettlementController>();
            Find<BountyBoardView>().Show(Get<ContentCatalogData>(settlement, "contentCatalog"),
                "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            Find<BountyBoardView>().SubmitAccept("bounty_guanzhong_shijiahou");
        }

        private IEnumerator BeginEncounter(bool reserveRecovery, bool reserveAttack = false)
        {
            // Existing character-domain API and real production spells; no invented content or session replacement.
            if (reserveRecovery)
                Assert.IsTrue(GameBootstrap.RequireRuntime().Player.AbilityLoadout.UnequipSpell(Recovery));
            if (reserveAttack)
                Assert.IsTrue(GameBootstrap.RequireRuntime().Player.AbilityLoadout.UnequipSpell(Attack));
            // Adventure takes its immutable character input on entry, after this legitimate loadout preparation.
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>();
            random = new CombatRandom();
            encounter.SetCombatResolutionRandomSource(random);
            hud = Find<CombatHudPresenter>();
            bar = Find<CombatActionBarView>();
            input = Find<CombatCommandInput>();
            preview = Find<CombatActionPreviewView>();
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            yield return Ready();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest] public IEnumerator SwordRoundTripAfterVictory() { yield return RoundTrip(false); }
        [UnityTest] public IEnumerator BladeRoundTripAfterDefeat() { yield return RoundTrip(true); }

        private IEnumerator RoundTrip(bool defeat)
        {
            var runtime = GameBootstrap.RequireRuntime();
            Assert.AreEqual("item_iron_sword", runtime.Player.MainEquipmentItemId);
            AssertInventory(runtime);
            yield return BeginEncounter(false);
            long inventoryVersion = runtime.InventoryVersion, turn = session.TurnSequence;
            int mp = spawned.Player.CurrentSpirit, tick = session.TurnScheduler.CurrentTick;
            var owner = runtime.Player;
            Assert.IsNotNull(bar.SwapEquipmentButton);
            bar.SwapEquipmentButton.onClick.Invoke();
            Assert.AreEqual(CombatActionKind.SwapEquipment, input.Candidate.Kind);
            Assert.AreEqual(25000, input.Preview.ActionPointCostUnits);
            Assert.IsTrue(input.Preview.CanConfirm, input.Preview.Reason);
            var cancelled = input.Candidate;
            int randomBefore = random.Count;
            preview.CancelButton.onClick.Invoke();
            Assert.AreEqual(100000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual("item_iron_sword", owner.MainEquipmentItemId);
            Assert.AreEqual(randomBefore, random.Count);
            foreach (int width in new[] { 1920, 1366 })
            {
                SetGameSize(width, width == 1920 ? 1080 : 768);
                for (int frame = 0; frame < 8; frame++) yield return null;
                bar.SwapEquipmentButton.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                var label = bar.SwapEquipmentButton.GetComponentInChildren<Text>();
                Assert.LessOrEqual(label.preferredWidth, label.rectTransform.rect.width + .1f);
                Assert.LessOrEqual(label.preferredHeight, label.rectTransform.rect.height + .1f);
                string directory = Path.Combine(Application.dataPath, "../Logs/equipment"); Directory.CreateDirectory(directory);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, "preview-" + width + (defeat ? "-blade" : "-sword") + ".png"));
                for (int frame = 0; frame < 3; frame++) yield return null;
                preview.CancelButton.onClick.Invoke();
            }
            bar.SwapEquipmentButton.onClick.Invoke();
            var old = input.Candidate;
            input.ConfirmCandidate(); yield return Ready();
            Assert.AreEqual(75000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual("basic_iron_blade", spawned.Player.BasicAttackProfileId);
            Assert.AreEqual("item_iron_blade", owner.MainEquipmentItemId);
            Assert.AreSame(owner, runtime.Player); Assert.AreEqual(turn, session.TurnSequence);
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick); Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
            Assert.AreEqual(inventoryVersion, runtime.InventoryVersion); Assert.AreEqual(randomBefore, random.Count);
            encounter.ConfirmAction(old);
            Assert.AreEqual(75000, spawned.Player.ActionPointsRemainingUnits);
            Assert.AreEqual(50000, hud.Snapshot.FindAction(CombatActionKind.BasicAttack).ActionPointCostUnits);
            if (!defeat)
            {
                bar.SwapEquipmentButton.onClick.Invoke(); input.ConfirmCandidate(); yield return Ready();
                Assert.AreEqual(50000, spawned.Player.ActionPointsRemainingUnits);
                Assert.AreEqual("basic_iron_sword", spawned.Player.BasicAttackProfileId);
                Assert.AreEqual(40000, hud.Snapshot.FindAction(CombatActionKind.BasicAttack).ActionPointCostUnits);
            }
            string expected = defeat ? "item_iron_blade" : "item_iron_sword";
            GuanzhongBasicAttackPlayModeTests.ArmEnemyOutcomeFixture(spawned.Enemy);
            if (defeat) spawned.Player.ReceiveDamage(spawned.Player.CurrentHealth - 1); // explicit near-death combat fixture
            for (float deadline = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name == GameplaySceneNames.Adventure;)
            {
                if (!defeat) encounter.RequestBasicAttack("player", "enemy");
                encounter.RequestEndTurn("player");
                yield return null;
            }
            Assert.AreEqual(GameplaySceneNames.Settlement, SceneManager.GetActiveScene().name,
                "Combat state=" + session.TurnPhase + "/" + session.CurrentActorId + "; AP=" + spawned.Player.ActionPointsRemainingUnits +
                "; HP=" + spawned.Player.CurrentHealth + "/" + spawned.Enemy.CurrentHealth + "; position=" + spawned.Player.Position + "/" + spawned.Enemy.Position);
            Assert.AreEqual(expected, runtime.Player.MainEquipmentItemId); AssertInventory(runtime);
            string json = runtime.CaptureSaveJson();
            Find<SettlementController>().SaveAndReturnToMenu(); yield return SceneReady(GameplaySceneNames.StartMenu);
            Find<StartMenuController>().LoadPlayer("formal-swap"); yield return SceneReady(GameplaySceneNames.Settlement);
            runtime = GameBootstrap.RequireRuntime(); Assert.AreEqual(json, runtime.CaptureSaveJson()); AssertInventory(runtime);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild")); yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            var next = Get<AdventureSpawnSet>(Find<EncounterCoordinator>(), "spawned");
            Assert.AreEqual(expected.Replace("item_", "basic_"), next.Player.BasicAttackProfileId);
            AssertInventory(runtime);
        }
        private static void AssertInventory(GameRuntime runtime)
        {
            foreach (string id in new[] { "item_iron_sword", "item_iron_blade" })
                Assert.IsTrue(runtime.OwnsEquipmentInstance("player", id, "player:initial:" + id));
        }

        private IEnumerator Ready()
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);)
                yield return null;
            yield return null;
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(encounter.IsPresenting, Is.False);
        }

        public static void SetGameSize(int width, int height)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), width, height, "Swap validation" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(width, height, false);
#endif
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;)
                yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        private sealed class CombatRandom : ICombatResolutionRandomSource
        {
            public int Count { get; private set; }
            public float NextPercent() => Count++ % 4 == 0 ? 0f : 99f;
        }
    }
}
