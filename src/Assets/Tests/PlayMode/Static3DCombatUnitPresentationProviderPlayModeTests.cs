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

            foreach (CombatUnitPresentationEvent presentationEvent in new[]
                     {
                         CombatUnitPresentationEvent.Idle,
                         CombatUnitPresentationEvent.Move,
                         CombatUnitPresentationEvent.Attack,
                         CombatUnitPresentationEvent.Hit,
                         CombatUnitPresentationEvent.Cast,
                     })
            {
                CombatUnitPresentationHex end = presentationEvent == CombatUnitPresentationEvent.Move
                    ? new CombatUnitPresentationHex(1, -1)
                    : player.Position;
                provider.Present(new CombatUnitPresentationEventProjection(
                    player.CombatantId,
                    presentationEvent,
                    player.Position,
                    end,
                    1,
                    new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 17, false) }));
                Assert.IsTrue(provider.TryGetLastEvent(player.CombatantId, out CombatUnitPresentationEvent recorded));
                Assert.AreEqual(presentationEvent, recorded);
            }

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
