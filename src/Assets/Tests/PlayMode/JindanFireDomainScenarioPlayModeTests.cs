using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.Adventure;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace TianZhang.Tests.PlayMode
{
    public sealed class JindanFireDomainScenarioPlayModeTests
    {
        private const string ScenePath = "Assets/Tests/Scenes/JindanFireDomainScenario.unity";
        private Scene original, fixture;
        private JindanFireDomainScenarioController c;
        private int originalWidth, originalHeight;
        private FullScreenMode originalMode;
#if UNITY_EDITOR
        private EditorWindow evidenceGame, previousWindow;
        private PropertyInfo sizeSelection;
        private object sizeGroup;
        private int previousSize, addedSize;
        private bool gameWasOpen;
#endif
        private Vector2Int P(int q, int r) => new Vector2Int(q, r);
        private FireDomainSnapshot S => c.Capture();
        private FireDomainCell Cell(int q, int r) => S.cells.Single(x => x.coordinate == P(q, r));
        private FireDomainUnit Unit(string id) => S.units.Single(u => u.id == id);

        [UnitySetUp]
        public IEnumerator LoadPersistedScene()
        {
            original = SceneManager.GetActiveScene();
            originalWidth = Screen.width; originalHeight = Screen.height; originalMode = Screen.fullScreenMode;
#if UNITY_EDITOR
            yield return PrepareGameView();
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            Assert.Fail("This fixture requires its Editor scene-loading entry.");
#endif
            fixture = SceneManager.GetSceneByPath(ScenePath); Assert.IsTrue(fixture.isLoaded);
            SceneManager.SetActiveScene(fixture);
            c = fixture.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<JindanFireDomainScenarioController>()).Single();
            Assert.AreEqual("JD_COMBO_FIRE_DOMAIN_01_SCENE_FIXTURE", c.Configuration.fixtureId);
            Assert.AreEqual(19, S.cells.Length); Assert.AreEqual(3, c.UnitMarkerCount);
            Assert.AreEqual(60, S.spirit); Assert.AreEqual(6, S.actions);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreAndUnload()
        {
            var owner = c == null ? null : c.State;
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (fixture.IsValid() && fixture.isLoaded) yield return SceneManager.UnloadSceneAsync(fixture);
            if (owner != null) AssertCleared(owner.Capture());
            Assert.IsFalse(SceneManager.GetSceneByPath(ScenePath).isLoaded);
            Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<JindanFireDomainScenarioController>());
            Assert.AreEqual(original, SceneManager.GetActiveScene());
#if UNITY_EDITOR
            RestoreGameView();
#endif
            Screen.SetResolution(originalWidth, originalHeight, originalMode);
            TestContext.WriteLine("CLEANUP: fixture unloaded; original active scene restored; no controller or live capacity remains.");
        }

        private void Trace(string id) => TestContext.WriteLine(id + " " + JsonUtility.ToJson(S));
        private void Start(FireDomainConfig cfg = null)
        {
            if (cfg != null) c.ResetFixture(cfg);
            Assert.IsTrue(c.Apply("manifest"), c.State.LastFailure);
            Assert.IsTrue(c.Apply("open"), c.State.LastFailure);
        }
        private void SameFailure(string op, string reason)
        {
            string before = JsonUtility.ToJson(S);
            Assert.IsFalse(c.Apply(op)); Assert.AreEqual(reason, c.State.LastFailure);
            Assert.AreEqual(before, JsonUtility.ToJson(S)); Trace("REJECT " + op + " " + reason);
        }
        private void Cycle() { Assert.IsTrue(c.Apply("cycle"), c.State.LastFailure); }
        private static void AssertCleared(FireDomainSnapshot s)
        {
            Assert.IsTrue(s.ended); Assert.AreEqual(0, s.SurfaceUsed); Assert.AreEqual(0, s.EnvironmentUsed);
            Assert.AreEqual("", s.sourceRef); Assert.AreEqual("", s.sourceMapping); Assert.AreEqual("", s.domainMapping);
            Assert.AreEqual(0, s.propagation); Assert.IsFalse(s.cells.Any(x => x.tailPaid));
        }
        private void CheckProjection()
        {
            c.Refresh();
            foreach (var u in S.units)
            {
                var marker = c.transform.GetComponentsInChildren<RectTransform>().Single(t => t.name == "Unit_" + u.id);
                Assert.AreEqual(JindanFireDomainScenarioController.BoardPosition(u.coordinate) + new Vector2(0, 47), marker.anchoredPosition);
                Assert.AreEqual(u.id + " HP " + u.hp, marker.GetComponentInChildren<Text>().text);
                StringAssert.Contains(u.id + "=" + u.hp, c.PanelText);
            }
            foreach (var cell in S.cells)
            {
                var marker = c.GetComponentsInChildren<RectTransform>().Single(t => t.name == "Cell_" + cell.coordinate.x + "_" + cell.coordinate.y);
                Assert.AreEqual(JindanFireDomainScenarioController.BoardPosition(cell.coordinate), marker.anchoredPosition);
                string phase = cell.normal > 0 ? "FIRE " + cell.normal : cell.ember > 0 ? "EMBER " + cell.ember : "";
                StringAssert.Contains(phase, marker.GetComponentInChildren<Text>().text);
                if (cell.normal > 0) Assert.AreEqual(new Color(.9f, .24f, .07f), marker.GetComponent<Image>().color);
            }
        }

        [UnityTest]
        public IEnumerator E02_DefaultAndActiveSupportPaySeparately()
        {
            var before = JsonUtility.ToJson(S);
            var first = c.Preview("manifest"); var second = c.Preview("manifest");
            CollectionAssert.AreEqual(first.Nodes, second.Nodes); Assert.AreEqual(before, JsonUtility.ToJson(S));
            Assert.IsTrue(c.Commit(first)); Assert.AreEqual(54, S.spirit); Assert.AreEqual(5, S.actions);
            var a = c.Preview("open"); var b = c.Preview("open");
            CollectionAssert.AreEqual(a.Nodes, b.Nodes); Assert.IsTrue(c.Commit(a));
            Assert.AreEqual(47, S.spirit); Assert.AreEqual(4, S.actions);
            Assert.AreEqual(3, S.SurfaceUsed); Assert.AreEqual(1, S.EnvironmentUsed);
            Assert.AreEqual(S.comboInstanceId, S.sourceMapping); Assert.AreEqual(S.comboInstanceId, S.domainMapping);
            Assert.AreEqual(12, S.fuelPaid); Assert.AreEqual(8, S.propagation); CheckProjection(); Trace("E02 default");
            var cfg = c.Configuration; cfg.naturalSupport = false; Start(cfg);
            Assert.AreEqual(45, S.spirit); Assert.AreEqual(3, S.actions); Assert.AreEqual(2, S.supportPaid);
            Assert.AreEqual(12, S.fuelPaid); Trace("E02 active support"); yield return null;
        }

        [UnityTest]
        public IEnumerator E03_E13_DeterministicCyclesAndFourRealGameViewFrames()
        {
            Start(); Trace("E03 opening"); yield return Screenshot("opening");
            var plan = c.Preview("cycle");
            CollectionAssert.AreEqual(new[] { P(-1, -1), P(0, -1) }, plan.Nodes);
            Assert.IsTrue(c.Commit(plan)); Assert.AreEqual(39, S.spirit); Assert.AreEqual(6, S.propagation);
            Assert.AreEqual(5, S.Reached); Assert.AreEqual(2, S.maintenancePaid); Assert.AreEqual(4, S.expansionPaid);
            Assert.AreEqual(5, S.tailPaid); Assert.AreEqual(20, S.fuelPaid); Assert.AreEqual(4, Cell(-1, -1).normal);
            Trace("E03 cycle1"); yield return Screenshot("expansion");
            Assert.IsTrue(c.Counter(P(-1, -1), "extinguish")); Assert.AreEqual(4, S.SurfaceUsed);
            Trace("E13 extinguished"); yield return Screenshot("gap");
            // Reset this independent E03 branch before validating the unmodified deterministic prefix.
            c.ResetFixture(); Start(); Cycle();
            plan = c.Preview("cycle"); CollectionAssert.AreEqual(new[] { P(0, -2), P(1, -2) }, plan.Nodes);
            Assert.IsTrue(c.Commit(plan)); Assert.AreEqual(31, S.spirit); Assert.AreEqual(4, S.propagation);
            Assert.AreEqual(7, S.Reached); Assert.AreEqual(28, S.fuelPaid); Trace("E03 cycle2");
            plan = c.Preview("cycle"); Assert.AreEqual(0, plan.Nodes.Count); Assert.AreEqual(2, plan.Maintenance);
            Assert.IsTrue(c.Commit(plan)); Assert.AreEqual(29, S.spirit); Assert.AreEqual(4, S.propagation); Trace("E03 cycle3");
            c.AttackCenter(10); Cycle(); Cycle(); Cycle();
            Assert.IsTrue(S.cells.Any(x => x.ember > 0)); Assert.AreEqual(29, S.spirit);
            Trace("E13 ending finite embers"); yield return Screenshot("ending");
        }

        private IEnumerator Screenshot(string stage)
        {
            CheckProjection(); Canvas.ForceUpdateCanvases(); yield return null; yield return null;
#if UNITY_EDITOR
            var canvas = c.GetComponentInChildren<Canvas>();
            var corners = new Vector3[4];
            c.GetComponentsInChildren<RectTransform>().Single(t => t.name == "FixtureStatus").GetWorldCorners(corners);
            var previewCorners = new Vector3[4]; var buttonCorners = new Vector3[4];
            c.GetComponentsInChildren<RectTransform>().Single(t => t.name == "FixturePreview").GetWorldCorners(previewCorners);
            c.GetComponentsInChildren<RectTransform>().Single(t => t.name == "Input_source_exit").GetWorldCorners(buttonCorners);
            Assert.Greater(previewCorners[0].x, buttonCorners[2].x, "Preview must remain outside the input controls.");
            TestContext.WriteLine("VIEWPORT " + stage + " Screen=" + Screen.width + "x" + Screen.height
                + " GameView=" + evidenceGame.position + " Canvas=" + canvas.renderMode + "/" + canvas.scaleFactor
                + " root=" + ((RectTransform)canvas.transform).rect + " reference=" + canvas.GetComponent<CanvasScaler>().referenceResolution
                + " statusCorners=" + string.Join("/", corners.Select(p => p.ToString("F2")))
                + " previewCorners=" + string.Join("/", previewCorners.Select(p => p.ToString("F2")))
                + " buttonCorners=" + string.Join("/", buttonCorners.Select(p => p.ToString("F2"))));
            Assert.AreEqual(new Vector2Int(1920, 1080), new Vector2Int(Screen.width, Screen.height));
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/JindanFireDomain/U/" + stage + ".png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);
            ScreenCapture.CaptureScreenshot(path);
            for (int i = 0; i < 120 && !File.Exists(path); i++) { evidenceGame.Repaint(); yield return null; }
            Assert.IsTrue(File.Exists(path), "Real Game view PNG missing: " + path);
            Assert.Greater(new FileInfo(path).Length, 1000); Trace("SCREENSHOT " + stage);
#endif
        }

#if UNITY_EDITOR
        // Establish the rendering context before loading UI; restore it once after scene teardown.
        private IEnumerator PrepareGameView()
        {
            var gameType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
            previousWindow = EditorWindow.focusedWindow;
            gameWasOpen = Resources.FindObjectsOfTypeAll<EditorWindow>().Any(w => w.GetType() == gameType);
            evidenceGame = EditorWindow.GetWindow(gameType);
            sizeSelection = gameType.GetProperty("selectedSizeIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            previousSize = (int)sizeSelection.GetValue(evidenceGame);
            var sizesType = gameType.Assembly.GetType("UnityEditor.GameViewSizes");
            var sizes = sizesType.BaseType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            sizeGroup = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { 0 });
            addedSize = (int)sizeGroup.GetType().GetMethod("GetTotalCount").Invoke(sizeGroup, null);
            var size = Activator.CreateInstance(gameType.Assembly.GetType("UnityEditor.GameViewSize"),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { Enum.ToObject(gameType.Assembly.GetType("UnityEditor.GameViewSizeType"), 1), 1920, 1080, "Fire domain evidence" }, null);
            sizeGroup.GetType().GetMethod("AddCustomSize").Invoke(sizeGroup, new[] { size });
            sizeSelection.SetValue(evidenceGame, addedSize);
            evidenceGame.Show(); evidenceGame.Focus(); evidenceGame.Repaint(); yield return null; yield return null;
            for (int i = 0; i < 60 && (Screen.width != 1920 || Screen.height != 1080); i++) { evidenceGame.Repaint(); yield return null; }
            Assert.AreEqual(new Vector2Int(1920, 1080), new Vector2Int(Screen.width, Screen.height));
        }

        private void RestoreGameView()
        {
            sizeSelection.SetValue(evidenceGame, previousSize);
            sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup, new object[] { addedSize });
            Assert.AreEqual(addedSize, sizeGroup.GetType().GetMethod("GetTotalCount").Invoke(sizeGroup, null));
            if (!gameWasOpen) evidenceGame.Close(); if (previousWindow != null) previousWindow.Focus();
        }
#endif

        [UnityTest]
        public IEnumerator E04_AllFactionsEnterStayCrossAndBypassWithRealHpAndOccupancy()
        {
            foreach (string id in new[] { "Friend", "Enemy", "Neutral" })
            {
                c.ResetFixture(); Start();
                if (id == "Neutral") { Assert.IsTrue(c.Move(id, P(-2, 1))); Assert.IsTrue(c.Move(id, P(-1, 1))); }
                var target = id == "Friend" ? P(0, 0) : id == "Enemy" ? P(1, 0) : P(-1, 0);
                Assert.IsTrue(c.Move(id, target)); Assert.AreEqual(26, Unit(id).hp);
                c.Refresh(); c.Preview("cycle"); c.Preview("cycle"); Assert.AreEqual(26, Unit(id).hp);
                Cycle(); Assert.AreEqual(24, Unit(id).hp); CheckProjection(); Trace("E04 " + id);
            }
            c.ResetFixture(); Start(); Assert.IsTrue(c.Move("Enemy", P(2, 0)));
            Assert.IsTrue(c.Move("Friend", P(0, 0))); Assert.IsTrue(c.Move("Friend", P(1, 0)));
            Assert.IsTrue(c.Move("Friend", P(1, 1))); Assert.AreEqual(22, Unit("Friend").hp);
            string before = JsonUtility.ToJson(S); Assert.IsFalse(c.Move("Friend", P(2, 0)));
            Assert.AreEqual("unit_occupied", c.State.LastFailure); Assert.AreEqual(before, JsonUtility.ToJson(S)); Trace("E04 crossing and occupied");
            c.ResetFixture(); Start(); c.Move("Enemy", P(2, 0));
            Assert.IsTrue(c.Move("Friend", P(-1, 2))); Assert.IsTrue(c.Move("Friend", P(0, 2)));
            Assert.IsTrue(c.Move("Friend", P(1, 1))); Assert.AreEqual(30, Unit("Friend").hp); CheckProjection(); Trace("E04 bypass");
            yield return null;
        }

        [UnityTest]
        public IEnumerator E05_GapIsImmediateAndReignitionRequiresNextPaidCycle()
        {
            Start(); Cycle(); c.Counter(P(-1, -1), "extinguish");
            Assert.AreEqual(4, S.SurfaceUsed); Assert.IsFalse(Cell(-1, -1).tailPaid);
            c.Move("Friend", P(-1, 1)); c.Move("Friend", P(-1, 0)); int hp = Unit("Friend").hp;
            Assert.IsTrue(c.Move("Friend", P(-1, -1))); Assert.AreEqual(hp, Unit("Friend").hp);
            c.Preview("cycle"); c.Refresh(); Assert.AreEqual(0, Cell(-1, -1).normal);
            var plan = c.Preview("cycle"); Assert.AreEqual(P(-1, -1), plan.Nodes[0]);
            var before = S; Assert.IsTrue(c.Commit(plan));
            Assert.AreEqual(4, Cell(-1, -1).normal); Assert.AreEqual(4, Cell(-1, -1).fuel);
            Assert.AreEqual(before.expansionPaid + 4, S.expansionPaid); Assert.AreEqual(before.tailPaid + 2, S.tailPaid);
            Assert.AreEqual(before.propagation - 2, S.propagation); Trace("E05 paid reignition");
            var cfg = c.Configuration; cfg.spirit = 25; Start(cfg); Cycle(); c.Counter(P(-1, -1), "extinguish");
            Assert.AreEqual(4, S.spirit); Cycle(); Assert.AreEqual(2, S.spirit);
            Assert.AreEqual(0, Cell(-1, -1).normal); Assert.AreEqual(4, S.expansionPaid); Trace("E05 insufficient reignition"); yield return null;
        }

        [UnityTest]
        public IEnumerator E06_WaterIsolationAndFuelBreakAreLocalAndCannotJump()
        {
            foreach (string kind in new[] { "water", "isolate", "fuel" })
            {
                c.ResetFixture(); Start(); c.Counter(P(-1, -1), kind);
                Assert.IsFalse(c.Preview("cycle").Nodes.Contains(P(-1, -1))); Cycle(); Assert.AreEqual(0, Cell(-1, -1).normal);
                Assert.AreEqual(2, S.expansionPaid / 2); Assert.AreEqual(20, S.fuelPaid);
                int surface = S.SurfaceUsed; c.Counter(P(0, 0), kind);
                Assert.AreEqual(surface - 1, S.SurfaceUsed); Assert.AreEqual(0, Cell(0, 0).ember); Assert.IsFalse(Cell(0, 0).tailPaid);
                Assert.IsNotEmpty(S.comboInstanceId); Trace("E06 " + kind);
            }
            var cfg = c.Configuration;
            foreach (var cell in cfg.cells) cell.isolated = !cfg.initial.Contains(cell.coordinate) && cell.coordinate != P(2, 0);
            Start(cfg); c.Counter(P(-1, 0), "fuel"); c.Counter(P(1, 0), "fuel");
            Assert.AreEqual(0, c.Preview("cycle").Nodes.Count); Cycle();
            Assert.AreEqual(0, Cell(2, 0).normal); Assert.AreEqual(0, S.expansionPaid); Assert.AreEqual(12, S.fuelPaid);
            Assert.AreEqual(2, S.maintenancePaid); Trace("E06 disconnected far fuel"); yield return null;
        }

        [UnityTest]
        public IEnumerator E07_CoverageCapacityPropagationAndWholeNodePaymentLimitIndependently()
        {
            Start(); Assert.AreEqual(2, c.Preview("cycle").Nodes.Count); Cycle(); Cycle();
            Assert.AreEqual(7, S.Reached); Assert.AreEqual(0, c.Preview("cycle").Nodes.Count);
            c.Counter(P(-1, 0), "extinguish"); var plan = c.Preview("cycle");
            Assert.AreEqual(1, plan.Nodes.Count); Assert.AreEqual(P(-1, 0), plan.Nodes[0]); Cycle(); Assert.AreEqual(7, S.Reached); Trace("E07 reached cap");
            foreach (string limit in new[] { "budget", "capacity", "one_node" })
            {
                var cfg = c.Configuration;
                if (limit == "budget") cfg.propagation = 0;
                if (limit == "capacity") cfg.surfaceCapacity = 3;
                if (limit == "one_node") cfg.spirit = 18;
                Start(cfg); Cycle();
                Assert.AreEqual(limit == "one_node" ? 1 : 0, S.expansionPaid / 2);
                Assert.AreEqual(limit == "one_node" ? 16 : 12, S.fuelPaid);
                if (limit == "capacity") StringAssert.Contains("Surface 3/3", c.PanelText);
                if (limit == "one_node") { Assert.AreEqual(0, S.spirit); Assert.AreEqual(7, S.propagation); Assert.AreEqual(4, Cell(-1, -1).normal); }
                Trace("E07 " + limit);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator E08_EveryStartupFailureIsAtomicAndLeavesEarlierPaidSource()
        {
            Action<FireDomainConfig>[] mutations = { x => x.actions = 0, x => x.spirit = 5, x => x.cells.Single(v => v.coordinate == P(0, 0)).fuel = 3,
                x => x.surfaceCapacity = 2, x => x.localRecipe = "", x => x.sourceEffect = "", x => x.breakPolicy = "",
                x => x.landing = P(3, 0), x => x.initial[2] = P(2, -2), x => { x.naturalSupport = false; x.spirit = 7; } };
            string[] reasons = { "actions_insufficient", "spirit_insufficient", "initial_carrier_or_fuel", "surface_capacity", "invalid_profile",
                "invalid_profile", "invalid_profile", "invalid_initial_geometry", "invalid_initial_geometry", "spirit_insufficient" };
            for (int i = 0; i < mutations.Length; i++) { var cfg = c.Configuration; mutations[i](cfg); c.ResetFixture(cfg); SameFailure("manifest", reasons[i]); }
            mutations = new Action<FireDomainConfig>[] { x => x.actions = 1, x => x.spirit = 12, x => x.centerHp = 0,
                x => x.domainSeatReserved = true, x => x.environmentCapacity = 0, x => x.center = P(5, 5) };
            reasons = new[] { "actions_insufficient", "spirit_insufficient", "invalid_center", "domain_occupied", "environment_capacity", "invalid_center" };
            for (int i = 0; i < mutations.Length; i++)
            {
                var cfg = c.Configuration; mutations[i](cfg); c.ResetFixture(cfg); Assert.IsTrue(c.Apply("manifest")); SameFailure("open", reasons[i]);
                Assert.AreEqual(3, S.SurfaceUsed); Assert.AreEqual(6, S.manifestPaid); Assert.AreEqual(0, S.tailPaid);
                Assert.AreEqual(0, S.EnvironmentUsed); Cycle(); Assert.AreEqual(3, Cell(0, 0).normal);
            }
            c.ResetFixture(); Assert.IsTrue(c.TryOccupy("source")); SameFailure("manifest", "source_occupied");
            c.ResetFixture(); Assert.IsTrue(c.Apply("manifest")); Assert.IsTrue(c.TryOccupy("domain")); SameFailure("open", "domain_occupied");
            c.ResetFixture(); SameFailure("open", "missing_source"); yield return null;
        }

        [UnityTest]
        public IEnumerator E09_MaintenanceAndExpansionFailSeparatelyWithoutFreeRenewal()
        {
            foreach (int balance in new[] { 1, 4 })
            {
                var cfg = c.Configuration; cfg.spirit = 13 + balance; Start(cfg); Cycle();
                Assert.AreEqual(balance == 1 ? 0 : 2, S.maintenancePaid); Assert.AreEqual(0, S.expansionPaid);
                Assert.AreEqual(3, S.tailPaid); Assert.AreEqual(3, Cell(0, 0).normal); Assert.AreEqual(8, S.propagation);
                Assert.AreEqual(12, S.fuelPaid); Trace("E09 balance " + balance);
            }
            c.ResetFixture(); Start(); c.CloseMain("close"); for (int i = 0; i < 4; i++) Cycle();
            Assert.AreEqual(2, Cell(0, 0).ember); int spirit = S.spirit; Cycle();
            Assert.AreEqual(spirit, S.spirit); Assert.AreEqual(0, S.maintenancePaid); Assert.AreEqual(0, S.expansionPaid); Trace("E09 ember only"); yield return null;
            var onlyEmberConfig = c.Configuration; onlyEmberConfig.propagation = 0; Start(onlyEmberConfig);
            for (int i = 0; i < 4; i++) Cycle();
            Assert.AreEqual(1, S.EnvironmentUsed); Assert.AreEqual(2, Cell(0, 0).ember);
            int paid = S.maintenancePaid; spirit = S.spirit; Cycle();
            Assert.AreEqual(paid, S.maintenancePaid); Assert.AreEqual(spirit, S.spirit); Trace("E09 active domain ember only");
        }

        [UnityTest]
        public IEnumerator E10_OneComboOwnsBothSeatsAndCannotDuplicate()
        {
            Start(); Assert.AreEqual(1, S.ActiveComboCount); Assert.AreEqual(S.comboInstanceId, S.sourceMapping);
            Assert.AreEqual(S.comboInstanceId, S.domainMapping); Assert.AreNotEqual(S.sourceRef, S.sourceMapping);
            SameFailure("manifest", "source_occupied"); SameFailure("open", "missing_source");
            var before = JsonUtility.ToJson(S);
            Assert.IsFalse(c.TryOccupy("source")); Assert.IsFalse(c.TryOccupy("domain")); Assert.AreEqual(before, JsonUtility.ToJson(S));
            c.CloseMain("close"); Assert.AreEqual(0, S.EnvironmentUsed); Assert.AreEqual("", S.sourceMapping); Assert.AreEqual("", S.domainMapping);
            Assert.AreEqual(3, S.SurfaceUsed); Trace("E10 single instance and release"); yield return null;
        }

        [UnityTest]
        public IEnumerator E11_FourMainExitPathsLeaveOnlyPaidFiniteFireAndSingleEmberRegistration()
        {
            foreach (string exit in new[] { "attack", "close", "source_exit", "domain_exit" })
            {
                c.ResetFixture(); Start(); Assert.IsTrue(c.Move("Friend", P(0, 0))); c.Command(exit);
                Assert.AreEqual("", S.comboInstanceId); Assert.AreEqual("", S.sourceMapping); Assert.AreEqual("", S.domainMapping);
                Assert.AreEqual(3, S.SurfaceUsed); int spirit = S.spirit;
                for (int i = 0; i < 4; i++) { Cycle(); Assert.AreEqual(3, S.SurfaceUsed); }
                Assert.AreEqual(2, Cell(0, 0).ember); Assert.AreEqual(18, Unit("Friend").hp); Trace("E11 " + exit + " ember2");
                Cycle(); Assert.AreEqual(1, Cell(0, 0).ember); Assert.AreEqual(17, Unit("Friend").hp);
                Cycle(); Assert.AreEqual(0, S.SurfaceUsed); Assert.AreEqual(16, Unit("Friend").hp);
                Assert.AreEqual(spirit, S.spirit); Assert.AreEqual(0, S.maintenancePaid); Assert.AreEqual(3, S.Reached);
                Assert.IsFalse(S.units.Any(u => u.soulDamage != 0)); Assert.IsFalse(S.cells.Any(x => x.tailPaid));
                Assert.IsTrue(S.cells.Where(x => !x.reached).All(x => !x.Registered)); CheckProjection(); Trace("E11 " + exit + " cleared");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator E12_BattleDisableAndUnloadClearIdempotentlyWithoutRefundOrHealing()
        {
            foreach (bool ember in new[] { false, true })
            {
                c.ResetFixture(); Start(); c.Move("Friend", P(0, 0));
                if (ember) { c.CloseMain("close"); for (int i = 0; i < 4; i++) Cycle(); }
                var before = S; c.EndBattle(); string after = JsonUtility.ToJson(S); c.EndBattle();
                Assert.AreEqual(after, JsonUtility.ToJson(S)); AssertCleared(S);
                Assert.AreEqual(before.spirit, S.spirit); Assert.AreEqual(before.units[0].hp, S.units[0].hp);
                SameFailure("manifest", "battle_ended"); SameFailure("open", "battle_ended"); SameFailure("cycle", "battle_ended"); Trace("E12 battle " + ember);
            }
            c.ResetFixture(); Start(); var owner = c.State; int paidSpirit = S.spirit;
            c.enabled = false; AssertCleared(owner.Capture()); Assert.AreEqual(paidSpirit, owner.Capture().spirit); c.enabled = true;
            c.ResetFixture(); Start(); owner = c.State;
            SceneManager.SetActiveScene(original); yield return SceneManager.UnloadSceneAsync(fixture);
            AssertCleared(owner.Capture()); Assert.AreEqual(47, owner.Capture().spirit);
            TestContext.WriteLine("E12 disable and actual scene unload cleared live state without refund.");
        }

        [UnityTest]
        public IEnumerator E13_StalePlanCannotExecuteOrSilentlyReplaceTargetsAndButtonsReachSameOwner()
        {
            Start(); var plan = c.Preview("cycle"); c.Counter(P(-1, -1), "water");
            var before = JsonUtility.ToJson(S); Assert.IsFalse(c.Commit(plan)); Assert.AreEqual("stale_preview", c.State.LastFailure);
            Assert.AreEqual(before, JsonUtility.ToJson(S)); Assert.IsFalse(c.Preview("cycle").Nodes.Contains(P(-1, -1)));
            var next = c.Preview("cycle"); Assert.IsTrue(c.Commit(next)); Assert.AreEqual(0, Cell(-1, -1).normal); Trace("E13 stale plan");
            c.ResetFixture();
            var button = c.GetComponentsInChildren<Button>().Single(b => b.name == "Input_manifest");
            Assert.AreEqual(1, button.onClick.GetPersistentEventCount()); button.onClick.Invoke(); Assert.AreEqual(54, S.spirit);
            c.GetComponentsInChildren<Button>().Single(b => b.name == "Input_open").onClick.Invoke(); Assert.AreEqual(47, S.spirit);
            CheckProjection(); yield return null;
        }
    }
}
