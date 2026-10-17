using System.Linq;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    public sealed class CombatHudPresenter : MonoBehaviour, ICombatPresentationSink, ICombatPresentationActivity
    {
        [SerializeField] private CombatHudView view;
        [SerializeField] private CombatCommandInput commandInput;
        [SerializeField] private CombatLogView logView;
        [SerializeField] private CombatUnitHealthBarView healthBars;

        [SerializeField] private CombatTimelineView timelineView;
        [SerializeField] private GameObject explorationPanel;
        private CombatBountyTrackerView bountyTracker;
        public void ConfigureBountyTracker(CombatBountyTrackerView tracker) => bountyTracker = tracker;
        private CombatTargetView targetView;
        private Static3DCombatUnitPresentationProvider unitProvider;
        private CombatMovementView movementView;
        public CombatHudSnapshot Snapshot { get; private set; }
        public string InspectedTargetId { get; private set; }
        public bool IsPresenting => timelineView != null && timelineView.IsPresenting;
        public bool CanInspect => Snapshot != null && !Snapshot.IsPresenting && !IsPresenting &&
            (commandInput == null || !commandInput.IsSelectingAttack) &&
            unitProvider != null && !unitProvider.IsPresenting && movementView != null && !movementView.IsSelecting;

        public void ConfigureTargets(CombatTargetView targets, Static3DCombatUnitPresentationProvider provider,
            CombatMovementView movement)
        {
            targetView = targets;
            unitProvider = provider;
            movementView = movement;
            timelineView.ConfigureSelection(InspectTarget);
            targetView.CloseButton.onClick.RemoveAllListeners();
            targetView.CloseButton.onClick.AddListener(ClearInspectedTarget);
        }

        public CombatantHudSnapshot FindInspectable(string id)
        {
            if (string.IsNullOrEmpty(id) || Snapshot == null) return null;
            CombatantHudSnapshot value = Snapshot.Player?.Id == id ? Snapshot.Player :
                Snapshot.Enemies.FirstOrDefault(enemy => enemy.Id == id);
            return value != null && value.CurrentHealth > 0 && unitProvider != null &&
                unitProvider.TryGetVisibleBounds(id, out _) ? value : null;
        }

        public void InspectTarget(string id)
        {
            if (!CanInspect) return;
            InspectedTargetId = FindInspectable(id)?.Id;
            RefreshTarget();
        }

        public void ClearInspectedTarget()
        {
            InspectedTargetId = null;
            RefreshTarget();
        }

        private void RefreshTarget()
        {
            CombatantHudSnapshot selected = FindInspectable(InspectedTargetId);
            if (selected == null) InspectedTargetId = null;
            targetView?.Present(selected, Snapshot?.Player?.Id == InspectedTargetId);
            timelineView?.SetInspectedTarget(InspectedTargetId);
        }

        private void LateUpdate()
        {
            if (InspectedTargetId != null && FindInspectable(InspectedTargetId) == null) ClearInspectedTarget();
            unitProvider?.SetSelectionHighlight(InspectedTargetId ??
                (movementView != null && movementView.IsSelecting ? Snapshot?.Player?.Id : null));
        }

        private void OnDisable()
        {
            timelineView?.Hide();
            commandInput?.ClearContext();
            Snapshot = null;
            ClearInspectedTarget();
        }

        public void ConfigureTimeline(CombatTimelineView timeline, GameObject exploration)
        {
            timelineView = timeline;
            explorationPanel = exploration;
        }

        public void Configure(
            CombatHudView hudView,
            CombatCommandInput input,
            CombatLogView combatLog)
        {
            view = hudView;
            commandInput = input;
            logView = combatLog;
        }

        public void Present(CombatHudSnapshot snapshot)
        {
            Snapshot = snapshot;
            view?.Present(snapshot);
            bountyTracker?.Present(snapshot != null);
            healthBars?.Present(snapshot);
            timelineView?.Present(snapshot?.Timeline);
            explorationPanel?.SetActive(snapshot == null);
            commandInput?.SetContext(snapshot);
            RefreshTarget();
        }

        public void ClearLog() => logView?.Clear();
        public void ConfigureHealthBars(CombatUnitHealthBarView value) => healthBars = value;
        public void AppendLog(string message) => logView?.Append(message);

        public void Hide()
        {
            Snapshot = null;
            ClearInspectedTarget();
            view?.Hide();
            bountyTracker?.Present(false);
            logView?.Hide();
            healthBars?.Hide();
            timelineView?.Hide();
            explorationPanel?.SetActive(true);
            commandInput?.ClearContext();
        }
    }
}
