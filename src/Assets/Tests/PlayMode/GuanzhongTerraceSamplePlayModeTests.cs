using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongTerraceSamplePlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/GuanzhongTerraceSample.unity";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d3.1-terrace-sample-20261024"));

        [UnitySetUp]
        public IEnumerator LoadSample()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Fail("This acceptance fixture requires the saved editor sample scene.");
#endif
            yield return null;
            yield return null;
            Assert.That(Application.isPlaying, Is.True);
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ScenePath));
        }

        [UnityTearDown]
        public IEnumerator UnloadSample()
        {
            Scene sample = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(SceneManager.CreateScene("D31_AfterSample"));
            if (sample.IsValid() && sample.isLoaded) yield return SceneManager.UnloadSceneAsync(sample);
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator SavedSampleHasMeasuredSupportsAndContinuousVisibleJoints()
        {
            GameObject root = GameObject.Find("D31_ThreeLevelSample");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.transform.childCount, Is.EqualTo(19));
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty, "Sample art must not introduce rule geometry.");
            var proof = new Proof { unityVersion = Application.unityVersion, scene = ScenePath, playMode = true,
                graphicsDevice = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString() };
            var temporaryColliders = new List<MeshCollider>();
            foreach (Transform cell in root.transform)
            {
                int level = int.Parse(cell.name.Substring(cell.name.LastIndexOf('L') + 1));
                var filters = cell.GetComponentsInChildren<MeshFilter>();
                Vector3[] vertices = filters.SelectMany(f => f.sharedMesh.vertices.Select(f.transform.TransformPoint)).ToArray();
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
                float surfaceY = float.NegativeInfinity;
                foreach (MeshFilter filter in filters)
                {
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    temporaryColliders.Add(collider);
                    Physics.SyncTransforms();
                    var ray = new Ray(new Vector3(cell.position.x, 5, cell.position.z), Vector3.down);
                    if (collider.Raycast(ray, out RaycastHit hit, 10)) surfaceY = Mathf.Max(surfaceY, hit.point.y);
                }
                Assert.That(surfaceY, Is.EqualTo(.340433121f + .75f * level).Within(.00001f), cell.name);
                Assert.That(bounds.max.y - surfaceY, Is.InRange(.0934f, .0937f), "Grass crown is not the support surface.");
                Assert.That(cell.localScale, Is.EqualTo(Vector3.one), "Do not vertically stretch the grass top.");
                int triangles = filters.Sum(f => f.sharedMesh.triangles.Length / 3);
                Assert.That(triangles, Is.EqualTo(99346), "Use the original Tile mesh.");
                proof.cells.Add(new CellProof { name = cell.name, level = level, rootPosition = cell.position,
                    supportY = surfaceY, crownY = bounds.max.y, boundsMin = bounds.min, boundsMax = bounds.max,
                    triangles = triangles });
            }
            // Dense runtime seam-strip rays, including both endpoints/triple junctions.
            foreach (Transform cell in root.transform)
            foreach (Transform other in root.transform)
            {
                Vector3 delta = other.position - cell.position;
                if (string.CompareOrdinal(cell.name, other.name) >= 0 || Mathf.Abs(delta.y) > .001f ||
                    Mathf.Abs(new Vector2(delta.x, delta.z).magnitude - 1) > .001f) continue;
                Vector3 normal = delta.normalized;
                Vector3 tangent = new Vector3(-normal.z, 0, normal.x);
                for (int sample = 0; sample <= 60; sample++)
                for (int strip = -1; strip <= 1; strip++)
                {
                    Vector3 p = (cell.position + other.position) * .5f + tangent * ((sample / 60f - .5f) / Mathf.Sqrt(3))
                        + normal * (.01f * strip);
                    Assert.That(Physics.Raycast(new Vector3(p.x, p.y + .3f, p.z), Vector3.down,
                        out RaycastHit hit, .325f), Is.True, $"Open seam at {cell.name}/{other.name} sample={sample}/{strip}");
                    Assert.That(hit.point.y, Is.GreaterThan(p.y - .025f));
                    proof.seamRays++;
                }
            }
            foreach (MeshCollider collider in temporaryColliders) Object.Destroy(collider);
            yield return null;
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
            Directory.CreateDirectory(Output);
            proof.instanceCount = proof.cells.Count;
            proof.drawnTriangles = proof.cells.Sum(c => c.triangles);
            proof.sharedMeshes = root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).Distinct().Count();
            File.WriteAllText(Path.Combine(Output, "unity-runtime-proof.json"), JsonUtility.ToJson(proof, true));
            Debug.Log($"D31_RUNTIME_PASS tiles={proof.instanceCount} triangles={proof.drawnTriangles} seamRays={proof.seamRays}");
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator SavedSampleRendersSixDirectionsAndCameraMotion()
        {
            GameObject root = GameObject.Find("D31_ThreeLevelSample");
            Material material = root.GetComponentInChildren<Renderer>().sharedMaterial;
            Assert.That(material.shader.name, Is.EqualTo("TianZhang/Guanzhong Terrace Lit"));
            Assert.That(material.shader.isSupported, Is.True);
#if UNITY_EDITOR
            Assert.That(UnityEditor.ShaderUtil.ShaderHasError(material.shader), Is.False);
#endif
            var proof = new ViewProof { shader = material.shader.name, tint = material.GetColor("_BaseColor"),
                scene = ScenePath, playMode = Application.isPlaying, pitch = 38, closeSize = 3, commonSize = 5 };
            Directory.CreateDirectory(Output);
            Camera camera = Camera.main;
            for (int direction = 0; direction < 6; direction++)
            {
                SetView(camera, 38, direction * 60, 3);
                yield return null;
                Capture(camera, $"runtime-direction-{direction}", 1920, 1080);
            }
            SetView(camera, 38, 0, 5);
            yield return null;
            Capture(camera, "runtime-common-size5", 1920, 1080);
            SetView(camera, 18, 60, 3);
            yield return null;
            Capture(camera, "runtime-low-side", 1920, 1080);
            SetView(camera, -25, 60, 3);
            yield return null;
            Capture(camera, "runtime-underside", 1920, 1080);
            string frames = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/d31-motion-frames"));
            Directory.CreateDirectory(frames);
            var frameTimes = new List<float>();
            for (int frame = 0; frame < 32; frame++)
            {
                float phase = frame / 31f;
                SetView(camera, 38, 0, 3 + .12f * Mathf.Sin(phase * Mathf.PI * 2));
                camera.transform.position += Vector3.right * (.12f * Mathf.Sin(phase * Mathf.PI * 2));
                yield return null;
                frameTimes.Add(Time.unscaledDeltaTime * 1000);
                Capture(camera, Path.Combine(frames, frame.ToString("D3")), 1366, 768);
            }
            proof.motionFrames = 32;
            proof.batchFrameMsMedian = frameTimes.OrderBy(v => v).ElementAt(frameTimes.Count / 2);
            proof.performanceNote = "Graphics-enabled batch PlayMode; median includes synchronous capture/file IO, not player FPS or full-map load.";
            File.WriteAllText(Path.Combine(Output, "unity-runtime-views.json"), JsonUtility.ToJson(proof, true));
            Debug.Log("D31_VIEWS_PASS shader=" + proof.shader);
        }

        private static void SetView(Camera camera, float pitch, float yaw, float size)
        {
            camera.orthographicSize = size;
            camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            camera.transform.position = new Vector3(0, .3f, 1) - camera.transform.forward * 14;
        }

        private static void Capture(Camera camera, string name, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            var request = new RenderPipeline.StandardRequest { destination = target };
            Assert.That(RenderPipeline.SupportsRenderRequest(camera, request), Is.True);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Output, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = previousTarget;
                Object.Destroy(image);
                target.Release();
                Object.Destroy(target);
            }
        }

        [Serializable] private sealed class Proof
        {
            public string unityVersion, scene, graphicsDevice, graphicsApi;
            public bool playMode;
            public int instanceCount, drawnTriangles, sharedMeshes, seamRays;
            public List<CellProof> cells = new List<CellProof>();
        }
        [Serializable] private sealed class ViewProof
        {
            public string scene, shader, performanceNote;
            public bool playMode;
            public Color tint;
            public int motionFrames;
            public float pitch, closeSize, commonSize, batchFrameMsMedian;
        }
        [Serializable] private sealed class CellProof
        {
            public string name;
            public int level, triangles;
            public Vector3 rootPosition, boundsMin, boundsMax;
            public float supportY, crownY;
        }
    }
}
