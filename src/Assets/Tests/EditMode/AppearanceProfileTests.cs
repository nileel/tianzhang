using System;
using System.IO;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Infrastructure.UnityContent;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class AppearanceProfileTests
    {
        private ContentCatalogData catalog;
        private AppearanceProfileData none;
        private AppearanceProfileData duplicate;

        [TearDown]
        public void TearDown()
        {
            if (catalog != null) UnityEngine.Object.DestroyImmediate(catalog);
            if (none != null) UnityEngine.Object.DestroyImmediate(none);
            if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
        }

        [Test]
        public void ParserAcceptsOnlyTheSingleNoneProfile()
        {
            AppearanceProfileData[] parsed = CharacterContentImporter.ParseAppearanceProfileDefinitions(
                new[] { "appearanceProfileId", "none" },
                "AppearanceProfiles.fixture.csv");
            try
            {
                Assert.That(parsed, Has.Length.EqualTo(1));
                Assert.That(parsed[0].appearanceProfileId, Is.EqualTo(AppearanceProfileData.NoneId));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parsed[0]);
            }

            Assert.Throws<InvalidDataException>(() => CharacterContentImporter.ParseAppearanceProfileDefinitions(
                new[] { "appearanceProfileId", "none", "none" },
                "AppearanceProfiles.duplicate.csv"));
            Assert.Throws<InvalidDataException>(() => CharacterContentImporter.ParseAppearanceProfileDefinitions(
                new[] { "appearanceProfileId", "fuyuan" },
                "AppearanceProfiles.unapproved.csv"));
        }

        [Test]
        public void CatalogRejectsDuplicateAndNonCatalogAppearanceIds()
        {
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            none = CreateProfile(AppearanceProfileData.NoneId);
            duplicate = CreateProfile(AppearanceProfileData.NoneId);
            catalog.SetAppearanceProfiles(new[] { none, duplicate });

            Assert.IsFalse(catalog.TryGetAppearanceProfile(AppearanceProfileData.NoneId, out _));

            catalog.SetAppearanceProfiles(new[] { none });
            Assert.IsTrue(catalog.TryGetAppearanceProfile(AppearanceProfileData.NoneId, out var resolved));
            Assert.AreSame(none, resolved);
            Assert.IsFalse(catalog.TryGetAppearanceProfile("appearance_not_cataloged", out _));
        }

        [Test]
        public void BattleAndPortraitConsumersResolveTheSameStableNoneProfileWithoutPresentation()
        {
            catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            none = CreateProfile(AppearanceProfileData.NoneId);
            catalog.SetAppearanceProfiles(new[] { none });

            Assert.IsTrue(CharacterVisualAssembler.TryResolveAppearance(catalog, AppearanceProfileData.NoneId, out var battle));
            Assert.IsTrue(PortraitComposer.TryResolveAppearance(catalog, AppearanceProfileData.NoneId, out var portrait));
            Assert.AreSame(battle, portrait);
            Assert.IsFalse(CharacterVisualAssembler.TryAssemble(catalog, AppearanceProfileData.NoneId, out _));
            Assert.IsFalse(PortraitComposer.TryCompose(catalog, AppearanceProfileData.NoneId, out _));
        }

        private static AppearanceProfileData CreateProfile(string id)
        {
            var profile = ScriptableObject.CreateInstance<AppearanceProfileData>();
            profile.appearanceProfileId = id;
            return profile;
        }
    }
}
