using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TianZhang.Gameplay.Contracts;

namespace TianZhang.Tests
{
    public sealed class CombatUnitPresentationContractsTests
    {
        [Test]
        public void UnitDescriptorKeepsOnlyStablePresentationFacts()
        {
            var descriptor = new CombatUnitPresentationDescriptor(
                "player",
                "combat_player_default_v1",
                CombatUnitDisplayFaction.Player,
                new CombatUnitPresentationHex(2, -1),
                4);

            Assert.That(descriptor.CombatantId, Is.EqualTo("player"));
            Assert.That(descriptor.PresentationProfileId, Is.EqualTo("combat_player_default_v1"));
            Assert.That(descriptor.DisplayFaction, Is.EqualTo(CombatUnitDisplayFaction.Player));
            Assert.That(descriptor.Position.Q, Is.EqualTo(2));
            Assert.That(descriptor.Position.R, Is.EqualTo(-1));
            Assert.That(descriptor.Facing, Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CombatUnitPresentationDescriptor(
                "player", "combat_player_default_v1", CombatUnitDisplayFaction.Player,
                new CombatUnitPresentationHex(0, 0), 6));
        }

        [Test]
        public void EventProjectionCopiesCommittedResultsWithoutWritableRuleObjects()
        {
            var sourceResults = new[]
            {
                new CombatUnitPresentationTargetResult("enemy", 18, true),
            };
            var projection = new CombatUnitPresentationEventProjection(
                "player",
                CombatUnitPresentationEvent.Attack,
                new CombatUnitPresentationHex(0, 0),
                new CombatUnitPresentationHex(1, 0),
                0,
                sourceResults);

            sourceResults[0] = new CombatUnitPresentationTargetResult("other", 1, false);

            Assert.That(projection.ActorCombatantId, Is.EqualTo("player"));
            Assert.That(projection.PresentationEvent, Is.EqualTo(CombatUnitPresentationEvent.Attack));
            Assert.That(projection.StartPosition.Q, Is.EqualTo(0));
            Assert.That(projection.StartPosition.R, Is.EqualTo(0));
            Assert.That(projection.EndPosition.Q, Is.EqualTo(1));
            Assert.That(projection.EndPosition.R, Is.EqualTo(0));
            Assert.That(projection.Facing, Is.EqualTo(0));
            Assert.That(projection.TargetResults, Has.Count.EqualTo(1));
            Assert.That(projection.TargetResults[0].CombatantId, Is.EqualTo("enemy"));
            Assert.That(projection.TargetResults[0].FinalDamage, Is.EqualTo(18));
            Assert.That(projection.TargetResults[0].IsDead, Is.True);
        }

        [Test]
        public void PortSeparatesLifecycleAndEventsFromTheExistingHudSink()
        {
            string[] portMethods = typeof(ICombatUnitPresentationPort).GetMethods()
                .Select(method => method.Name).OrderBy(name => name).ToArray();
            CollectionAssert.AreEqual(new[] { "Clear", "Prepare", "Present", "Remove", "Spawn" }, portMethods);

            string[] hudMethods = typeof(ICombatPresentationSink).GetMethods()
                .Select(method => method.Name).OrderBy(name => name).ToArray();
            CollectionAssert.AreEqual(new[] { "AppendLog", "ClearLog", "Hide", "Present" }, hudMethods);
            Assert.That(typeof(CombatHudSnapshot).GetConstructors(), Has.Length.EqualTo(1));
        }

        [Test]
        public void HudCopiesAbilityListsAndMovementCandidatesWithoutMutableRuleObjects()
        {
            var arts = new[] { "art" };
            var divines = new[] { "divine" };
            var path = new[] { new CombatUnitPresentationHex(0, 1) };
            var candidate = new CombatActionCandidate(CombatActionKind.Move, "player", null, null, 12,
                path[0], 33333, path);
            var quote = new CombatActionPreviewSnapshot(candidate, "移动", "角色", "自身", true, "", 0, true,
                0, 115, 3, actionPointCostUnits: 33333, ctPenalty: 0, reuseCooldownTicks: 0);
            var actions = new[] { quote };
            var hud = new CombatHudSnapshot(null, null, "行动", true, arts, divines, actions: actions,
                actionPointLimitUnits: 100000, pendingCtPenalty: 15);
            arts[0] = divines[0] = "changed";
            path[0] = new CombatUnitPresentationHex(2, 2);
            actions[0] = null;
            Assert.That(hud.ArtProfileIds.Single(), Is.EqualTo("art"));
            Assert.That(hud.DivineProfileIds.Single(), Is.EqualTo("divine"));
            Assert.That(hud.Actions.Single(), Is.SameAs(quote));
            Assert.That(candidate.MovementPath.Single().R, Is.EqualTo(1));
            Assert.That(candidate.Destination.Value.R, Is.EqualTo(1));
            Assert.That(quote.ActionPointCostUnits, Is.EqualTo(33333));
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<string>)hud.ArtProfileIds)[0] = "other");
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<CombatActionPreviewSnapshot>)hud.Actions).Clear());
            foreach (Type dto in new[] { typeof(CombatActionCandidate), typeof(CombatActionPreviewSnapshot),
                typeof(CombatMovementOption), typeof(CombatHudSnapshot), typeof(CombatArtTargetSnapshot) })
            {
                Assert.That(dto.GetProperties().All(property => !property.CanWrite), Is.True, dto.Name);
                Assert.That(dto.GetFields().Length, Is.Zero, dto.Name);
                Assert.That(dto.GetProperties().All(property => !property.PropertyType.Namespace.StartsWith("TianZhang.Combat") &&
                    !property.PropertyType.Namespace.StartsWith("TianZhang.Character")), Is.True, dto.Name);
            }
        }

        [Test]
        public void GameplayContractHasNoCombatOrUnityImplementationDependency()
        {
            string source = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath,
                "Scripts",
                "Modules",
                "GameplayContracts",
                "CombatPresentationContracts.cs"));

            foreach (string forbidden in new[]
            {
                "TianZhang.Combat", "CombatActionResult", "CombatantSnapshot", "GameObject",
                "Prefab", "Renderer", "Sprite", "Mesh",
            })
            {
                StringAssert.DoesNotContain(forbidden, source);
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    CombatUnitPresentationEvent.Idle,
                    CombatUnitPresentationEvent.Move,
                    CombatUnitPresentationEvent.Attack,
                    CombatUnitPresentationEvent.Hit,
                    CombatUnitPresentationEvent.Cast,
                    CombatUnitPresentationEvent.Death,
                },
                (CombatUnitPresentationEvent[])Enum.GetValues(typeof(CombatUnitPresentationEvent)));
        }
    }
}
