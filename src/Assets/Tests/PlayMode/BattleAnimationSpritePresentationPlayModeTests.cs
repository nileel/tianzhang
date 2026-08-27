using System;
using System.Collections;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TianZhang.Tests.PlayMode
{
    public sealed class BattleAnimationSpritePresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleAnimationRouteIsExplicitAndMutuallyExclusive()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            GameObject board = GameObject.Find(BattleAnimationSpriteProbeMatrix.BoardName);
            Assert.IsNotNull(board);
            Transform battleGroup = board.transform.Find(BattleAnimationSpriteProbeMatrix.GroupName);
            Assert.IsNotNull(battleGroup);
            Assert.IsFalse(battleGroup.gameObject.activeInHierarchy);
            Assert.IsFalse(board.transform.Find(TacticalSpriteProbeMatrix.GroupName).gameObject.activeInHierarchy);
            for (int direction = 0; direction < 6; direction++)
                Assert.IsTrue(board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction).gameObject.activeInHierarchy);

            BattleAnimationSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            Assert.IsTrue(battleGroup.gameObject.activeInHierarchy);
            Assert.IsFalse(board.transform.Find(TacticalSpriteProbeMatrix.GroupName).gameObject.activeInHierarchy);
            for (int direction = 0; direction < 6; direction++)
                Assert.IsFalse(board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction).gameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator EveryStateDirectionAndManifestEventFramePlaysThenResetsWithoutRuleOwners()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;
            BattleAnimationSpriteProbeMatrix.SetActiveRoute(true);
            yield return null;

            foreach (CombatUnitPresentationEvent presentationEvent in
                     (CombatUnitPresentationEvent[])Enum.GetValues(typeof(CombatUnitPresentationEvent)))
            for (int direction = 0; direction < 6; direction++)
            {
                GameObject probe = GameObject.Find(BattleAnimationSpriteProbeMatrix.ProbePrefix + direction);
                Assert.IsNotNull(probe);
                Assert.Zero(probe.GetComponentsInChildren<Animator>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<Animation>(true).Length);
                Assert.Zero(probe.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length);
                BattleAnimationSpritePresentationController controller =
                    probe.GetComponent<BattleAnimationSpritePresentationController>();
                Assert.IsNotNull(controller);

                Vector3 restPosition = probe.transform.position;
                Quaternion restRotation = probe.transform.rotation;
                int castSignals = 0;
                Action onCast = () => castSignals++;
                controller.CastEffectRequested += onCast;
                try
                {
                    controller.StartPresentation(presentationEvent,
                        restPosition + restRotation * Vector3.forward * 0.2f);
                    Assert.AreEqual(0, controller.ActiveFrameIndex);
                    Assert.AreEqual(ManifestEventFrame(presentationEvent), controller.EventFrameIndex);
                    Assert.AreEqual(SpriteName(
                        StateIndex(presentationEvent), direction, 0), controller.ActiveSpriteName);

                    if (presentationEvent == CombatUnitPresentationEvent.Idle)
                    {
                        Assert.IsFalse(controller.IsPresenting);
                        Assert.AreEqual(0, castSignals);
                        continue;
                    }

                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    Assert.AreEqual(1, controller.ActiveFrameIndex);
                    Assert.AreEqual(SpriteName(
                        StateIndex(presentationEvent), direction, 1), controller.ActiveSpriteName);
                    Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Cast ? 1 : 0, castSignals);

                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    Assert.AreEqual(2, controller.ActiveFrameIndex);
                    Assert.AreEqual(SpriteName(
                        StateIndex(presentationEvent), direction, 2), controller.ActiveSpriteName);
                    Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Cast ? 1 : 0, castSignals);

                    controller.Tick(BattleAnimationSpritePresentationController.FrameDuration + 0.01f);
                    Assert.IsFalse(controller.IsPresenting);
                    Assert.AreEqual(CombatUnitPresentationEvent.Idle, controller.ActiveEvent);
                    Assert.AreEqual(0, controller.ActiveFrameIndex);
                    Assert.AreEqual(SpriteName(0, direction, 0), controller.ActiveSpriteName);
                    Assert.Less(Vector3.Distance(restPosition, probe.transform.position), 0.0001f);
                    Assert.Less(Quaternion.Angle(restRotation, probe.transform.rotation), 0.001f);
                    Assert.AreEqual(presentationEvent == CombatUnitPresentationEvent.Cast ? 1 : 0, castSignals);
                }
                finally
                {
                    controller.CastEffectRequested -= onCast;
                }
            }
        }

        private static int StateIndex(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle: return 0;
                case CombatUnitPresentationEvent.Move: return 1;
                case CombatUnitPresentationEvent.Attack: return 2;
                case CombatUnitPresentationEvent.Hit: return 3;
                case CombatUnitPresentationEvent.Cast: return 4;
                case CombatUnitPresentationEvent.Death: return 5;
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }

        private static int ManifestEventFrame(CombatUnitPresentationEvent presentationEvent) =>
            presentationEvent == CombatUnitPresentationEvent.Death ? 2 :
            presentationEvent == CombatUnitPresentationEvent.Idle ? 0 : 1;

        private static string SpriteName(int state, int direction, int frame)
        {
            string stateName = new[] { "Idle", "Move", "Attack", "Hit", "Cast", "Death" }[state];
            return "FuYuan_Battle_" + stateName + "_Direction_" + direction + "_Frame_" + frame;
        }
    }
}
