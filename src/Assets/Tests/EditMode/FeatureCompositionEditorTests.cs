using System.Linq;
using TianZhang.Combat;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Spatial;
using UnityEditor;
using UnityEditor.SceneManagement;
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests
{
    public sealed class FeatureCompositionEditorTests
    {
        private static TianZhang.Content.CombatRuleSetData GrowthRules
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
#else
                throw new System.NotSupportedException("Editor fixture requires the production rule asset.");
#endif
            }
        }

        [Test]
        public void FormalOrdinaryDefinitionsReloadIdempotentlyAndReachTheSession()
        {
            const string scenePath = "Assets/Scenes/AdventureScene.unity";
            string path = Path.Combine(Application.dataPath, "Scenes", "AdventureScene.unity");
            string ReadScene() => string.Join("\n", File.ReadAllText(path).Split('\n').Select(line => line.TrimEnd()));
            string original = ReadScene();
            string guid = AssetDatabase.AssetPathToGUID(scenePath);
            TianZhang.Editor.AdventureSceneBuilder.UpdateOrdinaryCombatActions();
            Assert.AreEqual(original, ReadScene(), "Targeted migration must be idempotent after its initial save.");
            TianZhang.Editor.AdventureSceneBuilder.UpdateOrdinaryCombatActions();
            Assert.AreEqual(original, ReadScene());
            Assert.AreEqual("e3e595b673eb98142a11436e06fe54c6", guid);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(scenePath));
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            AdventureSceneInstaller installer = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out AdventureSceneInstaller found)) installer = found;
            Assert.IsNotNull(installer);
            object Field(string name) => typeof(AdventureSceneInstaller).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(installer);
            var ordinary = (CombatOrdinaryActionDefinition[])Field("ordinaryActions");
            Assert.AreEqual(4, ordinary.Length);
            foreach (var definition in ordinary)
            {
                int cost = definition.Kind == CombatCommandKind.UseItem ? 30000 : definition.Kind == CombatCommandKind.SwapEquipment ? 25000 : 50000;
                Assert.AreEqual(cost, definition.ActionPointCostUnits); Assert.AreEqual(cost, definition.MinimumNetCostUnits);
                Assert.AreEqual(0, definition.CtPenalty);
                Assert.AreEqual(definition.Kind == CombatCommandKind.SwapSpell ? 60 : 0, definition.ReuseCooldownTicks);
            }
            var player = new CombatantSnapshot("player", CombatTeam.Player, new HexCoord(0,0), 100,
                100,100,20,20,10,10,100000,2000, basicAttackProfileId: string.Empty);
            var enemy = new CombatantSnapshot("enemy", CombatTeam.Enemy, new HexCoord(1,0), 1,
                100,100,20,20,10,10,100000,2000, basicAttackProfileId: string.Empty);
            var spawned = new AdventureSpawnSet(player, enemy, null, string.Empty, string.Empty, Array.Empty<string>());
            var profiles = (AttackProfileData[])Field("attackProfiles");
            var environment = (EnvironmentProfileAsset)Field("environmentProfile");
            Assert.IsTrue(new CombatEntryAdapter(ordinary, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")).TryCreateSession(spawned, profiles, environment, out var session, out var reason), reason);
            var ruleData = AssetDatabase.LoadAssetAtPath<CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
            Assert.IsNotNull(session.RuleSet);
            Assert.AreEqual(ruleData.sourceDigest,session.RuleSet.SourceDigest);
            foreach (var profile in session.AttackProfiles)
            {
                Assert.IsNotNull(profile.RuleMetadata);
                Assert.That(profile.RuleMetadata.Tier.AbilityTierId, Is.EqualTo(profiles.Single(p => p.attackProfileId == profile.Id).abilityTierId));
                Assert.AreEqual(ruleData.sourceDigest,profile.RuleMetadata.SourceDigest);
                Assert.AreEqual(ruleData.ruleSetVersion,profile.RuleMetadata.RuleSetVersion);
            }
            var badProfile = UnityEngine.Object.Instantiate(profiles.First(value => value.attackProfileId == "spell_guanshiji"));
            try
            {
                badProfile.efficiencyDenominator=0;
                Assert.IsFalse(new CombatEntryAdapter(ordinary,ruleData).TryCreateSession(spawned,new[] {badProfile},environment,out var rejected,out reason));
                Assert.IsNull(rejected); Assert.AreEqual("attack_profile_budget_ratio_invalid",reason);
                badProfile.efficiencyDenominator=1; badProfile.physicalDamageMultiplier=float.NaN;
                Assert.IsFalse(new CombatEntryAdapter(ordinary,ruleData).TryCreateSession(spawned,new[] {badProfile},environment,out rejected,out reason));
                Assert.IsNull(rejected); Assert.AreEqual("attack_profile_required_field_invalid",reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(badProfile); }
            var service = new CombatCommandService(); Assert.AreEqual(player.Id, service.AdvanceUntilAction(session).ActorId);
            var guard = new CombatCommand(CombatCommandKind.Guard, player.Id);
            Assert.AreEqual(50000, service.Preview(session, guard).ActionPointCostUnits);
            Assert.IsTrue(service.Execute(session, guard).Succeeded); Assert.AreEqual(50000, player.ActionPointsRemainingUnits);
            foreach (var invalid in new[]
            {
                Array.Empty<CombatOrdinaryActionDefinition>(),
                new[] { ordinary[0] },
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,0,1,0,0),ordinary[1] },
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,-1,1,0,0),ordinary[1] },
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,50001,0,0),ordinary[1] },
                new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard,50000,0,0,0),ordinary[1] },
            })
            {
                Assert.IsFalse(new CombatEntryAdapter(invalid, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")).TryCreateSession(spawned,profiles,environment,out var rejected,out reason));
                Assert.IsNull(rejected); StringAssert.StartsWith("combat_session_setup_invalid:",reason);
            }
        }

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
            profile.unarmedAttackFamilyId = "body_human";
            profile.realmStage = "凡人";
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.SetCombatRuleSet(GrowthRules);
            try
            {
                var bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
                SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(directory));
                profile.charName = "旧档角色";
                profile.unarmedBasicAttackProfileId = string.Empty;
                profile.realmMultiplier = 1f;
                profile.actionPointMaximumUnits = 100000;
                profile.movementEfficiencyMilliUnits = 2000;
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
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
            profile.unarmedAttackFamilyId = "body_human";
            profile.realmStage = "凡人";
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.SetCombatRuleSet(GrowthRules);
            try
            {
                installerObject.SetActive(false);
                var bootstrap = bootstrapObject.AddComponent<GameBootstrap>();
                SetPrivateField(bootstrap, "slotStore", new GameSaveSlotStore(directory));
                profile.charName = "坏目标旧档";
                profile.unarmedBasicAttackProfileId = string.Empty;
                profile.realmMultiplier = 1f;
                profile.actionPointMaximumUnits = 100000;
                profile.movementEfficiencyMilliUnits = 2000;
                GameRuntime runtime = GameBootstrap.RequireRuntime();
                runtime.BeginNewGame(
                    CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
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

        [Test]
        public void MissingPointBuyConfigClosesCharacterCreationBeforeRuntimeOrSceneEntry()
        {
            var installerObject = new GameObject("StartMenuInstallerMissingPointBuyConfigTest");
            var catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            catalog.SetCombatRuleSet(GrowthRules);
            try
            {
                installerObject.SetActive(false);
                var startMenuController = installerObject.AddComponent<StartMenuController>();
                var startMenuView = installerObject.AddComponent<StartMenuView>();
                var creationController = installerObject.AddComponent<CharacterCreationController>();
                var creationView = installerObject.AddComponent<CharacterCreationView>();
                var installer = installerObject.AddComponent<StartMenuSceneInstaller>();
                SetPrivateField(installer, "contentCatalog", catalog);
                SetPrivateField(installer, "startMenuController", startMenuController);
                SetPrivateField(installer, "startMenuView", startMenuView);
                SetPrivateField(installer, "characterCreationController", creationController);
                SetPrivateField(installer, "characterCreationView", creationView);
                string activeScenePath = SceneManager.GetActiveScene().path;
                int bootstrapCount = UnityEngine.Object.FindObjectsByType<GameBootstrap>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length;

                LogAssert.Expect(
                    LogType.Error,
                    "[StartMenuInstaller] " + StartMenuSceneInstaller.PointBuyConfigMissingReason);
                InvokePrivateMethod(installer, "Awake");

                Assert.AreEqual(StartMenuSceneInstaller.PointBuyConfigMissingReason, installer.FailureReason);
                Assert.IsFalse(startMenuController.enabled);
                Assert.IsFalse(creationController.enabled);
                Assert.IsNull(creationController.Draft);
                Assert.AreEqual(activeScenePath, SceneManager.GetActiveScene().path);
                Assert.AreEqual(
                    bootstrapCount,
                    UnityEngine.Object.FindObjectsByType<GameBootstrap>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None).Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(installerObject);
            }
        }

        [Test]
        public void ExistingPlayerDeletionUsesTheInjectedEntryContract()
        {
            var go = new GameObject("StartMenuControllerDeleteTest");
            try
            {
                var controller = go.AddComponent<StartMenuController>();
                var host = new RecordingEntryHost();
                controller.Configure(host, null, null);

                controller.DeletePlayer("slot_existing");

                Assert.AreEqual("slot_existing", host.DeletedSlotId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SlotRowDeletionRequiresConfirmationAndCancelNeverCallsTheHost()
        {
            var root = new GameObject("StartMenuViewDeleteTest");
            try
            {
                var view = root.AddComponent<StartMenuView>();
                var container = new GameObject("SlotContainer", typeof(RectTransform));
                container.transform.SetParent(root.transform, false);
                var panel = new GameObject("DeleteConfirmPanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                Text message = CreateText(panel.transform, "DeleteConfirmText");
                Button confirm = CreateButton(panel.transform, "ConfirmDeleteButton");
                Button cancel = CreateButton(panel.transform, "CancelDeleteButton");
                SetPrivateField(view, "slotContainer", container.transform);
                SetPrivateField(view, "deleteConfirmPanel", panel);
                SetPrivateField(view, "deleteConfirmText", message);
                SetPrivateField(view, "deleteConfirmButton", confirm);
                SetPrivateField(view, "deleteCancelButton", cancel);

                var deleted = new List<string>();
                view.Configure(() => { }, _ => { }, deleted.Add);
                view.ShowSlots(new[]
                {
                    new PlayerSlotSummary("slot_keep", "保留角色", true, true, null),
                    new PlayerSlotSummary("broken", null, false, true, "InvalidSaveData"),
                    new PlayerSlotSummary("slots_unavailable", null, false, false, "DirectoryUnavailable"),
                });

                Button keepDelete = DeleteButton(container.transform, "slot_keep");
                Button brokenDelete = DeleteButton(container.transform, "broken");
                Assert.IsNotNull(keepDelete, "a readable slot must expose its own delete action");
                Assert.IsNotNull(brokenDelete, "an unreadable real slot must still be deletable");
                Assert.IsNull(
                    DeleteButton(container.transform, "slots_unavailable"),
                    "a directory-level synthetic row must not offer deletion");

                keepDelete.onClick.Invoke();
                Assert.IsTrue(panel.activeSelf);
                StringAssert.Contains("slot_keep", message.text);
                StringAssert.Contains("保留角色", message.text);
                StringAssert.Contains("不可恢复", message.text);

                cancel.onClick.Invoke();
                Assert.IsFalse(panel.activeSelf);
                Assert.IsEmpty(deleted, "cancelling a deletion must not reach the host");

                brokenDelete.onClick.Invoke();
                Assert.IsTrue(panel.activeSelf);
                StringAssert.Contains("broken", message.text);
                StringAssert.Contains("无法读取", message.text);
                confirm.onClick.Invoke();
                Assert.IsFalse(panel.activeSelf);
                CollectionAssert.AreEqual(new[] { "broken" }, deleted);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FailedSlotDeletionKeepsTheRowsAndReportsTheReasonInChinese()
        {
            var root = new GameObject("StartMenuDeleteFailureTest");
            try
            {
                var view = root.AddComponent<StartMenuView>();
                var container = new GameObject("SlotContainer", typeof(RectTransform));
                container.transform.SetParent(root.transform, false);
                var panel = new GameObject("DeleteConfirmPanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                SetPrivateField(view, "slotContainer", container.transform);
                SetPrivateField(view, "deleteConfirmPanel", panel);
                SetPrivateField(view, "deleteConfirmText", CreateText(panel.transform, "DeleteConfirmText"));
                Button confirm = CreateButton(panel.transform, "ConfirmDeleteButton");
                SetPrivateField(view, "deleteConfirmButton", confirm);
                SetPrivateField(view, "deleteCancelButton", CreateButton(panel.transform, "CancelDeleteButton"));
                Text failure = CreateText(root.transform, "FailureText");
                SetPrivateField(view, "failureText", failure);

                var host = new RecordingEntryHost
                {
                    DeleteResult = PlayerEntryResult.Failed("DeleteFailed"),
                };
                var controller = root.AddComponent<StartMenuController>();
                controller.Configure(host, view, null);
                controller.RefreshSlots();

                controller.DeletePlayer("slot_keep");

                Assert.AreEqual(1, host.DeleteCount);
                StringAssert.Contains("删除存档失败", failure.text);
                StringAssert.Contains("保留", failure.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Text CreateText(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Text>();
        }

        private static Button CreateButton(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Button>();
        }

        private static Button DeleteButton(Transform container, string slotId)
        {
            Transform row = container.Find("SaveSlot_" + slotId);
            if (row == null) return null;
            Transform button = row.Find("DeleteSlot_" + slotId);
            return button == null ? null : button.GetComponent<Button>();
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

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }

        private sealed class RecordingEntryHost : IPlayerEntryHost
        {
            public string LoadedSlotId { get; private set; }
            public string DeletedSlotId { get; private set; }
            public int DeleteCount { get; private set; }
            public PlayerEntryResult DeleteResult { get; set; } = PlayerEntryResult.Success();
            public IReadOnlyList<PlayerSlotSummary> ListSlots() => new PlayerSlotSummary[0];
            public PlayerEntryResult CreateNewPlayer(string slotId, CharacterData profile, string startNodeId) =>
                PlayerEntryResult.Success();
            public PlayerEntryResult LoadPlayer(string slotId)
            {
                LoadedSlotId = slotId;
                return PlayerEntryResult.Success();
            }
            public PlayerEntryResult DeletePlayer(string slotId)
            {
                DeletedSlotId = slotId;
                DeleteCount++;
                return DeleteResult;
            }
        }
    }
}
