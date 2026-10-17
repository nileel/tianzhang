using System;
using System.Collections.Generic;
using System.Linq;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    public sealed class CombatCommandPreview
    {
        internal CombatCommandPreview(CombatSession session, CombatCommand command, CombatActionResult validation,
            int cost, int minimum, int spirit, int penalty, int cooldown, CombatActionEconomyModifier modifier,
            string facts, bool remains, CombatTurnPreview turn, int baseNetCost, bool repeats, int count, int multiplier,
            CombatAttackAreaQueryResult attackArea)
        {
            Session = session; Command = command; Validation = validation; ActionPointCostUnits = cost;
            MinimumNetCostUnits = minimum; SpiritCost = spirit; CtPenalty = penalty; ReuseCooldownTicks = cooldown;
            EquipmentFacts = command.Kind == CombatCommandKind.SwapEquipment ? session.ExternalResources?.QueryEquipment(command)?.Facts : null;
            ItemResource = command.Kind == CombatCommandKind.UseItem ? session.ExternalResources?.QueryItem(command) : null;
            ItemHealthRecovery = ItemResource?.HealthRecovery ?? 0;
            ExpectedHealthRecovery = ActorHealthRecovery(session, command, ItemHealthRecovery);
            Modifier = modifier; Facts = facts; RemainsInTurn = remains; Turn = turn;
            BaseNetCostUnits = baseNetCost; AppliesRepeatCost = repeats;
            CommittedUseCount = count; RepeatMultiplierPermille = multiplier;
            AttackArea = attackArea;
            RuleSetVersion = session.RuleSet?.RuleSetVersion ?? string.Empty;
            RuleSetSourceDigest = session.RuleSet?.SourceDigest ?? string.Empty;
            AbilityTierId = session.TryGetProfile(command.ProfileId, out var profile)
                ? profile.RuleMetadata?.Tier.AbilityTierId ?? string.Empty : string.Empty;
            TurnSequence = session.TurnSequence; StateVersion = session.StateVersion;
            MovementEfficiencyMilliUnits = session.Combatants.TryGet(command.ActorId, out var actor) ? actor.MovementEfficiencyMilliUnits : 0;
            AbilityInstanceId = actor == null || command.ProfileId.Length == 0 ? string.Empty : actor.GetAbilityInstanceId(command.ProfileId);
        }
        internal CombatSession Session { get; }
        internal CombatCommand Command { get; }
        internal CombatActionEconomyModifier Modifier { get; }
        internal string Facts { get; }
        internal int BaseNetCostUnits { get; }
        internal bool AppliesRepeatCost { get; }
        internal string EquipmentFacts { get; }
        internal CombatItemResource ItemResource { get; }
        public string ItemId => Command.ItemId;
        public int ItemQuantityCost => Command.Kind == CombatCommandKind.UseItem ? Command.ItemQuantityCost : 0;
        public int ItemAvailableQuantity => ItemResource?.Quantity ?? 0;
        public long InventoryVersion => ItemResource?.Version ?? 0;
        public int ItemHealthRecovery { get; }
        public int ExpectedHealthRecovery { get; }
        private static int ActorHealthRecovery(CombatSession session, CombatCommand command, int requested) =>
            session.Combatants.TryGet(command.ActorId, out var actor) && actor.IsAlive
                ? Math.Min(requested, actor.MaximumHealth - actor.CurrentHealth) : 0;
        public string ActorId => Command.ActorId;
        public CombatCommandKind Kind => Command.Kind;
        public string TargetId => Command.TargetId;
        public string ProfileId => Command.ProfileId;
        public int? AttackDirection => Command.AttackDirection;
        public CombatAttackAreaQueryResult AttackArea { get; }
        public HexCoord? Destination => Command.Destination;
        public int SlotIndex => Command.SlotIndex;
        public long ContextVersion => Command.ContextVersion;
        public long TurnSequence { get; }
        public long StateVersion { get; }
        public CombatActionResult Validation { get; }
        public bool IsLegal => Validation.Succeeded;
        public string RejectionReason => Validation.RejectionReason;
        public int ActionPointCostUnits { get; }
        public int MinimumNetCostUnits { get; }
        public int SpiritCost { get; }
        public int CtPenalty { get; }
        public int ReuseCooldownTicks { get; }
        public string AbilityInstanceId { get; }
        public int CommittedUseCount { get; }
        public int RepeatMultiplierPermille { get; }
        public string RuleSetVersion { get; }
        public string RuleSetSourceDigest { get; }
        public string AbilityTierId { get; }
        public System.Collections.Generic.IReadOnlyList<HexCoord> MovementPath => Validation.MovementPath;
        public System.Collections.Generic.IReadOnlyList<SpatialMovementEdgeCost> MovementEdges => Validation.MovementEdges;
        public int UnitsPerRange => Validation.UnitsPerRange;
        public int MovementEfficiencyMilliUnits { get; }
        public bool RemainsInTurn { get; }
        public CombatTurnPreview Turn { get; }
    }

    /// <summary>One quote / preparation / synchronous commit gate; sub-actions never advance global time.</summary>
    public sealed class CombatCommandService
    {
        private readonly CombatActionResolver resolver;
        public CombatCommandService(CombatActionResolver resolver = null) => this.resolver = resolver ?? new CombatActionResolver();

        public CombatCommandPreview Preview(CombatSession session, CombatCommand command)
        {
            if (session == null || command == null) throw new ArgumentNullException(session == null ? nameof(session) : nameof(command));
            CombatActionResult validation = session.ValidateRules(command, out var attackArea);
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actor);
            int cost = 0, minimum = 0, spirit = 0, penalty = 0, cooldown = 0;
            CombatActionEconomyModifier modifier = null;
            if (command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine)
            {
                if (session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile))
                {
                    cost = profile.ActionPointCostUnits; minimum = profile.MinimumNetCostUnits;
                    spirit = profile.GetSpiritCost(command.ActorId == command.TargetId);
                    penalty = profile.CtPenalty; cooldown = profile.ReuseCooldownTicks;
                }
            }
            else if (command.Kind == CombatCommandKind.UseItem && session.TryGetOrdinary(command.Kind, out var itemDefinition))
            {
                cost = itemDefinition.ActionPointCostUnits; minimum = itemDefinition.MinimumNetCostUnits;
                penalty = itemDefinition.CtPenalty; cooldown = itemDefinition.ReuseCooldownTicks;
            }
            else if (command.Kind is CombatCommandKind.Guard or CombatCommandKind.SwapSpell ||
                command.Kind == CombatCommandKind.SwapEquipment && validation.Succeeded)
            {
                var definition = session.GetOrdinary(command.Kind);
                cost = definition.ActionPointCostUnits; minimum = definition.MinimumNetCostUnits;
                penalty = definition.CtPenalty; cooldown = definition.ReuseCooldownTicks;
            }
            else if (command.Kind == CombatCommandKind.Move && validation.Succeeded)
                cost = minimum = validation.MovementCost;
            if (actor != null && cost > 0 && command.Kind != CombatCommandKind.Move)
            {
                modifier = actor.ActionEconomyModifiers.SingleOrDefault(value => value.Matches(command.Kind, command.ProfileId));
                if (modifier != null)
                {
                    minimum = Math.Max(minimum, modifier.MinimumNetCostUnits);
                    long numerator = Math.Max(0L, cost - (long)modifier.FixedReductionUnits) * modifier.RetainedCostPermille;
                    cost = checked((int)Math.Max(minimum, (numerator + 999L) / 1000L));
                    penalty -= modifier.CtPenaltyReduction; cooldown -= modifier.ReuseCooldownReductionTicks;
                }
            }
            if (actor != null && command.Kind == CombatCommandKind.SwapSpell)
                cooldown = Math.Max(actor.GetCooldown(command.ProfileId), cooldown);
            int baseNetCost = cost, count = 0, multiplier = 1000;
            bool repeats = actor != null && cooldown == 0 &&
                command.Kind is (CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine) &&
                session.TryGetProfile(command.ProfileId, out _);
            if (repeats)
            {
                count = actor.GetCommittedUseCount(command.Kind, command.ProfileId);
                if (session.RuleSet == null)
                    validation = CombatActionResult.Rejected("combat_rule_set_missing");
                else
                {
                    var rules = session.RuleSet.Global;
                    multiplier = count switch
                    {
                        0 => rules.RepeatFirstPermille, 1 => rules.RepeatSecondPermille,
                        2 => rules.RepeatThirdPermille, _ => rules.RepeatCappedPermille,
                    };
                    try { cost = checked((int)(((long)baseNetCost * multiplier + 999L) / 1000L)); }
                    catch (OverflowException) { validation = CombatActionResult.Rejected("combat_effect_overflow"); }
                }
                if (count == int.MaxValue) validation = CombatActionResult.Rejected("combat_effect_overflow");
            }
            if (validation.Succeeded && actor.ActionPointsRemainingUnits < cost)
                validation = CombatActionResult.Rejected("action_points_insufficient");
            if (validation.Succeeded && session.StateVersion >= long.MaxValue - 2)
                validation = CombatActionResult.Rejected("combat_effect_overflow");
            if (validation.Succeeded && penalty > int.MaxValue - 100 - session.PendingCtPenalty)
                validation = CombatActionResult.Rejected("combat_ct_penalty_overflow");
            bool terminal = command.Kind is CombatCommandKind.Wait or CombatCommandKind.EndTurn;
            bool remains = validation.Succeeded && !terminal && actor.ActionPointsRemainingUnits - cost +
                actor.QuoteRefund(modifier, baseNetCost, minimum, actor.ActionPointsRemainingUnits - cost) > 0;
            CombatTurnPreview turn = validation.Succeeded ? session.TurnScheduler.PreviewTurnEnd(command.ActorId,
                session.PendingCtPenalty + penalty, command.Kind == CombatCommandKind.Wait, session.Combatants.All) : null;
            return new CombatCommandPreview(session, command, validation, cost, minimum, spirit, penalty, cooldown,
                modifier, Facts(session), remains, turn, baseNetCost, repeats, count, multiplier, attackArea);
        }

        /// <summary>Immediate authority requests form a fresh quote and commit it without yielding.</summary>
        public CombatActionResult Execute(CombatSession session, CombatCommand command) => Execute(session, command, Preview(session, command));

        /// <summary>Confirmation of an existing quote is exact; an old quote is never a wildcard.</summary>
        public CombatActionResult Execute(CombatSession session, CombatCommand command, CombatCommandPreview quote)
        {
            CombatActionResult result = Prepare(session, command, quote, out Action commit);
            if (result.Succeeded) commit();
            return result;
        }

        /// <summary>Prepare all combat effects before an owning bridge publishes its other prepared domain state.</summary>
        public CombatActionResult Prepare(CombatSession session, CombatCommand command, CombatCommandPreview quote, out Action apply)
        {
            apply = null;
            CombatActionResult confirmation = RevalidateQuote(session, command, quote, out CombatCommandPreview current);
            if (!confirmation.Succeeded) return confirmation;
            CombatActionResult result;
            Action commit;
            try { result = resolver.PrepareValidated(session, command, current.Validation, current.ReuseCooldownTicks,
                current.AttackArea, out commit); }
            catch (OverflowException) { return CombatActionResult.Rejected("combat_effect_overflow"); }
            if (!result.Succeeded) return result;
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actor);
            int quotedRefund = actor.QuoteRefund(current.Modifier, current.BaseNetCostUnits, current.MinimumNetCostUnits,
                actor.ActionPointsRemainingUnits - current.ActionPointCostUnits);
            apply = () =>
            {
                actor.SpendActionPoints(current.ActionPointCostUnits);
                commit();
                if (current.AppliesRepeatCost)
                    actor.RecordCommittedUse(command.Kind, command.ProfileId, current.CommittedUseCount + 1);
                actor.RefundActionPoints(current.Modifier, current.BaseNetCostUnits, current.MinimumNetCostUnits);
                if (command.Kind == CombatCommandKind.Wait)
                { session.TurnScheduler.Wait(actor.Id); session.FinishTurn(); }
                else if (command.Kind == CombatCommandKind.EndTurn) EndTurn(session, actor.Id);
                else
                {
                    session.RecordAction(current.CtPenalty);
                    if (session.IsFinished) { EndTurn(session, actor.Id); session.Terminate(); }
                    else if (actor.ActionPointsRemainingUnits == 0 || !actor.IsAlive) EndTurn(session, actor.Id);
                }
            };
            return result.WithPayment(current.ActionPointCostUnits, quotedRefund, current.StateVersion + 1);
        }

        /// <summary>Owning callers must pass this exact quote gate before sampling, then prepare synchronously.</summary>
        public CombatActionResult ValidateConfirmation(CombatSession session, CombatCommand command, CombatCommandPreview quote) =>
            RevalidateQuote(session, command, quote, out _);

        private CombatActionResult RevalidateQuote(CombatSession session, CombatCommand command, CombatCommandPreview quote,
            out CombatCommandPreview current)
        {
            current = null;
            if (session == null || command == null || quote == null) throw new ArgumentNullException(nameof(quote));
            if (quote.Session != session || quote.TurnSequence != session.TurnSequence || quote.StateVersion != session.StateVersion ||
                quote.ActorId != command.ActorId || quote.Kind != command.Kind || quote.TargetId != command.TargetId ||
                quote.ProfileId != command.ProfileId || quote.Destination != command.Destination || quote.SlotIndex != command.SlotIndex ||
                quote.AttackDirection != command.AttackDirection ||
                quote.ContextVersion != command.ContextVersion || quote.Facts != Facts(session) ||
                quote.Command.EquipmentSlotId != command.EquipmentSlotId || quote.Command.ItemId != command.ItemId ||
                quote.Command.InstanceId != command.InstanceId ||
                quote.Command.ItemQuantityCost != command.ItemQuantityCost ||
                (command.Kind == CombatCommandKind.UseItem && quote.ItemResource?.Facts != session.ExternalResources?.QueryItem(command)?.Facts) ||
                (command.Kind == CombatCommandKind.SwapEquipment && quote.EquipmentFacts != session.ExternalResources?.QueryEquipment(command)?.Facts))
                return CombatActionResult.Rejected("combat_quote_stale");
            if (!quote.IsLegal) return quote.Validation;
            current = Preview(session, command);
            if (!SameAttackArea(quote.AttackArea, current.AttackArea))
                return CombatActionResult.Rejected("combat_quote_stale");
            if (!current.IsLegal) return current.Validation;
            if (quote.ActionPointCostUnits != current.ActionPointCostUnits || quote.MinimumNetCostUnits != current.MinimumNetCostUnits ||
                quote.CommittedUseCount != current.CommittedUseCount || quote.RepeatMultiplierPermille != current.RepeatMultiplierPermille ||
                quote.AbilityInstanceId != current.AbilityInstanceId || quote.AbilityTierId != current.AbilityTierId ||
                quote.RuleSetVersion != current.RuleSetVersion || quote.RuleSetSourceDigest != current.RuleSetSourceDigest ||
                quote.SpiritCost != current.SpiritCost || quote.CtPenalty != current.CtPenalty || quote.ReuseCooldownTicks != current.ReuseCooldownTicks ||
                quote.UnitsPerRange != current.UnitsPerRange || !quote.MovementPath.SequenceEqual(current.MovementPath) ||
                !quote.MovementEdges.SequenceEqual(current.MovementEdges))
                return CombatActionResult.Rejected("combat_quote_stale");
            return current.Validation;
        }

        private static bool SameAttackArea(CombatAttackAreaQueryResult quoted, CombatAttackAreaQueryResult current)
        {
            if (quoted == null || current == null) return quoted == current;
            return quoted.ActorId == current.ActorId && quoted.Profile == current.Profile &&
                quoted.Origin == current.Origin && quoted.Direction == current.Direction &&
                quoted.TargetIds.SequenceEqual(current.TargetIds) && quoted.Cells.SequenceEqual(current.Cells);
        }

        public CombatActionResult Validate(CombatSession session, CombatCommand command) => Preview(session, command).Validation;

        public CombatTurnAdvance AdvanceUntilAction(CombatSession session) => AdvanceUntilAction(session, null);

        /// <summary>Records actual charge and same-tick settlement boundaries without running presentation callbacks.</summary>
        public CombatTurnAdvance AdvanceUntilAction(CombatSession session, out IReadOnlyList<CombatTurnSnapshot> steps)
        {
            var recorded = new List<CombatTurnSnapshot>();
            CombatTurnAdvance result = AdvanceUntilAction(session, recorded);
            steps = recorded.AsReadOnly();
            return result;
        }

        private CombatTurnAdvance AdvanceUntilAction(CombatSession session, List<CombatTurnSnapshot> steps)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            Record();
            int ticks = 0;
            while (true)
            {
                if (session.IsFinished) { session.Terminate(); return new CombatTurnAdvance(string.Empty, ticks); }
                if (session.TurnPhase != CombatTurnPhase.Finished)
                {
                    if (session.Combatants.TryGet(session.CurrentActorId, out var current) && current.IsAlive)
                        return new CombatTurnAdvance(current.Id, ticks);
                    EndTurn(session, session.CurrentActorId);
                    Record();
                }
                CombatTurnAdvance advance = session.TurnScheduler.AdvanceUntilAction(session.Combatants.All);
                Record();
                ticks = checked(ticks + advance.TicksElapsed);
                foreach (CombatantSnapshot combatant in session.Combatants.All)
                    if (combatant.IsAlive) combatant.AdvanceCooldowns(advance.TicksElapsed);
                if (!advance.HasActor) return new CombatTurnAdvance(string.Empty, ticks);
                session.Combatants.TryGet(advance.ActorId, out var actor);
                if (session.BeginTurn(actor)) return new CombatTurnAdvance(actor.Id, ticks);
                EndTurn(session, actor.Id);
                Record();
            }

            void Record() { if (steps != null) steps.Add(session.TurnScheduler.GetSnapshot(session.Combatants.All)); }
        }

        private static void EndTurn(CombatSession session, string actorId)
        {
            if (session.TurnPhase == CombatTurnPhase.Finished) return;
            session.TurnScheduler.ConsumeAction(actorId, session.PendingCtPenalty);
            session.FinishTurn();
        }

        // Snapshot setters are also used by deterministic fixtures and external state effects; quote them
        // as facts as well as checking the aggregate version, so a changed target/v/position cannot slip through.
        private static string Facts(CombatSession session) => string.Join(";", session.Combatants.All.Select(actor =>
            string.Join("|", actor.Id, actor.Position, actor.CurrentHealth, actor.MaximumSpirit, actor.CurrentSpirit,
                actor.ActionPointsRemainingUnits, actor.MovementEfficiencyMilliUnits, actor.IsGuarding, actor.Facing,
                actor.BasicAttackProfileId, actor.CombatSwapsUsed, actor.GongFaId, actor.GongFaElement,
                actor.CommittedBasicAttackUses,
                string.Join(",", actor.CommittedAbilityUses.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value)),
                actor.ShouyiStacks, actor.FudanStacks, actor.LeijieStacks, actor.BlockRate, actor.BlockReduction,
                actor.SoulShieldRate, actor.SoulShieldReduction, actor.DodgeRate, actor.CriticalRate, actor.CriticalDamage,
                actor.HitRateBonus, string.Join(",", actor.EquippedArtProfileIds), string.Join(",", actor.EquippedDivineProfileIds),
                string.Join(",", actor.Cooldowns.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + pair.Value)),
                string.Join(",", actor.Statuses.Select(status => status.Kind + ":" + status.RemainingOwnTurnEnds + ":" + status.DamagePerStart)))));
    }
}
