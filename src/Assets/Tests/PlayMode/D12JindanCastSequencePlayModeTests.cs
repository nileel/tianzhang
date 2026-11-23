using System.Collections;
using System.Linq;
using NUnit.Framework;
using TianZhang.Combat;
using TianZhang.Features.CombatPresentation;
using TianZhang.Spatial;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed partial class D12ChallengeRoutesPlayModeTests
    {
        [UnityTest, Timeout(150000)]
        public IEnumerator PlayerFireCastReachesChosenCellBeforeFieldOpens()
        {
            yield return StartChallenge(1366, Fire, "basic");
            yield return UntilActor(Player);
            var seat = Seat("FIRE_MANIFEST");
            var landing = (from support in surface.Cells
                let cell = new HexCoord(support.q, support.r)
                let distance = Current.Position.TopologicalDistanceTo(cell)
                where distance > 0
                orderby Mathf.Abs(distance - 2), cell.Q, cell.R
                let command = Jindan("FIRE_MANIFEST", new CombatJindanRequest(seat.Definition.SeatId, selectedTargetCell: cell))
                where encounter.PreviewJindan(command).IsLegal
                select cell).First();
            yield return Press("fire_tab");
            yield return Press("fire_preview");
            yield return Pick(landing);
            Assert.IsNull(Find<JindanCastVisual>(), "A selected cell alone must not start a cast.");
            Assert.IsNull(GameObject.Find("FireDomainWarmth"));

            var provider = Find<Static3DCombatUnitPresentationProvider>();
            Assert.IsTrue(provider.TryGetVisibleBounds(Player, out var body));
            float committedAt = -1f;
            encounter.JindanCastCommitted += (actor, effect, cell, edge, target) => committedAt = Time.time;
            float previousSpeed = Time.timeScale;
            try
            {
                // Keep screenshot I/O from skipping a phase on slow graphics runners.
                Time.timeScale = .2f;
                yield return Press("confirm");
                Assert.GreaterOrEqual(committedAt, 0f);
                Assert.AreEqual(landing, seat.FireDomain.Support.Cell);
                Assert.IsNotNull(seat.FireDomain.Main);
                var cast = Find<JindanCastVisual>();
                Assert.NotNull(cast);
                var seed = cast.transform.Find("GatheredLight").GetComponent<MeshRenderer>();
                var trail = cast.transform.Find("FlowingTrail").GetComponent<LineRenderer>();
                var column = cast.transform.Find("RisingPlume").GetComponent<MeshRenderer>();
                var impact = cast.transform.Find("LandingFlash").GetComponent<MeshRenderer>();
                Assert.IsTrue(seed.sharedMaterial.shader.isSupported);
                Assert.AreEqual("TianZhang/Jindan/Cast Light", seed.sharedMaterial.shader.name);

                yield return new WaitUntil(() => Time.time - committedAt >= .24f);
                Assert.IsTrue(provider.AwaitingJindanImpact);
                Assert.IsNull(GameObject.Find("FireDomainWarmth"));
                var origin = seed.transform.position;
                Assert.That(origin.y, Is.InRange(body.center.y, body.max.y));
                Assert.Less(Vector2.Distance(new Vector2(body.center.x, body.center.z), new Vector2(origin.x, origin.z)), .4f);
                Assert.IsFalse(trail.enabled || column.enabled || impact.enabled);
                yield return Capture("01-caster-gathers");

                yield return new WaitUntil(() => Time.time - committedAt >= .72f);
                Assert.IsNull(GameObject.Find("FireDomainWarmth"));
                Assert.IsTrue(trail.enabled);
                var head = trail.GetPosition(trail.positionCount - 1);
                var targetPosition = surface.Center(Hex(landing));
                Assert.Less(Vector3.Distance(origin, trail.GetPosition(0)), .01f);
                Assert.Greater(Vector3.Distance(origin, head), Vector3.Distance(origin, targetPosition) * .4f);
                Assert.Less(Vector2.Distance(new Vector2(head.x, head.z), new Vector2(targetPosition.x, targetPosition.z)),
                    Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(targetPosition.x, targetPosition.z)));
                yield return Capture("02-release-toward-cell");

                yield return new WaitUntil(() => Time.time - committedAt >= 1.04f);
                Assert.IsTrue(cast.HasLanded);
                Assert.IsNotNull(GameObject.Find("FireDomainWarmth"));
                Assert.Less(Vector3.Distance(impact.transform.position, targetPosition + Vector3.up * .13f), .01f);
                Assert.IsTrue(column.enabled && impact.enabled);
                Assert.Greater(column.transform.localScale.y, 1.5f);
                yield return Capture("03-selected-cell-opens");
            }
            finally { Time.timeScale = previousSpeed; }
            yield return Ready();
            Assert.IsNull(Find<JindanCastVisual>());
            Assert.IsNotNull(GameObject.Find("FireDomainWarmth"));
            Assert.IsNotNull(GameObject.Find("LampGlow"));
            yield return Capture("04-sustained-domain");
        }
    }
}
