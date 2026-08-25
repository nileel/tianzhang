using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Editor
{
    public static class VisualBaselineBuilder
    {
        public const string PipelineAssetPath =
            "Assets/Settings/Rendering/TianZhangUniversalRenderPipeline.asset";
        public const string LegacyRendererAssetPath =
            "Assets/Settings/Rendering/TianZhangUniversalRenderPipeline_Renderer.asset";
        public const string UniversalRendererAssetPath =
            "Assets/Settings/Rendering/TianZhangUniversalRenderPipeline_UniversalRenderer.asset";
        public const string HexColumnMeshPath =
            "Assets/Art/VisualBaseline/Meshes/VisualBaseline_HexColumn.asset";
        public const string HexOverlayMeshPath =
            "Assets/Art/VisualBaseline/Meshes/VisualBaseline_HexOverlay.asset";
        public const string BackdropMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Backdrop.mat";
        public const string GroundTopMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_GroundTop.mat";
        public const string GroundSideMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_GroundSide.mat";
        public const string SurfaceMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Surface.mat";
        public const string ReachableMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Reachable.mat";
        public const string SelectedMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Selected.mat";
        public const string AttackMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Attack.mat";
        public const string OccluderMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Occluder.mat";
        public const string UnitMaterialPath =
            "Assets/Art/VisualBaseline/Materials/VisualBaseline_Unit.mat";
        public const string UnitMarkerPrefabPath = "Assets/Resources/UnitMarker.prefab";
        public const string StaticChessModelPath =
            "Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.fbx";
        public const string StaticChessMaterialPath =
            "Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.mat";
        public const string StaticChessBaseColorTexturePath =
            "Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess_BaseColor.JPEG";
        public const string StaticChessPrefabPath =
            "Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.prefab";
        public const string StaticChessMotionFolderPath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion";
        public const string StaticChessMotionEffectPrefabPath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotionFx.prefab";
        public const string StaticChessMotionMoveCuePath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotion_Move.wav";
        public const string StaticChessMotionAttackCuePath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotion_Attack.wav";
        public const string StaticChessMotionHitCuePath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotion_Hit.wav";
        public const string StaticChessMotionCastCuePath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotion_Cast.wav";
        public const string StaticChessMotionDeathCuePath =
            "Assets/Art/Characters/StaticChess/FuYuan/Motion/FuYuan_StaticChessMotion_Death.wav";
        public const string TacticalSpriteFolderPath =
            "Assets/Art/Characters/TacticalSprites";
        public const string TacticalSpriteFuYuanFolderPath =
            "Assets/Art/Characters/TacticalSprites/FuYuan";
        public const string TacticalSpritePrefabPath =
            "Assets/Art/Characters/TacticalSprites/FuYuan/FuYuan_TacticalSprite.prefab";
        public const string BattleAnimationSpriteFuYuanFolderPath =
            "Assets/Art/Characters/TacticalSprites/FuYuanBattle";
        public const string BattleAnimationSpritePrefabPath =
            "Assets/Art/Characters/TacticalSprites/FuYuanBattle/FuYuan_BattleAnimationSprite.prefab";
        public const int TacticalSpriteDirectionCount = 6;
        public const int BattleAnimationStateCount = 6;
        public const int BattleAnimationFramesPerDirection = 3;
        public const float TacticalSpritePixelsPerUnit = 512f;
        public static readonly Vector2 TacticalSpritePivot = new Vector2(0.5f, 0.18f);
        public static readonly Vector3 StaticChessFigureEuler = new Vector3(-90f, 0f, 0f);
        public static readonly Vector3 StaticChessBaseScale = new Vector3(0.66f, 0.04f, 0.66f);

        public static string TacticalSpriteTexturePath(int direction) =>
            "Assets/Art/Characters/TacticalSprites/FuYuan/FuYuan_TacticalDirection_" + direction + ".png";

        [MenuItem("天章/视觉/重建 URP 技术基线")]
        public static void Rebuild()
        {
            EnsureFolders();
            ConfigureUniversalRenderer();
            BuildHexColumnMesh();
            BuildHexOverlayMesh();
            BuildMaterials();
            BuildUnitMarkerPrefab();
            BuildStaticChessAssets();
            BuildTacticalSpriteAssets();
            BuildBattleAnimationSpriteAssets();
            AssetDatabase.SaveAssets();

            StartMenuSceneBuilder.Build();
            WorldSceneBuilder.Build();
            SettlementSceneBuilder.Build();
            AdventureSceneBuilder.Build();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void RebuildForBatchMode() => Rebuild();

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Art", "VisualBaseline");
            EnsureFolder("Assets/Art/VisualBaseline", "Materials");
            EnsureFolder("Assets/Art/VisualBaseline", "Meshes");
        }

        private static void EnsureStaticChessFolders()
        {
            EnsureFolder("Assets/Art", "Characters");
            EnsureFolder("Assets/Art/Characters", "StaticChess");
            EnsureFolder("Assets/Art/Characters/StaticChess", "FuYuan");
            EnsureFolder("Assets/Art/Characters/StaticChess/FuYuan", "Motion");
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        private static void ConfigureUniversalRenderer()
        {
            RenderPipelineAsset pipeline = RequireAsset<RenderPipelineAsset>(PipelineAssetPath);
            Type rendererType = Type.GetType(
                "UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            if (rendererType == null)
                throw new InvalidOperationException("URP 17.3.0 UniversalRendererData type is unavailable.");
            UnityEngine.Object renderer = AssetDatabase.LoadMainAssetAtPath(UniversalRendererAssetPath);
            if (renderer == null)
            {
                RequireUnusedPath(UniversalRendererAssetPath);
                renderer = ScriptableObject.CreateInstance(rendererType);
                renderer.name = "TianZhangUniversalRenderPipeline_UniversalRenderer";
                AssetDatabase.CreateAsset(renderer, UniversalRendererAssetPath);
            }
            if (renderer.GetType() != rendererType)
                throw new InvalidOperationException("The frozen Universal Renderer path has an unexpected asset type.");

            renderer.name = "TianZhangUniversalRenderPipeline_UniversalRenderer";
            var rendererSerialized = new SerializedObject(renderer);
            SerializedProperty renderingMode = rendererSerialized.FindProperty("m_RenderingMode") ??
                throw new InvalidOperationException("UniversalRendererData is missing m_RenderingMode.");
            renderingMode.intValue = 0;
            rendererSerialized.ApplyModifiedPropertiesWithoutUndo();
            ReloadRendererResources(renderer);
            EditorUtility.SetDirty(renderer);

            var serialized = new SerializedObject(pipeline);
            SerializedProperty rendererList = serialized.FindProperty("m_RendererDataList") ??
                throw new InvalidOperationException("URP asset is missing m_RendererDataList.");
            rendererList.arraySize = 1;
            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            SerializedProperty defaultIndex = serialized.FindProperty("m_DefaultRendererIndex") ??
                throw new InvalidOperationException("URP asset is missing m_DefaultRendererIndex.");
            defaultIndex.intValue = 0;
            SerializedProperty msaa = serialized.FindProperty("m_MSAA") ??
                throw new InvalidOperationException("URP asset is missing m_MSAA.");
            msaa.intValue = 2;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            if (AssetDatabase.AssetPathExists(LegacyRendererAssetPath) &&
                !AssetDatabase.DeleteAsset(LegacyRendererAssetPath))
                throw new InvalidOperationException("Could not delete the legacy Renderer2D asset.");
        }

        private static void BuildHexColumnMesh()
        {
            Mesh mesh = GetOrCreateMesh(HexColumnMeshPath, "VisualBaseline_HexColumn");
            mesh.Clear();
            const float radius = 0.58f;
            var vertices = new List<Vector3> { new Vector3(0f, 1f, 0f) };
            var normals = new List<Vector3> { Vector3.up };
            var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            var corners = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                corners[i] = new Vector3(Mathf.Cos(angle) * radius, 1f, Mathf.Sin(angle) * radius);
                vertices.Add(corners[i]);
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(corners[i].x / (radius * 2f) + 0.5f, corners[i].z / (radius * 2f) + 0.5f));
            }

            var topTriangles = new List<int>(18);
            for (int i = 0; i < 6; i++)
            {
                topTriangles.Add(0);
                topTriangles.Add((i + 1) % 6 + 1);
                topTriangles.Add(i + 1);
            }

            var sideTriangles = new List<int>(36);
            for (int i = 0; i < 6; i++)
            {
                Vector3 topA = corners[i];
                Vector3 topB = corners[(i + 1) % 6];
                Vector3 bottomA = new Vector3(topA.x, 0f, topA.z);
                Vector3 bottomB = new Vector3(topB.x, 0f, topB.z);
                Vector3 normal = new Vector3(topA.x + topB.x, 0f, topA.z + topB.z).normalized;
                int start = vertices.Count;
                vertices.Add(topA);
                vertices.Add(topB);
                vertices.Add(bottomB);
                vertices.Add(bottomA);
                for (int vertex = 0; vertex < 4; vertex++) normals.Add(normal);
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0f));
                sideTriangles.Add(start);
                sideTriangles.Add(start + 1);
                sideTriangles.Add(start + 2);
                sideTriangles.Add(start);
                sideTriangles.Add(start + 2);
                sideTriangles.Add(start + 3);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(topTriangles, 0);
            mesh.SetTriangles(sideTriangles, 1);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static void ReloadRendererResources(UnityEngine.Object renderer)
        {
            Type reloaderType = Type.GetType(
                "UnityEngine.Rendering.ResourceReloader, Unity.RenderPipelines.Core.Runtime");
            MethodInfo reload = reloaderType?.GetMethod(
                "ReloadAllNullIn",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(object), typeof(string) },
                null);
            if (reload == null)
                throw new InvalidOperationException("URP resource reloader is unavailable.");
            reload.Invoke(null, new object[] { renderer, "Packages/com.unity.render-pipelines.universal" });
        }

        private static void BuildHexOverlayMesh()
        {
            Mesh mesh = GetOrCreateMesh(HexOverlayMeshPath, "VisualBaseline_HexOverlay");
            mesh.Clear();
            const float radius = 0.52f;
            var vertices = new Vector3[7];
            var normals = new Vector3[7];
            var uvs = new Vector2[7];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < vertices.Length; i++) normals[i] = Vector3.up;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                uvs[i + 1] = new Vector2(vertices[i + 1].x / (radius * 2f) + 0.5f, vertices[i + 1].z / (radius * 2f) + 0.5f);
            }
            var triangles = new int[18];
            for (int i = 0; i < 6; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % 6 + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }

        private static Mesh GetOrCreateMesh(string path, string name)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null)
            {
                mesh.name = name;
                return mesh;
            }
            RequireUnusedPath(path);
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void BuildMaterials()
        {
            BuildMaterial(BackdropMaterialPath, "VisualBaseline_Backdrop", new Color(0.16f, 0.18f, 0.17f, 1f), false, true);
            BuildMaterial(GroundTopMaterialPath, "VisualBaseline_GroundTop", new Color(0.45f, 0.38f, 0.25f, 1f), false, true);
            BuildMaterial(GroundSideMaterialPath, "VisualBaseline_GroundSide", new Color(0.22f, 0.18f, 0.13f, 1f), false, true);
            BuildMaterial(SurfaceMaterialPath, "VisualBaseline_Surface", new Color(0.18f, 0.52f, 0.48f, 0.44f), true, false);
            BuildMaterial(ReachableMaterialPath, "VisualBaseline_Reachable", new Color(0.28f, 0.78f, 0.68f, 0.48f), true, false);
            BuildMaterial(SelectedMaterialPath, "VisualBaseline_Selected", new Color(0.95f, 0.72f, 0.22f, 0.58f), true, false);
            BuildMaterial(AttackMaterialPath, "VisualBaseline_Attack", new Color(0.86f, 0.22f, 0.18f, 0.52f), true, false);
            BuildMaterial(OccluderMaterialPath, "VisualBaseline_Occluder", new Color(0.25f, 0.29f, 0.27f, 1f), false, true);
            BuildMaterial(UnitMaterialPath, "VisualBaseline_Unit", new Color(0.72f, 0.76f, 0.72f, 1f), false, true);
        }

        private static void BuildMaterial(string path, string name, Color color, bool transparent, bool lit)
        {
            Shader shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Required URP shader is unavailable.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                RequireUnusedPath(path);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = name;
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Surface", transparent ? 1f : 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", transparent ? 0f : 1f);
            material.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            material.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            material.enableInstancing = true;
            if (transparent)
            {
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Opaque");
                material.renderQueue = (int)RenderQueue.Geometry;
            }
            EditorUtility.SetDirty(material);
        }

        private static void BuildUnitMarkerPrefab()
        {
            Material material = RequireAsset<Material>(UnitMaterialPath);
            var root = new GameObject("UnitMarker");
            try
            {
                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                body.transform.localScale = new Vector3(0.34f, 0.48f, 0.34f);
                ConfigurePrimitive(body, material);

                GameObject facing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                facing.name = "Facing";
                facing.transform.SetParent(root.transform, false);
                facing.transform.localPosition = new Vector3(0f, 0.62f, 0.32f);
                facing.transform.localScale = new Vector3(0.16f, 0.16f, 0.42f);
                ConfigurePrimitive(facing, material);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, UnitMarkerPrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the 3D UnitMarker prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public static void BuildStaticChessAssets()
        {
            EnsureStaticChessFolders();
            BuildStaticChessMotionAssets();
            AssetDatabase.ImportAsset(StaticChessBaseColorTexturePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(StaticChessModelPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(StaticChessModelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Static chess FBX importer is unavailable.");
            if (importer.importAnimation || importer.animationType != ModelImporterAnimationType.None)
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.SaveAndReimport();
            }
            GameObject model = RequireAsset<GameObject>(StaticChessModelPath);
            Material material = GetOrCreateStaticChessMaterial(model);
            var root = new GameObject("FuYuan_StaticChess");
            try
            {
                GameObject figure = (GameObject)PrefabUtility.InstantiatePrefab(model);
                figure.name = "FuYuan_Model";
                figure.transform.SetParent(root.transform, false);
                figure.transform.localPosition = Vector3.zero;
                figure.transform.localRotation = Quaternion.Euler(StaticChessFigureEuler);
                figure.transform.localScale = Vector3.one;
                foreach (MeshRenderer renderer in figure.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                GameObject basePlaceholder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                basePlaceholder.name = "StaticChessBase";
                basePlaceholder.transform.SetParent(root.transform, false);
                basePlaceholder.transform.localPosition = Vector3.zero;
                basePlaceholder.transform.localScale = StaticChessBaseScale;
                ConfigurePrimitive(basePlaceholder, RequireAsset<Material>(UnitMaterialPath));

                AudioSource cueSource = root.AddComponent<AudioSource>();
                cueSource.playOnAwake = false;
                cueSource.spatialBlend = 1f;
                cueSource.rolloffMode = AudioRolloffMode.Linear;
                cueSource.maxDistance = 8f;

                StaticChessPresentationController controller =
                    root.AddComponent<StaticChessPresentationController>();
                ConfigureStaticChessMotionController(controller, cueSource);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StaticChessPrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the static chess prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static void BuildStaticChessMotionAssets()
        {
            foreach (string cuePath in StaticChessMotionCuePaths())
            {
                AssetDatabase.ImportAsset(cuePath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureStaticChessMotionCueImporter(cuePath);
                RequireAsset<AudioClip>(cuePath);
            }

            var root = new GameObject("FuYuan_StaticChessMotionFx");
            try
            {
                ParticleSystem particles = root.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = particles.main;
                main.loop = false;
                main.playOnAwake = false;
                main.duration = 0.18f;
                main.startLifetime = 0.16f;
                main.startSpeed = 0.32f;
                main.startSize = 0.14f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.stopAction = ParticleSystemStopAction.Destroy;

                ParticleSystem.EmissionModule emission = particles.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                ParticleSystem.ShapeModule shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.16f;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StaticChessMotionEffectPrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the static chess motion effect prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string[] StaticChessMotionCuePaths() => new[]
        {
            StaticChessMotionMoveCuePath,
            StaticChessMotionAttackCuePath,
            StaticChessMotionHitCuePath,
            StaticChessMotionCastCuePath,
            StaticChessMotionDeathCuePath,
        };

        private static void ConfigureStaticChessMotionCueImporter(string path)
        {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Static chess motion cue importer is unavailable: " + path);
            importer.forceToMono = true;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.quality = 1f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        private static void ConfigureStaticChessMotionController(
            StaticChessPresentationController controller,
            AudioSource cueSource)
        {
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("motionEffectPrefab").objectReferenceValue =
                RequireAsset<GameObject>(StaticChessMotionEffectPrefabPath);
            serialized.FindProperty("cueSource").objectReferenceValue = cueSource;
            serialized.FindProperty("moveCue").objectReferenceValue = RequireAsset<AudioClip>(StaticChessMotionMoveCuePath);
            serialized.FindProperty("attackCue").objectReferenceValue = RequireAsset<AudioClip>(StaticChessMotionAttackCuePath);
            serialized.FindProperty("hitCue").objectReferenceValue = RequireAsset<AudioClip>(StaticChessMotionHitCuePath);
            serialized.FindProperty("castCue").objectReferenceValue = RequireAsset<AudioClip>(StaticChessMotionCastCuePath);
            serialized.FindProperty("deathCue").objectReferenceValue = RequireAsset<AudioClip>(StaticChessMotionDeathCuePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void BuildTacticalSpriteAssets()
        {
            EnsureTacticalSpriteFolders();
            ConfigureTacticalSpriteImporters();
            BuildTacticalSpritePrefab();
            AssetDatabase.SaveAssets();
        }

        public static void BuildBattleAnimationSpriteAssets()
        {
            BattleAnimationManifest manifest = ReadBattleAnimationManifest();
            EnsureFolder("Assets/Art/Characters/TacticalSprites", "FuYuanBattle");
            for (int state = 0; state < BattleAnimationStateCount; state++)
            {
                BattleAnimationState sourceState = BattleAnimationStateAt(manifest.states, state);
                string sourcePath = Path.Combine(BattleAnimationPilotDirectory(), sourceState.file);
                string destinationPath = BattleAnimationSpriteTexturePath(state);
                VerifyAtlas(sourcePath, sourceState.sha256, BattleAnimationStateName(state));
                if (!File.Exists(destinationPath) || !string.Equals(ComputeSha256(destinationPath), sourceState.sha256,
                        System.StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, destinationPath, true);
                AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceSynchronousImport);
                ConfigureBattleAnimationImporter(destinationPath, state);
            }
            BuildBattleAnimationSpritePrefab();
            AssetDatabase.SaveAssets();
        }

        public static string BattleAnimationSpriteTexturePath(int state) =>
            BattleAnimationSpriteFuYuanFolderPath + "/FuYuan_Battle_" + BattleAnimationStateName(state) + ".png";

        public static string BattleAnimationSpriteName(int state, int direction, int frame) =>
            "FuYuan_Battle_" + BattleAnimationStateName(state) + "_Direction_" + direction + "_Frame_" + frame;

        public static string BattleAnimationStateName(int state)
        {
            switch (state)
            {
                case 0: return "Idle";
                case 1: return "Move";
                case 2: return "Attack";
                case 3: return "Hit";
                case 4: return "Cast";
                case 5: return "Death";
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static string BattleAnimationPilotDirectory() => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "..", "assets", "generated-character-art", "fuyuan-2d-battle-animation-pilot"));

        private static BattleAnimationManifest ReadBattleAnimationManifest()
        {
            string manifestPath = Path.Combine(BattleAnimationPilotDirectory(), "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException("Frozen battle animation manifest is missing: " + manifestPath);
            BattleAnimationManifest manifest = JsonUtility.FromJson<BattleAnimationManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.atlasContract == null || manifest.atlasContract.cell == null ||
                manifest.atlasContract.atlas == null || manifest.atlasContract.pivot == null || manifest.states == null ||
                manifest.atlasContract.columns != BattleAnimationFramesPerDirection || manifest.atlasContract.rows != 6 ||
                manifest.atlasContract.cell.width != 768 || manifest.atlasContract.cell.height != 768 ||
                manifest.atlasContract.atlas.width != 2304 || manifest.atlasContract.atlas.height != 4608 ||
                Mathf.Abs(manifest.atlasContract.pivot.normalized[0] - TacticalSpritePivot.x) > 0.001f ||
                Mathf.Abs(manifest.atlasContract.pivot.normalized[1] - TacticalSpritePivot.y) > 0.001f)
                throw new InvalidOperationException("Frozen battle animation manifest does not match the approved 6x3 atlas contract.");
            for (int state = 0; state < BattleAnimationStateCount; state++)
            {
                BattleAnimationState sourceState = BattleAnimationStateAt(manifest.states, state);
                if (sourceState == null || sourceState.framesPerDirection != BattleAnimationFramesPerDirection ||
                    string.IsNullOrWhiteSpace(sourceState.file) || string.IsNullOrWhiteSpace(sourceState.sha256) ||
                    sourceState.events == null || !HasApprovedEventIndices(state, sourceState.events))
                    throw new InvalidOperationException("Frozen battle animation manifest is incomplete for " + BattleAnimationStateName(state) + ".");
            }
            return manifest;
        }

        private static bool HasApprovedEventIndices(int state, BattleAnimationEvents events)
        {
            switch (state)
            {
                case 0: return events.start == 0 && events.end == 2;
                case 1: return events.start == 0 && events.step == 1 && events.end == 2;
                case 2:
                case 3: return events.start == 0 && events.impact == 1 && events.end == 2;
                case 4: return events.start == 0 && events.release == 1 && events.end == 2;
                case 5: return events.start == 0 && events.terminal == 2;
                default: return false;
            }
        }

        private static BattleAnimationState BattleAnimationStateAt(BattleAnimationStates states, int state)
        {
            switch (state)
            {
                case 0: return states.idle;
                case 1: return states.move;
                case 2: return states.attack;
                case 3: return states.hit;
                case 4: return states.cast;
                case 5: return states.death;
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static void VerifyAtlas(string path, string expectedSha256, string stateName)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Frozen battle animation atlas is missing: " + path);
            if (!string.Equals(ComputeSha256(path), expectedSha256, System.StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Frozen battle animation atlas SHA-256 mismatch: " + stateName);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 26 || bytes[24] != 8 || bytes[25] != 6 ||
                ReadBigEndianInt32(bytes, 16) != 2304 || ReadBigEndianInt32(bytes, 20) != 4608)
                throw new InvalidOperationException("Frozen battle animation atlas is not the approved 2304x4608 RGBA PNG: " + stateName);
        }

        private static void ConfigureBattleAnimationImporter(string path, int state)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Battle animation texture importer is unavailable: " + path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = TacticalSpritePivot;
            importer.SetTextureSettings(settings);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = TacticalSpritePixelsPerUnit;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.spritesheet = BuildBattleAnimationSpriteSheet(state);
            importer.SaveAndReimport();
        }

        private static SpriteMetaData[] BuildBattleAnimationSpriteSheet(int state)
        {
            var sprites = new SpriteMetaData[6 * BattleAnimationFramesPerDirection];
            int index = 0;
            for (int direction = 0; direction < 6; direction++)
            for (int frame = 0; frame < BattleAnimationFramesPerDirection; frame++)
            {
                sprites[index++] = new SpriteMetaData
                {
                    name = BattleAnimationSpriteName(state, direction, frame),
                    rect = new Rect(frame * 768, direction * 768, 768, 768),
                    alignment = (int)SpriteAlignment.Custom,
                    pivot = TacticalSpritePivot,
                };
            }
            return sprites;
        }

        private static void BuildBattleAnimationSpritePrefab()
        {
            var root = new GameObject("FuYuan_BattleAnimationSprite");
            try
            {
                GameObject body = new GameObject("SpriteBody", typeof(SpriteRenderer));
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = Vector3.one;
                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                BattleAnimationSpritePresentationController controller = root.AddComponent<BattleAnimationSpritePresentationController>();
                var serialized = new SerializedObject(controller);
                for (int state = 0; state < BattleAnimationStateCount; state++)
                {
                    SerializedProperty frames = serialized.FindProperty(BattleAnimationFrameFieldName(state)) ??
                        throw new InvalidOperationException("Battle animation controller is missing a state frame field.");
                    frames.arraySize = 6 * BattleAnimationFramesPerDirection;
                    for (int direction = 0; direction < 6; direction++)
                    for (int frame = 0; frame < BattleAnimationFramesPerDirection; frame++)
                    {
                        frames.GetArrayElementAtIndex(direction * BattleAnimationFramesPerDirection + frame).objectReferenceValue =
                            RequireBattleAnimationSprite(state, direction, frame);
                    }
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                renderer.sprite = RequireBattleAnimationSprite(0, 0, 0);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BattleAnimationSpritePrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the battle animation sprite prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static string BattleAnimationFrameFieldName(int state)
        {
            switch (state)
            {
                case 0: return "idleFrames";
                case 1: return "moveFrames";
                case 2: return "attackFrames";
                case 3: return "hitFrames";
                case 4: return "castFrames";
                case 5: return "deathFrames";
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static Sprite RequireBattleAnimationSprite(int state, int direction, int frame)
        {
            string path = BattleAnimationSpriteTexturePath(state);
            string name = BattleAnimationSpriteName(state, direction, frame);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                Sprite sprite = asset as Sprite;
                if (sprite != null && sprite.name == name) return sprite;
            }
            throw new InvalidOperationException("Battle animation sprite is missing from imported atlas: " + name);
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        [System.Serializable]
        private sealed class BattleAnimationManifest
        {
            public BattleAnimationAtlasContract atlasContract;
            public BattleAnimationStates states;
        }

        [System.Serializable]
        private sealed class BattleAnimationAtlasContract
        {
            public int columns;
            public int rows;
            public BattleAnimationDimensions cell;
            public BattleAnimationDimensions atlas;
            public BattleAnimationPivot pivot;
        }

        [System.Serializable]
        private sealed class BattleAnimationDimensions
        {
            public int width;
            public int height;
        }

        [System.Serializable]
        private sealed class BattleAnimationPivot
        {
            public float[] normalized;
        }

        [System.Serializable]
        private sealed class BattleAnimationStates
        {
            public BattleAnimationState idle;
            public BattleAnimationState move;
            public BattleAnimationState attack;
            public BattleAnimationState hit;
            public BattleAnimationState cast;
            public BattleAnimationState death;
        }

        [System.Serializable]
        private sealed class BattleAnimationState
        {
            public string file;
            public int framesPerDirection;
            public string sha256;
            public BattleAnimationEvents events;
        }

        [System.Serializable]
        private sealed class BattleAnimationEvents
        {
            public int start;
            public int step;
            public int impact;
            public int release;
            public int terminal;
            public int end;
        }

        private static void EnsureTacticalSpriteFolders()
        {
            EnsureFolder("Assets/Art/Characters", "TacticalSprites");
            EnsureFolder("Assets/Art/Characters/TacticalSprites", "FuYuan");
        }

        private static void ConfigureTacticalSpriteImporters()
        {
            for (int direction = 0; direction < TacticalSpriteDirectionCount; direction++)
            {
                string path = TacticalSpriteTexturePath(direction);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Tactical sprite texture importer is unavailable: " + path);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (importer.textureType != TextureImporterType.Sprite ||
                    importer.spriteImportMode != SpriteImportMode.Single ||
                    Mathf.Abs(importer.spritePixelsPerUnit - TacticalSpritePixelsPerUnit) > 0.001f ||
                    settings.spriteAlignment != (int)SpriteAlignment.Custom ||
                    Vector2.Distance(settings.spritePivot, TacticalSpritePivot) > 0.001f ||
                    !importer.alphaIsTransparency)
                {
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = TacticalSpritePivot;
                    importer.SetTextureSettings(settings);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = TacticalSpritePixelsPerUnit;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static void BuildTacticalSpritePrefab()
        {
            var root = new GameObject("FuYuan_TacticalSprite");
            try
            {
                GameObject body = new GameObject("SpriteBody", typeof(SpriteRenderer));
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = Vector3.one;
                SpriteRenderer renderer = body.GetComponent<SpriteRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                TacticalSpritePresentationController controller = root.AddComponent<TacticalSpritePresentationController>();
                Sprite[] sprites = new Sprite[TacticalSpriteDirectionCount];
                for (int direction = 0; direction < TacticalSpriteDirectionCount; direction++)
                    sprites[direction] = RequireAsset<Sprite>(TacticalSpriteTexturePath(direction));

                var serialized = new SerializedObject(controller);
                SerializedProperty spritesProperty = serialized.FindProperty("directionSprites") ??
                    throw new InvalidOperationException("Tactical sprite controller is missing the direction sprites field.");
                spritesProperty.arraySize = sprites.Length;
                for (int direction = 0; direction < sprites.Length; direction++)
                    spritesProperty.GetArrayElementAtIndex(direction).objectReferenceValue = sprites[direction];
                serialized.ApplyModifiedPropertiesWithoutUndo();

                renderer.sprite = sprites[0];

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, TacticalSpritePrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the tactical sprite prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Material GetOrCreateStaticChessMaterial(GameObject model)
        {
            MeshRenderer sourceRenderer = model.GetComponentInChildren<MeshRenderer>(true);
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null)
                throw new InvalidOperationException("Static chess FBX is missing its imported mesh material.");
            Material source = sourceRenderer.sharedMaterial;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(StaticChessMaterialPath);
            if (material == null)
            {
                RequireUnusedPath(StaticChessMaterialPath);
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("Required URP Lit shader is unavailable.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, StaticChessMaterialPath);
            }

            Texture2D baseMap = RequireAsset<Texture2D>(StaticChessBaseColorTexturePath);
            if (baseMap.width <= 0 || baseMap.height <= 0)
                throw new InvalidOperationException("Static chess BaseColor texture has no pixels: " + StaticChessBaseColorTexturePath);
            Color baseColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            material.name = "FuYuan_StaticChess";
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", baseColor);
            material.SetTexture("_BaseMap", baseMap);
            material.SetFloat("_Smoothness", 0.1f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigurePrimitive(GameObject target, Material material)
        {
            Collider collider = target.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            MeshRenderer renderer = target.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static T RequireAsset<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException("Required visual baseline asset is missing: " + path);
            return asset;
        }

        private static void RequireUnusedPath(string path)
        {
            if (AssetDatabase.AssetPathExists(path))
                throw new InvalidOperationException("Visual baseline path has an unexpected asset type: " + path);
        }
    }
}
