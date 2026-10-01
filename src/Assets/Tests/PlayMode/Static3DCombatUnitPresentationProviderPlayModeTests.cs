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
                new CombatUnitPresentationHex(1, -1), 2,
                new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 17, false) }));
            provider.Present(new CombatUnitPresentationEventProjection(
                enemy.CombatantId, CombatUnitPresentationEvent.Hit, enemy.Position, enemy.Position, 5,
                new[] { new CombatUnitPresentationTargetResult(player.CombatantId, 19, false) }));
            AssertBodyAndRootScale(playerRoot, playerBody, Vector3.one);
            AssertBodyAndRootScale(enemyRoot, enemyBody, new Vector3(0.94f, 1.06f, 0.94f));
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
            Assert.AreEqual(root.transform.position.y, renderer.bounds.min.y, 0.0001f,
                "The body pivot must keep its foot on GroundY while scaled.");
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
