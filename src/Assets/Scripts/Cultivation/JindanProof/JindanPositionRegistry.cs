using System;
using System.Collections.Generic;

namespace TianZhang.Cultivation.JindanProof
{
    public enum JindanBindFailureReason
    {
        None,
        AttemptNotReady,
        PositionUnavailable,
        StalePositionVersion,
        PreconditionsNotMet,
        CoreInvariantViolation
    }

    public sealed class JindanBindResult
    {
        public bool Succeeded { get; }
        public JindanBindFailureReason FailureReason { get; }

        public JindanBindResult(bool succeeded, JindanBindFailureReason failureReason)
        {
            Succeeded = succeeded;
            FailureReason = failureReason;
        }
    }

    public sealed class JindanBindRequest
    {
        public string AttemptId { get; }
        public string NewCoreBindingId { get; }
        public bool SiteStillValid { get; }
        public bool CarrierStillCompatible { get; }

        public JindanBindRequest(
            string attemptId,
            string newCoreBindingId,
            bool siteStillValid,
            bool carrierStillCompatible)
        {
            RequireId(attemptId, nameof(attemptId));

            AttemptId = attemptId;
            NewCoreBindingId = string.IsNullOrWhiteSpace(newCoreBindingId)
                ? null
                : newCoreBindingId;
            SiteStillValid = siteStillValid;
            CarrierStillCompatible = carrierStillCompatible;
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }

    public sealed class JindanPositionRecord
    {
        public string PositionId { get; }
        public string ProfileId { get; }
        public JindanSeatType SeatType { get; }
        public JindanPositionVisibility Visibility { get; private set; }
        public string HolderActorId { get; private set; }
        public long Version { get; private set; }

        public JindanPositionRecord(
            string positionId,
            string profileId,
            JindanSeatType seatType,
            JindanPositionVisibility visibility,
            long version = 0)
        {
            RequireId(positionId, nameof(positionId));
            RequireId(profileId, nameof(profileId));
            if (version < 0)
                throw new ArgumentOutOfRangeException(nameof(version));

            PositionId = positionId;
            ProfileId = profileId;
            SeatType = seatType;
            Visibility = visibility;
            Version = version;
        }

        public void AdvanceVersionForWorldChange()
        {
            long nextVersion = checked(Version + 1);
            Version = nextVersion;
        }

        internal bool CanAdvanceVersion => Version < long.MaxValue;

        internal void Bind(string actorId)
        {
            RequireId(actorId, nameof(actorId));
            if (HolderActorId != null)
                throw new InvalidOperationException("The position is already occupied.");

            long nextVersion = checked(Version + 1);
            HolderActorId = actorId;
            Version = nextVersion;
        }

        internal JindanPositionSaveData CaptureState()
        {
            return new JindanPositionSaveData
            {
                positionId = PositionId,
                profileId = ProfileId,
                seatType = SeatType,
                visibility = Visibility,
                holderActorId = HolderActorId,
                version = Version
            };
        }

        internal static JindanPositionRecord RestoreState(
            JindanPositionSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (!Enum.IsDefined(typeof(JindanSeatType), data.seatType) ||
                !Enum.IsDefined(
                    typeof(JindanPositionVisibility),
                    data.visibility) ||
                !string.IsNullOrEmpty(data.holderActorId) &&
                string.IsNullOrWhiteSpace(data.holderActorId))
            {
                throw new ArgumentException(
                    "Invalid position snapshot.",
                    nameof(data));
            }

            var position = new JindanPositionRecord(
                data.positionId,
                data.profileId,
                data.seatType,
                data.visibility,
                data.version);
            position.HolderActorId = string.IsNullOrEmpty(data.holderActorId)
                ? null
                : data.holderActorId;
            return position;
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }

    public sealed class SeatCarrierBinding
    {
        public string PositionId { get; }
        public JindanSeatType SeatType { get; }
        public string CarrierAbilityInstanceId { get; }

        public SeatCarrierBinding(
            string positionId,
            JindanSeatType seatType,
            string carrierAbilityInstanceId)
        {
            RequireId(positionId, nameof(positionId));
            RequireId(carrierAbilityInstanceId, nameof(carrierAbilityInstanceId));

            PositionId = positionId;
            SeatType = seatType;
            CarrierAbilityInstanceId = carrierAbilityInstanceId;
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }

    public sealed class JindanCoreState
    {
        private readonly List<SeatCarrierBinding> seatBindings =
            new List<SeatCarrierBinding>();

        public string ActorId { get; }
        public string CoreBindingId { get; private set; }
        public IReadOnlyList<SeatCarrierBinding> SeatBindings => seatBindings;

        public JindanCoreState(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                throw new ArgumentException("Actor ID is required.", nameof(actorId));

            ActorId = actorId;
        }

        internal bool CanAdd(
            JindanPositionRecord position,
            JindanProofAttempt attempt,
            string newCoreBindingId)
        {
            if (!string.Equals(ActorId, attempt.ActorId, StringComparison.Ordinal))
                return false;
            if (CoreBindingId == null && string.IsNullOrWhiteSpace(newCoreBindingId))
                return false;
            if (CoreBindingId != null && !string.IsNullOrWhiteSpace(newCoreBindingId))
                return false;

            foreach (SeatCarrierBinding binding in seatBindings)
            {
                if (binding.SeatType == position.SeatType)
                    return false;
                if (string.Equals(
                        binding.PositionId,
                        position.PositionId,
                        StringComparison.Ordinal))
                    return false;
                if (string.Equals(
                        binding.CarrierAbilityInstanceId,
                        attempt.CarrierAbilityInstanceId,
                        StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        internal void Add(
            JindanPositionRecord position,
            JindanProofAttempt attempt,
            string newCoreBindingId)
        {
            var binding = new SeatCarrierBinding(
                position.PositionId,
                position.SeatType,
                attempt.CarrierAbilityInstanceId);

            if (CoreBindingId == null)
                CoreBindingId = newCoreBindingId;
            seatBindings.Add(binding);
        }

        internal JindanCoreSaveData CaptureState()
        {
            var data = new JindanCoreSaveData
            {
                actorId = ActorId,
                coreBindingId = CoreBindingId
            };
            foreach (SeatCarrierBinding binding in seatBindings)
            {
                data.seatBindings.Add(new SeatCarrierBindingSaveData
                {
                    positionId = binding.PositionId,
                    seatType = binding.SeatType,
                    carrierAbilityInstanceId = binding.CarrierAbilityInstanceId
                });
            }

            return data;
        }

        internal static JindanCoreState RestoreState(JindanCoreSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var core = new JindanCoreState(data.actorId);
            var bindings = data.seatBindings ??
                new List<SeatCarrierBindingSaveData>();
            bool hasCore = !string.IsNullOrWhiteSpace(data.coreBindingId);
            if ((!hasCore &&
                    (!string.IsNullOrEmpty(data.coreBindingId) || bindings.Count != 0)) ||
                (hasCore && (bindings.Count < 1 || bindings.Count > 3)))
            {
                throw new ArgumentException(
                    "Core and seat count are inconsistent.",
                    nameof(data));
            }

            var seatTypes = new HashSet<JindanSeatType>();
            var positionIds = new HashSet<string>(StringComparer.Ordinal);
            var carrierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SeatCarrierBindingSaveData binding in bindings)
            {
                if (binding == null ||
                    !Enum.IsDefined(typeof(JindanSeatType), binding.seatType) ||
                    string.IsNullOrWhiteSpace(binding.positionId) ||
                    string.IsNullOrWhiteSpace(binding.carrierAbilityInstanceId) ||
                    !seatTypes.Add(binding.seatType) ||
                    !positionIds.Add(binding.positionId) ||
                    !carrierIds.Add(binding.carrierAbilityInstanceId))
                {
                    throw new ArgumentException(
                        "Invalid or duplicate seat binding.",
                        nameof(data));
                }
            }

            core.CoreBindingId = hasCore ? data.coreBindingId : null;
            foreach (SeatCarrierBindingSaveData binding in bindings)
            {
                core.seatBindings.Add(new SeatCarrierBinding(
                    binding.positionId,
                    binding.seatType,
                    binding.carrierAbilityInstanceId));
            }

            return core;
        }
    }

    public sealed class JindanPositionRegistry
    {
        private readonly Dictionary<string, JindanPositionRecord> positions =
            new Dictionary<string, JindanPositionRecord>(StringComparer.Ordinal);

        public void Add(JindanPositionRecord position)
        {
            if (position == null)
                throw new ArgumentNullException(nameof(position));
            if (!positions.TryAdd(position.PositionId, position))
            {
                throw new ArgumentException(
                    "Position ID already exists.", nameof(position));
            }
        }

        public JindanPositionRecord Get(string positionId)
        {
            return positionId != null &&
                positions.TryGetValue(positionId, out JindanPositionRecord position)
                    ? position
                    : null;
        }

        public JindanBindResult TryBind(
            JindanBindRequest request,
            JindanProofProfileDefinition profile,
            DaoProofLedger ledger,
            JindanCoreState core,
            JindanProofCoordinator coordinator)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (ledger == null)
                throw new ArgumentNullException(nameof(ledger));
            if (core == null)
                throw new ArgumentNullException(nameof(core));
            if (coordinator == null)
                throw new ArgumentNullException(nameof(coordinator));

            JindanProofAttempt attempt = coordinator.GetAttempt(request.AttemptId);
            if (attempt == null || attempt.Status != ProofAttemptStatus.ReadyToBind)
                return Failed(JindanBindFailureReason.AttemptNotReady);

            JindanPositionRecord position = Get(attempt.PositionId);
            if (position == null || position.HolderActorId != null)
                return Failed(JindanBindFailureReason.PositionUnavailable);
            if (position.Version != attempt.ExpectedPositionVersion)
                return Failed(JindanBindFailureReason.StalePositionVersion);

            if (!string.Equals(
                    position.ProfileId,
                    profile.ProfileId,
                    StringComparison.Ordinal) ||
                position.SeatType != profile.SeatType ||
                !string.Equals(
                    attempt.ProfileId,
                    profile.ProfileId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    ledger.ActorId,
                    attempt.ActorId,
                    StringComparison.Ordinal) ||
                !request.SiteStillValid ||
                !request.CarrierStillCompatible ||
                !JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied)
            {
                return Failed(JindanBindFailureReason.PreconditionsNotMet);
            }

            if (!position.CanAdvanceVersion ||
                !core.CanAdd(position, attempt, request.NewCoreBindingId))
            {
                return Failed(JindanBindFailureReason.CoreInvariantViolation);
            }

            core.Add(position, attempt, request.NewCoreBindingId);
            position.Bind(attempt.ActorId);
            attempt.MarkBound();
            coordinator.InvalidateOthers(position.PositionId, attempt.AttemptId);
            return new JindanBindResult(true, JindanBindFailureReason.None);
        }

        internal List<JindanPositionSaveData> CaptureState()
        {
            var data = new List<JindanPositionSaveData>();
            foreach (JindanPositionRecord position in positions.Values)
                data.Add(position.CaptureState());
            data.Sort((a, b) => string.CompareOrdinal(a.positionId, b.positionId));
            return data;
        }

        internal static JindanPositionRegistry RestoreState(
            IReadOnlyList<JindanPositionSaveData> data)
        {
            var registry = new JindanPositionRegistry();
            foreach (JindanPositionSaveData item in
                data ?? Array.Empty<JindanPositionSaveData>())
            {
                registry.Add(JindanPositionRecord.RestoreState(item));
            }

            return registry;
        }

        private static JindanBindResult Failed(JindanBindFailureReason reason)
        {
            return new JindanBindResult(false, reason);
        }
    }
}
