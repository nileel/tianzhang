using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Cultivation.JindanProof;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class JindanProofSnapshotTests
    {
        [Test]
        public void WorldRoundTripPreservesMultipleActorsLedgerProgressAndEventGuards()
        {
            DaoProofLedger playerLedger = JindanProofTestFixtures.EligibleFireLedger();
            var otherLedger = new DaoProofLedger("actor_other");
            var secondCoreLedger = new DaoProofLedger("actor_second_core");
            var otherRules = JindanProofTestFixtures.FireRules();
            var otherEvent = new DaoProofBehaviorEvent(
                "other_event",
                "actor_other",
                "other_target",
                "other_context",
                3,
                new[] { new DaoProofContribution("fire_seed_count", 4) },
                new[] { "other_achievement" });
            Assert.That(otherLedger.TryRecord(otherEvent, otherRules), Is.True);

            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_save");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(60, true);
            var core = new JindanCoreState("actor_player");

            JindanProofRestoredState restored = RoundTrip(
                new[] { playerLedger, otherLedger, secondCoreLedger },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                new[]
                {
                    core,
                    new JindanCoreState("actor_second_core")
                });

            Assert.That(restored.GetLedger("actor_player").GetMetricValue("fire_seed_count"), Is.EqualTo(3));
            Assert.That(restored.GetLedger("actor_other").GetMetricValue("fire_seed_count"), Is.EqualTo(4));
            Assert.That(restored.GetLedger("actor_other").HasAchievement("other_achievement"), Is.True);
            Assert.That(restored.Coordinator.GetAttempt("attempt_save").RegularProgress, Is.EqualTo(60));
            Assert.That(restored.GetCore("actor_player"), Is.Not.Null);
            Assert.That(restored.GetCore("actor_second_core"), Is.Not.Null);
            Assert.That(restored.GetCore("actor_other"), Is.Null,
                "An actor may have a proof ledger without having formed a core.");

            Assert.That(restored.GetLedger("actor_player").TryRecord(
                JindanProofTestFixtures.FireBehavior(
                    "eligible_1",
                    "target_new",
                    "context_new",
                    3,
                    new[] { new DaoProofContribution("fire_seed_count", 99) }),
                JindanProofTestFixtures.FireRules()), Is.False);
            Assert.That(restored.GetLedger("actor_other").TryRecord(
                new DaoProofBehaviorEvent(
                    "new_event",
                    "actor_other",
                    "new_target",
                    "other_context",
                    3,
                    new[] { new DaoProofContribution("fire_seed_count", 99) },
                    Array.Empty<string>()),
                otherRules), Is.False,
                "Repeat keys must survive the round trip as well as event IDs.");
        }

        [Test]
        public void PendingRegularTickRoundTripClosesExactlyOnce()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_pending");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(attempt.AttemptId, 9000);

            JindanProofRestoredState restored = RoundTrip(
                new[] { JindanProofTestFixtures.EligibleFireLedger() },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                new[] { new JindanCoreState("actor_player") });

            ProofTickResolution first = restored.Coordinator.CloseRegularTick(
                attempt.PositionId,
                9000);
            ProofTickResolution second = restored.Coordinator.CloseRegularTick(
                attempt.PositionId,
                9000);

            Assert.That(first.Kind, Is.EqualTo(ProofTickResolutionKind.UniqueReady));
            Assert.That(second.Kind, Is.EqualTo(ProofTickResolutionKind.NoCompletion));
        }

        [Test]
        public void PendingCriticalTickAndCriticalRoundRoundTripCloseExactlyOnce()
        {
            JindanProofCoordinator coordinator = JindanProofTestFixtures.CriticalContest(
                out JindanProofAttempt firstAttempt,
                out JindanProofAttempt secondAttempt);
            firstAttempt.AdvanceCritical(20);
            secondAttempt.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(firstAttempt.AttemptId, 9100);
            coordinator.SubmitCriticalCompletion(secondAttempt.AttemptId, 9100);

            JindanProofRestoredState restored = RoundTrip(
                new[]
                {
                    new DaoProofLedger("actor_a"),
                    new DaoProofLedger("actor_b")
                },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                Array.Empty<JindanCoreState>());

            ProofTickResolution first = restored.Coordinator.CloseCriticalTick(
                firstAttempt.PositionId,
                9100);
            ProofTickResolution second = restored.Coordinator.CloseCriticalTick(
                firstAttempt.PositionId,
                9100);

            Assert.That(first.Kind, Is.EqualTo(ProofTickResolutionKind.CriticalContestContinues));
            Assert.That(restored.Coordinator.GetAttempt(firstAttempt.AttemptId).CriticalRound, Is.EqualTo(2));
            Assert.That(restored.Coordinator.GetAttempt(secondAttempt.AttemptId).CriticalRound, Is.EqualTo(2));
            Assert.That(second.Kind, Is.EqualTo(ProofTickResolutionKind.NoCompletion));
        }

        [Test]
        public void ClosedTickRoundTripRejectsLateSubmitAndRepeatedClose()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt winner = JindanProofTestFixtures.NewAttempt("attempt_winner");
            JindanProofAttempt late = JindanProofTestFixtures.NewAttempt("attempt_late", "actor_late");
            coordinator.Register(winner);
            coordinator.Register(late);
            winner.AdvanceRegular(100, true);
            late.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(winner.AttemptId, 9200);
            coordinator.CloseRegularTick(winner.PositionId, 9200);

            JindanProofRestoredState restored = RoundTrip(
                new[]
                {
                    JindanProofTestFixtures.EligibleFireLedger(),
                    new DaoProofLedger("actor_late")
                },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                new[] { new JindanCoreState("actor_player") });

            Assert.Throws<InvalidOperationException>(() =>
                restored.Coordinator.SubmitRegularCompletion(late.AttemptId, 9200));
            Assert.That(
                restored.Coordinator.CloseRegularTick(winner.PositionId, 9200).Kind,
                Is.EqualTo(ProofTickResolutionKind.NoCompletion));
        }

        [Test]
        public void ClosedCriticalTickRoundTripRejectsLateSubmitAndRepeatedClose()
        {
            JindanProofCoordinator coordinator = JindanProofTestFixtures.CriticalContest(
                out JindanProofAttempt winner,
                out JindanProofAttempt late);
            winner.AdvanceCritical(20);
            coordinator.SubmitCriticalCompletion(winner.AttemptId, 9250);
            coordinator.CloseCriticalTick(winner.PositionId, 9250);
            late.AdvanceCritical(20);

            JindanProofRestoredState restored = RoundTrip(
                new[]
                {
                    new DaoProofLedger("actor_a"),
                    new DaoProofLedger("actor_b")
                },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                Array.Empty<JindanCoreState>());

            Assert.Throws<InvalidOperationException>(() =>
                restored.Coordinator.SubmitCriticalCompletion(late.AttemptId, 9250));
            Assert.That(
                restored.Coordinator.CloseCriticalTick(winner.PositionId, 9250).Kind,
                Is.EqualTo(ProofTickResolutionKind.NoCompletion));
        }

        [Test]
        public void BoundSeatRoundTripKeepsBindingLoopAndPositionVersion()
        {
            var coordinator = new JindanProofCoordinator();
            JindanPositionRegistry registry =
                JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            JindanProofTestFixtures.BindFirstSeat(registry, core, coordinator);

            JindanProofRestoredState restored = RoundTrip(
                new[] { JindanProofTestFixtures.EligibleFireLedger() },
                coordinator,
                registry,
                new[] { core });

            JindanCoreState restoredCore = restored.GetCore("actor_player");
            JindanPositionRecord restoredPosition =
                restored.Registry.Get("position_fire_source_01");
            Assert.That(restoredCore.CoreBindingId, Is.EqualTo("core_actor_player"));
            Assert.That(restoredCore.SeatBindings, Has.Count.EqualTo(1));
            Assert.That(restoredCore.SeatBindings[0].PositionId, Is.EqualTo(restoredPosition.PositionId));
            Assert.That(restoredCore.SeatBindings[0].SeatType, Is.EqualTo(restoredPosition.SeatType));
            Assert.That(
                restoredCore.SeatBindings[0].CarrierAbilityInstanceId,
                Is.EqualTo("ability_source_actor_player"));
            Assert.That(restoredPosition.HolderActorId, Is.EqualTo(restoredCore.ActorId));
            Assert.That(restoredPosition.Visibility, Is.EqualTo(JindanPositionVisibility.Hidden));
            Assert.That(restoredPosition.Version, Is.EqualTo(1));
            Assert.That(
                restored.Coordinator.GetAttempt("attempt_first").Status,
                Is.EqualTo(ProofAttemptStatus.Bound));
        }

        [Test]
        public void DuplicateWorldActorIdsFailClosed()
        {
            var coordinator = new JindanProofCoordinator();
            JindanPositionRegistry registry =
                JindanProofTestFixtures.RegistryWithVacantFireSource();

            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Capture(
                new[]
                {
                    new DaoProofLedger("actor_duplicate"),
                    new DaoProofLedger("actor_duplicate")
                },
                coordinator,
                registry,
                Array.Empty<JindanCoreState>()));
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Capture(
                new[] { new DaoProofLedger("actor_duplicate") },
                coordinator,
                registry,
                new[]
                {
                    new JindanCoreState("actor_duplicate"),
                    new JindanCoreState("actor_duplicate")
                }));
        }

        [Test]
        public void CorruptSchemaNumbersIdsAndPendingReferencesFailClosed()
        {
            JindanProofSaveData baseline = BaselineSaveWithPendingAttempt();

            JindanProofSaveData unsupported = Clone(baseline);
            unsupported.schemaVersion++;
            Assert.Throws<NotSupportedException>(() => JindanProofSnapshot.Restore(unsupported));

            JindanProofSaveData negativeMetric = Clone(baseline);
            negativeMetric.ledgers[0].metrics[0].value = -1;
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(negativeMetric));

            JindanProofSaveData duplicatePosition = Clone(baseline);
            duplicatePosition.positions.Add(ClonePosition(duplicatePosition.positions[0]));
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(duplicatePosition));

            JindanProofSaveData unknownAttempt = Clone(baseline);
            unknownAttempt.regularCompletions[0].attemptIds[0] = "attempt_unknown";
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(unknownAttempt));

            JindanProofSaveData statusMismatch = Clone(baseline);
            statusMismatch.attempts[0].status = ProofAttemptStatus.Active;
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(statusMismatch));

            JindanProofSaveData duplicateOpenAttempt = Clone(baseline);
            duplicateOpenAttempt.regularCompletions.Add(new ProofCompletionSaveData
            {
                positionId = "position_fire_source_01",
                worldTick = 9301,
                attemptIds = new List<string> { "attempt_pending" }
            });
            Assert.Throws<ArgumentException>(() =>
                JindanProofSnapshot.Restore(duplicateOpenAttempt));

            JindanProofSaveData negativeClosedTick = Clone(baseline);
            negativeClosedTick.closedRegularTicks.Add(new ProofCompletionKeySaveData
            {
                positionId = "position_fire_source_01",
                worldTick = -1
            });
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(negativeClosedTick));
        }

        [Test]
        public void CorruptPositionCoreHolderCrossReferencesFailClosed()
        {
            var coordinator = new JindanProofCoordinator();
            JindanPositionRegistry registry =
                JindanProofTestFixtures.RegistryWithVacantFireSource();
            var core = new JindanCoreState("actor_player");
            JindanProofTestFixtures.BindFirstSeat(registry, core, coordinator);
            JindanProofSaveData baseline = JindanProofSnapshot.Capture(
                new[] { JindanProofTestFixtures.EligibleFireLedger() },
                coordinator,
                registry,
                new[] { core });

            JindanProofSaveData wrongHolder = Clone(baseline);
            wrongHolder.positions[0].holderActorId = "actor_intruder";
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(wrongHolder));

            JindanProofSaveData wrongSeat = Clone(baseline);
            wrongSeat.cores[0].seatBindings[0].seatType = JindanSeatType.Domain;
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(wrongSeat));

            JindanProofSaveData missingCore = Clone(baseline);
            missingCore.cores.Clear();
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(missingCore));

            JindanProofSaveData missingPosition = Clone(baseline);
            missingPosition.positions.Clear();
            Assert.Throws<ArgumentException>(() => JindanProofSnapshot.Restore(missingPosition));
        }

        private static JindanProofRestoredState RoundTrip(
            IReadOnlyList<DaoProofLedger> ledgers,
            JindanProofCoordinator coordinator,
            JindanPositionRegistry registry,
            IReadOnlyList<JindanCoreState> cores)
        {
            string json = JsonUtility.ToJson(
                JindanProofSnapshot.Capture(ledgers, coordinator, registry, cores));
            return JindanProofSnapshot.Restore(
                JsonUtility.FromJson<JindanProofSaveData>(json));
        }

        private static JindanProofSaveData BaselineSaveWithPendingAttempt()
        {
            var coordinator = new JindanProofCoordinator();
            JindanProofAttempt attempt = JindanProofTestFixtures.NewAttempt("attempt_pending");
            coordinator.Register(attempt);
            attempt.AdvanceRegular(100, true);
            coordinator.SubmitRegularCompletion(attempt.AttemptId, 9300);
            return JindanProofSnapshot.Capture(
                new[] { JindanProofTestFixtures.EligibleFireLedger() },
                coordinator,
                JindanProofTestFixtures.RegistryWithVacantFireSource(),
                new[] { new JindanCoreState("actor_player") });
        }

        private static JindanProofSaveData Clone(JindanProofSaveData source)
        {
            return JsonUtility.FromJson<JindanProofSaveData>(JsonUtility.ToJson(source));
        }

        private static JindanPositionSaveData ClonePosition(JindanPositionSaveData source)
        {
            return new JindanPositionSaveData
            {
                positionId = source.positionId,
                profileId = source.profileId,
                seatType = source.seatType,
                visibility = source.visibility,
                holderActorId = source.holderActorId,
                version = source.version
            };
        }
    }
}
