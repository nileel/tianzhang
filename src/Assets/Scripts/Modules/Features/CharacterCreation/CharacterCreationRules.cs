using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Entity;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

namespace TianZhang.Features.CharacterCreation
{
    public static class CharacterCreationRules
    {
        public static CharacterCreationValidationResult Validate(
            CharacterCreationDraft draft,
            CharacterCreationPointBuyConfig pointBuyConfig)
        {
            if (pointBuyConfig == null) throw new ArgumentNullException(nameof(pointBuyConfig));
            var result = new CharacterCreationValidationResult
            {
                BudgetLimit = CharacterCreationCatalog.CreationBudget,
                InnatePurchasePointLimit = pointBuyConfig.purchasePointLimit,
            };

            if (draft == null)
            {
                result.Errors.Add("角色创建草稿不能为空。");
                result.BudgetAvailable = result.BudgetLimit;
                return result;
            }

            ValidateInnate(draft.Innate, pointBuyConfig, result);
            ValidateCreationBudget(draft, result);
            ValidateCraftSkills(draft, result);

            return result;
        }

        public static CharacterData BuildCharacterData(
            CharacterCreationDraft draft,
            CharacterCreationPointBuyConfig pointBuyConfig)
        {
            var validation = Validate(draft, pointBuyConfig);
            if (!validation.IsValid)
                throw new InvalidOperationException(string.Join("|", validation.Errors));

            var innate = draft.Innate ?? InnateAttributeSet.Balanced();
            var visibleRoot = CharacterCreationCatalog.FindVisibleRoot(draft.VisibleSpiritRootId);
            var hiddenSeed = string.IsNullOrWhiteSpace(draft.HiddenRootSeedId)
                ? null
                : CharacterCreationCatalog.FindHiddenRootSeed(draft.HiddenRootSeedId);
            var hiddenRoot = ResolveHiddenRoot(draft, hiddenSeed, out int hiddenRollSeed);
            var origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
            var profile = ScriptableObject.CreateInstance<CharacterData>();
            profile.charName = string.IsNullOrWhiteSpace(draft.CharacterName) ? "无名修士" : draft.CharacterName;
            profile.baseLevel = 1;
            profile.realmMultiplier = 1.5f;
            profile.realmStage = "练气";
            profile.gongFaName = "";
            profile.equippedSpells = new string[0];
            profile.availableSpells = new string[0];
            profile.equippedSkills = new string[0];
            profile.availableSkills = new string[0];
            // 角色装配所有者显式绑定生产无装备普攻档案；战斗调用点不再补默认值。
            profile.unarmedBasicAttackProfileId = CharacterCreationCatalog.BasicUnarmedAttackProfileId;

            profile.rootBone = innate.RootBone;
            profile.physique = innate.RootBone;
            profile.spirit = innate.Soul;
            profile.mind = innate.DivineSense;
            profile.reaction = innate.DivineSense;
            profile.talent = innate.Aptitude;
            profile.fortune = innate.Fortune;

            profile.innatePurchasePointsUsed = validation.InnatePurchasePointsUsed;
            profile.innatePurchasePointsLimit = validation.InnatePurchasePointLimit;
            profile.creationBudgetLimit = validation.BudgetLimit;
            profile.creationBudgetUsed = validation.BudgetUsed;
            profile.creationBudgetRefunded = validation.BudgetRefunded;
            profile.originId = origin != null ? origin.Id : "";
            profile.fateTagIds = draft.DistinctFateTagIds.ToArray();

            ApplyVisibleRoot(profile, visibleRoot);
            ApplyHiddenRoot(profile, hiddenSeed, hiddenRoot, hiddenRollSeed);
            ApplyCraftSkills(profile, draft);

            return profile;
        }

        public static float ResolveEffectiveCultivationMultiplier(CharacterData profile)
        {
            if (profile == null)
                return 1f;

            float visible = profile.visibleRootCultivationMultiplier > 0f
                ? profile.visibleRootCultivationMultiplier
                : 1f;

            if (!IsHiddenRootAwakened(profile) || profile.hiddenRootCultivationMultiplier <= 0f)
                return visible;

            return Mathf.Round((visible + profile.hiddenRootCultivationMultiplier) / 2.5f * 1000f) / 1000f;
        }

        public static string[] ResolveEffectiveLearnTags(CharacterData profile)
        {
            if (profile == null)
                return new string[0];

            var tags = new List<string>();
            AddDistinct(tags, profile.visibleRootLearnTags);

            if (IsHiddenRootAwakened(profile))
                AddDistinct(tags, profile.hiddenRootLearnTags);

            return tags.ToArray();
        }

        private static void ValidateInnate(
            InnateAttributeSet innate,
            CharacterCreationPointBuyConfig pointBuyConfig,
            CharacterCreationValidationResult result)
        {
            if (innate == null)
            {
                result.Errors.Add("先天属性不能为空。");
                return;
            }

            ValidateInnateValue("根骨", innate.RootBone, pointBuyConfig, result);
            ValidateInnateValue("魂魄", innate.Soul, pointBuyConfig, result);
            ValidateInnateValue("神识", innate.DivineSense, pointBuyConfig, result);
            ValidateInnateValue("资质", innate.Aptitude, pointBuyConfig, result);
            ValidateInnateValue("气运", innate.Fortune, pointBuyConfig, result);

            result.InnatePurchasePointsUsed = pointBuyConfig.CalculateCost(
                innate.RootBone,
                innate.Soul,
                innate.DivineSense,
                innate.Aptitude,
                innate.Fortune);
            result.InnatePurchasePointsRemaining = pointBuyConfig.purchasePointLimit - result.InnatePurchasePointsUsed;
            if (result.InnatePurchasePointsUsed > pointBuyConfig.purchasePointLimit)
                result.Errors.Add($"先天属性购买点数不能超过{pointBuyConfig.purchasePointLimit}。");
        }

        private static void ValidateInnateValue(
            string label,
            int value,
            CharacterCreationPointBuyConfig pointBuyConfig,
            CharacterCreationValidationResult result)
        {
            if (value < pointBuyConfig.minValue || value > pointBuyConfig.maxValue)
                result.Errors.Add($"{label}必须在{pointBuyConfig.minValue}到{pointBuyConfig.maxValue}之间。");
        }

        private static void ValidateCreationBudget(CharacterCreationDraft draft, CharacterCreationValidationResult result)
        {
            AddBudgetCost(CharacterCreationCatalog.FindVisibleRoot(draft.VisibleSpiritRootId)?.BudgetCost, result);
            var visibleRoot = CharacterCreationCatalog.FindVisibleRoot(draft.VisibleSpiritRootId);
            if (visibleRoot == null)
            {
                result.Errors.Add("显性灵根选项不存在。");
            }
            else
            {
                result.VisibleRootBudgetCost = visibleRoot.BudgetCost;
            }

            if (!string.IsNullOrWhiteSpace(draft.HiddenRootSeedId))
            {
                var hiddenSeed = CharacterCreationCatalog.FindHiddenRootSeed(draft.HiddenRootSeedId);
                if (hiddenSeed == null)
                {
                    result.Errors.Add("隐藏灵根种子选项不存在。");
                }
                else
                {
                    result.HiddenRootBudgetCost = hiddenSeed.BudgetCost;
                    ValidateHiddenSeedCompatibility(visibleRoot, hiddenSeed, result);
                    AddBudgetCost(hiddenSeed.BudgetCost, result);
                }
            }

            if (!string.IsNullOrWhiteSpace(draft.OriginId))
            {
                var origin = CharacterCreationCatalog.FindOrigin(draft.OriginId);
                if (origin == null)
                    result.Errors.Add("出身选项不存在。");
                else
                    AddBudgetCost(origin.BudgetCost, result);
            }

            foreach (var fateId in draft.DistinctFateTagIds)
            {
                var fate = CharacterCreationCatalog.FindFateTag(fateId);
                if (fate == null)
                    result.Errors.Add($"命格选项不存在：{fateId}");
                else
                    AddBudgetCost(fate.BudgetCost, result);
            }

            result.BudgetAvailable = result.BudgetLimit + result.BudgetRefunded - result.BudgetUsed;
            if (result.BudgetUsed > result.BudgetLimit + result.BudgetRefunded)
                result.Errors.Add("创建预算不能超过10点。");
        }

        private static void ValidateCraftSkills(CharacterCreationDraft draft, CharacterCreationValidationResult result)
        {
            if (draft.CraftSkills == null)
                return;

            foreach (var allocation in draft.CraftSkills)
            {
                if (allocation == null || string.IsNullOrWhiteSpace(allocation.SkillId))
                    continue;

                if (CharacterCreationCatalog.FindCraftSkill(allocation.SkillId) == null)
                    result.Errors.Add($"技艺选项不存在：{allocation.SkillId}");

                if (allocation.Level < 0)
                    result.Errors.Add("初始技艺等级不能为负数。");

                result.CraftSkillPointsUsed += Math.Max(0, allocation.Level);
            }

            if (result.CraftSkillPointsUsed > CharacterCreationCatalog.CraftSkillStartingPoints)
                result.Errors.Add("初始技艺等级合计不能超过3。");
        }

        private static void ValidateHiddenSeedCompatibility(
            SpiritRootOption visibleRoot,
            HiddenRootSeedOption hiddenSeed,
            CharacterCreationValidationResult result)
        {
            if (visibleRoot == null || hiddenSeed == null)
                return;

            if (hiddenSeed.Id == "hidden_ordinary_seed" && GradeRank(visibleRoot.Grade) > GradeRank("中品"))
                result.Errors.Add("隐灵根种子最高只支持中品显性灵根。");
        }

        private static int GradeRank(string grade)
        {
            return grade switch
            {
                "凡品" => 0,
                "下品" => 1,
                "中品" => 2,
                "上品" => 3,
                "极品" => 4,
                "上古" => 5,
                _ => 0,
            };
        }

        private static void AddBudgetCost(int? cost, CharacterCreationValidationResult result)
        {
            if (!cost.HasValue)
                return;

            if (cost.Value >= 0)
                result.BudgetUsed += cost.Value;
            else
                result.BudgetRefunded += -cost.Value;
        }

        private static SpiritRootOption ResolveHiddenRoot(
            CharacterCreationDraft draft,
            HiddenRootSeedOption hiddenSeed,
            out int hiddenRollSeed)
        {
            hiddenRollSeed = 0;
            if (hiddenSeed == null || hiddenSeed.CandidateRootIds.Length == 0)
                return null;

            uint hash = StableHash($"{draft.CharacterName}|{draft.VisibleSpiritRootId}|{hiddenSeed.Id}");
            hiddenRollSeed = unchecked((int)hash);
            string rootId = hiddenSeed.CandidateRootIds[hash % hiddenSeed.CandidateRootIds.Length];
            return CharacterCreationCatalog.FindHiddenRoot(rootId);
        }

        private static void ApplyVisibleRoot(CharacterData profile, SpiritRootOption root)
        {
            if (root == null)
                return;

            profile.visibleRootId = root.Id;
            profile.visibleRootKind = root.Kind.ToString();
            profile.visibleRootGrade = root.Grade;
            profile.visibleRootElement = root.Element;
            profile.visibleRootMotherElement = root.MotherElement;
            profile.visibleRootCultivationMultiplier = root.CultivationMultiplier;
            profile.visibleRootMpMultiplier = root.MpMultiplier;
            profile.visibleRootRealmCap = root.RealmCap;
            profile.visibleRootRegionAffinity = root.RegionAffinity;
            profile.visibleRootLearnTags = CloneArray(root.LearnTags);
        }

        private static void ApplyHiddenRoot(
            CharacterData profile,
            HiddenRootSeedOption seed,
            SpiritRootOption root,
            int hiddenRollSeed)
        {
            if (seed == null || root == null)
            {
                profile.hiddenRootState = HiddenRootState.None;
                profile.hiddenRootLearnTags = new string[0];
                return;
            }

            profile.hiddenRootSeedId = seed.Id;
            profile.hiddenRootId = root.Id;
            profile.hiddenRootKind = root.Kind.ToString();
            profile.hiddenRootGrade = root.Grade;
            profile.hiddenRootElement = root.Element;
            profile.hiddenRootMotherElement = root.MotherElement;
            profile.hiddenRootCultivationMultiplier = root.CultivationMultiplier;
            profile.hiddenRootMpMultiplier = root.MpMultiplier;
            profile.hiddenRootRealmCap = root.RealmCap;
            profile.hiddenRootRegionAffinity = root.RegionAffinity;
            profile.hiddenRootLearnTags = CloneArray(root.LearnTags);
            profile.hiddenRootState = HiddenRootState.Dormant;
            profile.hiddenRootRollSeed = hiddenRollSeed;
        }

        private static void ApplyCraftSkills(CharacterData profile, CharacterCreationDraft draft)
        {
            var allocations = draft.CraftSkills == null
                ? new List<CraftSkillAllocation>()
                : draft.CraftSkills
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.SkillId) && item.Level > 0)
                    .ToList();

            profile.craftSkillIds = allocations.Select(item => item.SkillId).ToArray();
            profile.craftSkillLevels = allocations.Select(item => item.Level).ToArray();
        }

        private static bool IsHiddenRootAwakened(CharacterData profile)
        {
            return string.Equals(profile.hiddenRootState, HiddenRootState.Awakened, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddDistinct(List<string> target, string[] source)
        {
            if (source == null)
                return;

            foreach (var tag in source)
            {
                if (!string.IsNullOrWhiteSpace(tag) && !target.Contains(tag))
                    target.Add(tag);
            }
        }

        private static string[] CloneArray(string[] source)
        {
            return source != null ? (string[])source.Clone() : new string[0];
        }

        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                if (value != null)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        hash ^= value[i];
                        hash *= 16777619;
                    }
                }

                return hash;
            }
        }
    }
}
