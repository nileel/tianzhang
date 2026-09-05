using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TianZhang.ClothWideSleevePilot.Editor
{
    public static class ClothWideSleeveExperimentSceneBuilder
    {
        public const string ScenePath = "Assets/Tests/Scenes/ClothWideSleeveExperimentScene.unity";
        public const string ModelPath =
            "Assets/Tests/ClothWideSleevePilot/Models/TZ_ClothWideSleevePilot_v001.fbx";
        public const string MaterialFolder = "Assets/Tests/ClothWideSleevePilot/Materials";
        public const string BodyMaterialPath = MaterialFolder + "/ClothWideSleevePilot_Body.mat";
        public const string SleeveMaterialPath = MaterialFolder + "/ClothWideSleevePilot_Sleeve.mat";
        public const string GroundMaterialPath = MaterialFolder + "/ClothWideSleevePilot_Ground.mat";

        private static readonly float[] SixDirectionYaw = { 90f, 150f, 210f, 270f, 330f, 30f };

        [MenuItem("天章/测试/重建单侧宽袖 Cloth 实验")]
        public static void Build()
        {
            EnsureFolder("Assets/Tests/ClothWideSleevePilot");
            EnsureFolder("Assets/Tests/ClothWideSleevePilot/Materials");
            EnsureFolder("Assets/Tests/Scenes");
            ConfigureModelImporter();

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
                throw new InvalidOperationException("Cloth pilot model is missing: " + ModelPath);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().SingleOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException("Cloth pilot model did not import as a valid Humanoid avatar.");

            Material bodyMaterial = CreateOrUpdateMaterial(BodyMaterialPath, new Color(0.66f, 0.63f, 0.57f), false);
            Material sleeveMaterial = CreateOrUpdateMaterial(SleeveMaterialPath, new Color(0.12f, 0.32f, 0.43f), true);
            Material groundMaterial = CreateOrUpdateMaterial(GroundMaterialPath, new Color(0.17f, 0.19f, 0.18f), false);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("ClothWideSleeveExperimentRoot");
            var motionRoot = new GameObject("ExperimentCharacterMotionRoot");
            motionRoot.transform.SetParent(root.transform, false);

            GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
            character.name = "QuaterniusStandardBody_WithLeftWideSleeve";
            character.transform.SetParent(motionRoot.transform, false);
            character.transform.localPosition = Vector3.zero;
            character.transform.localRotation = Quaternion.identity;
            character.transform.localScale = Vector3.one;

            Animator animator = character.GetComponent<Animator>();
            if (animator == null)
                animator = character.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            SkinnedMeshRenderer[] renderers = character.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            SkinnedMeshRenderer sleeveRenderer = renderers.SingleOrDefault(item => item.name == "TZ_LeftWideSleeve_Cloth");
            if (sleeveRenderer == null)
                throw new InvalidOperationException("Imported left wide sleeve renderer was not found.");

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Material material = renderer == sleeveRenderer ? sleeveMaterial : bodyMaterial;
                renderer.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, renderer.sharedMaterials.Length)).ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.updateWhenOffscreen = true;
            }

            Cloth cloth = sleeveRenderer.gameObject.AddComponent<Cloth>();
            ConfigureCloth(cloth, sleeveRenderer, character.transform);
            CreateEnvironment(root.transform, groundMaterial);
            Camera camera = CreateCamera(root.transform);

            ClothWideSleevePilotController controller = root.AddComponent<ClothWideSleevePilotController>();
            var serialized = new SerializedObject(controller);
            SetObject(serialized, "animator", animator);
            SetObject(serialized, "motionRoot", motionRoot.transform);
            SetObject(serialized, "experimentCamera", camera);
            SetObject(serialized, "sleeveCloth", cloth);
            SetObject(serialized, "sleeveRenderer", sleeveRenderer);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Failed to save cloth wide-sleeve experiment scene.");
            AssetDatabase.SaveAssets();

            if (EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath))
                throw new InvalidOperationException("The cloth experiment scene must remain outside BuildSettings.");

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<ClothWideSleevePilotController>() == null)
                throw new InvalidOperationException("Saved cloth experiment scene did not reopen with its runtime owner.");
        }

        public static void BuildForBatchMode()
        {
            Build();
        }

        public static void BuildPlayerForBatchMode()
        {
            Build();
            string buildPath = ReadCommandLineValue("-clothPilotBuildPath");
            if (string.IsNullOrWhiteSpace(buildPath))
                throw new InvalidOperationException("-clothPilotBuildPath is required.");

            buildPath = Path.GetFullPath(buildPath);
            Directory.CreateDirectory(Path.GetDirectoryName(buildPath) ?? throw new InvalidOperationException("Build path has no directory."));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Cloth pilot player build failed: " + report.summary.result);
        }

        private static void ConfigureModelImporter()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            }
            if (importer == null)
                throw new InvalidOperationException("Cloth pilot FBX did not produce a ModelImporter.");

            bool changed = importer.animationType != ModelImporterAnimationType.Human ||
                           importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                           importer.importAnimation || !importer.isReadable || importer.importBlendShapes ||
                           importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                           importer.importCameras || importer.importLights;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.isReadable = true;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importCameras = false;
            importer.importLights = false;
            if (changed)
                importer.SaveAndReimport();
        }

        private static void ConfigureCloth(Cloth cloth, SkinnedMeshRenderer sleeveRenderer, Transform characterRoot)
        {
            cloth.useGravity = true;
            cloth.damping = 0.34f;
            cloth.friction = 0.24f;
            cloth.collisionMassScale = 0.18f;
            cloth.stretchingStiffness = 0.88f;
            cloth.bendingStiffness = 0.42f;
            cloth.useTethers = true;
            cloth.useVirtualParticles = 1f;
            cloth.worldVelocityScale = 0.82f;
            cloth.worldAccelerationScale = 0.72f;
            cloth.clothSolverFrequency = 180f;
            cloth.stiffnessFrequency = 10f;
            cloth.enableContinuousCollision = true;
            cloth.selfCollisionDistance = 0.028f;
            cloth.selfCollisionStiffness = 0.62f;
            cloth.sleepThreshold = 0f;

            Transform shoulder = FindRequired(characterRoot, "upperarm_l");
            Transform wrist = FindRequired(characterRoot, "hand_l");
            Vector3 shoulderLocal = sleeveRenderer.transform.InverseTransformPoint(shoulder.position);
            Vector3 wristLocal = sleeveRenderer.transform.InverseTransformPoint(wrist.position);
            Vector3 axis = wristLocal - shoulderLocal;
            float axisLengthSquared = axis.sqrMagnitude;
            Debug.Log("[ClothWideSleevePilot] Imported local scale proof: shoulder=" + shoulderLocal +
                      ", wrist=" + wristLocal + ", armLength=" + axis.magnitude.ToString("F6") +
                      ", sleeveBounds=" + sleeveRenderer.sharedMesh.bounds.size +
                      ", rendererLossyScale=" + sleeveRenderer.transform.lossyScale +
                      ", worldArmLength=" + Vector3.Distance(shoulder.position, wrist.position).ToString("F6") +
                      ", worldSleeveBounds=" + sleeveRenderer.bounds.size + ".");
            if (axisLengthSquared < 0.01f)
                throw new InvalidOperationException("Left arm bind axis is too short for Cloth constraints.");

            Vector3[] vertices = sleeveRenderer.sharedMesh.vertices;
            var coefficients = new ClothSkinningCoefficient[vertices.Length];
            var selfCollisionIndices = new List<uint>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                float t = Mathf.Clamp01(Vector3.Dot(vertices[i] - shoulderLocal, axis) / axisLengthSquared);
                float maxDistance;
                if (t <= 0.17f)
                    maxDistance = 0f;
                else if (t <= 0.28f)
                    maxDistance = Mathf.Lerp(0f, 0.12f, Mathf.InverseLerp(0.17f, 0.28f, t));
                else if (t <= 0.58f)
                    maxDistance = Mathf.Lerp(0.12f, 0.40f, Mathf.InverseLerp(0.28f, 0.58f, t));
                else
                    maxDistance = Mathf.Lerp(0.40f, 0.64f, Mathf.InverseLerp(0.58f, 1f, t));

                coefficients[i] = new ClothSkinningCoefficient
                {
                    maxDistance = maxDistance,
                    collisionSphereDistance = 0.012f
                };
                if (maxDistance > 0.001f)
                    selfCollisionIndices.Add((uint)i);
            }
            cloth.coefficients = coefficients;
            cloth.SetSelfAndInterCollisionIndices(selfCollisionIndices);

            Transform spine = FindRequired(characterRoot, "spine_02");
            Transform neck = FindRequired(characterRoot, "neck_01");
            Transform upperArm = FindRequired(characterRoot, "upperarm_l");
            Transform lowerArm = FindRequired(characterRoot, "lowerarm_l");
            Transform hand = FindRequired(characterRoot, "hand_l");
            SphereCollider torsoLow = CreateSphere(spine, "ClothCollider_TorsoLow", 0.22f);
            SphereCollider torsoHigh = CreateSphere(neck, "ClothCollider_TorsoHigh", 0.14f);
            SphereCollider upperArmStart = CreateSphere(upperArm, "ClothCollider_UpperArmStart", 0.095f);
            SphereCollider elbow = CreateSphere(lowerArm, "ClothCollider_Elbow", 0.078f);
            SphereCollider wristCollider = CreateSphere(hand, "ClothCollider_Wrist", 0.058f);
            cloth.sphereColliders = new[]
            {
                new ClothSphereColliderPair(torsoLow, torsoHigh),
                new ClothSphereColliderPair(upperArmStart, elbow),
                new ClothSphereColliderPair(elbow, wristCollider)
            };
        }

        private static Camera CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("ExperimentCamera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 8f, -8.9f);
            cameraObject.transform.rotation = Quaternion.Euler(38f, 0f, 0f);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 1.35f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.055f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            return camera;
        }

        private static void CreateEnvironment(Transform parent, Material groundMaterial)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "OneMeterGridFloor";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, -0.012f, 0.35f);
            floor.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
            floor.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());

            Material lineMaterial = CreateOrUpdateMaterial(MaterialFolder + "/ClothWideSleevePilot_Grid.mat",
                new Color(0.34f, 0.39f, 0.37f), false);
            for (int i = -5; i <= 5; i++)
            {
                CreateGridLine(parent, lineMaterial, "GridX_" + i, new Vector3(0f, 0f, i), new Vector3(10f, 0.008f, 0.016f));
                CreateGridLine(parent, lineMaterial, "GridZ_" + i, new Vector3(i, 0f, 0f), new Vector3(0.016f, 0.008f, 10f));
            }

            var lightObject = new GameObject("Directional Light", typeof(Light));
            lightObject.transform.SetParent(parent, false);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var fillObject = new GameObject("Fill Light", typeof(Light));
            fillObject.transform.SetParent(parent, false);
            Light fill = fillObject.GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.45f, 0.60f, 0.78f);
            fill.intensity = 0.55f;
            fill.shadows = LightShadows.None;
            fillObject.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
        }

        private static void CreateGridLine(Transform parent, Material material, string name, Vector3 position, Vector3 scale)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = name;
            line.transform.SetParent(parent, false);
            line.transform.localPosition = position;
            line.transform.localScale = scale;
            line.GetComponent<MeshRenderer>().sharedMaterial = material;
            line.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
        }

        private static SphereCollider CreateSphere(Transform parent, string name, float radius)
        {
            var colliderObject = new GameObject(name, typeof(SphereCollider));
            colliderObject.transform.SetParent(parent, false);
            colliderObject.transform.localPosition = Vector3.zero;
            colliderObject.transform.localRotation = Quaternion.identity;
            colliderObject.transform.localScale = Vector3.one;
            SphereCollider collider = colliderObject.GetComponent<SphereCollider>();
            collider.radius = radius;
            return collider;
        }

        private static Transform FindRequired(Transform root, string name)
        {
            Transform[] matches = root.GetComponentsInChildren<Transform>(true).Where(item => item.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exactly one transform named " + name + ", found " + matches.Length + ".");
            return matches[0];
        }

        private static Material CreateOrUpdateMaterial(string path, Color color, bool twoSided)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("URP Lit shader is unavailable.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.16f);
            material.SetFloat("_Cull", twoSided ? 0f : 2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetObject(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName) ??
                                          throw new InvalidOperationException("Missing serialized field " + propertyName + ".");
            property.objectReferenceValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(parent) || !AssetDatabase.IsValidFolder(parent))
                throw new InvalidOperationException("Parent asset folder is missing for " + path + ".");
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string ReadCommandLineValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }
}
