using System;
using System.Linq;
using NUnit.Framework;
using TianZhang.World;

namespace TianZhang.Tests
{
    public sealed class CharterConflictRulesTests
    {
        [Test]
        public void ContractV1RequiresExplicitVersionAndCompleteIdentity()
        {
            RuleConflictDecision unsupported = CreateJindan(contractVersion: 0).Decide(null);
            Assert.AreEqual(RuleConflictOutcome.Rejected, unsupported.Outcome);
            Assert.AreEqual("TZ_CHARTER_CONFLICT_CONTRACT_VERSION_UNSUPPORTED", unsupported.Reason);

            RuleConflictDecision incomplete = CreateJindan(ruleEntryId: string.Empty).Decide(null);
            Assert.AreEqual("TZ_CHARTER_CONFLICT_INPUT_INVALID", incomplete.Reason);
            Assert.AreEqual(RuleConflictInstance.ContractVersionV1, CreateJindan().ContractVersion);
            Assert.IsFalse(typeof(RuleConflictInstance).GetProperties().Any(property =>
                property.PropertyType.Namespace != null && property.PropertyType.Namespace.StartsWith("Unity", StringComparison.Ordinal)));
        }

        [Test]
        public void JindanDecisionPreservesTheDeclaredPriorityOrderWithoutWritingState()
        {
            AssertWinner(
                CreateJindan(left: CreateCandidate("left", hasVariableAuthority: false), right: CreateCandidate("right")),
                RuleConflictOutcome.RightWins,
                "right",
                "VARIABLE_AUTHORITY_AND_TARGET");
            AssertWinner(
                CreateJindan(left: CreateCandidate("left", positionRank: 3), right: CreateCandidate("right", positionRank: 2)),
                RuleConflictOutcome.LeftWins,
                "left",
                "POSITION_TIER");
            AssertWinner(
                CreateJindan(left: CreateCandidate("left", realityAnchorRank: 2), right: CreateCandidate("right", realityAnchorRank: 1)),
                RuleConflictOutcome.LeftWins,
                "left",
                "REALITY_ANCHOR");
            AssertWinner(
                CreateJindan(left: CreateCandidate("left", alreadyPaidCost: 2), right: CreateCandidate("right", alreadyPaidCost: 1)),
                RuleConflictOutcome.LeftWins,
                "left",
                "ALREADY_PAID_COST");
            AssertWinner(
                CreateJindan(left: CreateCandidate("left", hasActiveContinuousCarrier: true), right: CreateCandidate("right", hasActiveContinuousCarrier: false)),
                RuleConflictOutcome.LeftWins,
                "left",
                "ACTIVE_CONTINUOUS_CARRIER");

            RuleConflictDecision pulse = CreateJindan(
                left: CreateCandidate("left", conflictReserve: 6),
                right: CreateCandidate("right", conflictReserve: 4)).Decide(null);
            Assert.AreEqual(RuleConflictOutcome.LeftWins, pulse.Outcome);
            Assert.AreEqual("PULSE_ADVANTAGE", pulse.Reason);
            Assert.AreEqual(3, pulse.LeftPulses);
            Assert.AreEqual(2, pulse.RightPulses);
            Assert.AreEqual(6, pulse.LeftReserveSpent);
            Assert.AreEqual(4, pulse.RightReserveSpent);
            Assert.AreEqual(3, pulse.LeftSettlementCooldown);
            Assert.AreEqual(3, pulse.RightSettlementCooldown);
            Assert.IsTrue(pulse.RequiresLedgerSettlement);
        }

        [Test]
        public void CrossTierArchivePreservesVersionedQualificationReasonsAndIdempotence()
        {
            CrossTierChallengeGrant grant = CreateGrant();
            var archive = new CrossTierChallengeArchive(new[] { grant });
            CrossTierChallengeRequest request = CreateRequest();
            CrossTierChallengeResolution first = archive.Resolve(request);
            CrossTierChallengeResolution repeated = archive.Resolve(request);

            Assert.IsTrue(first.IsEligible);
            Assert.AreEqual("JD_CHALLENGE_AUTHORIZED", first.Reason);
            Assert.AreSame(grant, first.Grant);
            Assert.IsTrue(repeated.IsEligible);
            Assert.AreSame(grant, repeated.Grant);
            AssertChallenge(new CrossTierChallengeArchive(Array.Empty<CrossTierChallengeGrant>()).Resolve(request), "JD_CHALLENGE_ARCHIVE_UNAVAILABLE");
            AssertChallenge(archive.Resolve(CreateRequest(grantId: "unknown")), "JD_CHALLENGE_GRANT_UNKNOWN");
            AssertChallenge(archive.Resolve(CreateRequest(expectedDefinitionVersion: 6)), "JD_CHALLENGE_VERSION_MISMATCH");
            AssertChallenge(new CrossTierChallengeArchive(new[] { CreateGrant(expiresAtTick: 11) }).Resolve(request), "JD_CHALLENGE_EXPIRED");
            AssertChallenge(new CrossTierChallengeArchive(new[] { CreateGrant(isRevoked: true) }).Resolve(request), "JD_CHALLENGE_REVOKED");
            AssertChallenge(archive.Resolve(CreateRequest(targetVariableId: "other-variable")), "JD_CHALLENGE_TARGET_MISMATCH");
            AssertChallenge(archive.Resolve(CreateRequest(challengerId: "other-challenger")), "JD_CHALLENGE_CHALLENGER_MISMATCH");
        }

        [Test]
        public void AuthorizedGrantBindsEveryConflictIdentityField()
        {
            var archive = new CrossTierChallengeArchive(new[] { CreateGrant() });
            CrossTierChallengeRequest request = CreateRequest();
            RuleConflictInstance authorized = CreateJindan(
                crossTierChallengeRequest: request,
                right: CreateCandidate("right", conflictReserve: 4));
            Assert.AreEqual("PULSE_ADVANTAGE", authorized.Decide(archive).Reason);
            AssertBinding(archive, CreateJindan(allowedOperationId: "other-operation", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_OPERATION_MISMATCH");
            AssertBinding(archive, CreateJindan(targetId: "other-target", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_TARGET_MISMATCH");
            AssertBinding(archive, CreateJindan(scopeId: "other-scope", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_SCOPE_MISMATCH");
            AssertBinding(archive, CreateJindan(beneficiaryId: "other-beneficiary", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_BENEFICIARY_MISMATCH");
            AssertBinding(archive, CreateJindan(realityAnchorId: "other-anchor", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_ANCHOR_MISMATCH");
            AssertBinding(archive, CreateJindan(resourceLedgerRef: "other-resource", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_RESOURCE_LEDGER_MISMATCH");
            AssertBinding(archive, CreateJindan(capacityLedgerRef: "other-capacity", crossTierChallengeRequest: request), "TZ_CHARTER_CONFLICT_GRANT_CAPACITY_LEDGER_MISMATCH");
        }

        [Test]
        public void YuanyingAnchorReturnsOnlyItsSharedAnchorDecision()
        {
            var anchored = new RuleConflictInstance(
                RuleConflictInstance.ContractVersionV1,
                "anchor-event",
                RuleConflictKind.YuanyingAnchored,
                "rule-entry",
                "target-variable",
                "operation",
                "target",
                "scope",
                "beneficiary",
                "anchor",
                "resource",
                "capacity",
                12,
                null,
                null,
                null);

            RuleConflictDecision decision = anchored.Decide(null);
            Assert.AreEqual(RuleConflictOutcome.Anchored, decision.Outcome);
            Assert.AreEqual("TZ_CHARTER_CONFLICT_YUANYING_ANCHORED", decision.Reason);
            Assert.IsFalse(decision.RequiresLedgerSettlement);
            Assert.IsNull(decision.CrossTierAuthorization);
            Assert.AreEqual(0, decision.LeftReserveSpent + decision.RightReserveSpent + decision.LeftSettlementCooldown + decision.RightSettlementCooldown);
        }

        private static void AssertWinner(RuleConflictInstance instance, RuleConflictOutcome outcome, string winnerCandidateId, string reason)
        {
            RuleConflictDecision decision = instance.Decide(null);
            Assert.AreEqual(outcome, decision.Outcome);
            Assert.AreEqual(winnerCandidateId, decision.WinnerCandidateId);
            Assert.AreEqual(reason, decision.Reason);
            Assert.IsFalse(decision.RequiresLedgerSettlement);
        }

        private static void AssertChallenge(CrossTierChallengeResolution resolution, string reason)
        {
            Assert.IsFalse(resolution.IsEligible);
            Assert.AreEqual(reason, resolution.Reason);
            Assert.IsNull(resolution.Grant);
        }

        private static void AssertBinding(CrossTierChallengeArchive archive, RuleConflictInstance instance, string reason)
        {
            RuleConflictDecision decision = instance.Decide(archive);
            Assert.AreEqual(RuleConflictOutcome.Rejected, decision.Outcome);
            Assert.AreEqual(reason, decision.Reason);
            Assert.IsTrue(decision.CrossTierAuthorization.IsEligible);
        }

        private static RuleConflictInstance CreateJindan(
            int contractVersion = RuleConflictInstance.ContractVersionV1,
            string ruleEntryId = "rule-entry",
            string allowedOperationId = "operation",
            string targetId = "target",
            string scopeId = "scope",
            string beneficiaryId = "beneficiary",
            string realityAnchorId = "anchor",
            string resourceLedgerRef = "resource",
            string capacityLedgerRef = "capacity",
            CrossTierChallengeRequest crossTierChallengeRequest = null,
            RuleConflictCandidate left = null,
            RuleConflictCandidate right = null)
        {
            return new RuleConflictInstance(
                contractVersion,
                "conflict-event",
                RuleConflictKind.JindanSameVariable,
                ruleEntryId,
                "target-variable",
                allowedOperationId,
                targetId,
                scopeId,
                beneficiaryId,
                realityAnchorId,
                resourceLedgerRef,
                capacityLedgerRef,
                12,
                left ?? CreateCandidate("left", targetId: targetId),
                right ?? CreateCandidate("right", targetId: targetId),
                crossTierChallengeRequest);
        }

        private static RuleConflictCandidate CreateCandidate(
            string candidateId,
            bool hasVariableAuthority = true,
            bool hasLegalTarget = true,
            int positionRank = 3,
            int realityAnchorRank = 1,
            int alreadyPaidCost = 2,
            bool hasActiveContinuousCarrier = true,
            int conflictReserve = 6,
            string targetId = "target")
        {
            return new RuleConflictCandidate(
                candidateId,
                "target-variable",
                targetId,
                hasVariableAuthority,
                hasLegalTarget,
                positionRank,
                realityAnchorRank,
                alreadyPaidCost,
                hasActiveContinuousCarrier,
                conflictReserve,
                2,
                3);
        }

        private static CrossTierChallengeGrant CreateGrant(
            int expiresAtTick = 20,
            bool isRevoked = false)
        {
            return new CrossTierChallengeGrant(
                "grant",
                7,
                "target-variable",
                "challenger",
                CrossTierChallengeSourceKind.YuanyingOrthodoxy,
                "operation",
                "target",
                "scope",
                "beneficiary",
                "anchor",
                "resource",
                "capacity",
                2,
                10,
                expiresAtTick,
                isRevoked,
                isRevoked ? "revoked" : string.Empty,
                "fixture");
        }

        private static CrossTierChallengeRequest CreateRequest(
            string grantId = "grant",
            int expectedDefinitionVersion = 7,
            string targetVariableId = "target-variable",
            string challengerId = "challenger")
        {
            return new CrossTierChallengeRequest(
                "challenge-event",
                grantId,
                expectedDefinitionVersion,
                targetVariableId,
                challengerId,
                12);
        }
    }
}
