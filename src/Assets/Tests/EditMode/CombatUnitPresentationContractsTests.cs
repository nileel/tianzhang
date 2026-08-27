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
