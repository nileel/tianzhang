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
            AttackProfileData[] arts, AttackProfileData[] divines, AttackProfileData basicAttack,
            CombatResolver resolver);
    }

    public class SimpleAI : IAIController
    {
        public string ExecuteTurn(Character self, Character target,
            AttackProfileData[] arts, AttackProfileData[] divines, AttackProfileData basicAttack,
            CombatResolver resolver)
        {
            // 1. 尝试施放术法（优先选第一个可用的）
            if (arts != null)
            {
                for (int i = 0; i < arts.Length; i++)
                {
                    if (arts[i] != null && arts[i].profileKind == AttackProfileKind.Art
                        && i < self.SpellCooldowns.Length && self.SpellCooldowns[i] <= 0
                        && (arts[i].resourceKind == AttackResourceKind.None || self.CurrentMP >= arts[i].resourceCost)
                        && resolver.CanTarget(
                            self.Position,
                            target.Position,
                            arts[i].minCastRange,
                            arts[i].maxCastRange,
                            out _))
                    {
                        var result = resolver.CastSpell(self, target, i, arts[i]);
                        return result.Message;
                    }
                }
            }

            // 2. 尝试使用神通
            if (divines != null)
            {
                for (int i = 0; i < divines.Length; i++)
                {
                    if (divines[i] != null && divines[i].profileKind == AttackProfileKind.Divine
                        && i < self.SkillCooldowns.Length && self.SkillCooldowns[i] <= 0
                        && (divines[i].resourceKind == AttackResourceKind.None || self.CurrentMP >= divines[i].resourceCost)
                        && resolver.CanTarget(
                            self.Position,
                            target.Position,
                            divines[i].minCastRange,
                            divines[i].maxCastRange,
                            out _))
                    {
                        var result = resolver.UseSkill(self, target, i, divines[i]);
                        return result.Message;
                    }
                }
            }

            // 3. 移动接近目标
            if (basicAttack != null && !resolver.CanTarget(
                    self.Position,
                    target.Position,
                    basicAttack.minCastRange,
                    basicAttack.maxCastRange,
                    out _))
            {
                var path = resolver.FindPathTowardTarget(self, target);
                if (path != null && path.Count > 0)
                {
                    var result = resolver.Move(self, path);
                    return result.Message;
                }
            }

            // 4. 近战攻击
            if (basicAttack != null && resolver.CanTarget(
                    self.Position,
                    target.Position,
                    basicAttack.minCastRange,
                    basicAttack.maxCastRange,
                    out _))
            {
                var result = resolver.BasicAttack(self, target, basicAttack);
                return result.Message;
            }

            // 5. 防御
            var guardResult = resolver.Guard(self);
            return guardResult.Message;
        }
    }
}
