using System;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Core;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    public class AbilityRequirementPolicyTests
    {
        [Test]
        public void RuntimeGateAllowsAndRejectsRealmAndElementRequirements()
        {
            var policyType = typeof(SpellData).Assembly.GetType("TianZhang.Combat.AbilityRequirementPolicy");
            Assert.IsNotNull(policyType, "TQ-059 requires a runtime ability requirement policy.");

            var method = policyType.GetMethod(
                "IsSatisfied",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Character), typeof(string), typeof(string) },
                null);
            Assert.IsNotNull(method, "The runtime policy must accept character, realmReq, and elementReq.");

            var visibleRootElement = typeof(Character).GetField("VisibleRootElement");
            Assert.IsNotNull(visibleRootElement, "Character must project the existing visible-root element at runtime.");

            var character = new Character();
            character.SetRealm("凡人");
            character.RealmMultiplier = 6f;
            visibleRootElement.SetValue(character, "水");

            Assert.IsTrue(InvokePolicy(method, character, "realm_zhuji", "element_water_root"));
            Assert.IsTrue(InvokePolicy(method, character, "realm_jindan", "element_water_or_wind"));
            Assert.IsFalse(InvokePolicy(method, character, "realm_yuanying", "element_water_root"));
            Assert.IsFalse(InvokePolicy(method, character, "realm_zhuji", "element_fire_root"));
            Assert.IsFalse(InvokePolicy(method, character, "realm_unknown", "element_water_root"));
            Assert.IsFalse(InvokePolicy(method, character, "realm_zhuji", "element_unknown_root"));
        }

        [Test]
        public void CharacterFromDataCarriesExistingVisibleRootElement()
        {
            var visibleRootElement = typeof(Character).GetField("VisibleRootElement");
            Assert.IsNotNull(visibleRootElement);

            var data = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                data.visibleRootElement = "雷";
                data.realmStage = "筑基初期";
                var character = Character.FromData(data, new HexCoord(0, 0));

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

        private static bool InvokePolicy(MethodInfo method, Character character, string realmReq, string elementReq)
        {
            return (bool)method.Invoke(null, new object[] { character, realmReq, elementReq });
        }
    }
}
