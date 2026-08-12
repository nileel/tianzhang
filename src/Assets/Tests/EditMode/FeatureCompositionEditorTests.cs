using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class FeatureCompositionEditorTests
    {
        [Test]
        public void FeaturesDoNotReferenceBootstrapOrSiblingFeatureNamespaces()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "Modules", "Features");
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string ownFeature = new DirectoryInfo(Path.GetDirectoryName(file)).Name;
                string source = File.ReadAllText(file);
                StringAssert.DoesNotContain("using TianZhang.Bootstrap", source, file);
                foreach (string sibling in new[]
                {
                    "CharacterCreation", "WorldMap", "Settlement", "Adventure", "CombatPresentation",
                })
                {
                    if (sibling == ownFeature) continue;
                    StringAssert.DoesNotContain("TianZhang.Features." + sibling, source, file);
                }
            }
        }

        [Test]
        public void DeletedHubTypesHaveNoResidualGuidReferences()
        {
            string assets = Application.dataPath;
            foreach (string guid in new[]
            {
                "7052d38e284468645a8479e414627145",
                "558c2734729650748a2637f49696815e",
                "facd18e1ee46e4d49a73c2f8a464d26f",
                "5e4ed907f8426214685964f886933c7c",
                "d44e4b1c1fb864f45af90b6c95017bfa",
                "4816ca9701d34b279d13db5761f295c0",
                "a7d8cd6ab0c4c5443ac87c0bd803b2bd",
            })
            {
                foreach (string path in Directory.GetFiles(assets, "*", SearchOption.AllDirectories))
                {
                    string extension = Path.GetExtension(path);
                    if (extension != ".unity" && extension != ".prefab" && extension != ".asset") continue;
                    StringAssert.DoesNotContain(guid, File.ReadAllText(path), path);
                }
            }
        }

        [Test]
        public void ExistingPlayerLoginUsesTheInjectedEntryContract()
        {
            var go = new GameObject("StartMenuControllerTest");
            try
            {
                var controller = go.AddComponent<StartMenuController>();
                var host = new RecordingEntryHost();
                controller.Configure(host, null, null);

                controller.LoadPlayer("slot_existing");

                Assert.AreEqual("slot_existing", host.LoadedSlotId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ActiveSlotSaveCanBeReadBackIntoAResetRuntime()
        {
            string directory = CreateTemporaryDirectoryPath();
            var bootstrapObject = new GameObject("GameBootstrapRoundTripTest");
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                var bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
                SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(directory));
                profile.charName = "旧档角色";
                profile.realmMultiplier = 1f;
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile),
                    CultivationState.CreateEmpty(),
                    "guanzhong_hub");
                runtime.EnterSettlement("guanzhong_city");
                bootstrap.ActivateSlot("slot_round_trip");

                GameSaveSlotWriteResult write = bootstrap.SaveActiveSlot();
                GameSaveSlotReadResult read = bootstrap.SlotStore.Read("slot_round_trip");
                runtime.Clear();
                runtime.RestoreSave(read.Envelope, catalog);

                Assert.IsTrue(write.Succeeded, write.FailureReason.ToString());
                Assert.IsTrue(read.Succeeded, read.FailureReason.ToString());
                Assert.AreEqual("旧档角色", runtime.Player.Identity.DisplayName);
                Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(bootstrapObject);
                DeleteTemporaryDirectory(directory);
            }
        }

        [Test]
        public void ExistingSaveWithUnknownAdventureStaysAtStartMenuBoundary()
        {
            string directory = CreateTemporaryDirectoryPath();
            var bootstrapObject = new GameObject("GameBootstrapInvalidNavigationTest");
            var installerObject = new GameObject("StartMenuInstallerInvalidNavigationTest");
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            try
            {
                installerObject.SetActive(false);
                var bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
                SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(directory));
                profile.charName = "坏目标旧档";
                profile.realmMultiplier = 1f;
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile),
                    CultivationState.CreateEmpty(),
                    "guanzhong_hub");
                runtime.EnterAdventure(
                    "adventure_missing",
                    SceneReturnTarget.World("guanzhong_hub"));
                Assert.IsTrue(bootstrap.SlotStore.Write("slot_bad_target", runtime.CaptureSave()).Succeeded);
                runtime.Clear();

                var installer = installerObject.AddComponent<StartMenuSceneInstaller>();
                SetPrivateField(installer, "bootstrap", bootstrap);
                SetPrivateField(installer, "contentCatalog", catalog);
                PlayerEntryResult result = installer.LoadPlayer("slot_bad_target");

                Assert.IsFalse(result.Succeeded);
                Assert.AreEqual("save_navigation_target_unresolved", result.FailureReason);
                Assert.IsNull(runtime.Player);
                Assert.IsNull(runtime.Navigation.AdventureId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(installerObject);
                UnityEngine.Object.DestroyImmediate(bootstrapObject);
                DeleteTemporaryDirectory(directory);
            }
        }

        private static string CreateTemporaryDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "TianZhang.FeatureCompositionEditorTests",
                Guid.NewGuid().ToString("N"));
        }

        private static void DeleteTemporaryDirectory(string directory)
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private sealed class RecordingEntryHost : IPlayerEntryHost
        {
            public string LoadedSlotId { get; private set; }
            public IReadOnlyList<PlayerSlotSummary> ListSlots() => new PlayerSlotSummary[0];
            public PlayerEntryResult CreateNewPlayer(string slotId, CharacterData profile, string startNodeId) =>
                PlayerEntryResult.Success();
            public PlayerEntryResult LoadPlayer(string slotId)
            {
                LoadedSlotId = slotId;
                return PlayerEntryResult.Success();
            }
        }
    }
}
