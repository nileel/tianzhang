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
        public static readonly HexCoord HighGoal = new HexCoord(8, 12);
        private static readonly HexCoord BasicGoal = new HexCoord(4, 1);
        private const string FirePreset = "demo_20261024_fire";
        private const string YinPreset = "demo_20261024_seal_yin";

        private enum PracticeStep { Move, Cast, EndTurn, Complete }
        private PracticeStep practiceStep;

        public JindanChallengeProgress(int segmentIndex, string presetId)
        {
            if (segmentIndex < -1 || segmentIndex > 2) throw new ArgumentOutOfRangeException(nameof(segmentIndex));
            SegmentIndex = segmentIndex;
            PresetId = presetId;
        }

        public int SegmentIndex { get; }
        public string PresetId { get; }
        public int Seed => 20261119 + SegmentIndex + 1;
        public int ConfigVersion => 2;
        internal bool FireDefender => SegmentIndex == 1 && PresetId == FirePreset;
        internal bool YinDefender => SegmentIndex == 2 && PresetId == YinPreset;

        public string ProgressText => SegmentIndex != -1 ? "目标：全歼敌方。" : practiceStep switch
        {
            PracticeStep.Move => "全歼敌方。可选练习：走到浅水格 (4,1)。",
            PracticeStep.Cast => "全歼敌方。可选练习：施放飞星指，可选空格。",
            PracticeStep.EndTurn => "全歼敌方。可选练习：点结束行动。",
            _ => "操作练习已完成。继续战斗，全歼敌方。"
        };

        public HexCoord? MovementTarget(CombatSession session) =>
            SegmentIndex == -1 && practiceStep == PracticeStep.Move && session != null ? BasicGoal : (HexCoord?)null;

        public static string FirstAction(int segmentIndex, string presetId) => segmentIndex switch
        {
            -1 => "对手原地守候。可到 (4,1) 试用飞星指，也可直接接敌。",
            0 => "对手会御风登台。可沿坡接近，再选择对空攻击或压低对手。",
            1 when presetId == FirePreset => "长明可封住来路。阵心受击时，也可直接攻击来敌。",
            1 => "可沿干地接敌，或借潮水灭火开路。",
            2 when presetId == YinPreset => "可借封关阻敌，借藏真寻找出手机会。",
            2 => "贴近可察觉藏真者，也可利用阳照找出对手。",
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
                case PracticeStep.Cast when command.Kind == CombatCommandKind.Art && command.ProfileId == "spell_feixingzhi":
                    practiceStep = PracticeStep.EndTurn;
                    break;
                case PracticeStep.EndTurn when command.Kind == CombatCommandKind.EndTurn:
                    practiceStep = PracticeStep.Complete;
                    break;
            }
        }
    }
}
