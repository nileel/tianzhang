using System.Collections;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.CombatPresentation;
using TianZhang.Spatial;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(240000)]
        public IEnumerator InspectChangedGroundThroughBoard()
        {
            yield return StartChallenge(1366, Water, "stage2");
            yield return UntilActor(Player);
            var cell = new HexCoord(5, 6);
            yield return TideOn(cell);
            yield return Pick(cell);
            var target = Find<CombatTargetView>();
            Assert.AreEqual("地块 (5,6)", target.NameText);
            StringAssert.Contains("湿泥", target.DetailsText);
            StringAssert.Contains("蓄澜", target.DetailsText);
            yield return Capture("01-inspected-wet-tile");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator InspectGravityAndItsRemovalThroughBoard()
        {
            yield return StartChallenge(1366, Earth, "stage1");
            yield return UntilActor(Player);
            var seat = Seat("EARTH_GRAVITY");
            var caster = Actor(Player);
            var request = new CombatJindanRequest(seat.Definition.SeatId, earthMode: CombatEarthMode.Anchor,
                earthTargetId: Player, selectedTargetCell: caster.Position);
            yield return Use("earth_tab", "earth_anchor", Jindan(seat.EffectId, request), caster.Position);
            yield return Pick(caster.Position);
            var target = Find<CombatTargetView>();
            Assert.AreEqual(Player, target.TargetId);
            StringAssert.Contains("镇岳", target.DetailsText);
            StringAssert.Contains("剩余", target.DetailsText);
            StringAssert.Contains($"脚下地块 ({caster.Position.Q},{caster.Position.R})", target.DetailsText);
            yield return Capture("01-inspected-anchored-unit");
            yield return Click(target.CloseButton);
            yield return Use("close_tab", "earth_close", Jindan(seat.EffectId, close: true));
            yield return Pick(caster.Position);
            Assert.AreEqual(Player, target.TargetId);
            StringAssert.DoesNotContain("镇岳", target.DetailsText);
            yield return Capture("02-inspected-after-close");
        }
    }
}
