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
    public sealed class GuanzhongMovementPlayModeTests
    {
        private const string SettlementId = "guanzhong_city", AdventureId = "guanzhong_wild", BountyId = "bounty_guanzhong_shijiahou";
        private string saveDirectory;
        private Mouse mouse;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior originalBackgroundBehavior;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode originalEditorInputBehavior;
#endif

        [SetUp]
        public void ConfigureInputTestFocus()
        {
            originalBackgroundBehavior = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            InputSystem.settings.backgroundBehavior = originalBackgroundBehavior;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorInputBehavior;
#endif
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator FormalSceneOpensMovePopupAndCommitsOncePerLegalClickAtBothResolutions()
        {
            GameBootstrap staleBootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (staleBootstrap != null) { Object.Destroy(staleBootstrap.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            GameBootstrap bootstrap = GameBootstrap.RequireInstance();
            saveDirectory = Path.Combine(Application.temporaryCachePath,
                "TianZhang-MoveRule-" + Guid.NewGuid().ToString("N"));
            Set(bootstrap, "slotStore", new GameSaveSlotStore(saveDirectory));
            CharacterCreationController creation = Find<CharacterCreationController>();
            creation.Open();
            creation.Draft.OriginId = "origin_minor_clan";
            creation.Draft.Innate = new InnateAttributeSet(15, 3, 6, 3, 3);
            creation.Submit("formal-move-rule", "移动规则验证");
            yield return SceneReady(GameplaySceneNames.World);
            WorldMapController world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            SettlementController settlement = Find<SettlementController>();
            BountyBoardView board = Find<BountyBoardView>();
            board.Show(Get<ContentCatalogData>(settlement, "contentCatalog"),
                SettlementId, GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept(BountyId);
            Assert.IsTrue(settlement.EnterAdventure(AdventureId));
            yield return SceneReady(GameplaySceneNames.Adventure);
            AdventureController adventure = Find<AdventureController>();
            yield return AdventureReady(adventure);
            adventure.SetEncounterRandomSource(new SequenceRandomSource(0, 0));
            EncounterCoordinator encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            CombatSession session = Get<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = Get<AdventureSpawnSet>(encounter, "spawned");
            CombatantSnapshot player = spawned.Player, enemy = spawned.Enemy;
            Static3DCombatUnitPresentationProvider provider = Find<Static3DCombatUnitPresentationProvider>();

            for (float deadline = Time.realtimeSinceStartup + 15f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || encounter.IsPresenting);) yield return null;
            Assert.AreEqual("player", session.CurrentActorId);
            CombatCommandInput commandInput = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatMovementView movement = Find<CombatMovementView>();
            CombatActionPreviewView preview = Find<CombatActionPreviewView>();
            Camera camera = Get<Camera>(movement, "battlefieldCamera");
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();

            string evidenceDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/movement-s3"));
            Directory.CreateDirectory(evidenceDirectory);

            foreach (int size in new[] { 1920, 1366 })
            {
                if (size == 1366) yield return ReadyForPlayerTurn(encounter, session);
                int height = size == 1920 ? 1080 : 768;
                GuanzhongCombatEquipmentPlayModeTests.SetGameSize(size, height);
                for (int frame = 0; frame < 8; frame++) yield return null;
                Canvas.ForceUpdateCanvases();
#if UNITY_EDITOR
                for (float deadline = Time.realtimeSinceStartup + 120f; UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
                Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling, "Shader compilation must finish before visual evidence.");
                yield return null;
#endif
                Assert.AreEqual(size, Screen.width);
                Assert.AreEqual(height, Screen.height);
                CombatHudSnapshot snapshot = Get<CombatHudSnapshot>(commandInput, "snapshot");
                Assert.NotNull(snapshot.Movement);
                Assert.IsFalse(movement.IsSelecting);
                Assert.IsFalse(preview.IsVisible);
                Assert.IsTrue(bar.MoveButton.interactable);
                HexCoord original = player.Position;
                int originalPoints = player.ActionPointsRemainingUnits;
                int originalTick = session.TurnScheduler.CurrentTick;
                int originalFacing = player.Facing;

                bar.MoveButton.onClick.Invoke();
                Assert.IsTrue(movement.IsSelecting, "Move must only open selection.");
                Assert.IsTrue(preview.IsVisible, "Movement must use the shared selection popup.");
                Assert.IsFalse(preview.ConfirmButton.gameObject.activeSelf, "Movement cannot keep a second confirmation entry.");
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);
                AssertReachableHighlights(movement, snapshot);
                Canvas.ForceUpdateCanvases();
                string summary = Get<Text>(preview, "summary").text;
                StringAssert.Contains("可达", summary);
                StringAssert.Contains("左键点击高亮地块立即移动", summary);

                var selected = FindVisibleMovementOption(movement, snapshot, camera);
                CombatMovementOption option = selected.option;
                Vector2 destinationPoint = selected.point;
                yield return Point(mouse, destinationPoint);
                Assert.IsTrue(movement.IsSelecting);
                Assert.AreSame(option, movement.Selected, "Hover must preview the authoritative option.");
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);
                Assert.IsTrue(Get<LineRenderer>(movement, "pathLine").gameObject.activeSelf, "Hover must draw the path.");
                StringAssert.Contains("消耗 " + CombatActionPreviewView.FormatActionPoints(option.Cost) + " AP",
                    Get<Text>(preview, "summary").text);
                StringAssert.Contains("路径 " + option.Path.Count + " 格", Get<Text>(preview, "summary").text);
                Assert.AreEqual(option.Path.Count + 1, Get<LineRenderer>(movement, "pathLine").positionCount);

                Vector2 popupPoint = PopupPoint(preview);
                // Right click and Escape must cancel even while the pointer is over the popup.
                yield return Click(mouse, popupPoint, MouseButton.Right);
                Assert.IsFalse(movement.IsSelecting, "Right click over UI must close movement selection.");
                Assert.IsFalse(preview.IsVisible);
                Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);
                Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);

                bar.MoveButton.onClick.Invoke();
                yield return Point(mouse, popupPoint);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
                yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                Assert.IsFalse(movement.IsSelecting, "Escape over UI must close movement selection.");
                Assert.IsFalse(preview.IsVisible);
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);

                // The popup's cancel button is the only remaining cancellation entry.
                bar.MoveButton.onClick.Invoke();
                Canvas.ForceUpdateCanvases();
                yield return Click(mouse, RectTransformUtility.WorldToScreenPoint(null, preview.CancelButton.transform.position),
                    MouseButton.Left);
                Assert.IsFalse(movement.IsSelecting, "The popup cancel button must close movement selection.");
                Assert.IsFalse(preview.IsVisible);
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);

                // Illegal board cells keep selection open and never charge AP.
                bar.MoveButton.onClick.Invoke();
                var legalKeys = new HashSet<Vector2Int>(snapshot.Movement.Options.Select(value => CellKey(value.Destination)));
                var centers = Get<Dictionary<Vector2Int, Vector3>>(movement, "centers");
                var testedIllegal = new HashSet<Vector2Int>();
                foreach (var ring in movement.Highlights)
                {
                    if (legalKeys.Contains(ring.Key)) continue;
                    if (!TryFindCellPoint(movement, camera, centers[ring.Key], ring.Key, out Vector2 point)) continue;
                    yield return Click(mouse, point, MouseButton.Left);
                    Assert.IsTrue(movement.IsSelecting, "Illegal click at " + ring.Key + " must keep selection open.");
                    Assert.IsTrue(preview.IsVisible);
                    Assert.IsNull(movement.Selected);
                    Assert.AreEqual(original, player.Position);
                    Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);
                    Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);
                    Assert.AreEqual(originalFacing, player.Facing);
                    testedIllegal.Add(ring.Key);
                }
                Assert.GreaterOrEqual(testedIllegal.Count, 3, "At least three visible illegal cells must be exercised.");
                Vector2Int enemyKey = CellKey(new CombatUnitPresentationHex(enemy.Position.Q, enemy.Position.R));
                Assert.Contains(enemyKey, testedIllegal.ToList(),
                    "The occupied enemy cell must reject the movement click.");
                Vector2Int[] rockKeys =
                {
                    new Vector2Int(2, 1), new Vector2Int(3, 1), new Vector2Int(2, 4), new Vector2Int(3, 4),
                };
                Assert.IsTrue(rockKeys.Any(testedIllegal.Contains),
                    "At least one visible formal rock cell must reject the movement click; tested=" +
                    string.Join("/", testedIllegal.Select(value => value.x + "," + value.y)));
                bool unreachableTested = testedIllegal.Any(key => !legalKeys.Contains(key) && key != enemyKey &&
                    !rockKeys.Contains(key));
                Assert.IsTrue(unreachableTested, "A visible out-of-range cell must reject the movement click.");
                StringAssert.Contains("不可到达", Get<Text>(preview, "summary").text);

                // UI and off-board clicks are inert as well.
                yield return Click(mouse, popupPoint, MouseButton.Left);
                Assert.IsTrue(movement.IsSelecting, "Clicking popup chrome must not move.");
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);
                Assert.IsTrue(TryFindOffBoardPoint(movement, camera, out Vector2 offBoard));
                yield return Click(mouse, offBoard, MouseButton.Left);
                Assert.IsTrue(movement.IsSelecting, "Clicking outside the board must not move.");
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.ActionPointsRemainingUnits);

                // A single left click on a highlighted cell commits exactly once.
                yield return Point(mouse, destinationPoint);
                Assert.AreSame(option, movement.Selected);
                string selectionEvidence = "scene=" + SceneManager.GetActiveScene().name +
                    "; screen=" + Screen.width + "x" + Screen.height + "; origin=" + original +
                    "; ap=" + originalPoints + "; options=" + snapshot.Movement.Options.Count +
                    "; destination=(" + option.Destination.Q + "," + option.Destination.R + "); cost=" + option.Cost +
                    "; path=" + string.Join("/", option.Path.Select(value => value.Q + "," + value.R)) +
                    "; popup=" + preview.IsVisible + "; confirm=" + preview.ConfirmButton.gameObject.activeSelf +
                    "; rings=" + movement.Highlights.Count(pair => pair.Value.gameObject.activeSelf) +
                    "; illegalTested=" + string.Join("/", testedIllegal.Select(value => value.x + "," + value.y)) +
                    "; summary=" + Get<Text>(preview, "summary").text.Replace("\n", "\\n");
                File.WriteAllText(Path.Combine(evidenceDirectory, "selection-" + size + ".txt"), selectionEvidence);
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceDirectory, "selection-" + size + ".png"));
                yield return null; yield return null;

                yield return Click(mouse, destinationPoint, MouseButton.Left);
                Assert.IsFalse(movement.IsSelecting, "A legal click must commit and close the popup immediately.");
                Assert.IsFalse(preview.IsVisible);
                Assert.IsNull(movement.Selected);
                Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
                Assert.AreEqual(option.Destination.Q, player.Position.Q);
                Assert.AreEqual(option.Destination.R, player.Position.R);
                Assert.AreEqual(originalPoints - option.Cost, player.ActionPointsRemainingUnits,
                    "One click may only spend the quoted AP once.");
                Assert.IsTrue(encounter.IsPresenting, "The committed move must enter its presentation lock.");
                Assert.IsFalse(bar.MoveButton.interactable);

                // Presentation/stale/duplicate submissions cannot apply the move or its AP again.
                string committed = State(encounter, session);
                movement.BeginSelection();
                Assert.IsFalse(movement.IsSelecting, "Selection cannot open during presentation.");
                CombatActionCandidate stale = option.Candidate;
                encounter.ConfirmAction(stale);
                yield return null;
                Assert.AreEqual(committed, State(encounter, session), "A stale movement candidate must be inert.");

                string commitEvidence = "scene=" + SceneManager.GetActiveScene().name +
                    "; screen=" + Screen.width + "x" + Screen.height + "; from=" + original + "; to=" + player.Position +
                    "; ap=" + originalPoints + "->" + player.ActionPointsRemainingUnits + "; cost=" + option.Cost +
                    "; presenting=" + encounter.IsPresenting + "; selecting=" + movement.IsSelecting +
                    "; popup=" + preview.IsVisible + "; rings=" + movement.Highlights.Count(pair => pair.Value.gameObject.activeSelf);
                File.WriteAllText(Path.Combine(evidenceDirectory, "committed-" + size + ".txt"), commitEvidence);
                ScreenCapture.CaptureScreenshot(Path.Combine(evidenceDirectory, "committed-" + size + ".png"));
                yield return ReadyForPlayerTurn(encounter, session);
                Assert.IsFalse(encounter.IsPresenting);
                Assert.IsFalse(movement.IsSelecting);
                Assert.IsFalse(preview.IsVisible);
                Assert.IsTrue(bar.MoveButton.interactable || player.ActionPointsRemainingUnits == 0);
                Assert.IsTrue(provider.TryGetPresentedUnit("player", out GameObject root));
                Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(option.Destination), root.transform.position);
            }

            // Turn change also clears an uncommitted move before the next authority projection.
            CombatHudSnapshot finalSnapshot = Get<CombatHudSnapshot>(commandInput, "snapshot");
            if (finalSnapshot.Movement != null && finalSnapshot.Movement.Options.Count > 0)
            {
                bar.MoveButton.onClick.Invoke();
                Assert.IsTrue(movement.IsSelecting);
                encounter.RequestEndTurn("player");
                Assert.IsFalse(movement.IsSelecting, "A turn change must clear the uncommitted movement selection.");
                Assert.IsFalse(preview.IsVisible);
                Assert.IsFalse(movement.Highlights.Values.Any(ring => ring.gameObject.activeSelf));
            }
            TestContext.WriteLine("S3 movement: popup, hover, single-click commit, cancellation over UI, illegal clicks, and stale rejection verified at 1920x1080 and 1366x768.");
        }

        private static IEnumerator ReadyForPlayerTurn(EncounterCoordinator encounter, CombatSession session)
        {
            for (float deadline = Time.realtimeSinceStartup + 20f; Time.realtimeSinceStartup < deadline &&
                (session.CurrentActorId != "player" || session.TurnPhase == CombatTurnPhase.Finished || encounter.IsPresenting);)
                yield return null;
            yield return null;
            Assert.AreEqual("player", session.CurrentActorId);
            Assert.IsFalse(encounter.IsPresenting);
        }

        private IEnumerator Point(Mouse device, Vector2 point)
        {
            InputSystem.QueueStateEvent(device, new MouseState { position = point });
            yield return null; yield return null;
        }

        private IEnumerator Click(Mouse device, Vector2 point, MouseButton button)
        {
            InputSystem.QueueStateEvent(device, new MouseState { position = point }.WithButton(button));
            yield return null; yield return null;
            InputSystem.QueueStateEvent(device, new MouseState { position = point });
            yield return null;
        }

        private static (CombatMovementOption option, Vector2 point) FindVisibleMovementOption(
            CombatMovementView movement, CombatHudSnapshot snapshot, Camera camera)
        {
            var centers = Get<Dictionary<Vector2Int, Vector3>>(movement, "centers");
            foreach (CombatMovementOption option in snapshot.Movement.Options)
            {
                Vector2 point = camera.WorldToScreenPoint(centers[CellKey(option.Destination)]);
                if (!OnScreen(point)) continue;
                if (!movement.TryPickCell(point, out CombatUnitPresentationHex picked) ||
                    !picked.Equals(option.Destination)) continue;
                return (option, point);
            }
            Assert.Fail("No visible, clickable movement option while the shared popup is open.");
            return (null, Vector2.zero);
        }

        private static bool TryFindCellPoint(CombatMovementView movement, Camera camera, Vector3 center,
            Vector2Int expected, out Vector2 point)
        {
            // The shared popup covers part of the board; a real obstacle click can still target an
            // exposed edge of the same hex. Stay well inside the 0.46-unit inradius.
            Vector3[] offsets =
            {
                Vector3.zero,
                new Vector3(0.30f, 0, 0), new Vector3(-0.30f, 0, 0),
                new Vector3(0, 0, 0.30f), new Vector3(0, 0, -0.30f),
                new Vector3(0.21f, 0, 0.21f), new Vector3(-0.21f, 0, 0.21f),
                new Vector3(0.21f, 0, -0.21f), new Vector3(-0.21f, 0, -0.21f),
            };
            foreach (Vector3 offset in offsets)
            {
                Vector2 candidate = camera.WorldToScreenPoint(center + offset);
                if (!OnScreen(candidate)) continue;
                if (!movement.TryPickCell(candidate, out CombatUnitPresentationHex picked) ||
                    CellKey(picked) != expected) continue;
                point = candidate;
                return true;
            }
            point = default;
            return false;
        }

        private static bool TryFindOffBoardPoint(CombatMovementView movement, Camera camera, out Vector2 point)
        {
            for (int row = 1; row < 10; row++)
            {
                for (int column = 1; column < 10; column++)
                {
                    var candidate = new Vector2(Screen.width * column / 10f, Screen.height * row / 10f);
                    if (PointerHitsUi(candidate)) continue;
                    if (movement.TryPickCell(candidate, out _)) continue;
                    point = candidate;
                    return true;
                }
            }
            point = Vector2.zero;
            return false;
        }

        private static bool PointerHitsUi(Vector2 point)
        {
            EventSystem system = EventSystem.current;
            if (system == null) return false;
            var hits = new List<RaycastResult>();
            system.RaycastAll(new PointerEventData(system) { position = point }, hits);
            return hits.Count > 0;
        }

        private static Vector2 PopupPoint(CombatActionPreviewView preview)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform rect = Get<GameObject>(preview, "root").GetComponent<RectTransform>();
            return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        }

        private static bool OnScreen(Vector2 point) =>
            point.x >= 0f && point.x < Screen.width && point.y >= 0f && point.y < Screen.height;

        private static string State(EncounterCoordinator encounter, CombatSession session)
        {
            bool presenting = encounter.IsPresenting;
            var player = session.Combatants.All.Single(unit => unit.Id == "player");
            return presenting + ":" + player.Position + ":" + player.ActionPointsRemainingUnits + ":" +
                player.Facing + ":" + session.TurnScheduler.CurrentTick + ":" + session.TurnPhase;
        }

#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/诊断/记录当前移动实机60秒")]
        public static void RecordVisibleMovement()
        {
            if (!Application.isPlaying || SceneManager.GetActiveScene().name != GameplaySceneNames.Adventure)
                throw new InvalidOperationException("movement_evidence_requires_formal_adventure_playmode");
            EncounterCoordinator encounter = Find<EncounterCoordinator>();
            CombatSession session = Get<CombatSession>(encounter, "session");
            if (session == null || Get<ICombatResolutionRandomSource>(encounter, "combatRandomSource").GetType() != typeof(SystemCombatResolutionRandomSource) ||
                Get<IFormalEncounterRandomSource>(Find<AdventureController>(), "randomSource").GetType() != typeof(SystemFormalEncounterRandomSource))
                throw new InvalidOperationException("movement_evidence_requires_live_production_sources");
            CombatMovementView movement = Find<CombatMovementView>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            Camera camera = Get<Camera>(movement, "battlefieldCamera");
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "move-ui"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "visible-movement-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 60d;
            string previous = null;
            UnityEditor.EditorApplication.CallbackFunction sample = null;
            sample = () =>
            {
                if (!Application.isPlaying || UnityEditor.EditorApplication.timeSinceStartup >= deadline)
                {
                    UnityEditor.EditorApplication.update -= sample;
                    return;
                }
                if (encounter == null)
                {
                    File.AppendAllText(file, JsonUtility.ToJson(new VisibleMovementEvidence
                    {
                        frame = Time.frameCount, time = Time.realtimeSinceStartup,
                        scene = SceneManager.GetActiveScene().name, root = "encounter_unloaded",
                        activeUnits = -1
                    }) + Environment.NewLine);
                    UnityEditor.EditorApplication.update -= sample;
                    return;
                }
                session.Combatants.TryGet("player", out CombatantSnapshot player);
                session.Combatants.TryGet("enemy", out CombatantSnapshot enemy);
                provider.TryGetPresentedUnit("player", out GameObject root);
                Text status = Get<Text>(bar, "movementLabel");
                var evidence = new VisibleMovementEvidence
                {
                    width = Screen.width, height = Screen.height, phase = session.TurnPhase.ToString(),
                    scene = SceneManager.GetActiveScene().name, actor = session.CurrentActorId,
                    tick = session.TurnScheduler.CurrentTick, points = player.ActionPointsRemainingUnits, q = player.Position.Q, r = player.Position.R,
                    hp = player.CurrentHealth, mp = player.CurrentSpirit, enemyHp = enemy.CurrentHealth,
                    busy = encounter.IsPresenting, running = encounter.IsRunning,
                    activeUnits = provider.ActiveCombatantCount, rootActive = root != null && root.activeInHierarchy,
                    selecting = movement.IsSelecting,
                    selected = movement.Selected == null ? "" : movement.Selected.Destination.ToString(),
                    root = root == null ? "removed" : $"position={root.transform.position:F5}; rotation={root.transform.eulerAngles:F3}; scale={root.transform.localScale:F3}; base={root.transform.Find("Static3DCombatBase").position:F5}",
                    status = $"{status.text}; rect={status.rectTransform.rect}; preferred={status.preferredWidth:F2}x{status.preferredHeight:F2}; vertices={status.cachedTextGenerator.vertexCount}; font={status.font.name}; canvas={status.canvas.renderMode}/{status.canvas.scaleFactor}",
                    camera = $"{camera.name}; position={camera.transform.position:F5}; size={camera.orthographicSize}; pixelRect={camera.pixelRect}",
                    controls = $"move={bar.MoveButton.interactable}; guard={bar.GuardButton.interactable}; end={bar.WaitButton.interactable}",
                    lines = movement.GetComponentsInChildren<LineRenderer>().Select(line =>
                    {
                        var mesh = new Mesh();
                        line.BakeMesh(mesh, camera, true);
                        Vector3[] vertices = mesh.vertices;
                        int[] triangles = mesh.triangles;
                        string faces = string.Join(",", Enumerable.Range(0, triangles.Length / 3).Select(i =>
                        {
                            Vector3 a = vertices[triangles[i * 3]], b = vertices[triangles[i * 3 + 1]], c = vertices[triangles[i * 3 + 2]];
                            return Vector3.Dot(Vector3.Cross(b - a, c - a).normalized, (camera.transform.position - a).normalized).ToString("F3");
                        }));
                        string value = line.name + ": bounds=" + line.bounds + "; enabled=" + line.enabled + "; visible=" + line.isVisible +
                            "; width=" + line.widthMultiplier + "/" + line.widthCurve.Evaluate(0) + "/" + line.widthCurve.Evaluate(.5f) + "/" + line.widthCurve.Evaluate(1) +
                            "; mesh=" + vertices.Length + "/" + triangles.Length + "; faces=" + faces +
                            "; material=" + line.sharedMaterial.name + "/" + line.sharedMaterial.shader.name + "; color=" + line.sharedMaterial.GetColor("_BaseColor") + "; points=" +
                            string.Join("|", Enumerable.Range(0, line.positionCount).Select(i => line.GetPosition(i).ToString("F5") + " screen=" + camera.WorldToScreenPoint(line.GetPosition(i)).ToString("F2")));
                        Object.DestroyImmediate(mesh);
                        return value;
                    }).ToArray(),
                    tiles = GameObject.Find("GuanzhongBattlefield").transform.Find("GuanzhongHex_0_1").GetComponentsInChildren<Renderer>()
                        .Select(renderer => renderer.name + ": " + renderer.bounds).ToArray()
                };
                string key = JsonUtility.ToJson(evidence);
                if (key == previous) return;
                previous = key;
                evidence.frame = Time.frameCount;
                evidence.time = Time.realtimeSinceStartup;
                File.AppendAllText(file, JsonUtility.ToJson(evidence) + Environment.NewLine);
                if (!encounter.IsRunning) UnityEditor.EditorApplication.update -= sample;
            };
            sample();
            UnityEditor.EditorApplication.update += sample;
            Debug.Log("Read-only live movement evidence (60 seconds): " + file);
        }

        [Serializable]
        private sealed class VisibleMovementEvidence
        {
            public int frame, width, height, tick, points, q, r, hp, mp, enemyHp, activeUnits;
            public float time;
            public bool busy, running, rootActive, selecting;
            public string scene, actor, phase, selected, root, status, camera, controls;
            public string[] lines, tiles;
        }
#endif

        private static void AssertReachableHighlights(CombatMovementView movement, CombatHudSnapshot snapshot)
        {
            var expected = snapshot.Movement.Options.Select(option => CellKey(option.Destination)).ToArray();
            CollectionAssert.AreEquivalent(expected,
                movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).Select(pair => pair.Key).ToArray());
            foreach (LineRenderer ring in movement.Highlights.Values.Where(line => line.gameObject.activeSelf))
                AssertLineColor(ring, new Color32(0x23, 0x8C, 0xFF, 0xFF));
        }

        private static void AssertLineColor(LineRenderer line, Color expected)
        {
            var properties = new MaterialPropertyBlock();
            line.GetPropertyBlock(properties);
            Color actual = properties.GetColor("_BaseColor");
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f), $"{line.name} final color={actual}");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static Vector2Int CellKey(CombatUnitPresentationHex hex) => new Vector2Int(hex.Q, hex.R);
        private static T Find<T>() where T : Component => SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).Single();
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static IEnumerator SceneReady(string scene)
        {
            for (int frame = 0; frame < 180 && SceneManager.GetActiveScene().name != scene; frame++) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private static IEnumerator AdventureReady(AdventureController owner)
        {
            for (int frame = 0; frame < 180 && owner.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, owner.CurrentState);
        }
        private sealed class SequenceRandomSource : IFormalEncounterRandomSource
        {
            private readonly Queue<int> values;
            public SequenceRandomSource(params int[] sequence) => values = new Queue<int>(sequence);
            public int NextPercent() => values.Count > 0 ? values.Dequeue() : 0;
        }
    }
}
