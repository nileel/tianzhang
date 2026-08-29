using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// 仅在 CombatPiece2DExperimentScene 中把已提交的棋子表现合同投影为既有苻渊 2D
    /// pilot。它不选择正式 profile、不会回写规则或保存状态，缺少实验专用输入即精确失败关闭。
    /// </summary>
    public sealed class BattleAnimationSpriteCombatUnitPresentationAdapter : MonoBehaviour, ICombatUnitPresentationPort
    {
        public const string ExperimentProfileId = "combat_piece_2d_experiment_fuyuan_v1";

        [SerializeField] private GameObject battleAnimationSpritePrefab;

        private readonly Dictionary<string, BattleAnimationSpritePresentationController> controllers =
            new Dictionary<string, BattleAnimationSpritePresentationController>();
        private readonly Dictionary<string, Vector3> pendingMoveEndPositions =
            new Dictionary<string, Vector3>();

        public int ActiveCombatantCount => controllers.Count;

        public void ConfigureExperimentPrefab(GameObject prefab)
        {
            if (controllers.Count != 0 || pendingMoveEndPositions.Count != 0)
                throw new InvalidOperationException("combat_piece_2d_experiment_reconfigure_after_prepare");
            battleAnimationSpritePrefab = prefab;
        }

        private void Awake()
        {
            ValidateExperimentPrefab();
        }

        private void Update()
        {
            if (pendingMoveEndPositions.Count == 0) return;

            var completedIds = new List<string>();
            foreach (KeyValuePair<string, Vector3> pendingMove in pendingMoveEndPositions)
            {
                if (!controllers.TryGetValue(pendingMove.Key, out BattleAnimationSpritePresentationController controller) ||
                    controller == null)
                {
                    completedIds.Add(pendingMove.Key);
                    continue;
                }
                if (controller.IsPresenting) continue;

                controller.transform.position = pendingMove.Value;
                controller.CaptureRestPose();
                completedIds.Add(pendingMove.Key);
            }

            foreach (string combatantId in completedIds)
                pendingMoveEndPositions.Remove(combatantId);
        }

        public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> combatants)
        {
            if (combatants == null) throw new ArgumentNullException(nameof(combatants));
            ValidateExperimentPrefab();

            var seenCombatantIds = new HashSet<string>();
            foreach (CombatUnitPresentationDescriptor combatant in combatants)
            {
                ValidateDescriptor(combatant);
                if (!seenCombatantIds.Add(combatant.CombatantId))
                    throw new InvalidOperationException("combat_piece_2d_experiment_duplicate_combatant");
            }

            Clear();
            foreach (CombatUnitPresentationDescriptor combatant in combatants)
                Spawn(combatant);
        }

        public void Spawn(CombatUnitPresentationDescriptor combatant)
        {
            ValidateExperimentPrefab();
            ValidateDescriptor(combatant);
            if (controllers.ContainsKey(combatant.CombatantId))
                throw new InvalidOperationException("combat_piece_2d_experiment_duplicate_combatant");

            GameObject instance = Instantiate(battleAnimationSpritePrefab, transform);
            instance.name = "CombatPiece2D_" + combatant.CombatantId;
            instance.transform.position = HexToWorld(combatant.Position);

            BattleAnimationSpritePresentationController controller =
                instance.GetComponent<BattleAnimationSpritePresentationController>();
            if (controller == null)
            {
                Destroy(instance);
                throw new InvalidOperationException("combat_piece_2d_experiment_controller_missing");
            }

            controller.CaptureRestPose();
            controller.SetDirection(combatant.Facing);
            controller.SetRootPresentationEnabled(true);
            controllers.Add(combatant.CombatantId, controller);
        }

        public void Present(CombatUnitPresentationEventProjection presentationEvent)
        {
            if (presentationEvent == null) throw new ArgumentNullException(nameof(presentationEvent));
            if (!controllers.TryGetValue(presentationEvent.ActorCombatantId,
                    out BattleAnimationSpritePresentationController controller) || controller == null)
                throw new InvalidOperationException("combat_piece_2d_experiment_actor_missing");
            if (presentationEvent.PresentationEvent < CombatUnitPresentationEvent.Idle ||
                presentationEvent.PresentationEvent > CombatUnitPresentationEvent.Death)
                throw new ArgumentOutOfRangeException(nameof(presentationEvent));

            controller.SetDirection(presentationEvent.Facing);
            Vector3 endPosition = HexToWorld(presentationEvent.EndPosition);
            if (presentationEvent.PresentationEvent == CombatUnitPresentationEvent.Move)
                pendingMoveEndPositions[presentationEvent.ActorCombatantId] = endPosition;
            else
                pendingMoveEndPositions.Remove(presentationEvent.ActorCombatantId);
            controller.StartPresentation(presentationEvent.PresentationEvent, endPosition);
        }

        public void Remove(string combatantId)
        {
            if (string.IsNullOrWhiteSpace(combatantId))
                throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (!controllers.TryGetValue(combatantId, out BattleAnimationSpritePresentationController controller)) return;

            controllers.Remove(combatantId);
            pendingMoveEndPositions.Remove(combatantId);
            if (controller != null) Destroy(controller.gameObject);
        }

        public void Clear()
        {
            foreach (BattleAnimationSpritePresentationController controller in controllers.Values)
                if (controller != null) Destroy(controller.gameObject);
            controllers.Clear();
            pendingMoveEndPositions.Clear();
        }

        public bool TryGetController(string combatantId, out BattleAnimationSpritePresentationController controller) =>
            controllers.TryGetValue(combatantId, out controller) && controller != null;

        public static Vector3 HexToWorld(CombatUnitPresentationHex position) =>
            new Vector3(position.Q + position.R * 0.5f, 0f, position.R * 0.8660254f);

        private void ValidateExperimentPrefab()
        {
            if (battleAnimationSpritePrefab == null)
                throw new InvalidOperationException("combat_piece_2d_experiment_prefab_missing");
            if (battleAnimationSpritePrefab.GetComponent<BattleAnimationSpritePresentationController>() == null ||
                battleAnimationSpritePrefab.GetComponentsInChildren<SpriteRenderer>(true).Length != 1)
                throw new InvalidOperationException("combat_piece_2d_experiment_prefab_invalid");
        }

        private static void ValidateDescriptor(CombatUnitPresentationDescriptor combatant)
        {
            if (combatant == null) throw new ArgumentNullException(nameof(combatant));
            if (!string.Equals(combatant.PresentationProfileId, ExperimentProfileId, StringComparison.Ordinal))
                throw new InvalidOperationException("combat_piece_2d_experiment_profile_unavailable");
        }
    }
}
