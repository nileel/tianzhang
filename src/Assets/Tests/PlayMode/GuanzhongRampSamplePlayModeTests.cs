using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongRampSamplePlayModeTests
    {
        private const string ScenePath = "Assets/Scenes/GuanzhongRampSample.unity";
        private const string Art = "Assets/Art/Characters/CombatPieces/Static3D/";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d3.2-ramp-sample-20261025"));
        private GameObject root;
        private MeshCollider ramp;
        private Collider[] terrain;

        [UnitySetUp]
        public IEnumerator LoadSample()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath,new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Fail("Requires the saved editor acceptance scene.");
#endif
            yield return null;
            yield return null;
            root = GameObject.Find("D32_RampAndPlatforms");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                if (filter.transform.IsChildOf(root.transform.Find("EarthRamp24"))) ramp = collider;
            }
            terrain = root.GetComponentsInChildren<Collider>();
            Physics.SyncTransforms();
            Directory.CreateDirectory(Output);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Scene sample = SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(SceneManager.CreateScene("D32_AfterSample"));
            if (sample.IsValid() && sample.isLoaded) yield return SceneManager.UnloadSceneAsync(sample);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator SavedSolidMatchesProfileAndSixRotatedPorts()
        {
            var proof = new GeometryProof { unityVersion = Application.unityVersion, playMode = Application.isPlaying };
#if UNITY_EDITOR
            var environment = UnityEditor.AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            var profiles = JsonUtility.FromJson<Profiles>(environment.physicalSurfaceProfiles);
            Profile template = profiles.rampProfiles.Single(p => p.id == "left_ramp");
            foreach (Profile profile in profiles.rampProfiles)
            {
                Assert.That(profile.coveredCells.Length, Is.EqualTo(24));
                Vector3 origin = Cell(profile.coveredCells[0]);
                Vector3 first = Cell(template.coveredCells[0]);
                var normalized = profile.coveredCells.Select(c => Cell(c)-origin).ToArray();
                Assert.That(normalized.Zip(template.coveredCells.Select(c => Cell(c)-first),
                    (a,b) => Vector3.Distance(a,b)).Max(), Is.LessThan(.00001f));
                Assert.That(profile.upperY-profile.lowerY, Is.EqualTo(.75f).Within(.00001f));
                Assert.That(profile.worldZEnd-profile.worldZStart, Is.EqualTo(5*Mathf.Sqrt(3)/2).Within(.00001f));
            }
            var portals = template.lowerPortalEdges.Concat(template.upperPortalEdges)
                .Select(e => e.Split('>').OrderBy(c => c,StringComparer.Ordinal).ToArray())
                .GroupBy(e => string.Join(">",e)).Select(g => g.First()).ToArray();
            var mesh = ramp.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            proof.rampTriangles = mesh.triangles.Length/3;
            proof.rampVertices = mesh.vertexCount;
            proof.instances = root.GetComponentsInChildren<MeshFilter>().Length;
            proof.instanceTriangles = root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length/3);
            for (int direction = 0; direction < 6; direction++)
            {
                root.transform.rotation = Quaternion.Euler(0,direction*60,0);
                Physics.SyncTransforms();
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (ramp.GetComponent<Renderer>().sharedMaterials[submesh].name == "GrassEarthStone") continue;
                    int[] indices = mesh.GetTriangles(submesh);
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        Vector3 p = ramp.transform.TransformPoint((vertices[indices[i]]+vertices[indices[i+1]]+vertices[indices[i+2]])/3);
                        Vector3 local = root.transform.InverseTransformPoint(p);
                        float expected = Support(local.z);
                        proof.maxProfileError = Mathf.Max(proof.maxProfileError,Mathf.Abs(p.y-expected));
                        Assert.That(p.y, Is.EqualTo(expected).Within(.00007f));
                        if (i%30 != 0) continue;
                        Assert.That(ramp.Raycast(new Ray(p+Vector3.up,Vector3.down),out RaycastHit hit,2),Is.True);
                        Assert.That(hit.point.y, Is.EqualTo(p.y).Within(.00001f));
                        proof.supportRays++;
                    }
                }
                foreach (string[] portal in portals)
                {
                    Vector3 a = Cell(portal[0]), b = Cell(portal[1]);
                    Vector3 normal = (b-a).normalized, tangent = new Vector3(-normal.z,0,normal.x);
                    for (int i=0;i<=40;i++)
                    for (int strip=-1;strip<=1;strip++)
                    {
                        // At an outer hex corner, the two normal-offset points can be outside
                        // both finite hexes. Check the common endpoint itself, not that void.
                        if ((i==0 || i==40) && strip!=0) continue;
                        Vector3 p = (a+b)/2 + tangent*((i/40f-.5f)/Mathf.Sqrt(3)) + normal*(strip*.01f);
                        float expected = Support(p.z);
                        p = root.transform.TransformPoint(p);
                        bool HitSupport(Vector3 at)
                        {
                            foreach (Collider collider in terrain.Where(c => c==ramp || c.name=="LandingExactCap"))
                                if (collider.Raycast(new Ray(new Vector3(at.x,3,at.z),Vector3.down),out RaycastHit hit,6) &&
                                    Mathf.Abs(hit.point.y-expected)<.0001f) return true;
                            return false;
                        }
                        bool supported=HitSupport(p);
                        if (!supported)
                        {
                            // Independent FBX float transforms can put an exact shared-edge ray
                            // a few ulps outside both triangles. Require a same-height bearing
                            // hit within 10 micrometres at these finite samples;
                            // never accept a lower rock hit or expand the 2mm design tolerance.
                            supported=new[] { Vector3.left,Vector3.right,Vector3.forward,Vector3.back }
                                .Any(offset => HitSupport(p+offset*.00001f));
                            if (supported) proof.boundaryPrecisionRays++;
                        }
                        Assert.That(supported,Is.True,$"Unsupported finite port: direction={direction} {string.Join(">",portal)} {i}/{strip} {p:F8}");
                        proof.seamRays++;
                    }
                }
                SetView(38,-25,4.7f,root.transform.TransformPoint(new Vector3(3.7f,.3f,4)));
                yield return null;
                Capture($"runtime-orientation-{direction}");
            }
            root.transform.rotation = Quaternion.identity;
            SetView(18,-60,4.5f,new Vector3(3.7f,.1f,4));
            Capture("runtime-side");
            SetView(-25,-60,4.5f,new Vector3(3.7f,-.3f,4));
            Capture("runtime-bottom");
            File.WriteAllText(Path.Combine(Output,"unity-geometry.json"),JsonUtility.ToJson(proof,true));
#endif
            yield return null;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator ActualPlayerAndPairFeetTraverseTheSavedRamp()
        {
#if UNITY_EDITOR
            var beast = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Art+"Shijiahou/Shijiahou_Static3D.prefab"));
            var player = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Art+"FormalPlayer/FormalPlayer_Static3D.prefab"));
            // Only authored bearing surfaces carry feet. Original decorative grass and all
            // neighbouring side walls remain in the independent full-body obstruction check.
            Collider[] supports=terrain.Where(c => c==ramp || c.name=="LandingExactCap").ToArray();
            Assert.That(supports.Length,Is.EqualTo(10));
            var pairProbe = new GuanzhongRampBodyProbe(beast,1.800879333f,true,terrain,supports);
            var playerProbe = new GuanzhongRampBodyProbe(player,1.4f,false,terrain,supports);
            string map = Path.GetFullPath(Path.Combine(Application.dataPath,"../../docs/基础设定/演武地图/关中阶地-20261024/map"));
            var poses = JsonUtility.FromJson<PoseList>("{\"poses\":"+File.ReadAllText(Path.Combine(map,"all-legal-pair-poses.json"))+"}").poses;
            foreach (Pose pose in poses.Where(p => p.frontPhysicalSurface=="left_ramp" && p.rearPhysicalSurface=="left_ramp"))
            {
                pairProbe.Fit(Cell(pose.front),90+pose.facing*60,true);
                yield return null;
            }
            for (int direction=0;direction<6;direction++)
            for (int row=1;row<=6;row++)
                playerProbe.Fit(Cell($"1:{row}"),90+direction*60,true);
            // D1 witness route: entry, on-ramp 60-degree turn, then every ramp row and exit.
            Pose[] route = JsonUtility.FromJson<PoseList>(File.ReadAllText(Path.Combine(map,"pair-main-route.json"))).poses;
            var segments = route.Skip(2).Take(7).ToArray();
            string frames = Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/d32-motion-frames"));
            Directory.CreateDirectory(frames);
            int frame = 0;
            SetView(38,-25,4.7f,new Vector3(3.7f,.3f,4));
            playerProbe.Fit(Cell("1:3"),30,false);
            for (int i=0;i<segments.Length;i++)
            {
                if (i>0)
                {
                    Pose previous = segments[i-1];
                    float fromYaw=90+previous.facing*60, toYaw=90+segments[i].facing*60;
                    for (int s=1;s<=12 && previous.facing!=segments[i].facing;s++)
                    {
                        pairProbe.Fit(Cell(previous.front),Mathf.LerpAngle(fromYaw,toYaw,s/12f),true);
                        yield return null;
                        Capture(Path.Combine(frames,(frame++).ToString("D3")),1366,768);
                    }
                    for (int s=1;s<=16;s++)
                    {
                        pairProbe.Fit(Vector3.Lerp(Cell(previous.front),Cell(segments[i].front),s/16f),toYaw,true);
                        yield return null;
                        Capture(Path.Combine(frames,(frame++).ToString("D3")),1366,768);
                    }
                }
                else pairProbe.Fit(Cell(segments[i].front),90+segments[i].facing*60,true);
                Capture("runtime-body-stop-"+i);
            }
            var proof = new BodyProof { playMode=Application.isPlaying,pairPoses=pairProbe.Poses,playerPoses=playerProbe.Poses,
                pairFootGap=pairProbe.MaxFootGap,playerFootGap=playerProbe.MaxFootGap,pairPenetration=pairProbe.MaxBodyPenetration,
                playerPenetration=playerProbe.MaxBodyPenetration,actualMeshChecks=pairProbe.VertexChecks+playerProbe.VertexChecks,
                frames=frame,scope="Saved sample art in Unity; actual mesh sole fitting and vertex/low-triangle samples. No production movement or rule changes; no all-map continuous proof." };
            File.WriteAllText(Path.Combine(Output,"unity-body.json"),JsonUtility.ToJson(proof,true));
#endif
            yield return null;
        }

        private static float Support(float z)
        {
            float t = Mathf.Clamp01((z-1-Mathf.Sqrt(3)*.5f)/(5*Mathf.Sqrt(3)*.5f));
            return .340433121f+.375f*(1-Mathf.Cos(Mathf.PI*t));
        }
        private static Vector3 Cell(string key)
        {
            string[] parts=key.Split(':');
            int q=int.Parse(parts[0]),r=int.Parse(parts[1]);
            return new Vector3(q+.5f*r,0,1+Mathf.Sqrt(3)*.5f*r);
        }
        private static void SetView(float pitch,float yaw,float size,Vector3 focus)
        {
            Camera camera=Camera.main;
            camera.orthographicSize=size;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            camera.transform.position=focus-camera.transform.forward*20;
        }
        private static void Capture(string name,int width=1920,int height=1080)
        {
            Camera camera=Camera.main;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            target.Create();
            var request=new RenderPipeline.StandardRequest { destination=target };
            Assert.That(RenderPipeline.SupportsRenderRequest(camera,request),Is.True);
            var image=new Texture2D(width,height,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active,previousTarget=camera.targetTexture;
            try
            {
                camera.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,width,height),0,0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Output,name+".png"),image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                camera.targetTexture=previousTarget;
                Object.Destroy(image);
                target.Release();
                Object.Destroy(target);
            }
        }
        [Serializable] private sealed class Profiles { public Profile[] rampProfiles; }
        [Serializable] private sealed class Profile
        {
            public string id;
            public string[] coveredCells,lowerPortalEdges,upperPortalEdges;
            public float lowerY,upperY,worldZStart,worldZEnd;
        }
        [Serializable] private sealed class PoseList { public Pose[] poses; }
        [Serializable] private sealed class Pose
        {
            public string front,rear,frontPhysicalSurface,rearPhysicalSurface;
            public int facing;
        }
        [Serializable] private sealed class GeometryProof
        {
            public string unityVersion;
            public bool playMode;
            public int rampTriangles,rampVertices,instances,instanceTriangles,supportRays,seamRays,boundaryPrecisionRays;
            public float maxProfileError;
        }
        [Serializable] private sealed class BodyProof
        {
            public bool playMode;
            public int pairPoses,playerPoses,actualMeshChecks,frames;
            public float pairFootGap,playerFootGap,pairPenetration,playerPenetration;
            public string scope;
        }
    }
}
