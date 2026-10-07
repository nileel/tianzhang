using System;
using System.Collections.Generic;
using TianZhang.Entity;
using UnityEngine;

namespace TianZhang.Character
{
    /// <summary>
    /// Immutable projection of the fourteen explicit combat fields carried from <see cref="CharacterData"/>
    /// into battle. It stores raw floats verbatim; the six first-order bonuses are rounded to integers only
    /// when consumed by <see cref="CharacterAttributes.Derive"/>.
    /// The action economy modifier inputs are the frozen long-term correction protocol; Character owns only
    /// their declaration, ranges and identity de-duplication, never a growth tree, an effect or a source registry.
    /// </summary>
    public sealed class CharacterCombatModifiers
    {
        private static readonly ActionEconomyModifierInput[] NoActionEconomyInputs = new ActionEconomyModifierInput[0];

        public static readonly CharacterCombatModifiers Empty = new CharacterCombatModifiers(
            0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, NoActionEconomyInputs);

        public CharacterCombatModifiers(
            float hpBonus,
            float mpBonus,
            float physAtkBonus,
            float magAtkBonus,
            float physDefBonus,
            float magDefBonus,
            float blockRate,
            float blockReduction,
            float soulShieldRate,
            float soulShieldReduction,
            float dodgeRate,
            float critRate,
            float critDamage,
            float hitRateBonus,
            IEnumerable<ActionEconomyModifierInput> actionEconomyInputs)
        {
            HpBonus = hpBonus;
            MpBonus = mpBonus;
            PhysAtkBonus = physAtkBonus;
            MagAtkBonus = magAtkBonus;
            PhysDefBonus = physDefBonus;
            MagDefBonus = magDefBonus;
            BlockRate = blockRate;
            BlockReduction = blockReduction;
            SoulShieldRate = soulShieldRate;
            SoulShieldReduction = soulShieldReduction;
            DodgeRate = dodgeRate;
            CritRate = critRate;
            CritDamage = critDamage;
            HitRateBonus = hitRateBonus;
            ActionEconomyInputs = CopyAndValidateActionEconomyInputs(actionEconomyInputs);
        }

        public float HpBonus { get; }
        public float MpBonus { get; }
        public float PhysAtkBonus { get; }
        public float MagAtkBonus { get; }
        public float PhysDefBonus { get; }
        public float MagDefBonus { get; }
        public float BlockRate { get; }
        public float BlockReduction { get; }
        public float SoulShieldRate { get; }
        public float SoulShieldReduction { get; }
        public float DodgeRate { get; }
        public float CritRate { get; }
        public float CritDamage { get; }
        public float HitRateBonus { get; }

        /// <summary>Declared long-term action economy corrections. An explicit empty array means no corrections and a zero refund budget.</summary>
        public IReadOnlyList<ActionEconomyModifierInput> ActionEconomyInputs { get; }

        public static CharacterCombatModifiers FromDefinition(CharacterData definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            return new CharacterCombatModifiers(
                definition.hpBonus,
                definition.mpBonus,
                definition.physAtkBonus,
                definition.magAtkBonus,
                definition.physDefBonus,
                definition.magDefBonus,
                definition.blockRate,
                definition.blockReduction,
                definition.soulShieldRate,
                definition.soulShieldReduction,
                definition.dodgeRate,
                definition.critRate,
                definition.critDamage,
                definition.hitRateBonus,
                NoActionEconomyInputs);
        }

        /// <summary>Converts the six first-order bonuses to integer bonuses at the Derive boundary only.</summary>
        public CharacterAttributeBonuses ToAttributeBonuses()
        {
            return new CharacterAttributeBonuses
            {
                Health = Mathf.RoundToInt(HpBonus),
                SpiritResource = Mathf.RoundToInt(MpBonus),
                PhysicalAttack = Mathf.RoundToInt(PhysAtkBonus),
                MagicAttack = Mathf.RoundToInt(MagAtkBonus),
                PhysicalDefense = Mathf.RoundToInt(PhysDefBonus),
                MagicDefense = Mathf.RoundToInt(MagDefBonus),
            };
        }

        /// <summary>
        /// Validates the frozen action economy protocol: non-empty stable identity strings, one entry per
        /// <c>sourceInstanceId</c>, and no positive refund without both a per-turn count and a per-turn budget.
        /// Unknown definitions are a consumer-side matching rule; Character owns no source registry.
        /// </summary>
        public static void ValidateActionEconomyInputs(IEnumerable<ActionEconomyModifierInput> inputs)
        {
            CopyAndValidateActionEconomyInputs(inputs);
        }

        private static ActionEconomyModifierInput[] CopyAndValidateActionEconomyInputs(
            IEnumerable<ActionEconomyModifierInput> inputs)
        {
            if (inputs == null) return NoActionEconomyInputs;

            var copy = new List<ActionEconomyModifierInput>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActionEconomyModifierInput input in inputs)
            {
                if (input == null) throw new ArgumentException("An action economy modifier input is missing.", nameof(inputs));
                if (!seen.Add(input.SourceInstanceId))
                {
                    throw new ArgumentException(
                        "Duplicate action economy source instance id: " + input.SourceInstanceId + ".", nameof(inputs));
                }
                copy.Add(input);
            }
            return copy.Count == 0 ? NoActionEconomyInputs : copy.ToArray();
        }
    }

    /// <summary>
    /// Frozen long-term action economy correction protocol carried beside the combat modifiers.
    /// Every field is an explicit long-term input of one declared source; Character only validates and
    /// projects it, and Combat consumes an immutable scalar copy. No runtime AP balance, turn ledger or
    /// growth tree lives here.
    /// </summary>
    public sealed class ActionEconomyModifierInput
    {
        /// <summary>Controlled command vocabulary mirrored from the existing combat command kinds; Character takes no Combat reference.</summary>
        public enum CommandKind
        {
            BasicAttack = 1,
            Art = 2,
            Divine = 3,
            Guard = 4,
            Wait = 5,
            Move = 6,
            SwapSpell = 7,
            EndTurn = 8,
        }

        /// <summary>Existing controlled success event that may carry a bounded refund; None declares no refund at all.</summary>
        public enum SuccessEvent
        {
            None = 0,
            RootActionCommitted = 1,
        }

        public ActionEconomyModifierInput(
            string sourceInstanceId,
            string sourceDefinitionId,
            CommandKind actionKind,
            string profileId,
            int actionPointMaximumBonusUnits,
            int movementEfficiencyBonusMilliUnits,
            int fixedReductionUnits,
            int retainedCostPermille,
            int ctPenaltyReduction,
            int reuseCooldownReductionTicks,
            int minimumNetCostUnits,
            SuccessEvent refundOnEvent,
            int refundUnits,
            int refundLimitPerOwnTurn,
            int refundBudgetPerOwnTurnUnits)
        {
            SourceInstanceId = RequireStableId(sourceInstanceId, nameof(sourceInstanceId));
            SourceDefinitionId = RequireStableId(sourceDefinitionId, nameof(sourceDefinitionId));
            if (!Enum.IsDefined(typeof(CommandKind), actionKind))
                throw new ArgumentOutOfRangeException(nameof(actionKind), actionKind, "Unknown action economy command kind.");
            ActionKind = actionKind;
            ProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId.Trim();
            ActionPointMaximumBonusUnits = actionPointMaximumBonusUnits;
            MovementEfficiencyBonusMilliUnits = movementEfficiencyBonusMilliUnits;
            FixedReductionUnits = RequireNonNegative(fixedReductionUnits, nameof(fixedReductionUnits));
            if (retainedCostPermille < 0 || retainedCostPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(retainedCostPermille), retainedCostPermille, "Retained cost permille must be between 0 and 1000.");
            RetainedCostPermille = retainedCostPermille;
            CtPenaltyReduction = RequireNonNegative(ctPenaltyReduction, nameof(ctPenaltyReduction));
            ReuseCooldownReductionTicks = RequireNonNegative(reuseCooldownReductionTicks, nameof(reuseCooldownReductionTicks));
            if (minimumNetCostUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(minimumNetCostUnits), minimumNetCostUnits, "Minimum net cost units must be positive.");
            MinimumNetCostUnits = minimumNetCostUnits;
            if (!Enum.IsDefined(typeof(SuccessEvent), refundOnEvent))
                throw new ArgumentOutOfRangeException(nameof(refundOnEvent), refundOnEvent, "Unknown action economy success event.");
            RefundOnEvent = refundOnEvent;
            RefundUnits = RequireNonNegative(refundUnits, nameof(refundUnits));
            RefundLimitPerOwnTurn = RequireNonNegative(refundLimitPerOwnTurn, nameof(refundLimitPerOwnTurn));
            RefundBudgetPerOwnTurnUnits = RequireNonNegative(refundBudgetPerOwnTurnUnits, nameof(refundBudgetPerOwnTurnUnits));
            if (RefundUnits > 0 &&
                (RefundOnEvent == SuccessEvent.None || RefundLimitPerOwnTurn == 0 || RefundBudgetPerOwnTurnUnits == 0))
            {
                throw new ArgumentException(
                    "A positive refund must declare its root action event, a per-turn count and a per-turn budget.",
                    nameof(refundOnEvent));
            }
        }

        public string SourceInstanceId { get; }
        public string SourceDefinitionId { get; }
        public CommandKind ActionKind { get; }
        public string ProfileId { get; }
        public int ActionPointMaximumBonusUnits { get; }
        public int MovementEfficiencyBonusMilliUnits { get; }
        public int FixedReductionUnits { get; }
        public int RetainedCostPermille { get; }
        public int CtPenaltyReduction { get; }
        public int ReuseCooldownReductionTicks { get; }
        public int MinimumNetCostUnits { get; }
        public SuccessEvent RefundOnEvent { get; }
        public int RefundUnits { get; }
        public int RefundLimitPerOwnTurn { get; }
        public int RefundBudgetPerOwnTurnUnits { get; }

        private static string RequireStableId(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("A stable non-empty identifier is required.", parameterName);
            return value.Trim();
        }

        private static int RequireNonNegative(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName, value, "The value must not be negative.");
            return value;
        }
    }
}
