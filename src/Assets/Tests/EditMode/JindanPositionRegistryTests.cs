using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanPositionRegistryTests
    {
        [Test]
        public void FirstSeatAtomicallyCreatesCoreAndCarrierBinding()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_first", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_source_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(core.CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(core.SeatBindings, Has.Count.EqualTo(1));
            Assert.That(registry.Get("position_fire_source_01").HolderActorId,
                Is.EqualTo("actor_player"));
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.Bound));
        }

        [Test]
        public void AdditionalSeatKeepsOriginalCoreBindingId()
        {
            var coordinator = new JindanProofCoordinator();
            var registry =
                JindanProofTestFixtures.RegistryWithVacantFireSourceAndTransformation();
            var core = new JindanCoreState("actor_player");
            JindanProofTestFixtures.BindFirstSeat(registry, core, coordinator);
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_second", "actor_player",
                "position_fire_transformation_01", "jindan_fire_transformation",
                "ability_transformation_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, null, true, true),
                JindanProofTestFixtures.FireTransformationProfile(),
                JindanProofTestFixtures.EligibleFireTransformationLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.True, result.FailureReason.ToString());
            Assert.That(core.CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(core.SeatBindings, Has.Count.EqualTo(2));
        }

        [Test]
        public void StaleVersionFailsWithoutPartialBinding()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            registry.Get("position_fire_source_01").AdvanceVersionForWorldChange();
            var core = new JindanCoreState("actor_player");
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_stale", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_source_actor_player", 0);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason,
                Is.EqualTo(JindanBindFailureReason.StalePositionVersion));
            Assert.That(core.CoreBindingId, Is.Null);
            Assert.That(core.SeatBindings, Is.Empty);
            Assert.That(registry.Get("position_fire_source_01").HolderActorId, Is.Null);
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.ReadyToBind));
        }

        [Test]
        public void SuccessfulBindInvalidatesEveryOtherAttemptForThePosition()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var winner = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_winner", "actor_player", "position_fire_source_01",
                "jindan_fire_source", "ability_winner", 0);
            var loser = JindanProofTestFixtures.NewAttempt(
                "attempt_loser", "actor_loser");
            coordinator.Register(loser);

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(winner.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                new JindanCoreState("actor_player"),
                coordinator);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(loser.Status, Is.EqualTo(ProofAttemptStatus.Invalidated));
        }

        [Test]
        public void LedgerActorMustMatchAttemptActorWithoutPartialBinding()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_ledger_mismatch", "actor_player",
                "position_fire_source_01", "jindan_fire_source",
                "ability_source_actor_player", 0);
            var core = new JindanCoreState("actor_player");

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                new DaoProofLedger("actor_other"),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason,
                Is.EqualTo(JindanBindFailureReason.PreconditionsNotMet));
            AssertUnchanged(registry, core, attempt, 0);
        }

        [Test]
        public void MaximumPositionVersionFailsClosedBeforeAnyMutation()
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource(long.MaxValue);
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_max_version", "actor_player",
                "position_fire_source_01", "jindan_fire_source",
                "ability_source_actor_player", long.MaxValue);
            var core = new JindanCoreState("actor_player");

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(attempt.AttemptId, "core_actor_player", true, true),
                JindanProofTestFixtures.FireSourceProfile(),
                JindanProofTestFixtures.EligibleFireLedger(),
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason,
                Is.EqualTo(JindanBindFailureReason.CoreInvariantViolation));
            AssertUnchanged(registry, core, attempt, long.MaxValue);
        }

        [TestCase(false, true, true)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void SiteCarrierAndHardRequirementFailuresUseSameNonLeakingReason(
            bool siteStillValid,
            bool carrierStillCompatible,
            bool useEligibleLedger)
        {
            var coordinator = new JindanProofCoordinator();
            var registry = JindanProofTestFixtures.RegistryWithVacantFireSource();
            var attempt = JindanProofTestFixtures.ReadyAttempt(
                coordinator, "attempt_hidden_precondition", "actor_player",
                "position_fire_source_01", "jindan_fire_source",
                "ability_source_actor_player", 0);
            var core = new JindanCoreState("actor_player");
            DaoProofLedger ledger = useEligibleLedger
                ? JindanProofTestFixtures.EligibleFireLedger()
                : new DaoProofLedger("actor_player");

            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(
                    attempt.AttemptId,
                    "core_actor_player",
                    siteStillValid,
                    carrierStillCompatible),
                JindanProofTestFixtures.FireSourceProfile(),
                ledger,
                core,
                coordinator);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.FailureReason,
                Is.EqualTo(JindanBindFailureReason.PreconditionsNotMet));
            AssertUnchanged(registry, core, attempt, 0);
        }

        private static void AssertUnchanged(
            JindanPositionRegistry registry,
            JindanCoreState core,
            JindanProofAttempt attempt,
            long expectedVersion)
        {
            Assert.That(core.CoreBindingId, Is.Null);
            Assert.That(core.SeatBindings, Is.Empty);
            Assert.That(registry.Get("position_fire_source_01").HolderActorId, Is.Null);
            Assert.That(registry.Get("position_fire_source_01").Version,
                Is.EqualTo(expectedVersion));
            Assert.That(attempt.Status, Is.EqualTo(ProofAttemptStatus.ReadyToBind));
            Assert.That(core.ActorId, Is.EqualTo("actor_player"));
        }
    }
}
