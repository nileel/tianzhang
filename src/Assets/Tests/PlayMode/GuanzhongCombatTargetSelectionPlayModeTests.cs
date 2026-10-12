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
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatTargetSelectionPlayModeTests
    {
        private string saveDirectory;
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private CombatHudPresenter presenter;
        private CombatTargetView target;
        private CombatTimelineView timeline;
        private Static3DCombatUnitPresentationProvider provider;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private AdventureSpawnSet spawned;

        [UnitySetUp]
        public IEnumerator EnterFormalEncounter()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Target-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.AreEqual("origin_loose", creation.Draft.OriginId);
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul, creation.Draft.Innate.DivineSense,
                creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            creation.Submit("formal-target", "目标联动验证");
            yield return SceneReady(GameplaySceneNames.World);
            GuanzhongBasicAttackPlayModeTests.PrepareEquippedPlayerFixture();
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        private IEnumerator EnterAdventure()
        {
            SettlementController settlement = Find<SettlementController>();
            BountyBoardView board = Find<BountyBoardView>();
            board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.IsTrue(settlement.EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            presenter = Find<CombatHudPresenter>();
            target = Find<CombatTargetView>();
            timeline = Find<CombatTimelineView>();
            provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsNull(presenter.InspectedTargetId);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new CombatRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            session = Get<CombatSession>(encounter, "session");
            spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            for (int frame = 0; frame < 600 && !presenter.CanInspect; frame++) yield return null;
            Assert.IsTrue(presenter.CanInspect);
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = background;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator WorldAxisAndKeyboardKeepStableIdentityWithoutChangingCommands()
        {
            string actor = session.CurrentActorId;
            CombatCommandInput commands = Find<CombatCommandInput>();
            string commandTarget = Get<string>(commands, "targetId");
            string artTarget = Get<string>(commands, "secondArtTargetId");
            Assert.AreEqual(2, presenter.Snapshot.ArtActions.Count);
            Assert.That(artTarget, Is.Not.Null.And.Not.Empty);
            int tick = session.TurnScheduler.CurrentTick, hp = spawned.Player.CurrentHealth, mp = spawned.Player.CurrentSpirit;
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Enemy.Id);
            StringAssert.Contains("敌方", target.DetailsText);
            StringAssert.Contains("伤害：未探明", target.DetailsText);
            AssertMarker();
            Button[] entries = Get<Button[]>(timeline, "entryButtons");
            int playerIndex = presenter.Snapshot.Timeline.Entries.ToList().FindIndex(value => value.Id == spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, entries[playerIndex].transform.position));
            AssertSelection(spawned.Player.Id);
            StringAssert.Contains("我方", target.DetailsText);
            CombatTimelineSnapshot original = presenter.Snapshot.Timeline;
            CombatHudSnapshot hud = presenter.Snapshot;
            presenter.Present(new CombatHudSnapshot(hud.Player, hud.Enemy, hud.TurnText, hud.AcceptsCommands,
                hud.ArtProfileIds, hud.DivineProfileIds,
                new CombatTimelineSnapshot(original.CurrentTick, original.CurrentActorId, original.Entries.Reverse().ToArray()),
                hud.CanEndTurn, hud.Movement, hud.IsPresenting, hud.ArtActions));
            int newPlayerIndex = original.Entries.Count - 1 - playerIndex;
            Assert.AreSame(entries[newPlayerIndex].gameObject, EventSystem.current.currentSelectedGameObject);
            AssertSelection(spawned.Player.Id);
            // Keyboard navigation changes peek only; submit changes inspection.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(newPlayerIndex == 0 ? Key.DownArrow : Key.UpArrow));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.AreEqual(spawned.Enemy.Id, timeline.FocusedCombatantId);
            AssertSelection(spawned.Player.Id);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            AssertSelection(spawned.Enemy.Id);
            Assert.AreEqual(actor, session.CurrentActorId);
            Assert.AreEqual(commandTarget, Get<string>(commands, "targetId"));
            Assert.AreEqual(artTarget, Get<string>(commands, "secondArtTargetId"));
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(hp, spawned.Player.CurrentHealth);
            Assert.AreEqual(mp, spawned.Player.CurrentSpirit);
        }

        [UnityTest]
        public IEnumerator UiMovementAndPresentationArbitrateWithoutDoubleTrigger()
        {
            yield return Click(WorldPoint(spawned.Player.Id));
            AssertSelection(spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, Get<Text>(target, "details").transform.position));
            AssertSelection(spawned.Player.Id);
            yield return Click(RectTransformUtility.WorldToScreenPoint(null, target.CloseButton.transform.position));
            AssertSelection(null);
            yield return Click(WorldPoint(spawned.Enemy.Id));
            yield return Click(new Vector2(Screen.width * 0.5f, Screen.height * 0.95f));
            AssertSelection(null);
        }

        [UnityTest]
        public IEnumerator ActionPointMovementAndPresentationArbitrateWithoutDoubleTrigger()
        {
            yield return Click(WorldPoint(spawned.Player.Id));
            AssertSelection(spawned.Player.Id);
            CombatMovementView movement = Find<CombatMovementView>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            HexCoord before = spawned.Player.Position;
            int points = spawned.Player.ActionPointsRemainingUnits;
            bar.MoveButton.onClick.Invoke();
            yield return Click(WorldPoint(spawned.Enemy.Id));
            Assert.IsTrue(movement.IsSelecting);
            AssertSelection(spawned.Player.Id);
            Assert.AreEqual(before, spawned.Player.Position);
            CombatMovementOption option = presenter.Snapshot.Movement.Options.First(value => value.Cost == 50000);
            Vector3 tile = GameObject.Find("GuanzhongBattlefield").transform.Find("GuanzhongHex_" + option.Destination.Q + "_" + option.Destination.R).position;
            yield return Click(Camera.main.WorldToScreenPoint(tile));
            Assert.NotNull(movement.Selected);
            Button[] entries = Get<Button[]>(timeline, "entryButtons");
            entries[1].onClick.Invoke();
            Assert.NotNull(movement.Selected, "Inspecting must not call SetContext and erase the movement candidate.");
            bar.CancelMoveButton.onClick.Invoke();
            Assert.AreEqual(before, spawned.Player.Position);
            Assert.AreEqual(points, spawned.Player.ActionPointsRemainingUnits);
            bar.MoveButton.onClick.Invoke();
            yield return Click(Camera.main.WorldToScreenPoint(tile));
            bar.ConfirmMoveButton.onClick.Invoke();
            Assert.IsTrue(encounter.IsPresenting);
            Assert.IsFalse(presenter.CanInspect);
            entries[1].onClick.Invoke();
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Player.Id);
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            Assert.IsFalse(encounter.IsPresenting);
            Assert.AreEqual(new HexCoord(option.Destination.Q, option.Destination.R), spawned.Player.Position);
            Assert.AreEqual(points - option.Cost, spawned.Player.ActionPointsRemainingUnits);
            yield return null;
            AssertMarker();
        }

        [UnityTest]
        public IEnumerator HideRealDeathAndReentryClearTheShortLivedSelection()
        {
            presenter.InspectTarget(spawned.Enemy.Id);
            AssertSelection(spawned.Enemy.Id);
            CombatHudSnapshot last = presenter.Snapshot;
            presenter.Hide();
            AssertSelection(null);
            presenter.Present(last);
            AssertSelection(null);
            presenter.InspectTarget(spawned.Enemy.Id);
            AssertSelection(spawned.Enemy.Id);
            // Controlled death precondition only; the command, presentation and scene return are real.
            spawned.Enemy.ReceiveDamage(spawned.Enemy.CurrentHealth - 1);
            ((ICombatCommandHandler)encounter).RequestBasicAttack(spawned.Player.Id, spawned.Enemy.Id);
            Assert.AreEqual(0, spawned.Enemy.CurrentHealth);
            AssertSelection(null);
            yield return SceneReady(GameplaySceneNames.Settlement);
            yield return EnterAdventure();
            AssertSelection(null);
            yield return Click(WorldPoint(spawned.Enemy.Id));
            AssertSelection(spawned.Enemy.Id);
        }

        private void AssertSelection(string id)
        {
            Assert.AreEqual(id, presenter.InspectedTargetId);
            Assert.AreEqual(id, timeline.InspectedTargetId);
            Assert.AreEqual(id, target.TargetId);
            Assert.AreEqual(id != null, Get<GameObject>(target, "root").activeSelf);
            if (id == null)
            {
                Assert.IsFalse(target.WorldMarker.gameObject.activeSelf);
                Assert.IsEmpty(target.DetailsText);
                Assert.That(Get<Text[]>(timeline, "entryLabels").All(label => !label.text.Contains("◆")), Is.True);
            }
            else StringAssert.Contains(presenter.FindInspectable(id).DisplayName, target.DetailsText);
        }

        private Vector2 WorldPoint(string id)
        {
            Assert.IsTrue(provider.TryGetVisibleBounds(id, out Bounds bounds));
            return Camera.main.WorldToScreenPoint(bounds.center);
        }

        private void AssertMarker()
        {
            Assert.IsTrue(provider.TryGetVisibleBounds(target.TargetId, out Bounds bounds));
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
            for (int index = 0; index < 8; index++)
            {
                Vector3 screen = Camera.main.WorldToScreenPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((index & 1) == 0 ? -1 : 1, (index & 2) == 0 ? -1 : 1, (index & 4) == 0 ? -1 : 1)));
                min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
            }
            var corners = new Vector3[4];
            target.WorldMarker.GetWorldCorners(corners);
            Assert.That(Vector2.Distance(min, corners[0]), Is.LessThan(1f));
            Assert.That(Vector2.Distance(max, corners[2]), Is.LessThan(1f));
            Assert.IsTrue(target.WorldMarker.gameObject.activeSelf);
        }

        private IEnumerator Click(Vector2 point)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            yield return null;
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline && SceneManager.GetActiveScene().name != scene;) yield return null;
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
            private int count;
            public float NextPercent() => count++ % 5 == 0 ? 0f : 99f;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/验证/记录目标联动现场 %&y")]
        private static void CaptureVisibleTargetEvidence()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Requires ordinary live Play Mode.");
            CombatHudPresenter hud = Find<CombatHudPresenter>();
            CombatTargetView view = Find<CombatTargetView>();
            Static3DCombatUnitPresentationProvider units = Find<Static3DCombatUnitPresentationProvider>();
            CombatCommandInput input = Find<CombatCommandInput>();
            Text detailText = Get<Text>(view, "details");
            var evidence = new VisibleEvidence
            {
                width = Screen.width, height = Screen.height, selected = hud.InspectedTargetId,
                axis = Find<CombatTimelineView>().InspectedTargetId, focus = Find<CombatTimelineView>().FocusedCombatantId,
                actor = hud.Snapshot?.Timeline?.CurrentActorId, commandTarget = Get<string>(input, "targetId"),
                secondArtTarget = Get<string>(input, "secondArtTargetId"), details = view.DetailsText,
                canInspect = hud.CanInspect, selecting = Find<CombatMovementView>().IsSelecting,
                markerActive = view.WorldMarker.gameObject.activeSelf,
                markerCorners = new Vector3[4], tick = hud.Snapshot?.Timeline?.CurrentTick ?? -1
            };
            evidence.textSize = detailText.rectTransform.rect.size;
            evidence.preferredHeight = detailText.preferredHeight;
            evidence.selectedButton = EventSystem.current?.currentSelectedGameObject?.name;
            evidence.focusedEditor = UnityEditor.EditorWindow.focusedWindow?.GetType().Name;
            view.WorldMarker.GetWorldCorners(evidence.markerCorners);
            if (evidence.selected != null && units.TryGetPresentedUnit(evidence.selected, out GameObject root))
            {
                evidence.root = root.name; evidence.rootPosition = root.transform.position;
                units.TryGetVisibleBounds(evidence.selected, out Bounds bounds);
                evidence.boundsCenter = bounds.center; evidence.boundsSize = bounds.size;
            }
            string directory = Path.Combine(Application.dataPath, "../Logs/target");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "visible-target-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, JsonUtility.ToJson(evidence, true));
            Debug.Log("Read-only target evidence: " + path);
        }
        [Serializable]
        private sealed class VisibleEvidence
        {
            public int width, height, tick;
            public string selected, axis, focus, actor, commandTarget, secondArtTarget, details, root;
            public bool canInspect, selecting, markerActive;
            public string selectedButton, focusedEditor;
            public Vector2 textSize;
            public float preferredHeight;
            public Vector3 rootPosition, boundsCenter, boundsSize;
            public Vector3[] markerCorners;
        }
#endif
    }
}
