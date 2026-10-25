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
using Object=UnityEngine.Object;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongPropSamplePlayModeTests
    {
        private const string ScenePath="Assets/Scenes/GuanzhongPropSample.unity";
        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
            "../../assets/generated-scene-art/guanzhong-jindan-demo/d3.5-prop-sample-20261025"));

        [UnityTest]
        public IEnumerator SavedStaticStatesHaveDistinctSilhouettesAndLeavePathsClear()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath,new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            GameObject root=GameObject.Find("D35_StaticPropStates");
            Transform props=root.transform.Find("IndependentProps");
            Assert.That(props.childCount,Is.EqualTo(11));
            Assert.That(root.GetComponentsInChildren<Collider>(true),Is.Empty);
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty,"No durability, obstacle, fuel or field registration.");
            var proof=new Proof { playMode=Application.isPlaying,instances=props.childCount,minPathClearance=float.MaxValue };
            var ground=new List<Collider>();
            foreach (MeshFilter filter in root.transform.Find("ReusedGround").GetComponentsInChildren<MeshFilter>())
                if (filter.name=="LandingExactCap") ground.Add(filter.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();
            foreach (Transform prop in props)
            {
                Assert.That(prop.Cast<Transform>().Count(c => c.gameObject.activeSelf),Is.EqualTo(1));
                float pathZ=prop.position.z-.4330127f;
                // Check all inactive variants too, so switching state cannot unexpectedly obstruct the sample path.
                foreach (MeshFilter filter in prop.GetComponentsInChildren<MeshFilter>(true))
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 world=filter.transform.TransformPoint(v);
                    proof.minPathClearance=Mathf.Min(proof.minPathClearance,world.z-pathZ);
                    Assert.That(world.z-pathZ,Is.GreaterThan(.20f),prop.name+" crosses the 0.4m clear row-centre strip.");
                    if (world.y<.35f)
                    {
                        Assert.That(ground.Any(c => c.Raycast(new Ray(new Vector3(world.x,2,world.z),Vector3.down),out RaycastHit h,3) &&
                            Mathf.Abs(h.point.y-.340433121f)<.0001f),Is.True,"Prop root must sit on an existing tile cap.");
                        Assert.That(world.y,Is.GreaterThanOrEqualTo(.3374f),"Plant roots may embed at most 3 mm.");
                        proof.baseSupportRays++;
                    }
                    proof.meshVertexChecks++;
                }
                proof.activeTriangles+=prop.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length/3);
            }
            proof.lowWallHeight=BoundsOf(props.Find("LowWall_Intact")).size.y;
            proof.highWallHeight=BoundsOf(props.Find("HighWall_Intact")).size.y;
            proof.steleHeight=BoundsOf(props.Find("SupportStele_Intact")).size.y;
            proof.failedSteleHeight=BoundsOf(props.Find("SupportStele_Failed")).size.y;
            proof.livingPlantHeight=BoundsOf(props.Find("LowPlant_Living")).size.y;
            proof.charredPlantHeight=BoundsOf(props.Find("LowPlant_Charred")).size.y;
            Assert.That(proof.lowWallHeight,Is.InRange(.36f,.39f));
            Assert.That(proof.highWallHeight,Is.InRange(.98f,1.02f));
            Assert.That(proof.steleHeight,Is.InRange(.74f,.77f));
            Assert.That(proof.failedSteleHeight,Is.LessThan(proof.steleHeight*.5f));
            Assert.That(proof.charredPlantHeight,Is.LessThan(proof.livingPlantHeight*.7f));
            Transform stele=props.Find("SupportStele_Intact");
            Transform glow=stele.Find("Intact/Glow");
            Assert.That(glow.gameObject.activeInHierarchy,Is.True);
            Assert.That(props.Find("SupportStele_Unlit/Intact/Glow").gameObject.activeInHierarchy,Is.False);
            foreach (Renderer renderer in glow.GetComponentsInChildren<Renderer>())
                Assert.That(renderer.sharedMaterial.GetColor("_EmissionColor").maxColorComponent,Is.GreaterThan(.5f));
            Directory.CreateDirectory(Output);
            SetView(53,-12,4.4f,new Vector3(2.1f,.6f,4));
            yield return null;
            Capture("runtime-all-static-states");
            for (int row=0;row<4;row++)
            {
                SetView(30,-12,row==3 ? 1.0f : 1.65f,new Vector3(row==3 ? 1.2f : 2.1f,.65f,1.4f+Mathf.Sqrt(3)*row));
                Capture(new[]{"runtime-low-wall-states","runtime-high-wall-states","runtime-stele-states","runtime-plant-states"}[row]);
            }
            // Exercise the same serialized objects at runtime; no new state controller is introduced.
            SetView(25,-15,.70f,stele.position+Vector3.up*.37f);
            Capture("runtime-stele-glow-on");
            glow.gameObject.SetActive(false);
            yield return null;
            // Unity still returns components on the queried root when that root is inactive.
            Assert.That(glow.GetComponentsInChildren<Renderer>(true).Any(r => r.gameObject.activeInHierarchy && r.enabled),Is.False);
            Capture("runtime-stele-glow-off");
            glow.gameObject.SetActive(true);
            foreach (string prefab in new[]{"LowWall","HighWall","SupportStele","LowPlant"})
            {
                Transform specimen=props.Cast<Transform>().First(t => t.name.StartsWith(prefab+"_"));
                Transform original=specimen.Cast<Transform>().Single(t => t.gameObject.activeSelf);
                foreach (Transform state in specimen)
                {
                    foreach (Transform other in specimen) other.gameObject.SetActive(other==state);
                    Assert.That(specimen.Cast<Transform>().Count(t => t.gameObject.activeSelf),Is.EqualTo(1));
                    Assert.That(specimen.GetComponentsInChildren<Renderer>().Length,Is.GreaterThan(0));
                    proof.stateSwitches++;
                }
                foreach (Transform other in specimen) other.gameObject.SetActive(other==original);
            }
            proof.totalInstanceTriangles=root.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.triangles.Length/3);
            File.WriteAllText(Path.Combine(Output,"unity-props.json"),JsonUtility.ToJson(proof,true));
            Scene sample=SceneManager.GetSceneByPath(ScenePath);
            SceneManager.SetActiveScene(SceneManager.CreateScene("D35_AfterSample"));
            yield return SceneManager.UnloadSceneAsync(sample);
#endif
        }

        private static Bounds BoundsOf(Transform root) => root.GetComponentsInChildren<Renderer>().Select(r => r.bounds)
            .Aggregate((a,b) => { a.Encapsulate(b); return a; });
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
            public int instances,meshVertexChecks,baseSupportRays,activeTriangles,totalInstanceTriangles,stateSwitches;
            public float minPathClearance,lowWallHeight,highWallHeight,steleHeight,failedSteleHeight,livingPlantHeight,charredPlantHeight;
        }
    }
}
