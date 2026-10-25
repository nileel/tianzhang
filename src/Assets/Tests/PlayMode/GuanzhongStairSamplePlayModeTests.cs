using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongStairSamplePlayModeTests
    {
        private const string ScenePath="Assets/Scenes/GuanzhongStairSample.unity";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d3.3-stair-sample-20261025"));
        private GameObject root;
        private Collider[] terrain;
        private EnvironmentProfileAsset environment;
        private Stair[] stairs;

        [UnitySetUp]
        public IEnumerator LoadSample()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath,new LoadSceneParameters(LoadSceneMode.Single));
            environment=UnityEditor.AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
#endif
            yield return null;
            yield return null;
            root=GameObject.Find("D33_StairsAndGroundRoute");
            Assert.That(root,Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Collider>(),Is.Empty,"No production hidden support collider.");
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh=filter.sharedMesh;
            terrain=root.GetComponentsInChildren<Collider>();
            stairs=JsonUtility.FromJson<Profiles>(environment.physicalSurfaceProfiles).stairConnections;
            Physics.SyncTransforms();
            Directory.CreateDirectory(Output);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Scene scene=SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(SceneManager.CreateScene("D33_AfterSample"));
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }

        [UnityTest,Timeout(180000)]
        public IEnumerator SavedStairsMatchEightRisersAndSixDirectionPorts()
        {
            var proof=new GeometryProof { playMode=Application.isPlaying,unityVersion=Application.unityVersion };
            Stair first=stairs[0];
            foreach (Stair stair in stairs)
            {
                Assert.That(stair.risers.Length,Is.EqualTo(8));
                Assert.That(stair.clearanceLimit,Is.EqualTo(1));
                for (int i=0;i<8;i++)
                {
                    Assert.That(stair.risers[i].worldZ-stair.worldZStart,Is.EqualTo(first.risers[i].worldZ-first.worldZStart).Within(.00001f));
                    Assert.That(stair.risers[i].supportYAfter-stair.lowerY,Is.EqualTo((i+1)*.09375f).Within(.00001f));
                }
                foreach (string cell in stair.coveredCells)
                    Assert.That(root.transform.Cast<Transform>().Any(t => t.name.StartsWith("Tile_"+cell.Replace(':','_')+"_")),Is.False);
            }
            proof.instances=root.GetComponentsInChildren<MeshFilter>().Length;
            proof.instanceTriangles=root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length/3);
            for (int direction=0;direction<6;direction++)
            {
                root.transform.rotation=Quaternion.Euler(0,direction*60,0);
                Physics.SyncTransforms();
                foreach (Stair stair in stairs)
                {
                    MeshCollider collider=root.transform.Find(stair.id).GetComponentInChildren<MeshCollider>();
                    Mesh mesh=collider.sharedMesh;
                    proof.stairTriangles=mesh.triangles.Length/3;
                    var levels=new HashSet<int>();
                    Vector3[] vertices=mesh.vertices;
                    int[] indices=mesh.triangles;
                    for (int i=0;i<indices.Length;i+=3)
                    {
                        Vector3 a=collider.transform.TransformPoint(vertices[indices[i]]),b=collider.transform.TransformPoint(vertices[indices[i+1]]),c=collider.transform.TransformPoint(vertices[indices[i+2]]);
                        Vector3 normal=Vector3.Cross(b-a,c-a).normalized;
                        Vector3 p=(a+b+c)/3;
                        Vector3 local=root.transform.InverseTransformPoint(p);
                        if (normal.y>.99f)
                        {
                            float expected=Height(stair,local.z);
                            proof.maxProfileError=Mathf.Max(proof.maxProfileError,Mathf.Abs(p.y-expected));
                            Assert.That(p.y,Is.EqualTo(expected).Within(.00002f));
                            Assert.That(collider.Raycast(new Ray(p+Vector3.up,Vector3.down),out RaycastHit hit,2),Is.True);
                            Assert.That(hit.point.y,Is.EqualTo(expected).Within(.00002f));
                            levels.Add(Mathf.RoundToInt((expected-stair.lowerY)/.09375f));
                            proof.treadRays++;
                        }
                        Vector3 localNormal=root.transform.InverseTransformDirection(normal);
                        if (localNormal.z<-.99f && local.y>stair.lowerY && local.y<stair.lowerY+.75f &&
                            stair.risers.Any(r => Mathf.Abs(r.worldZ-local.z)<.00002f))
                        {
                            Vector3 forward=root.transform.TransformDirection(Vector3.forward);
                            Assert.That(collider.Raycast(new Ray(p-forward*.02f,forward),out RaycastHit hit,.04f),Is.True);
                            Assert.That(Vector3.Distance(hit.point,p),Is.LessThan(.00002f));
                            proof.riserRays++;
                        }
                    }
                    Assert.That(levels.Count,Is.EqualTo(9),"All nine distinct horizontal levels must be real mesh faces.");
                    string[][] ports=stair.lowerPortalEdges.Concat(stair.upperPortalEdges).Select(e => e.Split('>').OrderBy(s => s,StringComparer.Ordinal).ToArray())
                        .GroupBy(e => string.Join(">",e)).Select(g => g.First()).ToArray();
                    foreach (string[] port in ports)
                    {
                        Vector3 a=Cell(port[0]),b=Cell(port[1]);
                        Vector3 normal=(b-a).normalized,tangent=new Vector3(-normal.z,0,normal.x);
                        for (int i=0;i<=40;i++)
                        for (int strip=-1;strip<=1;strip++)
                        {
                            if ((i==0 || i==40) && strip!=0) continue;
                            Vector3 local=(a+b)/2+tangent*((i/40f-.5f)/Mathf.Sqrt(3))+normal*(strip*.01f);
                            float expected=Height(stair,local.z);
                            Vector3 p=root.transform.TransformPoint(local);
                            bool Hit(Vector3 at) => Mathf.Abs(GuanzhongStairBodyProbe.Height(at,terrain)-expected)<.0001f;
                            bool supported=Hit(p);
                            if (!supported)
                            {
                                supported=new[] {Vector3.left,Vector3.right,Vector3.forward,Vector3.back}.Any(v => Hit(p+v*.00001f));
                                if (supported) proof.boundaryPrecisionRays++;
                            }
                            Assert.That(supported,Is.True,$"Unjoined stair port {stair.id} direction={direction} {string.Join(">",port)} {i}/{strip}");
                            proof.portRays++;
                        }
                    }
                    SetView(38,-25,2.0f,root.transform.TransformPoint(new Vector3(stair.platformCenters[1].worldX,stair.lowerY+.25f,stair.platformCenters[1].worldZ)));
                    yield return null;
                    Capture($"runtime-{stair.id}-direction-{direction}");
                }
            }
            root.transform.rotation=Quaternion.identity;
            Physics.SyncTransforms();
            foreach (Stair stair in stairs)
            {
                SetView(-25,-50,2.1f,new Vector3(stair.platformCenters[1].worldX,stair.lowerY-.3f,stair.platformCenters[1].worldZ));
                Capture("runtime-bottom-"+stair.id);
            }
            Assert.That(proof.riserRays,Is.GreaterThanOrEqualTo(8*2*6));
            File.WriteAllText(Path.Combine(Output,"unity-geometry.json"),JsonUtility.ToJson(proof,true));
        }

        [UnityTest,Timeout(600000)]
        public IEnumerator ActualPlayerStopsInSixFacingsAndStepsUpAndDown()
        {
#if UNITY_EDITOR
            GameObject player=Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.prefab"));
            var proof=new BodyProof { playMode=Application.isPlaying };
            string frames=Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/d33-motion-frames"));
            Directory.CreateDirectory(frames);
            foreach (Stair stair in stairs)
            {
                Bounds domain=root.transform.Find(stair.id).GetComponentInChildren<Renderer>().bounds;
                domain.Expand(new Vector3(.6f,8,.6f));
                Collider[] nearby=terrain.Where(c => c.bounds.Intersects(domain)).ToArray();
                Collider[] support=nearby.Where(c => c.transform.IsChildOf(root.transform.Find(stair.id)) || c.name=="LandingExactCap").ToArray();
                var probe=new GuanzhongStairBodyProbe(player,support,nearby,stair.risers.Select(r => r.worldZ).ToArray());
                for (int facing=0;facing<6;facing++)
                {
                    float yaw=90+facing*60;
                    for (int platform=0;platform<3;platform++)
                    {
                        probe.Fit(Cell(stair.coveredCells[platform]),yaw,true);
                        if (facing==5)
                        {
                            SetView(38,-25,1.9f,new Vector3(stair.platformCenters[1].worldX,stair.lowerY+.6f,stair.platformCenters[1].worldZ));
                            yield return null;
                            Capture($"runtime-{stair.id}-platform-{platform}");
                        }
                    }
                    Vector3 previous=Vector3.zero;
                    for (int step=0;step<=48;step++)
                    {
                        float t=-.1f+step/40f;
                        Vector3 center=Vector3.LerpUnclamped(Cell(stair.coveredCells[0]),Cell(stair.coveredCells[2]),t);
                        probe.Fit(center,yaw,false);
                        Vector3 next=player.transform.position;
                        if (step>0)
                        {
                            float lift=next.y-previous.y;
                            Assert.That(lift,Is.InRange(-.0001f,.09385f),"Each uphill lift crosses at most one actual riser.");
                            probe.MaxLift=Mathf.Max(probe.MaxLift,lift);
                            // Up: lift at old XZ then translate. Down is precisely this path in reverse.
                            Vector3 raised=new Vector3(previous.x,next.y,previous.z);
                            probe.Verify(Vector3.Lerp(previous,raised,.5f),Quaternion.Euler(0,yaw,0));
                            if (facing==5 && lift>.001f)
                            {
                                yield return null;
                                Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                            }
                            probe.Verify(Vector3.Lerp(raised,next,.5f),Quaternion.Euler(0,yaw,0));
                            if (facing==5 && lift>.001f)
                            {
                                yield return null;
                                Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                            }
                            probe.Verify(next,Quaternion.Euler(0,yaw,0));
                            if (facing==5)
                            {
                                yield return null;
                                Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                            }
                        }
                        previous=next;
                        yield return null;
                    }
                }
                // Render a separate downhill traversal, including the horizontal-then-lower phase.
                SetView(38,-25,1.9f,new Vector3(stair.platformCenters[1].worldX,stair.lowerY+.6f,stair.platformCenters[1].worldZ));
                Vector3 previousDown=Vector3.zero;
                for (int step=40;step>=0;step--)
                {
                    probe.Fit(Vector3.Lerp(Cell(stair.coveredCells[0]),Cell(stair.coveredCells[2]),step/40f),210,false);
                    Vector3 next=player.transform.position;
                    if (step<40 && previousDown.y-next.y>.001f)
                    {
                        Vector3 high=new Vector3(next.x,previousDown.y,next.z);
                        probe.Verify(Vector3.Lerp(previousDown,high,.5f),Quaternion.Euler(0,210,0));
                        yield return null;
                        Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                        probe.Verify(Vector3.Lerp(high,next,.5f),Quaternion.Euler(0,210,0));
                        yield return null;
                        Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                    }
                    player.transform.position=next;
                    yield return null;
                    Capture(Path.Combine(frames,(proof.frames++).ToString("D3")),1366,768);
                    previousDown=next;
                }
                proof.poses+=probe.Poses;
                proof.staticPoses+=probe.StaticPoses;
                proof.meshPointChecks+=probe.PointChecks;
                proof.maxStaticFootGap=Mathf.Max(proof.maxStaticFootGap,probe.MaxStaticFootGap);
                proof.maxMovingFootGap=Mathf.Max(proof.maxMovingFootGap,probe.MaxMovingFootGap);
                proof.maxPenetration=Mathf.Max(proof.maxPenetration,probe.MaxPenetration);
                proof.maxSingleLift=Mathf.Max(proof.maxSingleLift,probe.MaxLift);
            }
            Object.Destroy(player);
            File.WriteAllText(Path.Combine(Output,"unity-body.json"),JsonUtility.ToJson(proof,true));
#endif
        }

        [UnityTest]
        public IEnumerator GroundRouteAndShoreBranchRemainTraceable()
        {
            string[] main="4:0,3:0,2:1,2:2,2:3,2:4,2:5,2:6,2:7,3:7,4:7,5:7,6:7,7:7,8:7,8:8,8:9,8:10,8:11,8:12".Split(',');
            string[] shore="5:7,5:6,5:5,5:4,5:3,5:2,5:1,5:0,6:0".Split(',');
            var proof=new RouteProof { main=main,shore=shore };
            foreach (string[] route in new[] {main,shore})
            {
                var line=new GameObject("AcceptanceRoute",typeof(LineRenderer)).GetComponent<LineRenderer>();
                line.material=new Material(Shader.Find("Sprites/Default"));
                line.startColor=line.endColor=route==main ? new Color(1,.72f,.18f) : new Color(.35f,.85f,1);
                line.startWidth=line.endWidth=.025f;
                var points=new List<Vector3>();
                for (int i=0;i<route.Length;i++)
                {
                    int[] qr=route[i].Split(':').Select(int.Parse).ToArray();
                    var cell=environment.battlefieldCells.Single(c => c.q==qr[0] && c.r==qr[1]);
                    Assert.That(cell.blocksGroundMove,Is.False);
                    if (i==0) continue;
                    int[] from=route[i-1].Split(':').Select(int.Parse).ToArray();
                    var edge=environment.directedEdges.Single(e => e.fromQ==from[0] && e.fromR==from[1] && e.toQ==qr[0] && e.toR==qr[1]);
                    Assert.That(edge.allowsMovement,Is.True);
                    Assert.That(edge.clearanceLimit==0 || edge.clearanceLimit>=1,Is.True);
                    if (edge.edgeKind==EnvironmentEdgeKind.Stairs) Assert.That(edge.clearanceLimit,Is.EqualTo(1));
                    for (int step=0;step<=20;step++)
                    {
                        Vector3 p=Vector3.Lerp(Cell(route[i-1]),Cell(route[i]),step/20f);
                        float y=GuanzhongStairBodyProbe.Height(p,terrain);
                        Assert.That(float.IsNegativeInfinity(y),Is.False,"Route has missing actual geometry.");
                        p.y=y+.025f;
                        points.Add(p);
                        proof.bearingSamples++;
                    }
                }
                line.positionCount=points.Count;
                line.SetPositions(points.ToArray());
            }
            SetView(55,-16,6.8f,new Vector3(7.6f,.6f,6.2f));
            yield return null;
            Capture("runtime-ground-route");
            File.WriteAllText(Path.Combine(Output,"unity-routes.json"),JsonUtility.ToJson(proof,true));
        }

        [UnityTest,Timeout(180000)]
        public IEnumerator PairClearsTheReusedRampWithThisSamplesNeighbours()
        {
#if UNITY_EDITOR
            GameObject beast=Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Art/Characters/CombatPieces/Static3D/Shijiahou/Shijiahou_Static3D.prefab"));
            Transform ramp=root.transform.Find("left_ramp");
            Collider[] support=terrain.Where(c => c.transform.IsChildOf(ramp) || c.name=="LandingExactCap").ToArray();
            var probe=new GuanzhongRampBodyProbe(beast,1.800879333f,true,terrain,support);
            string path=Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../docs/基础设定/演武地图/关中阶地-20261024/map/all-legal-pair-poses.json"));
            var poses=JsonUtility.FromJson<PairPoses>("{\"poses\":"+File.ReadAllText(path)+"}").poses;
            var facings=new HashSet<int>();
            foreach (PairPose pose in poses.Where(p => p.frontPhysicalSurface=="left_ramp" && p.rearPhysicalSurface=="left_ramp"))
            {
                probe.Fit(Cell(pose.front),90+pose.facing*60,true);
                facings.Add(pose.facing);
                yield return null;
            }
            Assert.That(facings.Count,Is.EqualTo(6));
            SetView(38,-25,4.7f,new Vector3(3.7f,.3f,4));
            Capture("runtime-pair-ramp");
            File.WriteAllText(Path.Combine(Output,"unity-pair.json"),JsonUtility.ToJson(new PairProof {
                poses=probe.Poses,facings=facings.Count,meshPointChecks=probe.VertexChecks,
                maxFootGap=probe.MaxFootGap,maxPenetration=probe.MaxBodyPenetration },true));
            Object.Destroy(beast);
#endif
        }

        private static float Height(Stair stair,float z) => stair.risers.Where(r => r.worldZ<=z+.000001f).Select(r => r.supportYAfter).DefaultIfEmpty(stair.lowerY).Max();
        private static Vector3 Cell(string key)
        {
            int[] qr=key.Split(':').Select(int.Parse).ToArray();
            return new Vector3(qr[0]+.5f*qr[1],0,1+Mathf.Sqrt(3)*.5f*qr[1]);
        }
        private static void SetView(float pitch,float yaw,float size,Vector3 focus)
        {
            Camera camera=Camera.main;
            camera.orthographicSize=size;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            camera.transform.position=focus-camera.transform.forward*25;
        }
        private static void Capture(string name,int width=1920,int height=1080)
        {
            Camera camera=Camera.main;
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            target.Create();
            var request=new RenderPipeline.StandardRequest { destination=target };
            Assert.That(RenderPipeline.SupportsRenderRequest(camera,request),Is.True);
            var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active,previousTarget=camera.targetTexture;
            try
            {
                camera.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,width,height),0,0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(Output,name+".png"),texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;
                camera.targetTexture=previousTarget;
                Object.Destroy(texture);
                target.Release();
                Object.Destroy(target);
            }
        }
        [Serializable] private sealed class Profiles { public Stair[] stairConnections; }
        [Serializable] private sealed class Stair
        {
            public string id;
            public string[] coveredCells,lowerPortalEdges,upperPortalEdges;
            public int clearanceLimit;
            public float lowerY,worldZStart;
            public Platform[] platformCenters;
            public Riser[] risers;
        }
        [Serializable] private sealed class Platform { public float worldX,worldZ,supportY; }
        [Serializable] private sealed class Riser { public float worldZ,supportYAfter; }
        [Serializable] private sealed class GeometryProof
        {
            public bool playMode;
            public string unityVersion;
            public int instances,instanceTriangles,stairTriangles,treadRays,riserRays,portRays,boundaryPrecisionRays;
            public float maxProfileError;
        }
        [Serializable] private sealed class BodyProof
        {
            public bool playMode;
            public int poses,staticPoses,meshPointChecks,frames;
            public float maxStaticFootGap,maxMovingFootGap,maxPenetration,maxSingleLift;
        }
        [Serializable] private sealed class RouteProof { public string[] main,shore; public int bearingSamples; }
        [Serializable] private sealed class PairPoses { public PairPose[] poses; }
        [Serializable] private sealed class PairPose { public string front,frontPhysicalSurface,rearPhysicalSurface; public int facing; }
        [Serializable] private sealed class PairProof
        {
            public int poses,facings,meshPointChecks;
            public float maxFootGap,maxPenetration;
        }
    }
}
