using System.Collections.Generic;
using System.Linq;
using TianZhang.Combat;

namespace TianZhang.Features.Adventure
{
    /// <summary>The basic practice opponent holds position and makes at most one basic attack per turn.</summary>
    internal sealed class JindanChallengeEnemyPolicy : ICombatActionPolicy
    {
        public CombatCommand ChooseAction(CombatSession session, IReadOnlyList<CombatCommand> legal)
        {
            if (legal.Count == 0) return null;
            var strike = legal.FirstOrDefault(command => command.Kind == CombatCommandKind.BasicAttack);
            if (!session.HasCommittedBenefit && strike != null) return strike;
            return legal.First(command => command.Kind == CombatCommandKind.EndTurn);
        }
    }
}
