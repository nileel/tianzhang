using UnityEngine;
using TianZhang.Core;

namespace TianZhang.Entity
{
    /// <summary>
    /// 运行时角色实体
    /// 属性公式与 BattleSim 一致（v3.4 次线性HP + 二级重映射）
    /// </summary>
    public class Character
    {
        // ---- 一级属性 ----
        public int RootBone;
        public int Physique;
        public int Spirit;
        public int Mind;
        public int Reaction;
        public int Talent;

        // ---- 二级属性 ----
        public int MaxHP;
        public int CurrentHP;
        public int MaxMP;
        public int CurrentMP;
        public int PhysAtk;
        public int MagAtk;
        public int PhysDef;
        public int MagDef;

        // ---- 二级概率 ----
        public float BlockRate;
        public float BlockReduction;
        public float SoulShieldRate;
        public float SoulShieldReduction;
        public float DodgeRate;
        public float CritRate;
        public float CritDamage;
        public float HitRateBonus;

        // ---- 战斗状态 ----
        public int Facing;           // 0-5 朝向
        public HexCoord Position;
        public int MovePoints = 3;   // 每回合可移动格数（从反应推算）
        public bool IsGuarding;      // 防御姿态
        public bool IsAlive = true;

        // ---- CTB 引用 ----
        public CTBEngine.CTBUnit CTBUnit;

        // ---- 术法/神通冷却 ----
        public int[] SpellCooldowns;   // 剩余冷却刻数
        public int[] SkillCooldowns;

        // ---- 姓名 ----
        public string Name;

        public static Character FromData(CharacterData data, HexCoord startPos)
        {
            var c = new Character
            {
                Name = data.charName,
                RootBone = data.rootBone,
                Physique = data.physique,
                Spirit = data.spirit,
                Mind = data.mind,
                Reaction = data.reaction,
                Talent = data.talent,
                Position = startPos,
                Facing = 0,
            };

            // 境界倍率（从 data 读取，或默认值）
            float realm = data.realmMultiplier > 0 ? data.realmMultiplier : 1f;

            // ---- 次线性HP公式（v3.4：根骨^0.75 × 境界倍率 × 基础值）----
            float hpBase = Mathf.Pow(c.RootBone, 0.75f) * realm * 80f;
            c.MaxHP = Mathf.RoundToInt(hpBase + data.hpBonus);
            c.CurrentHP = c.MaxHP;

            // MP
            float mpBase = c.Spirit * realm * 15f;
            c.MaxMP = Mathf.RoundToInt(mpBase + data.mpBonus);
            c.CurrentMP = c.MaxMP;

            // 攻击力
            c.PhysAtk = Mathf.RoundToInt(c.RootBone * realm * 5f + data.physAtkBonus);
            c.MagAtk = Mathf.RoundToInt(c.Spirit * realm * 5f + data.magAtkBonus);

            // 防御力
            c.PhysDef = Mathf.RoundToInt(c.Physique * realm * 3.5f + data.physDefBonus);
            c.MagDef = Mathf.RoundToInt(c.Mind * realm * 3.5f + data.magDefBonus);

            // 反应 → 移动力
            c.MovePoints = Mathf.Clamp(Mathf.RoundToInt(c.Reaction / 20f), 2, 8);

            // 二级概率
            c.BlockRate = data.blockRate;
            c.BlockReduction = data.blockReduction;
            c.SoulShieldRate = data.soulShieldRate;
            c.SoulShieldReduction = data.soulShieldReduction;
            c.DodgeRate = data.dodgeRate;
            c.CritRate = data.critRate;
            c.CritDamage = data.critDamage;
            c.HitRateBonus = data.hitRateBonus;

            // 术法/神通冷却初始化
            c.SpellCooldowns = new int[data.equippedSpells?.Length ?? 0];
            c.SkillCooldowns = new int[data.equippedSkills?.Length ?? 0];

            return c;
        }

        /// <summary>受击朝向修正（正面0/侧面1-2/背面3 → 命中率/伤害修正）</summary>
        public float GetFacingHitModifier(Character attacker)
        {
            int dirToAttacker = Position.DirectionTo(attacker.Position);
            if (dirToAttacker < 0) return 1f; // 不相邻，无朝向
            int diff = HexCoord.DirectionDiff(Facing, dirToAttacker);
            return diff switch
            {
                0 => 0.85f,  // 正面：命中率降低
                1 or 2 => 1.0f, // 侧面：正常
                3 => 1.15f,  // 背面：命中率提高
                _ => 1f,
            };
        }

        public float GetFacingDamageModifier(Character attacker)
        {
            int dirToAttacker = Position.DirectionTo(attacker.Position);
            if (dirToAttacker < 0) return 1f;
            int diff = HexCoord.DirectionDiff(Facing, dirToAttacker);
            return diff switch
            {
                0 => 1f,     // 正面正常伤害
                1 or 2 => 1.15f,  // 侧面+15%
                3 => 1.3f,   // 背面+30%
                _ => 1f,
            };
        }

        /// <summary>转动朝向（面向目标）</summary>
        public void FaceTarget(HexCoord target)
        {
            int dir = Position.DirectionTo(target);
            if (dir >= 0) Facing = dir;
        }

        /// <summary>消耗MP</summary>
        public bool ConsumeMP(int amount)
        {
            if (CurrentMP < amount) return false;
            CurrentMP -= amount;
            return true;
        }

        public void TakeDamage(int amount)
        {
            CurrentHP -= amount;
            if (CurrentHP <= 0)
            {
                CurrentHP = 0;
                IsAlive = false;
            }
        }

        public void Heal(int amount)
        {
            CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        }

        public void RestoreMP(int amount)
        {
            CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
        }

        /// <summary>应用功法篇章加成（永久二级属性）</summary>
        public void ApplyGongFaBonuses(Cultivation.GongFaGrowthData gongFa, string realm)
        {
            if (gongFa == null) return;
            var bonus = gongFa.GetCumulativeBonus(realm);
            SoulShieldRate += bonus.soulShieldRate;
            HitRateBonus += bonus.hitRate;
            BlockRate += bonus.blockRate;
            CritRate += bonus.critRate;
            CritDamage += bonus.critDamage;
            DodgeRate += bonus.dodgeRate;
            MagAtk += Mathf.RoundToInt(bonus.magAtkBonus);
            MagDef += Mathf.RoundToInt(bonus.magDefBonus);
        }

        public override string ToString() =>
            $"{Name} HP={CurrentHP}/{MaxHP} MP={CurrentMP}/{MaxMP} " +
            $"PAtk={PhysAtk} MAtk={MagAtk} PDef={PhysDef} MDef={MagDef} " +
            $"Pos={Position} Face={Facing}";
    }
}