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
            Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_zhuji", "element_water_root"));
            Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_jindan", "element_water_or_wind"));
            Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_yuanying", "element_water_root"));
            Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_zhuji", "element_fire_root"));
            Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_unknown", "element_water_root"));
            Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied("金丹", "水", "中品", "realm_zhuji", "element_unknown_root"));
        }

        [TestCase("凡品")]
        [TestCase("下品")]
        [TestCase("中品")]
        [TestCase("上品")]
        [TestCase("极品")]
        public void StarterArtsAllowEveryValidRootGrade(string grade)
        {
            Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied("练气", "雷", grade, "realm_lianqi", "element_none"));
        }

        [TestCase("", "水", "中品")]
        [TestCase("未知", "水", "中品")]
        [TestCase("凡人", "水", "中品")]
        [TestCase("练气", "水", "")]
        [TestCase("练气", "水", "上古")]
        [TestCase("练气", "水", "未知")]
        [TestCase("练气", "", "中品")]
        [TestCase("练气", "element_unknown", "中品")]
        [TestCase("练气", "garbage_water_root", "中品")]
        public void StarterArtsRejectMissingOrInvalidQualificationFacts(string realm, string element, string grade)
        {
            Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied(realm, element, grade, "realm_lianqi", "element_none"));
        }

        [Test]
        public void UnrestrictedStarterArtsAcceptAllTwelveFormalRootElements()
        {
            foreach (string element in new[] { "金", "木", "水", "火", "土", "风", "雷", "冰", "暗", "星", "毒", "混沌" })
                Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied("练气", element, "中品", "realm_lianqi", "element_none"), element);
        }

        [Test]
        public void CharacterDefinitionFeedsIdentityRatherThanDamageMultiplierToRequirementPolicy()
        {
            var data = ScriptableObject.CreateInstance<CharacterData>();
            data.unarmedAttackFamilyId = "body_human";
            try
            {
                data.visibleRootElement = "雷";
                data.visibleRootGrade = "下品";
                data.realmStage = "筑基";
                data.realmMultiplier = 1f;
                Assert.IsTrue(AbilityRequirementPolicy.IsSatisfied(data.realmStage, data.visibleRootElement,
                    data.visibleRootGrade, "realm_zhuji", "element_thunder_root"));
                data.realmStage = "凡人";
                data.realmMultiplier = 24f;
                Assert.IsFalse(AbilityRequirementPolicy.IsSatisfied(data.realmStage, data.visibleRootElement,
                    data.visibleRootGrade, "realm_lianqi", "element_none"));
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
