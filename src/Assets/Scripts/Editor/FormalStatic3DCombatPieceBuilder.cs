using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TianZhang.Content;
using TianZhang.Features.CombatPresentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TianZhang.Editor
{
    /// <summary>
    /// Creates only the approved static-3D carrier assets. Both frozen inputs are verified before
    /// any Unity asset is created, and the player source is never copied, regenerated, or overwritten.
    /// </summary>
    public static class FormalStatic3DCombatPieceBuilder
    {
        public const string PlayerModelPath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.fbx";
        public const string PlayerBaseColorPath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D_BaseColor.png";
        public const string PlayerMaterialPath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.mat";
        public const string PlayerPrefabPath = "Assets/Art/Characters/CombatPieces/Static3D/FormalPlayer/FormalPlayer_Static3D.prefab";
        public const string ShijiahouFolderPath = "Assets/Art/Characters/CombatPieces/Static3D/Shijiahou";
        public const string ShijiahouModelPath = ShijiahouFolderPath + "/Shijiahou_Static3D.fbx";
        public const string ShijiahouBaseColorPath = ShijiahouFolderPath + "/Shijiahou_Static3D_BaseColor.png";
        public const string ShijiahouMaterialPath = ShijiahouFolderPath + "/Shijiahou_Static3D.mat";
        public const string ShijiahouPrefabPath = ShijiahouFolderPath + "/Shijiahou_Static3D.prefab";
        public const string CatalogAssetPath = "Assets/Data/CombatPresentationProfiles/CombatUnitPresentationProfileCatalog.asset";
        public const string ProfileSetAssetPath = "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset";
        public const string FeedbackAudioFolderPath = "Assets/Art/Audio/Guanzhong";
        public const string FeedbackVfxFolderPath = "Assets/Art/VFX/Guanzhong";
        public const string FeedbackMoveCuePath = FeedbackAudioFolderPath + "/move.wav";
        public const string FeedbackAttackCuePath = FeedbackAudioFolderPath + "/attack.wav";
        public const string FeedbackHitCuePath = FeedbackAudioFolderPath + "/hit.wav";
        public const string FeedbackCastCuePath = FeedbackAudioFolderPath + "/cast.wav";
        public const string FeedbackDeathCuePath = FeedbackAudioFolderPath + "/death.wav";
        public const string FeedbackPrefabPath = FeedbackVfxFolderPath + "/Guanzhong_CombatFeedback.prefab";
        public const string FeedbackMaterialPath = FeedbackVfxFolderPath + "/Guanzhong_CombatFeedback.mat";

        private const string PlayerModelSha256 = "393da803b51dc3800199538aa1e17dcdce84c274831091ebecfa3bbebb79ae28";
        private const string PlayerBaseColorSha256 = "4ad0083980d0af3d21d1117db1f65f51548f5f10019dae09ead1946be3380fa5";
        private const string ShijiahouModelSha256 = "36ba2de48926b5c1aee6f2692b51891c2d48c87c2be08331db62dc8baf470700";
        private const string ShijiahouBaseColorSha256 = "7499200a1b372963b190a0aa6e489811aa76f94c849c1cd610cfe96c3e8037ac";
        private const string FeedbackMoveCueSha256 = "c189b99794e9a2244caef72ac186870986f98b2be425cf3975ed0294f0733688";
        private const string FeedbackAttackCueSha256 = "f234c88dc5f752c470fb65edd6d8d57a7295d8c4ab056b432a2a38c2fb11dade";
        private const string FeedbackHitCueSha256 = "4c8c25688a4a815d16da4ce4a852825d2eeaf2796d997a80b4c49353b8f92f57";
        private const string FeedbackCastCueSha256 = "035624656c51874fbbce40510f65becfbee9007c72fe65efc4a4d42a79f10729";
        private const string FeedbackDeathCueSha256 = "0182fc1dbe126d121239d0cd971f2e206a130664ff654718325991f940edbfbf";
        private const float Tolerance = 0.0001f;

        private static readonly Vector3 PlayerBoundsMin = new(-0.17988520f, 0f, -0.14063750f);
        private static readonly Vector3 PlayerBoundsMax = new(0.17988520f, 1.03000000f, 0.14063750f);
        private static readonly Vector3 ShijiahouBoundsMin = new(-0.22778321f, 0f, -0.49975586f);
        private static readonly Vector3 ShijiahouBoundsMax = new(0.22778321f, 0.45654297f, 0.49975586f);

        [MenuItem("天章/战斗表现/构建正式静态3D棋子 Profile")]
        public static void Build()
        {
            VerifyFrozenInputs();
            VerifyFeedbackSources();
            EnsureFolder("Assets/Art/Characters/CombatPieces/Static3D", "Shijiahou");
            EnsureFolder("Assets/Data", "CombatPresentationProfiles");
            EnsureFolder("Assets/Art", "Audio");
            EnsureFolder("Assets/Art/Audio", "Guanzhong");
            EnsureFolder("Assets/Art", "VFX");
            EnsureFolder("Assets/Art/VFX", "Guanzhong");

            CopyFrozenShijiahouInputsIfMissing();
            CopyFrozenFeedbackInputsIfMissing();
            ConfigureStaticModelImporter(PlayerModelPath);
            ConfigureStaticModelImporter(ShijiahouModelPath);
            ConfigureBaseColorImporter(PlayerBaseColorPath);
            ConfigureBaseColorImporter(ShijiahouBaseColorPath);
            ConfigureFeedbackCueImporter(FeedbackMoveCuePath);
            ConfigureFeedbackCueImporter(FeedbackAttackCuePath);
            ConfigureFeedbackCueImporter(FeedbackHitCuePath);
            ConfigureFeedbackCueImporter(FeedbackCastCuePath);
            ConfigureFeedbackCueImporter(FeedbackDeathCuePath);

            ValidateImportedModel(PlayerModelPath, PlayerBoundsMin, PlayerBoundsMax);
            ValidateImportedModel(ShijiahouModelPath, ShijiahouBoundsMin, ShijiahouBoundsMax);
            ValidateBaseColor(PlayerBaseColorPath);
            ValidateBaseColor(ShijiahouBaseColorPath);
            ValidateFeedbackCue(FeedbackMoveCuePath);
            ValidateFeedbackCue(FeedbackAttackCuePath);
            ValidateFeedbackCue(FeedbackHitCuePath);
            ValidateFeedbackCue(FeedbackCastCuePath);
            ValidateFeedbackCue(FeedbackDeathCuePath);

            Material playerMaterial = GetOrCreateMaterial(PlayerMaterialPath, "FormalPlayer_Static3D", PlayerBaseColorPath);
            Material shijiahouMaterial = GetOrCreateMaterial(ShijiahouMaterialPath, "Shijiahou_Static3D", ShijiahouBaseColorPath);
            Material feedbackMaterial = GetOrCreateFeedbackMaterial();
            GameObject playerPrefab = BuildPrefab("FormalPlayer_Static3D", PlayerModelPath, playerMaterial, PlayerPrefabPath);
            GameObject shijiahouPrefab = BuildPrefab("Shijiahou_Static3D", ShijiahouModelPath, shijiahouMaterial, ShijiahouPrefabPath);
            GameObject feedbackPrefab = BuildFeedbackPrefab(feedbackMaterial);

            CombatUnitPresentationProfileCatalogData catalog = GetOrCreateAsset<CombatUnitPresentationProfileCatalogData>(CatalogAssetPath);
            catalog.SetEntries(new[]
            {
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.PlayerCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                },
                new CombatUnitPresentationProfileCatalogEntry
                {
                    combatantId = CombatUnitPresentationProfileCatalogData.ShijiahouCombatantId,
                    presentationProfileId = CombatUnitPresentationProfileCatalogData.ShijiahouProfileId,
                },
            });
            EditorUtility.SetDirty(catalog);

            Static3DCombatUnitPresentationProfileSet profileSet = GetOrCreateAsset<Static3DCombatUnitPresentationProfileSet>(ProfileSetAssetPath);
            profileSet.SetProfiles(new[]
            {
                CreateProfile(
                    CombatUnitPresentationProfileCatalogData.PlayerProfileId,
                    playerPrefab,
                    PlayerModelPath,
                    PlayerModelSha256,
                    PlayerBoundsMin,
                    PlayerBoundsMax,
                    CreateFeedbackProfile(feedbackPrefab, 0.82f, 0.14f)),
                CreateProfile(
                    CombatUnitPresentationProfileCatalogData.ShijiahouProfileId,
                    shijiahouPrefab,
                    ShijiahouModelPath,
                    ShijiahouModelSha256,
                    ShijiahouBoundsMin,
                    ShijiahouBoundsMax,
                    CreateFeedbackProfile(feedbackPrefab, 1.12f, 0.2f)),
            });
            EditorUtility.SetDirty(profileSet);

            AssetDatabase.SaveAssets();
            VerifySavedOutputs();
        }

        [MenuItem("天章/战斗表现/构建关中战斗反馈")]
        public static void BuildFeedback()
        {
            VerifyFeedbackSources();
            EnsureFolder("Assets/Art", "Audio");
            EnsureFolder("Assets/Art/Audio", "Guanzhong");
            EnsureFolder("Assets/Art", "VFX");
            EnsureFolder("Assets/Art/VFX", "Guanzhong");

            CopyFrozenFeedbackInputsIfMissing();
            ConfigureFeedbackCueImporter(FeedbackMoveCuePath);
            ConfigureFeedbackCueImporter(FeedbackAttackCuePath);
            ConfigureFeedbackCueImporter(FeedbackHitCuePath);
            ConfigureFeedbackCueImporter(FeedbackCastCuePath);
            ConfigureFeedbackCueImporter(FeedbackDeathCuePath);
            ValidateFeedbackCue(FeedbackMoveCuePath);
            ValidateFeedbackCue(FeedbackAttackCuePath);
            ValidateFeedbackCue(FeedbackHitCuePath);
            ValidateFeedbackCue(FeedbackCastCuePath);
            ValidateFeedbackCue(FeedbackDeathCuePath);

            GameObject feedbackPrefab = BuildFeedbackPrefab(GetOrCreateFeedbackMaterial());
            Static3DCombatUnitPresentationProfileSet profileSet =
                RequireAsset<Static3DCombatUnitPresentationProfileSet>(ProfileSetAssetPath);
            Static3DCombatUnitPresentationProfile playerProfile = profileSet.Profiles.SingleOrDefault(profile =>
                profile != null && profile.presentationProfileId == Static3DCombatUnitPresentationProfileSet.PlayerProfileId);
            Static3DCombatUnitPresentationProfile shijiahouProfile = profileSet.Profiles.SingleOrDefault(profile =>
                profile != null && profile.presentationProfileId == Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId);
            if (playerProfile == null || shijiahouProfile == null)
                throw new InvalidOperationException("Formal static 3D profile mapping is incomplete.");

            playerProfile.combatFeedback = CreateFeedbackProfile(feedbackPrefab, 0.82f, 0.14f);
            shijiahouProfile.combatFeedback = CreateFeedbackProfile(feedbackPrefab, 1.12f, 0.2f);
            EditorUtility.SetDirty(profileSet);
            AssetDatabase.SaveAssets();
            VerifyFeedbackOutputs();
        }

        public static void VerifySavedOutputs()
        {
            VerifyFrozenInputs();
            VerifyFeedbackSources();
            CombatUnitPresentationProfileCatalogData catalog = RequireAsset<CombatUnitPresentationProfileCatalogData>(CatalogAssetPath);
            Static3DCombatUnitPresentationProfileSet profileSet = RequireAsset<Static3DCombatUnitPresentationProfileSet>(ProfileSetAssetPath);
            if (!catalog.TryValidate(out string catalogReason))
                throw new InvalidOperationException("Combat presentation profile catalog is invalid: " + catalogReason);
            if (!profileSet.TryValidate(out string profileSetReason))
                throw new InvalidOperationException("Static 3D profile set is invalid: " + profileSetReason);

            if (!catalog.TryGetPresentationProfileId(CombatUnitPresentationProfileCatalogData.PlayerCombatantId, out string playerProfileId) ||
                !catalog.TryGetPresentationProfileId(CombatUnitPresentationProfileCatalogData.ShijiahouCombatantId, out string shijiahouProfileId) ||
                !profileSet.TryGetProfile(playerProfileId, out Static3DCombatUnitPresentationProfile playerProfile) ||
                !profileSet.TryGetProfile(shijiahouProfileId, out Static3DCombatUnitPresentationProfile shijiahouProfile))
            {
                throw new InvalidOperationException("Formal static 3D profile mapping is incomplete.");
            }

            VerifyPrefab(playerProfile, "FormalPlayer_Static3D", PlayerMaterialPath, PlayerBoundsMin, PlayerBoundsMax);
            VerifyPrefab(shijiahouProfile, "Shijiahou_Static3D", ShijiahouMaterialPath, ShijiahouBoundsMin, ShijiahouBoundsMax);
            VerifyFeedbackProfile(playerProfile, shijiahouProfile);
            string[] dependencies = AssetDatabase.GetDependencies(ProfileSetAssetPath, true);
            if (dependencies.Any(path => path.IndexOf("FuYuan_StaticChess", StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidOperationException("Formal static 3D profiles must not depend on FuYuan_StaticChess.");
        }

        public static void VerifyFeedbackOutputs()
        {
            VerifyFeedbackSources();
            Static3DCombatUnitPresentationProfileSet profileSet =
                RequireAsset<Static3DCombatUnitPresentationProfileSet>(ProfileSetAssetPath);
            if (!profileSet.TryValidate(out string profileSetReason))
                throw new InvalidOperationException("Static 3D profile set is invalid: " + profileSetReason);
            if (!profileSet.TryGetProfile(Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                    out Static3DCombatUnitPresentationProfile playerProfile) ||
                !profileSet.TryGetProfile(Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                    out Static3DCombatUnitPresentationProfile shijiahouProfile))
            {
                throw new InvalidOperationException("Formal combat feedback profile mapping is incomplete.");
            }

            VerifyFeedbackProfile(playerProfile, shijiahouProfile);
        }

        private static Static3DCombatUnitPresentationProfile CreateProfile(
            string profileId,
            GameObject prefab,
            string modelPath,
            string modelSha256,
            Vector3 boundsMin,
            Vector3 boundsMax,
            Static3DCombatFeedbackProfile combatFeedback)
        {
            return new Static3DCombatUnitPresentationProfile
            {
                presentationProfileId = profileId,
                prefab = prefab,
                approvedModelAssetPath = modelPath,
                approvedModelSha256 = modelSha256,
                approvedBoundsMin = boundsMin,
                approvedBoundsMax = boundsMax,
                sixDirectionYawDegrees = (int[])Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees.Clone(),
                combatFeedback = combatFeedback,
            };
        }

        private static Static3DCombatFeedbackProfile CreateFeedbackProfile(
            GameObject prefab,
            float intensity,
            float actionRecoverySeconds)
        {
            return new Static3DCombatFeedbackProfile
            {
                vfxPrefab = prefab,
                moveCue = RequireAsset<AudioClip>(FeedbackMoveCuePath),
                attackCue = RequireAsset<AudioClip>(FeedbackAttackCuePath),
                hitCue = RequireAsset<AudioClip>(FeedbackHitCuePath),
                castCue = RequireAsset<AudioClip>(FeedbackCastCuePath),
                deathCue = RequireAsset<AudioClip>(FeedbackDeathCuePath),
                intensity = intensity,
                actionRecoverySeconds = actionRecoverySeconds,
            };
        }

        private static void VerifyFrozenInputs()
        {
            VerifyFileHash(AbsoluteAssetPath(PlayerModelPath), PlayerModelSha256, "approved player FBX");
            VerifyFileHash(AbsoluteAssetPath(PlayerBaseColorPath), PlayerBaseColorSha256, "approved player BaseColor");
            VerifyFileHash(SourceShijiahouPath("shijiahou_static3d_v2.fbx"), ShijiahouModelSha256, "approved Shijiahou FBX source");
            VerifyFileHash(SourceShijiahouPath("shijiahou_static3d_v2_basecolor.png"), ShijiahouBaseColorSha256, "approved Shijiahou BaseColor source");
        }

        private static void CopyFrozenShijiahouInputsIfMissing()
        {
            CopyFrozenFileIfMissing(SourceShijiahouPath("shijiahou_static3d_v2.fbx"), AbsoluteAssetPath(ShijiahouModelPath), ShijiahouModelSha256);
            CopyFrozenFileIfMissing(SourceShijiahouPath("shijiahou_static3d_v2_basecolor.png"), AbsoluteAssetPath(ShijiahouBaseColorPath), ShijiahouBaseColorSha256);
            AssetDatabase.ImportAsset(ShijiahouModelPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(ShijiahouBaseColorPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void VerifyFeedbackSources()
        {
            VerifyFileHash(SourceFeedbackPath("move.wav"), FeedbackMoveCueSha256, "approved move cue");
            VerifyFileHash(SourceFeedbackPath("attack.wav"), FeedbackAttackCueSha256, "approved attack cue");
            VerifyFileHash(SourceFeedbackPath("hit.wav"), FeedbackHitCueSha256, "approved hit cue");
            VerifyFileHash(SourceFeedbackPath("cast.wav"), FeedbackCastCueSha256, "approved cast cue");
            VerifyFileHash(SourceFeedbackPath("death.wav"), FeedbackDeathCueSha256, "approved death cue");
        }

        private static void CopyFrozenFeedbackInputsIfMissing()
        {
            CopyFrozenFileIfMissing(SourceFeedbackPath("move.wav"), AbsoluteAssetPath(FeedbackMoveCuePath), FeedbackMoveCueSha256);
            CopyFrozenFileIfMissing(SourceFeedbackPath("attack.wav"), AbsoluteAssetPath(FeedbackAttackCuePath), FeedbackAttackCueSha256);
            CopyFrozenFileIfMissing(SourceFeedbackPath("hit.wav"), AbsoluteAssetPath(FeedbackHitCuePath), FeedbackHitCueSha256);
            CopyFrozenFileIfMissing(SourceFeedbackPath("cast.wav"), AbsoluteAssetPath(FeedbackCastCuePath), FeedbackCastCueSha256);
            CopyFrozenFileIfMissing(SourceFeedbackPath("death.wav"), AbsoluteAssetPath(FeedbackDeathCuePath), FeedbackDeathCueSha256);
            AssetDatabase.ImportAsset(FeedbackMoveCuePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(FeedbackAttackCuePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(FeedbackHitCuePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(FeedbackCastCuePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(FeedbackDeathCuePath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CopyFrozenFileIfMissing(string sourcePath, string destinationPath, string expectedHash)
        {
            if (!File.Exists(destinationPath))
            {
                File.Copy(sourcePath, destinationPath);
            }

            VerifyFileHash(destinationPath, expectedHash, "stable Unity input");
        }

        private static void ConfigureStaticModelImporter(string modelPath)
        {
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Model importer is unavailable: " + modelPath);

            if (importer.importAnimation || importer.animationType != ModelImporterAnimationType.None ||
                importer.importBlendShapes || importer.importCameras || importer.importLights ||
                !importer.isReadable || !importer.bakeAxisConversion || !importer.useFileScale ||
                Mathf.Abs(importer.globalScale - 1f) > Tolerance)
            {
                importer.importAnimation = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importBlendShapes = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.isReadable = true;
                importer.bakeAxisConversion = true;
                importer.useFileScale = true;
                importer.globalScale = 1f;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureBaseColorImporter(string texturePath)
        {
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Texture importer is unavailable: " + texturePath);
            if (importer.textureType != TextureImporterType.Default || !importer.sRGBTexture ||
                importer.alphaSource != TextureImporterAlphaSource.None || importer.alphaIsTransparency ||
                importer.maxTextureSize != 4096)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureFeedbackCueImporter(string cuePath)
        {
            AssetDatabase.ImportAsset(cuePath, ImportAssetOptions.ForceSynchronousImport);
            AudioImporter importer = AssetImporter.GetAtPath(cuePath) as AudioImporter;
            if (importer == null) throw new InvalidOperationException("Audio importer is unavailable: " + cuePath);
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            bool requiresReimport = !importer.forceToMono || importer.loadInBackground ||
                !settings.preloadAudioData || settings.loadType != AudioClipLoadType.DecompressOnLoad ||
                settings.compressionFormat != AudioCompressionFormat.PCM ||
                settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            if (requiresReimport)
            {
                importer.forceToMono = true;
                importer.loadInBackground = false;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static void ValidateImportedModel(string modelPath, Vector3 expectedMin, Vector3 expectedMax)
        {
            GameObject model = RequireAsset<GameObject>(modelPath);
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1 || filters[0].sharedMesh == null || filters[0].sharedMesh.vertexCount == 0)
                throw new InvalidOperationException("Formal static 3D model must contain exactly one non-empty MeshFilter: " + modelPath);
            if (model.GetComponentsInChildren<MeshRenderer>(true).Length != 1 ||
                model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0 ||
                model.GetComponentsInChildren<Animator>(true).Length != 0 ||
                model.GetComponentsInChildren<Animation>(true).Length != 0)
                throw new InvalidOperationException("Formal static 3D model has an invalid static component set: " + modelPath);

            foreach (Transform node in model.GetComponentsInChildren<Transform>(true))
            {
                if (node.localPosition.magnitude > Tolerance ||
                    Quaternion.Angle(node.localRotation, Quaternion.identity) > 0.01f ||
                    node.localScale.x <= 0f || Mathf.Abs(node.localScale.x - node.localScale.y) > Tolerance ||
                    Mathf.Abs(node.localScale.x - node.localScale.z) > Tolerance)
                    throw new InvalidOperationException("Formal static 3D model has an invalid import transform: " + modelPath + "/" + node.name);
            }

            Vector3[] vertices = filters[0].sharedMesh.vertices.Select(filters[0].transform.TransformPoint).ToArray();
            if (vertices.Length == 0 || vertices.Any(vertex => !IsFinite(vertex)))
                throw new InvalidOperationException("Formal static 3D model vertices are invalid: " + modelPath);
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            if (Vector3.Distance(bounds.min, expectedMin) > Tolerance || Vector3.Distance(bounds.max, expectedMax) > Tolerance ||
                Vector3.Dot(model.transform.TransformDirection(Vector3.forward).normalized, Vector3.forward) < 0.9999f)
                throw new InvalidOperationException("Formal static 3D model violates its approved axis, scale, or grounding contract: " + modelPath);
        }

        private static void ValidateBaseColor(string texturePath)
        {
            Texture2D texture = RequireAsset<Texture2D>(texturePath);
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null || texture.width != 4096 || texture.height != 4096 ||
                importer.textureType != TextureImporterType.Default || !importer.sRGBTexture ||
                importer.alphaSource != TextureImporterAlphaSource.None || importer.alphaIsTransparency ||
                importer.maxTextureSize != 4096)
                throw new InvalidOperationException("Formal static 3D BaseColor importer contract failed: " + texturePath);
        }

        private static void ValidateFeedbackCue(string cuePath)
        {
            AudioClip cue = RequireAsset<AudioClip>(cuePath);
            AudioImporter importer = AssetImporter.GetAtPath(cuePath) as AudioImporter;
            if (importer == null || cue.channels != 1 || cue.frequency != 44100 ||
                cue.length < 0.08f || cue.length > 0.5f || !importer.forceToMono || importer.loadInBackground ||
                importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad ||
                importer.defaultSampleSettings.compressionFormat != AudioCompressionFormat.PCM ||
                importer.defaultSampleSettings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate ||
                !importer.defaultSampleSettings.preloadAudioData)
            {
                throw new InvalidOperationException("Formal combat feedback cue import contract failed: " + cuePath);
            }
        }

        private static Material GetOrCreateMaterial(string materialPath, string materialName, string baseColorPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("Required URP Lit shader is unavailable.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            Texture2D baseColor = RequireAsset<Texture2D>(baseColorPath);
            material.name = materialName;
            material.shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("Required URP Lit shader is unavailable.");
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", baseColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.22f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateFeedbackMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FeedbackMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new InvalidOperationException("Required URP particle shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, FeedbackMaterialPath);
            }

            material.name = "Guanzhong_CombatFeedback";
            material.shader = shader;
            material.SetColor("_BaseColor", new Color(0.68f, 0.82f, 0.72f, 0.8f));
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject BuildFeedbackPrefab(Material material)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackPrefabPath);
            GameObject root = existing == null ? new GameObject("Guanzhong_CombatFeedback") : PrefabUtility.LoadPrefabContents(FeedbackPrefabPath);
            try
            {
                root.name = "Guanzhong_CombatFeedback";
                ParticleSystem particles = root.GetComponent<ParticleSystem>();
                if (particles == null) particles = root.AddComponent<ParticleSystem>();
                foreach (ParticleSystem extra in root.GetComponentsInChildren<ParticleSystem>(true).Where(item => item != particles))
                    UnityEngine.Object.DestroyImmediate(extra.gameObject);

                ParticleSystem.MainModule main = particles.main;
                main.duration = 0.36f;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.55f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.26f);
                main.maxParticles = 16;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.stopAction = ParticleSystemStopAction.Destroy;

                ParticleSystem.EmissionModule emission = particles.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)12) });
                ParticleSystem.ShapeModule shape = particles.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.22f;
                ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f),
                    },
                    new[]
                    {
                        new GradientAlphaKey(0.9f, 0f),
                        new GradientAlphaKey(0f, 1f),
                    });
                color.color = gradient;

                ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sortingOrder = 1;

                if (existing == null)
                    PrefabUtility.SaveAsPrefabAsset(root, FeedbackPrefabPath);
                else
                    PrefabUtility.SaveAsPrefabAsset(root, FeedbackPrefabPath);
            }
            finally
            {
                if (existing == null) UnityEngine.Object.DestroyImmediate(root);
                else PrefabUtility.UnloadPrefabContents(root);
            }

            return RequireAsset<GameObject>(FeedbackPrefabPath);
        }

        private static void VerifyFeedbackProfile(
            Static3DCombatUnitPresentationProfile playerProfile,
            Static3DCombatUnitPresentationProfile shijiahouProfile)
        {
            Static3DCombatFeedbackProfile playerFeedback = playerProfile.combatFeedback;
            Static3DCombatFeedbackProfile shijiahouFeedback = shijiahouProfile.combatFeedback;
            if (playerFeedback == null || shijiahouFeedback == null ||
                playerFeedback.vfxPrefab != shijiahouFeedback.vfxPrefab ||
                playerFeedback.moveCue != shijiahouFeedback.moveCue ||
                playerFeedback.attackCue != shijiahouFeedback.attackCue ||
                playerFeedback.hitCue != shijiahouFeedback.hitCue ||
                playerFeedback.castCue != shijiahouFeedback.castCue ||
                playerFeedback.deathCue != shijiahouFeedback.deathCue ||
                playerFeedback.intensity >= shijiahouFeedback.intensity ||
                playerFeedback.actionRecoverySeconds >= shijiahouFeedback.actionRecoverySeconds)
            {
                throw new InvalidOperationException("Formal combat feedback profile mapping is invalid.");
            }
            if (AssetDatabase.GetAssetPath(playerFeedback.vfxPrefab) != FeedbackPrefabPath ||
                playerFeedback.vfxPrefab.GetComponentsInChildren<ParticleSystem>(true).Length != 1)
            {
                throw new InvalidOperationException("Formal combat feedback VFX identity is invalid.");
            }
            ValidateFeedbackCue(FeedbackMoveCuePath);
            ValidateFeedbackCue(FeedbackAttackCuePath);
            ValidateFeedbackCue(FeedbackHitCuePath);
            ValidateFeedbackCue(FeedbackCastCuePath);
            ValidateFeedbackCue(FeedbackDeathCuePath);
            Material material = RequireAsset<Material>(FeedbackMaterialPath);
            ParticleSystemRenderer renderer = playerFeedback.vfxPrefab.GetComponentInChildren<ParticleSystemRenderer>(true);
            if (renderer == null || renderer.sharedMaterial != material)
                throw new InvalidOperationException("Formal combat feedback material mapping is invalid.");
        }

        private static GameObject BuildPrefab(string rootName, string modelPath, Material material, string prefabPath)
        {
            GameObject model = RequireAsset<GameObject>(modelPath);
            var root = new GameObject(rootName);
            try
            {
                GameObject figure = (GameObject)PrefabUtility.InstantiatePrefab(model);
                figure.name = rootName + "_Model";
                figure.transform.SetParent(root.transform, false);
                figure.transform.localPosition = Vector3.zero;
                figure.transform.localRotation = Quaternion.identity;
                foreach (MeshRenderer renderer in figure.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (prefab == null) throw new InvalidOperationException("Could not save formal static 3D prefab: " + prefabPath);
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void VerifyPrefab(
            Static3DCombatUnitPresentationProfile profile,
            string expectedName,
            string expectedMaterialPath,
            Vector3 expectedBoundsMin,
            Vector3 expectedBoundsMax)
        {
            string prefabPath = profile.prefab == null ? null : AssetDatabase.GetAssetPath(profile.prefab);
            if (string.IsNullOrWhiteSpace(prefabPath))
                throw new InvalidOperationException("Formal static 3D prefab identity is invalid: " + expectedName);

            GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (!string.Equals(prefab.name, expectedName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Formal static 3D prefab identity is invalid: " + expectedName);
                Material expectedMaterial = RequireAsset<Material>(expectedMaterialPath);
                MeshFilter filter = prefab.GetComponentInChildren<MeshFilter>(true);
                MeshRenderer renderer = prefab.GetComponentInChildren<MeshRenderer>(true);
                if (filter == null || renderer == null || renderer.sharedMaterial != expectedMaterial)
                    throw new InvalidOperationException("Formal static 3D prefab material is invalid: " + expectedName);

                Vector3[] vertices = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
                var bounds = new Bounds(vertices[0], Vector3.zero);
                foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
                if (Vector3.Distance(bounds.min, expectedBoundsMin) > Tolerance || Vector3.Distance(bounds.max, expectedBoundsMax) > Tolerance)
                    throw new InvalidOperationException("Formal static 3D prefab bounds are invalid: " + expectedName +
                        " actual=" + bounds.min + ".." + bounds.max +
                        " expected=" + expectedBoundsMin + ".." + expectedBoundsMax);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }

        private static T GetOrCreateAsset<T>(string assetPath) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, assetPath);
            return asset;
        }

        private static T RequireAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) throw new InvalidOperationException("Required asset is unavailable: " + assetPath);
            return asset;
        }

        private static void EnsureFolder(string parentAssetPath, string folderName)
        {
            string path = parentAssetPath + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.CreateFolder(parentAssetPath, folderName).Length == 0)
                throw new InvalidOperationException("Could not create required asset folder: " + path);
        }

        private static string AbsoluteAssetPath(string assetPath) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length)));

        private static string SourceShijiahouPath(string fileName) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "assets", "source", "characters", "combat-pieces", "shijiahou-static-3d-v2", fileName));

        private static string SourceFeedbackPath(string fileName) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "assets", "source", "combat-feedback", "guanzhong-first-bounty", fileName));

        private static void VerifyFileHash(string path, string expectedHash, string label)
        {
            if (!File.Exists(path)) throw new InvalidOperationException("Missing " + label + ": " + path);
            using SHA256 hash = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            string actualHash = string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("Frozen hash mismatch for " + label + ": " + path);
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
