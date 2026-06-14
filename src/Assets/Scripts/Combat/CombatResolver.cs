using System.Collections.Generic;
using UnityEngine;
using TianZhang.Core;
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

        /// <summary>移动角色</summary>
        public ActionResult Move(Character mover, List<HexCoord> path)
        {
            if (path == null || path.Count == 0)
                return new ActionResult { Success = false, Message = "无移动路径" };

            int steps = Mathf.Min(path.Count, mover.MovePoints);
            HexCoord finalPos = path[steps - 1];

            if (Grid.IsOccupied(finalPos))
                return new ActionResult { Success = false, Message = "目标格被占据" };

            Grid.ClearOccupied(mover.Position);
            mover.Position = finalPos;
            Grid.SetOccupied(finalPos, mover.CTBUnit.Id);

            string msg = $"{mover.Name} 移动到 {finalPos}（{steps}步）";
            BattleLog.Add(msg);
            return new ActionResult { Success = true, Message = msg };
        }

        /// <summary>普通攻击</summary>
        public ActionResult BasicAttack(Character attacker, Character defender,
            bool useMagic = false)
        {
            if (!IsInRange(attacker.Position, defender.Position, 1))
                return new ActionResult { Success = false, Message = "目标不在近战范围" };

            // 朝向目标
            attacker.FaceTarget(defender.Position);

            DamageCalculator.DamageResult damage;
            float mult = 1f;
            if (useMagic && attacker.GongFaName == "抱元守一经")
                mult = 1f + attacker.ShouyiStacks * 0.05f;

            if (useMagic)
                damage = DamageCalculator.CalcMagic((int)(attacker.MagAtk * mult), mult, attacker, defender);
            else
                damage = DamageCalculator.CalcPhysical(attacker.PhysAtk, mult, attacker, defender);

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

            // MP检查（符胆满层：零耗MP）
            bool fudanMax = caster.GongFaName == "云篆度人经" && caster.FudanStacks == caster.MaxFudan();
            int effectiveMpCost = fudanMax ? 0 : spell.mpCost;
            if (!caster.ConsumeMP(effectiveMpCost))
                return new ActionResult { Success = false, Message = "灵力不足" };

            // 射程检查（自指向术法 minRange=0 maxRange=0 跳过）
            bool isSelfTarget = spell.minRange == 0 && spell.maxRange == 0;
            if (!isSelfTarget)
            {
                int range = caster.Position.Distance(target.Position);
                if (range < spell.minRange || range > spell.maxRange)
                    return new ActionResult { Success = false, Message = "目标不在射程范围" };
            }

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
                    damage = DamageCalculator.CalcPhysical(caster.PhysAtk, spell.damageMultiplier,
                        caster, target, spell.cannotBlock);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
                    msg = $"{caster.Name} 施放 {spell.spellName} → {target.Name}: {damage.Log}";
                    break;

                case SpellType.Magic:
                    float syMult = caster.GongFaName == "抱元守一经" ? 1f + caster.ShouyiStacks * 0.05f : 1f;

                    // 符胆印记：消耗全部层数换伤害加成 + 满层无视30%魂防
                    float fdMult = 1f;
                    if (caster.GongFaName == "云篆度人经" && caster.FudanStacks > 0)
                    {
                        // 境界加成率：筑基12%/金丹15%/元婴18%/化神22%
                        float realmMult = Cultivation.CultivationEngine.GetRealmMultiplier(caster.GetRealm());
                        float fdRate = realmMult >= 24f ? 0.22f : realmMult >= 12f ? 0.18f :
                                       realmMult >= 6f ? 0.15f : realmMult >= 3f ? 0.12f : 0.15f;
                        fdMult = 1f + caster.FudanStacks * fdRate;
                        // 满层：无视30%魂防（通过defPen传入CalcMagic，暂时用MagDef降低模拟）
                        if (fudanMax) fdMult *= 1.30f;
                        // 消耗层数（化神保留2层）
                        float mr = Cultivation.CultivationEngine.GetRealmMultiplier(caster.GetRealm());
                        caster.FudanStacks = mr >= 24f ? 2 : 0;
                    }

                    damage = DamageCalculator.CalcMagic((int)(caster.MagAtk * syMult * fdMult),
                        spell.damageMultiplier, caster, target);
                    if (damage.IsHit) target.TakeDamage(damage.FinalDamage);
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
            // 符胆：出手后+1层（但术法已消耗，仅平A和神通有效）
            // 注意：术法case中已消耗Fudan，此处不再+1
            if (caster.GongFaName == "云篆度人经" && spell.type != SpellType.Magic)
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

            if (!caster.ConsumeMP(skill.mpCost))
                return new ActionResult { Success = false, Message = "灵力不足" };

            int range = caster.Position.Distance(target.Position);
            if (range < skill.minRange || range > skill.maxRange)
                return new ActionResult { Success = false, Message = "目标不在射程范围" };

            caster.SkillCooldowns[skillIndex] = skill.cooldownTicks;
            Engine.ApplySpellCooldown(caster.CTBUnit, skill.cooldownTicks);
            caster.FaceTarget(target.Position);

            DamageCalculator.DamageResult damage;
            if (skill.type == SpellType.Physical)
            {
                damage = DamageCalculator.CalcPhysical(caster.PhysAtk, skill.damageMultiplier,
                    caster, target, skill.cannotBlock);
            }
            else
            {
                float syMult = caster.GongFaName == "抱元守一经" ? 1f + caster.ShouyiStacks * 0.05f : 1f;
                damage = DamageCalculator.CalcMagic((int)(caster.MagAtk * syMult), skill.damageMultiplier * syMult,
                    caster, target);
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

        private bool IsInRange(HexCoord a, HexCoord b, int maxRange) =>
            a.Distance(b) <= maxRange;

        public void ClearLog() => BattleLog.Clear();
    }
}
