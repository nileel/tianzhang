using System;
using System.Collections;
using System.Collections.Generic;
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
using TianZhang.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static TianZhang.Tests.PlayMode.GuanzhongBasicAttackPlayModeTests;

namespace TianZhang.Tests.PlayMode
{
    // Bounty UI acceptance uses an explicit controlled combat precondition, not player-balance evidence.
    // Rock acceptance uses the formal scene, production movement, and original mesh evidence.
    // Input goes through InputSystem -> the production EventSystem, never Button.onClick.Invoke.
    public sealed class GuanzhongRuntimeEvidencePlayModeTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private readonly List<string> saveDirectories = new List<string>();
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private static string OutputDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/gz-r2-evidence"));

        [SetUp]
        public void ConfigureInput()
        {
            background = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            Directory.CreateDirectory(OutputDirectory);
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
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            foreach (string directory in saveDirectories)
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            saveDirectories.Clear();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator BountyPointerPipelineAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                var board = Find<BountyBoardView>();
                var settlement = Find<SettlementController>();
                board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
                yield return null;
                yield return Click(Get<Button>(board, "nextButton"));
                Assert.That(board.CurrentBountyId, Is.EqualTo(BountyId));
                Canvas.ForceUpdateCanvases();
                Button accept = Get<Button>(board, "acceptButton");
                Button claim = Get<Button>(board, "claimButton");
                var trace = new List<string> { Header(), "input=InputSystem virtual Mouse/Keyboard -> production EventSystem" };
                EventSystem.current.SetSelectedGameObject(null);
                int entered = 0, pressed = 0, released = 0, clicked = 0;
                var trigger = accept.gameObject.AddComponent<EventTrigger>();
                Observe(trigger, EventTriggerType.PointerEnter, () => entered++);
                Observe(trigger, EventTriggerType.PointerDown, () => pressed++);
                Observe(trigger, EventTriggerType.PointerUp, () => released++);
                Observe(trigger, EventTriggerType.PointerClick, () => clicked++);
                Vector2 center = RectTransformUtility.WorldToScreenPoint(accept.GetComponentInParent<Canvas>().worldCamera,
                    accept.transform.TransformPoint(((RectTransform)accept.transform).rect.center));
                yield return Pointer(new Vector2(2, 2), false);
                AssertButtons(accept, claim, true, false);
                AssertSprite(accept, "Normal");
                trace.Add(ButtonState("normal", accept, claim));
                yield return Capture("bounty-normal-" + size.x);
                yield return Click(claim);
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Available));
                Assert.That(board.LastResultReason, Is.Null, "Disabled claim must not dispatch a request.");
                yield return Pointer(center, false);
                Assert.That(entered, Is.GreaterThan(0), "Production raycast must reach AcceptBountyButton.");
                AssertSprite(accept, "Hover");
                trace.Add(ButtonState("hover", accept, claim));
                yield return Capture("bounty-hover-" + size.x);
                yield return Pointer(new Vector2(2, 2), false);
                AssertSprite(accept, "Normal");
                EventSystem.current.SetSelectedGameObject(Get<Button>(board, "closeButton").gameObject);
                yield return KeyPress(Key.UpArrow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(accept.gameObject), "Keyboard focus must skip the disabled claim button.");
                AssertSprite(accept, "Selected");
                trace.Add(ButtonState("keyboard-focus", accept, claim));
                yield return Capture("bounty-focus-" + size.x);
                EventSystem.current.SetSelectedGameObject(null);
                yield return Pointer(center, false);
                AssertSprite(accept, "Hover");
                yield return Pointer(center, true);
                Assert.That(pressed, Is.EqualTo(1));
                AssertSprite(accept, "Selected");
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Available));
                trace.Add(ButtonState("press", accept, claim));
                yield return Capture("bounty-press-" + size.x);
                yield return Pointer(center, false);
                Assert.That(released, Is.EqualTo(1));
                Assert.That(clicked, Is.EqualTo(1));
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Accepted));
                AssertButtons(accept, claim, false, false);
                AssertSprite(accept, "Normal");
                trace.Add(ButtonState("release-accepted", accept, claim));
                yield return Pointer(new Vector2(2, 2), false);
                AssertSprite(accept, "Normal");
                trace.Add(ButtonState("leave", accept, claim));
                trace.Add($"pointerEnter={entered}; pointerDown={pressed}; pointerUp={released}; pointerClick={clicked}; focus={EventSystem.current.currentSelectedGameObject?.name}");
                yield return Click(accept);
                yield return Click(claim);
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Accepted));
                Assert.That(board.LastResultReason, Is.Null, "Repeated accept and unfinished claim must not dispatch requests.");
                yield return Capture("bounty-accepted-" + size.x);
                yield return CompleteBountyThroughControlledCombat(settlement, trace);
                yield return SetGameSize(size);
                settlement = Find<SettlementController>();
                board = Find<BountyBoardView>();
                board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
                yield return null;
                yield return Click(Get<Button>(board, "nextButton"));
                Assert.That(board.CurrentBountyId, Is.EqualTo(BountyId));
                Canvas.ForceUpdateCanvases();
                accept = Get<Button>(board, "acceptButton");
                claim = Get<Button>(board, "claimButton");
                AssertButtons(accept, claim, false, true);
                EventSystem.current.SetSelectedGameObject(null);
                yield return Pointer(new Vector2(2, 2), false);
                AssertSprite(claim, "Normal");
                trace.Add(ButtonState("objective-completed", accept, claim));
                yield return Capture("bounty-completed-" + size.x);
                EventSystem.current.SetSelectedGameObject(Get<Button>(board, "closeButton").gameObject);
                yield return KeyPress(Key.UpArrow);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.EqualTo(claim.gameObject));
                AssertSprite(claim, "Selected");
                trace.Add(ButtonState("claim-keyboard-focus", accept, claim));
                yield return Capture("bounty-claim-focus-" + size.x);
                EventSystem.current.SetSelectedGameObject(null);
                center = ButtonCenter(claim);
                yield return Pointer(center, false);
                AssertSprite(claim, "Hover");
                trace.Add(ButtonState("claim-hover", accept, claim));
                yield return Capture("bounty-claim-hover-" + size.x);
                yield return Pointer(new Vector2(2, 2), false);
                AssertSprite(claim, "Normal");
                yield return Pointer(center, false);
                yield return Pointer(center, true);
                AssertSprite(claim, "Selected");
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.ObjectiveCompleted));
                trace.Add(ButtonState("claim-press", accept, claim));
                yield return Capture("bounty-claim-press-" + size.x);
                int rewardBefore = GameBootstrap.RequireRuntime().GetItemQuantity("item_lingshi_low");
                yield return Pointer(center, false);
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Claimed));
                Assert.That(GameBootstrap.RequireRuntime().GetItemQuantity("item_lingshi_low"), Is.EqualTo(rewardBefore + 3));
                AssertButtons(accept, claim, false, false);
                yield return Pointer(new Vector2(2, 2), false);
                AssertSprite(claim, "Normal");
                yield return Click(claim);
                yield return Click(accept);
                Assert.That(GameBootstrap.RequireRuntime().GetItemQuantity("item_lingshi_low"), Is.EqualTo(rewardBefore + 3), "Repeated claim must not grant another reward.");
                Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Claimed));
                Assert.That(board.LastResultReason, Is.Null, "Disabled buttons must not dispatch requests.");
                trace.Add(ButtonState("claimed-repeat-blocked", accept, claim));
                yield return Capture("bounty-claimed-" + size.x);
                Write("bounty-" + size.x, trace);
            }
        }

        private static IEnumerator CompleteBountyThroughControlledCombat(SettlementController settlement, List<string> trace)
        {
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.That(adventure.CurrentState, Is.EqualTo(AdventureSceneState.Exploration));
            adventure.SetEncounterRandomSource(new EncounterRandom());
            var encounter = Find<EncounterCoordinator>();
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            CombatSession session = Get<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            for (float deadline = Time.realtimeSinceStartup + 30f;
                 Time.realtimeSinceStartup < deadline &&
                 (session.CurrentActorId != "player" || session.TurnPhase != CombatTurnPhase.Active || encounter.IsPresenting);)
                yield return null;
            Assert.That(session.CurrentActorId, Is.EqualTo("player"));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(encounter.IsPresenting, Is.False);
            Assert.That(spawned.Player.BasicAttackProfileId, Is.EqualTo("basic_iron_sword"));
            Assert.That(spawned.Enemy.BasicAttackProfileId, Is.EqualTo("basic_body_shijiahou_lianqi"));
            yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);

            // Explicit one-hit fixtures keep this UI test focused on real death and bounty settlement.
            // Both formal enemies must die; no RecordDefeat or bounty-state injection is used.
            foreach (var enemy in spawned.Enemies) enemy.ReceiveDamage(enemy.CurrentHealth - 1);
            Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.Accepted));
            encounter.SetCombatResolutionRandomSource(new OutcomeFixtureRandom(session, true));
            var legal = new CombatLegalActionService();
            var ai = new LegalActionAI();
            for (float deadline = Time.realtimeSinceStartup + 90f;
                Time.realtimeSinceStartup < deadline && encounter != null && encounter.IsRunning;)
            {
                if (encounter.AcceptsPlayerCommand && !encounter.IsPresenting && session.CurrentActorId == "player" && session.TurnPhase == CombatTurnPhase.Active)
                {
                    CombatCommand action = ai.ChooseAction(session, legal.GetLegalActions(session, "player")
                        .Where(value => value.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Move or CombatCommandKind.EndTurn ||
                            value.Kind == CombatCommandKind.Art && value.TargetId != "player").ToArray());
                    TestContext.WriteLine("BOUNTY action=" + action.Kind + "; profile=" + action.ProfileId + "; player=" + spawned.Player.Position +
                        "; AP=" + spawned.Player.ActionPointsRemainingUnits + "; targets=" + string.Join(";", spawned.Enemies.Select(
                            enemy => enemy.Id + ":" + enemy.Position + ":HP" + enemy.CurrentHealth)));
                    if (action.Kind == CombatCommandKind.Move)
                        encounter.RequestMove("player", action.Destination.Value.Q, action.Destination.Value.R);
                    else if (action.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art)
                    {
                        var kind = action.Kind == CombatCommandKind.BasicAttack ? CombatActionKind.BasicAttack : CombatActionKind.Art;
                        var candidate = new CombatActionCandidate(kind, "player", action.TargetId,
                            action.ProfileId, Find<CombatHudPresenter>().Snapshot.CommandContextVersion, attackDirection: action.AttackDirection);
                        var quote = encounter.PreviewAction(candidate);
                        Assert.That(quote.CanConfirm, Is.True, quote.Reason);
                        encounter.ConfirmAction(quote.Candidate);
                    }
                    else encounter.RequestEndTurn("player");
                }
                yield return null;
            }
            Assert.That(spawned.Enemies.All(enemy => !enemy.IsAlive), Is.True, "Production attacks must defeat both enemies.");
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.That(adventure.LastEncounterOutcome, Is.EqualTo(CombatSessionOutcome.Victory));
            Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status, Is.EqualTo(BountyStatus.ObjectiveCompleted));
            Assert.That(GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Progress, Is.EqualTo(1));
            string evidence = "controlled-combat-precondition: bothTemporaryEnemyHP=1; productionAttackHP=0; outcome=Victory; bounty=ObjectiveCompleted; not natural-new-player victory evidence";
            trace.Add(evidence);
            TestContext.WriteLine(evidence);
        }

        [UnityTest]
        public IEnumerator BattlefieldBoundsAndSpawnProjectionAtBothResolutions()
        {
            yield return EnterSettlement();
            var settlement = Find<SettlementController>();
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.That(adventure.CurrentState, Is.EqualTo(AdventureSceneState.Exploration));
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            yield return null;
            var encounter = Find<EncounterCoordinator>();
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            CombatSession session = Get<CombatSession>(encounter, "session");
            Camera camera = Get<Camera>(Find<CombatMovementView>(), "battlefieldCamera");
            var objects = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform[] cells = objects.Where(item => item.name.StartsWith("GuanzhongHex_", StringComparison.Ordinal)).ToArray();
            Assert.That(cells.Length, Is.EqualTo(100));
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.That(provider.TryGetPresentedUnit("enemy", out GameObject enemyRoot), Is.True);
            Renderer[] enemyRenderers = enemyRoot.GetComponentsInChildren<Renderer>();
            Assert.That(enemyRenderers, Is.Not.Empty);
            string[] names = objects.Where(item => item.name.StartsWith("GuanzhongObstacle_", StringComparison.Ordinal))
                .Select(item => item.name).OrderBy(name => name).ToArray();
            Assert.That(names.Length, Is.EqualTo(15));
            foreach (Vector2Int size in Sizes())
            {
                yield return SetGameSize(size);
                var lines = new List<string> { Header(), $"camera={PathOf(camera.transform)}; position={camera.transform.position:F5}; rotation={camera.transform.eulerAngles:F5}; ortho={camera.orthographicSize}; pixelRect={camera.pixelRect}; aspect={camera.aspect}" };
                foreach (Image image in objects.SelectMany(item => item.GetComponents<Image>()).Where(item => item.isActiveAndEnabled && item.color.a > 0))
                {
                    var corners = new Vector3[4];
                    image.rectTransform.GetWorldCorners(corners);
                    Canvas canvas = image.canvas;
                    Vector2[] screen = corners.Select(point => RectTransformUtility.WorldToScreenPoint(
                        canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, point)).ToArray();
                    Rect rect = Rect.MinMaxRect(screen.Min(p => p.x), screen.Min(p => p.y), screen.Max(p => p.x), screen.Max(p => p.y));
                    if (rect.width * rect.height > 20000)
                        lines.Add($"hud={PathOf(image.transform)}; screenRect={rect}; canvas={canvas.renderMode}; scale={canvas.scaleFactor}");
                }
                foreach (Transform cell in cells.OrderBy(item => item.name))
                {
                    string[] coordinate = cell.name.Split('_');
                    var hex = new HexCoord(int.Parse(coordinate[1]), int.Parse(coordinate[2]));
                    // Read-only multi-turn reachability quote; does not grant AP or move either unit.
                    var path = session.SpatialQuery.QueryMovement(spawned.Player.Pose, hex, 10000000,
                        spawned.Player.MovementEfficiencyMilliUnits, Array.Empty<HexCoord>()).Path;
                    lines.Add($"cell={cell.name}; world={cell.position:F5}; screen={camera.WorldToScreenPoint(cell.position):F3}; pathFromPlayer={string.Join("/", path)}");
                    Vector3 offset = cell.position - enemyRoot.transform.position;
                    var projected = new List<Vector3>();
                    foreach (Renderer renderer in enemyRenderers)
                    {
                        Bounds bounds = renderer.bounds;
                        for (int corner = 0; corner < 8; corner++)
                            projected.Add(camera.WorldToScreenPoint(offset + new Vector3(
                                (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                                (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                                (corner & 4) == 0 ? bounds.min.z : bounds.max.z)));
                    }
                    lines.Add($"candidate={hex}; translatedEnemyBoundsMin=({projected.Min(p => p.x):F3},{projected.Min(p => p.y):F3}); translatedEnemyBoundsMax=({projected.Max(p => p.x):F3},{projected.Max(p => p.y):F3}); fullyOnScreen={projected.All(p => p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height && p.z > 0)}");
                }
                foreach (string name in names)
                {
                    Transform obstacle = objects.Single(item => item.name == name);
                    lines.Add("obstacle=" + PathOf(obstacle) + "; local=" + obstacle.localPosition.ToString("F5") + "; scale=" + obstacle.lossyScale.ToString("F5"));
                    foreach (Renderer renderer in obstacle.GetComponentsInChildren<Renderer>()) lines.Add(RendererState(renderer, camera));
                    foreach (Renderer renderer in obstacle.parent.GetComponentsInChildren<Renderer>().Where(item => !item.transform.IsChildOf(obstacle)))
                        lines.Add("tile=" + RendererState(renderer, camera));
                }
                foreach (Transform occluder in objects.Where(item => item.name.Contains("VisualBaselineOccluder")))
                    lines.Add($"separateOccluder={PathOf(occluder)}; active={occluder.gameObject.activeInHierarchy}");
                foreach (var unit in new[] { spawned.Player, spawned.Enemy })
                {
                    Vector3 world = Static3DCombatUnitPresentationProvider.HexToWorld(new CombatUnitPresentationHex(unit.Position.Q, unit.Position.R));
                    lines.Add($"unit={unit.Id}; logic={unit.Position}; world={world:F5}; screen={camera.WorldToScreenPoint(world):F3}");
                    Assert.That(provider.TryGetPresentedUnit(unit.Id, out GameObject root), Is.True);
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                        lines.Add("unitRenderer=" + RendererState(renderer, camera));
                }
                Write("battlefield-" + size.x, lines);
                yield return Capture("battlefield-" + size.x);
            }

            // Default zoom intentionally shows the nearby battlefield. CameraZoom tests cover
            // full-board inspection; here every rock is checked in world space at both resolutions.
            foreach (Vector2Int size in Sizes())
            {
                yield return SetGameSize(size);
                var lines = new List<string> { Header(), "camera=" + camera.transform.position.ToString("F5") };
                foreach (string name in names)
                {
                    Transform obstacle = objects.Single(item => item.name == name);
                    MeshFilter filter = obstacle.GetComponentsInChildren<MeshFilter>().Single();
#if UNITY_EDITOR
                    Assert.That(UnityEditor.AssetDatabase.GetAssetPath(filter.sharedMesh),
                        Is.EqualTo("Assets/Art/Environments/Guanzhong/RockCluster.fbx"));
#endif
                    Vector3[] points = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                    var bounds = new Bounds(points[0], Vector3.zero);
                    foreach (Vector3 point in points)
                    {
                        bounds.Encapsulate(point);
                        Vector3 p = point - obstacle.position;
                        Assert.That(Mathf.Abs(p.x), Is.LessThanOrEqualTo(.45001f));
                        Assert.That(Mathf.Abs(.5f * p.x + .8660254f * p.z), Is.LessThanOrEqualTo(.45001f));
                        Assert.That(Mathf.Abs(.5f * p.x - .8660254f * p.z), Is.LessThanOrEqualTo(.45001f));
                    }
                    Assert.That(Vector2.Distance(new Vector2(bounds.center.x, bounds.center.z),
                        new Vector2(obstacle.position.x, obstacle.position.z)), Is.LessThan(.01f));
                    Assert.That(bounds.max.y - .3404328f, Is.EqualTo(.60f).Within(.05f));
                    Vector3[] screen = points.Select(camera.WorldToScreenPoint).ToArray();
                    Rect rockRect = Rect.MinMaxRect(screen.Min(p => p.x), screen.Min(p => p.y), screen.Max(p => p.x), screen.Max(p => p.y));
                    lines.Add(name + "; actualMeshMin=" + bounds.min.ToString("F6") + "; actualMeshMax=" + bounds.max.ToString("F6") + "; screen=" + rockRect);
                }
                Write("rocks-unobstructed-" + size.x, lines);
                yield return Capture("rocks-unobstructed-" + size.x);
            }

            var movementTrace = new List<string> { Header(), "Production stats and AI unchanged; one legal mouse click commits the quoted move." };
            var detour = new List<CombatUnitPresentationHex>();
            yield return MoveAroundRock(2, 0, encounter, spawned, camera, movementTrace, null);
            yield return RockInputReady(encounter);
            CombatActionBarView bar = Find<CombatActionBarView>();
            int apBefore = spawned.Player.ActionPointsRemainingUnits;
            HexCoord positionBefore = spawned.Player.Position;
            yield return Click(bar.MoveButton);
            Transform blocked = objects.Single(item => item.name == "GuanzhongHex_2_1");
            Vector2 blockedPoint = camera.WorldToScreenPoint(blocked.position);
            yield return Pointer(blockedPoint, false);
            yield return Pointer(blockedPoint, true);
            yield return Pointer(blockedPoint, false);
            Assert.That(Find<CombatMovementView>().Selected, Is.Null, "Clicking the rock must not select a movement destination.");
            Assert.That(spawned.Player.Position, Is.EqualTo(positionBefore));
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(apBefore));
            yield return KeyPress(Key.Escape);
            movementTrace.Add("rock 2,1 click rejected; position=" + positionBefore + "; AP=" + apBefore);
            yield return MoveAroundRock(1, 2, encounter, spawned, camera, movementTrace, detour);
            yield return MoveAroundRock(2, 2, encounter, spawned, camera, movementTrace, detour);
            Assert.That(detour.Count, Is.EqualTo(3), "The blocked direct distance is two; the actual detour is three steps.");
            Write("rock-detour", movementTrace);
            yield return Capture("rock-detour-complete");
        }

        private static IEnumerator RockInputReady(EncounterCoordinator encounter)
        {
            float deadline = Time.realtimeSinceStartup + 25;
            while (Time.realtimeSinceStartup < deadline)
            {
                var hud = Find<CombatHudPresenter>().Snapshot;
                if (hud != null && hud.AcceptsCommands && !encounter.IsPresenting) yield break;
                yield return null;
            }
            Assert.Fail("Formal encounter did not yield player input during the rock detour.");
        }

        private IEnumerator MoveAroundRock(int q, int r, EncounterCoordinator encounter, AdventureSpawnSet spawned,
            Camera camera, List<string> trace, List<CombatUnitPresentationHex> path)
        {
            yield return RockInputReady(encounter);
            var bar = Find<CombatActionBarView>();
            var option = Find<CombatHudPresenter>().Snapshot.Movement.Options.SingleOrDefault(o => o.Destination.Q == q && o.Destination.R == r);
            if (option == null)
            {
                Assert.That(bar.WaitButton.interactable, Is.True, "The next leg requires a legal turn boundary.");
                yield return Click(bar.WaitButton);
                yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
                yield return RockInputReady(encounter);
                option = Find<CombatHudPresenter>().Snapshot.Movement.Options.Single(o => o.Destination.Q == q && o.Destination.R == r);
            }
            Assert.That(option.Path.Any(p => (p.Q == 2 || p.Q == 3) && (p.R == 1 || p.R == 4)), Is.False);
            trace.Add("from=" + spawned.Player.Position + "; to=" + q + "," + r + "; path=" + string.Join("/", option.Path.Select(p => p.Q + "," + p.R)) + "; cost=" + option.Cost);
            int ap = spawned.Player.ActionPointsRemainingUnits;
            yield return Click(bar.MoveButton);
            var movement = Find<CombatMovementView>();
            var key = new Vector2Int(q, r);
            var centers = Get<Dictionary<Vector2Int, Vector3>>(movement, "centers");
            Assert.That(GuanzhongMovementPlayModeTests.TryFindCellPoint(movement, camera, centers[key], key,
                out Vector2 point), Is.True, "Movement destination must have an exposed, pickable point: " + key);
            yield return Pointer(point, false);
            Assert.That(Find<CombatMovementView>().Selected, Is.EqualTo(option), "Hover must preview the move.");
            yield return Pointer(point, true);
            yield return Pointer(point, false);
            Assert.That(spawned.Player.Position, Is.EqualTo(new HexCoord(q, r)));
            trace.Add("committed=" + spawned.Player.Position + "; AP before=" + ap + "; after=" + spawned.Player.ActionPointsRemainingUnits + "; HP=" + spawned.Player.CurrentHealth);
            path?.AddRange(option.Path);
            for (float deadline = Time.realtimeSinceStartup + 15; encounter.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.That(encounter.IsPresenting, Is.False);
            yield return Capture("rock-move-" + q + "-" + r);
        }

        [UnityTest]
        public IEnumerator RemoteSpawnRejectsAttackAndMovesIntoContactAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
                yield return SceneReady(GameplaySceneNames.Adventure);
                var adventure = Find<AdventureController>();
                for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
                adventure.SetEncounterRandomSource(new EncounterRandom());
                Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
                var encounter = Find<EncounterCoordinator>();
                CombatSession session = Get<CombatSession>(encounter, "session");
                AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
                yield return SetGameSize(size);
                Assert.That(spawned.Player.Position, Is.EqualTo(new HexCoord(0, 0)));
                Assert.That(spawned.Enemy.Position, Is.EqualTo(new HexCoord(7, 8)));
                Assert.That(spawned.Enemy.OccupiedCells, Does.Contain(new HexCoord(7, 9)));
                Assert.That(ContactDistance(session, spawned.Player.Pose, spawned.Enemy.Pose), Is.EqualTo(15d));
                Camera camera = Get<Camera>(Find<CombatMovementView>(), "battlefieldCamera");
                var provider = Find<Static3DCombatUnitPresentationProvider>();
                Assert.That(provider.TryGetVisibleBounds("enemy", out Bounds bounds), Is.True);
                var corners = new List<Vector3>();
                for (int corner = 0; corner < 8; corner++)
                    corners.Add(camera.WorldToScreenPoint(new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z)));
                Assert.That(corners.All(p => p.x >= 0 && p.x <= Screen.width && p.y >= 0 && p.y <= Screen.height && p.z > 0), Is.True);
                Rect enemyRect = Rect.MinMaxRect(corners.Min(p => p.x), corners.Min(p => p.y), corners.Max(p => p.x), corners.Max(p => p.y));
                Image tracker = Get<GameObject>(Find<CombatBountyTrackerView>(), "root").GetComponent<Image>();
                Assert.That(tracker, Is.Not.Null);
                var hudCorners = new Vector3[4];
                tracker.rectTransform.GetWorldCorners(hudCorners);
                Assert.That(enemyRect.Overlaps(Rect.MinMaxRect(hudCorners[0].x, hudCorners[0].y, hudCorners[2].x, hudCorners[2].y)), Is.False);
                var service = new CombatCommandService();
                // The equipped sword is a two-cell forward area, so a basic attack is a direction
                // rather than a target. Model it exactly like CombatCommandInput does.
                CombatActionCandidate AttackCandidate(int attackDirection) => new CombatActionCandidate(
                    CombatActionKind.BasicAttack, "player", null, null,
                    Find<CombatHudPresenter>().Snapshot.CommandContextVersion, attackDirection: attackDirection);
                var inputTrace = new List<string>();
                // ExecutePlayer drops a direct request until the turn-advance presentation ends, so
                // every direct call below waits for the coordinator's own acceptance gate.
                yield return PlayerInputReady(encounter, session, inputTrace);
                var path = session.SpatialQuery.QueryMovement(spawned.Player.Pose, spawned.Enemy.Position,
                    10000000, spawned.Player.MovementEfficiencyMilliUnits, Array.Empty<HexCoord>());
                Assert.That(path.IsReachable, Is.True, path.Reason);
                // HexCoord.DirectionTo only resolves adjacent cells, so the remote aim comes from the
                // first step of the authoritative path.
                int direction = path.Path.Select(cell => spawned.Player.Position.DirectionTo(cell)).First(value => value >= 0);
                var attack = new CombatCommand(CombatCommandKind.BasicAttack, "player",
                    profileId: spawned.Player.BasicAttackProfileId, attackDirection: direction);
                var distant = service.Preview(session, attack);
                Assert.That(distant.IsLegal, Is.False);
                Assert.That(distant.RejectionReason, Is.EqualTo("combat_attack_area_empty"),
                    "A remote spawn must be rejected because no enemy stands in the forward pair, not because the command is malformed.");
                var distantPreview = encounter.PreviewAction(AttackCandidate(direction));
                Assert.That(distantPreview.CanConfirm, Is.False, distantPreview.Reason);
                int ap = spawned.Player.ActionPointsRemainingUnits, health = spawned.Enemy.CurrentHealth;
                encounter.ConfirmAction(AttackCandidate(direction));
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap));
                Assert.That(spawned.Enemy.CurrentHealth, Is.EqualTo(health));
                var lines = new List<string> { Header(), $"spawn player={spawned.Player.Position}; enemy={spawned.Enemy.Position}; distance={ContactDistance(session, spawned.Player.Pose, spawned.Enemy.Pose)}; enemyScreenRect={enemyRect}; rejectedAttack={distant.RejectionReason}; path={string.Join("/", path.Path)}" };
                yield return Capture("remote-spawn-" + size.x);
                yield return PlayerInputReady(encounter, session, inputTrace);
                var move = service.Preview(session, new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(0, 1)));
                Assert.That(move.IsLegal, Is.True, move.RejectionReason);
                encounter.RequestMove("player", 0, 1);
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - move.ActionPointCostUnits));
                Assert.That(spawned.Player.Position, Is.EqualTo(new HexCoord(0, 1)), "The quoted move must commit exactly once.");
                for (float deadline = Time.realtimeSinceStartup + 10f; encounter.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
                Assert.That(encounter.IsPresenting, Is.False);
                yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
                Assert.That(spawned.Enemy.Position, Is.Not.EqualTo(new HexCoord(7, 8)), "Production AI must advance from its remote spawn.");
                SpatialContactResult closeContact = session.SpatialQuery.QueryRange(spawned.Player.Pose, spawned.Enemy.Pose, 1, 1);
                Assert.That(closeContact.IsInRange, Is.True,
                    "The player must close into melee contact instead of staying on the first move's cell.");
                yield return PlayerInputReady(encounter, session, inputTrace);
                int closeDirection = closeContact.SourceCell.DirectionToward(closeContact.TargetCell);
                var close = service.Preview(session, new CombatCommand(CombatCommandKind.BasicAttack, "player",
                    profileId: spawned.Player.BasicAttackProfileId, attackDirection: closeDirection));
                Assert.That(close.IsLegal, Is.True, close.RejectionReason);
                var closePreview = encounter.PreviewAction(AttackCandidate(closeDirection));
                Assert.That(closePreview.CanConfirm, Is.True, closePreview.Reason);
                ap = spawned.Player.ActionPointsRemainingUnits;
                encounter.ConfirmAction(AttackCandidate(closeDirection));
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(ap - close.ActionPointCostUnits));
                lines.AddRange(inputTrace);
                lines.Add($"contact player={spawned.Player.Position}; enemy={spawned.Enemy.Position}; distance={ContactDistance(session, spawned.Player.Pose, spawned.Enemy.Pose)}; tick={session.TurnScheduler.CurrentTick}; turn={session.TurnSequence}; legalAttackCost={close.ActionPointCostUnits}");
                Write("remote-spawn-" + size.x, lines);
            }
        }

        // 战斗→收益→成长→再战斗：第一场打石甲兽领灵石，第二场打裂石甲兽领青锋剑，结算换装后
        // 第三场用新武器再战；换装结果还要经保存／读取一次。
        [UnityTest]
        public IEnumerator TwoBattlesPayForTheWeaponGrowthAndRestartAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                var lines = new List<string> { Header() };
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                ContentCatalogData catalog = Get<ContentCatalogData>(Find<SettlementController>(), "contentCatalog");
                BountyBoardView Board()
                {
                    BountyBoardView view = Find<BountyBoardView>();
                    view.Show(catalog, SettlementController.GuanzhongSettlementId, runtime.Bounties);
                    return view;
                }

                Board().SubmitAccept("bounty_guanzhong_shijiahou");
                yield return RunGrowthBattle("shijiahou_encounter", size, "growth-battle1", lines);
                runtime = GameBootstrap.RequireRuntime();
                Board().SubmitClaim("bounty_guanzhong_shijiahou");
                Assert.That(runtime.Bounties.GetState("bounty_guanzhong_shijiahou").Status, Is.EqualTo(BountyStatus.Claimed));
                Assert.That(runtime.Player.MainEquipmentItemId, Is.EqualTo("item_iron_sword"),
                    "The first bounty pays resources, not equipment.");
                Assert.That(runtime.GetItemQuantity("item_qingfeng_jian"), Is.EqualTo(0));

                Board().SubmitAccept("bounty_guanzhong_lieshijia");
                yield return RunGrowthBattle("lieshijia_encounter", size, "growth-battle2", lines);
                runtime = GameBootstrap.RequireRuntime();
                SettlementController settlement = Find<SettlementController>();
                Board().SubmitClaim("bounty_guanzhong_lieshijia");
                Assert.That(runtime.GetItemQuantity("item_qingfeng_jian"), Is.EqualTo(1),
                    "The ranged bounty must grant the growth weapon.");
                Assert.That(runtime.Player.MainEquipmentItemId, Is.EqualTo("item_iron_sword"),
                    "Claiming the weapon must not equip it behind the player's back.");

                Assert.That(settlement.RequestEquipMainHand("item_qingfeng_jian"), Is.Null);
                Assert.That(runtime.Player.MainEquipmentItemId, Is.EqualTo("item_qingfeng_jian"));
                Assert.That(runtime.Player.MainEquipmentBasicAttackProfileId, Is.EqualTo("basic_qingfeng_jian"));
                Assert.That(settlement.RequestEquipMainHand("item_qingfeng_jian"), Is.EqualTo("equipment_no_change"));
                lines.Add("equip -> item_qingfeng_jian; profile=" + runtime.Player.MainEquipmentBasicAttackProfileId);

                string saved = runtime.CaptureSaveJson();
                runtime.RestoreSaveJson(saved, catalog);
                Assert.That(runtime.Player.MainEquipmentItemId, Is.EqualTo("item_qingfeng_jian"),
                    "The equipped weapon must survive a save round trip.");

                yield return RunGrowthBattle("shijiahou_encounter", size, "growth-battle3", lines);
                runtime = GameBootstrap.RequireRuntime();
                Assert.That(runtime.GetItemQuantity("item_qingfeng_jian"), Is.EqualTo(1));
                Write("growth-loop-" + size.x, lines);
            }
        }

        private static IEnumerator RunGrowthBattle(string nodeId, Vector2Int size, string label, List<string> lines)
        {
            Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.That(Find<AdventureInputController>().SelectNode(nodeId), Is.True);
            var encounter = Find<EncounterCoordinator>();
            CombatSession session = Get<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            Assert.That(session.TryGetProfile(spawned.Player.BasicAttackProfileId, out CombatAttackProfile profile), Is.True);
            lines.Add($"{label}: node={nodeId}; weaponProfile={spawned.Player.BasicAttackProfileId}; area={profile.Area}; " +
                $"cost={profile.ActionPointCostUnits}; multiplier={profile.PhysicalMultiplier:F2}; " +
                $"enemies={string.Join("/", spawned.Enemies.Select(enemy => enemy.Id + "@" + enemy.Position))}");
            yield return Capture(label + "-" + size.x);
            yield return GuanzhongBasicAttackPlayModeTests.FightFormalTwoEnemies(encounter);
            yield return SceneReady(GameplaySceneNames.Settlement);
        }

        // 铁剑普攻播剑气、飞星指播弹道；两种特效都只读表现事件，不改规则、不延长输入锁。
        [UnityTest]
        public IEnumerator AttackEffectsDifferentiateSwordAndStarAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
                yield return SceneReady(GameplaySceneNames.Adventure);
                var adventure = Find<AdventureController>();
                for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
                adventure.SetEncounterRandomSource(new EncounterRandom());
                Assert.That(Find<AdventureInputController>().SelectNode("lieshijia_encounter"), Is.True);
                var encounter = Find<EncounterCoordinator>();
                CombatSession session = Get<CombatSession>(encounter, "session");
                AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
                var service = new CombatCommandService();
                var lines = new List<string> { Header(),
                    $"node=lieshijia_encounter; enemies={string.Join("/", spawned.Enemies.Select(enemy => enemy.Id + "@" + enemy.Position))}" };

                HexCoord castTarget = default;
                bool TryCast(CombatantSnapshot target)
                {
                    var command = new CombatCommand(CombatCommandKind.Art, "player", target.Id, "spell_feixingzhi");
                    CombatCommandPreview quote = service.Preview(session, command);
                    if (!quote.IsLegal) return false;
                    var candidate = new CombatActionCandidate(CombatActionKind.Art, "player", target.Id,
                        "spell_feixingzhi", Find<CombatHudPresenter>().Snapshot.CommandContextVersion);
                    CombatActionPreviewSnapshot preview = encounter.PreviewAction(candidate);
                    SpatialContactResult contact = session.SpatialQuery.QueryRange(spawned.Player.Pose, target.Pose, 1, 5);
                    TestContext.WriteLine($"VFX TryCast target={target.Id} distance={ContactDistance(session, spawned.Player.Pose, target.Pose)} " +
                        $"presenting={encounter.IsPresenting} accepts={Get<bool>(encounter, "acceptsPlayerCommand")} " +
                        $"ctx={Find<CombatHudPresenter>().Snapshot.CommandContextVersion} can={preview.CanConfirm} reason={preview.Reason}");
                    Assert.That(preview.CanConfirm, Is.True, preview.Reason);
                    encounter.ConfirmAction(candidate);
                    castTarget = contact.TargetCell;
                    lines.Add($"star cast: target={target.Id}@{castTarget}; distance={ContactDistance(session, spawned.Player.Pose, target.Pose)}; cost={quote.ActionPointCostUnits}");
                    return true;
                }

                bool cast = false;
                for (int turn = 0; turn < 6 && !cast; turn++)
                {
                    yield return PlayerInputReady(encounter, session);
                    CombatantSnapshot target = spawned.Enemies.Where(enemy => enemy.IsAlive)
                        .OrderByDescending(enemy => enemy.CurrentHealth).First();
                    if (TryCast(target)) { cast = true; break; }
                    int artCost = service.Preview(session,
                        new CombatCommand(CombatCommandKind.Art, "player", target.Id, "spell_feixingzhi")).ActionPointCostUnits;
                    var move = Find<CombatHudPresenter>().Snapshot.Movement.Options
                        .Where(option => option.Cost + artCost <= spawned.Player.ActionPointsRemainingUnits)
                        .OrderBy(option => ContactDistance(session, spawned.Player.Pose.WithAnchor(ToHex(option.Destination)), target.Pose))
                        .ThenBy(option => option.Cost)
                        .FirstOrDefault();
                    if (move != null)
                    {
                        Assert.That(encounter.PreviewAction(move.Candidate).CanConfirm, Is.True);
                        encounter.ConfirmAction(move.Candidate);
                        for (int frame = 0; frame < 240 && encounter.IsPresenting; frame++) yield return null;
                        // The move can spend the last AP and hand the turn over; wait for the next
                        // accepted window before building a command context version.
                        yield return PlayerInputReady(encounter, session);
                        target = spawned.Enemies.Where(enemy => enemy.IsAlive)
                            .OrderByDescending(enemy => enemy.CurrentHealth).First();
                        if (TryCast(target)) { cast = true; break; }
                    }
                    if (session.CurrentActorId == "player" && session.TurnPhase == CombatTurnPhase.Active && !encounter.IsPresenting)
                        encounter.RequestEndTurn("player");
                    for (int frame = 0; frame < 240 && encounter.IsPresenting; frame++) yield return null;
                }
                Assert.That(cast, Is.True, "飞星指必须在 6 回合内进入射程并施放。");
                Assert.That(GameObject.Find(CombatAttackVfx.ProjectileObjectName), Is.Not.Null,
                    "飞星指必须播出弹道特效。");
                GameObject casterRoot = GameObject.Find("Static3DCombatUnit_player");
                Assert.That(casterRoot, Is.Not.Null, "正式场景必须存在玩家表现根节点。");
                Vector3 casterWorld = Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(spawned.Player.Position.Q, spawned.Player.Position.R));
                Vector3 castTo = Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(castTarget.Q, castTarget.R)) - casterWorld;
                castTo.y = 0f;
                float castFacingDot = Vector3.Dot(casterRoot.transform.forward, castTo.normalized);
                Assert.That(castFacingDot, Is.GreaterThan(0.35f), "施法时应转向目标地块。");
                lines.Add($"star facingDot={castFacingDot:F2}");
                Assert.That(encounter.IsPresenting, Is.True, "特效应由表现锁覆盖。");
                // Mid-flight frame: the projectile is between caster and target and the tail is longest.
                for (int frame = 0; frame < 13; frame++) yield return null;
                yield return Capture("attack-vfx-star-" + size.x);

                for (int frame = 0; frame < 240 && encounter.IsPresenting; frame++) yield return null;
                yield return GuanzhongBasicAttackPlayModeTests.WaitForMeleeContact(encounter, session);
                yield return PlayerInputReady(encounter, session);
                CombatHudSnapshot hud = Find<CombatHudPresenter>().Snapshot;
                SpatialContactResult meleeContact = session.SpatialQuery.QueryRange(spawned.Player.Pose, spawned.Enemy.Pose, 1, 1);
                int direction = meleeContact.SourceCell.DirectionToward(meleeContact.TargetCell);
                var attack = new CombatActionCandidate(CombatActionKind.BasicAttack, "player", null, null,
                    hud.CommandContextVersion, attackDirection: direction);
                CombatActionPreviewSnapshot attackPreview = encounter.PreviewAction(attack);
                Assert.That(attackPreview.CanConfirm, Is.True, attackPreview.Reason);
                encounter.ConfirmAction(attack);
                GameObject slash = GameObject.Find(CombatAttackVfx.SlashObjectName);
                Assert.That(slash, Is.Not.Null, "铁剑普攻必须播出剑气特效。");
                Vector3 actorWorld = Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(spawned.Player.Position.Q, spawned.Player.Position.R));
                Assert.That(Vector3.Distance(slash.transform.position, actorWorld), Is.LessThan(1.2f),
                    "近战特效必须锚定在出手角色附近，而不是目标格。");
                Vector3 toTarget = Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(meleeContact.TargetCell.Q, meleeContact.TargetCell.R)) - actorWorld;
                toTarget.y = 0f;
                GameObject playerRoot = GameObject.Find("Static3DCombatUnit_player");
                Assert.That(playerRoot, Is.Not.Null, "正式场景必须存在玩家表现根节点。");
                float facingDot = Vector3.Dot(playerRoot.transform.forward, toTarget.normalized);
                Assert.That(facingDot, Is.GreaterThan(0.35f), "攻击或施法时应转向目标地块。");
                lines.Add($"sword slash: direction={direction}; target={spawned.Enemy.Position}; cost={attackPreview.ActionPointCostUnits}; facingDot={facingDot:F2}");
                // Wide sweep frame rather than the initial short stroke.
                for (int frame = 0; frame < 11; frame++) yield return null;
                yield return Capture("attack-vfx-slash-" + size.x);
                Write("attack-vfx-" + size.x, lines);
            }
        }

        [UnityTest]
        public IEnumerator R4ScaledEnemiesMoveAttackAndFinishTheFormalEncounterAtBothResolutions()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/r4-stats"));
            Directory.CreateDirectory(output);
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
                yield return SceneReady(GameplaySceneNames.Adventure);
                var adventure = Find<AdventureController>();
                for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
                var encounter = Find<EncounterCoordinator>();
                encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
                Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
                var session = Get<CombatSession>(encounter, "session");
                var spawned = Get<AdventureSpawnSet>(encounter, "spawned");
                var provider = Find<Static3DCombatUnitPresentationProvider>();
                var recorder = new R4RecordingPort(provider, session);
                Set(encounter, "unitPresentation", recorder);
                yield return SetGameSize(size);
                Assert.That(spawned.Enemies.Count, Is.EqualTo(2));
                Assert.That(spawned.EnemyDataList.Select(data => data.enemyId),
                    Is.EqualTo(new[] { "enemy_shijiahou", "enemy_lieshijia" }));
                Assert.That(spawned.Player.MaximumHealth, Is.EqualTo(571));
                Assert.That(spawned.Player.PhysicalAttack, Is.EqualTo(60));
                CombatantSnapshot armadillo = spawned.Enemies[0];
                Assert.That(armadillo.MaximumHealth, Is.EqualTo(329)); Assert.That(armadillo.PhysicalAttack, Is.EqualTo(39));
                Assert.That(armadillo.ActionPointMaximumUnits, Is.EqualTo(50000));
                Assert.That(armadillo.MovementEfficiencyMilliUnits, Is.EqualTo(6000));
                Assert.That(armadillo.Speed, Is.EqualTo(3));
                CombatantSnapshot spitter = spawned.Enemies[1];
                Assert.That(spitter.IsAlive, Is.True);
                Assert.That(spitter.MaximumHealth, Is.GreaterThan(0));
                Assert.That(spitter.MaximumHealth, Is.LessThan(armadillo.MaximumHealth), "The ranged beast is the lighter species.");
                Assert.That(spitter.ActionPointMaximumUnits, Is.EqualTo(50000));
                Assert.That(spitter.EquippedArtProfileIds, Does.Contain("spell_lieshi"));
                Assert.That(session.AttackProfiles.Any(profile => profile.Id == "spell_lieshi"), Is.True,
                    "The scene attackProfile array must include the imported ranged art.");
                Assert.That(spitter.Speed, Is.GreaterThan(armadillo.Speed), "The ranged beast acts more often.");
                File.WriteAllText(Path.Combine(output, "runtime-effective-" + size.x + ".json"), JsonUtility.ToJson(spawned.EnemyStats, true));
                yield return Capture("opening-" + size.x, output);
                var legal = new CombatLegalActionService();
                bool contactCaptured = false, attackCaptured = false;
                int commands = 0;
                float deadline = Time.realtimeSinceStartup + 180f;
                while (encounter != null && encounter.IsRunning && Time.realtimeSinceStartup < deadline && commands < 150)
                {
                    if (encounter.IsPresenting || session.CurrentActorId != "player" ||
                        !Get<bool>(encounter, "acceptsPlayerCommand") || session.TurnPhase == CombatTurnPhase.Finished)
                    { yield return null; continue; }
                    var options = legal.GetLegalActions(session, "player");
                    var alive = spawned.Enemies.Where(e => e.IsAlive).ToArray();
                    if (alive.Length == 0) { yield return null; continue; }
                    double distance = alive.Min(e => ContactDistance(session, spawned.Player.Pose, e.Pose));
                    if (distance == 1 && !contactCaptured)
                    { yield return Capture("contact-" + size.x, output); contactCaptured = true; }
                    bool enemyAttacked = recorder.Samples.Any(e => e.actor != "player" && e.action == "Attack");
                    if (enemyAttacked && !attackCaptured)
                    { yield return Capture("after-enemy-attack-" + size.x, output); attackCaptured = true; }
                    // Allow a real enemy turn in contact before fighting back; no HP/AP/position injection.
                    CombatCommand chosen = distance == 1 && !enemyAttacked ?
                        options.FirstOrDefault(c => c.Kind == CombatCommandKind.Guard) :
                        options.FirstOrDefault(c => c.Kind == CombatCommandKind.Art && c.ProfileId == "spell_guanshiji") ??
                        options.FirstOrDefault(c => c.Kind == CombatCommandKind.BasicAttack);
                    if (chosen == null && distance > 1)
                        chosen = options.Where(c => c.Kind == CombatCommandKind.Move &&
                                alive.Min(e => ContactDistance(session, spawned.Player.Pose.WithAnchor(c.Destination.Value), e.Pose)) < distance)
                            .OrderBy(c => alive.Min(e => ContactDistance(session, spawned.Player.Pose.WithAnchor(c.Destination.Value), e.Pose)))
                            .ThenBy(c => spawned.Player.Position.Distance(c.Destination.Value)).FirstOrDefault();
                    chosen = chosen ?? options.FirstOrDefault(c => c.Kind == CombatCommandKind.EndTurn) ??
                        options.First(c => c.Kind == CombatCommandKind.Wait);
                    if (chosen.Kind == CombatCommandKind.Move)
                        encounter.RequestMove("player", chosen.Destination.Value.Q, chosen.Destination.Value.R);
                    else if (chosen.Kind == CombatCommandKind.Guard) encounter.RequestGuard("player");
                    else if (chosen.Kind == CombatCommandKind.EndTurn) encounter.RequestEndTurn("player");
                    else if (chosen.Kind == CombatCommandKind.Wait) encounter.RequestWait("player");
                    else
                    {
                        var candidate = new CombatActionCandidate(chosen.Kind == CombatCommandKind.Art ? CombatActionKind.Art : CombatActionKind.BasicAttack,
                            "player", chosen.TargetId, chosen.ProfileId, Get<long>(encounter, "commandContextVersion"), attackDirection: chosen.AttackDirection);
                        encounter.PreviewAction(candidate);
                        encounter.ConfirmAction(candidate);
                    }
                    commands++;
                    yield return null;
                }
                File.WriteAllText(Path.Combine(output, "actions-" + size.x + ".json"), JsonUtility.ToJson(recorder, true));
                Assert.That(spawned.Enemies.All(e => !e.IsAlive), Is.True, "Formal two-enemy fight must reach victory within the bounded playthrough.");
                Assert.That(spawned.Player.IsAlive, Is.True);
                Assert.That(contactCaptured && attackCaptured, Is.True);
                var moves = recorder.Samples.Where(e => e.actor != "player" && e.action == "Move").ToArray();
                Assert.That(moves, Is.Not.Empty);
                Assert.That(moves.All(e => e.ap == 0), Is.True, "A normal one-cell enemy move spends all 50 AP.");
                var attacks = recorder.Samples.Where(e => e.actor != "player" && e.action == "Attack").ToArray();
                Assert.That(attacks, Is.Not.Empty);
                Assert.That(attacks.All(e => e.ap == 25000 || e.ap == 0), Is.True);
                Assert.That(attacks.GroupBy(e => new { e.actor, e.turn }).All(g => g.Count() <= 2), Is.True);
                yield return SceneReady(GameplaySceneNames.Settlement);
                yield return Capture("returned-" + size.x, output);
                TestContext.WriteLine($"R4 {size}: commands={commands}, HP={spawned.Player.CurrentHealth}/571, enemyMoves={moves.Length}, enemyAttacks={attacks.Length}, tick={session.TurnScheduler.CurrentTick}; production AI, seed=1729; no altered combat stats.");
            }
        }

        // S4: the selection ring is measured from the tile body only. Units and obstacles share
        // the cell root, so a formal rock must never lift its cell's border off the ground.
        [UnityTest]
        public IEnumerator SelectionBordersStayOnTileBodyAroundFormalRocksAtBothResolutions()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/s4-evidence"));
            Directory.CreateDirectory(output);
            yield return EnterSettlement();
            Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            var encounter = Find<EncounterCoordinator>();
            var session = Get<CombatSession>(encounter, "session");
            var spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            var movement = Find<CombatMovementView>();
            var bar = Find<CombatActionBarView>();
            var presenter = Find<CombatHudPresenter>();
            var input = Find<CombatCommandInput>();
            Camera camera = Get<Camera>(movement, "battlefieldCamera");
            yield return PlayerTurn(encounter, session);

            var rocks = new[] { new HexCoord(2, 1), new HexCoord(3, 1), new HexCoord(2, 4), new HexCoord(3, 4) };
            for (int step = 0; step < 3 && NearestRock(spawned.Player.Position, rocks) > 1; step++)
            {
                CombatHudSnapshot snapshot = presenter.Snapshot;
                Assert.That(snapshot.Movement, Is.Not.Null, "The player turn must quote movement options.");
                CombatMovementOption option = snapshot.Movement.Options
                    .FirstOrDefault(candidate => rocks.Any(rock => rock.Distance(ToHex(candidate.Destination)) == 1))
                    ?? snapshot.Movement.Options.OrderBy(candidate => rocks.Min(rock => rock.Distance(ToHex(candidate.Destination)))).FirstOrDefault();
                Assert.That(option, Is.Not.Null, "A legal step toward the formal rocks must exist.");
                int before = spawned.Player.ActionPointsRemainingUnits;
                encounter.RequestMove("player", option.Destination.Q, option.Destination.R);
                for (float deadline = Time.realtimeSinceStartup + 15f; encounter.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
                Assert.That(spawned.Player.Position, Is.EqualTo(ToHex(option.Destination)), "The production move must land on the quoted cell.");
                Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.LessThan(before));
                yield return PlayerTurn(encounter, session);
            }
            Assert.That(NearestRock(spawned.Player.Position, rocks), Is.LessThanOrEqualTo(1),
                "The player must stand next to a formal rock before the art range can include it: " + spawned.Player.Position);

            foreach (Vector2Int size in Sizes())
            {
                yield return SetGameSize(size);
                var lines = new List<string>
                {
                    Header(),
                    "player=" + spawned.Player.Position + "; ap=" + spawned.Player.ActionPointsRemainingUnits +
                    "; spirit=" + spawned.Player.CurrentSpirit + "; turn=" + session.CurrentActorId + "/" + session.TurnPhase,
                };
                foreach (var pair in movement.Highlights.OrderBy(item => item.Key.x).ThenBy(item => item.Key.y))
                {
                    Transform tile = GameObject.Find("GuanzhongHex_" + pair.Key.x + "_" + pair.Key.y).transform;
                    float bodyTop = TileBodyTop(tile);
                    float ringY = pair.Value.GetPosition(0).y;
                    Assert.That(ringY, Is.EqualTo(bodyTop + .01f).Within(.001f),
                        "Border Y must be measured from the tile body only: " + pair.Key);
                    Transform rock = tile.Find("GuanzhongObstacle_" + pair.Key.x + "_" + pair.Key.y);
                    if (rock == null) continue;
                    float rockTop = rock.GetComponentsInChildren<Renderer>().Max(renderer => renderer.bounds.max.y);
                    Assert.That(ringY, Is.LessThan(rockTop - .3f),
                        "The formal rock must not raise the border: " + pair.Key);
                    lines.Add($"rock={pair.Key.x},{pair.Key.y}; tileBodyTop={bodyTop:F4}; ringY={ringY:F4}; offset={ringY - bodyTop:F4}; rockTop={rockTop:F4}; ringBelowRockTop={rockTop - ringY:F4}");
                }

                Button art = new[] { bar.ArtButton, bar.SecondArtButton }
                    .FirstOrDefault(button => button != null && button.gameObject.activeInHierarchy && button.interactable);
                Assert.That(art, Is.Not.Null, "An executable art is required to open target selection; ap=" +
                    spawned.Player.ActionPointsRemainingUnits + "; spirit=" + spawned.Player.CurrentSpirit +
                    "; art=" + bar.ArtButton.interactable + "; second=" + bar.SecondArtButton.interactable +
                    "; status=" + Get<Text>(bar, "actionStatusLabel")?.text);
                yield return Pointer(new Vector2(-50, -50), false);
                art.onClick.Invoke();
                yield return null; yield return null;
                Assert.That(input.IsSelectingAttack, Is.True, "Art selection must open without a preselected target.");
                HexCoord[] active = movement.Highlights.Where(item => item.Value.gameObject.activeSelf)
                    .Select(item => new HexCoord(item.Key.x, item.Key.y)).ToArray();
                GuanzhongCombatTargetSelectionPlayModeTests.AssertSpellHighlights(session);
                Assert.That(active.Any(cell => rocks.Contains(cell)), Is.False, "Empty rock cells are not selectable unit targets.");
                lines.Add("artSelectableCells=" + string.Join("/", active.OrderBy(cell => cell.Q).ThenBy(cell => cell.R).Select(cell => cell.Q + "," + cell.R)));
                lines.Add("artButton=" + art.name + "; label=" + art.GetComponentInChildren<Text>().text.Replace("\n", " "));
                yield return Capture("s4-art-range-rock-" + size.x, output);
                input.CancelCandidate();
                yield return null;

                bar.MoveButton.onClick.Invoke();
                yield return null; yield return null;
                Assert.That(movement.IsSelecting, Is.True, "Movement selection must open for the range capture.");
                CombatMovementOption hovered = null;
                Vector2 hoverPoint = default;
                foreach (CombatMovementOption candidate in presenter.Snapshot.Movement.Options
                    .OrderBy(option => rocks.Min(rock => rock.Distance(ToHex(option.Destination)))))
                {
                    if (!TryPointAtCell(movement, camera, ToHex(candidate.Destination), out Vector2 point)) continue;
                    hovered = candidate;
                    hoverPoint = point;
                    break;
                }
                Assert.That(hovered, Is.Not.Null, "A clickable movement destination must be visible on screen.");
                yield return Pointer(hoverPoint, false);
                yield return null;
                Assert.That(movement.Selected, Is.Not.Null, "Hovering a legal cell must preview its path.");
                Assert.That(movement.Selected.Destination.Q, Is.EqualTo(hovered.Destination.Q));
                Assert.That(movement.Selected.Destination.R, Is.EqualTo(hovered.Destination.R));
                lines.Add("movementHover=" + hovered.Destination.Q + "," + hovered.Destination.R +
                    "; path=" + string.Join("/", hovered.Path.Select(cell => cell.Q + "," + cell.R)) +
                    "; cost=" + hovered.Cost + "; activeRings=" + movement.Highlights.Count(item => item.Value.gameObject.activeSelf));
                yield return Capture("s4-move-range-rock-" + size.x, output);
                movement.Cancel();
                yield return null;
                File.WriteAllText(Path.Combine(output, "highlight-height-" + size.x + ".txt"), string.Join("\n", lines));
                TestContext.WriteLine("S4 " + size.x + ": " + string.Join(" | ", lines));
            }
        }

        [Serializable]
        private sealed class R4Sample
        {
            public string actor, action;
            public long turn;
            public int frame, tick, ap, hp, q, r;
        }

        [Serializable]
        private sealed class R4RecordingPort : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly ICombatUnitPresentationPort actual;
            private readonly CombatSession session;
            public List<R4Sample> Samples = new List<R4Sample>();
            public R4RecordingPort(ICombatUnitPresentationPort actual, CombatSession session) { this.actual = actual; this.session = session; }
            public bool IsPresenting => ((ICombatPresentationActivity)actual).IsPresenting;
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) => actual.Prepare(values);
            public void Spawn(CombatUnitPresentationDescriptor value) => actual.Spawn(value);
            public void Present(CombatUnitPresentationEventProjection value)
            {
                if (session.Combatants.TryGet(value.ActorCombatantId, out var actor))
                    Samples.Add(new R4Sample { actor = actor.Id, action = value.PresentationEvent.ToString(),
                        turn = session.TurnSequence, frame = Time.frameCount, tick = session.TurnScheduler.CurrentTick,
                        ap = actor.ActionPointsRemainingUnits, hp = actor.CurrentHealth, q = actor.Position.Q, r = actor.Position.R });
                actual.Present(value);
            }
            public void Remove(string id) => actual.Remove(id);
            public void Clear() => actual.Clear();
        }

        [UnityTest]
        public IEnumerator PlayerFiveHexArtFiresOnlyInsideItsRange()
        {
            yield return EnterSettlement();
            Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            yield return SetGameSize(new Vector2Int(1920, 1080));

            var encounter = Find<EncounterCoordinator>();
            CombatSession session = Get<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            CombatantSnapshot player = spawned.Player;
            CombatantSnapshot enemy = spawned.Enemy;
            Assert.That(player.EquippedArtProfileIds, Does.Contain("spell_feixingzhi"));
            var hud = Find<CombatHudPresenter>().Snapshot;
            Assert.That(hud.ArtActions.Count, Is.EqualTo(3), "The action bar must present every equipped art.");
            Assert.That(hud.ArtActions[2].DisplayName, Is.EqualTo("飞星指"));
            Assert.That(hud.ArtActions[2].MaximumRange, Is.EqualTo(5));

            var service = new CombatCommandService();
            CombatCommand art = new CombatCommand(CombatCommandKind.Art, player.Id, enemy.Id, "spell_feixingzhi");
            var distant = service.Preview(session, art);
            Assert.That(distant.IsLegal, Is.False, "The enemy starts beyond five hexes.");
            Assert.That(distant.RejectionReason, Is.EqualTo("above_max_range"));

            double firedAt = -1;
            int apBefore = 0, healthBefore = 0;
            for (int turn = 0; turn < 12 && encounter.IsRunning && firedAt < 0; turn++)
            {
                for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline;)
                {
                    if (session.CurrentActorId == player.Id && session.TurnPhase == CombatTurnPhase.Active && !encounter.IsPresenting) break;
                    yield return null;
                }
                if (session.CurrentActorId != player.Id || session.TurnPhase != CombatTurnPhase.Active) continue;
                var now = service.Preview(session, art);
                if (now.IsLegal)
                {
                    firedAt = ContactDistance(session, player.Pose, enemy.Pose);
                    var candidate = new CombatActionCandidate(CombatActionKind.Art, player.Id, enemy.Id, "spell_feixingzhi",
                        Find<CombatHudPresenter>().Snapshot.CommandContextVersion);
                    CombatActionPreviewSnapshot preview = encounter.PreviewAction(candidate);
                    Assert.That(preview.CanConfirm, Is.True, preview.Reason);
                    apBefore = player.ActionPointsRemainingUnits;
                    healthBefore = enemy.CurrentHealth;
                    encounter.ConfirmAction(preview.Candidate);
                    Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(apBefore - now.ActionPointCostUnits));
                    break;
                }
                CombatCommand step = new CombatLegalActionService().GetLegalActions(session, player.Id)
                    .Where(action => action.Kind == CombatCommandKind.Move && action.Destination.HasValue)
                    .Where(action => ContactDistance(session, player.Pose.WithAnchor(action.Destination.Value), enemy.Pose) <
                        ContactDistance(session, player.Pose, enemy.Pose))
                    .OrderBy(action => ContactDistance(session, player.Pose.WithAnchor(action.Destination.Value), enemy.Pose))
                    .ThenByDescending(action => action.Destination.Value.Distance(player.Position))
                    .FirstOrDefault();
                if (step != null)
                {
                    encounter.RequestMove(player.Id, step.Destination.Value.Q, step.Destination.Value.R);
                    for (float deadline = Time.realtimeSinceStartup + 15f; encounter.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
                    Assert.That(encounter.IsPresenting, Is.False);
                }
                if (session.CurrentActorId == player.Id && session.TurnPhase != CombatTurnPhase.Finished && !encounter.IsPresenting)
                    encounter.RequestEndTurn(player.Id);
                yield return null;
            }

            Assert.That(firedAt, Is.InRange(2, 5), "The five-hex art must fire before contact, not only when adjacent.");
            TestContext.WriteLine("FEIXINGZHI fired at distance=" + firedAt + "; AP " + apBefore + "->" + player.ActionPointsRemainingUnits +
                "; enemy HP " + healthBefore + "->" + enemy.CurrentHealth);
            var lines = new List<string>
            {
                Header(),
                $"arts={string.Join(",", player.EquippedArtProfileIds)}; artCount={hud.ArtActions.Count}; third={hud.ArtActions[2].DisplayName}; range=1-{hud.ArtActions[2].MaximumRange}",
                $"distantRejection={distant.RejectionReason}; firedAt={firedAt}; damage={healthBefore - enemy.CurrentHealth}; enemy={enemy.Id}@{enemy.Position}",
            };
            Write("feixingzhi-range", lines);
            yield return Capture("feixingzhi-range");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator EnvironmentLayersCoverTheInitialViewAndPanCornersAtBothResolutions()
        {
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
                yield return SceneReady(GameplaySceneNames.Adventure);
                var adventure = Find<AdventureController>();
                for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
                adventure.SetEncounterRandomSource(new EncounterRandom());
                Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
                for (int frame = 0; frame < 300 && Find<CombatHudPresenter>().Snapshot == null; frame++) yield return null;
                Assert.That(Find<CombatHudPresenter>().Snapshot, Is.Not.Null, "战斗 HUD 必须已呈现。");
                yield return Capture("env-initial-" + size.x);

                var lines = new List<string> { Header() };
                GameObject environment = GameObject.Find("GuanzhongEnvironment");
                Assert.That(environment, Is.Not.Null, "环境层根必须存在。");
                Assert.That(environment.GetComponentsInChildren<Collider>().Length, Is.Zero, "环境层不得拦截棋盘点击。");
                GameObject battlefield = GameObject.Find("GuanzhongBattlefield");
                Assert.That(environment.transform.IsChildOf(battlefield.transform), Is.False,
                    "环境层不能挂在战场下，否则会扩大自动取景与平移边界。");
                Camera camera = Camera.main;
                Assert.That(environment.transform.parent, Is.EqualTo(camera.transform),
                    "环境层必须像天空盒一样跟随镜头。");
                foreach (string layer in new[] { "Environment_DistantCanyon", "Environment_ForegroundLeft", "Environment_ForegroundRight", "Environment_ValleyMist" })
                {
                    Transform node = environment.transform.Find(layer);
                    Assert.That(node, Is.Not.Null, "缺少环境层：" + layer);
                    Renderer renderer = node.GetComponent<Renderer>();
                    Assert.That(renderer, Is.Not.Null);
                    Assert.That(renderer.enabled, Is.True, layer + " 必须可见。");
                    lines.Add($"{layer}: local={node.localPosition:F3}; scale={node.localScale:F3}; " +
                        $"shader={renderer.sharedMaterial.shader.name}; texture={renderer.sharedMaterial.mainTexture.name}");
                }

                Dictionary<string, Rect> locked = LayerScreenRects(environment, camera);
                lines.Add("locked=" + string.Join("; ", locked.Select(pair => pair.Key + "@" + pair.Value)));
                Bounds board = BattlefieldRenderBounds(battlefield);
                float groundY = battlefield.transform.GetChild(0).position.y;
                foreach (var corner in new[]
                {
                    ("sw", board.min.x, board.min.z), ("se", board.max.x, board.min.z),
                    ("nw", board.min.x, board.max.z), ("ne", board.max.x, board.max.z),
                })
                {
                    MoveCameraFocus(camera, new Vector3(corner.Item2, groundY, corner.Item3));
                    yield return null;
                    yield return null;
                    Dictionary<string, Rect> after = LayerScreenRects(environment, camera);
                    foreach (var pair in locked)
                    {
                        Rect now = after[pair.Key];
                        Assert.That(Mathf.Abs(now.x - pair.Value.x), Is.LessThan(1f), pair.Key + " 平移后水平漂移");
                        Assert.That(Mathf.Abs(now.y - pair.Value.y), Is.LessThan(1f), pair.Key + " 平移后垂直漂移");
                        Assert.That(Mathf.Abs(now.width - pair.Value.width), Is.LessThan(1f), pair.Key + " 平移后宽度变化");
                        Assert.That(Mathf.Abs(now.height - pair.Value.height), Is.LessThan(1f), pair.Key + " 平移后高度变化");
                    }
                    lines.Add($"pan {corner.Item1}: camera={camera.transform.position:F3}; lockedRectsStable=true");
                    yield return Capture($"env-pan-{corner.Item1}-{size.x}");
                }
                Write("environment-" + size.x, lines);
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LightingAndGroundingFollowProductionMovesAtBothResolutions()
        {
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/v3-lighting"));
            Directory.CreateDirectory(output);
            foreach (Vector2Int size in Sizes())
            {
                yield return EnterSettlement();
                yield return SetGameSize(size);
                Assert.That(Find<SettlementController>().EnterAdventure("guanzhong_wild"), Is.True);
                yield return SceneReady(GameplaySceneNames.Adventure);
                var adventure = Find<AdventureController>();
                for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
                adventure.SetEncounterRandomSource(new EncounterRandom());
                Assert.That(Find<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
                var encounter = Find<EncounterCoordinator>();
                var session = Get<CombatSession>(encounter, "session");
                var spawned = Get<AdventureSpawnSet>(encounter, "spawned");
                yield return PlayerInputReady(encounter, session);
                var lines = new List<string> { Header(), "input=production EncounterCoordinator.RequestMove; no position/AP injection" };
                Light key = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Single(light => light.type == LightType.Directional);
                lines.Add($"light: rotation={key.transform.eulerAngles:F3}; color={key.color}; intensity={key.intensity}; shadows={key.shadows}; strength={key.shadowStrength}; bias={key.shadowBias}; normalBias={key.shadowNormalBias}");
                Vector3 boardCenter = BattlefieldRenderBounds(GameObject.Find("GuanzhongBattlefield")).center;
                Vector3 screenToLight = Camera.main.WorldToScreenPoint(boardCenter - key.transform.forward) - Camera.main.WorldToScreenPoint(boardCenter);
                Assert.That(screenToLight.x, Is.LessThan(0), "主光应来自画面左上方，与背景太阳一致。");
                Assert.That(screenToLight.y, Is.GreaterThan(0));
                lines.Add($"screenToLight={screenToLight:F3}");
                lines.Add($"ambient: mode={RenderSettings.ambientMode}; sky={RenderSettings.ambientSkyColor}; equator={RenderSettings.ambientEquatorColor}; ground={RenderSettings.ambientGroundColor}; intensity={RenderSettings.ambientIntensity}");
                Vector3[] probeDirections = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
                var probeColors = new Color[probeDirections.Length];
                RenderSettings.ambientProbe.Evaluate(probeDirections, probeColors);
                lines.Add("ambientProbe linear [up,down,left,right,forward,back]=" + string.Join(";", probeColors.Select(color => color.ToString("F4"))));
                yield return Capture("after-" + size.x, output);
                HexCoord[] targets = { new HexCoord(2, 0), new HexCoord(1, 2), new HexCoord(2, 3) };
                for (int step = 0; step < targets.Length; step++)
                {
                    yield return PlayerInputReady(encounter, session);
                    CombatMovementOption option = Find<CombatHudPresenter>().Snapshot.Movement.Options
                        .OrderBy(candidate => ToHex(candidate.Destination).Distance(targets[step]))
                        .ThenBy(candidate => candidate.Cost).First();
                    var command = new CombatCommand(CombatCommandKind.Move, "player", destination: ToHex(option.Destination));
                    var quote = new CombatCommandService().Preview(session, command);
                    Assert.That(quote.IsLegal, Is.True, quote.RejectionReason);
                    int before = spawned.Player.ActionPointsRemainingUnits;
                    HexCoord from = spawned.Player.Position;
                    encounter.RequestMove("player", option.Destination.Q, option.Destination.R);
                    Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.EqualTo(before - quote.ActionPointCostUnits));
                    if (step == 1) yield return Capture("moving-" + size.x, output);
                    for (float deadline = Time.realtimeSinceStartup + 15f; encounter.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
                    Assert.That(encounter.IsPresenting, Is.False);
                    Assert.That(spawned.Player.Position, Is.EqualTo(ToHex(option.Destination)));
                    Transform player = GameObject.Find("Static3DCombatUnit_player").transform;
                    Vector3 expected = Static3DCombatUnitPresentationProvider.HexToWorld(option.Destination);
                    Assert.That(Vector3.Distance(player.position, expected), Is.LessThan(0.001f));
                    lines.Add($"move {step + 1}: {from}->{spawned.Player.Position}; AP={before}->{before - quote.ActionPointCostUnits}; world={player.position:F4}; expected={expected:F4}");
                    yield return Capture($"move-{step + 1}-{size.x}", output);
                    if (step < targets.Length - 1)
                    {
                        yield return PlayerInputReady(encounter, session);
                        encounter.RequestEndTurn("player");
                        yield return null;
                    }
                }
                foreach (string id in new[] { "player", "enemy", "enemy_2" })
                {
                    Transform unit = GameObject.Find("Static3DCombatUnit_" + id).transform;
                    foreach (Renderer renderer in unit.GetComponentsInChildren<Renderer>())
                        lines.Add($"{PathOf(renderer.transform)}: min={renderer.bounds.min:F4}; max={renderer.bounds.max:F4}; cast={renderer.shadowCastingMode}; receive={renderer.receiveShadows}; materials=" +
                            string.Join(",", renderer.sharedMaterials.Select(material => material.name + "/" + material.shader.name)));
                }
                File.WriteAllLines(Path.Combine(output, "runtime-" + size.x + ".txt"), lines);
            }
        }

        private static Bounds BattlefieldRenderBounds(GameObject battlefield)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (Renderer renderer in battlefield.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(any, Is.True, "战场必须有可见几何。");
            return bounds;
        }

        private static Dictionary<string, Rect> LayerScreenRects(GameObject environment, Camera camera)
        {
            var result = new Dictionary<string, Rect>();
            foreach (string name in new[] { "Environment_DistantCanyon", "Environment_ForegroundLeft", "Environment_ForegroundRight", "Environment_ValleyMist" })
            {
                Bounds bounds = environment.transform.Find(name).GetComponent<Renderer>().bounds;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (int corner = 0; corner < 8; corner++)
                {
                    var world = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector2 point = camera.WorldToScreenPoint(world);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                result[name] = new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
            }
            return result;
        }

        private static void MoveCameraFocus(Camera camera, Vector3 target)
        {
            var plane = new Plane(Vector3.up, new Vector3(0f, target.y, 0f));
            Ray ray = camera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 focus = ray.GetPoint(distance);
            camera.transform.position += new Vector3(target.x - focus.x, 0f, target.z - focus.z);
        }

        private IEnumerator EnterSettlement()
        {
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            string directory = Path.Combine(Application.temporaryCachePath, "TianZhang-R2Evidence-" + Guid.NewGuid().ToString("N"));
            saveDirectories.Add(directory);
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(directory));
            var creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.That(creation.SelectOrigin("origin_minor_clan"), Is.True);
            creation.Submit("runtime-evidence", "运行时取证");
            yield return SceneReady(GameplaySceneNames.World);
            var world = Find<WorldMapController>();
            Assert.That(world.SelectNode("guanzhong_hub"), Is.True);
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
        }

        private static IEnumerator SetGameSize(Vector2Int size)
        {
#if UNITY_EDITOR
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            var value = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { Enum.ToObject(assembly.GetType("UnityEditor.GameViewSizeType"), 1), size.x, size.y, "R2 runtime evidence" }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { value });
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            var game = UnityEditor.EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));
            game.GetType().GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(game, count - 1);
#else
            Screen.SetResolution(size.x, size.y, false);
#endif
            for (int frame = 0; frame < 60 && (Screen.width != size.x || Screen.height != size.y); frame++) yield return null;
            Assert.That(new Vector2Int(Screen.width, Screen.height), Is.EqualTo(size), "Requested size must be the actual rendered size.");
            yield return null;
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator Pointer(Vector2 position, bool down)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(down ? 1 : 0) });
            yield return null; yield return null;
        }

        private static Vector2 ButtonCenter(Button button) => RectTransformUtility.WorldToScreenPoint(
            button.GetComponentInParent<Canvas>().worldCamera,
            button.transform.TransformPoint(((RectTransform)button.transform).rect.center));

        private IEnumerator Click(Button button)
        {
            Vector2 center = ButtonCenter(button);
            yield return Pointer(center, false);
            yield return Pointer(center, true);
            yield return Pointer(center, false);
        }

        private IEnumerator KeyPress(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        private static void AssertSprite(Button button, string state) =>
            Assert.That(button.image.overrideSprite.name, Is.EqualTo("Guanzhong_Button_" + state), button.name);

        private static void AssertButtons(Button accept, Button claim, bool canAccept, bool canClaim)
        {
            Assert.That(accept.interactable, Is.EqualTo(canAccept));
            Assert.That(claim.interactable, Is.EqualTo(canClaim));
            foreach (Button button in new[] { accept, claim })
                Assert.That(button.image.color, Is.EqualTo(button.interactable ? Color.white : button.colors.disabledColor), button.name);
        }

        private static void Observe(EventTrigger trigger, EventTriggerType type, Action action)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => action());
            trigger.triggers.Add(entry);
        }

        private static string ButtonState(string phase, params Button[] buttons) => phase + "; bounty=" + GameBootstrap.RequireRuntime().Bounties.GetState(BountyId).Status + "; " +
            string.Join("; ", buttons.Select(button => $"{PathOf(button.transform)}: interactable={button.interactable}, sprite={button.image.overrideSprite?.name}, rect={((RectTransform)button.transform).rect}, canvasScale={button.GetComponentInParent<Canvas>().scaleFactor}"));

        private static string RendererState(Renderer renderer, Camera camera)
        {
            Bounds bounds = renderer.bounds;
            var corners = new List<Vector3>();
            for (int i = 0; i < 8; i++) corners.Add(new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z));
            Vector3[] screen = corners.Select(camera.WorldToScreenPoint).ToArray();
            string top = "";
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Vector3[] vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                int[] triangles = filter.sharedMesh.triangles;
                var surface = new List<Vector3>();
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, Vector3.up) < .8f) continue;
                    surface.Add(a); surface.Add(b); surface.Add(c);
                }
                Assert.That(surface, Is.Not.Empty, "Expected upward mesh surface: " + PathOf(renderer.transform));
                var topBounds = new Bounds(surface[0], Vector3.zero);
                foreach (Vector3 vertex in surface) topBounds.Encapsulate(vertex);
                Vector3[] projected = surface.Select(camera.WorldToScreenPoint).ToArray();
                // Upward cliff ledges may be included: keep the raw transformed mesh so design can
                // identify the actual tile cap instead of treating this envelope as its footprint.
                string meshFile = PathOf(renderer.transform).Replace('/', '_') + "-" + Screen.width + ".json";
                File.WriteAllText(Path.Combine(OutputDirectory, meshFile), JsonUtility.ToJson(new MeshEvidence
                {
                    hierarchy = PathOf(renderer.transform), worldVertices = vertices,
                    screenVertices = vertices.Select(camera.WorldToScreenPoint).ToArray(), triangles = triangles
                }));
                top = $"; upwardSurfaceNormalDotMin=0.8; upwardWorldMin={topBounds.min:F5}; upwardWorldMax={topBounds.max:F5}; upwardScreenMin=({projected.Min(point => point.x):F3},{projected.Min(point => point.y):F3}); upwardScreenMax=({projected.Max(point => point.x):F3},{projected.Max(point => point.y):F3}); rawMesh={meshFile}";
            }
            return $"{PathOf(renderer.transform)}; enabled={renderer.enabled}; boundsMin={bounds.min:F5}; boundsMax={bounds.max:F5}; center={bounds.center:F5}; screenMin=({screen.Min(point => point.x):F3},{screen.Min(point => point.y):F3}); screenMax=({screen.Max(point => point.x):F3},{screen.Max(point => point.y):F3})" + top;
        }

        private static IEnumerator Capture(string label, string directory = null)
        {
#if UNITY_EDITOR
            // A fresh worktree compiles shader variants on first render. Never capture their cyan placeholder.
            for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.That(UnityEditor.ShaderUtil.anythingCompiling, Is.False, "Shader compilation must finish before visual evidence.");
            yield return null;
#endif
            string path = Path.Combine(directory ?? OutputDirectory, label + ".png");
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            for (int frame = 0; frame < 120 && !File.Exists(path); frame++) yield return null;
            Assert.That(File.Exists(path), Is.True, "Graphics capture did not produce " + path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(100));
        }

        private static void Write(string label, IEnumerable<string> lines)
        {
            string text = string.Join("\n", lines);
            File.WriteAllText(Path.Combine(OutputDirectory, label + ".txt"), text);
            TestContext.WriteLine(text);
        }
        private static string Header() => $"scene={SceneManager.GetActiveScene().name}; resolution={Screen.width}x{Screen.height}; project={Application.dataPath}; utc={DateTime.UtcNow:O}";
        private static Vector2Int[] Sizes() => new[] { new Vector2Int(1920, 1080), new Vector2Int(1366, 768) };

        private static IEnumerator PlayerTurn(EncounterCoordinator encounter, CombatSession session)
        {
            for (float deadline = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || encounter.IsPresenting);) yield return null;
            yield return null;
            Assert.That(session.CurrentActorId, Is.EqualTo("player"), "Expected a player turn.");
            Assert.That(encounter.IsPresenting, Is.False);
        }

        // The session already belongs to the player while the action-order playback still runs;
        // EncounterCoordinator.ExecutePlayer drops direct requests in that window instead of queuing
        // them. Wait for the same gate the production HUD uses before sending a command.
        private static IEnumerator PlayerInputReady(EncounterCoordinator encounter, CombatSession session, List<string> trace = null)
        {
            string entry = CoordinatorState(encounter, session);
            int frames = 0;
            for (float deadline = Time.realtimeSinceStartup + 30f; Time.realtimeSinceStartup < deadline; frames++)
            {
                if (encounter.AcceptsPlayerCommand && session.CurrentActorId == "player" &&
                    session.TurnPhase == CombatTurnPhase.Active)
                {
                    trace?.Add("inputReady frames=" + frames + "; entry={" + entry + "}; exit={" + CoordinatorState(encounter, session) + "}");
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Formal encounter did not accept player commands: " + CoordinatorState(encounter, session));
        }

        private static string CoordinatorState(EncounterCoordinator encounter, CombatSession session)
        {
            CombatHudSnapshot hud = Find<CombatHudPresenter>().Snapshot;
            return "presenting=" + encounter.IsPresenting + "; accepts=" + encounter.AcceptsPlayerCommand +
                "; actor=" + session.CurrentActorId + "; phase=" + session.TurnPhase + "; turn=" + session.TurnSequence +
                "; tick=" + session.TurnScheduler.CurrentTick + "; hudAccepts=" + hud?.AcceptsCommands +
                "; hudPresenting=" + hud?.IsPresenting;
        }

        /// <summary>Tile body top only: obstacles and units are parented under the same cell root.</summary>
        private static float TileBodyTop(Transform tile)
        {
            Transform body = tile.Find("MeasuredFbxAlignment");
            Renderer[] renderers = body != null
                ? body.GetComponentsInChildren<Renderer>()
                : tile.GetComponents<Renderer>();
            float top = tile.position.y;
            foreach (Renderer renderer in renderers) top = Mathf.Max(top, renderer.bounds.max.y);
            return top;
        }

        private static int NearestRock(HexCoord position, HexCoord[] rocks) => rocks.Min(rock => rock.Distance(position));

        private static HexCoord ToHex(CombatUnitPresentationHex hex) => new HexCoord(hex.Q, hex.R);

        private static bool TryPointAtCell(CombatMovementView movement, Camera camera, HexCoord cell, out Vector2 point)
        {
            Transform tile = GameObject.Find("GuanzhongHex_" + cell.Q + "_" + cell.R).transform;
            Vector3 center = camera.WorldToScreenPoint(tile.position);
            point = center;
            if (center.z <= 0) return false;
            for (int dx = -30; dx <= 30; dx += 10)
            for (int dy = -30; dy <= 30; dy += 10)
            {
                Vector2 candidate = new Vector2(center.x + dx, center.y + dy);
                if (!movement.TryPickCell(candidate, out CombatUnitPresentationHex picked)) continue;
                if (picked.Q != cell.Q || picked.R != cell.R) continue;
                point = candidate;
                return true;
            }
            return false;
        }

        private static string PathOf(Transform value) => value.parent == null ? value.name : PathOf(value.parent) + "/" + value.name;
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static IEnumerator SceneReady(string scene)
        {
            for (int frame = 0; frame < 180 && SceneManager.GetActiveScene().name != scene; frame++) yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(scene));
            yield return null;
        }
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        [Serializable]
        private sealed class MeshEvidence
        {
            public string hierarchy;
            public Vector3[] worldVertices;
            public Vector3[] screenVertices;
            public int[] triangles;
        }
    }
}
