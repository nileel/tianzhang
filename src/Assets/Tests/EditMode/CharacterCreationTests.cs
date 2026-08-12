using NUnit.Framework;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

using TianZhang.Spatial;
using EntityCharacter = TianZhang.Entity.Character;

namespace TianZhang.Tests
{
    public class CharacterCreationRuleTests
    {
        [Test]
        public void BalancedInnateAttributesSpendAllPurchasePoints()
        {
            var innate = new InnateAttributeSet(8, 8, 8, 8, 8);

            Assert.AreEqual(25, innate.PurchaseCost);

            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = innate;

            var result = CharacterCreationRules.Validate(draft);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(25, result.InnatePurchasePointsUsed);
            Assert.AreEqual(0, result.InnatePurchasePointsRemaining);
        }

        [Test]
        public void ExtremeInnateSpreadCanSpendAllPurchasePointsWithoutFixedTotal()
        {
            var innate = new InnateAttributeSet(15, 6, 3, 3, 3);

            Assert.AreEqual(30, innate.Total);
            Assert.AreEqual(25, innate.PurchaseCost);

            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = innate;

            var result = CharacterCreationRules.Validate(draft);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(25, result.InnatePurchasePointsUsed);
        }

        [Test]
        public void ValidationRejectsInnateAboveCreationCap()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(16, 6, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "根骨必须在3到15之间。");
        }

        [Test]
        public void ValidationRejectsInnatePurchaseCostOverTwentyFive()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(15, 7, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "先天属性购买点数不能超过25。");
        }

        [Test]
        public void InnateValidationUsesProvidedPointBuyConfig()
        {
            var config = CharacterCreationPointBuyConfig.CreateFallback();
            config.purchasePointLimit = 30;
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.Innate = new InnateAttributeSet(15, 8, 3, 3, 3);

            var result = CharacterCreationRules.Validate(draft, config);

            Assert.IsTrue(result.IsValid, string.Join("|", result.Errors));
            Assert.AreEqual(27, result.InnatePurchasePointsUsed);
            Assert.AreEqual(3, result.InnatePurchasePointsRemaining);
        }

        [Test]
        public void PointBuyConfigCanChangeTierCosts()
        {
            var config = CharacterCreationPointBuyConfig.CreateFallback();
            config.costRanges = new[]
            {
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 4, toValue = 8, costPerLevel = 1 },
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 9, toValue = 12, costPerLevel = 2 },
                new CharacterCreationPointBuyConfig.CostRange { fromValue = 13, toValue = 15, costPerLevel = 4 },
            };

            Assert.AreEqual(22, CharacterCreationPointBuyConfig.CreateFallback().CalculateCost(15));
            Assert.AreEqual(25, config.CalculateCost(15));
        }

        [Test]
        public void VisibleSpiritRootCostsUseCreationBudget()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();

            var defaultResult = CharacterCreationRules.Validate(draft);

            Assert.IsTrue(defaultResult.IsValid, string.Join("|", defaultResult.Errors));
            Assert.AreEqual(0, defaultResult.VisibleRootBudgetCost);

            draft.VisibleSpiritRootId = "root_thunder_high";

            var highVariantResult = CharacterCreationRules.Validate(draft);

            Assert.IsTrue(highVariantResult.IsValid, string.Join("|", highVariantResult.Errors));
            Assert.AreEqual(5, highVariantResult.VisibleRootBudgetCost);
            Assert.AreEqual(5, highVariantResult.BudgetUsed);
        }

        [Test]
        public void HiddenRootSeedIsDormantAndDoesNotChangeEffectiveRootState()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.HiddenRootSeedId = "hidden_variant_seed";

            var profile = CharacterCreationRules.BuildCharacterData(draft);

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

            var result = CharacterCreationRules.Validate(draft);

            Assert.IsFalse(result.IsValid);
            CollectionAssert.Contains(result.Errors, "隐灵根种子最高只支持中品显性灵根。");
        }

        [Test]
        public void AwakenedHiddenRootJoinsEffectiveRootsAndUsesMultiRootFormula()
        {
            var profile = ScriptableObject.CreateInstance<CharacterData>();
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
        public void CharacterDataAndRuntimeCharacterPreserveCreationMetadataWithoutDormantRootCombatEffect()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();
            draft.CharacterName = "试炼修士";
            draft.Innate = new InnateAttributeSet(8, 8, 8, 8, 8);
            draft.VisibleSpiritRootId = "root_thunder_high";
            draft.HiddenRootSeedId = "hidden_variant_seed";
            draft.CraftSkills.Clear();
            draft.CraftSkills.Add(new CraftSkillAllocation("craft_alchemy", 2));
            draft.CraftSkills.Add(new CraftSkillAllocation("craft_talisman", 1));

            var profile = CharacterCreationRules.BuildCharacterData(draft);
            var character = EntityCharacter.FromData(profile, new TianZhang.Spatial.HexCoord(0, 0));

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
            Assert.AreEqual(profile.fortune, character.Fortune);
            Assert.AreEqual(profile.visibleRootId, character.VisibleRootId);
            Assert.AreEqual("Dormant", character.HiddenRootState);
        }

        [Test]
        public void NewPlayerProfileExplicitlyBindsTheBasicUnarmedFallbackProfile()
        {
            var draft = CharacterCreationCatalog.CreateDefaultDraft();

            var profile = CharacterCreationRules.BuildCharacterData(draft);

            Assert.AreEqual(
                CharacterCreationCatalog.BasicUnarmedAttackProfileId,
                profile.unarmedBasicAttackProfileId);
            Assert.IsTrue(string.IsNullOrEmpty(profile.mainEquipmentBasicAttackProfileId));

            var character = EntityCharacter.FromData(profile, new TianZhang.Spatial.HexCoord(0, 0));
            Assert.AreEqual(
                CharacterCreationCatalog.BasicUnarmedAttackProfileId,
                character.BasicAttackProfileId);
            Assert.AreEqual("unarmed_fallback", character.BasicAttackBindingKind);
        }

        [Test]
        public void NewPlayerHasNoCreationTimeSectOrCultivationLoadout()
        {
            CharacterData profile = CharacterCreationRules.BuildCharacterData(
                CharacterCreationCatalog.CreateDefaultDraft());

            Assert.IsTrue(string.IsNullOrEmpty(profile.gongFaName));
            CollectionAssert.IsEmpty(profile.equippedSpells);
            CollectionAssert.IsEmpty(profile.availableSpells);
        }

        [Test]
        public void DormantHiddenRootDoesNotChangeRuntimeDerivedCombatStats()
        {
            var baselineDraft = CharacterCreationCatalog.CreateDefaultDraft();
            baselineDraft.CharacterName = "基线修士";
            var hiddenDraft = CharacterCreationCatalog.CreateDefaultDraft();
            hiddenDraft.CharacterName = "基线修士";
            hiddenDraft.HiddenRootSeedId = "hidden_variant_seed";

            var baseline = EntityCharacter.FromData(
                CharacterCreationRules.BuildCharacterData(baselineDraft),
                new TianZhang.Spatial.HexCoord(0, 0));
            var withDormantHiddenRoot = EntityCharacter.FromData(
                CharacterCreationRules.BuildCharacterData(hiddenDraft),
                new TianZhang.Spatial.HexCoord(0, 0));

            Assert.AreEqual(baseline.MaxHP, withDormantHiddenRoot.MaxHP);
            Assert.AreEqual(baseline.MaxMP, withDormantHiddenRoot.MaxMP);
            Assert.AreEqual(baseline.PhysAtk, withDormantHiddenRoot.PhysAtk);
            Assert.AreEqual(baseline.MagAtk, withDormantHiddenRoot.MagAtk);
            Assert.AreEqual(baseline.PhysDef, withDormantHiddenRoot.PhysDef);
            Assert.AreEqual(baseline.MagDef, withDormantHiddenRoot.MagDef);
            Assert.AreEqual(baseline.MovePoints, withDormantHiddenRoot.MovePoints);
            Assert.AreEqual(baseline.MaxSpellSlots, withDormantHiddenRoot.MaxSpellSlots);
            Assert.AreEqual(baseline.MaxSkillSlots, withDormantHiddenRoot.MaxSkillSlots);
            Assert.AreEqual("Dormant", withDormantHiddenRoot.HiddenRootState);
        }
    }
}
