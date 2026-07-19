using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public enum JindanSeatType { Source, Transformation, Domain }
    public enum ProofRequirementType { SharedMetric, SignatureAchievement }
    public enum ProofKnowledgeLevel { Unknown, RoadDirection, FullProfile }
    public enum JindanPositionVisibility { Public, FactionKnown, Rumored, Hidden }
    public enum ProofAttemptStatus
    {
        Active,
        AwaitingRegularTickClose,
        CriticalContest,
        AwaitingCriticalTickClose,
        ReadyToBind,
        Interrupted,
        Invalidated,
        Bound
    }
    public enum ProofRepeatPolicy { UniqueEvent, OncePerTarget, OncePerContext }

    public sealed class JindanProofRequirement
    {
        public string RecordId { get; }
        public ProofRequirementType Type { get; }
        public int MinimumValue { get; }

        public JindanProofRequirement(
            string recordId,
            ProofRequirementType type,
            int minimumValue)
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("Requirement record ID is required.", nameof(recordId));
            if (minimumValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumValue));

            RecordId = recordId;
            Type = type;
            MinimumValue = minimumValue;
        }
    }

    public sealed class JindanProofProfileDefinition
    {
        private readonly List<JindanProofRequirement> requirements;

        public string ProfileId { get; }
        public string RoadId { get; }
        public JindanSeatType SeatType { get; }
        public IReadOnlyList<JindanProofRequirement> Requirements => requirements;
        public int RegularProgressTarget { get; }
        public int CriticalProgressTarget { get; }

        public JindanProofProfileDefinition(
            string profileId,
            string roadId,
            JindanSeatType seatType,
            IEnumerable<JindanProofRequirement> requirements,
            int regularProgressTarget,
            int criticalProgressTarget)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID is required.", nameof(profileId));
            if (string.IsNullOrWhiteSpace(roadId))
                throw new ArgumentException("Road ID is required.", nameof(roadId));
            if (requirements == null)
                throw new ArgumentNullException(nameof(requirements));
            if (regularProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
            if (criticalProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

            var seen = new HashSet<string>(StringComparer.Ordinal);
            this.requirements = new List<JindanProofRequirement>();
            foreach (JindanProofRequirement requirement in requirements)
            {
                if (requirement == null || !seen.Add(requirement.RecordId))
                    throw new ArgumentException(
                        "Requirements must be non-null and unique.",
                        nameof(requirements));
                this.requirements.Add(requirement);
            }
            if (this.requirements.Count == 0)
                throw new ArgumentException(
                    "At least one requirement is required.",
                    nameof(requirements));

            ProfileId = profileId;
            RoadId = roadId;
            SeatType = seatType;
            RegularProgressTarget = regularProgressTarget;
            CriticalProgressTarget = criticalProgressTarget;
        }
    }

    public sealed class DaoProofContribution
    {
        public string MetricId { get; }
        public int Amount { get; }

        public DaoProofContribution(string metricId, int amount)
        {
            if (string.IsNullOrWhiteSpace(metricId))
                throw new ArgumentException("Metric ID is required.", nameof(metricId));
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            MetricId = metricId;
            Amount = amount;
        }
    }

    public sealed class DaoProofMetricRule
    {
        public string MetricId { get; }
        public ProofRepeatPolicy RepeatPolicy { get; }
        public int MinimumChallengeTier { get; }

        public DaoProofMetricRule(
            string metricId,
            ProofRepeatPolicy repeatPolicy,
            int minimumChallengeTier)
        {
            if (string.IsNullOrWhiteSpace(metricId))
                throw new ArgumentException("Metric ID is required.", nameof(metricId));
            if (minimumChallengeTier < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumChallengeTier));

            MetricId = metricId;
            RepeatPolicy = repeatPolicy;
            MinimumChallengeTier = minimumChallengeTier;
        }
    }

    public sealed class DaoProofBehaviorEvent
    {
        public string EventId { get; }
        public string ActorId { get; }
        public string TargetKey { get; }
        public string ContextKey { get; }
        public int ChallengeTier { get; }
        public IReadOnlyList<DaoProofContribution> Contributions { get; }
        public IReadOnlyList<string> AchievementIds { get; }

        public DaoProofBehaviorEvent(
            string eventId,
            string actorId,
            string targetKey,
            string contextKey,
            int challengeTier,
            IReadOnlyList<DaoProofContribution> contributions,
            IReadOnlyList<string> achievementIds)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                throw new ArgumentException("Event ID is required.", nameof(eventId));
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));
            if (challengeTier < 0)
                throw new ArgumentOutOfRangeException(nameof(challengeTier));

            EventId = eventId;
            ActorId = actorId;
            TargetKey = targetKey ?? string.Empty;
            ContextKey = contextKey ?? string.Empty;
            ChallengeTier = challengeTier;
            Contributions = contributions ?? Array.Empty<DaoProofContribution>();
            AchievementIds = achievementIds ?? Array.Empty<string>();
        }
    }

    public sealed class ProofEligibilityResult
    {
        public bool IsSatisfied { get; }
        public IReadOnlyList<string> UnmetRequirementIds { get; }

        public ProofEligibilityResult(
            bool isSatisfied,
            IReadOnlyList<string> unmetRequirementIds)
        {
            IsSatisfied = isSatisfied;
            UnmetRequirementIds = unmetRequirementIds == null
                ? Array.Empty<string>()
                : new List<string>(unmetRequirementIds);
        }
    }

    public sealed class ProofRequirementView
    {
        public string RecordId { get; }
        public int CurrentValue { get; }
        public int RequiredValue { get; }
        public bool IsMet { get; }

        public ProofRequirementView(
            string recordId,
            int currentValue,
            int requiredValue)
        {
            RecordId = recordId;
            CurrentValue = currentValue;
            RequiredValue = requiredValue;
            IsMet = currentValue >= requiredValue;
        }
    }

    public sealed class JindanProofView
    {
        public string RoadId { get; }
        public IReadOnlyList<ProofRequirementView> Requirements { get; }
        public int? AdaptationPercent { get; }
        public int? EstimatedSuccessPercent { get; }
        public bool RevealsExactConditions { get; }

        public JindanProofView(
            string roadId,
            IReadOnlyList<ProofRequirementView> requirements,
            int? adaptationPercent,
            int? estimatedSuccessPercent,
            bool revealsExactConditions)
        {
            RoadId = roadId;
            Requirements = requirements == null
                ? Array.Empty<ProofRequirementView>()
                : new List<ProofRequirementView>(requirements);
            AdaptationPercent = adaptationPercent;
            EstimatedSuccessPercent = estimatedSuccessPercent;
            RevealsExactConditions = revealsExactConditions;
        }
    }

    public enum ProofTickResolutionKind
    {
        NoCompletion,
        UniqueReady,
        CriticalContestContinues
    }

    public sealed class ProofTickResolution
    {
        public ProofTickResolutionKind Kind { get; }
        public string UniqueAttemptId { get; }
        public IReadOnlyList<string> ParticipantAttemptIds { get; }

        public ProofTickResolution(
            ProofTickResolutionKind kind,
            string uniqueAttemptId,
            IReadOnlyList<string> participantAttemptIds)
        {
            Kind = kind;
            UniqueAttemptId = uniqueAttemptId;
            ParticipantAttemptIds = participantAttemptIds == null
                ? Array.Empty<string>()
                : new List<string>(participantAttemptIds);
        }
    }
}
