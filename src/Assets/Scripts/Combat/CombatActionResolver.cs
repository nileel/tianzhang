using System;
using TianZhang.Spatial;

namespace TianZhang.Combat
{
    /// <summary>Pure deterministic projection of the established single-target combat rules.</summary>
    public sealed class CombatActionResolver
    {
        private const float BaseHitRate = 100f;
        private const float BaseCriticalMultiplier = 1.5f;

        public CombatActionResult Resolve(CombatSession session, CombatCommand command)
        {
            if (session == null || command == null)
                throw new ArgumentNullException(session == null ? nameof(session) : nameof(command));
            return new CombatCommandService(this).Execute(session, command);
        }

        internal CombatActionResult PrepareValidated(
            CombatSession session,
            CombatCommand command,
            CombatActionResult validation,
            int reuseCooldownTicks,
            out Action commit)
        {
            commit = null;
            session.Combatants.TryGet(command.ActorId, out CombatantSnapshot originalActor);
            var actor = originalActor.CopyForResolution();
            session.Combatants.TryGet(command.TargetId, out CombatantSnapshot originalTarget);
            var target = originalTarget == originalActor ? actor : originalTarget?.CopyForResolution();
            if (command.Kind is not (CombatCommandKind.Guard or CombatCommandKind.Wait or CombatCommandKind.EndTurn))
                actor.IsGuarding = false;
            CombatActionResult result = command.Kind switch
            {
                CombatCommandKind.Guard => ResolveGuard(actor),
                CombatCommandKind.EndTurn => CombatActionResult.Success(),
                CombatCommandKind.Wait => CombatActionResult.Success(),
                CombatCommandKind.BasicAttack or CombatCommandKind.Art or CombatCommandKind.Divine =>
                    ResolveAttack(session, command, actor, target, reuseCooldownTicks),
                CombatCommandKind.Move => ResolveMove(actor, command, validation),
                CombatCommandKind.SwapSpell => ResolveSwapSpell(actor, command, reuseCooldownTicks),
                CombatCommandKind.SwapEquipment => ResolveEquipment(actor, command),
                CombatCommandKind.UseItem => ResolveItem(actor, session.ExternalResources.QueryItem(command).HealthRecovery),
                _ => CombatActionResult.Rejected("combat_command_kind_invalid"),
            };
            if (result.Succeeded) commit = () =>
            {
                originalActor.CommitResolvedState(actor);
                if (originalTarget != null && originalTarget != originalActor) originalTarget.CommitResolvedState(target);
            };
            return result;
        }

        private static CombatActionResult ResolveEquipment(CombatantSnapshot actor, CombatCommand command)
        {
            actor.BasicAttackProfileId = command.ProfileId;
            return CombatActionResult.Success();
        }

        private static CombatActionResult ResolveItem(CombatantSnapshot actor, int recovery)
        {
            actor.RestoreHealth(recovery);
            return CombatActionResult.Success();
        }

        private static CombatActionResult ResolveGuard(CombatantSnapshot actor)
        {
            actor.IsGuarding = true;
            return CombatActionResult.Success();
        }

        private static CombatActionResult ResolveMove(
            CombatantSnapshot actor,
            CombatCommand command,
            CombatActionResult validation)
        {
            actor.CommitMovement(command.Destination.Value, validation.MovementCost, validation.MovementFacing);
            return validation;
        }

        private static CombatActionResult ResolveSwapSpell(CombatantSnapshot actor, CombatCommand command, int cooldown)
        {
            actor.SwapEquippedArt(command.SlotIndex, command.ProfileId);
            actor.SetCooldown(command.ProfileId, Math.Max(actor.GetCooldown(command.ProfileId), cooldown));
            return CombatActionResult.Success();
        }

        private static CombatActionResult ResolveAttack(
            CombatSession session,
            CombatCommand command,
            CombatantSnapshot actor,
            CombatantSnapshot target,
            int cooldown)
        {
            session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile);

            bool control = profile.RuleMetadata != null &&
                (profile.RuleMetadata.ControlChancePermille > 0 || profile.RuleMetadata.ControlUsesMindContest);
            if (control && (!command.Rolls.ControlRoll.HasValue ||
                float.IsNaN(command.Rolls.ControlRoll.Value) || float.IsInfinity(command.Rolls.ControlRoll.Value) ||
                command.Rolls.ControlRoll.Value < 0 || command.Rolls.ControlRoll.Value > 100))
                return CombatActionResult.Rejected("combat_control_roll_invalid");

            bool targetsSelf = string.Equals(actor.Id, target.Id, StringComparison.Ordinal);
            bool kuxing = profile.RuleMetadata?.PrototypeDefinitionRef == "KuxingAction";
            int healthRecovery = 0;
            if (kuxing)
            {
                actor.TryGetRealmOrder(session.RuleSet, out int order);
                decimal rate = order switch { 3 => .20m, 2 => .15m, _ => .10m };
                int cost = Math.Min(actor.CurrentHealth - 1, (int)decimal.Floor(actor.CurrentHealth * rate));
                actor.ReceiveDamage(cost);
                actor.ApplyKuxingDefenseDebt(actor.GetAbilityInstanceId(profile.Id));
                if (order == 3) healthRecovery = checked((int)decimal.Floor(cost * profile.RuleMetadata.BaseEffectBudget / 10m));
            }
            int spiritCost = profile.GetSpiritCost(targetsSelf);
            if (!actor.TryConsumeSpirit(spiritCost))
                return CombatActionResult.Rejected("spirit_insufficient");
            CombatActionResult result;
            if (profile.Effect == CombatAttackEffect.RestoreSpirit)
            {
                int sourceSpirit = profile.RuleMetadata == null ? actor.RawSpirit :
                    Math.Min(actor.RawSpirit, profile.RuleMetadata.Tier.RawSpiritRecoveryCapacity);
                int requestedRecovery = checked((int)Math.Floor(Math.Max(profile.MinimumSpiritRecovery,
                    sourceSpirit * (double)profile.SpiritRecoveryMultiplier) *
                    session.SpiritRecoveryEnvironmentMultiplier *
                    (targetsSelf ? 1f : profile.OtherTargetSpiritRecoveryMultiplier)));
                int before = target.CurrentSpirit;
                target.RestoreSpirit(requestedRecovery);
                result = CombatActionResult.SpiritRecoverySuccess(spiritCost,
                    new CombatSpiritRecoveryResult(target.Id, requestedRecovery, target.CurrentSpirit - before));
            }
            else if (profile.Effect is CombatAttackEffect.Buff or CombatAttackEffect.Debuff)
            {
                target.ApplyAttributeStatus(profile.RuleMetadata, RealmCoefficient(session, actor, target),
                    session.RuleSet.Global.RelativeStatusLimitPermille, actor.GetAbilityInstanceId(profile.Id), targetsSelf);
                result = CombatActionResult.AttackSuccess(spiritCost, new CombatDamageResult(0, true, false, false, false));
            }
            else
            {
                bool lowHealthBonus = false;
                if (kuxing) { actor.TryGetRealmOrder(session.RuleSet, out int actorOrder); lowHealthBonus = actorOrder >= 2; }
                CombatDamageResult damage = ResolveEffect(actor, target, profile, command.Rolls, lowHealthBonus);
                if (profile.RuleMetadata?.PrototypeDefinitionRef == "WaterDivine")
                {
                    actor.TryGetRealmOrder(session.RuleSet, out int sourceOrder);
                    target.ApplyWaterPrototype(profile.RuleMetadata, session.RuleSet, RealmCoefficient(session, actor, target),
                        sourceOrder, actor.GetAbilityInstanceId(profile.Id), actor);
                }
                if (control && damage.IsHit && damage.FinalDamage > 0 && target.IsAlive &&
                    !target.HasStatus(CombatStatusKind.Stun) && !target.HasStatus(CombatStatusKind.Xuantong) &&
                    ControlSucceeds(session, actor, target, profile.RuleMetadata, command.Rolls.ControlRoll.Value))
                    target.ApplyStatus(CombatStatusKind.Stun, 1, sourceRef: actor.GetAbilityInstanceId(profile.Id));
                result = CombatActionResult.AttackSuccess(spiritCost, damage);
            }
            if (healthRecovery > 0 && actor.IsAlive) actor.RestoreHealth(healthRecovery);
            // 全局刻重用冷却账本只由 reuseCooldownTicks 启动；CT 惩罚由 CommandService 单独累计。
            if (cooldown > 0) actor.SetCooldown(profile.Id, cooldown);
            return result;
        }

        private static int RealmCoefficient(CombatSession session, CombatantSnapshot actor, CombatantSnapshot target)
        {
            actor.TryGetRealmOrder(session.RuleSet, out int sourceOrder);
            target.TryGetRealmOrder(session.RuleSet, out int targetOrder);
            var global = session.RuleSet.Global;
            return (sourceOrder - targetOrder) switch
            {
                <= -2 => global.RealmBelowTwoPermille, -1 => global.RealmBelowOnePermille,
                0 => global.RealmEqualPermille, 1 => global.RealmAboveOnePermille, _ => global.RealmAboveTwoPermille,
            };
        }

        private static bool ControlSucceeds(CombatSession session, CombatantSnapshot actor, CombatantSnapshot target,
            CombatAttackRuleMetadata payload, float roll)
        {
            var global = session.RuleSet.Global;
            long divisor = payload.ControlUsesMindContest ? global.WaterMindDifferenceDivisor : 1;
            long numerator = payload.ControlChancePermille;
            if (payload.ControlUsesMindContest)
            {
                long difference = (long)actor.RawMind + actor.MindStrengthBonus - target.RawMind - target.MindStrengthBonus;
                numerator = checked(global.WaterBaseChancePermille * divisor + difference * 1000);
                numerator = Math.Max(global.WaterMinimumChancePermille * divisor,
                    Math.Min(global.StrongControlLimitPermille * divisor, numerator));
            }
            long denominator = checked(1000000L * divisor);
            numerator = Math.Min(checked(numerator * RealmCoefficient(session, actor, target)),
                checked(global.StrongControlLimitPermille * divisor * 1000));
            // Compare the unrounded rational with the independent percentage sample; no post-C floor.
            return (decimal)roll * denominator < (decimal)numerator * 100;
        }

        private static CombatDamageResult ResolveEffect(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile,
            CombatResolutionRolls rolls, bool kuxingLowHealthBonus)
        {
            switch (profile.Effect)
            {
                case CombatAttackEffect.Heal:
                    target.RestoreHealth(profile.HealAmount);
                    return new CombatDamageResult(0, true, false, false, false);
                case CombatAttackEffect.Physical:
                    return ResolvePhysical(actor, target, profile, rolls, true, true, kuxingLowHealthBonus);
                case CombatAttackEffect.Soul:
                    return ResolveSoul(actor, target, profile, rolls, true, true);
                case CombatAttackEffect.Hybrid:
                    CombatDamageResult physical = ResolvePhysical(actor, target, profile, rolls, false, false, kuxingLowHealthBonus);
                    CombatDamageResult soul = ResolveSoul(actor, target, profile, rolls, false, false);
                    if (physical.IsHit)
                        target.ReceiveDamage(physical.FinalDamage);
                    if (soul.IsHit)
                        target.ReceiveDamage(soul.FinalDamage);
                    return new CombatDamageResult(
                        checked(physical.FinalDamage + soul.FinalDamage),
                        physical.IsHit || soul.IsHit,
                        physical.IsCritical || soul.IsCritical,
                        physical.IsBlocked,
                        soul.IsSoulShielded);
                default:
                    throw new InvalidOperationException("Validated combat profile has an unsupported effect.");
            }
        }

        private static CombatDamageResult ResolvePhysical(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile,
            CombatResolutionRolls rolls,
            bool advanceAction,
            bool applyDamage, bool kuxingLowHealthBonus = false)
        {
            if (!RollHit(actor, target, rolls.HitPercent))
                return new CombatDamageResult(0, false, false, false, false);

            ElementMatch element = GetElementMatch(profile.DamageElement, actor.GongFaElement, target.GongFaElement);
            bool critical = rolls.CriticalPercent < actor.CriticalRate + element.CriticalRateBonus;
            float multiplier = profile.PhysicalMultiplier * ConsumeLeijieForPhysicalAction(actor) *
                (critical ? CriticalMultiplier(actor.CriticalDamage, element.CriticalDamageBonus) : 1f);
            bool kuxing = profile.RuleMetadata?.PrototypeDefinitionRef == "KuxingAction";
            if (kuxingLowHealthBonus && target.CurrentHealth < target.MaximumHealth * .5)
                multiplier *= 1.3f;
            int effectiveDefense = (int)Math.Round(Math.Max(0, target.PhysicalDefense - target.ConsumeKuxingDefenseDebt()) * target.DefenseMultiplier(physical: true));
            effectiveDefense = (int)Math.Round(effectiveDefense * (1f - Clamp(profile.SoulDefensePenetration, 0f, 100f) / 100f));
            int attack = profile.RuleMetadata == null ? actor.PhysicalAttack :
                Math.Min(actor.PhysicalAttack, profile.RuleMetadata.EffectBudgetMode == "fixed_unarmed" ?
                    profile.RuleMetadata.UnarmedPhysicalAttackCapacity : profile.RuleMetadata.Tier.PhysicalAttackCapacity);
            float damage = CalculateLineDamage(attack, effectiveDefense, 0f, multiplier,
                actor.RealmMultiplier, target.RealmMultiplier, element.DamageMultiplier);
            damage *= FacingDamageModifier(target, actor);

            bool backAttack = IsBackAttack(target, actor);
            bool blocked = !kuxing && !backAttack && rolls.BlockPercent < target.BlockRate;
            if (blocked)
                damage *= BlockReductionMultiplier(target.BlockReduction);
            damage = ApplyCommonReductions(damage, target);

            int finalDamage = Math.Max(1, checked((int)Math.Round(damage, MidpointRounding.AwayFromZero)));
            if (applyDamage)
                target.ReceiveDamage(finalDamage);
            return new CombatDamageResult(finalDamage, true, critical, blocked, false);
        }

        private static CombatDamageResult ResolveSoul(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile,
            CombatResolutionRolls rolls,
            bool advanceAction,
            bool applyDamage)
        {
            if (!RollHit(actor, target, rolls.HitPercent))
                return new CombatDamageResult(0, false, false, false, false);

            ElementMatch element = GetElementMatch(profile.DamageElement, actor.GongFaElement, target.GongFaElement);
            bool critical = rolls.CriticalPercent < actor.CriticalRate + element.CriticalRateBonus;
            FudanBonus fudan = profile.Kind == CombatAttackKind.Art && profile.IsTalisman
                ? ConsumeFudanForSoulAction(actor) : new FudanBonus(1f, 0f);
            float shouyiMultiplier = actor.GongFaId == "抱元守一经" ? 1f + actor.ShouyiStacks * 0.05f : 1f;
            float multiplier = profile.SoulMultiplier * shouyiMultiplier * fudan.DamageMultiplier *
                (critical ? BaseCriticalMultiplier + (actor.CriticalDamage + element.CriticalDamageBonus) / 100f : 1f);
            float soulResist = target.GongFaId == CombatGongFaRules.ShouyiGongFaId &&
                target.ShouyiStacks == target.MaximumShouyiStacks ? 15f : 0f;
            float effectiveDefense = target.SoulDefense * target.DefenseMultiplier(physical: false);
            if (target.GongFaId == CombatGongFaRules.LeijieGongFaId &&
                target.LeijieStacks == target.MaximumLeijieStacks)
                effectiveDefense *= 0.8f;
            effectiveDefense *= 1f - Clamp(profile.SoulDefensePenetration + fudan.SoulDefensePenetration, 0f, 100f) / 100f;
            int attack = profile.RuleMetadata == null ? actor.SoulAttack + actor.MindStrengthBonus :
                (int)Math.Min((long)actor.SoulAttack + actor.MindStrengthBonus, profile.RuleMetadata.Tier.SoulAttackCapacity);
            float damage = CalculateLineDamage(attack,
                (int)Math.Round(effectiveDefense), soulResist, multiplier,
                actor.RealmMultiplier, target.RealmMultiplier, element.DamageMultiplier);

            bool backAttack = IsBackAttack(target, actor);
            bool soulShielded = !backAttack && rolls.SoulShieldPercent < target.SoulShieldRate;
            if (soulShielded)
                damage *= 1f - Clamp(target.SoulShieldReduction, 0f, 100f) / 100f;
            damage = ApplyCommonReductions(damage, target);

            int finalDamage = Math.Max(1, checked((int)Math.Round(damage, MidpointRounding.AwayFromZero)));
            if (applyDamage)
                target.ReceiveDamage(finalDamage);
            return new CombatDamageResult(finalDamage, true, critical, false, soulShielded);
        }

        public static float EffectiveHitRate(float hitRateBonus, float dodgeRate) =>
            Clamp(BaseHitRate + hitRateBonus - dodgeRate, 5f, 100f);

        public static float CriticalMultiplier(float criticalDamage, float criticalDamageBonus) =>
            BaseCriticalMultiplier + (criticalDamage + criticalDamageBonus) / 100f;

        public static float BlockReductionMultiplier(float blockReduction) =>
            1f - Clamp(blockReduction, 0f, 100f) / 100f;

        private static bool RollHit(CombatantSnapshot actor, CombatantSnapshot target, float rollPercent)
        {
            float rate = EffectiveHitRate(actor.HitRateBonus, target.DodgeRate);
            rate *= FacingHitModifier(target, actor);
            return rollPercent <= Clamp(rate, 5f, 100f);
        }

        private static float CalculateLineDamage(
            int attack,
            int defense,
            float resistPercent,
            float skillMultiplier,
            float attackerRealm,
            float defenderRealm,
            float elementMultiplier)
        {
            float safeAttack = Math.Max(1f, attack);
            float safeDefense = Math.Max(0f, defense);
            float attacker = Math.Max(1f, attackerRealm);
            float defender = Math.Max(1f, defenderRealm);
            float resistance = Clamp(1f - resistPercent / 100f * (float)Math.Sqrt(defender / attacker), 0f, 1f);
            return safeAttack * skillMultiplier * (attacker / defender) * (safeAttack / (safeAttack + safeDefense)) * resistance * elementMultiplier;
        }

        private static float ApplyCommonReductions(float damage, CombatantSnapshot target)
        {
            damage = target.ConsumeWaterReduction(damage);
            if (target.GongFaId == "抱元守一经" && target.ShouyiStacks > 0 && damage > 0f)
            {
                damage *= 0.8f;
                target.ShouyiStacks--;
            }
            return damage;
        }

        private static float ConsumeLeijieForPhysicalAction(CombatantSnapshot actor)
        {
            if (actor.GongFaId != CombatGongFaRules.LeijieGongFaId || actor.LeijieStacks <= 0)
                return 1f;
            float multiplier = 1f + actor.LeijieStacks * actor.LeijieDamageBonusPerStack;
            actor.LeijieStacks = 0;
            return multiplier;
        }

        private static FudanBonus ConsumeFudanForSoulAction(CombatantSnapshot actor)
        {
            if (actor.GongFaId != CombatGongFaRules.FudanGongFaId || actor.FudanStacks <= 0)
                return new FudanBonus(1f, 0f);
            bool wasFull = actor.FudanStacks == actor.MaximumFudanStacks;
            float rate = actor.RealmMultiplier >= 24f ? 0.22f :
                actor.RealmMultiplier >= 12f ? 0.18f :
                actor.RealmMultiplier >= 6f ? 0.15f :
                actor.RealmMultiplier >= 3f ? 0.12f : 0.15f;
            float multiplier = 1f + actor.FudanStacks * rate;
            actor.FudanStacks = actor.RealmMultiplier >= 24f ? 2 : 0;
            return new FudanBonus(multiplier, wasFull ? 30f : 0f);
        }

        private static float FacingHitModifier(CombatantSnapshot target, CombatantSnapshot actor)
        {
            int direction = target.Position.DirectionTo(actor.Position);
            if (direction < 0)
                return 1f;
            int difference = DirectionDifference(target.Facing, direction);
            return difference switch { 3 => 1.2f, 1 or 2 => 1.1f, _ => 1f };
        }

        private static float FacingDamageModifier(CombatantSnapshot target, CombatantSnapshot actor)
        {
            int direction = target.Position.DirectionTo(actor.Position);
            if (direction < 0)
                return 1f;
            int difference = DirectionDifference(target.Facing, direction);
            return difference switch { 3 => 1.3f, 1 or 2 => 1.15f, _ => 1f };
        }

        private static bool IsBackAttack(CombatantSnapshot target, CombatantSnapshot actor)
        {
            return FacingDamageModifier(target, actor) >= 1.25f;
        }

        private static int DirectionDifference(int left, int right)
        {
            int difference = Math.Abs((left % 6 + 6) % 6 - (right % 6 + 6) % 6);
            return difference > 3 ? 6 - difference : difference;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Min(maximum, Math.Max(minimum, value));
        }

        private readonly struct ElementMatch
        {
            public ElementMatch(float damageMultiplier, float criticalRateBonus, float criticalDamageBonus)
            {
                DamageMultiplier = damageMultiplier;
                CriticalRateBonus = criticalRateBonus;
                CriticalDamageBonus = criticalDamageBonus;
            }
            public float DamageMultiplier { get; }
            public float CriticalRateBonus { get; }
            public float CriticalDamageBonus { get; }
        }

        private readonly struct FudanBonus
        {
            public FudanBonus(float damageMultiplier, float soulDefensePenetration)
            {
                DamageMultiplier = damageMultiplier;
                SoulDefensePenetration = soulDefensePenetration;
            }
            public float DamageMultiplier { get; }
            public float SoulDefensePenetration { get; }
        }

        private static ElementMatch GetElementMatch(string actionElement, string attackerElement, string defenderElement)
        {
            string action = NormalizeElement(actionElement);
            if (string.IsNullOrEmpty(action) || action == "混沌")
                return new ElementMatch(1f, 0f, 0f);

            float damage = 1f;
            float criticalRate = 0f;
            float criticalDamage = 0f;
            string attacker = NormalizeElement(attackerElement);
            if (!string.IsNullOrEmpty(attacker) && attacker != "混沌" && ToBase(attacker) != ToBase(action))
            {
                if (Generates(ToBase(attacker), ToBase(action))) damage *= 1.1f;
                else if (Overcomes(ToBase(attacker), ToBase(action)))
                {
                    damage *= 0.9f;
                    criticalRate += 5f;
                    criticalDamage += 10f;
                }
            }

            string defender = NormalizeElement(defenderElement);
            if (!string.IsNullOrEmpty(defender) && defender != "混沌" && ToBase(action) != ToBase(defender))
            {
                bool variant = action is "风" or "雷" or "冰" or "暗" or "星";
                if (Overcomes(ToBase(action), ToBase(defender))) damage *= variant ? 1.15f : 1.1f;
                else if (Overcomes(ToBase(defender), ToBase(action))) damage *= variant ? 0.85f : 0.9f;
                else if (Generates(ToBase(action), ToBase(defender))) damage *= 0.95f;
                else if (Generates(ToBase(defender), ToBase(action))) damage *= 1.05f;
            }
            return new ElementMatch(damage, criticalRate, criticalDamage);
        }

        private static string NormalizeElement(string value)
        {
            return value?.Trim() switch
            {
                "金" or "木" or "水" or "火" or "土" or "风" or "雷" or "冰" or "暗" or "星" or "毒" or "混沌" => value.Trim(),
                "element_metal" or "element_metal_root" => "金", "element_wood" or "element_wood_root" => "木",
                "element_water" or "element_water_root" => "水", "element_fire" or "element_fire_root" => "火",
                "element_earth" or "element_earth_root" => "土", "element_wind" or "element_wind_root" => "风",
                "element_thunder" or "element_thunder_root" => "雷", "element_ice" or "element_ice_root" => "冰",
                "element_dark" or "element_dark_root" => "暗", "element_star" or "element_star_root" => "星",
                "element_poison" or "element_poison_root" => "毒", "element_chaos" or "element_chaos_root" => "混沌",
                _ => string.Empty,
            };
        }

        private static string ToBase(string element)
        {
            return element switch { "风" or "毒" => "木", "雷" => "金", "冰" => "水", "暗" => "土", "星" => "火", _ => element };
        }

        private static bool Generates(string source, string target)
        {
            return (source, target) is ("木", "火") or ("火", "土") or ("土", "金") or ("金", "水") or ("水", "木");
        }

        private static bool Overcomes(string source, string target)
        {
            return (source, target) is ("木", "土") or ("土", "水") or ("水", "火") or ("火", "金") or ("金", "木");
        }
    }
}
