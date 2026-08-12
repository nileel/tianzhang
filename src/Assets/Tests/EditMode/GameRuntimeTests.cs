using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Character;
using TianZhang.Cultivation;
using TianZhang.Entity;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class GameRuntimeTests
    {
        private CharacterData definition;

        [TearDown]
        public void TearDown()
        {
            if (definition != null) Object.DestroyImmediate(definition);
        }

        [Test]
        public void NewGameAndClearUseTheSameDeterministicInitialization()
        {
            var runtime = new GameRuntime();
            definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "测试角色";
            definition.realmMultiplier = 1f;

            runtime.BeginNewGame(
                CharacterRuntimeProfile.FromDefinition("player", definition),
                CultivationState.CreateEmpty(),
                "guanzhong_hub");
            runtime.AdvanceWorldDay();
            runtime.EnterSettlement("guanzhong_city");

            runtime.Clear();

            Assert.That(runtime.Player, Is.Null);
            Assert.That(runtime.Cultivation, Is.Null);
            Assert.That(runtime.WorldClock.Year, Is.EqualTo(GameRuntime.InitialWorldYear));
            Assert.That(runtime.WorldClock.Day, Is.EqualTo(1));
            Assert.That(runtime.Navigation.WorldNodeId, Is.EqualTo(GameRuntime.DefaultWorldNodeId));
            Assert.That(runtime.Navigation.SettlementId, Is.Null);
            Assert.That(runtime.Bounties, Is.Not.Null);
            Assert.That(runtime.InventoryGrants, Is.Not.Null);
            Assert.That(runtime.Charters, Is.Not.Null);
        }

        [Test]
        public void NavigationRoundTripPreservesOnlyDeclaredSourceContext()
        {
            var runtime = new GameRuntime();

            Assert.That(runtime.EnterWorld("guanzhong_hub"), Is.EqualTo(GameplaySceneNames.World));
            Assert.That(runtime.EnterSettlement("guanzhong_city"), Is.EqualTo(GameplaySceneNames.Settlement));
            Assert.That(
                runtime.EnterAdventure("guanzhong_wild", SceneReturnTarget.Settlement("guanzhong_city")),
                Is.EqualTo(GameplaySceneNames.Adventure));
            Assert.That(runtime.ReturnToPreviousScene(), Is.EqualTo(GameplaySceneNames.Settlement));
            Assert.That(runtime.Navigation.SettlementId, Is.EqualTo("guanzhong_city"));
            Assert.That(runtime.Navigation.AdventureId, Is.Null);
        }
    }
}
