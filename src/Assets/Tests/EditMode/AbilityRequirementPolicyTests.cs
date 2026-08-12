using System;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Content;
using TianZhang.Entity;
using UnityEngine;

using TianZhang.Spatial;
using EntityCharacter = TianZhang.Entity.Character;

namespace TianZhang.Tests
{
    public class AbilityRequirementPolicyTests
    {
        [Test]
        public void RuntimeGateAllowsAndRejectsRealmAndElementRequirements()
        {
            var character = new EntityCharacter();
            character.SetRealm("凡人");
            character.RealmMultiplier = 6f;
            character.VisibleRootElement = "水";

            Assert.IsTrue(IsSatisfied(character, "realm_zhuji", "element_water_root"));
            Assert.IsTrue(IsSatisfied(character, "realm_jindan", "element_water_or_wind"));
            Assert.IsFalse(IsSatisfied(character, "realm_yuanying", "element_water_root"));
            Assert.IsFalse(IsSatisfied(character, "realm_zhuji", "element_fire_root"));
            Assert.IsFalse(IsSatisfied(character, "realm_unknown", "element_water_root"));
            Assert.IsFalse(IsSatisfied(character, "realm_zhuji", "element_unknown_root"));
        }

        [Test]
        public void CharacterFromDataCarriesExistingVisibleRootElement()
        {
            var visibleRootElement = typeof(EntityCharacter).GetField("VisibleRootElement");
            Assert.IsNotNull(visibleRootElement);

            var data = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                data.visibleRootElement = "雷";
                data.realmStage = "筑基初期";
                var character = EntityCharacter.FromData(data, new HexCoord(0, 0));

                Assert.AreEqual("雷", visibleRootElement.GetValue(character));
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

        private static bool IsSatisfied(EntityCharacter character, string realmRequirement, string elementRequirement)
        {
            return AbilityRequirementPolicy.IsSatisfied(
                character.RealmMultiplier,
                character.VisibleRootElement,
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
