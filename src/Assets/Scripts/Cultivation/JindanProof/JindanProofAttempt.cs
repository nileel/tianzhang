using System;

namespace TianZhang.Cultivation.JindanProof
{
    public sealed class JindanProofAttempt
    {
        public string AttemptId { get; }
        public string PositionId { get; }
        public string ActorId { get; }
        public string ProfileId { get; }
        public string SiteId { get; }
        public string CarrierAbilityInstanceId { get; }
        public long ExpectedPositionVersion { get; }
        public int RegularProgressTarget { get; }
        public int CriticalProgressTarget { get; }
        public int RegularProgress { get; private set; }
        public int CriticalProgress { get; private set; }
        public int CriticalRound { get; private set; }
        public string InterruptionReason { get; private set; }
        public ProofAttemptStatus Status { get; private set; }

        public JindanProofAttempt(
            string attemptId,
            string positionId,
            string actorId,
            string profileId,
            string siteId,
            string carrierAbilityInstanceId,
            long expectedPositionVersion,
            int regularProgressTarget,
            int criticalProgressTarget)
        {
            RequireId(attemptId, nameof(attemptId));
            RequireId(positionId, nameof(positionId));
            RequireId(actorId, nameof(actorId));
            RequireId(profileId, nameof(profileId));
            RequireId(siteId, nameof(siteId));
            RequireId(carrierAbilityInstanceId, nameof(carrierAbilityInstanceId));
            if (expectedPositionVersion < 0)
                throw new ArgumentOutOfRangeException(nameof(expectedPositionVersion));
            if (regularProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(regularProgressTarget));
            if (criticalProgressTarget <= 0)
                throw new ArgumentOutOfRangeException(nameof(criticalProgressTarget));

            AttemptId = attemptId;
            PositionId = positionId;
            ActorId = actorId;
            ProfileId = profileId;
            SiteId = siteId;
            CarrierAbilityInstanceId = carrierAbilityInstanceId;
            ExpectedPositionVersion = expectedPositionVersion;
            RegularProgressTarget = regularProgressTarget;
            CriticalProgressTarget = criticalProgressTarget;
            Status = ProofAttemptStatus.Active;
        }

        public void AdvanceRegular(int amount, bool hardRequirementsMet)
        {
            if (Status != ProofAttemptStatus.Active)
                throw new InvalidOperationException("Only an active attempt can advance regular proof.");
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            RegularProgress = Math.Min(
                RegularProgressTarget,
                checked(RegularProgress + amount));
            if (RegularProgress == RegularProgressTarget && hardRequirementsMet)
                Status = ProofAttemptStatus.AwaitingRegularTickClose;
        }

        public void EnterCriticalContest()
        {
            if (Status != ProofAttemptStatus.AwaitingRegularTickClose)
                throw new InvalidOperationException("Critical contest requires regular completion.");

            Status = ProofAttemptStatus.CriticalContest;
            CriticalProgress = 0;
            CriticalRound = 1;
        }

        public void AdvanceCritical(int amount)
        {
            if (Status != ProofAttemptStatus.CriticalContest)
                throw new InvalidOperationException("Attempt is not in critical contest.");
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));

            CriticalProgress = Math.Min(
                CriticalProgressTarget,
                checked(CriticalProgress + amount));
            if (CriticalProgress == CriticalProgressTarget)
                Status = ProofAttemptStatus.AwaitingCriticalTickClose;
        }

        public void RestartCriticalRound()
        {
            if (Status != ProofAttemptStatus.AwaitingCriticalTickClose)
            {
                throw new InvalidOperationException(
                    "Only simultaneous critical completion restarts a round.");
            }

            Status = ProofAttemptStatus.CriticalContest;
            CriticalProgress = 0;
            CriticalRound = checked(CriticalRound + 1);
        }

        public void MarkReadyToBind()
        {
            if (Status != ProofAttemptStatus.AwaitingRegularTickClose &&
                Status != ProofAttemptStatus.AwaitingCriticalTickClose)
            {
                throw new InvalidOperationException(
                    "Attempt has not completed a closable stage.");
            }

            Status = ProofAttemptStatus.ReadyToBind;
        }

        public void FatalInterrupt(string reason)
        {
            RequireId(reason, nameof(reason));
            if (Status == ProofAttemptStatus.Bound ||
                Status == ProofAttemptStatus.Invalidated ||
                Status == ProofAttemptStatus.Interrupted)
            {
                throw new InvalidOperationException(
                    "A terminal attempt cannot be interrupted again.");
            }

            RegularProgress = 0;
            CriticalProgress = 0;
            InterruptionReason = reason;
            Status = ProofAttemptStatus.Interrupted;
        }

        public void Invalidate()
        {
            if (Status != ProofAttemptStatus.Bound)
                Status = ProofAttemptStatus.Invalidated;
        }

        public void MarkBound()
        {
            if (Status != ProofAttemptStatus.ReadyToBind)
                throw new InvalidOperationException("Only a ready attempt can bind.");

            Status = ProofAttemptStatus.Bound;
        }

        internal JindanProofAttemptSaveData CaptureState()
        {
            return new JindanProofAttemptSaveData
            {
                attemptId = AttemptId,
                positionId = PositionId,
                actorId = ActorId,
                profileId = ProfileId,
                siteId = SiteId,
                carrierAbilityInstanceId = CarrierAbilityInstanceId,
                expectedPositionVersion = ExpectedPositionVersion,
                regularProgressTarget = RegularProgressTarget,
                criticalProgressTarget = CriticalProgressTarget,
                regularProgress = RegularProgress,
                criticalProgress = CriticalProgress,
                criticalRound = CriticalRound,
                interruptionReason = InterruptionReason,
                status = Status
            };
        }

        internal static JindanProofAttempt RestoreState(
            JindanProofAttemptSaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (!Enum.IsDefined(typeof(ProofAttemptStatus), data.status))
                throw new ArgumentException("Invalid attempt status.", nameof(data));

            var attempt = new JindanProofAttempt(
                data.attemptId,
                data.positionId,
                data.actorId,
                data.profileId,
                data.siteId,
                data.carrierAbilityInstanceId,
                data.expectedPositionVersion,
                data.regularProgressTarget,
                data.criticalProgressTarget);
            if (data.regularProgress < 0 ||
                data.regularProgress > data.regularProgressTarget ||
                data.criticalProgress < 0 ||
                data.criticalProgress > data.criticalProgressTarget ||
                data.criticalRound < 0 ||
                !HasConsistentState(data))
            {
                throw new ArgumentException(
                    "Invalid attempt progress snapshot.",
                    nameof(data));
            }

            attempt.RegularProgress = data.regularProgress;
            attempt.CriticalProgress = data.criticalProgress;
            attempt.CriticalRound = data.criticalRound;
            attempt.InterruptionReason = string.IsNullOrEmpty(data.interruptionReason)
                ? null
                : data.interruptionReason;
            attempt.Status = data.status;
            return attempt;
        }

        private static bool HasConsistentState(JindanProofAttemptSaveData data)
        {
            bool hasInterruption = !string.IsNullOrWhiteSpace(data.interruptionReason);
            switch (data.status)
            {
                case ProofAttemptStatus.Active:
                    return data.criticalProgress == 0 &&
                        data.criticalRound == 0 &&
                        !hasInterruption;
                case ProofAttemptStatus.AwaitingRegularTickClose:
                    return data.regularProgress == data.regularProgressTarget &&
                        data.criticalProgress == 0 &&
                        data.criticalRound == 0 &&
                        !hasInterruption;
                case ProofAttemptStatus.CriticalContest:
                    return data.regularProgress == data.regularProgressTarget &&
                        data.criticalProgress < data.criticalProgressTarget &&
                        data.criticalRound > 0 &&
                        !hasInterruption;
                case ProofAttemptStatus.AwaitingCriticalTickClose:
                    return data.regularProgress == data.regularProgressTarget &&
                        data.criticalProgress == data.criticalProgressTarget &&
                        data.criticalRound > 0 &&
                        !hasInterruption;
                case ProofAttemptStatus.ReadyToBind:
                case ProofAttemptStatus.Bound:
                    return data.regularProgress == data.regularProgressTarget &&
                        IsCompletedClosingStage(data) &&
                        !hasInterruption;
                case ProofAttemptStatus.Interrupted:
                    return data.regularProgress == 0 &&
                        data.criticalProgress == 0 &&
                        hasInterruption;
                case ProofAttemptStatus.Invalidated:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsCompletedClosingStage(JindanProofAttemptSaveData data)
        {
            return (data.criticalRound == 0 && data.criticalProgress == 0) ||
                (data.criticalRound > 0 &&
                    data.criticalProgress == data.criticalProgressTarget);
        }

        private static void RequireId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A non-empty ID is required.", parameterName);
        }
    }
}
