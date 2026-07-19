using System;
using System.Collections.Generic;
using TianZhang.Cultivation.JindanProof;

namespace TianZhang.Tests
{
    internal static class JindanProofTestFixtures
    {
        internal static JindanProofProfileDefinition FireSourceProfile()
        {
            return new JindanProofProfileDefinition(
                "jindan_fire_source",
                "fire",
                JindanSeatType.Source,
                new[]
                {
                    new JindanProofRequirement(
                        "fire_seed_count", ProofRequirementType.SharedMetric, 3),
                    new JindanProofRequirement(
                        "valid_ignition_count", ProofRequirementType.SharedMetric, 5),
                    new JindanProofRequirement(
                        "fire_source_precise_ignition",
                        ProofRequirementType.SignatureAchievement,
                        1)
                },
                100,
                20);
        }

        internal static IReadOnlyDictionary<string, DaoProofMetricRule> FireRules()
        {
            return new Dictionary<string, DaoProofMetricRule>(StringComparer.Ordinal)
            {
                ["fire_seed_count"] = new DaoProofMetricRule(
                    "fire_seed_count", ProofRepeatPolicy.OncePerContext, 1),
                ["valid_ignition_count"] = new DaoProofMetricRule(
                    "valid_ignition_count", ProofRepeatPolicy.OncePerTarget, 1)
            };
        }

        internal static DaoProofBehaviorEvent FireBehavior(
            string eventId,
            string targetKey,
            string contextKey,
            int challengeTier,
            IReadOnlyList<DaoProofContribution> contributions,
            IReadOnlyList<string> achievementIds = null)
        {
            return new DaoProofBehaviorEvent(
                eventId,
                "actor_player",
                targetKey,
                contextKey,
                challengeTier,
                contributions,
                achievementIds ?? Array.Empty<string>());
        }

        internal static DaoProofLedger EligibleFireLedger()
        {
            var ledger = new DaoProofLedger("actor_player");
            FillEligibleFireLedger(ledger);
            return ledger;
        }

        internal static void FillEligibleFireLedger(DaoProofLedger ledger)
        {
            var rules = FireRules();
            ledger.TryRecord(
                FireBehavior(
                    "eligible_1",
                    "target_1",
                    "context_1",
                    3,
                    new[]
                    {
                        new DaoProofContribution("fire_seed_count", 3),
                        new DaoProofContribution("valid_ignition_count", 3)
                    }),
                rules);
            ledger.TryRecord(
                FireBehavior(
                    "eligible_2",
                    "target_2",
                    "context_2",
                    3,
                    new[]
                    {
                        new DaoProofContribution("valid_ignition_count", 2)
                    },
                    new[] { "fire_source_precise_ignition" }),
                rules);
        }

        internal static JindanProofAttempt NewAttempt(
            string attemptId,
            string actorId = "actor_player")
        {
            JindanProofProfileDefinition profile = FireSourceProfile();
            return new JindanProofAttempt(
                attemptId,
                "position_fire_source_01",
                actorId,
                profile.ProfileId,
                "site_fire_altar_01",
                "ability_fire_carrier_01_" + actorId,
                0,
                profile.RegularProgressTarget,
                profile.CriticalProgressTarget);
        }

        internal static JindanProofCoordinator CriticalContest(
            out JindanProofAttempt a,
            out JindanProofAttempt b)
        {
            var coordinator = new JindanProofCoordinator();
            a = NewAttempt("attempt_a", "actor_a");
            b = NewAttempt("attempt_b", "actor_b");
            coordinator.Register(a);
            coordinator.Register(b);
            a.AdvanceRegular(100, true);
            b.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(a.AttemptId, 2000);
            coordinator.SubmitRegularCompletion(b.AttemptId, 2000);
            coordinator.CloseRegularTick(a.PositionId, 2000);
            return coordinator;
        }

        internal static JindanProofProfileDefinition FireTransformationProfile()
        {
            return new JindanProofProfileDefinition(
                "jindan_fire_transformation",
                "fire",
                JindanSeatType.Transformation,
                new[]
                {
                    new JindanProofRequirement(
                        "fire_seed_count", ProofRequirementType.SharedMetric, 3),
                    new JindanProofRequirement(
                        "valid_ignition_count", ProofRequirementType.SharedMetric, 5),
                    new JindanProofRequirement(
                        "fire_transformation_filtered_spread",
                        ProofRequirementType.SignatureAchievement,
                        1)
                },
                100,
                20);
        }

        internal static DaoProofLedger EligibleFireTransformationLedger()
        {
            DaoProofLedger ledger = EligibleFireLedger();
            ledger.TryRecord(
                FireBehavior(
                    "eligible_transformation",
                    "target_3",
                    "context_3",
                    3,
                    new[] { new DaoProofContribution("fire_seed_count", 1) },
                    new[] { "fire_transformation_filtered_spread" }),
                FireRules());
            return ledger;
        }

        internal static JindanPositionRegistry RegistryWithVacantFireSource(
            long version = 0)
        {
            var registry = new JindanPositionRegistry();
            registry.Add(new JindanPositionRecord(
                "position_fire_source_01",
                "jindan_fire_source",
                JindanSeatType.Source,
                JindanPositionVisibility.Hidden,
                version));
            return registry;
        }

        internal static JindanPositionRegistry
            RegistryWithVacantFireSourceAndTransformation()
        {
            JindanPositionRegistry registry = RegistryWithVacantFireSource();
            registry.Add(new JindanPositionRecord(
                "position_fire_transformation_01",
                "jindan_fire_transformation",
                JindanSeatType.Transformation,
                JindanPositionVisibility.Public));
            return registry;
        }

        internal static JindanProofAttempt ReadyAttempt(
            JindanProofCoordinator coordinator,
            string attemptId,
            string actorId,
            string positionId,
            string profileId,
            string carrierId,
            long expectedVersion)
        {
            var attempt = new JindanProofAttempt(
                attemptId,
                positionId,
                actorId,
                profileId,
                "site_" + positionId,
                carrierId,
                expectedVersion,
                100,
                20);
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(attemptId, 100);
            coordinator.CloseRegularTick(positionId, 100);
            return attempt;
        }

        internal static void BindFirstSeat(
            JindanPositionRegistry registry,
            JindanCoreState core,
            JindanProofCoordinator coordinator)
        {
            JindanProofAttempt first = ReadyAttempt(
                coordinator,
                "attempt_first",
                "actor_player",
                "position_fire_source_01",
                "jindan_fire_source",
                "ability_source_actor_player",
                0);
            JindanBindResult result = registry.TryBind(
                new JindanBindRequest(
                    first.AttemptId, "core_actor_player", true, true),
                FireSourceProfile(),
                EligibleFireLedger(),
                core,
                coordinator);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Fixture failed to bind first seat.");
            }
        }

        internal static NpcProofDecisionInput ReadyNpcInput()
        {
            return new NpcProofDecisionInput
            {
                IsPersistentNpc = true,
                IsPurpleMansionComplete = true,
                HardRequirementsMet = true,
                KnowsVacancy = true,
                KnowsUsableSite = true,
                HasCompatibleCarrier = true,
                HasFacilities = true,
                HasResources = true,
                HasGuard = true,
                HasHigherPrioritySurvivalDuty = false,
                RiskDisposition = NpcRiskDisposition.Normal,
                SubjectiveSuccessPercent = 80,
                DaysOfLifeRemaining = 2000
            };
        }

        internal static NpcJindanProofPolicy NpcPolicy()
        {
            return new NpcJindanProofPolicy(
                cautiousThreshold: 70,
                normalThreshold: 55,
                boldThreshold: 40,
                lifespanDangerDays: 180,
                lifespanThresholdReduction: 20);
        }
    }
}
