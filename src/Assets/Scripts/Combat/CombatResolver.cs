using System.Collections.Generic;
using UnityEngine;
using TianZhang.Core;
using TianZhang.Core.SpatialRules;
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
        private SpatialQueryBoard spatialBoard;
        private readonly HashSet<SpatialHexCoord> occupied = new HashSet<SpatialHexCoord>();

        public SpatialQueryBoard SpatialBoard => spatialBoard;

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
            public string Reason;
        }

        public void ConfigureSpatialQueries(
            SpatialQueryBoard board,
            IEnumerable<HexCoord> occupiedCoords = null)
        {
            spatialBoard = board ?? throw new System.ArgumentNullException(nameof(board));
            SetOccupiedAnchors(occupiedCoords);
        }

        public void SetOccupiedAnchors(IEnumerable<HexCoord> occupiedCoords)
        {
            occupied.Clear();
            if (occupiedCoords == null)
                return;

            foreach (var coord in occupiedCoords)
                occupied.Add(ToSpatial(coord));
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
            if (spatialBoard == null)
                return RangeFailure("无移动路径", SpatialQueryReasons.QueryBoardNotConfigured);

            HexCoord finalPos = path[path.Count - 1];
            var reachable = FindReachable(mover);
            if (!reachable.ContainsKey(finalPos))
                return new ActionResult
                {
                    Success = false,
                    Message = "目标格不可达",
                    Reason = SpatialQueryReasons.NoLegalPath,
                };

            Grid?.ClearOccupied(mover.Position);
            occupied.Remove(ToSpatial(mover.Position));
            mover.Position = finalPos;
            occupied.Add(ToSpatial(finalPos));
            Grid?.SetOccupied(finalPos, mover.CTBUnit.Id);

            string msg = $"{mover.Name} 移动到 {finalPos}";
            BattleLog.Add(msg);
            return new ActionResult { Success = true, Message = msg };
        }

        /// <summary>普通攻击</summary>
        public ActionResult BasicAttack(Character attacker, Character defender,
            bool useMagic = false)
        {
            if (!CanTarget(attacker.Position, defender.Position, 1, 1, out string rangeReason))
                return RangeFailure("目标不在近战范围", rangeReason);

            // 朝向目标
            attacker.FaceTarget(defender.Position);

            DamageCalculator.DamageResult damage;
            float mult = 1f;
            if (useMagic && attacker.GongFaName == "抱元守一经")
                mult = 1f + attacker.ShouyiStacks * 0.05f;

            FudanActionBonus fudanBonus = new FudanActionBonus(1f, 0f, false);
            if (useMagic)
            {
                fudanBonus = ConsumeFudanForMagicAction(attacker);
                mult *= fudanBonus.DamageMultiplier;
            }

            if (useMagic)
                damage = DamageCalculator.CalcMagic(
                    attacker.MagAtk,
                    mult,
                    attacker,
                    defender,
                    cannotDodge: fudanBonus.WasFull,
                    magicDefensePenetrationPercent: fudanBonus.MagicDefensePenetrationPercent);
            else
                damage = DamageCalculator.CalcPhysical(
                    attacker.PhysAtk,
                    mult * ConsumeLeijieForPhysicalAction(attacker),
                    attacker,
                    defender);

            if (damage.IsHit)
                defender.TakeDamage(damage.FinalDamage);

            // 守一：出手后+1层
            if (attacker.GongFaName == "抱元守一经")
                attacker.ShouyiStacks = Mathf.Min(attacker.ShouyiStacks + 1, attacker.MaxShouyi());
            // 符胆：出手后+1层
            if (attacker.GongFaName == "云篆度人经")
                attacker.FudanStacks = Mathf.Min(attacker.FudanStacks + 1, attacker.MaxFudan());

            string log = $"{attacker.Name} {(useMagic ? "神魂" : "物理")}攻击 {defender.Name}: {damage.Log}";
            BattleLog.Add(log);
            Debug.Log(log);

            return new ActionResult { Success = true, Damage = damage, Message = log };
        }

        /// <summary>施放术法</summary>
        public ActionResult CastSpell(Character caster, Character target,
            int spellIndex, SpellData spell)
        {
            // 冷却检查
            if (caster.SpellCooldowns[spellIndex] > 0)
                return new ActionResult { Success = false, Message = $"{spell.spellName} 冷却中" };

            if (!CanTarget(
                caster.Position,
                target.Position,
                spell.minRange,
                spell.maxRange,
                out string rangeReason))
                return RangeFailure("目标不在射程范围", rangeReason);

            // MP检查（符胆满层：零耗MP）
            bool fudanMax = caster.GongFaName == "云篆度人经" && caster.FudanStacks == caster.MaxFudan();
            int effectiveMpCost = fudanMax ? 0 : spell.mpCost;
            if (!caster.ConsumeMP(effectiveMpCost))
                return new ActionResult { Success = false, Message = "灵力不足" };

            // 设置冷却
            caster.SpellCooldowns[spellIndex] = spell.cooldownTicks;

            // CT冷却惩罚
            Engine.ApplySpellCooldown(caster.CTBUnit, spell.cooldownTicks);

            // 朝向目标
            caster.FaceTarget(target.Position);

            DamageCalculator.DamageResult damage = default;
            string msg;

            switch (spell.type)
            {
                case SpellType.Physical:
                    damage = DamageCalculator.CalcPhysical(caster.PhysAtk,
                        spell.physicalDamageMultiplier * ConsumeLeijieForPhysicalAction(caster),
                        caster, target, spell.cannotBlock, spell.element, spell.cannotDodge);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
                    msg = $"{caster.Name} 施放 {spell.spellName} → {target.Name}: {damage.Log}";
                    break;

                case SpellType.Magic:
                    float syMult = caster.GongFaName == "抱元守一经" ? 1f + caster.ShouyiStacks * 0.05f : 1f;
                    FudanActionBonus spellFudanBonus = ConsumeFudanForMagicAction(caster);

                    damage = DamageCalculator.CalcMagic(caster.MagAtk,
                        spell.soulDamageMultiplier * syMult * spellFudanBonus.DamageMultiplier,
                        caster,
                        target,
                        spell.element,
                        spell.cannotDodge || spellFudanBonus.WasFull,
                        spell.penetratingShield,
                        spellFudanBonus.MagicDefensePenetrationPercent);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
                    msg = $"{caster.Name} 施放 {spell.spellName} → {target.Name}: {damage.Log}";
                    break;

                case SpellType.Hybrid:
                    var physicalDamage = DamageCalculator.CalcPhysical(caster.PhysAtk,
                        spell.physicalDamageMultiplier * ConsumeLeijieForPhysicalAction(caster),
                        caster, target, spell.cannotBlock, spell.element, spell.cannotDodge);
                    float hybridSoulMultiplier = caster.GongFaName == "抱元守一经" ? 1f + caster.ShouyiStacks * 0.05f : 1f;
                    FudanActionBonus hybridFudanBonus = ConsumeFudanForMagicAction(caster);
                    var soulDamage = DamageCalculator.CalcMagic(caster.MagAtk,
                        spell.soulDamageMultiplier * hybridSoulMultiplier * hybridFudanBonus.DamageMultiplier,
                        caster, target, spell.element, spell.cannotDodge || hybridFudanBonus.WasFull,
                        spell.penetratingShield, hybridFudanBonus.MagicDefensePenetrationPercent);
                    if (physicalDamage.IsHit) target.TakeDamage(physicalDamage.FinalDamage);
                    if (soulDamage.IsHit) target.TakeDamage(soulDamage.FinalDamage);
                    damage = new DamageCalculator.DamageResult
                    {
                        FinalDamage = physicalDamage.FinalDamage + soulDamage.FinalDamage,
                        IsHit = physicalDamage.IsHit || soulDamage.IsHit,
                        Log = $"{physicalDamage.Log}; {soulDamage.Log}"
                    };
                    msg = $"{caster.Name} 施放 {spell.spellName} → {target.Name}: {damage.Log}";
                    break;

                case SpellType.Heal:
                    caster.Heal(spell.healAmount);
                    msg = $"{caster.Name} 施放 {spell.spellName}: 恢复{spell.healAmount}HP";
                    break;

                case SpellType.Buff:
                    msg = $"{caster.Name} 施放 {spell.spellName}: 获得增益";
                    break;

                case SpellType.Movement:
                    // 位移术法（如缩地成寸）：TODO
                    msg = $"{caster.Name} 施放 {spell.spellName}（位移）";
                    break;

                default:
                    msg = $"{caster.Name} 施放 {spell.spellName}";
                    break;
            }

            // 守一：出手后+1层
            if (caster.GongFaName == "抱元守一经")
                caster.ShouyiStacks = Mathf.Min(caster.ShouyiStacks + 1, caster.MaxShouyi());
            // 符胆：行动结束补1层；若本次神魂行动已消耗符胆，则从消耗后的层数开始回补。
            if (caster.GongFaName == "云篆度人经")
                caster.FudanStacks = Mathf.Min(caster.FudanStacks + 1, caster.MaxFudan());

            // 眩晕判定
            if (spell.stunChance > 0 && Random.value * 100f < spell.stunChance)
            {
                msg += " [眩晕!]";
                // TODO: 目标下回合跳过
            }

            BattleLog.Add(msg);
            Debug.Log(msg);

            return new ActionResult { Success = true, Damage = damage, Message = msg };
        }

        /// <summary>使用神通</summary>
        public ActionResult UseSkill(Character caster, Character target,
            int skillIndex, DivineSkillData skill)
        {
            if (caster.SkillCooldowns[skillIndex] > 0)
                return new ActionResult { Success = false, Message = $"{skill.skillName} 冷却中" };

            if (!CanTarget(
                caster.Position,
                target.Position,
                skill.minRange,
                skill.maxRange,
                out string rangeReason))
                return RangeFailure("目标不在射程范围", rangeReason);

            if (!caster.ConsumeMP(skill.mpCost))
                return new ActionResult { Success = false, Message = "灵力不足" };

            caster.SkillCooldowns[skillIndex] = skill.cooldownTicks;
            Engine.ApplySpellCooldown(caster.CTBUnit, skill.cooldownTicks);
            caster.FaceTarget(target.Position);

            DamageCalculator.DamageResult damage;
            if (skill.type == SpellType.Physical)
            {
                damage = DamageCalculator.CalcPhysical(caster.PhysAtk,
                    skill.damageMultiplier * ConsumeLeijieForPhysicalAction(caster),
                    caster, target, skill.cannotBlock, skill.element, skill.cannotDodge);
            }
            else
            {
                float syMult = caster.GongFaName == "抱元守一经" ? 1f + caster.ShouyiStacks * 0.05f : 1f;
                FudanActionBonus fudanBonus = ConsumeFudanForMagicAction(caster);
                damage = DamageCalculator.CalcMagic(
                    caster.MagAtk,
                    skill.damageMultiplier * syMult * fudanBonus.DamageMultiplier,
                    caster,
                    target,
                    skill.element,
                    skill.cannotDodge || fudanBonus.WasFull,
                    skill.penetratingShield,
                    fudanBonus.MagicDefensePenetrationPercent);
            }

            if (damage.IsHit) target.TakeDamage(damage.FinalDamage);

            // 守一：出手后+1层
            if (caster.GongFaName == "抱元守一经")
                caster.ShouyiStacks = Mathf.Min(caster.ShouyiStacks + 1, caster.MaxShouyi());
            // 符胆：出手后+1层
            if (caster.GongFaName == "云篆度人经")
                caster.FudanStacks = Mathf.Min(caster.FudanStacks + 1, caster.MaxFudan());

            string msg = $"{caster.Name} 神通·{skill.skillName} → {target.Name}: {damage.Log}";
            BattleLog.Add(msg);
            Debug.Log(msg);

            return new ActionResult { Success = true, Damage = damage, Message = msg };
        }

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

        public IReadOnlyDictionary<HexCoord, int> FindReachable(Character mover)
        {
            var result = new Dictionary<HexCoord, int>();
            if (spatialBoard == null || mover == null)
                return result;

            var blocked = new HashSet<SpatialHexCoord>(occupied);
            blocked.Remove(ToSpatial(mover.Position));
            foreach (var entry in spatialBoard.FindReachable(
                ToSpatial(mover.Position), mover.MovePoints, blocked))
                result[ToHex(entry.Key)] = entry.Value;
            return result;
        }

        public bool TryFindPositionForRange(
            Character mover,
            Character target,
            int minRange,
            int maxRange,
            out HexCoord position)
        {
            position = mover != null ? mover.Position : default;
            if (spatialBoard == null || mover == null || target == null)
                return false;

            bool foundInRange = false;
            int bestMovementCost = int.MaxValue;
            int bestDistanceUnits = int.MaxValue;
            foreach (var entry in FindReachable(mover))
            {
                bool inRange = CanTarget(
                    entry.Key, target.Position, minRange, maxRange, out _);
                var distance = spatialBoard.QueryMetricDistance(
                    ToSpatial(entry.Key),
                    ToSpatial(target.Position),
                    SpatialQueryKind.Attack);
                int distanceUnits = distance.IsReachable ? distance.DistanceUnits : int.MaxValue;

                if (foundInRange && !inRange)
                    continue;
                if (inRange && !foundInRange)
                {
                    foundInRange = true;
                    bestMovementCost = int.MaxValue;
                    bestDistanceUnits = int.MaxValue;
                }
                if (!inRange && entry.Key == mover.Position)
                    continue;
                bool losesTie = foundInRange
                    ? entry.Value > bestMovementCost ||
                      (entry.Value == bestMovementCost && distanceUnits > bestDistanceUnits)
                    : distanceUnits > bestDistanceUnits ||
                      (distanceUnits == bestDistanceUnits && entry.Value > bestMovementCost);
                if (losesTie ||
                    (entry.Value == bestMovementCost && distanceUnits == bestDistanceUnits &&
                     Compare(entry.Key, position) >= 0))
                    continue;

                position = entry.Key;
                bestMovementCost = entry.Value;
                bestDistanceUnits = distanceUnits;
            }

            return bestMovementCost != int.MaxValue;
        }

        public bool CanTarget(
            HexCoord source,
            HexCoord target,
            int minRange,
            int maxRange,
            out string reason)
        {
            if (spatialBoard == null)
            {
                reason = SpatialQueryReasons.QueryBoardNotConfigured;
                return false;
            }

            try
            {
                var range = spatialBoard.QueryRange(
                    ToSpatial(source),
                    minRange,
                    maxRange,
                    SpatialQueryKind.Attack,
                    requireLineOfSight: true);
                if (!range.TryGet(ToSpatial(target), out var entry))
                {
                    reason = SpatialQueryReasons.MissingCell;
                    return false;
                }

                reason = entry.Reason;
                return entry.IsInRange;
            }
            catch (System.ArgumentException)
            {
                reason = SpatialQueryReasons.MissingCell;
                return false;
            }
        }

        private static ActionResult RangeFailure(string message, string reason)
        {
            return new ActionResult
            {
                Success = false,
                Message = reason == SpatialQueryReasons.QueryBoardNotConfigured
                    ? "空间查询未配置"
                    : message,
                Reason = reason,
            };
        }

        private static SpatialHexCoord ToSpatial(HexCoord coord) =>
            new SpatialHexCoord(coord.q, coord.r);

        private static HexCoord ToHex(SpatialHexCoord coord) =>
            new HexCoord(coord.Q, coord.R);

        private static int Compare(HexCoord left, HexCoord right)
        {
            int q = left.q.CompareTo(right.q);
            return q != 0 ? q : left.r.CompareTo(right.r);
        }

        public void ClearLog() => BattleLog.Clear();
    }
}
