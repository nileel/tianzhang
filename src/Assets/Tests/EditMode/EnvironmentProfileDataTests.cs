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
