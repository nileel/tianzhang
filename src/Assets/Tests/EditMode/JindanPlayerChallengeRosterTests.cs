using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.Bootstrap;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using TianZhang.Infrastructure.Persistence;
using UnityEditor;

namespace TianZhang.Tests.EditMode
{
    public sealed class JindanPlayerChallengeRosterTests
    {
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
                var spawned = JindanRehearsalFactory.CreateForPlayer(demo, "EARTH_GRAVITY", id => id, owner, segment, presetId);
                var actors = spawned.Players.Concat(spawned.Enemies).ToArray();
                Assert.AreEqual("player", spawned.Player.Id);
                Assert.AreEqual("陆遥", spawned.RehearsalNames["player"]);
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
                var flight = JindanRehearsalFactory.CreateFlight(demo, spawned).Find("player");
                Assert.AreEqual(flies, flight != null);
                if (flies) Assert.AreEqual(JindanDemoCatalogData.PlayerInstanceId(presetId, preset.flightAbilityInstanceId), flight.InstanceId);
                if (segment == 1 && presetId == JindanDemoCatalogData.PlayerFirePresetId)
                    CollectionAssert.AreEquivalent(new[] { JindanDemoCatalogData.EarthPresetId, JindanDemoCatalogData.WaterPresetId },
                        spawned.Enemies.Select(actor => actor.Id));
                if (segment == 2 && presetId == JindanDemoCatalogData.YinPresetId)
                    Assert.AreEqual(JindanDemoCatalogData.YangPresetId, spawned.Enemy.Id);
            }
            Assert.AreEqual(json, runtime.CaptureSaveJson());
            var invalid = GameSaveSerializer.Deserialize(json);
            invalid.jindanDemoPresetId = presetId == JindanDemoCatalogData.YangPresetId
                ? JindanDemoCatalogData.YinPresetId : JindanDemoCatalogData.YangPresetId;
            Assert.Throws<ArgumentException>(() => runtime.RestoreSave(invalid, content, demo));
            Assert.AreEqual(json, runtime.CaptureSaveJson());
        }
    }
}
