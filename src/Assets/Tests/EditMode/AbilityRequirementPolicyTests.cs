using System;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public class AbilityRequirementPolicyTests
    {
        [Test]
        public void RequirementPolicyAllowsAndRejectsExplicitRealmAndElementInputs()
        {
            Assert.IsTrue(IsSatisfied(6f, "水", "realm_zhuji", "element_water_root"));
            Assert.IsTrue(IsSatisfied(6f, "水", "realm_jindan", "element_water_or_wind"));
            Assert.IsFalse(IsSatisfied(6f, "水", "realm_yuanying", "element_water_root"));
            Assert.IsFalse(IsSatisfied(6f, "水", "realm_zhuji", "element_fire_root"));
            Assert.IsFalse(IsSatisfied(6f, "水", "realm_unknown", "element_water_root"));
            Assert.IsFalse(IsSatisfied(6f, "水", "realm_zhuji", "element_unknown_root"));
        }

        [Test]
        public void CharacterDefinitionFeedsVisibleRootElementToRequirementPolicy()
        {
            var data = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                data.visibleRootElement = "雷";
                data.realmMultiplier = 3f;

                Assert.IsTrue(IsSatisfied(
                    data.realmMultiplier,
                    data.visibleRootElement,
                    "realm_zhuji",
                    "element_thunder_root"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void AbilityAssetsUseRuntimeRequirementsAndSourceOnlyAffiliationMetadata()
        {
            Assert.IsNotNull(typeof(SpellData).GetField("realmRequirement"));
            Assert.IsNotNull(typeof(SpellData).GetField("elementRequirement"));
            Assert.IsNotNull(typeof(SpellData).GetField("sourceAffiliation"));
            Assert.IsNull(typeof(SpellData).GetField("affiliation"));

            Assert.IsNotNull(typeof(DivineSkillData).GetField("realmRequirement"));
            Assert.IsNotNull(typeof(DivineSkillData).GetField("sourceAffiliation"));
            Assert.IsNull(typeof(DivineSkillData).GetField("affiliation"));
        }

        private static bool IsSatisfied(
            float realmMultiplier,
            string visibleRootElement,
            string realmRequirement,
            string elementRequirement)
        {
            return AbilityRequirementPolicy.IsSatisfied(
                realmMultiplier,
                visibleRootElement,
                realmRequirement,
                elementRequirement);
        }

        [Test]
        public void ElementFactsPreserveStableIdsNamesAndUnknownFallbacks()
        {
            Assert.AreEqual("雷", CombatElementFacts.ResolveGongFaElement("gongfa_jiuxiaoleijie"));
            Assert.AreEqual("土", CombatElementFacts.ResolveGongFaElement("含弘光大典"));
            Assert.AreEqual("水", CombatElementFacts.ResolveElement("element_water_root"));
            Assert.AreEqual("毒", CombatElementFacts.ResolveElement("element_toxin"));
            Assert.AreEqual(string.Empty, CombatElementFacts.ResolveGongFaElement("gongfa_unknown"));
            Assert.AreEqual(string.Empty, CombatElementFacts.ResolveElement("element_unknown"));
        }
    }
}
