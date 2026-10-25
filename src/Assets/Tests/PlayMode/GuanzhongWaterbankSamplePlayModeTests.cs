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
    public sealed class GuanzhongWaterbankSamplePlayModeTests
    {
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d3.4-waterbank-sample-20261025"));

        [UnityTest]
        public IEnumerator SavedWaterAndBanksMatchFrozenCellsAndKeepLandingsOpen()
        {
#if UNITY_EDITOR
            const string scenePath="Assets/Scenes/GuanzhongWaterbankSample.unity";
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath,new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            var root=GameObject.Find("D34_WaterbankAndRoutes");
            var surface=root.transform.Find("WaterAndMud");
            var environment=UnityEditor.AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_jindan_demo.asset");
            Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty,"Static sample must not register gameplay objects.");
            var proof=new Proof { playMode=Application.isPlaying,mapVersion=environment.mapVersion };
            var support=new List<Collider>();
            foreach (MeshFilter mesh in root.GetComponentsInChildren<MeshFilter>())
                if (mesh.name=="LandingExactCap" || mesh.transform.IsChildOf(root.transform.Find("front_right_stairs")))
                    support.Add(mesh.gameObject.AddComponent<MeshCollider>());
            int[,] directions={ {1,0},{1,-1},{0,-1},{-1,0},{-1,1},{0,1} };
            foreach (var cell in environment.battlefieldCells.Where(c => c.surfaceKind=="shallow_water" || c.surfaceKind=="mud_bank"))
            {
                bool water=cell.surfaceKind=="shallow_water";
                Transform tile=surface.Find($"{(water ? "Water" : "Mud")}_{cell.q}_{cell.r}");
                Assert.That(tile,Is.Not.Null);
                Vector3 expected=new Vector3(cell.q+.5f*cell.r,.340433121f+.75f*cell.heightLevel,1+Mathf.Sqrt(3)*.5f*cell.r);
                Assert.That(Vector3.Distance(tile.position,expected),Is.LessThan(.00001f));
                Assert.That(root.transform.Find($"Tile_{cell.q}_{cell.r}_L{cell.heightLevel}"),Is.Null,"No duplicate horizontal tile.");
                MeshFilter cap=tile.GetComponentsInChildren<MeshFilter>().Single(m => m.name=="LandingExactCap");
                var capCollider=cap.GetComponent<MeshCollider>();
                Physics.SyncTransforms();
                Assert.That(capCollider.Raycast(new Ray(expected+Vector3.up,Vector3.down),out RaycastHit hit,2),Is.True);
                proof.maxSupportError=Mathf.Max(proof.maxSupportError,Mathf.Abs(hit.point.y-expected.y));
                Assert.That(hit.point.y,Is.EqualTo(expected.y).Within(.00001f));
                if (!water) { proof.mudCells++; continue; }
                proof.waterCells++;
                if (cell.heightLevel==1) proof.upperWaterCells++;
                Transform sheet=tile.Find("WaterSurface");
                foreach (MeshFilter filter in sheet.GetComponentsInChildren<MeshFilter>())
                {
                    Vector3[] vertices=filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                    foreach (Vector3 vertex in vertices)
                    {
                        Assert.That(vertex.y,Is.EqualTo(expected.y+.004f).Within(.00001f));
                        Vector3 p=vertex-expected;
                        float hexRadius=Mathf.Max(Mathf.Abs(p.x),Mathf.Abs(.5f*p.x+.8660254f*p.z),Mathf.Abs(.5f*p.x-.8660254f*p.z));
                        Assert.That(hexRadius,Is.LessThanOrEqualTo(.50001f));
                        if (p.sqrMagnitude>.01f) Assert.That(hexRadius,Is.EqualTo(.5f).Within(.00001f));
                        proof.waterVertices++;
                    }
                    var collider=filter.gameObject.AddComponent<MeshCollider>();
                    Physics.SyncTransforms();
                    for (int d=0;d<6;d++)
                    for (int i=1;i<=12;i++)
                    {
                        Vector3 p=expected+Quaternion.Euler(0,d*60,0)*new Vector3(i*.04f,1,0);
                        Assert.That(collider.Raycast(new Ray(p,Vector3.down),out hit,2),Is.True,"Visible water face must point up.");
                        Assert.That(hit.point.y,Is.EqualTo(expected.y+.004f).Within(.00001f));
                        proof.surfaceRays++;
                    }
                }
                for (int d=0;d<6;d++)
                {
                    bool wet=environment.battlefieldCells.Any(c => c.q==cell.q+directions[d,0] && c.r==cell.r+directions[d,1] &&
                        c.surfaceKind=="shallow_water" && c.heightLevel==cell.heightLevel);
                    Transform edge=tile.Find("Shore_"+d);
                    Assert.That(edge==null,Is.EqualTo(wet),"Only external wet/dry edges receive the shore band.");
                    if (edge==null) continue;
                    foreach (MeshFilter f in edge.GetComponentsInChildren<MeshFilter>())
                    foreach (Vector3 v in f.sharedMesh.vertices)
                    {
                        Vector3 p=f.transform.TransformPoint(v)-expected;
                        Assert.That(p.y,Is.EqualTo(.005f).Within(.00001f));
                        Vector3 canonical=Quaternion.Euler(0,-d*60,0)*p;
                        Assert.That(canonical.x,Is.InRange(.45499f,.50001f));
                    }
                    proof.shoreEdges++;
                }
            }
            Assert.That(proof.waterCells,Is.EqualTo(6));
            Assert.That(proof.upperWaterCells,Is.EqualTo(1));
            Assert.That(proof.mudCells,Is.EqualTo(2));
            // Test only affected joins; do not repeat the unchanged full stair/ramp suite.
            int[][] joins={new[]{4,0,3,0},new[]{5,0,6,0},new[]{5,1,5,2},new[]{5,4,5,5},
                new[]{5,4,4,5},new[]{6,0,7,0},new[]{7,0,8,0},new[]{8,0,9,0}};
            Physics.SyncTransforms();
            foreach (int[] join in joins)
            {
                var from=environment.battlefieldCells.Single(c => c.q==join[0] && c.r==join[1]);
                var to=environment.battlefieldCells.Single(c => c.q==join[2] && c.r==join[3]);
                Assert.That(to.blocksGroundMove,Is.False);
                Assert.That(environment.directedEdges.Any(e => e.fromQ==from.q && e.fromR==from.r && e.toQ==to.q && e.toR==to.r && e.allowsMovement),Is.True);
                Vector3 a=Cell(join[0],join[1]),b=Cell(join[2],join[3]);
                float y=.340433121f+.75f*from.heightLevel;
                for (int i=0;i<=20;i++)
                {
                    Vector3 p=Vector3.Lerp(a,b,i/20f);
                    bool hit=support.Any(c => c.Raycast(new Ray(p+Vector3.up*4,Vector3.down),out RaycastHit h,8) && Mathf.Abs(h.point.y-y)<.0001f);
                    Assert.That(hit,Is.True,$"Unsupported landing {string.Join(",",join)} t={i}");
                    proof.landingRays++;
                }
            }
            Directory.CreateDirectory(Output);
            SetView(47,-20,4.5f,new Vector3(6,.6f,3.4f));
            yield return null;
            Capture("runtime-waterbank-overview");
            SetView(52,0,2.35f,new Vector3(4.9f,.35f,1.7f));
            Capture("runtime-low-water-and-landings");
            SetView(48,-35,2.1f,new Vector3(6.6f,.8f,4.5f));
            Capture("runtime-upper-pool-and-stair");
            SetView(90,0,3.3f,new Vector3(5.4f,.5f,3.0f));
            Capture("runtime-hex-boundaries");
            proof.meshInstances=root.GetComponentsInChildren<MeshFilter>().Length;
            proof.instanceTriangles=root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length/3);
            File.WriteAllText(Path.Combine(Output,"unity-waterbank.json"),JsonUtility.ToJson(proof,true));
            Scene sample=SceneManager.GetSceneByPath(scenePath);
            SceneManager.SetActiveScene(SceneManager.CreateScene("D34_AfterSample"));
            yield return SceneManager.UnloadSceneAsync(sample);
#endif
        }

        private static Vector3 Cell(int q,int r) => new Vector3(q+.5f*r,0,1+Mathf.Sqrt(3)*.5f*r);
        private static void SetView(float pitch,float yaw,float size,Vector3 focus)
        {
            Camera camera=Camera.main;
            camera.orthographicSize=size;
            camera.transform.rotation=Quaternion.Euler(pitch,yaw,0);
            camera.transform.position=focus-camera.transform.forward*25;
        }
        private static void Capture(string name)
        {
            Camera camera=Camera.main;
            var target=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);
            target.Create();
            var request=new RenderPipeline.StandardRequest { destination=target };
            Assert.That(RenderPipeline.SupportsRenderRequest(camera,request),Is.True);
            var texture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
            RenderTexture previous=RenderTexture.active,previousTarget=camera.targetTexture;
            try
            {
                camera.targetTexture=target;
                RenderPipeline.SubmitRenderRequest(camera,request);
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1920,1080),0,0);
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
        [Serializable] private sealed class Proof
        {
            public bool playMode;
            public string mapVersion;
            public int waterCells,upperWaterCells,mudCells,waterVertices,shoreEdges,surfaceRays,landingRays,meshInstances,instanceTriangles;
            public float maxSupportError;
        }
    }
}
