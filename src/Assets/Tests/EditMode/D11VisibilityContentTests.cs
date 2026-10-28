using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.Editor;
using TianZhang.Entity;

namespace TianZhang.Tests.EditMode
{
    public sealed class D11VisibilityContentTests
    {
        [Test]
        public void BothDomainsUseIndependentSupportsAndEqualPaidTerms()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions();
            try
            {
                Assert.IsTrue(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), reason);
                var rules = catalog.visibilityRules;
                Assert.AreEqual("reveal_support", rules.yangSupportId);
                Assert.AreEqual("conceal_support", rules.yinSupportId);
                Assert.AreNotEqual(rules.yangSupportId, rules.yinSupportId);
                Assert.AreEqual(200, rules.conflictReserve);
                Assert.AreEqual(50, rules.pulseMana);
                foreach (string effect in new[] { "YANG_RADIANCE", "YIN_CONCEAL" })
                {
                    var deploy = catalog.commands.Single(value => value.effectId == effect && value.kind == "DEPLOY");
                    var close = catalog.commands.Single(value => value.effectId == effect && value.kind == "TERMINATE");
                    Assert.IsTrue(deploy.enabled); Assert.IsTrue(close.enabled);
                    Assert.AreEqual(250000, deploy.actionPointCostUnits); Assert.AreEqual(400, deploy.manaCost);
                    Assert.AreEqual(600, deploy.supportDurability); Assert.AreEqual(40, deploy.upkeepMana);
                    Assert.AreEqual("GLOBAL_TICK", deploy.upkeepClock);
                    Assert.AreEqual("GLOBAL_TICK", deploy.durationClock); Assert.AreEqual(6, deploy.durationCount);
                    Assert.AreEqual(25000, close.actionPointCostUnits); Assert.AreEqual(0, close.manaCost);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        [TestCase("conflictReserve")]
        [TestCase("pulseMana")]
        [TestCase("windowCloseMilliseconds")]
        [TestCase("gaugeStep")]
        [TestCase("supportRank")]
        [TestCase("yangWindowBudget")]
        [TestCase("yinQ")]
        public void ChangedVisibilityTermsInvalidateBothContractsButNotSpace(string field)
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions();
            try
            {
                var yang = catalog.contracts.Single(value => value.effectId == "YANG_RADIANCE");
                var yin = catalog.contracts.Single(value => value.effectId == "YIN_CONCEAL");
                var space = catalog.contracts.Single(value => value.effectId == "SPACE_SCALE");
                string initialYang = yang.compatibilityProfileId, initialYin = yin.compatibilityProfileId;
                string initialSpace = space.compatibilityProfileId;
                var member = typeof(JindanDemoVisibilityRules).GetField(field);
                member.SetValue(catalog.visibilityRules, (int)member.GetValue(catalog.visibilityRules) + 1);
                Assert.AreNotEqual(initialYang, JindanDemoCompatibilityCompiler.Compile(catalog, yang));
                Assert.AreNotEqual(initialYin, JindanDemoCompatibilityCompiler.Compile(catalog, yin));
                Assert.AreEqual(initialSpace, JindanDemoCompatibilityCompiler.Compile(catalog, space));
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out _));
                Recompile(catalog);
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out string reason));
                Assert.AreEqual("JD_DEMO_VISIBILITY_RULES", reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void RebindingKeepsResourceRulesAndRequiresDistinctNonemptyKeys()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions();
            try
            {
                catalog.visibilityRules.holdKey = "LeftShift";
                catalog.visibilityRules.firstKey = "J";
                catalog.visibilityRules.secondKey = "L";
                Recompile(catalog);
                Assert.IsTrue(JindanDemoCatalogValidator.Validate(catalog, false, out string reason), reason);
                catalog.visibilityRules.secondKey = "j";
                Recompile(catalog);
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out reason));
                Assert.AreEqual("JD_DEMO_VISIBILITY_INPUT_KEYS", reason);
                catalog.visibilityRules.secondKey = "";
                Recompile(catalog);
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out reason));
                Assert.AreEqual("JD_DEMO_VISIBILITY_INPUT_KEYS", reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void MissingConfigAndUnequalDeploymentCannotEnterTheCatalog()
        {
            var catalog = JindanDemoContentImporter.ReadDefinitions();
            try
            {
                var command = catalog.commands.Single(value => value.effectId == "YIN_CONCEAL" && value.kind == "DEPLOY");
                command.manaCost = 450;
                Recompile(catalog);
                Assert.IsFalse(JindanDemoCatalogValidator.Validate(catalog, false, out string reason));
                Assert.AreEqual("JD_DEMO_VISIBILITY_COMMAND", reason);
                command.manaCost = 400;
                catalog.visibilityRules = null;
                Assert.Throws<System.IO.InvalidDataException>(() => JindanDemoCompatibilityCompiler.Compile(catalog,
                    catalog.contracts.Single(value => value.effectId == "YANG_RADIANCE")));
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        private static void Recompile(JindanDemoCatalogData catalog)
        {
            foreach (var contract in catalog.contracts)
                contract.compatibilityProfileId = JindanDemoCompatibilityCompiler.Compile(catalog, contract);
        }
    }
}
