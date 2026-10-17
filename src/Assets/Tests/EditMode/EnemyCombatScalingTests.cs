using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Character;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TianZhang.Tests.EditMode
{
    public sealed class EnemyCombatScalingTests
    {
        [Test]
        public void ProductionImportAndRepeatedSpawnsScaleAllEnemyResultsExactlyOnce()
        {
            SettlementContentImporter.ImportContentCatalog();
            var catalog = AssetDatabase.LoadAssetAtPath<ContentCatalogData>("Assets/Data/ContentCatalog/ContentCatalog.asset");
            Assert.That(catalog.TryGetEnemy("enemy_shijiahou", out var enemy), Is.True);
            Assert.That(enemy.combatStatScale, Is.EqualTo(0.5f));
            string templateBefore = JsonUtility.ToJson(enemy.combatTemplate);
            string rulesBefore = JsonUtility.ToJson(catalog.CombatRuleSet);
            var baseline = AdventureUnitSpawner.ProjectEnemyStats(enemy.combatTemplate, catalog.CombatRuleSet, 1f);
            CollectionAssert.AreEqual(new[] { 13, 16, 6, 6, 6, 4, 657, 108, 78, 36, 67, 25, 100000, 4000 }, Integers(baseline));
            var playerDefinition = AssetDatabase.LoadAssetAtPath<CharacterData>("Assets/Data/Characters/Char_char_taiyixiushi.asset");
            Assert.That(playerDefinition, Is.Not.Null);
            var player = CharacterRuntimeProfile.FromDefinition("player", playerDefinition, catalog.CombatRuleSet).Capture();
            var owner = new GameObject("R4Spawner");
            try
            {
                var spawner = owner.AddComponent<AdventureUnitSpawner>();
                for (int entry = 0; entry < 2; entry++)
                {
                    Assert.That(spawner.TrySpawn(player, catalog, new AdventureNodeData { q = 0, r = 0 },
                        new AdventureNodeData { q = 3, r = 5, contentId = enemy.enemyId,
                            additionalEnemies = new[] { new AdventureEnemySpawnData { instanceId = "enemy_2", q = 4, r = 5 } } },
                        out var spawned, out var reason), Is.True, reason);
                    CollectionAssert.AreEqual(new[] { 7, 8, 3, 3, 3, 2, 329, 54, 39, 18, 34, 13, 50000, 2000 }, Integers(spawned.EnemyStats));
                    foreach (var field in typeof(EnemyCombatStats).GetFields().Where(f => f.FieldType == typeof(float)))
                        Assert.That(field.GetValue(spawned.EnemyStats), Is.EqualTo((float)field.GetValue(baseline) * 0.5f), field.Name);
                    foreach (var actor in spawned.Enemies)
                    {
                        Assert.That(actor.MaximumHealth, Is.EqualTo(329)); Assert.That(actor.CurrentHealth, Is.EqualTo(329));
                        Assert.That(actor.MaximumSpirit, Is.EqualTo(54)); Assert.That(actor.RawSpirit, Is.EqualTo(3));
                        Assert.That(actor.RawMind, Is.EqualTo(3)); Assert.That(actor.Speed, Is.EqualTo(3));
                        Assert.That(actor.PhysicalAttack, Is.EqualTo(39)); Assert.That(actor.SoulAttack, Is.EqualTo(18));
                        Assert.That(actor.PhysicalDefense, Is.EqualTo(34)); Assert.That(actor.SoulDefense, Is.EqualTo(13));
                        Assert.That(actor.BlockRate, Is.EqualTo(7.5f)); Assert.That(actor.BlockReduction, Is.EqualTo(12.5f));
                        Assert.That(actor.CriticalRate, Is.EqualTo(2.5f)); Assert.That(actor.CriticalDamage, Is.EqualTo(5f));
                        Assert.That(actor.ActionPointMaximumUnits, Is.EqualTo(50000));
                        Assert.That(actor.MovementEfficiencyMilliUnits, Is.EqualTo(2000));
                        Assert.That(actor.BasicAttackProfileId, Is.EqualTo("basic_body_shijiahou_lianqi"));
                    }
                    Assert.That(spawned.Player.ActionPointMaximumUnits, Is.EqualTo(player.Attributes.ActionPointMaximumUnits));
                    Assert.That(spawned.Player.MovementEfficiencyMilliUnits, Is.EqualTo(player.Attributes.MovementEfficiencyMilliUnits));
                    Assert.That(spawned.Player.MaximumHealth, Is.EqualTo(player.Resources.MaximumHealth));
                    string output = Path.GetFullPath("Logs/r4-stats"); Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "after-effective.json"), JsonUtility.ToJson(spawned.EnemyStats, true));
                    File.WriteAllText(Path.Combine(output, "before-effective.json"), JsonUtility.ToJson(baseline, true));
                }
                Assert.That(JsonUtility.ToJson(enemy.combatTemplate), Is.EqualTo(templateBefore));
                Assert.That(JsonUtility.ToJson(catalog.CombatRuleSet), Is.EqualTo(rulesBefore));
                string imported = JsonUtility.ToJson(enemy);
                SettlementContentImporter.ImportContentCatalog();
                Assert.That(JsonUtility.ToJson(enemy), Is.EqualTo(imported), "Reimport must preserve the same scale and template reference.");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(1.5f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidScaleIsRejected(float scale) => Assert.Throws<ArgumentOutOfRangeException>(() => new EnemyCombatStats().Scale(scale));

        private static int[] Integers(EnemyCombatStats v) => new[] { v.rootBone, v.physique, v.spirit, v.mind, v.reaction, v.talent,
            v.maximumHealth, v.maximumSpirit, v.physicalAttack, v.soulAttack, v.physicalDefense, v.soulDefense,
            v.actionPointMaximumUnits, v.movementEfficiencyMilliUnits };
    }
}
