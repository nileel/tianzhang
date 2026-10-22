using System;
using System.Collections;
using System.Collections.Generic;
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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    /// <summary>
    /// F3 acceptance evidence: the three formal combatants stand with their own foot contact on
    /// the measured tile plane, without the former physical base, at both game resolutions.
    /// </summary>
    public sealed class GuanzhongGroundedUnitsPlayModeTests
    {
        private string saveDirectory;
        private static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/f3-grounding"));

        [UnitySetUp]
        public IEnumerator EnterSettlement()
        {
            var stale = Object.FindFirstObjectByType<GameBootstrap>();
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            yield return SceneManager.LoadSceneAsync(GameplaySceneNames.StartMenu);
            saveDirectory = Path.Combine(Application.temporaryCachePath, "TianZhang-F3-" + Guid.NewGuid().ToString("N"));
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(GameBootstrap.RequireInstance(), new GameSaveSlotStore(saveDirectory));
            var creation = Find<CharacterCreationController>();
            creation.Open();
            Assert.IsTrue(creation.SelectOrigin("origin_minor_clan"));
            creation.Submit("f3-grounding", "贴地验证");
            yield return SceneReady(GameplaySceneNames.World);
            var world = Find<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub"));
            world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Directory.CreateDirectory(Evidence);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap != null) { Object.Destroy(bootstrap.gameObject); yield return null; }
            Scene previous = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("F3GroundingCleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
            if (saveDirectory != null && Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator FormalUnitsStandWithoutBasesAt1920() => Verify(1920, 1080);

        [UnityTest, Timeout(300000)]
        public IEnumerator FormalUnitsStandWithoutBasesAt1366() => Verify(1366, 768);

        private IEnumerator Verify(int width, int height)
        {
            GuanzhongCombatEquipmentPlayModeTests.SetGameSize(width, height);
            for (int frame = 0; frame < 60 && (Screen.width != width || Screen.height != height); frame++) yield return null;
            Assert.AreEqual(width, Screen.width);
            Assert.AreEqual(height, Screen.height);
            Assert.IsTrue(Find<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Find<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Find<AdventureInputController>().SelectNode("shijiahou_encounter"));
            var encounter = Find<EncounterCoordinator>();
            yield return Ready(encounter);
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();

            var provider = Find<Static3DCombatUnitPresentationProvider>();
            var spawned = Field<AdventureSpawnSet>(encounter, "spawned");
            var hud = Find<CombatHudPresenter>();
            var pick = Find<CombatTargetInput>();
            Camera camera = Camera.main;
            var combatants = new[] { spawned.Player }.Concat(spawned.Enemies).ToArray();
            Assert.AreEqual(3, combatants.Length);
            Assert.AreEqual("player", combatants[0].Id);

            var lines = new List<string>
            {
                "scene=" + SceneManager.GetActiveScene().name + "; size=" + Screen.width + "x" + Screen.height +
                "; provider=" + provider.name + "; activeUnits=" + provider.ActiveCombatantCount,
            };
            foreach (CombatantSnapshot combatant in combatants)
            {
                Assert.IsTrue(provider.TryGetPresentedUnit(combatant.Id, out GameObject root), combatant.Id);
                Assert.IsTrue(provider.TryGetPresentedPosition(combatant.Id, out CombatUnitPresentationHex cell), combatant.Id);
                lines.Add(MeasureGrounded(combatant.Id, root, cell));
            }

            // The default framing keeps all three grounded units and their head bars visible.
            var bars = Field<CombatUnitHealthBarView.Bar[]>(Find<CombatUnitHealthBarView>(), "bars");
            foreach (CombatantSnapshot combatant in combatants)
            {
                Assert.IsTrue(provider.TryGetVisibleBounds(combatant.Id, out Bounds body), combatant.Id);
                Vector3 viewport = camera.WorldToViewportPoint(body.center);
                Assert.That(viewport.x, Is.InRange(.02f, .98f), combatant.Id + " must stay horizontally visible.");
                Assert.That(viewport.y, Is.InRange(.02f, .98f), combatant.Id + " must stay vertically visible.");
                Assert.IsTrue(provider.TryGetHeadAnchor(combatant.Id, out Vector3 anchor), combatant.Id);
                Assert.Greater(anchor.y, body.center.y, "The head anchor must stay above the body center.");
                Vector2 screen = camera.WorldToScreenPoint(body.center);
                Assert.IsTrue(pick.TryPick(screen, out string pickedId),
                    combatant.Id + " must stay pickable from its body without a base collider.");
                Assert.AreEqual(combatant.Id, pickedId);
                hud.InspectTarget(combatant.Id);
                Assert.AreEqual(combatant.Id, hud.InspectedTargetId);
                hud.ClearInspectedTarget();
            }
            yield return null;
            Assert.AreEqual(3, bars.Count(bar => bar.root != null && bar.root.gameObject.activeInHierarchy),
                "Every living formal unit keeps its head health bar after base removal.");
            lines.Add("activeHealthBars=" + bars.Count(bar => bar.root != null && bar.root.gameObject.activeInHierarchy));

            File.WriteAllLines(Path.Combine(Evidence, "f3-grounding-" + width + ".txt"), lines);
            yield return Capture("f3-grounding-" + width);
            Assert.IsTrue(provider.TryGetVisibleBounds("player", out Bounds playerBounds));
            yield return CaptureCloseup("f3-grounding-" + width + "-player", playerBounds.center, 1.6f);
            Vector3 enemyFocus = (Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(spawned.Enemies[0].Position.Q, spawned.Enemies[0].Position.R)) +
                Static3DCombatUnitPresentationProvider.HexToWorld(
                    new CombatUnitPresentationHex(spawned.Enemies[1].Position.Q, spawned.Enemies[1].Position.R))) * .5f;
            yield return CaptureCloseup("f3-grounding-" + width + "-enemies", enemyFocus + Vector3.up * .45f, 2.4f);
            hud.ClearInspectedTarget();
        }

        /// <summary>
        /// Measures the actual support plane from the cell's triangle at the cell center. The
        /// tile renderer's top includes grass and must stay above the foot, proving it was not
        /// mistaken for the ground.
        /// </summary>
        private static string MeasureGrounded(string combatantId, GameObject root, CombatUnitPresentationHex cell)
        {
            Assert.IsNull(root.transform.Find("Static3DCombatBase"),
                combatantId + " must not carry the removed physical base.");
            Assert.Zero(root.GetComponentsInChildren<LineRenderer>(true).Length,
                combatantId + " must not carry the old base selection ring.");
            Assert.AreEqual(1, root.transform.childCount, "Only the approved body may stay under the unit root.");
            Transform body = root.transform.GetChild(0);
            Assert.AreEqual(Vector3.zero, body.localPosition,
                "The body must not keep the former BaseThickness lift.");
            MeshRenderer renderer = root.GetComponentsInChildren<MeshRenderer>(true).Single();
            Assert.AreEqual(1.4f, body.localScale.x, .00001f);

            Transform tile = GameObject.Find("GuanzhongBattlefield").transform.Find(
                "GuanzhongHex_" + cell.Q + "_" + cell.R);
            Assert.NotNull(tile, "Missing formal tile for " + combatantId);
            float surfaceY = CenterSurfaceY(tile);
            float tileRendererTop = tile.GetComponentsInChildren<Renderer>(true).Max(value => value.bounds.max.y);
            Assert.AreEqual(.34f, tile.position.y, .001f);
            Assert.AreEqual(.34f, surfaceY, .002f,
                "The tile's measured bearing surface at the cell center is the support plane.");
            Assert.AreEqual(.34f, root.transform.position.y, .001f);
            Assert.AreEqual(surfaceY, renderer.bounds.min.y, .002f,
                "The body foot must rest on the measured tile surface, not float or sink.");
            Assert.Greater(tileRendererTop, renderer.bounds.min.y + .05f,
                "The tile renderer top includes grass; it must not be used as the support plane.");
            return "unit=" + combatantId + ";cell=" + cell.Q + "," + cell.R +
                ";rootY=" + root.transform.position.y.ToString("F5") +
                ";bodyLocalY=" + body.localPosition.y.ToString("F5") +
                ";footY=" + renderer.bounds.min.y.ToString("F5") +
                ";surfaceY=" + surfaceY.ToString("F5") +
                ";tileRendererTop=" + tileRendererTop.ToString("F5") +
                ";children=" + root.transform.childCount + ";base=none;lineRenderers=0";
        }

        private IEnumerator Capture(string label)
        {
#if UNITY_EDITOR
            for (float deadline = Time.realtimeSinceStartup + 120f;
                UnityEditor.ShaderUtil.anythingCompiling && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsFalse(UnityEditor.ShaderUtil.anythingCompiling);
#endif
            for (int frame = 0; frame < 8; frame++) yield return null;
            Canvas.ForceUpdateCanvases();
            string path = Path.Combine(Evidence, label);
            if (File.Exists(path + ".png")) File.Delete(path + ".png");
            ScreenCapture.CaptureScreenshot(path + ".png");
            for (int frame = 0; frame < 120 && !File.Exists(path + ".png"); frame++) yield return null;
            Assert.IsTrue(File.Exists(path + ".png"));
            yield return null;
        }

        /// <summary>Inspection-only framing; the formal camera and controller are restored afterwards.</summary>
        private IEnumerator CaptureCloseup(string label, Vector3 focus, float size)
        {
            var controller = Find<AdventureCameraController>();
            Camera camera = Camera.main;
            Vector3 originalPosition = camera.transform.position;
            float originalSize = camera.orthographicSize;
            bool originalEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            try
            {
                camera.transform.position = focus - camera.transform.forward * 12f;
                camera.orthographicSize = size;
                yield return Capture(label);
            }
            finally
            {
                camera.transform.position = originalPosition;
                camera.orthographicSize = originalSize;
                if (controller != null) controller.enabled = originalEnabled;
            }
            yield return null;
        }

        /// <summary>The highest source triangle that supports the logical cell center.</summary>
        private static float CenterSurfaceY(Transform tile)
        {
            float highest = float.NegativeInfinity;
            Vector3 center = tile.position;
            foreach (MeshFilter filter in tile.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.IsTrue(mesh.isReadable, "Source mesh must remain readable for triangle evidence.");
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(vertices[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    if (Mathf.Abs(denominator) < 1e-10f) continue;
                    float u = ((b.z - c.z) * (center.x - c.x) + (c.x - b.x) * (center.z - c.z)) / denominator;
                    float v = ((c.z - a.z) * (center.x - c.x) + (a.x - c.x) * (center.z - c.z)) / denominator;
                    float w = 1f - u - v;
                    if (u >= 0f && v >= 0f && w >= 0f)
                        highest = Mathf.Max(highest, u * a.y + v * b.y + w * c.y);
                }
            }
            Assert.IsFalse(float.IsInfinity(highest), "No source triangle supports the logical cell center.");
            return highest;
        }

        private static IEnumerator Ready(EncounterCoordinator encounter)
        {
            for (float deadline = Time.realtimeSinceStartup + 45f;
                !encounter.AcceptsPlayerCommand && Time.realtimeSinceStartup < deadline;) yield return null;
            Assert.IsTrue(encounter.AcceptsPlayerCommand);
            yield return null;
        }

        private static IEnumerator SceneReady(string name)
        {
            for (int frame = 0; frame < 300 && SceneManager.GetActiveScene().name != name; frame++) yield return null;
            Assert.AreEqual(name, SceneManager.GetActiveScene().name);
            yield return null;
        }

        private static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }
    }
}
