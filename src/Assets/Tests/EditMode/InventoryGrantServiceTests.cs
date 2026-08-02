using System;
using System.Collections.Generic;
using NUnit.Framework;
using TianZhang.Content;
using TianZhang.Game;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class InventoryGrantServiceTests
    {
        private readonly List<UnityEngine.Object> temporaryAssets = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            if (GameSession.Instance != null)
                UnityEngine.Object.DestroyImmediate(GameSession.Instance.gameObject);
            foreach (UnityEngine.Object asset in temporaryAssets)
                UnityEngine.Object.DestroyImmediate(asset);
        }

        [Test]
        public void GrantItemsCommitsSingleAndMultipleItemsWithMergedDuplicateIds()
        {
            ContentCatalogData catalog = CreateCatalog(
                CreateItem("item_lingshi_low", 99),
                CreateItem("item_shijia_piece", 99),
                CreateItem("item_untouched", 99));
            GameSession session = CreateSession();
            StateStepSnapshot untouchedSteps = new StateStepSnapshot(
                shown: true,
                clicked: true,
                opened: true,
                selected: true,
                applied: false,
                completed: false,
                persisted: true);
            session.InventoryStates.Set(new InventoryStateSnapshot("item_lingshi_low", 10, untouchedSteps));
            session.InventoryStates.Set(new InventoryStateSnapshot("item_untouched", 1, untouchedSteps));

            InventoryGrantResult result = session.GrantItems(catalog, new[]
            {
                new InventoryGrantRequest("item_lingshi_low", 2),
                new InventoryGrantRequest("item_shijia_piece", 3),
                new InventoryGrantRequest("item_lingshi_low", 4),
            });

            AssertApplied(result);
            AssertInventory(session, "item_lingshi_low", 16, expectedAppliedSteps: true);
            AssertInventory(session, "item_shijia_piece", 3, expectedAppliedSteps: true);
            Assert.IsTrue(session.InventoryStates.TryGet("item_untouched", out InventoryStateSnapshot untouched));
            Assert.AreSame(untouchedSteps, untouched.Steps);
        }

        [Test]
        public void GrantItemsAcceptsAnExistingStackAtItsExactApprovedLimit()
        {
            ContentCatalogData catalog = CreateCatalog(CreateItem("item_lingshi_low", 99));
            GameSession session = CreateSession();
            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                98,
                UnappliedSteps()));

            InventoryGrantResult result = session.GrantItems(catalog, new[]
            {
                new InventoryGrantRequest("item_lingshi_low", 1),
            });

            AssertApplied(result);
            AssertInventory(session, "item_lingshi_low", 99, expectedAppliedSteps: true);
        }

        [Test]
        public void GrantItemsRejectsEmptyUnknownNonProductionAndInvalidQuantityWithoutMutation()
        {
            ContentCatalogData catalog = CreateCatalog(
                CreateItem("item_lingshi_low", 99),
                CreateItem("item_draft", 99, "content_scope_draft"));
            GameSession session = CreateSession();
            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                5,
                UnappliedSteps()));

            AssertRejectedWithoutMutation(
                session,
                catalog,
                Array.Empty<InventoryGrantRequest>(),
                InventoryGrantFailureReason.EmptyRequest);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_unknown", 1) },
                InventoryGrantFailureReason.ItemNotFound);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_draft", 1) },
                InventoryGrantFailureReason.ItemNotProduction);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_lingshi_low", 0) },
                InventoryGrantFailureReason.QuantityInvalid);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_lingshi_low", -1) },
                InventoryGrantFailureReason.QuantityInvalid);
        }

        [Test]
        public void GrantItemsRejectsSingleAndMergedStackOverflowWithoutMutation()
        {
            ContentCatalogData catalog = CreateCatalog(CreateItem("item_lingshi_low", 99));
            GameSession session = CreateSession();
            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                50,
                UnappliedSteps()));

            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_lingshi_low", 50) },
                InventoryGrantFailureReason.StackLimitExceeded);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[]
                {
                    new InventoryGrantRequest("item_lingshi_low", 25),
                    new InventoryGrantRequest("item_lingshi_low", 25),
                },
                InventoryGrantFailureReason.StackLimitExceeded);
        }

        [Test]
        public void GrantItemsRejectsIntegerMergeOverflowAndInvalidExistingInventoryWithoutMutation()
        {
            ContentCatalogData catalog = CreateCatalog(
                CreateItem("item_lingshi_low", 99),
                CreateItem("item_unapproved", 0));
            GameSession session = CreateSession();

            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[]
                {
                    new InventoryGrantRequest("item_lingshi_low", int.MaxValue),
                    new InventoryGrantRequest("item_lingshi_low", 1),
                },
                InventoryGrantFailureReason.QuantityOverflow);
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_unapproved", 1) },
                InventoryGrantFailureReason.MaxStackInvalid);

            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                100,
                UnappliedSteps()));
            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[] { new InventoryGrantRequest("item_lingshi_low", 1) },
                InventoryGrantFailureReason.ExistingInventoryInvalid);
        }

        [Test]
        public void GrantItemsRejectsAMultiItemBatchWhenALaterGrantFailsWithoutPartialCommit()
        {
            ContentCatalogData catalog = CreateCatalog(
                CreateItem("item_lingshi_low", 99),
                CreateItem("item_shijia_piece", 99));
            GameSession session = CreateSession();
            session.InventoryStates.Set(new InventoryStateSnapshot(
                "item_lingshi_low",
                4,
                UnappliedSteps()));

            AssertRejectedWithoutMutation(
                session,
                catalog,
                new[]
                {
                    new InventoryGrantRequest("item_shijia_piece", 3),
                    new InventoryGrantRequest("item_unknown", 1),
                },
                InventoryGrantFailureReason.ItemNotFound);
        }

        [Test]
        public void TryBuildGrantSeparatesCandidateConstructionFromReplacement()
        {
            ContentCatalogData catalog = CreateCatalog(CreateItem("item_lingshi_low", 99));
            var store = new InventoryStateStore();
            store.Set(new InventoryStateSnapshot("item_lingshi_low", 5, UnappliedSteps()));
            var service = new InventoryGrantService();

            Assert.IsTrue(
                service.TryBuildGrant(
                    store,
                    catalog,
                    new[] { new InventoryGrantRequest("item_lingshi_low", 3) },
                    out IReadOnlyList<InventoryStateSnapshot> candidate,
                    out InventoryGrantFailureReason failureReason),
                failureReason.ToString());
            Assert.AreEqual(5, store.TryGet("item_lingshi_low", out InventoryStateSnapshot before)
                ? before.Quantity
                : -1);

            service.ApplyGrant(store, candidate);

            Assert.AreEqual(8, store.TryGet("item_lingshi_low", out InventoryStateSnapshot after)
                ? after.Quantity
                : -1);

            Assert.IsFalse(
                service.TryBuildGrant(
                    store,
                    catalog,
                    new[] { new InventoryGrantRequest("item_unknown", 1) },
                    out _,
                    out failureReason));
            Assert.AreEqual(InventoryGrantFailureReason.ItemNotFound, failureReason);
            Assert.AreEqual(8, store.TryGet("item_lingshi_low", out after) ? after.Quantity : -1);
        }

        private static GameSession CreateSession()
        {
            return new GameObject("InventoryGrantSession").AddComponent<GameSession>();
        }

        private ContentCatalogData CreateCatalog(params ItemData[] items)
        {
            ContentCatalogData catalog = ScriptableObject.CreateInstance<ContentCatalogData>();
            temporaryAssets.Add(catalog);
            catalog.ReplaceEntries(null, null, items, null);
            return catalog;
        }

        private ItemData CreateItem(string itemId, int maxStack, string contentScope = "content_scope_production")
        {
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            temporaryAssets.Add(item);
            item.itemId = itemId;
            item.contentScope = contentScope;
            item.maxStack = maxStack;
            return item;
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

        private static void AssertApplied(InventoryGrantResult result)
        {
            Assert.IsTrue(result.Applied);
            Assert.AreEqual(InventoryGrantFailureReason.None, result.FailureReason);
        }

        private static void AssertRejectedWithoutMutation(
            GameSession session,
            ContentCatalogData catalog,
            InventoryGrantRequest[] requests,
            InventoryGrantFailureReason expectedReason)
        {
            string before = JsonUtility.ToJson(session.CaptureSaveData());

            InventoryGrantResult result = session.GrantItems(catalog, requests);

            Assert.IsFalse(result.Applied);
            Assert.AreEqual(expectedReason, result.FailureReason);
            Assert.AreEqual(before, JsonUtility.ToJson(session.CaptureSaveData()));
        }

        private static void AssertInventory(
            GameSession session,
            string itemId,
            int expectedQuantity,
            bool expectedAppliedSteps)
        {
            Assert.IsTrue(session.InventoryStates.TryGet(itemId, out InventoryStateSnapshot snapshot));
            Assert.AreEqual(expectedQuantity, snapshot.Quantity);
            Assert.AreEqual(expectedAppliedSteps, snapshot.Steps.Applied);
            Assert.AreEqual(expectedAppliedSteps, snapshot.Steps.Completed);
            Assert.IsFalse(snapshot.Steps.Shown);
            Assert.IsFalse(snapshot.Steps.Clicked);
            Assert.IsFalse(snapshot.Steps.Opened);
            Assert.IsFalse(snapshot.Steps.Selected);
            Assert.IsFalse(snapshot.Steps.Persisted);
        }
    }
}
