using System;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofDefinitionTests
    {
        [Test]
        public void FireSourceFixtureContainsSharedMetricsAndSignatureAchievement()
        {
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            Assert.That(profile.ProfileId, Is.EqualTo("jindan_fire_source"));
            Assert.That(profile.RoadId, Is.EqualTo("fire"));
            Assert.That(profile.SeatType, Is.EqualTo(JindanSeatType.Source));
            Assert.That(profile.Requirements, Has.Count.EqualTo(3));
            Assert.That(profile.RegularProgressTarget, Is.EqualTo(100));
            Assert.That(profile.CriticalProgressTarget, Is.EqualTo(20));
        }

        [Test]
        public void DuplicateRequirementIdsFailClosed()
        {
            var requirement = new JindanProofRequirement(
                "fire_seed_count", ProofRequirementType.SharedMetric, 3);

            Assert.Throws<ArgumentException>(() => new JindanProofProfileDefinition(
                "jindan_fire_source",
                "fire",
                JindanSeatType.Source,
                new[] { requirement, requirement },
                100,
                20));
        }
    }
}
