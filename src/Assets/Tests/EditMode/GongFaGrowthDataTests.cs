using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Cultivation;
using TianZhang.Editor;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class GongFaGrowthDataTests
    {
        private const string ProductionGongFaId = "gongfa_baoyuanshouyi";
        private const string ProductionAssetPath = "Assets/Data/GongFa/GongFa_gongfa_baoyuanshouyi.asset";
        private const string ThreeFoundationStages =
            "realm_zhuji@10:1/2/3/4/5/6/7/8/9|" +
            "realm_zhuji@20:10/20/30/40/50/60/70/80/90|" +
            "realm_zhuji@30:100/200/300/400/500/600/700/800/900";

        [Test]
        public void ThreeFoundationStageCodesResolveTheirExactGrowthValues()
        {
            var growth = CultivationContentImporter.ParseGongFaGrowth(ThreeFoundationStages, "fixture");
            var data = ScriptableObject.CreateInstance<GongFaGrowthData>();
            try
            {
                data.subGrowth = growth;
                Assert.That(growth.Select(entry => entry.foundationStageCode), Is.EqualTo(new[] { 10, 20, 30 }));
                Assert.That(data.GetGrowth("筑基", 10).hp, Is.EqualTo(1));
                Assert.That(data.GetGrowth("筑基", 20).hp, Is.EqualTo(10));
                Assert.That(data.GetGrowth("筑基", 30).hp, Is.EqualTo(100));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void LegacyRowsKeepStageCodeZeroAndDuplicateExactKeysAreRejected()
        {
            var legacy = CultivationContentImporter.ParseGongFaGrowth(
                "realm_zhuji:1/2/3/4/5/6/7/8/9|realm_jindan:10/20/30/40/50/60/70/80/90",
                "legacy-fixture");
            Assert.That(legacy.Select(entry => entry.foundationStageCode), Is.EqualTo(new[] { 0, 0 }));

            var duplicate = ScriptableObject.CreateInstance<GongFaGrowthData>();
            try
            {
                duplicate.subGrowth = new[] { legacy[0], legacy[0] };
                Assert.Throws<InvalidOperationException>(() => duplicate.GetGrowth("筑基", 0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(duplicate);
            }

            var exception = Assert.Throws<InvalidDataException>(() => CultivationContentImporter.ParseGongFaGrowth(
                "realm_zhuji@10:1/2/3/4/5/6/7/8/9|realm_zhuji@10:10/20/30/40/50/60/70/80/90",
                "duplicate-fixture"));
            StringAssert.StartsWith("GONGFA_GROWTH_DUPLICATE_KEY:", exception.Message);
        }

        [TestCase("realm_zhuji@0:1/2/3/4/5/6/7/8/9")]
        [TestCase("realm_zhuji@40:1/2/3/4/5/6/7/8/9")]
        [TestCase("realm_jindan@10:1/2/3/4/5/6/7/8/9")]
        public void InvalidFoundationStageCodesAreRejected(string growth)
        {
            var exception = Assert.Throws<InvalidDataException>(() =>
                CultivationContentImporter.ParseGongFaGrowth(growth, "invalid-stage-fixture"));
            StringAssert.StartsWith("GONGFA_GROWTH_STAGE_INVALID:", exception.Message);
        }

        [Test]
        public void ProductionGrowthCsvAndAssetFieldsRemainOneToOne()
        {
            string sourcePath = Path.Combine(Application.dataPath, "DataConfig/GongFa.csv");
            string[] lines = File.ReadAllLines(sourcePath);
            string[] headers = CsvTableReader.FindHeader(lines);
            string[] columns = CsvTableReader.ParseRow(lines.Single(line => line.StartsWith(ProductionGongFaId + ",", StringComparison.Ordinal)));
            var expected = CultivationContentImporter.ParseGongFaGrowth(
                CsvTableReader.GetRequiredValue(headers, columns, "growth", sourcePath),
                sourcePath);
            var asset = AssetDatabase.LoadAssetAtPath<GongFaGrowthData>(ProductionAssetPath);

            Assert.That(asset, Is.Not.Null, $"Missing generated GongFa asset at {ProductionAssetPath}.");
            Assert.That(asset.subGrowth, Has.Length.EqualTo(expected.Length));
            foreach (var entry in expected)
            {
                var actual = asset.GetGrowth(entry.realm, entry.foundationStageCode);
                Assert.That(actual.hp, Is.EqualTo(entry.hp));
                Assert.That(actual.mp, Is.EqualTo(entry.mp));
                Assert.That(actual.physAtk, Is.EqualTo(entry.physAtk));
                Assert.That(actual.magAtk, Is.EqualTo(entry.magAtk));
                Assert.That(actual.physDef, Is.EqualTo(entry.physDef));
                Assert.That(actual.magDef, Is.EqualTo(entry.magDef));
                Assert.That(actual.reaction, Is.EqualTo(entry.reaction));
                Assert.That(actual.movePoints, Is.EqualTo(entry.movePoints));
                Assert.That(actual.mindGrowth, Is.EqualTo(entry.mindGrowth));
            }
        }
    }
}
