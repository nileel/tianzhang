using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    [Serializable]
    public sealed class MetricValueSaveData
    {
        public string id;
        public int value;
    }

    [Serializable]
    public sealed class DaoProofLedgerSaveData
    {
        public string actorId;
        public List<MetricValueSaveData> metrics = new List<MetricValueSaveData>();
        public List<string> achievements = new List<string>();
        public List<string> processedEventIds = new List<string>();
        public List<string> repeatKeys = new List<string>();
    }

    [Serializable]
    public sealed class JindanProofAttemptSaveData
    {
        public string attemptId;
        public string positionId;
        public string actorId;
        public string profileId;
        public string siteId;
        public string carrierAbilityInstanceId;
        public long expectedPositionVersion;
        public int regularProgressTarget;
        public int criticalProgressTarget;
        public int regularProgress;
        public int criticalProgress;
        public int criticalRound;
        public string interruptionReason;
        public ProofAttemptStatus status;
    }

    [Serializable]
    public sealed class ProofCompletionSaveData
    {
        public string positionId;
        public long worldTick;
        public List<string> attemptIds = new List<string>();
    }

    [Serializable]
    public sealed class ProofCompletionKeySaveData
    {
        public string positionId;
        public long worldTick;
    }

    [Serializable]
    public sealed class JindanPositionSaveData
    {
        public string positionId;
        public string profileId;
        public JindanSeatType seatType;
        public JindanPositionVisibility visibility;
        public string holderActorId;
        public long version;
    }

    [Serializable]
    public sealed class SeatCarrierBindingSaveData
    {
        public string positionId;
        public JindanSeatType seatType;
        public string carrierAbilityInstanceId;
    }

    [Serializable]
    public sealed class JindanCoreSaveData
    {
        public string actorId;
        public string coreBindingId;
        public List<SeatCarrierBindingSaveData> seatBindings =
            new List<SeatCarrierBindingSaveData>();
    }

    [Serializable]
    public sealed class JindanProofSaveData
    {
        public int schemaVersion = JindanProofSnapshot.CurrentSchemaVersion;
        public List<DaoProofLedgerSaveData> ledgers =
            new List<DaoProofLedgerSaveData>();
        public List<JindanProofAttemptSaveData> attempts =
            new List<JindanProofAttemptSaveData>();
        public List<ProofCompletionSaveData> regularCompletions =
            new List<ProofCompletionSaveData>();
        public List<ProofCompletionSaveData> criticalCompletions =
            new List<ProofCompletionSaveData>();
        public List<ProofCompletionKeySaveData> closedRegularTicks =
            new List<ProofCompletionKeySaveData>();
        public List<ProofCompletionKeySaveData> closedCriticalTicks =
            new List<ProofCompletionKeySaveData>();
        public List<JindanPositionSaveData> positions =
            new List<JindanPositionSaveData>();
        public List<JindanCoreSaveData> cores =
            new List<JindanCoreSaveData>();
    }

    public sealed class JindanProofRestoredState
    {
        private readonly Dictionary<string, DaoProofLedger> ledgers =
            new Dictionary<string, DaoProofLedger>(StringComparer.Ordinal);
        private readonly Dictionary<string, JindanCoreState> cores =
            new Dictionary<string, JindanCoreState>(StringComparer.Ordinal);

        public JindanProofCoordinator Coordinator { get; }
        public JindanPositionRegistry Registry { get; }

        internal JindanProofRestoredState(
            IReadOnlyList<DaoProofLedger> ledgers,
            JindanProofCoordinator coordinator,
            JindanPositionRegistry registry,
            IReadOnlyList<JindanCoreState> cores)
        {
            if (ledgers == null)
                throw new ArgumentNullException(nameof(ledgers));
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (cores == null)
                throw new ArgumentNullException(nameof(cores));

            Coordinator = coordinator;
            Registry = registry;
            foreach (DaoProofLedger ledger in ledgers)
            {
                if (ledger == null ||
                    !this.ledgers.TryAdd(ledger.ActorId, ledger))
                {
                    throw new ArgumentException(
                        "Duplicate or null ledger actor ID.",
                        nameof(ledgers));
                }
            }

            foreach (JindanCoreState core in cores)
            {
                if (core == null || !this.cores.TryAdd(core.ActorId, core))
                {
                    throw new ArgumentException(
                        "Duplicate or null core actor ID.",
                        nameof(cores));
                }
            }
        }

        public DaoProofLedger GetLedger(string actorId)
        {
            return actorId != null &&
                ledgers.TryGetValue(actorId, out DaoProofLedger ledger)
                    ? ledger
                    : null;
        }

        public JindanCoreState GetCore(string actorId)
        {
            return actorId != null &&
                cores.TryGetValue(actorId, out JindanCoreState core)
                    ? core
                    : null;
        }
    }

    public static class JindanProofSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public static JindanProofSaveData Capture(
            IReadOnlyList<DaoProofLedger> ledgers,
            JindanProofCoordinator coordinator,
            JindanPositionRegistry registry,
            IReadOnlyList<JindanCoreState> cores)
        {
            if (ledgers == null)
                throw new ArgumentNullException(nameof(ledgers));
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (cores == null)
                throw new ArgumentNullException(nameof(cores));

            var data = new JindanProofSaveData
            {
                schemaVersion = CurrentSchemaVersion,
                positions = registry.CaptureState()
            };
            foreach (DaoProofLedger ledger in ledgers)
            {
                if (ledger == null)
                    throw new ArgumentException("Ledger cannot be null.", nameof(ledgers));
                data.ledgers.Add(ledger.CaptureState());
            }
            data.ledgers.Sort((a, b) =>
                string.CompareOrdinal(a.actorId, b.actorId));

            foreach (JindanCoreState core in cores)
            {
                if (core == null)
                    throw new ArgumentException("Core cannot be null.", nameof(cores));
                data.cores.Add(core.CaptureState());
            }
            data.cores.Sort((a, b) =>
                string.CompareOrdinal(a.actorId, b.actorId));

            coordinator.CaptureState(
                data.attempts,
                data.regularCompletions,
                data.criticalCompletions,
                data.closedRegularTicks,
                data.closedCriticalTicks);
            ValidateWorldGraph(data);
            return data;
        }

        public static JindanProofRestoredState Restore(JindanProofSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.schemaVersion != CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    "Unsupported jindan proof save schema: " +
                    data.schemaVersion);
            }

            var ledgers = new List<DaoProofLedger>();
            foreach (DaoProofLedgerSaveData ledger in
                data.ledgers ?? new List<DaoProofLedgerSaveData>())
            {
                ledgers.Add(DaoProofLedger.RestoreState(ledger));
            }

            var cores = new List<JindanCoreState>();
            foreach (JindanCoreSaveData core in
                data.cores ?? new List<JindanCoreSaveData>())
            {
                cores.Add(JindanCoreState.RestoreState(core));
            }

            JindanProofCoordinator coordinator =
                JindanProofCoordinator.RestoreState(
                    data.attempts,
                    data.regularCompletions,
                    data.criticalCompletions,
                    data.closedRegularTicks,
                    data.closedCriticalTicks);
            JindanPositionRegistry registry =
                JindanPositionRegistry.RestoreState(data.positions);
            ValidateWorldGraph(data);
            return new JindanProofRestoredState(
                ledgers,
                coordinator,
                registry,
                cores);
        }

        private static void ValidateWorldGraph(JindanProofSaveData data)
        {
            Dictionary<string, DaoProofLedgerSaveData> ledgers =
                IndexLedgers(data.ledgers);
            Dictionary<string, JindanPositionSaveData> positions =
                IndexPositions(data.positions);
            Dictionary<string, JindanProofAttemptSaveData> attempts =
                IndexAttempts(data.attempts);
            Dictionary<string, JindanCoreSaveData> cores =
                IndexCores(data.cores);

            ValidateTickPositions(data.regularCompletions, positions);
            ValidateTickPositions(data.criticalCompletions, positions);
            ValidateTickPositions(data.closedRegularTicks, positions);
            ValidateTickPositions(data.closedCriticalTicks, positions);

            var bindingsByPosition =
                new Dictionary<string, BindingOwner>(StringComparer.Ordinal);
            var coreIds = new HashSet<string>(StringComparer.Ordinal);
            var carrierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JindanCoreSaveData> pair in cores)
            {
                JindanCoreSaveData core = pair.Value;
                if (!ledgers.ContainsKey(core.actorId))
                {
                    throw new ArgumentException(
                        "A core actor must have a proof ledger.",
                        nameof(data));
                }
                if (!string.IsNullOrWhiteSpace(core.coreBindingId) &&
                    !coreIds.Add(core.coreBindingId))
                {
                    throw new ArgumentException(
                        "Duplicate core binding ID.",
                        nameof(data));
                }

                foreach (SeatCarrierBindingSaveData binding in
                    core.seatBindings ?? new List<SeatCarrierBindingSaveData>())
                {
                    if (!positions.TryGetValue(
                            binding.positionId,
                            out JindanPositionSaveData position) ||
                        !string.Equals(
                            position.holderActorId,
                            core.actorId,
                            StringComparison.Ordinal) ||
                        position.seatType != binding.seatType ||
                        !carrierIds.Add(binding.carrierAbilityInstanceId) ||
                        !bindingsByPosition.TryAdd(
                            binding.positionId,
                            new BindingOwner(core.actorId, binding)))
                    {
                        throw new ArgumentException(
                            "Invalid position/core/holder binding reference.",
                            nameof(data));
                    }
                }
            }

            var boundAttemptByPosition =
                new Dictionary<string, JindanProofAttemptSaveData>(
                    StringComparer.Ordinal);
            foreach (JindanProofAttemptSaveData attempt in attempts.Values)
            {
                if (!ledgers.ContainsKey(attempt.actorId) ||
                    !positions.TryGetValue(
                        attempt.positionId,
                        out JindanPositionSaveData position) ||
                    !string.Equals(
                        attempt.profileId,
                        position.profileId,
                        StringComparison.Ordinal) ||
                    attempt.expectedPositionVersion > position.version)
                {
                    throw new ArgumentException(
                        "Invalid attempt/position reference.",
                        nameof(data));
                }

                if (attempt.status == ProofAttemptStatus.Bound)
                {
                    if (!boundAttemptByPosition.TryAdd(
                            attempt.positionId,
                            attempt) ||
                        !bindingsByPosition.TryGetValue(
                            attempt.positionId,
                            out BindingOwner owner) ||
                        !string.Equals(
                            owner.ActorId,
                            attempt.actorId,
                            StringComparison.Ordinal) ||
                        !string.Equals(
                            owner.Binding.carrierAbilityInstanceId,
                            attempt.carrierAbilityInstanceId,
                            StringComparison.Ordinal))
                    {
                        throw new ArgumentException(
                            "Invalid bound attempt reference.",
                            nameof(data));
                    }
                }
                else if (!string.IsNullOrWhiteSpace(position.holderActorId) &&
                    attempt.status != ProofAttemptStatus.Invalidated &&
                    attempt.status != ProofAttemptStatus.Interrupted)
                {
                    throw new ArgumentException(
                        "A live attempt cannot target an occupied position.",
                        nameof(data));
                }
            }

            foreach (JindanPositionSaveData position in positions.Values)
            {
                bool occupied = !string.IsNullOrWhiteSpace(position.holderActorId);
                bool hasBinding = bindingsByPosition.ContainsKey(position.positionId);
                bool hasBoundAttempt =
                    boundAttemptByPosition.ContainsKey(position.positionId);
                if (occupied != hasBinding || occupied != hasBoundAttempt ||
                    occupied && position.version == 0)
                {
                    throw new ArgumentException(
                        "Position occupancy is not closed through core and attempt state.",
                        nameof(data));
                }
            }
        }

        private static Dictionary<string, DaoProofLedgerSaveData> IndexLedgers(
            IReadOnlyList<DaoProofLedgerSaveData> source)
        {
            var result = new Dictionary<string, DaoProofLedgerSaveData>(
                StringComparer.Ordinal);
            foreach (DaoProofLedgerSaveData item in
                source ?? Array.Empty<DaoProofLedgerSaveData>())
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.actorId) ||
                    !result.TryAdd(item.actorId, item))
                {
                    throw new ArgumentException("Invalid or duplicate ledger ID.");
                }
            }
            return result;
        }

        private static Dictionary<string, JindanPositionSaveData> IndexPositions(
            IReadOnlyList<JindanPositionSaveData> source)
        {
            var result = new Dictionary<string, JindanPositionSaveData>(
                StringComparer.Ordinal);
            foreach (JindanPositionSaveData item in
                source ?? Array.Empty<JindanPositionSaveData>())
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.positionId) ||
                    !result.TryAdd(item.positionId, item))
                {
                    throw new ArgumentException("Invalid or duplicate position ID.");
                }
            }
            return result;
        }

        private static Dictionary<string, JindanProofAttemptSaveData> IndexAttempts(
            IReadOnlyList<JindanProofAttemptSaveData> source)
        {
            var result = new Dictionary<string, JindanProofAttemptSaveData>(
                StringComparer.Ordinal);
            foreach (JindanProofAttemptSaveData item in
                source ?? Array.Empty<JindanProofAttemptSaveData>())
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.attemptId) ||
                    !result.TryAdd(item.attemptId, item))
                {
                    throw new ArgumentException("Invalid or duplicate attempt ID.");
                }
            }
            return result;
        }

        private static Dictionary<string, JindanCoreSaveData> IndexCores(
            IReadOnlyList<JindanCoreSaveData> source)
        {
            var result = new Dictionary<string, JindanCoreSaveData>(
                StringComparer.Ordinal);
            foreach (JindanCoreSaveData item in
                source ?? Array.Empty<JindanCoreSaveData>())
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.actorId) ||
                    !result.TryAdd(item.actorId, item))
                {
                    throw new ArgumentException("Invalid or duplicate core actor ID.");
                }
            }
            return result;
        }

        private static void ValidateTickPositions(
            IReadOnlyList<ProofCompletionSaveData> source,
            IReadOnlyDictionary<string, JindanPositionSaveData> positions)
        {
            foreach (ProofCompletionSaveData item in
                source ?? Array.Empty<ProofCompletionSaveData>())
            {
                if (item == null || !positions.ContainsKey(item.positionId))
                {
                    throw new ArgumentException(
                        "Completion references an unknown position.");
                }
            }
        }

        private static void ValidateTickPositions(
            IReadOnlyList<ProofCompletionKeySaveData> source,
            IReadOnlyDictionary<string, JindanPositionSaveData> positions)
        {
            foreach (ProofCompletionKeySaveData item in
                source ?? Array.Empty<ProofCompletionKeySaveData>())
            {
                if (item == null || !positions.ContainsKey(item.positionId))
                {
                    throw new ArgumentException(
                        "Closed completion references an unknown position.");
                }
            }
        }

        private sealed class BindingOwner
        {
            public string ActorId { get; }
            public SeatCarrierBindingSaveData Binding { get; }

            public BindingOwner(
                string actorId,
                SeatCarrierBindingSaveData binding)
            {
                ActorId = actorId;
                Binding = binding;
            }
        }
    }
}
