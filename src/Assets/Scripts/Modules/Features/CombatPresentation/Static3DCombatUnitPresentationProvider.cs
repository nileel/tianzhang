using System;
using System.Collections.Generic;
using TianZhang.Gameplay.Contracts;
using UnityEngine;

namespace TianZhang.Features.CombatPresentation
{
    /// <summary>
    /// The sole formal Adventure carrier for approved static-3D combat pieces.  It projects only
    /// already-committed combat lifecycle events and has no route back into Combat or save state.
    /// </summary>
    public sealed class Static3DCombatUnitPresentationProvider : MonoBehaviour, ICombatUnitPresentationPort, ICombatPresentationActivity
    {
        public const float MovementCellsPerSecond = 4f;
        private readonly Dictionary<string, Coroutine> movements = new Dictionary<string, Coroutine>();
        public bool IsPresenting => movements.Count > 0 || actionRecoveries.Count > 0 || transientFeedback.Count > 0;

        /// <summary>
        /// Battlefield support plane. Formal tile roots sit at this height and their measured
        /// bearing surface at the cell center is the same plane; the unit body is placed with
        /// its own foot pivot on it, without any independent base geometry.
        /// </summary>
        private const float GroundY = 0.34f;

        [SerializeField] private Static3DCombatUnitPresentationProfileSet profileSet;
        [SerializeField] private CombatAttackVfxCatalogData attackVfxCatalog;
        [SerializeField] private Material targetPreviewMaterial;
        [SerializeField, Range(.1f, 1f)] private float doubleCellFootprintFill = .9f;

        private readonly Dictionary<string, PresentedUnit> units = new Dictionary<string, PresentedUnit>();
        private readonly Dictionary<string, Coroutine> actionRecoveries = new Dictionary<string, Coroutine>();
        private readonly HashSet<GameObject> transientFeedback = new HashSet<GameObject>();

        public int ActiveCombatantCount => units.Count;

        public bool TryValidate(out string reason)
        {
            if (profileSet == null)
            {
                reason = "static_3d_presentation_profile_set_invalid:profile_set_missing";
                return false;
            }
            if (!profileSet.TryValidate(out string profileReason))
            {
                reason = "static_3d_presentation_profile_set_invalid:" + profileReason;
                return false;
            }
            if (attackVfxCatalog != null && !attackVfxCatalog.TryValidate(out string vfxReason))
            {
                reason = "static_3d_presentation_attack_vfx_catalog_invalid:" + vfxReason;
                return false;
            }
            reason = null;
            return true;
        }

        private void OnDisable()
        {
            Clear();
            ClearTransientFeedback();
        }

        public void Prepare(IReadOnlyList<CombatUnitPresentationDescriptor> combatants)
        {
            if (combatants == null) throw new ArgumentNullException(nameof(combatants));
            if (!TryValidate(out string reason)) throw new InvalidOperationException(reason);

            var seenCombatantIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatUnitPresentationDescriptor combatant in combatants)
            {
                ValidateDescriptor(combatant);
                if (!seenCombatantIds.Add(combatant.CombatantId))
                    throw new InvalidOperationException("static_3d_presentation_duplicate_combatant");
            }

            Clear();
            try
            {
                foreach (CombatUnitPresentationDescriptor combatant in combatants)
                    Spawn(combatant);
            }
            catch
            {
                Clear();
                throw;
            }
        }

        public void Spawn(CombatUnitPresentationDescriptor combatant)
        {
            ValidateDescriptor(combatant);
            if (units.ContainsKey(combatant.CombatantId))
                throw new InvalidOperationException("static_3d_presentation_duplicate_combatant");
            if (!profileSet.TryGetProfile(combatant.PresentationProfileId, out Static3DCombatUnitPresentationProfile profile))
                throw new InvalidOperationException("static_3d_presentation_profile_unavailable");

            GameObject root = new GameObject("Static3DCombatUnit_" + combatant.CombatantId);
            try
            {
                root.transform.SetParent(transform, false);
                root.transform.position = PoseToWorld(combatant.Pose);
                root.transform.rotation = Quaternion.Euler(0f, profile.sixDirectionYawDegrees[combatant.Facing], 0f);

                GameObject body = Instantiate(profile.prefab, root.transform);
                body.name = profile.prefab.name;
                // The approved meshes carry their own foot contact at local Y=0; the root already
                // stands on the support plane, so no base plate or extra lift may be inserted.
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = CombatUnitPresentationMotion.BodyScale(profile, combatant.IsDoubleCell, doubleCellFootprintFill);
                // Use the rendered static mesh for casting picks; a tall body must not project
                // through to a different ground cell behind it. These colliders live with the body.
                foreach (MeshFilter mesh in body.GetComponentsInChildren<MeshFilter>(true))
                    if (mesh.sharedMesh != null)
                    {
                        MeshCollider collider = mesh.GetComponent<MeshCollider>();
                        if (collider == null) collider = mesh.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = mesh.sharedMesh;
                    }
                units.Add(combatant.CombatantId, new PresentedUnit(
                    root,
                    body.GetComponentsInChildren<Renderer>(true),
                    combatant.PresentationProfileId,
                    combatant.CombatantId, combatant.Pose));
            }
            catch
            {
                Destroy(root);
                throw;
            }
        }

        public void Present(CombatUnitPresentationEventProjection presentationEvent)
        {
            if (presentationEvent == null) throw new ArgumentNullException(nameof(presentationEvent));
            if (!units.TryGetValue(presentationEvent.ActorCombatantId, out PresentedUnit unit) || unit.Root == null)
                throw new InvalidOperationException("static_3d_presentation_actor_missing");
            if (presentationEvent.PresentationEvent < CombatUnitPresentationEvent.Idle ||
                presentationEvent.PresentationEvent > CombatUnitPresentationEvent.Death)
                throw new ArgumentOutOfRangeException(nameof(presentationEvent));
            if (!profileSet.TryGetProfile(unit.ProfileId, out Static3DCombatUnitPresentationProfile profile))
                throw new InvalidOperationException("static_3d_presentation_profile_unavailable");

            if (presentationEvent.PresentationEvent == CombatUnitPresentationEvent.Move)
            {
                ValidateMovement(unit, presentationEvent);
                unit.LastEvent = CombatUnitPresentationEvent.Move;
                ResetRootScale(unit);
                movements.Add(unit.CombatantId, StartCoroutine(MoveAlongPath(unit, profile, presentationEvent)));
                PlayFeedback(unit, profile, presentationEvent);
                return;
            }
            if (movements.ContainsKey(unit.CombatantId))
                throw new InvalidOperationException("static_3d_presentation_busy");
            // A long body can turn only along the committed movement pose path. Attack direction
            // is carried separately by its contact cells and never changes the occupied footprint.
            int facing = unit.Pose.IsDoubleCell ? unit.Pose.Facing : presentationEvent.Facing;
            unit.Pose = new CombatUnitPresentationPose(presentationEvent.EndPosition, facing, unit.Pose.IsDoubleCell);
            unit.Root.transform.rotation = Quaternion.Euler(0f, profile.sixDirectionYawDegrees[facing], 0f);
            unit.Root.transform.position = PoseToWorld(unit.Pose);
            unit.LastEvent = presentationEvent.PresentationEvent;
            PlayFeedback(unit, profile, presentationEvent);
            PlayAttackVfx(presentationEvent);
            switch (presentationEvent.PresentationEvent)
            {
                case CombatUnitPresentationEvent.Idle:
                    ResetRootScale(unit);
                    break;
                case CombatUnitPresentationEvent.Move:
                    ResetRootScale(unit);
                    break;
                case CombatUnitPresentationEvent.Attack:
                    SetActionScale(unit, new Vector3(1.08f, 0.94f, 1.08f), profile.combatFeedback.actionRecoverySeconds);
                    break;
                case CombatUnitPresentationEvent.Hit:
                    SetActionScale(unit, new Vector3(0.94f, 1.06f, 0.94f), profile.combatFeedback.actionRecoverySeconds);
                    break;
                case CombatUnitPresentationEvent.Cast:
                    SetActionScale(unit, new Vector3(1.04f, 1.1f, 1.04f), profile.combatFeedback.actionRecoverySeconds);
                    break;
                case CombatUnitPresentationEvent.Death:
                    unit.TargetPreview?.SetVisible(false);
                    StopActionRecovery(presentationEvent.ActorCombatantId);
                    unit.Root.SetActive(false);
                    break;
            }
        }

        public void Remove(string combatantId)
        {
            if (string.IsNullOrWhiteSpace(combatantId)) throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (!units.TryGetValue(combatantId, out PresentedUnit unit)) return;
            unit.TargetPreview?.SetVisible(false);
            StopMovement(combatantId);
            units.Remove(combatantId);
            StopActionRecovery(combatantId);
            if (unit.Root != null) Destroy(unit.Root);
        }

        public void Clear()
        {
            foreach (Coroutine routine in movements.Values) StopCoroutine(routine);
            movements.Clear();
            ClearTransientFeedback();
            foreach (PresentedUnit unit in units.Values)
            {
                unit.TargetPreview?.SetVisible(false);
                if (unit.Root != null)
                {
                    foreach (MeshCollider collider in unit.Root.GetComponentsInChildren<MeshCollider>(true))
                        collider.sharedMesh = null;
                    Destroy(unit.Root);
                }
            }
            units.Clear();
            StopAllActionRecoveries();
        }

        public bool TryGetPresentedUnit(string combatantId, out GameObject root)
        {
            root = null;
            return units.TryGetValue(combatantId, out PresentedUnit unit) && (root = unit.Root) != null;
        }

        /// <summary>Projects only the current confirmable quote's affected units, independently of action playback.</summary>
        public void PresentTargetPreview(IReadOnlyList<CombatantHudSnapshot> targets)
        {
            foreach (PresentedUnit unit in units.Values)
            {
                bool visible = false;
                if (targets != null && unit.LastEvent != CombatUnitPresentationEvent.Death &&
                    unit.Root != null && unit.Root.activeInHierarchy)
                    foreach (CombatantHudSnapshot target in targets)
                        if (target.Id == unit.CombatantId && target.CurrentHealth > 0) { visible = true; break; }
                if (visible)
                    unit.TargetPreview ??= new CombatTargetPreview(unit.BodyRenderers, targetPreviewMaterial);
                unit.TargetPreview?.SetVisible(visible);
                RefreshTargetPreview(unit);
            }
        }

        private void LateUpdate()
        {
            foreach (PresentedUnit unit in units.Values) RefreshTargetPreview(unit);
        }

        private void RefreshTargetPreview(PresentedUnit unit)
        {
            if (unit.TargetPreview?.IsVisible != true) return;
            if (TryGetVisibleBounds(unit.CombatantId, out Bounds bounds)) unit.TargetPreview.Refresh(bounds);
            else unit.TargetPreview.SetVisible(false);
        }

        /// <summary>Committed presentation cell of a spawned unit; used only to draw selection hints.</summary>
        public bool TryGetPresentedPosition(string combatantId, out CombatUnitPresentationHex position)
        {
            position = default;
            if (string.IsNullOrWhiteSpace(combatantId) || !units.TryGetValue(combatantId, out PresentedUnit unit))
                return false;
            position = unit.Pose.Position;
            return true;
        }

        public bool TryGetPresentedPose(string combatantId, out CombatUnitPresentationPose pose)
        {
            pose = default;
            if (string.IsNullOrWhiteSpace(combatantId) || !units.TryGetValue(combatantId, out PresentedUnit unit))
                return false;
            pose = unit.Pose;
            return true;
        }

        public static Vector3 PoseToWorld(CombatUnitPresentationPose pose) => CombatUnitPresentationMotion.Center(pose);

        public bool TryGetHeadAnchor(string combatantId, out Vector3 anchor)
        {
            anchor = default;
            if (!TryGetVisibleBounds(combatantId, out Bounds bounds)) return false;
            anchor = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return true;
        }

        public bool TryGetVisibleBounds(string combatantId, out Bounds bounds)
        {
            bounds = default;
            if (string.IsNullOrWhiteSpace(combatantId) ||
                !units.TryGetValue(combatantId, out PresentedUnit unit) ||
                unit.Root == null || !unit.Root.activeInHierarchy)
                return false;

            bool found = false;
            foreach (Renderer renderer in unit.BodyRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (found) bounds.Encapsulate(renderer.bounds);
                else { bounds = renderer.bounds; found = true; }
            }
            return found;
        }

        public bool TryGetLastEvent(string combatantId, out CombatUnitPresentationEvent presentationEvent)
        {
            if (units.TryGetValue(combatantId, out PresentedUnit unit))
            {
                presentationEvent = unit.LastEvent;
                return true;
            }
            presentationEvent = default;
            return false;
        }

        public static Vector3 HexToWorld(CombatUnitPresentationHex position) =>
            new Vector3(position.Q + position.R * 0.5f, GroundY, position.R * 0.8660254f + 1f);

        private static int SegmentDirection(CombatUnitPresentationHex from, CombatUnitPresentationHex to)
        {
            int q = to.Q - from.Q, r = to.R - from.R;
            if (q == 1 && r == 0) return 0;
            if (q == 1 && r == -1) return 1;
            if (q == 0 && r == -1) return 2;
            if (q == -1 && r == 0) return 3;
            if (q == -1 && r == 1) return 4;
            if (q == 0 && r == 1) return 5;
            return -1;
        }

        private void ValidateMovement(PresentedUnit unit, CombatUnitPresentationEventProjection projection)
        {
            if (movements.ContainsKey(unit.CombatantId) || projection.MovementPath.Count == 0 ||
                !unit.Pose.Position.Equals(projection.StartPosition))
                throw new InvalidOperationException("static_3d_presentation_move_stale");
            if (unit.Pose.IsDoubleCell && projection.MovementSteps.Count == 0)
                throw new InvalidOperationException("static_3d_presentation_move_pose_missing");
            if (projection.MovementSteps.Count > 0)
            {
                if (projection.MovementSteps.Count != projection.MovementPath.Count)
                    throw new InvalidOperationException("static_3d_presentation_move_pose_path_mismatch");
                CombatUnitPresentationPose previousPose = unit.Pose;
                for (int index = 0; index < projection.MovementSteps.Count; index++)
                {
                    CombatUnitPresentationMovementStep step = projection.MovementSteps[index];
                    if (step == null || !step.From.Position.Equals(previousPose.Position) ||
                        step.From.IsDoubleCell != previousPose.IsDoubleCell ||
                        (previousPose.IsDoubleCell && step.From.Facing != previousPose.Facing) ||
                        !step.To.Position.Equals(projection.MovementPath[index]))
                        throw new InvalidOperationException("static_3d_presentation_move_pose_stale");
                    previousPose = step.To;
                }
                if (previousPose.Facing != projection.Facing)
                    throw new InvalidOperationException("static_3d_presentation_move_pose_endpoint_invalid");
            }
            CombatUnitPresentationHex previous = projection.StartPosition;
            int direction = -1;
            foreach (CombatUnitPresentationHex next in projection.MovementPath)
            {
                direction = SegmentDirection(previous, next);
                if (direction < 0) throw new InvalidOperationException("static_3d_presentation_move_nonadjacent");
                previous = next;
            }
            if (!previous.Equals(projection.EndPosition) || direction != projection.Facing)
                throw new InvalidOperationException("static_3d_presentation_move_endpoint_invalid");
        }

        private System.Collections.IEnumerator MoveAlongPath(PresentedUnit unit,
            Static3DCombatUnitPresentationProfile profile, CombatUnitPresentationEventProjection projection)
        {
            for (int index = 0; index < projection.MovementPath.Count; index++)
            {
                CombatUnitPresentationHex next = projection.MovementPath[index];
                CombatUnitPresentationMovementStep step = projection.MovementSteps.Count > 0
                    ? projection.MovementSteps[index]
                    : new CombatUnitPresentationMovementStep(unit.Pose,
                        new CombatUnitPresentationPose(next, SegmentDirection(unit.Pose.Position, next)));
                yield return CombatUnitPresentationMotion.AnimateStep(unit.Root.transform, step,
                    profile.sixDirectionYawDegrees, MovementCellsPerSecond);
                unit.Pose = step.To;
            }
            unit.Root.transform.position = PoseToWorld(unit.Pose);
            movements.Remove(unit.CombatantId);
        }

        private void StopMovement(string id)
        {
            if (!movements.TryGetValue(id, out Coroutine routine)) return;
            StopCoroutine(routine);
            movements.Remove(id);
        }

        /// <summary>
        /// Per-attack light VFX. Only profiles listed in the catalog play; every other attack keeps
        /// the existing generic feedback. The effect is transient, never extends the input lock
        /// beyond the existing 1s feedback window, and is skipped when no target is known.
        /// </summary>
        private void PlayAttackVfx(CombatUnitPresentationEventProjection presentationEvent)
        {
            if (attackVfxCatalog == null ||
                presentationEvent.PresentationEvent is not (CombatUnitPresentationEvent.Attack or CombatUnitPresentationEvent.Cast) ||
                !attackVfxCatalog.TryGet(presentationEvent.ProfileId, out CombatAttackVfxEntry entry))
                return;

            Vector3 feet = HexToWorld(presentationEvent.EndPosition);
            Vector3 origin = feet + Vector3.up * 0.85f;
            Vector3 target = origin;
            bool found = presentationEvent.TargetCell.HasValue;
            if (found)
            {
                feet = HexToWorld(presentationEvent.SourceCell ?? presentationEvent.EndPosition);
                origin = feet + Vector3.up * .85f;
                target = HexToWorld(presentationEvent.TargetCell.Value) + Vector3.up * .75f;
            }
            foreach (CombatUnitPresentationTargetResult targetResult in presentationEvent.TargetResults)
            {
                if (found) break;
                if (!units.TryGetValue(targetResult.CombatantId, out PresentedUnit targetUnit) ||
                    targetUnit.Root == null)
                    continue;
                if (targetResult.SourceCell.HasValue)
                {
                    feet = HexToWorld(targetResult.SourceCell.Value);
                    origin = feet + Vector3.up * .85f;
                    target = HexToWorld(targetResult.TargetCell.Value) + Vector3.up * .75f;
                }
                else target = targetUnit.Root.transform.position + Vector3.up * .75f;
                found = true;
                break;
            }
            if (!found) return;

            CombatAttackVfx effect = entry.kind == CombatAttackVfxKind.Slash
                ? CombatAttackVfx.PlaySlash(feet, target, entry.color, entry.scale, entry.durationSeconds)
                : CombatAttackVfx.PlayProjectile(origin, target, entry.color, entry.scale, entry.durationSeconds);
            RegisterTransient(effect.gameObject, entry.durationSeconds + 0.3f);
        }

        public bool TryPickBodyPoint(Ray ray, out Vector3 point)
        {
            // Animation and movement update transforms outside the physics step.
            Physics.SyncTransforms();
            point = default;
            float nearest = float.PositiveInfinity;
            foreach (PresentedUnit unit in units.Values)
            {
                if (unit.Root == null || unit.LastEvent == CombatUnitPresentationEvent.Death) continue;
                foreach (Renderer renderer in unit.BodyRenderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                    MeshCollider collider = renderer.GetComponent<MeshCollider>();
                    if (collider != null && collider.Raycast(ray, out RaycastHit hit, nearest))
                    {
                        nearest = hit.distance;
                        point = hit.point;
                    }
                }
            }
            return !float.IsPositiveInfinity(nearest);
        }

        private void PlayFeedback(
            PresentedUnit unit,
            Static3DCombatUnitPresentationProfile profile,
            CombatUnitPresentationEventProjection presentationEvent)
        {
            if (presentationEvent.PresentationEvent == CombatUnitPresentationEvent.Idle)
                return;

            Vector3 position = PoseToWorld(unit.Pose) + Vector3.up * 0.22f;
            if (presentationEvent.PresentationEvent is CombatUnitPresentationEvent.Hit or CombatUnitPresentationEvent.Death)
                foreach (CombatUnitPresentationTargetResult target in presentationEvent.TargetResults)
                    if (target.CombatantId == unit.CombatantId && target.TargetCell.HasValue)
                    { position = HexToWorld(target.TargetCell.Value) + Vector3.up * .22f; break; }
            Static3DCombatFeedbackProfile feedback = profile.combatFeedback;
            AudioClip cue = feedback.GetCue(presentationEvent.PresentationEvent);
            SpawnCue(cue, position, feedback.intensity);
            if (presentationEvent.PresentationEvent != CombatUnitPresentationEvent.Move)
                SpawnVfx(feedback.vfxPrefab, presentationEvent.PresentationEvent, position, feedback.intensity);
        }

        private void SpawnCue(AudioClip cue, Vector3 position, float intensity)
        {
            if (cue == null) return;
            var audioObject = new GameObject("GuanzhongCombatFeedback_Audio");
            audioObject.transform.position = position;
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.clip = cue;
            source.volume = 0.42f * intensity;
            source.spatialBlend = 1f;
            source.minDistance = 1.2f;
            source.maxDistance = 8f;
            source.Play();
            RegisterTransient(audioObject, cue.length + 0.05f);
        }

        private void SpawnVfx(GameObject prefab, CombatUnitPresentationEvent presentationEvent, Vector3 position, float intensity)
        {
            if (prefab == null) return;
            GameObject effect = Instantiate(prefab, position, Quaternion.identity);
            effect.name = "GuanzhongCombatFeedback_" + presentationEvent + "_Vfx";
            effect.transform.localScale = Vector3.one * intensity;
            ParticleSystem particles = effect.GetComponentInChildren<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.startColor = FeedbackColor(presentationEvent);
            particles.Play(true);
            RegisterTransient(effect, 1f);
        }

        private void RegisterTransient(GameObject value, float lifetime)
        {
            transientFeedback.Add(value);
            StartCoroutine(ReapTransient(value, lifetime));
        }

        private System.Collections.IEnumerator ReapTransient(GameObject value, float lifetime)
        {
            yield return new WaitForSeconds(lifetime);
            transientFeedback.Remove(value);
            if (value != null) Destroy(value);
        }

        private void SetActionScale(PresentedUnit unit, Vector3 scale, float recoverySeconds)
        {
            unit.Root.transform.localScale = scale;
            StopActionRecovery(unit.CombatantId);
            actionRecoveries.Add(unit.CombatantId, StartCoroutine(RecoverRootScale(unit, recoverySeconds)));
        }

        private System.Collections.IEnumerator RecoverRootScale(PresentedUnit unit, float recoverySeconds)
        {
            yield return new WaitForSeconds(recoverySeconds);
            actionRecoveries.Remove(unit.CombatantId);
            if (unit.Root != null && unit.Root.activeSelf)
                unit.Root.transform.localScale = Vector3.one;
        }

        private void ResetRootScale(PresentedUnit unit)
        {
            StopActionRecovery(unit.CombatantId);
            if (unit.Root != null && unit.Root.activeSelf)
                unit.Root.transform.localScale = Vector3.one;
        }

        private void StopActionRecovery(string combatantId)
        {
            if (!actionRecoveries.TryGetValue(combatantId, out Coroutine routine)) return;
            StopCoroutine(routine);
            actionRecoveries.Remove(combatantId);
        }

        private void StopAllActionRecoveries()
        {
            foreach (Coroutine routine in actionRecoveries.Values)
                StopCoroutine(routine);
            actionRecoveries.Clear();
        }

        private void ClearTransientFeedback()
        {
            foreach (GameObject feedback in transientFeedback)
                if (feedback != null) Destroy(feedback);
            transientFeedback.Clear();
        }

        private static Color FeedbackColor(CombatUnitPresentationEvent presentationEvent)
        {
            return presentationEvent switch
            {
                CombatUnitPresentationEvent.Attack => new Color(0.95f, 0.68f, 0.28f, 0.9f),
                CombatUnitPresentationEvent.Hit => new Color(0.9f, 0.28f, 0.18f, 0.9f),
                CombatUnitPresentationEvent.Cast => new Color(0.28f, 0.72f, 1f, 0.9f),
                CombatUnitPresentationEvent.Death => new Color(0.5f, 0.42f, 0.32f, 0.85f),
                _ => new Color(0.56f, 0.8f, 0.65f, 0.8f),
            };
        }

        private void ValidateDescriptor(CombatUnitPresentationDescriptor combatant)
        {
            if (combatant == null) throw new ArgumentNullException(nameof(combatant));
            if (!TryValidate(out string reason)) throw new InvalidOperationException(reason);
            if (!profileSet.TryGetProfile(combatant.PresentationProfileId, out _))
                throw new InvalidOperationException("static_3d_presentation_profile_unavailable");
        }

        private sealed class PresentedUnit
        {
            public PresentedUnit(GameObject root, Renderer[] bodyRenderers, string profileId,
                string combatantId, CombatUnitPresentationPose pose)
            {
                Root = root;
                BodyRenderers = bodyRenderers;
                ProfileId = profileId;
                CombatantId = combatantId;
                Pose = pose;
                LastEvent = CombatUnitPresentationEvent.Idle;
            }

            public GameObject Root { get; }
            public Renderer[] BodyRenderers { get; }
            public CombatTargetPreview TargetPreview { get; set; }
            public string ProfileId { get; set; }
            public CombatUnitPresentationPose Pose { get; set; }
            public string CombatantId { get; }
            public CombatUnitPresentationEvent LastEvent { get; set; }
        }
    }
}
