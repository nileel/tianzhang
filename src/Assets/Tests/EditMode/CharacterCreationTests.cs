using NUnit.Framework;
using System.Linq;
using TianZhang.Character;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

namespace TianZhang.Tests
{
    public class CharacterCreationRuleTests
    {
        private static TianZhang.Content.CombatRuleSetData GrowthRules
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.AssetDatabase.LoadAssetAtPath<TianZhang.Content.CombatRuleSetData>("Assets/Data/CombatRuleSet.asset");
#else
                throw new System.NotSupportedException("Editor fixture requires the production rule asset.");
#endif
            }
        }

        private CharacterCreationPointBuyConfig pointBuyConfig;
        private GameObject creationPanel;
        private CharacterCreationController creationController;
        private CharacterData submittedProfile;
        private string submittedSlotId;
        private string submittedStartNodeId;
        private int submissionCount;

        [SetUp]
        public void SetUp()
        {
            pointBuyConfig = CreatePointBuyConfig();
        }

        [TearDown]
        public void TearDown()
        {
            if (creationPanel != null) Object.DestroyImmediate(creationPanel);
            if (submittedProfile != null) Object.DestroyImmediate(submittedProfile);
            Object.DestroyImmediate(pointBuyConfig);
        }

        [Test]
        public void CreationControllerDefaultsToLooseOriginWithoutGrantingStarterArts()
        {
            ConfigureCreationController();
            creationController.Open();

            Assert.AreEqual("origin_loose", creationController.Draft.OriginId);
            Assert.AreEqual("root_water_middle", creationController.Draft.VisibleSpiritRootId);
            creationController.Submit("default-origin", creationController.Draft.CharacterName);

            Assert.AreEqual(1, submissionCount);
            Assert.AreEqual("origin_loose", submittedProfile.originId);
            Assert.AreEqual("jiangzuo_hub", submittedStartNodeId);
            CollectionAssert.IsEmpty(submittedProfile.availableSpells);
            CollectionAssert.IsEmpty(submittedProfile.equippedSpells);
        }

        [TestCase("origin_loose", 0, 0, 10, "jiangzuo_hub")]
        [TestCase("origin_sect_servant", 1, 0, 9, "jiangzuo_hub")]
        [TestCase("origin_minor_clan", 2, 0, 8, "guanzhong_hub")]
        [TestCase("origin_debt_exile", 0, 2, 12, "jiangzuo_hub")]
        public void OriginSelectionAppliesExistingBudgetAndStartNodeWithoutReplacingDraft(
            string originId, int used, int refunded, int remaining, string startNodeId)
        {
            ConfigureCreationController();
            CharacterCreationDraft draft = creationController.Draft;
            draft.CharacterName = "保留姓名修士";
            InnateAttributeSet innate = draft.Innate;

            Assert.IsTrue(creationController.SelectOrigin(originId));

            Assert.AreSame(draft, creationController.Draft);
            Assert.AreSame(innate, draft.Innate);
            Assert.AreEqual(originId, draft.OriginId);
            Assert.AreEqual("保留姓名修士", draft.CharacterName);
            Assert.AreEqual("root_water_middle", draft.VisibleSpiritRootId);
            var validation = CharacterCreationRules.Validate(draft, pointBuyConfig);
            Assert.IsTrue(validation.IsValid, string.Join("|", validation.Errors));
            Assert.AreEqual(used, validation.BudgetUsed);
            Assert.AreEqual(refunded, validation.BudgetRefunded);
            Assert.AreEqual(remaining, validation.BudgetAvailable);
            creationController.Submit("选择出身存档", draft.CharacterName);

            Assert.AreEqual(1, submissionCount);
            Assert.AreEqual("选择出身存档", submittedSlotId);
            Assert.AreEqual("保留姓名修士", submittedProfile.charName);
            Assert.AreEqual(originId, submittedProfile.originId);
            Assert.AreEqual(used, submittedProfile.creationBudgetUsed);
            Assert.AreEqual(refunded, submittedProfile.creationBudgetRefunded);
            Assert.AreEqual(startNodeId, submittedStartNodeId);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("origin_unknown")]
        public void InvalidOriginSelectionLeavesEveryDraftFieldUntouched(string originId)
        {
            ConfigureCreationController();
            Assert.IsTrue(creationController.SelectOrigin("origin_minor_clan"));
            CharacterCreationDraft draft = creationController.Draft;
            draft.VisibleSpiritRootId = "root_thunder_middle";
            draft.HiddenRootSeedId = "hidden_variant_seed";
            draft.CharacterName = "草稿姓名";
            draft.FateTagIds.Add("fate_market_contact");

            Assert.IsFalse(creationController.SelectOrigin(originId));

            Assert.AreSame(draft, creationController.Draft);
            Assert.AreEqual("origin_minor_clan", draft.OriginId);
            Assert.AreEqual("root_thunder_middle", draft.VisibleSpiritRootId);
            Assert.AreEqual("hidden_variant_seed", draft.HiddenRootSeedId);
            Assert.AreEqual("草稿姓名", draft.CharacterName);
            CollectionAssert.AreEqual(new[] { 8, 8, 8, 8, 8 }, new[]
                { draft.Innate.RootBone, draft.Innate.Soul, draft.Innate.DivineSense, draft.Innate.Aptitude, draft.Innate.Fortune });
            CollectionAssert.AreEqual(new[] { "fate_market_contact" }, draft.FateTagIds);
            CollectionAssert.AreEqual(new[] { "craft_alchemy", "craft_talisman", "craft_herb" },
                draft.CraftSkills.Select(skill => skill.SkillId).ToArray());
            Assert.IsTrue(draft.CraftSkills.All(skill => skill.Level == 1));
            Assert.AreEqual(0, submissionCount);
        }

        [Test]
        public void BalancedInnateAttributesSpendAllPurchasePoints()
        {
            var innate = new InnateAttributeSet(8, 8, 8, 8, 8);

            Assert.AreEqual(25, innate.CalculatePurchaseCost(pointBuyConfig));

            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = innate;

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(25, result.InnatePurchasePointsUsed);
            Assert.AreEqual(0, result.InnatePurchasePointsRemaining);
        }

        [Test]
        public void ExtremeInnateSpreadCanSpendAllPurchasePointsWithoutFixedTotal()
        {
            var innate = new InnateAttributeSet(15, 6, 3, 3, 3);

            Assert.AreEqual(30, innate.Total);
            Assert.AreEqual(25, innate.CalculatePurchaseCost(pointBuyConfig));

            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = innate;

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(25, result.InnatePurchasePointsUsed);
        }

        [Test]
        public void ValidationRejectsInnateAboveCreationCap()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(16, 6, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "根骨必须在3到15之间。");
        }

        [Test]
        public void ValidationRejectsInnatePurchaseCostOverTwentyFive()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(15, 7, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "先天属性购买点数不能超过25。");
        }

        [Test]
        public void InnateValidationUsesProvidedPointBuyConfig()
        {
            pointBuyConfig.purchasePointLimit = 30;
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(15, 8, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(27, result.InnatePurchasePointsUsed);
            Assert.AreEqual(3, result.InnatePurchasePointsRemaining);
        }

        [Test]
        public void PointBuyConfigCanChangeTierCosts()
        {
            int defaultCost = pointBuyConfig.CalculateCost(15);
            pointBuyConfig.costRanges = new[]
            {
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 4, toValue = 8, costPerLevel = 1 },
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 9, toValue = 12, costPerLevel = 2 },
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 13, toValue = 15, costPerLevel = 4 },
            };

            Assert.AreEqual(22, defaultCost);
            Assert.AreEqual(25, pointBuyConfig.CalculateCost(15));
        }

        [Test]
        public void VisibleSpiritRootCostsUseCreationBudget()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();

            var defaultResult = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsTrue(defaultResult.IsValid, string.Join("|", defaultResult.Errors));
            Assert.AreEqual(0, defaultResult.VisibleRootBudgetCost);

            draft.VisibleSpiritRootId = "root_thunder_high";

            var highVariantResult = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsTrue(highVariantResult.IsValid, string.Join("|", highVariantResult.Errors));
            Assert.AreEqual(5, highVariantResult.VisibleRootBudgetCost);
            Assert.AreEqual(5, highVariantResult.BudgetUsed);
        }

        [Test]
        public void HiddenRootSeedIsDormantAndDoesNotChangeEffectiveRootState()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.HiddenRootSeedId = "hidden_variant_seed";

            var profile = CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig, GrowthRules);

            Assert.AreEqual("Dormant", profile.hiddenRootState);
            Assert.AreEqual("hidden_variant_seed", profile.hiddenRootSeedId);
            Assert.IsFalse(string.IsNullOrEmpty(profile.hiddenRootElement));
            Assert.AreEqual(profile.visibleRootCultivationMultiplier, CharacterCreationRules.ResolveEffectiveCultivationMultiplier(profile));
            CollectionAssert.AreEqual(
                profile.visibleRootLearnTags,
                CharacterCreationRules.ResolveEffectiveLearnTags(profile));
        }

        [Test]
        public void HiddenOrdinarySeedRejectsVisibleRootAboveMiddleGrade()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.VisibleSpiritRootId = "root_thunder_high";
            draft.HiddenRootSeedId = "hidden_ordinary_seed";

            var result = CharacterCreationRules.Validate(draft, pointBuyConfig);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "隐灵根种子最高只支持中品显性灵根。");
        }

        [Test]
        public void AwakenedHiddenRootJoinsEffectiveRootsAndUsesMultiRootFormula()
        {
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.unarmedAttackFamilyId = "body_human";
            profile.realmStage = "凡人";
            try
            {
                profile.visibleRootCultivationMultiplier = 1.0f;
                profile.visibleRootLearnTags = new[] { "element_water", "profession_talisman" };
                profile.hiddenRootState = "Awakened";
                profile.hiddenRootCultivationMultiplier = 2.0f;
                profile.hiddenRootLearnTags = new[] { "element_thunder", "profession_sword" };

                Assert.AreEqual(1.2f, CharacterCreationRules.ResolveEffectiveCultivationMultiplier(profile));
                CollectionAssert.AreEquivalent(
                    new[] { "element_water", "profession_talisman", "element_thunder", "profession_sword" },
                    CharacterCreationRules.ResolveEffectiveLearnTags(profile));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CharacterDataAndRuntimeProfilePreserveCreationAttributesWithoutTakingRootOwnership()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.CharacterName = "试炼修士";
            draft.Innate = new InnateAttributeSet(8, 8, 8, 8, 8);
            draft.VisibleSpiritRootId = "root_thunder_high";
            draft.HiddenRootSeedId = "hidden_variant_seed";
            draft.CraftSkills.Clear();
            draft.CraftSkills.Add(new CraftSkillAllocation("craft_alchemy", 2));
            draft.CraftSkills.Add(new CraftSkillAllocation("craft_talisman", 1));

            var profile = CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig, GrowthRules);
            CharacterRuntimeProfile runtime = CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules);

            Assert.AreEqual("试炼修士", profile.charName);
            Assert.AreEqual(25, profile.innatePurchasePointsUsed);
            Assert.AreEqual("root_thunder_high", profile.visibleRootId);
            Assert.AreEqual("雷", profile.visibleRootElement);
            Assert.AreEqual("金", profile.visibleRootMotherElement);
            Assert.AreEqual("Dormant", profile.hiddenRootState);
            Assert.AreEqual("hidden_variant_seed", profile.hiddenRootSeedId);
            CollectionAssert.AreEqual(new[] { "craft_alchemy", "craft_talisman" }, profile.craftSkillIds);
            CollectionAssert.AreEqual(new[] { 2, 1 }, profile.craftSkillLevels);
            Assert.AreEqual(profile.visibleRootCultivationMultiplier, CharacterCreationRules.ResolveEffectiveCultivationMultiplier(profile));
            Assert.AreEqual(profile.fortune, runtime.Attributes.Fortune);
        }

        [Test]
        public void NewPlayerProfileHasNoWeaponOrUnarmedBinding()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();

            var profile = CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig, GrowthRules);

            Assert.AreEqual(
                string.Empty,
                profile.unarmedBasicAttackProfileId);
            Assert.IsTrue(string.IsNullOrEmpty(profile.mainEquipmentBasicAttackProfileId));

            CharacterRuntimeProfile runtime = CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules);
            Assert.AreEqual(
                "basic_body_human_fanren",
                runtime.UnarmedBasicAttackProfileId);
            Assert.IsTrue(string.IsNullOrEmpty(runtime.MainEquipmentBasicAttackProfileId));
        }

        [Test]
        public void RuntimeProfileUsesExplicitCharacterDefinitionSlotLimits()
        {
            CharacterData profile = CharacterCreationRules.BuildCharacterData(
                CharacterCreationCatalog.CreateDefaultDraft(),
                pointBuyConfig, GrowthRules);
            profile.maxSpellSlots = 5;
            profile.maxSkillSlots = 2;
            profile.availableSpells = new[] { "spell_fixture" };
            profile.availableSkills = new[] { "skill_fixture" };
            profile.equippedSpells = new[] { "spell_fixture" };
            profile.equippedSkills = new[] { "skill_fixture" };

            CharacterRuntimeProfile runtime = CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules);

            Assert.AreEqual(5, runtime.AbilityLoadout.SpellSlots);
            Assert.AreEqual(2, runtime.AbilityLoadout.SkillSlots);
            CollectionAssert.AreEqual(new[] { "spell_fixture" }, runtime.AbilityLoadout.EquippedSpells);
            CollectionAssert.AreEqual(new[] { "skill_fixture" }, runtime.AbilityLoadout.EquippedSkills);
        }

        [Test]
        public void NewPlayerHasNoCreationTimeSectOrCultivationLoadout()
        {
            CharacterData profile = CharacterCreationRules.BuildCharacterData(
                CharacterCreationCatalog.CreateDefaultDraft(),
                pointBuyConfig, GrowthRules);

            Assert.IsTrue(string.IsNullOrEmpty(profile.gongFaName));
            CollectionAssert.IsEmpty(profile.equippedSpells);
            CollectionAssert.IsEmpty(profile.availableSpells);
        }

        [Test]
        public void MinorClanLearnsAndEquipsOnlyTheTwoApprovedStarterArts()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.OriginId = "origin_minor_clan";
            CharacterData profile = CharacterCreationRules.BuildCharacterData(draft, pointBuyConfig, GrowthRules);
            try
            {
                var expected = new[] { "spell_guanshiji", "spell_julingshu" };
                Assert.IsTrue(string.IsNullOrEmpty(profile.gongFaName));
                Assert.AreEqual(2, profile.maxSpellSlots);
                CollectionAssert.AreEqual(expected, profile.availableSpells);
                CollectionAssert.AreEqual(expected, profile.equippedSpells);
                CharacterRuntimeProfile runtime = CharacterRuntimeProfile.FromDefinition("player", profile, GrowthRules);
                CollectionAssert.AreEqual(expected, runtime.AbilityLoadout.KnownSpells);
                CollectionAssert.AreEqual(expected, runtime.AbilityLoadout.EquippedSpells);
                Assert.AreEqual("basic_body_human_fanren", runtime.UnarmedBasicAttackProfileId);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DormantHiddenRootDoesNotChangeRuntimeDerivedCombatStats()
        {
            var baselineDraft = CharacterCreationCatalog.CreateDefaultDraft();
            baselineDraft.CharacterName = "基线修士";
            var hiddenDraft = CharacterCreationCatalog.CreateDefaultDraft();
            hiddenDraft.CharacterName = "基线修士";
            hiddenDraft.HiddenRootSeedId = "hidden_variant_seed";

            CharacterData baselineData = CharacterCreationRules.BuildCharacterData(baselineDraft, pointBuyConfig, GrowthRules);
            CharacterData hiddenData = CharacterCreationRules.BuildCharacterData(hiddenDraft, pointBuyConfig, GrowthRules);
            CharacterRuntimeProfile baseline = CharacterRuntimeProfile.FromDefinition("baseline", baselineData, GrowthRules);
            CharacterRuntimeProfile withDormantHiddenRoot = CharacterRuntimeProfile.FromDefinition("hidden", hiddenData, GrowthRules);
            CharacterDerivedAttributes baselineDerived = baseline.Attributes.Derive(
                baseline.Progression.RealmMultiplier,
                CharacterAttributeBonuses.Empty);
            CharacterDerivedAttributes hiddenDerived = withDormantHiddenRoot.Attributes.Derive(
                withDormantHiddenRoot.Progression.RealmMultiplier,
                CharacterAttributeBonuses.Empty);

            Assert.AreEqual(baselineDerived.MaxHealth, hiddenDerived.MaxHealth);
            Assert.AreEqual(baselineDerived.MaxSpirit, hiddenDerived.MaxSpirit);
            Assert.AreEqual(baselineDerived.PhysicalAttack, hiddenDerived.PhysicalAttack);
            Assert.AreEqual(baselineDerived.MagicAttack, hiddenDerived.MagicAttack);
            Assert.AreEqual(baselineDerived.PhysicalDefense, hiddenDerived.PhysicalDefense);
            Assert.AreEqual(baselineDerived.MagicDefense, hiddenDerived.MagicDefense);
            Assert.AreEqual(baseline.Attributes.Reaction, withDormantHiddenRoot.Attributes.Reaction);
            Assert.AreEqual(baseline.AbilityLoadout.SpellSlots, withDormantHiddenRoot.AbilityLoadout.SpellSlots);
            Assert.AreEqual(baseline.AbilityLoadout.SkillSlots, withDormantHiddenRoot.AbilityLoadout.SkillSlots);
            Assert.AreEqual("Dormant", hiddenData.hiddenRootState);
        }

        [Test]
        public void MissingPointBuyConfigDoesNotDefault()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                CharacterCreationRules.Validate(CharacterCreationCatalog.CreateDefaultDraft(), null));
        }

        private void ConfigureCreationController()
        {
            creationPanel = new GameObject("CharacterCreationTestController");
            creationController = creationPanel.AddComponent<CharacterCreationController>();
            submissionCount = 0;
            creationController.Configure(null, pointBuyConfig, (slot, profile, start) =>
            {
                submissionCount++;
                submittedSlotId = slot;
                submittedProfile = profile;
                submittedStartNodeId = start;
            }, GrowthRules);
        }

        private static CharacterCreationPointBuyConfig CreatePointBuyConfig()
        {
            var config = ScriptableObject.CreateInstance<CharacterCreationPointBuyConfig>();
            config.purchasePointLimit = 25;
            config.minValue = 3;
            config.baseValue = 3;
            config.maxValue = 15;
            config.actionPointMaximumUnits = 100000;
            config.movementEfficiencyMilliUnits = 2000;
            config.costRanges = new[]
            {
                new CharacterCreationPointBuyConfig.CostRange
                    { fromValue = 4, toValue = 8, costPerLevel = 1 },
                new CharacterCreationPointBuyConfig.CostRange
                    { fromValue = 9, toValue = 12, costPerLevel = 2 },
                new CharacterCreationPointBuyConfig.CostRange
                    { fromValue = 13, toValue = 15, costPerLevel = 3 },
            };
            return config;
        }
    }
}
