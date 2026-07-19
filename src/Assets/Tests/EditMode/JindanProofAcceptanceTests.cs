using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofAcceptanceTests
    {
        [Test]
        public void EarlyHiddenHistoryAppearsAfterKnowledgeAndCanPowerFirstBinding()
        {
            DaoProofLedger ledger = JindanProofTestFixtures.EligibleFireLedger();
            Assert.That(JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(), ledger,
                ProofKnowledgeLevel.Unknown, 80, 70).Requirements, Is.Empty);
            Assert.That(JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(), ledger,
                ProofKnowledgeLevel.FullProfile, 80, 70).Requirements,
                Has.All.Matches<ProofRequirementView>(item => item.IsMet));

            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            JindanProofAttempt attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_acceptance", "actor_player",
                "position_fire_source_01", "jindan_fire_source", "ability_acceptance", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_acceptance", true, true),
                JindanProofTestFixtures.FireSourceProfile(), ledger, core, coordinator);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(core.CoreBindingId, Is.EqualTo("core_acceptance"));
        }

        [Test]
        public void UnqualifiedAttemptNeverBindsEvenWithValidSiteAndCarrier()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var attempt = JindanProofTestFixtures.NewAttempt("attempt_unqualified");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, false);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_invalid", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                new DaoProofLedger("actor_player"),
                new JindanCoreState("actor_player"),
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(JindanBindFailureReason.AttemptNotReady));
        }

        [Test]
        public void LaterUniqueCriticalCompletionWinsWithoutRandomOrIdPriority()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt zAttempt = JindanProofTestFixtures.NewAttempt(
                "z_attempt", "actor_z");
            JindanProofAttempt aAttempt = JindanProofTestFixtures.NewAttempt(
                "a_attempt", "actor_a");
            coordinator.Register(zAttempt);
            coordinator.Register(aAttempt);
            zAttempt.AdvanceRegular(100, true);
            aAttempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(zAttempt.AttemptId, 6000);
            coordinator.SubmitRegularCompletion(aAttempt.AttemptId, 6000);
            coordinator.CloseRegularTick(zAttempt.PositionId, 6000);
            zAttempt.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(zAttempt.AttemptId, 7000);

            ProofTickResolution result = coordinator.CloseCriticalTick(
                zAttempt.PositionId, 7000);

            Assert.That(result.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(result.UniqueAttemptId, Is.EqualTo(zAttempt.AttemptId));
            Assert.That(aAttempt.Status, Is.EqualTo(ProofAttemptStatus.CriticalContest));
        }

        [Test]
        public void AdaptationAndForecastCannotChangeEligibilityOrBindPriority()
        {
            var emptyLedger = new DaoProofLedger("actor_player");
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            JindanProofView view = JindanProofKnowledge.Project(
                profile, emptyLedger, ProofKnowledgeLevel.FullProfile, 100, 100);

            Assert.That(view.AdaptationPercent, Is.EqualTo(100));
            Assert.That(view.EstimatedSuccessPercent, Is.EqualTo(100));
            Assert.That(JindanProofEligibility.Evaluate(profile, emptyLedger).IsSatisfied, Is.False);
        }
    }
}
