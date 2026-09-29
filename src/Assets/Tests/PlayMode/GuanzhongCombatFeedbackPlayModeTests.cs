using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class GuanzhongCombatFeedbackPlayModeTests
    {
        private const string ProfileSetPath =
            "Assets/Data/CombatPresentationProfiles/Static3DCombatUnitPresentationProfileSet.asset";

        [UnityTest]
        public IEnumerator ApprovedProfilesShareFiveCuesAndProjectAllSixEventsWithoutResiduals()
        {
            Static3DCombatUnitPresentationProfileSet profileSet = LoadProfileSet();
            Assert.IsTrue(profileSet.TryGetProfile(
                Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                out Static3DCombatUnitPresentationProfile playerProfile));
            Assert.IsTrue(profileSet.TryGetProfile(
                Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                out Static3DCombatUnitPresentationProfile shijiahouProfile));
            Assert.AreSame(playerProfile.combatFeedback.vfxPrefab, shijiahouProfile.combatFeedback.vfxPrefab);
            Assert.AreSame(playerProfile.combatFeedback.moveCue, shijiahouProfile.combatFeedback.moveCue);
            Assert.AreSame(playerProfile.combatFeedback.attackCue, shijiahouProfile.combatFeedback.attackCue);
            Assert.AreSame(playerProfile.combatFeedback.hitCue, shijiahouProfile.combatFeedback.hitCue);
            Assert.AreSame(playerProfile.combatFeedback.castCue, shijiahouProfile.combatFeedback.castCue);
            Assert.AreSame(playerProfile.combatFeedback.deathCue, shijiahouProfile.combatFeedback.deathCue);
            Assert.Less(playerProfile.combatFeedback.intensity, shijiahouProfile.combatFeedback.intensity);
            Assert.Less(playerProfile.combatFeedback.actionRecoverySeconds, shijiahouProfile.combatFeedback.actionRecoverySeconds);

            var host = new GameObject("GuanzhongCombatFeedbackTest");
            host.SetActive(false);
            Static3DCombatUnitPresentationProvider provider =
                host.AddComponent<Static3DCombatUnitPresentationProvider>();
            SetPrivateField(provider, "profileSet", profileSet);
            host.SetActive(true);
            var listener = new GameObject("GuanzhongCombatFeedbackAudioListener");
            listener.AddComponent<AudioListener>();
            yield return null;

            var player = new CombatUnitPresentationDescriptor(
                "player", Static3DCombatUnitPresentationProfileSet.PlayerProfileId,
                CombatUnitDisplayFaction.Player, new CombatUnitPresentationHex(0, 0), 0);
            var enemy = new CombatUnitPresentationDescriptor(
                "enemy", Static3DCombatUnitPresentationProfileSet.ShijiahouProfileId,
                CombatUnitDisplayFaction.Enemy, new CombatUnitPresentationHex(1, 0), 3);
            provider.Prepare(new[] { player, enemy });
            Assert.IsTrue(provider.TryGetPresentedUnit(player.CombatantId, out GameObject playerRoot));

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
            }

            yield return null;
            Assert.GreaterOrEqual(FindFeedbackParticles().Length, 3,
                "Attack, Hit, and Cast must each create one detached feedback burst.");
            yield return new WaitForSeconds(playerProfile.combatFeedback.actionRecoverySeconds + 0.05f);
            Assert.AreEqual(Vector3.one, playerRoot.transform.localScale);

            provider.Present(new CombatUnitPresentationEventProjection(
                enemy.CombatantId,
                CombatUnitPresentationEvent.Death,
                enemy.Position,
                enemy.Position,
                3,
                new[] { new CombatUnitPresentationTargetResult(enemy.CombatantId, 19, true) }));
            yield return null;
            Assert.IsTrue(provider.TryGetPresentedUnit(enemy.CombatantId, out GameObject enemyRoot));
            Assert.IsFalse(enemyRoot.activeSelf);
            Assert.AreEqual(1, FindFeedbackParticles().Count(particle =>
                particle.name.StartsWith("GuanzhongCombatFeedback_Death", System.StringComparison.Ordinal)));

            yield return new WaitForSeconds(1.05f);
            Assert.Zero(FindFeedbackParticles().Length, "Short-lived effects must clean themselves after playback.");
            provider.Clear();
            Assert.Zero(provider.ActiveCombatantCount);
            Object.Destroy(listener);
            Object.Destroy(host);
            yield return null;
        }

        private static ParticleSystem[] FindFeedbackParticles()
        {
            return Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(particle => particle.name.StartsWith("GuanzhongCombatFeedback_", System.StringComparison.Ordinal))
                .ToArray();
        }

        private static Static3DCombatUnitPresentationProfileSet LoadProfileSet()
        {
            System.Type assetDatabaseType = System.Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            Assert.IsNotNull(assetDatabaseType, "PlayMode validation requires the editor asset database.");
            MethodInfo loadAsset = assetDatabaseType.GetMethod("LoadAssetAtPath", new[] { typeof(string), typeof(System.Type) });
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
