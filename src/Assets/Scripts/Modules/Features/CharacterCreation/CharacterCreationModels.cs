using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Game.CharacterCreation;

namespace TianZhang.Features.CharacterCreation
{
    public enum SpiritRootKind
    {
        Basic,
        Variant,
        Special,
    }

    public static class HiddenRootState
    {
        public const string None = "";
        public const string Dormant = "Dormant";
        public const string Clue = "Clue";
        public const string Awakened = "Awakened";
    }

    public sealed class InnateAttributeSet
    {
        public int RootBone;
        public int Soul;
        public int DivineSense;
        public int Aptitude;
        public int Fortune;

        public InnateAttributeSet()
            : this(8, 8, 8, 8, 8)
        {
        }

        public InnateAttributeSet(int rootBone, int soul, int divineSense, int aptitude, int fortune)
        {
            RootBone = rootBone;
            Soul = soul;
            DivineSense = divineSense;
            Aptitude = aptitude;
            Fortune = fortune;
        }

        public int Total => RootBone + Soul + DivineSense + Aptitude + Fortune;

        public static InnateAttributeSet Balanced()
        {
            return new InnateAttributeSet(8, 8, 8, 8, 8);
        }

        public int CalculatePurchaseCost(CharacterCreationPointBuyConfig pointBuyConfig)
        {
            if (pointBuyConfig == null) throw new ArgumentNullException(nameof(pointBuyConfig));
            return pointBuyConfig.CalculateCost(RootBone, Soul, DivineSense, Aptitude, Fortune);
        }

        public static int CalculateAttributeCost(
            int value,
            CharacterCreationPointBuyConfig pointBuyConfig)
        {
            if (pointBuyConfig == null) throw new ArgumentNullException(nameof(pointBuyConfig));
            return pointBuyConfig.CalculateCost(value);
        }
    }

    public sealed class SpiritRootOption
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly SpiritRootKind Kind;
        public readonly string Grade;
        public readonly string Element;
        public readonly string MotherElement;
        public readonly int BudgetCost;
        public readonly float CultivationMultiplier;
        public readonly float MpMultiplier;
        public readonly string RealmCap;
        public readonly string RegionAffinity;
        public readonly string[] LearnTags;

        public SpiritRootOption(
            string id,
            string displayName,
            SpiritRootKind kind,
            string grade,
            string element,
            string motherElement,
            int budgetCost,
            float cultivationMultiplier,
            float mpMultiplier,
            string realmCap,
            string regionAffinity,
            string[] learnTags)
        {
            Id = id;
            DisplayName = displayName;
            Kind = kind;
            Grade = grade;
            Element = element;
            MotherElement = motherElement;
            BudgetCost = budgetCost;
            CultivationMultiplier = cultivationMultiplier;
            MpMultiplier = mpMultiplier;
            RealmCap = realmCap;
            RegionAffinity = regionAffinity;
            LearnTags = learnTags ?? new string[0];
        }
    }

    public sealed class HiddenRootSeedOption
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int BudgetCost;
        public readonly string[] CandidateRootIds;

        public HiddenRootSeedOption(string id, string displayName, int budgetCost, string[] candidateRootIds)
        {
            Id = id;
            DisplayName = displayName;
            BudgetCost = budgetCost;
            CandidateRootIds = candidateRootIds ?? new string[0];
        }
    }

    public sealed class OriginOption
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int BudgetCost;
        public readonly string StartNodeId;

        public OriginOption(string id, string displayName, int budgetCost, string startNodeId)
        {
            Id = id;
            DisplayName = displayName;
            BudgetCost = budgetCost;
            StartNodeId = startNodeId;
        }
    }

    public sealed class FateTagOption
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly int BudgetCost;

        public FateTagOption(string id, string displayName, int budgetCost)
        {
            Id = id;
            DisplayName = displayName;
            BudgetCost = budgetCost;
        }
    }

    public sealed class CraftSkillOption
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly string GoverningInnate;

        public CraftSkillOption(string id, string displayName, string governingInnate)
        {
            Id = id;
            DisplayName = displayName;
            GoverningInnate = governingInnate;
        }
    }

    public sealed class CraftSkillAllocation
    {
        public string SkillId;
        public int Level;

        public CraftSkillAllocation(string skillId, int level)
        {
            SkillId = skillId;
            Level = level;
        }
    }

    public sealed class CharacterCreationDraft
    {
        public string CharacterName = "无名修士";
        public string VisibleSpiritRootId = "root_water_middle";
        public string HiddenRootSeedId = "";
        public string OriginId = "origin_loose";
        public InnateAttributeSet Innate = InnateAttributeSet.Balanced();
        public List<string> FateTagIds = new List<string>();
        public List<CraftSkillAllocation> CraftSkills = new List<CraftSkillAllocation>();

        public IEnumerable<string> DistinctFateTagIds => FateTagIds == null
            ? Enumerable.Empty<string>()
            : FateTagIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct();
    }

    public sealed class CharacterCreationValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public readonly List<string> Errors = new List<string>();
        public int BudgetLimit;
        public int BudgetUsed;
        public int BudgetRefunded;
        public int BudgetAvailable;
        public int VisibleRootBudgetCost;
        public int HiddenRootBudgetCost;
        public int CraftSkillPointsUsed;
        public int InnatePurchasePointLimit;
        public int InnatePurchasePointsUsed;
        public int InnatePurchasePointsRemaining;
    }
}
