using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Editor;
using TianZhang.Entity;
using TianZhang.Features.Adventure;
using UnityEditor;

namespace TianZhang.Tests.EditMode
{
    public sealed class D9SpaceContentTests
    {
        [Test]
        public void ImportedThirdSegmentUsesFrozenGateAndExistingStoneStrike()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<JindanDemoCatalogData>(JindanDemoContentImporter.CatalogPath);
            Assert.IsTrue(catalog.Validate(out string reason), reason);
            var spawned = JindanRehearsalFactory.Create(catalog, 2, "EARTH_GRAVITY", key => key);
            Assert.AreEqual(2, spawned.Players.Concat(spawned.Enemies).Count());
            CollectionAssert.AreEqual(new[] { "spell_guanshiji" }, spawned.Player.EquippedArtProfileIds);
            Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied(spawned.Player.RealmStage,
                spawned.Player.VisibleRootElement, spawned.Player.VisibleRootGrade, "realm_lianqi", "element_none"));
            var loadout = JindanRehearsalFactory.CreateLoadout(catalog, spawned, "EARTH_GRAVITY");
            var ability = loadout.Ability("SPACE_SEAL");
            Assert.IsTrue(ability.Enabled); Assert.AreEqual(200, ability.SupportDurability);
            Assert.AreEqual(6, ability.Seal.DurationTicks); Assert.AreEqual(2, ability.Seal.LocalRadius);
            Assert.AreEqual(100, ability.Seal.LayerDurability);
        }

        [Test]
        public void DirectionAndLayerContractChangesInvalidateCompilation()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions();
            try
            {
                var contract = catalog.contracts.Single(value => value.effectId == "SPACE_SEAL");
                string initial = JindanDemoCompatibilityCompiler.Compile(catalog, contract);
                catalog.sealRules.brokenLayerTicks++;
                Assert.AreNotEqual(initial, JindanDemoCompatibilityCompiler.Compile(catalog, contract));
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out _));
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }
    }
}
