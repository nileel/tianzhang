using System.Collections;
using NUnit.Framework;
using TianZhang.Features.CombatPresentation;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TianZhang.Tests.PlayMode
{
    public sealed class BattleVisualComparisonPlayModeTests
    {
        [UnityTest]
        public IEnumerator ComparisonButtonsSwitchSymmetricRoutesAndReplayEverySharedEvent()
        {
            Screen.SetResolution(1920, 1080, false);
            LogAssert.Expect(LogType.Error, "[AdventureInstaller] game_bootstrap_missing");
            SceneManager.LoadScene("AdventureScene");
            yield return null;

            GameObject board = GameObject.Find(TacticalSpriteProbeMatrix.BoardName);
            Assert.IsNotNull(board);
            BattleVisualComparisonController comparison = board.GetComponent<BattleVisualComparisonController>();
            Assert.IsNotNull(comparison);
            Assert.IsFalse(comparison.IsBattleAnimation2DRouteActive);
            AssertRoute(board, false);

            Button[] routeButtons =
            {
                FindButton("Comparison2DRouteButton"),
                FindButton("ComparisonStatic3DRouteButton"),
            };
            Button[] eventButtons =
            {
                FindButton("ComparisonEventButton_0"), FindButton("ComparisonEventButton_1"),
                FindButton("ComparisonEventButton_2"), FindButton("ComparisonEventButton_3"),
                FindButton("ComparisonEventButton_4"), FindButton("ComparisonEventButton_5"),
            };
            Button reset = FindButton("ComparisonResetButton");

            for (int route = 0; route < routeButtons.Length; route++)
            {
                routeButtons[route].onClick.Invoke();
                Assert.AreEqual(route == 0, comparison.IsBattleAnimation2DRouteActive);
                AssertRoute(board, route == 0);

                for (int direction = 0; direction < BattleVisualComparisonController.DirectionCount; direction++)
                {
                    FindButton("ComparisonDirectionButton_" + direction).onClick.Invoke();
                    Assert.AreEqual(direction, comparison.SelectedDirection);

                    for (int eventIndex = 0; eventIndex < eventButtons.Length; eventIndex++)
                    {
                        StaticChessPresentationEvent presentationEvent = (StaticChessPresentationEvent)eventIndex;
                        eventButtons[eventIndex].onClick.Invoke();
                        Assert.AreEqual(presentationEvent, comparison.LastPresentationEvent);
                        AssertActiveEvent(board, route == 0, direction, presentationEvent);

                        reset.onClick.Invoke();
                        Assert.AreEqual(StaticChessPresentationEvent.Idle, comparison.LastPresentationEvent);
                        AssertAllProbesReset(board);
                    }
                }
            }
        }

        private static Button FindButton(string name)
        {
            GameObject button = GameObject.Find(name);
            Assert.IsNotNull(button, "AdventureScene is missing comparison button " + name + ".");
            return button.GetComponent<Button>();
        }

        private static void AssertRoute(GameObject board, bool battleAnimation2D)
        {
            Assert.AreEqual(battleAnimation2D,
                board.transform.Find(BattleAnimationSpriteProbeMatrix.GroupName).gameObject.activeInHierarchy);
            Assert.IsFalse(board.transform.Find(TacticalSpriteProbeMatrix.GroupName).gameObject.activeInHierarchy);
            for (int direction = 0; direction < BattleVisualComparisonController.DirectionCount; direction++)
                Assert.AreEqual(!battleAnimation2D,
                    board.transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction).gameObject.activeInHierarchy);
        }

        private static void AssertActiveEvent(GameObject board, bool battleAnimation2D, int direction,
            StaticChessPresentationEvent presentationEvent)
        {
            if (battleAnimation2D)
            {
                BattleAnimationSpritePresentationController controller = board.transform.Find(
                        BattleAnimationSpriteProbeMatrix.GroupName + "/" + BattleAnimationSpriteProbeMatrix.ProbePrefix + direction)
                    .GetComponent<BattleAnimationSpritePresentationController>();
                Assert.AreEqual(presentationEvent, controller.ActiveEvent);
                Assert.AreEqual(presentationEvent != StaticChessPresentationEvent.Idle, controller.IsPresenting);
                return;
            }

            StaticChessPresentationController staticController = board.transform
                .Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction)
                .GetComponent<StaticChessPresentationController>();
            Assert.AreEqual(presentationEvent, staticController.ActiveEvent);
            Assert.AreEqual(presentationEvent != StaticChessPresentationEvent.Idle, staticController.IsPresenting);
        }

        private static void AssertAllProbesReset(GameObject board)
        {
            for (int direction = 0; direction < BattleVisualComparisonController.DirectionCount; direction++)
            {
                StaticChessPresentationController staticController = board.transform
                    .Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction)
                    .GetComponent<StaticChessPresentationController>();
                BattleAnimationSpritePresentationController dynamicController = board.transform.Find(
                        BattleAnimationSpriteProbeMatrix.GroupName + "/" + BattleAnimationSpriteProbeMatrix.ProbePrefix + direction)
                    .GetComponent<BattleAnimationSpritePresentationController>();
                Assert.IsFalse(staticController.IsPresenting);
                Assert.AreEqual(StaticChessPresentationEvent.Idle, staticController.ActiveEvent);
                Assert.IsFalse(dynamicController.IsPresenting);
                Assert.AreEqual(StaticChessPresentationEvent.Idle, dynamicController.ActiveEvent);
            }
        }
    }
}
