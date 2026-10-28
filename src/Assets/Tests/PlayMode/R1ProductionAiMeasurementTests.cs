using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
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
    // R1 before/after experiment. R2 owns the eventual performance regression budgets.
    public sealed class R1ProductionAiMeasurementTests
    {
        private Mouse mouse;
        private string saves, output;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private R1FrameProbe probe;
        private readonly List<string> actions = new();
        private readonly Dictionary<string, (int count, double ms, double max)> stages = new();
        private int round;
        private FieldInfo surfaceMeasurement;

        [UnityTest, Timeout(1800000)]
        public IEnumerator TerrainProductionAi1366()
        {
            string label = Environment.GetEnvironmentVariable("TZ_R1_LABEL") ?? "after";
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/r1/", label));
            Directory.CreateDirectory(output);
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(1366, 768);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "R1-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("r1", "寻路验证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            SpatialQueryMeasurement.Completed = Measure;
            surfaceMeasurement = typeof(Static3DCombatUnitPresentationProvider).Assembly
                .GetType("TianZhang.Features.CombatPresentation.CombatUnitSurfacePose")
                .GetField("PathMeasured", BindingFlags.Static | BindingFlags.NonPublic);
            surfaceMeasurement.SetValue(null, (Action<int, int, double>)((soles, samples, ms) =>
            {
                Measure("surface_path", ms);
                File.AppendAllText(Path.Combine(output, "surface.csv"), string.Join(",", round, soles, samples,
                    ms.ToString("F4", CultureInfo.InvariantCulture)) + "\n");
            }));
            probe = new GameObject("R1 frame measurements").AddComponent<R1FrameProbe>();
            probe.Output = output;
            probe.Phase = "entry";
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            session = Get<CombatSession>(encounter, "session");
            probe.Provider = Find<Static3DCombatUnitPresentationProvider>();
            var supports = Get<IDictionary>(probe.Provider, "surfacePoses");
            File.WriteAllLines(Path.Combine(output, "soles.txt"), supports.Keys.Cast<string>().Select(key =>
            {
                var points = Get<Vector3[]>(supports[key], "soles");
                return key + " count=" + points.Length + " unique=" + points.Distinct().Count() +
                    " minY=" + points.Min(v => v.y) + " maxY=" + points.Max(v => v.y);
            }));
            Assert.AreEqual(144, Find<CombatBattlefieldSurface>().Cells.Count);
            var policies = Get<Dictionary<string, ICombatActionPolicy>>(encounter, "enemyPolicies");
            Assert.IsTrue(policies.Values.All(p => p is LegalActionAI || p is RangedActionAI));
            File.WriteAllText(Path.Combine(output, "configuration.txt"), "1366x768; seed=1729; fixed terrain; production policies=" +
                string.Join(",", policies.Select(p => p.Key + ":" + p.Value.GetType().Name)) + "\n" + State());
            yield return Ready();
            SaveStages("entry");
            probe.Phase = "approach";
            foreach (string coordinate in "3:0,2:1,2:2,2:3,2:4,2:5,2:6,2:7,3:7,4:7,5:7,6:7,7:7,8:7,8:8".Split(','))
            {
                int[] qr = coordinate.Split(':').Select(int.Parse).ToArray();
                var command = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(qr[0], qr[1]));
                if (!new CombatCommandService().Preview(session, command).IsLegal)
                { encounter.RequestEndTurn("player"); yield return Ready(); }
                Assert.IsTrue(new CombatCommandService().Preview(session, command).IsLegal, "route " + coordinate);
                encounter.RequestMove("player", qr[0], qr[1]); yield return Ready();
            }
            File.AppendAllText(Path.Combine(output, "configuration.txt"), "\nmeasurement start=" + State());
            if (Environment.GetEnvironmentVariable("TZ_R1_RECORD") == "1")
            {
                var target = Find<CombatBattlefieldSurface>().Center(new CombatUnitPresentationHex(7, 10));
                Camera.main.transform.position = target - Camera.main.transform.forward * 20;
                probe.Record = true;
            }
            for (round = 1; round <= 4; round++)
            {
                stages.Clear();
                probe.Phase = "round-" + round;
                actions.Add("start," + round + "," + State());
                yield return Click(Find<CombatActionBarView>().WaitButton);
                yield return Click(Find<CombatActionPreviewView>().ConfirmButton);
                yield return Ready();
                SaveStages("round-" + round);
                actions.Add("end," + round + "," + State());
                File.WriteAllLines(Path.Combine(output, "actions.csv"), actions);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "round-" + round + ".png"));
                probe.Phase = "between-rounds";
                yield return null;
            }
            probe.Flush();
            Assert.IsTrue(actions.Any(s => s.StartsWith("execute/enemy_2/Move,1,")), "Measured production AI must actually move.");
            Assert.IsTrue(actions.Any(s => s.StartsWith("execute/enemy/Art,2,")), "Measured production AI must actually attack.");
        }

        private void Measure(string name, double ms)
        {
            string category = name.StartsWith("execute/") ? "execute" : name;
            stages.TryGetValue(category, out var value);
            stages[category] = (value.count + 1, value.ms + ms, Math.Max(value.max, ms));
            if (name.StartsWith("execute/")) actions.Add(name + "," + round + "," + State());
        }
        private string State() => session == null ? "loading" : "tick=" + session.TurnScheduler.CurrentTick + ";actor=" + session.CurrentActorId + ";" +
            string.Join(";", session.Combatants.All.Select(a => a.Id + "@" + a.Position + "/" + a.Pose.Facing + "/hp=" + a.CurrentHealth + "/ap=" + a.ActionPointsRemainingUnits));
        private void SaveStages(string phase)
        {
            File.WriteAllLines(Path.Combine(output, phase + "-stages.csv"), new[] { "stage,count,total_ms,max_ms" }.Concat(
                stages.OrderBy(p => p.Key).Select(p => string.Join(",", p.Key, p.Value.count,
                    p.Value.ms.ToString("F4", CultureInfo.InvariantCulture), p.Value.max.ToString("F4", CultureInfo.InvariantCulture)))));
            probe.Flush();
        }
        private IEnumerator Ready()
        {
            double end = Time.realtimeSinceStartupAsDouble + 600;
            bool ReadyForInput() => encounter.AcceptsPlayerCommand && Find<CombatHudPresenter>().Snapshot.AcceptsCommands;
            do { yield return null; } while (!ReadyForInput() && Time.realtimeSinceStartupAsDouble < end);
            Assert.IsTrue(ReadyForInput(), "Production AI did not return control and refresh the action bar.");
        }
        private IEnumerator Click(Button button)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null; yield return null;
        }
        private static IEnumerator SceneReady(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);

        [UnityTearDown] public IEnumerator Cleanup()
        {
            SpatialQueryMeasurement.Completed = null;
            surfaceMeasurement?.SetValue(null, null);
            if (probe != null) { probe.Flush(); Object.Destroy(probe.gameObject); }
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldInput;
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    public sealed class R1FrameProbe : MonoBehaviour
    {
        public string Output, Phase;
        public Static3DCombatUnitPresentationProvider Provider;
        public bool Record;
        private readonly List<string> captures = new() { "file,realtime" };
        private IEnumerator Start()
        {
            double next = 0;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                double now = Time.realtimeSinceStartupAsDouble;
                if (!Record || now < next) continue;
                next = now + 1d / 30;
                string name = "frame-" + captures.Count.ToString("D5") + ".png";
                ScreenCapture.CaptureScreenshot(Path.Combine(Output, name));
                captures.Add(name + "," + now.ToString("F6", CultureInfo.InvariantCulture));
            }
        }
        private readonly List<string> frames = new() { "phase,frame,realtime_ms,delta_ms,enemy_x,enemy_y,enemy_z,enemy2_x,enemy2_y,enemy2_z" };
        private double last;
        private void Update()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (last > 0) frames.Add(string.Join(",", Phase, Time.frameCount,
                ((now - last) * 1000).ToString("F4", CultureInfo.InvariantCulture),
                (Time.unscaledDeltaTime * 1000).ToString("F4", CultureInfo.InvariantCulture), Position("enemy"), Position("enemy_2")));
            last = now;
        }
        private string Position(string id)
        {
            if (Provider == null || !Provider.TryGetPresentedUnit(id, out var unit)) return ",,";
            var p = unit.transform.position;
            return string.Join(",", p.x.ToString("F5", CultureInfo.InvariantCulture), p.y.ToString("F5", CultureInfo.InvariantCulture), p.z.ToString("F5", CultureInfo.InvariantCulture));
        }
        public void Flush()
        {
            if (Output == null) return;
            File.WriteAllLines(Path.Combine(Output, "frames.csv"), frames);
            if (Record) File.WriteAllLines(Path.Combine(Output, "captures.csv"), captures);
        }
    }
}
