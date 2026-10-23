using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Content;
using TianZhang.Infrastructure.UnityContent;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class EnvironmentProfileDataTests
    {
        private const string Header =
            "profileId,queryLimits,battlefieldCells,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs,spiritRecoveryEnvironment,edgeClearanceLimits";

        private const string ValidRow =
            "fixture_profile,unitsPerRange=2;maxQueryRange=16,0:0@0@0|1:0@1@0,surface_wet|surface_ash,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:smoke+mist>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none";

        [Test]
        public void ParseEnvironmentProfilesBuildsOneDeterministicProfileFromAValidRow()
        {
            var profiles = WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header, ValidRow },
                "EnvironmentProfiles.csv");

            Assert.AreEqual(1, profiles.Length);
            var profile = profiles[0];
            Assert.AreEqual("fixture_profile", profile.profileId);
            Assert.AreEqual(2, profile.unitsPerRange);
            Assert.AreEqual(16, profile.maxQueryRange);
            Assert.AreEqual(2, profile.battlefieldCells.Length);
            Assert.AreEqual(0, profile.battlefieldCells[0].q);
            Assert.AreEqual(0, profile.battlefieldCells[0].r);
            Assert.IsFalse(profile.battlefieldCells[0].blocksGroundMove);
            Assert.AreEqual(1, profile.battlefieldCells[1].q);
            Assert.IsTrue(profile.battlefieldCells[1].blocksGroundMove);
            Assert.IsFalse(profile.battlefieldCells[1].blocksLineOfSight);
            Assert.AreEqual(2, profile.directedEdges.Length);
            Assert.AreEqual(2, profile.directedEdges[0].metricDistanceUnits);
            Assert.IsTrue(profile.directedEdges[0].allowsMovement);
            Assert.IsTrue(profile.directedEdges[0].allowsEffects);
            Assert.AreEqual(2, profile.directedEdges[1].metricDistanceUnits);
            Assert.IsTrue(profile.directedEdges[1].allowsMovement);
            Assert.AreEqual(6, profile.phenomenonChannels.Length);
            Assert.AreEqual(1, profile.phenomenonPairs.Length);
            Assert.AreEqual(EnvironmentPhenomenonChannel.Visibility, profile.phenomenonPairs[0].channel);
            Assert.AreEqual("mist", profile.phenomenonPairs[0].firstTypeRef);
            Assert.AreEqual("smoke", profile.phenomenonPairs[0].secondTypeRef);
            Assert.AreEqual("haze", profile.phenomenonPairs[0].resultTypeRef);
            CollectionAssert.AreEqual(
                new[] { "element_wood", "element_fire", "element_earth", "element_metal", "element_water" },
                profile.elementRelationRefs);
        }

        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>2:0@2@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@0@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@2@yes@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@2@1@1,surface_wet,airflow=wind;invalid=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@2@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+unknown>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        [TestCase("fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@2@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze|visibility:smoke+mist>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none")]
        public void ParseEnvironmentProfilesRejectsInvalidProfileReferencesBeforeImport(string row)
        {
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header, row },
                "EnvironmentProfiles.csv"));
        }

        [Test]
        public void ParseEnvironmentProfilesRejectsRowsWithMissingRequiredFields()
        {
            const string missingElementRelations =
                "fixture_profile,unitsPerRange=2;maxQueryRange=16;edges=0:0>1:0@2@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze";

            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header, missingElementRelations },
                "EnvironmentProfiles.csv"));
        }

        private const string GuanzhongWildRow =
            "env_guanzhong_wild,unitsPerRange=2;maxQueryRange=24,0:0@0@0|0:1@0@0|0:2@0@0|0:3@0@0|0:4@0@0|0:5@0@0|0:6@0@0|0:7@0@0|0:8@0@0|0:9@0@0|1:0@0@0|1:1@0@0|1:2@0@0|1:3@1@0|1:4@0@0|1:5@0@0|1:6@0@0|1:7@0@0|1:8@0@0|1:9@0@0|2:0@0@0|2:1@1@0|2:2@0@0|2:3@0@0|2:4@1@0|2:5@0@0|2:6@0@0|2:7@0@0|2:8@1@0|2:9@0@0|3:0@0@0|3:1@1@0|3:2@0@0|3:3@0@0|3:4@1@0|3:5@0@0|3:6@0@0|3:7@0@0|3:8@1@0|3:9@0@0|4:0@0@0|4:1@0@0|4:2@1@0|4:3@1@0|4:4@0@0|4:5@0@0|4:6@0@0|4:7@0@0|4:8@0@0|4:9@0@0|5:0@0@0|5:1@0@0|5:2@1@0|5:3@0@0|5:4@0@0|5:5@0@0|5:6@1@0|5:7@0@0|5:8@0@0|5:9@0@0|6:0@0@0|6:1@0@0|6:2@0@0|6:3@0@0|6:4@0@0|6:5@1@0|6:6@1@0|6:7@0@0|6:8@0@0|6:9@0@0|7:0@0@0|7:1@0@0|7:2@0@0|7:3@0@0|7:4@0@0|7:5@0@0|7:6@0@0|7:7@0@0|7:8@0@0|7:9@0@0|8:0@0@0|8:1@0@0|8:2@0@0|8:3@0@0|8:4@1@0|8:5@1@0|8:6@0@0|8:7@0@0|8:8@0@0|8:9@0@0|9:0@0@0|9:1@0@0|9:2@0@0|9:3@0@0|9:4@0@0|9:5@0@0|9:6@0@0|9:7@0@0|9:8@0@0|9:9@0@0,surface_grassland|surface_loess,airflow=wind+gust;visibility=mist+haze;temperature=heat+cold;precipitation=rain+drizzle;suspendedHazard=ash+dust;cloudDischarge=storm+lightning,airflow:wind+gust>gust|visibility:mist+haze>haze|temperature:heat+cold>cold|precipitation:rain+drizzle>drizzle|suspendedHazard:ash+dust>ash|cloudDischarge:storm+lightning>lightning,element_wood|element_fire|element_earth|element_metal|element_water,ordinary,none";

        [Test]
        public void ClearanceLimitsProjectToOnlyTheDeclaredDirection()
        {
            string row = ValidRow.Substring(0, ValidRow.LastIndexOf(',') + 1) + "0:0>1:0@1";
            var definition = WorldContentImporter.ParseEnvironmentProfiles(new[] { Header, row }, "fixture")[0];
            var asset = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
            try
            {
                asset.Apply(definition);
                Assert.IsTrue(asset.TryCreateDefinition(out var projected, out string reason), reason);
                Assert.IsTrue(EnvironmentProfileRuntime.TryCreate(projected, out var runtime, out reason), reason);
                Assert.AreEqual(1, runtime.DirectedEdges.Single(edge => edge.fromQ == 0).clearanceLimit);
                Assert.AreEqual(0, runtime.DirectedEdges.Single(edge => edge.fromQ == 1).clearanceLimit);
                definition.directedEdges[0].clearanceLimit = -1;
                Assert.IsFalse(EnvironmentProfileRuntime.TryCreate(definition, out _, out reason));
                Assert.AreEqual("clearance_limit_invalid", reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [TestCase("")]
        [TestCase("None")]
        [TestCase("0:0>1:0@0")]
        [TestCase("0:0>1:0@-1")]
        [TestCase("0:0>1:0@2147483648")]
        [TestCase("0:0>1:0@1|0:0>1:0@2")]
        [TestCase("0:0>2:0@1")]
        [TestCase("0:0>0:0@1")]
        [TestCase("0:0>2147483648:0@1")]
        [TestCase("0:0>1:0")]
        [TestCase("0:0>1:0@1|")]
        public void ClearanceInvalidEntriesFailBeforeAssetImport(string value)
        {
            string row = ValidRow.Substring(0, ValidRow.LastIndexOf(',') + 1) + value;
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(new[] { Header, row }, "fixture"));
        }

        [Test]
        public void ClearanceColumnIsRequiredAndUnknownColumnsFail()
        {
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header.Replace(",edgeClearanceLimits", ""), ValidRow }, "fixture"));
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header + ",unknown", ValidRow + ",1" }, "fixture"));
        }

        [Test]
        public void GuanzhongWildProductionProfileCsvAndAssetRemainSynchronized()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv");
            var expectedProfiles = WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header, GuanzhongWildRow },
                "EnvironmentProfiles.csv");
            var actualProfiles = WorldContentImporter.ParseEnvironmentProfiles(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath);

            Assert.AreEqual(1, expectedProfiles.Length);
            Assert.AreEqual(1, actualProfiles.Length);
            Assert.AreEqual(100, expectedProfiles[0].battlefieldCells.Length);
            Assert.AreEqual(15, expectedProfiles[0].battlefieldCells.Count(item => item.blocksGroundMove));
            Assert.IsTrue(expectedProfiles[0].battlefieldCells
                .Where(item => item.blocksGroundMove)
                .All(item => !item.blocksLineOfSight));
            Assert.AreEqual(522, expectedProfiles[0].directedEdges.Length);
            foreach (EnvironmentDirectedEdge edge in expectedProfiles[0].directedEdges)
            {
                Assert.IsTrue(expectedProfiles[0].directedEdges.Any(reverse =>
                    reverse.fromQ == edge.toQ &&
                    reverse.fromR == edge.toR &&
                    reverse.toQ == edge.fromQ &&
                    reverse.toR == edge.fromR));
            }
            AssertEnvironmentProfileEquals(expectedProfiles[0], actualProfiles[0]);

            const string assetPath =
                "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset";
            var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(assetPath);
            Assert.IsNotNull(asset, $"Missing generated environment asset at {assetPath}.");
            AssertEnvironmentProfileEquals(expectedProfiles[0], asset);
        }

        [TestCase("ordinary", SpiritRecoveryEnvironment.Ordinary)]
        [TestCase("spirit_vein_node", SpiritRecoveryEnvironment.SpiritVeinNode)]
        [TestCase("blessed_land", SpiritRecoveryEnvironment.BlessedLand)]
        public void ExplicitRecoveryEnvironmentProjectsThroughAssetAndRuntime(string value, SpiritRecoveryEnvironment expected)
        {
            string row = ValidRow.Replace(",ordinary,", "," + value + ",");
            var definition = WorldContentImporter.ParseEnvironmentProfiles(new[] { Header, row }, "fixture")[0];
            var asset = ScriptableObject.CreateInstance<EnvironmentProfileAsset>();
            try
            {
                asset.Apply(definition);
                Assert.AreEqual(expected, asset.spiritRecoveryEnvironment);
                Assert.IsTrue(asset.TryCreateDefinition(out var projected, out string reason), reason);
                Assert.IsTrue(EnvironmentProfileRuntime.TryCreate(projected, out var runtime, out reason), reason);
                Assert.AreEqual(expected, runtime.SpiritRecoveryEnvironment);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [TestCase("")]
        [TestCase("unknown")]
        [TestCase("Ordinary")]
        [TestCase("spirit_vein")]
        public void RecoveryEnvironmentMissingOrUnknownFailsBeforeImport(string value)
        {
            string row = ValidRow.Replace(",ordinary,", "," + value + ",");
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(new[] { Header, row }, "fixture"));
        }

        [Test]
        public void RecoveryEnvironmentColumnCannotBeOmitted()
        {
            Assert.Throws<InvalidDataException>(() => WorldContentImporter.ParseEnvironmentProfiles(
                new[] { Header.Replace(",spiritRecoveryEnvironment", ""), ValidRow.Replace(",ordinary,", ",") }, "fixture"));
        }

        [TestCase(SpiritRecoveryEnvironment.Unknown)]
        [TestCase((SpiritRecoveryEnvironment)99)]
        public void RuntimeRejectsUnconfiguredRecoveryEnvironment(SpiritRecoveryEnvironment value)
        {
            var definition = WorldContentImporter.ParseEnvironmentProfiles(new[] { Header, ValidRow }, "fixture")[0];
            definition.spiritRecoveryEnvironment = value;
            Assert.IsFalse(EnvironmentProfileRuntime.TryCreate(definition, out _, out string reason));
            Assert.AreEqual("spirit_recovery_environment_not_configured", reason);
        }

        private static void AssertEnvironmentProfileEquals(EnvironmentProfileDefinition expected, EnvironmentProfileDefinition actual)
        {
            Assert.AreEqual(expected.profileId, actual.profileId);
            Assert.AreEqual(expected.spiritRecoveryEnvironment, actual.spiritRecoveryEnvironment);
            Assert.AreEqual(expected.unitsPerRange, actual.unitsPerRange);
            Assert.AreEqual(expected.maxQueryRange, actual.maxQueryRange);

            Assert.AreEqual(expected.battlefieldCells.Length, actual.battlefieldCells.Length);
            for (int index = 0; index < expected.battlefieldCells.Length; index++)
            {
                Assert.AreEqual(expected.battlefieldCells[index].q, actual.battlefieldCells[index].q);
                Assert.AreEqual(expected.battlefieldCells[index].r, actual.battlefieldCells[index].r);
                Assert.AreEqual(expected.battlefieldCells[index].blocksGroundMove,
                    actual.battlefieldCells[index].blocksGroundMove);
                Assert.AreEqual(expected.battlefieldCells[index].blocksLineOfSight,
                    actual.battlefieldCells[index].blocksLineOfSight);
            }

            Assert.AreEqual(expected.directedEdges.Length, actual.directedEdges.Length);
            for (int index = 0; index < expected.directedEdges.Length; index++)
            {
                Assert.AreEqual(expected.directedEdges[index].fromQ, actual.directedEdges[index].fromQ);
                Assert.AreEqual(expected.directedEdges[index].fromR, actual.directedEdges[index].fromR);
                Assert.AreEqual(expected.directedEdges[index].toQ, actual.directedEdges[index].toQ);
                Assert.AreEqual(expected.directedEdges[index].toR, actual.directedEdges[index].toR);
                Assert.AreEqual(expected.directedEdges[index].metricDistanceUnits, actual.directedEdges[index].metricDistanceUnits);
                Assert.AreEqual(expected.directedEdges[index].allowsMovement, actual.directedEdges[index].allowsMovement);
                Assert.AreEqual(expected.directedEdges[index].allowsEffects, actual.directedEdges[index].allowsEffects);
                Assert.AreEqual(expected.directedEdges[index].clearanceLimit, actual.directedEdges[index].clearanceLimit);
            }

            CollectionAssert.AreEqual(expected.surfacePrototypeRefs, actual.surfacePrototypeRefs);

            Assert.AreEqual(expected.phenomenonChannels.Length, actual.phenomenonChannels.Length);
            for (int index = 0; index < expected.phenomenonChannels.Length; index++)
            {
                Assert.AreEqual(expected.phenomenonChannels[index].channel, actual.phenomenonChannels[index].channel);
                CollectionAssert.AreEqual(
                    expected.phenomenonChannels[index].phenomenonTypeRefs,
                    actual.phenomenonChannels[index].phenomenonTypeRefs);
            }

            Assert.AreEqual(expected.phenomenonPairs.Length, actual.phenomenonPairs.Length);
            for (int index = 0; index < expected.phenomenonPairs.Length; index++)
            {
                Assert.AreEqual(expected.phenomenonPairs[index].channel, actual.phenomenonPairs[index].channel);
                Assert.AreEqual(expected.phenomenonPairs[index].firstTypeRef, actual.phenomenonPairs[index].firstTypeRef);
                Assert.AreEqual(expected.phenomenonPairs[index].secondTypeRef, actual.phenomenonPairs[index].secondTypeRef);
                Assert.AreEqual(expected.phenomenonPairs[index].resultTypeRef, actual.phenomenonPairs[index].resultTypeRef);
            }

            CollectionAssert.AreEqual(expected.elementRelationRefs, actual.elementRelationRefs);
        }

        private static void AssertEnvironmentProfileEquals(EnvironmentProfileDefinition expected, EnvironmentProfileAsset actual)
        {
            Assert.IsTrue(actual.TryCreateDefinition(out var definition, out var reason), reason);
            AssertEnvironmentProfileEquals(expected, definition);
        }

        [Test]
        public void ImportEnvironmentProfilesRejectsInvalidRowsBeforeCreatingAssets()
        {
            const string sourceAssetPath = "Assets/DataConfig/EnvironmentProfiles.csv";
            const string importedAssetPath = "Assets/Data/EnvironmentProfiles/EnvironmentProfile_fixture_invalid.asset";
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv");
            byte[] originalContents = File.ReadAllBytes(sourceFilePath);

            try
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllText(
                    sourceFilePath,
                    Header + "\n" +
                    "fixture_invalid,unitsPerRange=2;maxQueryRange=16;edges=0:0>2:0@2@1@1,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water,ordinary\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                Assert.Throws<InvalidDataException>(() => WorldContentImporter.ImportEnvironmentProfiles());
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<EnvironmentProfileAsset>(importedAssetPath));
            }
            finally
            {
                AssetDatabase.DeleteAsset(importedAssetPath);
                File.WriteAllBytes(sourceFilePath, originalContents);
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }
    }
}
