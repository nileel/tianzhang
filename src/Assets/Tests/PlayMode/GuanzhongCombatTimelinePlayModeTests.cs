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
    public sealed class GuanzhongCombatTimelinePlayModeTests
    {
        private string timelineSaveDirectory;
        private GuanzhongCombatActionPreviewPlayModeTests timelineFixture;

#if UNITY_EDITOR
        [UnityEditor.MenuItem("天章/诊断/采集当前头顶文字")]
        private static void CaptureCurrentHealthTextGeometry()
        {
            if (!Application.isPlaying || SceneManager.GetActiveScene().name != GameplaySceneNames.Adventure)
                throw new InvalidOperationException("Capture requires the live formal AdventureScene in Play Mode.");
            GameObject canvasRoot = SceneManager.GetActiveScene().GetRootGameObjects().Single(value => value.name == "UICanvas");
            Transform container = canvasRoot.transform.Find("CombatUnitHealthBars");
            if (container == null) throw new InvalidOperationException("Formal health bar container is missing.");
            var texts = new List<Text>();
            foreach (string slot in new[] { "UnitHealthBar_0", "UnitHealthBar_1" })
                foreach (string name in new[] { "Identity", "Health", "Spirit" })
                {
                    Transform target = container.Find(slot + "/" + name);
                    if (target == null || target.GetComponent<Text>() == null)
                        throw new InvalidOperationException("Formal health text is missing: " + slot + "/" + name);
                    texts.Add(target.GetComponent<Text>());
                }
            var records = new List<string>
            {
                "utc=" + DateTime.UtcNow.ToString("O") + ", scene=" + SceneManager.GetActiveScene().name +
                ", actualScreen=" + Screen.width + "x" + Screen.height,
                "Read-only diagnostic: no Text setters, canvas/layout rebuild, or scene mutation. " +
                "All original generators and rendered meshes are recorded before preferred getters or independent generation."
            };
            EncounterCoordinator encounter = FindTimelineOwner<EncounterCoordinator>();
            CombatSession session = encounter == null ? null : GetTimelineField<CombatSession>(encounter, "session");
            if (session != null)
                records.Add("session actor=" + session.CurrentActorId + ", phase=" + session.TurnPhase +
                    ", tick=" + session.TurnScheduler.CurrentTick);

            // Capture every original mesh first: preferred getters and independent generation can touch font caches.
            foreach (Text text in texts)
            {
                string path = "/UICanvas/CombatUnitHealthBars/" + text.transform.parent.name + "/" + text.name;
                Canvas canvas = text.canvas;
                Rect rect = text.rectTransform.rect;
                records.Add("ORIGINAL " + path + ", instance=" + text.GetInstanceID() + ", text=[" + text.text +
                    "], active=" + text.gameObject.activeInHierarchy + ", font=" + text.font.name +
                    ", fontInstance=" + text.font.GetInstanceID() + ", dynamic=" + text.font.dynamic +
                    ", fontSize=" + text.fontSize + ", fontStyle=" + text.fontStyle +
                    ", horizontal=" + text.horizontalOverflow + ", vertical=" + text.verticalOverflow);
                records.Add("rect=" + HealthRect(rect) + ", pixelRect=" + HealthRect(text.GetPixelAdjustedRect()) +
                    ", ppu=" + HealthFloat(text.pixelsPerUnit) + ", canvas=" + canvas.name +
                    ", canvasInstance=" + canvas.GetInstanceID() + ", renderMode=" + canvas.renderMode +
                    ", canvasScale=" + HealthFloat(canvas.scaleFactor) + ", pixelPerfect=" + canvas.pixelPerfect +
                    ", rootCanvas=" + canvas.rootCanvas.name + ", camera=" +
                    (canvas.worldCamera == null ? "<null>" : canvas.worldCamera.name));
                AppendHealthGenerator(records, "original", text.cachedTextGenerator);
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                foreach (RectTransform bounds in new[] { text.rectTransform, (RectTransform)text.transform.parent })
                {
                    var corners = new Vector3[4];
                    bounds.GetWorldCorners(corners);
                    records.Add("screenCorners " + bounds.name + "=" + string.Join(";", corners.Select(corner =>
                        HealthPoint(RectTransformUtility.WorldToScreenPoint(camera, corner)))));
                }
                Mesh mesh = text.canvasRenderer.GetMesh();
                records.Add("renderedMeshVertices=" + (mesh == null ? 0 : mesh.vertexCount) +
                    ", rendererCull=" + text.canvasRenderer.cull);
                if (mesh != null)
                {
                    Vector3[] vertices = mesh.vertices;
                    Vector2[] uv = mesh.uv;
                    for (int index = 0; index < vertices.Length; index++)
                        records.Add("meshVertex " + index + " local=" + HealthPoint(vertices[index]) +
                            ", screen=" + HealthPoint(RectTransformUtility.WorldToScreenPoint(camera,
                                text.rectTransform.TransformPoint(vertices[index]))) +
                            ", uv=" + (index < uv.Length ? HealthPoint(uv[index]) : "<none>"));
                }
            }
            foreach (Text text in texts)
            {
                float preferredWidth = text.preferredWidth;
                float preferredHeight = text.preferredHeight;
                float width = text.rectTransform.rect.width;
                records.Add("COMPARISON " + text.transform.parent.name + "/" + text.name +
                    ", preferredWidth=" + HealthFloat(preferredWidth) + ", preferredHeight=" + HealthFloat(preferredHeight) +
                    ", widthMinusPreferred=" + HealthFloat(width - preferredWidth) +
                    ", widthPixels=" + HealthFloat(width * text.pixelsPerUnit) +
                    ", preferredPixels=" + HealthFloat(preferredWidth * text.pixelsPerUnit));
                TextGenerationSettings settings = text.GetGenerationSettings(text.rectTransform.rect.size);
                using (var same = new TextGenerator())
                using (var singleLine = new TextGenerator())
                {
                    records.Add("sameSettingsPopulated=" + same.Populate(text.text, settings));
                    AppendHealthGenerator(records, "same-settings", same);
                    settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                    records.Add("singleLinePopulated=" + singleLine.Populate(text.text, settings));
                    AppendHealthGenerator(records, "horizontal-overflow-only", singleLine);
                }
            }
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "timeline"));
            Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, "health-text-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmss.fffffffZ") + ".txt");
            File.WriteAllLines(output, records);
            Debug.Log("Health text geometry captured: " + output);
        }

        private static void AppendHealthGenerator(List<string> records, string label, TextGenerator generator)
        {
            records.Add(label + " lines=" + generator.lineCount + ", characters=" + generator.characterCount +
                ", visibleCharacters=" + generator.characterCountVisible + ", vertices=" + generator.vertexCount +
                ", extents=" + HealthRect(generator.rectExtents));
            foreach (UILineInfo line in generator.lines)
                records.Add(label + " line start=" + line.startCharIdx + ", height=" + line.height + ", topY=" + HealthFloat(line.topY));
            for (int index = 0; index < generator.characters.Count; index++)
            {
                UICharInfo character = generator.characters[index];
                records.Add(label + " character " + index + " cursor=" + HealthPoint(character.cursorPos) +
                    ", width=" + HealthFloat(character.charWidth));
            }
        }

        private static string HealthFloat(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        private static string HealthPoint(Vector2 value) => "(" + HealthFloat(value.x) + "," + HealthFloat(value.y) + ")";
        private static string HealthRect(Rect value) => "(" + HealthFloat(value.x) + "," + HealthFloat(value.y) +
            "," + HealthFloat(value.width) + "," + HealthFloat(value.height) + ")";
#endif

        [UnityTearDown]
        public IEnumerator CleanupFormalTimelineRoute()
        {
            if (timelineFixture != null) { yield return timelineFixture.Cleanup(); timelineFixture = null; }
            if (timelineSaveDirectory == null) yield break;
            GameBootstrap bootstrap = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return null;
            if (Directory.Exists(timelineSaveDirectory)) Directory.Delete(timelineSaveDirectory, true);
            timelineSaveDirectory = null;
        }

        [UnityTest]
        public IEnumerator FormalDefaultStartMenuRouteProjectsActualTimelineThroughMoveGuardWaitDeathAndReentry()
        {
            GameBootstrap stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return WaitForTimelineScene(GameplaySceneNames.StartMenu);
            timelineSaveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-Timeline-" + Guid.NewGuid().ToString("N"));
            SetTimelineField(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(timelineSaveDirectory));
            CharacterCreationController creation = FindTimelineOwner<CharacterCreationController>();
            creation.Open();
            Assert.That(creation.Draft.OriginId, Is.EqualTo("origin_loose"), "Use the actual StartMenu default origin without changing the draft.");
            Assert.That(new[] { creation.Draft.Innate.RootBone, creation.Draft.Innate.Soul, creation.Draft.Innate.DivineSense,
                creation.Draft.Innate.Aptitude, creation.Draft.Innate.Fortune }, Is.EqualTo(new[] { 8, 8, 8, 8, 8 }));
            creation.Submit("formal-timeline", "默认行动轴验证");
            yield return WaitForTimelineScene(GameplaySceneNames.World);
            Assert.AreEqual("凡人", GameBootstrap.RequireRuntime().Player.Progression.RealmStage);
            Assert.AreEqual("basic_body_human_fanren", GameBootstrap.RequireRuntime().Player.UnarmedBasicAttackProfileId);
            WorldMapController world = FindTimelineOwner<WorldMapController>();
            Assert.That(world.SelectNode("guanzhong_hub"), Is.True);
            Assert.That(world.SelectedNode.displayName, Is.EqualTo("关陇玄域"));
            world.EnterSelectedLocation();
            yield return WaitForTimelineScene(GameplaySceneNames.Settlement);
            SettlementController settlement = FindTimelineOwner<SettlementController>();
            BountyBoardView board = FindTimelineOwner<BountyBoardView>();
            board.Show(GetTimelineField<ContentCatalogData>(settlement, "contentCatalog"), "guanzhong_city", GameBootstrap.RequireRuntime().Bounties);
            board.SubmitAccept("bounty_guanzhong_shijiahou");
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return WaitForTimelineScene(GameplaySceneNames.Adventure);
            AdventureController adventure = FindTimelineOwner<AdventureController>();
            yield return WaitForTimelineExploration(adventure);
            CombatHudPresenter presenter = FindTimelineOwner<CombatHudPresenter>();
            CombatTimelineView view = FindTimelineOwner<CombatTimelineView>();
            GameObject exploration = GetTimelineField<GameObject>(presenter, "explorationPanel");
            AssertTimelineHidden(view, exploration);
            EncounterCoordinator encounter = FindTimelineOwner<EncounterCoordinator>();
            var captured = new FormalTimelineSink(encounter, GetTimelineField<ICombatPresentationSink>(encounter, "presentation"));
            SetTimelineField(encounter, "presentation", captured);
            adventure.SetEncounterRandomSource(new TimelineEncounterRandom());
            encounter.SetCombatResolutionRandomSource(new TimelineCombatRandom());
            Assert.That(FindTimelineOwner<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            CombatSession session = GetTimelineField<CombatSession>(encounter, "session");
            AdventureSpawnSet spawned = GetTimelineField<AdventureSpawnSet>(encounter, "spawned");
            yield return WaitForTimelinePlayer(encounter, session, spawned.Player.Id);
            Assert.That(exploration.activeSelf, Is.False);
            Assert.That(GetTimelineField<GameObject>(view, "root").activeSelf, Is.True);
            DiagnoseFormalTimelineRects(view, "first-player-turn");

            Button[] entries = GetTimelineField<Button[]>(view, "entryButtons");
            CombatTimelineSnapshot timeline = captured.Latest.Timeline;
            int enemyEntry = timeline.Entries.ToList().FindIndex(entry => entry.Id == spawned.Enemy.Id);
            Assert.That(enemyEntry, Is.GreaterThanOrEqualTo(0));
            string current = session.CurrentActorId;
            entries[enemyEntry].onClick.Invoke();
            Assert.That(view.FocusedCombatantId, Is.EqualTo(spawned.Enemy.Id));
            Assert.That(session.CurrentActorId, Is.EqualTo(current), "Viewing the enemy must not transfer the real turn.");
            StringAssert.Contains(captured.Latest.Enemy.DisplayName, GetTimelineField<Text>(view, "focusText").text);
            StringAssert.Contains("CT ", GetTimelineField<Text>(view, "focusText").text);
            presenter.Hide();
            AssertTimelineHidden(view, exploration);
            Assert.That(GetTimelineField<RectTransform[]>(view, "entryRoots").All(rect => !rect.gameObject.activeSelf), Is.True);
            Assert.That(GetTimelineField<Button[]>(view, "entryButtons").All(button => !button.interactable), Is.True);
            presenter.Present(captured.Latest);
            Assert.That(view.FocusedCombatantId, Is.EqualTo(captured.Latest.Timeline.Entries[0].Id), "Hide must clear the previous enemy focus.");
            Assert.That(exploration.activeSelf, Is.False);

            ICombatCommandHandler commands = encounter;
            CombatTurnSnapshot beforeMove = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            CombatMovementQueryResult path = session.SpatialQuery.QueryMovement(spawned.Player.Position, new HexCoord(0, 1),
                spawned.Player.ActionPointsRemainingUnits, spawned.Player.MovementEfficiencyMilliUnits, new[] { spawned.Enemy.Position });
            Assert.That(path.IsReachable, Is.True, path.Reason);
            commands.RequestMove(spawned.Player.Id, 0, 1);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            AssertSameSchedulerSnapshot(beforeMove, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.EqualTo(spawned.Player.Id));
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            Assert.That(encounter.IsPresenting, Is.False);
            Assert.AreEqual(50000, path.ActionPointCostUnits, "The actual mortal movement efficiency is 2000, not the adept's 4000.");
            CombatCommandPreview guard = new CombatCommandService().Preview(session,
                new CombatCommand(CombatCommandKind.Guard, spawned.Player.Id));
            Assert.IsTrue(guard.IsLegal, guard.RejectionReason);
            Assert.AreEqual(50000, guard.ActionPointCostUnits);
            commands.RequestGuard(spawned.Player.Id);
            Assert.That(spawned.Player.ActionPointsRemainingUnits, Is.Zero);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            commands.RequestEndTurn(spawned.Player.Id);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
            CombatTurnSnapshot exhausted = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            Assert.That(exhausted.CurrentTick, Is.EqualTo(beforeMove.CurrentTick));
            Assert.That(Unit(exhausted, spawned.Player.Id).NextActionThreshold, Is.EqualTo(100f));
            commands.RequestGuard(spawned.Player.Id);
            commands.RequestEndTurn(spawned.Player.Id);
            AssertSameSchedulerSnapshot(exhausted, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.Null.Or.Empty);
            Assert.That(captured.Latest.Timeline.Entries.All(entry => !entry.IsCurrent), Is.True);
            yield return WaitForTimelinePlayer(encounter, session, spawned.Player.Id);
            float ctBeforeWait = Unit(session.TurnScheduler.GetSnapshot(session.Combatants.All), spawned.Player.Id).ChargeTime;
            commands.RequestWait(spawned.Player.Id);
            Assert.That(Unit(session.TurnScheduler.GetSnapshot(session.Combatants.All), spawned.Player.Id).ChargeTime,
                Is.EqualTo(ctBeforeWait * 0.5f));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.Null.Or.Empty);

            for (float deadline = Time.realtimeSinceStartup + 60f; Time.realtimeSinceStartup < deadline && encounter != null && encounter.IsRunning;)
            {
                if (!encounter.IsPresenting && session.CurrentActorId == spawned.Player.Id && session.TurnPhase == CombatTurnPhase.Active)
                    commands.RequestBasicAttack(spawned.Player.Id, spawned.Enemies.FirstOrDefault(enemy => enemy.IsAlive)?.Id);
                if (!encounter.IsPresenting && session.CurrentActorId == spawned.Player.Id && session.TurnPhase == CombatTurnPhase.Active)
                    commands.RequestEndTurn(spawned.Player.Id);
                yield return null;
            }
            Assert.That(captured.SawDeathFilteredSnapshot, Is.True, "An actual combat death must disappear from the timeline before the return callback.");
            yield return WaitForTimelineScene(GameplaySceneNames.Settlement);
            settlement = FindTimelineOwner<SettlementController>();
            Assert.That(settlement.EnterAdventure("guanzhong_wild"), Is.True);
            yield return WaitForTimelineScene(GameplaySceneNames.Adventure);
            adventure = FindTimelineOwner<AdventureController>();
            yield return WaitForTimelineExploration(adventure);
            presenter = FindTimelineOwner<CombatHudPresenter>();
            view = FindTimelineOwner<CombatTimelineView>();
            AssertTimelineHidden(view, GetTimelineField<GameObject>(presenter, "explorationPanel"));
            encounter = FindTimelineOwner<EncounterCoordinator>();
            var reentry = new FormalTimelineSink(encounter, GetTimelineField<ICombatPresentationSink>(encounter, "presentation"));
            SetTimelineField(encounter, "presentation", reentry);
            adventure.SetEncounterRandomSource(new TimelineEncounterRandom());
            encounter.SetCombatResolutionRandomSource(new TimelineCombatRandom());
            Assert.That(FindTimelineOwner<AdventureInputController>().SelectNode("shijiahou_encounter"), Is.True);
            session = GetTimelineField<CombatSession>(encounter, "session");
            spawned = GetTimelineField<AdventureSpawnSet>(encounter, "spawned");
            yield return WaitForTimelinePlayer(encounter, session, spawned.Player.Id);
            Assert.That(reentry.Latest.Timeline.Entries, Has.Count.EqualTo(3));
            Assert.That(view.FocusedCombatantId, Is.EqualTo(reentry.FirstFocusId));
            DiagnoseFormalTimelineRects(view, "fresh-encounter");
        }

        private static IEnumerator WaitForTimelineScene(string scene)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (SceneManager.GetActiveScene().name == scene) { yield return null; yield break; }
                yield return null;
            }
            Assert.Fail("Scene did not load: " + scene);
        }

        private static IEnumerator WaitForTimelineExploration(AdventureController adventure)
        {
            for (int frame = 0; frame < 120; frame++)
            {
                if (adventure != null && adventure.Session != null && adventure.CurrentState == AdventureSceneState.Exploration) yield break;
                yield return null;
            }
            Assert.Fail("Adventure did not initialize exploration.");
        }

        private static IEnumerator WaitForTimelinePlayer(EncounterCoordinator encounter, CombatSession session, string id)
        {
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline;)
            {
                Assert.That(encounter != null && encounter.IsRunning, Is.True);
                if (!encounter.IsPresenting && session.CurrentActorId == id && session.TurnPhase == CombatTurnPhase.Active) yield break;
                yield return null;
            }
            Assert.Fail("The actual scheduler did not yield the player's turn.");
        }

        private static void AssertTimelineHidden(CombatTimelineView view, GameObject exploration)
        {
            Assert.That(GetTimelineField<GameObject>(view, "root").activeSelf, Is.False);
            Assert.That(view.FocusedCombatantId, Is.Null.Or.Empty);
            Assert.That(GetTimelineField<Text>(view, "focusText").text, Is.Empty);
            Assert.That(GetTimelineField<Text>(view, "tickText").text, Is.Empty);
            Assert.That(GetTimelineField<Text[]>(view, "entryLabels").All(text => text.text == string.Empty), Is.True);
            Assert.That(GetTimelineField<RectTransform[]>(view, "entryRoots").All(rect => !rect.gameObject.activeInHierarchy), Is.True);
            Assert.That(exploration.activeSelf, Is.True);
        }

        private static void DiagnoseFormalTimelineRects(CombatTimelineView view, string stage)
        {
            Canvas.ForceUpdateCanvases();
            Text[] labels = GetTimelineField<Text[]>(view, "entryLabels");
            Text[] texts = new[] { GetTimelineField<Text>(view, "tickText"), GetTimelineField<Text>(view, "focusText") }
                .Concat(labels).ToArray();
            var lines = new List<string> { "stage=" + stage + ", actualScreen=" + Screen.width + "x" + Screen.height +
                "; PlayMode diagnostic only, no resolution switch or claim of dual-resolution visual acceptance." };
            foreach (Text text in texts)
            {
                Vector3[] corners = new Vector3[4];
                text.rectTransform.GetWorldCorners(corners);
                Canvas canvas = text.canvas;
                Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners[0]);
                Vector2 topRight = RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, corners[2]);
                lines.Add(text.name + ": text=" + text.text.Replace("\n", "|") + ", rect=" + text.rectTransform.rect +
                    ", screen=" + bottomLeft + ".." + topRight + ", preferred=" + text.preferredWidth + "x" + text.preferredHeight +
                    ", fontSize=" + text.fontSize + ", canvasScale=" + canvas.scaleFactor + ", vertices=" + text.cachedTextGenerator.vertexCount);
            }
            Image[] portraits = GetTimelineField<Image[]>(view, "entryPortraits");
            var geometry = new List<(Text label, Rect portrait, Rect lastLine, int lineCount, int glyphVertices)>();
            for (int index = 0; index < labels.Length; index++)
            {
                Text label = labels[index];
                Image portrait = portraits[index];
                if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
                Canvas canvas = label.canvas;
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                Vector3[] portraitCorners = new Vector3[4];
                portrait.rectTransform.GetWorldCorners(portraitCorners);
                Vector2 portraitMin = RectTransformUtility.WorldToScreenPoint(camera, portraitCorners[0]);
                Vector2 portraitMax = RectTransformUtility.WorldToScreenPoint(camera, portraitCorners[2]);
                Rect portraitBounds = Rect.MinMaxRect(portraitMin.x, portraitMin.y, portraitMax.x, portraitMax.y);

                // Read the actual rendered mesh, including Text's pixel adjustment, rather than estimating a line from font size.
                Mesh mesh = label.canvasRenderer.GetMesh();
                Vector3[] vertices = mesh.vertices;
                IList<UILineInfo> generatedLines = label.cachedTextGenerator.lines;
                int lastLineStart = generatedLines.Count == 0 ? vertices.Length : generatedLines[generatedLines.Count - 1].startCharIdx * 4;
                var glyphScreenPoints = new List<Vector2>();
                for (int vertex = lastLineStart; vertex + 3 < vertices.Length; vertex += 4)
                {
                    Vector3[] quad = { vertices[vertex], vertices[vertex + 1], vertices[vertex + 2], vertices[vertex + 3] };
                    if (quad.Max(point => point.x) <= quad.Min(point => point.x) || quad.Max(point => point.y) <= quad.Min(point => point.y)) continue;
                    glyphScreenPoints.AddRange(quad.Select(point => RectTransformUtility.WorldToScreenPoint(camera, label.rectTransform.TransformPoint(point))));
                }
                Rect lastLineBounds = glyphScreenPoints.Count == 0 ? Rect.zero : Rect.MinMaxRect(
                    glyphScreenPoints.Min(point => point.x), glyphScreenPoints.Min(point => point.y),
                    glyphScreenPoints.Max(point => point.x), glyphScreenPoints.Max(point => point.y));
                geometry.Add((label, portraitBounds, lastLineBounds, generatedLines.Count, glyphScreenPoints.Count));
                lines.Add(label.name + ": portraitScreen=" + portraitBounds + ", lastVisibleLineScreen=" + lastLineBounds +
                    ", generatedLines=" + generatedLines.Count + ", lastLineGlyphVertices=" + glyphScreenPoints.Count +
                    ", portraitToTextGap=" + (portraitBounds.yMin - lastLineBounds.yMax));
            }
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "timeline"));
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, stage + "-rects.txt"), lines);
            foreach (string line in lines) TestContext.WriteLine(line);
            foreach (Text text in texts.Where(text => !string.IsNullOrEmpty(text.text) && text.gameObject.activeInHierarchy))
            {
                Assert.That(text.cachedTextGenerator.vertexCount, Is.GreaterThan(0), text.name + " must produce actual text geometry.");
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 0.1f), text.name + " must show all mandatory lines.");
            }
            foreach (var entry in geometry)
            {
                Assert.That(entry.label.preferredWidth, Is.LessThanOrEqualTo(entry.label.rectTransform.rect.width), entry.label.name + " must fit without automatic wrapping.");
                // Font line-box height includes leading; actual line count and rendered glyph geometry prove the single-line layout.
                Assert.That(entry.lineCount, Is.EqualTo(1), entry.label.name + " must generate exactly one line.");
                Assert.That(entry.glyphVertices, Is.GreaterThan(0), entry.label.name + " last visible line must contain actual glyph geometry.");
                Assert.That(entry.lastLine.Overlaps(entry.portrait), Is.False, entry.label.name + " actual glyphs must not overlap the portrait.");
                Assert.That(entry.lastLine.yMax, Is.LessThanOrEqualTo(entry.portrait.yMin), entry.label.name + " actual label line must remain below its portrait.");
            }
        }

        private static T FindTimelineOwner<T>() where T : Component => Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(value => value.gameObject.scene == SceneManager.GetActiveScene());
        private static T GetTimelineField<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void SetTimelineField(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class TimelineEncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
        private sealed class TimelineCombatRandom : ICombatResolutionRandomSource
        {
            private int count;
            public float NextPercent() => count++ % 4 == 0 ? 0f : 99f;
        }

        private sealed class FormalTimelineSink : ICombatPresentationSink
        {
            private readonly EncounterCoordinator owner;
            private readonly ICombatPresentationSink actual;
            private readonly List<string> records = new List<string>();
            public FormalTimelineSink(EncounterCoordinator owner, ICombatPresentationSink actual) { this.owner = owner; this.actual = actual; }
            public CombatHudSnapshot Latest { get; private set; }
            public bool SawDeathFilteredSnapshot { get; private set; }
            public string FirstFocusId { get; private set; }
            public void Present(CombatHudSnapshot hud)
            {
                Latest = hud;
                CombatSession session = GetTimelineField<CombatSession>(owner, "session");
                CombatTurnSnapshot turns = session.TurnScheduler.GetSnapshot(session.Combatants.All);
                CombatTimelineSnapshot timeline = hud.Timeline;
                Assert.That(timeline, Is.Not.Null);
                Assert.That(timeline.CurrentTick, Is.EqualTo(turns.CurrentTick));
                string current = session.TurnPhase == CombatTurnPhase.Finished ? null : session.CurrentActorId;
                Assert.That(timeline.CurrentActorId, Is.EqualTo(current));
                string[] expectedOrder = new[] { current }.Concat(turns.ReadyQueueIds).Concat(turns.ChargingUnitIds)
                    .Where(id => !string.IsNullOrEmpty(id) && turns.Units.Any(value => value.Id == id)).Distinct().ToArray();
                Assert.That(timeline.Entries.Select(entry => entry.Id), Is.EqualTo(expectedOrder));
                Assert.That(timeline.Entries.Count(entry => entry.IsCurrent), Is.EqualTo(string.IsNullOrEmpty(current) ? 0 : 1));
                foreach (CombatTimelineEntrySnapshot entry in timeline.Entries)
                {
                    CombatTurnUnitSnapshot unit = Unit(turns, entry.Id);
                    Assert.That((entry.Speed, entry.CurrentCt, entry.NextActionThreshold, entry.IsReady),
                        Is.EqualTo((unit.Speed, unit.ChargeTime, unit.NextActionThreshold, unit.IsReady)));
                    Assert.That(entry.QueuePosition, Is.EqualTo(turns.ReadyQueueIds.ToList().IndexOf(entry.Id)));
                    Assert.That(entry.IsCurrent, Is.EqualTo(entry.Id == current));
                    Assert.That(session.Combatants.TryGet(entry.Id, out CombatantSnapshot combatant), Is.True);
                    Assert.That(entry.Faction, Is.EqualTo(combatant.Team == CombatTeam.Player ? CombatUnitDisplayFaction.Player : CombatUnitDisplayFaction.Enemy));
                    Assert.That(entry.DisplayName, Is.EqualTo(entry.Faction == CombatUnitDisplayFaction.Player ? hud.Player.DisplayName : hud.Enemy.DisplayName));
                }
                SawDeathFilteredSnapshot |= session.Combatants.All.Any(value => !value.IsAlive) &&
                    timeline.Entries.All(entry => session.Combatants.All.Single(value => value.Id == entry.Id).IsAlive);
                FirstFocusId ??= timeline.Entries.FirstOrDefault()?.Id;
                records.Add("tick=" + timeline.CurrentTick + ", actor=" + (timeline.CurrentActorId ?? "<none>") + ", phase=" + session.TurnPhase +
                    ", readyQueue=[" + string.Join(",", turns.ReadyQueueIds) + "], charging=[" + string.Join(",", turns.ChargingUnitIds) + "] " +
                    string.Join("; ", timeline.Entries.Select(entry => entry.Id + ":CT=" + entry.CurrentCt + "/" + entry.NextActionThreshold +
                        ",speed=" + entry.Speed + ",ready=" + entry.IsReady + ",queue=" + entry.QueuePosition + ",current=" + entry.IsCurrent)));
                actual.Present(hud);
                string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "timeline"));
                Directory.CreateDirectory(directory);
                File.WriteAllLines(Path.Combine(directory, "formal-session-" + owner.GetInstanceID() + ".txt"), records);
            }
            public void ClearLog() => actual.ClearLog();
            public void AppendLog(string message) => actual.AppendLog(message);
            public void Hide() => actual.Hide();
        }

        [Test]
        public void SnapshotKeepsTheExistingReadyQueueBeforeAnyFasterChargingCandidate()
        {
            CombatantSnapshot[] combatants =
            {
                CreateTimelineCombatant("fast", CombatTeam.Player, 300),
                CreateTimelineCombatant("queued_second", CombatTeam.Enemy, 250),
                CreateTimelineCombatant("queued_third", CombatTeam.Enemy, 200),
            };
            var scheduler = new CombatTurnScheduler(combatants);
            CombatTurnAdvance first = scheduler.AdvanceUntilAction(combatants);
            Assert.That(first.ActorId, Is.EqualTo("fast"));
            Assert.That(first.TicksElapsed, Is.EqualTo(1));
            CombatTurnSnapshot ready = scheduler.GetSnapshot(combatants);
            Assert.That(ready.ReadyQueueIds, Is.EqualTo(new[] { "queued_second", "queued_third" }));
            Assert.That(ready.Units.All(value => value.IsReady), Is.True);
            Assert.That(ready.Units.Select(value => value.ChargeTime), Is.EqualTo(new[] { 300f, 250f, 200f }));
            scheduler.ConsumeAction("fast", 0);
            CombatTurnSnapshot afterConsume = scheduler.GetSnapshot(combatants);
            Assert.That(afterConsume.ChargingUnitIds, Is.EqualTo(new[] { "fast" }));
            Assert.That(afterConsume.ReadyQueueIds, Is.EqualTo(ready.ReadyQueueIds));

            CombatTurnAdvance second = scheduler.AdvanceUntilAction(combatants);
            Assert.That(second.ActorId, Is.EqualTo(afterConsume.ReadyQueueIds[0]));
            Assert.That(second.TicksElapsed, Is.Zero);
            scheduler.ConsumeAction(second.ActorId, 0);
            CombatTurnAdvance third = scheduler.AdvanceUntilAction(combatants);
            Assert.That(third.ActorId, Is.EqualTo(afterConsume.ReadyQueueIds[1]));
            Assert.That(third.TicksElapsed, Is.Zero);
            Assert.That(scheduler.CurrentTick, Is.EqualTo(1));
            Assert.That(ready.ReadyQueueIds, Is.EqualTo(new[] { "queued_second", "queued_third" }),
                "A previous snapshot must retain its copied queue after real dequeue operations.");
        }

        [Test]
        public void ChargingProjectionMatchesSubsequentRealAdvanceAtDifferentSpeedsAndThresholds()
        {
            CombatantSnapshot[] combatants =
            {
                CreateTimelineCombatant("speed_100", CombatTeam.Player, 100),
                CreateTimelineCombatant("speed_50", CombatTeam.Enemy, 50),
            };
            var projected = new CombatTurnScheduler(combatants);
            var actual = new CombatTurnScheduler(combatants);
            Assert.That(projected.AdvanceUntilAction(combatants).ActorId, Is.EqualTo("speed_100"));
            Assert.That(actual.AdvanceUntilAction(combatants).ActorId, Is.EqualTo("speed_100"));
            projected.ConsumeAction("speed_100", 30);
            actual.ConsumeAction("speed_100", 30);
            CombatTurnSnapshot snapshot = projected.GetSnapshot(combatants);
            Assert.That(Unit(snapshot, "speed_100").Speed, Is.EqualTo(100));
            Assert.That(Unit(snapshot, "speed_100").ChargeTime, Is.Zero);
            Assert.That(Unit(snapshot, "speed_100").NextActionThreshold, Is.EqualTo(130f));
            Assert.That(Unit(snapshot, "speed_50").Speed, Is.EqualTo(50));
            Assert.That(Unit(snapshot, "speed_50").ChargeTime, Is.EqualTo(50f));
            Assert.That(Unit(snapshot, "speed_50").NextActionThreshold, Is.EqualTo(100f));

            var dispatched = new List<string>();
            for (int index = 0; index < 2; index++)
            {
                CombatTurnAdvance advance = actual.AdvanceUntilAction(combatants);
                Assert.That(advance.TicksElapsed, Is.EqualTo(1));
                dispatched.Add(advance.ActorId);
                actual.ConsumeAction(advance.ActorId, 0);
            }
            Assert.That(snapshot.ChargingUnitIds, Is.EqualTo(dispatched));
            Assert.That(dispatched, Is.EqualTo(new[] { "speed_50", "speed_100" }));
            Assert.That(snapshot.ReadyQueueIds, Is.Empty);
            Assert.That(snapshot.CurrentTick, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedSnapshotReadsCannotChangeTheNextRealAdvanceOrMutateCopiedCollections()
        {
            CombatantSnapshot[] combatants =
            {
                CreateTimelineCombatant("speed_100", CombatTeam.Player, 100),
                CreateTimelineCombatant("speed_50", CombatTeam.Enemy, 50),
            };
            var observed = new CombatTurnScheduler(combatants);
            var control = new CombatTurnScheduler(combatants);
            CombatTurnSnapshot initial = observed.GetSnapshot(combatants);
            var units = (IList<CombatTurnUnitSnapshot>)initial.Units;
            var charging = (IList<string>)initial.ChargingUnitIds;
            var queue = (IList<string>)initial.ReadyQueueIds;
            Assert.That(units.IsReadOnly && charging.IsReadOnly && queue.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => units.Clear());
            Assert.Throws<NotSupportedException>(() => charging[0] = "invented");
            Assert.Throws<NotSupportedException>(() => queue.Add("invented"));

            for (int action = 0; action < 8; action++)
            {
                CombatTurnSnapshot before = observed.GetSnapshot(combatants);
                for (int read = 0; read < 20; read++)
                    AssertSameSchedulerSnapshot(before, observed.GetSnapshot(combatants));
                AssertSameSchedulerSnapshot(control.GetSnapshot(combatants), observed.GetSnapshot(combatants));
                CombatTurnAdvance observedAdvance = observed.AdvanceUntilAction(combatants);
                CombatTurnAdvance controlAdvance = control.AdvanceUntilAction(combatants);
                Assert.That(observedAdvance.ActorId, Is.EqualTo(controlAdvance.ActorId));
                Assert.That(observedAdvance.TicksElapsed, Is.EqualTo(controlAdvance.TicksElapsed));
                AssertSameSchedulerSnapshot(control.GetSnapshot(combatants), observed.GetSnapshot(combatants));
                int penalty = action % 2 == 0 ? 30 : 0;
                observed.ConsumeAction(observedAdvance.ActorId, penalty);
                control.ConsumeAction(controlAdvance.ActorId, penalty);
            }
            Assert.That(initial.CurrentTick, Is.Zero);
            Assert.That(initial.Units.All(value => value.ChargeTime == 0f && value.NextActionThreshold == 100f && !value.IsReady), Is.True);
            Assert.That(initial.ReadyQueueIds, Is.Empty);
        }

        [TestCase(100, 100f, 50f, 150f)]
        [TestCase(300, 300f, 150f, 450f)]
        public void WaitRetainsHalfOfActualCtAndStillChargesOneTickWhenRetentionAlreadyMeetsThreshold(
            int speed, float readyCt, float retainedCt, float nextCt)
        {
            CombatantSnapshot[] combatants = { CreateTimelineCombatant("actor", CombatTeam.Player, speed) };
            var scheduler = new CombatTurnScheduler(combatants);
            CombatTurnAdvance first = scheduler.AdvanceUntilAction(combatants);
            Assert.That(first.TicksElapsed, Is.EqualTo(1));
            Assert.That(Unit(scheduler.GetSnapshot(combatants), "actor").ChargeTime, Is.EqualTo(readyCt));
            scheduler.Wait("actor");
            CombatTurnSnapshot waiting = scheduler.GetSnapshot(combatants);
            Assert.That(Unit(waiting, "actor").ChargeTime, Is.EqualTo(retainedCt));
            Assert.That(Unit(waiting, "actor").NextActionThreshold, Is.EqualTo(100f));
            Assert.That(Unit(waiting, "actor").IsReady, Is.False);
            Assert.That(waiting.CurrentTick, Is.EqualTo(1));
            Assert.That(waiting.ChargingUnitIds, Is.EqualTo(new[] { "actor" }));

            CombatTurnAdvance next = scheduler.AdvanceUntilAction(combatants);
            Assert.That(next.ActorId, Is.EqualTo(waiting.ChargingUnitIds.Single()));
            Assert.That(next.TicksElapsed, Is.EqualTo(1), "Wait cannot make a non-ready unit dispatch before another real tick.");
            Assert.That(scheduler.CurrentTick, Is.EqualTo(2));
            Assert.That(Unit(scheduler.GetSnapshot(combatants), "actor").ChargeTime, Is.EqualTo(nextCt));
            Assert.That(Unit(waiting, "actor").ChargeTime, Is.EqualTo(retainedCt));
        }

        [Test]
        public void SchedulerFiltersDeadUnitsFromStateAndQueueAndSkipsTheirActualDispatch()
        {
            CombatantSnapshot[] combatants =
            {
                CreateTimelineCombatant("current", CombatTeam.Player, 300),
                CreateTimelineCombatant("dies_queued", CombatTeam.Player, 250),
                CreateTimelineCombatant("living_queued", CombatTeam.Enemy, 200),
                CreateTimelineCombatant("dies_charging", CombatTeam.Enemy, 50),
            };
            var scheduler = new CombatTurnScheduler(combatants);
            Assert.That(scheduler.AdvanceUntilAction(combatants).ActorId, Is.EqualTo("current"));
            CombatTurnSnapshot aliveSnapshot = scheduler.GetSnapshot(combatants);
            combatants[1].ReceiveDamage(combatants[1].CurrentHealth);
            combatants[3].ReceiveDamage(combatants[3].CurrentHealth);
            CombatTurnSnapshot filtered = scheduler.GetSnapshot(combatants);
            Assert.That(filtered.Units.Select(value => value.Id), Is.EquivalentTo(new[] { "current", "living_queued" }));
            Assert.That(filtered.ReadyQueueIds, Is.EqualTo(new[] { "living_queued" }));
            Assert.That(filtered.ChargingUnitIds, Is.Empty);
            CombatTurnAdvance next = scheduler.AdvanceUntilAction(combatants);
            Assert.That(next.ActorId, Is.EqualTo("living_queued"));
            Assert.That(next.TicksElapsed, Is.Zero);
            Assert.That(aliveSnapshot.Units, Has.Count.EqualTo(4));
            Assert.That(aliveSnapshot.ReadyQueueIds, Is.EqualTo(new[] { "dies_queued", "living_queued" }));
            Assert.That(aliveSnapshot.ChargingUnitIds, Is.EqualTo(new[] { "dies_charging" }));
        }

        [Test]
        public void MovementAndPendingActionKeepActualCtUntilTheExplicitEndTurnSettlement()
        {
            CombatantSnapshot player = CreateTimelineCombatant("player", CombatTeam.Player, 100, movePoints: 3);
            CombatantSnapshot enemy = CreateTimelineCombatant("enemy", CombatTeam.Enemy, 1);
            var session = new CombatSession(new[] { player, enemy },
                new[]
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1,
                    1, 1, 30, 10, physicalMultiplier: 1f),
            },
                new TimelineSpatialQuery(),
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) });
            var service = new CombatCommandService();
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatTurnSnapshot before = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id,
                destination: new HexCoord(0, 1))).Succeeded, Is.True);
            Assert.That(session.CurrentActorId, Is.EqualTo(player.Id));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));

            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack,
                player.Id, enemy.Id, "basic")).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            Assert.That(session.PendingCtPenalty, Is.EqualTo(30));
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack,
                player.Id, enemy.Id, "basic")).Succeeded, Is.False);
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id,
                destination: new HexCoord(0, 0))).Succeeded, Is.True);
            Assert.That(player.ActionPointsRemainingUnits, Is.EqualTo(59999));
            Assert.That(session.CurrentActorId, Is.EqualTo(player.Id));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Active));
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));

            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.EndTurn, player.Id)).Succeeded, Is.True);
            CombatTurnSnapshot after = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            Assert.That(Unit(after, player.Id).ChargeTime, Is.Zero);
            Assert.That(Unit(after, player.Id).NextActionThreshold, Is.EqualTo(130f));
            Assert.That(Unit(after, player.Id).IsReady, Is.False);
            Assert.That(after.CurrentTick, Is.EqualTo(before.CurrentTick));
            Assert.That(session.CurrentActorId, Is.Empty);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
        }

        [Test]
        public void RecordedAdvanceIncludesSkippedTurnResetAndKeepsControlOrderAndTicks()
        {
            CombatSession Make()
            {
                var player = CreateTimelineCombatant("player", CombatTeam.Player, 100);
                player.ApplyStatus(CombatStatusKind.Stun, 1);
                return new CombatSession(new[] { player, CreateTimelineCombatant("enemy", CombatTeam.Enemy, 50) },
                    Array.Empty<CombatAttackProfile>(), new TimelineSpatialQuery(), TimelineOrdinaryActions());
            }
            var observed = Make(); var control = Make(); var service = new CombatCommandService();
            for (int action = 0; action < 8; action++)
            {
                CombatTurnSnapshot before = observed.TurnScheduler.GetSnapshot(observed.Combatants.All);
                var advance = service.AdvanceUntilAction(observed, out var steps);
                var expected = service.AdvanceUntilAction(control);
                Assert.That((advance.ActorId, advance.TicksElapsed), Is.EqualTo((expected.ActorId, expected.TicksElapsed)));
                AssertSameSchedulerSnapshot(before, steps[0]);
                AssertSameSchedulerSnapshot(control.TurnScheduler.GetSnapshot(control.Combatants.All), steps[steps.Count - 1]);
                Assert.That(steps[steps.Count - 1].CurrentTick - steps[0].CurrentTick, Is.EqualTo(advance.TicksElapsed));
                Assert.That(((IList<CombatTurnSnapshot>)steps).IsReadOnly, Is.True);
                for (int index = 1; index < steps.Count; index++)
                {
                    int ticks = steps[index].CurrentTick - steps[index - 1].CurrentTick;
                    if (ticks == 0) continue;
                    foreach (var unit in steps[index].Units)
                        Assert.That(unit.ChargeTime, Is.EqualTo(Unit(steps[index - 1], unit.Id).ChargeTime + ticks * unit.Speed));
                }
                if (action == 0)
                {
                    Assert.That(steps.Select(step => step.CurrentTick), Is.EqualTo(new[] { 0, 1, 1, 2 }));
                    Assert.That(steps.Select(step => Unit(step, "player").ChargeTime), Is.EqualTo(new[] { 0f, 100f, 0f, 100f }));
                }
                Assert.That(service.Execute(observed, new CombatCommand(CombatCommandKind.EndTurn, advance.ActorId)).Succeeded, Is.True);
                Assert.That(service.Execute(control, new CombatCommand(CombatCommandKind.EndTurn, expected.ActorId)).Succeeded, Is.True);
            }
        }

        [Test]
        public void RecordedAdvanceRetainsDeathBoundaryEvenWhenNoNextActorExists()
        {
            var player = CreateTimelineCombatant("player", CombatTeam.Player, 100);
            player.ApplyStatus(CombatStatusKind.DamageOverTime, 1, 100);
            var session = new CombatSession(new[] { player, CreateTimelineCombatant("enemy", CombatTeam.Enemy, 50) },
                Array.Empty<CombatAttackProfile>(), new TimelineSpatialQuery(), TimelineOrdinaryActions());
            var result = new CombatCommandService().AdvanceUntilAction(session, out var steps);
            Assert.That(result.HasActor, Is.False);
            Assert.That(result.TicksElapsed, Is.EqualTo(1));
            Assert.That(steps.Select(step => step.CurrentTick), Is.EqualTo(new[] { 0, 1, 1 }));
            Assert.That(Unit(steps[1], "player").ChargeTime, Is.EqualTo(100f));
            Assert.That(steps[2].Units.Select(unit => unit.Id), Is.EqualTo(new[] { "enemy" }));
        }

        [UnityTest] public IEnumerator FormalCtPlaybackAndInputLockAt1920() => VerifyCtPlayback(1920, 1080);
        [UnityTest] public IEnumerator FormalCtPlaybackAndInputLockAt1366() => VerifyCtPlayback(1366, 768);

        [UnityTest]
        public IEnumerator SameCtAndPredictionRemainReadableAtBothResolutions()
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "q2-timeline"));
            Directory.CreateDirectory(directory);
            foreach (var size in new[] { (1920, 1080), (1366, 768) })
            {
                // Set resolution before loading UI: resizing an existing batch GameView retains old font meshes.
                GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(size.Item1, size.Item2);
                for (int frame = 0; frame < 8; frame++) yield return null;
                timelineFixture = new GuanzhongCombatActionPreviewPlayModeTests();
                yield return timelineFixture.EnterFormalEncounter();
                FindTimelineOwner<EncounterCoordinator>().StopAllCoroutines();
                var hud = FindTimelineOwner<CombatHudPresenter>();
                var view = FindTimelineOwner<CombatTimelineView>();
                var actual = hud.Snapshot.Timeline;
                var sameCt = new CombatTimelineSnapshot(actual.CurrentTick, actual.CurrentActorId,
                    actual.Entries.Select(unit => new CombatTimelineEntrySnapshot(unit.Id, unit.DisplayName,
                        unit.Faction, unit.Speed, 100, 100, true, unit.QueuePosition, unit.IsCurrent)).ToArray());
                view.Present(sameCt);
                view.PresentPrediction(hud.Snapshot.Actions.First(action => action.CanConfirm));
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(GetTimelineField<GameObject>(view, "predictionRoot").activeInHierarchy, Is.True);
                var diagnostics = new List<string>();
                foreach (Text text in GetTimelineField<GameObject>(view, "root").GetComponentsInChildren<Text>())
                {
                    var settings = (TextGenerationSettings)typeof(TextGenerator).GetField("m_LastSettings",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(text.cachedTextGenerator);
                    Texture texture = text.mainTexture;
                    diagnostics.Add(text.name + " text=" + text.text.Replace('\n', '|') + "; currentScale=" + text.pixelsPerUnit +
                        "; generatedScale=" + settings.scaleFactor + "; generatedFont=" + settings.fontSize +
                        "; texture=" + texture.width + "x" + texture.height + "; vertices=" + text.canvasRenderer.GetMesh()?.vertexCount);
                    Assert.That(settings.scaleFactor, Is.EqualTo(text.pixelsPerUnit).Within(0.001f), text.name);
                }
                foreach (Image portrait in GetTimelineField<Image[]>(view, "entryPortraits"))
                    diagnostics.Add(portrait.transform.parent.name + "; rect=" + ScreenRect(portrait.rectTransform) +
                        "; sprite=" + portrait.sprite + "; override=" + portrait.overrideSprite + "; color=" + portrait.color +
                        "; enabled=" + portrait.enabled + "; cull=" + portrait.canvasRenderer.cull +
                        "; alpha=" + portrait.canvasRenderer.GetAlpha() + "; vertices=" + portrait.canvasRenderer.GetMesh()?.vertexCount +
                        "; texture=" + portrait.mainTexture.name + "; uv=" + string.Join(",", portrait.canvasRenderer.GetMesh().uv) +
                        "; colors=" + string.Join(",", portrait.canvasRenderer.GetMesh().colors32));
                File.WriteAllLines(Path.Combine(directory, size.Item1 + "-same-ct-render.txt"), diagnostics);
                ScreenCapture.CaptureScreenshot(Path.Combine(directory, size.Item1 + "-same-ct-and-preview.png"));
                // Capture first: preferred-size queries in geometry checks can touch Unity's dynamic font cache.
                yield return null; yield return null;
                AssertTimelineGeometry(view);
                yield return timelineFixture.Cleanup();
                timelineFixture = null;
            }
        }

        [UnityTest]
        public IEnumerator ViewReplaysRecordedSkipResetAndDeathRemoval()
        {
            timelineFixture = new GuanzhongCombatActionPreviewPlayModeTests();
            yield return timelineFixture.EnterFormalEncounter();
            // The isolated trace owns the view here; stop the fixture's live HUD refresh.
            FindTimelineOwner<EncounterCoordinator>().StopAllCoroutines();
            var view = FindTimelineOwner<CombatTimelineView>();
            var actual = FindTimelineOwner<CombatHudPresenter>().Snapshot.Timeline;
            CombatTimelineSnapshot Project(CombatTurnSnapshot step) => new CombatTimelineSnapshot(step.CurrentTick, null,
                step.Units.Select(unit => new CombatTimelineEntrySnapshot(unit.Id, unit.Id,
                    unit.Id == "player" ? CombatUnitDisplayFaction.Player : CombatUnitDisplayFaction.Enemy,
                    unit.Speed, unit.ChargeTime, unit.NextActionThreshold, unit.IsReady,
                    step.ReadyQueueIds.ToList().IndexOf(unit.Id), false)).ToArray());
            foreach (bool death in new[] { false, true })
            {
                var player = CreateTimelineCombatant("player", CombatTeam.Player, 100);
                player.ApplyStatus(death ? CombatStatusKind.DamageOverTime : CombatStatusKind.Stun, 1, death ? 100 : 0);
                var isolated = new CombatSession(new[] { player, CreateTimelineCombatant("enemy", CombatTeam.Enemy, 50) },
                    Array.Empty<CombatAttackProfile>(), new TimelineSpatialQuery(), TimelineOrdinaryActions());
                var advance = new CombatCommandService().AdvanceUntilAction(isolated, out var recorded);
                var steps = recorded.Select(Project).ToArray();
                var last = steps[steps.Length - 1];
                view.Present(new CombatTimelineSnapshot(last.CurrentTick, advance.ActorId, last.Entries, steps));
                bool sawResetSegment = false;
                float? enemyLane = null;
                int frames = 0;
                while (view.IsPresenting && frames++ < 20000)
                {
                    var shown = GetTimelineField<CombatTimelineSnapshot>(view, "snapshot");
                    float tick = GetTimelineField<float>(view, "displayedTick");
                    var ct = GetTimelineField<float[]>(view, "displayedCt");
                    var roots = GetTimelineField<RectTransform[]>(view, "entryRoots");
                    for (int index = 0; index < shown.Entries.Count; index++)
                    {
                        var unit = shown.Entries[index];
                        Assert.That(ct[index], Is.EqualTo(unit.CurrentCt + (tick - shown.CurrentTick) * unit.Speed).Within(0.002));
                        if (unit.Id == "player" && shown.CurrentTick == 1 && unit.CurrentCt == 0 && tick > 1)
                        {
                            sawResetSegment = true;
                            Assert.That(ct[index], Is.LessThan(100), "The second segment must start from the recorded reset.");
                        }
                        if (unit.Id == "enemy")
                        {
                            enemyLane ??= roots[index].anchoredPosition.x;
                            Assert.That(roots[index].anchoredPosition.x, Is.EqualTo(enemyLane.Value));
                        }
                    }
                    yield return null;
                }
                Assert.That(view.IsPresenting, Is.False);
                Assert.That(GetTimelineField<CombatTimelineSnapshot>(view, "snapshot").Entries.Select(unit => unit.Id),
                    Is.EqualTo(last.Entries.Select(unit => unit.Id)));
                Assert.That(sawResetSegment, Is.EqualTo(!death));
                if (death) Assert.That(GetTimelineField<RectTransform[]>(view, "entryRoots")[1].gameObject.activeSelf, Is.False);
            }
            view.Present(actual);
        }

        private IEnumerator VerifyCtPlayback(int width, int height)
        {
            timelineFixture = new GuanzhongCombatActionPreviewPlayModeTests();
            yield return timelineFixture.EnterFormalEncounter();
            GuanzhongCombatActionPreviewPlayModeTests.SetGameSize(width, height);
            yield return null; yield return null;
            Assert.That((Screen.width, Screen.height), Is.EqualTo((width, height)));
            var coordinator = FindTimelineOwner<EncounterCoordinator>();
            var session = GetTimelineField<CombatSession>(coordinator, "session");
            var hud = FindTimelineOwner<CombatHudPresenter>();
            var view = FindTimelineOwner<CombatTimelineView>();
            var bar = FindTimelineOwner<CombatActionBarView>();
            var roots = GetTimelineField<RectTransform[]>(view, "entryRoots");
            var buttons = GetTimelineField<Button[]>(view, "entryButtons");
            var original = hud.Snapshot.Timeline;
            var fixedPositions = roots.Select(root => root.anchoredPosition).ToArray();
            var frozen = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            for (int frame = 0; frame < 5; frame++) yield return null;
            AssertSameSchedulerSnapshot(frozen, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(roots.Select(root => root.anchoredPosition), Is.EqualTo(fixedPositions), "An open turn cannot drift.");
            var stale = hud.Snapshot.Actions.First(action => action.Candidate.Kind == CombatActionKind.EndTurn).Candidate;
            coordinator.RequestEndTurn("player");
            for (float deadline = Time.realtimeSinceStartup + 10; !view.IsPresenting && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.That(view.IsPresenting, Is.True);
            var destination = hud.Snapshot.Timeline;
            Assert.That(destination.AdvanceSteps.Count, Is.GreaterThanOrEqualTo(2));
            string State() => session.TurnSequence + ":" + session.StateVersion + ":" + session.CurrentActorId + ":" +
                session.TurnScheduler.CurrentTick + ":" + string.Join(";", session.Combatants.All.Select(unit =>
                    unit.Id + "," + unit.CurrentHealth + "," + unit.CurrentSpirit + "," + unit.ActionPointsRemainingUnits));
            string locked = State();
            var lines = new List<string>();
            int frames = 0, pictures = 0;
            float lastCapture = -1;
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "q2-timeline"));
            Directory.CreateDirectory(directory);
            while (view.IsPresenting && ReferenceEquals(GetTimelineField<CombatTimelineSnapshot>(view, "playbackEnd"), destination))
            {
                Assert.That(coordinator.IsPresenting && hud.Snapshot.IsPresenting, Is.True);
                Assert.That(hud.Snapshot.AcceptsCommands || hud.CanInspect, Is.False);
                Assert.That(coordinator.CanWithdraw(out _), Is.False);
                Assert.That(buttons.All(button => !button.interactable), Is.True);
                coordinator.RequestWait("player");
                coordinator.ConfirmAction(stale);
                bar.WaitButton.onClick.Invoke();
                hud.InspectTarget("enemy");
                Assert.That(State(), Is.EqualTo(locked), "Commands cannot be accepted or cached during CT playback.");
                var shown = GetTimelineField<CombatTimelineSnapshot>(view, "snapshot");
                float tick = GetTimelineField<float>(view, "displayedTick");
                var ct = GetTimelineField<float[]>(view, "displayedCt");
                foreach (var item in shown.Entries.Select((unit, index) => (unit, index)))
                {
                    var start = destination.AdvanceSteps[0].Entries.Single(unit => unit.Id == item.unit.Id);
                    Assert.That(ct[item.index], Is.EqualTo(start.CurrentCt + (tick - destination.AdvanceSteps[0].CurrentTick) * start.Speed).Within(0.002));
                    lines.Add("frame=" + frames + ", tick=" + tick + ", id=" + item.unit.Id + ", ct=" + ct[item.index] +
                        ", speed=" + item.unit.Speed + ", threshold=" + item.unit.NextActionThreshold +
                        ", local=" + roots[item.index].anchoredPosition + ", screen=" + ScreenRect(roots[item.index]));
                }
                AssertTimelineGeometry(view);
                if (Time.realtimeSinceStartup - lastCapture > 0.12f)
                {
                    ScreenCapture.CaptureScreenshot(Path.Combine(directory, width + "-advance-" + pictures++ + ".png"));
                    lastCapture = Time.realtimeSinceStartup;
                }
                if (frames == 0)
                {
                    var keyboard = GetTimelineField<Keyboard>(timelineFixture, "keyboard");
                    EventSystem.current.SetSelectedGameObject(bar.WaitButton.gameObject);
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Enter, Key.Space));
                    InputSystem.QueueStateEvent(GetTimelineField<Mouse>(timelineFixture, "mouse"),
                        new MouseState { position = ScreenRect(bar.WaitButton.GetComponent<RectTransform>()).center, buttons = 1 });
                }
                if (frames == 1)
                {
                    InputSystem.QueueStateEvent(GetTimelineField<Keyboard>(timelineFixture, "keyboard"), new KeyboardState());
                    InputSystem.QueueStateEvent(GetTimelineField<Mouse>(timelineFixture, "mouse"), new MouseState());
                }
                frames++;
                yield return null;
            }
            Assert.That(frames, Is.GreaterThan(3));
            File.WriteAllLines(Path.Combine(directory, width + "-frames.txt"), lines);
            Assert.That(GetTimelineField<float>(view, "displayedTick"), Is.EqualTo(destination.CurrentTick));
            Assert.That(GetTimelineField<float[]>(view, "displayedCt").Take(destination.Entries.Count),
                Is.EqualTo(destination.Entries.Select(unit => unit.CurrentCt)));
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, width + "-endpoint.png"));
            yield return WaitForTimelinePlayer(coordinator, session, "player");
            Assert.That(FindTimelineOwner<CombatCommandInput>().Preview, Is.Null, "Blocked input must not reopen after playback.");
            var idlePositions = roots.Select(root => root.anchoredPosition).ToArray();
            for (int frame = 0; frame < 5; frame++) yield return null;
            Assert.That(roots.Select(root => root.anchoredPosition), Is.EqualTo(idlePositions));

            // Isolated display inputs cover penalties, exact threshold, overflow, wait retention and reordered identity.
            var actual = hud.Snapshot.Timeline;
            CombatTimelineSnapshot Sample(float firstCt, float firstThreshold, bool reverse = false, int count = 3) =>
                new CombatTimelineSnapshot(actual.CurrentTick, actual.CurrentActorId,
                    actual.Entries.Take(count).Select((unit, index) => new CombatTimelineEntrySnapshot(unit.Id, unit.DisplayName,
                        unit.Faction, unit.Speed, index == 0 ? firstCt : 100, index == 0 ? firstThreshold : 100,
                        index > 0, index == 0 ? -1 : index - 1, index == 0)).OrderBy(unit => reverse ? -Array.FindIndex(actual.Entries.ToArray(), item => item.Id == unit.Id) :
                            Array.FindIndex(actual.Entries.ToArray(), item => item.Id == unit.Id)).ToArray());
            view.Present(Sample(100, 130));
            Assert.That(roots[0].anchoredPosition.y, Is.LessThan(roots[1].anchoredPosition.y));
            view.Present(Sample(300, 100));
            Assert.That(roots[0].anchoredPosition.y, Is.GreaterThan(roots[1].anchoredPosition.y));
            float overflow = roots[0].anchoredPosition.y;
            view.Present(Sample(150, 100));
            Assert.That(roots[0].anchoredPosition.y, Is.LessThan(overflow).And.GreaterThan(roots[1].anchoredPosition.y));
            view.Present(Sample(100, 100));
            Assert.That(roots.Select(root => root.anchoredPosition.y).Distinct().Count(), Is.EqualTo(1));
            AssertTimelineGeometry(view);
            var sameCt = roots.Select(root => root.anchoredPosition).ToArray();
            view.Present(Sample(100, 100, true));
            Assert.That(roots.Select(root => root.anchoredPosition), Is.EqualTo(sameCt.Reverse()), "Reordering cannot exchange identities or positions.");
            view.Present(Sample(100, 100));
            for (int index = 0; index < 3; index++)
            {
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenRect(roots[index]).center }, hits);
                Assert.That(hits[0].gameObject.GetComponentInParent<Button>(), Is.EqualTo(buttons[index]));
                buttons[index].onClick.Invoke();
                Assert.That(hud.InspectedTargetId, Is.EqualTo(actual.Entries[index].Id));
            }
            view.PresentPrediction(hud.Snapshot.Actions.First(action => action.CanConfirm));
            yield return null;
            AssertTimelineGeometry(view);
            view.PresentPrediction(null);
            view.Present(new CombatTimelineSnapshot(actual.CurrentTick, null, Sample(100, 100).Entries,
                new[] { Sample(100, 100), Sample(100, 100, true) }));
            Assert.That(view.IsPresenting, Is.False, "A zero-tick ready dequeue cannot manufacture movement.");
            Assert.That(roots.Select(root => root.anchoredPosition), Is.EqualTo(sameCt));
            view.Present(Sample(100, 100, count: 2));
            Assert.That(roots[2].gameObject.activeSelf, Is.False, "Death removes the old slot.");
            view.Present(destination);
            Assert.That(view.IsPresenting, Is.True);
            view.Hide();
            Assert.That(view.IsPresenting, Is.False);
            yield return null;
            Assert.That(roots.All(root => !root.gameObject.activeSelf), Is.True);
            view.Present(actual);
            Assert.That(view.IsPresenting, Is.False);
            view.Present(destination);
            view.enabled = false;
            Assert.That(view.IsPresenting, Is.False);
            view.enabled = true;
            view.Present(actual);
            DiagnoseFormalTimelineRects(view, "q2-" + width);
            for (int frame = 0; frame < 4; frame++) yield return null;
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        private static void AssertTimelineGeometry(CombatTimelineView view)
        {
            Canvas.ForceUpdateCanvases();
            var roots = GetTimelineField<RectTransform[]>(view, "entryRoots").Where(root => root.gameObject.activeInHierarchy).ToArray();
            for (int index = 0; index < roots.Length; index++)
            {
                Rect rect = ScreenRect(roots[index]);
                Assert.That(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= Screen.width && rect.yMax <= Screen.height, Is.True);
                foreach (var other in roots.Skip(index + 1)) Assert.That(rect.Overlaps(ScreenRect(other)), Is.False);
            }
            foreach (Text text in GetTimelineField<GameObject>(view, "root").GetComponentsInChildren<Text>())
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 0.1f), text.name);
            var prediction = GetTimelineField<GameObject>(view, "predictionRoot");
            if (prediction.activeInHierarchy)
                foreach (var root in roots) Assert.That(ScreenRect(root).Overlaps(ScreenRect((RectTransform)prediction.transform)), Is.False);
        }

        private static CombatOrdinaryActionDefinition[] TimelineOrdinaryActions() => new[]
        {
            new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0),
            new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60),
        };

        private static CombatTurnUnitSnapshot Unit(CombatTurnSnapshot snapshot, string id) => snapshot.Units.Single(value => value.Id == id);

        private static void AssertSameSchedulerSnapshot(CombatTurnSnapshot expected, CombatTurnSnapshot actual)
        {
            Assert.That(actual.CurrentTick, Is.EqualTo(expected.CurrentTick));
            Assert.That(actual.ReadyQueueIds, Is.EqualTo(expected.ReadyQueueIds));
            Assert.That(actual.ChargingUnitIds, Is.EqualTo(expected.ChargingUnitIds));
            Assert.That(actual.Units.Select(value => (value.Id, value.Speed, value.ChargeTime, value.NextActionThreshold, value.IsReady)),
                Is.EqualTo(expected.Units.Select(value => (value.Id, value.Speed, value.ChargeTime, value.NextActionThreshold, value.IsReady))));
        }

        private static CombatantSnapshot CreateTimelineCombatant(string id, CombatTeam team, int speed, int movePoints = 0) =>
            new CombatantSnapshot(id,
                team,
                new HexCoord(team == CombatTeam.Player ? 0 : 1, 0),
                speed,
                100,
                100,
                10,
                10,
                10,
                10,
                100000,
                movePoints * 1000,
                basicAttackProfileId: "basic");

        private sealed class TimelineSpatialQuery : ICombatSpatialQuery
        {
            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange) =>
                new CombatRangeQueryResult(source.Distance(target) >= minimumRange && source.Distance(target) <= maximumRange, "target_out_of_range");
            public CombatMovementQueryResult QueryMovement(HexCoord source, HexCoord destination, int budget,
                int v, IReadOnlyCollection<HexCoord> occupied) => new CombatMovementQueryResult(
                    source.Distance(destination) == 1 && v > 0 && budget >= 20000 && !occupied.Contains(destination),
                    string.Empty, new[] { destination }, new[] { new SpatialMovementEdgeCost(source, destination, v / 5, 0) }, 1000, 20000);
            public IReadOnlyDictionary<HexCoord, int> FindReachable(HexCoord source, int movementPoints,
                int movementEfficiencyMilliUnits,
                IReadOnlyCollection<HexCoord> occupied) => new Dictionary<HexCoord, int>();
        }
    }
}
