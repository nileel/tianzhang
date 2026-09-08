using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.SceneManagement;

namespace FuyuanPilot.Editor
{
    public static class PilotRigBuilder
    {
        public const string Folder = "Assets/FuyuanPilot";
        public const float Ppu = 768f;
        public static readonly Vector2 Origin = new Vector2(512, 120);
        public static readonly string[] Names = { "Root", "Body", "Chest", "Head", "HairTip",
            "NearShoulder", "NearElbow", "NearWrist", "NearBelly", "NearLip",
            "FarShoulder", "FarElbow", "FarWrist", "FarBelly", "SkirtHem", "FootNear", "FootFar" };
        public static readonly int[] Parents = { -1,0,1,2,3,2,5,6,0,0,2,10,11,0,0,0,0 };
        public static readonly Vector2[] Rest = { new Vector2(0,0), new Vector2(.025f,.78f), new Vector2(.025f,.99f),
            new Vector2(-.018f,1.15f), new Vector2(.075f,1.00f), new Vector2(.17f,1.02f),
            new Vector2(.255f,.86f), new Vector2(.35f,.755f), new Vector2(.16f,.60f), new Vector2(.30f,.61f),
            new Vector2(-.11f,1.01f), new Vector2(-.20f,.85f), new Vector2(-.245f,.725f),
            new Vector2(-.12f,.61f), new Vector2(.025f,.14f), new Vector2(.105f,0), new Vector2(-.095f,.005f) };
        static readonly string[] Layers = { "foot_far", "foot_near", "sleeve_far", "hand_far", "robe_back",
            "robe_front", "hair_back", "head", "torso", "sleeve_near", "hand_near" };

        [MenuItem("Fuyuan Pilot/Rebuild Editable Sample")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder + "/Animation");
            Directory.CreateDirectory(Folder + "/Scenes");
            Directory.CreateDirectory(Folder + "/Prefabs");
            foreach (string name in Layers) ImportAndSkin(name);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var actor = new GameObject("FuYuan_Direction1_Editable");
            var bones = new Transform[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                bones[i] = new GameObject(Names[i]).transform;
                bones[i].SetParent(Parents[i] < 0 ? actor.transform : bones[Parents[i]], false);
                bones[i].localPosition = Parents[i] < 0 ? Rest[i] : Rest[i] - Rest[Parents[i]];
            }
            for (int i = 0; i < Layers.Length; i++)
            {
                var go = new GameObject(Layers[i]);
                go.transform.SetParent(actor.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/Art/" + Layers[i] + ".png");
                sr.sortingOrder = i;
                var skin = go.AddComponent<SpriteSkin>();
                skin.autoRebind = false;
                skin.alwaysUpdate = true;
                skin.forceCpuDeformation = true;
                skin.SetRootBone(bones[0]);
                var state = skin.SetBoneTransforms(bones);
                if (state != SpriteSkinState.Ready) throw new Exception(Layers[i] + ": " + state);
            }
            var animator = actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = PilotAnimationAuthor.CreateController();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PrefabUtility.SaveAsPrefabAsset(actor, Folder + "/Prefabs/FuYuan_Direction1.prefab");
            UnityEngine.Object.DestroyImmediate(actor);
            actor = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Prefabs/FuYuan_Direction1.prefab"));
            actor.transform.position = new Vector3(.5f,.34f,.1339746f);
            actor.transform.rotation = Quaternion.Euler(38,0,0);
            var close = MakeCamera("CloseCamera", new Rect(0,0,.55f,1), .90f);
            var tactical = MakeCamera("TacticalCamera", new Rect(.55f,0,.45f,1), 6.2f);
            tactical.transform.position = new Vector3(0,8,-10);
            close.transform.position = actor.transform.position + close.transform.up * .66f - close.transform.forward * 10;
            BuildBoard();
            var runner = new GameObject("PilotPlaybackAndCapture").AddComponent<PilotPlayback>();
            runner.actor = actor;
            runner.closeCamera = close;
            runner.tacticalCamera = tactical;
            runner.skins = actor.GetComponentsInChildren<SpriteSkin>();
            runner.bones = bones = actor.GetComponentsInChildren<Transform>().Where(t => Names.Contains(t.name)).OrderBy(t => Array.IndexOf(Names, t.name)).ToArray();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, Folder + "/Scenes/FuyuanSkinningPilot.unity");
            EditorBuildSettings.scenes = new[] {new EditorBuildSettingsScene(Folder + "/Scenes/FuyuanSkinningPilot.unity", true)};
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.companyName = "TianZhang Experiment";
            PlayerSettings.productName = "FuYuan 2D Skinning Pilot";
            AssetDatabase.SaveAssets();
            Debug.Log("PILOT_BUILD_OK: 11 SpriteSkin, 17 bones, editable PNG mesh/weights, 2 animation clips.");
        }

        static void ImportAndSkin(string name)
        {
            string path = Folder + "/Art/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = Ppu;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(Origin.x / 1024f, Origin.y / 1280f);
            importer.SetTextureSettings(settings);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            GUID id = provider.GetSpriteRects()[0].spriteID;
            var spriteBones = new List<SpriteBone>();
            for (int i = 0; i < Names.Length; i++)
                spriteBones.Add(new SpriteBone { name = Names[i], guid = Names[i], parentId = Parents[i],
                    position = Parents[i] < 0 ? Origin + Rest[i] * Ppu : (Rest[i] - Rest[Parents[i]]) * Ppu,
                    rotation = Quaternion.identity, length = i == 0 ? 1 : 60 });
            provider.GetDataProvider<ISpriteBoneDataProvider>().SetBones(id, spriteBones);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var pixels = tex.GetPixels32();
            int xmin = tex.width, ymin = tex.height, xmax = 0, ymax = 0;
            for (int y = 0; y < tex.height; y++) for (int x = 0; x < tex.width; x++)
                if (pixels[y * tex.width + x].a > 8) { xmin = Mathf.Min(xmin,x); ymin = Mathf.Min(ymin,y); xmax = Mathf.Max(xmax,x); ymax = Mathf.Max(ymax,y); }
            xmin = Mathf.Max(0,xmin-3); ymin = Mathf.Max(0,ymin-3); xmax = Mathf.Min(tex.width-1,xmax+3); ymax = Mathf.Min(tex.height-1,ymax+3);
            int nx = Mathf.CeilToInt((xmax-xmin)/14f), ny = Mathf.CeilToInt((ymax-ymin)/14f);
            var verts = new List<Vertex2DMetaData>();
            for (int y = 0; y <= ny; y++) for (int x = 0; x <= nx; x++)
            {
                var pixel = new Vector2(Mathf.Lerp(xmin,xmax,x/(float)nx), Mathf.Lerp(ymin,ymax,y/(float)ny));
                verts.Add(new Vertex2DMetaData { position = pixel, boneWeight = Weight(name,(pixel-Origin)/Ppu) });
            }
            var indices = new List<int>();
            var edges = new List<Vector2Int>();
            for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
            {
                int a = y*(nx+1)+x, b=a+1,c=a+nx+1,d=c+1;
                indices.AddRange(new[]{a,c,b,b,c,d});
                edges.Add(new Vector2Int(a,b)); edges.Add(new Vector2Int(a,c)); edges.Add(new Vector2Int(b,c));
                if (y == ny-1) edges.Add(new Vector2Int(c,d));
                if (x == nx-1) edges.Add(new Vector2Int(b,d));
            }
            var mesh = provider.GetDataProvider<ISpriteMeshDataProvider>();
            mesh.SetVertices(id,verts.ToArray()); mesh.SetIndices(id,indices.ToArray()); mesh.SetEdges(id,edges.ToArray());
            provider.Apply();
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite.GetBones().Length != Names.Length) throw new Exception("Bone persistence failed: " + name);
            Debug.Log("SKIN_SOURCE " + name + " vertices=" + verts.Count + " triangles=" + indices.Count/3);
        }

        static BoneWeight Weight(string layer, Vector2 p)
        {
            int rigid = layer == "head" ? 3 : layer == "hand_near" ? 7 : layer == "hand_far" ? 12 : layer == "foot_near" ? 15 : layer == "foot_far" ? 16 : -1;
            if (rigid >= 0) return new BoneWeight { boneIndex0 = rigid, weight0 = 1 };
            int[] candidates = layer == "sleeve_near" ? new[]{5,6,7,8,9} : layer == "sleeve_far" ? new[]{10,11,12,13} : layer == "hair_back" ? new[]{3,4} : layer == "torso" ? new[]{1,2} : new[]{1,14,0};
            var sorted = candidates.Select(i => new {index=i, w=1f/Mathf.Pow(Vector2.Distance(p,Rest[i])+.055f,4)}).OrderByDescending(v=>v.w).Take(4).ToArray();
            float sum = sorted.Sum(v=>v.w);
            var w = new BoneWeight {boneIndex0=sorted[0].index,weight0=sorted[0].w/sum};
            if (sorted.Length>1) { w.boneIndex1=sorted[1].index; w.weight1=sorted[1].w/sum; }
            if (sorted.Length>2) { w.boneIndex2=sorted[2].index; w.weight2=sorted[2].w/sum; }
            if (sorted.Length>3) { w.boneIndex3=sorted[3].index; w.weight3=sorted[3].w/sum; }
            return w;
        }

        static Camera MakeCamera(string name, Rect rect, float size)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.orthographic=true; cam.orthographicSize=size; cam.rect=rect;
            cam.transform.rotation=Quaternion.Euler(38,0,0);
            cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(.20f,.235f,.255f);
            cam.nearClipPlane=.1f; cam.farClipPlane=60f;
            return cam;
        }

        static void BuildBoard()
        {
            var mat = new Material(Shader.Find("Unlit/Color")) {color=new Color(.255f,.29f,.30f)};
            AssetDatabase.CreateAsset(mat,Folder+"/Board.mat");
            var root = new GameObject("MetricHexBoard_IndependentExperiment");
            for (int q=-4;q<=4;q++) for(int r=-4;r<=4;r++)
            {
                var go=new GameObject("Hex_"+q+"_"+r); go.transform.SetParent(root.transform,false);
                go.transform.position=new Vector3(q+r*.5f,.34f,r*.8660254f+1f);
                var vertices=new Vector3[7];
                for(int k=0;k<6;k++){float a=(60*k+30)*Mathf.Deg2Rad;vertices[k+1]=new Vector3(Mathf.Cos(a)*.56f,0,Mathf.Sin(a)*.56f);}
                var triangles=new List<int>(); for(int k=0;k<6;k++)triangles.AddRange(new[]{0,(k+1)%6+1,k+1});
                var mesh=new Mesh {name="Hex"};mesh.vertices=vertices;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();
                // Scene meshes are saved as explicit editable assets rather than transient objects.
                if(q==-4&&r==-4) AssetDatabase.CreateAsset(mesh,Folder+"/Hex.asset");
                else UnityEngine.Object.DestroyImmediate(mesh);
                go.AddComponent<MeshFilter>().sharedMesh=AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/Hex.asset");
                go.AddComponent<MeshRenderer>().sharedMaterial=mat;
            }
        }

        [MenuItem("Fuyuan Pilot/Build Standalone Player")]
        public static void BuildPlayer()
        {
            Build();
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{Folder+"/Scenes/FuyuanSkinningPilot.unity"},
                locationPathName="../Build/FuyuanPilot.exe", target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development });
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception(result.summary.result.ToString());
        }
    }
}
