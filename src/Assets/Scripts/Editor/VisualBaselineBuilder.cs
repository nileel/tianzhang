using System;
using System.Collections.Generic;
using System.Reflection;
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
        public const string StaticChessPrefabPath =
            "Assets/Art/Characters/StaticChess/FuYuan/FuYuan_StaticChess.prefab";
        public const string TacticalSpriteFolderPath =
            "Assets/Art/Characters/TacticalSprites";
        public const string TacticalSpriteFuYuanFolderPath =
            "Assets/Art/Characters/TacticalSprites/FuYuan";
        public const string TacticalSpritePrefabPath =
            "Assets/Art/Characters/TacticalSprites/FuYuan/FuYuan_TacticalSprite.prefab";
        public const int TacticalSpriteDirectionCount = 6;

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
                figure.transform.localRotation = Quaternion.identity;
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
                basePlaceholder.transform.localPosition = new Vector3(0f, -0.04f, 0f);
                basePlaceholder.transform.localScale = new Vector3(0.66f, 0.04f, 0.66f);
                ConfigurePrimitive(basePlaceholder, RequireAsset<Material>(UnitMaterialPath));

                root.AddComponent<TianZhang.Features.CombatPresentation.StaticChessPresentationController>();
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, StaticChessPrefabPath);
                if (saved == null) throw new InvalidOperationException("Could not save the static chess prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        public static void BuildTacticalSpriteAssets()
        {
            EnsureTacticalSpriteFolders();
            ConfigureTacticalSpriteImporters();
            BuildTacticalSpritePrefab();
            AssetDatabase.SaveAssets();
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
                if (importer.textureType != TextureImporterType.Sprite ||
                    importer.spriteImportMode != SpriteImportMode.Single ||
                    Mathf.Abs(importer.spritePixelsPerUnit - 512f) > 0.001f ||
                    Vector2.Distance(importer.spritePivot, new Vector2(0.5f, 0.125f)) > 0.001f ||
                    !importer.alphaIsTransparency)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 512f;
                    importer.spritePivot = new Vector2(0.5f, 0.125f);
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

            Texture baseMap = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (baseMap == null && source.HasProperty("_MainTex")) baseMap = source.GetTexture("_MainTex");
            Color baseColor = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            material.name = "FuYuan_StaticChess";
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", baseColor);
            if (baseMap != null) material.SetTexture("_BaseMap", baseMap);
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
