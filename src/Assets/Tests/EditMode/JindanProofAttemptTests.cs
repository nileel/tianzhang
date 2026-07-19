using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofAttemptTests
    {
        [Test]
        public void UnqualifiedPlayerCanStartButCannotReachTickClose()
        {
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");

            attempt.AdvanceRegular(100, hardRequirementsMet: false);

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Active));
            Assert.That(attempt.RegularProgress, Is.EqualTo(100));
        }

        [Test]
        public void QualifiedCandidateReachesRegularTickCloseWithoutRandomRoll()
        {
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");

            attempt.AdvanceRegular(100, hardRequirementsMet: true);

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.AwaitingRegularTickClose));
        }

        [Test]
        public void FatalInterruptionClearsAttemptProgressOnly()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_player");
            attempt.AdvanceRegular(100, hardRequirementsMet: true);
            attempt.EnterCriticalContest();
            attempt.AdvanceCritical(12);

            attempt.FatalInterrupt("left_proof_boundary");

            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Interrupted));
            Assert.That(attempt.RegularProgress, Is.Zero);
            Assert.That(attempt.CriticalProgress, Is.Zero);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.EqualTo(3));
            Assert.That(ledger.HasAchievement("fire_source_precise_ignition"), Is.True);
        }

        [TestCase(ProofAttemptStatus.Bound)]
        [TestCase(ProofAttemptStatus.Invalidated)]
        [TestCase(ProofAttemptStatus.Interrupted)]
        public void TerminalAttemptCannotBeFatallyInterruptedAgain(ProofAttemptStatus terminalStatus)
        {
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_terminal");
            MoveToTerminalStatus(attempt, terminalStatus);

            Assert.Throws<InvalidOperationException>(
                () => attempt.FatalInterrupt("second_fatal_interruption"));
            Assert.That(attempt.Status, Is.EqualTo(terminalStatus));
        }

        private static void MoveToTerminalStatus(
            JindanProofAttempt attempt,
            ProofAttemptStatus terminalStatus)
        {
            switch (terminalStatus)
            {
                case ProofAttemptStatus.Bound:
                    attempt.AdvanceRegular(100, hardRequirementsMet: true);
                    attempt.MarkReadyToBind();
                    attempt.MarkBound();
                    break;
                case ProofAttemptStatus.Invalidated:
                    attempt.Invalidate();
                    break;
                case ProofAttemptStatus.Interrupted:
                    attempt.FatalInterrupt("first_fatal_interruption");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(terminalStatus));
            }
        }
    }
}
