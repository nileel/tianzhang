using System;
using TianZhang.Combat;
using TianZhang.Spatial;

namespace TianZhang.Features.Adventure
{
    /// <summary>Encounter configuration and optional introductory prompts for the terrace battles.</summary>
    public sealed class JindanChallengeProgress
    {
        public const string VictoryCondition = "敌方全灭，我方有人存活。";
        public const string DefeatCondition = "我方全灭，包括双方同归于尽。";
        private static readonly HexCoord BasicGoal = new HexCoord(4, 1);

        private enum PracticeStep { Move, Cast, EndTurn, Complete }
        private PracticeStep practiceStep;

        public JindanChallengeProgress(int segmentIndex)
        {
            if (segmentIndex < -1 || segmentIndex > 2) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            SegmentIndex = segmentIndex;
        }

        public int SegmentIndex { get; }
        public int Seed => 20261119 + SegmentIndex + 1;
        public int ConfigVersion => 3;

        public static string StageName(int segmentIndex) => segmentIndex switch
        {
            -1 => "基础演练", 0 => "登高", 1 => "水火", 2 => "关隘",
            _ => throw new ArgumentOutOfRangeException(nameof(segmentIndex))
        };

        public string ProgressText => SegmentIndex != -1 ? "目标：全歼敌方。" : practiceStep switch
        {
            PracticeStep.Move => "全歼敌方。可选练习：走到浅水格 (4,1)。",
            PracticeStep.Cast => "全歼敌方。可选练习：普攻或施放一项已装配的术法。",
            PracticeStep.EndTurn => "全歼敌方。可选练习：点结束行动。",
            _ => "操作练习已完成。继续战斗，全歼敌方。"
        };

        public HexCoord? MovementTarget(CombatSession session) =>
            SegmentIndex == -1 && practiceStep == PracticeStep.Move && session != null ? BasicGoal : (HexCoord?)null;

        public static string FirstAction(int segmentIndex) => segmentIndex switch
        {
            -1 => "对手原地守候。可走到浅水格 (4,1) 熟悉移动，也可直接接敌。",
            0 => "裂石甲兽能从高台使用裂石术。可沿坡接近，留意射程和高差。",
            1 => "水岸有天然火。可从干地绕行，再接近石甲兽。",
            2 => "裂石甲兽守在关隘。可从侧面接近，或进入射程后出手。",
            _ => throw new ArgumentOutOfRangeException(nameof(segmentIndex))
        };

        internal void Observe(CombatSession session, CombatCommand command)
        {
            if (SegmentIndex != -1 || command.ActorId != "player") return;
            switch (practiceStep)
            {
                case PracticeStep.Move when command.Kind == CombatCommandKind.Move &&
                    session.Combatants.TryGet(command.ActorId, out var player) && player.Position == BasicGoal:
                    practiceStep = PracticeStep.Cast;
                    break;
                case PracticeStep.Cast when command.Kind is CombatCommandKind.BasicAttack or CombatCommandKind.Art:
                    practiceStep = PracticeStep.EndTurn;
                    break;
                case PracticeStep.EndTurn when command.Kind == CombatCommandKind.EndTurn:
                    practiceStep = PracticeStep.Complete;
                    break;
            }
        }
    }
}
