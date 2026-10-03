using System;
using System.Collections;
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
            Assert.AreEqual(2, playerRoot.GetComponentsInChildren<MeshRenderer>(true).Length,
                "The provider must create the approved static body and its independent faction base.");
            Transform playerBody = FindBody(playerRoot);
            Transform enemyBody = FindBody(enemyRoot);
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, Vector3.one);

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
                AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
                AssertBodyAndRootScale(enemyRoot, enemyBody, Vector3.one);
            }

            PresentAndAssertAction(provider, player, enemy, playerRoot, playerBody, 1,
                new Vector3(1.08f, 0.94f, 1.08f));
            PresentAndAssertAction(provider, enemy, player, enemyRoot, enemyBody, 4,
                new Vector3(1.08f, 0.94f, 1.08f));
            yield return new WaitForSeconds(0.25f);
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, Vector3.one);

            provider.Present(new CombatUnitPresentationEventProjection(
                player.CombatantId, CombatUnitPresentationEvent.Move, player.Position,
                new CombatUnitPresentationHex(1, -1), 1,
                new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 17, false) },
                new[] { new CombatUnitPresentationHex(1, -1) }));
            provider.Present(new CombatUnitPresentationEventProjection(
                enemy.CombatantId, CombatUnitPresentationEvent.Hit, enemy.Position, enemy.Position, 5,
                new[] { new CombatUnitPresentationTargetResult(player.CombatantId, 19, false) }));
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, new Vector3(0.94f, 1.06f, 0.94f));
            yield return new WaitForSeconds(0.25f);
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, Vector3.one);

            while (provider.IsPresenting) yield return null;
            foreach (CombatUnitPresentationDescriptor actor in new[] { player, enemy })
            {
                provider.Present(new CombatUnitPresentationEventProjection(
                    actor.CombatantId, CombatUnitPresentationEvent.Cast, actor.Position, actor.Position, 0,
                    Array.Empty<CombatUnitPresentationTargetResult>()));
                Assert.IsTrue(provider.TryGetPresentedUnit(actor.CombatantId, out GameObject actorRoot));
                AssertBodyAndRootScale(actorRoot, FindBody(actorRoot), new Vector3(1.04f, 1.1f, 1.04f));
            }
            yield return new WaitForSeconds(0.25f);
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, Vector3.one);

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
                AssertBodyAndRootScale(root, FindBody(root), Vector3.one);
                if (Vector3.Distance(previous, current) > 0) samples++;
                previous = current;
            }
            Assert.Greater(samples, 2);
            Assert.IsFalse(provider.IsPresenting);
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(path[2]), root.transform.position);
            AssertYaw(root, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[0]);
            Assert.Throws<InvalidOperationException>(() => provider.Present(projection));
            provider.Prepare(new[] { actor });
            provider.Present(projection);
            yield return null;
            provider.Clear();
            provider.Prepare(new[] { actor });
            Assert.IsTrue(provider.TryGetPresentedUnit("player", out GameObject replacement));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(Static3DCombatUnitPresentationProvider.HexToWorld(actor.Position), replacement.transform.position);
            Assert.IsFalse(provider.IsPresenting);
            provider.Present(projection);
            host.SetActive(false);
            Assert.IsFalse(provider.IsPresenting);
            Assert.AreEqual(0, provider.ActiveCombatantCount);
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
            AssertBodyAndRootScale(root, body, expectedRootScale);
            AssertYaw(root, Static3DCombatUnitPresentationProfileSet.RequiredSixDirectionYawDegrees[facing]);
        }

        private static Transform FindBody(GameObject root)
        {
            for (int index = 0; index < root.transform.childCount; index++)
            {
                Transform child = root.transform.GetChild(index);
                if (child.name != "Static3DCombatBase" && child.GetComponentInChildren<MeshRenderer>(true) != null)
                    return child;
            }

            Assert.Fail("The presented unit is missing its approved static body.");
            return null;
        }

        private static void AssertBodyAndRootScale(GameObject root, Transform body, Vector3 expectedRootScale)
        {
            Assert.Less(Vector3.Distance(expectedRootScale, root.transform.localScale), 0.00001f);
            Assert.Less(Vector3.Distance(expectedRootScale, root.transform.lossyScale), 0.00001f);
            Assert.Less(Vector3.Distance(BodyScale, body.localScale), 0.00001f);
            Assert.Less(Vector3.Distance(Vector3.Scale(expectedRootScale, BodyScale), body.lossyScale), 0.00001f);
            MeshRenderer renderer = body.GetComponentInChildren<MeshRenderer>(true);
            Assert.IsNotNull(renderer);
            MeshRenderer factionBase = root.transform.Find("Static3DCombatBase").GetComponent<MeshRenderer>();
            Assert.AreEqual(0.34f, root.transform.position.y, 0.0001f);
            Assert.AreEqual(root.transform.position.y, factionBase.bounds.min.y, 0.0001f,
                "The base bottom must stay on the battlefield plane during actions.");
            Assert.AreEqual(0.08f * expectedRootScale.y, factionBase.bounds.size.y, 0.0001f);
            Assert.AreEqual(factionBase.bounds.max.y, renderer.bounds.min.y, 0.0001f,
                "The body foot must stay on the base top through root action scaling.");
            TestContext.WriteLine($"contact:{root.name}:rootY={root.transform.position.y:F4},rootScale={expectedRootScale},bodyScale={body.localScale},baseBottom={factionBase.bounds.min.y:F4},baseTop={factionBase.bounds.max.y:F4},foot={renderer.bounds.min.y:F4},thickness={factionBase.bounds.size.y:F4}");
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
