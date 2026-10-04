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

        private const float GroundY = 0.34f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float BaseThickness = 0.04f;

        [SerializeField] private Static3DCombatUnitPresentationProfileSet profileSet;

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
                root.transform.position = HexToWorld(combatant.Position);
                root.transform.rotation = Quaternion.Euler(0f, profile.sixDirectionYawDegrees[combatant.Facing], 0f);

                GameObject body = Instantiate(profile.prefab, root.transform);
                body.name = profile.prefab.name;
                body.transform.localPosition = new Vector3(0f, BaseThickness, 0f);
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = new Vector3(1.4f, 1.4f, 1.4f);
                MeshRenderer baseRenderer = CreateFactionBase(root.transform, profile, combatant.DisplayFaction);
                units.Add(combatant.CombatantId, new PresentedUnit(
                    root,
                    baseRenderer,
                    body.GetComponentsInChildren<Renderer>(true),
                    combatant.PresentationProfileId,
                    combatant.CombatantId, combatant.Position));
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
            unit.Position = presentationEvent.EndPosition;
            unit.Root.transform.rotation = Quaternion.Euler(0f, profile.sixDirectionYawDegrees[presentationEvent.Facing], 0f);
            unit.Root.transform.position = HexToWorld(presentationEvent.EndPosition);
            unit.LastEvent = presentationEvent.PresentationEvent;
            PlayFeedback(unit, profile, presentationEvent);
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
                    StopActionRecovery(presentationEvent.ActorCombatantId);
                    unit.Root.SetActive(false);
                    break;
            }
        }

        public void Remove(string combatantId)
        {
            if (string.IsNullOrWhiteSpace(combatantId)) throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (!units.TryGetValue(combatantId, out PresentedUnit unit)) return;
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
                if (unit.Root != null) Destroy(unit.Root);
            units.Clear();
            StopAllActionRecoveries();
        }

        public bool TryGetPresentedUnit(string combatantId, out GameObject root)
        {
            root = null;
            return units.TryGetValue(combatantId, out PresentedUnit unit) && (root = unit.Root) != null;
        }

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

        public void SetSelectionHighlight(string combatantId)
        {
            foreach (PresentedUnit unit in units.Values)
                unit.SelectionHighlight.enabled =
                    unit.CombatantId == combatantId || movements.ContainsKey(unit.CombatantId);
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
                !unit.Position.Equals(projection.StartPosition))
                throw new InvalidOperationException("static_3d_presentation_move_stale");
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
            CombatUnitPresentationHex previous = projection.StartPosition;
            foreach (CombatUnitPresentationHex next in projection.MovementPath)
            {
                Vector3 start = HexToWorld(previous), end = HexToWorld(next);
                unit.Root.transform.rotation = Quaternion.Euler(0f,
                    profile.sixDirectionYawDegrees[SegmentDirection(previous, next)], 0f);
                float progress = 0f;
                while (progress < 1f)
                {
                    yield return null;
                    progress = Mathf.Min(1f, progress + Time.deltaTime * MovementCellsPerSecond);
                    unit.Root.transform.position = Vector3.Lerp(start, end, progress);
                }
                unit.Position = next;
                previous = next;
            }
            unit.Root.transform.position = HexToWorld(projection.EndPosition);
            movements.Remove(unit.CombatantId);
        }

        private void StopMovement(string id)
        {
            if (!movements.TryGetValue(id, out Coroutine routine)) return;
            StopCoroutine(routine);
            movements.Remove(id);
        }

        private void PlayFeedback(
            PresentedUnit unit,
            Static3DCombatUnitPresentationProfile profile,
            CombatUnitPresentationEventProjection presentationEvent)
        {
            if (presentationEvent.PresentationEvent == CombatUnitPresentationEvent.Idle)
                return;

            Vector3 position = HexToWorld(presentationEvent.EndPosition) + Vector3.up * 0.22f;
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

        private static MeshRenderer CreateFactionBase(Transform parent, Static3DCombatUnitPresentationProfile profile,
            CombatUnitDisplayFaction faction)
        {
            GameObject baseObject = Instantiate(profile.combatBasePrefab, parent);
            baseObject.name = "Static3DCombatBase";
            baseObject.transform.localPosition = new Vector3(0f, BaseThickness, 0f);
            baseObject.transform.localRotation = Quaternion.identity;
            baseObject.transform.localScale = Vector3.one;
            MeshRenderer renderer = baseObject.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, 1);
            block.SetColor(BaseColorId, faction == CombatUnitDisplayFaction.Player ? Color.cyan : Color.red);
            renderer.SetPropertyBlock(block, 1);
            return renderer;
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
            public PresentedUnit(GameObject root, MeshRenderer baseRenderer, Renderer[] bodyRenderers, string profileId, string combatantId, CombatUnitPresentationHex position)
            {
                Root = root;
                BaseRenderer = baseRenderer;
                SelectionHighlight = root.transform.Find("Static3DCombatBase").GetComponentInChildren<LineRenderer>(true);
                BodyRenderers = bodyRenderers;
                ProfileId = profileId;
                CombatantId = combatantId;
                Position = position;
                LastEvent = CombatUnitPresentationEvent.Idle;
            }

            public GameObject Root { get; }
            public MeshRenderer BaseRenderer { get; }
            public LineRenderer SelectionHighlight { get; }
            public Renderer[] BodyRenderers { get; }
            public string ProfileId { get; set; }
            public CombatUnitPresentationHex Position { get; set; }
            public string CombatantId { get; }
            public CombatUnitPresentationEvent LastEvent { get; set; }
        }
    }
}
