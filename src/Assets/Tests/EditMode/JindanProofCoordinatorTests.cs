using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofCoordinatorTests
    {
        [Test]
        public void OneRegularCompletionBecomesReadyOnlyWhenWorldTickCloses()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt =
                JindanProofTestFixtures.NewAttempt("attempt_a", "actor_a");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);

            coordinator.SubmitRegularCompletion(attempt.AttemptId, 1000);
            Assert.That(
                attempt.Status,
                Is.EqualTo(ProofAttemptStatus.AwaitingRegularTickClose));

            ProofTickResolution result =
                coordinator.CloseRegularTick(attempt.PositionId, 1000);

            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(result.UniqueAttemptId, Is.EqualTo(attempt.AttemptId));
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.ReadyToBind));
        }

        [Test]
        public void SameTickRegularCompletionsEnterCriticalContestWithoutIdTiebreak()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt z =
                JindanProofTestFixtures.NewAttempt("z_attempt", "actor_z");
            JindanProofAttempt a =
                JindanProofTestFixtures.NewAttempt("a_attempt", "actor_a");
            coordinator.Register(z);
            coordinator.Register(a);
            z.AdvanceRegular(100, true);
            a.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(z.AttemptId, 2000);
            coordinator.SubmitRegularCompletion(a.AttemptId, 2000);

            ProofTickResolution result =
                coordinator.CloseRegularTick(z.PositionId, 2000);

            Assert.That(
                result.Kind,
                Is.EqualTo(ProofTickResolutionKind.CriticalContestContinues));
            Assert.That(result.UniqueAttemptId, Is.Null);
            Assert.That(
                result.ParticipantAttemptIds,
                Is.EqualTo(new[] { "a_attempt", "z_attempt" }));
            Assert.That(z.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
            Assert.That(a.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }

        [Test]
        public void SameTickCriticalCompletionsStartAnotherRound()
        {
            JindanProofCoordinator coordinator =
                JindanProofTestFixtures.CriticalContest(
                    out JindanProofAttempt a,
                    out JindanProofAttempt b);
            a.AdvanceCritical(20);
            b.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(a.AttemptId, 3000);
            coordinator.SubmitCriticalCompletion(b.AttemptId, 3000);

            ProofTickResolution result =
                coordinator.CloseCriticalTick(a.PositionId, 3000);

            Assert.That(
                result.Kind,
                Is.EqualTo(ProofTickResolutionKind.CriticalContestContinues));
            Assert.That(a.CriticalRound, Is.EqualTo(2));
            Assert.That(b.CriticalRound, Is.EqualTo(2));
            Assert.That(a.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
            Assert.That(b.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }

        [Test]
        public void OneAttemptCannotBeSubmittedToTwoOpenRegularTicks()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt =
                JindanProofTestFixtures.NewAttempt("attempt_a", "actor_a");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(attempt.AttemptId, 4000);

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.SubmitRegularCompletion(attempt.AttemptId, 4001));

            ProofTickResolution result =
                coordinator.CloseRegularTick(attempt.PositionId, 4000);
            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(result.UniqueAttemptId, Is.EqualTo(attempt.AttemptId));
        }

        [Test]
        public void ClosedRegularTickIsIdempotentAndRejectsLateSubmission()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt first =
                JindanProofTestFixtures.NewAttempt("attempt_first", "actor_first");
            JindanProofAttempt late =
                JindanProofTestFixtures.NewAttempt("attempt_late", "actor_late");
            coordinator.Register(first);
            coordinator.Register(late);
            first.AdvanceRegular(100, true);
            late.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(first.AttemptId, 5000);

            ProofTickResolution firstClose =
                coordinator.CloseRegularTick(first.PositionId, 5000);

            Assert.That(firstClose.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.Throws<InvalidOperationException>(() =>
                coordinator.SubmitRegularCompletion(late.AttemptId, 5000));
            ProofTickResolution repeatedClose =
                coordinator.CloseRegularTick(first.PositionId, 5000);
            Assert.That(
                repeatedClose.Kind,
                Is.EqualTo(ProofTickResolutionKind.NoCompletion));
            Assert.That(
                late.Status,
                Is.EqualTo(ProofAttemptStatus.AwaitingRegularTickClose));
        }

        [Test]
        public void ClosedCriticalTickIsIdempotentAndRejectsLateSubmission()
        {
            JindanProofCoordinator coordinator =
                JindanProofTestFixtures.CriticalContest(
                    out JindanProofAttempt first,
                    out JindanProofAttempt late);
            first.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(first.AttemptId, 6000);

            ProofTickResolution firstClose =
                coordinator.CloseCriticalTick(first.PositionId, 6000);
            late.AdvanceCritical(20);

            Assert.That(firstClose.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.Throws<InvalidOperationException>(() =>
                coordinator.SubmitCriticalCompletion(late.AttemptId, 6000));
            ProofTickResolution repeatedClose =
                coordinator.CloseCriticalTick(first.PositionId, 6000);
            Assert.That(
                repeatedClose.Kind,
                Is.EqualTo(ProofTickResolutionKind.NoCompletion));
            Assert.That(
                late.Status,
                Is.EqualTo(ProofAttemptStatus.AwaitingCriticalTickClose));
        }
    }
}
