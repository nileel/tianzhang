using System;
using TianZhang.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// 只为 VisualBaselineBoard 编排苻渊 2D 动态与静态 3D 样例的同条件比较输入。
    /// 它不拥有战斗规则、朝向、格位或资产选择；按钮只切换已冻结的隔离路线，并把同一
    /// CombatUnitPresentationEvent 交给当前路线中选定方向的既有表现控制器。
    /// </summary>
    public sealed class BattleVisualComparisonController : MonoBehaviour
    {
        public const int DirectionCount = 6;

        private enum ComparisonRoute
        {
            Static3D,
            BattleAnimation2D,
        }

        [SerializeField] private Text statusText;

        private ComparisonRoute activeRoute = ComparisonRoute.Static3D;
        private int selectedDirection;
        private CombatUnitPresentationEvent lastPresentationEvent = CombatUnitPresentationEvent.Idle;
        private bool is2DOverallMotionEnabled;

        public bool IsBattleAnimation2DRouteActive => activeRoute == ComparisonRoute.BattleAnimation2D;

        public bool Is2DOverallMotionEnabled => is2DOverallMotionEnabled;

        public int SelectedDirection => selectedDirection;

        public CombatUnitPresentationEvent LastPresentationEvent => lastPresentationEvent;

        public void Configure(Text status)
        {
            statusText = status;
        }

        private void Awake()
        {
            ValidateConfiguration();
            Apply2DMotionMode(false);
            SelectStatic3DRoute();
        }

        public void SelectBattleAnimation2DRoute()
        {
            SelectRoute(ComparisonRoute.BattleAnimation2D);
        }

        public void SelectStatic3DRoute()
        {
            SelectRoute(ComparisonRoute.Static3D);
        }

        public void SelectPureFrame2DMode()
        {
            Select2DMotionMode(false);
        }

        public void SelectOverall2DMotionMode()
        {
            Select2DMotionMode(true);
        }

        public void SelectDirection(int direction)
        {
            if (direction < 0 || direction >= DirectionCount)
                throw new ArgumentOutOfRangeException(nameof(direction));
            selectedDirection = direction;
            UpdateStatus();
        }

        public void TriggerPresentation(CombatUnitPresentationEvent presentationEvent)
        {
            ResetPresentations();
            lastPresentationEvent = presentationEvent;
            if (presentationEvent != CombatUnitPresentationEvent.Idle)
            {
                if (activeRoute == ComparisonRoute.BattleAnimation2D)
                {
                    BattleAnimationSpritePresentationController controller = BattleAnimation2DProbe(selectedDirection);
                    controller.StartPresentation(presentationEvent, ApprovedPosition(controller.transform));
                }
                else
                {
                    StaticChessPresentationController controller = Static3DProbe(selectedDirection);
                    controller.StartPresentation(presentationEvent, ApprovedPosition(controller.transform));
                }
            }
            UpdateStatus();
        }

        public void TriggerPresentationByIndex(int presentationEvent)
        {
            if (presentationEvent < (int)CombatUnitPresentationEvent.Idle ||
                presentationEvent > (int)CombatUnitPresentationEvent.Death)
                throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            TriggerPresentation((CombatUnitPresentationEvent)presentationEvent);
        }

        public void ResetPresentations()
        {
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                StaticChessPresentationController staticController = Static3DProbe(direction);
                staticController.StartPresentation(CombatUnitPresentationEvent.Idle, staticController.transform.position);
                BattleAnimationSpritePresentationController dynamicController = BattleAnimation2DProbe(direction);
                dynamicController.StartPresentation(CombatUnitPresentationEvent.Idle, dynamicController.transform.position);
            }
            lastPresentationEvent = CombatUnitPresentationEvent.Idle;
            UpdateStatus();
        }

        private void SelectRoute(ComparisonRoute route)
        {
            ResetPresentations();
            activeRoute = route;
            BattleAnimation2DGroup().SetActive(route == ComparisonRoute.BattleAnimation2D);
            LegacyTactical2DGroup().SetActive(false);
            for (int direction = 0; direction < DirectionCount; direction++)
                Static3DProbe(direction).gameObject.SetActive(route == ComparisonRoute.Static3D);
            UpdateStatus();
        }

        private void Select2DMotionMode(bool overallMotionEnabled)
        {
            ResetPresentations();
            Apply2DMotionMode(overallMotionEnabled);
            UpdateStatus();
        }

        private void Apply2DMotionMode(bool overallMotionEnabled)
        {
            is2DOverallMotionEnabled = overallMotionEnabled;
            for (int direction = 0; direction < DirectionCount; direction++)
                BattleAnimation2DProbe(direction).SetRootPresentationEnabled(overallMotionEnabled);
        }

        private void ValidateConfiguration()
        {
            if (statusText == null)
                throw new InvalidOperationException("Battle visual comparison entry is missing its status reference.");
            BattleAnimation2DGroup();
            LegacyTactical2DGroup();
            for (int direction = 0; direction < DirectionCount; direction++)
            {
                Static3DProbe(direction);
                BattleAnimation2DProbe(direction);
            }
        }

        private GameObject BattleAnimation2DGroup()
        {
            Transform group = transform.Find(BattleAnimationSpriteProbeMatrix.GroupName);
            if (group == null) throw new InvalidOperationException("Battle visual comparison entry is missing the 2D route group.");
            return group.gameObject;
        }

        private GameObject LegacyTactical2DGroup()
        {
            Transform group = transform.Find(TacticalSpriteProbeMatrix.GroupName);
            if (group == null) throw new InvalidOperationException("Battle visual comparison entry is missing the legacy 2D route group.");
            return group.gameObject;
        }

        private StaticChessPresentationController Static3DProbe(int direction)
        {
            Transform probe = transform.Find(TacticalSpriteProbeMatrix.FacingProbePrefix + direction);
            StaticChessPresentationController controller = probe == null ? null : probe.GetComponent<StaticChessPresentationController>();
            if (controller == null)
                throw new InvalidOperationException("Battle visual comparison entry is missing the static 3D probe for direction " + direction + ".");
            return controller;
        }

        private BattleAnimationSpritePresentationController BattleAnimation2DProbe(int direction)
        {
            Transform probe = transform.Find(BattleAnimationSpriteProbeMatrix.GroupName + "/" +
                BattleAnimationSpriteProbeMatrix.ProbePrefix + direction);
            BattleAnimationSpritePresentationController controller = probe == null
                ? null
                : probe.GetComponent<BattleAnimationSpritePresentationController>();
            if (controller == null)
                throw new InvalidOperationException("Battle visual comparison entry is missing the 2D probe for direction " + direction + ".");
            return controller;
        }

        private static Vector3 ApprovedPosition(Transform probe) =>
            probe.position + probe.rotation * Vector3.forward * 0.2f;

        private void UpdateStatus()
        {
            if (statusText == null) return;
            statusText.text = "路线：" +
                (activeRoute == ComparisonRoute.BattleAnimation2D ? "2D 动态战斗样例" : "静态 3D 动态样例") +
                "\n2D 模式：" + (is2DOverallMotionEnabled ? "整体动效" : "纯帧动画") +
                "\n方向：" + selectedDirection + "　事件：" + EventLabel(lastPresentationEvent) +
                "\n同一相机、光照、地块与规则；仅供用户实机比较。";
        }

        private static string EventLabel(CombatUnitPresentationEvent presentationEvent)
        {
            switch (presentationEvent)
            {
                case CombatUnitPresentationEvent.Idle: return "待机";
                case CombatUnitPresentationEvent.Move: return "移动";
                case CombatUnitPresentationEvent.Attack: return "攻击";
                case CombatUnitPresentationEvent.Hit: return "受击";
                case CombatUnitPresentationEvent.Cast: return "施法";
                case CombatUnitPresentationEvent.Death: return "死亡";
                default: throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            }
        }
    }
}
