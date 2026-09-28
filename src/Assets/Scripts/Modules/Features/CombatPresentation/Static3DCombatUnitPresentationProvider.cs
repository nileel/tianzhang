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
    public sealed class Static3DCombatUnitPresentationProvider : MonoBehaviour, ICombatUnitPresentationPort
    {
        private const float GroundY = 0.34f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Static3DCombatUnitPresentationProfileSet profileSet;

        private readonly Dictionary<string, PresentedUnit> units = new Dictionary<string, PresentedUnit>();

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
                body.transform.localPosition = Vector3.zero;
                body.transform.localRotation = Quaternion.identity;
                body.transform.localScale = Vector3.one;
                MeshRenderer baseRenderer = CreateFactionBase(root.transform, combatant.DisplayFaction);
                units.Add(combatant.CombatantId, new PresentedUnit(root, baseRenderer, combatant.PresentationProfileId));
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

            unit.Root.transform.rotation = Quaternion.Euler(0f, profile.sixDirectionYawDegrees[presentationEvent.Facing], 0f);
            unit.Root.transform.position = HexToWorld(presentationEvent.EndPosition);
            unit.LastEvent = presentationEvent.PresentationEvent;
            switch (presentationEvent.PresentationEvent)
            {
                case CombatUnitPresentationEvent.Idle:
                    unit.Root.transform.localScale = Vector3.one;
                    break;
                case CombatUnitPresentationEvent.Move:
                    unit.Root.transform.localScale = Vector3.one;
                    break;
                case CombatUnitPresentationEvent.Attack:
                    unit.Root.transform.localScale = new Vector3(1.08f, 0.94f, 1.08f);
                    break;
                case CombatUnitPresentationEvent.Hit:
                    unit.Root.transform.localScale = new Vector3(0.94f, 1.06f, 0.94f);
                    break;
                case CombatUnitPresentationEvent.Cast:
                    unit.Root.transform.localScale = new Vector3(1.04f, 1.1f, 1.04f);
                    break;
                case CombatUnitPresentationEvent.Death:
                    unit.Root.SetActive(false);
                    break;
            }
        }

        public void Remove(string combatantId)
        {
            if (string.IsNullOrWhiteSpace(combatantId)) throw new ArgumentException("Combatant ID is required.", nameof(combatantId));
            if (!units.TryGetValue(combatantId, out PresentedUnit unit)) return;
            units.Remove(combatantId);
            if (unit.Root != null) Destroy(unit.Root);
        }

        public void Clear()
        {
            foreach (PresentedUnit unit in units.Values)
                if (unit.Root != null) Destroy(unit.Root);
            units.Clear();
        }

        public bool TryGetPresentedUnit(string combatantId, out GameObject root)
        {
            root = null;
            return units.TryGetValue(combatantId, out PresentedUnit unit) && (root = unit.Root) != null;
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

        private static MeshRenderer CreateFactionBase(Transform parent, CombatUnitDisplayFaction faction)
        {
            GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Static3DCombatBase";
            baseObject.transform.SetParent(parent, false);
            baseObject.transform.localPosition = new Vector3(0f, -0.04f, 0f);
            baseObject.transform.localScale = new Vector3(0.62f, 0.08f, 0.62f);
            Collider collider = baseObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            MeshRenderer renderer = baseObject.GetComponent<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, faction == CombatUnitDisplayFaction.Player ? Color.cyan : Color.red);
            renderer.SetPropertyBlock(block);
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
            public PresentedUnit(GameObject root, MeshRenderer baseRenderer, string profileId)
            {
                Root = root;
                BaseRenderer = baseRenderer;
                ProfileId = profileId;
                LastEvent = CombatUnitPresentationEvent.Idle;
            }

            public GameObject Root { get; }
            public MeshRenderer BaseRenderer { get; }
            public string ProfileId { get; set; }
            public CombatUnitPresentationEvent LastEvent { get; set; }
        }
    }
}
