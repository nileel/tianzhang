using System;
using System.Collections.Generic;
using TianZhang.Entity;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class JindanProofCoordinator
    {
        private readonly struct CompletionKey : IEquatable<CompletionKey>
        {
            public string PositionId { get; }
            public long WorldTick { get; }

            public CompletionKey(string positionId, long worldTick)
            {
                if (string.IsNullOrWhiteSpace(positionId))
                {
                    throw new ArgumentException(
                        "Position ID is required.",
                        nameof(positionId));
                }
                if (worldTick < 0)
                    throw new ArgumentOutOfRangeException(nameof(worldTick));

                PositionId = positionId;
                WorldTick = worldTick;
            }

            public bool Equals(CompletionKey other)
            {
                return WorldTick == other.WorldTick &&
                    string.Equals(
                        PositionId,
                        other.PositionId,
                        StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CompletionKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return (StringComparer.Ordinal.GetHashCode(PositionId) * 397) ^
                    WorldTick.GetHashCode();
            }
        }

        private readonly Dictionary<string, JindanProofAttempt> attempts =
            new Dictionary<string, JindanProofAttempt>(StringComparer.Ordinal);
        private readonly Dictionary<CompletionKey, List<string>> regularCompletions =
            new Dictionary<CompletionKey, List<string>>();
        private readonly Dictionary<CompletionKey, List<string>> criticalCompletions =
            new Dictionary<CompletionKey, List<string>>();
        private readonly HashSet<CompletionKey> closedRegularTicks =
            new HashSet<CompletionKey>();
        private readonly HashSet<CompletionKey> closedCriticalTicks =
            new HashSet<CompletionKey>();

        public void Register(JindanProofAttempt attempt)
        {
            if (attempt == null)
                throw new ArgumentNullException(nameof(attempt));
            if (!attempts.TryAdd(attempt.AttemptId, attempt))
                throw new ArgumentException("Attempt ID already exists.", nameof(attempt));
        }

        public JindanProofAttempt GetAttempt(string attemptId)
        {
            return attemptId != null &&
                attempts.TryGetValue(attemptId, out JindanProofAttempt attempt)
                    ? attempt
                    : null;
        }

        public void SubmitRegularCompletion(string attemptId, long worldTick)
        {
            JindanProofAttempt attempt = RequireAttempt(attemptId);
            if (attempt.Status != ProofAttemptStatus.AwaitingRegularTickClose)
            {
                throw new InvalidOperationException(
                    "Attempt has not completed the regular stage.");
            }

            AddCompletion(
                regularCompletions,
                criticalCompletions,
                closedRegularTicks,
                attempt.PositionId,
                worldTick,
                attemptId);
        }

        public void SubmitCriticalCompletion(string attemptId, long worldTick)
        {
            JindanProofAttempt attempt = RequireAttempt(attemptId);
            if (attempt.Status != ProofAttemptStatus.AwaitingCriticalTickClose)
            {
                throw new InvalidOperationException(
                    "Attempt has not completed the critical stage.");
            }

            AddCompletion(
                criticalCompletions,
                regularCompletions,
                closedCriticalTicks,
                attempt.PositionId,
                worldTick,
                attemptId);
        }

        public ProofTickResolution CloseRegularTick(string positionId, long worldTick)
        {
            var key = new CompletionKey(positionId, worldTick);
            if (!closedRegularTicks.Add(key))
                return Empty();

            List<JindanProofAttempt> completed =
                TakeCompletions(regularCompletions, key);
            if (completed.Count == 1)
            {
                completed[0].MarkReadyToBind();
                return Unique(completed[0]);
            }

            if (completed.Count > 1)
            {
                foreach (JindanProofAttempt attempt in completed)
                    attempt.EnterCriticalContest();
                return Continued(completed);
            }

            return Empty();
        }

        public ProofTickResolution CloseCriticalTick(string positionId, long worldTick)
        {
            var key = new CompletionKey(positionId, worldTick);
            if (!closedCriticalTicks.Add(key))
                return Empty();

            List<JindanProofAttempt> completed =
                TakeCompletions(criticalCompletions, key);
            if (completed.Count == 1)
            {
                completed[0].MarkReadyToBind();
                return Unique(completed[0]);
            }

            if (completed.Count > 1)
            {
                foreach (JindanProofAttempt attempt in completed)
                    attempt.RestartCriticalRound();
                return Continued(completed);
            }

            return Empty();
        }

        public void InvalidateOthers(string positionId, string winningAttemptId)
        {
            foreach (JindanProofAttempt attempt in attempts.Values)
            {
                if (string.Equals(
                        attempt.PositionId,
                        positionId,
                        StringComparison.Ordinal) &&
                    !string.Equals(
                        attempt.AttemptId,
                        winningAttemptId,
                        StringComparison.Ordinal))
                {
                    attempt.Invalidate();
                }
            }
        }

        /// <summary>
        /// 结丹协调边界是道基／紫府不可逆快照的唯一运行时写入入口。
        /// </summary>
        public FoundationPurpleMansionOperationResult TryFormFoundationPurpleMansionLock(
            Character character)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));

            return character.TryFormJindanLock();
        }

        internal void CaptureState(
            List<JindanProofAttemptSaveData> attemptData,
            List<ProofCompletionSaveData> regularData,
            List<ProofCompletionSaveData> criticalData,
            List<ProofCompletionKeySaveData> closedRegularData,
            List<ProofCompletionKeySaveData> closedCriticalData)
        {
            if (attemptData == null ||
                regularData == null ||
                criticalData == null ||
                closedRegularData == null ||
                closedCriticalData == null)
            {
                throw new ArgumentNullException("Snapshot destinations are required.");
            }

            foreach (JindanProofAttempt attempt in attempts.Values)
                attemptData.Add(attempt.CaptureState());
            attemptData.Sort((a, b) =>
                string.CompareOrdinal(a.attemptId, b.attemptId));
            CaptureCompletions(regularCompletions, regularData);
            CaptureCompletions(criticalCompletions, criticalData);
            CaptureClosedTicks(closedRegularTicks, closedRegularData);
            CaptureClosedTicks(closedCriticalTicks, closedCriticalData);
        }

        internal static JindanProofCoordinator RestoreState(
            IReadOnlyList<JindanProofAttemptSaveData> attemptData,
            IReadOnlyList<ProofCompletionSaveData> regularData,
            IReadOnlyList<ProofCompletionSaveData> criticalData,
            IReadOnlyList<ProofCompletionKeySaveData> closedRegularData,
            IReadOnlyList<ProofCompletionKeySaveData> closedCriticalData)
        {
            var coordinator = new JindanProofCoordinator();
            foreach (JindanProofAttemptSaveData data in
                attemptData ?? Array.Empty<JindanProofAttemptSaveData>())
            {
                coordinator.Register(JindanProofAttempt.RestoreState(data));
            }

            coordinator.RestoreClosedTicks(
                coordinator.closedRegularTicks,
                closedRegularData);
            coordinator.RestoreClosedTicks(
                coordinator.closedCriticalTicks,
                closedCriticalData);
            var openAttemptIds = new HashSet<string>(StringComparer.Ordinal);
            coordinator.RestoreCompletions(
                coordinator.regularCompletions,
                coordinator.closedRegularTicks,
                regularData,
                ProofAttemptStatus.AwaitingRegularTickClose,
                openAttemptIds);
            coordinator.RestoreCompletions(
                coordinator.criticalCompletions,
                coordinator.closedCriticalTicks,
                criticalData,
                ProofAttemptStatus.AwaitingCriticalTickClose,
                openAttemptIds);
            return coordinator;
        }

        private JindanProofAttempt RequireAttempt(string attemptId)
        {
            if (attemptId == null ||
                !attempts.TryGetValue(attemptId, out JindanProofAttempt attempt))
            {
                throw new KeyNotFoundException("Unknown attempt: " + attemptId);
            }

            return attempt;
        }

        private static void AddCompletion(
            IDictionary<CompletionKey, List<string>> store,
            IReadOnlyDictionary<CompletionKey, List<string>> otherStore,
            ISet<CompletionKey> closedTicks,
            string positionId,
            long worldTick,
            string attemptId)
        {
            var key = new CompletionKey(positionId, worldTick);
            if (closedTicks.Contains(key))
                throw new InvalidOperationException("World tick is already closed.");

            foreach (KeyValuePair<CompletionKey, List<string>> pair in store)
            {
                if (!pair.Key.Equals(key) && pair.Value.Contains(attemptId))
                {
                    throw new InvalidOperationException(
                        "Attempt already belongs to another open tick.");
                }
            }

            foreach (List<string> values in otherStore.Values)
            {
                if (values.Contains(attemptId))
                {
                    throw new InvalidOperationException(
                        "Attempt already belongs to another open tick.");
                }
            }

            if (!store.TryGetValue(key, out List<string> current))
            {
                current = new List<string>();
                store.Add(key, current);
            }

            if (!current.Contains(attemptId))
                current.Add(attemptId);
        }

        private List<JindanProofAttempt> TakeCompletions(
            IDictionary<CompletionKey, List<string>> store,
            CompletionKey key)
        {
            if (!store.TryGetValue(key, out List<string> values))
                return new List<JindanProofAttempt>();

            store.Remove(key);
            var result = new List<JindanProofAttempt>();
            foreach (string attemptId in values)
                result.Add(RequireAttempt(attemptId));
            return result;
        }

        private static void CaptureCompletions(
            IReadOnlyDictionary<CompletionKey, List<string>> source,
            List<ProofCompletionSaveData> destination)
        {
            foreach (KeyValuePair<CompletionKey, List<string>> pair in source)
            {
                var item = new ProofCompletionSaveData
                {
                    positionId = pair.Key.PositionId,
                    worldTick = pair.Key.WorldTick,
                    attemptIds = new List<string>(pair.Value)
                };
                item.attemptIds.Sort(StringComparer.Ordinal);
                destination.Add(item);
            }

            SortCompletionData(destination);
        }

        private static void CaptureClosedTicks(
            IEnumerable<CompletionKey> source,
            List<ProofCompletionKeySaveData> destination)
        {
            foreach (CompletionKey key in source)
            {
                destination.Add(new ProofCompletionKeySaveData
                {
                    positionId = key.PositionId,
                    worldTick = key.WorldTick
                });
            }

            destination.Sort(CompareCompletionKeys);
        }

        private void RestoreClosedTicks(
            ISet<CompletionKey> destination,
            IReadOnlyList<ProofCompletionKeySaveData> source)
        {
            foreach (ProofCompletionKeySaveData item in
                source ?? Array.Empty<ProofCompletionKeySaveData>())
            {
                if (item == null)
                {
                    throw new ArgumentException(
                        "Invalid closed completion key.",
                        nameof(source));
                }

                CompletionKey key;
                try
                {
                    key = new CompletionKey(item.positionId, item.worldTick);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        "Invalid closed completion key.",
                        nameof(source),
                        exception);
                }

                if (!destination.Add(key))
                {
                    throw new ArgumentException(
                        "Duplicate closed completion key.",
                        nameof(source));
                }
            }
        }

        private void RestoreCompletions(
            IDictionary<CompletionKey, List<string>> destination,
            ISet<CompletionKey> closedTicks,
            IReadOnlyList<ProofCompletionSaveData> source,
            ProofAttemptStatus expectedStatus,
            ISet<string> openAttemptIds)
        {
            foreach (ProofCompletionSaveData item in
                source ?? Array.Empty<ProofCompletionSaveData>())
            {
                if (item == null ||
                    item.attemptIds == null ||
                    item.attemptIds.Count == 0)
                {
                    throw new ArgumentException(
                        "Invalid completion snapshot.",
                        nameof(source));
                }

                CompletionKey key;
                try
                {
                    key = new CompletionKey(item.positionId, item.worldTick);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        "Invalid completion snapshot.",
                        nameof(source),
                        exception);
                }

                if (destination.ContainsKey(key) || closedTicks.Contains(key))
                {
                    throw new ArgumentException(
                        "Duplicate or closed completion key.",
                        nameof(source));
                }

                var restoredIds = new List<string>();
                var uniqueIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string attemptId in item.attemptIds)
                {
                    if (string.IsNullOrWhiteSpace(attemptId) ||
                        !uniqueIds.Add(attemptId) ||
                        !openAttemptIds.Add(attemptId) ||
                        !attempts.TryGetValue(
                            attemptId,
                            out JindanProofAttempt attempt) ||
                        attempt.Status != expectedStatus ||
                        !string.Equals(
                            attempt.PositionId,
                            item.positionId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Invalid completion attempt.",
                            nameof(source));
                    }

                    restoredIds.Add(attemptId);
                }

                destination.Add(key, restoredIds);
            }
        }

        private static void SortCompletionData(
            List<ProofCompletionSaveData> destination)
        {
            destination.Sort((a, b) =>
            {
                int positionOrder =
                    string.CompareOrdinal(a.positionId, b.positionId);
                return positionOrder != 0
                    ? positionOrder
                    : a.worldTick.CompareTo(b.worldTick);
            });
        }

        private static int CompareCompletionKeys(
            ProofCompletionKeySaveData a,
            ProofCompletionKeySaveData b)
        {
            int positionOrder = string.CompareOrdinal(a.positionId, b.positionId);
            return positionOrder != 0
                ? positionOrder
                : a.worldTick.CompareTo(b.worldTick);
        }

        private static ProofTickResolution Unique(JindanProofAttempt attempt)
        {
            return new ProofTickResolution(
                ProofTickResolutionKind.UniqueReady,
                attempt.AttemptId,
                new[] { attempt.AttemptId });
        }

        private static ProofTickResolution Continued(
            IReadOnlyList<JindanProofAttempt> completed)
        {
            var ids = new List<string>();
            foreach (JindanProofAttempt attempt in completed)
                ids.Add(attempt.AttemptId);

            ids.Sort(StringComparer.Ordinal);
            return new ProofTickResolution(
                ProofTickResolutionKind.CriticalContestContinues,
                null,
                ids);
        }

        private static ProofTickResolution Empty()
        {
            return new ProofTickResolution(
                ProofTickResolutionKind.NoCompletion,
                null,
                Array.Empty<string>());
        }
    }
}
