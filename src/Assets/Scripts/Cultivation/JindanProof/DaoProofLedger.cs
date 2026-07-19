using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class DaoProofLedger
    {
        private readonly Dictionary<string, int> metricValues =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> achievements =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> processedEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> repeatKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public string ActorId { get; }

        public DaoProofLedger(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));

            ActorId = actorId;
        }

        public bool TryRecord(
            DaoProofBehaviorEvent behavior,
            IReadOnlyDictionary<string, DaoProofMetricRule> rules)
        {
            if (behavior == null)
                throw new ArgumentNullException(nameof(behavior));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (!string.Equals(ActorId, behavior.ActorId, StringComparison.Ordinal))
                return false;
            if (!processedEventIds.Add(behavior.EventId))
                return false;

            bool accepted = false;
            foreach (DaoProofContribution contribution in behavior.Contributions)
            {
                if (!rules.TryGetValue(contribution.MetricId, out DaoProofMetricRule rule))
                    continue;
                if (behavior.ChallengeTier < rule.MinimumChallengeTier)
                    continue;

                string repeatKey = BuildRepeatKey(rule, behavior);
                if (!repeatKeys.Add(repeatKey))
                    continue;

                metricValues.TryGetValue(contribution.MetricId, out int current);
                metricValues[contribution.MetricId] =
                    checked(current + contribution.Amount);
                accepted = true;
            }

            if (accepted)
            {
                foreach (string achievementId in behavior.AchievementIds)
                {
                    if (!string.IsNullOrWhiteSpace(achievementId))
                        achievements.Add(achievementId);
                }
            }

            return accepted;
        }

        public int GetMetricValue(string metricId)
        {
            return metricValues.TryGetValue(metricId, out int value) ? value : 0;
        }

        public bool HasAchievement(string achievementId)
        {
            return achievements.Contains(achievementId);
        }

        public bool HasProcessedEvent(string eventId)
        {
            return processedEventIds.Contains(eventId);
        }

        internal DaoProofLedgerSaveData CaptureState()
        {
            var data = new DaoProofLedgerSaveData { actorId = ActorId };
            foreach (KeyValuePair<string, int> pair in metricValues)
            {
                data.metrics.Add(new MetricValueSaveData
                {
                    id = pair.Key,
                    value = pair.Value
                });
            }

            data.metrics.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            data.achievements.AddRange(achievements);
            data.achievements.Sort(StringComparer.Ordinal);
            data.processedEventIds.AddRange(processedEventIds);
            data.processedEventIds.Sort(StringComparer.Ordinal);
            data.repeatKeys.AddRange(repeatKeys);
            data.repeatKeys.Sort(StringComparer.Ordinal);
            return data;
        }

        internal static DaoProofLedger RestoreState(DaoProofLedgerSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var ledger = new DaoProofLedger(data.actorId);
            foreach (MetricValueSaveData metric in
                data.metrics ?? new List<MetricValueSaveData>())
            {
                if (metric == null ||
                    string.IsNullOrWhiteSpace(metric.id) ||
                    metric.value < 0 ||
                    ledger.metricValues.ContainsKey(metric.id))
                {
                    throw new ArgumentException(
                        "Invalid metric snapshot.",
                        nameof(data));
                }

                ledger.metricValues.Add(metric.id, metric.value);
            }

            RestoreUniqueIds(
                data.achievements,
                ledger.achievements,
                "achievement",
                nameof(data));
            RestoreUniqueIds(
                data.processedEventIds,
                ledger.processedEventIds,
                "processed event",
                nameof(data));
            RestoreUniqueIds(
                data.repeatKeys,
                ledger.repeatKeys,
                "repeat key",
                nameof(data));
            return ledger;
        }

        private static void RestoreUniqueIds(
            IEnumerable<string> source,
            ISet<string> destination,
            string valueKind,
            string parameterName)
        {
            if (source == null)
                return;

            foreach (string id in source)
            {
                if (string.IsNullOrWhiteSpace(id) || !destination.Add(id))
                {
                    throw new ArgumentException(
                        "Invalid or duplicate " + valueKind + ".",
                        parameterName);
                }
            }
        }

        private static string BuildRepeatKey(
            DaoProofMetricRule rule,
            DaoProofBehaviorEvent behavior)
        {
            switch (rule.RepeatPolicy)
            {
                case ProofRepeatPolicy.OncePerTarget:
                    return rule.MetricId + "|target|" + behavior.TargetKey;
                case ProofRepeatPolicy.OncePerContext:
                    return rule.MetricId + "|context|" + behavior.ContextKey;
                default:
                    return rule.MetricId + "|event|" + behavior.EventId;
            }
        }
    }
}
