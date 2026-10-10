#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.UnityContent;
using TianZhang.Infrastructure.Persistence;
using TianZhang.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatNavigationGuardPlayModeTests
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

        [UnityTest]
        public IEnumerator VictoryReturnsThroughFormalSettlementOnce() => VerifyFormalReturn(CombatSessionOutcome.Victory);

        [UnityTest]
        public IEnumerator DefeatReturnsThroughFormalSettlementOnce() => VerifyFormalReturn(CombatSessionOutcome.Defeat);

        private static IEnumerator VerifyFormalReturn(CombatSessionOutcome outcome)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var environment = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset");
            var config = AssetDatabase.LoadAssetAtPath<CharacterCreationPointBuyConfig>(
                "Assets/Resources/Data/CharacterCreation/CharacterCreationPointBuyConfig.asset");
            Assert.IsNotNull(catalog);
            Assert.IsNotNull(environment);
            Assert.IsNotNull(config);
            Assert.IsTrue(catalog.TryGetAdventureMap(FormalEncounterRules.GuanzhongWildAdventureId, out AdventureMapData map));
            Assert.IsTrue(catalog.TryGetEnemy(FormalEncounterRules.ShijiahouEnemyId, out EnemyData enemy));
            CharacterData profile = CharacterCreationManager.CreateProfile(CharacterCreationCatalog.CreateDefaultDraft(), config, GrowthRules);
            var host = new GameObject("FormalNavigationGuardRuntimeTest");
            try
            {
                var runtime = new GameRuntime();
                runtime.BeginNewGame(CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules),
                    CultivationState.FromDefinition(profile.foundationPurpleMansionState), "guanzhong_hub");
                runtime.EnterWorld("guanzhong_hub");
                runtime.EnterSettlement("guanzhong_city");
                var accepted = runtime.Bounties.Accept(catalog, "bounty_guanzhong_shijiahou", "guanzhong_city");
                Assert.IsTrue(accepted.Succeeded, accepted.FailureReason);
                runtime.EnterAdventure(map.adventureId, SceneReturnTarget.Settlement("guanzhong_city"));
                var controller = host.AddComponent<AdventureController>();
                controller.enabled = false;
                var input = host.AddComponent<AdventureInputController>();
                var hud = host.AddComponent<AdventureHudPresenter>();
                var spawner = host.AddComponent<AdventureUnitSpawner>();
                var encounters = host.AddComponent<EncounterCoordinator>();
                int loads = 0;
                string loadedScene = null;
                controller.Configure(catalog, map, runtime.Player.Capture(), environment,
                    System.Array.Empty<AttackProfileData>(), new AdventureMapLoader(), spawner, input, hud,
                    encounters, new CombatEntryAdapter(new[] { new CombatOrdinaryActionDefinition(CombatCommandKind.Guard, 50000, 50000, 0, 0), new CombatOrdinaryActionDefinition(CombatCommandKind.SwapSpell, 50000, 50000, 0, 60) }, UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset")), runtime, runtime.Bounties, runtime.InventoryGrants,
                    scene => { loads++; loadedScene = scene; });
                controller.SetEncounterRandomSource(new ZeroRandomSource());
                typeof(AdventureController).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
                Assert.AreEqual(AdventureSceneState.Exploration, controller.CurrentState);
                typeof(AdventureController).GetProperty("CurrentState").SetValue(controller, AdventureSceneState.Combat);
                var priorNode = controller.Session.CurrentNode;
                var priorNavigation = runtime.Navigation;
                Assert.IsFalse(input.SelectNode("return"));
                Assert.IsFalse(input.SelectNode("return"));
                Assert.AreEqual(0, loads);
                Assert.AreSame(priorNode, controller.Session.CurrentNode);
                Assert.AreSame(priorNavigation, runtime.Navigation);
                yield return null;

                controller.ResolveEncounter(outcome, enemy);
                Assert.AreEqual(1, loads);
                Assert.AreEqual(GameplaySceneNames.Settlement, loadedScene);
                Assert.AreEqual("guanzhong_city", runtime.Navigation.SettlementId);
                Assert.IsNull(runtime.Navigation.AdventureId);
                Assert.AreEqual(AdventureSceneState.Returning, controller.CurrentState);
                Assert.AreEqual(outcome, controller.LastEncounterOutcome);
                Assert.IsNotNull(controller.LastFormalEncounterResult);
                int expectedCount = outcome == CombatSessionOutcome.Victory ? 1 : 0;
                Assert.AreEqual(expectedCount, runtime.Bounties.GetState("bounty_guanzhong_shijiahou").Progress);
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_lingshi_low"));
                var result = controller.LastFormalEncounterResult;
                var returnedNavigation = runtime.Navigation;
                LogAssert.Expect(LogType.Error, new Regex(FormalEncounterRules.AlreadyConsumedReason));
                controller.ResolveEncounter(outcome, enemy);
                Assert.AreEqual(FormalEncounterRules.AlreadyConsumedReason, controller.LastFailureReason);
                Assert.AreSame(result, controller.LastFormalEncounterResult);
                Assert.AreSame(returnedNavigation, runtime.Navigation);
                Assert.AreEqual(1, loads);
                Assert.AreEqual(expectedCount, runtime.Bounties.GetState("bounty_guanzhong_shijiahou").Progress);
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_shijia_piece"));
                Assert.AreEqual(expectedCount, Quantity(runtime, "item_lingshi_low"));
            }
            finally
            {
                Object.Destroy(host);
                Object.Destroy(profile);
            }
            yield return null;
        }

        private static int Quantity(GameRuntime runtime, string itemId)
        {
            foreach (InventoryRecord record in runtime.CaptureSave().inventory)
                if (record.itemId == itemId) return record.quantity;
            return 0;
        }

        private sealed class ZeroRandomSource : IFormalEncounterRandomSource
        {
            public int NextPercent() => 0;
        }
    }
}
#endif
