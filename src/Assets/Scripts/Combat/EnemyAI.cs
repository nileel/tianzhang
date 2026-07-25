using TianZhang.Core;
using TianZhang.Entity;

namespace TianZhang.Combat
{
    /// <summary>
    /// 简易 AI 对手
    /// 逻辑：选最近可用术法 → 移动接近目标 → 攻击 → 防御
    /// 预留 IAIController 接口供阶段二升级
    /// </summary>
    public interface IAIController
    {
        string ExecuteTurn(Character self, Character target,
            SpellData[] spells, DivineSkillData[] skills,
            CombatResolver resolver);
    }

    public class SimpleAI : IAIController
    {
        public string ExecuteTurn(Character self, Character target,
            SpellData[] spells, DivineSkillData[] skills,
            CombatResolver resolver)
        {
            // 1. 尝试施放术法（优先选第一个可用的）
            if (spells != null)
            {
                for (int i = 0; i < spells.Length; i++)
                {
                    if (self.SpellCooldowns[i] <= 0
                        && self.CurrentMP >= spells[i].mpCost
                        && resolver.CanTarget(
                            self.Position,
                            target.Position,
                            spells[i].minRange,
                            spells[i].maxRange,
                            out _))
                    {
                        var result = resolver.CastSpell(self, target, i, spells[i]);
                        return result.Message;
                    }
                }
            }

            // 2. 尝试使用神通
            if (skills != null)
            {
                for (int i = 0; i < skills.Length; i++)
                {
                    if (self.SkillCooldowns[i] <= 0
                        && self.CurrentMP >= skills[i].mpCost
                        && resolver.CanTarget(
                            self.Position,
                            target.Position,
                            skills[i].minRange,
                            skills[i].maxRange,
                            out _))
                    {
                        var result = resolver.UseSkill(self, target, i, skills[i]);
                        return result.Message;
                    }
                }
            }

            // 3. 移动接近目标
            if (!resolver.CanTarget(self.Position, target.Position, 1, 1, out _))
            {
                var path = resolver.FindPathTowardTarget(self, target);
                if (path != null && path.Count > 0)
                {
                    var result = resolver.Move(self, path);
                    return result.Message;
                }
            }

            // 4. 近战攻击
            if (resolver.CanTarget(self.Position, target.Position, 1, 1, out _))
            {
                bool useMagic = self.MagAtk > self.PhysAtk;
                var result = resolver.BasicAttack(self, target, useMagic);
                return result.Message;
            }

            // 5. 防御
            var guardResult = resolver.Guard(self);
            return guardResult.Message;
        }
    }
}
