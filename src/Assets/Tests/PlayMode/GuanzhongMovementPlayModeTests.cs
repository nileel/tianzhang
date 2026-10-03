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
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return null;
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest]
        public IEnumerator FormalSceneProjectsAuthorityAndCommitsOnlyConfirmedMovement()
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

            for (int frame = 0; frame < 600 && (session.CurrentActorId != "player" || encounter.IsPresenting); frame++) yield return null;
            Assert.AreEqual("player", session.CurrentActorId);
            CombatCommandInput commandInput = Find<CombatCommandInput>();
            CombatActionBarView bar = Find<CombatActionBarView>();
            CombatMovementView movement = Find<CombatMovementView>();
            CombatHudSnapshot snapshot = Get<CombatHudSnapshot>(commandInput, "snapshot");
            Assert.NotNull(snapshot.Movement);
            var expected = session.SpatialQuery.FindReachable(player.Position, player.MovePoints, new[] { enemy.Position })
                .Keys.Where(hex => hex != player.Position).Select(hex => new Vector2Int(hex.Q, hex.R)).ToArray();
            CollectionAssert.AreEquivalent(expected, snapshot.Movement.Options.Select(option => Key(option.Destination)).ToArray());
            HexCoord original = player.Position;
            int originalPoints = player.MovePoints, originalTick = session.TurnScheduler.CurrentTick;
            bar.MoveButton.onClick.Invoke();
            Assert.IsTrue(movement.IsSelecting);
            CollectionAssert.AreEquivalent(expected, movement.Highlights.Where(pair => pair.Value.gameObject.activeSelf).Select(pair => pair.Key).ToArray());
            bar.CancelMoveButton.onClick.Invoke();
            Assert.IsFalse(movement.IsSelecting);
            Assert.AreEqual(original, player.Position);
            Assert.AreEqual(originalPoints, player.MovePoints);
            bar.MoveButton.onClick.Invoke();
            Camera camera = Get<Camera>(movement, "battlefieldCamera");
            var destination = new CombatUnitPresentationHex(0, 1);
            Vector3 worldPoint = Static3DCombatUnitPresentationProvider.HexToWorld(destination);
            Vector3 screenPoint = camera.WorldToScreenPoint(worldPoint);
            Assert.IsTrue(movement.TryPickCell(screenPoint, out CombatUnitPresentationHex picked));
            Assert.AreEqual(destination, picked);
            Vector3 originalCameraPosition = camera.transform.position;
            camera.transform.position += new Vector3(0.15f, 0f, 0.1f);
            screenPoint = camera.WorldToScreenPoint(worldPoint);
            Assert.IsTrue(movement.TryPickCell(screenPoint, out picked));
            Assert.AreEqual(destination, picked, "Picking must use the camera's current pose.");
            camera.transform.position = originalCameraPosition;
            yield return null;
            Canvas.ForceUpdateCanvases();
            RectTransform moveRect = bar.MoveButton.GetComponent<RectTransform>();
            Vector2 buttonPoint = RectTransformUtility.WorldToScreenPoint(null, moveRect.position);
            var uiResults = new List<UnityEngine.EventSystems.RaycastResult>();
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            eventSystem.RaycastAll(new UnityEngine.EventSystems.PointerEventData(eventSystem) { position = buttonPoint }, uiResults);
            TestContext.WriteLine($"UI gate probe: screen={Screen.width}x{Screen.height}, point={buttonPoint}, rect={moveRect.rect}, world={moveRect.position}, active={bar.MoveButton.gameObject.activeInHierarchy}, raycast={bar.MoveButton.image.raycastTarget}, depth={bar.MoveButton.image.depth}, canvas={moveRect.GetComponentInParent<Canvas>().renderMode}, hits={string.Join(",", uiResults.Select(hit => hit.gameObject.name))}");
            Assert.IsFalse(movement.TryPickCell(buttonPoint, out _), "UI must own its pointer.");
            Assert.LessOrEqual(moveRect.rect.height, 56.01f);
            Assert.Greater(moveRect.rect.width, 0f);
            mouse = InputSystem.AddDevice<Mouse>();
            screenPoint = camera.WorldToScreenPoint(worldPoint);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPoint });
            yield return null;
            yield return null;
            Assert.IsNull(movement.Selected, "Hover must not select or commit.");
            Assert.IsFalse(bar.ConfirmMoveButton.interactable);
            TestContext.WriteLine($"Hover probe: currentMouse={Mouse.current?.deviceId}, testMouse={mouse.deviceId}, position={Mouse.current?.position.ReadValue()}, expected={screenPoint}, options={Get<CombatHudSnapshot>(commandInput, "snapshot").Movement.Options.Count}, accepts={Get<CombatHudSnapshot>(commandInput, "snapshot").AcceptsCommands}, selecting={movement.IsSelecting}, enabled={movement.enabled}, picked={movement.TryPickCell(mouse.position.ReadValue(), out var hoverCell)}, hex={hoverCell.Q},{hoverCell.R}, line={Get<LineRenderer>(movement, "pathLine").gameObject.activeSelf}");
            StringAssert.Contains("消耗", Get<Text>(bar, "movementLabel").text);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPoint }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = screenPoint });
            yield return null;
            Assert.NotNull(movement.Selected);
            Assert.AreEqual(original, player.Position);
            Assert.AreEqual(originalPoints, player.MovePoints);
            LineRenderer previewLine = Get<LineRenderer>(movement, "pathLine");
            Assert.AreEqual(movement.Selected.Path.Count + 1, previewLine.positionCount);
            for (int index = 0; index < movement.Selected.Path.Count; index++)
            {
                Vector3 drawn = previewLine.GetPosition(index + 1);
                Vector3 expectedWorld = Static3DCombatUnitPresentationProvider.HexToWorld(movement.Selected.Path[index]);
                Assert.AreEqual(expectedWorld.x, drawn.x, 0.0001f);
                Assert.AreEqual(expectedWorld.z, drawn.z, 0.0001f);
            }
            int originalFacing = player.Facing;
            bar.CancelMoveButton.onClick.Invoke();
            Assert.IsNull(movement.Selected);
            Assert.IsFalse(movement.IsSelecting);
            Assert.AreEqual(original, player.Position);
            Assert.AreEqual(originalPoints, player.MovePoints);
            Assert.AreEqual(originalFacing, player.Facing);
            Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(CombatTurnPhase.MoveThenAction, session.TurnPhase);
            bar.MoveButton.onClick.Invoke();
            foreach (HexCoord invalid in new[] { original, enemy.Position, new HexCoord(2, 1), new HexCoord(5, 5), new HexCoord(-20, -20) })
            {
                Vector2 invalidPoint = camera.WorldToScreenPoint(Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(invalid.Q, invalid.R)));
                yield return ClickCell(mouse, invalidPoint);
                Assert.IsNull(movement.Selected, "Illegal cells must not become pending moves.");
                Assert.IsFalse(bar.ConfirmMoveButton.interactable);
                Assert.AreEqual(original, player.Position);
                Assert.AreEqual(originalPoints, player.MovePoints);
                Assert.AreEqual(originalFacing, player.Facing);
                Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);
            }
            yield return ClickCell(mouse, screenPoint);
            Assert.NotNull(movement.Selected);
            CombatMovementOption option = movement.Selected;
            CombatActionResult validation = session.ValidateCommand(new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(0, 1)));
            Assert.AreEqual(validation.MovementCost, option.Cost);
            CollectionAssert.AreEqual(validation.MovementPath.Select(hex => new Vector2Int(hex.Q, hex.R)).ToArray(), option.Path.Select(Key).ToArray());
            bar.ConfirmMoveButton.onClick.Invoke();
            Assert.IsTrue(encounter.IsPresenting);
            Assert.AreEqual(new HexCoord(0, 1), player.Position);
            Assert.AreEqual(originalPoints - option.Cost, player.MovePoints);
            Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);
            Assert.IsTrue(provider.TryGetPresentedUnit("player", out GameObject root));
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(new CombatUnitPresentationHex(original.Q, original.R)), root.transform.position);
            encounter.RequestGuard("player");
            encounter.RequestEndTurn("player");
            encounter.RequestMove("player", 0, 0);
            Assert.AreEqual(CombatTurnPhase.MoveThenAction, session.TurnPhase);
            Assert.AreEqual(new HexCoord(0, 1), player.Position);
            Assert.AreEqual(originalPoints - option.Cost, player.MovePoints);
            Assert.AreEqual(originalTick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(validation.MovementFacing, player.Facing);
            Assert.IsFalse(bar.MoveButton.interactable);
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            yield return null;
            Assert.IsFalse(encounter.IsPresenting);
            Assert.IsTrue(bar.MoveButton.interactable);
            Assert.AreEqual(worldPoint, root.transform.position);
            Assert.AreEqual(Vector3.one, root.transform.localScale);
            Assert.AreEqual(0.04f, root.transform.Find("Static3DCombatBase").localPosition.y, 0.0001f);
            encounter.RequestGuard("player");
            Assert.AreEqual(CombatTurnPhase.ActionThenMove, session.TurnPhase);
            Assert.AreEqual("结束行动", Get<Text>(bar, "waitLabel").text);
            bar.WaitButton.onClick.Invoke();
            Object.FindFirstObjectByType<CombatActionPreviewView>().ConfirmButton.onClick.Invoke();
            Assert.AreEqual(CombatTurnPhase.Finished, session.TurnPhase);
            TestContext.WriteLine($"MOVE scene: options={expected.Length}, cost={option.Cost}, points={player.MovePoints}, root={root.transform.position}, button={moveRect.rect}");
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
                    tick = session.TurnScheduler.CurrentTick, points = player.MovePoints, q = player.Position.Q, r = player.Position.R,
                    hp = player.CurrentHealth, mp = player.CurrentSpirit, enemyHp = enemy.CurrentHealth,
                    busy = encounter.IsPresenting, running = encounter.IsRunning,
                    activeUnits = provider.ActiveCombatantCount, rootActive = root != null && root.activeInHierarchy,
                    selecting = movement.IsSelecting,
                    selected = movement.Selected == null ? "" : movement.Selected.Destination.ToString(),
                    root = root == null ? "removed" : $"position={root.transform.position:F5}; rotation={root.transform.eulerAngles:F3}; scale={root.transform.localScale:F3}; base={root.transform.Find("Static3DCombatBase").position:F5}",
                    status = $"{status.text}; rect={status.rectTransform.rect}; preferred={status.preferredWidth:F2}x{status.preferredHeight:F2}; vertices={status.cachedTextGenerator.vertexCount}; font={status.font.name}; canvas={status.canvas.renderMode}/{status.canvas.scaleFactor}",
                    camera = $"{camera.name}; position={camera.transform.position:F5}; size={camera.orthographicSize}; pixelRect={camera.pixelRect}",
                    controls = $"move={bar.MoveButton.interactable}; confirm={bar.ConfirmMoveButton.interactable}; cancel={bar.CancelMoveButton.interactable}; guard={bar.GuardButton.interactable}; end={bar.WaitButton.interactable}",
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

        private static IEnumerator ClickCell(Mouse device, Vector2 point)
        {
            InputSystem.QueueStateEvent(device, new MouseState { position = point }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(device, new MouseState { position = point });
            yield return null;
        }

        private static Vector2Int Key(CombatUnitPresentationHex hex) => new Vector2Int(hex.Q, hex.R);
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
