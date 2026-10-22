using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class Static3DCombatUnitPresentationProviderPlayModeTests
    {
        private static readonly Vector3 BodyScale = new Vector3(1.4f, 1.4f, 1.4f);
        private const string ProfileSetPath =
            "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset";

        [UnityTest]
        public IEnumerator ApprovedProfilesProjectAllSixEventsAndCleanByCombatantId()
        {
            var host = new GameObject("Static3DCombatUnitPresentationProviderTest");
            host.SetActive(false);
            Static3DCombatUnitPresentationProvider provider =
                host.AddComponent<Static3DCombatUnitPresentationProvider>();
            SetPrivateField(provider, "profileSet", LoadProfileSet());
            host.SetActive(true);
            yield return null;

            var player = new CombatUnitPresentationDescriptor(
                "player", Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                CombatUnitDisplayFaction.Player, new CombatUnitPresentationHex(0, 0), 0);
            var enemy = new CombatUnitPresentationDescriptor(
                "enemy", Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                CombatUnitDisplayFaction.Enemy, new CombatUnitPresentationHex(1, 0), 3);
            provider.Prepare(new[] { player, enemy });

            Assert.AreEqual(2, provider.ActiveCombatantCount);
            Assert.IsTrue(provider.TryGetPresentedUnit(player.CombatantId, out GameObject playerRoot));
            Assert.IsTrue(provider.TryGetPresentedUnit(enemy.CombatantId, out GameObject enemyRoot));
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(player.Position), playerRoot.transform.position);
            Assert.Zero(playerRoot.GetComponentsInChildren<SpriteRenderer>(true).Length);
            Assert.AreEqual(1, playerRoot.GetComponentsInChildren<MeshRenderer>(true).Length,
                "The provider must create only the approved static body; no independent base may remain.");
            Transform playerBody = FindBody(playerRoot);
            Transform enemyBody = FindBody(enemyRoot);
            AssertGroundedBody(playerRoot, playerBody, Vector3.one);
            AssertGroundedBody(enemyRoot, enemyBody, Vector3.one);

            for (int facing = 0; facing < Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees.Length; facing++)
            {
                provider.Present(new CombatUnitPresentationEventProjection(
                    player.CombatantId, CombatUnitPresentationEvent.Idle, player.Position, player.Position, facing,
                    Array.Empty<CombatUnitPresentationTargetResult>()));
                provider.Present(new CombatUnitPresentationEventProjection(
                    enemy.CombatantId, CombatUnitPresentationEvent.Idle, enemy.Position, enemy.Position, facing,
                    Array.Empty<CombatUnitPresentationTargetResult>()));
                AssertYaw(playerRoot, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[facing]);
                AssertYaw(enemyRoot, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[facing]);
                AssertGroundedBody(playerRoot, playerBody, Vector3.one);
                AssertGroundedBody(enemyRoot, enemyBody, Vector3.one);
            }

            PresentAndAssertAction(provider, player, enemy, playerRoot, playerBody, 1,
                new Vector3(1.08f, 0.94f, 1.08f));
            PresentAndAssertAction(provider, enemy, player, enemyRoot, enemyBody, 4,
                new Vector3(1.08f, 0.94f, 1.08f));
            yield return new WaitForSeconds(0.25f);
            AssertGroundedBody(playerRoot, playerBody, Vector3.one);
            AssertGroundedBody(enemyRoot, enemyBody, Vector3.one);

            provider.Present(new CombatUnitPresentationEventProjection(
                player.CombatantId, CombatUnitPresentationEvent.Move, player.Position,
                new CombatUnitPresentationHex(1, -1), 1,
                new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 17, false) },
                new[] { new CombatUnitPresentationHex(1, -1) }));
            provider.Present(new CombatUnitPresentationEventProjection(
                enemy.CombatantId, CombatUnitPresentationEvent.Hit, enemy.Position, enemy.Position, 5,
                new[] { new CombatUnitPresentationTargetResult(player.CombatantId, 19, false) }));
            AssertGroundedBody(playerRoot, playerBody, Vector3.one);
            AssertGroundedBody(enemyRoot, enemyBody, new Vector3(0.94f, 1.06f, 0.94f));
            yield return new WaitForSeconds(0.25f);
            AssertGroundedBody(playerRoot, playerBody, Vector3.one);
            AssertGroundedBody(enemyRoot, enemyBody, Vector3.one);

            while (provider.IsPresenting) yield return null;
            foreach (CombatUnitPresentationDescriptor actor in new[] { player, enemy })
            {
                provider.Present(new CombatUnitPresentationEventProjection(
                    actor.CombatantId, CombatUnitPresentationEvent.Cast, actor.Position, actor.Position, 0,
                    Array.Empty<CombatUnitPresentationTargetResult>()));
                Assert.IsTrue(provider.TryGetPresentedUnit(actor.CombatantId, out GameObject actorRoot));
                AssertGroundedBody(actorRoot, FindBody(actorRoot), new Vector3(1.04f, 1.1f, 1.04f));
            }
            yield return new WaitForSeconds(0.25f);
            AssertGroundedBody(playerRoot, playerBody, Vector3.one);
            AssertGroundedBody(enemyRoot, enemyBody, Vector3.one);

            provider.Present(new CombatUnitPresentationEventProjection(
                enemy.CombatantId,
                CombatUnitPresentationEvent.Death,
                enemy.Position,
                enemy.Position,
                3,
                new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 19, true) }));
            Assert.IsFalse(enemyRoot.activeSelf);

            provider.Remove(player.CombatantId);
            Assert.AreEqual(1, provider.ActiveCombatantCount);
            provider.Clear();
            Assert.Zero(provider.ActiveCombatantCount);
            UnityEngine.Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AdjacentPathMovesEachSegmentAndClearCancelsOldSameIdUnit()
        {
            var host = new GameObject("PathProviderTest");
            var provider = host.AddComponent<Static3DCombatUnitPresentationProvider>();
            SetPrivateField(provider, "profileSet", LoadProfileSet());
            var actor = new CombatUnitPresentationDescriptor("player", Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                CombatUnitDisplayFaction.Player, new CombatUnitPresentationHex(0, 0), 0);
            provider.Prepare(new[] { actor });
            Assert.IsTrue(provider.TryGetPresentedUnit("player", out GameObject root));
            var path = new[] { new CombatUnitPresentationHex(1, 0), new CombatUnitPresentationHex(1, 1), new CombatUnitPresentationHex(2, 1) };
            var projection = new CombatUnitPresentationEventProjection("player", CombatUnitPresentationEvent.Move,
                actor.Position, path[2], 0, Array.Empty<CombatUnitPresentationTargetResult>(), path);
            Assert.Throws<InvalidOperationException>(() => provider.Present(new CombatUnitPresentationEventProjection(
                "player", CombatUnitPresentationEvent.Move, actor.Position, path[2], 0,
                Array.Empty<CombatUnitPresentationTargetResult>(), new[] { path[2] })));
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(actor.Position), root.transform.position);
            provider.Present(projection);
            Assert.IsTrue(provider.IsPresenting);
            Assert.Throws<InvalidOperationException>(() => provider.Present(projection));
            Vector3 previous = root.transform.position;
            int samples = 0;
            for (float deadline = Time.realtimeSinceStartup + 10f; Time.realtimeSinceStartup < deadline && provider.IsPresenting;)
            {
                yield return null;
                Vector3 current = root.transform.position;
                float distanceToPath = float.PositiveInfinity;
                Vector3 start = Static3DCombatUnitPresentationProvider.HexToWorld(actor.Position);
                foreach (CombatUnitPresentationHex cell in path)
                {
                    Vector3 end = Static3DCombatUnitPresentationProvider.HexToWorld(cell);
                    float t = Mathf.Clamp01(Vector3.Dot(current - start, end - start) / (end - start).sqrMagnitude);
                    distanceToPath = Mathf.Min(distanceToPath, Vector3.Distance(current, Vector3.Lerp(start, end, t)));
                    start = end;
                }
                Assert.Less(distanceToPath, 0.0001f, "The root must stay on adjacent path segments.");
                Assert.LessOrEqual(Vector3.Distance(previous, current), Time.deltaTime * Static3DCombatUnitPresentationProvider.MovementCellsPerSecond + 0.001f);
                AssertGroundedBody(root, FindBody(root), Vector3.one);
                if (Vector3.Distance(previous, current) > 0) samples++;
                previous = current;
            }
            Assert.Greater(samples, 2);
            Assert.IsFalse(provider.IsPresenting);
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(path[2]), root.transform.position);
            AssertYaw(root, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[0]);
            AssertGroundedBody(root, FindBody(root), Vector3.one);
            Assert.Throws<InvalidOperationException>(() => provider.Present(projection));
            provider.Prepare(new[] { actor });
            provider.Present(projection);
            yield return null;
            provider.Clear();
            provider.Prepare(new[] { actor });
            Assert.IsTrue(provider.TryGetPresentedUnit("player", out GameObject replacement));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(actor.Position), replacement.transform.position);
            AssertGroundedBody(replacement, FindBody(replacement), Vector3.one);
            Assert.IsFalse(provider.IsPresenting);
            provider.Present(projection);
            host.SetActive(false);
            Assert.IsFalse(provider.IsPresenting);
            Assert.AreEqual(0, provider.ActiveCombatantCount);
            UnityEngine.Object.Destroy(host);
            yield return null;
        }

        [Test]
        public void DoubleCellPoseContractPreservesBothShortestHalfTurnsAndRejectsLongTurns()
        {
            var from = new CombatUnitPresentationPose(new CombatUnitPresentationHex(2, 2), 0, true);
            var to = new CombatUnitPresentationPose(new CombatUnitPresentationHex(1, 2), 3, true);
            Assert.AreEqual(2, from.OccupiedCells.Count);
            Assert.IsTrue(from.Occupies(from.Position));
            Assert.IsTrue(from.Occupies(from.Rear));
            Assert.AreEqual(to.Position, from.Rear);
            var clockwise = new CombatUnitPresentationMovementStep(from, to, new[] { 1, 2, 3 });
            var counterclockwise = new CombatUnitPresentationMovementStep(from, to, new[] { 5, 4, 3 });
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, clockwise.TurnFacings);
            CollectionAssert.AreEqual(new[] { 5, 4, 3 }, counterclockwise.TurnFacings);
            Assert.AreEqual(clockwise, new CombatUnitPresentationMovementStep(from, to, new[] { 1, 2, 3 }));
            Assert.AreNotEqual(clockwise, counterclockwise, "Opposite half-turn routes must invalidate a stale quote.");
            var candidate = new CombatActionCandidate(CombatActionKind.Move, "enemy", null, null, 1,
                destination: to.Position, destinationFacing: to.Facing, movementSteps: new[] { clockwise });
            Assert.AreEqual(3, candidate.DestinationFacing);
            Assert.AreEqual(clockwise, candidate.MovementSteps[0]);
            Assert.Throws<ArgumentException>(() => new CombatUnitPresentationMovementStep(from, to));
            Assert.Throws<ArgumentException>(() => new CombatUnitPresentationMovementStep(from, to, new[] { 1, 0, 3 }));
            Assert.Throws<ArgumentException>(() => new CombatUnitPresentationMovementStep(from,
                new CombatUnitPresentationPose(new CombatUnitPresentationHex(3, 1), 1, true), new[] { 5, 4, 3, 2, 1 }));
            Assert.Throws<ArgumentException>(() => new CombatUnitPresentationTargetResult("enemy", 1, false,
                sourceCell: from.Position));
            var contact = new CombatUnitPresentationTargetResult("enemy", 1, false,
                sourceCell: from.Position, targetCell: to.Position);
            Assert.AreEqual(to.Position, contact.TargetCell);
        }

        [UnityTest]
        public IEnumerator DoubleCellProfilesCenterOnBothCellsAndKeepBodyFacingDuringFeedback()
        {
            var host = new GameObject("DoubleCellProfilesTest");
            var provider = host.AddComponent<Static3DCombatUnitPresentationProvider>();
            var profiles = LoadProfileSet();
            SetPrivateField(provider, "profileSet", profiles);
            foreach (string profileId in new[] { Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                Static3DCombatUnitPresentationProfileSet.LieshijiaProfileId })
            {
                Assert.IsTrue(profiles.TryGetProfile(profileId, out var profile));
                float scale = Mathf.Min(1.8f / (profile.approvedBoundsMax.z - profile.approvedBoundsMin.z),
                    .9f / (profile.approvedBoundsMax.x - profile.approvedBoundsMin.x));
                for (int facing = 0; facing < 6; facing++)
                {
                    var descriptor = new CombatUnitPresentationDescriptor("enemy", profileId,
                        CombatUnitDisplayFaction.Enemy, new CombatUnitPresentationHex(3, 3), facing, true);
                    provider.Prepare(new[] { descriptor });
                    Assert.IsTrue(provider.TryGetPresentedUnit("enemy", out GameObject root));
                    Assert.IsTrue(provider.TryGetPresentedPose("enemy", out var pose));
                    Assert.AreEqual(descriptor.Pose, pose);
                    Assert.Less(Vector3.Distance(Static3DCombatUnitPresentationProvider.PoseToWorld(pose),
                        root.transform.position), .00001f);
                    Assert.Less(Vector3.Distance(root.transform.forward,
                        (Static3DCombatUnitPresentationProvider.HexToWorld(pose.Position) -
                         Static3DCombatUnitPresentationProvider.HexToWorld(pose.Rear)).normalized), .00001f);
                    Assert.Less(Vector3.Distance(Vector3.one * scale, FindBody(root).localScale), .00001f);
                    foreach (CombatUnitPresentationEvent action in new[] { CombatUnitPresentationEvent.Attack,
                        CombatUnitPresentationEvent.Cast, CombatUnitPresentationEvent.Hit })
                    {
                        provider.Present(new CombatUnitPresentationEventProjection("enemy", action, pose.Position,
                            pose.Position, (facing + 3) % 6, new[] { new CombatUnitPresentationTargetResult(
                                "enemy", 1, false, sourceCell: new CombatUnitPresentationHex(0, 0), targetCell: pose.Rear) }));
                        AssertYaw(root, profile.sixDirectionYawDegrees[facing]);
                        Assert.AreEqual(.34f, FindBody(root).GetComponentInChildren<MeshRenderer>().bounds.min.y, .001f);
                        Assert.IsTrue(provider.TryGetPresentedPose("enemy", out var after));
                        Assert.AreEqual(pose, after, "Feedback cannot rotate the committed footprint.");
                        if (action == CombatUnitPresentationEvent.Hit)
                        {
                            var feedback = (HashSet<GameObject>)typeof(Static3DCombatUnitPresentationProvider)
                                .GetField("transientFeedback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(provider);
                            int impacts = 0;
                            foreach (GameObject effect in feedback)
                                if (effect != null && effect.name == "GuanzhongCombatFeedback_Hit_Vfx")
                                {
                                    Assert.Less(Vector3.Distance(effect.transform.position,
                                        Static3DCombatUnitPresentationProvider.HexToWorld(pose.Rear) + Vector3.up * .22f), .001f);
                                    impacts++;
                                }
                            Assert.AreEqual(1, impacts, "A hit uses the selected rear contact cell once.");
                        }
                        yield return new WaitForSeconds(.25f);
                    }
                    Assert.AreEqual(Vector3.one, root.transform.localScale);
                    provider.Clear();
                    yield return null;
                }
            }
            UnityEngine.Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DoubleCellMoveFollowsChosenHalfTurnAroundFrontBeforeAdvancing()
        {
            var host = new GameObject("DoubleCellMoveTest");
            var provider = host.AddComponent<Static3DCombatUnitPresentationProvider>();
            SetPrivateField(provider, "profileSet", LoadProfileSet());
            foreach (int[] turns in new[] { new[] { 1, 2, 3 }, new[] { 5, 4, 3 } })
            {
                var descriptor = new CombatUnitPresentationDescriptor("enemy",
                    Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId, CombatUnitDisplayFaction.Enemy,
                    new CombatUnitPresentationHex(2, 2), 0, true);
                var destination = new CombatUnitPresentationPose(new CombatUnitPresentationHex(1, 2), 3, true);
                provider.Prepare(new[] { descriptor });
                Assert.IsTrue(provider.TryGetPresentedUnit("enemy", out GameObject root));
                var step = new CombatUnitPresentationMovementStep(descriptor.Pose, destination, turns);
                Assert.Throws<InvalidOperationException>(() => provider.Present(new CombatUnitPresentationEventProjection(
                    "enemy", CombatUnitPresentationEvent.Move, descriptor.Position, destination.Position, 3, null,
                    new[] { destination.Position })));
                provider.Present(new CombatUnitPresentationEventProjection("enemy", CombatUnitPresentationEvent.Move,
                    descriptor.Position, destination.Position, 3, null, new[] { destination.Position }, movementSteps: new[] { step }));
                Vector3 front = Static3DCombatUnitPresentationProvider.HexToWorld(descriptor.Position);
                float turned = 0f;
                float previousYaw = root.transform.eulerAngles.y;
                int rotationSamples = 0;
                while (provider.IsPresenting)
                {
                    yield return null;
                    float yaw = root.transform.eulerAngles.y;
                    float change = Mathf.DeltaAngle(previousYaw, yaw);
                    if (Mathf.Abs(change) > .001f)
                    {
                        Assert.AreEqual(turns[0] == 1 ? 1 : -1, Math.Sign(change), "Must preserve the authority's chosen 180-degree route.");
                        turned += Mathf.Abs(change);
                        Assert.Less(Vector3.Distance(front, root.transform.position + root.transform.forward * .5f), .001f,
                            "The front cell stays fixed throughout each sixty-degree turn.");
                        rotationSamples++;
                    }
                    previousYaw = yaw;
                    Assert.AreEqual(.34f, root.transform.position.y, .0001f);
                    Assert.AreEqual(.34f, FindBody(root).GetComponentInChildren<MeshRenderer>().bounds.min.y, .001f);
                }
                Assert.Greater(rotationSamples, 2);
                Assert.AreEqual(180f, turned, .01f);
                Assert.IsTrue(provider.TryGetPresentedPose("enemy", out var actual));
                Assert.AreEqual(destination, actual);
                Assert.Less(Vector3.Distance(Static3DCombatUnitPresentationProvider.PoseToWorld(destination), root.transform.position), .0001f);
                provider.Clear();
                yield return null;
            }
            UnityEngine.Object.Destroy(host);
            yield return null;
        }

        private static void PresentAndAssertAction(
            Static3DCombatUnitPresentationProvider provider,
            CombatUnitPresentationDescriptor actor,
            CombatUnitPresentationDescriptor target,
            GameObject root,
            Transform body,
            int facing,
            Vector3 expectedRootScale)
        {
            provider.Present(new CombatUnitPresentationEventProjection(
                actor.CombatantId, CombatUnitPresentationEvent.Attack, actor.Position, actor.Position, facing,
                new[] { new CombatUnitPresentationTargetResult(target.CombatantId, 17, false) }));
            AssertGroundedBody(root, body, expectedRootScale);
            AssertYaw(root, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[facing]);
        }

        private static Transform FindBody(GameObject root)
        {
            Assert.AreEqual(1, root.transform.childCount,
                "The presented unit must contain only the approved static body.");
            Transform body = root.transform.GetChild(0);
            Assert.IsNotNull(body.GetComponentInChildren<MeshRenderer>(true));
            return body;
        }

        /// <summary>
        /// The approved model is authored with its foot contact at local Y=0. Its parent root is
        /// already on the tile support plane, so feet, root and plane share one height in idle,
        /// six directions, movement and every temporary action scale; there is no base geometry.
        /// </summary>
        private static void AssertGroundedBody(GameObject root, Transform body, Vector3 expectedRootScale)
        {
            Assert.Less(Vector3.Distance(expectedRootScale, root.transform.localScale), 0.00001f);
            Assert.Less(Vector3.Distance(expectedRootScale, root.transform.lossyScale), 0.00001f);
            Assert.Less(Vector3.Distance(BodyScale, body.localScale), 0.00001f);
            Assert.Less(Vector3.Distance(Vector3.Scale(expectedRootScale, BodyScale), body.lossyScale), 0.00001f);
            Assert.AreEqual(Vector3.zero, body.localPosition);
            Assert.IsNull(root.transform.Find("Static3DCombatBase"), "The formal unit must not carry a physical base.");
            Assert.Zero(root.GetComponentsInChildren<LineRenderer>(true).Length,
                "The old base selection ring must not be recreated.");
            MeshRenderer renderer = body.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(renderer);
            Assert.AreEqual(0.34f, root.transform.position.y, 0.0001f);
            Assert.AreEqual(0.34f, renderer.bounds.min.y, 0.001f,
                "Feet must stay on the measured tile support plane through root action scaling.");
            TestContext.WriteLine($"grounding:{root.name}:rootY={root.transform.position.y:F5},rootScale={expectedRootScale}," +
                $"bodyLocalY={body.localPosition.y:F5},bodyScale={body.localScale},footY={renderer.bounds.min.y:F5}," +
                $"topY={renderer.bounds.max.y:F5},base=none,children={root.transform.childCount}");
        }

        private static void AssertYaw(GameObject root, float expectedYaw)
        {
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(expectedYaw, root.transform.eulerAngles.y)), 0.001f);
        }

        private static Static3DCombatUnitPresentationProfileSet LoadProfileSet()
        {
            Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            Assert.IsNotNull(assetDatabaseType, "PlayMode validation requires the editor asset database.");
            MethodInfo loadAsset = assetDatabaseType.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(Type) });
            Assert.IsNotNull(loadAsset);
            var profileSet = loadAsset.Invoke(null, new object[]
            {
                ProfileSetPath,
                typeof(Static3DCombatUnitPresentationProfileSet),
            }) as Static3DCombatUnitPresentationProfileSet;
            Assert.IsNotNull(profileSet, ProfileSetPath);
            return profileSet;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }
    }
}
