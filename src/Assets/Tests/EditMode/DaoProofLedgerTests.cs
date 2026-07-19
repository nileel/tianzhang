using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class DaoProofLedgerTests
    {
        [Test]
        public void OneAcceptedBehaviorCanAdvanceMultipleSharedMetricsAndAchievement()
        {
            var ledger = new DaoProofLedger("actor_player");
            var rules = JindanProofTestFixtures.FireRules();
            var behavior = JindanProofTestFixtures.FireBehavior(
                "event_1", "target_bandit_camp", "region_jiangzuo", 3,
                new[]
                {
                    new DaoProofContribution("fire_seed_count", 1),
                    new DaoProofContribution("valid_ignition_count", 2)
                },
                new[] { "fire_source_precise_ignition" });

            Assert.That(ledger.TryRecord(behavior, rules), Is.True);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.EqualTo(1));
            Assert.That(ledger.GetMetricValue("valid_ignition_count"), Is.EqualTo(2));
            Assert.That(ledger.HasAchievement("fire_source_precise_ignition"), Is.True);
        }

        [Test]
        public void ReplayedEventAndRepeatedTargetDoNotFarmProgress()
        {
            var ledger = new DaoProofLedger("actor_player");
            var rules = JindanProofTestFixtures.FireRules();
            var first = JindanProofTestFixtures.FireBehavior(
                "event_1", "target_dummy", "region_jiangzuo", 3,
                new[] { new DaoProofContribution("valid_ignition_count", 1) });
            var repeatedTarget = JindanProofTestFixtures.FireBehavior(
                "event_2", "target_dummy", "region_guanzhong", 3,
                new[] { new DaoProofContribution("valid_ignition_count", 1) });

            Assert.That(ledger.TryRecord(first, rules), Is.True);
            Assert.That(ledger.TryRecord(first, rules), Is.False);
            Assert.That(ledger.TryRecord(repeatedTarget, rules), Is.False);
            Assert.That(ledger.GetMetricValue("valid_ignition_count"), Is.EqualTo(1));
        }

        [Test]
        public void LowChallengeBehaviorDoesNotCount()
        {
            var ledger = new DaoProofLedger("actor_player");
            var behavior = JindanProofTestFixtures.FireBehavior(
                "event_low", "target_straw", "region_jiangzuo", 0,
                new[] { new DaoProofContribution("fire_seed_count", 1) });

            Assert.That(
                ledger.TryRecord(behavior, JindanProofTestFixtures.FireRules()),
                Is.False);
            Assert.That(ledger.GetMetricValue("fire_seed_count"), Is.Zero);
        }
    }
}
