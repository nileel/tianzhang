using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Entity;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class BountyRuntimeTests
    {
        private const string BountyId = "bounty_guanzhong_shijiahou";
        private const string SecondBountyId = "bounty_guanzhong_shijiahou_second";
        private const string SettlementId = "guanzhong_city";
        private const string AdventureId = "guanzhong_wild";
        private const string EnemyId = "enemy_shijiahou";

        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();
        private GameObject sessionGo;

        [TearDown]
        public void TearDown()
        {
            if (sessionGo != null)
                UnityEngine.Object.DestroyImmediate(sessionGo);
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void LegalTransitionsFlowThroughAllFourStatesAndGrantRewards()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            Assert.AreEqual(BountyStatus.Available, session.GetBountyState(BountyId).Status);

            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertState(session, BountyStatus.Accepted, 0);

            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);

            AssertSucceeded(session.ClaimBounty(catalog, BountyId));
            AssertState(session, BountyStatus.Claimed, 1);
            Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out InventoryStateSnapshot granted));
            Assert.AreEqual(3, granted.Quantity);
        }

        [Test]
        public void AcceptRejectsMissingCatalogUnknownIdAndInvalidIdWithoutMutation()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            AssertRejected(session.AcceptBounty(null, BountyId), BountyRuntimeRules.CatalogMissingReason);
            AssertRejected(session.AcceptBounty(catalog, "bounty_unknown"), BountyRuntimeRules.BountyMissingReason);
            AssertRejected(session.AcceptBounty(catalog, "  "), BountyRuntimeRules.BountyIdInvalidReason);

            Assert.AreEqual(BountyStatus.Available, session.GetBountyState(BountyId).Status);
        }

        [Test]
        public void AcceptRejectsNonProductionScopeWrongSettlementAndInvalidShape()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            Assert.IsTrue(catalog.TryGetBounty(BountyId, out BountyData bounty));

            bounty.contentScope = "content_scope_draft";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.BountyNotProductionReason);

            bounty.contentScope = InventoryGrantService.ProductionContentScope;
            bounty.issuerSettlementId = "other_city";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.WrongSettlementReason);

            bounty.issuerSettlementId = SettlementId;
            bounty.objectiveType = "collect_item";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.ObjectiveTypeUnsupportedReason);

            bounty.objectiveType = "defeat_enemy";
            bounty.requiredCount = 0;
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RequiredCountInvalidReason);

            bounty.requiredCount = 1;
            bounty.targetEnemyId = " ";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.TargetInvalidReason);

            bounty.targetEnemyId = EnemyId;
            bounty.allowedAdventureId = "";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.AdventureInvalidReason);

            bounty.allowedAdventureId = AdventureId;
            bounty.repeatPolicy = "daily";
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RepeatPolicyUnsupportedReason);

            Assert.AreEqual(BountyStatus.Available, session.GetBountyState(BountyId).Status);
        }

        [Test]
        public void AcceptRejectsInvalidRewardStructureAndReferences()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            Assert.IsTrue(catalog.TryGetBounty(BountyId, out BountyData bounty));
            bounty.rewardEntries = Array.Empty<BountyRewardEntry>();
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RewardInvalidReason);

            bounty.rewardEntries = new[]
            {
                new BountyRewardEntry { itemId = "item_lingshi_low", quantity = 0 },
            };
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RewardInvalidReason);

            bounty.rewardEntries = new[]
            {
                new BountyRewardEntry { itemId = "item_missing", quantity = 1 },
            };
            AssertRejected(
                session.AcceptBounty(catalog, BountyId),
                BountyRuntimeRules.RewardItemMissingReason + ":item_missing");

            ContentCatalogData draftItemCatalog = CreateCatalog(rewardItemScope: "content_scope_draft");
            AssertRejected(
                session.AcceptBounty(draftItemCatalog, BountyId),
                BountyRuntimeRules.RewardItemNotProductionReason + ":item_lingshi_low");

            ContentCatalogData zeroStackCatalog = CreateCatalog(rewardMaxStack: 0);
            AssertRejected(
                session.AcceptBounty(zeroStackCatalog, BountyId),
                BountyRuntimeRules.RewardItemStackInvalidReason + ":item_lingshi_low");

            ContentCatalogData overStackCatalog = CreateCatalog(rewardMaxStack: 2);
            AssertRejected(
                session.AcceptBounty(overStackCatalog, BountyId),
                BountyRuntimeRules.RewardInvalidReason + ":item_lingshi_low");

            Assert.AreEqual(BountyStatus.Available, session.GetBountyState(BountyId).Status);
        }

        [Test]
        public void AcceptRejectsRepeatedAcceptInEveryNonAvailableState()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RepeatedAcceptReason);

            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RepeatedAcceptReason);

            AssertSucceeded(session.ClaimBounty(catalog, BountyId));
            AssertRejected(session.AcceptBounty(catalog, BountyId), BountyRuntimeRules.RepeatedAcceptReason);
        }

        [Test]
        public void RecordDefeatRejectsWrongAdventureWrongEnemyAndUnacceptedInstance()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            AssertRejected(session.RecordBountyDefeat(catalog, AdventureId, EnemyId), BountyRuntimeRules.NotAcceptedReason);

            AssertSucceeded(session.AcceptBounty(catalog, BountyId));

            AssertRejected(
                session.RecordBountyDefeat(catalog, "taiyi_trial", EnemyId),
                BountyRuntimeRules.DefeatWrongAdventureReason);
            AssertRejected(
                session.RecordBountyDefeat(catalog, AdventureId, "enemy_other"),
                BountyRuntimeRules.DefeatWrongEnemyReason);
            AssertRejected(
                session.RecordBountyDefeat(catalog, "", EnemyId),
                BountyRuntimeRules.AdventureInvalidReason);
            AssertRejected(
                session.RecordBountyDefeat(catalog, AdventureId, ""),
                BountyRuntimeRules.TargetInvalidReason);
            AssertRejected(
                session.RecordBountyDefeat(null, AdventureId, EnemyId),
                BountyRuntimeRules.CatalogMissingReason);

            AssertState(session, BountyStatus.Accepted, 0);
        }

        [Test]
        public void RecordDefeatCompletesAtTargetAndRejectsFurtherRegistration()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            AssertSucceeded(session.AcceptBounty(catalog, BountyId));

            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);

            AssertRejected(session.RecordBountyDefeat(catalog, AdventureId, EnemyId), BountyRuntimeRules.NotAcceptedReason);
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);
        }

        [Test]
        public void RecordDefeatRejectsOverTargetProgressAndSnapshotRejectsNegativeProgress()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            session.BountyStates.Set(new BountyStateSnapshot(BountyId, BountyStatus.Accepted, 5));

            AssertRejected(session.RecordBountyDefeat(catalog, AdventureId, EnemyId), BountyRuntimeRules.ProgressOutOfRangeReason);
            AssertState(session, BountyStatus.Accepted, 5);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new BountyStateSnapshot(BountyId, BountyStatus.Accepted, -1));
        }

        [Test]
        public void ClaimRejectsNonCompletedStatesRepeatedClaimsAndCorruptProgress()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();

            AssertRejected(session.ClaimBounty(catalog, BountyId), BountyRuntimeRules.NotCompletedReason);
            AssertRejected(session.ClaimBounty(catalog, "bounty_unknown"), BountyRuntimeRules.NotCompletedReason);
            AssertRejected(session.ClaimBounty(null, BountyId), BountyRuntimeRules.CatalogMissingReason);

            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertRejected(session.ClaimBounty(catalog, BountyId), BountyRuntimeRules.NotCompletedReason);
            AssertState(session, BountyStatus.Accepted, 0);

            session.BountyStates.Set(new BountyStateSnapshot(BountyId, BountyStatus.ObjectiveCompleted, 0));
            AssertRejected(session.ClaimBounty(catalog, BountyId), BountyRuntimeRules.ProgressInvalidReason);
            AssertState(session, BountyStatus.ObjectiveCompleted, 0);

            session.BountyStates.Set(new BountyStateSnapshot(BountyId, BountyStatus.ObjectiveCompleted, 1));
            AssertSucceeded(session.ClaimBounty(catalog, BountyId));
            AssertRejected(session.ClaimBounty(catalog, BountyId), BountyRuntimeRules.RepeatedClaimReason);
            AssertState(session, BountyStatus.Claimed, 1);
        }

        [Test]
        public void ClaimIsAtomicWhenStackLimitWouldBeExceeded()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                98,
                UnappliedSteps()));
            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));

            AssertRejected(
                session.ClaimBounty(catalog, BountyId),
                BountyRuntimeRules.ClaimInventoryRejectedReason + ":" + InventoryGrantFailureReason.StackLimitExceeded);

            Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out InventoryStateSnapshot snapshot));
            Assert.AreEqual(98, snapshot.Quantity);
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);
        }

        [Test]
        public void ClaimIsAtomicWhenRewardItemBecomesUnresolvable()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));

            Assert.IsTrue(catalog.TryGetBounty(BountyId, out BountyData bounty));
            ContentCatalogData rewardlessCatalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            rewardlessCatalog.ReplaceEntries(null, null, null, new[] { bounty });

            AssertRejected(
                session.ClaimBounty(rewardlessCatalog, BountyId),
                BountyRuntimeRules.RewardItemMissingReason + ":item_lingshi_low");

            Assert.IsFalse(session.InventoryStates.TryGet("item_lingshi_low", out _));
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);
        }

        [Test]
        public void ClaimIsAtomicAndLeavesOtherBountyInstancesUntouched()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            Assert.IsTrue(catalog.TryGetBounty(BountyId, out BountyData first));
            Assert.IsTrue(catalog.TryGetItem("item_lingshi_low", out ItemData rewardItem));
            BountyData second = CreateBounty(SecondBountyId, requiredCount: 2);
            catalog.ReplaceEntries(null, null, new[] { rewardItem }, new[] { first, second });

            AssertSucceeded(session.AcceptBounty(catalog, BountyId));
            AssertSucceeded(session.AcceptBounty(catalog, SecondBountyId));

            AssertSucceeded(session.RecordBountyDefeat(catalog, AdventureId, EnemyId));
            AssertState(session, BountyStatus.ObjectiveCompleted, 1);
            AssertState(session, SecondBountyId, BountyStatus.Accepted, 1);

            AssertSucceeded(session.ClaimBounty(catalog, BountyId));
            AssertState(session, BountyStatus.Claimed, 1);
            AssertState(session, SecondBountyId, BountyStatus.Accepted, 1);
            Assert.IsTrue(session.InventoryStates.TryGet("item_lingshi_low", out InventoryStateSnapshot granted));
            Assert.AreEqual(3, granted.Quantity);
        }

        [Test]
        public void NewGameClearsBountyInstancesAndGetStateRejectsEmptyId()
        {
            GameSession session = CreateSession();
            ContentCatalogData catalog = CreateCatalog();
            CharacterData profile = Track(ScriptableObject.CreateInstance<CharacterData>());
            AssertSucceeded(session.AcceptBounty(catalog, BountyId));

            session.BeginNewGame(profile, "jiangzuo_hub");

            Assert.AreEqual(BountyStatus.Available, session.GetBountyState(BountyId).Status);
            Assert.Throws<ArgumentException>(() => session.GetBountyState(" "));
        }

        private GameSession CreateSession()
        {
            sessionGo = new GameObject("BountyRuntimeSession");
            GameSession session = sessionGo.AddComponent<GameSession>();
            session.SetSettlementId(SettlementId);
            return session;
        }

        private ContentCatalogData CreateCatalog(
            string rewardItemScope = "content_scope_production",
            int rewardMaxStack = 99,
            int rewardQuantity = 3)
        {
            ItemData reward = Track(ScriptableObject.CreateInstance<ItemData>());
            reward.itemId = "item_lingshi_low";
            reward.contentScope = rewardItemScope;
            reward.maxStack = rewardMaxStack;

            BountyData bounty = CreateBounty(BountyId, rewardQuantity: rewardQuantity);
            ContentCatalogData catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            catalog.ReplaceEntries(null, null, new[] { reward }, new[] { bounty });
            return catalog;
        }

        private BountyData CreateBounty(string bountyId, int requiredCount = 1, int rewardQuantity = 3)
        {
            BountyData bounty = Track(ScriptableObject.CreateInstance<BountyData>());
            bounty.bountyId = bountyId;
            bounty.contentScope = InventoryGrantService.ProductionContentScope;
            bounty.issuerSettlementId = SettlementId;
            bounty.objectiveType = "defeat_enemy";
            bounty.targetEnemyId = EnemyId;
            bounty.requiredCount = requiredCount;
            bounty.allowedAdventureId = AdventureId;
            bounty.rewardEntries = new[]
            {
                new BountyRewardEntry { itemId = "item_lingshi_low", quantity = rewardQuantity },
            };
            bounty.repeatPolicy = "one_time";
            return bounty;
        }

        private T Track<T>(T value)
            where T : UnityEngine.Object
        {
            temporaryAssets.Add(value);
            return value;
        }

        private static StateStepSnapshot UnappliedSteps()
        {
            return new StateStepSnapshot(
                shown: true,
                clicked: false,
                opened: true,
                selected: false,
                applied: false,
                completed: false,
                persisted: true);
        }

        private static void AssertSucceeded(BountyActionResult result)
        {
            Assert.IsTrue(result.Succeeded, result.FailureReason);
        }

        private static void AssertRejected(BountyActionResult result, string expectedReason)
        {
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(expectedReason, result.FailureReason);
        }

        private static void AssertState(GameSession session, string bountyId, BountyStatus status, int progress)
        {
            BountyStateSnapshot state = session.GetBountyState(bountyId);
            Assert.AreEqual(status, state.Status);
            Assert.AreEqual(progress, state.Progress);
        }

        private void AssertState(GameSession session, BountyStatus status, int progress)
        {
            AssertState(session, BountyId, status, progress);
        }
    }
}
