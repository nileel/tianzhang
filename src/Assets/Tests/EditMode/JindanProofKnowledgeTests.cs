using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    public sealed class JindanProofKnowledgeTests
    {
        [Test]
        public void UnknownKnowledgeRevealsNoRoadConditionsProgressAdaptationOrForecast()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                ledger,
                ProofKnowledgeLevel.Unknown,
                88,
                73);

            Assert.That(view.RoadId, Is.Null);
            Assert.That(view.Requirements, Is.Empty);
            Assert.That(view.AdaptationPercent, Is.Null);
            Assert.That(view.EstimatedSuccessPercent, Is.Null);
            Assert.That(view.RevealsExactConditions, Is.False);
        }

        [Test]
        public void RoadDirectionKnowledgeShowsRoadOnly()
        {
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                new DaoProofLedger("actor_player"),
                ProofKnowledgeLevel.RoadDirection,
                88,
                73);

            Assert.That(view.RoadId, Is.EqualTo("fire"));
            Assert.That(view.Requirements, Is.Empty);
            Assert.That(view.AdaptationPercent, Is.Null);
            Assert.That(view.EstimatedSuccessPercent, Is.Null);
            Assert.That(view.RevealsExactConditions, Is.False);
        }

        [Test]
        public void FullKnowledgeReadsCurrentLedgerAndExposesForecast()
        {
            var ledger = JindanProofTestFixtures.EligibleFireLedger();
            JindanProofView view = JindanProofKnowledge.Project(
                JindanProofTestFixtures.FireSourceProfile(),
                ledger,
                ProofKnowledgeLevel.FullProfile,
                88,
                73);

            Assert.That(view.RoadId, Is.EqualTo("fire"));
            Assert.That(view.Requirements, Has.Count.EqualTo(3));
            Assert.That(
                view.Requirements,
                Has.All.Matches<ProofRequirementView>(item => item.IsMet));
            Assert.That(view.AdaptationPercent, Is.EqualTo(88));
            Assert.That(view.EstimatedSuccessPercent, Is.EqualTo(73));
            Assert.That(view.RevealsExactConditions, Is.True);
        }

        [Test]
        public void EligibilityIsDerivedAndChangesWhenLedgerChanges()
        {
            var ledger = new DaoProofLedger("actor_player");
            JindanProofProfileDefinition profile = JindanProofTestFixtures.FireSourceProfile();

            Assert.That(
                JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied,
                Is.False);
            JindanProofTestFixtures.FillEligibleFireLedger(ledger);
            Assert.That(
                JindanProofEligibility.Evaluate(profile, ledger).IsSatisfied,
                Is.True);
        }
    }
}
