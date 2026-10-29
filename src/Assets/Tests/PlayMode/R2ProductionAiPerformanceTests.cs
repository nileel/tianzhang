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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class R2ProductionAiPerformanceTests
    {
        private Mouse mouse;
        private Keyboard keyboard;
        private string saves, output;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldInput;
        private EncounterCoordinator encounter;
        private CombatSession session;
        private R2HandoffProbe probe;
        private FieldInfo surfaceMeasurement;
        private FieldInfo sampleMeasurement;
        private readonly List<(string phase, int frame, double time, string name, double ms)> stages = new();
        private readonly List<string> actions = new();
        private readonly List<R2MeasuredAction> measuredActions = new();
        private bool native;
        private int nativeRound;

        [UnityTest, Timeout(600000)] public IEnumerator InitialHandoff1366() => Run(1366, false);
        [UnityTest, Timeout(600000)] public IEnumerator EngagedHandoff1366() => Run(1366, true);
        [UnityTest, Timeout(600000)] public IEnumerator InitialHandoff1920() => Run(1920, false);
        [UnityTest, Timeout(600000)] public IEnumerator EngagedHandoff1920() => Run(1920, true);
        [UnityTest, Timeout(600000)] public IEnumerator StreamingKeepsEagerSampledTrajectory() => Run(1366, false, true);

        private IEnumerator Run(int width, bool engaged, bool compareSurface = false)
        {
            stages.Clear();
            actions.Clear(); measuredActions.Clear();
            native = Environment.GetEnvironmentVariable("TZ_R2_NATIVE") == "1";
            nativeRound = 0;
            string label = Environment.GetEnvironmentVariable("TZ_R2_LABEL") ?? "measurement";
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/r2", label,
                (compareSurface ? "surface-" : engaged ? "engaged-" : "initial-") + width));
            Directory.CreateDirectory(output);
            oldBackground = InputSystem.settings.backgroundBehavior;
            oldInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard = InputSystem.AddDevice<Keyboard>();
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1366 ? 768 : 1080);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "R2-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("r2", "回合验收");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            probe = new GameObject("R2 handoff measurements").AddComponent<R2HandoffProbe>();
            probe.Phase = "entry";
            probe.Encounter = encounter;
            SpatialQueryMeasurement.Completed = Measure;
            surfaceMeasurement = typeof(Static3DCombatUnitPresentationProvider).Assembly
                .GetType("TianZhang.Features.CombatPresentation.CombatUnitSurfacePose")
                .GetField("PathMeasured", BindingFlags.Static | BindingFlags.NonPublic);
            surfaceMeasurement.SetValue(null, (Action<int, int, double>)((soles, samples, ms) => Measure("surface_path/" + samples, ms)));
            sampleMeasurement = surfaceMeasurement.DeclaringType.GetField("SamplesMeasured", BindingFlags.Static | BindingFlags.NonPublic);
            sampleMeasurement.SetValue(null, (Action<int, double>)((count, ms) => Measure("surface_samples/" + count, ms)));
            yield return Click(GameObject.Find("TerrainRehearsalButton").GetComponent<Button>());
            session = Get<CombatSession>(encounter, "session");
            probe.Session = session;
            probe.Provider = Find<Static3DCombatUnitPresentationProvider>();
            probe.Camera = Camera.main;
            Assert.AreEqual(144, Find<CombatBattlefieldSurface>().Cells.Count);
            Assert.AreEqual(width, Screen.width);
            Assert.AreEqual(width == 1366 ? 768 : 1080, Screen.height);
            var policies = Get<Dictionary<string, ICombatActionPolicy>>(encounter, "enemyPolicies");
            Assert.IsInstanceOf<RangedActionAI>(policies["enemy"]);
            Assert.IsInstanceOf<LegalActionAI>(policies["enemy_2"]);
            yield return Ready();
            if (compareSurface)
            {
                yield return R2SurfacePlaybackComparison.Run(probe.Provider, output);
                yield break;
            }
            probe.Phase = "approach";
            if (engaged)
                foreach (string coordinate in "3:0,2:1,2:2,2:3,2:4,2:5,2:6,2:7,3:7,4:7,5:7,6:7,7:7,8:7,8:8".Split(','))
                {
                    int[] qr = coordinate.Split(':').Select(int.Parse).ToArray();
                    var command = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(qr[0], qr[1]));
                    if (!new CombatCommandService().Preview(session, command).IsLegal)
                    { encounter.RequestEndTurn("player"); yield return Ready(); }
                    Assert.IsTrue(new CombatCommandService().Preview(session, command).IsLegal, coordinate);
                    encounter.RequestMove("player", qr[0], qr[1]); yield return Ready();
                }
            File.WriteAllText(Path.Combine(output, "configuration.txt"), width + "x" + Screen.height +
                "; seed=1729; production RangedActionAI/LegalActionAI; synthetic InputSystem mouse/keyboard\n" + State());
            // Center the view before the sample, so pan has room in both directions; never reset it during a turn.
            if (engaged)
            {
                Vector3 target = Find<CombatBattlefieldSurface>().Center(new CombatUnitPresentationHex(7, 10));
                probe.Camera.transform.position = target - probe.Camera.transform.forward * 20;
            }
            if (native)
            {
                InputSystem.RemoveDevice(mouse); mouse = null;
                InputSystem.RemoveDevice(keyboard); keyboard = null;
                probe.Native = true;
                probe.Output = output;
                probe.Record = Environment.GetEnvironmentVariable("TZ_R2_RECORD") == "1";
                yield return ObserveNative(engaged ? 12 : 3);
            }
            else
            for (int round = 1; round <= (engaged ? 12 : 3); round++)
            {
                probe.Phase = "round-" + round;
                yield return Click(Find<CombatActionBarView>().WaitButton);
                yield return Click(Find<CombatActionPreviewView>().ConfirmButton, true);
                yield return Ready();
                probe.Pan = false;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                probe.Phase = "between-rounds";
                yield return null;
            }
            Flush();
            AssertActions(engaged);
            AssertBudgets(engaged);
        }

        private void Measure(string name, double ms)
        {
            if (native && probe.Native && name == "execute/player/EndTurn")
                probe.Phase = "round-" + (++nativeRound);
            stages.Add((probe.Phase, Time.frameCount, Time.realtimeSinceStartupAsDouble, name, ms));
            if (name.StartsWith("execute/"))
            {
                actions.Add(probe.Phase + "," + name + ",\"" + State() + "\"");
                string[] parts = name.Split('/');
                var action = new R2MeasuredAction { Phase = probe.Phase, Frame = Time.frameCount, Time = Time.realtimeSinceStartupAsDouble, Actor = parts[1], Kind = parts[2] };
                if (action.Actor != "player" && action.Kind is "Art" or "BasicAttack")
                {
                    string feedbackName = "GuanzhongCombatFeedback_" + (action.Kind == "Art" ? "Cast" : "Attack") + "_Vfx";
                    action.Feedback = Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)
                        .Where(p => p.name == feedbackName && !measuredActions.Any(a => a.Feedback == p)).SingleOrDefault();
                    probe.Attacks.Add(action);
                }
                measuredActions.Add(action);
            }
        }

        private IEnumerator ObserveNative(int roundCount)
        {
            probe.Phase = "native-ready";
            int completed = 0;
            File.AppendAllText(Path.Combine(output, "configuration.txt"), "\nNative sampling: virtual setup devices removed; OS Mouse/Keyboard required.\n");
            File.WriteAllText(Path.Combine(output, "native-ready.txt"), "0/" + roundCount);
            double deadline = Time.realtimeSinceStartupAsDouble + 600;
            while (completed < roundCount && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                if (nativeRound > completed && encounter.AcceptsPlayerCommand && Find<CombatHudPresenter>().Snapshot.AcceptsCommands)
                {
                    // Include the first player-ready rendered frame, then leave the timing window before disk I/O.
                    yield return null;
                    completed = nativeRound;
                    probe.Phase = "native-ready";
                    yield return null;
                    Flush();
                    File.WriteAllText(Path.Combine(output, "native-ready.txt"), completed + "/" + roundCount);
                }
            }
            Assert.AreEqual(roundCount, completed, "Native production turns were not completed.");
        }

        private void AssertActions(bool engaged)
        {
            var rounds = measuredActions.Where(a => a.Phase.StartsWith("round-")).ToArray();
            Assert.IsTrue(rounds.Any(a => a.Actor == "enemy_2" && a.Kind == "Move"), "The production double-cell enemy must move.");
            if (engaged)
            {
                Assert.IsTrue(rounds.Any(a => a.Actor == "enemy" && a.Kind == "Art"), "The production ranged enemy must cast.");
                Assert.IsTrue(rounds.Any(a => a.Actor == "enemy_2" && a.Kind == "BasicAttack"), "The production melee enemy must attack.");
            }
            foreach (var action in rounds.Where(a => a.Actor != "player" && (a.Kind == "Move" || a.Kind == "Art" || a.Kind == "BasicAttack")))
            {
                int end = measuredActions.Where(a => a.Frame > action.Frame && a.Actor == action.Actor).Select(a => a.Frame).DefaultIfEmpty(int.MaxValue).Min();
                var frames = probe.Samples.Where(f => f.Frame >= action.Frame && f.Frame < end && f.Phase == action.Phase).ToArray();
                Assert.GreaterOrEqual(frames.Count(f => f.Presenting), 3, action.Kind + " playback was skipped.");
                if (action.Kind == "Move")
                {
                    var positions = frames.Select(f => action.Actor == "enemy" ? f.Enemy : f.Enemy2).Distinct().ToArray();
                    Assert.GreaterOrEqual(positions.Length, 4, "Move must have intermediate positions, not only the endpoint.");
                }
                else Assert.GreaterOrEqual(action.FeedbackFrames, 3, action.Actor + " " + action.Kind + " emitted no visible attack feedback.");
            }
            foreach (string phase in rounds.Select(a => a.Phase).Distinct())
            {
                var pan = probe.Samples.Where(f => f.Phase == phase && f.Input != 0 && !f.UiOwns).ToArray();
                Assert.GreaterOrEqual(pan.Length, 2, phase + " must include continuous camera input away from UI.");
                Assert.IsTrue(pan.Any(f => f.CameraDelta.sqrMagnitude > .0000001f), phase + " camera did not move.");
                var confirmed = rounds.Single(a => a.Phase == phase && a.Actor == "player" && a.Kind == "EndTurn");
                if (native) Assert.LessOrEqual((pan[0].Realtime - confirmed.Time) * 1000, 60, "Native camera input missed the turn handoff.");
                else Assert.LessOrEqual(pan[0].Frame, confirmed.Frame + 1, "Camera input missed the turn handoff.");
                var window = probe.Samples.Where(f => f.Phase == phase && f.Frame >= pan[0].Frame && f.Pan);
                Assert.IsTrue(window.All(f => f.Input != 0 && !f.UiOwns), "Continuous camera input was interrupted during the handoff.");
                if (native) Assert.IsTrue(pan.All(f => f.NativeInput), "Native input evidence cannot use virtual devices.");
            }
        }
        private void AssertBudgets(bool engaged)
        {
            // Calibrated on the same R1/R2 seed, actors, route and resolutions; loading is reported separately.
            // Recording incurs image readback/encoding, so it proves native input/motion, not a timing budget.
            if (native) return;
            int[] metric = engaged ? new[] {60,70,92,36,70,92,36,70,92,36,122,70} : new[] {48,46,48};
            int[] movement = engaged ? new[] {8,4,9,3,4,10,3,4,10,3,6,4} : new[] {5,10,5};
            for (int i=0; i<metric.Length; i++)
            {
                string phase="round-"+(i+1);
                var measured=stages.Where(s=>s.phase==phase).ToArray();
                var times=probe.Samples.Where(f=>f.Phase==phase).Select(f=>f.WallMs).OrderBy(t=>t).ToArray();
                int commands=measuredActions.Count(a=>a.Phase==phase && a.Actor!="player");
                Assert.AreEqual(commands,measured.Count(s=>s.name=="decision"),phase+" repeated AI decisions");
                Assert.AreEqual(commands,measured.Count(s=>s.name=="legal_actions"),phase+" repeated legal action scans");
                Assert.LessOrEqual(measured.Count(s=>s.name=="metric_search"),metric[i],phase+" metric search budget");
                Assert.LessOrEqual(measured.Count(s=>s.name=="movement_search"),movement[i],phase+" movement search budget");
                Assert.LessOrEqual(times.Last(),40,phase+" maximum frame budget (ms)");
                Assert.LessOrEqual(times[(int)Math.Ceiling(times.Length*.95)-1],8,phase+" P95 frame budget (ms)");
                foreach(var stage in measured)
                {
                    double limit=stage.name=="decision"?12:stage.name=="hud"?20:stage.name=="legal_actions"?12:
                        stage.name.StartsWith("surface_samples/")?8:double.PositiveInfinity;
                    Assert.LessOrEqual(stage.ms,limit,phase+" "+stage.name+" CPU budget (ms)");
                }
            }
        }
        private string State() => session == null ? "loading" : "tick=" + session.TurnScheduler.CurrentTick + ";actor=" + session.CurrentActorId + ";" +
            string.Join(";", session.Combatants.All.Select(a => a.Id + "@" + a.Position + "/" + a.Pose.Facing + "/hp=" + a.CurrentHealth + "/ap=" + a.ActionPointsRemainingUnits));
        private IEnumerator Ready()
        {
            double end = Time.realtimeSinceStartupAsDouble + 120;
            bool ReadyForInput() => encounter.AcceptsPlayerCommand && Find<CombatHudPresenter>().Snapshot.AcceptsCommands;
            do { yield return null; } while (!ReadyForInput() && Time.realtimeSinceStartupAsDouble < end);
            Assert.IsTrue(ReadyForInput(), "Production AI did not return control and refresh the action bar.");
        }
        private IEnumerator Click(Button button, bool pan = false)
        {
            Canvas.ForceUpdateCanvases(); Assert.IsTrue(button.interactable && button.gameObject.activeInHierarchy, button.name);
            var point = RectTransformUtility.WorldToScreenPoint(null, button.transform.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
            if (pan)
            {
                probe.Pan = true; probe.Keyboard = keyboard; probe.PanStarted = Time.realtimeSinceStartupAsDouble;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            }
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width * .5f, Screen.height * .65f) });
            yield return null;
        }
        private void Flush()
        {
            if (output == null) return;
            File.WriteAllLines(Path.Combine(output, "stages.csv"), new[] { "phase,frame,realtime,stage,ms" }.Concat(
                stages.Select(s => string.Join(",", s.phase, s.frame, F(s.time), s.name, F(s.ms)))));
            File.WriteAllLines(Path.Combine(output, "actions.csv"), actions);
            probe?.Flush(output);
            if (probe != null)
                File.WriteAllLines(Path.Combine(output, "performance-summary.csv"), new[] { "phase,frames,max_ms,p95_ms,over_33ms,over_50ms,over_100ms" }.Concat(
                    probe.Samples.GroupBy(f => f.Phase).Select(g =>
                    {
                        double[] times = g.Select(f => f.WallMs).OrderBy(v => v).ToArray();
                        return string.Join(",", g.Key, times.Length, F(times.Last()), F(times[(int)Math.Ceiling(times.Length * .95) - 1]),
                            times.Count(v => v > 1000d / 30), times.Count(v => v > 50), times.Count(v => v > 100));
                    })));
        }
        internal static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
        private static IEnumerator SceneReady(string name)
        { for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null; yield return null; yield return null; Assert.AreEqual(name, SceneManager.GetActiveScene().name); }
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(owner, value);

        [UnityTearDown] public IEnumerator Cleanup()
        {
            SpatialQueryMeasurement.Completed = null;
            surfaceMeasurement?.SetValue(null, null);
            sampleMeasurement?.SetValue(null, null);
            Flush();
            if (probe != null) Object.Destroy(probe.gameObject);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldInput;
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }

    internal sealed class R2MeasuredAction
    {
        public string Phase, Actor, Kind;
        public int Frame;
        public double Time;
        public ParticleSystem Feedback;
        public int FeedbackFrames;
    }

    internal sealed class R2FrameSample
    {
        public string Phase, Actor;
        public int Frame;
        public double Realtime, WallMs, DeltaMs;
        public bool Presenting, Pan;
        public int Input;
        public bool UiOwns, NativeInput;
        public Vector3 Enemy, Enemy2, CameraDelta;
    }

    [DefaultExecutionOrder(10000)]
    public sealed class R2HandoffProbe : MonoBehaviour
    {
        public string Phase;
        public EncounterCoordinator Encounter;
        public CombatSession Session;
        public Static3DCombatUnitPresentationProvider Provider;
        public Camera Camera;
        public Keyboard Keyboard;
        public bool Pan;
        public double PanStarted;
        public bool Native, Record;
        public string Output;
        internal readonly List<R2FrameSample> Samples = new();
        internal readonly List<R2MeasuredAction> Attacks = new();
        private readonly List<(int frame, double eventTime, double observed, bool mouse, bool native, float left, float a, float d, Vector2 pointer)> inputs = new();
        private readonly List<string> captures = new() { "file,realtime" };
        private double last;
        private Vector3 lastCamera;
        private void Update()
        {
            if (!Pan || Keyboard == null) return;
            bool right = (int)((Time.realtimeSinceStartupAsDouble - PanStarted) / .4) % 2 == 0;
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(right ? Key.D : Key.A));
        }
        private void LateUpdate()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            Vector3 current = Camera != null ? Camera.transform.position : default;
            Vector3 delta = current - lastCamera;
            var inputKeyboard = Native ? UnityEngine.InputSystem.Keyboard.current : Keyboard;
            int input = inputKeyboard == null ? 0 : (inputKeyboard.dKey.isPressed ? 1 : 0) - (inputKeyboard.aKey.isPressed ? 1 : 0);
            bool ui = Mouse.current != null && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId);
            if (last > 0) Samples.Add(new R2FrameSample { Phase = Phase, Frame = Time.frameCount, WallMs = (now - last) * 1000,
                Presenting = Encounter != null && Encounter.IsPresenting, Enemy = UnitPosition("enemy"), Enemy2 = UnitPosition("enemy_2"),
                Realtime = now, DeltaMs = Time.unscaledDeltaTime * 1000, Actor = Session?.CurrentActorId, Pan = Native ? Phase.StartsWith("round-") : Pan,
                Input = input, UiOwns = ui, CameraDelta = delta, NativeInput = inputKeyboard?.native == true && Mouse.current?.native == true });
            foreach (var attack in Attacks)
                if (attack.Feedback != null && attack.Feedback.isPlaying && attack.Feedback.particleCount > 0 &&
                    attack.Feedback.GetComponent<ParticleSystemRenderer>().enabled) attack.FeedbackFrames++;
            last = now; lastCamera = current;
        }
        private Vector3 UnitPosition(string id) => Provider != null && Provider.TryGetPresentedUnit(id, out var unit) ? unit.transform.position : default;
        private static string F(double value) => R2ProductionAiPerformanceTests.F(value);
        private void OnEnable() => InputSystem.onEvent += ObserveInput;
        private void OnDisable() => InputSystem.onEvent -= ObserveInput;
        private void ObserveInput(InputEventPtr evt, InputDevice device)
        {
            if (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>()) return;
            if (device is Mouse m)
                inputs.Add((Time.frameCount, evt.time, Time.realtimeSinceStartupAsDouble, true, m.native,
                    m.leftButton.ReadValueFromEvent(evt, out float left) ? left : float.NaN, float.NaN, float.NaN,
                    m.position.ReadValueFromEvent(evt, out Vector2 pointer) ? pointer : new Vector2(float.NaN, float.NaN)));
            else if (device is Keyboard k)
                inputs.Add((Time.frameCount, evt.time, Time.realtimeSinceStartupAsDouble, false, k.native,
                    float.NaN, k.aKey.ReadValueFromEvent(evt, out float a) ? a : float.NaN,
                    k.dKey.ReadValueFromEvent(evt, out float d) ? d : float.NaN, new Vector2(float.NaN, float.NaN)));
        }
        private IEnumerator Start()
        {
            double next = 0;
            while (true)
            {
                yield return new WaitForEndOfFrame();
                double now = Time.realtimeSinceStartupAsDouble;
                if (!Record || Output == null || !Phase.StartsWith("round-") || now < next) continue;
                next = now + 1d / 30;
                string file = "frame-" + captures.Count.ToString("D5") + ".jpg";
                // PNG encoding itself stalled the handoff at 1920; retain full resolution with faster JPEG capture.
                Texture2D texture = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(Output, file), texture.EncodeToJPG(90));
                Destroy(texture);
                captures.Add(file + "," + F(now));
            }
        }
        public void Flush(string output)
        {
            string V(Vector3 p) => string.Join(",", F(p.x), F(p.y), F(p.z));
            File.WriteAllLines(Path.Combine(output, "frames.csv"), new[] { "phase,frame,realtime,wall_ms,delta_ms,pan,input_x,ui_owns,actor,presenting,camera_dx,camera_dy,camera_dz,enemy_x,enemy_y,enemy_z,enemy2_x,enemy2_y,enemy2_z,native" }.Concat(
                Samples.Select(f => string.Join(",", f.Phase, f.Frame, F(f.Realtime), F(f.WallMs), F(f.DeltaMs), f.Pan, f.Input, f.UiOwns,
                    f.Actor, f.Presenting, V(f.CameraDelta), V(f.Enemy), V(f.Enemy2), f.NativeInput))));
            File.WriteAllLines(Path.Combine(output, "inputs.csv"), new[] { "frame,event_time,observed_time,device,native,left,a,d,pointer_x,pointer_y" }.Concat(
                inputs.Select(i => string.Join(",", i.frame, F(i.eventTime), F(i.observed), i.mouse ? "mouse" : "keyboard", i.native,
                    F(i.left), F(i.a), F(i.d), F(i.pointer.x), F(i.pointer.y)))));
            File.WriteAllLines(Path.Combine(output, "attack-feedback.csv"), new[] { "phase,frame,actor,kind,visible_particle_frames" }.Concat(
                Attacks.Select(a => string.Join(",", a.Phase, a.Frame, a.Actor, a.Kind, a.FeedbackFrames))));
            if (Record) File.WriteAllLines(Path.Combine(output, "captures.csv"), captures);
        }
    }
}
