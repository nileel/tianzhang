using System;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Character;
using TianZhang.Entity;
using TianZhang.Features.CharacterCreation;
using TianZhang.Game.CharacterCreation;
using UnityEngine;

namespace TianZhang.Tests
{
    public sealed class CharacterStateTests
    {
        [Test]
        public void CharacterResourcesCaptureAndRestoreWithoutCombatState()
        {
            Type type = Load("TianZhang.Character", "TianZhang.Character.CharacterResources");
            object resources = Activator.CreateInstance(type, 100, 25, 40, 10);
            object snapshot = Invoke(resources, "Capture");
            Invoke(resources, "Restore", snapshot);
            Assert.That(Get(resources, "CurrentHealth"), Is.EqualTo(25));
            Assert.That(Get(resources, "CurrentSpirit"), Is.EqualTo(10));
            Assert.That(type.GetProperty("Position"), Is.Null);
            Assert.That(type.GetProperty("CTBUnit"), Is.Null);
        }

        [Test]
        public void CombatModifiersPreserveRawValuesAndRoundOnlyAtDeriveBoundary()
        {
            var modifiers = new CharacterCombatModifiers(
                12.4f, 7.6f, 3.2f, 4.8f, 1.1f, 2.9f,
                15f, 25f, 10f, 20f, 5f, 8f, 15f, 3f);

            Assert.That(modifiers.HpBonus, Is.EqualTo(12.4f));
            Assert.That(modifiers.MpBonus, Is.EqualTo(7.6f));
            Assert.That(modifiers.PhysAtkBonus, Is.EqualTo(3.2f));
            Assert.That(modifiers.MagAtkBonus, Is.EqualTo(4.8f));
            Assert.That(modifiers.PhysDefBonus, Is.EqualTo(1.1f));
            Assert.That(modifiers.MagDefBonus, Is.EqualTo(2.9f));
            Assert.That(modifiers.BlockRate, Is.EqualTo(15f));
            Assert.That(modifiers.BlockReduction, Is.EqualTo(25f));
            Assert.That(modifiers.SoulShieldRate, Is.EqualTo(10f));
            Assert.That(modifiers.SoulShieldReduction, Is.EqualTo(20f));
            Assert.That(modifiers.DodgeRate, Is.EqualTo(5f));
            Assert.That(modifiers.CritRate, Is.EqualTo(8f));
            Assert.That(modifiers.CritDamage, Is.EqualTo(15f));
            Assert.That(modifiers.HitRateBonus, Is.EqualTo(3f));

            CharacterAttributeBonuses bonuses = modifiers.ToAttributeBonuses();
            Assert.That(bonuses.Health, Is.EqualTo(Mathf.RoundToInt(12.4f)));
            Assert.That(bonuses.SpiritResource, Is.EqualTo(Mathf.RoundToInt(7.6f)));
            Assert.That(bonuses.PhysicalAttack, Is.EqualTo(Mathf.RoundToInt(3.2f)));
            Assert.That(bonuses.MagicAttack, Is.EqualTo(Mathf.RoundToInt(4.8f)));
            Assert.That(bonuses.PhysicalDefense, Is.EqualTo(Mathf.RoundToInt(1.1f)));
            Assert.That(bonuses.MagicDefense, Is.EqualTo(Mathf.RoundToInt(2.9f)));
        }

        [Test]
        public void EmptyCombatModifiersAreAllZero()
        {
            CharacterCombatModifiers modifiers = CharacterCombatModifiers.Empty;
            Assert.That(modifiers.HpBonus, Is.EqualTo(0f));
            Assert.That(modifiers.MpBonus, Is.EqualTo(0f));
            Assert.That(modifiers.PhysAtkBonus, Is.EqualTo(0f));
            Assert.That(modifiers.MagAtkBonus, Is.EqualTo(0f));
            Assert.That(modifiers.PhysDefBonus, Is.EqualTo(0f));
            Assert.That(modifiers.MagDefBonus, Is.EqualTo(0f));
            Assert.That(modifiers.BlockRate, Is.EqualTo(0f));
            Assert.That(modifiers.BlockReduction, Is.EqualTo(0f));
            Assert.That(modifiers.SoulShieldRate, Is.EqualTo(0f));
            Assert.That(modifiers.SoulShieldReduction, Is.EqualTo(0f));
            Assert.That(modifiers.DodgeRate, Is.EqualTo(0f));
            Assert.That(modifiers.CritRate, Is.EqualTo(0f));
            Assert.That(modifiers.CritDamage, Is.EqualTo(0f));
            Assert.That(modifiers.HitRateBonus, Is.EqualTo(0f));
        }

        [Test]
        public void FormalCreationVisibleRootSurvivesRuntimeCaptureAndReconstruction()
        {
            var config = Resources.Load<CharacterCreationPointBuyConfig>(
                "Data/CharacterCreation/CharacterCreationPointBuyConfig");
            Assert.That(config, Is.Not.Null);
            CharacterData definition = CharacterCreationRules.BuildCharacterData(
                CharacterCreationCatalog.CreateDefaultDraft(), config);
            try
            {
                Assert.That(definition.visibleRootId, Is.EqualTo("root_water_middle"));
                Assert.That(definition.visibleRootGrade, Is.EqualTo("中品"));
                Assert.That(definition.visibleRootElement, Is.EqualTo("水"));

                CharacterStateSnapshot snapshot = CharacterRuntimeProfile.FromDefinition("player", definition).Capture();
                CharacterRuntimeProfile restored = CharacterRuntimeProfile.FromSnapshot(snapshot);
                Assert.That(snapshot.VisibleRootId, Is.EqualTo(definition.visibleRootId));
                Assert.That(snapshot.VisibleRootGrade, Is.EqualTo(definition.visibleRootGrade));
                Assert.That(snapshot.VisibleRootElement, Is.EqualTo(definition.visibleRootElement));
                Assert.That(restored.VisibleRootId, Is.EqualTo(snapshot.VisibleRootId));
                Assert.That(restored.VisibleRootGrade, Is.EqualTo(snapshot.VisibleRootGrade));
                Assert.That(restored.VisibleRootElement, Is.EqualTo(snapshot.VisibleRootElement));
                Assert.That(restored.Capture().AbilityLoadout.KnownSpells, Is.EqualTo(snapshot.AbilityLoadout.KnownSpells));
                Assert.That(restored.Capture().AbilityLoadout.EquippedSpells, Is.EqualTo(snapshot.AbilityLoadout.EquippedSpells));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void RestoreReplacesVisibleRootFactsAndLegacySnapshotClearsThemToUnknown()
        {
            CharacterData definition = ScriptableObject.CreateInstance<CharacterData>();
            try
            {
                definition.visibleRootId = "root_water_middle";
                definition.visibleRootGrade = "中品";
                definition.visibleRootElement = "水";
                CharacterRuntimeProfile profile = CharacterRuntimeProfile.FromDefinition("player", definition);
                CharacterStateSnapshot before = profile.Capture();
                var replacement = new CharacterStateSnapshot(
                    before.Identity, before.Attributes, before.Resources, before.AbilityLoadout, before.Progression,
                    before.CombatModifiers, before.MainEquipmentBasicAttackProfileId,
                    before.UnarmedBasicAttackProfileId, before.AppearanceProfileId,
                    "root_thunder_high", "上品", "雷");

                profile.Restore(replacement);
                Assert.That(profile.VisibleRootId, Is.EqualTo("root_thunder_high"));
                Assert.That(profile.VisibleRootGrade, Is.EqualTo("上品"));
                Assert.That(profile.VisibleRootElement, Is.EqualTo("雷"));

                var legacy = new CharacterStateSnapshot(
                    before.Identity, before.Attributes, before.Resources, before.AbilityLoadout, before.Progression,
                    before.CombatModifiers, before.MainEquipmentBasicAttackProfileId,
                    before.UnarmedBasicAttackProfileId, before.AppearanceProfileId);
                profile.Restore(legacy);
                Assert.That(profile.VisibleRootId, Is.Null);
                Assert.That(profile.VisibleRootGrade, Is.Null);
                Assert.That(profile.VisibleRootElement, Is.Null);
                Assert.That(profile.Capture().AbilityLoadout.KnownSpells, Is.EqualTo(before.AbilityLoadout.KnownSpells));
                Assert.That(profile.Capture().AbilityLoadout.EquippedSpells, Is.EqualTo(before.AbilityLoadout.EquippedSpells));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static Type Load(string assemblyName, string typeName) { return Assembly.Load(assemblyName).GetType(typeName, true); }
        private static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name).Invoke(target, args); }
        private static object Get(object target, string name) { return target.GetType().GetProperty(name).GetValue(target, null); }
    }
}
