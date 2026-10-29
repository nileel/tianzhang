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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    // Observes the production command and presentation port. No replacement AI, time step,
    // actor relocation, asset substitution or VFX lifetime override is used.
    public sealed class R3ProjectileEvidenceTests
    {
        private EncounterCoordinator encounter;
        private CombatSession session;
        private CombatBattlefieldSurface surface;
        private string saves, output;
        private readonly List<string> route = new();
        private readonly List<string> stages = new();
        private readonly List<string> frames = new();
        private readonly List<(double time, int frame, Texture2D image)> captures = new();
        private bool sampling, record;
        private RecordingPort port;
        private readonly CombatCommandService commands = new();

        [UnityTest, Timeout(600000)] public IEnumerator Flat1366() => Run(1366, "flat");
        [UnityTest, Timeout(600000)] public IEnumerator TerrainSame1366() => Run(1366, "same");
        [UnityTest, Timeout(600000)] public IEnumerator TerrainCross1366() => Run(1366, "cross");
        [UnityTest, Timeout(600000)] public IEnumerator Flat1920() => Run(1920, "flat");
        [UnityTest, Timeout(600000)] public IEnumerator TerrainSame1920() => Run(1920, "same");
        [UnityTest, Timeout(600000)] public IEnumerator TerrainCross1920() => Run(1920, "cross");

        private IEnumerator Run(int width, string scenario)
        {
            route.Clear(); stages.Clear(); frames.Clear(); captures.Clear();
            record = Environment.GetEnvironmentVariable("TZ_R3_RECORD") == "1";
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/r3",
                Environment.GetEnvironmentVariable("TZ_R3_LABEL") ?? "measurement", scenario + "-" + width));
            Directory.CreateDirectory(output);
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, width == 1366 ? 768 : 1080);
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saves = Path.Combine(Application.temporaryCachePath, "R3-" + Guid.NewGuid().ToString("N"));
            Set(GameBootstrap.RequireInstance(), "slotStore", new GameSaveSlotStore(saves));
            var creation = Find<CharacterCreationController>();
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_minor_clan")); creation.Submit("r3", "飞星取证");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.IsTrue(Find<WorldMapController>().SelectNode("guanzhong_hub"));
            Find<WorldMapController>().EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            encounter = Find<EncounterCoordinator>();
            encounter.SetCombatResolutionRandomSource(new SystemCombatResolutionRandomSource(1729));
            var adventure = Find<AdventureController>();
            for (int i = 0; i < 180 && !adventure.CanSelectExplorationNode; i++) yield return null;
            Assert.IsTrue(adventure.CanSelectExplorationNode);
            if (scenario == "flat")
            {
                adventure.SetEncounterRandomSource(new EncounterRandom());
                Assert.IsTrue(Find<AdventureInputController>().SelectNode("lieshijia_encounter"));
            }
            // R3 measures VFX, not native input. Invoke the actual terrain button handler
            // after its exploration precondition instead of relying on synthetic click timing.
            else Find<AdventureSceneInstaller>().BeginTerrainRehearsal();
            session = Get<CombatSession>(encounter, "session");
            surface = Object.FindFirstObjectByType<CombatBattlefieldSurface>();
            if (scenario != "flat") Assert.AreEqual(144, surface.Cells.Count);
            var provider = Find<Static3DCombatUnitPresentationProvider>();
            port = new RecordingPort(provider);
            Set(encounter, "unitPresentation", port);
            SpatialQueryMeasurement.Completed = (name, ms) =>
            { if (sampling) stages.Add(string.Join(",", Time.frameCount, F(Time.realtimeSinceStartupAsDouble), name, F(ms))); };
            yield return Ready();

            // Follow an already validated land route for a cross-height shot; for same-height
            // and flat shots let the unchanged production enemies approach naturally.
            string[] landRoute = "3:0,2:1,2:2,2:3,2:4,2:5,2:6,2:7,3:7,4:7,5:7,6:7,7:7,8:7,8:8".Split(',');
            CombatantSnapshot target = null;
            int next = 0;
            for (int step = 0; step < 50 && target == null; step++)
            {
                route.Add(State());
                target = session.Combatants.All.Where(a => a.Id != "player" && a.IsAlive)
                    .FirstOrDefault(a => Eligible(a, scenario));
                if (target != null) break;
                if (scenario == "cross" && next < landRoute.Length)
                {
                    int[] qr = landRoute[next].Split(':').Select(int.Parse).ToArray();
                    var move = new CombatCommand(CombatCommandKind.Move, "player", destination: new HexCoord(qr[0], qr[1]));
                    if (commands.Preview(session, move).IsLegal)
                    { encounter.RequestMove("player", qr[0], qr[1]); next++; }
                    else encounter.RequestEndTurn("player");
                }
                else encounter.RequestEndTurn("player");
                yield return Ready();
            }
            File.WriteAllLines(Path.Combine(output, "route.txt"), route);
            Assert.IsNotNull(target, "No legal " + scenario + " shot along the production approach.");
            Assert.AreEqual(width, Screen.width);
            Assert.AreEqual(width == 1366 ? 768 : 1080, Screen.height);
            session.Combatants.TryGet("player", out var player);
            var contact = session.SpatialQuery.QueryRange(player.Pose, target.Pose, 1, 5);
            Vector3 from = World(contact.SourceCell), to = World(contact.TargetCell);
            Camera camera = Camera.main;
            Vector3 focus = (from + to) * .5f;
            var plane = new Plane(Vector3.up, focus);
            if (plane.Raycast(camera.ViewportPointToRay(new Vector3(.5f, .5f)), out float distance))
            {
                Vector3 delta = focus - camera.ViewportPointToRay(new Vector3(.5f, .5f)).GetPoint(distance);
                camera.transform.position += new Vector3(delta.x, 0, delta.z);
            }
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture();
            string before = State();
            var candidate = new CombatActionCandidate(CombatActionKind.Art, "player", target.Id, "spell_feixingzhi",
                Find<CombatHudPresenter>().Snapshot.CommandContextVersion);
            var quote = encounter.PreviewAction(candidate);
            Assert.IsTrue(quote.CanConfirm, quote.Reason);
            sampling = true;
            double commandAt = Time.realtimeSinceStartupAsDouble;
            int commandFrame = Time.frameCount;
            encounter.ConfirmAction(candidate);
            double returnedAt = Time.realtimeSinceStartupAsDouble;
            var effect = port.Effect;
            Assert.IsNotNull(effect, "Production Cast must create the projectile.");
            Assert.AreEqual(.5f, Get<float>(effect, "duration"));
            Vector3 actualFrom = Get<Vector3>(effect, "from"), actualTo = Get<Vector3>(effect, "to");
            Assert.Less(Vector3.Distance(actualFrom, from + Vector3.up * .85f), .0001f);
            Assert.Less(Vector3.Distance(actualTo, to + Vector3.up * .75f), .0001f);
            var head = Get<Transform>(effect, "head");
            var tail = Get<List<Transform>>(effect, "tail");
            Material material = head.GetComponent<Renderer>().sharedMaterial;
            string commandRecord = "scenario=" + scenario + ";seed=1729;record=" + record + ";timeScale=" + F(Time.timeScale) +
                ";captureDeltaTime=" + F(Time.captureDeltaTime) + "\n" + before + "\n" + State() +
                "\nsource=" + contact.SourceCell + ";target=" + contact.TargetCell + ";from=" + V(actualFrom) + ";to=" + V(actualTo) +
                "\ncommandFrame=" + commandFrame + ";commandAt=" + F(commandAt) + ";providerBefore=" + F(port.Before) +
                ";providerAfter=" + F(port.After) + ";returnedAt=" + F(returnedAt) +
                "\nshader=" + material.shader.name + ";supported=" + material.shader.isSupported + ";texture=" + material.mainTexture.name +
                ";color=" + material.color + ";camera=" + V(camera.transform.position) + ";orthographicSize=" + F(camera.orthographicSize);
            Assert.IsTrue(material.shader.isSupported);
            Sample(effect, head, tail, "created");
            int flying = 0, impactFrames = 0;
            var positions = new HashSet<Vector3>();
            while (effect != null && Time.realtimeSinceStartupAsDouble - returnedAt < 5)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                if (effect == null) break;
                Sample(effect, head, tail, "rendered");
                Vector3 screen = camera.WorldToViewportPoint(head.position);
                if (!Get<bool>(effect, "impacted") && Get<float>(effect, "elapsed") > 0 && head.gameObject.activeInHierarchy &&
                    head.GetComponent<Renderer>().enabled && tail.All(t => t.gameObject.activeInHierarchy && t.GetComponent<Renderer>().enabled) &&
                    screen.z > 0 && screen.x > 0 && screen.x < 1 && screen.y > 0 && screen.y < 1)
                { flying++; positions.Add(head.position); }
                else if (Get<bool>(effect, "impacted")) impactFrames++;
                Capture();
            }
            sampling = false;
            frames.Add(string.Join(",", "destroyed", Time.frameCount, F(Time.realtimeSinceStartupAsDouble), F(Time.deltaTime)));
            yield return new WaitForEndOfFrame(); Capture();
            File.WriteAllText(Path.Combine(output, "command.txt"), commandRecord);
            File.WriteAllText(Path.Combine(output, "summary.txt"), "flyingFrames=" + flying + ";distinctHeadPositions=" + positions.Count +
                ";impactFrames=" + impactFrames + ";destroyedAt=" + F(Time.realtimeSinceStartupAsDouble));
            Flush();
            Assert.IsTrue(effect == null, "Projectile must finish its original lifetime.");
            Assert.GreaterOrEqual(flying, 3, "Flight cannot be accepted from object existence alone.");
            Assert.GreaterOrEqual(positions.Count, 3, "Head must advance through the trajectory.");
            Assert.Greater(impactFrames, 0);
            Assert.IsTrue(head == null && tail.All(t => t == null), "Unparented head/tail must be cleaned up.");
        }

        private bool Eligible(CombatantSnapshot target, string scenario)
        {
            if (!commands.Preview(session, new CombatCommand(CombatCommandKind.Art, "player", target.Id, "spell_feixingzhi")).IsLegal) return false;
            session.Combatants.TryGet("player", out var player);
            var contact = session.SpatialQuery.QueryRange(player.Pose, target.Pose, 1, 5);
            float difference = Mathf.Abs(World(contact.SourceCell).y - World(contact.TargetCell).y);
            return scenario == "cross" ? difference > .4f : difference < .01f;
        }
        private Vector3 World(HexCoord cell) => surface != null
            ? surface.Center(new CombatUnitPresentationHex(cell.Q, cell.R))
            : Static3DCombatUnitPresentationProvider.HexToWorld(new CombatUnitPresentationHex(cell.Q, cell.R));
        private void Sample(CombatAttackVfx effect, Transform head, List<Transform> tail, string phase)
        {
            Vector3 screen = Camera.main.WorldToScreenPoint(head.position);
            frames.Add(string.Join(",", phase, Time.frameCount, F(Time.realtimeSinceStartupAsDouble), F(Time.deltaTime),
                F(Get<float>(effect, "elapsed")), Get<bool>(effect, "impacted"), head.gameObject.activeInHierarchy,
                tail.Count(t => t.gameObject.activeInHierarchy), head.GetComponent<Renderer>().enabled,
                V(head.position), V(screen), V(tail[0].position), V(tail[3].position)));
        }
        private void Capture()
        {
            if (record && (captures.Count == 0 || Time.realtimeSinceStartupAsDouble - captures.Last().time >= 1d / 60))
                captures.Add((Time.realtimeSinceStartupAsDouble, Time.frameCount, ScreenCapture.CaptureScreenshotAsTexture()));
        }
        private void Flush()
        {
            if (output == null) return;
            File.WriteAllLines(Path.Combine(output, "stages.csv"), new[] { "frame,realtime,stage,ms" }.Concat(stages));
            File.WriteAllLines(Path.Combine(output, "frames.csv"), new[] {
                "phase,frame,realtime,delta_s,elapsed,impacted,head_active,tail_active_count,renderer_enabled,head_x,head_y,head_z,screen_x,screen_y,screen_z,tail0_x,tail0_y,tail0_z,tail3_x,tail3_y,tail3_z" }.Concat(frames));
            if (captures.Count == 0) return;
            File.WriteAllLines(Path.Combine(output, "captures.csv"), new[] { "file,realtime,frame" }.Concat(captures.Select((c, i) => "frame-" + i.ToString("D4") + ".jpg," + F(c.time) + "," + c.frame)));
            for (int i = 0; i < captures.Count; i++)
            {
                File.WriteAllBytes(Path.Combine(output, "frame-" + i.ToString("D4") + ".jpg"), captures[i].image.EncodeToJPG(95));
                Object.Destroy(captures[i].image);
            }
            captures.Clear();
        }
        private string State() => "tick=" + session.TurnScheduler.CurrentTick + ";actor=" + session.CurrentActorId + ";" +
            string.Join(";", session.Combatants.All.Select(a => a.Id + "@" + a.Position + "/hp=" + a.CurrentHealth + "/ap=" + a.ActionPointsRemainingUnits));
        private IEnumerator Ready()
        {
            double until = Time.realtimeSinceStartupAsDouble + 60;
            do { yield return null; } while (encounter.IsRunning && !encounter.AcceptsPlayerCommand && Time.realtimeSinceStartupAsDouble < until);
            Assert.IsTrue(encounter.AcceptsPlayerCommand, State());
        }
        private static IEnumerator SceneReady(string name)
        {
            for (int i = 0; i < 300 && SceneManager.GetActiveScene().name != name; i++) yield return null;
            yield return null; yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
        }
        private static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
        private static string V(Vector3 value) => string.Join(",", F(value.x), F(value.y), F(value.z));
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>() ?? throw new Exception(typeof(T).Name + " missing");
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);

        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }

        private sealed class RecordingPort : ICombatUnitPresentationPort, ICombatPresentationActivity
        {
            private readonly Static3DCombatUnitPresentationProvider actual;
            public CombatAttackVfx Effect;
            public double Before, After;
            public RecordingPort(Static3DCombatUnitPresentationProvider provider) => actual = provider;
            public bool IsPresenting => actual.IsPresenting;
            public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> values) => actual.Prepare(values);
            public void Spawn(CombatUnitPresentationDescriptor value) => actual.Spawn(value);
            public void Present(CombatUnitPresentationEventProjection value)
            {
                bool star = value.ActorCombatantId == "player" && value.ProfileId == "spell_feixingzhi" && value.PresentationEvent == CombatUnitPresentationEvent.Cast;
                if (star) Before = Time.realtimeSinceStartupAsDouble;
                actual.Present(value);
                if (!star) return;
                After = Time.realtimeSinceStartupAsDouble;
                Effect = GameObject.Find(CombatAttackVfx.ProjectileObjectName)?.GetComponent<CombatAttackVfx>();
            }
            public void Remove(string id) => actual.Remove(id);
            public void Clear() => actual.Clear();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            sampling = false; SpatialQueryMeasurement.Completed = null; Flush();
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) Object.Destroy(bootstrap.gameObject);
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            if (saves != null && Directory.Exists(saves)) Directory.Delete(saves, true);
        }
    }
}
