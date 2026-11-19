using System.Collections.Generic;
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
        [SerializeField] private CombatSceneObjectView sceneObjects;

        [SerializeField] private CombatTimelineView timelineView;
        [SerializeField] private GameObject explorationPanel;
        private CombatBountyTrackerView bountyTracker;
        public void ConfigureTrialPortraits(JindanDemoView rehearsal)
        {
            var badges = new Dictionary<string, Sprite>();
            foreach (string key in new[] { "earth_tab", "tide_tab", "fire_tab", "visibility_tab", "seal_tab" })
                badges.Add(key, rehearsal.GetButton(key).transform.Find("ActionIcon").GetComponent<UnityEngine.UI.Image>().sprite);
            view.BindTrialPortraits(badges);
            timelineView.TrialPortraits = badges;
            targetView.TrialPortraits = badges;
        }

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
            commandInput.ConfigureTargetPreview(provider);
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
            bool allied = selected != null && (Snapshot.Player.Id == selected.Id ||
                Snapshot.Timeline?.Entries.Any(entry => entry.Id == selected.Id && entry.Faction == CombatUnitDisplayFaction.Player) == true);
            targetView?.Present(selected, allied);
            timelineView?.SetInspectedTarget(InspectedTargetId);
            RefreshInspectionMarker();
        }

        /// <summary>Tile-body rings follow every occupied cell of the inspected unit.</summary>
        private void RefreshInspectionMarker()
        {
            if (movementView == null) return;
            CombatUnitPresentationPose? pose = null;
            if (commandInput?.IsSelectingAttack != true && InspectedTargetId != null && unitProvider != null &&
                unitProvider.TryGetPresentedPose(InspectedTargetId, out CombatUnitPresentationPose presented))
                pose = presented;
            movementView.SetInspectionPose(pose);
        }

        private void LateUpdate()
        {
            if (InspectedTargetId != null && FindInspectable(InspectedTargetId) == null) ClearInspectedTarget();
            RefreshInspectionMarker();
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
            sceneObjects?.Present(snapshot?.SceneObjects);
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
            sceneObjects?.Present(null);
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
