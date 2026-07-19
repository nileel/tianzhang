using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class NpcJindanProofPolicyTests
    {
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void LifespanPressureNeverBypassesRealmOrProofRequirements(
            bool purpleMansionComplete,
            bool hardRequirementsMet)
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.IsPurpleMansionComplete = purpleMansionComplete;
            input.HardRequirementsMet = hardRequirementsMet;
            input.DaysOfLifeRemaining = 1;
            input.SubjectiveSuccessPercent = 100;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(NpcProofDecisionReason.HardGateFailed));
        }

        [Test]
        public void LowSubjectiveChanceStopsHealthyNpcButNotDyingNpc()
        {
            NpcProofDecisionInput healthy = JindanProofTestFixtures.ReadyNpcInput();
            healthy.SubjectiveSuccessPercent = 45;
            healthy.DaysOfLifeRemaining = 2000;
            NpcProofDecisionInput dying = JindanProofTestFixtures.ReadyNpcInput();
            dying.SubjectiveSuccessPercent = 45;
            dying.DaysOfLifeRemaining = 30;

            NpcProofDecision healthyDecision =
                JindanProofTestFixtures.NpcPolicy().Evaluate(healthy);
            NpcProofDecision dyingDecision =
                JindanProofTestFixtures.NpcPolicy().Evaluate(dying);

            Assert.That(healthyDecision.ShouldStart, Is.False);
            Assert.That(healthyDecision.Reason,
                Is.EqualTo(NpcProofDecisionReason.SubjectiveRiskTooHigh));
            Assert.That(healthyDecision.RequiredSubjectivePercent, Is.EqualTo(55));
            Assert.That(dyingDecision.ShouldStart, Is.True);
            Assert.That(dyingDecision.RequiredSubjectivePercent, Is.EqualTo(35));
        }

        [TestCase(nameof(NpcProofDecisionInput.IsPersistentNpc))]
        [TestCase(nameof(NpcProofDecisionInput.KnowsVacancy))]
        [TestCase(nameof(NpcProofDecisionInput.KnowsUsableSite))]
        [TestCase(nameof(NpcProofDecisionInput.HasCompatibleCarrier))]
        [TestCase(nameof(NpcProofDecisionInput.HasFacilities))]
        [TestCase(nameof(NpcProofDecisionInput.HasResources))]
        [TestCase(nameof(NpcProofDecisionInput.HasGuard))]
        public void MissingRequiredCapabilityAlwaysFailsHardGate(string fieldName)
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            typeof(NpcProofDecisionInput).GetField(fieldName).SetValue(input, false);
            input.DaysOfLifeRemaining = 1;
            input.SubjectiveSuccessPercent = 100;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(NpcProofDecisionReason.HardGateFailed));
        }

        [Test]
        public void HigherPrioritySurvivalDutyAlwaysFailsHardGate()
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.HasHigherPrioritySurvivalDuty = true;
            input.DaysOfLifeRemaining = 1;
            input.SubjectiveSuccessPercent = 100;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(NpcProofDecisionReason.HardGateFailed));
        }

        [Test]
        public void DecisionInputContainsOnlySubjectiveNpcFacts()
        {
            string[] expectedFields =
            {
                nameof(NpcProofDecisionInput.IsPersistentNpc),
                nameof(NpcProofDecisionInput.IsPurpleMansionComplete),
                nameof(NpcProofDecisionInput.HardRequirementsMet),
                nameof(NpcProofDecisionInput.KnowsVacancy),
                nameof(NpcProofDecisionInput.KnowsUsableSite),
                nameof(NpcProofDecisionInput.HasCompatibleCarrier),
                nameof(NpcProofDecisionInput.HasFacilities),
                nameof(NpcProofDecisionInput.HasResources),
                nameof(NpcProofDecisionInput.HasGuard),
                nameof(NpcProofDecisionInput.HasHigherPrioritySurvivalDuty),
                nameof(NpcProofDecisionInput.RiskDisposition),
                nameof(NpcProofDecisionInput.SubjectiveSuccessPercent),
                nameof(NpcProofDecisionInput.DaysOfLifeRemaining)
            };

            string[] actualFields = typeof(NpcProofDecisionInput)
                .GetFields()
                .Select(field => field.Name)
                .ToArray();

            Assert.That(actualFields, Is.EquivalentTo(expectedFields));
            Assert.That(actualFields, Has.None.Matches<string>(field =>
                field.IndexOf("True", StringComparison.OrdinalIgnoreCase) >= 0 ||
                field.IndexOf("Backend", StringComparison.OrdinalIgnoreCase) >= 0 ||
                field.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0 ||
                field.IndexOf("Rival", StringComparison.OrdinalIgnoreCase) >= 0 ||
                field.IndexOf("Competitor", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void PolicyRequiresExternallyProvidedThresholds()
        {
            Assert.That(typeof(NpcJindanProofPolicy).GetConstructor(Type.EmptyTypes), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NpcJindanProofPolicy(101, 55, 40, 180, 20));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new NpcJindanProofPolicy(70, 55, 40, -1, 20));
        }

        [TestCase(-1)]
        [TestCase(101)]
        public void InvalidSubjectiveSuccessPercentFailsClosed(int value)
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.SubjectiveSuccessPercent = value;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JindanProofTestFixtures.NpcPolicy().Evaluate(input));
        }

        [Test]
        public void LifespanBelowUnknownSentinelFailsClosed()
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.DaysOfLifeRemaining = -2;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JindanProofTestFixtures.NpcPolicy().Evaluate(input));
        }

        [Test]
        public void UndefinedRiskDispositionFailsClosed()
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.RiskDisposition = (NpcRiskDisposition)999;

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                JindanProofTestFixtures.NpcPolicy().Evaluate(input));
        }

        [Test]
        public void UnknownLifespanDoesNotApplyLifespanPressure()
        {
            NpcProofDecisionInput input = JindanProofTestFixtures.ReadyNpcInput();
            input.DaysOfLifeRemaining = -1;
            input.SubjectiveSuccessPercent = 54;

            NpcProofDecision decision = JindanProofTestFixtures.NpcPolicy().Evaluate(input);

            Assert.That(decision.ShouldStart, Is.False);
            Assert.That(decision.RequiredSubjectivePercent, Is.EqualTo(55));
        }
    }
}
