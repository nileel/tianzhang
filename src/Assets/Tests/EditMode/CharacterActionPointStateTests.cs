using System;
using NUnit.Framework;
using TianZhang.Character;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Tests
{
    /// <summary>
    /// U-COMBAT-AP-CHAR-01. Covers the long-term action point maximum and movement efficiency inputs and the
    /// frozen action economy modifier protocol owned by Character: explicit creation inputs, integer range
    /// rejection, presence semantics, capture/restore round trip and all-or-nothing restore.
    /// </summary>
    public sealed class CharacterActionPointStateTests
    {
        private const int ApprovedMaximumUnits = 100000;
        private const int ApprovedEfficiencyMilliUnits = 2000;

        [Test]
        public void PlayerAndEnemyDefinitionsCarryTheSameExplicitLongTermInputs()
        {
            CharacterData playerDefinition = CreateDefinition();
            CharacterData enemyDefinition = CreateDefinition();
            try
            {
                CharacterAttributes player = CharacterAttributes.FromDefinition(playerDefinition);
                CharacterAttributes enemy = CharacterAttributes.FromDefinition(enemyDefinition);

                Assert.That(player.ActionPointMaximumUnits, Is.EqualTo(ApprovedMaximumUnits));
                Assert.That(player.MovementEfficiencyMilliUnits, Is.EqualTo(ApprovedEfficiencyMilliUnits));
                Assert.That(player.ActionEconomyInputsPresent, Is.True);
                Assert.That(enemy.ActionPointMaximumUnits, Is.EqualTo(player.ActionPointMaximumUnits));
                Assert.That(enemy.MovementEfficiencyMilliUnits, Is.EqualTo(player.MovementEfficiencyMilliUnits));
                Assert.That(enemy.ActionEconomyInputsPresent, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(playerDefinition);
                UnityEngine.Object.DestroyImmediate(enemyDefinition);
            }
        }

        [TestCase(1L, 0L)]
        [TestCase(100000L, 0L)]
        [TestCase(100000L, 2000L)]
        [TestCase(int.MaxValue, int.MaxValue)]
        public void LegalLongTermInputsAreAcceptedIncludingZeroMovementEfficiency(long maximumUnits, long efficiency)
        {
            CharacterAttributes attributes = new CharacterAttributes(
                10, 10, 10, 10, 10, 10, 10, maximumUnits, efficiency, true);

            Assert.That(attributes.ActionPointMaximumUnits, Is.EqualTo((int)maximumUnits));
            Assert.That(attributes.MovementEfficiencyMilliUnits, Is.EqualTo((int)efficiency));
            Assert.That(attributes.ActionEconomyInputsPresent, Is.True);
        }

        [TestCase(0L, 2000L)]
        [TestCase(-1L, 2000L)]
        [TestCase(2147483648L, 2000L)]
        [TestCase(long.MaxValue, 2000L)]
        public void MissingZeroNegativeOrOverflowingMaximumUnitsIsRejected(long maximumUnits, long efficiency)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CharacterAttributes(10, 10, 10, 10, 10, 10, 10, maximumUnits, efficiency, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CharacterAttributesSnapshot(10, 10, 10, 10, 10, 10, 10, maximumUnits, efficiency, true));
        }

        [TestCase(100000L, -1L)]
        [TestCase(100000L, 2147483648L)]
        [TestCase(100000L, long.MaxValue)]
        public void NegativeOrOverflowingMovementEfficiencyIsRejected(long maximumUnits, long efficiency)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CharacterAttributes(10, 10, 10, 10, 10, 10, 10, maximumUnits, efficiency, true));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CharacterAttributesSnapshot(10, 10, 10, 10, 10, 10, 10, maximumUnits, efficiency, true));
        }

        [Test]
        public void MissingPresenceMarkerIsRejectedEvenWhenBothValuesArePresent()
        {
            Assert.Throws<ArgumentException>(() =>
                new CharacterAttributes(10, 10, 10, 10, 10, 10, 10, ApprovedMaximumUnits, ApprovedEfficiencyMilliUnits, false));
            Assert.Throws<ArgumentException>(() =>
                new CharacterAttributesSnapshot(10, 10, 10, 10, 10, 10, 10, ApprovedMaximumUnits, ApprovedEfficiencyMilliUnits, false));

            // A legal v=0 is only distinguishable from a missing input through the presence marker.
            Assert.Throws<ArgumentException>(() =>
                new CharacterAttributes(10, 10, 10, 10, 10, 10, 10, ApprovedMaximumUnits, 0L, false));
        }

        [Test]
        public void DefinitionWithoutExplicitInputsIsRejectedInsteadOfFallingBackToTheApprovedValues()
        {
            CharacterData definition = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                definition.charName = "缺少长期输入";
                definition.realmMultiplier = 1f;
                Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAttributes.FromDefinition(definition));
                Assert.Throws<ArgumentOutOfRangeException>(() => CharacterRuntimeProfile.FromDefinition("player", definition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void CaptureAndRestoreRoundTripKeepsTheLongTermInputsWithoutReapplyingThem()
        {
            CharacterData definition = CreateDefinition();
            try
            {
                definition.availableSpells = new[] { "spell_guanshiji" };
                definition.equippedSpells = new[] { "spell_guanshiji" };
                definition.maxSpellSlots = 1;
                CharacterRuntimeProfile profile = CharacterRuntimeProfile.FromDefinition("player", definition);
                CharacterStateSnapshot snapshot = profile.Capture();

                CharacterRuntimeProfile restored = CharacterRuntimeProfile.FromSnapshot(snapshot);
                CharacterStateSnapshot second = restored.Capture();

                Assert.That(second.Attributes.ActionPointMaximumUnits, Is.EqualTo(ApprovedMaximumUnits));
                Assert.That(second.Attributes.MovementEfficiencyMilliUnits, Is.EqualTo(ApprovedEfficiencyMilliUnits));
                Assert.That(second.Attributes.ActionEconomyInputsPresent, Is.True);
                Assert.That(second.Attributes.ActionPointMaximumUnits, Is.EqualTo(snapshot.Attributes.ActionPointMaximumUnits));
                Assert.That(restored.Attributes.ActionPointMaximumUnits, Is.EqualTo(profile.Attributes.ActionPointMaximumUnits));
                Assert.That(restored.CombatModifiers.ActionEconomyInputs, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void FailedRestoreKeepsIdentityAttributesResourcesLoadoutProgressionAndNewInputsUnchanged()
        {
            CharacterData definition = CreateDefinition();
            try
            {
                definition.availableSpells = new[] { "spell_guanshiji", "spell_julingshu" };
                definition.equippedSpells = new[] { "spell_guanshiji" };
                definition.maxSpellSlots = 1;
                CharacterRuntimeProfile profile = CharacterRuntimeProfile.FromDefinition("player", definition);
                CharacterStateSnapshot before = profile.Capture();

                AssertRejectedWithoutAnyChange(profile, before, WithResources(before, 500, 900, 100, 50));
                AssertRejectedWithoutAnyChange(profile, before, WithProgression(before, 0f));
                AssertRejectedWithoutAnyChange(profile, before, WithIdentity(before, "", "无身份"));
                AssertRejectedWithoutAnyChange(profile, before, WithLoadout(before,
                    new AbilityLoadoutSnapshot(new[] { "spell_guanshiji" }, new string[0],
                        new[] { "spell_julingshu" }, new string[0], 1, 0)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void NoDeclaredSourceLeavesAnExplicitEmptyArrayAndZeroRefundBudget()
        {
            CharacterData definition = CreateDefinition();
            try
            {
                CharacterCombatModifiers modifiers = CharacterCombatModifiers.FromDefinition(definition);
                Assert.That(modifiers.ActionEconomyInputs, Is.Empty);
                Assert.That(CharacterCombatModifiers.Empty.ActionEconomyInputs, Is.Empty);
                Assert.That(modifiers.HpBonus, Is.EqualTo(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void OnlyEmptySourcesAreAcceptedByTheCharacterOwner()
        {
            ActionEconomyModifierInput valid = CreateInput("gear_a", "definition_a");
            Assert.Throws<ArgumentException>(() =>
                CharacterCombatModifiers.ValidateActionEconomyInputs(new[] { valid }));
            CharacterCombatModifiers.ValidateActionEconomyInputs(new ActionEconomyModifierInput[0]);
            CharacterCombatModifiers.ValidateActionEconomyInputs(null);

            Assert.Throws<ArgumentException>(() =>
                CharacterCombatModifiers.ValidateActionEconomyInputs(new[] { valid, CreateInput("gear_a", "definition_b") }));
            Assert.Throws<ArgumentException>(() =>
                CharacterCombatModifiers.ValidateActionEconomyInputs(new ActionEconomyModifierInput[] { null }));
            Assert.Throws<ArgumentException>(() =>
                new CharacterCombatModifiers(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                    new[] { valid, CreateInput("gear_a", "definition_b") }));
        }

        [Test]
        public void DeclaredSourceProtocolFieldsAreRangeChecked()
        {
            Assert.Throws<ArgumentException>(() => CreateInput("", "definition_a"));
            Assert.Throws<ArgumentException>(() => CreateInput("gear_a", " "));
            Assert.DoesNotThrow(() => CreateInput("gear_a", "definition_a", retainedCostPermille: 0));
            Assert.DoesNotThrow(() => CreateInput("gear_a", "definition_a", retainedCostPermille: 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", retainedCostPermille: 1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", retainedCostPermille: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", fixedReductionUnits: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", ctPenaltyReduction: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", reuseCooldownReductionTicks: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", minimumNetCostUnits: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a", refundUnits: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a",
                actionKind: (ActionEconomyModifierInput.CommandKind)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => CreateInput("gear_a", "definition_a",
                refundOnEvent: (ActionEconomyModifierInput.SuccessEvent)99));
            // A positive refund must declare its root action event, a per-turn count and a per-turn budget.
            Assert.Throws<ArgumentException>(() => CreateInput("gear_a", "definition_a", refundUnits: 5000));
            Assert.Throws<ArgumentException>(() => CreateInput("gear_a", "definition_a",
                refundUnits: 5000,
                refundOnEvent: ActionEconomyModifierInput.SuccessEvent.RootActionCommitted,
                refundLimitPerOwnTurn: 0,
                refundBudgetPerOwnTurnUnits: 0));
            Assert.DoesNotThrow(() => CreateInput("gear_a", "definition_a",
                refundUnits: 5000,
                refundOnEvent: ActionEconomyModifierInput.SuccessEvent.RootActionCommitted,
                refundLimitPerOwnTurn: 2,
                refundBudgetPerOwnTurnUnits: 20000));
        }

        [Test]
        public void ProtocolFixtureKeepsItsFieldsWithoutAuthorizingAProductionSource()
        {
            ActionEconomyModifierInput input = CreateInput("gear_a", "definition_a");
            Assert.That(input.SourceInstanceId, Is.EqualTo("gear_a"));
            Assert.That(input.SourceDefinitionId, Is.EqualTo("definition_a"));
            Assert.That(input.ActionKind, Is.EqualTo(ActionEconomyModifierInput.CommandKind.Guard));
            Assert.That(input.ProfileId, Is.Null);
            Assert.That(input.MinimumNetCostUnits, Is.EqualTo(1));
        }

        [TestCase("unknown_source", null)]
        [TestCase("unknown_source", "unknown_profile")]
        [TestCase("definition_a", "basic_unarmed")]
        [TestCase("basic_unarmed", "basic_unarmed")]
        public void NonEmptySourcesCannotEnterCharacterEvenWithAnExistingAttackId(string sourceId, string profileId)
        {
            ActionEconomyModifierInput input = CreateInput("instance_a", sourceId, profileId: profileId);

            Assert.Throws<ArgumentException>(() =>
                CharacterCombatModifiers.ValidateActionEconomyInputs(new[] { input }));
            Assert.Throws<ArgumentException>(() =>
                new CharacterCombatModifiers(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
                    new[] { input }));
        }

        private static void AssertRejectedWithoutAnyChange(
            CharacterRuntimeProfile profile,
            CharacterStateSnapshot before,
            CharacterStateSnapshot replacement)
        {
            Assert.Throws<ArgumentException>(() => profile.Restore(replacement));

            CharacterStateSnapshot after = profile.Capture();
            Assert.That(after.Identity.CharacterId, Is.EqualTo(before.Identity.CharacterId));
            Assert.That(after.Identity.DisplayName, Is.EqualTo(before.Identity.DisplayName));
            Assert.That(after.Attributes.RootBone, Is.EqualTo(before.Attributes.RootBone));
            Assert.That(after.Attributes.Reaction, Is.EqualTo(before.Attributes.Reaction));
            Assert.That(after.Attributes.ActionPointMaximumUnits, Is.EqualTo(before.Attributes.ActionPointMaximumUnits));
            Assert.That(after.Attributes.MovementEfficiencyMilliUnits, Is.EqualTo(before.Attributes.MovementEfficiencyMilliUnits));
            Assert.That(after.Attributes.ActionEconomyInputsPresent, Is.EqualTo(before.Attributes.ActionEconomyInputsPresent));
            Assert.That(after.Resources.MaximumHealth, Is.EqualTo(before.Resources.MaximumHealth));
            Assert.That(after.Resources.CurrentHealth, Is.EqualTo(before.Resources.CurrentHealth));
            Assert.That(after.Resources.MaximumSpirit, Is.EqualTo(before.Resources.MaximumSpirit));
            Assert.That(after.Resources.CurrentSpirit, Is.EqualTo(before.Resources.CurrentSpirit));
            Assert.That(after.AbilityLoadout.KnownSpells, Is.EqualTo(before.AbilityLoadout.KnownSpells));
            Assert.That(after.AbilityLoadout.EquippedSpells, Is.EqualTo(before.AbilityLoadout.EquippedSpells));
            Assert.That(after.Progression.GongFaId, Is.EqualTo(before.Progression.GongFaId));
            Assert.That(after.Progression.RealmStage, Is.EqualTo(before.Progression.RealmStage));
            Assert.That(after.Progression.RealmMultiplier, Is.EqualTo(before.Progression.RealmMultiplier));
            Assert.That(after.CombatModifiers.ActionEconomyInputs, Is.Empty);
        }

        private static CharacterStateSnapshot WithIdentity(CharacterStateSnapshot source, string characterId, string displayName)
        {
            return Rebuild(source, new CharacterIdentitySnapshot(characterId, displayName), source.Resources,
                source.AbilityLoadout, source.Progression, source.Attributes);
        }

        private static CharacterStateSnapshot WithResources(
            CharacterStateSnapshot source, int maximumHealth, int currentHealth, int maximumSpirit, int currentSpirit)
        {
            return Rebuild(source, source.Identity,
                new CharacterResourcesSnapshot(maximumHealth, currentHealth, maximumSpirit, currentSpirit),
                source.AbilityLoadout, source.Progression, source.Attributes);
        }

        private static CharacterStateSnapshot WithLoadout(CharacterStateSnapshot source, AbilityLoadoutSnapshot loadout)
        {
            return Rebuild(source, source.Identity, source.Resources, loadout, source.Progression, source.Attributes);
        }

        private static CharacterStateSnapshot WithProgression(CharacterStateSnapshot source, float realmMultiplier)
        {
            return Rebuild(source, source.Identity, source.Resources, source.AbilityLoadout,
                new CharacterProgressionSnapshot(source.Progression.GongFaId, source.Progression.RealmStage, realmMultiplier),
                source.Attributes);
        }

        private static CharacterStateSnapshot Rebuild(
            CharacterStateSnapshot source,
            CharacterIdentitySnapshot identity,
            CharacterResourcesSnapshot resources,
            AbilityLoadoutSnapshot loadout,
            CharacterProgressionSnapshot progression,
            CharacterAttributesSnapshot attributes)
        {
            return new CharacterStateSnapshot(
                identity, attributes, resources, loadout, progression, source.CombatModifiers,
                source.MainEquipmentBasicAttackProfileId, source.UnarmedBasicAttackProfileId, source.AppearanceProfileId,
                source.VisibleRootId, source.VisibleRootGrade, source.VisibleRootElement);
        }

        private static CharacterData CreateDefinition()
        {
            CharacterData definition = ScriptableObject.CreateInstance<CharacterData>();
            definition.charName = "长期输入角色";
            definition.realmMultiplier = 1f;
            definition.actionPointMaximumUnits = ApprovedMaximumUnits;
            definition.movementEfficiencyMilliUnits = ApprovedEfficiencyMilliUnits;
            return definition;
        }

        private static ActionEconomyModifierInput CreateInput(
            string sourceInstanceId,
            string sourceDefinitionId,
            int retainedCostPermille = 0,
            int fixedReductionUnits = 0,
            int ctPenaltyReduction = 0,
            int reuseCooldownReductionTicks = 0,
            int minimumNetCostUnits = 1,
            int refundUnits = 0,
            ActionEconomyModifierInput.CommandKind actionKind = ActionEconomyModifierInput.CommandKind.Guard,
            ActionEconomyModifierInput.SuccessEvent refundOnEvent = ActionEconomyModifierInput.SuccessEvent.None,
            int refundLimitPerOwnTurn = 0,
            int refundBudgetPerOwnTurnUnits = 0,
            string profileId = null)
        {
            return new ActionEconomyModifierInput(
                sourceInstanceId,
                sourceDefinitionId,
                actionKind,
                profileId,
                0,
                0,
                fixedReductionUnits,
                retainedCostPermille,
                ctPenaltyReduction,
                reuseCooldownReductionTicks,
                minimumNetCostUnits,
                refundOnEvent,
                refundUnits,
                refundLimitPerOwnTurn,
                refundBudgetPerOwnTurnUnits);
        }
    }
}
