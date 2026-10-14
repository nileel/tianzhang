using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Combat;
using TianZhang.Cultivation;
using TianZhang.Editor;
using TianZhang.Editor.ConfigEditor;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using TianZhang.Features.CharacterCreation;
using TianZhang.Features.WorldMap;
using TianZhang.Features.Settlement;
using TianZhang.Features.Adventure;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TianZhang.Tests
{
    public sealed class ConfigEditorApplyTests
    {
        private ConfigTableRegistry registry;
        private ConfigDraftSession session;
        private string tempRoot;
        private bool playOptionsEnabled;
        private EnterPlayModeOptions playOptions;
        private readonly Dictionary<string, byte[]> backup = new Dictionary<string, byte[]>();
        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
        private static string Absolute(string path) => Path.Combine(ProjectRoot, path);

        [SetUp]
        public void SetUp()
        {
            playOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            playOptions = EditorSettings.enterPlayModeOptions;
            registry = ConfigTableRegistry.Discover(); session = registry.OpenSession();
            tempRoot = Path.Combine(Path.GetTempPath(), "tzg-config-apply-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "Assets/DataConfig"));
            foreach (var table in registry.Tables)
                File.WriteAllBytes(Path.Combine(tempRoot, table.CsvPath), session.Snapshot().Tables[table.TableId].Document.OriginalBytes);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled = playOptionsEnabled;
            EditorSettings.enterPlayModeOptions = playOptions;
            foreach (var window in Resources.FindObjectsOfTypeAll<ConfigEditorWindow>()) { window.DiscardChanges(); window.Close(); }
            foreach (var path in backup.Keys.Where(p => p.EndsWith(".asset", StringComparison.Ordinal)))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null) EditorUtility.ClearDirty(asset);
            }
            foreach (var pair in backup) File.WriteAllBytes(Absolute(pair.Key), pair.Value);
            foreach (var path in backup.Keys.Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)))
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            foreach (var pair in backup) CollectionAssert.AreEqual(pair.Value, File.ReadAllBytes(Absolute(pair.Key)), pair.Key);
            backup.Clear();
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        }
        private void ProtectProduction()
        {
            using var plan = ConfigImportPlan.Build(session, registry.Tables.Select(t => t.TableId).ToArray());
            foreach (var path in registry.Tables.Select(t => t.CsvPath).Concat(plan.Steps.SelectMany(s => s.Products.Keys)).Distinct())
                foreach (var file in new[] { path, path + ".meta" }) backup.Add(file, File.ReadAllBytes(Absolute(file)));
        }
        private static void Good(ConfigOperationResult result) => Assert.IsEmpty(result.Issues,
            result.Evidence + "\n" + string.Join("\n", result.Issues.Select(i => i.TableId + "/" + i.RowKey + "/" + i.FieldPath + ": " + i.ChineseReason)));
        private void GrowthEdit(string value = "1500") => session.SetValue("RealmCombatGrowth", "realm_fanren", "baseMovementEfficiencyMilliUnits", ConfigValue.Scalar(value));
        private string SpellEdit()
        {
            var row = session.Snapshot().Tables["Spells"].Rows.First();
            session.SetValue("Spells", row.Key, "mpCost", ConfigValue.Scalar((int.Parse(row.Value.At("mpCost").RawToken) + 1).ToString()));
            return row.Key;
        }

        [Test]
        public void AllTwentyTwoProvidersBuildOneOrderedPlanWithoutAssetWrites()
        {
            Assert.AreEqual(22, registry.Tables.Count);
            Assert.IsInstanceOf<ConfigSaveApplyService>(registry.ApplyService);
            using var plan = ConfigImportPlan.Build(session, registry.Tables.Select(t => t.TableId).ToArray());
            Assert.AreEqual(14, plan.Steps.Count);
            Assert.AreEqual(14, plan.Steps.Select(s => s.Description.Method).Distinct().Count());
            Assert.That(plan.Steps.SelectMany(s => s.Products.Keys).Distinct().Count(), Is.GreaterThan(170));
            Assert.IsFalse(session.IsDirty);
        }

        [Test]
        public void ExternalDependencyChangeWithSameTimestampBlocksBeforeFirstWrite()
        {
            GrowthEdit();
            var path = Path.Combine(tempRoot, "Assets/DataConfig/Language.csv");
            var stamp = File.GetLastWriteTimeUtc(path); File.AppendAllText(path, "\n#external\n"); File.SetLastWriteTimeUtc(path, stamp);
            var writes = 0;
            var service = new ConfigSaveApplyService(tempRoot, (p, bytes) => writes++);
            var result = service.ValidateAndSave(session);
            Assert.IsNotEmpty(result.Issues); Assert.AreEqual(0, writes); Assert.IsTrue(session.IsDirty);
            StringAssert.Contains("外部修改", result.Issues[0].ChineseReason);
        }

        [Test]
        public void PartialIoFailureKeepsUnsavedDraftAndReportsExactSavedFiles()
        {
            GrowthEdit(); var writes = 0;
            var service = new ConfigSaveApplyService(tempRoot, (path, bytes) => {
                if (++writes == 2) throw new IOException("injected second-file failure");
                ConfigSaveApplyService.WriteFile(path, bytes);
            });
            var result = service.ValidateAndSave(session);
            Assert.IsNotEmpty(result.Issues); Assert.AreEqual(1, result.SavedPaths.Count);
            Assert.IsNotEmpty(result.FailedSavePaths); Assert.IsEmpty(result.AppliedPaths); Assert.IsTrue(session.IsDirty);
            Assert.IsTrue(service.HasPendingApplication(session));
            foreach (var table in session.Snapshot().Tables.Values)
                Assert.AreEqual(table.Document.BaselineHash, CsvConfigDocument.ComputeHash(File.ReadAllBytes(Path.Combine(tempRoot, table.Definition.CsvPath))));
            Assert.AreEqual(1, service.GetPreviousSavedBytes(session).Count);
            Assert.IsEmpty(Directory.GetFiles(Path.Combine(tempRoot, "Assets/DataConfig"), ".config-save-*"));
        }

        [Test]
        public void ReadOnlyDestinationBlocksAllCsvWrites()
        {
            GrowthEdit(); var path = Path.Combine(tempRoot, "Assets/DataConfig/RealmCombatGrowth.csv");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                var writes = 0; var service = new ConfigSaveApplyService(tempRoot, (p, bytes) => writes++);
                var result = service.ValidateAndSave(session);
                Assert.IsNotEmpty(result.Issues); Assert.AreEqual(0, writes); Assert.IsTrue(session.IsDirty);
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        }

        [UnityTest]
        public IEnumerator ActualWindowCanSaveThenApplyCleanDraftAndReadBackMirrors()
        {
            ProtectProduction();
            var service = new ConfigSaveApplyService();
            var window = ConfigEditorWindow.Open(); window.Initialize(new ConfigTableRegistry(registry.Tables, service), session);
            window.SelectTable("RealmCombatGrowth"); window.SelectRow("realm_fanren");
            window.SetSelectedValue("baseMovementEfficiencyMilliUnits", ConfigValue.Scalar("1500"));
            Assert.IsTrue(window.SaveEnabled); Assert.IsTrue(window.ApplyEnabled);
            Assert.IsNotEmpty(ConfigSaveApplyService.LinkedEdits(session));
            window.Repaint(); yield return null;
            window.RunApply(false); Good(window.LastOperation);
            Assert.IsFalse(session.IsDirty); Assert.IsFalse(window.SaveEnabled); Assert.IsTrue(window.ApplyEnabled);
            window.RunApply(true); Good(window.LastOperation);
            StringAssert.StartsWith("应用成功", window.LastOperation.Evidence); Assert.IsFalse(window.ApplyEnabled);
            var point = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>("Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");
            Assert.AreEqual(1500, point.movementEfficiencyMilliUnits);
            Assert.AreEqual(1500, AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/Char_Player.asset").movementEfficiencyMilliUnits);
            var reopened = registry.OpenSession();
            Assert.AreEqual("1500", reopened.Snapshot().Tables["RealmCombatGrowth"].Rows["realm_fanren"].At("baseMovementEfficiencyMilliUnits").RawToken);
            Assert.IsEmpty(reopened.LastApplicationEvidence);
            window.Initialize(new ConfigTableRegistry(registry.Tables, service), reopened);
            Assert.IsFalse(window.ApplyEnabled); window.Repaint(); yield return null;
        }

        [Test]
        public void DirtyAssetGatePreservesUserChangeAndAllowsExplicitApplyAfterDiscard()
        {
            ProtectProduction(); SpellEdit();
            var service = new ConfigSaveApplyService();
            var path = "Assets/Data/Characters/Char_Player.asset";
            var asset = AssetDatabase.LoadAssetAtPath<CharacterData>(path);
            var originalName = asset.charName; asset.charName = "unsaved-user-change"; EditorUtility.SetDirty(asset);
            var result = service.SaveAndApply(session);
            Assert.IsFalse(session.IsDirty); Assert.IsTrue(service.HasPendingApplication(session));
            Assert.That(result.Issues.Any(i => i.FieldPath == path && i.ErrorCode == "application_gate"));
            Assert.AreEqual("unsaved-user-change", asset.charName); Assert.IsEmpty(result.AppliedPaths);
            asset.charName = originalName; EditorUtility.ClearDirty(asset);
            result = service.SaveAndApply(session); Good(result); StringAssert.StartsWith("应用成功", result.Evidence);
        }

        [UnityTest]
        public IEnumerator SavedGrowthIsReadByNewCreationAndFormalCombat()
        {
            ProtectProduction(); GrowthEdit();
            Good(new ConfigSaveApplyService().SaveAndApply(session));
            // Keep this fixture's restoration bytes alive while exercising the actual scene owners.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            yield return new EnterPlayMode(false);
            var stale = Object.FindFirstObjectByType<GameBootstrap>(FindObjectsInactive.Include);
            if (stale != null) { Object.Destroy(stale.gameObject); yield return null; }
            SceneManager.LoadScene(GameplaySceneNames.StartMenu);
            yield return SceneReady(GameplaySceneNames.StartMenu);
            typeof(GameBootstrap).GetField("slotStore", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(GameBootstrap.RequireInstance(), new GameSaveSlotStore(Path.Combine(tempRoot, "saves")));
            var creation = Object.FindFirstObjectByType<CharacterCreationController>(FindObjectsInactive.Include);
            creation.Open(); Assert.IsTrue(creation.SelectOrigin("origin_loose"));
            creation.Submit("config-apply-evidence", "配置验收");
            yield return SceneReady(GameplaySceneNames.World);
            Assert.AreEqual(1500, GameBootstrap.RequireRuntime().Player.Attributes.MovementEfficiencyMilliUnits);
            var world = Object.FindFirstObjectByType<WorldMapController>();
            Assert.IsTrue(world.SelectNode("guanzhong_hub")); world.EnterSelectedLocation();
            yield return SceneReady(GameplaySceneNames.Settlement);
            Assert.IsTrue(Object.FindFirstObjectByType<SettlementController>().EnterAdventure("guanzhong_wild"));
            yield return SceneReady(GameplaySceneNames.Adventure);
            var adventure = Object.FindFirstObjectByType<AdventureController>();
            for (int frame = 0; frame < 180 && adventure.CurrentState != AdventureSceneState.Exploration; frame++) yield return null;
            Assert.AreEqual(AdventureSceneState.Exploration, adventure.CurrentState);
            adventure.SetEncounterRandomSource(new EncounterRandom());
            Assert.IsTrue(Object.FindFirstObjectByType<AdventureInputController>().SelectNode("shijiahou_encounter"));
            var encounter = Object.FindFirstObjectByType<EncounterCoordinator>();
            var combat = (CombatSession)typeof(EncounterCoordinator).GetField("session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(encounter);
            Assert.AreEqual(1500, combat.Combatants.All.Single(c => c.Id == "player").MovementEfficiencyMilliUnits);
            yield return new ExitPlayMode();
        }

        private static IEnumerator SceneReady(string scene)
        {
            for (int frame = 0; frame < 180 && SceneManager.GetActiveScene().name != scene; frame++) yield return null;
            Assert.AreEqual(scene, SceneManager.GetActiveScene().name);
            yield return null;
        }
        private sealed class EncounterRandom : IFormalEncounterRandomSource { public int NextPercent() => 0; }

        [UnityTest]
        public IEnumerator WindowSavesWorldAndCharterRepresentativesAndRestoresAllInputs()
        {
            ProtectProduction();
            var window = ConfigEditorWindow.Open();
            window.Initialize(registry, session);
            window.SelectTable("EnvironmentProfiles");
            window.SetSelectedValue("battlefieldCells/0/blocksLineOfSight", ConfigValue.Scalar("1"));
            window.SelectTable("CharterSites");
            window.SetSelectedValue("leftCandidatePositionRank", ConfigValue.Scalar("4"));
            window.Repaint(); yield return null;
            window.RunApply(true); Good(window.LastOperation);
            Assert.That(window.LastOperation.AppliedPaths, Does.Contain("Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset"));
            Assert.That(window.LastOperation.AppliedPaths, Does.Contain("Assets/Data/CharterSites/CharterSite_charter_site_old_water_station.asset"));
            var reloaded = registry.OpenSession().Snapshot();
            Assert.AreEqual("1", reloaded.Tables["EnvironmentProfiles"].Rows.First().Value.At("battlefieldCells/0/blocksLineOfSight").RawToken);
            Assert.AreEqual("4", reloaded.Tables["CharterSites"].Rows.First().Value.At("leftCandidatePositionRank").RawToken);
        }

        [Test]
        public void ImportLogErrorStopsLaterStepsAndRetainsPendingScope()
        {
            ProtectProduction(); SpellEdit(); var calls = 0;
            var service = new ConfigSaveApplyService(ProjectRoot, null, (name, action) => {
                calls++; Debug.LogError("apply-test-import-error");
            });
            LogAssert.Expect(LogType.Error, "apply-test-import-error");
            var result = service.SaveAndApply(session);
            Assert.AreEqual(1, calls); Assert.IsNotEmpty(result.FailedImportStep); Assert.IsEmpty(result.AppliedPaths);
            Assert.IsFalse(session.IsDirty); Assert.IsTrue(service.HasPendingApplication(session));
            StringAssert.Contains("部分结果", result.Evidence); Assert.IsEmpty(session.LastApplicationEvidence);
        }

        [Test]
        public void SavedScopeMergesWithLaterEditsAndLanguageConsumersReimport()
        {
            ProtectProduction(); var key = SpellEdit(); var service = new ConfigSaveApplyService();
            Good(service.ValidateAndSave(session));
            session.SetValue("Language", key, "zh_CN", ConfigValue.Scalar("保存应用验证文本"));
            var result = service.SaveAndApply(session); Good(result);
            Assert.That(result.AppliedPaths.Any(p => p.Contains("GongFa_")));
            Assert.That(result.AppliedPaths.Any(p => p.Contains("CharterSite_")));
            Assert.AreEqual("保存应用验证文本", AssetDatabase.LoadAssetAtPath<SpellData>("Assets/Data/Spells/Spell_" + key + ".asset").spellName);
        }

        [Test]
        public void NpcHashAndGongfaChapterClearPersistAndMatchPureProjection()
        {
            ProtectProduction();
            var npc = session.Snapshot().Tables["NpcCultivationActionWeightProfiles"].Rows.First(r => r.Value.At("recordKind").RawToken == "ACTION");
            session.SetValue("NpcCultivationActionWeightProfiles", npc.Key, "baseWeight", ConfigValue.Scalar("91"));
            var gongfa = session.Snapshot().Tables["GongFa"].Rows.First(r => r.Value.At("chapters").Items.Count > 0);
            session.SetValue("GongFa", gongfa.Key, "chapters", ConfigValue.List(Array.Empty<ConfigValue>(), hasValue: false));
            var result = new ConfigSaveApplyService().SaveAndApply(session); Good(result);
            Assert.AreEqual(0, AssetDatabase.LoadAssetAtPath<GongFaGrowthData>("Assets/Data/GongFa/GongFa_" + gongfa.Key + ".asset").chapters.Length);
            Assert.IsEmpty(session.ValidateDraft()); StringAssert.StartsWith("应用成功", result.Evidence);
        }

        [Test]
        public void MissingExpectedAssetFailsBeforeCsvSave()
        {
            ProtectProduction(); var key = SpellEdit();
            var path = "Assets/Data/Spells/Spell_" + key + ".asset";
            File.Move(Absolute(path), Path.Combine(tempRoot, "missing.asset"));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var result = new ConfigSaveApplyService().SaveAndApply(session);
            Assert.IsNotEmpty(result.Issues); Assert.IsEmpty(result.SavedPaths); Assert.IsTrue(session.IsDirty);
        }
    }
}
