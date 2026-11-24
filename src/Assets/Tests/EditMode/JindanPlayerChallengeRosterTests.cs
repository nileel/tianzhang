using System;
using System.Linq;
using System.IO;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Gameplay.Contracts;
using TianZhang.Infrastructure.Persistence;
using UnityEditor;
using UnityEngine;
using TianZhang.Spatial;

namespace TianZhang.Tests.EditMode
{
    public sealed class JindanPlayerChallengeRosterTests
    {
        private AdventureUnitSpawner spawner;

        [SetUp] public void CreateSpawner() => spawner = new GameObject("TerraceSpawner").AddComponent<AdventureUnitSpawner>();
        [TearDown] public void DestroySpawner() => UnityEngine.Object.DestroyImmediate(spawner.gameObject);

        [TestCase(JindanDemoCatalogData.EarthPresetId, "土", true)]
        [TestCase(JindanDemoCatalogData.WaterPresetId, "水", true)]
        [TestCase(JindanDemoCatalogData.PlayerFirePresetId, "火", false)]
        [TestCase(JindanDemoCatalogData.YangPresetId, "水/火", true)]
        [TestCase(JindanDemoCatalogData.YinPresetId, "水/火", false)]
        public void SavedPlayerKeepsItsLegalBuildInAllChallenges(string presetId, string element, bool flies)
        {
            var demo = AssetDatabase.LoadAssetAtPath<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            var content = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var runtime = new GameRuntime();
            runtime.ConfigureGrowth(content.CombatRuleSet);
            runtime.BeginJindanDemoGame("陆遥", demo, presetId);
            string json = runtime.CaptureSaveJson();
            runtime.RestoreSaveJson(json, content, demo);
            Assert.AreEqual(json, runtime.CaptureSaveJson());
            Assert.AreEqual(presetId, runtime.JindanDemoPresetId);
            Assert.AreEqual(element, runtime.Player.VisibleRootElement);
            var preset = demo.GetPreset(presetId);
            var owner = runtime.Player.Capture();
            Assert.AreEqual(preset.realmSubstage, owner.Progression.RealmStage);
            Assert.AreEqual(5, owner.AbilityLoadout.SpellSlots);
            Assert.AreEqual(2, owner.AbilityLoadout.SkillSlots);
            Assert.Contains("spell_feixingzhi", owner.AbilityLoadout.EquippedSpells);
            Assert.AreEqual(presetId == demo.demolition.characterId, owner.AbilityLoadout.EquippedSpells.Contains(demo.demolition.profileId));
            Assert.AreEqual(presetId == JindanDemoCatalogData.YangPresetId, owner.AbilityLoadout.EquippedSpells.Contains("spell_guanshiji"));

            for (int segment = -1; segment < 3; segment++)
            {
                Assert.IsTrue(JindanRehearsalFactory.TryCreateForPlayer(demo, content, spawner, owner, segment,
                    presetId, out var spawned, out var reason), reason);
                AssertFixedOpening(spawned, segment);
                CollectionAssert.AreEqual(owner.AbilityLoadout.EquippedSpells, spawned.Player.EquippedArtProfileIds);
                var actors = spawned.Players.Concat(spawned.Enemies).ToArray();
                Assert.AreEqual("player", spawned.Player.Id);
                Assert.AreEqual("陆遥", owner.Identity.DisplayName);
                Assert.IsTrue(actors.All(actor => actor.Id != presetId));
                Assert.AreEqual(actors.Length, actors.Select(actor => actor.Position).Distinct().Count());
                var seats = JindanRehearsalFactory.CreateLoadout(demo, spawned, "EARTH_GRAVITY").Seats
                    .Select(seat => seat.Definition).ToArray();
                Assert.AreEqual(seats.Length, seats.Select(seat => seat.PositionId).Distinct().Count());
                Assert.AreEqual(seats.Length, seats.Select(seat => seat.AbilityInstanceId).Distinct().Count());
                var mine = seats.Where(seat => seat.ActorId == "player").ToArray();
                CollectionAssert.AreEquivalent(preset.seatInstanceIds.Select(id => JindanDemoCatalogData.PlayerInstanceId(presetId, id)),
                    mine.Select(seat => seat.SeatId));
                foreach (var seat in mine)
                    Assert.AreEqual(seat.AbilityInstanceId, spawned.Player.GetAbilityInstanceId(seat.InitialEffect));
                var flight = JindanRehearsalFactory.CreateFlight(demo, spawned)?.Find("player");
                Assert.AreEqual(flies, flight != null);
                if (flies) Assert.AreEqual(JindanDemoCatalogData.PlayerInstanceId(presetId, preset.flightAbilityInstanceId), flight.InstanceId);
                Assert.AreEqual(mine.Length, seats.Length, "Only the saved player holds seats.");
            }
            Assert.AreEqual(json, runtime.CaptureSaveJson());
            var invalid = GameSaveSerializer.Deserialize(json);
            invalid.jindanDemoPresetId = presetId == JindanDemoCatalogData.YangPresetId
                ? JindanDemoCatalogData.YinPresetId : JindanDemoCatalogData.YangPresetId;
            Assert.Throws<ArgumentException>(() => runtime.RestoreSave(invalid, content, demo));
            Assert.AreEqual(json, runtime.CaptureSaveJson());
        }

        [TestCase(false, AdventureEntrance.Unspecified, false)]
        [TestCase(true, AdventureEntrance.Unspecified, true)]
        [TestCase(false, AdventureEntrance.Wilderness, false)]
        [TestCase(false, AdventureEntrance.Terrace, true)]
        public void TerraceNavigationPreservesOldAndExplicitDestinations(bool trial, AdventureEntrance entrance, bool terrace)
        {
            var demo = AssetDatabase.LoadAssetAtPath<JindanDemoCatalogData>("Assets/Data/JindanDemo/JindanDemoCatalog.asset");
            var content = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            var source = new GameRuntime();
            source.ConfigureGrowth(content.CombatRuleSet);
            if (trial) source.BeginJindanDemoGame("陆遥", demo, JindanDemoCatalogData.PlayerFirePresetId);
            else source.RestoreSaveJson(File.ReadAllText(Path.Combine(Application.dataPath,
                "../../artifacts/guanzhong-playtest-20261119/jiangzuo-qa-save.json")), content);
            source.EnterWorld("guanzhong_hub");
            source.EnterSettlement("guanzhong_city");
            var town = SceneReturnTarget.Settlement("guanzhong_city");
            source.EnterAdventure("guanzhong_wild", town, entrance);
            string json = source.CaptureSaveJson();
            if (entrance == AdventureEntrance.Unspecified)
            {
                const string field = ",\"entrance\":0";
                StringAssert.Contains(field, json);
                json = json.Replace(field, "");
            }

            var restored = new GameRuntime();
            restored.RestoreSaveJson(json, content, demo);
            Assert.AreEqual(entrance, restored.Navigation.Entrance);
            Assert.AreEqual(terrace, restored.IsTerraceDestination);
            Assert.AreEqual(source.Player.Identity.DisplayName, restored.Player.Identity.DisplayName);
            Assert.AreEqual(GameplaySceneNames.Settlement, restored.ReturnToPreviousScene());
            Assert.AreEqual(AdventureEntrance.Unspecified, restored.Navigation.Entrance);
            Assert.IsFalse(restored.IsTerraceDestination);
            Assert.AreEqual("guanzhong_city", restored.Navigation.SettlementId);
        }

        [Test]
        public void UnknownAdventureEntranceCannotReplaceLiveNavigation()
        {
            var runtime = new GameRuntime();
            runtime.EnterSettlement("guanzhong_city");
            var original = runtime.Navigation;
            Assert.Throws<ArgumentOutOfRangeException>(() => runtime.EnterAdventure("guanzhong_wild",
                SceneReturnTarget.Settlement("guanzhong_city"), (AdventureEntrance)99));
            Assert.AreSame(original, runtime.Navigation);
        }

        private static void AssertFixedOpening(AdventureSpawnSet spawned, int segment)
        {
            var playerStart = new[] { new HexCoord(4, 0), new HexCoord(4, 0), new HexCoord(4, 4), new HexCoord(4, 14) };
            var enemyStart = new[] { new HexCoord(7, 3), new HexCoord(7, 12), new HexCoord(7, 7), new HexCoord(7, 14) };
            string species = segment == 0 || segment == 2 ? "enemy_lieshijia" : "enemy_shijiahou";
            Assert.AreEqual(1, spawned.Players.Count);
            Assert.AreEqual(1, spawned.Enemies.Count);
            Assert.AreEqual(species, spawned.EnemyData.enemyId);
            Assert.AreEqual(playerStart[segment + 1], spawned.Player.Position);
            Assert.AreEqual(enemyStart[segment + 1], spawned.Enemy.Position);
            Assert.AreEqual(0, spawned.Player.Facing);
            Assert.AreEqual(3, spawned.Enemy.Facing);
            Assert.AreEqual(spawned.Enemy.MaximumHealth, spawned.Enemy.CurrentHealth);
        }
    }
}
