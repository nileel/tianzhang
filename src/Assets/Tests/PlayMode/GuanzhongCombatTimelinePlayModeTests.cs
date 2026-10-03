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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatTimelinePlayModeTests
    {
        private string timelineSaveDirectory;

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
                spawned.Player.MovePoints, new[] { spawned.Enemy.Position });
            Assert.That(path.IsReachable, Is.True, path.Reason);
            commands.RequestMove(spawned.Player.Id, 0, 1);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.MoveThenAction));
            AssertSameSchedulerSnapshot(beforeMove, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.EqualTo(spawned.Player.Id));
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && encounter.IsPresenting;) yield return null;
            Assert.That(encounter.IsPresenting, Is.False);
            commands.RequestGuard(spawned.Player.Id);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.ActionThenMove));
            AssertSameSchedulerSnapshot(beforeMove, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.EqualTo(spawned.Player.Id));
            int tick = session.TurnScheduler.CurrentTick;
            commands.RequestGuard(spawned.Player.Id);
            Assert.That(session.TurnScheduler.CurrentTick, Is.EqualTo(tick));
            Assert.That(captured.Latest.Timeline.CurrentActorId, Is.EqualTo(spawned.Player.Id));
            commands.RequestEndTurn(spawned.Player.Id);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.Finished));
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
                if (!encounter.IsPresenting && session.CurrentActorId == spawned.Player.Id && session.TurnPhase == CombatTurnPhase.MoveThenAction)
                    commands.RequestBasicAttack(spawned.Player.Id, spawned.Enemy.Id);
                if (!encounter.IsPresenting && session.CurrentActorId == spawned.Player.Id && session.TurnPhase == CombatTurnPhase.ActionThenMove)
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
            Assert.That(reentry.Latest.Timeline.Entries, Has.Count.EqualTo(2));
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
                if (!encounter.IsPresenting && session.CurrentActorId == id && session.TurnPhase == CombatTurnPhase.MoveThenAction) yield break;
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
                Assert.That(entry.label.preferredHeight, Is.LessThanOrEqualTo(18f), entry.label.name + " must occupy one readable line below the portrait.");
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
            var session = new CombatSession(new[] { player, enemy }, new[]
            {
                new CombatAttackProfile("basic", CombatAttackKind.Basic, CombatAttackEffect.Physical, 1, 1,
                    physicalMultiplier: 1f, cooldownTicks: 30),
            }, new TimelineSpatialQuery());
            var service = new CombatCommandService();
            Assert.That(service.AdvanceUntilAction(session).ActorId, Is.EqualTo(player.Id));
            CombatTurnSnapshot before = session.TurnScheduler.GetSnapshot(session.Combatants.All);
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id,
                destination: new HexCoord(0, 1))).Succeeded, Is.True);
            Assert.That(session.CurrentActorId, Is.EqualTo(player.Id));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.MoveThenAction));
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));

            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack,
                player.Id, enemy.Id, "basic")).Succeeded, Is.True);
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.ActionThenMove));
            Assert.That(session.PendingCooldownPenalty, Is.EqualTo(30));
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.BasicAttack,
                player.Id, enemy.Id, "basic")).Succeeded, Is.False);
            AssertSameSchedulerSnapshot(before, session.TurnScheduler.GetSnapshot(session.Combatants.All));
            Assert.That(service.Execute(session, new CombatCommand(CombatCommandKind.Move, player.Id,
                destination: new HexCoord(0, 0))).Succeeded, Is.True);
            Assert.That(player.MovePoints, Is.EqualTo(1));
            Assert.That(session.CurrentActorId, Is.EqualTo(player.Id));
            Assert.That(session.TurnPhase, Is.EqualTo(CombatTurnPhase.ActionThenMove));
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
            new CombatantSnapshot(id, team, new HexCoord(team == CombatTeam.Player ? 0 : 1, 0), speed,
                100, 100, 10, 10, 10, 10, movePoints: movePoints, basicAttackProfileId: "basic");

        private sealed class TimelineSpatialQuery : ICombatSpatialQuery
        {
            public CombatRangeQueryResult QueryRange(HexCoord source, HexCoord target, int minimumRange, int maximumRange) =>
                new CombatRangeQueryResult(source.Distance(target) >= minimumRange && source.Distance(target) <= maximumRange, "target_out_of_range");
            public CombatMovementQueryResult QueryMovement(HexCoord source, HexCoord destination, int movementPoints,
                IReadOnlyCollection<HexCoord> occupied) => new CombatMovementQueryResult(
                    source.Distance(destination) == 1 && movementPoints >= 1 && !occupied.Contains(destination),
                    string.Empty, new[] { destination }, 1);
            public IReadOnlyDictionary<HexCoord, int> FindReachable(HexCoord source, int movementPoints,
                IReadOnlyCollection<HexCoord> occupied) => new Dictionary<HexCoord, int>();
        }
    }
}
