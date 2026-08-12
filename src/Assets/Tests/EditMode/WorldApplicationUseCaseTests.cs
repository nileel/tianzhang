using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.World;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class WorldApplicationUseCaseTests
    {
        private readonly List<Object> assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object asset in assets) Object.DestroyImmediate(asset);
            assets.Clear();
        }

        [Test]
        public void BountyLifecycleClaimsRewardThroughOneAtomicUseCase()
        {
            ContentCatalogData catalog = CreateCatalog(99);
            var bounties = new BountyStore();
            var inventory = new InventoryStore();
            var useCase = new BountyUseCase(bounties, inventory);

            Assert.That(useCase.Accept(catalog, "bounty_test", "settlement_test").Succeeded, Is.True);
            Assert.That(useCase.RecordDefeat(catalog, "adventure_test", "enemy_test").Succeeded, Is.True);
            Assert.That(useCase.Claim(catalog, "bounty_test").Succeeded, Is.True);

            Assert.That(useCase.GetState("bounty_test").Status, Is.EqualTo(BountyStatus.Claimed));
            Assert.That(inventory.GetQuantity("item_test"), Is.EqualTo(3));
        }

        [Test]
        public void FailedBountyClaimLeavesBothOwnersUnchanged()
        {
            ContentCatalogData catalog = CreateCatalog(3);
            var bounties = new BountyStore();
            var inventory = new InventoryStore();
            inventory.Restore(new InventoryStoreSnapshot(new[] { new InventoryEntry("item_test", 1) }));
            var useCase = new BountyUseCase(bounties, inventory);
            Assert.That(useCase.Accept(catalog, "bounty_test", "settlement_test").Succeeded, Is.True);
            Assert.That(useCase.RecordDefeat(catalog, "adventure_test", "enemy_test").Succeeded, Is.True);

            BountyActionResult result = useCase.Claim(catalog, "bounty_test");

            Assert.That(result.Succeeded, Is.False);
            Assert.That(useCase.GetState("bounty_test").Status, Is.EqualTo(BountyStatus.ObjectiveCompleted));
            Assert.That(inventory.GetQuantity("item_test"), Is.EqualTo(1));
        }

        [Test]
        public void InventoryGrantRejectsWholeBatchBeforeMutation()
        {
            ContentCatalogData catalog = CreateCatalog(99);
            var inventory = new InventoryStore();
            var useCase = new InventoryGrantUseCase(inventory);

            InventoryGrantResult result = useCase.Grant(catalog, new[]
            {
                new InventoryGrantRequest("item_test", 2),
                new InventoryGrantRequest("missing", 1),
            });

            Assert.That(result.Applied, Is.False);
            Assert.That(inventory.GetQuantity("item_test"), Is.Zero);
        }

        [Test]
        public void CharterUseCaseFailsClosedWithoutCatalogOrEvaluatedState()
        {
            var useCase = new CharterUseCase(new CharterStore());

            Assert.That(
                useCase.CommitEvaluatedState(null, null, 1).Reason,
                Is.EqualTo(CharterUseCaseReasons.InvalidResult));
            Assert.That(useCase.CurrentState, Is.Null);
            Assert.That(useCase.DefinitionCatalogVersion, Is.Zero);
        }

        private ContentCatalogData CreateCatalog(int maxStack)
        {
            var settlement = Track(ScriptableObject.CreateInstance<SettlementData>());
            settlement.settlementId = "settlement_test";
            var enemy = Track(ScriptableObject.CreateInstance<EnemyData>());
            enemy.enemyId = "enemy_test";
            var item = Track(ScriptableObject.CreateInstance<ItemData>());
            item.itemId = "item_test";
            item.contentScope = InventoryGrantUseCase.ProductionContentScope;
            item.maxStack = maxStack;
            var bounty = Track(ScriptableObject.CreateInstance<BountyData>());
            bounty.bountyId = "bounty_test";
            bounty.contentScope = InventoryGrantUseCase.ProductionContentScope;
            bounty.issuerSettlementId = settlement.settlementId;
            bounty.objectiveType = BountyUseCaseReasons.SupportedObjectiveType;
            bounty.targetEnemyId = enemy.enemyId;
            bounty.requiredCount = 1;
            bounty.allowedAdventureId = "adventure_test";
            bounty.repeatPolicy = BountyUseCaseReasons.SupportedRepeatPolicy;
            bounty.rewardEntries = new[] { new BountyRewardEntry { itemId = item.itemId, quantity = 3 } };
            var catalog = Track(ScriptableObject.CreateInstance<ContentCatalogData>());
            catalog.ReplaceEntries(new[] { settlement }, new[] { enemy }, new[] { item }, new[] { bounty });
            return catalog;
        }

        private T Track<T>(T asset) where T : Object
        {
            assets.Add(asset);
            return asset;
        }
    }
}
