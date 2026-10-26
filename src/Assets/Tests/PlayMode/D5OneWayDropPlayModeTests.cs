using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.Adventure;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class D5OneWayDropPlayModeTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string ActorId = "demo_20261024_fire";
        private readonly D5JindanEntryPlayModeTests entry = new D5JindanEntryPlayModeTests();
        private Static3DCombatUnitPresentationProvider provider;
        private int oldCaptureRate;
        private bool captureRateChanged;

        [UnityTest, Timeout(180000)]
        public IEnumerator FormalHumanDropsOnDeclaredEdgeWithoutPenetrationOrTilt()
        {
            yield return InvokeEntry("Setup", false);
            yield return InvokeEntry("OpenPreparation");
            yield return InvokeEntry("Press", "stage2");
            yield return InvokeEntry("Press", "enter");
            entry.GetType().GetMethod("Bind", Instance).Invoke(entry, null);
            yield return InvokeEntry("Ready", new object[] { "demo_20261024_earth" });
            var encounter = Object.FindFirstObjectByType<EncounterCoordinator>();
            encounter.StopAllCoroutines();
            provider = Object.FindFirstObjectByType<Static3DCombatUnitPresentationProvider>();
            var surface = Read<CombatBattlefieldSurface>(provider, "surface");
            var session = encounter.CurrentSession;
            Assert.IsTrue(session.Combatants.TryGet(ActorId, out var actor));
            int tick = session.TurnScheduler.CurrentTick, ap = actor.ActionPointsRemainingUnits, spirit = actor.CurrentSpirit;
            long turn = session.TurnSequence;
            var originalCell = actor.Position;

            var high = new CombatUnitPresentationHex(9, 9);
            var low = new CombatUnitPresentationHex(9, 8);
            Func<CombatUnitPresentationHex, CombatUnitPresentationHex, bool> isDrop = surface.IsOneWayDrop;
            Assert.IsTrue(isDrop(high, low));
            Assert.IsFalse(isDrop(low, high));
            var marked = new List<string>();
            foreach (var cell in surface.Cells)
                for (int direction = 0; direction < 6; direction++)
                {
                    var from = new CombatUnitPresentationHex(cell.q, cell.r);
                    var to = CombatUnitPresentationPose.Step(from, direction);
                    if (isDrop(from, to)) marked.Add(from.Q + ":" + from.R + ">" + to.Q + ":" + to.R);
                }
            CollectionAssert.AreEqual(new[] { "9:9>9:8" }, marked);

            Assert.IsTrue(provider.TryGetPresentedUnit(ActorId, out GameObject root));
            var support = ((IDictionary)Read<object>(provider, "surfacePoses"))[ActorId];
            var unit = ((IDictionary)Read<object>(provider, "units"))[ActorId];
            string profileId = (string)unit.GetType().GetProperty("ProfileId").GetValue(unit);
            Assert.IsTrue(Read<Static3DCombatUnitPresentationProfileSet>(provider, "profileSet").TryGetProfile(profileId, out var profile));
            Assert.AreEqual("combat_player_default_v1", profileId);
            Assert.IsFalse(Read<bool>(support, "pair"));
            const int facing = 2;
            float yaw = profile.sixDirectionYawDegrees[facing];
            Assert.AreEqual(210, yaw);
            Vector3 highCenter = surface.Center(high), lowCenter = surface.Center(low);
            Vector3 start = Evaluate(support, highCenter, yaw);
            Vector3 finish = Evaluate(support, lowCenter, yaw);
            support.GetType().GetMethod("Place", Instance).Invoke(support, new object[] { root.transform, highCenter, yaw });
            var body = root.transform.GetChild(0);
            Vector3[] vertices = body.GetComponentsInChildren<MeshFilter>().SelectMany(filter => filter.sharedMesh.vertices
                .Select(vertex => root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex)))).ToArray();
            Vector3[] soles = Read<Vector3[]>(support, "soles");
            Assert.That(Vector3.Distance(body.localScale, Vector3.one * 1.4f), Is.LessThan(.0001f));

            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d5/one-way-drop"));
            Directory.CreateDirectory(output);
            var report = new Report { actor = ActorId, profile = profileId, prefab = profile.prefab.name,
                root = Hierarchy(root.transform), surface = Hierarchy(surface.transform), bodyScale = body.localScale,
                vertexCount = vertices.Length, soleCount = soles.Length, dropEdges = marked.ToArray(),
                start = start, finish = finish, yaw = yaw, speed = Static3DCombatUnitPresentationProvider.MovementCellsPerSecond };
            report.expectedHorizontal = HorizontalDistance(start, finish);
            report.expectedVertical = start.y - finish.y;
            report.expectedPath = report.expectedHorizontal + report.expectedVertical;
            report.expectedSeconds = report.expectedPath / report.speed;
            report.startFeet = Feet(root.transform, soles, surface);
            var samples = new List<string> { "frame,time,x,y,z,tiltDegrees,yawErrorDegrees,penetration,horizontalTravel,verticalTravel,pathTravel,chordTravel,supportedVertices" };
            var camera = Camera.main;
            Assert.IsNotNull(camera);
            camera.transform.position = (highCenter + lowCenter) * .5f - camera.transform.forward * 20;
            Object.FindFirstObjectByType<JindanDemoView>().GetButton("toggle_panel").onClick.Invoke();
            oldCaptureRate = Time.captureFramerate;
            captureRateChanged = true;
            Time.captureFramerate = 60;
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "00-high-rest.png"));

            var step = new CombatUnitPresentationMovementStep(new CombatUnitPresentationPose(high, facing),
                new CombatUnitPresentationPose(low, facing));
            Type motion = typeof(Static3DCombatUnitPresentationProvider).Assembly.GetType(
                "TianZhang.Features.CombatPresentation.CombatUnitPresentationMotion", true);
            var animation = (IEnumerator)motion.GetMethod("AnimateStep", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { root.transform, step, profile.sixDirectionYawDegrees, report.speed, surface, support });
            bool completed = false, horizontalShot = false, verticalShot = false;
            Vector3 previous = root.transform.position;
            float began = Time.time;
            Observe(0);
            provider.StartCoroutine(Play());
            for (int frame = 1; Time.time - began < 3; frame++)
            {
                yield return null;
                yield return new WaitForEndOfFrame();
                Observe(frame);
                if (!horizontalShot && report.horizontalTravel >= report.expectedHorizontal * .4f)
                { ScreenCapture.CaptureScreenshot(Path.Combine(output, "01-horizontal-clearance.png")); horizontalShot = true; }
                if (!verticalShot && report.verticalTravel >= report.expectedVertical * .3f)
                { ScreenCapture.CaptureScreenshot(Path.Combine(output, "02-vertical-drop.png")); verticalShot = true; }
                if (completed) break;
            }
            report.seconds = Time.time - began;
            report.completed = completed;
            report.finalPositionError = Vector3.Distance(root.transform.position, finish);
            report.endFeet = Feet(root.transform, soles, surface);
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "03-low-rest.png"));
            File.WriteAllLines(Path.Combine(output, "frames.csv"), samples);
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("D5_ONE_WAY_DROP " + JsonUtility.ToJson(report));
            yield return null;

            Assert.IsTrue(completed, "The actual AnimateStep coroutine must finish.");
            Assert.Greater(report.frames, 5, "The proof must observe the motion across real frames.");
            Assert.Greater(report.supportedVertexChecks, 0);
            Assert.Less(report.maxPenetration, .002f, "Actual mesh vertices must not enter any finite support surface.");
            Assert.Less(report.maxTiltDegrees, .1f);
            Assert.Less(report.maxYawErrorDegrees, .1f);
            Assert.Less(report.maxPathDeviation, .002f, "Clear horizontally at the upper level, then descend over the destination.");
            Assert.Less(report.maxScaleDeviation, .0001f);
            Assert.Less(report.finalPositionError, .002f);
            Assert.AreEqual(report.expectedHorizontal, report.horizontalTravel, .002f);
            Assert.AreEqual(report.expectedVertical, report.verticalTravel, .002f);
            Assert.AreEqual(report.expectedPath, report.pathTravel, .002f);
            Assert.That(report.seconds, Is.InRange(report.expectedSeconds - 1f / 60, report.expectedSeconds + 2f / 60));
            AssertFeet(report.startFeet); AssertFeet(report.endFeet);
            Assert.IsTrue(horizontalShot && verticalShot);
            Assert.AreEqual(tick, session.TurnScheduler.CurrentTick);
            Assert.AreEqual(turn, session.TurnSequence);
            Assert.AreEqual(ap, actor.ActionPointsRemainingUnits);
            Assert.AreEqual(spirit, actor.CurrentSpirit);
            Assert.AreEqual(originalCell, actor.Position, "This targeted carrier proof must not run enemy AI or issue a core move.");

            IEnumerator Play() { yield return animation; completed = true; }
            void Observe(int frame)
            {
                Vector3 position = root.transform.position;
                report.frames = frame + 1;
                report.horizontalTravel += HorizontalDistance(previous, position);
                report.verticalTravel += Mathf.Abs(position.y - previous.y);
                report.pathTravel = report.horizontalTravel + report.verticalTravel;
                report.chordTravel += Vector3.Distance(previous, position);
                previous = position;
                float tilt = Vector3.Angle(root.transform.up, Vector3.up);
                float yawError = Mathf.Abs(Mathf.DeltaAngle(root.transform.eulerAngles.y, yaw));
                report.maxTiltDegrees = Mathf.Max(report.maxTiltDegrees, tilt);
                report.maxYawErrorDegrees = Mathf.Max(report.maxYawErrorDegrees, yawError);
                report.maxScaleDeviation = Mathf.Max(report.maxScaleDeviation, Vector3.Distance(body.localScale, report.bodyScale));
                float horizontalRemaining = HorizontalDistance(position, finish);
                float pathError = horizontalRemaining > .002f ? Mathf.Abs(position.y - start.y) : horizontalRemaining;
                report.maxPathDeviation = Mathf.Max(report.maxPathDeviation, pathError);
                float penetration = 0; int hits = 0;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = root.transform.TransformPoint(vertex);
                    if (!surface.TryHeight(point, out float height)) continue;
                    hits++;
                    float depth = height - point.y;
                    penetration = Mathf.Max(penetration, depth);
                    if (depth > report.maxPenetration)
                    { report.maxPenetration = depth; report.worstPoint = point; report.worstSupportY = height; report.worstFrame = frame; }
                }
                report.supportedVertexChecks += hits;
                samples.Add(string.Join(",", frame.ToString(), F(Time.time - began), F(position.x), F(position.y), F(position.z),
                    F(tilt), F(yawError), F(penetration), F(report.horizontalTravel), F(report.verticalTravel), F(report.pathTravel), F(report.chordTravel), hits.ToString()));
            }
        }

        private static Vector3 Evaluate(object support, Vector3 center, float yaw)
        {
            object[] arguments = { center, yaw, Vector3.zero, Quaternion.identity };
            support.GetType().GetMethod("Evaluate", Instance).Invoke(support, arguments);
            return (Vector3)arguments[2];
        }
        private static FootContact Feet(Transform root, Vector3[] soles, CombatBattlefieldSurface surface)
        {
            var result = new FootContact { leftGap = float.PositiveInfinity, rightGap = float.PositiveInfinity };
            foreach (Vector3 sole in soles)
            {
                Vector3 point = root.TransformPoint(sole);
                if (!surface.TryHeight(point, out float height)) continue;
                if (sole.x < 0) { result.leftHits++; result.leftGap = Mathf.Min(result.leftGap, point.y - height); }
                else { result.rightHits++; result.rightGap = Mathf.Min(result.rightGap, point.y - height); }
            }
            return result;
        }
        private static void AssertFeet(FootContact value)
        {
            Assert.Greater(value.leftHits, 0); Assert.Greater(value.rightHits, 0);
            Assert.Less(Mathf.Abs(value.leftGap), .002f); Assert.Less(Mathf.Abs(value.rightGap), .002f);
        }
        private IEnumerator InvokeEntry(string name, params object[] arguments) => (IEnumerator)entry.GetType().GetMethod(name, Instance).Invoke(entry, arguments);
        private static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Instance).GetValue(target);
        private static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        private static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
        private static string Hierarchy(Transform value) => value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name;
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (provider != null) provider.StopAllCoroutines();
            if (captureRateChanged) Time.captureFramerate = oldCaptureRate;
            yield return entry.Cleanup();
        }
        [Serializable] private sealed class FootContact { public int leftHits, rightHits; public float leftGap, rightGap; }
        [Serializable] private sealed class Report
        {
            public string actor, profile, prefab, root, surface;
            public string[] dropEdges;
            public Vector3 bodyScale, start, finish, worstPoint;
            public int vertexCount, soleCount, frames, worstFrame;
            public long supportedVertexChecks;
            public bool completed;
            public float yaw, speed, expectedHorizontal, expectedVertical, expectedPath, expectedSeconds, seconds;
            public float horizontalTravel, verticalTravel, pathTravel, chordTravel, maxPenetration, worstSupportY;
            public float maxTiltDegrees, maxYawErrorDegrees, maxPathDeviation, maxScaleDeviation, finalPositionError;
            public FootContact startFeet, endFeet;
        }
    }
}
