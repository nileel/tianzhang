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
            CombatActionResult validation = session.ValidateCommand(command);
            return !validation.Succeeded
                ? validation
                : ResolveValidated(session, command, validation);
        }

        internal CombatActionResult ResolveValidated(
            CombatSession session,
            CombatCommand command,
            CombatActionResult validation)
        {
            if (!session.Combatants.TryGet(command.ActorId, out CombatantSnapshot actor))
                return CombatActionResult.Rejected("combat_session_actor_invalid");
            return command.Kind switch
            {
                CombatCommandKind.Guard => ResolveGuard(actor),
                CombatCommandKind.Wait => CombatActionResult.Success(),
                CombatCommandKind.BasicAttack => ResolveAttack(session, command, actor, CombatAttackKind.Basic),
                CombatCommandKind.Art => ResolveAttack(session, command, actor, CombatAttackKind.Art),
                CombatCommandKind.Divine => ResolveAttack(session, command, actor, CombatAttackKind.Divine),
                CombatCommandKind.Move => ResolveMove(actor, command, validation),
                CombatCommandKind.SwapSpell => ResolveSwapSpell(actor, command),
                _ => CombatActionResult.Rejected("combat_command_kind_invalid"),
            };
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
            actor.SetPosition(command.Destination.Value);
            return CombatActionResult.MovementSuccess(validation.MovementPath, validation.MovementCost);
        }

        private static CombatActionResult ResolveSwapSpell(CombatantSnapshot actor, CombatCommand command)
        {
            actor.SwapEquippedArt(command.SlotIndex, command.ProfileId);
            actor.SetCooldown(command.ProfileId, 60);
            return CombatActionResult.Success();
        }

        private static CombatActionResult ResolveAttack(
            CombatSession session,
            CombatCommand command,
            CombatantSnapshot actor,
            CombatAttackKind expectedKind)
        {
            if (!session.Combatants.TryGet(command.TargetId, out CombatantSnapshot target) ||
                !session.TryGetProfile(command.ProfileId, out CombatAttackProfile profile) ||
                profile.Kind != expectedKind)
                return CombatActionResult.Rejected("combat_command_validation_changed");

            CombatDamageResult damage = ResolveEffect(actor, target, profile, command.Rolls);
            if (profile.SpiritCost > 0)
                actor.TryConsumeSpirit(profile.SpiritCost);
            if (profile.CooldownTicks > 0)
                actor.SetCooldown(profile.Id, profile.CooldownTicks);
            return CombatActionResult.Success(damage);
        }

        private static CombatDamageResult ResolveEffect(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile,
            CombatResolutionRolls rolls)
        {
            switch (profile.Effect)
            {
                case CombatAttackEffect.Heal:
                    target.RestoreHealth(profile.HealAmount);
                    return new CombatDamageResult(0, true, false, false, false);
                case CombatAttackEffect.Physical:
                    return ResolvePhysical(actor, target, profile, rolls, true);
                case CombatAttackEffect.Soul:
                    return ResolveSoul(actor, target, profile, rolls, true);
                case CombatAttackEffect.Hybrid:
                    CombatDamageResult physical = ResolvePhysical(actor, target, profile, rolls, false);
                    CombatDamageResult soul = ResolveSoul(actor, target, profile, rolls, false);
                    AdvanceActionState(actor);
                    return new CombatDamageResult(
                        physical.FinalDamage + soul.FinalDamage,
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
            bool advanceAction)
        {
            if (!RollHit(actor, target, rolls.HitPercent))
                return new CombatDamageResult(0, false, false, false, false);

            ElementMatch element = GetElementMatch(profile.DamageElement, actor.GongFaElement, target.GongFaElement);
            bool critical = rolls.CriticalPercent < actor.CriticalRate + element.CriticalRateBonus;
            float multiplier = profile.PhysicalMultiplier * ConsumeLeijieForPhysicalAction(actor) *
                (critical ? BaseCriticalMultiplier + (actor.CriticalDamage + element.CriticalDamageBonus) / 100f : 1f);
            float damage = CalculateLineDamage(actor.PhysicalAttack, target.PhysicalDefense, 0f, multiplier,
                actor.RealmMultiplier, target.RealmMultiplier, element.DamageMultiplier);
            damage *= FacingDamageModifier(target, actor);

            bool backAttack = IsBackAttack(target, actor);
            bool blocked = !backAttack && rolls.BlockPercent < target.BlockRate;
            if (blocked)
                damage *= 1f - Clamp(target.BlockReduction, 0f, 100f) / 100f;
            damage = ApplyCommonReductions(damage, target);

            int finalDamage = Math.Max(1, (int)Math.Round(damage, MidpointRounding.AwayFromZero));
            target.ReceiveDamage(finalDamage);
            if (advanceAction)
                AdvanceActionState(actor);
            return new CombatDamageResult(finalDamage, true, critical, blocked, false);
        }

        private static CombatDamageResult ResolveSoul(
            CombatantSnapshot actor,
            CombatantSnapshot target,
            CombatAttackProfile profile,
            CombatResolutionRolls rolls,
            bool advanceAction)
        {
            if (!RollHit(actor, target, rolls.HitPercent))
                return new CombatDamageResult(0, false, false, false, false);

            ElementMatch element = GetElementMatch(profile.DamageElement, actor.GongFaElement, target.GongFaElement);
            bool critical = rolls.CriticalPercent < actor.CriticalRate + element.CriticalRateBonus;
            FudanBonus fudan = ConsumeFudanForSoulAction(actor);
            float shouyiMultiplier = actor.GongFaId == "抱元守一经" ? 1f + actor.ShouyiStacks * 0.05f : 1f;
            float multiplier = profile.SoulMultiplier * shouyiMultiplier * fudan.DamageMultiplier *
                (critical ? BaseCriticalMultiplier + (actor.CriticalDamage + element.CriticalDamageBonus) / 100f : 1f);
            float soulResist = target.GongFaId == "抱元守一经" && target.ShouyiStacks >= 2 ? 15f : 0f;
            float effectiveDefense = target.SoulDefense;
            if (target.GongFaId == "九霄雷劫录" && target.LeijieStacks >= 2)
                effectiveDefense *= 0.8f;
            effectiveDefense *= 1f - Clamp(profile.SoulDefensePenetration + fudan.SoulDefensePenetration, 0f, 100f) / 100f;
            float damage = CalculateLineDamage(actor.SoulAttack, (int)Math.Round(effectiveDefense), soulResist, multiplier,
                actor.RealmMultiplier, target.RealmMultiplier, element.DamageMultiplier);

            bool backAttack = IsBackAttack(target, actor);
            bool soulShielded = !backAttack && rolls.SoulShieldPercent < target.SoulShieldRate;
            if (soulShielded)
                damage *= 1f - Clamp(target.SoulShieldReduction, 0f, 100f) / 100f;
            damage = ApplyCommonReductions(damage, target);

            int finalDamage = Math.Max(1, (int)Math.Round(damage, MidpointRounding.AwayFromZero));
            target.ReceiveDamage(finalDamage);
            if (advanceAction)
                AdvanceActionState(actor);
            return new CombatDamageResult(finalDamage, true, critical, false, soulShielded);
        }

        private static bool RollHit(CombatantSnapshot actor, CombatantSnapshot target, float rollPercent)
        {
            float rate = Clamp(BaseHitRate + actor.HitRateBonus - target.DodgeRate, 5f, 100f);
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
            if (target.IsGuarding)
                damage *= 0.5f;
            if (target.GongFaId == "抱元守一经" && target.ShouyiStacks > 0 && damage > 0f)
            {
                damage *= 0.8f;
                target.ShouyiStacks--;
            }
            return damage;
        }

        private static void AdvanceActionState(CombatantSnapshot actor)
        {
            if (actor.GongFaId == "抱元守一经")
                actor.ShouyiStacks = Math.Min(actor.ShouyiStacks + 1, 2);
            if (actor.GongFaId == "云篆度人经")
                actor.FudanStacks = Math.Min(actor.FudanStacks + 1, 2);
        }

        private static float ConsumeLeijieForPhysicalAction(CombatantSnapshot actor)
        {
            if (actor.GongFaId != "九霄雷劫录" || actor.LeijieStacks <= 0)
                return 1f;
            float multiplier = 1f + actor.LeijieStacks * 0.05f;
            actor.LeijieStacks = 0;
            return multiplier;
        }

        private static FudanBonus ConsumeFudanForSoulAction(CombatantSnapshot actor)
        {
            if (actor.GongFaId != "云篆度人经" || actor.FudanStacks <= 0)
                return new FudanBonus(1f, 0f);
            bool wasFull = actor.FudanStacks >= 2;
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
