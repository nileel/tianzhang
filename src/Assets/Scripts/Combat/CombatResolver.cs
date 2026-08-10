using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TianZhang.Core;
using TianZhang.Spatial;
using TianZhang.Entity;


namespace TianZhang.Combat
{
    /// <summary>
    /// 战斗行动解析器
    /// 统一处理：普通攻击 / 术法 / 神通 / 移动 / 防御 / 待机
    /// </summary>
    public class CombatResolver
    {
        public HexGrid Grid;
        public CTBEngine Engine;
        public SpatialQueryBoard SpatialBoard { get; set; }

        // 战斗日志
        public List<string> BattleLog = new List<string>();

        public enum ActionType
        {
            Move,
            BasicAttack,
            CastSpell,
            UseSkill,
            Guard,
            Wait,
        }

        public struct ActionResult
        {
            public bool Success;
            public DamageCalculator.DamageResult Damage;
            public string Message;
        }

        public readonly struct AreaTargetCandidate
        {
            public AreaTargetCandidate(
                int unitId,
                HexCoord position,
                bool isAlive,
                AttackAreaTargetFaction faction)
            {
                UnitId = unitId;
                Position = position;
                IsAlive = isAlive;
                Faction = faction;
            }

            public int UnitId { get; }
            public HexCoord Position { get; }
            public bool IsAlive { get; }
            public AttackAreaTargetFaction Faction { get; }
        }

        public sealed class AreaTargetingResult
        {
            public AreaTargetingResult(HexCoord? center, IReadOnlyList<int> hitUnitIds, string rejectionReason)
            {
                Center = center;
                HitUnitIds = hitUnitIds ?? System.Array.Empty<int>();
                RejectionReason = rejectionReason ?? string.Empty;
            }

            public HexCoord? Center { get; }
            public IReadOnlyList<int> HitUnitIds { get; }
            public string RejectionReason { get; }
        }

        private readonly struct FudanActionBonus
        {
            public readonly float DamageMultiplier;
            public readonly float MagicDefensePenetrationPercent;
            public readonly bool WasFull;

            public FudanActionBonus(float damageMultiplier, float magicDefensePenetrationPercent, bool wasFull)
            {
                DamageMultiplier = damageMultiplier;
                MagicDefensePenetrationPercent = magicDefensePenetrationPercent;
                WasFull = wasFull;
            }
        }

        /// <summary>移动角色</summary>
        public ActionResult Move(Character mover, List<HexCoord> path)
        {
            if (path == null || path.Count == 0)
                return new ActionResult { Success = false, Message = "无移动路径" };
            if (SpatialBoard == null)
                return new ActionResult { Success = false, Message = "空间查询未配置" };

            var occupied = Grid.GetOccupiedCoords()
                .Where(coord => coord != mover.Position)
                .ToArray();
            var legalPath = SpatialBoard.FindPath(
                mover.Position,
                path[path.Count - 1],
                mover.MovePoints,
                occupied);
            if (legalPath.Count == 0)
                return new ActionResult { Success = false, Message = "无移动路径" };

            HexCoord finalPos = legalPath[legalPath.Count - 1];

            if (Grid.IsOccupied(finalPos))
                return new ActionResult { Success = false, Message = "目标格被占据" };

            Grid.ClearOccupied(mover.Position);
            mover.Position = finalPos;
            Grid.SetOccupied(finalPos, mover.CTBUnit.Id);

            string msg = $"{mover.Name} 移动到 {finalPos}（{legalPath.Count}格）";
            BattleLog.Add(msg);
            return new ActionResult { Success = true, Message = msg };
        }

        /// <summary>基础攻击只消费已解析的统一攻击档案。</summary>
        public ActionResult BasicAttack(Character attacker, Character defender, AttackProfileData profile)
        {
            return ExecuteSingleTargetProfile(attacker, defender, profile, AttackProfileKind.Basic, null, -1, "攻击");
        }

        public ActionResult CastSpell(Character caster, Character target, int spellIndex, AttackProfileData profile)
        {
            return ExecuteSingleTargetProfile(caster, target, profile, AttackProfileKind.Art,
                caster.SpellCooldowns, spellIndex, "施放");
        }

        public ActionResult UseSkill(Character caster, Character target, int skillIndex, AttackProfileData profile)
        {
            return ExecuteSingleTargetProfile(caster, target, profile, AttackProfileKind.Divine,
                caster.SkillCooldowns, skillIndex, "神通");
        }

        public AreaTargetingResult ResolveAreaTargets(
            AttackProfileData profile,
            Character caster,
            HexCoord? requestedTargetCell,
            IReadOnlyList<AreaTargetCandidate> candidates)
        {
            if (profile == null || !profile.TryValidate(out _) || profile.targetingMode != AttackTargetingMode.Area)
                return new AreaTargetingResult(null, null, "attack_profile_area_unresolved");
            if (SpatialBoard == null || caster == null || candidates == null)
                return new AreaTargetingResult(null, null, SpatialQueryReasons.QueryBoardNotConfigured);

            HexCoord center;
            if (profile.areaCenterKind == AttackAreaCenterKind.Caster)
            {
                center = caster.Position;
            }
            else if (requestedTargetCell.HasValue)
            {
                center = requestedTargetCell.Value;
            }
            else
            {
                return new AreaTargetingResult(null, null, "target_cell_invalid_or_out_of_bounds");
            }

            var spatialCenter = center;
            if (!SpatialBoard.Cells.Contains(spatialCenter))
                return new AreaTargetingResult(null, null, "target_cell_invalid_or_out_of_bounds");

            var source = caster.Position;
            var castDistance = SpatialBoard.QueryRangeEntry(
                source,
                spatialCenter,
                profile.minCastRange,
                profile.maxCastRange,
                SpatialQueryKind.Area,
                requireLineOfSight: false,
                activeEffectBlockers: 0);
            if (!castDistance.IsInRange)
                return new AreaTargetingResult(center, null, "cast_distance_out_of_range");

            var castPropagation = SpatialBoard.QueryRangeEntry(
                source,
                spatialCenter,
                profile.minCastRange,
                profile.maxCastRange,
                SpatialQueryKind.Area,
                requireLineOfSight: false,
                activeEffectBlockers: (ulong)profile.areaEffectBlockers);
            if (!castPropagation.IsInRange)
                return new AreaTargetingResult(center, null, "declared_effect_blocker");

            var hits = new List<int>();
            bool propagationBlocked = false;
            bool stateRejected = false;
            bool factionRejected = false;
            foreach (var candidate in candidates)
            {
                if (!IsWithinAreaShape(center, candidate.Position, profile))
                    continue;

                var propagation = SpatialBoard.QueryMetricDistance(
                    spatialCenter,
                    candidate.Position,
                    SpatialQueryKind.Area,
                    activeEffectBlockers: (ulong)profile.areaEffectBlockers,
                    canTraverse: coord => IsWithinAreaEnvelope(center, coord, profile));
                if (!propagation.IsReachable)
                {
                    propagationBlocked = true;
                    continue;
                }

                var requiredState = candidate.IsAlive
                    ? AttackAreaTargetState.Alive
                    : AttackAreaTargetState.Corpse;
                if ((profile.areaAllowedStates & requiredState) == 0)
                {
                    stateRejected = true;
                    continue;
                }
                if ((profile.areaAllowedFactions & candidate.Faction) == 0)
                {
                    factionRejected = true;
                    continue;
                }
                hits.Add(candidate.UnitId);
            }

            if (hits.Count > 0)
                return new AreaTargetingResult(center, hits, string.Empty);
            if (propagationBlocked)
                return new AreaTargetingResult(center, null, "declared_effect_blocker");
            if (stateRejected)
                return new AreaTargetingResult(center, null, "target_state_or_corpse_ineligible");
            if (factionRejected)
                return new AreaTargetingResult(center, null, "target_faction_ineligible");
            return new AreaTargetingResult(center, null, "no_legal_target");
        }

        private ActionResult ExecuteSingleTargetProfile(
            Character caster,
            Character target,
            AttackProfileData profile,
            AttackProfileKind expectedKind,
            int[] cooldowns,
            int cooldownIndex,
            string actionVerb)
        {
            if (caster == null || target == null || profile == null ||
                !profile.TryValidate(out _) || profile.profileKind != expectedKind ||
                profile.targetingMode != AttackTargetingMode.Single)
            {
                return Failure("attack_profile_unresolved");
            }
            if (!CanResolveSingleTargetEffect(profile))
                return Failure("attack_profile_effect_unresolved");
            if (cooldowns != null && (cooldownIndex < 0 || cooldownIndex >= cooldowns.Length))
                return Failure("attack_profile_cooldown_slot_invalid");
            if (cooldowns != null && cooldowns[cooldownIndex] > 0)
                return Failure($"{profile.displayNameKey} 冷却中");
            if (!CanTarget(caster.Position, target.Position, profile.minCastRange, profile.maxCastRange, out _))
                return Failure("目标不在射程范围");
            if (profile.resourceKind == AttackResourceKind.Mp && !caster.ConsumeMP(profile.resourceCost))
                return Failure("灵力不足");

            caster.FaceTarget(target.Position);
            var result = ApplyProfileEffect(caster, target, profile, actionVerb);
            if (!result.Success)
                return result;

            if (cooldowns != null)
                cooldowns[cooldownIndex] = profile.cooldownTicks;
            if (profile.cooldownTicks > 0)
                Engine.ApplySpellCooldown(caster.CTBUnit, profile.cooldownTicks);
            return result;
        }

        private ActionResult ApplyProfileEffect(Character caster, Character target, AttackProfileData profile, string actionVerb)
        {
            DamageCalculator.DamageResult damage;
            switch (profile.effectType)
            {
                case AttackEffectType.Physical:
                    damage = DamageCalculator.CalcPhysical(
                        caster.PhysAtk,
                        profile.physicalDamageMultiplier * ConsumeLeijieForPhysicalAction(caster),
                        caster,
                        target,
                        skillElement: profile.damageElementId);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
                    break;
                case AttackEffectType.Magic:
                    damage = CalculateMagicProfileDamage(caster, target, profile);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
                    break;
                case AttackEffectType.Hybrid:
                    var physicalDamage = DamageCalculator.CalcPhysical(
                        caster.PhysAtk,
                        profile.physicalDamageMultiplier * ConsumeLeijieForPhysicalAction(caster),
                        caster,
                        target,
                        skillElement: profile.damageElementId);
                    var soulDamage = CalculateMagicProfileDamage(caster, target, profile);
                    if (physicalDamage.IsHit) target.TakeDamage(physicalDamage.FinalDamage);
                    if (soulDamage.IsHit) target.TakeDamage(soulDamage.FinalDamage);
                    damage = new DamageCalculator.DamageResult
                    {
                        FinalDamage = physicalDamage.FinalDamage + soulDamage.FinalDamage,
                        IsHit = physicalDamage.IsHit || soulDamage.IsHit,
                        Log = $"{physicalDamage.Log}; {soulDamage.Log}",
                    };
                    break;
                case AttackEffectType.Heal:
                    target.Heal(profile.healAmount);
                    damage = default;
                    damage.Log = $"恢复{profile.healAmount}HP";
                    break;
                default:
                    return Failure("attack_profile_effect_unresolved");
            }

            AdvanceGongFaActionState(caster);
            string log = $"{caster.Name} {actionVerb} {profile.displayNameKey} → {target.Name}: {damage.Log}";
            BattleLog.Add(log);
            Debug.Log(log);
            return new ActionResult { Success = true, Damage = damage, Message = log };
        }

        private static bool CanResolveSingleTargetEffect(AttackProfileData profile)
        {
            return profile.effectType is AttackEffectType.Physical or AttackEffectType.Magic or
                AttackEffectType.Hybrid or AttackEffectType.Heal;
        }

        private static DamageCalculator.DamageResult CalculateMagicProfileDamage(
            Character caster,
            Character target,
            AttackProfileData profile)
        {
            float shouyiMultiplier = caster.GongFaName == "抱元守一经"
                ? 1f + caster.ShouyiStacks * 0.05f
                : 1f;
            FudanActionBonus fudanBonus = ConsumeFudanForMagicAction(caster);
            return DamageCalculator.CalcMagic(
                caster.MagAtk,
                profile.soulDamageMultiplier * shouyiMultiplier * fudanBonus.DamageMultiplier,
                caster,
                target,
                profile.damageElementId,
                fudanBonus.WasFull,
                magicDefensePenetrationPercent: profile.defensePenetration + fudanBonus.MagicDefensePenetrationPercent);
        }

        private static void AdvanceGongFaActionState(Character caster)
        {
            if (caster.GongFaName == "抱元守一经")
                caster.ShouyiStacks = Mathf.Min(caster.ShouyiStacks + 1, caster.MaxShouyi());
            if (caster.GongFaName == "云篆度人经")
                caster.FudanStacks = Mathf.Min(caster.FudanStacks + 1, caster.MaxFudan());
        }

        private static bool IsWithinAreaEnvelope(HexCoord center, HexCoord target, AttackProfileData profile)
        {
            int originalInnerRadius = profile.areaInnerRadius;
            return IsWithinAreaShape(center, target, profile, originalInnerRadius: 0);
        }

        private static bool IsWithinAreaShape(HexCoord center, HexCoord target, AttackProfileData profile, int? originalInnerRadius = null)
        {
            int distance = center.Distance(target);
            int innerRadius = originalInnerRadius ?? profile.areaInnerRadius;
            if (innerRadius > 0 && distance <= innerRadius)
                return false;
            return profile.areaShapeKind switch
            {
                AttackAreaShapeKind.Circle => distance <= profile.areaRadius,
                AttackAreaShapeKind.Line => IsOnLine(center, target, profile.areaFacing, profile.areaLength),
                AttackAreaShapeKind.Fan => IsInFan(
                    center,
                    target,
                    profile.areaFacing,
                    profile.areaLength,
                    profile.areaFanHalfAngleSteps),
                _ => false,
            };
        }

        private static bool IsOnLine(HexCoord center, HexCoord target, int facing, int length)
        {
            var current = center;
            for (int step = 1; step <= length; step++)
            {
                current = current.Neighbor(facing);
                if (current == target)
                    return true;
            }
            return false;
        }

        private static bool IsInFan(HexCoord center, HexCoord target, int facing, int length, int halfAngleSteps)
        {
            if (center.Distance(target) > length)
                return false;
            if (target == center)
                return true;

            var offset = new HexCoord(target.q - center.q, target.r - center.r);
            for (int step = 0; step < facing; step++)
                offset = new HexCoord(-offset.r, offset.q + offset.r);
            return halfAngleSteps == 0
                ? offset.r == 0 && offset.q > 0
                : offset.q >= 0 && offset.q + offset.r >= 0;
        }

        private static ActionResult Failure(string message) =>
            new ActionResult { Success = false, Message = message };

        private static FudanActionBonus ConsumeFudanForMagicAction(Character character)
        {
            if (character.GongFaName != "云篆度人经" || character.FudanStacks <= 0)
                return new FudanActionBonus(1f, 0f, false);

            bool wasFull = character.FudanStacks == character.MaxFudan();
            float realmMult = Cultivation.CultivationEngine.GetRealmMultiplier(character.GetRealm());
            float rate = realmMult >= 24f ? 0.22f :
                         realmMult >= 12f ? 0.18f :
                         realmMult >= 6f ? 0.15f :
                         realmMult >= 3f ? 0.12f :
                         0.15f;
            float damageMultiplier = 1f + character.FudanStacks * rate;
            character.FudanStacks = realmMult >= 24f ? 2 : 0;

            return new FudanActionBonus(damageMultiplier, wasFull ? 30f : 0f, wasFull);
        }

        private static float ConsumeLeijieForPhysicalAction(Character character)
        {
            if (character.GongFaName != "九霄雷劫录" || character.LeijieStacks <= 0)
                return 1f;

            float damageMultiplier = 1f + character.LeijieStacks * character.LeijieDamageBonusPerStack();
            character.LeijieStacks = 0;
            return damageMultiplier;
        }

        /// <summary>防御姿态</summary>
        public ActionResult Guard(Character character)
        {
            character.IsGuarding = true;
            string msg = $"{character.Name} 进入防御姿态";
            BattleLog.Add(msg);
            return new ActionResult { Success = true, Message = msg };
        }

        /// <summary>待机</summary>
        public ActionResult Wait(Character character)
        {
            character.IsGuarding = false;
            Engine.WaitAction(character.CTBUnit);
            string msg = $"{character.Name} 待机（保留50%CT）";
            BattleLog.Add(msg);
            return new ActionResult { Success = true, Message = msg };
        }

        /// <summary>推进所有术法/神通冷却</summary>
        public void AdvanceCooldowns(Character character, int ticks)
        {
            for (int i = 0; i < character.SpellCooldowns.Length; i++)
                character.SpellCooldowns[i] = Mathf.Max(0, character.SpellCooldowns[i] - ticks);

            for (int i = 0; i < character.SkillCooldowns.Length; i++)
                character.SkillCooldowns[i] = Mathf.Max(0, character.SkillCooldowns[i] - ticks);
        }

        public bool CanTarget(
            HexCoord source,
            HexCoord target,
            int minRange,
            int maxRange,
            out string reason)
        {
            if (SpatialBoard == null)
            {
                reason = SpatialQueryReasons.QueryBoardNotConfigured;
                return false;
            }

            var result = SpatialBoard.QueryRangeEntry(
                source,
                target,
                minRange,
                maxRange,
                SpatialQueryKind.Attack,
                requireLineOfSight: true);
            reason = result.Reason;
            return result.IsInRange;
        }

        public List<HexCoord> FindPathTowardTarget(Character mover, Character target)
        {
            if (mover == null) throw new System.ArgumentNullException(nameof(mover));
            if (target == null) throw new System.ArgumentNullException(nameof(target));
            if (SpatialBoard == null)
                return new List<HexCoord>();

            var start = mover.Position;
            var targetCoord = target.Position;
            var occupied = Grid.GetOccupiedCoords()
                .Where(coord => coord != mover.Position)
                .ToArray();
            var destination = SpatialBoard
                .FindReachable(start, mover.MovePoints, occupied)
                .Select(entry => new
                {
                    Coord = entry.Key,
                    MovementCost = entry.Value,
                    Range = SpatialBoard.QueryRangeEntry(
                        entry.Key,
                        targetCoord,
                        1,
                        1,
                        SpatialQueryKind.Attack,
                        requireLineOfSight: true),
                    Distance = SpatialBoard.QueryMetricDistance(
                        entry.Key,
                        targetCoord,
                        SpatialQueryKind.Attack),
                })
                .OrderBy(candidate => candidate.Range.IsInRange ? 0 : 1)
                .ThenBy(candidate => candidate.Distance.IsReachable
                    ? candidate.Distance.DistanceUnits
                    : int.MaxValue)
                .ThenBy(candidate => candidate.MovementCost)
                .ThenBy(candidate => candidate.Coord.Q)
                .ThenBy(candidate => candidate.Coord.R)
                .FirstOrDefault();
            if (destination == null || destination.Coord == start)
                return new List<HexCoord>();

            return SpatialBoard
                .FindPath(start, destination.Coord, mover.MovePoints, occupied)
                .ToList();
        }

        public void ClearLog() => BattleLog.Clear();
    }
}
