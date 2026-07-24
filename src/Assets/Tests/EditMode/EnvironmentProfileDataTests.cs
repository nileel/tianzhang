using System;
using System.IO;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Tactical;
using UnityEditor;
using UnityEngine;

namespace TianZhang.Tests
{
    public class EnvironmentProfileDataTests
    {
        private const string Header =
            "profileId,directedEdges,surfacePrototypeRefs,phenomenonChannels,phenomenonPairs,elementRelationRefs";

        private const string ValidRow =
            "fixture_profile,0:0>1:0|1:0>1:-1,surface_wet|surface_ash,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:smoke+mist>haze,element_wood|element_fire|element_earth|element_metal|element_water";

        [Test]
        public void ParseEnvironmentProfilesBuildsOneDeterministicProfileFromAValidRow()
        {
            var profiles = DataConfigImporter.ParseEnvironmentProfiles(
                new[] { Header, ValidRow },
                "EnvironmentProfiles.csv");

            Assert.AreEqual(1, profiles.Length);
            var profile = profiles[0];
            Assert.AreEqual("fixture_profile", profile.profileId);
            Assert.AreEqual(2, profile.directedEdges.Length);
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

        [TestCase("fixture_profile,0:0>2:0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water")]
        [TestCase("fixture_profile,0:0>1:0,surface_wet,airflow=wind;invalid=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water")]
        [TestCase("fixture_profile,0:0>1:0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+unknown>haze,element_wood|element_fire|element_earth|element_metal|element_water")]
        [TestCase("fixture_profile,0:0>1:0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze|visibility:smoke+mist>haze,element_wood|element_fire|element_earth|element_metal|element_water")]
        public void ParseEnvironmentProfilesRejectsInvalidProfileReferencesBeforeImport(string row)
        {
            Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseEnvironmentProfiles(
                new[] { Header, row },
                "EnvironmentProfiles.csv"));
        }

        [Test]
        public void ParseEnvironmentProfilesRejectsRowsWithMissingRequiredFields()
        {
            const string missingElementRelations =
                "fixture_profile,0:0>1:0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze";

            Assert.Throws<InvalidDataException>(() => DataConfigImporter.ParseEnvironmentProfiles(
                new[] { Header, missingElementRelations },
                "EnvironmentProfiles.csv"));
        }

        private const string GuanzhongWildRow =
            "env_guanzhong_wild,-1:0>0:0|0:0>1:0|1:0>1:-1|0:0>0:1|0:1>-1:1|-1:0>-1:1,surface_grassland|surface_loess,airflow=wind+gust;visibility=mist+haze;temperature=heat+cold;precipitation=rain+drizzle;suspendedHazard=ash+dust;cloudDischarge=storm+lightning,airflow:wind+gust>gust|visibility:mist+haze>haze|temperature:heat+cold>cold|precipitation:rain+drizzle>drizzle|suspendedHazard:ash+dust>ash|cloudDischarge:storm+lightning>lightning,element_wood|element_fire|element_earth|element_metal|element_water";

        [Test]
        public void GuanzhongWildProductionProfileCsvAndAssetRemainSynchronized()
        {
            string sourceFilePath = Path.Combine(Application.dataPath, "DataConfig/EnvironmentProfiles.csv");
            var expectedProfiles = DataConfigImporter.ParseEnvironmentProfiles(
                new[] { Header, GuanzhongWildRow },
                "EnvironmentProfiles.csv");
            var actualProfiles = DataConfigImporter.ParseEnvironmentProfiles(
                File.ReadAllLines(sourceFilePath),
                sourceFilePath);

            try
            {
                Assert.AreEqual(1, expectedProfiles.Length);
                Assert.AreEqual(1, actualProfiles.Length);
                AssertEnvironmentProfileEquals(expectedProfiles[0], actualProfiles[0]);

                const string assetPath =
                    "Assets/Data/EnvironmentProfiles/EnvironmentProfile_env_guanzhong_wild.asset";
                var asset = AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(assetPath);
                Assert.IsNotNull(asset, $"Missing generated environment asset at {assetPath}.");
                AssertEnvironmentProfileEquals(expectedProfiles[0], asset);
            }
            finally
            {
                foreach (var profile in expectedProfiles)
                    UnityEngine.Object.DestroyImmediate(profile);
                foreach (var profile in actualProfiles)
                    UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static void AssertEnvironmentProfileEquals(EnvironmentProfileData expected, EnvironmentProfileData actual)
        {
            Assert.AreEqual(expected.profileId, actual.profileId);

            Assert.AreEqual(expected.directedEdges.Length, actual.directedEdges.Length);
            for (int index = 0; index < expected.directedEdges.Length; index++)
            {
                Assert.AreEqual(expected.directedEdges[index].fromQ, actual.directedEdges[index].fromQ);
                Assert.AreEqual(expected.directedEdges[index].fromR, actual.directedEdges[index].fromR);
                Assert.AreEqual(expected.directedEdges[index].toQ, actual.directedEdges[index].toQ);
                Assert.AreEqual(expected.directedEdges[index].toR, actual.directedEdges[index].toR);
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
                    "fixture_invalid,0:0>2:0,surface_wet,airflow=wind;visibility=mist+smoke+haze;temperature=heat;precipitation=rain;suspendedHazard=ash;cloudDischarge=storm,visibility:mist+smoke>haze,element_wood|element_fire|element_earth|element_metal|element_water\n");
                AssetDatabase.ImportAsset(sourceAssetPath, ImportAssetOptions.ForceSynchronousImport);

                Assert.Throws<InvalidDataException>(() => DataConfigImporter.ImportEnvironmentProfiles());
                Assert.IsNull(AssetDatabase.LoadAssetAtPath<EnvironmentProfileData>(importedAssetPath));
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
